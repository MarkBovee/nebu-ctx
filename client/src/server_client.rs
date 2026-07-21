use crate::config;
use crate::models::{
    ProjectContext, ProjectResolutionRequest, ProjectResolutionResponse, RepositoryFingerprint,
    ServerConnection, TelemetryIngestRequest, ToolCallRequest, ToolCallResponse, ToolListResponse,
};
use anyhow::{anyhow, Context, Result};
use md5::{Digest, Md5};
use serde::de::DeserializeOwned;
use serde::{Deserialize, Serialize};
use serde_json::{Map, Value};
use std::path::{Path, PathBuf};
use std::time::{SystemTime, UNIX_EPOCH};

const HOSTED_MEMORY_SYNC_DEBOUNCE_SECS: i64 = 30;
const COPILOT_REPO_MEMORY_SOURCE_TYPE: &str = "copilot_repo_memory";

#[derive(Debug, Clone, Serialize, Deserialize, PartialEq, Eq)]
struct RepoMemoryStateEntry {
    category: String,
    key: String,
}

#[derive(Debug, Clone, Default, Serialize, Deserialize, PartialEq, Eq)]
struct RepoMemorySyncState {
    digest: String,
    entries: Vec<RepoMemoryStateEntry>,
}

#[derive(Debug, Clone, PartialEq, Eq)]
struct RepoMemoryRecord {
    category: String,
    key: String,
    value: String,
    source_scope: String,
}

pub struct ServerClient {
    connection: ServerConnection,
    agent: ureq::Agent,
}

fn build_agent() -> ureq::Agent {
    ureq::Agent::new_with_config(
        ureq::config::Config::builder()
            .timeout_global(Some(std::time::Duration::from_secs(8)))
            .timeout_connect(Some(std::time::Duration::from_secs(3)))
            .build(),
    )
}

#[derive(Debug, Clone, serde::Serialize, serde::Deserialize)]
pub struct QueuedServerToolCall {
    pub tool_name: String,
    pub arguments: Map<String, Value>,
    pub project_context: QueuedProjectContext,
    #[serde(default)]
    pub operation_id: Option<String>,
}

#[derive(Debug, Clone, serde::Serialize, serde::Deserialize)]
pub struct QueuedProjectContext {
    pub project_slug: String,
    pub project_root: String,
    pub fingerprint: crate::models::RepositoryFingerprint,
    pub checkout_binding: crate::models::CheckoutBinding,
    pub project_metadata: Option<crate::models::ProjectMetadataEnvelope>,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct QueuedIndexSync {
    pub project_context: QueuedProjectContext,
    pub files: Vec<IndexSyncFile>,
    pub symbols: Vec<IndexSyncSymbol>,
    pub edges: Vec<IndexSyncEdge>,
}

impl From<&ProjectContext> for QueuedProjectContext {
    fn from(value: &ProjectContext) -> Self {
        Self {
            project_slug: value.project_slug.clone(),
            project_root: value.project_root.clone(),
            fingerprint: value.fingerprint.clone(),
            checkout_binding: value.checkout_binding.clone(),
            project_metadata: value.project_metadata.clone(),
        }
    }
}

impl From<QueuedProjectContext> for ProjectContext {
    fn from(value: QueuedProjectContext) -> Self {
        Self {
            project_slug: value.project_slug,
            project_root: value.project_root,
            fingerprint: value.fingerprint,
            checkout_binding: value.checkout_binding,
            project_metadata: value.project_metadata,
        }
    }
}

impl ServerClient {
    pub fn load() -> Result<Self> {
        let connection = config::load_connection()?.ok_or_else(|| {
            anyhow!("No server connection saved. Run `nebu-ctx connect --endpoint <url> --token <token>`.")
        })?;
        Ok(Self { connection, agent: build_agent() })
    }

    pub fn new(connection: ServerConnection) -> Self {
        Self { connection, agent: build_agent() }
    }

    pub fn endpoint(&self) -> &str {
        &self.connection.endpoint
    }

    pub fn health(&self) -> Result<Value> {
        self.get_json("/health")
    }

    pub fn manifest(&self) -> Result<Value> {
        self.get_json("/v1/manifest")
    }

    pub fn list_tools(&self) -> Result<ToolListResponse> {
        self.get_json("/v1/tools")
    }

    pub fn resolve_project(
        &self,
        project_context: &ProjectContext,
    ) -> Result<ProjectResolutionResponse> {
        if !project_context.fingerprint.has_safe_identity() {
            return Err(anyhow!(
                "Project resolution requires a repository fingerprint with a remote URL or host/owner/repo."
            ));
        }

        self.post_json(
            "/v1/projects/resolve",
            &ProjectResolutionRequest {
                fingerprint: project_context.fingerprint.clone(),
                suggested_slug: Some(project_context.project_slug.clone()),
                checkout_binding: Some(project_context.checkout_binding.clone()),
                project_metadata: project_context.project_metadata.clone(),
            },
        )
    }

    pub fn call_tool(
        &self,
        tool_name: &str,
        arguments: Map<String, Value>,
        project_context: &ProjectContext,
    ) -> Result<Value> {
        let repository_fingerprint = project_context
            .fingerprint
            .has_safe_identity()
            .then(|| project_context.fingerprint.clone());

        let response: ToolCallResponse = self.post_json(
            "/v1/tools/call",
            &ToolCallRequest {
                name: tool_name.to_string(),
                arguments,
                project_id: None,
                project_slug: Some(project_context.project_slug.clone()),
                repository_fingerprint,
                checkout_binding: Some(project_context.checkout_binding.clone()),
                project_metadata: project_context.project_metadata.clone(),
                operation_id: None,
            },
        )?;

        Ok(response.result)
    }

    /// Posts a single tool-call telemetry event to the server for dashboard aggregation.
    /// Only token counts and metadata are sent; no raw content.
    pub fn ingest_telemetry(&self, request: &TelemetryIngestRequest) -> Result<()> {
        let _: serde_json::Value = self.post_json("/v1/telemetry/ingest", request)?;
        Ok(())
    }

    /// Syncs the full project code index (files, symbols, call edges) to the server.
    /// Returns the number of files, symbols, and edges successfully synced.
    pub fn sync_index(&self, request: &IndexSyncPayload) -> Result<serde_json::Value> {
        self.post_json("/v1/index/sync", request)
    }

    fn get_json<T>(&self, path: &str) -> Result<T>
    where
        T: DeserializeOwned,
    {
        let response = self.agent
            .get(&self.url(path))
            .header(
                "Authorization",
                &format!("Bearer {}", self.connection.token.trim()),
            )
            .call()
            .map_err(|error| anyhow!("Request to {} failed: {}", self.url(path), error))?;
        Self::read_json(response)
    }

    fn post_json<TResponse, TRequest>(&self, path: &str, request: &TRequest) -> Result<TResponse>
    where
        TResponse: DeserializeOwned,
        TRequest: Serialize,
    {
        let body = serde_json::to_vec(request).context("failed to serialize request")?;
        let response = self.agent
            .post(&self.url(path))
            .header(
                "Authorization",
                &format!("Bearer {}", self.connection.token.trim()),
            )
            .header("Content-Type", "application/json")
            .send(body.as_slice())
            .map_err(|error| anyhow!("Request to {} failed: {}", self.url(path), error))?;
        Self::read_json(response)
    }

