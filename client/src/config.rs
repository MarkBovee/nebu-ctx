use crate::models::ServerConnection;
use anyhow::{Context, Result};
use std::fs;
use std::path::PathBuf;

const CONNECTION_FILE: &str = "server_connection.json";

pub fn config_dir() -> PathBuf {
    let home = preferred_home_dir().unwrap_or_else(|| PathBuf::from("."));
    home.join(".nebu-ctx").join("cloud")
}

pub(crate) fn preferred_home_dir() -> Option<PathBuf> {
    for variable_name in ["NEBU_CTX_HOME", "HOME", "USERPROFILE"] {
        if let Ok(value) = std::env::var(variable_name) {
            let trimmed = value.trim();
            if !trimmed.is_empty() {
                return Some(PathBuf::from(trimmed));
            }
        }
    }

    dirs::home_dir()
}

pub(crate) fn preferred_os_home_dir() -> Option<PathBuf> {
    for variable_name in ["HOME", "USERPROFILE"] {
        if let Ok(value) = std::env::var(variable_name) {
            let trimmed = value.trim();
            if !trimmed.is_empty() {
                return Some(PathBuf::from(trimmed));
            }
        }
    }

    dirs::home_dir()
}

pub fn connection_path() -> PathBuf {
    config_dir().join(CONNECTION_FILE)
}

pub fn normalize_server_endpoint(endpoint: &str) -> String {
    let trimmed = endpoint.trim().trim_end_matches('/');
    if let Some(prefix) = trimmed.strip_suffix("/mcp") {
        return prefix.to_string();
    }

    if let Some(prefix) = trimmed.strip_suffix("/v1/tools/call") {
        return prefix.to_string();
    }

    if let Some(prefix) = trimmed.strip_suffix("/v1/tools") {
        return prefix.to_string();
    }

    if let Some(prefix) = trimmed.strip_suffix("/v1/manifest") {
        return prefix.to_string();
    }

    if let Some(prefix) = trimmed.strip_suffix("/health") {
        return prefix.to_string();
    }

    trimmed.to_string()
}

pub fn save_connection(endpoint: &str, token: &str) -> Result<ServerConnection> {
    let connection = ServerConnection {
        endpoint: normalize_server_endpoint(endpoint),
        token: token.trim().to_string(),
    };

    fs::create_dir_all(config_dir()).context("failed to create client config directory")?;
    let json = serde_json::to_string_pretty(&connection).context("failed to serialize connection")?;
    fs::write(connection_path(), json).context("failed to write connection file")?;
    Ok(connection)
}

pub fn load_connection() -> Result<Option<ServerConnection>> {
    let path = connection_path();
    if !path.exists() {
        return Ok(None);
    }

    let data = fs::read_to_string(&path).with_context(|| format!("failed to read {}", path.display()))?;
    let connection = serde_json::from_str(&data).context("failed to parse connection file")?;
    Ok(Some(connection))
}

pub fn clear_connection() -> Result<()> {
    let path = connection_path();
    if path.exists() {
        fs::remove_file(&path).with_context(|| format!("failed to remove {}", path.display()))?;
    }

    Ok(())
}

#[cfg(test)]
mod tests {
    use super::normalize_server_endpoint;

    #[test]
    fn normalize_server_endpoint_trims_known_api_paths() {
        assert_eq!(normalize_server_endpoint("http://localhost:4242/mcp"), "http://localhost:4242");
        assert_eq!(normalize_server_endpoint("http://localhost:4242/v1/tools/call"), "http://localhost:4242");
        assert_eq!(normalize_server_endpoint("http://localhost:4242/v1/tools"), "http://localhost:4242");
        assert_eq!(normalize_server_endpoint("http://localhost:4242/health"), "http://localhost:4242");
    }
}