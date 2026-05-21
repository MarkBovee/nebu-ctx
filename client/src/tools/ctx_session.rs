use crate::core::session::SessionState;

pub fn handle(
    session: &mut SessionState,
    action: &str,
    value: Option<&str>,
    session_id: Option<&str>,
) -> String {
    let scoped_project_root = current_project_root(session);

    match action {
        "status" => session.format_compact(),

        "load" => {
            let loaded = if let Some(root) = scoped_project_root.as_deref() {
                if let Some(id) = session_id {
                    SessionState::load_by_id_for_project_root(root, id)
                } else {
                    SessionState::load_latest_for_project_root(root)
                }
            } else {
                if let Some(id) = session_id {
                    SessionState::load_by_id(id)
                } else {
                    SessionState::load_latest()
                }
            };

            match loaded {
                Some(prev) => {
                    let summary = prev.format_compact();
                    *session = prev;
                    format!("Session loaded.\n{summary}")
                }
                None => {
                    let id_str = session_id.unwrap_or("latest");
                    format!("No session found (id: {id_str}). Starting fresh.")
                }
            }
        }

        "save" => {
            match session.save() {
                Ok(()) => format!("Session {} saved (v{}).", session.id, session.version),
                Err(e) => format!("Save failed: {e}"),
            }
        }

        "task" => {
            let desc = value.unwrap_or("(no description)");
            session.set_task(desc, None);
            format!("Task set: {desc}")
        }

        "finding" => {
            let summary = value.unwrap_or("(no summary)");
            let (file, line, text) = parse_finding_value(summary);
            session.add_finding(file.as_deref(), line, text);
            format!("Finding added: {summary}")
        }

        "decision" => {
            let desc = value.unwrap_or("(no description)");
            session.add_decision(desc, None);
            format!("Decision recorded: {desc}")
        }

        "reset" => {
            let _ = session.save();
            let old_id = session.id.clone();
            *session = SessionState::new();
            format!("Session reset. Previous: {old_id}. New: {}", session.id)
        }

        "list" => {
            let sessions = scoped_project_root
                .as_deref()
                .map(SessionState::list_sessions_for_project_root)
                .unwrap_or_else(SessionState::list_sessions);
            if sessions.is_empty() {
                return "No sessions found.".to_string();
            }
            let mut lines = vec![format!("Sessions ({}):", sessions.len())];
            for s in sessions.iter().take(10) {
                let task = s.task.as_deref().unwrap_or("(no task)");
                let task_short: String = task.chars().take(40).collect();
                lines.push(format!(
                    "  {} v{} | {} calls | {} tok | {}",
                    s.id, s.version, s.tool_calls, s.tokens_saved, task_short
                ));
            }
            if sessions.len() > 10 {
                lines.push(format!("  ... +{} more", sessions.len() - 10));
            }
            lines.join("\n")
        }

        "cleanup" => {
            let removed = scoped_project_root
                .as_deref()
                .map(|root| SessionState::cleanup_old_sessions_for_project_root(root, 7))
                .unwrap_or_else(|| SessionState::cleanup_old_sessions(7));
            format!("Cleaned up {removed} old session(s) (>7 days).")
        }

        "snapshot" => match session.save_compaction_snapshot() {
            Ok(snapshot) => {
                format!(
                    "Compaction snapshot saved ({} bytes).\n{snapshot}",
                    snapshot.len()
                )
            }
            Err(e) => format!("Snapshot failed: {e}"),
        },

        "restore" => {
            let snapshot = if let Some(root) = scoped_project_root.as_deref() {
                if let Some(id) = session_id {
                    SessionState::load_compaction_snapshot_for_project_root(root, id)
                } else {
                    SessionState::load_latest_snapshot_for_project_root(root)
                }
            } else {
                if let Some(id) = session_id {
                    SessionState::load_compaction_snapshot(id)
                } else {
                    SessionState::load_latest_snapshot()
                }
            };
            match snapshot {
                Some(s) => format!("Session restored from compaction snapshot:\n{s}"),
                None => "No compaction snapshot found. Session continues fresh.".to_string(),
            }
        }

        "resume" => session.build_resume_block(),

        _ => format!("Unknown action: {action}. Use: status, load, save, task, finding, decision, reset, list, cleanup, snapshot, restore, resume"),
    }
}

fn current_project_root(session: &SessionState) -> Option<String> {
    session
        .project_root
        .clone()
        .or_else(|| {
            session
                .shell_cwd
                .as_deref()
                .and_then(crate::core::protocol::detect_project_root)
        })
        .or_else(|| {
            std::env::current_dir()
                .ok()
                .and_then(|cwd| crate::core::protocol::detect_project_root(cwd.to_string_lossy().as_ref()))
        })
}

fn parse_finding_value(value: &str) -> (Option<String>, Option<u32>, &str) {
    // Format: "file.rs:42 — summary text" or just "summary text"
    if let Some(dash_pos) = value.find(" \u{2014} ").or_else(|| value.find(" - ")) {
        let location = &value[..dash_pos];
        let sep_len = 3;
        let text = &value[dash_pos + sep_len..];

        if let Some(colon_pos) = location.rfind(':') {
            let file = &location[..colon_pos];
            if let Ok(line) = location[colon_pos + 1..].parse::<u32>() {
                return (Some(file.to_string()), Some(line), text);
            }
        }
        return (Some(location.to_string()), None, text);
    }
    (None, None, value)
}