    /// Deserializes a JSON response body into a target type.
    fn read_json<T>(response: ureq::http::Response<ureq::Body>) -> Result<T>
    where
        T: DeserializeOwned,
    {
        let mut body = response.into_body();
        let payload = body
            .read_to_string()
            .context("failed to read response body")?;
        serde_json::from_str(&payload).context("failed to parse server response")
    }

    /// Combines the normalized endpoint and a relative API path.
    fn url(&self, path: &str) -> String {
        format!("{}{}", self.connection.endpoint.trim_end_matches('/'), path)
    }
}

/// Payload for syncing a project's code index to the server.
#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct IndexSyncPayload {
    pub project_id: String,
    pub files: Vec<IndexSyncFile>,
    pub symbols: Vec<IndexSyncSymbol>,
    pub edges: Vec<IndexSyncEdge>,
}

/// A single file entry in the index sync payload.
#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct IndexSyncFile {
    pub path: String,
    pub hash: String,
    pub language: String,
    pub line_count: usize,
    pub token_count: usize,
    pub exports: Vec<String>,
    pub summary: String,
}

/// A single symbol entry in the index sync payload.
#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct IndexSyncSymbol {
    pub file_path: String,
    pub name: String,
    pub kind: String,
    pub start_line: usize,
    pub end_line: usize,
    pub is_exported: bool,
}

/// A single call edge in the index sync payload.
#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct IndexSyncEdge {
    pub from_symbol: String,
    pub to_symbol: String,
    pub kind: String,
}

/// Posts every current, high-confidence fact from local `knowledge.json` to the
/// server-backed `ctx_knowledge` store. Called after auto-consolidation and from
/// `handle_stop()` to keep PostgreSQL in sync with the local session outcome.
/// Silently returns if the server is not configured or any call fails.
pub fn post_knowledge_to_server(project_root: &str) {
    let ctx = crate::git_context::discover_project_context(std::path::Path::new(project_root));
    let knowledge = crate::core::knowledge::ProjectKnowledge::load_or_create(project_root);
    let shared_context = shared_memory_project_context(&ctx);

    let items: Vec<Value> = knowledge
        .facts
        .iter()
        .filter(|f| f.is_current() && f.confidence >= 0.7)
        .map(|fact| {
            let source_scope = fact.source_session.clone();
            let promotion_identity = deterministic_promotion_identity(
                "promote",
                &source_scope,
                &fact.category,
                &fact.key,
            );
            serde_json::json!({
                "category": fact.category,
                "key": fact.key,
                "value": fact.value,
                "confidence": fact.confidence,
                "source_type": "promote",
                "source_scope": source_scope,
                "promotion_identity": promotion_identity,
            })
        })
        .collect();

    if items.is_empty() {
        return;
    }

    let mut args = Map::new();
    args.insert("action".to_string(), Value::String("promote".to_string()));
    args.insert("items".to_string(), Value::Array(items));
    let _ = queue_or_call_tool("ctx_knowledge", args.clone(), &ctx);
    if let Some(shared_context) = shared_context.as_ref() {
        let shared_args = namespace_shared_memory_arguments(&ctx, &args);
        let _ = queue_or_call_tool("ctx_knowledge", shared_args, shared_context);
    }
}

pub fn post_brain_facts_to_server(
    project_root: &str,
    facts: &[crate::core::brain_memory::BrainFactCandidate],
) {
    let ctx = crate::git_context::discover_project_context(std::path::Path::new(project_root));
    for fact in facts {
        let mut args = Map::new();
        args.insert("action".to_string(), Value::String("ingest".to_string()));
        args.insert("key".to_string(), Value::String(fact.key.clone()));
        args.insert("value".to_string(), Value::String(fact.value.clone()));
        args.insert("kind".to_string(), Value::String(fact.kind.clone()));
        args.insert("category".to_string(), Value::String(fact.category.clone()));
        args.insert(
            "source_type".to_string(),
            Value::String(fact.source_type.clone()),
        );
        args.insert(
            "source_scope".to_string(),
            Value::String(fact.source_scope.clone()),
        );
        args.insert(
            "promotion_identity".to_string(),
            Value::String(fact.promotion_identity.clone()),
        );
        args.insert(
            "logical_key".to_string(),
            Value::String(fact.logical_key.clone()),
        );
        args.insert(
            "confidence".to_string(),
            Value::Number(
                serde_json::Number::from_f64(fact.confidence as f64)
                    .unwrap_or_else(|| serde_json::Number::from(1)),
            ),
        );
        args.insert("evidence".to_string(), Value::String(fact.evidence.clone()));
        let _ = queue_or_call_tool("ctx_brain", args, &ctx);
    }
}

pub fn post_durable_memory_candidates_to_server(
    project_root: &str,
    candidates: &[crate::core::brain_memory::DurableMemoryCandidate],
) {
    if candidates.is_empty() {
        return;
    }

    let ctx = crate::git_context::discover_project_context(std::path::Path::new(project_root));
    let shared_context = shared_memory_project_context(&ctx);
    let items = candidates
        .iter()
        .map(|candidate| {
            serde_json::json!({
                "category": candidate.category,
                "key": candidate.key,
                "value": candidate.value,
                "confidence": candidate.confidence,
                "source_type": candidate.source_type,
                "source_scope": candidate.source_scope,
                "promotion_identity": candidate.promotion_identity,
                "logical_key": candidate.logical_key,
                "evidence": candidate.evidence,
            })
        })
        .collect();

    let mut args = Map::new();
    args.insert("action".to_string(), Value::String("promote".to_string()));
    args.insert("items".to_string(), Value::Array(items));
    let _ = queue_or_call_tool("ctx_knowledge", args.clone(), &ctx);
    if let Some(shared_context) = shared_context.as_ref() {
        let shared_args = namespace_shared_memory_arguments(&ctx, &args);
        let _ = queue_or_call_tool("ctx_knowledge", shared_args, shared_context);
    }
}

pub fn post_journal_events_to_server(
    project_root: &str,
    events: &[crate::core::brain_memory::JournalEvent],
) {
    let ctx = crate::git_context::discover_project_context(std::path::Path::new(project_root));
    for event in events {
        let event_kind = event_kind_name(&event.kind);
        let key = format!(
            "timeline-{}-{}-{}-{}",
            event.timestamp.timestamp_millis(),
            normalize_identity_token(&event.session_id),
            normalize_identity_token(&event_kind),
            event_fingerprint(event)
        );
        let source_type = event_kind_source_type(&event.kind);
        let mut args = Map::new();
        args.insert("action".to_string(), Value::String("ingest".to_string()));
        args.insert("key".to_string(), Value::String(key.clone()));
        args.insert("value".to_string(), Value::String(event.text.clone()));
        args.insert(
            "kind".to_string(),
            Value::String("session_event".to_string()),
        );
        args.insert(
            "category".to_string(),
            Value::String("session_timeline".to_string()),
        );
        args.insert(
            "source_type".to_string(),
            Value::String(source_type.clone()),
        );
        args.insert(
            "source_scope".to_string(),
            Value::String(event.session_id.clone()),
        );
        args.insert(
            "promotion_identity".to_string(),
            Value::String(deterministic_promotion_identity(
                &source_type,
                &event.session_id,
                "session_timeline",
                &key,
            )),
        );
        args.insert("logical_key".to_string(), Value::String(key));
        args.insert(
            "lifecycle_status".to_string(),
            Value::String("timeline".to_string()),
        );
        args.insert(
            "created_at".to_string(),
            Value::String(event.timestamp.to_rfc3339()),
        );
        args.insert(
            "confidence".to_string(),
            Value::Number(
                serde_json::Number::from_f64(0.6).unwrap_or_else(|| serde_json::Number::from(1)),
            ),
        );
        args.insert(
            "evidence".to_string(),
            Value::String(build_journal_event_evidence(event)),
        );
        let _ = queue_or_call_tool("ctx_brain", args, &ctx);
    }
}

