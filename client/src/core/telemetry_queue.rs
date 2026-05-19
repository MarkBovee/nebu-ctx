//! In-process async telemetry queue for the MCP server process.
//!
//! MCP tool calls enqueue events without blocking; a single background Tokio
//! task drains the channel and POSTs each event to the configured server on a
//! threadpool thread.
//!
//! Shell hooks run as short-lived separate processes that cannot share the
//! in-process channel. They use [`fire_sync`], which caps overhead at 300 ms
//! via a detached thread and a receive-timeout, so the user's shell command
//! never stalls perceptibly even when the server is unreachable.
//!
//! Unlike the earlier best-effort-only behavior, failed telemetry delivery is
//! now written to the local sync outbox so offline sessions can be replayed.

use std::sync::OnceLock;
use std::time::Duration;

use tokio::sync::mpsc::{self, UnboundedSender};

use crate::models::TelemetryIngestRequest;

static TX: OnceLock<UnboundedSender<TelemetryIngestRequest>> = OnceLock::new();

/// Enqueue a telemetry event for background delivery.
///
/// Returns immediately — no network I/O on the calling thread.
/// Events enqueued before [`start_drain_task`] is called are persisted so they
/// can be replayed when the runtime or server becomes available.
pub fn enqueue(request: TelemetryIngestRequest) {
    if let Some(tx) = TX.get() {
        // UnboundedSender::send only errors when the receiver is dropped,
        // which cannot happen while the drain task is running.
        let _ = tx.send(request);
        return;
    }

    let _ = persist_request(&request);
}

/// Spawn the background drain task inside the running Tokio runtime.
///
/// Must be called once at MCP server startup. Subsequent calls are no-ops;
/// the first call wins and installs the channel sender into [`TX`].
pub fn start_drain_task() {
    let (tx, mut rx) = mpsc::unbounded_channel::<TelemetryIngestRequest>();

    // OnceLock::set is atomic — only the first caller proceeds.
    if TX.set(tx).is_err() {
        return;
    }

    tokio::spawn(async move {
        drain_persisted();

        while let Some(req) = rx.recv().await {
            // Offload the blocking HTTP POST to the threadpool so the async
            // runtime is never stalled by network I/O.
            tokio::task::spawn_blocking(move || {
                if deliver_request(&req).is_err() {
                    let _ = persist_request(&req);
                }
            });
        }
    });
}

/// Send a telemetry event from a short-lived process such as a shell hook.
///
/// Spawns a thread for the HTTP call and waits at most 300 ms before
/// returning so the invoking process can exit promptly. If the server is
/// unreachable the event is queued locally instead of being dropped.
pub fn fire_sync(request: TelemetryIngestRequest) {
    let (done_tx, done_rx) = std::sync::mpsc::channel::<()>();
    std::thread::spawn(move || {
        if deliver_request(&request).is_err() {
            let _ = persist_request(&request);
        }
        let _ = done_tx.send(());
    });
    let _ = done_rx.recv_timeout(Duration::from_millis(300));
}

/// Attempts to flush every queued outbox item once.
/// Returns the number of entries that were pending before the flush attempt.
pub fn flush_pending() -> usize {
    let Ok(entries) = crate::core::sync_outbox::load_entries() else {
        return 0;
    };

    let count = entries.len();
    drain_persisted();
    count
}

fn deliver_request(request: &TelemetryIngestRequest) -> anyhow::Result<()> {
    let client = crate::server_client::ServerClient::load()?;
    client.ingest_telemetry(request)
}

fn persist_request(request: &TelemetryIngestRequest) -> Result<(), String> {
    crate::core::sync_outbox::enqueue(
        crate::core::sync_outbox::OutboxOperationKind::TelemetryIngest,
        serde_json::to_value(request).map_err(|e| e.to_string())?,
    )
    .map(|_| ())
}

fn drain_persisted() {
    let Ok(entries) = crate::core::sync_outbox::load_entries() else {
        return;
    };

    for entry in entries {
        let result = match entry.kind {
            crate::core::sync_outbox::OutboxOperationKind::TelemetryIngest => {
                serde_json::from_value::<TelemetryIngestRequest>(entry.payload.clone())
                    .map_err(anyhow::Error::from)
                    .and_then(|request| deliver_request(&request))
            }
            crate::core::sync_outbox::OutboxOperationKind::ServerToolCall => {
                crate::server_client::replay_queued_server_tool_call(entry.payload.clone())
            }
        };

        match result {
            Ok(()) => {
                let _ = crate::core::sync_outbox::delete(&entry.id);
            }
            Err(error) => {
                let _ = crate::core::sync_outbox::mark_failed(&entry, &error.to_string());
            }
        }
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn enqueue_persists_when_runtime_not_started() {
        let _lock = crate::core::data_dir::test_env_lock();
        let tmp = tempfile::tempdir().unwrap();
        std::env::set_var("NEBU_CTX_DATA_DIR", tmp.path());

        enqueue(TelemetryIngestRequest {
            tool_name: "ctx_read".to_string(),
            tokens_original: 10,
            tokens_saved: 2,
            duration_ms: 0,
            mode: Some("test".to_string()),
            repository_fingerprint: None,
            checkout_binding: None,
            project_slug: None,
        });

        let entries = crate::core::sync_outbox::load_entries().unwrap();
        assert_eq!(entries.len(), 1);
        assert_eq!(entries[0].kind, crate::core::sync_outbox::OutboxOperationKind::TelemetryIngest);
    }
}
