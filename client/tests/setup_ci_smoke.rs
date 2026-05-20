use std::process::Command;

use nebu_ctx::core::setup_report::SetupReport;
use nebu_ctx::status::StatusReport;
use nebu_ctx::sync_cli::SyncReport;
use nebu_ctx::token_report::TokenReport;

#[test]
fn setup_ci_smoke_windows_packaging_keeps_rust_lld_override() {
    let config = std::fs::read_to_string(
        std::path::Path::new(env!("CARGO_MANIFEST_DIR")).join(".cargo/config.toml"),
    )
    .expect("read cargo config");

    assert!(
        config.contains("x86_64-pc-windows-msvc") && config.contains("rust-lld"),
        "client/.cargo/config.toml should keep the Windows rust-lld override"
    );
}

#[test]
fn sync_status_json_reports_outbox_items() {
    let _lock = nebu_ctx::core::data_dir::test_env_lock();
    let bin = env!("CARGO_BIN_EXE_nebu-ctx");
    let tmp = tempfile::tempdir().unwrap();
    let home = tmp.path().join("home");
    let data_dir = tmp.path().join("data");
    std::fs::create_dir_all(&home).unwrap();
    std::fs::create_dir_all(&data_dir).unwrap();

    let home_str = home.to_string_lossy().to_string();
    let data_str = data_dir.to_string_lossy().to_string();
    let envs = [
        ("HOME", home_str.as_str()),
        ("NEBU_CTX_DATA_DIR", data_str.as_str()),
    ];
    std::env::set_var("NEBU_CTX_DATA_DIR", data_str.as_str());

    nebu_ctx::core::sync_outbox::enqueue(
        nebu_ctx::core::sync_outbox::OutboxOperationKind::TelemetryIngest,
        serde_json::json!({"tool_name":"ctx_read"}),
    )
    .unwrap();

    let (code, out) = run_json(bin, &["sync", "status", "--json"], &envs);
    assert_eq!(code, 0, "sync status exit code");
    let report: SyncReport = serde_json::from_str(&out).expect("sync status JSON parse");
    assert_eq!(report.schema_version, 1);
    assert_eq!(report.before.queued, 1);
    assert_eq!(report.before.telemetry, 1);
    assert!(report.before.readable);
}

#[test]
fn setup_ci_smoke_install_docs_prefer_binstall_with_cargo_install_fallback() {
    let client_readme =
        std::fs::read_to_string(std::path::Path::new(env!("CARGO_MANIFEST_DIR")).join("README.md"))
            .expect("read client README");
    let root_readme = std::fs::read_to_string(
        std::path::Path::new(env!("CARGO_MANIFEST_DIR")).join("../README.md"),
    )
    .expect("read root README");

    fn install_section<'a>(readme: &'a str, start: &str, end: Option<&str>) -> &'a str {
        let start_index = readme.find(start).expect("install section start") + start.len();
        let rest = &readme[start_index..];

        if let Some(end_marker) = end {
            let end_index = rest.find(end_marker).expect("install section end");
            &rest[..end_index]
        } else {
            rest
        }
    }

    let client_install = install_section(
        client_readme.as_str(),
        "## Install",
        Some("## Local install from source"),
    );
    let root_install = install_section(
        root_readme.as_str(),
        "## Install And Run",
        Some("### 2. Start the host"),
    );

    for (name, readme) in [("client", client_install), ("root", root_install)] {
        assert!(
            readme.contains("cargo binstall nebu-ctx"),
            "{name} README should mention cargo binstall nebu-ctx"
        );
        assert!(
            readme.contains("cargo install nebu-ctx"),
            "{name} README should mention cargo install nebu-ctx"
        );
        assert!(
            readme.contains("cargo-binstall"),
            "{name} README should note that cargo binstall requires cargo-binstall"
        );
        assert!(
            readme.contains("If you do not have cargo-binstall yet"),
            "{name} README should include an explicit cargo-binstall prerequisite sentence"
        );
        assert!(
            readme.contains("https://github.com/cargo-bins/cargo-binstall"),
            "{name} README should include the cargo-binstall install URL"
        );
        assert!(
            readme.contains("release asset") || readme.contains("published release"),
            "{name} README should mention release assets or a published release fallback"
        );
        assert!(
            readme.contains("source build") || readme.contains("source-build"),
            "{name} README should describe cargo install as a source build path"
        );
        assert!(
            !readme.contains("cargo install cargo-binstall"),
            "{name} README should not tell users to cargo install cargo-binstall in the install section"
        );
    }
}

