use std::path::PathBuf;

use serde::{Deserialize, Serialize};

const MARKER: &str = "# nebu-ctx — Context Engineering Layer";
const LEGACY_MARKER: &str = "# lean-ctx — Context Engineering Layer";
const END_MARKER: &str = "<!-- /lean-ctx -->";
const RULES_VERSION: &str = "nebu-ctx-rules-v12";
const LEGACY_RULES_VERSION: &str = "lean-ctx-rules-v9";

pub const RULES_MARKER: &str = MARKER;
pub const RULES_VERSION_STR: &str = RULES_VERSION;

pub fn rules_dedicated_markdown() -> &'static str {
    RULES_DEDICATED
}

// ---------------------------------------------------------------------------
// Rules content for SHARED config files (appended to user's existing config).
// LITM-optimized: critical instruction at START and END of block.
// Used for: CLAUDE.md, instructions.md, GEMINI.md, copilot-instructions.md
// ---------------------------------------------------------------------------
const RULES_SHARED: &str = r#"# nebu-ctx — Context Engineering Layer
<!-- nebu-ctx-rules-v12 -->

CRITICAL: ALWAYS use the public nebu-ctx MCP surface instead of native equivalents. This is NOT optional.

| ALWAYS USE | NEVER USE | Why |
|------------|-----------|-----|
| `ctx_read(target="file"|"files"|"symbol"|"outline"|"archive", ...)` | `Read` / `cat` / `head` / `tail` | Public cached read API for files, symbols, outlines, and archives |
| `ctx_shell(command, shell?)` | `Shell` / `bash` / terminal | Pattern compression plus active shell visibility |
| `ctx_search(mode="regex"|"semantic", ...)` | `Grep` / `rg` | Compact regex and semantic search results |
| `ctx_tree(path, depth)` | `ls` / `find` | Compact directory maps |
| `ctx(domain="memory"|"context"|"graph"|"analytics"|"agents"|"inspect", action="...")` | private `ctx_*` implementation details | Stable public gateway for advanced operations |

Public `ctx_read` targets: `file`, `files`, `symbol`, `outline`, `archive`.
Public `ctx_search` modes: `regex`, `semantic`.
Public `ctx` domains: `memory`, `context`, `graph`, `analytics`, `agents`, `inspect`.
`ctx_shell` uses active shell semantics; output includes `[shell: ...]`. Use `shell="pwsh.exe"`, `shell="cmd.exe"`, or `shell="/bin/bash"` to force a shell per call.

File mutation stays on native Edit/Write/Delete tools.

Use private tool names only when documenting nebu-ctx internals. User-facing guidance should recommend only the 5 public tools.

If a public nebu-ctx tool (`ctx_read`, `ctx_search`, `ctx_tree`, `ctx_shell`, `ctx(...)`) fails reproducibly, retry once if the failure may be environmental. If it still fails, create a GitHub issue in `MarkBovee/nebu-ctx` with repro steps, expected vs actual behavior, shell/platform, and the failing tool call. Prefer `gh issue create --repo MarkBovee/nebu-ctx ...`; fall back to `nebu-ctx report-issue --title ... --description ...` if needed.

fallback only if a nebu-ctx tool is unavailable: use native equivalents.
<!-- /lean-ctx -->"#;

// ---------------------------------------------------------------------------
// Rules content for DEDICATED lean-ctx rule files (we control entire file).
// LITM-optimized with critical mapping at start and end.
// Used for: Windsurf, Zed, Cline, Roo Code, OpenCode, Continue, Aider
// ---------------------------------------------------------------------------
const RULES_DEDICATED: &str = r#"# nebu-ctx — Context Engineering Layer
<!-- nebu-ctx-rules-v12 -->

CRITICAL: ALWAYS use the public nebu-ctx MCP surface instead of native equivalents. This is NOT optional.

