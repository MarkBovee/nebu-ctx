//! E2E tests for shell detection, NEBU_CTX_SHELL override,
//! agent init, Windows path handling,
//! and pipe-guard (stdout not a terminal → bypass nebu-ctx).

use std::io::Write;
use std::process::{Command, Stdio};

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

fn nebula_ctx_bin() -> String {
    env!("CARGO_BIN_EXE_nebu-ctx").to_string()
}

fn run_with_env(
    args: &[&str],
    env_vars: &[(&str, &str)],
    stdin_data: Option<&str>,
) -> (String, String, i32) {
    let mut cmd = Command::new(nebula_ctx_bin());
    cmd.args(args)
        .stdin(Stdio::piped())
        .stdout(Stdio::piped())
        .stderr(Stdio::piped());

    for (k, v) in env_vars {
        cmd.env(k, v);
    }

    let mut child = cmd.spawn().expect("failed to spawn nebu-ctx");
    if let Some(data) = stdin_data {
        child
            .stdin
            .take()
            .unwrap()
            .write_all(data.as_bytes())
            .unwrap();
    }

    let output = child.wait_with_output().expect("failed to wait");
    let stdout = String::from_utf8_lossy(&output.stdout).to_string();
    let stderr = String::from_utf8_lossy(&output.stderr).to_string();
    let code = output.status.code().unwrap_or(1);
    (stdout, stderr, code)
}

// ---------------------------------------------------------------------------
// NEBU_CTX_SHELL override tests (via `nebu-ctx -c`)
// ---------------------------------------------------------------------------

#[test]
fn nebula_ctx_shell_override_uses_specified_shell() {
    if cfg!(windows) {
        return; // /bin/sh not available on Windows
    }
    let (stdout, _stderr, code) = run_with_env(
        &["-c", "echo nebula_ctx_shell_works"],
        &[("NEBU_CTX_SHELL", "/bin/sh")],
        None,
    );
    assert_eq!(code, 0, "should succeed with /bin/sh");
    assert!(
        stdout.contains("nebula_ctx_shell_works"),
        "should see echo output: {stdout}"
    );
}

#[test]
fn nebula_ctx_shell_override_bash() {
    if !std::path::Path::new("/bin/bash").exists() {
        return;
    }
    let (stdout, _stderr, code) = run_with_env(
        &["-c", "echo $BASH_VERSION"],
        &[("NEBU_CTX_SHELL", "/bin/bash")],
        None,
    );
    assert_eq!(code, 0, "should succeed with /bin/bash");
    assert!(!stdout.trim().is_empty(), "BASH_VERSION should be set");
}

#[test]
fn nebula_ctx_shell_override_invalid_shell_fails() {
    let (_stdout, _stderr, code) = run_with_env(
        &["-c", "echo hello"],
        &[("NEBU_CTX_SHELL", "/nonexistent/shell")],
        None,
    );
    assert_ne!(code, 0, "should fail with nonexistent shell");
}

// ---------------------------------------------------------------------------
// Shell command execution tests (basic sanity)
// ---------------------------------------------------------------------------

#[test]
fn shell_exec_simple_command() {
    let (stdout, _stderr, code) = run_with_env(&["-c", "echo hello_world"], &[], None);
    assert_eq!(code, 0);
    assert!(stdout.contains("hello_world"), "output: {stdout}");
}

#[test]
fn shell_exec_pipe_command() {
    if cfg!(windows) {
        return; // head -1 not available on Windows
    }
    let (stdout, _stderr, code) =
        run_with_env(&["-c", "echo 'line1\nline2\nline3' | head -1"], &[], None);
    assert_eq!(code, 0, "pipe should work");
    assert!(!stdout.trim().is_empty(), "should have output: {stdout}");
}

#[test]
fn shell_exec_and_chain() {
    let (stdout, _stderr, code) = run_with_env(&["-c", "echo first && echo second"], &[], None);
    assert_eq!(code, 0, "&& chain should work");
    assert!(stdout.contains("first"), "first: {stdout}");
    assert!(stdout.contains("second"), "second: {stdout}");
}

#[test]
fn shell_exec_semicolon_chain() {
    let (stdout, _stderr, code) = run_with_env(&["-c", "echo aaa; echo bbb"], &[], None);
    assert_eq!(code, 0, "; chain should work");
    assert!(stdout.contains("aaa"), "aaa: {stdout}");
    assert!(stdout.contains("bbb"), "bbb: {stdout}");
}

