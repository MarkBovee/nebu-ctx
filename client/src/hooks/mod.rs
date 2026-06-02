use std::path::PathBuf;

pub mod agents;
mod support;
use agents::*;
use support::{
    ensure_codex_hooks_enabled, install_codex_instruction_docs, upsert_lean_ctx_codex_hook_entries,
};

pub(crate) fn mcp_server_quiet_mode() -> bool {
    std::env::var_os("NEBU_CTX_MCP_SERVER").is_some()
        || matches!(std::env::var("NEBU_CTX_QUIET"), Ok(value) if value.trim() == "1")
}

/// Silently refresh all hook scripts for agents that are already configured.
/// Called after updates and on MCP server start to ensure hooks match the current binary version.
pub fn refresh_installed_hooks() {
    let home = match dirs::home_dir() {
        Some(h) => h,
        None => return,
    };

    let claude_dir = crate::setup::claude_config_dir(&home);
    let claude_hooks = claude_dir.join("hooks/nebu-ctx-rewrite.sh").exists()
        || claude_dir.join("settings.json").exists()
            && std::fs::read_to_string(claude_dir.join("settings.json"))
                .unwrap_or_default()
                .contains("nebu-ctx");

    if claude_hooks {
        install_claude_hook_scripts(&home);
        install_claude_hook_config(&home);
    }

    let codex_hooks = home.join(".codex/hooks/nebu-ctx-rewrite-codex.sh").exists()
        || home.join(".codex/hooks.json").exists()
            && std::fs::read_to_string(home.join(".codex/hooks.json"))
                .unwrap_or_default()
                .contains("nebu-ctx");

    if codex_hooks {
        install_codex_hook();
    }

    // Copilot CLI hooks
    let copilot_global_hooks = home.join(".github").join("hooks").join("hooks.json");
    if copilot_global_hooks.exists()
        && std::fs::read_to_string(&copilot_global_hooks)
            .unwrap_or_default()
            .contains("nebu-ctx")
    {
        install_copilot_hook(true);
    }
}

fn resolve_binary_path() -> String {
    if is_lean_ctx_in_path() {
        return "nebu-ctx".to_string();
    }
    crate::core::portable_binary::resolve_portable_binary()
}

fn is_lean_ctx_in_path() -> bool {
    let which_cmd = if cfg!(windows) { "where" } else { "which" };
    std::process::Command::new(which_cmd)
        .arg("nebu-ctx")
        .stdout(std::process::Stdio::null())
        .stderr(std::process::Stdio::null())
        .status()
        .map(|s| s.success())
        .unwrap_or(false)
}

fn resolve_binary_path_for_bash() -> String {
    let path = resolve_binary_path();
    to_bash_compatible_path(&path)
}

pub fn to_host_compatible_path(path: &str) -> String {
    crate::core::pathutil::strip_verbatim_str(path).unwrap_or_else(|| path.to_string())
}

pub fn to_bash_compatible_path(path: &str) -> String {
    let path = match crate::core::pathutil::strip_verbatim_str(path) {
        Some(stripped) => stripped,
        None => path.replace('\\', "/"),
    };
    if path.len() >= 2 && path.as_bytes()[1] == b':' {
        let drive = (path.as_bytes()[0] as char).to_ascii_lowercase();
        format!("/{drive}{}", &path[2..])
    } else {
        path
    }
}

/// Normalize paths from any client format to a consistent OS-native form.
/// Handles MSYS2/Git Bash (`/c/Users/...` -> `C:/Users/...`), mixed separators,
/// double slashes, and trailing slashes. Always uses forward slashes for consistency.
pub fn normalize_tool_path(path: &str) -> String {
    let mut p = match crate::core::pathutil::strip_verbatim_str(path) {
        Some(stripped) => stripped,
        None => path.to_string(),
    };

    // MSYS2/Git Bash: /c/Users/... -> C:/Users/...
    if p.len() >= 3
        && p.starts_with('/')
        && p.as_bytes()[1].is_ascii_alphabetic()
        && p.as_bytes()[2] == b'/'
    {
        let drive = p.as_bytes()[1].to_ascii_uppercase() as char;
        p = format!("{drive}:{}", &p[2..]);
    }

    p = p.replace('\\', "/");

    // Collapse double slashes (preserve UNC paths starting with //)
    while p.contains("//") && !p.starts_with("//") {
        p = p.replace("//", "/");
    }

    // Remove trailing slash (unless root like "/" or "C:/")
    if p.len() > 1 && p.ends_with('/') && !p.ends_with(":/") {
        p.pop();
    }

    p
}

