# Client Surface Connect Cleanup Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Remove `bind`, `dashboard`, and `watch` from the Rust client surface, make project resolution fully implicit, and replace `cloud` naming with `connect` / `host` / `server` across the relevant client code and README.

**Architecture:** The implementation should lean on the existing lazy project resolution path instead of replacing it with a new bootstrap mechanism. The client becomes thinner by dropping host-owned surfaces, while internal naming is cleaned up so the codebase matches the current product model.

**Tech Stack:** Rust, .NET contracts as reference, Markdown docs, Cargo tests, .NET tests

---

## File Map

- Modify: `client/src/cli/dispatch.rs`
  - Remove `bind`, `dashboard`, and `watch` from dispatch/help
- Rename/modify: `client/src/cli/cloud.rs` -> `client/src/cli/connect.rs`
  - Keep connect/disconnect behavior only
- Rename/modify: `client/src/cloud_client.rs` -> `client/src/server_client.rs`
  - Preserve host HTTP behavior while removing `cloud` terminology
- Modify: `client/src/cli/mod.rs`
  - Update module exports and any local-client-surface error messaging
- Modify: `client/src/mcp_server/mod.rs`
  - Rename routing constants and internal cloud wording
- Modify: `client/src/core/index_orchestrator.rs`
  - Update renamed client module references
- Modify: `client/src/status.rs`
  - Replace user-facing `cloud` wording with `host` / `server` / `connect`
- Modify: `client/src/hook_handlers.rs`
  - Update stale user-facing or architectural comments/messages where needed
- Modify: `README.md`
  - Remove `bind` / `dashboard` / `watch` and align wording with the new model
- Modify: `client/tests/integration_tests.rs`
  - Update help/CLI expectations
- Modify: `client/tests/shell_and_agent_tests.rs`
  - Update any affected user-facing text expectations if needed

### Task 1: Remove `bind`, `dashboard`, and `watch` from the client CLI surface

**Files:**
- Modify: `client/src/cli/dispatch.rs:24-30`
- Modify: `client/src/cli/dispatch.rs:694-695`
- Modify: `client/src/cli/dispatch.rs:784-924`
- Test: `client/tests/integration_tests.rs`

- [ ] **Step 1: Write the failing tests first**

Add or update client integration tests in `client/tests/integration_tests.rs` to assert:

```rust
#[test]
fn help_no_longer_lists_bind_or_dashboard() {
    let output = nebula_ctx_bin()
        .arg("--help")
        .output()
        .expect("failed to run nebu-ctx");
    let stdout = String::from_utf8_lossy(&output.stdout);

    assert!(!stdout.contains("bind"), "help should not list bind: {stdout}");
    assert!(!stdout.contains("dashboard"), "help should not list dashboard: {stdout}");
    assert!(!stdout.contains("watch"), "help should not list watch: {stdout}");
}
```

- [ ] **Step 2: Run the focused test to verify it fails**

Run: `cargo test --manifest-path client/Cargo.toml help_no_longer_lists_bind_or_dashboard -- --exact`

Expected: FAIL because current help still contains `bind`, `dashboard`, and `watch`.

- [ ] **Step 3: Remove the client commands and help text**

Update `client/src/cli/dispatch.rs` to:

- remove the early `matches!(command, "dashboard" | "watch" | "heatmap" | "stats")` branch for `dashboard` and `watch`
- keep analytics-only handling only for still-supported analytics surfaces if needed
- remove the `"bind" => { ... }` match arm
- remove `dashboard|watch` and `bind` lines from `print_help()`
- update the host-connection section in help to keep only `connect`, `status`, and `disconnect` if `status` still exists, or only the actually supported commands

Minimal target shape in `dispatch.rs`:

```rust
if matches!(command, "heatmap" | "stats") {
    super::exit_cloud_analytics_only(command);
}
```

and remove this arm entirely:

```rust
"bind" => {
    super::cloud::cmd_bind();
    return;
}
```

- [ ] **Step 4: Re-run the focused test to verify it passes**

