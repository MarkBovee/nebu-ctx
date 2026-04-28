//! In-process async telemetry queue for the MCP server process.
//!
//! MCP tool calls enqueue events without blocking; a single background Tokio
//! task drains the channel and POSTs each event to the cloud server on a
//! threadpool thread.
//!
//! Shell hooks run as short-lived separate processes that cannot share the
//! in-process channel. They use [`fire_sync`], which caps overhead at 300 ms
//! via a detached thread and a receive-timeout, so the user's shell command
//! never stalls perceptibly even when the server is unreachable.

use std::sync::OnceLock;
use std::time::Duration;

use tokio::sync::mpsc::{self, UnboundedSender};

use crate::models::TelemetryIngestRequest;

static TX: OnceLock<UnboundedSender<TelemetryIngestRequest>> = OnceLock::new();

/// Enqueue a telemetry event for background delivery.
///
/// Returns immediately — no network I/O on the calling thread.
/// Events enqueued before [`start_drain_task`] is called are silently dropped.
pub fn enqueue(request: TelemetryIngestRequest) {
    if let Some(tx) = TX.get() {
        // UnboundedSender::send only errors when the receiver is dropped,
        // which cannot happen while the drain task is running.
        let _ = tx.send(request);
    }
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
        while let Some(req) = rx.recv().await {
            // Offload the blocking HTTP POST to the threadpool so the async
            // runtime is never stalled by network I/O.
            tokio::task::spawn_blocking(move || {
                let Ok(client) = crate::cloud_client::ServerClient::load() else {
                    return;
                };
                let _ = client.ingest_telemetry(&req);
            });
        }
    });
}

/// Send a telemetry event from a short-lived process such as a shell hook.
///
/// Spawns a thread for the HTTP call and waits at most 300 ms before
/// returning so the invoking process can exit promptly. If the server is
/// unreachable the timeout fires and the event is silently dropped — telemetry
/// is best-effort and must never delay the user's shell commands.
pub fn fire_sync(request: TelemetryIngestRequest) {
    let (done_tx, done_rx) = std::sync::mpsc::channel::<()>();
    std::thread::spawn(move || {
        if let Ok(client) = crate::cloud_client::ServerClient::load() {
            let _ = client.ingest_telemetry(&request);
        }
        let _ = done_tx.send(());
    });
    let _ = done_rx.recv_timeout(Duration::from_millis(300));
}
