use crate::config;
use crate::models::{
    ProjectContext, ProjectResolutionRequest, ProjectResolutionResponse, ServerConnection,
    TelemetryIngestRequest, ToolCallRequest, ToolCallResponse, ToolListResponse,
};
use anyhow::{anyhow, Context, Result};
use md5::{Digest, Md5};
use serde::de::DeserializeOwned;
use serde::{Deserialize, Serialize};
use serde_json::{Map, Value};

pub struct ServerClient {
    connection: ServerConnection,
}

#[derive(Debug, Clone, serde::Serialize, serde::Deserialize)]
pub struct QueuedServerToolCall {
    pub tool_name: String,
    pub arguments: Map<String, Value>,
    pub project_context: QueuedProjectContext,
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
        Ok(Self { connection })
    }

    pub fn new(connection: ServerConnection) -> Self {
        Self { connection }
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
        let response = ureq::get(&self.url(path))
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
        let response = ureq::post(&self.url(path))
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
    let _ = queue_or_call_tool("ctx_knowledge", args, &ctx);
}

/// Posts a session summary to `ctx_brain` when a session is saved.
/// Silently returns if the server is not configured.
pub fn post_session_to_brain(session: &crate::core::session::SessionState) {
    let current_dir = std::env::current_dir().unwrap_or_default();
    let ctx = crate::git_context::discover_project_context(&current_dir);

    let task = session
        .task
        .as_ref()
        .map(|t| t.description.as_str())
        .unwrap_or("(no task)");
    let summary = format!(
        "session={} task=\"{}\" calls={} tokens_saved={} decisions={} findings={}",
        session.id,
        task,
        session.stats.total_tool_calls,
        session.stats.total_tokens_saved,
        session.decisions.len(),
        session.findings.len(),
    );
    let key = format!("session-{}", session.id);

    let mut args = Map::new();
    args.insert("action".to_string(), Value::String("store".to_string()));
    args.insert("key".to_string(), Value::String(key));
    args.insert("value".to_string(), Value::String(summary));
    let _ = queue_or_call_tool("ctx_brain", args, &ctx);
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

    crate::core::sync_outbox::enqueue(
        crate::core::sync_outbox::OutboxOperationKind::ServerToolCall,
        serde_json::to_value(QueuedServerToolCall {
            tool_name: tool_name.to_string(),
            arguments,
            project_context: project_context.into(),
        })
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
}