/// Posts a session summary to `ctx_brain` when a session is saved.
/// Silently returns if the server is not configured.
pub fn post_session_to_brain(session: &crate::core::session::SessionState) {
    let _ = session;
}

/// Builds the synthetic project context used for shared user-wide memory.
pub(crate) fn shared_memory_project_context(
    project_context: &ProjectContext,
) -> Option<ProjectContext> {
    let client = ServerClient::load().ok()?;
    Some(shared_memory_project_context_from_token(
        project_context,
        &client.connection.token,
    ))
}

/// Applies a repo-specific namespace to shared-memory writes so repositories
/// using the same token do not overwrite each other's shared facts.
fn namespace_shared_memory_arguments(
    project_context: &ProjectContext,
    arguments: &Map<String, Value>,
) -> Map<String, Value> {
    let mut namespaced = arguments.clone();
    let namespace = shared_memory_namespace(project_context);
    if let Some(Value::Array(items)) = namespaced.get_mut("items") {
        for item in items {
            if let Some(object) = item.as_object_mut() {
                namespace_shared_memory_item(object, &namespace);
            }
        }
    }

    namespaced
}

/// Prefixes the fields that participate in shared-memory uniqueness checks.
fn namespace_shared_memory_item(item: &mut serde_json::Map<String, Value>, namespace: &str) {
    for field in ["key", "logical_key", "promotion_identity", "source_scope"] {
        if let Some(Value::String(value)) = item.get_mut(field) {
            *value = format!("{namespace}:{value}");
        }
    }
}

/// Builds a stable repository namespace for shared-memory writes.
fn shared_memory_namespace(project_context: &ProjectContext) -> String {
    let basis = project_context
        .fingerprint
        .remote_url
        .as_deref()
        .or(project_context.fingerprint.host.as_deref())
        .or(project_context.fingerprint.owner.as_deref())
        .or(project_context.fingerprint.repo_name.as_deref())
        .unwrap_or(project_context.project_slug.as_str());

    let mut hasher = Md5::new();
    hasher.update(basis.trim().as_bytes());
    format!("repo-{:x}", hasher.finalize())
}

/// Derives a stable shared-memory project context from the active server token.
fn shared_memory_project_context_from_token(
    project_context: &ProjectContext,
    token: &str,
) -> ProjectContext {
    let token_hash = shared_memory_namespace_hash(token);
    let mut checkout_binding = project_context.checkout_binding.clone();
    checkout_binding.client_label = Some("shared-memory".to_string());

    ProjectContext {
        project_slug: format!("shared-memory-{}", &token_hash[..12]),
        project_root: project_context.project_root.clone(),
        fingerprint: RepositoryFingerprint {
            remote_url: Some(format!("nebu-ctx://shared/{token_hash}")),
            host: Some("nebu-ctx".to_string()),
            owner: Some("shared".to_string()),
            repo_name: Some(format!("user-{token_hash}")),
            default_branch: Some("main".to_string()),
        },
        checkout_binding,
        project_metadata: None,
    }
}

/// Hashes the server token into a deterministic shared-memory namespace id.
fn shared_memory_namespace_hash(token: &str) -> String {
    let mut hasher = Md5::new();
    hasher.update(token.trim().as_bytes());
    format!("{:x}", hasher.finalize())
}

pub fn sync_session_memory_to_server(project_root: &str, source_type: &str) {
    if project_root.trim().is_empty() {
        return;
    }

    let repo_memory_synced = sync_copilot_repo_memories_to_server(project_root);

    if !should_sync_hosted_memory(project_root) {
        return;
    }

    let mut synced_anything = false;
    let recent_events = load_recent_unsynced_journal_events(project_root).ok();

    if let Ok(outcome) = crate::core::brain_memory::flush_to_brain(project_root, source_type) {
        if outcome.derived_facts > 0 {
            synced_anything = true;
        }
    }

    if let Some(events) =
        recent_events.or_else(|| crate::core::brain_memory::load_events(project_root).ok())
    {
        let candidates = crate::core::brain_memory::derive_durable_memory_candidates(
            project_root,
            source_type,
            &events,
        );
        if !candidates.is_empty() {
            post_durable_memory_candidates_to_server(project_root, &candidates);
            synced_anything = true;
        }
    }

    if synced_anything || repo_memory_synced {
        let _ = write_hosted_memory_sync_marker(project_root, now_unix_seconds());
        let _ = write_journal_sync_marker(
            project_root,
            latest_journal_event_millis(project_root).unwrap_or_default(),
        );
    }
}

// Import Copilot repo memory markdown files from VS Code workspaceStorage into hosted knowledge.
fn sync_copilot_repo_memories_to_server(project_root: &str) -> bool {
    let Some(memory_dir) = find_copilot_repo_memory_dir(project_root) else {
        return false;
    };

    let Ok(records) = load_repo_memory_records(&memory_dir) else {
        return false;
    };

    let previous_state = read_repo_memory_sync_state(project_root).unwrap_or_default();
    if records.is_empty() && previous_state.entries.is_empty() {
        return false;
    }

    let digest = hash_repo_memory_records(&records);
    if previous_state.digest == digest {
        return false;
    }

    let ctx = crate::git_context::discover_project_context(Path::new(project_root));
    let current_entries: Vec<RepoMemoryStateEntry> = records
        .iter()
        .map(|record| RepoMemoryStateEntry {
            category: record.category.clone(),
            key: record.key.clone(),
        })
        .collect();

    let removed_entries: Vec<RepoMemoryStateEntry> = previous_state
        .entries
        .iter()
        .filter(|entry| !current_entries.contains(entry))
        .cloned()
        .collect();

    if !records.is_empty() {
        let memories: Vec<Value> = records
            .iter()
            .map(|record| {
                serde_json::json!({
                    "category": record.category,
                    "key": record.key,
                    "value": record.value,
                    "confidence": 0.98,
                    "source_type": COPILOT_REPO_MEMORY_SOURCE_TYPE,
                    "source_scope": record.source_scope,
                })
            })
            .collect();

        let mut import_args = Map::new();
        import_args.insert("action".to_string(), Value::String("import".to_string()));
        import_args.insert(
            "import_payload".to_string(),
            serde_json::json!({ "memories": memories }),
        );
        import_args.insert("overwrite".to_string(), Value::Bool(true));

        if queue_or_call_tool("ctx_knowledge", import_args, &ctx).is_err() {
            return false;
        }
    }

    for entry in &removed_entries {
        let mut remove_args = Map::new();
        remove_args.insert("action".to_string(), Value::String("remove".to_string()));
        remove_args.insert(
            "category".to_string(),
            Value::String(entry.category.clone()),
        );
        remove_args.insert("key".to_string(), Value::String(entry.key.clone()));
        if queue_or_call_tool("ctx_knowledge", remove_args, &ctx).is_err() {
            return false;
        }
    }

    let next_state = RepoMemorySyncState {
        digest,
        entries: current_entries,
    };
    write_repo_memory_sync_state(project_root, &next_state).is_ok()
}

