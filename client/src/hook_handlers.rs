use crate::compound_lexer;
use crate::rewrite_registry;
use serde_json::Value;
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
    extract_first_json_field(input, &["tool_name", "toolName", "tool"])
}

fn drain_sync_outbox() {
    let _ = crate::core::telemetry_queue::flush_pending();
}

fn sync_memory_if_possible(project_root: &str, source_type: &str) {
    crate::server_client::sync_session_memory_to_server(project_root, source_type);
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
    let payload = serde_json::json!({
        "hookSpecificOutput": {
            "hookEventName": "PreToolUse",
            "permissionDecision": "allow",
            "updatedInput": {
                "command": rewritten,
            }
        }
    });
    print!("{}", payload);
}

// Return an explicit deny decision so Copilot surfaces a reroute message instead of crashing.
fn emit_pretool_deny(reason: &str) {
    let payload = serde_json::json!({
        "hookSpecificOutput": {
            "hookEventName": "PreToolUse",
            "permissionDecision": "deny",
            "permissionDecisionReason": reason,
        }
    });
    print!("{}", payload);
}

// Stop a known Copilot wrapper crash before deferred nebu-ctx tool calls reach the host.
fn blocked_copilot_parallel_reason(input: &str) -> Option<String> {
    let payload: Value = serde_json::from_str(input).ok()?;
    let tool_name = payload
        .get("tool_name")
        .or_else(|| payload.get("toolName"))
        .or_else(|| payload.get("tool"))
        .and_then(Value::as_str)?;
    if tool_name != "multi_tool_use.parallel" {
        return None;
    }

    let tool_uses = payload
        .get("tool_input")
        .or_else(|| payload.get("toolInput"))
        .or_else(|| payload.get("input"))
        .and_then(|value| value.get("tool_uses").or_else(|| value.get("toolUses")))
        .and_then(Value::as_array)?;

    let mut deferred_calls: Vec<String> = tool_uses
        .iter()
        .filter_map(|tool_use| {
            tool_use
                .get("recipient_name")
                .or_else(|| tool_use.get("recipientName"))
                .and_then(Value::as_str)
        })
        .filter(|name| name.contains("mcp_nebuctx_"))
        .map(str::to_string)
        .collect();
    if deferred_calls.is_empty() {
        return None;
    }

    deferred_calls.sort();
    deferred_calls.dedup();
    Some(format!(
        "Blocked known host bug: multi_tool_use.parallel crashes on deferred nebu-ctx tools ({}). Call public ctx_* tools directly instead. Use ctx_read(target=\"files\", paths=[...]) for batch reads, and run repeated ctx_search calls separately.",
        deferred_calls.join(", ")
    ))
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
        "For shell commands matched by nebu-ctx compression rules, always use `nebu-ctx -c \"<command>\"`. If a Bash call is blocked, rerun it with the exact command suggested by the hook. Do not bypass to the original native command; use `--raw` or the repo-built nebu-ctx client if needed, then file/update an issue."
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

    if let Some(reason) = blocked_copilot_parallel_reason(&input) {
        emit_pretool_deny(&reason);
        return;
    }

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

/// Session-end handler: consolidate local session facts, flush local journal,
/// and forward derived brain facts to the server-backed canonical memory.
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
        post_promoted_facts_to_server(&project_root);
    }

    let session_id =
        crate::core::session::SessionState::load_latest_for_project_root(&project_root)
            .map(|session| session.id)
            .unwrap_or_else(|| "sessionless".to_string());
    let _ = crate::core::brain_memory::record_lifecycle_marker(
        &project_root,
        Some(&session_id),
        "hook-stop",
        crate::core::brain_memory::LifecycleEventKind::SessionStop,
        "session stop flush",
    );
    sync_memory_if_possible(&project_root, "stop");
}

