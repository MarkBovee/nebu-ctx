use std::process::{Command, Output};

fn nebula_ctx_bin() -> Command {
    let mut cmd = Command::new(env!("CARGO_BIN_EXE_nebu-ctx"));
    cmd.current_dir(env!("CARGO_MANIFEST_DIR"));
    cmd.env("LEAN_CTX_ACTIVE", "1");
    cmd
}

fn fresh_install_stdio_start_output() -> Output {
    let temp = tempfile::tempdir().expect("tempdir");
    let home = temp.path().join("home");
    std::fs::create_dir_all(&home).expect("home dir");

    nebula_ctx_bin()
        .env("HOME", &home)
        .env("USERPROFILE", &home)
        .env_remove("NEBU_CTX_HOME")
        .output()
        .expect("run nebu-ctx stdio startup")
}

#[test]
fn binary_prints_version() {
    let output = nebula_ctx_bin()
        .arg("--version")
        .output()
        .expect("failed to run nebu-ctx");
    let stdout = String::from_utf8_lossy(&output.stdout);
    assert!(
        stdout.contains("nebu-ctx"),
        "version output should contain 'nebu-ctx', got: {stdout}"
    );
}

#[test]
fn binary_prints_help() {
    let output = nebula_ctx_bin()
        .arg("--help")
        .output()
        .expect("failed to run nebu-ctx");
    let stdout = String::from_utf8_lossy(&output.stdout);
    assert!(
        stdout.contains("Context Runtime"),
        "help should contain tagline"
    );
    assert!(stdout.contains("nebu-ctx"), "help should mention nebu-ctx");
}

#[test]
fn binary_read_file() {
    let output = nebula_ctx_bin()
        .args(["read", "Cargo.toml", "-m", "signatures"])
        .output()
        .expect("failed to run nebu-ctx");
    assert!(output.status.success(), "read should succeed");
}

#[test]
fn binary_config_shows_defaults() {
    let output = nebula_ctx_bin()
        .arg("config")
        .output()
        .expect("failed to run nebu-ctx");
    let stdout = String::from_utf8_lossy(&output.stdout);
    assert!(
        stdout.contains("checkpoint_interval"),
        "config should show checkpoint_interval"
    );
}

#[test]
fn gain_remains_available_as_local_cli_surface() {
    let output = nebula_ctx_bin()
        .arg("gain")
        .output()
        .expect("failed to run nebu-ctx gain");
    let stderr = String::from_utf8_lossy(&output.stderr);

    assert!(
        !stderr.contains("no longer available as a local client surface"),
        "gain should still run locally, got stderr: {stderr}"
    );
}

#[test]
fn shell_hook_compresses_echo() {
    let output = nebula_ctx_bin()
        .args(["-c", "echo", "hello", "world"])
        .output()
        .expect("failed to run nebu-ctx -c");
    let stdout = String::from_utf8_lossy(&output.stdout);
    assert!(
        stdout.contains("hello"),
        "shell hook should pass through echo output"
    );
}

#[test]
fn disabled_env_bypasses_compression() {
    let output = Command::new(env!("CARGO_BIN_EXE_nebu-ctx"))
        .current_dir(env!("CARGO_MANIFEST_DIR"))
        .env("LEAN_CTX_DISABLED", "1")
        .env("LEAN_CTX_COMPRESS", "1")
        .args(["-c", "echo", "passthrough test"])
        .output()
        .expect("failed to run nebu-ctx with LEAN_CTX_DISABLED");
    let stdout = String::from_utf8_lossy(&output.stdout);
    assert!(
        stdout.contains("passthrough"),
        "LEAN_CTX_DISABLED should pass output through unmodified"
    );
    assert!(
        !stdout.contains("[nebu-ctx:"),
        "LEAN_CTX_DISABLED should not add compression markers"
    );
}

#[test]
fn help_shows_environment_section() {
    let output = nebula_ctx_bin()
        .arg("--help")
        .output()
        .expect("failed to run nebu-ctx");
    let stdout = String::from_utf8_lossy(&output.stdout);
    assert!(
        stdout.contains("NEBU_CTX_DISABLED"),
        "help should document NEBU_CTX_DISABLED"
    );
    assert!(
        stdout.contains("NEBU_CTX_RAW"),
        "help should document NEBU_CTX_RAW"
    );
}

