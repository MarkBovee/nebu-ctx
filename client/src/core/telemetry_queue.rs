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
            crate::core::sync_outbox::OutboxOperationKind::CodeIndexSync => {
                crate::server_client::replay_queued_index_sync(entry.payload.clone())
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
    use serde_json::Map;
    use std::io::{Read, Write};
    use std::net::TcpListener;
    use std::sync::mpsc;
    use std::time::Duration;

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
            command_preview: None,
        });

        let entries = crate::core::sync_outbox::load_entries().unwrap();
        assert_eq!(entries.len(), 1);
        assert_eq!(
            entries[0].kind,
            crate::core::sync_outbox::OutboxOperationKind::TelemetryIngest
        );
    }

    #[test]
    fn flush_pending_replays_all_outbox_kinds_to_server() {
        let _lock = crate::core::data_dir::test_env_lock();
        let tmp = tempfile::tempdir().unwrap();
        std::env::set_var("NEBU_CTX_DATA_DIR", tmp.path());
        std::env::set_var("NEBU_CTX_HOME", tmp.path().join("home"));

        let (endpoint, received_paths) = spawn_replay_server(4);
        crate::config::save_connection(&endpoint, "test-token").unwrap();
        let fixture_ids = enqueue_replay_fixtures(tmp.path());

        assert_eq!(flush_pending(), 3);
        wait_for_replayed_entries_to_clear(&fixture_ids);

        let mut paths = Vec::new();
        let required_paths = [
            "/v1/telemetry/ingest",
            "/v1/tools/call",
            "/v1/projects/resolve",
            "/v1/index/sync",
        ];
        while paths.len() < 16 {
            let path = received_paths.recv_timeout(Duration::from_secs(2)).unwrap();
            paths.push(path);
            if required_paths
                .iter()
                .all(|required| paths.iter().any(|path| path == required))
            {
                break;
            }
        }

        assert!(paths.contains(&"/v1/telemetry/ingest".to_string()));
        assert!(paths.contains(&"/v1/tools/call".to_string()));
        assert!(paths.contains(&"/v1/projects/resolve".to_string()));
        assert!(paths.contains(&"/v1/index/sync".to_string()));
    }

    fn wait_for_replayed_entries_to_clear(expected_ids: &[String]) {
        for _ in 0..20 {
            let entries = crate::core::sync_outbox::load_entries().unwrap();
            if expected_ids
                .iter()
                .all(|expected_id| entries.iter().all(|entry| &entry.id != expected_id))
            {
                return;
            }

            std::thread::sleep(Duration::from_millis(25));
        }

        let entries = crate::core::sync_outbox::load_entries().unwrap();
        panic!("expected replay fixture entries to clear after replay, found: {entries:?}");
    }

    fn enqueue_replay_fixtures(root: &std::path::Path) -> Vec<String> {
        let context = replay_project_context(root);
        let telemetry_id = crate::core::sync_outbox::enqueue(
            crate::core::sync_outbox::OutboxOperationKind::TelemetryIngest,
            serde_json::to_value(TelemetryIngestRequest {
                tool_name: "ctx_read".to_string(),
                tokens_original: 100,
                tokens_saved: 40,
                duration_ms: 7,
                mode: Some("test".to_string()),
                repository_fingerprint: Some(context.fingerprint.clone()),
                checkout_binding: Some(context.checkout_binding.clone()),
                project_slug: Some(context.project_slug.clone()),
                command_preview: None,
            })
            .unwrap(),
        )
        .unwrap();

        let tool_call_id = crate::core::sync_outbox::enqueue(
            crate::core::sync_outbox::OutboxOperationKind::ServerToolCall,
            serde_json::to_value(crate::server_client::QueuedServerToolCall {
                tool_name: "ctx_brain".to_string(),
                arguments: Map::from_iter([
                    ("action".to_string(), serde_json::json!("store")),
                    ("key".to_string(), serde_json::json!("session-test")),
                    ("value".to_string(), serde_json::json!("replayed")),
                ]),
                project_context: (&context).into(),
                operation_id: None,
            })
            .unwrap(),
        )
        .unwrap();

        let index_sync_id = crate::core::sync_outbox::enqueue(
            crate::core::sync_outbox::OutboxOperationKind::CodeIndexSync,
            serde_json::to_value(crate::server_client::QueuedIndexSync {
                project_context: (&context).into(),
                files: vec![crate::server_client::IndexSyncFile {
                    path: "src/lib.rs".to_string(),
                    hash: "abc".to_string(),
                    language: "rust".to_string(),
                    line_count: 8,
                    token_count: 30,
                    exports: vec!["run".to_string()],
                    summary: "library".to_string(),
                }],
                symbols: vec![crate::server_client::IndexSyncSymbol {
                    file_path: "src/lib.rs".to_string(),
                    name: "run".to_string(),
                    kind: "function".to_string(),
                    start_line: 1,
                    end_line: 3,
                    is_exported: true,
                }],
                edges: vec![crate::server_client::IndexSyncEdge {
                    from_symbol: "run".to_string(),
                    to_symbol: "helper".to_string(),
                    kind: "calls".to_string(),
                }],
            })
            .unwrap(),
        )
        .unwrap();

        vec![telemetry_id, tool_call_id, index_sync_id]
    }

    fn replay_project_context(root: &std::path::Path) -> crate::models::ProjectContext {
        crate::models::ProjectContext {
            project_slug: "sync-test".to_string(),
            project_root: root.to_string_lossy().to_string(),
            fingerprint: crate::models::RepositoryFingerprint {
                remote_url: Some("https://github.com/example/sync-test.git".to_string()),
                host: Some("github.com".to_string()),
                owner: Some("example".to_string()),
                repo_name: Some("sync-test".to_string()),
                default_branch: Some("main".to_string()),
            },
            checkout_binding: crate::models::CheckoutBinding::default(),
            project_metadata: None,
        }
    }

    fn spawn_replay_server(expected_requests: usize) -> (String, mpsc::Receiver<String>) {
        let listener = TcpListener::bind("127.0.0.1:0").unwrap();
        listener.set_nonblocking(true).unwrap();
        let endpoint = format!("http://{}", listener.local_addr().unwrap());
        let (tx, rx) = mpsc::channel();

        std::thread::spawn(move || {
            let mut served_requests = 0usize;
            let mut idle_after_expected_since = None;

            loop {
                match listener.accept() {
                    Ok((mut stream, _)) => {
                        idle_after_expected_since = None;
                        served_requests += 1;
                        stream
                            .set_read_timeout(Some(Duration::from_secs(2)))
                            .unwrap();
                        let path = read_request_path(&mut stream);
                        let body = response_body_for(&path);
                        let response = format!(
                            "HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nContent-Length: {}\r\nConnection: close\r\n\r\n{}",
                            body.len(),
                            body
                        );
                        let _ = stream.write_all(response.as_bytes());
                        let _ = tx.send(path);
                    }
                    Err(error) if error.kind() == std::io::ErrorKind::WouldBlock => {
                        if served_requests >= expected_requests {
                            let idle_since = idle_after_expected_since
                                .get_or_insert_with(std::time::Instant::now);
                            if idle_since.elapsed() >= Duration::from_millis(250) {
                                break;
                            }
                        }

                        std::thread::sleep(Duration::from_millis(10));
                    }
                    Err(_) => break,
                }
            }
        });

        (endpoint, rx)
    }

    fn read_request_path(stream: &mut std::net::TcpStream) -> String {
        let mut buffer = Vec::new();
        let mut chunk = [0u8; 512];
        loop {
            let Ok(read) = stream.read(&mut chunk) else {
                break;
            };
            if read == 0 {
                break;
            }
            buffer.extend_from_slice(&chunk[..read]);
            if request_complete(&buffer) {
                break;
            }
        }

        let request = String::from_utf8_lossy(&buffer);
        request
            .lines()
            .next()
            .and_then(|line| line.split_whitespace().nth(1))
            .unwrap_or("/")
            .to_string()
    }

    fn request_complete(buffer: &[u8]) -> bool {
        let Some(header_end) = buffer.windows(4).position(|window| window == b"\r\n\r\n") else {
            return false;
        };
        let headers = String::from_utf8_lossy(&buffer[..header_end]);
        let content_length = headers
            .lines()
            .find_map(|line| line.split_once(':'))
            .filter(|(name, _)| name.eq_ignore_ascii_case("content-length"))
            .and_then(|(_, value)| value.trim().parse::<usize>().ok())
            .unwrap_or(0);
        buffer.len() >= header_end + 4 + content_length
    }

    fn response_body_for(path: &str) -> &'static str {
        match path {
            "/v1/projects/resolve" => {
                r#"{"project":{"project_id":"proj_sync_test","slug":"sync-test","fingerprint":null,"created_at":"2026-01-01T00:00:00Z","updated_at":"2026-01-01T00:00:00Z"},"checkout_bound":true}"#
            }
            "/v1/tools/call" => r#"{"result":{"ok":true}}"#,
            "/v1/telemetry/ingest" | "/v1/index/sync" => r#"{"ok":true}"#,
            _ => r#"{"ok":true}"#,
        }
    }
}