pub fn generate_rewrite_script(binary: &str) -> String {
    let case_pattern = crate::rewrite_registry::bash_case_pattern();
    format!(
        r#"#!/usr/bin/env bash
# nebu-ctx PreToolUse hook — rewrites bash commands to nebu-ctx equivalents
set -euo pipefail

NEBU_CTX_BIN="{binary}"

INPUT=$(cat)
TOOL=$(echo "$INPUT" | grep -oE '"tool_name":"([^"\\]|\\.)*"' | head -1 | sed 's/^"tool_name":"//;s/"$//' | sed 's/\\"/"/g;s/\\\\/\\/g')

if [ "$TOOL" != "Bash" ] && [ "$TOOL" != "bash" ]; then
  exit 0
fi

CMD=$(echo "$INPUT" | grep -oE '"command":"([^"\\]|\\.)*"' | head -1 | sed 's/^"command":"//;s/"$//' | sed 's/\\"/"/g;s/\\\\/\\/g')

if [ -z "$CMD" ] || echo "$CMD" | grep -qE "^(nebu-ctx |$NEBU_CTX_BIN )"; then
  exit 0
fi

case "$CMD" in
  {case_pattern})
    # Shell-escape then JSON-escape (two passes)
    SHELL_ESC=$(printf '%s' "$CMD" | sed 's/\\/\\\\/g;s/"/\\"/g')
    REWRITE="$NEBU_CTX_BIN -c \"$SHELL_ESC\""
    JSON_CMD=$(printf '%s' "$REWRITE" | sed 's/\\/\\\\/g;s/"/\\"/g')
    printf '{{"hookSpecificOutput":{{"hookEventName":"PreToolUse","permissionDecision":"allow","updatedInput":{{"command":"%s"}}}}}}' "$JSON_CMD"
    ;;
  *) exit 0 ;;
esac
"#
    )
}

pub fn generate_compact_rewrite_script(binary: &str) -> String {
    let case_pattern = crate::rewrite_registry::bash_case_pattern();
    format!(
        r#"#!/usr/bin/env bash
# nebu-ctx hook — rewrites shell commands
set -euo pipefail
NEBU_CTX_BIN="{binary}"
INPUT=$(cat)
CMD=$(echo "$INPUT" | grep -oE '"command":"([^"\\]|\\.)*"' | head -1 | sed 's/^"command":"//;s/"$//' | sed 's/\\"/"/g;s/\\\\/\\/g' 2>/dev/null || echo "")
if [ -z "$CMD" ] || echo "$CMD" | grep -qE "^(nebu-ctx |$NEBU_CTX_BIN )"; then exit 0; fi
case "$CMD" in
  {case_pattern})
    SHELL_ESC=$(printf '%s' "$CMD" | sed 's/\\/\\\\/g;s/"/\\"/g')
    REWRITE="$NEBU_CTX_BIN -c \"$SHELL_ESC\""
    JSON_CMD=$(printf '%s' "$REWRITE" | sed 's/\\/\\\\/g;s/"/\\"/g')
    printf '{{"hookSpecificOutput":{{"hookEventName":"PreToolUse","permissionDecision":"allow","updatedInput":{{"command":"%s"}}}}}}' "$JSON_CMD" ;;
  *) exit 0 ;;
esac
"#
    )
}