#[test]
fn help_no_longer_lists_bind_or_dashboard() {
    let output = nebula_ctx_bin()
        .arg("--help")
        .output()
        .expect("failed to run nebu-ctx");
    let stdout = String::from_utf8_lossy(&output.stdout);

    assert!(!stdout.contains("bind"), "help should not list bind: {stdout}");
    assert!(
        !stdout.contains("dashboard"),
        "help should not list dashboard: {stdout}"
    );
    assert!(!stdout.contains("watch"), "help should not list watch: {stdout}");
}

#[test]
fn help_prefers_connect_over_cloud_wording() {
    let output = nebula_ctx_bin()
        .arg("--help")
        .output()
        .expect("failed to run nebu-ctx");
    let stdout = String::from_utf8_lossy(&output.stdout);

    assert!(stdout.contains("connect"), "help should mention connect: {stdout}");
    assert!(
        !stdout.contains("CLOUD SERVER"),
        "help should not use cloud server heading: {stdout}"
    );
}

#[test]
fn mcp_stdio_start_without_saved_connection_prints_connect_instructions() {
    let output = fresh_install_stdio_start_output();

    let stderr = String::from_utf8_lossy(&output.stderr);

    assert!(
        !output.status.success(),
        "fresh install stdio startup should exit non-zero"
    );
    assert!(
        stderr.contains("nebu-ctx status"),
        "stderr should point to status, got: {stderr}"
    );
    assert!(
        stderr.contains("http://127.0.0.1:4242"),
        "stderr should include localhost example, got: {stderr}"
    );
    assert!(
        stderr.contains("http://192.168.1.50:4242"),
        "stderr should include LAN example, got: {stderr}"
    );
    assert!(
        !stderr.contains("serde error EOF"),
        "stderr should not leak codec noise, got: {stderr}"
    );
    assert!(
        !stderr.contains("initialize request"),
        "stderr should not leak MCP initialize noise, got: {stderr}"
    );
}

#[test]
fn mcp_stdio_start_message_mentions_token_and_host_port() {
    let output = fresh_install_stdio_start_output();

    let stderr = String::from_utf8_lossy(&output.stderr);

    assert!(
        !output.status.success(),
        "fresh install stdio startup should exit non-zero"
    );
    assert!(
        stderr.contains("--token <token>"),
        "missing token hint: {stderr}"
    );
    assert!(stderr.contains("Port 4242"), "missing port note: {stderr}");
}

// ── Pipe Guard Tests ────────────────────────────────────────

#[test]
fn pipe_guard_no_compression_when_stdout_is_piped() {
    if cfg!(windows) {
        return;
    }
    let output = Command::new(env!("CARGO_BIN_EXE_nebu-ctx"))
        .current_dir(env!("CARGO_MANIFEST_DIR"))
        .args(["-c", "echo hello world"])
        .output()
        .expect("failed to run nebu-ctx -c with piped stdout");
    let stdout = String::from_utf8_lossy(&output.stdout);
    assert_eq!(
        stdout.trim(),
        "hello world",
        "piped stdout must pass through raw output, got: {stdout}"
    );
}

#[test]
fn pipe_guard_force_compress_overrides_pipe_guard() {
    if cfg!(windows) {
        return;
    }
    let output = Command::new(env!("CARGO_BIN_EXE_nebu-ctx"))
        .current_dir(env!("CARGO_MANIFEST_DIR"))
        .env("LEAN_CTX_COMPRESS", "1")
        .args(["-c", "echo hello world"])
        .output()
        .expect("failed to run nebu-ctx -c with LEAN_CTX_COMPRESS");
    assert!(
        output.status.success(),
        "LEAN_CTX_COMPRESS should not crash even with piped stdout"
    );
    let stdout = String::from_utf8_lossy(&output.stdout);
    assert!(
        stdout.contains("hello"),
        "output should contain the echoed text"
    );
}