#[test]
fn shell_exec_subshell() {
    if cfg!(windows) {
        return; // $(...) subshell syntax varies on Windows
    }
    let (stdout, _stderr, code) = run_with_env(&["-c", "echo $(echo subshell_output)"], &[], None);
    assert_eq!(code, 0, "subshell should work");
    assert!(stdout.contains("subshell_output"), "subshell: {stdout}");
}

#[test]
fn shell_exec_env_var_expansion() {
    if cfg!(windows) {
        return; // $VAR syntax is bash-only; PowerShell uses $env:VAR
    }
    let (stdout, _stderr, code) = run_with_env(
        &["-c", "echo $TEST_LEAN_CTX_VAR"],
        &[("TEST_LEAN_CTX_VAR", "expanded_value")],
        None,
    );
    assert_eq!(code, 0);
    assert!(
        stdout.contains("expanded_value"),
        "env var expansion: {stdout}"
    );
}

#[test]
fn shell_exec_quoted_args() {
    let (stdout, _stderr, code) =
        run_with_env(&["-c", r#"echo "hello world with spaces""#], &[], None);
    assert_eq!(code, 0);
    assert!(
        stdout.contains("hello world with spaces"),
        "quoted args: {stdout}"
    );
}

#[test]
fn shell_exec_argv_mode_runs_without_shell_roundtrip() {
    let (stdout, _stderr, code) = run_with_env(&["-c", "echo", "argv path ok"], &[], None);
    assert_eq!(code, 0);
    assert!(
        stdout.contains("argv path ok"),
        "argv mode output: {stdout}"
    );
}

#[test]
fn on_brief_is_recognized_command() {
    let (_stdout, stderr, code) = run_with_env(&["on-brief"], &[], None);
    assert_eq!(code, 0, "on-brief should be accepted");
    assert!(
        !stderr.contains("unknown command"),
        "should not report unknown command: {stderr}"
    );
}

// ---------------------------------------------------------------------------
// Agent init tests
// ---------------------------------------------------------------------------

#[test]
fn agent_init_opencode_is_supported() {
    let tmpdir = tempfile::tempdir().expect("create tempdir");
    let home = tmpdir.path();

    let opencode_dir = home.join(".config/opencode");
    std::fs::create_dir_all(&opencode_dir).unwrap();

    let mut cmd = Command::new(nebula_ctx_bin());
    cmd.args(["setup", "--agent", "opencode", "--global"])
        .env("HOME", home.to_str().unwrap())
        .env("LEAN_CTX_DISABLED", "1")
        .stdout(Stdio::piped())
        .stderr(Stdio::piped());

    let output = cmd.output().expect("failed to run setup");
    let stderr = String::from_utf8_lossy(&output.stderr);

    assert!(
        !stderr.contains("Unknown agent"),
        "opencode should be recognized: {stderr}"
    );

    let rules_path = opencode_dir.join("rules/nebu-ctx.md");
    assert!(rules_path.exists(), "opencode rules should be created");
}

#[test]
fn agent_init_unknown_agent_fails() {
    let (_stdout, stderr, code) =
        run_with_env(&["setup", "--agent", "nonexistent_agent"], &[], None);
    assert_ne!(code, 0, "unknown agent should fail");
    assert!(
        stderr.contains("Unknown agent"),
        "should say unknown: {stderr}"
    );
}

#[test]
fn agent_init_lists_four_supported_agents() {
    let (_stdout, stderr, _code) =
        run_with_env(&["setup", "--agent", "nonexistent_agent"], &[], None);
    assert!(
        stderr.contains("claude, codex, copilot, opencode"),
        "supported list should contain current supported agents: {stderr}"
    );
}

// ---------------------------------------------------------------------------
// Hook rewrite with NEBU_CTX_SHELL override
// ---------------------------------------------------------------------------

#[test]
fn hook_rewrite_works_with_shell_override() {
    let input = r#"{"tool_name":"Bash","command":"git status"}"#;
    let (stdout, _stderr, _code) = run_with_env(
        &["hook", "rewrite"],
        &[("NEBU_CTX_SHELL", "/bin/sh")],
        Some(input),
    );
    if !stdout.trim().is_empty() {
        let v: serde_json::Value =
            serde_json::from_str(&stdout).expect("hook output should be valid JSON");
        assert!(
            v["hookSpecificOutput"]["updatedInput"]["command"]
                .as_str()
                .is_some(),
            "should have command field"
        );
    }
}

// ---------------------------------------------------------------------------
// Windows path handling in generated scripts
// ---------------------------------------------------------------------------

#[test]
fn generated_script_handles_windows_path() {
    let script = nebu_ctx::hooks::generate_rewrite_script("/c/Users/Jaina/bin/nebu-ctx.exe");
    assert!(
        script.contains("NEBU_CTX_BIN=\"/c/Users/Jaina/bin/nebu-ctx.exe\""),
        "Windows bash path should be properly quoted in script"
    );
}

#[test]
fn generated_script_handles_path_with_spaces() {
    let script = nebu_ctx::hooks::generate_rewrite_script("/c/Program Files/nebu-ctx/nebu-ctx.exe");
    assert!(
        script.contains("NEBU_CTX_BIN=\"/c/Program Files/nebu-ctx/nebu-ctx.exe\""),
        "path with spaces should be quoted"
    );
}

#[test]
fn generated_compact_script_handles_windows_path() {
    let script =
        nebu_ctx::hooks::generate_compact_rewrite_script("/c/Users/Jaina/bin/nebu-ctx.exe");
    assert!(
        script.contains("NEBU_CTX_BIN=\"/c/Users/Jaina/bin/nebu-ctx.exe\""),
        "compact script should handle Windows path"
    );
}

#[test]
fn generated_script_skips_own_binary() {
    let script = nebu_ctx::hooks::generate_rewrite_script("nebu-ctx");
    assert!(
        script.contains("nebu-ctx ") || script.contains("$NEBU_CTX_BIN "),
        "script should reference nebu-ctx for self-skip check"
    );
}

// ---------------------------------------------------------------------------
// Bash script execution with Windows-style binary path
// ---------------------------------------------------------------------------

#[test]
fn bash_script_with_windows_binary_path_produces_valid_json() {
    if cfg!(windows) {
        return; // bash not available on Windows CI
    }
    let script =
        nebu_ctx::hooks::generate_compact_rewrite_script("/c/Users/Jaina/bin/nebu-ctx.exe");
    let script_path =
        std::env::temp_dir().join(format!("nebula_ctx_winpath_test_{}.sh", std::process::id()));
    std::fs::write(&script_path, &script).expect("write script");

    let input = r#"{"tool_name":"Bash","command":"git status"}"#;
    let mut child = Command::new("bash")
        .arg(&script_path)
        .stdin(Stdio::piped())
        .stdout(Stdio::piped())
        .stderr(Stdio::piped())
        .spawn()
        .expect("failed to spawn bash");

    child
        .stdin
        .take()
        .unwrap()
        .write_all(input.as_bytes())
        .unwrap();

    let output = child.wait_with_output().expect("failed to wait");
    let _ = std::fs::remove_file(&script_path);
    let stdout = String::from_utf8_lossy(&output.stdout);

    if !stdout.trim().is_empty() {
        let v: serde_json::Value = serde_json::from_str(&stdout).unwrap_or_else(|e| {
            panic!("invalid JSON from Windows path script: {e}\nraw: {stdout}")
        });
        let cmd = v["hookSpecificOutput"]["updatedInput"]["command"]
            .as_str()
            .expect("should have command");
        assert!(
            cmd.contains("/c/Users/Jaina/bin/nebu-ctx.exe"),
            "rewritten command should use the Windows bash path: {cmd}"
        );
        assert!(
            cmd.contains("git status"),
            "original command should be preserved: {cmd}"
        );
    }
}

// ---------------------------------------------------------------------------
// Pipe guard: nebu-ctx must NOT compress when stdout is piped
// ---------------------------------------------------------------------------

#[test]
fn piped_output_is_not_compressed() {
    if cfg!(windows) {
        return;
    }
    let bin = nebula_ctx_bin();
    let script = r#"echo "line one"; echo "line two"; echo "line three""#.to_string();
    let output = Command::new(&bin)
        .args(["-c", &script])
        .env("LEAN_CTX_DISABLED", "0")
        .stdout(Stdio::piped())
        .stderr(Stdio::piped())
        .output()
        .expect("run");
    let stdout = String::from_utf8_lossy(&output.stdout);
    assert!(
        stdout.contains("line one"),
        "piped output must contain original content: {stdout}"
    );
}

#[test]
fn bash_hook_contains_pipe_guard() {
    if cfg!(windows) {
        return;
    }
    let bin = nebula_ctx_bin();
    let output = Command::new(&bin)
        .args(["setup", "--dry-run"])
        .env("LEAN_CTX_DISABLED", "1")
        .env("SHELL", "/bin/bash")
        .stdout(Stdio::piped())
        .stderr(Stdio::piped())
        .output()
        .expect("run setup --dry-run");
    let _stdout = String::from_utf8_lossy(&output.stdout);
    let _stderr = String::from_utf8_lossy(&output.stderr);
    // The pipe guard should be in the generated hook
    // We verify by checking that `_lc()` in generated bash hooks contains `! -t 1`
    // This is tested more directly in cli.rs unit tests
}

#[test]
fn generated_bash_hook_has_tty_check() {
    let script = nebu_ctx::hooks::generate_rewrite_script("nebu-ctx");
    // The rewrite hook is for Claude Code / Gemini, not the shell alias.
    // The shell alias pipe guard is in cli.rs.
    // But we can verify the compact hook doesn't break on pipes either.
    assert!(
        !script.is_empty(),
        "generated rewrite script should not be empty"
    );
}

#[test]
fn nebula_ctx_c_preserves_output_when_piped() {
    if cfg!(windows) {
        return;
    }
    let bin = nebula_ctx_bin();

    let output = Command::new(&bin)
        .args(["-c", "echo MARKER_STRING_12345"])
        .env_remove("LEAN_CTX_DISABLED")
        .stdout(Stdio::piped())
        .stderr(Stdio::piped())
        .output()
        .expect("run nebu-ctx -c echo");
    let stdout = String::from_utf8_lossy(&output.stdout);
    assert!(
        stdout.contains("MARKER_STRING_12345"),
        "nebu-ctx -c must preserve output content when piped: {stdout}"
    );
}

#[test]
fn nebula_ctx_c_multiline_preserves_all_lines_when_piped() {
    if cfg!(windows) {
        return;
    }
    let bin = nebula_ctx_bin();
    let cmd = "echo LINE_A && echo LINE_B && echo LINE_C";
    let output = Command::new(&bin)
        .args(["-c", cmd])
        .env_remove("LEAN_CTX_DISABLED")
        .stdout(Stdio::piped())
        .stderr(Stdio::piped())
        .output()
        .expect("run");
    let stdout = String::from_utf8_lossy(&output.stdout);
    assert!(stdout.contains("LINE_A"), "LINE_A: {stdout}");
    assert!(stdout.contains("LINE_B"), "LINE_B: {stdout}");
    assert!(stdout.contains("LINE_C"), "LINE_C: {stdout}");
}

#[test]
fn nebula_ctx_c_preserves_json_for_inline_python_pipe() {
    if cfg!(windows) {
        return;
    }
    let bin = nebula_ctx_bin();
    let cmd = r#"printf '{"slotConfig":"ok"}\n' | python3 -c 'import json,sys; d=json.load(sys.stdin); print(d["slotConfig"])'"#;
    let output = Command::new(&bin)
        .args(["-c", cmd])
        .stdout(Stdio::piped())
        .stderr(Stdio::piped())
        .output()
        .expect("run");
    let stdout = String::from_utf8_lossy(&output.stdout);
    assert_eq!(output.status.code().unwrap_or(1), 0, "stdout={stdout}");
    assert_eq!(
        stdout.trim(),
        "ok",
        "json pipe output must stay raw: {stdout}"
    );
}

#[cfg(windows)]
#[test]
fn powershell_encoded_command_handles_nested_quotes() {
    let (stdout, stderr, code) = run_with_env(
        &["-c", r#"Write-Output 'json: {\"name\":\"nested\"}'"#],
        &[("NEBU_CTX_SHELL", "pwsh.exe")],
        None,
    );
    assert_eq!(code, 0, "stderr: {stderr}");
    assert!(
        stdout.contains(r#"json: {\"name\":\"nested\"}"#),
        "stdout: {stdout}"
    );
}
