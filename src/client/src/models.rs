use serde::{Deserialize, Serialize};
use serde_json::{Map, Value};

/// Canonical repository identity used by the server to resolve a project.
#[derive(Debug, Clone, Default, Serialize, Deserialize)]
pub struct RepositoryFingerprint {
    pub remote_url: Option<String>,
    pub host: Option<String>,
    pub owner: Option<String>,
    pub repo_name: Option<String>,
    pub default_branch: Option<String>,
}

/// Non-canonical local workspace metadata sent with client requests.
#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct WorkspaceBinding {
    pub project_id: String,
    pub local_root: Option<String>,
    pub branch: Option<String>,
    pub last_commit: Option<String>,
    pub client_label: Option<String>,
    pub last_sync: Option<String>,
}

impl Default for WorkspaceBinding {
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

/// Request payload for resolving or creating a server-side project.
#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct ProjectResolutionRequest {
    pub fingerprint: RepositoryFingerprint,
    pub suggested_slug: Option<String>,
    pub workspace_binding: Option<WorkspaceBinding>,
}

/// Canonical project information returned by the server.
#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct ProjectRecord {
    pub project_id: String,
    pub slug: String,
    pub fingerprint: Option<RepositoryFingerprint>,
    pub created_at: String,
    pub updated_at: String,
}

/// Response payload for project resolution requests.
#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct ProjectResolutionResponse {
    pub project: ProjectRecord,
    pub workspace_bound: bool,
}

/// Generic tool definition exposed by the server manifest.
#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct ToolDefinition {
    pub name: String,
    pub description: String,
    #[serde(rename = "inputSchema")]
    pub input_schema: Value,
}

/// Tool listing response from the server.
#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct ToolListResponse {
    pub tools: Vec<ToolDefinition>,
    pub total: usize,
}

/// Generic tool call request sent by the Rust client.
#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct ToolCallRequest {
    pub name: String,
    pub arguments: Map<String, Value>,
    pub project_id: Option<String>,
    pub project_slug: Option<String>,
    pub repository_fingerprint: Option<RepositoryFingerprint>,
    pub workspace_binding: Option<WorkspaceBinding>,
}

/// Generic tool call response from the server.
#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct ToolCallResponse {
    pub result: Value,
}

/// Thin client server connection record saved on disk.
#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct ServerConnection {
    pub endpoint: String,
    pub token: String,
}

/// Repository-derived context attached to most client requests.
#[derive(Debug, Clone)]
pub struct RepositoryContext {
    pub suggested_slug: String,
    pub fingerprint: RepositoryFingerprint,
    pub workspace_binding: WorkspaceBinding,
}