#[test]
fn setup_ci_smoke_windows_packaging_docs_still_mention_user_facing_promise() {
    let client_readme =
        std::fs::read_to_string(std::path::Path::new(env!("CARGO_MANIFEST_DIR")).join("README.md"))
            .expect("read client README");
    let root_readme = std::fs::read_to_string(
        std::path::Path::new(env!("CARGO_MANIFEST_DIR")).join("../README.md"),
    )
    .expect("read root README");

    let combined = format!("{client_readme}\n{root_readme}");
    assert!(
        combined.contains("rust-lld") && combined.contains("Visual Studio Build Tools"),
        "user-facing docs should still mention the Windows rust-lld / Visual Studio Build Tools packaging promise"
    );
}

#[test]
fn cargo_install_defaults_do_not_enable_http_server() {
    let manifest_path = std::path::Path::new(env!("CARGO_MANIFEST_DIR")).join("Cargo.toml");
    let manifest = std::fs::read_to_string(manifest_path).expect("read Cargo.toml");
    let default_line = manifest
        .lines()
        .find(|line| line.starts_with("default = "))
        .expect("default features line");

    assert!(
        !default_line.contains("http-server"),
        "cargo install should not enable http-server by default"
    );
}

#[test]
fn cargo_install_defaults_are_minimal() {
    let manifest_path = std::path::Path::new(env!("CARGO_MANIFEST_DIR")).join("Cargo.toml");
    let manifest = std::fs::read_to_string(manifest_path).expect("read Cargo.toml");
    assert!(manifest.contains("default = []"));
    assert!(!manifest.contains("default = [\"tree-sitter\"]"));
}

fn run_json(bin: &str, args: &[&str], envs: &[(&str, &str)]) -> (i32, String) {
    let mut cmd = Command::new(bin);
    cmd.args(args);
    for (k, v) in envs {
        cmd.env(k, v);
    }
    let out = cmd.output().expect("process start");
    let code = out.status.code().unwrap_or(1);
    let stdout = String::from_utf8_lossy(&out.stdout).to_string();
    (code, stdout)
}

fn write_exe(path: &std::path::Path, content: &str) {
    std::fs::write(path, content).expect("write");
    #[cfg(unix)]
    {
        use std::os::unix::fs::PermissionsExt;
        let mut perms = std::fs::metadata(path).unwrap().permissions();
        perms.set_mode(0o755);
        std::fs::set_permissions(path, perms).unwrap();
    }
}

