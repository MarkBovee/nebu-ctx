# nebu-ctx .NET 10 Server Migration Plan

> Updated: 2026-04-22
> Status: Draft for review
> Goal: replace the Rust-based deployed server, dashboard, and container/add-on runtime with a .NET 10 implementation that becomes the canonical project system for nebu-ctx. The first release is an MVP migration of current behavior, not an optimization pass.

## Understanding Summary

- The legacy cloud API is retired. The Postgres-backed server is the new cloud/runtime target.
- Full product capability remains in scope, but the migration needs an MVP checkpoint first: current behavior working end-to-end on the .NET server before optimization, redesign, or cleanup.
- The current workspace-root and `cwd` driven scoping model is not acceptable for the target product because the same project must keep the same data across different machines and checkouts.
- Canonical project data must move to the server and be keyed by a stable project identity, not by a local workspace path.
- Rust may remain as the local CLI in the near term, but only as a thin project-aware client and local execution layer, not as the canonical owner of project state.
- Day-1 operator contracts stay stable unless explicitly changed later: ports `3333` and `4242`, token file persistence, current env vars, current dashboard expectations, and the documented `/v1/*` surface.
- The end of the MVP phase must include a full end-to-end test: run the .NET server in a container, connect the CLI to it, and verify project-scoped behavior across different local checkouts of the same project.

## Assumptions

- The legacy cloud API under `src/cloud_server/*` is retired and is not being ported as a separate .NET runtime.
- Full feature coverage is still the end-state requirement. Nothing is being permanently de-scoped from the product.
- MVP means “working parity for the current server-connected experience first,” not “final architecture polish.”
- Persistent project data lives on the server. Local filesystem paths, `cwd`, and local roots remain execution context only.
- Rust can stay as the local CLI during the migration, but the deployed server/container/add-on path must not depend on Rust.
- Existing HTTP routes, auth behavior, ports, env vars, and persisted token file paths should stay stable in the first cut.
- Existing Postgres and SQLite schemas should be preserved initially; data model redesign comes later.
- Repository-root granularity remains the default project boundary for MVP, matching the current root-detection behavior.
- .NET 10, ASP.NET Core 10, Linux x64/arm64 publishing, and Postgres-backed deployment remain available.

## Explicit Non-Goals

- Rewriting the CLI in .NET during the MVP phase.
- Redesigning the dashboard UI before parity is established.
- Redesigning the persistence schema before the .NET server is functionally stable.
- Building a full remote-checkout or repo-sync platform in the MVP phase.
- Tuning performance, cost, or best-practice refinements before the first end-to-end working system exists.
- Preserving workspace-path-based identity as a first-class concept.

## Resolved Scope Decisions

- The legacy cloud API is retired. Its responsibilities either move into the new server where still needed or disappear if obsolete.
- The product direction is project-based, not workspace-based. Multiple machines and local roots must be able to resolve to the same server-side project.
- Rust local CLI support may remain during migration, but the canonical product runtime is the .NET server.
- The MVP goal is a working server-backed system first. Optimization and cleanup are explicitly later phases.
- Full feature parity remains required. The MVP checkpoint is not permission to leave major functionality behind permanently.

## Current-State Inventory

### Server Surfaces That Must Move

| Surface | Current Rust owner | Current behavior that matters |
|--------|---------------------|-------------------------------|
| Main HTTP MCP server | `src/http_server/mod.rs` | `GET /health`, `GET /v1/manifest`, `GET /v1/tools`, `POST /v1/tools/call`, auth middleware, rate limiting, concurrency limits, request timeout, body-size limits |
| Dashboard | `src/dashboard/mod.rs` + `src/dashboard/dashboard.html` | dashboard HTML, JSON endpoints under `/api/*`, auth token handling, operator visibility into stats/session/pipeline state |
| Container runtime | `Dockerfile`, `docker-entrypoint.sh` | container startup, host/port binding, token-driven remote bind behavior, `/health` readiness |
| Home Assistant runtime | `homeassistant/run.sh`, `homeassistant/config.yaml`, `homeassistant/Dockerfile*` | dual-port behavior, token persistence to `/data/auth_token`, Postgres settings, ingress dashboard |
| Project identity and state scoping | `src/core/protocol.rs`, `src/core/session.rs`, `src/core/graph_index.rs`, `src/tools/*` | root detection from local markers, `cwd` tracking, path-derived cache keys, project-local graph/session state |

