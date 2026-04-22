use std::path::PathBuf;

pub fn nebula_ctx_data_dir() -> Result<PathBuf, String> {
    if let Ok(dir) = std::env::var("NEBULA_CTX_DATA_DIR") {
        let trimmed = dir.trim();
        if !trimmed.is_empty() {
            return Ok(PathBuf::from(trimmed));
        }
    }

    Ok(dirs::home_dir()
        .ok_or_else(|| "Cannot determine home directory".to_string())?
        .join(".nebu-ctx"))
}

pub fn test_env_lock() -> std::sync::MutexGuard<'static, ()> {
    use std::sync::{Mutex, OnceLock};
    static LOCK: OnceLock<Mutex<()>> = OnceLock::new();
    LOCK.get_or_init(|| Mutex::new(())).lock().unwrap()
}