fn load_recent_unsynced_journal_events(
    project_root: &str,
) -> Result<Vec<crate::core::brain_memory::JournalEvent>> {
    let events =
        crate::core::brain_memory::load_events(project_root).map_err(anyhow::Error::msg)?;
    let last_synced = read_journal_sync_marker(project_root).unwrap_or_default();
    Ok(events
        .into_iter()
        .filter(|event| event.timestamp.timestamp_millis() > last_synced)
        .rev()
        .take(6)
        .collect::<Vec<_>>()
        .into_iter()
        .rev()
        .collect())
}

fn latest_journal_event_millis(project_root: &str) -> Option<i64> {
    crate::core::brain_memory::load_events(project_root)
        .ok()?
        .last()
        .map(|event| event.timestamp.timestamp_millis())
}

// Resolve the VS Code workspaceStorage repo-memory directory for the current project root.
fn find_copilot_repo_memory_dir(project_root: &str) -> Option<PathBuf> {
    let normalized_project_root = normalize_compare_path(project_root)?;

    for user_dir in candidate_vscode_user_dirs() {
        let workspace_storage = user_dir.join("workspaceStorage");
        let Ok(entries) = std::fs::read_dir(&workspace_storage) else {
            continue;
        };
        for entry in entries.flatten() {
            let workspace_dir = entry.path();
            let workspace_json = workspace_dir.join("workspace.json");
            if !workspace_json.exists() {
                continue;
            }

            if workspace_matches_project(&workspace_json, &normalized_project_root) {
                let memory_dir = workspace_dir
                    .join("GitHub.copilot-chat")
                    .join("memory-tool")
                    .join("memories")
                    .join("repo");
                if memory_dir.is_dir() {
                    return Some(memory_dir);
                }
            }
        }
    }

    None
}

fn candidate_vscode_user_dirs() -> Vec<PathBuf> {
    let mut candidates = Vec::new();

    if let Ok(explicit) = std::env::var("NEBU_CTX_VSCODE_USER_DATA_DIR") {
        let trimmed = explicit.trim();
        if !trimmed.is_empty() {
            candidates.push(PathBuf::from(trimmed));
        }
    }

    if let Ok(appdata) = std::env::var("APPDATA") {
        let trimmed = appdata.trim();
        if !trimmed.is_empty() {
            let base = PathBuf::from(trimmed);
            candidates.push(base.join("Code").join("User"));
            candidates.push(base.join("Code - Insiders").join("User"));
        }
    }

    if let Some(home) = crate::config::preferred_os_home_dir() {
        candidates.push(
            home.join("scoop")
                .join("persist")
                .join("vscode")
                .join("data")
                .join("user-data")
                .join("User"),
        );
        candidates.push(
            home.join("AppData")
                .join("Roaming")
                .join("Code")
                .join("User"),
        );
        candidates.push(
            home.join("AppData")
                .join("Roaming")
                .join("Code - Insiders")
                .join("User"),
        );
    }

    let mut unique = Vec::new();
    for candidate in candidates {
        if candidate.is_dir()
            && !unique
                .iter()
                .any(|existing: &PathBuf| existing == &candidate)
        {
            unique.push(candidate);
        }
    }
    unique
}

fn workspace_matches_project(workspace_json: &Path, normalized_project_root: &str) -> bool {
    let payload = std::fs::read_to_string(workspace_json).ok();
    let Some(raw) = payload else {
        return false;
    };

    let Ok(parsed) = serde_json::from_str::<serde_json::Value>(&raw) else {
        return false;
    };
    if let Some(folder) = parsed.get("folder").and_then(Value::as_str) {
        return file_uri_matches_project(folder, normalized_project_root);
    }

    if let Some(workspace_file) = parsed.get("workspace").and_then(Value::as_str) {
        return workspace_file_matches_project(workspace_file, normalized_project_root);
    }

    false
}

fn workspace_file_matches_project(workspace_uri: &str, normalized_project_root: &str) -> bool {
    let Some(workspace_path) = file_uri_to_path(workspace_uri) else {
        return false;
    };
    let workspace_dir = workspace_path.parent().unwrap_or_else(|| Path::new("."));
    let content = std::fs::read_to_string(&workspace_path).ok();
    let Some(raw) = content else {
        return false;
    };
    let Ok(parsed) = serde_json::from_str::<serde_json::Value>(&raw) else {
        return false;
    };
    let Some(folders) = parsed.get("folders").and_then(Value::as_array) else {
        return false;
    };
    folders.iter().any(|folder| {
        workspace_folder_matches_project(folder, workspace_dir, normalized_project_root)
    })
}

fn workspace_folder_matches_project(
    folder: &Value,
    workspace_dir: &Path,
    normalized_project_root: &str,
) -> bool {
    folder
        .get("path")
        .and_then(Value::as_str)
        .and_then(|path| resolve_workspace_folder_path(workspace_dir, path))
        .is_some_and(|path| path == normalized_project_root)
        || folder
            .get("uri")
            .and_then(Value::as_str)
            .is_some_and(|uri| file_uri_matches_project(uri, normalized_project_root))
}

fn resolve_workspace_folder_path(workspace_dir: &Path, folder_path: &str) -> Option<String> {
    let candidate = PathBuf::from(folder_path);
    let resolved = if candidate.is_absolute() {
        candidate
    } else {
        workspace_dir.join(candidate)
    };

    normalize_compare_path(resolved.to_string_lossy().as_ref())
}

fn file_uri_matches_project(uri: &str, normalized_project_root: &str) -> bool {
    file_uri_to_path(uri)
        .and_then(|path| normalize_compare_path(path.to_string_lossy().as_ref()))
        .is_some_and(|path| path == normalized_project_root)
}

fn file_uri_to_path(uri: &str) -> Option<PathBuf> {
    let trimmed = uri.trim();
    let path_part = trimmed.strip_prefix("file://")?;
    let decoded = percent_decode(path_part).ok()?;
    if cfg!(windows) {
        let without_prefix = decoded.strip_prefix('/').unwrap_or(&decoded);
        Some(PathBuf::from(without_prefix.replace('/', "\\")))
    } else {
        Some(PathBuf::from(decoded))
    }
}

fn percent_decode(input: &str) -> Result<String> {
    let mut bytes = Vec::with_capacity(input.len());
    let input_bytes = input.as_bytes();
    let mut index = 0;
    while index < input_bytes.len() {
        if input_bytes[index] == b'%' && index + 2 < input_bytes.len() {
            let hex = std::str::from_utf8(&input_bytes[index + 1..index + 3])
                .map_err(|error| anyhow!(error.to_string()))?;
            let value = u8::from_str_radix(hex, 16).map_err(|error| anyhow!(error.to_string()))?;
            bytes.push(value);
            index += 3;
            continue;
        }
        bytes.push(input_bytes[index]);
        index += 1;
    }
    String::from_utf8(bytes).map_err(|error| anyhow!(error.to_string()))
}

fn normalize_compare_path(path: &str) -> Option<String> {
    let trimmed = path.trim();
    if trimmed.is_empty() {
        return None;
    }

    let normalized = trimmed.replace('\\', "/").trim_end_matches('/').to_string();
    if cfg!(windows) {
        Some(normalized.to_lowercase())
    } else {
        Some(normalized)
    }
}

