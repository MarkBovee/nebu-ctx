# nebu-ctx — Handover & Continuation Guide

> Last updated: 2026-04-29 · Version: 0.5.6

This document captures the current state of the project and what to do next. Read this when picking up after a break.

---

## What We Have

### Architecture

| Layer | Technology | Location |
|-------|-----------|----------|
| CLI client | Rust binary `nebu-ctx` | `client/` · published to crates.io |
| MCP / dashboard host | .NET 10 (ASP.NET) | `server/` · published to GHCR as `ghcr.io/markbovee/nebu-ctx` |
| Container packaging | Multi-stage Dockerfile (SDK build → Alpine runtime) | `Dockerfile` (root) |
| Home Assistant add-on | Pulls GHCR image directly — no local build | `homeassistant/config.yaml` |

The Rust client is thin: it installs shell hooks, writes MCP configs for all supported editors, and proxies MCP tool calls to the .NET host over HTTP.

The .NET host serves:
- MCP HTTP endpoint on port `4242`
- Dashboard UI on port `3333`
- PostgreSQL-backed storage (brain, knowledge, session, telemetry)

### Version Sync — Three Places Must Always Match

```
client/Cargo.toml                             version = "0.5.6"
homeassistant/config.yaml                     version: "0.5.6"
server/src/NebuCtx.Application/ToolRegistry.cs  Current = "0.5.6"
```

When bumping the version, update all three in one commit.

### What Is Installed Locally (This Linux Machine)

| Item | Location | Status |
|------|----------|--------|
| Rust client binary | `~/.cargo/bin/nebu-ctx` | ✅ installed v0.5.6 |
| Fish shell hook | `~/.nebu-ctx/shell-hook.fish` | ✅ active (`nebu-ctx: ON`) |
| Copilot CLI MCP config | `~/.copilot/mcp-config.json` | ✅ wired, all tools auto-approved |
| VS Code MCP config | `~/.config/Code/User/mcp.json` | ✅ wired |
| .NET server | Running locally in container `nebu-ctx-local` on ports 3333/4242 | ✅ live |
| Claude Code hooks | `Stop`, `PostToolUse`, `PreToolUse` (Bash + Read/View) | ✅ wired in `.claude/settings.local.json` |

The fish shell hook adds `~/.cargo/bin` to `fish_add_path`, so `nebu-ctx` should be available in new fish sessions.

If `nebu-ctx` is not found, run:
```fish
fish_add_path ~/.cargo/bin
```

### MCP Server (Copilot CLI)

The MCP server is now active in this Copilot CLI session. You should see all `ctx_*` tools available. They route to the .NET host. The server URL and auth token are read from the client's config at `~/.nebu-ctx/`.

To reconfigure or reset:
```bash
nebu-ctx setup --non-interactive --yes   # rewrites all editor MCP configs
nebu-ctx cloud status                    # shows server connection state
nebu-ctx doctor                          # full health check
```

---

## Next Session: Full Integration Test Plan

The next session should be a **structured end-to-end integration test** of everything shipped in v0.5.6. Run each test, note pass/fail, and iterate on failures.

### Pre-flight

```bash
nebu-ctx --version         # must show 0.5.6
nebu-ctx doctor            # check hooks, MCP configs, cloud connection
nebu-ctx cloud status      # must show connected + token valid
podman ps | grep nebu-ctx  # must show nebu-ctx-local running
```

### Test 1 — PostToolUse hook fires telemetry

Every tool call should fire `nebu-ctx hook post-tool-use` which POSTs to `/v1/telemetry/ingest`.

```bash
# Before: check baseline call count
curl -sH "Authorization: Bearer $TOKEN" http://127.0.0.1:3333/api/gain | head -5

# Make a tool call (any ctx_read, ctx_brain, etc.) in Claude Code / Copilot CLI
# After: verify count went up
curl -sH "Authorization: Bearer $TOKEN" http://127.0.0.1:3333/api/gain | head -5
```

Expected: call count increases. If not → check hook_handlers.rs `handle_post_tool_use()` and the `.claude/settings.local.json` PostToolUse entry.

### Test 2 — Stop hook fires brain snapshot

When a Claude Code session ends (`Stop` event fires → `nebu-ctx hook stop`):