## Tool replacement:
| ALWAYS USE | NEVER USE | Why |
|------------|-----------|-----|
| `ctx_read(target="file"|"files"|"symbol"|"outline"|"archive", ...)` | `Read` / `cat` / `head` / `tail` | Public cached read API for files, symbols, outlines, and archives |
| `ctx_shell(command, shell?)` | `Shell` / `bash` / terminal | Pattern compression plus active shell visibility |
| `ctx_search(mode="regex"|"semantic", ...)` | `Grep` / `rg` | Compact regex and semantic search results |
| `ctx_tree(path, depth)` | `ls` / `find` | Compact directory maps |
| `ctx(domain="memory"|"context"|"graph"|"analytics"|"agents"|"inspect", action="...")` | private `ctx_*` implementation details | Stable public gateway for advanced operations |

Public `ctx_read` targets: `file`, `files`, `symbol`, `outline`, `archive`.
Public `ctx_search` modes: `regex`, `semantic`.
Public `ctx` domains: `memory`, `context`, `graph`, `analytics`, `agents`, `inspect`.
`ctx_shell` uses active shell semantics; output includes `[shell: ...]`. Use `shell="pwsh.exe"`, `shell="cmd.exe"`, or `shell="/bin/bash"` to force a shell per call.

Use private tool names only when documenting nebu-ctx internals. User-facing guidance should recommend only the 5 public tools.

If a public nebu-ctx tool (`ctx_read`, `ctx_search`, `ctx_tree`, `ctx_shell`, `ctx(...)`) fails reproducibly, retry once if the failure may be environmental. If it still fails, create a GitHub issue in `MarkBovee/nebu-ctx` with repro steps, expected vs actual behavior, shell/platform, and the failing tool call. Prefer `gh issue create --repo MarkBovee/nebu-ctx ...`; fall back to `nebu-ctx report-issue --title ... --description ...` if needed.

## File editing:
Use native Edit/StrReplace/Write/Delete tools for mutations.

## Proactive (use without being asked):
- `ctx(domain="context", action="overview", task="...")` at session start
- `ctx(domain="context", action="compress")` when context grows large
- `ctx(domain="memory", action="save"|"recall"|"store"|"consolidate")` when useful facts or session state should persist

fallback only if a nebu-ctx tool is unavailable: use native equivalents.
Project bootstrap: use `nebu-ctx project-bootstrap preview` when user asks to map/bootstrap project knowledge; only `apply` after review.
<!-- /lean-ctx -->"#;

// ---------------------------------------------------------------------------
// Rules for Cursor MDC format (dedicated file with frontmatter).
// alwaysApply: true ensures this is loaded into EVERY conversation.
// LITM-optimized: critical warning at top and bottom.
// ---------------------------------------------------------------------------
const RULES_CURSOR_MDC: &str = r#"---
description: "nebu-ctx: ALWAYS use the public 5-tool MCP surface instead of native or private tool names"
alwaysApply: true
---

# nebu-ctx — Context Engineering Layer
<!-- nebu-ctx-rules-v12 -->

CRITICAL: ALWAYS use the public nebu-ctx MCP surface instead of native equivalents. This is NOT optional.

## Tool Mapping

| ALWAYS USE | NEVER USE | Why |
|------------|-----------|-----|
| `ctx_read(target="file"|"files"|"symbol"|"outline"|"archive", ...)` | `Read` | Public cached read API for files, symbols, outlines, and archives |
| `ctx_shell` | `Shell` | Pattern-based compression plus active shell visibility |
| `ctx_search(mode="regex"|"semantic", ...)` | `Grep` | Compact regex and semantic search results |
| `ctx_tree` | `ls`, `find` | Compact directory maps with file counts |
| `ctx(domain="memory"|"context"|"graph"|"analytics"|"agents"|"inspect", action="...")` | private `ctx_*` implementation details | Stable public gateway for advanced operations |

## Public Contract

- `ctx_read` targets: `file`, `files`, `symbol`, `outline`, `archive`
- `ctx_search` modes: `regex`, `semantic`
- `ctx` domains: `memory`, `context`, `graph`, `analytics`, `agents`, `inspect`
- `ctx_shell` uses active shell semantics; output includes `[shell: ...]`. Use `shell` to force a specific executable per call.

## Memory

- Use `ctx(domain="memory", action="save"|"recall")` to carry forward task state and working memory.
- Use `ctx(domain="memory", action="store"|"recall"|"wakeup"|"consolidate")` for durable facts that should survive future sessions.
- For repo mapping/bootstrap, preview candidate facts first with `nebu-ctx project-bootstrap preview`; do not silently persist on first scan.
- Stop/compact hooks already consolidate the current session into the nebu-ctx server; keep new facts there instead of relying on chat history.