fn load_repo_memory_records(memory_dir: &Path) -> Result<Vec<RepoMemoryRecord>> {
    let mut markdown_files: Vec<PathBuf> = std::fs::read_dir(memory_dir)
        .map_err(|error| anyhow!(error.to_string()))?
        .flatten()
        .map(|entry| entry.path())
        .filter(|path| {
            path.extension()
                .is_some_and(|extension| extension.eq_ignore_ascii_case("md"))
        })
        .collect();
    markdown_files.sort();

    let mut records = Vec::new();
    for file_path in markdown_files {
        let source_scope = file_path
            .file_name()
            .and_then(|name| name.to_str())
            .unwrap_or_default()
            .to_string();
        if source_scope.is_empty() {
            continue;
        }

        let category = file_path
            .file_stem()
            .and_then(|stem| stem.to_str())
            .map(normalize_identity_token)
            .filter(|value| !value.is_empty())
            .unwrap_or_else(|| "repo-memory".to_string());

        let content =
            std::fs::read_to_string(&file_path).map_err(|error| anyhow!(error.to_string()))?;
        for (index, line) in extract_repo_memory_lines(&content).into_iter().enumerate() {
            records.push(RepoMemoryRecord {
                category: category.clone(),
                key: format!("{}-{}", category, index + 1),
                value: line,
                source_scope: source_scope.clone(),
            });
        }
    }

    Ok(records)
}

fn extract_repo_memory_lines(content: &str) -> Vec<String> {
    content.lines().filter_map(clean_repo_memory_line).collect()
}

fn clean_repo_memory_line(line: &str) -> Option<String> {
    let trimmed = line.trim();
    if trimmed.is_empty()
        || trimmed.starts_with('#')
        || trimmed.starts_with("<!--")
        || trimmed.starts_with("```")
    {
        return None;
    }

    let without_bullet = trimmed
        .strip_prefix("- ")
        .or_else(|| trimmed.strip_prefix("* "))
        .or_else(|| trimmed.strip_prefix("+ "))
        .unwrap_or(trimmed);
    let without_number = strip_markdown_number_prefix(without_bullet).unwrap_or(without_bullet);
    let cleaned = without_number.trim();
    if cleaned.is_empty() {
        None
    } else {
        Some(cleaned.to_string())
    }
}

fn strip_markdown_number_prefix(line: &str) -> Option<&str> {
    let mut digits = 0usize;
    for ch in line.chars() {
        if ch.is_ascii_digit() {
            digits += 1;
            continue;
        }
        break;
    }

    if digits == 0 {
        return None;
    }

    let rest = &line[digits..];
    rest.strip_prefix('.')
        .map(str::trim_start)
        .filter(|tail| !tail.is_empty())
}

fn hash_repo_memory_records(records: &[RepoMemoryRecord]) -> String {
    let mut hasher = Md5::new();
    for record in records {
        hasher.update(record.category.as_bytes());
        hasher.update(record.key.as_bytes());
        hasher.update(record.value.as_bytes());
        hasher.update(record.source_scope.as_bytes());
    }
    format!("{:x}", hasher.finalize())
}

fn repo_memory_sync_state_path(project_root: &str) -> Result<PathBuf> {
    let project_hash = crate::core::project_hash::hash_project_root(project_root);
    Ok(crate::core::data_dir::nebu_ctx_data_dir()
        .map_err(anyhow::Error::msg)?
        .join("sync")
        .join("memory")
        .join(format!("{project_hash}.repo_memories.json")))
}

fn read_repo_memory_sync_state(project_root: &str) -> Option<RepoMemorySyncState> {
    let path = repo_memory_sync_state_path(project_root).ok()?;
    let raw = std::fs::read_to_string(path).ok()?;
    serde_json::from_str(&raw).ok()
}

fn write_repo_memory_sync_state(project_root: &str, state: &RepoMemorySyncState) -> Result<()> {
    let path = repo_memory_sync_state_path(project_root)?;
    if let Some(parent) = path.parent() {
        std::fs::create_dir_all(parent).map_err(|error| anyhow!(error.to_string()))?;
    }
    let payload =
        serde_json::to_string_pretty(state).map_err(|error| anyhow!(error.to_string()))?;
    std::fs::write(path, payload).map_err(|error| anyhow!(error.to_string()))
}

fn should_sync_hosted_memory(project_root: &str) -> bool {
    match read_hosted_memory_sync_marker(project_root) {
        Some(last_sync) => {
            now_unix_seconds().saturating_sub(last_sync) >= HOSTED_MEMORY_SYNC_DEBOUNCE_SECS
        }
        None => true,
    }
}

fn hosted_memory_sync_marker_path(project_root: &str) -> Result<std::path::PathBuf> {
    let project_hash = crate::core::project_hash::hash_project_root(project_root);
    Ok(crate::core::data_dir::nebu_ctx_data_dir()
        .map_err(anyhow::Error::msg)?
        .join("sync")
        .join("memory")
        .join(format!("{project_hash}.last_sync")))
}

fn journal_sync_marker_path(project_root: &str) -> Result<std::path::PathBuf> {
    let project_hash = crate::core::project_hash::hash_project_root(project_root);
    Ok(crate::core::data_dir::nebu_ctx_data_dir()
        .map_err(anyhow::Error::msg)?
        .join("sync")
        .join("memory")
        .join(format!("{project_hash}.journal_sync")))
}

fn read_hosted_memory_sync_marker(project_root: &str) -> Option<i64> {
    let path = hosted_memory_sync_marker_path(project_root).ok()?;
    let raw = std::fs::read_to_string(path).ok()?;
    raw.trim().parse::<i64>().ok()
}

fn read_journal_sync_marker(project_root: &str) -> Option<i64> {
    let path = journal_sync_marker_path(project_root).ok()?;
    let raw = std::fs::read_to_string(path).ok()?;
    raw.trim().parse::<i64>().ok()
}

fn write_hosted_memory_sync_marker(project_root: &str, timestamp: i64) -> Result<()> {
    let path = hosted_memory_sync_marker_path(project_root)?;
    if let Some(parent) = path.parent() {
        std::fs::create_dir_all(parent).map_err(|error| anyhow!(error.to_string()))?;
    }

    std::fs::write(path, timestamp.to_string()).map_err(|error| anyhow!(error.to_string()))
}

fn write_journal_sync_marker(project_root: &str, timestamp: i64) -> Result<()> {
    let path = journal_sync_marker_path(project_root)?;
    if let Some(parent) = path.parent() {
        std::fs::create_dir_all(parent).map_err(|error| anyhow!(error.to_string()))?;
    }

    std::fs::write(path, timestamp.to_string()).map_err(|error| anyhow!(error.to_string()))
}

fn event_kind_source_type(kind: &crate::core::brain_memory::LifecycleEventKind) -> String {
    match kind {
        crate::core::brain_memory::LifecycleEventKind::SessionStart => "session_start",
        crate::core::brain_memory::LifecycleEventKind::UserTurn => "user_turn",
        crate::core::brain_memory::LifecycleEventKind::AssistantTurn => "assistant_output",
        crate::core::brain_memory::LifecycleEventKind::ToolActivity => "tool_activity",
        crate::core::brain_memory::LifecycleEventKind::PreCompact => "pre_compact",
        crate::core::brain_memory::LifecycleEventKind::IdleFlush => "idle_flush",
        crate::core::brain_memory::LifecycleEventKind::SessionStop => "stop",
    }
    .to_string()
}