```bash
# Trigger manually to test without ending session:
echo '{}' | nebu-ctx hook stop

# Then check brain for session entry
curl -sX POST -H "Authorization: Bearer $TOKEN" -H "Content-Type: application/json" \
  http://127.0.0.1:4242/v1/tools/call \
  -d '{"name":"ctx_brain","arguments":{"action":"recall","query":"session"}}' | python3 -m json.tool
```

Expected: a `session-<id>` key appears in brain results. If not → check `handle_stop()` in `hook_handlers.rs` and `post_session_to_brain()` in `cloud_client.rs`.

### Test 3 — Analytics tools return real data

```bash
TOKEN=$(grep '^NEBULA_CTX_HTTP_TOKEN=' .env | cut -d= -f2)
BASE="http://127.0.0.1:4242/v1/tools/call"
H='-H "Authorization: Bearer $TOKEN" -H "Content-Type: application/json"'

# ctx_gain
curl -s $H $BASE -d '{"name":"ctx_gain","arguments":{"action":"report"}}' | python3 -m json.tool

# ctx_cost
curl -s $H $BASE -d '{"name":"ctx_cost","arguments":{"action":"report"}}' | python3 -m json.tool

# ctx_heatmap
curl -s $H $BASE -d '{"name":"ctx_heatmap","arguments":{"action":"status"}}' | python3 -m json.tool

# ctx_stats
curl -s $H $BASE -d '{"name":"ctx_stats","arguments":{"action":"report"}}' | python3 -m json.tool

# Per-project stats REST endpoint
curl -s -H "Authorization: Bearer $TOKEN" \
  "http://127.0.0.1:3333/api/projects/nebu-ctx/stats" | python3 -m json.tool
```

Expected: non-zero counts. If zero → use ctx_gain with `action="report"` in an active session first to generate telemetry, then re-test.

### Test 4 — Token tracking (known gap)

Most tool calls currently show `0` for input/output tokens in `ctx_cost` — the PostToolUse hook uses `tool_input`/`tool_output` byte-length as a rough proxy but many tools don't populate those fields.

```bash
curl -sH "Authorization: Bearer $TOKEN" http://127.0.0.1:3333/api/gain \
  | python3 -c "import sys,json; d=json.load(sys.stdin); print(d)"
```

Check if `totalTokens > 0`. If still 0 → investigate `handle_post_tool_use()` stdin parsing. The hook reads `tool_input` and `tool_output` JSON fields — check if Claude Code / Copilot CLI actually sends those field names in the hook event JSON.

Fix if needed: adjust field extraction in `hook_handlers.rs` to match the actual hook payload schema.

### Test 5 — ctx_knowledge cloud routing

Verify `ctx_knowledge` goes to cloud (not local fallback) when server is reachable:

```bash
# In Claude Code / Copilot CLI:
ctx_knowledge(action="remember", category="testing", key="integration-test", value="2026-04-29 passed")
ctx_knowledge(action="recall", query="integration-test")
```

Expected: no `⚠ Running locally` warning in output. Check dashboard knowledge page to confirm it landed in PostgreSQL.

### Test 6 — Dashboard accuracy

Open `http://127.0.0.1:3333` and verify:
- **Gain** page shows activity score + agent breakdown
- **Heatmap** page shows file access counts
- **Projects** page shows per-project stats
- **Brain** page shows stored memories including session entries

### Known Remaining Issues

| Issue | Location | Priority |
|-------|----------|----------|
| Token tracking shows 0 for most tools | `hook_handlers.rs::handle_post_tool_use` | High |
| `ctx_knowledge` local fallback when cloud configured (Task A) | `mcp_server/mod.rs` ~line 233 | Medium |
| Consolidation → PostgreSQL bridge (Task B) | `mcp_server/mod.rs` autopilot loop | Medium |

---

## What Was Built in v0.5.6 (this sprint)

### Cloud Analytics Tools (4 new MCP tools)

| Tool | Action | Description |
|------|--------|-------------|
| `ctx_gain` | report/score/tasks/agents/wrapped/json | Per-project activity score + agent breakdown |
| `ctx_cost` | report/tools/status/json | Token usage + $2.50/1M cost estimate |
| `ctx_heatmap` | status/directory/dirs/cold/json | File-access frequency heatmap |
| `ctx_stats` | report/json | Unified per-project telemetry snapshot |

REST endpoint added: `GET /api/projects/{projectId}/stats` → always 200, zeros for unknown projects.

### TelemetryStore Changes

