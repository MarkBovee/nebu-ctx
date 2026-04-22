use crate::models::{RepositoryContext, RepositoryFingerprint, WorkspaceBinding};
use std::path::{Path, PathBuf};
use std::process::Command;

/// Discovers repository metadata from the current workspace so tool calls become project-aware.
pub fn discover_repository_context(current_directory: &Path) -> RepositoryContext {
    let local_root = git_output(current_directory, ["rev-parse", "--show-toplevel"])
        .map(PathBuf::from)
        .unwrap_or_else(|| current_directory.to_path_buf());
    let remote_url = git_output(&local_root, ["config", "--get", "remote.origin.url"]);
    let branch = git_output(&local_root, ["rev-parse", "--abbrev-ref", "HEAD"]);
    let last_commit = git_output(&local_root, ["rev-parse", "HEAD"]);
    let default_branch = git_output(&local_root, ["symbolic-ref", "refs/remotes/origin/HEAD"])
        .and_then(|value| value.rsplit('/').next().map(ToString::to_string));
    let parsed_remote = remote_url.as_deref().and_then(parse_remote_url);
    let suggested_slug = parsed_remote
        .as_ref()
        .map(|(_, _, repo_name)| repo_name.clone())
        .or_else(|| local_root.file_name().map(|value| value.to_string_lossy().to_string()))
        .unwrap_or_else(|| "project".to_string());

    let fingerprint = RepositoryFingerprint {
        remote_url,
        host: parsed_remote.as_ref().map(|(host, _, _)| host.clone()),
        owner: parsed_remote.as_ref().map(|(_, owner, _)| owner.clone()),
        repo_name: parsed_remote.as_ref().map(|(_, _, repo_name)| repo_name.clone()),
        default_branch,
    };

    let workspace_binding = WorkspaceBinding {
        project_id: "pending".to_string(),
        local_root: Some(local_root.to_string_lossy().to_string()),
        branch,
        last_commit,
        client_label: detect_client_label(),
        last_sync: None,
    };

    RepositoryContext {
        suggested_slug,
        fingerprint,
        workspace_binding,
    }
}

/// Parses a remote URL into host, owner and repository name.
fn parse_remote_url(remote_url: &str) -> Option<(String, String, String)> {
    let trimmed = remote_url.trim().trim_end_matches('/').trim_end_matches(".git");
    if let Some(rest) = trimmed.strip_prefix("https://") {
        return parse_host_path(rest);
    }

    if let Some(rest) = trimmed.strip_prefix("http://") {
        return parse_host_path(rest);
    }

    if let Some(rest) = trimmed.strip_prefix("ssh://git@") {
        return parse_host_path(rest);
    }

    if let Some(rest) = trimmed.strip_prefix("git@") {
        let (host, path) = rest.split_once(':')?;
        return parse_path_segments(host, path);
    }

    None
}

/// Parses a host/path URL fragment into the repo tuple.
fn parse_host_path(value: &str) -> Option<(String, String, String)> {
    let (host, path) = value.split_once('/')?;
    parse_path_segments(host, path)
}

/// Parses path segments into host, owner and repository name.
fn parse_path_segments(host: &str, path: &str) -> Option<(String, String, String)> {
    let mut segments = path.split('/').filter(|segment| !segment.is_empty());
    let owner = segments.next()?.to_string();
    let repo_name = segments.next()?.to_string();
    Some((host.to_string(), owner, repo_name))
}

/// Executes a git command and returns trimmed stdout on success.
fn git_output<const N: usize>(working_directory: &Path, args: [&str; N]) -> Option<String> {
    let output = Command::new("git")
        .args(args)
        .current_dir(working_directory)
        .output()
        .ok()?;
    if !output.status.success() {
        return None;
    }

    let value = String::from_utf8(output.stdout).ok()?;
    let trimmed = value.trim();
    if trimmed.is_empty() {
        return None;
    }

    Some(trimmed.to_string())
}

/// Derives a stable client label from common workstation environment variables.
fn detect_client_label() -> Option<String> {
    std::env::var("COMPUTERNAME")
        .ok()
        .filter(|value| !value.trim().is_empty())
        .or_else(|| std::env::var("HOSTNAME").ok().filter(|value| !value.trim().is_empty()))
}

#[cfg(test)]
mod tests {
    use super::parse_remote_url;

    #[test]
    fn parse_remote_url_supports_https_and_ssh_formats() {
        assert_eq!(
            parse_remote_url("https://github.com/MarkBovee/nebu-ctx.git"),
            Some(("github.com".to_string(), "MarkBovee".to_string(), "nebu-ctx".to_string()))
        );

        assert_eq!(
            parse_remote_url("git@github.com:MarkBovee/nebu-ctx.git"),
            Some(("github.com".to_string(), "MarkBovee".to_string(), "nebu-ctx".to_string()))
        );
    }
}