fn event_kind_name(kind: &crate::core::brain_memory::LifecycleEventKind) -> &'static str {
    match kind {
        crate::core::brain_memory::LifecycleEventKind::SessionStart => "session_start",
        crate::core::brain_memory::LifecycleEventKind::UserTurn => "user_turn",
        crate::core::brain_memory::LifecycleEventKind::AssistantTurn => "assistant_turn",
        crate::core::brain_memory::LifecycleEventKind::ToolActivity => "tool_activity",
        crate::core::brain_memory::LifecycleEventKind::PreCompact => "pre_compact",
        crate::core::brain_memory::LifecycleEventKind::IdleFlush => "idle_flush",
        crate::core::brain_memory::LifecycleEventKind::SessionStop => "session_stop",
    }
}

fn event_fingerprint(event: &crate::core::brain_memory::JournalEvent) -> String {
    let mut hasher = Md5::new();
    hasher.update(event.session_id.as_bytes());
    hasher.update(event_kind_name(&event.kind).as_bytes());
    hasher.update(event.source.as_bytes());
    hasher.update(event.text.as_bytes());
    if let Some(tool) = event.tool.as_deref() {
        hasher.update(tool.as_bytes());
    }
    if let Some(command) = event.command.as_deref() {
        hasher.update(command.as_bytes());
    }
    format!("{:x}", hasher.finalize())[..10].to_string()
}

fn build_journal_event_evidence(event: &crate::core::brain_memory::JournalEvent) -> String {
    let mut parts = vec![
        format!("source={}", event.source),
        format!("timestamp={}", event.timestamp.to_rfc3339()),
    ];
    if let Some(tool) = event.tool.as_deref() {
        parts.push(format!("tool={tool}"));
    }
    if let Some(command) = event.command.as_deref() {
        parts.push(format!("command={command}"));
    }
    parts.join(" ")
}

fn now_unix_seconds() -> i64 {
    SystemTime::now()
        .duration_since(UNIX_EPOCH)
        .map(|duration| duration.as_secs() as i64)
        .unwrap_or(0)
}

pub fn queue_or_call_tool(
    tool_name: &str,
    arguments: Map<String, Value>,
    project_context: &ProjectContext,
) -> Result<()> {
    if let Ok(client) = ServerClient::load() {
        if client
            .call_tool(tool_name, arguments.clone(), project_context)
            .is_ok()
        {
            return Ok(());
        }
    }

    let queued = QueuedServerToolCall {
        tool_name: tool_name.to_string(),
        arguments,
        project_context: project_context.into(),
        operation_id: None,
    };
    crate::core::sync_outbox::enqueue(
        crate::core::sync_outbox::OutboxOperationKind::ServerToolCall,
        serde_json::to_value(queued)
            .context("failed to serialize queued server tool call")?,
    )
    .map(|_| ())
    .map_err(anyhow::Error::msg)
}

pub fn replay_queued_server_tool_call(payload: serde_json::Value) -> Result<()> {
    let queued: QueuedServerToolCall =
        serde_json::from_value(payload).context("failed to deserialize queued server tool call")?;
    let client = ServerClient::load()?;
    let context: ProjectContext = queued.project_context.into();
    client.call_tool(&queued.tool_name, queued.arguments, &context)?;
    Ok(())
}

pub fn deterministic_promotion_identity(
    source_type: &str,
    source_scope: &str,
    category: &str,
    key: &str,
) -> String {
    let canonical = format!(
        "{}:{}:{}:{}",
        normalize_identity_token(source_type),
        normalize_identity_token(source_scope),
        normalize_identity_token(category),
        normalize_identity_token(key)
    );

    let mut hasher = Md5::new();
    hasher.update(canonical.as_bytes());
    format!("{}:{:x}", canonical, hasher.finalize())
}

fn normalize_identity_token(value: &str) -> String {
    let lowered = value.trim().to_lowercase();
    let mut normalized = lowered
        .chars()
        .map(|ch| if ch.is_ascii_alphanumeric() { ch } else { '-' })
        .collect::<String>();

    while normalized.contains("--") {
        normalized = normalized.replace("--", "-");
    }

    let trimmed = normalized.trim_matches('-');
    if trimmed.is_empty() {
        "unknown".to_string()
    } else {
        trimmed.to_string()
    }
}

pub fn queue_or_sync_index(
    project_context: &ProjectContext,
    files: Vec<IndexSyncFile>,
    symbols: Vec<IndexSyncSymbol>,
    edges: Vec<IndexSyncEdge>,
) -> Result<()> {
    if let Ok(client) = ServerClient::load() {
        if let Ok(resolved) = client.resolve_project(project_context) {
            let payload = IndexSyncPayload {
                project_id: resolved.project.project_id,
                files: files.clone(),
                symbols: symbols.clone(),
                edges: edges.clone(),
            };
            if client.sync_index(&payload).is_ok() {
                return Ok(());
            }
        }
    }

    crate::core::sync_outbox::enqueue(
        crate::core::sync_outbox::OutboxOperationKind::CodeIndexSync,
        serde_json::to_value(QueuedIndexSync {
            project_context: project_context.into(),
            files,
            symbols,
            edges,
        })
        .context("failed to serialize queued index sync")?,
    )
    .map(|_| ())
    .map_err(anyhow::Error::msg)
}

pub fn replay_queued_index_sync(payload: serde_json::Value) -> Result<()> {
    let queued: QueuedIndexSync =
        serde_json::from_value(payload).context("failed to deserialize queued index sync")?;
    let client = ServerClient::load()?;
    let context: ProjectContext = queued.project_context.into();
    let resolved = client.resolve_project(&context)?;
    client.sync_index(&IndexSyncPayload {
        project_id: resolved.project.project_id,
        files: queued.files,
        symbols: queued.symbols,
        edges: queued.edges,
    })?;
    Ok(())
}

#[cfg(test)]
mod tests {
    use super::*;
    use std::fs;

    fn test_project_context(root: &std::path::Path) -> ProjectContext {
        ProjectContext {
            project_slug: "sync-test".to_string(),
            project_root: root.to_string_lossy().to_string(),
            fingerprint: crate::models::RepositoryFingerprint {
                remote_url: Some("https://github.com/example/sync-test.git".to_string()),
                host: Some("github.com".to_string()),
                owner: Some("example".to_string()),
                repo_name: Some("sync-test".to_string()),
                default_branch: Some("main".to_string()),
            },
            checkout_binding: crate::models::CheckoutBinding::default(),
            project_metadata: None,
        }
    }

    #[test]
    fn queue_or_sync_index_persists_when_server_unconfigured() {
        let _lock = crate::core::data_dir::test_env_lock();
        let tmp = tempfile::tempdir().unwrap();
        std::env::set_var("NEBU_CTX_DATA_DIR", tmp.path());
        std::env::set_var("NEBU_CTX_HOME", tmp.path().join("home"));

        queue_or_sync_index(
            &test_project_context(tmp.path()),
            vec![IndexSyncFile {
                path: "src/lib.rs".to_string(),
                hash: "abc".to_string(),
                language: "rust".to_string(),
                line_count: 10,
                token_count: 50,
                exports: vec!["run".to_string()],
                summary: "library".to_string(),
            }],
            vec![IndexSyncSymbol {
                file_path: "src/lib.rs".to_string(),
                name: "run".to_string(),
                kind: "function".to_string(),
                start_line: 1,
                end_line: 5,
                is_exported: true,
            }],
            vec![IndexSyncEdge {
                from_symbol: "run".to_string(),
                to_symbol: "helper".to_string(),
                kind: "calls".to_string(),
            }],
        )
        .unwrap();

        let entries = crate::core::sync_outbox::load_entries().unwrap();
        assert_eq!(entries.len(), 1);
        assert_eq!(
            entries[0].kind,
            crate::core::sync_outbox::OutboxOperationKind::CodeIndexSync
        );
        assert_eq!(entries[0].payload["files"][0]["path"], "src/lib.rs");
    }

