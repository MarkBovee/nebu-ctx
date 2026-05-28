# AGENTS

## Purpose

`nebu-ctx` is organized around a thin Rust client plus a .NET MCP host and dashboard stack.

- Rust thin client installed from `client/`
- .NET MCP HTTP host under `server/src/NebuCtx.Server.Host/`
- Dashboard HTTP UI served by that same .NET host on its dashboard port

## Main Surfaces

- `client/src/main.rs`: thin-client CLI entrypoint
- `client/src/status.rs`: shell startup brief/banner rendering (`nebu-ctx on-brief`)
- `client/src/hook_handlers.rs`: all Claude Code / Copilot CLI hook logic (7 hook types)
- `client/src/cli/shell_init.rs`: generated shell hooks for bash/zsh/fish/PowerShell and upgrade-safe profile rewrites
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

## Coding Standards For Agents

- Follow DRY and SOLID. Before adding code, check whether the behavior already exists and extract shared helpers instead of duplicating logic.
- Prefer small, focused functions with clear names and a single level of abstraction. Use guard clauses and early returns to keep control flow flat.
- Prefer pure functions when practical. Keep orchestration separate from object construction, formatting, or parsing helpers.
- Fail fast on invalid input and return clear errors with enough context for diagnostics.
- Keep code self-documenting. Add short intent comments only for non-obvious logic.
- Keep builds warning-free and error-free, and keep the relevant tests passing before finishing a change.

### Rust Client Rules

- Prefer `match` or focused helper extraction over growing large conditional chains in CLI, MCP, and hook dispatch code.
- Normalize and canonicalize filesystem paths at the boundary where paths enter the system so session state, caches, and path jail logic agree on the same real path.
- Keep public API and tool behavior changes minimal and explicit. Avoid widening behavior accidentally when fixing path, session, or routing bugs.
- Reuse existing helpers in `core/`, `hooks/`, and `tools/` before adding new utility layers.
- Keep shell hook behavior aligned across bash/zsh/fish/PowerShell when practical; if startup behavior changes, update `on-brief`, generated hooks, uninstall cleanup, and README usage together.
- Validate Rust changes with the narrowest relevant `cargo check` or `cargo test --manifest-path client/Cargo.toml` command first, then widen only if needed.

### C# Server Rules

- Do not use fully qualified type names unless required for disambiguation. Add `using` directives instead.
- Do not use `dynamic`. Prefer concrete DTOs, `object` with safe casting, or explicit JSON types.
- Add XML documentation comments to classes, records, methods, and helper functions. Keep them concise but useful.
- Add a short inline why-comment for non-obvious handlers, protocol branches, or business logic.
- For methods with 3 or more parameters, prefer a request/DTO model instead of long parameter lists.
- Keep parameter lists and method invocations on one line when they fit. Break only at logical boundaries when needed.
- Use descriptive variable names. Avoid generic names when the domain concept is known.
- Prefer `switch` or pattern matching over long `if` / `else if` dispatch chains.
- In integration tests, use real response models. Do not use `ApiJsonRequestAsync<object>` or `ApiJsonRequestAsync<JsonElement>` when a concrete model exists.
- For required JSON/OpenAPI properties with non-public setters, add `[JsonInclude]` or make the setter public so schema/runtime generation stays valid.
- For EF Core timestamps, centralize timestamp handling in the `DbContext` save pipeline instead of business logic.

## Storage Model

- **Only supported store**: PostgreSQL via `NEBULA_STORE=postgres` and `DATABASE_URL`.
- No in-memory/JSON store fallback in production — `StartupValidator` enforces Postgres at startup.
- `TelemetryStore` is in-memory (singleton); `PostgresTelemetryStore` persists events; `TelemetryHydrationService` loads on startup.
- `IBrainStore`, `IKnowledgeStore`, `ISessionStore`, `IProjectStore`, `ICodeIndexStore`, `ICheckoutBindingStore` are all Postgres-backed in production.

## Tool Routing Architecture

```
Public MCP surface = ["ctx_read", "ctx_search", "ctx_tree", "ctx_shell", "ctx"]

SERVER_ONLY_TOOLS      = ["ctx_brain", "ctx_gain", "ctx_cost", "ctx_heatmap", "ctx_stats"]
SERVER_PREFERRED_TOOLS = ["ctx_knowledge", "ctx_session"]
```