- `_projectCommands` dict: per-project tool call counters
- `_fileAccessCounts` dict: `(projectId, path) → count`
- `FileAccessTools` FrozenSet: 12 read/edit tools that track file access
- `IngestEvent` now updates `_projectCommands` (Rust client sessions feed analytics)
- `ProjectTelemetrySnapshot` + `PerProject` on `Snapshot`

### Hook Automation (complete)

| Hook | Event | Handler | Does |
|------|-------|---------|------|
| `PreToolUse: Bash` | Before every shell command | `hook rewrite` | Rewrites to compression-aware form |
| `PreToolUse: Read/View/Grep` | Before every file read | `hook redirect` | Redirects native reads to MCP |
| `PostToolUse: .*` | After every tool call | `hook post-tool-use` | Fires telemetry to `/v1/telemetry/ingest` |
| `Stop` | Session end | `hook stop` | Consolidates knowledge → `ctx_knowledge` + snapshots session → `ctx_brain` |

Stop hook was previously broken: `promoted == 0` early return silently skipped brain snapshot every session that had no new knowledge promotions. Fixed in `7b1997c`.

### InputSchema Fix

All 4 analytics handlers had `InputSchema` at root level (not wrapped in `type:object/properties`). Fixed in `caa86f8` — all handlers now follow the MCP JSON Schema envelope pattern:

```csharp
public Dictionary<string, object?> InputSchema => new()
{
    ["type"] = "object",
    ["properties"] = new Dictionary<string, object?>
    {
        ["action"] = new Dictionary<string, object?> { ["type"] = "string", ... },
        ["project_id"] = new Dictionary<string, object?> { ["type"] = "string", ... },
    },
    ["required"] = new[] { "action" },
};
```

---

## Brain Automation — Current State

### What is automated now

1. **PostToolUse hook** → `handle_post_tool_use()` → `TelemetryStore.IngestEvent()` → per-project counters in memory
2. **Stop hook** → `handle_stop()`:
   - `consolidate_latest()` → promotes session facts to local `knowledge.json`
   - If `promoted > 0`: `post_promoted_facts_to_cloud()` → `ctx_knowledge` (PostgreSQL)
   - Always: `SessionState::load_latest_for_project_root()` + `post_session_to_brain()` → `ctx_brain` (PostgreSQL)

### What is still manual / not bridged

**Task A — ctx_knowledge local fallback:** `ctx_knowledge` is in `CLOUD_PREFERRED_TOOLS` and silently falls back to local `knowledge.json` when cloud call fails. In a setup where cloud is always on, this creates hidden divergence. Fix: in `mcp_server/mod.rs` ~line 233, if `ServerClient::load()` succeeds treat `ctx_knowledge` like a cloud-only tool.

**Task B — Autopilot consolidation → PostgreSQL:** The auto-consolidation loop in `mcp_server/mod.rs` (~line 499) fires `consolidate_latest()` and writes only to local `knowledge.json`. After promotion, the loop should call `post_promoted_facts_to_cloud()` to bridge to PostgreSQL. (The Stop hook already does this — the autopilot mid-session loop does not.)



---

### 1. Token Tracking Shows 0 for Most Tools

`ctx_cost` shows near-zero token usage because `handle_post_tool_use()` extracts token count from `tool_input`/`tool_output` byte length in the hook event JSON. Many Claude Code / Copilot CLI tool events don't populate those fields with the actual content (they send metadata only). The raw byte proxy may be using wrong field names.

To investigate:
```bash
# Log what the hook actually receives from Claude Code
echo '{"tool_name":"ctx_read","tool_input":{"path":"test"},"tool_output":"..."}' | nebu-ctx hook post-tool-use
# Compare to what Claude Code actually sends — add a debug log line temporarily
```

Files: `client/src/hook_handlers.rs::handle_post_tool_use()`

### 2. ctx_knowledge Local Fallback (Task A)

See "Brain Automation — Current State" above.

### 3. Autopilot Consolidation → PostgreSQL Gap (Task B)

See "Brain Automation — Current State" above.

### 4. Home Assistant Add-on Verification

After v0.5.6 GHCR image is published via CI:
- Test add-on discovery in HA (should appear in store)
- Test install — HA should pull `ghcr.io/markbovee/nebu-ctx:0.5.6` automatically
- Verify dashboard on port 3333 and MCP on port 4242

---

## CI/CD Pipeline

