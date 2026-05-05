# Fresh Install MCP Startup And Nebu-ctx Rename Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make fresh installs fail cleanly with actionable host connection instructions instead of MCP EOF noise, and remove the remaining active `lean-ctx` naming debt from the Rust client.

**Architecture:** Add a small preflight gate in the stdio MCP startup path so the client checks for saved host configuration before transport startup. Keep the rename cleanup focused on active source/test code and user-facing strings so the codebase no longer leaks `lean_ctx` or `lean-ctx` in normal runtime behavior.

**Tech Stack:** Rust, Cargo integration tests, CLI process tests, Markdown spec/plan docs

---

## File Map

- Modify: `client/src/cli/dispatch.rs`
  - Add the stdio MCP startup preflight gate and the instructional stderr message.
- Modify: `client/src/config.rs`
  - Optionally add a tiny helper if startup messaging benefits from reusing connection-state checks.
- Modify: `client/tests/integration_tests.rs`
  - Add focused process tests for the unconfigured startup failure mode and message contents.
- Modify: `client/Cargo.toml`
  - Rename the Rust library crate away from `lean_ctx`.
- Modify: `client/src/main.rs`
  - Update the binary entrypoint to use the new crate name.
- Modify: `client/tests/setup_ci_smoke.rs`
  - Update imports to the new crate name.
- Modify: `client/src/tools/mod.rs`
  - Rename `LeanCtxServer` and its constructor/return references.
- Modify: `client/src/mcp_server/dispatch.rs`
  - Update imports and impl target for the renamed server type.
- Modify: `client/src/bin/seed_observatory.rs`
  - Update imports from `lean_ctx` to the new crate name.
- Modify: `client/src/hook_handlers.rs`
  - Replace user-facing `lean-ctx` messaging and test literals with `nebu-ctx` where they describe the current product binary.
- Modify: `README.md`
  - Remove the remaining active `lean-ctx` product reference.
- Modify: `client/README.md`
  - Remove the remaining active `lean-ctx` product reference.

### Task 1: Add a failing test for unconfigured stdio MCP startup

**Files:**
- Modify: `client/tests/integration_tests.rs`
- Test: `client/tests/integration_tests.rs`

- [ ] **Step 1: Write the failing test**

Add this test to `client/tests/integration_tests.rs`:

```rust
#[test]
fn mcp_stdio_start_without_saved_connection_prints_connect_instructions() {
    use std::process::Command;

    let temp = tempfile::tempdir().expect("tempdir");
    let home = temp.path().join("home");
    std::fs::create_dir_all(&home).expect("home dir");

    let output = Command::new(env!("CARGO_BIN_EXE_nebu-ctx"))
        .env("HOME", &home)
        .env("USERPROFILE", &home)
        .env_remove("NEBU_CTX_HOME")
        .output()
        .expect("run nebu-ctx stdio startup");

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
}
```

- [ ] **Step 2: Run the focused test to verify it fails**

Run: `cargo test --manifest-path client/Cargo.toml mcp_stdio_start_without_saved_connection_prints_connect_instructions -- --exact`

Expected: FAIL because current startup emits low-level MCP/EOF behavior instead of the instructional message.

- [ ] **Step 3: Add a second failing assertion for the noise we want gone**

Extend the same test with:

```rust
    assert!(
        !stderr.contains("serde error EOF"),
        "stderr should not leak codec noise, got: {stderr}"
    );
    assert!(
        !stderr.contains("initialize request"),
        "stderr should not leak MCP initialize noise, got: {stderr}"
    );
```

- [ ] **Step 4: Re-run the focused test to verify it still fails for the right reason**

Run: `cargo test --manifest-path client/Cargo.toml mcp_stdio_start_without_saved_connection_prints_connect_instructions -- --exact`

Expected: FAIL because the current stderr still contains the old startup failure mode.

- [ ] **Step 5: Commit**

```bash
git add client/tests/integration_tests.rs
git commit -m "test: capture fresh install stdio startup guidance"
```

### Task 2: Add the stdio MCP startup preflight gate

**Files:**
- Modify: `client/src/cli/dispatch.rs:763-794`
- Modify: `client/src/config.rs:39-89`
- Test: `client/tests/integration_tests.rs`

- [ ] **Step 1: Implement the minimal startup preflight helper**

Add this helper near `run_mcp_server()` in `client/src/cli/dispatch.rs`:

```rust
fn ensure_mcp_host_connection_configured() -> Result<()> {
    if crate::config::load_connection()?.is_some() {
        return Ok(());
    }

    anyhow::bail!(
        "nebu-ctx host connection is not configured yet.\n\
Run: nebu-ctx status\n\
Connect to a local host:   nebu-ctx connect --endpoint http://127.0.0.1:4242 --token <token>\n\
Connect to a network host: nebu-ctx connect --endpoint http://192.168.1.50:4242 --token <token>\n\
Port 4242 is the MCP/host port. After connecting, retry your editor or agent."
    );
}
```

