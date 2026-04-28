# nebu-ctx — Handover & Continuation Guide

> Last updated: 2026-04-28 · Version: 0.5.5

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
client/Cargo.toml                             version = "0.5.5"
homeassistant/config.yaml                     version: "0.5.5"
server/src/NebuCtx.Application/ToolRegistry.cs  Current = "0.5.5"
```

When bumping the version, update all three in one commit.

### What Is Installed Locally (This Linux Machine)

| Item | Location | Status |
|------|----------|--------|
| Rust client binary | `~/.cargo/bin/nebu-ctx` | ✅ installed v0.5.5 |
| Fish shell hook | `~/.nebu-ctx/shell-hook.fish` | ✅ active (`nebu-ctx: ON`) |
| Copilot CLI MCP config | `~/.copilot/mcp-config.json` | ✅ wired, all tools auto-approved |
| VS Code MCP config | `~/.config/Code/User/mcp.json` | ✅ wired |
| .NET server | Not running locally (runs in container or on HA) | — |

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

## What Needs Testing

Nothing has been tested end-to-end yet on this machine. The following testing sessions are needed in order:

### 1. Client Smoke Test (local, no server)

```bash
# Verify binary works
nebu-ctx --version

# Doctor check — should show editors detected, hooks in place
nebu-ctx doctor

# Status — shows cloud connection, should show "not connected" if server is down
nebu-ctx cloud status
```

### 2. Server / Dashboard

The .NET server needs a running PostgreSQL instance. The fastest way to test locally:

```bash
# Build and run the server container (requires Postgres)
podman build -t nebu-ctx-server -f Dockerfile .
podman run -p 3333:3333 -p 4242:4242 \
  -e NEBULA_STORE=postgres \
  -e DATABASE_URL="postgres://user:pass@host/nebula" \
  -e NEBULA_MCP_AUTH_TOKEN=test \
  nebu-ctx-server
```

Or pull the published image:
```bash
podman run -p 3333:3333 -p 4242:4242 \
  -e NEBULA_STORE=postgres \
  -e DATABASE_URL="postgres://user:pass@host/nebula" \
  -e NEBULA_MCP_AUTH_TOKEN=test \
  ghcr.io/markbovee/nebu-ctx:0.5.5
```

Then open `http://127.0.0.1:3333` for the dashboard and test:
- Brain page — shows stored memories
- Knowledge page — shows project facts
- Sessions page

### 3. End-to-End via MCP Tools (Copilot CLI)

With the server running and the Copilot CLI session restarted (`/restart`), test each tool:

```
ctx_brain         — store and recall memories (requires Postgres)
ctx_knowledge     — remember/recall project facts
ctx_session       — save/load session state
ctx_overview      — project map (runs locally, no server needed)
ctx_shell         — run shell commands through the tool
ctx_read          — read and compress files
```

Quick smoke sequence:
1. `ctx_knowledge(action="remember", key="test", value="hello", category="testing")`
2. `ctx_knowledge(action="recall", query="test")`
3. `ctx_brain(action="store", key="handover-test", value="2026-04-28")`
4. `ctx_brain(action="recall", query="handover")`

### 4. Home Assistant Add-on

The add-on should be tested on the actual HA instance before the next release:

```bash
# Local add-on test (uses podman, requires .env with Postgres creds)
bash scripts/server/refresh-dist.sh
podman build -t nebu-ctx-addon-dev -f homeassistant/Dockerfile .
bash tests/local-addon-test.sh
```

Check:
- MCP endpoint responds: `curl http://127.0.0.1:4242/health`
- Dashboard loads: `curl http://127.0.0.1:3333`
- Auth token is printed to logs on startup
- Token persists across restarts (`/data/auth_token` inside container)

---

## Brain Automation — Why It's Not Fully Automatic

### What exists today

The client has **two auto-consolidation paths**, but both write to **local JSON files only** — not to the PostgreSQL brain:

1. **MCP server autopilot** (`mcp_server/mod.rs`): After every N tool calls (default: 25, cooldown: 120s), `should_auto_consolidate()` fires `consolidate_latest()`. This promotes session decisions + salient findings into the **local** `ProjectKnowledge` store (`~/.nebu-ctx/<project>/knowledge.json`).

2. **`ctx_knowledge(action="consolidate")`**: Same engine, callable manually via MCP tool.

### Why `ctx_brain` is NOT automated

