use std::path::PathBuf;

use serde::{Deserialize, Serialize};

const MARKER: &str = crate::public_guidance::RULES_MARKER;
const LEGACY_MARKER: &str = crate::public_guidance::LEGACY_RULES_MARKER;
const END_MARKER: &str = crate::public_guidance::RULES_END_MARKER;
const RULES_VERSION: &str = crate::public_guidance::RULES_VERSION;
const LEGACY_RULES_VERSION: &str = "lean-ctx-rules-v9";

pub const RULES_MARKER: &str = MARKER;
pub const RULES_VERSION_STR: &str = RULES_VERSION;

pub fn rules_dedicated_markdown() -> String {
    crate::public_guidance::rules_dedicated_markdown()
}

// ---------------------------------------------------------------------------

struct RulesTarget {
    name: &'static str,
    path: PathBuf,
    format: RulesFormat,
}

enum RulesFormat {
    SharedMarkdown,
    DedicatedMarkdown,
}

pub struct InjectResult {
    pub injected: Vec<String>,
    pub updated: Vec<String>,
    pub already: Vec<String>,
    pub errors: Vec<String>,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct RulesTargetStatus {
    pub name: String,
    pub detected: bool,
    pub path: String,
    pub state: String,
    pub note: Option<String>,
}

pub fn inject_all_rules(home: &std::path::Path) -> InjectResult {
    if crate::core::config::Config::load().rules_scope_effective()
        == crate::core::config::RulesScope::Project
    {
        return InjectResult {
            injected: Vec::new(),
            updated: Vec::new(),
            already: Vec::new(),
            errors: Vec::new(),
        };
    }

    let targets = build_rules_targets(home);

    let mut result = InjectResult {
        injected: Vec::new(),
        updated: Vec::new(),
        already: Vec::new(),
        errors: Vec::new(),
    };

    for target in &targets {
        if !is_tool_detected(target, home) {
            continue;
        }

        match inject_rules(target) {
            Ok(RulesResult::Injected) => result.injected.push(target.name.to_string()),
            Ok(RulesResult::Updated) => result.updated.push(target.name.to_string()),
            Ok(RulesResult::AlreadyPresent) => result.already.push(target.name.to_string()),
            Err(e) => result.errors.push(format!("{}: {e}", target.name)),
        }
    }

    result
}

pub fn collect_rules_status(home: &std::path::Path) -> Vec<RulesTargetStatus> {
    let targets = build_rules_targets(home);
    let mut out = Vec::new();

    for target in &targets {
        let detected = is_tool_detected(target, home);
        let path = target.path.to_string_lossy().to_string();

        let state = if !detected {
            "not_detected".to_string()
        } else if !target.path.exists() {
            "missing".to_string()
        } else {
            match std::fs::read_to_string(&target.path) {
                Ok(content) => {
                    if content.contains(MARKER) || content.contains(LEGACY_MARKER) {
                        if content.contains(RULES_VERSION) || content.contains(LEGACY_RULES_VERSION)
                        {
                            "up_to_date".to_string()
                        } else {
                            "outdated".to_string()
                        }
                    } else {
                        "present_without_marker".to_string()
                    }
                }
                Err(_) => "read_error".to_string(),
            }
        };

        out.push(RulesTargetStatus {
            name: target.name.to_string(),
            detected,
            path,
            state,
            note: None,
        });
    }

    out
}

// ---------------------------------------------------------------------------
// Injection logic
// ---------------------------------------------------------------------------

enum RulesResult {
    Injected,
    Updated,
    AlreadyPresent,
}

fn rules_content(format: &RulesFormat) -> String {
    match format {
        RulesFormat::SharedMarkdown => crate::public_guidance::rules_shared_markdown(),
        RulesFormat::DedicatedMarkdown => crate::public_guidance::rules_dedicated_markdown(),
    }
}

fn inject_rules(target: &RulesTarget) -> Result<RulesResult, String> {
    if target.path.exists() {
        let content = std::fs::read_to_string(&target.path).map_err(|e| e.to_string())?;
        if content.contains(MARKER) || content.contains(LEGACY_MARKER) {
            if content.contains(RULES_VERSION) || content.contains(LEGACY_RULES_VERSION) {
                return Ok(RulesResult::AlreadyPresent);
            }
            ensure_parent(&target.path)?;
            return match target.format {
                RulesFormat::SharedMarkdown => replace_markdown_section(&target.path, &content),
                RulesFormat::DedicatedMarkdown => {
                    write_dedicated(&target.path, &rules_content(&target.format))
                }
            };
        }
    }

    ensure_parent(&target.path)?;

    match target.format {
        RulesFormat::SharedMarkdown => append_to_shared(&target.path),
        RulesFormat::DedicatedMarkdown => {
            write_dedicated(&target.path, &rules_content(&target.format))
        }
    }
}

fn ensure_parent(path: &std::path::Path) -> Result<(), String> {
    if let Some(parent) = path.parent() {
        std::fs::create_dir_all(parent).map_err(|e| e.to_string())?;
    }
    Ok(())
}

fn append_to_shared(path: &std::path::Path) -> Result<RulesResult, String> {
    let mut content = if path.exists() {
        std::fs::read_to_string(path).map_err(|e| e.to_string())?
    } else {
        String::new()
    };

    if !content.is_empty() && !content.ends_with('\n') {
        content.push('\n');
    }
    if !content.is_empty() {
        content.push('\n');
    }
    content.push_str(&crate::public_guidance::rules_shared_markdown());
    content.push('\n');

    std::fs::write(path, content).map_err(|e| e.to_string())?;
    Ok(RulesResult::Injected)
}

fn replace_markdown_section(path: &std::path::Path, content: &str) -> Result<RulesResult, String> {
    let start = content.find(MARKER).or_else(|| content.find(LEGACY_MARKER));
    let end = content.find(END_MARKER);

    let new_content = match (start, end) {
        (Some(s), Some(e)) => {
            let before = &content[..s];
            let after_end = e + END_MARKER.len();
            let after = content[after_end..].trim_start_matches('\n');
            let mut result = before.to_string();
            result.push_str(&crate::public_guidance::rules_shared_markdown());
            if !after.is_empty() {
                result.push('\n');
                result.push_str(after);
            }
            result
        }
        (Some(s), None) => {
            let before = &content[..s];
            let mut result = before.to_string();
            result.push_str(&crate::public_guidance::rules_shared_markdown());
            result.push('\n');
            result
        }
        _ => return Ok(RulesResult::AlreadyPresent),
    };

    std::fs::write(path, new_content).map_err(|e| e.to_string())?;
    Ok(RulesResult::Updated)
}

fn write_dedicated(path: &std::path::Path, content: &str) -> Result<RulesResult, String> {
    let is_update = path.exists() && {
        let existing = std::fs::read_to_string(path).unwrap_or_default();
        existing.contains(MARKER) || existing.contains(LEGACY_MARKER)
    };

    std::fs::write(path, content).map_err(|e| e.to_string())?;

    if is_update {
        Ok(RulesResult::Updated)
    } else {
        Ok(RulesResult::Injected)
    }
}

// ---------------------------------------------------------------------------
// Tool detection
// ---------------------------------------------------------------------------

fn is_tool_detected(target: &RulesTarget, home: &std::path::Path) -> bool {
    match target.name {
        "Claude Code" => {
            if command_exists("claude") {
                return true;
            }
            let state_dir = crate::core::editor_registry::claude_state_dir(home);
            crate::core::editor_registry::claude_mcp_json_path(home).exists() || state_dir.exists()
        }
        "Codex CLI" => home.join(".codex").exists() || command_exists("codex"),
        "VS Code / Copilot" => detect_vscode_installed(home),
        "OpenCode" => home.join(".config/opencode").exists(),
        _ => false,
    }
}

fn command_exists(name: &str) -> bool {
    #[cfg(target_os = "windows")]
    let result = std::process::Command::new("where")
        .arg(name)
        .output()
        .map(|o| o.status.success())
        .unwrap_or(false);

    #[cfg(not(target_os = "windows"))]
    let result = std::process::Command::new("which")
        .arg(name)
        .output()
        .map(|o| o.status.success())
        .unwrap_or(false);

    result
}

fn detect_vscode_installed(_home: &std::path::Path) -> bool {
    let check_dir = |dir: PathBuf| -> bool {
        dir.join("settings.json").exists() || dir.join("mcp.json").exists()
    };

    #[cfg(target_os = "macos")]
    if check_dir(_home.join("Library/Application Support/Code/User")) {
        return true;
    }
    #[cfg(target_os = "linux")]
    if check_dir(_home.join(".config/Code/User")) {
        return true;
    }
    #[cfg(target_os = "windows")]
    if let Ok(appdata) = std::env::var("APPDATA") {
        if check_dir(PathBuf::from(&appdata).join("Code/User")) {
            return true;
        }
    }
    false
}

// ---------------------------------------------------------------------------
// Target definitions
// ---------------------------------------------------------------------------

fn build_rules_targets(home: &std::path::Path) -> Vec<RulesTarget> {
    vec![
        RulesTarget {
            name: "Claude Code",
            path: crate::core::editor_registry::claude_rules_dir(home).join("nebu-ctx.md"),
            format: RulesFormat::DedicatedMarkdown,
        },
        RulesTarget {
            name: "Codex CLI",
            path: home.join(".codex/instructions.md"),
            format: RulesFormat::SharedMarkdown,
        },
        RulesTarget {
            name: "VS Code / Copilot",
            path: copilot_instructions_path(home),
            format: RulesFormat::SharedMarkdown,
        },
        RulesTarget {
            name: "OpenCode",
            path: home.join(".config/opencode/rules/nebu-ctx.md"),
            format: RulesFormat::DedicatedMarkdown,
        },
    ]
}

fn copilot_instructions_path(home: &std::path::Path) -> PathBuf {
    #[cfg(target_os = "macos")]
    {
        return home.join("Library/Application Support/Code/User/github-copilot-instructions.md");
    }
    #[cfg(target_os = "linux")]
    {
        return home.join(".config/Code/User/github-copilot-instructions.md");
    }
    #[cfg(target_os = "windows")]
    {
        if let Ok(appdata) = std::env::var("APPDATA") {
            return PathBuf::from(appdata).join("Code/User/github-copilot-instructions.md");
        }
    }
    #[allow(unreachable_code)]
    home.join(".config/Code/User/github-copilot-instructions.md")
}

// ---------------------------------------------------------------------------
// Tests
// ---------------------------------------------------------------------------

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn shared_rules_have_markers() {
        let content = crate::public_guidance::rules_shared_markdown();
        assert!(content.contains(MARKER));
        assert!(content.contains(END_MARKER));
        assert!(content.contains(RULES_VERSION));
    }

