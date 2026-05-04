use crate::config;
use crate::models::{
    ProjectContext, ProjectResolutionRequest, ProjectResolutionResponse, ServerConnection,
    TelemetryIngestRequest, ToolCallRequest, ToolCallResponse, ToolListResponse,
};
use anyhow::{anyhow, Context, Result};
use serde::de::DeserializeOwned;
use serde::{Deserialize, Serialize};
use serde_json::{Map, Value};

pub struct ServerClient {
    connection: ServerConnection,
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
        let payload = body.read_to_string().context("failed to read response body")?;
        serde_json::from_str(&payload).context("failed to parse server response")
    }

    /// Combines the normalized endpoint and a relative API path.
    fn url(&self, path: &str) -> String {
        format!("{}{}", self.connection.endpoint.trim_end_matches('/'), path)
    }
}

/// Payload for syncing a project's code index to the server.
#[derive(Debug, Serialize, Deserialize)]
pub struct IndexSyncPayload {
    pub project_id: String,
    pub files: Vec<IndexSyncFile>,
    pub symbols: Vec<IndexSyncSymbol>,
    pub edges: Vec<IndexSyncEdge>,
}

/// A single file entry in the index sync payload.
#[derive(Debug, Serialize, Deserialize)]
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
#[derive(Debug, Serialize, Deserialize)]
pub struct IndexSyncSymbol {
    pub file_path: String,
    pub name: String,
    pub kind: String,
    pub start_line: usize,
    pub end_line: usize,
    pub is_exported: bool,
}

/// A single call edge in the index sync payload.
#[derive(Debug, Serialize, Deserialize)]
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
    let Ok(client) = ServerClient::load() else {
        return;
    };
    let ctx = crate::git_context::discover_project_context(std::path::Path::new(project_root));
    let knowledge = crate::core::knowledge::ProjectKnowledge::load_or_create(project_root);

    for fact in knowledge
        .facts
        .iter()
        .filter(|f| f.is_current() && f.confidence >= 0.7)
    {
        let mut args = Map::new();
        args.insert("action".to_string(), Value::String("remember".to_string()));
        args.insert("category".to_string(), Value::String(fact.category.clone()));
        args.insert("key".to_string(), Value::String(fact.key.clone()));
        args.insert("value".to_string(), Value::String(fact.value.clone()));
        args.insert(
            "confidence".to_string(),
            serde_json::json!(fact.confidence),
        );
        let _ = client.call_tool("ctx_knowledge", args, &ctx);
    }
}

/// Posts a session summary to `ctx_brain` when a session is saved.
/// Silently returns if the server is not configured.
pub fn post_session_to_brain(session: &crate::core::session::SessionState) {
    let Ok(client) = ServerClient::load() else {
        return;
    };
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
    let _ = client.call_tool("ctx_brain", args, &ctx);
}
