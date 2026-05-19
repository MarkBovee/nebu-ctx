use std::path::PathBuf;
use std::time::{SystemTime, UNIX_EPOCH};

use serde::{Deserialize, Serialize};

use crate::config_io;

const OUTBOX_DIR: &str = "sync/outbox";

#[derive(Debug, Clone, Serialize, Deserialize, PartialEq, Eq)]
#[serde(rename_all = "snake_case")]
pub enum OutboxOperationKind {
    TelemetryIngest,
    ServerToolCall,
    CodeIndexSync,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct OutboxEntry {
    pub id: String,
    pub kind: OutboxOperationKind,
    pub created_at: i64,
    pub attempts: u32,
    pub last_error: Option<String>,
    pub payload: serde_json::Value,
}

pub fn enqueue(kind: OutboxOperationKind, payload: serde_json::Value) -> Result<String, String> {
    let entry = OutboxEntry {
        id: generate_entry_id(),
        kind,
        created_at: now_unix_seconds(),
        attempts: 0,
        last_error: None,
        payload,
    };

    let path = entry_path(&entry.id)?;
    let json = serde_json::to_string_pretty(&entry).map_err(|e| e.to_string())?;
    config_io::write_atomic(&path, &json)?;
    Ok(entry.id)
}

pub fn load_entries() -> Result<Vec<OutboxEntry>, String> {
    let dir = outbox_dir()?;
    std::fs::create_dir_all(&dir).map_err(|e| e.to_string())?;

    let mut entries = Vec::new();
    for item in std::fs::read_dir(&dir).map_err(|e| e.to_string())? {
        let item = item.map_err(|e| e.to_string())?;
        let path = item.path();
        if path.extension().and_then(|s| s.to_str()) != Some("json") {
            continue;
        }

        let data = std::fs::read_to_string(&path).map_err(|e| e.to_string())?;
        let entry = serde_json::from_str::<OutboxEntry>(&data)
            .map_err(|e| format!("failed to parse outbox entry {}: {e}", path.display()))?;
        entries.push(entry);
    }

    entries.sort_by(|a, b| {
        a.created_at
            .cmp(&b.created_at)
            .then_with(|| a.id.cmp(&b.id))
    });
    Ok(entries)
}

pub fn mark_failed(entry: &OutboxEntry, error: &str) -> Result<(), String> {
    let mut updated = entry.clone();
    updated.attempts = updated.attempts.saturating_add(1);
    updated.last_error = Some(error.to_string());
    save_entry(&updated)
}

pub fn delete(id: &str) -> Result<(), String> {
    let path = entry_path(id)?;
    if path.exists() {
        std::fs::remove_file(&path).map_err(|e| e.to_string())?;
    }
    Ok(())
}

fn save_entry(entry: &OutboxEntry) -> Result<(), String> {
    let path = entry_path(&entry.id)?;
    let json = serde_json::to_string_pretty(entry).map_err(|e| e.to_string())?;
    config_io::write_atomic(&path, &json)
}

fn outbox_dir() -> Result<PathBuf, String> {
    Ok(crate::core::data_dir::nebu_ctx_data_dir()?.join(OUTBOX_DIR))
}

fn entry_path(id: &str) -> Result<PathBuf, String> {
    Ok(outbox_dir()?.join(format!("{id}.json")))
}

fn now_unix_seconds() -> i64 {
    SystemTime::now()
        .duration_since(UNIX_EPOCH)
        .map(|d| d.as_secs() as i64)
        .unwrap_or(0)
}

fn generate_entry_id() -> String {
    let nanos = SystemTime::now()
        .duration_since(UNIX_EPOCH)
        .map(|d| d.as_nanos())
        .unwrap_or(0);
    format!("outbox-{nanos}")
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn enqueue_and_load_roundtrip() {
        let _lock = crate::core::data_dir::test_env_lock();
        let tmp = tempfile::tempdir().unwrap();
        std::env::set_var("NEBU_CTX_DATA_DIR", tmp.path());

        enqueue(
            OutboxOperationKind::TelemetryIngest,
            serde_json::json!({"tool":"ctx_read"}),
        )
        .unwrap();
        let entries = load_entries().unwrap();

        assert_eq!(entries.len(), 1);
        assert_eq!(entries[0].kind, OutboxOperationKind::TelemetryIngest);
        assert_eq!(entries[0].payload["tool"], "ctx_read");
    }

    #[test]
    fn mark_failed_updates_attempts() {
        let _lock = crate::core::data_dir::test_env_lock();
        let tmp = tempfile::tempdir().unwrap();
        std::env::set_var("NEBU_CTX_DATA_DIR", tmp.path());

        let id = enqueue(
            OutboxOperationKind::ServerToolCall,
            serde_json::json!({"name":"ctx_brain"}),
        )
        .unwrap();
        let entry = load_entries()
            .unwrap()
            .into_iter()
            .find(|item| item.id == id)
            .unwrap();
        mark_failed(&entry, "offline").unwrap();

        let updated = load_entries()
            .unwrap()
            .into_iter()
            .find(|item| item.id == id)
            .unwrap();
        assert_eq!(updated.attempts, 1);
        assert_eq!(updated.last_error.as_deref(), Some("offline"));
    }
}