const REDIRECT_SCRIPT_CLAUDE: &str = r#"#!/usr/bin/env bash
# nebu-ctx PreToolUse hook — all native tools pass through
# Read/Grep/ListFiles are allowed so Edit (which requires native Read) works.
# The MCP instructions guide the AI to prefer ctx_read/ctx_search/ctx_tree.
exit 0
"#;

pub fn install_project_rules() {
    if crate::core::config::Config::load().rules_scope_effective()
        == crate::core::config::RulesScope::Global
    {
        return;
    }

    let cwd = std::env::current_dir().unwrap_or_default();

    if !is_inside_git_repo(&cwd) {
        eprintln!(
            "  Skipping project files: not inside a git repository.\n  \
             Run this command from your project root to create CLAUDE.md / AGENTS.md."
        );
        return;
    }

    let home = dirs::home_dir().unwrap_or_default();
    if cwd == home {
        eprintln!(
            "  Skipping project files: current directory is your home folder.\n  \
             Run this command from a project directory instead."
        );
        return;
    }

    ensure_project_agents_integration(&cwd);

    let claude_rules_dir = cwd.join(".claude").join("rules");
    let claude_rules_file = claude_rules_dir.join("nebu-ctx.md");
    if !claude_rules_file.exists()
        || !std::fs::read_to_string(&claude_rules_file)
            .unwrap_or_default()
            .contains(crate::rules_inject::RULES_VERSION_STR)
    {
        let _ = std::fs::create_dir_all(&claude_rules_dir);
        write_file(
            &claude_rules_file,
            &crate::rules_inject::rules_dedicated_markdown(),
        );
        println!("Created .claude/rules/nebu-ctx.md (Claude Code project rules).");
    }

    install_claude_project_hooks(&cwd);
}

const PROJECT_NEBU_CTX_MD_MARKER: &str = "<!-- nebu-ctx-owned: PROJECT-NEBU-CTX.md v1 -->";
const PROJECT_NEBU_CTX_MD: &str = "LEAN-CTX.md";
const PROJECT_AGENTS_MD: &str = "AGENTS.md";
const AGENTS_BLOCK_START: &str = "<!-- nebu-ctx -->";
const AGENTS_BLOCK_END: &str = "<!-- /nebu-ctx -->";

fn ensure_project_agents_integration(cwd: &std::path::Path) {
    let lean_ctx_md = cwd.join(PROJECT_NEBU_CTX_MD);
    let desired = format!(
        "{PROJECT_NEBU_CTX_MD_MARKER}\n{}\n",
        crate::rules_inject::rules_dedicated_markdown()
    );

    if !lean_ctx_md.exists() {
        write_file(&lean_ctx_md, &desired);
    } else if std::fs::read_to_string(&lean_ctx_md)
        .unwrap_or_default()
        .contains(PROJECT_NEBU_CTX_MD_MARKER)
    {
        let current = std::fs::read_to_string(&lean_ctx_md).unwrap_or_default();
        if !current.contains(crate::rules_inject::RULES_VERSION_STR) {
            write_file(&lean_ctx_md, &desired);
        }
    }

    let block = format!(
        "{AGENTS_BLOCK_START}\n\
## nebu-ctx\n\n\
Prefer nebu-ctx MCP tools over native equivalents for token savings.\n\
Full rules: @{PROJECT_NEBU_CTX_MD}\n\
{AGENTS_BLOCK_END}\n"
    );

    let agents_md = cwd.join(PROJECT_AGENTS_MD);
    if !agents_md.exists() {
        let content = format!("# Agent Instructions\n\n{block}");
        write_file(&agents_md, &content);
        println!("Created AGENTS.md in project root (nebu-ctx reference only).");
        return;
    }

    let existing = std::fs::read_to_string(&agents_md).unwrap_or_default();
    if existing.contains(AGENTS_BLOCK_START) {
        let updated = replace_marked_block(&existing, AGENTS_BLOCK_START, AGENTS_BLOCK_END, &block);
        if updated != existing {
            write_file(&agents_md, &updated);
        }
        return;
    }

    if existing.contains("nebu-ctx") && existing.contains(PROJECT_NEBU_CTX_MD) {
        return;
    }

    let mut out = existing;
    if !out.ends_with('\n') {
        out.push('\n');
    }
    out.push('\n');
    out.push_str(&block);
    write_file(&agents_md, &out);
    println!("Updated AGENTS.md (added nebu-ctx reference block).");
}

