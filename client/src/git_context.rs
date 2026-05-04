use crate::models::{CheckoutBinding, ProjectContext, RepositoryFingerprint};
use crate::project_metadata;
use std::path::{Path, PathBuf};
use std::process::Command;

pub fn discover_project_context(current_directory: &Path) -> ProjectContext {
    let local_root = git_output(current_directory, ["rev-parse", "--show-toplevel"])
        .map(PathBuf::from)
        .unwrap_or_else(|| current_directory.to_path_buf());
    let remote_url = git_output(&local_root, ["config", "--get", "remote.origin.url"]);
    let branch = git_output(&local_root, ["rev-parse", "--abbrev-ref", "HEAD"]);
    let last_commit = git_output(&local_root, ["rev-parse", "HEAD"]);
    let default_branch = git_output(&local_root, ["symbolic-ref", "refs/remotes/origin/HEAD"])
        .and_then(|value| value.rsplit('/').next().map(ToString::to_string));
    let parsed_remote = remote_url.as_deref().and_then(parse_remote_url);
    let project_slug = parsed_remote
        .as_ref()
        .map(|(_, _, repo_name)| repo_name.clone())
        .or_else(|| fallback_project_slug(&local_root))
        .unwrap_or_else(|| "project".to_string());

    let fingerprint = RepositoryFingerprint {
        remote_url,
        host: parsed_remote.as_ref().map(|(host, _, _)| host.clone()),
        owner: parsed_remote.as_ref().map(|(_, owner, _)| owner.clone()),
        repo_name: parsed_remote.as_ref().map(|(_, _, repo_name)| repo_name.clone()),
        default_branch,
    };

    let checkout_binding = CheckoutBinding {
        project_id: "pending".to_string(),
        local_root: Some(local_root.to_string_lossy().to_string()),
        branch,
        last_commit,
        client_label: detect_client_label(),
        last_sync: None,
    };

    ProjectContext {
        project_slug,
        project_root: local_root.to_string_lossy().to_string(),
        fingerprint,
        checkout_binding,
        project_metadata: project_metadata::build_project_metadata(&local_root).ok(),
    }
}

pub fn discover_repository_context(current_directory: &Path) -> ProjectContext {
    discover_project_context(current_directory)
}

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

fn parse_host_path(value: &str) -> Option<(String, String, String)> {
    let (host, path) = value.split_once('/')?;
    parse_path_segments(host, path)
}

fn parse_path_segments(host: &str, path: &str) -> Option<(String, String, String)> {
    let mut segments = path.split('/').filter(|segment| !segment.is_empty());
    let owner = segments.next()?.to_string();
    let repo_name = segments.next()?.to_string();
    Some((host.to_string(), owner, repo_name))
}

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

fn detect_client_label() -> Option<String> {
    std::env::var("COMPUTERNAME")
        .ok()
        .filter(|value| !value.trim().is_empty())
        .or_else(|| std::env::var("HOSTNAME").ok().filter(|value| !value.trim().is_empty()))
}

fn fallback_project_slug(local_root: &Path) -> Option<String> {
    let raw = local_root.file_name()?.to_string_lossy().trim().to_string();
    if raw.is_empty() {
        return None;
    }

    let sanitized = raw
        .chars()
        .map(|ch| if ch.is_ascii_alphanumeric() { ch.to_ascii_lowercase() } else { '-' })
        .collect::<String>()
        .trim_matches('-')
        .to_string();

    if sanitized.is_empty() {
        return None;
    }

    if sanitized.len() <= 4 {
        return Some(format!("project-{sanitized}"));
    }

    Some(sanitized)
}

#[cfg(test)]
mod tests {
    use super::{fallback_project_slug, parse_remote_url};
    use std::path::Path;

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

    #[test]
    fn fallback_project_slug_avoids_tiny_ambiguous_names() {
        assert_eq!(
            fallback_project_slug(Path::new("/home/mark")),
            Some("project-mark".to_string())
        );
        assert_eq!(
            fallback_project_slug(Path::new("/repo/nebu-ctx")),
            Some("nebu-ctx".to_string())
        );
    }
}
