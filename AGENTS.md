# AGENTS

## Purpose

`nebu-ctx` is organized around a thin Rust client plus a .NET MCP host and dashboard stack.

- Rust thin client installed from `client/`
- .NET MCP HTTP host under `server/src/NebuCtx.Server.Host/`
- Dashboard HTTP UI served by that same .NET host on its dashboard port

## Main Surfaces

- `client/src/main.rs`: thin-client CLI entrypoint
- `client/src/hook_handlers.rs`: all Claude Code / Copilot CLI hook logic (7 hook types)
- `client/src/mcp_server/mod.rs`: MCP tool routing, CLOUD_ONLY_TOOLS, CLOUD_PREFERRED_TOOLS
- `client/src/mcp_server/dispatch.rs`: local tool dispatch (stubs/delegates for non-cloud tools)
- `client/src/cloud_client.rs`: HTTP client for cloud server calls
- `server/src/`: .NET host, dashboard, contracts, storage, project registry, and tool handlers
- `server/src/NebuCtx.Tools/`: one directory per IToolHandler (Brain/, Gain/, Cost/, Heatmap/, Stats/, Knowledge/, Routes/, Session/)
- `server/src/NebuCtx.Application/TelemetryStore.cs`: in-memory telemetry; per-project counters + file-access tracking
- `server/src/NebuCtx.Contracts/`: all DTOs shared across server projects
- `server/tests/NebuCtx.IntegrationTests/`: endpoint + analytics tests; uses `NebuCtxTestFactory` (no Postgres required)
- `tests/`: cross-stack smoke, add-on, and release validation
- `homeassistant/`: Home Assistant add-on packaging
- `Dockerfile`: multi-stage build (SDK → Alpine runtime); produces GHCR image and local dev builds

## Layout Rules

- Treat `client/target/` as normal Cargo output.
- `server/dist/` is gitignored — binaries are never committed; built in CI and published to GHCR.
- Keep cross-stack and repo-level tests in top-level `tests/` only.

## Storage Model

- **Only supported store**: PostgreSQL via `NEBULA_STORE=postgres` and `DATABASE_URL`.
- No in-memory/JSON store fallback in production — `StartupValidator` enforces Postgres at startup.
- `TelemetryStore` is in-memory (singleton); `PostgresTelemetryStore` persists events; `TelemetryHydrationService` loads on startup.
- `IBrainStore`, `IKnowledgeStore`, `ISessionStore`, `IProjectStore`, `ICodeIndexStore`, `ICheckoutBindingStore` are all Postgres-backed in production.

## Tool Routing Architecture

```
CLOUD_ONLY_TOOLS  = ["ctx_brain", "ctx_routes", "ctx_gain", "ctx_cost", "ctx_heatmap", "ctx_stats"]
CLOUD_PREFERRED   = ["ctx_knowledge", "ctx_session"]
```

- `CLOUD_ONLY_TOOLS`: error if server unreachable — no local fallback.
- `CLOUD_PREFERRED`: routes to cloud when configured; falls back to local JSON only when no cloud server is set up. If cloud is configured but unreachable → hard fail (no silent fallback).
- All other tools: dispatched locally via `dispatch.rs`.

## Hook System (Claude Code / Copilot CLI)

7 hook types registered in `.claude/settings.local.json`:

| Hook | Command | Timeout |
|------|---------|---------|
| `PostToolUse.*` | `nebu-ctx hook post-tool-use` | 10s |
| `PreCompact` | `nebu-ctx hook pre-compact` | 15s |
| `PreToolUse:Bash\|bash` | `nebu-ctx hook rewrite` | — |
| `PreToolUse:Read\|read\|...` | `nebu-ctx hook redirect` | — |
| `SessionStart` | `nebu-ctx hook session-start` | 10s |
| `Stop` | `nebu-ctx hook stop` | 30s |
| `UserPromptSubmit` | `nebu-ctx hook user-prompt-submit` | 5s |

- **PreCompact**: reads session state + knowledge, builds `<session_state>` XML ≤2KB, stores to brain, outputs `{"additionalContext":"..."}`.
- **SessionStart**: on `"compact"/"resume"` source → snapshot + routing XML; on `"startup"` → routing XML only.
- **UserPromptSubmit**: stores prompt to `ctx_brain` with full project context.
- All hook logic lives in `client/src/hook_handlers.rs`. CLI dispatch arms are in `main.rs`.

## Adding a New IToolHandler

1. Create `server/src/NebuCtx.Tools/<ToolName>/<ToolName>ToolHandler.cs` implementing `IToolHandler`.
2. Register in `server/src/NebuCtx.Tools/ToolRegistration.cs` via `AddToolHandlers()`.
3. If cloud-only: add to `CLOUD_ONLY_TOOLS` in `client/src/mcp_server/mod.rs`.
4. Remove any local dispatch stub in `client/src/mcp_server/dispatch.rs`.
5. Write integration tests in `server/tests/NebuCtx.IntegrationTests/` using direct handler instantiation (not `WebApplicationFactory`) where possible.

## Integration Test Pattern