    #[test]
    fn deterministic_promotion_identity_is_stable() {
        let first =
            deterministic_promotion_identity("promote", "session-1", "decision", "memory-owner");
        let second =
            deterministic_promotion_identity(" promote ", "session-1", "decision", "memory owner");
        assert_eq!(first, second);
        assert!(first.contains("promote:session-1:decision:memory-owner:"));
    }

    #[test]
    fn shared_memory_project_context_is_stable_for_same_token() {
        let base = test_project_context(std::path::Path::new("/tmp/project"));

        let first = shared_memory_project_context_from_token(&base, "token-1");
        let second = shared_memory_project_context_from_token(&base, "token-1");
        let third = shared_memory_project_context_from_token(&base, "token-2");

        assert_eq!(first.project_slug, second.project_slug);
        assert_eq!(first.fingerprint.repo_name, second.fingerprint.repo_name);
        assert_ne!(first.project_slug, third.project_slug);
        assert!(first.project_slug.starts_with("shared-memory-"));
        assert_eq!(
            first.checkout_binding.client_label.as_deref(),
            Some("shared-memory")
        );
    }

    #[test]
    fn post_knowledge_to_server_queues_deterministic_promote_batch_when_offline() {
        let _lock = crate::core::data_dir::test_env_lock();
        let tmp = tempfile::tempdir().unwrap();
        std::env::set_var("NEBU_CTX_DATA_DIR", tmp.path());
        std::env::set_var("NEBU_CTX_HOME", tmp.path().join("home"));

        let project_root = tmp.path().join("project");
        std::fs::create_dir_all(&project_root).unwrap();

        let mut knowledge =
            crate::core::knowledge::ProjectKnowledge::new(&project_root.to_string_lossy());
        let _ = knowledge.remember(
            "decision",
            "memory-owner",
            "server owns canonical memory",
            "session-42",
            0.95,
        );
        knowledge.save().unwrap();

        post_knowledge_to_server(&project_root.to_string_lossy());

        let entries = crate::core::sync_outbox::load_entries().unwrap();
        let entry = entries
            .into_iter()
            .find(|item| item.kind == crate::core::sync_outbox::OutboxOperationKind::ServerToolCall)
            .unwrap();
        assert_eq!(entry.payload["tool_name"], "ctx_knowledge");
        assert_eq!(entry.payload["arguments"]["action"], "promote");
        let items = entry.payload["arguments"]["items"].as_array().unwrap();
        assert_eq!(items.len(), 1);
        assert_eq!(items[0]["source_type"], "promote");
        assert_eq!(items[0]["source_scope"], "session-42");
        assert!(items[0]["promotion_identity"]
            .as_str()
            .unwrap()
            .contains("promote:session-42:decision:memory-owner:"));
    }

    #[test]
    fn post_brain_facts_to_server_queues_ingest_calls_when_offline() {
        let _lock = crate::core::data_dir::test_env_lock();
        let tmp = tempfile::tempdir().unwrap();
        std::env::set_var("NEBU_CTX_DATA_DIR", tmp.path());
        std::env::set_var("NEBU_CTX_HOME", tmp.path().join("home"));

        let project_root = tmp.path().join("project");
        std::fs::create_dir_all(&project_root).unwrap();

        post_brain_facts_to_server(
            &project_root.to_string_lossy(),
            &[crate::core::brain_memory::BrainFactCandidate {
                key: "primary-ide".to_string(),
                value: "OpenCode".to_string(),
                kind: "preference".to_string(),
                category: "workflow".to_string(),
                source_type: "idle_flush".to_string(),
                source_scope: "session-123".to_string(),
                promotion_identity: "idle-flush:session-123:workflow:primary-ide".to_string(),
                logical_key: "workflow:primary-ide".to_string(),
                confidence: 0.98,
                evidence: "derived from user-stated primary IDE".to_string(),
            }],
        );

        let entries = crate::core::sync_outbox::load_entries().unwrap();
        let entry = entries
            .into_iter()
            .find(|item| item.kind == crate::core::sync_outbox::OutboxOperationKind::ServerToolCall)
            .unwrap();
        assert_eq!(entry.payload["tool_name"], "ctx_brain");
        assert_eq!(entry.payload["arguments"]["action"], "ingest");
        assert_eq!(entry.payload["arguments"]["key"], "primary-ide");
        assert_eq!(
            entry.payload["arguments"]["promotion_identity"],
            "idle-flush:session-123:workflow:primary-ide"
        );
        assert_eq!(
            entry.payload["arguments"]["logical_key"],
            "workflow:primary-ide"
        );
    }

    #[test]
    fn sync_session_memory_to_server_queues_brain_when_offline() {
        let _lock = crate::core::data_dir::test_env_lock();
        let tmp = tempfile::tempdir().unwrap();
        std::env::set_var("NEBU_CTX_DATA_DIR", tmp.path());
        std::env::set_var("NEBU_CTX_HOME", tmp.path().join("home"));

        let project_root = tmp.path().join("project");
        std::fs::create_dir_all(&project_root).unwrap();
        let project_root_text = project_root.to_string_lossy().to_string();

        let mut session = crate::core::session::SessionState::new();
        session.project_root = Some(project_root_text.clone());
        session.set_task("Persist memory earlier", None);
        session.add_decision("Queue hosted memory on save", None);
        session.save().unwrap();

        let _ = crate::core::brain_memory::record_user_turn(
            &project_root_text,
            Some("session-1"),
            "test",
            "Brain stores derived facts only; raw journal remains local on the client.",
        );

        sync_session_memory_to_server(&project_root_text, "session_save");

        let entries = crate::core::sync_outbox::load_entries().unwrap();
        let tool_names: Vec<String> = entries
            .iter()
            .filter(|item| {
                item.kind == crate::core::sync_outbox::OutboxOperationKind::ServerToolCall
            })
            .map(|item| {
                item.payload["tool_name"]
                    .as_str()
                    .unwrap_or_default()
                    .to_string()
            })
            .collect();

        assert!(tool_names.iter().any(|name| name == "ctx_brain"));

        let brain_entries: Vec<_> = entries
            .iter()
            .filter(|item| {
                item.kind == crate::core::sync_outbox::OutboxOperationKind::ServerToolCall
                    && item.payload["tool_name"].as_str().unwrap_or_default() == "ctx_brain"
            })
            .collect();
        assert!(!brain_entries.is_empty());
        assert!(brain_entries
            .iter()
            .all(|entry| entry.payload["arguments"]["kind"] != "session_event"));
    }

    #[test]
    fn hosted_memory_sync_marker_debounces_repeated_sync() {
        let _lock = crate::core::data_dir::test_env_lock();
        let tmp = tempfile::tempdir().unwrap();
        std::env::set_var("NEBU_CTX_DATA_DIR", tmp.path());

        let project_root = tmp.path().join("project");
        std::fs::create_dir_all(&project_root).unwrap();
        let project_root_text = project_root.to_string_lossy().to_string();

        assert!(should_sync_hosted_memory(&project_root_text));
        write_hosted_memory_sync_marker(&project_root_text, now_unix_seconds()).unwrap();
        assert!(!should_sync_hosted_memory(&project_root_text));
        write_hosted_memory_sync_marker(
            &project_root_text,
            now_unix_seconds() - HOSTED_MEMORY_SYNC_DEBOUNCE_SECS - 1,
        )
        .unwrap();
        assert!(should_sync_hosted_memory(&project_root_text));
    }