Run: `cargo test --manifest-path client/Cargo.toml help_no_longer_lists_bind_or_dashboard -- --exact`

Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add client/src/cli/dispatch.rs client/tests/integration_tests.rs
git commit -m "refactor: remove bind and dashboard from client CLI"
```

### Task 2: Remove `bind` implementation and rename `cloud` client modules to `connect` / `server`

**Files:**
- Move/Modify: `client/src/cli/cloud.rs` -> `client/src/cli/connect.rs`
- Move/Modify: `client/src/cloud_client.rs` -> `client/src/server_client.rs`
- Modify: `client/src/cli/mod.rs`
- Modify: `client/src/cli/dispatch.rs`
- Modify: `client/src/core/index_orchestrator.rs`
- Modify: any compile errors caused by module rename

- [ ] **Step 1: Write a failing compile-driven check**

No new behavior test is needed here first; use the existing compile boundary as the failing check.

Run before changes: `cargo test --manifest-path client/Cargo.toml binary_prints_help -- --exact`

Expected: PASS before rename, establishing a baseline.

- [ ] **Step 2: Rename the CLI module and drop bind behavior**

Move `client/src/cli/cloud.rs` to `client/src/cli/connect.rs` and update it to this shape:

```rust
use crate::server_client::ServerClient;
use crate::models::ServerConnection;
use crate::{config, core};
```

Remove:

```rust
pub fn cmd_bind() { ... }
fn bind_current_project() -> Result<()> { ... }
use crate::{config, core, git_context};
```

Rename internal function names from `*_cloud` to `*_server` or `*_connection` where practical:

- `connect_cloud` -> `connect_server`
- `disconnect_cloud` -> `disconnect_server`
- `load_or_prompt_cloud_client` -> `load_or_prompt_server_client`

Update prompt strings from `Cloud URL` / `Cloud token` to `Server URL` / `Server token`.

- [ ] **Step 3: Rename the HTTP client module and references**

Move `client/src/cloud_client.rs` to `client/src/server_client.rs`.

Update imports such as:

```rust
use crate::cloud_client::ServerClient;
```

to:

```rust
use crate::server_client::ServerClient;
```

Also update internal error strings like:

```rust
"No server connection saved. Run `nebu-ctx cloud connect`."
```

to a `connect`-based message, for example:

```rust
"No server connection saved. Run `nebu-ctx connect --endpoint <url> --token <token>`."
```

- [ ] **Step 4: Update module exports and compile references**

Update `client/src/cli/mod.rs` from:

```rust
pub mod cloud;
```

to:

```rust
pub mod connect;
```

Update dispatch call sites from `super::cloud::...` to `super::connect::...`.

Update any remaining references in `index_orchestrator.rs` and elsewhere from `cloud_client` to `server_client`.

- [ ] **Step 5: Re-run a focused client test to verify the rename is green**

Run: `cargo test --manifest-path client/Cargo.toml binary_prints_help -- --exact`

Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add client/src/cli/connect.rs client/src/server_client.rs client/src/cli/mod.rs client/src/cli/dispatch.rs client/src/core/index_orchestrator.rs
git commit -m "refactor: rename cloud client surfaces to connect and server"
```

### Task 3: Rename routing constants and remaining user-facing `cloud` terminology

**Files:**
- Modify: `client/src/mcp_server/mod.rs`
- Modify: `client/src/mcp_server/dispatch.rs`
- Modify: `client/src/status.rs`
- Modify: `client/src/tool_defs/granular.rs`
- Modify: `client/src/terminal_ui.rs`
- Modify: `client/src/hook_handlers.rs`

- [ ] **Step 1: Add a failing wording test**

Extend `client/tests/integration_tests.rs` with:

```rust
#[test]
fn help_prefers_connect_over_cloud_wording() {
    let output = nebula_ctx_bin()
        .arg("--help")
        .output()
        .expect("failed to run nebu-ctx");
    let stdout = String::from_utf8_lossy(&output.stdout);

    assert!(stdout.contains("connect"), "help should mention connect: {stdout}");
    assert!(!stdout.contains("CLOUD SERVER"), "help should not use cloud server heading: {stdout}");
}
```

