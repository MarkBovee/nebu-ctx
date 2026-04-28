# nebu-ctx — Handover & Continuation Guide

> Last updated: 2026-04-28 · Version: 0.5.3

This document captures the current state of the project and what to do next. Read this when picking up after a break.

---

## What We Have

### Architecture

| Layer | Technology | Location |
|-------|-----------|----------|
| CLI client | Rust binary `nebu-ctx` | `client/` |
| MCP / dashboard host | .NET 10 (ASP.NET) | `server/` |
| Container packaging | Docker / Podman + Home Assistant add-on | `homeassistant/Dockerfile`, `docker-entrypoint.sh` |
| Published server payload | Committed binaries | `server/dist/linux/` |

The Rust client is thin: it installs shell hooks, writes MCP configs for all supported editors, and proxies MCP tool calls to the .NET host over HTTP.

The .NET host serves:
- MCP HTTP endpoint on port `4242`
- Dashboard UI on port `3333`
- PostgreSQL-backed storage (brain, knowledge, session, telemetry)

### Version Sync — Three Places Must Always Match

```
client/Cargo.toml                             version = "0.5.3"
homeassistant/config.yaml                     version: "0.5.3"
server/src/NebuCtx.Application/ToolRegistry.cs  Current = "0.5.3"
```

When bumping the version, update all three in one commit.

### What Is Installed Locally (This Linux Machine)

| Item | Location | Status |
|------|----------|--------|
| Rust client binary | `~/.cargo/bin/nebu-ctx` | ✅ installed v0.5.3 |
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
bash scripts/server/refresh-dist.sh          # rebuild server/dist/linux
podman build -t nebu-ctx-server -f homeassistant/Dockerfile .
podman run -p 3333:3333 -p 4242:4242 \
  -e NEBULA_STORE=postgres \
  -e DATABASE_URL="postgres://user:pass@host/nebula" \
  -e NEBULA_MCP_AUTH_TOKEN=test \
  nebu-ctx-server
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

## What Is Not Done Yet (Pending Work Items)

These are tracked in the session SQL database and in `docs/nebu-ctx-lean-ctx-realignment-plan.md`.

### WP6 — Telemetry Ingest

| Task | Description |
|------|-------------|
| `wp6-contract` | `TelemetryIngestRequest` DTO — **in progress** |
| `wp6-endpoint` | `POST /v1/telemetry/ingest` endpoint |
| `wp6-store` | `TelemetryStore.IngestEvent()` |
| `wp6-client` | Client-side telemetry emit |

### WP7 — Dashboard Real Data

| Task | Description |
|------|-------------|
| `wp7-knowledge-list` | `IKnowledgeStore.ListAllForProjectAsync()` |
| `wp7-brain-list` | `IBrainStore.ListAllAsync()` |
| `wp7-dashboard-knowledge` | Wire `/api/knowledge` to real Postgres facts |
| `wp7-dashboard-brain` | Add `/api/brain` endpoint |
| `wp7-dotnet-tests` | Run .NET test suite after WP7 |

### Distribution

| Task | Description |
|------|-------------|
| `wp-dist-rebuild` | Rebuild `server/dist/linux/` and run smoke after WP6/WP7 land |

---

## Build & Validation Commands

### crates.io Publish Token

The `publish-crate` job in `release.yml` requires a `CARGO_REGISTRY_TOKEN` GitHub Actions secret.

1. Go to https://crates.io/settings/tokens and create a token with **Publish new crates** and **Publish updates** scopes.
2. In GitHub: Settings → Secrets and variables → Actions → New repository secret.
   - Name: `CARGO_REGISTRY_TOKEN`
   - Value: the token from step 1.

Without this secret, or if the version is already published, the `publish-crate` job will fail and the overall workflow run will show as failed. The GitHub release assets (binaries) are published by the `release` job before `publish-crate` runs, so binaries will still be available.

```bash
# Rust client
cargo test --manifest-path client/Cargo.toml --lib
cargo test --manifest-path client/Cargo.toml --test setup_ci_smoke -- --nocapture
cargo install --path client/

# .NET server
dotnet build server/NebuCtx.slnx -p:AllowMissingPrunePackageData=true
dotnet vstest server/tests/*/bin/Debug/net10.0/*.dll --logger:"console;verbosity=detailed"

# Container (full stack)
bash scripts/server/refresh-dist.sh
podman build -t nebu-ctx-addon-dev -f homeassistant/Dockerfile .
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
  dist/linux/                Committed publish payload (used by Docker)
  tests/                     .NET contract + integration tests
homeassistant/    HA add-on packaging (Dockerfile, config.yaml, README)
tests/            Cross-stack smoke tests (local-addon-test.sh, etc.)
scripts/server/   Build/publish scripts for .NET host
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

# Force-refresh the server dist payload
bash scripts/server/refresh-dist.sh

# Tail server logs from a running container (replace name as needed)
podman logs -f nebu-ctx-server
```