### Contracts That Must Be Preserved On Day 1

#### HTTP MCP contract

- `GET /health` returns a simple healthy response and is auth-exempt.
- `GET /v1/manifest` returns the tool manifest JSON.
- `GET /v1/tools` returns tool metadata JSON.
- `POST /v1/tools/call` accepts `{ "name": "...", "arguments": { ... } }` and returns `{ "result": ... }` or `{ "error": ... }`.
- Auth is `Authorization: Bearer <token>`.
- Non-loopback binding requires auth.
- Rate limiting, concurrency caps, request-size limits, and request timeouts remain available.

#### Dashboard contract

- Port `3333` remains the dashboard port for local, container, and Home Assistant flows.
- The current dashboard JSON endpoints used by the shipped UI remain available.
- The current token lookup behavior via `NEBU_CTX_TOKEN_FILE` / `NEBULA_CTX_TOKEN_FILE` and `/api/auth-token` remains available.
- The dashboard remains safe behind ingress and preserves its current auth-disable behavior where that behavior is already relied on.

#### Deployment and operator contract

- `NEBULA_STORE` and `DATABASE_URL` remain the primary store selectors.
- `NEBULA_CTX_HTTP_TOKEN`, `NEBULA_CTX_HTTP_PORT`, and host-binding behavior are preserved initially.
- The Home Assistant add-on keeps `3333` for dashboard ingress and `4242` for MCP HTTP.
- `/data/auth_token` remains the persisted token file inside the add-on/runtime volume.

### The Current Scoping Problem

The current Rust implementation still assumes that path-like values can stand in for project identity:

- `Session::effective_cwd` falls back through explicit `cwd`, tracked shell cwd, project root, and finally process cwd.
- `detect_project_root_or_cwd` derives the project boundary from local markers like `.git`, `Cargo.toml`, or workspace files.
- `ProjectIndex::load_or_build` normalizes and loads indexes by local root path and still contains migration logic from `"."` as a cache key.

That is acceptable for a purely local tool, but it is the wrong persistence model for a server-backed product. The same repository cloned in two different directories or used from two different machines would fragment into different identities if the server keeps path-based ownership.

## Design Approaches

### Recommended: Server-First Hybrid With Canonical Project Identity

Build the .NET 10 server as the canonical owner of project state, auth, dashboard, tool metadata, and persistent data. Keep the Rust CLI in the MVP as a thin local execution client that connects to the server, identifies the project, and performs the local-only work that a remote server cannot do directly against a live working copy.

Why this is the recommended option:

- It satisfies the cross-machine project requirement.
- It keeps Rust out of the deployed production runtime while still using it where locality matters.
- It avoids the false simplification of pretending a remote server can directly read a developer’s local checkout or run shell commands in it.
- It provides a credible MVP path: get the server, project identity, persistence, dashboard, and CLI connection working first, then finish the remaining feature waves.

### Alternative 1: Pure Server-Only Product In Phase 1

Run everything in the remote .NET server and eliminate local execution immediately.

Why not recommended for MVP:

- File, search, and shell tools would need either repo sync/upload, remote checkout orchestration, or a much bigger agent system.
- That expands scope far beyond “migrate current functionality first.”
- It turns the MVP into a platform rewrite instead of a runtime migration.

### Alternative 2: Keep The Existing Path-Derived Hybrid Model

Port the server to .NET but keep path/root/cwd as the persistent identity for project data.

Why not recommended:

- It directly conflicts with the multi-machine requirement.
- It would keep producing fragmented project state when roots differ.
- It would force a second identity migration later instead of solving the core issue now.

## Decision Log

1. **Use a server-first hybrid architecture for the MVP.**
   - Alternatives considered: pure server-only in phase 1; path-derived hybrid.
   - Chosen because local filesystem and shell access still need a client-side execution surface, while persistent state must move to the server.