#[test]
fn setup_bootstrap_doctor_status_json_smoke() {
    let bin = env!("CARGO_BIN_EXE_nebu-ctx");

    let tmp = tempfile::tempdir().unwrap();
    let home = tmp.path().join("home");
    std::fs::create_dir_all(&home).unwrap();
    let data_dir = tmp.path().join("data");
    std::fs::create_dir_all(&data_dir).unwrap();
    let bin_dir = tmp.path().join("bin");
    std::fs::create_dir_all(&bin_dir).unwrap();

    let home_str = home.to_string_lossy().to_string();
    let data_str = data_dir.to_string_lossy().to_string();

    // Fake claude binary so we can verify `claude mcp add-json` integration.
    // It writes stdin JSON to $HOME/claude-mcp.json and exits 0.
    let claude_path = bin_dir.join(if cfg!(windows) {
        "claude.cmd"
    } else {
        "claude"
    });
    if cfg!(windows) {
        write_exe(
            &claude_path,
            "@echo off\r\nset OUT=%HOME%\\claude-mcp.json\r\nmore > \"%OUT%\"\r\nexit /b 0\r\n",
        );
    } else {
        write_exe(
            &claude_path,
            "#!/bin/sh\nset -eu\nOUT=\"$HOME/claude-mcp.json\"\ncat > \"$OUT\"\nexit 0\n",
        );
    }

    let mut envs = vec![
        ("HOME", home_str.as_str()),
        ("NEBU_CTX_DATA_DIR", data_str.as_str()),
        ("NEBU_CTX_ACTIVE", "1"),
        ("NEBU_CTX_DISABLED", "1"),
    ];

    #[cfg(not(windows))]
    {
        envs.push(("SHELL", "/bin/bash"));
    }
    #[cfg(windows)]
    {
        envs.push(("USERPROFILE", home_str.as_str()));
    }

    // Prefer our fake claude first in PATH.
    let old_path = std::env::var("PATH").unwrap_or_default();
    let new_path = format!("{}:{}", bin_dir.to_string_lossy(), old_path);
    envs.push(("PATH", new_path.as_str()));

    // bootstrap --json returns clean JSON (SetupReport)
    let (code, out) = run_json(bin, &["bootstrap", "--json"], &envs);
    assert_eq!(code, 0, "bootstrap exit code");
    let setup: SetupReport = serde_json::from_str(&out).expect("bootstrap JSON parse");
    assert_eq!(setup.schema_version, 1);

    // bootstrap should create env.sh in NEBU_CTX_DATA_DIR for Docker/CI shells.
    let env_sh = data_dir.join("env.sh");
    let env_sh_content = std::fs::read_to_string(&env_sh).expect("env.sh exists");
    assert!(
        env_sh_content.contains("nebu-ctx docker self-heal"),
        "env.sh missing docker self-heal snippet"
    );

    // init --agent claude should prefer `claude mcp add-json` when available.
    let out = Command::new(bin)
        .args(["init", "--agent", "claude", "--global"])
        .envs(envs.iter().copied())
        .output()
        .expect("init --agent claude");
    assert!(out.status.success(), "init --agent claude exit");
    let saved = std::fs::read_to_string(home.join("claude-mcp.json")).expect("claude-mcp.json");
    let v: serde_json::Value = serde_json::from_str(&saved).expect("claude json parse");
    assert!(
        v.get("command").is_some(),
        "claude input should be server entry json"
    );

    // doctor --fix --json returns clean JSON (SetupReport shape)
    let (code, out) = run_json(bin, &["doctor", "--fix", "--json"], &envs);
    assert_eq!(code, 0, "doctor --fix exit code");
    let doctor_report: SetupReport = serde_json::from_str(&out).expect("doctor JSON parse");
    assert_eq!(doctor_report.schema_version, 1);

    // status --json returns clean JSON
    let (code, out) = run_json(bin, &["status", "--json"], &envs);
    assert_eq!(code, 0, "status exit code");
    let status: StatusReport = serde_json::from_str(&out).expect("status JSON parse");
    assert_eq!(status.schema_version, 1);
    assert!(status.sync_outbox.readable);
    assert_eq!(status.sync_outbox.queued, 0);

    // token-report --json returns clean JSON
    let (code, out) = run_json(bin, &["token-report", "--json"], &envs);
    assert_eq!(code, 0, "token-report exit code");
    let report: TokenReport = serde_json::from_str(&out).expect("token-report JSON parse");
    assert_eq!(report.schema_version, 1);
}

#[test]
fn bootstrap_configures_opencode_plugin_and_rules() {
    let bin = env!("CARGO_BIN_EXE_nebu-ctx");

    let tmp = tempfile::tempdir().unwrap();
    let home = tmp.path().join("home");
    std::fs::create_dir_all(home.join(".config/opencode")).unwrap();
    let data_dir = tmp.path().join("data");
    std::fs::create_dir_all(&data_dir).unwrap();

    let home_str = home.to_string_lossy().to_string();
    let data_str = data_dir.to_string_lossy().to_string();

    let mut envs = vec![
        ("HOME", home_str.as_str()),
        ("NEBU_CTX_DATA_DIR", data_str.as_str()),
        ("NEBU_CTX_ACTIVE", "1"),
        ("NEBU_CTX_DISABLED", "1"),
    ];
    #[cfg(not(windows))]
    {
        envs.push(("SHELL", "/bin/bash"));
    }
    #[cfg(windows)]
    {
        envs.push(("USERPROFILE", home_str.as_str()));
    }

    let (code, out) = run_json(bin, &["bootstrap", "--json"], &envs);
    assert_eq!(code, 0, "bootstrap exit code");
    let setup: SetupReport = serde_json::from_str(&out).expect("bootstrap JSON parse");
    assert_eq!(setup.schema_version, 1);

    let opencode_path = home.join(".config/opencode/opencode.json");
    let json: serde_json::Value =
        serde_json::from_str(&std::fs::read_to_string(&opencode_path).unwrap()).unwrap();
    assert_eq!(json["plugin"], serde_json::json!(["./plugins/nebu-ctx.ts"]));
    assert_eq!(json["instructions"], serde_json::json!(["./rules/nebu-ctx.md"]));
    assert_eq!(
        json["mcp"]["nebu-ctx"]["environment"]["NEBU_CTX_DATA_DIR"],
        serde_json::json!(data_str)
    );

    assert!(home.join(".config/opencode/plugins/nebu-ctx.ts").exists());
    assert!(home.join(".config/opencode/rules/nebu-ctx.md").exists());
}