- [ ] **Step 2: Call the preflight before transport startup**

Update `run_mcp_server()` in `client/src/cli/dispatch.rs` to this shape:

```rust
fn run_mcp_server() -> Result<()> {
    use rmcp::ServiceExt;
    use tracing_subscriber::EnvFilter;

    std::env::set_var("NEBU_CTX_MCP_SERVER", "1");
    ensure_mcp_host_connection_configured()?;

    let rt = tokio::runtime::Runtime::new()?;
    rt.block_on(async {
        tracing_subscriber::fmt()
            .with_env_filter(EnvFilter::from_default_env())
            .with_writer(std::io::stderr)
            .init();

        tracing::info!(
            "nebu-ctx v{} MCP server starting",
            env!("CARGO_PKG_VERSION")
        );

        let server = tools::create_server();
        core::telemetry_queue::start_drain_task();
        let transport =
            mcp_stdio::HybridStdioTransport::new_server(tokio::io::stdin(), tokio::io::stdout());
        let service = server.serve(transport).await?;
        service.waiting().await?;

        core::stats::flush();
        core::mode_predictor::ModePredictor::flush();
        core::feedback::FeedbackStore::flush();

        Ok(())
    })
}
```

- [ ] **Step 3: Keep config reuse minimal**

Do not add a new config file or state model. If needed, add only this helper to `client/src/config.rs`:

```rust
pub fn has_saved_connection() -> Result<bool> {
    Ok(load_connection()?.is_some())
}
```

Then use it from `dispatch.rs` instead of calling `load_connection()` directly.

- [ ] **Step 4: Run the focused test to verify it passes**

Run: `cargo test --manifest-path client/Cargo.toml mcp_stdio_start_without_saved_connection_prints_connect_instructions -- --exact`

Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add client/src/cli/dispatch.rs client/src/config.rs client/tests/integration_tests.rs
git commit -m "fix: gate stdio startup on host connection"
```

### Task 3: Add a second focused test for message wording

**Files:**
- Modify: `client/tests/integration_tests.rs`
- Test: `client/tests/integration_tests.rs`

- [ ] **Step 1: Write the failing wording test**

Add this test to `client/tests/integration_tests.rs`:

```rust
#[test]
fn mcp_stdio_start_message_mentions_token_and_host_port() {
    use std::process::Command;

    let temp = tempfile::tempdir().expect("tempdir");
    let home = temp.path().join("home");
    std::fs::create_dir_all(&home).expect("home dir");

    let output = Command::new(env!("CARGO_BIN_EXE_nebu-ctx"))
        .env("HOME", &home)
        .env("USERPROFILE", &home)
        .output()
        .expect("run nebu-ctx stdio startup");

    let stderr = String::from_utf8_lossy(&output.stderr);

    assert!(stderr.contains("--token <token>"), "missing token hint: {stderr}");
    assert!(stderr.contains("Port 4242"), "missing port note: {stderr}");
}
```

- [ ] **Step 2: Run the focused test to verify it fails if the message drifted**

Run: `cargo test --manifest-path client/Cargo.toml mcp_stdio_start_message_mentions_token_and_host_port -- --exact`

Expected: If Task 2 used the exact message above, this likely PASSes immediately. If it does, keep it as the regression test and proceed. If it FAILs, adjust the stderr wording minimally until it PASSes.

- [ ] **Step 3: Re-run both startup tests together**

Run: `cargo test --manifest-path client/Cargo.toml mcp_stdio_start_ -- --nocapture`

Expected: both startup tests PASS.

- [ ] **Step 4: Commit**

```bash
git add client/tests/integration_tests.rs client/src/cli/dispatch.rs
git commit -m "test: lock startup connect guidance wording"
```

### Task 4: Rename the Rust library crate and binary entrypoint imports

**Files:**
- Modify: `client/Cargo.toml:20-22`
- Modify: `client/src/main.rs:1-17`
- Modify: `client/tests/setup_ci_smoke.rs:1-5`
- Modify: `client/src/bin/seed_observatory.rs:1-5,252`

- [ ] **Step 1: Write the failing compile-oriented check**

Run: `cargo test --manifest-path client/Cargo.toml setup_bootstrap_doctor_status_json_smoke -- --exact`

Expected: PASS before rename, establishing the current baseline.

- [ ] **Step 2: Rename the lib crate in `client/Cargo.toml`**

Change:

```toml
[lib]
name = "lean_ctx"
path = "src/lib.rs"
```

to:

```toml
[lib]
name = "nebu_ctx"
path = "src/lib.rs"
```

- [ ] **Step 3: Update the binary entrypoint import**

Change `client/src/main.rs` from:

```rust
fn main() {
    std::panic::set_hook(Box::new(|info| {
        eprintln!("nebu-ctx: unexpected error (your command was not affected)");
        eprintln!("  Disable temporarily: nebu-ctx-off");
        eprintln!("  Full uninstall:      nebu-ctx uninstall");
        if let Some(msg) = info.payload().downcast_ref::<&str>() {
            eprintln!("  Details: {msg}");
        } else if let Some(msg) = info.payload().downcast_ref::<String>() {
            eprintln!("  Details: {msg}");
        }
        if let Some(loc) = info.location() {
            eprintln!("  Location: {}:{}", loc.file(), loc.line());
        }
    }));

    lean_ctx::cli::run();
}
```

to:

```rust
fn main() {
    std::panic::set_hook(Box::new(|info| {
        eprintln!("nebu-ctx: unexpected error (your command was not affected)");
        eprintln!("  Disable temporarily: nebu-ctx-off");
        eprintln!("  Full uninstall:      nebu-ctx uninstall");
        if let Some(msg) = info.payload().downcast_ref::<&str>() {
            eprintln!("  Details: {msg}");
        } else if let Some(msg) = info.payload().downcast_ref::<String>() {
            eprintln!("  Details: {msg}");
        }
        if let Some(loc) = info.location() {
            eprintln!("  Location: {}:{}", loc.file(), loc.line());
        }
    }));

    nebu_ctx::cli::run();
}
```

- [ ] **Step 4: Update direct test/bin imports**

Change imports such as these:

```rust
use lean_ctx::core::setup_report::SetupReport;
use lean_ctx::status::StatusReport;
use lean_ctx::token_report::TokenReport;
```

to:

```rust
use nebu_ctx::core::setup_report::SetupReport;
use nebu_ctx::status::StatusReport;
use nebu_ctx::token_report::TokenReport;
```

Also update `client/src/bin/seed_observatory.rs` imports from `lean_ctx::...` to `nebu_ctx::...`.

- [ ] **Step 5: Re-run the focused smoke test to verify rename compiles cleanly**

Run: `cargo test --manifest-path client/Cargo.toml setup_bootstrap_doctor_status_json_smoke -- --exact`

Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add client/Cargo.toml client/src/main.rs client/tests/setup_ci_smoke.rs client/src/bin/seed_observatory.rs
git commit -m "refactor: rename Rust client crate to nebu_ctx"
```

