use chrono::{DateTime, Utc};
use serde::{Deserialize, Serialize};

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct SyncOutboxStatus {
    pub queued: usize,
    pub failed: usize,
    pub telemetry: usize,
    pub server_tool_calls: usize,
    pub path: String,
    pub readable: bool,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct SyncReport {
    pub schema_version: u32,
    pub generated_at: DateTime<Utc>,
    pub action: String,
    pub before: SyncOutboxStatus,
    pub after: Option<SyncOutboxStatus>,
    pub attempted: usize,
    pub warnings: Vec<String>,
}

pub fn run_cli(args: &[String]) -> i32 {
    let json = args.iter().any(|arg| arg == "--json");
    let help = args.iter().any(|arg| arg == "--help" || arg == "-h");
    let action = args
        .iter()
        .find(|arg| !arg.starts_with('-'))
        .map(|arg| arg.as_str())
        .unwrap_or("status");

    if help {
        println!("Usage:");
        println!("  nebu-ctx sync status [--json]");
        println!("  nebu-ctx sync flush [--json]");
        return 0;
    }

    match action {
        "status" => print_report(build_status_report("status", 0, None), json),
        "flush" | "replay" => {
            let before = build_outbox_status();
            let attempted = crate::core::telemetry_queue::flush_pending();
            let after = build_outbox_status();
            print_report(build_status_report("flush", attempted, Some((before, after))), json)
        }
        _ => {
            eprintln!("Unknown sync action: {action}");
            eprintln!("Usage: nebu-ctx sync status|flush [--json]");
            2
        }
    }
}

pub fn build_outbox_status() -> SyncOutboxStatus {
    let path = outbox_path();
    match crate::core::sync_outbox::load_entries() {
        Ok(entries) => SyncOutboxStatus {
            queued: entries.len(),
            failed: entries.iter().filter(|entry| entry.last_error.is_some()).count(),
            telemetry: entries
                .iter()
                .filter(|entry| entry.kind == crate::core::sync_outbox::OutboxOperationKind::TelemetryIngest)
                .count(),
            server_tool_calls: entries
                .iter()
                .filter(|entry| entry.kind == crate::core::sync_outbox::OutboxOperationKind::ServerToolCall)
                .count(),
            path: path.to_string_lossy().to_string(),
            readable: true,
        },
        Err(_) => SyncOutboxStatus {
            queued: 0,
            failed: 0,
            telemetry: 0,
            server_tool_calls: 0,
            path: path.to_string_lossy().to_string(),
            readable: false,
        },
    }
}

fn build_status_report(
    action: &str,
    attempted: usize,
    flush_statuses: Option<(SyncOutboxStatus, SyncOutboxStatus)>,
) -> SyncReport {
    let before = flush_statuses
        .as_ref()
        .map(|(before, _)| before.clone())
        .unwrap_or_else(build_outbox_status);
    let after = flush_statuses.map(|(_, after)| after);
    let mut warnings = Vec::new();
    if !before.readable || after.as_ref().is_some_and(|status| !status.readable) {
        warnings.push("sync outbox is not readable".to_string());
    }

    SyncReport {
        schema_version: 1,
        generated_at: Utc::now(),
        action: action.to_string(),
        before,
        after,
        attempted,
        warnings,
    }
}

fn print_report(report: SyncReport, json: bool) -> i32 {
    if json {
        println!("{}", serde_json::to_string_pretty(&report).unwrap_or_else(|_| "{}".to_string()));
    } else {
        print_human(&report);
    }

    if report.before.readable && report.after.as_ref().is_none_or(|status| status.readable) {
        0
    } else {
        1
    }
}

fn print_human(report: &SyncReport) {
    println!("nebu-ctx sync {}", report.action);
    println!(
        "  before: {} queued, {} failed ({} telemetry, {} server calls)",
        report.before.queued, report.before.failed, report.before.telemetry, report.before.server_tool_calls
    );
    if let Some(after) = &report.after {
        println!(
            "  after:  {} queued, {} failed (attempted {})",
            after.queued, after.failed, report.attempted
        );
    }
    println!("  outbox: {}", report.before.path);
}

fn outbox_path() -> std::path::PathBuf {
    crate::core::data_dir::nebu_ctx_data_dir()
        .map(|dir| dir.join("sync/outbox"))
        .unwrap_or_else(|_| std::path::PathBuf::from("sync/outbox"))
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn build_outbox_status_counts_kinds() {
        let _lock = crate::core::data_dir::test_env_lock();
        let tmp = tempfile::tempdir().unwrap();
        std::env::set_var("NEBU_CTX_DATA_DIR", tmp.path());

        crate::core::sync_outbox::enqueue(
            crate::core::sync_outbox::OutboxOperationKind::TelemetryIngest,
            serde_json::json!({"tool_name":"ctx_read"}),
        )
        .unwrap();
        crate::core::sync_outbox::enqueue(
            crate::core::sync_outbox::OutboxOperationKind::ServerToolCall,
            serde_json::json!({"tool_name":"ctx_brain"}),
        )
        .unwrap();

        let status = build_outbox_status();
        assert!(status.readable);
        assert_eq!(status.queued, 2);
        assert_eq!(status.telemetry, 1);
        assert_eq!(status.server_tool_calls, 1);
    }
}