    #[test]
    fn post_durable_memory_candidates_to_server_queues_promote_batch_when_offline() {
        let _lock = crate::core::data_dir::test_env_lock();
        let tmp = tempfile::tempdir().unwrap();
        std::env::set_var("NEBU_CTX_DATA_DIR", tmp.path());
        std::env::set_var("NEBU_CTX_HOME", tmp.path().join("home"));

        let project_root = tmp.path().join("project");
        std::fs::create_dir_all(&project_root).unwrap();

        post_durable_memory_candidates_to_server(
            &project_root.to_string_lossy(),
            &[crate::core::brain_memory::DurableMemoryCandidate {
                category: "root_cause".to_string(),
                key: "ha-schema-root-cause".to_string(),
                value: "Root cause: invalid schema entry modbus_entities: dict?".to_string(),
                confidence: 0.95,
                source_type: "idle_flush".to_string(),
                source_scope: "session-123".to_string(),
                promotion_identity: "idle_flush:session-123:root_cause:ha-schema-root-cause"
                    .to_string(),
                logical_key: "root_cause:ha-schema-root-cause".to_string(),
                evidence: "derived_from=assistant".to_string(),
            }],
        );

        let entries = crate::core::sync_outbox::load_entries().unwrap();
        let entry = entries
            .into_iter()
            .find(|item| item.kind == crate::core::sync_outbox::OutboxOperationKind::ServerToolCall)
            .unwrap();
        assert_eq!(entry.payload["tool_name"], "ctx_knowledge");
        assert_eq!(entry.payload["arguments"]["action"], "promote");
        let items = entry.payload["arguments"]["items"].as_array().unwrap();
        assert_eq!(items[0]["category"], "root_cause");
        assert_eq!(items[0]["evidence"], "derived_from=assistant");
    }

    #[test]
    fn find_copilot_repo_memory_dir_matches_workspace_storage_folder() {
        let _lock = crate::core::data_dir::test_env_lock();
        let temp = tempfile::tempdir().unwrap();
        let user_dir = temp.path().join("User");
        let workspace_id = "workspace-123";
        let project_root = temp.path().join("project-root");
        let workspace_dir = user_dir.join("workspaceStorage").join(workspace_id);
        let memory_dir = workspace_dir
            .join("GitHub.copilot-chat")
            .join("memory-tool")
            .join("memories")
            .join("repo");

        fs::create_dir_all(&memory_dir).unwrap();
        fs::create_dir_all(&project_root).unwrap();
        fs::write(
            workspace_dir.join("workspace.json"),
            format!(
                "{{\n  \"folder\": \"file://{}\"\n}}",
                project_root
                    .to_string_lossy()
                    .replace('\\', "/")
                    .replace(':', "%3A")
            ),
        )
        .unwrap();

        std::env::set_var("NEBU_CTX_VSCODE_USER_DATA_DIR", &user_dir);
        let found = find_copilot_repo_memory_dir(&project_root.to_string_lossy()).unwrap();
        assert_eq!(found, memory_dir);
    }

    #[test]
    fn workspace_file_matches_project_resolves_relative_folder_paths() {
        let temp = tempfile::tempdir().unwrap();
        let workspace_root = temp.path().join("workspace");
        let project_root = workspace_root.join("repos").join("example-project");
        fs::create_dir_all(&project_root).unwrap();

        let workspace_file = workspace_root.join("example.code-workspace");
        fs::create_dir_all(&workspace_root).unwrap();
        fs::write(
            &workspace_file,
            r#"{
  "folders": [
    { "path": "repos/example-project" }
  ]
}"#,
        )
        .unwrap();

        let workspace_uri = format!(
            "file://{}",
            workspace_file
                .to_string_lossy()
                .replace('\\', "/")
                .replace(':', "%3A")
        );
        let normalized_project_root =
            normalize_compare_path(&project_root.to_string_lossy()).unwrap();

        assert!(workspace_file_matches_project(
            &workspace_uri,
            &normalized_project_root
        ));
    }

    #[test]
    fn load_repo_memory_records_parses_markdown_bullets() {
        let temp = tempfile::tempdir().unwrap();
        let repo_dir = temp.path().join("repo");
        fs::create_dir_all(&repo_dir).unwrap();
        fs::write(
            repo_dir.join("aspire-testing.md"),
            "# Heading\n- First fact\n- Second fact\n\n<!-- note -->\n",
        )
        .unwrap();

        let records = load_repo_memory_records(&repo_dir).unwrap();
        assert_eq!(records.len(), 2);
        assert_eq!(records[0].category, "aspire-testing");
        assert_eq!(records[0].key, "aspire-testing-1");
        assert_eq!(records[0].value, "First fact");
        assert_eq!(records[1].key, "aspire-testing-2");
    }

    #[test]
    fn sync_session_memory_to_server_imports_repo_memories_when_present() {
        let _lock = crate::core::data_dir::test_env_lock();
        let temp = tempfile::tempdir().unwrap();
        std::env::set_var("NEBU_CTX_DATA_DIR", temp.path());
        std::env::set_var("NEBU_CTX_HOME", temp.path().join("home"));

        let user_dir = temp.path().join("User");
        let workspace_id = "workspace-123";
        let project_root = temp.path().join("project-root");
        let workspace_dir = user_dir.join("workspaceStorage").join(workspace_id);
        let memory_dir = workspace_dir
            .join("GitHub.copilot-chat")
            .join("memory-tool")
            .join("memories")
            .join("repo");
        fs::create_dir_all(&memory_dir).unwrap();
        fs::create_dir_all(&project_root).unwrap();
        fs::write(
            workspace_dir.join("workspace.json"),
            format!(
                "{{\n  \"folder\": \"file://{}\"\n}}",
                project_root
                    .to_string_lossy()
                    .replace('\\', "/")
                    .replace(':', "%3A")
            ),
        )
        .unwrap();
        fs::write(
            memory_dir.join("pim-parsing.md"),
            "- OnePIM extKey can be null\n- Guard with ?.StartsWith\n",
        )
        .unwrap();

        std::env::set_var("NEBU_CTX_VSCODE_USER_DATA_DIR", &user_dir);
        sync_session_memory_to_server(&project_root.to_string_lossy(), "session_save");

        let entries = crate::core::sync_outbox::load_entries().unwrap();
        let import_entry = entries
            .iter()
            .find(|item| {
                item.kind == crate::core::sync_outbox::OutboxOperationKind::ServerToolCall
                    && item.payload["tool_name"].as_str().unwrap_or_default() == "ctx_knowledge"
                    && item.payload["arguments"]["action"]
                        .as_str()
                        .unwrap_or_default()
                        == "import"
            })
            .expect("repo memory import should be queued");

        let memories = import_entry.payload["arguments"]["import_payload"]["memories"]
            .as_array()
            .expect("memories array");
        assert_eq!(memories.len(), 2);
        assert!(memories
            .iter()
            .all(|item| item["source_type"] == COPILOT_REPO_MEMORY_SOURCE_TYPE));
    }
}