### Task 5: Rename `LeanCtxServer` and its active references

**Files:**
- Modify: `client/src/tools/mod.rs`
- Modify: `client/src/mcp_server/dispatch.rs`

- [ ] **Step 1: Write the failing compile-oriented check**

Run: `cargo test --manifest-path client/Cargo.toml binary_prints_help -- --exact`

Expected: PASS before the type rename.

- [ ] **Step 2: Rename the server type and constructor references**

In `client/src/tools/mod.rs`, change:

```rust
pub struct LeanCtxServer {
```

to:

```rust
pub struct NebuCtxServer {
```

Change the impl blocks:

```rust
impl Default for LeanCtxServer {
```

to:

```rust
impl Default for NebuCtxServer {
```

and:

```rust
impl LeanCtxServer {
```

to:

```rust
impl NebuCtxServer {
```

Change the factory:

```rust
pub fn create_server() -> LeanCtxServer {
    LeanCtxServer::new()
}
```

to:

```rust
pub fn create_server() -> NebuCtxServer {
    NebuCtxServer::new()
}
```

Rename the test-local constructor calls in this file from `LeanCtxServer::...` to `NebuCtxServer::...`.

- [ ] **Step 3: Update downstream imports and impl target**

In `client/src/mcp_server/dispatch.rs`, change:

```rust
use crate::tools::LeanCtxServer;

impl LeanCtxServer {
```

to:

```rust
use crate::tools::NebuCtxServer;

impl NebuCtxServer {
```

Also update any static method calls such as:

```rust
let effective_mode = LeanCtxServer::upgrade_mode_if_stale(&mode, stale).to_string();
```

to:

```rust
let effective_mode = NebuCtxServer::upgrade_mode_if_stale(&mode, stale).to_string();
```

- [ ] **Step 4: Re-run the focused compile check**