`McpEndpointTests` (full HTTP pipeline) uses `NebuCtxTestFactory`:
- `NebuCtxTestFactory` extends `WebApplicationFactory<Program>`, sets `ASPNETCORE_ENVIRONMENT=Test`.
- `Program.cs` skips `StoreFactory.InitializeSchemaAsync` when environment is `Test`.
- All Postgres stores are replaced with in-memory stubs; `TelemetryHydrationService` is removed.
- Analytics/unit tests (`AnalyticsToolTests`, `TelemetryStoreTests`) use direct handler instantiation — no factory needed.

**Never use `WebApplicationFactory<Program>` directly** in `IClassFixture<>` — use `NebuCtxTestFactory`.

## Dashboard

15 panels in order: Overview, Live Observatory, Knowledge Graph, Dependency Map, Compression Lab, Agent World, Bug Memory, Brain Memory, Search Explorer, Learning Curves, Symbol Explorer, Call Graph, Route Map, Context Layer, MCP Token.

Dashboard served on port `3333`. MCP server on port `4242`.

## Product Naming

- Binary/package name: `nebu-ctx`.
- Older internal names (`LeanCtxServer`, `lean-ctx`, `lean_ctx`) are compatibility debt — do not introduce new uses.
- Environment variables use `NEBULA_CTX_*` prefix (not `LEAN_CTX_*` or `AUTH_TOKEN`).

## Home Assistant Add-on

Add-on runs dashboard (3333) + MCP (4242) in one container. Keep these files in sync:

- `docker-entrypoint.sh`, `homeassistant/config.yaml`, `homeassistant/README.md`, `tests/local-addon-test.sh`

No `homeassistant/Dockerfile` at runtime — HA Supervisor pulls pre-built GHCR image when `image:` is set in `config.yaml`.

## Release Flow

**Three version locations must be kept in sync on every bump:**

1. `client/Cargo.toml` — `version = "x.y.z"`
2. `homeassistant/config.yaml` — `version: "x.y.z"`
3. `server/src/NebuCtx.Application/ToolRegistry.cs` — `ServerVersion.Current = "x.y.z"`

Also update `Cargo.lock` via `cargo update --manifest-path client/Cargo.toml` before committing.

- `auto-release.yml` verifies all three locations are in sync, then tags and triggers `release.yml`.
- `release.yml` builds amd64+arm64 binaries, creates GitHub release, publishes crate to crates.io (no `--locked`), and builds+pushes multi-platform server image to `ghcr.io/markbovee/nebu-ctx`.
- **Required secret:** `CARGO_REGISTRY_TOKEN` in GitHub Settings → Secrets → Actions.

If renaming package/binary/image, update: `Cargo.toml`, `client/Cargo.toml`, both workflow files, `homeassistant/Dockerfile`, `tests/` smoke scripts.

## Build And Validation

```bash
# Rust client tests
cargo test --manifest-path client/Cargo.toml

# .NET server tests (all 67 pass, 0 fail — no Postgres required)
dotnet test server/NebuCtx.slnx -p:AllowMissingPrunePackageData=true

# CLI smoke test
bash tests/local-server-cli-test.sh
```

Local dev container (shares Postgres with HA server):
```bash
podman build -t nebu-ctx-server -f Dockerfile .
podman run -d --name nebu-ctx-local \
  -p 127.0.0.1:3333:3333 -p 127.0.0.1:4242:4242 \
  --env-file .env \
  nebu-ctx-server
```

`.env` must include `NEBULA_CTX_HTTP_TOKEN` and `NEBULA_CTX_HOST=0.0.0.0`.

For HA addon validation (builds from source):
```bash
ADDON_DOCKERFILE=Dockerfile bash tests/local-addon-test.sh
```

## Session Startup Protocol

At the start of every session, retrieve project state before investigating:

```
ctx_brain(action="recall", query="session state decisions")
ctx_brain(action="recall", query="build commands version")
ctx_knowledge(action="wakeup")
```

Read [docs/DEVELOPER-KNOWLEDGE.md](docs/DEVELOPER-KNOWLEDGE.md) for non-trivial tasks.

**Pull from brain before touching:**
- Any unfamiliar file → `ctx_brain(action="recall", query="<topic>")`
- Adding a new IToolHandler → `ctx_brain(action="recall", query="itoolhandler pattern")`
- Version bump → `ctx_brain(action="recall", query="version sync rule")`
- Hook system → `ctx_brain(action="recall", query="hook system")`
- CLI routing → `ctx_brain(action="recall", query="mcp routing architecture")`

**At session end:**
```
ctx_session(action="save")
ctx_knowledge(action="consolidate")
ctx_brain(action="store", key="session-YYYY-MM-DD", value="<summary>")
```

## Practical Guidance

- Before writing a new tool handler, check if a similar one exists under `server/src/NebuCtx.Tools/`.
- Before adding CLI dispatch arms, check `main.rs` and `cli/cloud.rs` for existing patterns.
- Preserve LF line endings in shell scripts (`.gitattributes` handles this; container builds normalize defensively).
- If a task touches Postgres-backed behavior, validate `ctx_brain` over HTTP before claiming the server path is healthy.
- `dotnet test` requires no live Postgres — `NebuCtxTestFactory` handles all isolation.

<!-- nebu-ctx -->
## nebu-ctx

Prefer nebu-ctx MCP tools over native equivalents for token savings.
Full rules: @LEAN-CTX.md
<!-- /nebu-ctx -->