2. **Retire the legacy cloud API as a separate runtime.**
   - Alternatives considered: port it as-is; keep it indefinitely beside the new server.
   - Chosen because the new server with Postgres is the new cloud surface.

3. **Make project identity canonical and server-owned.**
   - Alternatives considered: workspace-path identity; local-root identity with aliasing hacks.
   - Chosen because the same project must keep one identity across machines and workspaces.

4. **Treat `cwd` and project root as execution context, not persistence keys.**
   - Alternatives considered: continue storing rows and caches by local root path.
   - Chosen because path-derived identity is the core cause of cross-machine fragmentation.

5. **Preserve the current external contracts in the first cut.**
   - Alternatives considered: redesign routes, ports, or config while migrating.
   - Chosen because parity testing is much simpler when the user-facing contract stays stable.

6. **Preserve the current schema first; optimize later.**
   - Alternatives considered: schema redesign during migration.
   - Chosen because correctness and cutover safety matter more than elegance in phase 1.

7. **Make MVP the first explicit gate, then complete the remaining parity work.**
   - Alternatives considered: one giant rewrite with no intermediate success condition.
   - Chosen because the team needs a working .NET server and CLI flow before optimization or deeper redesign.

## Target .NET 10 Architecture

## Recommended solution layout

```text
dotnet/
  src/
    NebuCtx.Server.Host/          # ASP.NET Core host for MCP HTTP + dashboard ports
    NebuCtx.Contracts/            # HTTP DTOs, manifest contracts, project/client DTOs
    NebuCtx.Application/          # Use cases, orchestration, tool execution pipeline
    NebuCtx.Projects/             # Project registry, binding, identity resolution
    NebuCtx.Storage/              # Store abstractions + SQL implementations
    NebuCtx.Tools/                # Tool handlers grouped by domain
    NebuCtx.Dashboard/            # Static dashboard assets + endpoint services
    NebuCtx.Hosting/              # Shared hosting/config/auth/rate-limit helpers
  tests/
    NebuCtx.ContractTests/
    NebuCtx.ProjectIdentityTests/
    NebuCtx.IntegrationTests/
    NebuCtx.ContainerTests/
    NebuCtx.HomeAssistantTests/
```

## Recommended host shape

### `NebuCtx.Server.Host`

- ASP.NET Core 10 host running on Kestrel.
- Two listeners/endpoints by default:
  - port `4242` for MCP HTTP
  - port `3333` for dashboard HTTP
- Shared middleware for auth, host checks, request sizing, rate limiting, correlation IDs, and structured logging.
- Configuration sources: env vars first, optional config files second.
- `/health` must answer early and must not depend on expensive warmup.

### Storage layer

Recommended first-pass choice:

- `Npgsql` for Postgres.
- `Microsoft.Data.Sqlite` for SQLite.
- Prefer explicit SQL and repository/services over an ORM-first rewrite in the first migration pass.

Reasoning:

- The current schema already exists.
- Behavior parity matters more than abstract modeling in the MVP.
- The current Rust implementation already behaves closer to explicit SQL than to a rich ORM domain model.

### Tool execution layer

- Introduce a .NET tool registry with explicit handler classes rather than one giant dispatch switch.
- Group tools by domain:
  - project identity and session orchestration
  - persistent memory and project state
  - file and symbol projection
  - search and graph analysis
  - shell and execution
  - workflow, stats, and dashboard views
- Generate `/v1/manifest` and `/v1/tools` from .NET contracts rather than hand-maintained JSON.

## Project Identity And Scope Model

### Canonical identifiers

For MVP, the server should treat a project as a first-class resource with these concepts:

- `project_id`: server-generated stable identifier used as the primary key for persistent project data.
- `project_slug`: human-readable name used in CLI and dashboard flows.
- `repository_fingerprint`: optional matching metadata such as canonical git remote URL, repo host/provider id if available, repo name, and default branch.
- `workspace_binding`: non-canonical metadata for a specific local checkout or machine, such as local root path, branch, last seen commit, last sync time, and client label.
- `session_id`: connection/session scope for a specific CLI or dashboard interaction.

### Resolution flow

Recommended MVP resolution order:

1. The CLI detects local repo metadata from the current checkout.
2. The CLI sends either an explicit project reference or the local repository fingerprint when connecting or calling the server.
3. The server resolves the request to a `project_id` using:
   - an explicit prior binding first
   - a unique repository fingerprint match second
   - explicit user choice if the match is ambiguous
4. The server persists project-scoped state under `project_id`, not under local path strings.
5. The CLI still sends `cwd`, project root, and similar values when the action needs local execution, but those values do not define the persistent identity.

### Storage rules

- Never key persistent rows by raw workspace path or absolute `cwd`.
- Never use `"."` or any process-local path alias as a durable cache key.
- Project moves, checkout moves, or machine changes must not create a new project identity.
- Workspace bindings are aliases and diagnostics, not the source of truth.
- Repository-root granularity remains the MVP default so current monorepo behavior does not change unexpectedly during the migration.

### MVP boundary for identity

The MVP does not need a perfect enterprise project catalog. It does need the following:

- explicit project creation or binding when auto-match is not enough
- reliable reuse of the same project across two local roots of the same repo
- clean separation between local execution context and server-owned project state
- dashboard and memory/stat views that aggregate by `project_id`

## Local Vs Server Feature Ownership

The clean split for MVP is not “everything remote” versus “everything local.” It is “server owns project state and contracts; local client executes what must stay close to the working copy.”

| Capability | MVP owner | Why |
|-----------|-----------|-----|
| Auth, tokens, project registry, project membership | .NET server | canonical cross-machine ownership lives here |
| Dashboard UI and `/api/*` endpoints | .NET server | deployed operator surface |
| `/health`, `/v1/manifest`, `/v1/tools`, `/v1/tools/call` | .NET server | primary external contract |
| Persistent `ctx_brain` memory and related project state | .NET server | cross-machine persistence is the point of the migration |
| Shared graph/index metadata meant to survive across machines | .NET server | should not fragment by local path |
| Stats, session summaries, pipeline history, ledger-style history | .NET server | server-owned project timeline |
| Local file reads against a live checkout | Rust CLI / local agent | remote server cannot directly see a developer’s working tree |
| Local grep/tree/symbol/search over unsynced local changes | Rust CLI / local agent | locality-sensitive and workspace-specific |
| Local shell command execution | Rust CLI / local agent | must run on the user’s machine or inside a local execution environment |
| Hook/bootstrap/editor integration | Rust CLI / local agent | machine- and editor-local concerns |
| Cloud connect/status/project bind UX | Rust CLI talking to .NET server | natural thin-client responsibility |
| Fully local no-server mode | optional compatibility path, not MVP-critical | can remain temporary or be removed later based on product direction |

This split is the key recommendation: nebu-ctx should be server-first and project-scoped, but not pretend that repo-local execution can be remote in the MVP.

## Scope Breakdown By Workstream

### Workstream 1: Contract And Scope Freeze

Deliverables:

- frozen HTTP/dashboard/operator contract matrix
- explicit project-scoping rules
- explicit list of what stays local versus what becomes server-owned

### Workstream 2: Project Identity And Client/Server Binding

Deliverables:

- project registry in .NET
- project resolution protocol for the CLI
- project binding rules for same-project multi-machine use

### Workstream 3: Host, Auth, And MCP Surface Migration

Deliverables:

- .NET host boots cleanly on Linux x64 and arm64
- `/health`, `/v1/manifest`, `/v1/tools`, `/v1/tools/call` work
- auth, request limits, and readiness behavior match expectations

### Workstream 4: Dashboard And Persistence Migration

Deliverables:

- current dashboard served by .NET
- SQLite and Postgres support in .NET
- project-scoped `ctx_brain` parity first

### Workstream 5: CLI Refit And Remaining Feature Waves

Deliverables:

- Rust CLI speaks project-aware server protocol
- local-only execution flows remain usable where they must stay local
- remaining server-owned feature families port in controlled waves

### Workstream 6: Packaging, Release, And Home Assistant

Deliverables:

- .NET Dockerfiles for dev and production
- Home Assistant add-on updated to .NET publish artifacts
- x64 and arm64 build automation

### Workstream 7: End-To-End Validation And Cutover