Run: `cargo test --manifest-path client/Cargo.toml binary_prints_help -- --exact`

Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add client/src/tools/mod.rs client/src/mcp_server/dispatch.rs
git commit -m "refactor: rename LeanCtxServer to NebuCtxServer"
```

### Task 6: Replace remaining active user-facing `lean-ctx` strings with `nebu-ctx`

**Files:**
- Modify: `client/src/hook_handlers.rs`
- Modify: `README.md`
- Modify: `client/README.md`
- Optionally modify: `client/src/shell.rs`, `client/src/tools/ctx_shell.rs` if the remaining occurrences are active product-facing text rather than legacy compatibility/test fixtures

- [ ] **Step 1: Write the failing wording test**

Update or add a focused test in `client/src/hook_handlers.rs`:

```rust
#[test]
fn codex_reroute_message_uses_nebu_ctx_binary_name() {
    let message = codex_reroute_message("nebu-ctx -c 'git status'");
    assert_eq!(
        message,
        "Command should run via nebu-ctx for compact output. Do not retry the original command. Re-run with: nebu-ctx -c 'git status'"
    );
}
```

- [ ] **Step 2: Run the focused test to verify it fails**

Run: `cargo test --manifest-path client/Cargo.toml codex_reroute_message_uses_nebu_ctx_binary_name -- --exact`

Expected: FAIL because the current message still says `lean-ctx`.

- [ ] **Step 3: Update the active product-facing strings and tests**

In `client/src/hook_handlers.rs`, change product-facing strings like:

```rust
"Command should run via lean-ctx for compact output. Do not retry the original command. Re-run with: {rewritten}"
```

to:

```rust
"Command should run via nebu-ctx for compact output. Do not retry the original command. Re-run with: {rewritten}"
```

Update associated test literals to use `nebu-ctx` as the current binary name.

In `README.md`, replace:

```md
The client side of `nebu-ctx` started from a fork and practical inspiration of `lean-ctx`, then was reshaped into a broader system that fits the rest of Mark's projects.
```

with:

```md
The client side of `nebu-ctx` started from an earlier internal client foundation and was reshaped into a broader system that fits the rest of Mark's projects.
```

In `client/README.md`, replace:

```md
- Started from the practical `lean-ctx` client surface and was reshaped into the current `nebu-ctx` runtime client.
```

with:

```md
- Started from an earlier practical client surface and was reshaped into the current `nebu-ctx` runtime client.
```

Only change `client/src/shell.rs` or `client/src/tools/ctx_shell.rs` if the remaining matches are shown to users as current product text rather than legacy compatibility/test input.

- [ ] **Step 4: Re-run the focused wording test**

Run: `cargo test --manifest-path client/Cargo.toml codex_reroute_message_uses_nebu_ctx_binary_name -- --exact`

Expected: PASS.

- [ ] **Step 5: Run a targeted grep to confirm the intended cleanup scope**

Run: `rg -n "lean-ctx|lean_ctx|LeanCtxServer" client/src client/tests README.md client/README.md --glob '!client/target/**'`

Expected: no remaining active matches except intentional legacy compatibility references you explicitly chose to keep.

- [ ] **Step 6: Commit**

```bash
git add client/src/hook_handlers.rs README.md client/README.md client/src/shell.rs client/src/tools/ctx_shell.rs
git commit -m "fix: remove remaining lean-ctx product references"
```

### Task 7: Run final verification for the full client change set

**Files:**
- No code changes required
- Verify: `client/tests/integration_tests.rs`
- Verify: `client/tests/setup_ci_smoke.rs`
- Verify: `client/src/hook_handlers.rs`

- [ ] **Step 1: Run the two targeted startup tests**

Run: `cargo test --manifest-path client/Cargo.toml mcp_stdio_start_ -- --nocapture`

Expected: PASS.

- [ ] **Step 2: Run the smoke/status test covering the renamed crate imports**

Run: `cargo test --manifest-path client/Cargo.toml setup_bootstrap_doctor_status_json_smoke -- --exact`

Expected: PASS.

- [ ] **Step 3: Run the focused hook wording test**

Run: `cargo test --manifest-path client/Cargo.toml codex_reroute_message_uses_nebu_ctx_binary_name -- --exact`

Expected: PASS.

- [ ] **Step 4: Run the full client test suite**

Run: `cargo test --manifest-path client/Cargo.toml`

Expected: all client tests PASS.

- [ ] **Step 5: Commit verification-only or final touch-ups if needed**

If verification required no further edits, do not create an extra commit.

If verification required fixes, commit them with a message matching the actual fix, for example:

```bash
git add <fixed-files>
git commit -m "fix: align startup guidance and client naming"
```

## Self-Review

- Spec coverage check:
  - startup preflight gate: covered in Tasks 1-3
  - local and network `connect` examples on port `4242`: covered in Tasks 1-3
  - removal of startup serde/initialize noise: covered in Tasks 1-3
  - Rust lib crate rename away from `lean_ctx`: covered in Task 4
  - `LeanCtxServer` rename and active source cleanup: covered in Task 5
  - remaining active `lean-ctx` user-facing cleanup: covered in Task 6
  - verification expectations: covered in Task 7
- Placeholder scan: no `TBD`, `TODO`, or underspecified “write tests” steps remain.
- Type consistency check: the plan consistently uses `nebu_ctx` as the crate name and `NebuCtxServer` as the renamed server type.