/// Idle flush hook: persists derived brain facts without treating the session as stopped.
/// Used by OpenCode when a session goes idle but may still continue later.
pub fn handle_idle_flush() {
    drain_sync_outbox();
    let input = read_stdin_string().unwrap_or_default();
    let project_root = std::env::current_dir()
        .map(|p| p.to_string_lossy().to_string())
        .unwrap_or_default();

    if project_root.is_empty() {
        return;
    }

    let session_id = extract_first_json_field(&input, &["session_id", "sessionID"])
        .or_else(|| {
            crate::core::session::SessionState::load_latest_for_project_root(&project_root)
                .map(|session| session.id)
        })
        .unwrap_or_else(|| "sessionless".to_string());
    let _ = crate::core::brain_memory::record_lifecycle_marker(
        &project_root,
        Some(&session_id),
        "hook-idle-flush",
        crate::core::brain_memory::LifecycleEventKind::IdleFlush,
        "idle flush",
    );
    sync_memory_if_possible(&project_root, "idle_flush");
}

/// PreCompact hook: fired by Claude Code just before it compacts the context window.
///
/// Reads the current local session state and knowledge facts, builds a compact
/// XML `<session_state>` snapshot (≤2KB), and outputs it as `additionalContext`
/// so Claude Code injects it into the post-compaction context automatically.
/// Also flushes local journal and derived facts to canonical brain memory.
///
/// Wired to Claude Code `PreCompact`.
pub fn handle_pre_compact() {
    let input = read_stdin_string().unwrap_or_default();
    drain_sync_outbox();
    let project_root = std::env::current_dir()
        .map(|p| p.to_string_lossy().to_string())
        .unwrap_or_default();

    let xml = build_session_snapshot_xml(&project_root, "compaction");

    if !project_root.is_empty() {
        let session_id = extract_first_json_field(&input, &["session_id", "sessionID"])
            .or_else(|| {
                crate::core::session::SessionState::load_latest_for_project_root(&project_root)
                    .map(|session| session.id)
            })
            .unwrap_or_else(|| "sessionless".to_string());
        let _ = crate::core::brain_memory::record_lifecycle_marker(
            &project_root,
            Some(&session_id),
            "hook-pre-compact",
            crate::core::brain_memory::LifecycleEventKind::PreCompact,
            "pre compact flush",
        );
        sync_memory_if_possible(&project_root, "pre_compact");
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

    if !project_root.is_empty() {
        let session_id = extract_first_json_field(&input, &["session_id", "sessionID"])
            .unwrap_or_else(|| "sessionless".to_string());
        let _ = crate::core::brain_memory::record_lifecycle_marker(
            &project_root,
            Some(&session_id),
            "hook-session-start",
            crate::core::brain_memory::LifecycleEventKind::SessionStart,
            &format!("session start {source}"),
        );
    }

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
/// Captures the raw prompt into the local journal for later fact extraction.
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

    let project_root = std::env::current_dir()
        .map(|p| p.to_string_lossy().to_string())
        .unwrap_or_default();
    if project_root.is_empty() {
        return;
    }
    let session_id = extract_first_json_field(&input, &["session_id", "sessionID"]);
    let source = extract_first_json_field(&input, &["source", "editor"])
        .unwrap_or_else(|| "hook".to_string());
    let _ = crate::core::brain_memory::record_user_turn(
        &project_root,
        session_id.as_deref(),
        &source,
        &trimmed,
    );
    sync_memory_if_possible(&project_root, "user_turn");
    emit_contextual_suggestions(&project_root, &trimmed);
}

/// AssistantOutputSubmit hook: fired by editor plugins when assistant text is
/// available from streaming message-part events.
pub fn handle_assistant_output_submit() {
    let Some(input) = read_stdin_string() else {
        return;
    };

    let message =
        extract_first_json_field(&input, &["message", "text", "response"]).unwrap_or_default();

    let trimmed = message.trim().to_string();
    if trimmed.is_empty() {
        return;
    }

    let is_system = trimmed.starts_with("<session_state")
        || trimmed.starts_with("<context_guidance>")
        || trimmed.starts_with("<system-reminder>")
        || trimmed.starts_with("<tool-result>");
    if is_system {
        return;
    }

    let project_root = std::env::current_dir()
        .map(|p| p.to_string_lossy().to_string())
        .unwrap_or_default();
    if project_root.is_empty() {
        return;
    }

    let session_id = extract_first_json_field(&input, &["session_id", "sessionID"]);
    let source = extract_first_json_field(&input, &["source", "editor"])
        .unwrap_or_else(|| "hook".to_string());
    let _ = crate::core::brain_memory::record_assistant_turn(
        &project_root,
        session_id.as_deref(),
        &source,
        &trimmed,
    );
    sync_memory_if_possible(&project_root, "assistant_output");
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
    let suggester = crate::core::contextual_surface::ContextualSuggester::from_env();
    let surfacing_query = session
        .as_ref()
        .and_then(|s| s.task.as_ref().map(|t| t.description.clone()))
        .unwrap_or_else(|| source.to_string());
    let surfacing_suggestions = suggester.suggest(&surfacing_query, &knowledge);

    if !has_session && surfacing_suggestions.is_empty() {
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

    // P5: Contextual suggestions take priority over the static wake-up briefing.
    if !surfacing_suggestions.is_empty() {
        parts.push(crate::core::contextual_surface::render_suggestions_block(
            &surfacing_suggestions,
        ));
    } else if let Some(hosted) = fetch_hosted_wakeup_briefing(project_root) {
        parts.push(format!(
            "<knowledge>\n{}\n</knowledge>",
            xml_escape(&hosted)
        ));
    } else {
        let high_confidence_facts: Vec<_> = knowledge
            .facts
            .iter()
            .filter(|f| f.is_current() && f.confidence >= 0.7)
            .collect();
        if !high_confidence_facts.is_empty() {
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
  - ctx_read / ctx_search / ctx_tree instead of Read / Grep / ls
  - ctx for hosted memory, graph, analytics, and agent actions
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

/// Builds a textual representation of a tool execution suitable for
/// relevance scoring. Stays well below MAX_CONTEXT_BYTES so callers can
/// pass it directly to the contextual suggester.
fn build_tool_context(tool_name: &str, command: Option<&str>, response: Option<&str>) -> String {
    let mut parts: Vec<String> = Vec::with_capacity(3);
    parts.push(tool_name.to_string());
    if let Some(cmd) = command {
        parts.push(cmd.to_string());
    }
    if let Some(resp) = response {
        let trimmed = resp.trim();
        if !trimmed.is_empty() {
            parts.push(
                crate::core::sanitize::telemetry_command_preview(trimmed)
                    .unwrap_or_else(|| trimmed.chars().take(200).collect()),
            );
        }
    }
    parts.join("\n")
}

/// Emits a `{"additionalContext": ...}` JSON line to stdout when the
/// contextual suggester has relevant knowledge to surface. No output when
/// surfacing is disabled, the project has no knowledge, or no fact clears
/// the configured threshold.
fn emit_contextual_suggestions(project_root: &str, query: &str) {
    if project_root.is_empty() || query.trim().is_empty() {
        return;
    }
    let knowledge = crate::core::knowledge::ProjectKnowledge::load_or_create(project_root);
    if knowledge.facts.is_empty() {
        return;
    }
    let suggester = crate::core::contextual_surface::ContextualSuggester::from_env();
    let suggestions = suggester.suggest(query, &knowledge);
    if let Some(json) =
        crate::core::contextual_surface::render_additional_context_json(&suggestions)
    {
        println!("{json}");
    }
}
/// extracts the tool name and rough token sizes, and fires a telemetry
/// event to the server. Wired to Claude Code `PostToolUse` and Copilot
/// CLI `postToolUse`.
pub fn handle_post_tool_use() {
    let Some(input) = read_stdin_string() else {
        return;
    };

    let tool_name = extract_tool_name(&input).unwrap_or_else(|| "unknown".to_string());
    let command = extract_command_from_hook_input(&input);
    let tool_response = extract_first_json_field(&input, &["tool_response", "tool_result"]);
    let session_id = extract_first_json_field(&input, &["session_id", "sessionID"]);
    let project_root = std::env::current_dir()
        .ok()
        .map(|dir| dir.to_string_lossy().to_string())
        .unwrap_or_default();
    if !project_root.is_empty() {
        let _ = crate::core::brain_memory::record_tool_activity(
            &project_root,
            session_id.as_deref(),
            "hook-post-tool-use",
            &tool_name,
            command.as_deref(),
            tool_response.as_deref(),
        );
    }

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

    let command_preview = command
        .as_deref()
        .and_then(crate::core::sanitize::telemetry_command_preview);
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

    emit_contextual_suggestions(
        &project_root,
        &build_tool_context(&tool_name, command.as_deref(), tool_response.as_deref()),
    );
}

/// Tool activity hook: records tool activity into the local journal without
/// emitting telemetry. Used by OpenCode, which already has separate token-savings
/// telemetry and does not share Claude/Copilot post-tool hook payloads.
pub fn handle_tool_activity() {
    let Some(input) = read_stdin_string() else {
        return;
    };

    let tool_name = extract_tool_name(&input).unwrap_or_else(|| "unknown".to_string());
    let command = extract_command_from_hook_input(&input);
    let tool_response =
        extract_first_json_field(&input, &["tool_response", "tool_result", "output"]);
    let session_id = extract_first_json_field(&input, &["session_id", "sessionID"]);
    let source = extract_first_json_field(&input, &["source", "editor"])
        .unwrap_or_else(|| "hook-tool-activity".to_string());
    let project_root = std::env::current_dir()
        .ok()
        .map(|dir| dir.to_string_lossy().to_string())
        .unwrap_or_default();
    if project_root.is_empty() {
        return;
    }

    let _ = crate::core::brain_memory::record_tool_activity(
        &project_root,
        session_id.as_deref(),
        &source,
        &tool_name,
        command.as_deref(),
        tool_response.as_deref(),
    );
    sync_memory_if_possible(&project_root, "tool_activity");
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
    fn blocked_copilot_parallel_reason_detects_deferred_nebuctx_tools() {
        let input = r#"{
            "tool_name": "multi_tool_use.parallel",
            "tool_input": {
                "tool_uses": [
                    {
                        "recipient_name": "mcp_nebuctx_ctx_read.mcp_nebuctx_ctx_read",
                        "parameters": { "path": "client/src/main.rs" }
                    },
                    {
                        "recipient_name": "mcp_nebuctx_ctx_search.mcp_nebuctx_ctx_search",
                        "parameters": { "pattern": "main" }
                    }
                ]
            }
        }"#;

        let reason = blocked_copilot_parallel_reason(input).expect("guard should trigger");
        assert!(reason.contains("multi_tool_use.parallel"));
        assert!(reason.contains("mcp_nebuctx_ctx_read"));
        assert!(reason.contains("ctx_read(target=\"files\", paths=[...])"));
    }

    #[test]
    fn blocked_copilot_parallel_reason_ignores_public_ctx_calls() {
        let input = r#"{
            "tool_name": "multi_tool_use.parallel",
            "tool_input": {
                "tool_uses": [
                    {
                        "recipient_name": "ctx_read",
                        "parameters": { "target": "files", "paths": ["a", "b"] }
                    },
                    {
                        "recipient_name": "ctx_search",
                        "parameters": { "pattern": "main" }
                    }
                ]
            }
        }"#;

        assert_eq!(blocked_copilot_parallel_reason(input), None);
    }

    #[test]
    fn blocked_copilot_parallel_reason_ignores_other_tools() {
        let input = r#"{
            "tool_name": "runInTerminal",
            "tool_input": {
                "command": "git status"
            }
        }"#;

        assert_eq!(blocked_copilot_parallel_reason(input), None);
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