`ctx_brain` is a **cloud-only tool** (`CLOUD_ONLY_TOOLS` in `mcp_server/mod.rs`). It routes to the PostgreSQL-backed server over HTTP. The consolidation engine (`consolidation_engine.rs`) only calls `ProjectKnowledge::save()` — which writes to a local JSON file (`knowledge.json`). There is **no bridge** between the local consolidation engine and `ctx_brain` / PostgreSQL.

So what we did manually at end-of-session (storing lessons to `ctx_brain` and `ctx_knowledge`) is **not automated**. The system consolidates to local JSON automatically, but the PostgreSQL brain requires explicit tool calls.

### Also: ctx_knowledge falls back to local JSON when cloud is available

`ctx_knowledge` is in `CLOUD_PREFERRED_TOOLS` (not `CLOUD_ONLY_TOOLS`). When the cloud server is reachable it routes to PostgreSQL, but when the cloud call fails it **silently falls back to local `knowledge.json`** with a warning appended to the output. This means knowledge facts can end up split across local files and PostgreSQL depending on connectivity at the time of the call. The warning text is:

> ⚠ Running locally (no cloud connection). Data stored in .nebu-ctx/ only.

**This must change.** In our setup the cloud server is always the source of truth. Local fallback creates hidden divergence.

### What needs to be built (tomorrow)

**Task A — Eliminate local fallback for ctx_knowledge when cloud is configured**

In `mcp_server/mod.rs`, the `CLOUD_PREFERRED_TOOLS` path (`route_to_cloud` → fallback on failure) should be split:
- If `ServerClient::load()` succeeds (cloud is configured), treat `ctx_knowledge` like a cloud-only tool — fail hard instead of silently writing local.
- Only use local fallback when no cloud server is configured at all.

File to change: `client/src/mcp_server/mod.rs` — `call_tool` routing block (~line 233).

**Task B — Consolidation → PostgreSQL bridge**

The autopilot consolidation loop (`mcp_server/mod.rs` ~line 499) fires `consolidate_latest()` which writes `knowledge.json` locally. After it runs, promoted facts should be forwarded to the cloud:

1. Add a `post_consolidation_to_cloud(outcome, project_root)` async fn in `cloud_client.rs` — iterates the just-promoted facts and calls `ctx_knowledge(action="remember")` for each via `route_to_cloud`.
2. Call it in the autopilot `tokio::task::spawn_blocking` block after `consolidate_latest()` succeeds.
3. Also call it from `ctx_knowledge(action="consolidate")` handler in `tools/ctx_knowledge.rs`.
4. Gate behind `autonomy.auto_brain_sync = true` (add to `AutonomyConfig` with default `true`).

**Task C — Session-end brain snapshot**

When `ctx_session(action="save")` is called, auto-post a summary to `ctx_brain`. In `tools/ctx_session.rs`, after the save completes, call `route_to_cloud("ctx_brain", {action:"store", key:"session-{id}", value:"{summary}"})`.

### Until tasks A/B/C are done: manual end-of-session ritual

```
ctx_session(action="save")
ctx_knowledge(action="consolidate")
ctx_brain(action="store", key="session-YYYY-MM-DD", value="<summary>")
```

---

## IDE Hook Expansion

### Current state (per IDE)

| IDE / Agent | Hook events wired | What they do |
|-------------|-------------------|--------------|
| **Claude Code** | `PreToolUse: Bash` → `hook rewrite`; `PreToolUse: Read/Grep/View/ListFiles` → `hook redirect` | Rewrites shell commands to route through nebu-ctx; redirects native file reads to MCP |
| **Copilot CLI** | `preToolUse` → `hook rewrite` + `hook redirect` | Same as Claude Code; written to `~/.github/hooks/hooks.json` |
| **Cursor** | `PreToolUse: Bash` → `hook rewrite` | Shell command rewriting only |
| **Gemini** | `PreToolUse: Bash` → `hook rewrite`; `PreToolUse: Read/Grep/...` → `hook redirect` | Full rewrite + redirect |
| **Codex** | `SessionStart` → `hook codex-session-start`; `PreToolUse: Bash` → `hook codex-pretooluse` | Session-start wakeup + command rewriting |
| **Windsurf / Cline / Roo** | Rules file injection only | No hook events; adds CLAUDE.md-style context |
| **Amp / JetBrains / Kiro / Crush / OpenCode / Hermes** | MCP server registration only | No hook events wired at all |