#[test]
fn pipe_guard_multiline_output_unchanged_when_piped() {
    if cfg!(windows) {
        return;
    }
    let script = "echo line1; echo line2; echo line3; echo 'result: 42'";
    let output = Command::new(env!("CARGO_BIN_EXE_nebu-ctx"))
        .current_dir(env!("CARGO_MANIFEST_DIR"))
        .args(["-c", script])
        .output()
        .expect("failed to run nebu-ctx -c with multiline output");
    let stdout = String::from_utf8_lossy(&output.stdout);
    assert!(stdout.contains("line1"), "must contain line1");
    assert!(stdout.contains("line2"), "must contain line2");
    assert!(stdout.contains("line3"), "must contain line3");
    assert!(
        stdout.contains("result: 42"),
        "must preserve exact output content"
    );
}

#[test]
fn pipe_guard_bash_hook_script_test() {
    if cfg!(windows) {
        return;
    }
    let binary = env!("CARGO_BIN_EXE_nebu-ctx");
    let script = format!(
        r#"
_lc() {{
    if [ -n "${{LEAN_CTX_DISABLED:-}}" ] || [ ! -t 1 ]; then
        command "$@"
        return
    fi
    '{binary}' -c "$@"
}}
# Pipe test: _lc echo should bypass nebu-ctx when piped
RESULT=$(_lc echo "pipe-guard-test-value")
echo "CAPTURED:$RESULT"
"#
    );
    let output = Command::new("bash")
        .args(["-c", &script])
        .output()
        .expect("failed to run bash pipe guard test");
    let stdout = String::from_utf8_lossy(&output.stdout);
    assert!(
        stdout.contains("CAPTURED:pipe-guard-test-value"),
        "pipe guard must bypass nebu-ctx in command substitution, got: {stdout}"
    );
}

#[test]
fn pipe_guard_bash_hook_pipe_to_sh() {
    if cfg!(windows) {
        return;
    }
    let binary = env!("CARGO_BIN_EXE_nebu-ctx");
    let script = format!(
        r#"
_lc() {{
    if [ -n "${{LEAN_CTX_DISABLED:-}}" ] || [ ! -t 1 ]; then
        command "$@"
        return
    fi
    '{binary}' -c "$@"
}}
# Simulate curl | sh: echo a script, pipe to sh
_lc echo 'echo INSTALL_SUCCESS' | sh
"#
    );
    let output = Command::new("bash")
        .args(["-c", &script])
        .output()
        .expect("failed to run bash pipe-to-sh test");
    let stdout = String::from_utf8_lossy(&output.stdout);
    assert!(
        stdout.contains("INSTALL_SUCCESS"),
        "piped script must execute correctly through sh, got: {stdout}"
    );
}

#[test]
fn pipe_guard_bash_hook_redirect_to_file() {
    if cfg!(windows) {
        return;
    }
    let binary = env!("CARGO_BIN_EXE_nebu-ctx");
    let tmp = std::env::temp_dir().join("nebu-ctx-pipe-guard-test.txt");
    let tmp_path = tmp.to_str().unwrap();
    let script = format!(
        r#"
_lc() {{
    if [ -n "${{LEAN_CTX_DISABLED:-}}" ] || [ ! -t 1 ]; then
        command "$@"
        return
    fi
    '{binary}' -c "$@"
}}
_lc echo "redirect-test-value" > {tmp_path}
cat {tmp_path}
rm -f {tmp_path}
"#
    );
    let output = Command::new("bash")
        .args(["-c", &script])
        .output()
        .expect("failed to run bash redirect test");
    let stdout = String::from_utf8_lossy(&output.stdout);
    assert!(
        stdout.contains("redirect-test-value"),
        "redirected output must be raw, got: {stdout}"
    );
}

#[test]
fn pipe_guard_rust_side_defense_in_depth() {
    if cfg!(windows) {
        return;
    }
    let script = "printf 'item_1: a\nitem_2: b\nitem_3: c\nitem_4: d\nitem_5: e\n'";
    let output = Command::new(env!("CARGO_BIN_EXE_nebu-ctx"))
        .current_dir(env!("CARGO_MANIFEST_DIR"))
        .args(["-c", script])
        .output()
        .expect("failed to run nebu-ctx -c");
    let stdout = String::from_utf8_lossy(&output.stdout);
    for i in 1..=5 {
        assert!(
            stdout.contains(&format!("item_{i}:")),
            "Rust-side pipe guard must pass through all lines unchanged (missing item_{i})\nstdout:\n{stdout}"
        );
    }
}