    #[test]
    fn dedicated_rules_have_markers() {
        let content = crate::public_guidance::rules_dedicated_markdown();
        assert!(content.contains(MARKER));
        assert!(content.contains(END_MARKER));
        assert!(content.contains(RULES_VERSION));
    }

    #[test]
    fn shared_rules_contain_tool_mapping() {
        let content = crate::public_guidance::rules_shared_markdown();
        assert!(content.contains("ctx_read"));
        assert!(content.contains("ctx_shell"));
        assert!(content.contains("ctx_search"));
        assert!(content.contains("ctx_tree"));
        assert!(content.contains("ctx(domain=\"memory\""));
    }

    #[test]
    fn shared_rules_litm_optimized() {
        let content = crate::public_guidance::rules_shared_markdown();
        let lines: Vec<&str> = content.lines().collect();
        let first_5 = lines[..5.min(lines.len())].join("\n");
        assert!(
            first_5.contains("ALWAYS")
                || first_5.contains("nebu-ctx")
                || first_5.contains("lean-ctx"),
            "LITM: preference instruction must be near start"
        );
        let last_5 = lines[lines.len().saturating_sub(5)..].join("\n");
        assert!(
            last_5.contains("fallback") || last_5.contains("native"),
            "LITM: fallback note must be near end"
        );
    }