## File editing

- Use native Edit/StrReplace/Write/Delete tools for mutations.
- Use private tool names only when documenting nebu-ctx internals.
- If a public nebu-ctx tool fails reproducibly, retry once if it may be environmental. If still broken, do not bypass to the native equivalent. Use supported raw mode or the repo-built nebu-ctx client, then create a GitHub issue in `MarkBovee/nebu-ctx` with repro, expected vs actual, shell/platform, and the failing tool call. Prefer `gh issue create --repo MarkBovee/nebu-ctx ...`; fall back to `nebu-ctx report-issue` if needed.
- Use native equivalents only when no public nebu-ctx path exists at all, not when the nebu-ctx path is buggy or inconvenient.
<!-- /lean-ctx -->"#;

// ---------------------------------------------------------------------------

struct RulesTarget {
    name: &'static str,
    path: PathBuf,
    format: RulesFormat,
}

enum RulesFormat {
    SharedMarkdown,
    DedicatedMarkdown,
    CursorMdc,
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

fn rules_content(format: &RulesFormat) -> &'static str {
    match format {
        RulesFormat::SharedMarkdown => RULES_SHARED,
        RulesFormat::DedicatedMarkdown => RULES_DEDICATED,
        RulesFormat::CursorMdc => RULES_CURSOR_MDC,
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
                RulesFormat::DedicatedMarkdown | RulesFormat::CursorMdc => {
                    write_dedicated(&target.path, rules_content(&target.format))
                }
            };
        }
    }

    ensure_parent(&target.path)?;

    match target.format {
        RulesFormat::SharedMarkdown => append_to_shared(&target.path),
        RulesFormat::DedicatedMarkdown | RulesFormat::CursorMdc => {
            write_dedicated(&target.path, rules_content(&target.format))
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
    content.push_str(RULES_SHARED);
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
            result.push_str(RULES_SHARED);
            if !after.is_empty() {
                result.push('\n');
                result.push_str(after);
            }
            result
        }
        (Some(s), None) => {
            let before = &content[..s];
            let mut result = before.to_string();
            result.push_str(RULES_SHARED);
            result.push('\n');
            result
        }
        _ => return Ok(RulesResult::AlreadyPresent),
    };

    std::fs::write(path, new_content).map_err(|e| e.to_string())?;
    Ok(RulesResult::Updated)
}