#[test]
fn claude_config_dir_fallback_writes_dot_claude_json() {
    let bin = env!("CARGO_BIN_EXE_nebu-ctx");

    let tmp = tempfile::tempdir().unwrap();
    let home = tmp.path().join("home");
    std::fs::create_dir_all(&home).unwrap();
    let data_dir = tmp.path().join("data");
    std::fs::create_dir_all(&data_dir).unwrap();
    let bin_dir = tmp.path().join("bin");
    std::fs::create_dir_all(&bin_dir).unwrap();

    let claude_cfg = tmp.path().join("claude-cfg");
    std::fs::create_dir_all(&claude_cfg).unwrap();

    let home_str = home.to_string_lossy().to_string();
    let data_str = data_dir.to_string_lossy().to_string();
    let claude_cfg_str = claude_cfg.to_string_lossy().to_string();

    // Fake claude that fails (forces nebu-ctx to fallback to file merge/write).
    let claude_path = bin_dir.join(if cfg!(windows) {
        "claude.cmd"
    } else {
        "claude"
    });
    if cfg!(windows) {
        write_exe(&claude_path, "@echo off\r\nexit /b 1\r\n");
    } else {
        write_exe(&claude_path, "#!/bin/sh\nexit 1\n");
    }

    let mut envs = vec![
        ("HOME", home_str.as_str()),
        ("NEBU_CTX_DATA_DIR", data_str.as_str()),
        ("NEBU_CTX_ACTIVE", "1"),
        ("NEBU_CTX_DISABLED", "1"),
        ("CLAUDE_CONFIG_DIR", claude_cfg_str.as_str()),
    ];

    #[cfg(not(windows))]
    {
        envs.push(("SHELL", "/bin/bash"));
    }
    #[cfg(windows)]
    {
        envs.push(("USERPROFILE", home_str.as_str()));
    }

    let old_path = std::env::var("PATH").unwrap_or_default();
    let new_path = format!("{}:{}", bin_dir.to_string_lossy(), old_path);
    envs.push(("PATH", new_path.as_str()));

    let out = Command::new(bin)
        .args(["init", "--agent", "claude", "--global"])
        .envs(envs.iter().copied())
        .output()
        .expect("init --agent claude");
    assert!(out.status.success(), "init --agent claude exit");

    let cfg_path = claude_cfg.join(".claude.json");
    let content = std::fs::read_to_string(&cfg_path).expect(".claude.json exists");
    assert!(
        content.contains("\"mcpServers\""),
        "must contain mcpServers"
    );
    assert!(content.contains("nebu-ctx"), "must contain nebu-ctx entry");

    let out = Command::new(bin)
        .args(["doctor"])
        .envs(envs.iter().copied())
        .output()
        .expect("doctor");
    assert!(out.status.success(), "doctor exit");
    let stdout = String::from_utf8_lossy(&out.stdout);
    assert!(
        stdout.contains("MCP config") && stdout.contains("nebu-ctx found"),
        "doctor should report nebu-ctx found in MCP config; got:\n{stdout}"
    );
    assert!(
        stdout.contains("sync outbox"),
        "doctor should report current sync outbox status; got:\n{stdout}"
    );
    assert!(
        stdout.contains("Dashboard port 3333"),
        "doctor should report dashboard port status; got:\n{stdout}"
    );
    assert!(
        !stdout.contains("stats.json"),
        "doctor should not report stale stats.json status; got:\n{stdout}"
    );
    assert!(
        !stdout.contains("pi-lean-ctx not installed")
            && !stdout.contains("npm:pi-lean-ctx")
            && !stdout.contains(".claude/skills/lean-ctx"),
        "doctor should not recommend old package or skill names; got:\n{stdout}"
    );
}

