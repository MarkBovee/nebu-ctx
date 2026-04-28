use serde::{Deserialize, Serialize};
use serde_json::{Map, Value};

#[derive(Debug, Clone, Default, Serialize, Deserialize)]
pub struct RepositoryFingerprint {
    pub remote_url: Option<String>,
    pub host: Option<String>,
    pub owner: Option<String>,
    pub repo_name: Option<String>,
    pub default_branch: Option<String>,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct CheckoutBinding {
    pub project_id: String,
    pub local_root: Option<String>,
    pub branch: Option<String>,
    pub last_commit: Option<String>,
    pub client_label: Option<String>,
    pub last_sync: Option<String>,
}

impl Default for CheckoutBinding {
    fn default() -> Self {
        Self {
            project_id: "pending".to_string(),
            local_root: None,
            branch: None,
            last_commit: None,
            client_label: None,
            last_sync: None,
        }
    }
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct ProjectResolutionRequest {
    pub fingerprint: RepositoryFingerprint,
    pub suggested_slug: Option<String>,
    #[serde(rename = "checkout_binding", alias = "workspace_binding")]
    pub checkout_binding: Option<CheckoutBinding>,
    pub project_metadata: Option<ProjectMetadataEnvelope>,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct ProjectRecord {
    pub project_id: String,
    pub slug: String,
    pub fingerprint: Option<RepositoryFingerprint>,
    pub created_at: String,
    pub updated_at: String,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct ProjectResolutionResponse {
    pub project: ProjectRecord,
    #[serde(rename = "checkout_bound", alias = "workspace_bound")]
    pub checkout_bound: bool,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct ToolDefinition {
    pub name: String,
    pub description: String,
    #[serde(rename = "inputSchema")]
    pub input_schema: Value,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct ToolListResponse {
    pub tools: Vec<ToolDefinition>,
    pub total: usize,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct ToolCallRequest {
    pub name: String,
    pub arguments: Map<String, Value>,
    pub project_id: Option<String>,
    pub project_slug: Option<String>,
    pub repository_fingerprint: Option<RepositoryFingerprint>,
    #[serde(rename = "checkout_binding", alias = "workspace_binding")]
    pub checkout_binding: Option<CheckoutBinding>,
    pub project_metadata: Option<ProjectMetadataEnvelope>,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct ToolCallResponse {
    pub result: Value,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct ServerConnection {
    pub endpoint: String,
    pub token: String,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct ProjectMetadataEnvelope {
    pub schema_version: u32,
    pub summary: ProjectMetadataSummary,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct ProjectMetadataSummary {
    pub total_file_count: u64,
    pub source_file_count: u64,
    pub markers: Vec<String>,
    pub languages: Vec<ProjectLanguageStat>,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct ProjectLanguageStat {
    pub language: String,
    pub file_count: u64,
}

#[derive(Debug, Clone)]
pub struct ProjectContext {
    pub project_slug: String,
    pub project_root: String,
    pub fingerprint: RepositoryFingerprint,
    pub checkout_binding: CheckoutBinding,
    pub project_metadata: Option<ProjectMetadataEnvelope>,
}

pub type RepositoryContext = ProjectContext;
pub type WorkspaceBinding = CheckoutBinding;
/// Request payload for POST /v1/telemetry/ingest.
/// Only token counts and metadata — no raw file content or shell output.
#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct TelemetryIngestRequest {
    pub tool_name: String,
    pub tokens_original: i64,
    pub tokens_saved: i64,
    pub duration_ms: i64,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub mode: Option<String>,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub repository_fingerprint: Option<RepositoryFingerprint>,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub checkout_binding: Option<CheckoutBinding>,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub project_slug: Option<String>,
}
