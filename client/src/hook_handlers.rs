use crate::compound_lexer;
use crate::rewrite_registry;
use std::io::Read;

fn read_stdin_string() -> Option<String> {
    let mut input = String::new();
    std::io::stdin().read_to_string(&mut input).ok()?;
    Some(input)
}

fn extract_first_json_field(input: &str, fields: &[&str]) -> Option<String> {
    fields
        .iter()
        .find_map(|field| extract_json_field(input, field))
}

fn extract_command_from_hook_input(input: &str) -> Option<String> {
    extract_json_field(input, "command")
}

fn extract_tool_name(input: &str) -> Option<String> {
    extract_first_json_field(input, &["tool_name", "toolName"])
}

fn drain_sync_outbox() {
    let _ = crate::core::telemetry_queue::flush_pending();
}

fn fetch_hosted_wakeup_briefing(project_root: &str) -> Option<String> {
    if project_root.is_empty() {
        return None;
    }

    let ctx = crate::git_context::discover_project_context(std::path::Path::new(project_root));
    let client = crate::server_client::ServerClient::load().ok()?;
    let mut args = serde_json::Map::new();
    args.insert("action".to_string(), serde_json::json!("wakeup"));
    let value = client.call_tool("ctx_knowledge", args, &ctx).ok()?;
    let briefing = value.get("briefing")?.as_str()?.trim().to_string();
    if briefing.is_empty() {
        None
    } else {
        Some(briefing)
    }
}

pub fn handle_rewrite() {
    let binary = resolve_binary();
    let Some(input) = read_stdin_string() else {
        return;
    };

    let tool = extract_tool_name(&input);
    if !matches!(tool.as_deref(), Some("Bash" | "bash")) {
        return;
    }

    let cmd = match extract_command_from_hook_input(&input) {
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
        "Command should run via nebu-ctx for compact output. Do not retry the original command. Re-run with: {rewritten}"
    )
}

pub fn handle_codex_pretooluse() {
    let binary = resolve_binary();
    let Some(input) = read_stdin_string() else {
        return;
    };

    let tool = extract_tool_name(&input);
    if !matches!(tool.as_deref(), Some("Bash" | "bash")) {
        return;
    }

    let cmd = match extract_command_from_hook_input(&input) {
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
        "For shell commands matched by nebu-ctx compression rules, prefer `nebu-ctx -c \"<command>\"`. If a Bash call is blocked, rerun it with the exact command suggested by the hook."
    );
}

