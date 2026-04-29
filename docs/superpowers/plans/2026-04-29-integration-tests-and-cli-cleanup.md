# Integration Tests, Bug Fixes & CLI Cleanup Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Run the v0.5.6 integration test suite from HANDOVER.md, fix all identified failures, and clean up the CLI to remove broken/legacy commands.

**Architecture:** Three-phase: (1) establish baseline via integration tests, (2) fix bugs found + known issues from HANDOVER.md, (3) clean up CLI command routing/help. Server side is .NET 10 / ASP.NET at `server/`; client side is Rust at `client/`.

**Tech Stack:** Rust (ureq 3.x, serde), C# .NET 10, PostgreSQL, podman container `nebu-ctx-local`

---

## Session-Start Findings (2026-04-29)

These were confirmed during the planning session — skip investigation steps marked "verify the bug" and apply fixes directly:

| # | Finding | Location | Confirmed? |
|---|---------|----------|-----------|
| F1 | `cloud bind` / `sync` fail with "failed to parse server response" | `ProjectResolutionContracts.cs` + serde alias | ✅ Root cause confirmed |
| F2 | Root cause: server serializes **both** `checkout_bound` AND `workspace_bound` → serde duplicate-field error | `ProjectResolutionResponse.WorkspaceBound` lacks `[JsonIgnore]` | ✅ Verified with curl |
| F3 | `nebu-ctx on` / `nebu-ctx off` → "unknown command" | `main.rs` dispatch — no match arm for `on`/`off` | ✅ Confirmed |
| F4 | `nebu-ctx sync` (top-level alias) → "failed to sync with cloud" for same reason as F1 | calls `resolve_project()` | ✅ Confirmed |
| F5 | `login`, `register`, `forgot-password`, `contribute`, `upgrade` are ghost stubs in dispatch | `main.rs`, `cli/cloud.rs` | ✅ Confirmed |
| F6 | `ctx_knowledge` MCP call returned HTTP 400 during this session | Possible server-side validation issue or config drift | ⚠️ Investigate |
| F7 | `ctx_brain` recall returned empty — MCP brain bridge may not be seeded | May be env-specific; re-check after container rebuild | ⚠️ Investigate |
| F8 | Token tracking shows 0 — hook reads wrong field names | `hook_handlers.rs::handle_post_tool_use` | Known from HANDOVER |
| F9 | Task A: ctx_knowledge silently falls back to local when cloud configured | `mcp_server/mod.rs` ~line 233 | Known from HANDOVER |
| F10 | Task B: autopilot consolidation loop does not call `post_knowledge_to_cloud()` | `mcp_server/mod.rs` ~line 499 | Known from HANDOVER |

---

## Pre-work: Environment Setup

Before any task, verify the local environment is healthy:

```bash
nebu-ctx --version        # must show 0.5.6
podman ps | grep nebu-ctx # must show nebu-ctx-local running
TOKEN=$(grep '^NEBULA_CTX_HTTP_TOKEN=' .env | cut -d= -f2)
curl -sH "Authorization: Bearer $TOKEN" http://192.168.1.135:4242/health | python3 -m json.tool
```

Expected: version 0.5.6, container running, health status ok.

---

## Phase 1 — Integration Tests (Baseline)

### Task 1: Pre-flight Checks

**Files:** None — CLI/curl only

- [x] **Step 1: Verify version**

```bash
nebu-ctx --version
```

Expected: `nebu-ctx 0.5.6 ...`

- [x] **Step 2: Verify doctor**

```bash
nebu-ctx doctor
```

Expected: no critical failures. Note any warnings.

- [x] **Step 3: Verify cloud status**

```bash
nebu-ctx cloud status
```

Expected: `"health": {"status": "ok"}` with endpoint `http://192.168.1.135:4242`

- [x] **Step 4: Verify container**

```bash
podman ps --format "{{.Names}} {{.Status}}" | grep nebu-ctx
```

Expected: `nebu-ctx-local Up ...`

---

### Task 2: Test 1 — PostToolUse Hook Fires Telemetry

**Files:** `client/src/hook_handlers.rs`

- [x] **Step 1: Get baseline telemetry count**

```bash
TOKEN=$(grep '^NEBULA_CTX_HTTP_TOKEN=' .env | cut -d= -f2)
curl -sH "Authorization: Bearer $TOKEN" http://127.0.0.1:3333/api/gain | python3 -m json.tool
```

Note the current `totalCalls` value.

- [x] **Step 2: Fire a manual hook event**

```bash
echo '{"tool_name":"ctx_read","tool_input":{"path":"test"},"tool_output":"result","usage":{"input_tokens":100,"output_tokens":50}}' \
  | nebu-ctx hook post-tool-use
```

Expected: no error, exits 0.

- [x] **Step 3: Verify telemetry count increased**

```bash
TOKEN=$(grep '^NEBULA_CTX_HTTP_TOKEN=' .env | cut -d= -f2)
curl -sH "Authorization: Bearer $TOKEN" http://127.0.0.1:3333/api/gain | python3 -m json.tool
```

Expected: `totalCalls` increased by at least 1.

