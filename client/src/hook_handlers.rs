use crate::compound_lexer;
use crate::rewrite_registry;
use std::io::Read;

pub fn handle_rewrite() {
    let binary = resolve_binary();
    let mut input = String::new();
    if std::io::stdin().read_to_string(&mut input).is_err() {
        return;
    }

    let tool = extract_json_field(&input, "tool_name");
    if !matches!(tool.as_deref(), Some("Bash" | "bash")) {
        return;
    }

    let cmd = match extract_json_field(&input, "command") {
        Some(c) => c,
        None => return,
    };

    if let Some(rewritten) = rewrite_candidate(&cmd, &binary) {
        emit_rewrite(&rewritten);
    }
}

fn is_rewritable(cmd: &str) -> bool {
    rewrite_registry::is_rewritable_command(cmd)
}

fn wrap_single_command(cmd: &str, binary: &str) -> String {
    let shell_escaped = cmd.replace('\'', "'\\''");
    format!("{binary} -c '{shell_escaped}'")
}

fn rewrite_candidate(cmd: &str, binary: &str) -> Option<String> {
    if cmd.starts_with("lean-ctx ") || cmd.starts_with(&format!("{binary} ")) {
        return None;
    }

    // Heredocs cannot survive the quoting round-trip through `lean-ctx -c '...'`.
    // Newlines get escaped, breaking the heredoc syntax entirely (GitHub #140).
    if cmd.contains("<<") {
        return None;
    }

    if let Some(rewritten) = build_rewrite_compound(cmd, binary) {
        return Some(rewritten);
    }

    if is_rewritable(cmd) {
        return Some(wrap_single_command(cmd, binary));
    }

    None
}

fn build_rewrite_compound(cmd: &str, binary: &str) -> Option<String> {
    compound_lexer::rewrite_compound(cmd, |segment| {
        if segment.starts_with("lean-ctx ") || segment.starts_with(&format!("{binary} ")) {
            return None;
        }
        if is_rewritable(segment) {
            Some(wrap_single_command(segment, binary))
        } else {
            None
        }
    })
}

fn emit_rewrite(rewritten: &str) {
    let json_escaped = rewritten.replace('\\', "\\\\").replace('"', "\\\"");
    print!(
        "{{\"hookSpecificOutput\":{{\"hookEventName\":\"PreToolUse\",\"permissionDecision\":\"allow\",\"updatedInput\":{{\"command\":\"{json_escaped}\"}}}}}}"
    );
}

pub fn handle_redirect() {
    // Allow all native tools (Read, Grep, ListFiles) to pass through.
    // Blocking them breaks Edit (which requires native Read) and causes
    // unnecessary friction. The MCP instructions already guide the AI
    // to prefer ctx_read/ctx_search/ctx_tree.
}

fn codex_reroute_message(rewritten: &str) -> String {
    format!(
        "Command should run via lean-ctx for compact output. Do not retry the original command. Re-run with: {rewritten}"
    )
}

pub fn handle_codex_pretooluse() {
    let binary = resolve_binary();
    let mut input = String::new();
    if std::io::stdin().read_to_string(&mut input).is_err() {
        return;
    }

    let tool = extract_json_field(&input, "tool_name");
    if !matches!(tool.as_deref(), Some("Bash" | "bash")) {
        return;
    }

    let cmd = match extract_json_field(&input, "command") {
        Some(c) => c,
        None => return,
    };

    if let Some(rewritten) = rewrite_candidate(&cmd, &binary) {
        eprintln!("{}", codex_reroute_message(&rewritten));
        std::process::exit(2);
    }
}

pub fn handle_codex_session_start() {
    println!(
        "For shell commands matched by lean-ctx compression rules, prefer `lean-ctx -c \"<command>\"`. If a Bash call is blocked, rerun it with the exact command suggested by the hook."
    );
}

/// Copilot-specific PreToolUse handler.
/// VS Code Copilot Chat uses the same hook format as Claude Code.
/// Tool names differ: "runInTerminal" / "editFile" instead of "Bash" / "Read".
pub fn handle_copilot() {
    let binary = resolve_binary();
    let mut input = String::new();
    if std::io::stdin().read_to_string(&mut input).is_err() {
        return;
    }

    let tool = extract_json_field(&input, "tool_name");
    let tool_name = match tool.as_deref() {
        Some(name) => name,
        None => return,
    };

    let is_shell_tool = matches!(
        tool_name,
        "Bash" | "bash" | "runInTerminal" | "run_in_terminal" | "terminal" | "shell"
    );
    if !is_shell_tool {
        return;
    }

    let cmd = match extract_json_field(&input, "command") {
        Some(c) => c,
        None => return,
    };

    if let Some(rewritten) = rewrite_candidate(&cmd, &binary) {
        emit_rewrite(&rewritten);
    }
}