- [ ] **Step 2: Run the focused test to verify it fails**

Run: `cargo test --manifest-path client/Cargo.toml help_prefers_connect_over_cloud_wording -- --exact`

Expected: FAIL while old `CLOUD SERVER` wording remains.

- [ ] **Step 3: Rename constants and update user-facing strings**

In `client/src/mcp_server/mod.rs` rename:

```rust
pub const CLOUD_ONLY_TOOLS
const CLOUD_PREFERRED_TOOLS
enum CloudResult
```

to names such as:

```rust
pub const SERVER_ONLY_TOOLS
const SERVER_PREFERRED_TOOLS
enum ServerRoutingResult
```

Update nearby comments and error messages from `cloud` to `server` / `host` where they describe the current architecture.

Update user-facing strings in:

- `client/src/status.rs`
- `client/src/terminal_ui.rs`
- `client/src/tool_defs/granular.rs`
- `client/src/mcp_server/dispatch.rs`

Examples of target wording:

- `host: connected -> ...`
- `run: nebu-ctx connect`
- `requires a server connection`
- `server-only analytics surface`

- [ ] **Step 4: Re-run the focused wording test**

Run: `cargo test --manifest-path client/Cargo.toml help_prefers_connect_over_cloud_wording -- --exact`

Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add client/src/mcp_server/mod.rs client/src/mcp_server/dispatch.rs client/src/status.rs client/src/tool_defs/granular.rs client/src/terminal_ui.rs client/src/hook_handlers.rs client/tests/integration_tests.rs
git commit -m "refactor: replace cloud terminology in client surfaces"
```

### Task 4: Rewrite the README around the new client/host split

**Files:**
- Modify: `README.md`

- [ ] **Step 1: Add a failing doc expectation test using grep**

Run: `rg -n "bind|dashboard|cloud" README.md`

Expected: matches include client-facing `bind`, `dashboard`, or stale `cloud` wording that must be removed or reduced.

- [ ] **Step 2: Rewrite the affected README sections**

Update `README.md` so it:

- removes `bind` from install flow
- removes `dashboard` from client CLI surface lists
- stops telling users to run `nebu-ctx dashboard`
- says the dashboard is accessed directly on the host
- replaces `cloud` with `connect`, `host`, or `server` wherever that is now the intended terminology

The install flow should end up structurally like:

```md
1. Install the Rust client
2. Start the host
3. Connect the client to the host
4. Configure editor and agent integrations
5. Verify with doctor
6. Visit the dashboard directly on the host
```

- [ ] **Step 3: Verify the README no longer advertises removed client surfaces**

Run: `rg -n "nebu-ctx bind|nebu-ctx dashboard|cloud bind|cloud connect" README.md`

Expected: no matches.

Run: `rg -n "connect|host|server|3333|4242" README.md`

Expected: matches confirm the new wording and host ownership are explicit.

- [ ] **Step 4: Commit**

```bash
git add README.md
git commit -m "docs: remove bind and dashboard from client documentation"
```

### Task 5: Full verification

**Files:**
- Verify: client and server tests

- [ ] **Step 1: Run client test suite**

Run: `cargo test --manifest-path client/Cargo.toml`

Expected: PASS.

- [ ] **Step 2: Run server test suite**

Run: `dotnet test server/NebuCtx.slnx -p:AllowMissingPrunePackageData=true`

Expected: PASS.

- [ ] **Step 3: Run final spot checks for removed client commands**

Run: `cargo run --manifest-path client/Cargo.toml -- --help`

Expected: help output contains `connect` and does not contain `bind`, `dashboard`, or `watch`.

Run: `rg -n "\bcloud\b" client/src README.md`

Expected: only legacy/internal leftovers remain if absolutely necessary; no dominant client/README product wording still says `cloud`.

- [ ] **Step 4: Commit verification follow-ups if needed**

```bash
git add README.md client/src client/tests
git commit -m "test: verify connect terminology and thinner client surface"
```