- Public clients only see the 5-tool MCP contract.
- The Rust client translates `ctx(domain, action)` into internal local or server-backed handlers.
- `SERVER_ONLY_TOOLS`: error if server unreachable — no local fallback.
- `SERVER_PREFERRED_TOOLS`: route to the host when configured; fall back locally only when no host is configured at all.

## Adding a New IToolHandler

1. Create `server/src/NebuCtx.Tools/<ToolName>/<ToolName>ToolHandler.cs` implementing `IToolHandler`.
2. Register in `server/src/NebuCtx.Tools/ToolRegistration.cs` via `AddToolHandlers()`.
3. If server-only: add to `SERVER_ONLY_TOOLS` in `client/src/mcp_server/mod.rs`.
4. Remove any local dispatch stub in `client/src/mcp_server/dispatch.rs`.
5. Write integration tests in `server/tests/NebuCtx.IntegrationTests/` using direct handler instantiation (not `WebApplicationFactory`) where possible.

## Integration Test Pattern

`McpEndpointTests` (full HTTP pipeline) uses `NebuCtxTestFactory`:
- `NebuCtxTestFactory` extends `WebApplicationFactory<Program>`, sets `ASPNETCORE_ENVIRONMENT=Test`.
- `Program.cs` skips `StoreFactory.InitializeSchemaAsync` when environment is `Test`.
- All Postgres stores are replaced with in-memory stubs; `TelemetryHydrationService` is removed.
- Analytics/unit tests (`AnalyticsToolTests`, `TelemetryStoreTests`) use direct handler instantiation — no factory needed.

**Never use `WebApplicationFactory<Program>` directly** in `IClassFixture<>` — use `NebuCtxTestFactory`.

## Home Assistant Add-on

Add-on runs dashboard (3333) + MCP (4242) in one container. Keep these files in sync:

- `docker-entrypoint.sh`, `homeassistant/config.yaml`, `homeassistant/README.md`, `homeassistant/CHANGELOG.md`, `tests/local-addon-test.sh`

No `homeassistant/Dockerfile` at runtime — HA Supervisor pulls pre-built GHCR image when `image:` is set in `config.yaml`.

## Release Flow

**Three version locations must be kept in sync on every bump:**

1. `client/Cargo.toml` — `version = "x.y.z"`
2. `homeassistant/config.yaml` — `version: "x.y.z"`
3. `server/src/NebuCtx.Server.Core/ToolRegistry.cs` — `ServerVersion.Current = "x.y.z"`

Also update `Cargo.lock` via `cargo update --manifest-path client/Cargo.toml` before committing.

Every version bump must also add release notes in both places:

- `CHANGELOG.md` — repo/client/server release notes for the bumped version
- `homeassistant/CHANGELOG.md` — Home Assistant add-on release notes for the same version, even when the underlying change is client-focused

A version bump is not complete until both changelog entries exist for that exact bumped version.

- `auto-release.yml` verifies all three locations are in sync, then tags the release. The tag push triggers `release.yml`.
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

## Practical Guidance

- Before writing a new tool handler, check if a similar one exists under `server/src/NebuCtx.Tools/`.
- Before adding CLI dispatch arms, check `main.rs` and `cli/cloud.rs` for existing patterns.
- Preserve LF line endings in shell scripts (`.gitattributes` handles this; container builds normalize defensively).
- If a task touches Postgres-backed behavior, validate `ctx(domain="memory", action="recall", ...)` over HTTP before claiming the server path is healthy.
- `dotnet test` requires no live Postgres — `NebuCtxTestFactory` handles all isolation.
- Do not bypass nebu-ctx wrapper/routing in agent workflows when a nebu-ctx path exists. No direct native fallback just because wrapper output is inconvenient, lossy, or buggy.
- If nebu-ctx wrapper behavior is wrong, stay inside nebu-ctx path: retry once, use supported raw mode (`--raw` / `raw=true`) when available, or run the repo-built client via `cargo run --manifest-path client/Cargo.toml -- ...` instead of bypassing to the native command. Then file/update a GitHub issue.