Deliverables:

- full containerized server + CLI validation
- same-project multi-workspace verification
- cutover/rollback rules

## Phased Migration Plan

## Phase 0: Freeze Contracts And Scope

Objectives:

- Stop treating migration as a vague rewrite.
- Freeze the contracts that the .NET implementation must meet.
- Replace the old workspace-centered assumptions with explicit project-centered rules.

Tasks:

1. Capture the current HTTP MCP contract in fixtures and contract tests.
2. Capture the dashboard API contract used by the current UI.
3. Capture current env vars, CLI flags, token-file paths, and Home Assistant config expectations.
4. Document the project identity rules and the local-vs-server ownership split.
5. Freeze new Rust server-side feature work until the migration footing is established.

Exit criteria:

- A contract matrix exists and is checked in.
- The project-scoping rules are explicit.
- No open ambiguity remains about ports, env vars, token persistence, or feature ownership.

## Phase 1: Build Project Identity And Binding First

Objectives:

- Solve the cross-machine identity problem before porting deeper behavior.

Tasks:

1. Define the .NET project registry schema and APIs.
2. Define the client/server contract for `project_id`, `project_slug`, repository fingerprint, and workspace binding.
3. Decide how the CLI creates, binds, and reuses projects.
4. Add tests that prove two different local roots of the same repo resolve to the same `project_id`.
5. Add tests that prove different projects do not collide, even if local folder names match.

Exit criteria:

- The server can resolve a project consistently across multiple local roots.
- The CLI-server protocol carries project identity explicitly.
- Persistent project state is no longer defined by workspace path.

## Phase 2: Build The .NET Host Skeleton And Outer MCP Surface

Objectives:

- Stand up a minimal .NET 10 host that proves hosting, config, health, auth, and the main `/v1/*` contract.

Tasks:

1. Create the .NET solution and host projects.
2. Implement configuration binding for the current env-var contract.
3. Bind dashboard and MCP ports in one host process unless a concrete reason to split appears.
4. Implement `/health` so it returns quickly and reliably.
5. Implement Bearer auth middleware and host-binding safety rules.
6. Implement `/v1/manifest`, `/v1/tools`, and `/v1/tools/call` contracts.
7. Port rate limiting, request-size limits, concurrency caps, and request timeouts.

Exit criteria:

- The .NET container starts.
- `/health` is reachable.
- Token-protected routes reject missing/invalid tokens.
- Contract tests pass for the documented `/v1/*` endpoints.

## Phase 3: Port The Dashboard

Objectives:

- Move the dashboard runtime off Rust without changing the current operator experience.

Tasks:

1. Serve the existing dashboard asset set from ASP.NET Core.
2. Port the currently used dashboard JSON endpoints.
3. Keep token-file loading and optional auth-disable behavior.
4. Preserve the `3333` dashboard port and Home Assistant ingress behavior.
5. Add dashboard integration tests using the current HTML/API expectations.

Exit criteria:

- The dashboard loads via the .NET host.
- The shipped dashboard UI works against the .NET endpoints.
- Home Assistant ingress semantics remain intact.

## Phase 4: Port Persistence And The First Project-Scoped Server Slice

Objectives:

- Move the validated Postgres-backed server slice first and make it project-scoped.

Tasks:

1. Implement store abstractions in .NET for SQLite and Postgres.
2. Preserve the current schema layout and naming unless project identity requires additive tables.
3. Port `ctx_brain` actions needed for production validation first:
   - `status`
   - `store`
   - `recall`
   - then `consolidate`, `activate`, `checkpoint`
4. Re-key or map server-owned state to `project_id` where path-derived ownership exists today.
5. Validate against existing Postgres data and SQLite state files.

Exit criteria:

- `.NET /v1/tools/call` can execute `ctx_brain status/store/recall` successfully against Postgres.
- Dashboard and memory/state views aggregate by project identity, not by workspace path.
- No deployed runtime bridge to Rust is required for this server slice.

## Phase 5: Refit The CLI And Port Remaining Feature Families For MVP

Objectives:

- Make the CLI a real thin client to the .NET server while preserving local execution where required.

Tasks:

1. Update the Rust CLI `cloud connect` and related flows to negotiate project identity and binding.
2. Split feature behavior clearly into:
   - server-owned stateful features
   - local execution features that report into a project context
3. Port remaining server-owned feature families in deliberate waves.
4. Keep local file/search/shell flows usable against a live working copy without pretending they are remote-first.
5. Add parity fixtures for each migrated server-owned tool family before declaring it complete.

Exit criteria:

- The CLI can connect, identify the project, and use the server-backed feature flows.
- Local-only execution still works where locality is required.
- The MVP user flow works without the Rust server runtime.

## Phase 6: Replace Packaging, Release Flow, And Home Assistant Runtime

Objectives:

- Make .NET the only production packaging/runtime path.

Tasks:

1. Replace Rust Docker build/publish with `.NET publish` based images.
2. Build x64 and arm64 artifacts for release automation.
3. Rework the Home Assistant add-on to launch the .NET host.
4. Preserve `/data/auth_token`, `3333`, `4242`, and current config options unless intentionally changed later.
5. Update smoke scripts to target the .NET runtime.

Exit criteria:

- Standalone container boots reliably.
- Home Assistant add-on boots reliably.
- No Rust runtime artifact is part of the deployed server/add-on path.

## Phase 7: Run The Full MVP End-To-End Validation

Objectives:

- Prove the MVP works in the actual deployment shape.

Tasks:

1. Run the .NET server in a container against Postgres.
2. Install or build the Rust CLI locally.
3. Connect the CLI to the running server with a real auth token.
4. Bind the current checkout to a project and execute the MVP server-backed flows.
5. Repeat from a second local checkout path of the same repository and verify the same project data is visible.
6. Verify dashboard availability and key `/api/*` endpoints against the same server instance.
7. Record the exact MVP smoke command set so it can become an automated test script.

Exit criteria:

- The .NET server container and CLI work together end-to-end.
- The same project survives two different local roots.
- The dashboard, token flow, and core server-backed tools are green.

## Phase 8: Close The Remaining Parity Gaps, Then Optimize

Objectives:

- Finish any remaining feature parity required for “full features, no exception,” then move into optimization and best-practice work.

Tasks:

1. Close any remaining tool families or transport paths not needed for the MVP checkpoint.
2. Validate full feature parity against the current Rust behavior where still relevant.
3. Remove or archive Rust server/container release paths.
4. Only after parity is complete, start performance, cost, architecture cleanup, and best-practice improvements.

Exit criteria:

- All kept product features are owned by the .NET server or the thin local client boundary.
- Rust server/container release paths are removed or archived.
- Optimization work begins from a stable, working platform instead of a half-migrated system.

## MVP Release Boundary

The MVP is considered running when all of the following are true:

- The .NET server runs in a container with Postgres.
- The Rust CLI connects to it with auth and explicit project resolution.
- The same project identity is reused across two different local checkouts of the same repo.
- `/health`, `/v1/manifest`, `/v1/tools`, `/v1/tools/call`, and the current dashboard endpoints work.
- `ctx_brain status/store/recall` works end-to-end against the server.
- The dashboard shows project-scoped state from that same server.

## Validation Strategy

## Contract tests

- Build a fixture suite from the current Rust implementation for:
  - `/health`
  - `/v1/manifest`
  - `/v1/tools`
  - `/v1/tools/call` on representative tools
  - dashboard `/api/*` responses used by the current UI

## Project identity tests

- same repository, different local root, same `project_id`
- different repositories, different `project_id`
- ambiguous fingerprint requires explicit bind instead of silent wrong resolution
- moving a checkout path does not create duplicate project data
- dashboard and memory views aggregate by project, not workspace root

## Integration tests

- SQLite mode
- Postgres mode
- auth-required and auth-exempt endpoints
- request-size, rate-limit, concurrency, and timeout behavior
- project binding and reconnect flows

## Container tests

- standalone container boot and health
- persisted token behavior
- x64 and arm64 image validation
- CLI-to-container connectivity
- same-project multi-root validation against one containerized server

## Home Assistant tests