    #[test]
    fn dedicated_rules_contain_public_contract() {
        let content = crate::public_guidance::rules_dedicated_markdown();
        assert!(content.contains("public nebu-ctx MCP surface"));
        assert!(content.contains(
            "ctx_read(target=\"file\"|\"files\"|\"symbol\"|\"outline\"|\"archive\", ...)"
        ));
        assert!(content.contains(
            "Public `ctx_read` targets: `file`, `files`, `symbol`, `outline`, `archive`."
        ));
        assert!(content.contains("Public `ctx_search` modes: `regex`, `semantic`."));
        assert!(content.contains("ctx(domain=\"memory\""));
        assert!(content.contains("only the 5 public tools"));
    }

    #[test]
    fn dedicated_rules_litm_optimized() {
        let content = crate::public_guidance::rules_dedicated_markdown();
        let lines: Vec<&str> = content.lines().collect();
        let first_5 = lines[..5.min(lines.len())].join("\n");
        assert!(
            first_5.contains("ALWAYS")
                || first_5.contains("nebu-ctx")
                || first_5.contains("lean-ctx"),
            "LITM: preference instruction must be near start"
        );
        let last_5 = lines[lines.len().saturating_sub(5)..].join("\n");
        assert!(
            last_5.contains("fallback")
                || last_5.contains("ctx(domain=\"context\", action=\"compress\")"),
            "LITM: practical note must be near end"
        );
    }