### What is missing

**1. `Stop` / session-end hook (Claude Code, Copilot CLI)**

Claude Code fires a `Stop` notification when the session ends. This is the perfect trigger to auto-consolidate to PostgreSQL. Currently **not wired** — the client never receives it because no `Stop` hook is registered.

To add:
- Claude Code: add `"Stop": [{"hooks": [{"type": "command", "command": "nebu-ctx hook stop"}]}]` to `settings.json` in `install_claude_hook_config()`
- Copilot CLI: add `"postSession"` entry to `.github/hooks/hooks.json` in `install_copilot_pretooluse_hook()`
- Add `handle_stop()` in `hook_handlers.rs` — calls `consolidate_latest()` then `post_consolidation_to_cloud()`
- Wire `"hook stop"` in `main.rs` dispatch

**2. `PostToolUse` hook (all IDEs)**

After every tool call, a `PostToolUse` event fires. Useful for:
- Emitting per-call telemetry (token counts, tool name) — currently only done via `fire_sync` in `-c`/`-t` paths
- Triggering incremental cloud sync of newly stored knowledge

Currently only wired in Codex tests — not in any production hook install.

**3. Missing agents have no hooks at all**

Amp, JetBrains, Kiro, Crush, OpenCode, Hermes — they register the MCP server but install **zero hook events**. This means:
- Shell command interception doesn't work
- File read redirection doesn't work
- Session-end consolidation won't work even after Task A/B/C above

Each needs at minimum `PreToolUse: Bash → hook rewrite` wired into their respective config format.

### Implementation plan (tomorrow)

**Step 1 — Add `Stop` hook to Claude Code and Copilot CLI**

Files: `client/src/hooks/agents.rs` (`install_claude_hook_config`, `install_copilot_pretooluse_hook`), `client/src/hook_handlers.rs`, `client/src/main.rs`

```rust
// hook_handlers.rs — new function
pub fn handle_stop() {
    // consolidate local session → knowledge.json
    // then POST promoted facts to cloud via route_to_cloud
}
```

```json
// Claude Code settings.json addition
"Stop": [{ "hooks": [{ "type": "command", "command": "nebu-ctx hook stop" }] }]
```

**Step 2 — Wire `PostToolUse` for telemetry in Claude Code and Copilot CLI**

Add `"PostToolUse": [{ "matcher": ".*", "hooks": [{ "type": "command", "command": "nebu-ctx hook post-tool-use" }] }]` and a `handle_post_tool_use()` in `hook_handlers.rs` that reads stdin JSON, extracts tool name + output length, and fires telemetry.

**Step 3 — Add `PreToolUse: Bash` hooks to Amp, Kiro, OpenCode, Hermes, Crush**

Each agent has its own config format — check the install functions in `hooks/agents.rs` and add the `hook rewrite` call to each.

**Step 4 — Run `nebu-ctx hooks install --all` after each change to re-deploy**

```bash
nebu-ctx hooks install claude --global
nebu-ctx hooks install copilot --global
# etc.
```

---

### 1. Rust Compiler Warnings (19 warnings in lib)

The Rust client build produces 19 warnings, concentrated in `client/src/core/knowledge_embedding.rs`. 13 are auto-fixable:

```bash
cargo fix --lib -p nebu-ctx --manifest-path client/Cargo.toml
```

Remaining manual fixes (dead code that needs a decision — keep or delete):
- `ALPHA_SEMANTIC`, `BETA_CONFIDENCE`, `GAMMA_RECENCY`, `MAX_RECENCY_DAYS` — unused constants
- `lexical_fallback`, `recency_decay` — unused functions
- `filter` parameter — unused in one function (prefix with `_` or remove)

These are clean-up tasks, not bugs. Fix, run `cargo test --manifest-path client/Cargo.toml`, then commit.

### 2. Home Assistant Add-on Verification

The HA add-on now uses `image: "ghcr.io/markbovee/nebu-ctx"` in `config.yaml` (no Dockerfile). After the v0.5.5 GHCR image is published:
- Test add-on discovery in HA (should appear in the store)
- Test install — HA should pull `ghcr.io/markbovee/nebu-ctx:0.5.5` automatically
- Verify dashboard on port 3333 and MCP on port 4242

### 3. End-to-End Testing Pass

See "What Needs Testing" section below — nothing has been fully validated end-to-end since the GHCR migration.

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
