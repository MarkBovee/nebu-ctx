use crate::config;
use crate::models::{ProjectContext, ProjectResolutionRequest, ProjectResolutionResponse, ServerConnection, ToolCallRequest, ToolCallResponse, ToolListResponse};
use anyhow::{anyhow, Context, Result};
use serde::de::DeserializeOwned;
use serde::Serialize;
use serde_json::{Map, Value};

pub struct ServerClient {
    connection: ServerConnection,
}

impl ServerClient {
    pub fn load() -> Result<Self> {
        let connection = config::load_connection()?
            .ok_or_else(|| anyhow!("No server connection saved. Run `nebu-ctx cloud connect`."))?;
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

    pub fn resolve_project(&self, project_context: &ProjectContext) -> Result<ProjectResolutionResponse> {
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

    pub fn call_tool(&self, tool_name: &str, arguments: Map<String, Value>, project_context: &ProjectContext) -> Result<Value> {
        let response: ToolCallResponse = self.post_json(
            "/v1/tools/call",
            &ToolCallRequest {
                name: tool_name.to_string(),
                arguments,
                project_id: None,
                project_slug: Some(project_context.project_slug.clone()),
                repository_fingerprint: Some(project_context.fingerprint.clone()),
                checkout_binding: Some(project_context.checkout_binding.clone()),
                project_metadata: project_context.project_metadata.clone(),
            },
        )?;

        Ok(response.result)
    }

    fn get_json<T>(&self, path: &str) -> Result<T>
    where
        T: DeserializeOwned,
    {
        let response = ureq::get(&self.url(path))
            .header("Authorization", &format!("Bearer {}", self.connection.token.trim()))
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
            .header("Authorization", &format!("Bearer {}", self.connection.token.trim()))
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