    #[test]
    fn ensure_temp_dir() {
        let tmp = std::env::temp_dir();
        if !tmp.exists() {
            std::fs::create_dir_all(&tmp).ok();
        }
    }

    #[test]
    fn replace_section_with_end_marker() {
        ensure_temp_dir();
        let old = "user stuff\n\n# lean-ctx — Context Engineering Layer\n<!-- lean-ctx-rules-v2 -->\nold rules\n<!-- /lean-ctx -->\nmore user stuff\n";
        let path = std::env::temp_dir().join("test_replace_with_end.md");
        std::fs::write(&path, old).unwrap();

        let result = replace_markdown_section(&path, old).unwrap();
        assert!(matches!(result, RulesResult::Updated));

        let new_content = std::fs::read_to_string(&path).unwrap();
        assert!(new_content.contains(RULES_VERSION));
        assert!(new_content.starts_with("user stuff"));
        assert!(new_content.contains("more user stuff"));
        assert!(!new_content.contains("lean-ctx-rules-v2"));

        std::fs::remove_file(&path).ok();
    }

    #[test]
    fn replace_section_without_end_marker() {
        ensure_temp_dir();
        let old = "user stuff\n\n# lean-ctx — Context Engineering Layer\nold rules only\n";
        let path = std::env::temp_dir().join("test_replace_no_end.md");
        std::fs::write(&path, old).unwrap();

        let result = replace_markdown_section(&path, old).unwrap();
        assert!(matches!(result, RulesResult::Updated));

        let new_content = std::fs::read_to_string(&path).unwrap();
        assert!(new_content.contains(RULES_VERSION));
        assert!(new_content.starts_with("user stuff"));

        std::fs::remove_file(&path).ok();
    }

    #[test]
    fn append_to_shared_preserves_existing() {
        ensure_temp_dir();
        let path = std::env::temp_dir().join("test_append_shared.md");
        std::fs::write(&path, "existing user rules\n").unwrap();

        let result = append_to_shared(&path).unwrap();
        assert!(matches!(result, RulesResult::Injected));

        let content = std::fs::read_to_string(&path).unwrap();
        assert!(content.starts_with("existing user rules"));
        assert!(content.contains(MARKER));
        assert!(content.contains(END_MARKER));

        std::fs::remove_file(&path).ok();
    }

    #[test]
    fn write_dedicated_creates_file() {
        ensure_temp_dir();
        let path = std::env::temp_dir().join("test_write_dedicated.md");
        if path.exists() {
            std::fs::remove_file(&path).ok();
        }

        let result =
            write_dedicated(&path, &crate::public_guidance::rules_dedicated_markdown()).unwrap();
        assert!(matches!(result, RulesResult::Injected));

        let content = std::fs::read_to_string(&path).unwrap();
        assert!(content.contains(MARKER));
        assert!(content.contains("Public `ctx_read` targets"));
        assert!(content.contains("Public `ctx_search` modes"));

        std::fs::remove_file(&path).ok();
    }

    #[test]
    fn write_dedicated_updates_existing() {
        ensure_temp_dir();
        let path = std::env::temp_dir().join("test_write_dedicated_update.md");
        std::fs::write(&path, "# lean-ctx — Context Engineering Layer\nold version").unwrap();

        let result =
            write_dedicated(&path, &crate::public_guidance::rules_dedicated_markdown()).unwrap();
        assert!(matches!(result, RulesResult::Updated));

        std::fs::remove_file(&path).ok();
    }

    #[test]
    fn target_count() {
        let home = std::path::PathBuf::from("/tmp/fake_home");
        let targets = build_rules_targets(&home);
        assert_eq!(targets.len(), 4);
    }
}