- add-on startup
- token generation and persistence to `/data/auth_token`
- dashboard reachability on `3333`
- MCP reachability on `4242`
- config mapping from HA options to runtime env/config

## Full MVP end-to-end test

The final MVP test should be automated and should prove the new product shape, not just isolated endpoints:

1. Start Postgres.
2. Start the .NET server container with the expected env vars and auth token.
3. Build or install the Rust CLI.
4. Connect the CLI to the containerized server.
5. Bind checkout A of the repo to a project and run the core server-backed flows.
6. Open checkout B of the same repo from a different local path and bind or auto-resolve it to the same project.
7. Verify project memory/state created from checkout A is visible from checkout B.
8. Verify the dashboard endpoints reflect the same project-scoped state.

That test is the real proof that the migration has moved from workspace-based logic to project-based logic.

## Performance And Reliability Defaults

These are tracked during migration, but they are not allowed to derail the MVP before correctness exists:

- health endpoint reachable within 2 seconds of process start
- cold container ready within 10 seconds on standard hardware
- `ctx_brain status/store/recall` latency not materially worse than the current validated baseline
- no auth regression on non-loopback binds
- no forced schema migration for the first cutover unless project identity requires additive tables

## Risk Register

| Risk | Why it matters | Mitigation |
|------|----------------|------------|
| Project identity collisions or fragmentation | the same repo could split into multiple server projects or two repos could collide | require explicit bind fallback, add identity tests early, keep workspace binding non-canonical |
| Local-vs-server boundary confusion | teams may try to force remote execution for tools that still need local context | document the split explicitly and test both sides together |
| MCP protocol compatibility beyond the common `/v1/*` path | clients may rely on more than the obvious routes | keep full parity in scope and close remaining gaps after the MVP checkpoint |
| Path sandboxing and file safety drift | file-oriented tools are security-sensitive | create dedicated path-jailing tests and preserve behavior before optimizing |
| Shell/execution behavior drift | execution tools are platform- and environment-sensitive | keep execution local where needed and port the contract carefully |
| Schema drift during migration | existing Postgres/SQLite state must continue working | preserve schema first and prefer additive project tables |
| Home Assistant regression | add-on has distinct ingress/token/port assumptions | keep `3333`, `4242`, and `/data/auth_token` stable until after cutover |

## Recommended Initial Action Items

- [ ] Freeze the current server/dashboard/add-on contracts into a parity matrix.
- [ ] Define the project identity model and CLI-server binding contract.
- [ ] Scaffold the .NET 10 solution with host, contracts, projects, storage, tools, and tests projects.
- [ ] Implement the .NET hosting/config/auth/health foundation first.
- [ ] Implement the project registry and same-project multi-root resolution before deeper tool migration.
- [ ] Port the MCP HTTP `/v1/*` surface before broader feature migration.
- [ ] Port the dashboard runtime without changing the current UI contract.
- [ ] Port SQLite/Postgres storage and `ctx_brain` first as the server-critical validation slice.
- [ ] Refit the Rust CLI as a thin project-aware client.
- [ ] Create the final end-to-end containerized server + CLI test and make it green.

## Definition Of Done

The migration is complete only when all of the following are true:

- The deployed MCP HTTP server is .NET 10.
- The deployed dashboard runtime is .NET 10.
- The deployed container and Home Assistant add-on contain no Rust runtime dependency.
- The server owns canonical project identity and project-scoped persistent state.
- The same project is stable across multiple machines and local checkouts.
- All kept public server contracts are preserved or intentionally versioned.
- SQLite and Postgres modes both work under .NET.
- The Home Assistant add-on still provides dashboard ingress on `3333`, MCP HTTP on `4242`, and token persistence to `/data/auth_token`.
- Rust server/container release paths are removed from the production deployment path.

## Review Gate

This plan is now aligned with the major scope decisions, but a few design choices still deserve explicit confirmation before implementation starts:

1. Should project identity default to explicit CLI binding first, or to automatic repository-fingerprint matching with explicit bind only on ambiguity?
2. Is repository-root granularity sufficient for phase 1, with subproject support deferred until later?
3. Should the MVP keep any intentionally supported local-only mode, or should the connected server-backed mode become the only supported nebu feature path?