fn replace_marked_block(content: &str, start: &str, end: &str, replacement: &str) -> String {
    let s = content.find(start);
    let e = content.find(end);
    match (s, e) {
        (Some(si), Some(ei)) if ei >= si => {
            let after_end = ei + end.len();
            let before = &content[..si];
            let after = &content[after_end..];
            let mut out = String::new();
            out.push_str(before.trim_end_matches('\n'));
            out.push('\n');
            out.push('\n');
            out.push_str(replacement.trim_end_matches('\n'));
            out.push('\n');
            out.push_str(after.trim_start_matches('\n'));
            out
        }
        _ => content.to_string(),
    }
}

pub fn install_agent_hook(agent: &str, global: bool) {
    match agent {
        "claude" | "claude-code" => install_claude_hook(global),
        "codex" => install_codex_hook(),
        "copilot" => install_copilot_hook(global),
        "opencode" => install_opencode_hook(),
        _ => {
            eprintln!("Unknown agent: {agent}");
            eprintln!("  Supported: claude, codex, copilot, opencode");
            std::process::exit(1);
        }
    }
}

fn write_file(path: &std::path::Path, content: &str) {
    if let Err(e) = crate::config_io::write_atomic_with_backup(path, content) {
        eprintln!("Error writing {}: {e}", path.display());
    }
}

fn is_inside_git_repo(path: &std::path::Path) -> bool {
    let mut p = path;
    loop {
        if p.join(".git").exists() {
            return true;
        }
        match p.parent() {
            Some(parent) => p = parent,
            None => return false,
        }
    }
}

#[cfg(unix)]
fn make_executable(path: &PathBuf) {
    use std::os::unix::fs::PermissionsExt;
    let _ = std::fs::set_permissions(path, std::fs::Permissions::from_mode(0o755));
}