fn write_dedicated(path: &std::path::Path, content: &'static str) -> Result<RulesResult, String> {
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
        "Cursor" => home.join(".cursor").exists(),
        "Windsurf" => home.join(".codeium/windsurf").exists(),
        "Gemini CLI" => home.join(".gemini").exists(),
        "VS Code / Copilot" => detect_vscode_installed(home),
        "Copilot CLI" => home.join(".copilot").exists(),
        "Zed" => home.join(".config/zed").exists(),
        "Cline" => detect_extension_installed(home, "saoudrizwan.claude-dev"),
        "Roo Code" => detect_extension_installed(home, "rooveterinaryinc.roo-cline"),
        "OpenCode" => home.join(".config/opencode").exists(),
        "Continue" => detect_extension_installed(home, "continue.continue"),
        "Aider" => command_exists("aider") || home.join(".aider.conf.yml").exists(),
        "Amp" => command_exists("amp") || home.join(".ampcoder").exists(),
        "Qwen Code" => home.join(".qwen").exists(),
        "Trae" => home.join(".trae").exists(),
        "Amazon Q Developer" => home.join(".aws/amazonq").exists(),
        "JetBrains IDEs" => detect_jetbrains_installed(home),
        "Antigravity" => home.join(".gemini/antigravity").exists(),
        "Pi Coding Agent" => home.join(".pi").exists() || command_exists("pi"),
        "AWS Kiro" => home.join(".kiro").exists(),
        "Crush" => home.join(".config/crush").exists() || command_exists("crush"),
        "Verdent" => home.join(".verdent").exists(),
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

fn detect_jetbrains_installed(home: &std::path::Path) -> bool {
    #[cfg(target_os = "macos")]
    if home.join("Library/Application Support/JetBrains").exists() {
        return true;
    }
    #[cfg(target_os = "linux")]
    if home.join(".config/JetBrains").exists() {
        return true;
    }
    home.join(".jb-mcp.json").exists()
}

fn detect_extension_installed(_home: &std::path::Path, extension_id: &str) -> bool {
    #[cfg(target_os = "macos")]
    {
        if _home
            .join(format!(
                "Library/Application Support/Code/User/globalStorage/{extension_id}"
            ))
            .exists()
        {
            return true;
        }
    }
    #[cfg(target_os = "linux")]
    {
        if _home
            .join(format!(".config/Code/User/globalStorage/{extension_id}"))
            .exists()
        {
            return true;
        }
    }
    #[cfg(target_os = "windows")]
    {
        if let Ok(appdata) = std::env::var("APPDATA") {
            if std::path::PathBuf::from(&appdata)
                .join(format!("Code/User/globalStorage/{extension_id}"))
                .exists()
            {
                return true;
            }
        }
    }
    false
}

// ---------------------------------------------------------------------------
// Target definitions
// ---------------------------------------------------------------------------

fn build_rules_targets(home: &std::path::Path) -> Vec<RulesTarget> {
    vec![
        // --- Shared config files (append-only) ---
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
            name: "Gemini CLI",
            path: home.join(".gemini/GEMINI.md"),
            format: RulesFormat::SharedMarkdown,
        },
        RulesTarget {
            name: "VS Code / Copilot",
            path: copilot_instructions_path(home),
            format: RulesFormat::SharedMarkdown,
        },
        RulesTarget {
            name: "Copilot CLI",
            path: home.join(".copilot/copilot-instructions.md"),
            format: RulesFormat::SharedMarkdown,
        },
        // --- Dedicated lean-ctx rule files ---
        RulesTarget {
            name: "Cursor",
            path: home.join(".cursor/rules/nebu-ctx.mdc"),
            format: RulesFormat::CursorMdc,
        },
        RulesTarget {
            name: "Windsurf",
            path: home.join(".codeium/windsurf/rules/nebu-ctx.md"),
            format: RulesFormat::DedicatedMarkdown,
        },
        RulesTarget {
            name: "Zed",
            path: home.join(".config/zed/rules/nebu-ctx.md"),
            format: RulesFormat::DedicatedMarkdown,
        },
        RulesTarget {
            name: "Cline",
            path: home.join(".cline/rules/nebu-ctx.md"),
            format: RulesFormat::DedicatedMarkdown,
        },
        RulesTarget {
            name: "Roo Code",
            path: home.join(".roo/rules/nebu-ctx.md"),
            format: RulesFormat::DedicatedMarkdown,
        },
        RulesTarget {
            name: "OpenCode",
            path: home.join(".config/opencode/rules/nebu-ctx.md"),
            format: RulesFormat::DedicatedMarkdown,
        },
        RulesTarget {
            name: "Continue",
            path: home.join(".continue/rules/nebu-ctx.md"),
            format: RulesFormat::DedicatedMarkdown,
        },
        RulesTarget {
            name: "Aider",
            path: home.join(".aider/rules/nebu-ctx.md"),
            format: RulesFormat::DedicatedMarkdown,
        },
        RulesTarget {
            name: "Amp",
            path: home.join(".ampcoder/rules/nebu-ctx.md"),
            format: RulesFormat::DedicatedMarkdown,
        },
        RulesTarget {
            name: "Qwen Code",
            path: home.join(".qwen/rules/nebu-ctx.md"),
            format: RulesFormat::DedicatedMarkdown,
        },
        RulesTarget {
            name: "Trae",
            path: home.join(".trae/rules/nebu-ctx.md"),
            format: RulesFormat::DedicatedMarkdown,
        },
        RulesTarget {
            name: "Amazon Q Developer",
            path: home.join(".aws/amazonq/rules/nebu-ctx.md"),
            format: RulesFormat::DedicatedMarkdown,
        },
        RulesTarget {
            name: "JetBrains IDEs",
            path: home.join(".jb-rules/nebu-ctx.md"),
            format: RulesFormat::DedicatedMarkdown,
        },
        RulesTarget {
            name: "Antigravity",
            path: home.join(".gemini/antigravity/rules/nebu-ctx.md"),
            format: RulesFormat::DedicatedMarkdown,
        },
        RulesTarget {
            name: "Pi Coding Agent",
            path: home.join(".pi/rules/nebu-ctx.md"),
            format: RulesFormat::DedicatedMarkdown,
        },
        RulesTarget {
            name: "AWS Kiro",
            path: home.join(".kiro/steering/nebu-ctx.md"),
            format: RulesFormat::DedicatedMarkdown,
        },
        RulesTarget {
            name: "Verdent",
            path: home.join(".verdent/rules/nebu-ctx.md"),
            format: RulesFormat::DedicatedMarkdown,
        },
        RulesTarget {
            name: "Crush",
            path: home.join(".config/crush/rules/nebu-ctx.md"),
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
        assert!(RULES_SHARED.contains(MARKER));
        assert!(RULES_SHARED.contains(END_MARKER));
        assert!(RULES_SHARED.contains(RULES_VERSION));
    }

    #[test]
    fn dedicated_rules_have_markers() {
        assert!(RULES_DEDICATED.contains(MARKER));
        assert!(RULES_DEDICATED.contains(END_MARKER));
        assert!(RULES_DEDICATED.contains(RULES_VERSION));
    }

    #[test]
    fn cursor_mdc_has_markers_and_frontmatter() {
        assert!(RULES_CURSOR_MDC.contains(MARKER));
        assert!(RULES_CURSOR_MDC.contains(END_MARKER));
        assert!(RULES_CURSOR_MDC.contains(RULES_VERSION));
        assert!(RULES_CURSOR_MDC.contains("alwaysApply: true"));
    }

    #[test]
    fn shared_rules_contain_tool_mapping() {
        assert!(RULES_SHARED.contains("ctx_read"));
        assert!(RULES_SHARED.contains("ctx_shell"));
        assert!(RULES_SHARED.contains("ctx_search"));
        assert!(RULES_SHARED.contains("ctx_tree"));
        assert!(RULES_SHARED.contains("ctx(domain=\"memory\""));
    }

    #[test]
    fn shared_rules_litm_optimized() {
        let lines: Vec<&str> = RULES_SHARED.lines().collect();
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
        assert!(RULES_DEDICATED.contains("public nebu-ctx MCP surface"));
        assert!(RULES_DEDICATED.contains(
            "ctx_read(target=\"file\"|\"files\"|\"symbol\"|\"outline\"|\"archive\", ...)"
        ));
        assert!(RULES_DEDICATED.contains(
            "Public `ctx_read` targets: `file`, `files`, `symbol`, `outline`, `archive`."
        ));
        assert!(RULES_DEDICATED.contains("Public `ctx_search` modes: `regex`, `semantic`."));
        assert!(RULES_DEDICATED.contains("ctx(domain=\"memory\""));
        assert!(RULES_DEDICATED.contains("only the 5 public tools"));
    }

    #[test]
    fn dedicated_rules_litm_optimized() {
        let lines: Vec<&str> = RULES_DEDICATED.lines().collect();
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
    fn cursor_mdc_litm_optimized() {
        let lines: Vec<&str> = RULES_CURSOR_MDC.lines().collect();
        let first_10 = lines[..10.min(lines.len())].join("\n");
        assert!(
            first_10.contains("ALWAYS") || first_10.contains("lean-ctx"),
            "LITM: preference instruction must be near start of MDC"
        );
        let last_5 = lines[lines.len().saturating_sub(5)..].join("\n");
        assert!(
            last_5.contains("fallback") || last_5.contains("native"),
            "LITM: fallback note must be near end of MDC"
        );
    }

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

        let result = write_dedicated(&path, RULES_DEDICATED).unwrap();
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

        let result = write_dedicated(&path, RULES_DEDICATED).unwrap();
        assert!(matches!(result, RulesResult::Updated));

        std::fs::remove_file(&path).ok();
    }

    #[test]
    fn target_count() {
        let home = std::path::PathBuf::from("/tmp/fake_home");
        let targets = build_rules_targets(&home);
        assert_eq!(targets.len(), 22);
    }
}