/// Copilot-specific PreToolUse handler.
/// VS Code Copilot Chat uses the same hook format as Claude Code.
/// Tool names differ: "runInTerminal" / "editFile" instead of "Bash" / "Read".
pub fn handle_copilot() {
    let binary = resolve_binary();
    let Some(input) = read_stdin_string() else {
        return;
    };

    let tool = extract_tool_name(&input);
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

    let cmd = match extract_command_from_hook_input(&input) {
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
/// then forward every promoted fact to the server via `ctx_knowledge`.
/// Always snapshots the session summary to `ctx_brain` regardless of whether
/// any facts were promoted.
/// Wired to Claude Code `Stop` and Copilot CLI `postSession`.
pub fn handle_stop() {
    drain_sync_outbox();
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
        // Forward promoted facts to the server so they land in PostgreSQL.
        post_promoted_facts_to_server(&project_root);
    }

    // Always snapshot session summary to brain (even if no knowledge facts promoted).
    if let Some(session) =
        crate::core::session::SessionState::load_latest_for_project_root(&project_root)
    {
        crate::server_client::post_session_to_brain(&session);
    }
}

/// PreCompact hook: fired by Claude Code just before it compacts the context window.
///
/// Reads the current local session state and knowledge facts, builds a compact
/// XML `<session_state>` snapshot (≤2KB), and outputs it as `additionalContext`
/// so Claude Code injects it into the post-compaction context automatically.
/// Also fires an async save to the server-backed brain so the state survives cross-session.
///
/// Wired to Claude Code `PreCompact`.
pub fn handle_pre_compact() {
    drain_sync_outbox();
    let project_root = std::env::current_dir()
        .map(|p| p.to_string_lossy().to_string())
        .unwrap_or_default();

    let xml = build_session_snapshot_xml(&project_root, "compaction");

    // Fire async server save so state lands in PostgreSQL (same as handle_stop does).
    if !project_root.is_empty() {
        if let Some(session) =
            crate::core::session::SessionState::load_latest_for_project_root(&project_root)
        {
            crate::server_client::post_session_to_brain(&session);
        }
        post_promoted_facts_to_server(&project_root);
    }

    // Output the snapshot as additionalContext for Claude Code to inject after compact.
    if !xml.is_empty() {
        let escaped = xml
            .replace('\\', "\\\\")
            .replace('"', "\\\"")
            .replace('\n', "\\n");
        println!("{{\"additionalContext\":\"{escaped}\"}}");
    }
}

/// SessionStart hook: fired by Claude Code at session start, after compact, or on resume.
///
/// - `source="compact"`: Injects task/decisions/files from the most recent local session
///   and current knowledge facts to restore context after a compaction.
/// - `source="startup"` or `source="resume"`: Injects the nebu-ctx routing block so
///   the agent prefers ctx_* MCP tools and compressed shell output from session start.
///
/// Wired to Claude Code `SessionStart`.
pub fn handle_session_start() {
    let Some(input) = read_stdin_string() else {
        return;
    };

    let source = extract_json_field(&input, "source").unwrap_or_else(|| "startup".to_string());

    let project_root = std::env::current_dir()
        .map(|p| p.to_string_lossy().to_string())
        .unwrap_or_default();

    let additional = if source == "compact" || source == "resume" {
        // After compact/resume: inject session state so agent picks up exactly where it left off.
        let snapshot = build_session_snapshot_xml(&project_root, &source);
        let routing = session_start_routing_block();
        if snapshot.is_empty() {
            routing
        } else {
            format!("{routing}\n\n{snapshot}")
        }
    } else {
        let snapshot = build_session_snapshot_xml(&project_root, &source);
        let routing = session_start_routing_block();
        if snapshot.is_empty() {
            routing
        } else {
            format!("{routing}\n\n{snapshot}")
        }
    };

    if !additional.is_empty() {
        let escaped = additional
            .replace('\\', "\\\\")
            .replace('"', "\\\"")
            .replace('\n', "\\n");
        println!("{{\"additionalContext\":\"{escaped}\"}}");
    }
}

/// UserPromptSubmit hook: fired by Claude Code when the user submits a prompt.
///
/// Captures the raw prompt for session continuity tracking. Stores it in the
/// server-backed brain so the pre-compact snapshot can include the user's most recent
/// intent. Must be fast — fires async and exits immediately.
///
/// Wired to Claude Code `UserPromptSubmit`.
pub fn handle_user_prompt_submit() {
    let Some(input) = read_stdin_string() else {
        return;
    };

    let prompt = extract_first_json_field(&input, &["prompt", "message"]).unwrap_or_default();

    let trimmed = prompt.trim().to_string();
    if trimmed.is_empty() {
        return;
    }

    // Skip system-generated messages injected by hooks.
    let is_system = trimmed.starts_with("<session_state")
        || trimmed.starts_with("<context_guidance>")
        || trimmed.starts_with("<system-reminder>")
        || trimmed.starts_with("<tool-result>");
    if is_system {
        return;
    }

    // Store the user prompt in brain so PreCompact can surface recent intent.
    let project_root = std::env::current_dir()
        .map(|p| p.to_string_lossy().to_string())
        .unwrap_or_default();
    if project_root.is_empty() {
        return;
    }
    let ctx = crate::git_context::discover_project_context(std::path::Path::new(&project_root));
    let mut args = serde_json::Map::new();
    args.insert("action".to_string(), serde_json::json!("store"));
    let key = format!("user-prompt-{}", chrono::Utc::now().timestamp());
    args.insert("key".to_string(), serde_json::Value::String(key));
    let value = format!("user_prompt: {}", &trimmed[..trimmed.len().min(400)]);
    args.insert("value".to_string(), serde_json::Value::String(value));
    let _ = crate::server_client::queue_or_call_tool("ctx_brain", args, &ctx);
}

/// Builds a compact XML `<session_state>` block (≤2KB) from local session state
/// and knowledge facts. Used by `handle_pre_compact` and `handle_session_start`.
///
/// `source` is included in the XML attribute so the agent knows where it came from
/// (e.g. `"compaction"` or `"compact"` or `"resume"`).
/// Returns an empty string if no session state is found.
fn build_session_snapshot_xml(project_root: &str, source: &str) -> String {
    if project_root.is_empty() {
        return String::new();
    }

    let session = crate::core::session::SessionState::load_latest_for_project_root(project_root);
    let knowledge = crate::core::knowledge::ProjectKnowledge::load_or_create(project_root);

    let has_session = session.is_some();
    let high_confidence_facts: Vec<_> = knowledge
        .facts
        .iter()
        .filter(|f| f.is_current() && f.confidence >= 0.7)
        .collect();

    if !has_session && high_confidence_facts.is_empty() {
        return String::new();
    }

    let mut parts: Vec<String> = Vec::new();

    if let Some(ref s) = session {
        // P1: Current task (never truncated)
        if let Some(ref task) = s.task {
            parts.push(format!(
                "<current_task>{}</current_task>",
                xml_escape(&task.description)
            ));
        }

        // P2: Recent decisions (latest 5)
        let decisions: Vec<_> = s.decisions.iter().rev().take(5).collect();
        if !decisions.is_empty() {
            let lines: Vec<String> = decisions
                .iter()
                .map(|d| format!("- {}", xml_escape(&d.summary)))
                .collect();
            parts.push(format!("<decisions>\n{}\n</decisions>", lines.join("\n")));
        }

        // P3: Files touched (modified only, latest 8)
        let modified_files: Vec<_> = s
            .files_touched
            .iter()
            .filter(|f| f.modified)
            .rev()
            .take(8)
            .collect();
        if !modified_files.is_empty() {
            let lines: Vec<String> = modified_files
                .iter()
                .map(|f| format!("- {}", xml_escape(&f.path)))
                .collect();
            parts.push(format!(
                "<files_modified>\n{}\n</files_modified>",
                lines.join("\n")
            ));
        }

        // P4: Next steps (latest 3)
        let next_steps: Vec<_> = s.next_steps.iter().rev().take(3).collect();
        if !next_steps.is_empty() {
            let lines: Vec<String> = next_steps
                .iter()
                .map(|ns| format!("- {}", xml_escape(ns)))
                .collect();
            parts.push(format!("<next_steps>\n{}\n</next_steps>", lines.join("\n")));
        }
    }

    // P5: Key knowledge facts or hosted wake-up briefing.
    if let Some(hosted) = fetch_hosted_wakeup_briefing(project_root) {
        parts.push(format!(
            "<knowledge>\n{}\n</knowledge>",
            xml_escape(&hosted)
        ));
    } else if !high_confidence_facts.is_empty() {
        let facts_text: Vec<String> = high_confidence_facts
            .iter()
            .rev()
            .take(5)
            .map(|f| {
                format!(
                    "- [{}] {}: {}",
                    xml_escape(&f.category),
                    xml_escape(&f.key),
                    xml_escape(&f.value)
                )
            })
            .collect();
        parts.push(format!(
            "<knowledge>\n{}\n</knowledge>",
            facts_text.join("\n")
        ));
    }

    if parts.is_empty() {
        return String::new();
    }

    // Enforce ≤2KB (≈500 tokens) budget: truncate parts from the end if over limit.
    let mut xml = format!(
        "<session_state source=\"{source}\">\n\n{}\n\n</session_state>",
        parts.join("\n\n")
    );
    if xml.len() > 2048 {
        while xml.len() > 2048 && parts.len() > 1 {
            parts.pop();
            xml = format!(
                "<session_state source=\"{source}\">\n\n{}\n\n</session_state>",
                parts.join("\n\n")
            );
        }
    }

    xml
}

/// Returns the static routing block injected at session start.
/// Guides the agent to prefer ctx_* MCP tools for compressed output.
fn session_start_routing_block() -> String {
    r#"<context_window_protection>
  Use nebu-ctx MCP tools instead of raw native tools to save tokens:
  - ctx_read / ctx_search / ctx_shell / ctx_tree instead of Read / Grep / Bash / ls
  - ctx_batch_execute for multi-step research (one call replaces many)
  - Bash only for: git, mkdir, rm, mv, navigation
  Skills, roles, and decisions from this session remain active until revoked.
</context_window_protection>"#
        .to_string()
}

/// Escapes characters that are not safe inside XML text nodes.
fn xml_escape(s: &str) -> String {
    s.replace('&', "&amp;")
        .replace('<', "&lt;")
        .replace('>', "&gt;")
        .replace('"', "&quot;")
}

/// Forwards the current knowledge facts for the project to the configured server
/// via a deterministic `ctx_knowledge(action="promote")` batch.
fn post_promoted_facts_to_server(project_root: &str) {
    crate::server_client::post_knowledge_to_server(project_root);
}

/// PostToolUse telemetry handler: reads the hook event JSON from stdin,
/// extracts the tool name and rough token sizes, and fires a telemetry
/// event to the server. Wired to Claude Code `PostToolUse` and Copilot
/// CLI `postToolUse`.
pub fn handle_post_tool_use() {
    let Some(input) = read_stdin_string() else {
        return;
    };

    let tool_name = extract_tool_name(&input).unwrap_or_else(|| "unknown".to_string());

    // Prefer Claude Code's nested usage.{input,output}_tokens; fall back to
    // byte-length proxy when those fields are absent.
    let parsed: Option<serde_json::Value> = serde_json::from_str(&input).ok();

    let tokens_in = parsed
        .as_ref()
        .and_then(|v| v.get("usage"))
        .and_then(|u| u.get("input_tokens"))
        .and_then(|t| t.as_i64())
        .unwrap_or_else(|| {
            let bytes = extract_first_json_field(&input, &["tool_input"])
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
            let bytes = extract_first_json_field(&input, &["tool_response", "tool_result"])
                .map(|s| s.len())
                .unwrap_or(0);
            (bytes / 4) as i64
        });

    let command_preview = extract_command_from_hook_input(&input)
        .and_then(|command| crate::core::sanitize::telemetry_command_preview(&command));
    let project_context = std::env::current_dir()
        .ok()
        .map(|dir| crate::git_context::discover_project_context(&dir));

    crate::core::telemetry_queue::fire_sync(crate::models::TelemetryIngestRequest {
        tool_name: crate::core::stats::normalize_command(&tool_name),
        tokens_original: tokens_in + tokens_out,
        tokens_saved: 0,
        duration_ms: 0,
        mode: Some("hook".to_string()),
        repository_fingerprint: project_context.as_ref().and_then(|context| {
            context
                .fingerprint
                .has_safe_identity()
                .then(|| context.fingerprint.clone())
        }),
        checkout_binding: project_context
            .as_ref()
            .map(|context| context.checkout_binding.clone()),
        project_slug: project_context
            .as_ref()
            .map(|context| context.project_slug.clone())
            .filter(|slug| !slug.is_empty()),
        command_preview,
    });
}

fn resolve_binary() -> String {
    let path = crate::core::portable_binary::resolve_portable_binary();
    crate::hooks::to_host_compatible_path(&path)
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
    fn session_start_includes_snapshot_on_startup_when_memory_exists() {
        let _lock = crate::core::data_dir::test_env_lock();
        let tmp = tempfile::tempdir().unwrap();
        std::env::set_var("NEBU_CTX_DATA_DIR", tmp.path());

        let root = tmp.path().join("project");
        std::fs::create_dir_all(&root).unwrap();

        let mut session = crate::core::session::SessionState::new();
        session.project_root = Some(root.to_string_lossy().to_string());
        session.set_task("Tighten dashboard overview", None);
        session.save().unwrap();

        let xml = build_session_snapshot_xml(&root.to_string_lossy(), "startup");
        assert!(xml.contains("current_task"));
        assert!(xml.contains("Tighten dashboard overview"));
    }

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
    fn codex_reroute_message_uses_nebu_ctx_binary_name() {
        let message = codex_reroute_message("nebu-ctx -c 'git status'");
        assert_eq!(
            message,
            "Command should run via nebu-ctx for compact output. Do not retry the original command. Re-run with: nebu-ctx -c 'git status'"
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
    fn normalize_host_binary_path_preserves_windows_path() {
        let p = crate::hooks::to_host_compatible_path(r"C:\Users\test\bin\nebu-ctx.exe");
        assert_eq!(p, r"C:\Users\test\bin\nebu-ctx.exe");
    }

    #[test]
    fn normalize_host_binary_path_strips_verbatim_prefix() {
        let p = crate::hooks::to_host_compatible_path(r"\\?\C:\Users\test\bin\nebu-ctx.exe");
        assert_eq!(p, "C:/Users/test/bin/nebu-ctx.exe");
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

    #[test]
    fn hook_telemetry_serializes_project_context() {
        let _lock = crate::core::data_dir::test_env_lock();
        let temp = tempfile::tempdir().unwrap();
        let original_dir = std::env::current_dir().unwrap();
        let previous_hostname = std::env::var_os("HOSTNAME");
        let previous_computername = std::env::var_os("COMPUTERNAME");

        std::env::set_var("HOSTNAME", "hook-test-host");
        std::env::remove_var("COMPUTERNAME");
        std::env::set_current_dir(temp.path()).unwrap();

        let project_context = std::env::current_dir()
            .ok()
            .map(|dir| crate::git_context::discover_project_context(&dir))
            .unwrap();
        let request = crate::models::TelemetryIngestRequest {
            tool_name: crate::core::stats::normalize_command("Bash"),
            tokens_original: 12,
            tokens_saved: 0,
            duration_ms: 0,
            mode: Some("hook".to_string()),
            repository_fingerprint: project_context
                .fingerprint
                .has_safe_identity()
                .then(|| project_context.fingerprint.clone()),
            checkout_binding: Some(project_context.checkout_binding.clone()),
            project_slug: Some(project_context.project_slug.clone())
                .filter(|slug| !slug.is_empty()),
            command_preview: crate::core::sanitize::telemetry_command_preview(
                r#"dotnet test --filter "Category=Unit" -p:Token=abc"#,
            ),
        };

        let payload = serde_json::to_value(request).unwrap();
        let local_root = payload
            .get("checkout_binding")
            .and_then(|binding| binding.get("local_root"))
            .and_then(|value| value.as_str())
            .unwrap();
        let client_label = payload
            .get("checkout_binding")
            .and_then(|binding| binding.get("client_label"))
            .and_then(|value| value.as_str())
            .unwrap();
        let project_slug = payload
            .get("project_slug")
            .and_then(|value| value.as_str())
            .unwrap();
        let command_preview = payload
            .get("command_preview")
            .and_then(|value| value.as_str())
            .unwrap();

        assert_eq!(local_root, temp.path().to_string_lossy());
        assert_eq!(client_label, "hook-test-host");
        assert_eq!(project_slug, project_context.project_slug);
        assert!(command_preview.starts_with("dotnet test --filter \"Category=Unit\""));
        assert!(command_preview.contains("-p:Token=abc"));

        std::env::set_current_dir(original_dir).unwrap();
        if let Some(value) = previous_hostname {
            std::env::set_var("HOSTNAME", value);
        } else {
            std::env::remove_var("HOSTNAME");
        }
        if let Some(value) = previous_computername {
            std::env::set_var("COMPUTERNAME", value);
        } else {
            std::env::remove_var("COMPUTERNAME");
        }
    }
}