#[cfg(not(unix))]
fn make_executable(_path: &PathBuf) {}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn bash_path_unix_unchanged() {
        assert_eq!(
            to_bash_compatible_path("/usr/local/bin/nebu-ctx"),
            "/usr/local/bin/nebu-ctx"
        );
    }

    #[test]
    fn bash_path_home_unchanged() {
        assert_eq!(
            to_bash_compatible_path("/home/user/.cargo/bin/nebu-ctx"),
            "/home/user/.cargo/bin/nebu-ctx"
        );
    }

    #[test]
    fn bash_path_windows_drive_converted() {
        assert_eq!(
            to_bash_compatible_path("C:\\Users\\Fraser\\bin\\nebu-ctx.exe"),
            "/c/Users/Fraser/bin/nebu-ctx.exe"
        );
    }

    #[test]
    fn bash_path_windows_lowercase_drive() {
        assert_eq!(
            to_bash_compatible_path("D:\\tools\\nebu-ctx.exe"),
            "/d/tools/nebu-ctx.exe"
        );
    }

    #[test]
    fn bash_path_windows_forward_slashes() {
        assert_eq!(
            to_bash_compatible_path("C:/Users/Fraser/bin/nebu-ctx.exe"),
            "/c/Users/Fraser/bin/nebu-ctx.exe"
        );
    }

    #[test]
    fn bash_path_bare_name_unchanged() {
        assert_eq!(to_bash_compatible_path("nebu-ctx"), "nebu-ctx");
    }

    #[test]
    fn host_path_windows_preserved() {
        assert_eq!(
            to_host_compatible_path(r"C:\Users\Fraser\bin\nebu-ctx.exe"),
            r"C:\Users\Fraser\bin\nebu-ctx.exe"
        );
    }

    #[test]
    fn host_path_verbatim_stripped() {
        assert_eq!(
            to_host_compatible_path(r"\\?\C:\Users\Fraser\bin\nebu-ctx.exe"),
            "C:/Users/Fraser/bin/nebu-ctx.exe"
        );
    }

    #[test]
    fn normalize_msys2_path() {
        assert_eq!(
            normalize_tool_path("/c/Users/game/Downloads/project"),
            "C:/Users/game/Downloads/project"
        );
    }

    #[test]
    fn normalize_msys2_drive_d() {
        assert_eq!(
            normalize_tool_path("/d/Projects/app/src"),
            "D:/Projects/app/src"
        );
    }

    #[test]
    fn normalize_backslashes() {
        assert_eq!(
            normalize_tool_path("C:\\Users\\game\\project\\src"),
            "C:/Users/game/project/src"
        );
    }

    #[test]
    fn normalize_mixed_separators() {
        assert_eq!(
            normalize_tool_path("C:\\Users/game\\project/src"),
            "C:/Users/game/project/src"
        );
    }

    #[test]
    fn normalize_double_slashes() {
        assert_eq!(
            normalize_tool_path("/home/user//project///src"),
            "/home/user/project/src"
        );
    }

    #[test]
    fn normalize_trailing_slash() {
        assert_eq!(
            normalize_tool_path("/home/user/project/"),
            "/home/user/project"
        );
    }

    #[test]
    fn normalize_root_preserved() {
        assert_eq!(normalize_tool_path("/"), "/");
    }

    #[test]
    fn normalize_windows_root_preserved() {
        assert_eq!(normalize_tool_path("C:/"), "C:/");
    }

    #[test]
    fn normalize_unix_path_unchanged() {
        assert_eq!(
            normalize_tool_path("/home/user/project/src/main.rs"),
            "/home/user/project/src/main.rs"
        );
    }

    #[test]
    fn normalize_relative_path_unchanged() {
        assert_eq!(normalize_tool_path("src/main.rs"), "src/main.rs");
    }

    #[test]
    fn normalize_dot_unchanged() {
        assert_eq!(normalize_tool_path("."), ".");
    }

    #[test]
    fn normalize_unc_path_preserved() {
        assert_eq!(
            normalize_tool_path("//server/share/file"),
            "//server/share/file"
        );
    }

    #[test]
    fn rewrite_script_uses_registry_pattern() {
        let script = generate_rewrite_script("/usr/bin/nebu-ctx");
        assert!(script.contains(r"git\ *"), "script missing git pattern");
        assert!(script.contains(r"cargo\ *"), "script missing cargo pattern");
        assert!(script.contains(r"npm\ *"), "script missing npm pattern");
        assert!(
            !script.contains(r"rg\ *"),
            "script should not contain rg pattern"
        );
        assert!(
            script.contains("NEBU_CTX_BIN=\"/usr/bin/nebu-ctx\""),
            "script missing binary path"
        );
    }

    #[test]
    fn compact_rewrite_script_uses_registry_pattern() {
        let script = generate_compact_rewrite_script("/usr/bin/nebu-ctx");
        assert!(script.contains(r"git\ *"), "compact script missing git");
        assert!(script.contains(r"cargo\ *"), "compact script missing cargo");
        assert!(
            !script.contains(r"rg\ *"),
            "compact script should not contain rg"
        );
    }

    #[test]
    fn rewrite_scripts_contain_all_registry_commands() {
        let script = generate_rewrite_script("nebu-ctx");
        let compact = generate_compact_rewrite_script("nebu-ctx");
        for entry in crate::rewrite_registry::REWRITE_COMMANDS {
            if entry.category == crate::rewrite_registry::Category::Search {
                continue;
            }
            let pattern = if entry.command.contains('-') {
                format!("{}*", entry.command.replace('-', r"\-"))
            } else {
                format!(r"{}\ *", entry.command)
            };
            assert!(
                script.contains(&pattern),
                "rewrite_script missing '{}' (pattern: {})",
                entry.command,
                pattern
            );
            assert!(
                compact.contains(&pattern),
                "compact_rewrite_script missing '{}' (pattern: {})",
                entry.command,
                pattern
            );
        }
    }
}