### How It Works

Every push to `main` that touches `client/`, `server/`, `homeassistant/`, or workflow files triggers `auto-release.yml`:

1. **Version sync check** — verifies all 3 version locations match (fails fast if not)
2. **Tag check** — if the tag already exists, stops (no duplicate release)
3. **Tag + dispatch** — creates the git tag and immediately dispatches `release.yml` via `workflow_dispatch`

`release.yml` then runs in parallel:
- `build` — compiles Rust client for amd64 + arm64
- `release` — creates GitHub release with binaries
- `publish-crate` — publishes to crates.io (requires `CARGO_REGISTRY_TOKEN` secret ✅ set)
- `publish-server-image` — builds multi-platform Docker image, pushes to GHCR

> **Why `workflow_dispatch` and not the tag push event?**
> `GITHUB_TOKEN` cannot trigger other workflows via push events (GitHub's loop prevention). We explicitly dispatch `release.yml` with `gh workflow run` after tagging.

### To Release a New Version

1. Bump all 3 version locations in one commit:
   - `client/Cargo.toml` → `version = "X.Y.Z"`
   - `homeassistant/config.yaml` → `version: "X.Y.Z"`
   - `server/src/NebuCtx.Application/ToolRegistry.cs` → `Current = "X.Y.Z"`
2. Push to `main` — `auto-release.yml` handles the rest automatically.

No `refresh-dist.sh` needed. No manual tag push needed.

---

## Build & Validation Commands

### crates.io Publish Token

The `CARGO_REGISTRY_TOKEN` GitHub Actions secret is **already set**. If it ever needs to be rotated:

1. Go to https://crates.io/settings/tokens → create token with **Publish new crates** + **Publish updates** scopes.
2. `gh secret set CARGO_REGISTRY_TOKEN --repo MarkBovee/nebu-ctx --body "<token>"`

```bash
# Rust client
cargo test --manifest-path client/Cargo.toml --lib
cargo test --manifest-path client/Cargo.toml --test setup_ci_smoke -- --nocapture
cargo install --path client/

# .NET server
dotnet build server/NebuCtx.slnx -p:AllowMissingPrunePackageData=true
dotnet vstest server/tests/*/bin/Debug/net10.0/*.dll --logger:"console;verbosity=detailed"

# Container (local build from source)
podman build -t nebu-ctx-server -f Dockerfile .
bash tests/local-addon-test.sh
```

> **Note:** On this Linux machine, `dotnet test` output is silently swallowed — use `dotnet vstest` with the built DLLs instead.

---

## Rename Notes (lean-ctx → nebu-ctx)

The branding rename from `lean-ctx` to `nebu-ctx` is complete in all user-visible surfaces. Key backward-compat notes:

- Internal Rust crate lib name stays `lean_ctx` (used in `use lean_ctx::` imports across tests)
- Writers do backward-compat reads: `.get("nebu-ctx").or_else(|| .get("lean-ctx"))` for existing user configs
- Rule file markers: both `MARKER` (`nebu-ctx`) and `LEGACY_MARKER` (`lean-ctx`) are detected in user files

---

## Repository Structure

```
client/           Rust thin client (CLI, shell hooks, MCP config writers)
server/           .NET MCP host + dashboard
  src/
    NebuCtx.Server.Host/     ASP.NET entrypoint
    NebuCtx.Application/     Tool registry, routing, MCP handlers
    NebuCtx.Storage.*/       Postgres-backed stores
  tests/                     .NET contract + integration tests
homeassistant/    HA add-on packaging (config.yaml, README — no Dockerfile needed)
Dockerfile        Multi-stage build: SDK → Alpine runtime → GHCR image
tests/            Cross-stack smoke tests (local-addon-test.sh, etc.)
scripts/server/   Build/publish scripts (local dev only — CI builds from source)
docs/             Design docs and realignment plan
```

---

## Useful One-Liners

```bash
# Reinstall client after code changes
cargo install --path client/

# Re-run setup to update all editor MCP configs
nebu-ctx setup --non-interactive --yes

# Check what editors were detected and configured
nebu-ctx setup --non-interactive --json | python -m json.tool | grep -A3 '"editors"'

# Tail server logs from a running container (replace name as needed)
podman logs -f nebu-ctx-server

# Pull and run the latest published server image
podman pull ghcr.io/markbovee/nebu-ctx:latest
```