- [x] **Step 4: Record pass/fail in notes**

Record: ✅ PASS or ❌ FAIL (with error detail). Continue regardless.

---

### Task 3: Test 2 — Stop Hook Fires Brain Snapshot

**Files:** `client/src/hook_handlers.rs`, `client/src/cloud_client.rs`

- [x] **Step 1: Trigger stop hook manually**

```bash
echo '{}' | nebu-ctx hook stop
```

Expected: exits 0 (may print nothing or a summary line).

- [x] **Step 2: Query brain for session entry**

```bash
TOKEN=$(grep '^NEBULA_CTX_HTTP_TOKEN=' .env | cut -d= -f2)
curl -sX POST \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  http://127.0.0.1:4242/v1/tools/call \
  -d '{"name":"ctx_brain","arguments":{"action":"recall","query":"session"}}' \
  | python3 -m json.tool
```

Expected: a `session-<id>` key appears in results with recent timestamp.

- [x] **Step 3: Record pass/fail**

Record: ✅ PASS or ❌ FAIL.

---

### Task 4: Test 3 — Analytics Tools Return Real Data

**Files:** None (server-side, tests only)

- [x] **Step 1: Call ctx_gain**

```bash
TOKEN=$(grep '^NEBULA_CTX_HTTP_TOKEN=' .env | cut -d= -f2)
BASE="http://127.0.0.1:4242/v1/tools/call"
curl -s -H "Authorization: Bearer $TOKEN" -H "Content-Type: application/json" \
  $BASE -d '{"name":"ctx_gain","arguments":{"action":"report"}}' | python3 -m json.tool
```

Expected: JSON output with non-empty content (not all zeros).

- [x] **Step 2: Call ctx_cost**

```bash
curl -s -H "Authorization: Bearer $TOKEN" -H "Content-Type: application/json" \
  $BASE -d '{"name":"ctx_cost","arguments":{"action":"report"}}' | python3 -m json.tool
```

Expected: JSON with cost data.

- [x] **Step 3: Call ctx_heatmap**

```bash
curl -s -H "Authorization: Bearer $TOKEN" -H "Content-Type: application/json" \
  $BASE -d '{"name":"ctx_heatmap","arguments":{"action":"status"}}' | python3 -m json.tool
```

Expected: heatmap data.

- [x] **Step 4: Call ctx_stats**

```bash
curl -s -H "Authorization: Bearer $TOKEN" -H "Content-Type: application/json" \
  $BASE -d '{"name":"ctx_stats","arguments":{"action":"report"}}' | python3 -m json.tool
```

Expected: stats snapshot.

- [x] **Step 5: Check per-project stats REST endpoint**

```bash
curl -s -H "Authorization: Bearer $TOKEN" \
  "http://127.0.0.1:3333/api/projects/nebu-ctx/stats" | python3 -m json.tool
```

Expected: 200 with project stats (zeros OK for a new project, non-zero better).

- [x] **Step 6: Record pass/fail per tool**

---

### Task 5: Test 4 — Token Tracking Investigation

**Files:** `client/src/hook_handlers.rs`

- [x] **Step 1: Check ctx_cost for token totals**

```bash
TOKEN=$(grep '^NEBULA_CTX_HTTP_TOKEN=' .env | cut -d= -f2)
curl -sH "Authorization: Bearer $TOKEN" http://127.0.0.1:3333/api/gain \
  | python3 -c "import sys,json; d=json.load(sys.stdin); print('totalTokens:', d.get('totalTokens',d.get('total_tokens','missing')))"
```

If `totalTokens` is 0, note it as a known issue. The fix is in Phase 2, Task 9.

- [x] **Step 2: Inspect what Claude Code sends in PostToolUse hook**

Look at `client/src/hook_handlers.rs` function `handle_post_tool_use()` to see which JSON field names it reads for token counts. Compare with the Claude Code hook event schema.

```bash
grep -A 30 "fn handle_post_tool_use" client/src/hook_handlers.rs
```

Expected: the code reads `tool_input` / `tool_output` OR `usage.input_tokens` / `usage.output_tokens`. Note which fields it looks for.

- [x] **Step 3: Test with known-good token data**

```bash
echo '{"tool_name":"ctx_read","tool_input":{"path":"test"},"tool_output":"result","usage":{"input_tokens":100,"output_tokens":50}}' \
  | nebu-ctx hook post-tool-use
```

Check if this gets picked up. Record the field names that work.

- [x] **Step 4: Record findings**

Note: which field names work, what changes are needed, feed into Phase 2 Task 9.

---

### Task 6: Test 5 — ctx_knowledge Cloud Routing

**Files:** `client/src/mcp_server/mod.rs`

- [x] **Step 1: Call ctx_knowledge via MCP tool**

```bash
TOKEN=$(grep '^NEBULA_CTX_HTTP_TOKEN=' .env | cut -d= -f2)
curl -s -H "Authorization: Bearer $TOKEN" -H "Content-Type: application/json" \
  http://127.0.0.1:4242/v1/tools/call \
  -d '{"name":"ctx_knowledge","arguments":{"action":"remember","category":"testing","key":"integration-test-2026-04-29","value":"passed v0.5.6 integration test"}}' \
  | python3 -m json.tool
```