/// Inline rewrite: takes a command as CLI args, prints the rewritten command to stdout.
/// Used by the OpenCode TS plugin where the command is passed as an argument,
/// not via stdin JSON.
pub fn handle_rewrite_inline() {
    let binary = resolve_binary();
    let args: Vec<String> = std::env::args().collect();
    // args: [binary, "hook", "rewrite-inline", ...command parts]
    if args.len() < 4 {
        return;
    }
    let cmd = args[3..].join(" ");

    if let Some(rewritten) = rewrite_candidate(&cmd, &binary) {
        print!("{rewritten}");
        return;
    }

    if cmd.starts_with("lean-ctx ") || cmd.starts_with(&format!("{binary} ")) {
        print!("{cmd}");
        return;
    }

    print!("{cmd}");
}

/// Session-end handler: consolidate local session facts into `knowledge.json`,
/// then forward every promoted fact to the cloud via `ctx_knowledge`.
/// Always snapshots the session summary to `ctx_brain` regardless of whether
/// any facts were promoted.
/// Wired to Claude Code `Stop` and Copilot CLI `postSession`.
pub fn handle_stop() {
    let project_root = std::env::current_dir()
        .map(|p| p.to_string_lossy().to_string())
        .unwrap_or_default();

    if project_root.is_empty() {
        return;
    }

    let outcome = crate::core::consolidation_engine::consolidate_latest(
        &project_root,
        crate::core::consolidation_engine::ConsolidationBudgets::default(),
    );

    let promoted = outcome.as_ref().map(|o| o.promoted).unwrap_or(0);
    if promoted > 0 {
        // Forward promoted facts to the cloud so they land in PostgreSQL.
        post_promoted_facts_to_cloud(&project_root);
    }

    // Always snapshot session summary to brain (even if no knowledge facts promoted).
    if let Some(session) =
        crate::core::session::SessionState::load_latest_for_project_root(&project_root)
    {
        crate::cloud_client::post_session_to_brain(&session);
    }
}

/// Forwards the current knowledge facts for the project to the cloud server
/// via `ctx_knowledge(action="remember")` for each current, high-confidence fact.
fn post_promoted_facts_to_cloud(project_root: &str) {
    let Ok(client) = crate::cloud_client::ServerClient::load() else {
        return;
    };
    let ctx = crate::git_context::discover_project_context(std::path::Path::new(project_root));
    let knowledge = crate::core::knowledge::ProjectKnowledge::load_or_create(project_root);

    for fact in knowledge.facts.iter().filter(|f| f.is_current() && f.confidence >= 0.7) {
        let mut args = serde_json::Map::new();
        args.insert("action".to_string(), serde_json::json!("remember"));
        args.insert("category".to_string(), serde_json::json!(fact.category));
        args.insert("key".to_string(), serde_json::json!(fact.key));
        args.insert("value".to_string(), serde_json::json!(fact.value));
        args.insert("confidence".to_string(), serde_json::json!(fact.confidence));
        let _ = client.call_tool("ctx_knowledge", args, &ctx);
    }
}

/// PostToolUse telemetry handler: reads the hook event JSON from stdin,
/// extracts the tool name and rough token sizes, and fires a telemetry
/// event to the server. Wired to Claude Code `PostToolUse` and Copilot
/// CLI `postToolUse`.
pub fn handle_post_tool_use() {
    let mut input = String::new();
    if std::io::stdin().read_to_string(&mut input).is_err() {
        return;
    }

    let tool_name = extract_json_field(&input, "tool_name")
        .or_else(|| extract_json_field(&input, "toolName"))
        .unwrap_or_else(|| "unknown".to_string());

    // Prefer Claude Code's nested usage.{input,output}_tokens; fall back to
    // byte-length proxy when those fields are absent.
    let parsed: Option<serde_json::Value> = serde_json::from_str(&input).ok();

    let tokens_in = parsed
        .as_ref()
        .and_then(|v| v.get("usage"))
        .and_then(|u| u.get("input_tokens"))
        .and_then(|t| t.as_i64())
        .unwrap_or_else(|| {
            let bytes = extract_json_field(&input, "tool_input")
                .map(|s| s.len())
                .unwrap_or(0);
            (bytes / 4) as i64
        });

    let tokens_out = parsed
        .as_ref()
        .and_then(|v| v.get("usage"))
        .and_then(|u| u.get("output_tokens"))
        .and_then(|t| t.as_i64())
        .unwrap_or_else(|| {
            let bytes = extract_json_field(&input, "tool_response")
                .or_else(|| extract_json_field(&input, "tool_result"))
                .map(|s| s.len())
                .unwrap_or(0);
            (bytes / 4) as i64
        });

    crate::core::telemetry_queue::fire_sync(crate::models::TelemetryIngestRequest {
        tool_name: crate::core::stats::normalize_command(&tool_name),
        tokens_original: tokens_in + tokens_out,
        tokens_saved: 0,
        duration_ms: 0,
        mode: Some("hook".to_string()),
        repository_fingerprint: None,
        checkout_binding: None,
        project_slug: None,
    });
}