#[test]
fn init_agent_preserves_agents_md_and_is_idempotent() {
    let bin = env!("CARGO_BIN_EXE_nebu-ctx");

    let tmp = tempfile::tempdir().unwrap();
    let home = tmp.path().join("home");
    std::fs::create_dir_all(&home).unwrap();
    let data_dir = tmp.path().join("data");
    std::fs::create_dir_all(&data_dir).unwrap();
    let bin_dir = tmp.path().join("bin");
    std::fs::create_dir_all(&bin_dir).unwrap();
    let project = tmp.path().join("project");
    std::fs::create_dir_all(&project).unwrap();

    // Create a git repo so project files are generated.
    std::fs::create_dir_all(project.join(".git")).unwrap();

    // Existing user AGENTS.md should be preserved.
    let agents_path = project.join("AGENTS.md");
    std::fs::write(&agents_path, "# My Agents\n\nDo not overwrite.\n").unwrap();

    let home_str = home.to_string_lossy().to_string();
    let data_str = data_dir.to_string_lossy().to_string();

    // Fake claude (success) so init --agent claude prefers `claude mcp add-json`.
    let claude_path = bin_dir.join(if cfg!(windows) {
        "claude.cmd"
    } else {
        "claude"
    });
    if cfg!(windows) {
        write_exe(&claude_path, "@echo off\r\nrem succeed\r\nexit /b 0\r\n");
    } else {
        write_exe(&claude_path, "#!/bin/sh\nexit 0\n");
    }

    let mut envs = vec![
        ("HOME", home_str.as_str()),
        ("NEBU_CTX_DATA_DIR", data_str.as_str()),
        ("NEBU_CTX_ACTIVE", "1"),
        ("NEBU_CTX_DISABLED", "1"),
    ];
    #[cfg(not(windows))]
    {
        envs.push(("SHELL", "/bin/bash"));
    }
    #[cfg(windows)]
    {
        envs.push(("USERPROFILE", home_str.as_str()));
    }

    let old_path = std::env::var("PATH").unwrap_or_default();
    let new_path = format!("{}:{}", bin_dir.to_string_lossy(), old_path);
    envs.push(("PATH", new_path.as_str()));

    for _ in 0..2 {
        let out = Command::new(bin)
            .args(["init", "--agent", "claude"])
            .current_dir(&project)
            .envs(envs.iter().copied())
            .output()
            .expect("init --agent claude");
        assert!(out.status.success(), "init --agent claude exit");
    }

    let agents = std::fs::read_to_string(&agents_path).unwrap();
    assert!(agents.contains("# My Agents"), "must preserve user header");
    assert!(
        agents.contains("Do not overwrite."),
        "must preserve user content"
    );
    assert!(
        agents.contains("<!-- nebu-ctx -->") && agents.contains("@LEAN-CTX.md"),
        "must add nebu-ctx reference block"
    );
    assert_eq!(
        agents.matches("<!-- nebu-ctx -->").count(),
        1,
        "must not duplicate marker block"
    );

    let nebula_ctx_md = project.join("LEAN-CTX.md");
    let nebula_ctx_content = std::fs::read_to_string(&nebula_ctx_md).expect("LEAN-CTX.md exists");
    assert!(
        nebula_ctx_content.contains("nebu-ctx — Context Engineering Layer"),
        "LEAN-CTX.md must contain rules"
    );
}

#[test]
fn init_claude_installs_dedicated_rules_file_without_claude_md() {
    let bin = env!("CARGO_BIN_EXE_nebu-ctx");

    let tmp = tempfile::tempdir().unwrap();
    let home = tmp.path().join("home");
    std::fs::create_dir_all(&home).unwrap();
    let data_dir = tmp.path().join("data");
    std::fs::create_dir_all(&data_dir).unwrap();
    let project = tmp.path().join("project");
    std::fs::create_dir_all(&project).unwrap();

    let home_str = home.to_string_lossy().to_string();
    let data_str = data_dir.to_string_lossy().to_string();

    let mut envs = vec![
        ("HOME", home_str.as_str()),
        ("NEBU_CTX_DATA_DIR", data_str.as_str()),
        ("NEBU_CTX_ACTIVE", "1"),
        ("NEBU_CTX_DISABLED", "1"),
    ];
    #[cfg(not(windows))]
    {
        envs.push(("SHELL", "/bin/bash"));
    }
    #[cfg(windows)]
    {
        envs.push(("USERPROFILE", home_str.as_str()));
    }

    let out = Command::new(bin)
        .args(["init", "--agent", "claude", "--global"])
        .current_dir(&project)
        .envs(envs.iter().copied())
        .output()
        .expect("init --agent claude --global");
    assert!(out.status.success(), "init --agent claude --global exit");

    let claude_md_path = home.join(".claude/CLAUDE.md");
    assert!(
        claude_md_path.exists(),
        "must create ~/.claude/CLAUDE.md with nebu-ctx block"
    );
    let claude_md = std::fs::read_to_string(&claude_md_path).expect("CLAUDE.md readable");
    assert!(
        claude_md.contains("<!-- nebu-ctx -->"),
        "CLAUDE.md must contain nebu-ctx marker block"
    );
    assert!(
        claude_md.contains("@rules/nebu-ctx.md"),
        "CLAUDE.md must import rules file"
    );

    assert!(
        !project.join("CLAUDE.md").exists(),
        "must not create project CLAUDE.md"
    );

    let rules_path = home.join(".claude/rules/nebu-ctx.md");
    assert!(
        rules_path.exists(),
        "must create dedicated Claude rules file"
    );
    let content = std::fs::read_to_string(&rules_path).expect("rules readable");
    assert!(
        content.contains("nebu-ctx-rules-"),
        "rules must contain marker"
    );
}
