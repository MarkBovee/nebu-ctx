pub fn resolve_portable_binary() -> String {
    let which_cmd = if cfg!(windows) { "where" } else { "which" };
    if let Ok(output) = std::process::Command::new(which_cmd)
        .arg("nebu-ctx")
        .stderr(std::process::Stdio::null())
        .output()
    {
        if output.status.success() {
            let path = first_discovered_path(&output.stdout);
            if !path.is_empty() {
                return sanitize_exe_path(path);
            }
        }
    }
    let path = std::env::current_exe()
        .map(|p| p.to_string_lossy().to_string())
        .unwrap_or_else(|_| "nebu-ctx".to_string());
    sanitize_exe_path(path)
}

fn sanitize_exe_path(path: String) -> String {
    path.trim_end_matches(" (deleted)").to_string()
}

fn first_discovered_path(stdout: &[u8]) -> String {
    String::from_utf8_lossy(stdout)
        .lines()
        .map(str::trim)
        .find(|line| !line.is_empty())
        .unwrap_or("")
        .to_string()
}

#[cfg(test)]
mod tests {
    use super::first_discovered_path;

    #[test]
    fn first_discovered_path_uses_first_non_empty_line() {
        let stdout = b"C:\\Projects\\Playground\\nebu-ctx\\client\\target\\debug\\nebu-ctx.exe\r\nC:\\Users\\nsmbo\\.cargo\\bin\\nebu-ctx.exe\r\n";
        assert_eq!(
            first_discovered_path(stdout),
            "C:\\Projects\\Playground\\nebu-ctx\\client\\target\\debug\\nebu-ctx.exe"
        );
    }
}