Expected: success response, NOT a `⚠ Running locally` warning.

- [x] **Step 2: Verify it landed in PostgreSQL**

```bash
curl -s -H "Authorization: Bearer $TOKEN" -H "Content-Type: application/json" \
  http://127.0.0.1:4242/v1/tools/call \
  -d '{"name":"ctx_knowledge","arguments":{"action":"recall","query":"integration-test-2026-04-29"}}' \
  | python3 -m json.tool
```

Expected: the key is returned from the server (not local fallback).

- [x] **Step 3: Record pass/fail**

If shows local fallback: this is Task A bug, tracked in Phase 2, Task 10.

---

### Task 7: Test 6 — Dashboard Accuracy

**Files:** None (browser check)

- [x] **Step 1: Verify dashboard is reachable**

```bash
curl -sH "Authorization: Bearer $(grep '^NEBULA_CTX_HTTP_TOKEN=' .env | cut -d= -f2)" \
  http://127.0.0.1:3333/ | head -5
```

Expected: HTML response.

- [x] **Step 2: Check API endpoints used by dashboard**

```bash
TOKEN=$(grep '^NEBULA_CTX_HTTP_TOKEN=' .env | cut -d= -f2)
curl -sH "Authorization: Bearer $TOKEN" http://127.0.0.1:3333/api/gain | python3 -m json.tool | head -10
curl -sH "Authorization: Bearer $TOKEN" "http://127.0.0.1:3333/api/projects" | python3 -m json.tool | head -10
```

Expected: non-empty JSON responses.

- [x] **Step 3: Record pass/fail**

---

## Phase 2 — Bug Fixes

### Task 8: Fix cloud bind / sync — Duplicate JSON Field Bug

**Root cause:** `ProjectResolutionResponse` in C# serializes BOTH `checkout_bound` AND `workspace_bound` (two properties backed by the same field). The Rust serde `alias` treats them as duplicates and throws "duplicate field" deserialization error.

**Files:**
- Modify: `server/src/NebuCtx.Contracts/Projects/ProjectResolutionContracts.cs`