fn resolve_binary() -> String {
    let path = crate::core::portable_binary::resolve_portable_binary();
    crate::hooks::to_bash_compatible_path(&path)
}

fn extract_json_field(input: &str, field: &str) -> Option<String> {
    let pattern = format!("\"{}\":\"", field);
    let start = input.find(&pattern)? + pattern.len();
    let rest = &input[start..];
    let bytes = rest.as_bytes();
    let mut end = 0;
    while end < bytes.len() {
        if bytes[end] == b'\\' && end + 1 < bytes.len() {
            end += 2;
            continue;
        }
        if bytes[end] == b'"' {
            break;
        }
        end += 1;
    }
    if end >= bytes.len() {
        return None;
    }
    let raw = &rest[..end];
    Some(raw.replace("\\\"", "\"").replace("\\\\", "\\"))
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn is_rewritable_basic() {
        assert!(is_rewritable("git status"));
        assert!(is_rewritable("cargo test --lib"));
        assert!(is_rewritable("npm run build"));
        assert!(!is_rewritable("echo hello"));
        assert!(!is_rewritable("cd src"));
    }

    #[test]
    fn wrap_single() {
        let r = wrap_single_command("git status", "lean-ctx");
        assert_eq!(r, "lean-ctx -c 'git status'");
    }

    #[test]
    fn wrap_with_quotes() {
        let r = wrap_single_command(r#"curl -H "Auth" https://api.com"#, "lean-ctx");
        assert_eq!(r, r#"lean-ctx -c 'curl -H "Auth" https://api.com'"#);
    }

    #[test]
    fn rewrite_candidate_returns_none_for_existing_lean_ctx_command() {
        assert_eq!(
            rewrite_candidate("lean-ctx -c git status", "lean-ctx"),
            None
        );
    }

    #[test]
    fn rewrite_candidate_wraps_single_command() {
        assert_eq!(
            rewrite_candidate("git status", "lean-ctx"),
            Some("lean-ctx -c 'git status'".to_string())
        );
    }

    #[test]
    fn rewrite_candidate_passes_through_heredoc() {
        assert_eq!(
            rewrite_candidate(
                "git commit -m \"$(cat <<'EOF'\nfix: something\nEOF\n)\"",
                "lean-ctx"
            ),
            None
        );
    }

    #[test]
    fn rewrite_candidate_passes_through_heredoc_compound() {
        assert_eq!(
            rewrite_candidate(
                "git add . && git commit -m \"$(cat <<EOF\nfeat: add\nEOF\n)\"",
                "lean-ctx"
            ),
            None
        );
    }

    #[test]
    fn codex_reroute_message_includes_exact_rewritten_command() {
        let message = codex_reroute_message("lean-ctx -c 'git status'");
        assert_eq!(
            message,
            "Command should run via lean-ctx for compact output. Do not retry the original command. Re-run with: lean-ctx -c 'git status'"
        );
    }

    #[test]
    fn compound_rewrite_and_chain() {
        let result = build_rewrite_compound("cd src && git status && echo done", "lean-ctx");
        assert_eq!(
            result,
            Some("cd src && lean-ctx -c 'git status' && echo done".into())
        );
    }

    #[test]
    fn compound_rewrite_pipe() {
        let result = build_rewrite_compound("git log --oneline | head -5", "lean-ctx");
        assert_eq!(
            result,
            Some("lean-ctx -c 'git log --oneline' | head -5".into())
        );
    }

    #[test]
    fn compound_rewrite_no_match() {
        let result = build_rewrite_compound("cd src && echo done", "lean-ctx");
        assert_eq!(result, None);
    }

    #[test]
    fn compound_rewrite_multiple_rewritable() {
        let result = build_rewrite_compound("git add . && cargo test && npm run lint", "lean-ctx");
        assert_eq!(
            result,
            Some(
                "lean-ctx -c 'git add .' && lean-ctx -c 'cargo test' && lean-ctx -c 'npm run lint'"
                    .into()
            )
        );
    }

    #[test]
    fn compound_rewrite_semicolons() {
        let result = build_rewrite_compound("git add .; git commit -m 'fix'", "lean-ctx");
        assert_eq!(
            result,
            Some("lean-ctx -c 'git add .' ; lean-ctx -c 'git commit -m '\\''fix'\\'''".into())
        );
    }

    #[test]
    fn compound_rewrite_or_chain() {
        let result = build_rewrite_compound("git pull || echo failed", "lean-ctx");
        assert_eq!(result, Some("lean-ctx -c 'git pull' || echo failed".into()));
    }

    #[test]
    fn compound_skips_already_rewritten() {
        let result = build_rewrite_compound("lean-ctx -c git status && git diff", "lean-ctx");
        assert_eq!(
            result,
            Some("lean-ctx -c git status && lean-ctx -c 'git diff'".into())
        );
    }

    #[test]
    fn single_command_not_compound() {
        let result = build_rewrite_compound("git status", "lean-ctx");
        assert_eq!(result, None);
    }

    #[test]
    fn extract_field_works() {
        let input = r#"{"tool_name":"Bash","command":"git status"}"#;
        assert_eq!(
            extract_json_field(input, "tool_name"),
            Some("Bash".to_string())
        );
        assert_eq!(
            extract_json_field(input, "command"),
            Some("git status".to_string())
        );
    }

    #[test]
    fn extract_field_handles_escaped_quotes() {
        let input = r#"{"tool_name":"Bash","command":"grep -r \"TODO\" src/"}"#;
        assert_eq!(
            extract_json_field(input, "command"),
            Some(r#"grep -r "TODO" src/"#.to_string())
        );
    }

    #[test]
    fn extract_field_handles_escaped_backslash() {
        let input = r#"{"tool_name":"Bash","command":"echo \\\"hello\\\""}"#;
        assert_eq!(
            extract_json_field(input, "command"),
            Some(r#"echo \"hello\""#.to_string())
        );
    }

    #[test]
    fn extract_field_handles_complex_curl() {
        let input = r#"{"tool_name":"Bash","command":"curl -H \"Authorization: Bearer token\" https://api.com"}"#;
        assert_eq!(
            extract_json_field(input, "command"),
            Some(r#"curl -H "Authorization: Bearer token" https://api.com"#.to_string())
        );
    }

    #[test]
    fn to_bash_compatible_path_windows_drive() {
        let p = crate::hooks::to_bash_compatible_path(r"E:\packages\lean-ctx.exe");
        assert_eq!(p, "/e/packages/lean-ctx.exe");
    }

    #[test]
    fn to_bash_compatible_path_backslashes() {
        let p = crate::hooks::to_bash_compatible_path(r"C:\Users\test\bin\lean-ctx.exe");
        assert_eq!(p, "/c/Users/test/bin/lean-ctx.exe");
    }

    #[test]
    fn to_bash_compatible_path_unix_unchanged() {
        let p = crate::hooks::to_bash_compatible_path("/usr/local/bin/lean-ctx");
        assert_eq!(p, "/usr/local/bin/lean-ctx");
    }

    #[test]
    fn to_bash_compatible_path_msys2_unchanged() {
        let p = crate::hooks::to_bash_compatible_path("/e/packages/lean-ctx.exe");
        assert_eq!(p, "/e/packages/lean-ctx.exe");
    }

    #[test]
    fn wrap_command_with_bash_path() {
        let binary = crate::hooks::to_bash_compatible_path(r"E:\packages\lean-ctx.exe");
        let result = wrap_single_command("git status", &binary);
        assert!(
            !result.contains('\\'),
            "wrapped command must not contain backslashes, got: {result}"
        );
        assert!(
            result.starts_with("/e/packages/lean-ctx.exe"),
            "must use bash-compatible path, got: {result}"
        );
    }

    #[test]
    fn wrap_single_command_em_dash() {
        let r = wrap_single_command("gh --comment \"closing — see #407\"", "lean-ctx");
        assert_eq!(r, "lean-ctx -c 'gh --comment \"closing — see #407\"'");
    }

    #[test]
    fn wrap_single_command_dollar_sign() {
        let r = wrap_single_command("echo $HOME", "lean-ctx");
        assert_eq!(r, "lean-ctx -c 'echo $HOME'");
    }

    #[test]
    fn wrap_single_command_backticks() {
        let r = wrap_single_command("echo `date`", "lean-ctx");
        assert_eq!(r, "lean-ctx -c 'echo `date`'");
    }

    #[test]
    fn wrap_single_command_nested_single_quotes() {
        let r = wrap_single_command("echo 'hello world'", "lean-ctx");
        assert_eq!(r, r"lean-ctx -c 'echo '\''hello world'\'''");
    }

    #[test]
    fn wrap_single_command_exclamation_mark() {
        let r = wrap_single_command("echo hello!", "lean-ctx");
        assert_eq!(r, "lean-ctx -c 'echo hello!'");
    }

    #[test]
    fn wrap_single_command_find_with_many_excludes() {
        let r = wrap_single_command(
            "find . -not -path ./node_modules -not -path ./.git -not -path ./dist",
            "lean-ctx",
        );
        assert_eq!(
            r,
            "lean-ctx -c 'find . -not -path ./node_modules -not -path ./.git -not -path ./dist'"
        );
    }
}