> **Root cause already confirmed** (F1/F2 above). Skip straight to the fix.
>
> The server response contains `"checkout_bound": false, "workspace_bound": false` (two C# properties on the same backing field). The Rust `#[serde(rename = "checkout_bound", alias = "workspace_bound")]` treats both as referring to the same field → duplicate field deserialization error → "failed to parse server response".

- [x] **Step 1: Confirm current failure**

```bash
nebu-ctx cloud bind 2>&1
```

Expected currently: `failed to parse server response`

- [x] **Step 2: Fix — suppress WorkspaceBound from response serialization**

In `server/src/NebuCtx.Contracts/Projects/ProjectResolutionContracts.cs`, add `[JsonIgnore]` to the `WorkspaceBound` property on `ProjectResolutionResponse`:

```csharp
// Before:
/// <summary>
/// Legacy workspace-bound alias kept for older clients.
/// </summary>
[JsonPropertyName("workspace_bound")]
public bool WorkspaceBound
{
    get => _checkoutBound;
    set => _checkoutBound = value;
}

// After:
/// <summary>
/// Legacy workspace-bound alias kept for older clients.
/// Not emitted in responses — use checkout_bound.
/// </summary>
[JsonIgnore]
public bool WorkspaceBound
{
    get => _checkoutBound;
    set => _checkoutBound = value;
}
```

- [x] **Step 3: Build the server**

```bash
dotnet build server/NebuCtx.slnx -p:AllowMissingPrunePackageData=true
```

Expected: 0 errors, 0 warnings.

- [x] **Step 4: Rebuild and redeploy the local container**

```bash
podman build -t nebu-ctx-server -f Dockerfile .
podman stop nebu-ctx-local || true
podman rm nebu-ctx-local || true
podman run -d --name nebu-ctx-local \
  -p 127.0.0.1:3333:3333 -p 127.0.0.1:4242:4242 \
  --env-file .env \
  nebu-ctx-server
sleep 3
```

- [x] **Step 5: Verify fix**

```bash
nebu-ctx cloud bind 2>&1
```

Expected: JSON with project details, no error.

```bash
nebu-ctx sync 2>&1
```

Expected: JSON with synced state, no error.

- [x] **Step 6: Run server tests**

```bash
dotnet vstest server/tests/*/bin/Debug/net10.0/*.dll --logger:"console;verbosity=detailed" 2>&1 | tail -20
```

Expected: all tests pass.

- [x] **Step 7: Commit**

```bash
git add server/src/NebuCtx.Contracts/Projects/ProjectResolutionContracts.cs
git commit -m "fix: suppress WorkspaceBound from ProjectResolutionResponse serialization

The response included both checkout_bound and workspace_bound JSON fields
(two C# properties sharing a backing field). Serde's alias resolution
treats duplicate alias keys as an error, causing cloud bind/sync to fail
with 'failed to parse server response'. WorkspaceBound is accepted on
inbound requests (backward compat) but must not appear in responses.

Co-authored-by: Copilot <223556219+Copilot@users.noreply.github.com>"
```

---

### Task 9: Fix Token Tracking (Shows 0 for Most Tools)

**Files:** `client/src/hook_handlers.rs`

- [x] **Step 1: Check current field extraction logic**

```bash
grep -A 50 "fn handle_post_tool_use" client/src/hook_handlers.rs
```

Note which JSON field names the code reads (e.g., `tool_input`, `tool_output`, `usage`).

- [x] **Step 2: Test with Claude Code hook payload format**

The Claude Code PostToolUse hook sends a JSON payload with this structure. Test all candidate field names:

```bash
# Test with Claude Code's "usage" field format
echo '{"tool_name":"ctx_read","usage":{"input_tokens":100,"output_tokens":50}}' \
  | nebu-ctx hook post-tool-use

# Test with Copilot CLI format (may use different fields)
echo '{"tool_name":"ctx_read","tool_input":{"path":"file"},"tool_output":"content of file"}' \
  | nebu-ctx hook post-tool-use
```

Check the telemetry ingestion result in the dashboard to see which format produces non-zero token counts.

- [x] **Step 3: Read the current extraction code**

```bash
cat client/src/hook_handlers.rs | grep -A 80 "fn handle_post_tool_use"
```

- [x] **Step 4: Update field extraction to handle both formats**

In `client/src/hook_handlers.rs`, update `handle_post_tool_use()` to try multiple field name patterns:

```rust
// Try Claude Code's "usage" field first, fall back to byte-length proxy
let input_tokens = payload
    .get("usage")
    .and_then(|u| u.get("input_tokens"))
    .and_then(|v| v.as_i64())
    .unwrap_or_else(|| {
        // Byte-length proxy from tool_input JSON
        payload.get("tool_input")
            .map(|v| v.to_string().len() as i64 / 4)
            .unwrap_or(0)
    });

let output_tokens = payload
    .get("usage")
    .and_then(|u| u.get("output_tokens"))
    .and_then(|v| v.as_i64())
    .unwrap_or_else(|| {
        // Byte-length proxy from tool_output string
        payload.get("tool_output")
            .and_then(|v| v.as_str())
            .map(|s| s.len() as i64 / 4)
            .unwrap_or(0)
    });
```

- [x] **Step 5: Build and test**

```bash
cargo build --manifest-path client/Cargo.toml 2>&1 | tail -5
cargo test --manifest-path client/Cargo.toml --lib 2>&1 | tail -10
```

Expected: 0 errors.

- [x] **Step 6: Reinstall the client**

```bash
cargo install --path client/
```

- [x] **Step 7: Verify tokens are now non-zero**

```bash
echo '{"tool_name":"ctx_read","usage":{"input_tokens":100,"output_tokens":50}}' \
  | nebu-ctx hook post-tool-use
# Then check ctx_cost:
TOKEN=$(grep '^NEBULA_CTX_HTTP_TOKEN=' .env | cut -d= -f2)
curl -s -H "Authorization: Bearer $TOKEN" -H "Content-Type: application/json" \
  http://127.0.0.1:4242/v1/tools/call \
  -d '{"name":"ctx_cost","arguments":{"action":"status"}}' | python3 -m json.tool
```

Expected: totalTokens > 0.

- [x] **Step 8: Commit**

```bash
git add client/src/hook_handlers.rs
git commit -m "fix: read usage.input_tokens/output_tokens in PostToolUse hook

Claude Code sends token counts in a nested 'usage' object. The hook was
only reading byte-length proxies from tool_input/tool_output, producing
near-zero token tracking. Now tries usage.{input,output}_tokens first
with byte-length fallback.

Co-authored-by: Copilot <223556219+Copilot@users.noreply.github.com>"
```

---

### Task 10: Fix ctx_knowledge Local Fallback (Task A)

**Files:** `client/src/mcp_server/mod.rs`

**Context:** `ctx_knowledge` is in `CLOUD_PREFERRED_TOOLS` and silently falls back to local `knowledge.json` when the cloud call fails. When a server is configured, it should behave as cloud-only.

- [x] **Step 1: Find the fallback logic**

```bash
grep -n "CLOUD_PREFERRED\|cloud_preferred\|ctx_knowledge\|fallback" client/src/mcp_server/mod.rs | head -20
```

Note the line number of the fallback branch (~line 233 from HANDOVER.md).

- [x] **Step 2: Read the routing logic around line 233**

```bash
sed -n '220,260p' client/src/mcp_server/mod.rs
```

- [x] **Step 3: Apply the fix**

When `ServerClient::load()` succeeds AND the tool is in `CLOUD_PREFERRED_TOOLS`, skip the local fallback and return the cloud error to the caller instead of silently falling back. The pattern should be:

```rust
// Before (roughly):
if is_cloud_preferred(tool_name) {
    if let Ok(client) = ServerClient::load() {
        match client.call_tool(tool_name, args, &ctx) {
            Ok(result) => return Ok(result),
            Err(_) => { /* fall through to local */ }
        }
    }
    // falls through to local handling
}

// After:
if is_cloud_preferred(tool_name) {
    match ServerClient::load() {
        Ok(client) => {
            // Cloud is configured — use it exclusively, surface errors
            return client.call_tool(tool_name, args, &ctx)
                .map_err(|e| anyhow!("Cloud call failed for {tool_name}: {e}"));
        }
        Err(_) => {
            // No cloud configured — fall through to local
        }
    }
}
```

Find the exact code and apply the matching change.

- [x] **Step 4: Build**

```bash
cargo build --manifest-path client/Cargo.toml 2>&1 | tail -5
cargo test --manifest-path client/Cargo.toml --lib 2>&1 | tail -10
```

Expected: 0 errors.

- [x] **Step 5: Reinstall and verify no local fallback warning**

```bash
cargo install --path client/
```

In an active MCP session, call `ctx_knowledge(action="recall", query="test")`. Confirm there is no `⚠ Running locally` output.

- [x] **Step 6: Commit**

```bash
git add client/src/mcp_server/mod.rs
git commit -m "fix(Task A): route ctx_knowledge to cloud-only when server is configured

When a ServerClient is configured, CLOUD_PREFERRED_TOOLS must not fall
back to local storage. Previously ctx_knowledge silently used local
knowledge.json on any cloud error, creating hidden divergence between
local and PostgreSQL. Now surfaces cloud errors directly so the caller
knows the cloud is unavailable.

Co-authored-by: Copilot <223556219+Copilot@users.noreply.github.com>"
```

---

### Task 11: Fix Autopilot Consolidation → PostgreSQL Bridge (Task B)

**Files:** `client/src/mcp_server/mod.rs`

**Context:** The auto-consolidation loop (~line 499) fires `consolidate_latest()` and writes only to local `knowledge.json`. After promotion, it should call `post_promoted_facts_to_cloud()` (same as the Stop hook does).

- [x] **Step 1: Find the auto-consolidation loop**

```bash
sed -n '490,530p' client/src/mcp_server/mod.rs
```

Note the structure: it calls `consolidate_latest()` and checks `promoted > 0`.

- [x] **Step 2: Read the Stop hook bridge code for reference**

```bash
grep -n "post_promoted_facts_to_cloud\|post_knowledge_to_cloud\|post_session_to_brain" client/src/hook_handlers.rs
```

Identify the exact function call used in the Stop hook.

- [x] **Step 3: Apply the bridge**

After the `consolidate_latest()` call in the autopilot loop, add the same cloud bridge that `handle_stop()` uses:

```rust
// After consolidate_latest() returns promoted count:
if promoted > 0 {
    // Bridge newly promoted facts to PostgreSQL (same as Stop hook)
    cloud_client::post_knowledge_to_cloud(&project_root_str);
}
```

Find the exact context and apply the matching change.

- [x] **Step 4: Build and test**

```bash
cargo build --manifest-path client/Cargo.toml 2>&1 | tail -5
cargo test --manifest-path client/Cargo.toml --lib 2>&1 | tail -10
```

Expected: 0 errors.

- [x] **Step 5: Reinstall**

```bash
cargo install --path client/
```

- [x] **Step 6: Commit**

```bash
git add client/src/mcp_server/mod.rs
git commit -m "fix(Task B): bridge autopilot consolidation to PostgreSQL

The mid-session auto-consolidation loop promoted facts to local
knowledge.json but never called post_knowledge_to_cloud(). The Stop hook
already did this correctly. Align the autopilot loop to also call the
cloud bridge after every successful promotion batch.

Co-authored-by: Copilot <223556219+Copilot@users.noreply.github.com>"
```

---

### Task 12: Re-run Integration Tests to Verify All Fixes

**Files:** None

- [x] **Step 1: Rebuild container with all server fixes**

```bash
podman build -t nebu-ctx-server -f Dockerfile .
podman stop nebu-ctx-local || true
podman rm nebu-ctx-local || true
podman run -d --name nebu-ctx-local \
  -p 127.0.0.1:3333:3333 -p 127.0.0.1:4242:4242 \
  --env-file .env \
  nebu-ctx-server
sleep 5
```

- [x] **Step 2: Re-run cloud bind**

```bash
nebu-ctx cloud bind 2>&1
```

Expected: JSON response, no error.

- [x] **Step 3: Re-run stop hook → brain snapshot**

```bash
echo '{}' | nebu-ctx hook stop
TOKEN=$(grep '^NEBULA_CTX_HTTP_TOKEN=' .env | cut -d= -f2)
curl -sX POST -H "Authorization: Bearer $TOKEN" -H "Content-Type: application/json" \
  http://127.0.0.1:4242/v1/tools/call \
  -d '{"name":"ctx_brain","arguments":{"action":"recall","query":"session"}}' \
  | python3 -m json.tool | head -10
```

Expected: session key present.

- [x] **Step 4: Re-run token tracking test**

```bash
echo '{"tool_name":"ctx_read","usage":{"input_tokens":123,"output_tokens":456}}' \
  | nebu-ctx hook post-tool-use
TOKEN=$(grep '^NEBULA_CTX_HTTP_TOKEN=' .env | cut -d= -f2)
curl -s -H "Authorization: Bearer $TOKEN" -H "Content-Type: application/json" \
  http://127.0.0.1:4242/v1/tools/call \
  -d '{"name":"ctx_cost","arguments":{"action":"status"}}' | python3 -m json.tool
```

Expected: totalTokens > 0.

- [x] **Step 5: Run full Rust test suite**

```bash
cargo test --manifest-path client/Cargo.toml 2>&1 | tail -20
```

Expected: all tests pass.

- [x] **Step 6: Run .NET test suite**

```bash
dotnet vstest server/tests/*/bin/Debug/net10.0/*.dll --logger:"console;verbosity=detailed" 2>&1 | tail -20
```

Expected: all tests pass.

---

## Phase 3 — CLI Cleanup

### Task 13: Fix `nebu-ctx on` — Unknown Command

**Root cause:** `nebu-ctx on` is documented in help as `nebu-ctx-on` (a shell function). The binary does not recognize `on` as a subcommand. When users type `nebu-ctx on`, they get "unknown command 'on'". Either add a thin subcommand that explains the shell function, or improve the error to guide the user.

**Files:**
- Modify: `client/src/main.rs`

- [x] **Step 1: Write failing test**

```bash
nebu-ctx on 2>&1
```

Expected currently: `nebu-ctx: unknown command 'on'\n` (unhelpful).

- [x] **Step 2: Add `on`/`off` as informational subcommands**

In `client/src/main.rs`, add handlers in the main `match command` block (before the `_ =>` fallthrough):

```rust
"on" | "off" => {
    let state = if command == "on" { "ON" } else { "OFF" };
    eprintln!("nebu-ctx: `nebu-ctx {command}` is a shell function, not a CLI command.");
    eprintln!("  To activate, run:  eval \"$(nebu-ctx init bash)\"  (or fish/zsh/powershell)");
    eprintln!("  Then use: nebu-ctx-{state_cmd} to toggle", state_cmd = command);
    eprintln!("  Or: nebu-ctx init --global  to install aliases permanently");
    std::process::exit(1);
}
```

Exact implementation:

```rust
"on" => {
    eprintln!("nebu-ctx: `nebu-ctx on` is a shell function, not a binary command.");
    eprintln!("  Run: eval \"$(nebu-ctx init bash)\"  (or fish/zsh/powershell)");
    eprintln!("  Then type: nebu-ctx-on");
    eprintln!("  Or install permanently: nebu-ctx init --global");
    std::process::exit(1);
}
"off" => {
    eprintln!("nebu-ctx: `nebu-ctx off` is a shell function, not a binary command.");
    eprintln!("  Run: eval \"$(nebu-ctx init bash)\"  (or fish/zsh/powershell)");
    eprintln!("  Then type: nebu-ctx-off");
    eprintln!("  Or install permanently: nebu-ctx init --global");
    std::process::exit(1);
}
```

- [x] **Step 3: Build and verify**

```bash
cargo build --manifest-path client/Cargo.toml 2>&1 | tail -3
```

```bash
# Test new behavior — install temp binary:
cargo install --path client/ --quiet
nebu-ctx on 2>&1
nebu-ctx off 2>&1
```

Expected: helpful error message pointing to shell init, exit code 1.

- [x] **Step 4: Run tests**

```bash
cargo test --manifest-path client/Cargo.toml --lib 2>&1 | tail -10
```

- [x] **Step 5: Commit**

```bash
git add client/src/main.rs
git commit -m "fix: add helpful error for 'nebu-ctx on/off' (shell function, not subcommand)

Users type 'nebu-ctx on' expecting the shell hook to activate, but these
are shell functions generated by 'nebu-ctx init'. Instead of 'unknown
command', show a clear message pointing to 'nebu-ctx init' and 'nebu-ctx-on'.

Co-authored-by: Copilot <223556219+Copilot@users.noreply.github.com>"
```

---

### Task 14: Remove Ghost Legacy Commands from Main Dispatch

**Context:** Five legacy commands (`login`, `register`, `forgot-password`, `contribute`, `upgrade`) are wired into the main dispatch with either "removed" error messages or silent redirects. They confuse the surface area and clutter the router. `upgrade` redirects to `update`. The other four print "removed" messages.

**Files:**
- Modify: `client/src/main.rs`

- [x] **Step 1: Verify current behavior of each legacy command**

```bash
nebu-ctx login 2>&1
nebu-ctx register 2>&1
nebu-ctx forgot-password 2>&1
nebu-ctx contribute 2>&1
nebu-ctx upgrade 2>&1 | head -3
```

- [x] **Step 2: Remove `login`, `register`, `forgot-password`, `contribute`, `upgrade` from main dispatch**

In `client/src/main.rs`, remove these match arms entirely:

```rust
// DELETE these match arms:
"login" => {
    cli::cloud::cmd_login(&rest);
    return;
}
"register" => {
    cli::cloud::cmd_register(&rest);
    return;
}
"forgot-password" => {
    cli::cloud::cmd_forgot_password(&rest);
    return;
}
"sync" => {
    cli::cloud::cmd_sync();
    return;
}
"contribute" => {
    cli::cloud::cmd_contribute();
    return;
}
"upgrade" => {
    cmd_upgrade();
    return;
}
```

**Important:** `sync` is a top-level alias for `cloud sync` — keep it only if it works after the cloud bind fix (Task 8). If it works, keep the `sync` dispatch but remove the others.

After removal, the `_ =>` fallthrough will print "unknown command" for these. That is acceptable — the user is directed to the help text.

- [x] **Step 3: Remove unused functions from `cli/cloud.rs`**

In `client/src/cli/cloud.rs`, remove:
- `pub fn cmd_login(_args: &[String])` and `removed_cloud_command("login")` call
- `pub fn cmd_forgot_password(_args: &[String])` 
- `pub fn cmd_register(_args: &[String])`
- `pub fn cmd_contribute()`
- `fn removed_cloud_command(command: &str)` if no longer referenced
- `pub fn cmd_upgrade()` in `main.rs` (the `fn cmd_upgrade()` function)

- [x] **Step 4: Build and verify no compile errors**

```bash
cargo build --manifest-path client/Cargo.toml 2>&1 | tail -10
```

Expected: 0 errors, 0 warnings (fix any unused-import warnings).

- [x] **Step 5: Verify old commands now show a consistent error**

```bash
cargo install --path client/ --quiet
nebu-ctx login 2>&1
nebu-ctx register 2>&1
nebu-ctx contribute 2>&1
```

Expected: `nebu-ctx: unknown command 'login'\n` + help text. This is cleaner than a custom "removed" message.

- [x] **Step 6: Run tests**

```bash
cargo test --manifest-path client/Cargo.toml 2>&1 | tail -10
```

- [x] **Step 7: Commit**

```bash
git add client/src/main.rs client/src/cli/cloud.rs
git commit -m "chore: remove legacy ghost commands from CLI dispatch

Remove login, register, forgot-password, contribute, and upgrade from the
main dispatch. These were stubs printing 'removed' messages or silent
redirects. They no longer serve a purpose and clutter the command router.
The fallthrough 'unknown command' handler guides users to the help text.

Co-authored-by: Copilot <223556219+Copilot@users.noreply.github.com>"
```

---

### Task 15: Clean Up Help Text and CLOUD Section

**Context:** The help text in `client/src/cli/dispatch.rs` (and `main.rs::print_help()`) references `nebu-ctx-on`, `nebu-ctx-off`, `lean-ctx-mode`, the `CLOUD` section, and removed commands. Bring it in sync with what actually works.

**Files:**
- Modify: `client/src/main.rs` (the `print_help()` function)
- Modify: `client/src/cli/dispatch.rs` (the `print_dispatch_help()` or similar)

- [x] **Step 1: Audit the help text**

```bash
nebu-ctx --help 2>&1 | grep -n "lean-ctx\|nebu-ctx-on\|nebu-ctx-off\|login\|register\|contribute\|upgrade\|forgot"
```

Note every occurrence of:
- `lean-ctx-mode` references (old branding)
- `login`, `register`, `contribute`, `forgot-password` (removed)
- `upgrade` (renamed to `update`)
- Any outdated `CLOUD` section entries

- [x] **Step 2: Update the help text**

In `print_help()` in `client/src/main.rs`:

1. Remove references to `lean-ctx-mode track/compress/off`
2. Remove `login`, `register`, `forgot-password`, `contribute` from any documented command lists
3. Change `upgrade` to `update` in example lines
4. Update `CLOUD` section to show only working commands:
   ```
   CLOUD:
       cloud connect [--endpoint <url>] [--token <token>]  Save and validate a cloud connection
       cloud status                   Show cloud connection status
       cloud bind                     Bind the current checkout to a canonical project
       cloud sync                     Sync current checkout state to the cloud
       cloud disconnect               Remove the saved cloud connection
   ```
5. In the shell init section, keep `nebu-ctx-on` / `nebu-ctx-off` but add a note that these are shell functions:
   ```
   SHELL FUNCTIONS (only available after: eval "$(nebu-ctx init bash)"):
       nebu-ctx-on     Enable shell aliases in track mode
       nebu-ctx-off    Disable all shell aliases
   ```

- [x] **Step 3: Build and verify help output**

```bash
cargo build --manifest-path client/Cargo.toml 2>&1 | tail -3
cargo install --path client/ --quiet
nebu-ctx --help 2>&1 | grep -c "lean-ctx"
```

Expected: 0 (no more lean-ctx references in help).

```bash
nebu-ctx --help 2>&1 | grep -c "login\|register\|contribute\|forgot"
```

Expected: 0 references to removed commands.

- [x] **Step 4: Run tests**

```bash
cargo test --manifest-path client/Cargo.toml 2>&1 | tail -10
```

- [x] **Step 5: Commit**

```bash
git add client/src/main.rs client/src/cli/dispatch.rs
git commit -m "chore: clean up help text — remove lean-ctx references and removed commands

- Remove lean-ctx-mode references (old branding)
- Remove login/register/contribute/forgot-password from documented commands
- Update CLOUD section to only show working commands
- Clarify nebu-ctx-on/off are shell functions requiring init

Co-authored-by: Copilot <223556219+Copilot@users.noreply.github.com>"
```

---

### Task 16: Final CLI Smoke Test

**Files:** None

- [x] **Step 1: Verify all canonical commands work**

```bash
nebu-ctx --version
nebu-ctx --help | head -5
nebu-ctx cloud status
nebu-ctx cloud bind
nebu-ctx sync
nebu-ctx doctor
nebu-ctx status
nebu-ctx gain report
```

Expected: all return sensible output, no panics.

- [x] **Step 2: Verify cleaned-up commands give clean errors**

```bash
nebu-ctx on 2>&1 | head -3        # helpful "shell function" message
nebu-ctx off 2>&1 | head -3       # helpful "shell function" message
nebu-ctx login 2>&1 | head -2     # "unknown command"
nebu-ctx register 2>&1 | head -2  # "unknown command"
```

Expected: helpful errors, no panics, exit code 1.

- [x] **Step 3: Run full test suite one final time**

```bash
cargo test --manifest-path client/Cargo.toml 2>&1 | tail -10
dotnet vstest server/tests/*/bin/Debug/net10.0/*.dll --logger:"console;verbosity=detailed" 2>&1 | tail -10
```

Expected: all pass.

---

## Phase 4 — Release

### Task 17: Version Bump to 0.5.7

All three locations must be updated in one commit.

**Files:**
- Modify: `client/Cargo.toml` — bump `version = "0.5.7"`
- Modify: `homeassistant/config.yaml` — bump `version: "0.5.7"`
- Modify: `server/src/NebuCtx.Application/ToolRegistry.cs` — bump `Current = "0.5.7"`

- [x] **Step 1: Update client/Cargo.toml**

Change `version = "0.5.6"` to `version = "0.5.7"` in `client/Cargo.toml`.

- [x] **Step 2: Update homeassistant/config.yaml**

Change `version: "0.5.6"` to `version: "0.5.7"` in `homeassistant/config.yaml`.

- [x] **Step 3: Update ToolRegistry.cs**

Change `Current = "0.5.6"` to `Current = "0.5.7"` in `server/src/NebuCtx.Application/ToolRegistry.cs`.

- [x] **Step 4: Verify all three locations match**

```bash
grep 'version = "0.5.7"' client/Cargo.toml
grep 'version: "0.5.7"' homeassistant/config.yaml
grep '"0.5.7"' server/src/NebuCtx.Application/ToolRegistry.cs
```

Expected: each grep returns one line.

- [x] **Step 5: Build the client to update Cargo.lock**

```bash
cargo build --manifest-path client/Cargo.toml 2>&1 | tail -5
```

- [x] **Step 6: Final test run**

```bash
cargo test --manifest-path client/Cargo.toml --lib 2>&1 | tail -5
dotnet build server/NebuCtx.slnx -p:AllowMissingPrunePackageData=true 2>&1 | tail -5
```

- [x] **Step 7: Commit all three version files together**

```bash
git add client/Cargo.toml client/Cargo.lock homeassistant/config.yaml \
  server/src/NebuCtx.Application/ToolRegistry.cs
git commit -m "release: bump version to 0.5.7

Fixes in this release:
- fix: cloud bind/sync 'failed to parse server response' (duplicate JSON field)
- fix: token tracking now reads usage.input_tokens/output_tokens
- fix: ctx_knowledge routes cloud-only when server is configured (Task A)
- fix: autopilot consolidation bridges to PostgreSQL (Task B)
- fix: 'nebu-ctx on/off' shows helpful shell-function guidance
- chore: remove legacy CLI commands (login, register, contribute, etc.)
- chore: clean up help text references to lean-ctx and removed commands

Co-authored-by: Copilot <223556219+Copilot@users.noreply.github.com>"
```

- [x] **Step 8: Push to main to trigger auto-release**

```bash
git push origin main
```

Expected: `auto-release.yml` detects the version bump, verifies all 3 locations sync, creates tag `0.5.7`, dispatches `release.yml`.

- [x] **Step 9: Watch the release pipeline**

```bash
gh run list --limit 5
```

Verify `auto-release` and `release` workflows start and pass.

---

## Summary of Changes

| Component | File(s) | Change |
|-----------|---------|--------|
| Server | `ProjectResolutionContracts.cs` | `[JsonIgnore]` on `WorkspaceBound` response field |
| Client | `hook_handlers.rs` | Read `usage.{input,output}_tokens` for token tracking |
| Client | `mcp_server/mod.rs` | Cloud-only routing for `ctx_knowledge` (Task A) |
| Client | `mcp_server/mod.rs` | Autopilot consolidation bridges to PostgreSQL (Task B) |
| Client | `main.rs` | Add helpful `on`/`off` subcommand errors |
| Client | `main.rs`, `cli/cloud.rs` | Remove legacy ghost commands |
| Client | `main.rs`, `cli/dispatch.rs` | Clean up help text |
| Version | 3 files | Bump to 0.5.7 |
