# NebuCtx Lean-ctx Realignment Plan

Updated: 2026-04-24
Status: Authoritative
Audience: implementation sessions working in this repository

## WP Status Tracker

Read this first. Each session must verify the status column before starting work.

| WP | Title | Status | Notes |
|----|-------|--------|-------|
| WP0 | Freeze target surface | ✅ Done | Plan frozen, `reference/` is read-only |
| WP1 | Re-establish Rust client from lean-ctx baseline | ✅ Done | All lean-ctx modules present; smoke 4/4 green on Linux |
| WP2 | Cloud-first UX | ✅ Done | `cloud connect/bind/sync/disconnect/status` all working; `sync` sends current branch+commit to cloud |
| WP3 | workspace → checkout rename | ✅ Done | Rust client + full .NET server renamed; SQLite deleted; SQL migration added; backward-compat alias kept |
| WP4 | Audit and complete local tool surface | ✅ Done | Dead code removed (`cloud_server/`, `local_dashboard/`, `tui/`, `heatmap.rs`); ratatui/crossterm stripped; embeddings removed from defaults |
| WP5 | Cloud-owned shared-state in .NET | ✅ Done | `ctx_knowledge` (remember/recall/status/remove/categories) and `ctx_session` (task/finding/decision/save/load/reset/list/cleanup) implemented with Postgres storage |
| WP6 | Hybrid sync pipeline | ✅ Done | `TelemetryIngestRequest` contract + `TelemetryStore.IngestEvent()` + `POST /v1/telemetry/ingest` + Rust fire-and-forget emit in `record_call_with_timing()` |
| WP7 | Dashboard parity with project-scoped data | ✅ Done | `IKnowledgeStore.ListAllForProjectAsync` + `IBrainStore.ListAllAsync` wired into `/api/knowledge` and new `/api/brain` dashboard endpoints |
| WP8 | Packaging and add-on stabilization | ✅ Done | `server/dist/linux/` refreshed, image built, add-on smoke passed on Linux |
| WP9 | Rust client ↔ server MCP gateway | ✅ Done | `CloudResult` enum, `CLOUD_ONLY_TOOLS`/`CLOUD_PREFERRED_TOOLS`, `ctx_brain` stub, cloud-preferred fallback; version bumped to 0.5.0 |

This document is the authoritative redesign plan for NebuCtx product realignment.
It is the only active redesign plan for NebuCtx product realignment.

The goal is explicit:

- NebuCtx must feel like lean-ctx again.
- The Rust runtime must be re-established from the lean-ctx runtime under `reference/rust/`, not by extending the reduced Nebu client.
- The shared remote surface is our NebuCtx cloud service, backed by the existing .NET host under `server/`.
- The existing .NET host and dashboard stack already exist. Realignment work extends that runtime and renames its product surface to NebuCtx Cloud; it does not create a second cloud implementation.
- NebuCtx remains project-based, not workspace-based.
- The Rust binary is rebuilt from lean-ctx first and only then adapted into the NebuCtx local client and execution layer.
- The `reference/` tree is read-only source material and must never be modified.

## 1. Source Of Truth

This plan is based on these inputs, in this priority order:

1. The original lean-ctx runtime under `reference/rust/`.
2. The original lean-ctx product surface in `reference/README.md`, `reference/PROJECT.md`, and related docs.
3. The existing NebuCtx cloud runtime under `server/`.
4. `client/src-old/` as a donor for Nebu-specific behavior that must survive the inversion.
5. The remaining current NebuCtx implementation under `client/` only when needed for packaging compatibility.

If a future implementation detail is ambiguous, resolve it as follows:

1. Prefer original lean-ctx user-facing behavior.
2. Apply the project-based overrides defined in this document.
3. Preserve current deployment contracts only when they do not conflict with 1 or 2.

## 2. Problem Statement

Current NebuCtx is too far away from lean-ctx.

Observed divergence:

- The current Rust client only exposes 8 local tools.
- The current .NET cloud/server only exposes 2 tools: `ctx_brain` and `ctx_routes`.
- The CLI uses `server ...` as the primary remote UX instead of lean-ctx-style `cloud` language.
- The current `ctx_routes` implementation reports host routes from the .NET server, which is not the original lean-ctx meaning of the tool.
- The remote product story is centered on a server runtime instead of on a lean-ctx-like local experience with cloud-backed shared state.

That is the wrong product shape.

The previous implementation direction is also rejected:

- extending the reduced Nebu client outward will keep missing lean-ctx runtime behavior hidden until late
- restoring parity tool by tool from a reduced surface is slower and less safe than starting from the full lean-ctx runtime
- the correct default is to keep lean-ctx behavior unless NebuCtx Cloud or project-based identity requires an explicit change

Target product statement:

- NebuCtx is lean-ctx with NebuCtx Cloud instead of LeanCTX Cloud.
- Lean-ctx runtime behavior is the default implementation baseline unless this plan explicitly overrides it.
- Local code intelligence and shell execution stay local.
- Shared state, project identity, dashboard data, and cross-session collaboration live in NebuCtx Cloud.
- The connected experience is the primary product path.

## 3. Non-Negotiable Decisions

These decisions are final for the realignment work.

1. Canonical user-facing remote terminology is `cloud`, not `server`.
2. The current `server` CLI commands remain only as compatibility aliases during migration.
3. The physical repository folder `server/` is not renamed during the realignment project.
4. The .NET host remains the deployed runtime, but it is described in product UX as NebuCtx Cloud.
5. The existing .NET host under `server/` is already the NebuCtx Cloud baseline and must be adapted, extended, and renamed in UX, not rebuilt from zero.
6. No second remote runtime is introduced during realignment.
7. `client/src-old/` is a donor/reference path only and is not the active architectural base.
8. The first implementation step is to restore a lean-ctx-derived runtime under `client/src/`.
9. NebuCtx persistent state is keyed by `project_id`, never by absolute workspace path.
10. The term `workspace` is removed from the product model; the canonical term is `checkout`.
11. The wire/property name `workspace_binding` becomes `checkout_binding` as the canonical name; the old name is accepted only as a backward-compatible alias.
12. The lean-ctx tool catalog becomes the canonical tool catalog again.
13. Nebu-specific tool names are not allowed as new primary surfaces.
14. `ctx_brain` remains supported only as a compatibility alias and is not a canonical long-term tool name.
15. NebuCtx Cloud must never attempt to directly read a developer's local checkout or run shell commands on the developer machine.
16. Hybrid tools are always local-first. The cloud does not initiate reverse RPC into the client in phase 1.
17. Local file contents and raw shell stdout are never uploaded to NebuCtx Cloud automatically.
18. Automatic sync only sends telemetry, hashes, relative paths, and derived metadata. Explicitly shared payload tools are the only exception.
19. `reference/` is read-only and cannot be used as an edit target.
20. The canonical runtime topology is `agent -> local MCP client (Rust) -> NebuCtx Cloud MCP (.NET) -> cloud dashboard`.
21. There is no product-local dashboard in the target architecture. Any client-local dashboard runtime, web UI, or local analytics server is compatibility debt to remove or reroute.
22. Canonical stats, gain, cost, heatmaps, wrapped metrics, and dashboard rollups live in NebuCtx Cloud only.
23. The Rust client may buffer telemetry locally for retry or offline durability, but local stats files are never the canonical analytics source of truth.
24. The local Rust MCP runtime is a local execution and sync edge only. It is not a second shared-state server and it does not own shared dashboard views.
25. **PostgreSQL is the only supported server storage backend.** SQLite store implementations are removed. Do not add SQLite support back. All server-side persistence uses Postgres only. `NEBULA_STORE=postgres` and `DATABASE_URL` are the only valid configuration path.
26. **The Rust client is the single MCP gateway.** Claude (and all AI agents) connect to exactly one MCP endpoint — the Rust client via stdio or `nebu-ctx serve`. The client serves local tools natively and proxies cloud tools (`ctx_brain`, `ctx_knowledge`, `ctx_session`) to the .NET server, automatically enriching each call with the current git context. Two-endpoint MCP setups are not a supported configuration.

## 4. Naming And UX Contract

### 4.1 Canonical terms

Use these terms in CLI help, docs, dashboard copy, and future issues/plans:

- `cloud`: the remote NebuCtx shared service, implemented by the existing .NET host and dashboard stack under `server/`
- `client`: the local Rust binary installed by the user
- `project`: the stable server-owned identity
- `checkout`: one local clone/path of a project on one machine

Do not use these terms as canonical product language:

- `server` as the primary user-facing term
- `workspace` as the persistence boundary

### 4.2 CLI contract

The canonical CLI surface after realignment is:

- `nebu-ctx cloud connect --endpoint <url> --token <token>`
- `nebu-ctx cloud status`
- `nebu-ctx cloud bind`
- `nebu-ctx cloud disconnect`
- `nebu-ctx sync`
- `nebu-ctx manifest`
- `nebu-ctx tools list`
- `nebu-ctx tools call <tool>`
- `nebu-ctx ctx_* ...`

Compatibility aliases that must keep working during migration:

- `nebu-ctx server connect`
- `nebu-ctx server status`
- `nebu-ctx server bind`
- `nebu-ctx server disconnect`

### 4.3 Cloud auth command policy

Original lean-ctx exposed consumer account commands such as `login`, `register`, `forgot-password`, and `contribute`.

Decision for NebuCtx:

- Do not add placeholder account commands unless NebuCtx Cloud implements the required user-account backend.
- The canonical remote auth flow for phase 1 is endpoint plus bearer token.
- Therefore the required cloud UX is `cloud connect/status/bind/disconnect` plus `sync`.
- If account-backed auth is added later, it must wrap or provision the same bearer-token connection model rather than create a second remote model.

### 4.4 Compatibility policy

Compatibility must be preserved in this order:

1. Existing deployment env vars and runtime ports.
2. Existing connected users using `server ...` commands.
3. Existing `ctx_brain` integrations.

Compatibility does not override the canonical naming decision. It only delays breaking changes.

### 4.5 Rename policy

The answer to "do we rename the current server to cloud?" is:

- yes in product terminology, CLI UX, dashboard copy, docs, and future planning
- no for the physical repository folder and solution layout during realignment
- no for the runtime baseline; the existing .NET host is the cloud runtime we are adapting

Do not start a repo-wide rename from `server/` to `cloud/` in this phase.

## 5. Product Architecture Contract

### 5.1 Rust client responsibilities

The Rust client owns:

- the lean-ctx runtime baseline as the starting implementation
- the local MCP runtime that agents connect to before requests reach NebuCtx Cloud
- local checkout discovery
- project fingerprint generation
- local tool execution for anything that needs a live working tree
- shell-hook and agent/editor bootstrap integration
- local caches and local archive files
- local retry/spool state for telemetry delivery when needed
- sync of telemetry and derived metadata to NebuCtx Cloud
- merged manifest/tool listing that combines local and cloud tools
- alias routing and backward-compatible CLI UX

The Rust client does not own:

- a persistent dashboard runtime
- canonical stats, gain, cost, heatmap, or wrapped views
- project-scoped shared analytics storage

### 5.1.1 Client restart policy

Implementation sessions must assume all of the following:

- `client/src/` is rebuilt from the lean-ctx runtime baseline
- `client/src-old/` is a donor for Nebu-specific compatibility behavior only
- missing lean-ctx runtime modules should be restored from the lean-ctx baseline before introducing Nebu-specific rewrites

Implementation sessions must not do any of the following:

- treat `client/src-old/` as the main base to extend
- rebuild the client by re-adding missing tools one by one to the reduced thin-client architecture
- drop lean-ctx runtime subsystems just because the current reduced client does not have them yet

### 5.2 NebuCtx Cloud responsibilities

The existing .NET host under `server/` already owns the runtime baseline and is the cloud runtime to extend:

- auth token validation
- project registry and project resolution
- project-scoped persistent state
- the only product dashboard runtime
- dashboard and `/api/*` endpoints
- `/health`, `/v1/manifest`, `/v1/tools`, `/v1/tools/call`
- cloud-owned tool handlers
- telemetry aggregation, stats, cost, gain, feedback, heatmaps
- cross-session memory, knowledge, agent coordination, tasks, workflows
- shared archives for cloud-owned tool outputs

### 5.2.1 Existing cloud baseline policy

Implementation sessions must assume all of the following:

- the .NET host already exists
- the dashboard stack already exists
- the packaging/runtime path already exists
- the work is to extend, rename in UX, and fill parity gaps in the existing runtime

Implementation sessions must not do any of the following:

- scaffold a second cloud service beside `server/`
- restart the cloud work from a blank solution
- treat "move from server to cloud" as a request to replace the existing .NET host

### 5.3 Hard boundary

Topology rule:

- agents talk to the local Rust MCP surface
- the local Rust MCP surface talks to NebuCtx Cloud over the cloud contracts
- the dashboard is served by NebuCtx Cloud only
- no client-owned dashboard or client-owned analytics API is part of the target product path

The client is allowed to send:

- repository fingerprint
- checkout metadata
- tool call telemetry
- relative file paths
- derived graphs, symbol maps, route maps, semantic index summaries, and similar derived metadata
- explicit payloads from user-invoked share/handoff/knowledge/session workflows

The client is not allowed to send automatically:

- full local file bodies
- raw shell stdout or stderr
- absolute local machine paths
- hidden local machine identifiers not required for project resolution

The client is not allowed to own canonically:

- dashboard HTTP routes for product analytics views
- cross-session stats rollups
- project-scoped gain/cost summaries
- project-scoped command or performance dashboards

### 5.4 Primary connected flow

The canonical connected flow is:

1. Agent connects to the local Rust MCP client.
2. The local client discovers checkout metadata.
3. The local client resolves the checkout to a `project_id` in NebuCtx Cloud.
4. The local client dispatches the requested tool.
5. Local tools run locally and sync telemetry or derived metadata.
6. Cloud tools run on NebuCtx Cloud.
7. Hybrid tools run locally first, then push or pull typed cloud state.
8. The cloud-hosted dashboard aggregates by `project_id`.

## 6. Project Identity Model

### 6.1 Canonical identifiers

NebuCtx must use these identifiers:

- `project_id`: server-generated durable primary key
- `project_slug`: human-readable stable project name
- `repository_fingerprint`: deterministic identity payload derived from repository metadata
- `checkout_binding`: a per-checkout alias record for one local clone/path
- `session_id`: a cloud session identifier for one connected client session

### 6.2 Repository fingerprint rules

`repository_fingerprint` must be built from stable repo properties only. Minimum required fields:

- normalized origin remote URL if present
- normalized repo host/provider
- normalized repo owner/name when derivable
- default branch when known
- repository root marker hash based on tracked manifests and root files

Allowed supplemental fields:

- HEAD commit hash
- language summary
- top-level project markers
- configured project slug hint

Absolute local path is forbidden as a fingerprint component.

### 6.3 Checkout binding rules

`checkout_binding` replaces the conceptual role of `workspace_binding`.

It may contain:

- local root path for diagnostics only
- current branch
- current HEAD commit
- client label
- machine label if user-configured
- last-seen timestamp

It may not be used as the durable identity key.

### 6.4 Persistence rules

All persistent file-oriented cloud records must use project-relative paths, not absolute paths.

Examples:

- heatmaps keyed by `project_id + relative_path`
- telemetry keyed by `project_id + tool_name + relative_path`
- graph metadata keyed by `project_id + relative_path`
- route metadata keyed by `project_id + relative_path`

### 6.5 Expected behavior

Required invariants:

- Two different local clones of the same repository resolve to the same `project_id`.
- Renaming or moving a local checkout does not create a new project.
- Two unrelated repositories never collide even if folder names match.
- The cloud can show multiple `checkout_binding` records for one `project_id`.
- Any previous `workspace_binding` field must deserialize into the new `checkout_binding` model.

## 7. Sync Rules

Use these sync rule labels in implementation and tests.

### Rule A: telemetry-only sync

Allowed payload:

- `project_id`
- `session_id`
- client label
- tool name
- duration
- token counts
- result counts
- relative paths
- language and file-kind hints

Forbidden payload:

- raw file contents
- raw shell output

### Rule B: derived-metadata sync

Allowed payload:

- Rule A fields
- file hashes
- graph edges
- symbol summaries
- route summaries
- semantic index vectors or hashes
- heatmap increments
- model or benchmark summaries

Forbidden payload:

- full source file body
- raw command output

### Rule C: explicit shared-payload sync

Allowed payload:

- user-requested context shares
- handoff ledgers
- knowledge entries
- workflow evidence
- cloud session snapshots

Rule C is the only rule that can intentionally move user-authored content to NebuCtx Cloud.

## 8. Canonical Tool Catalog And Ownership

The connected manifest must expose the 47 canonical lean-ctx tools plus the compatibility alias `ctx_brain`.

### 8.1 Local-only tools

These tools run entirely in the Rust client.

| Tool | Sync rule | Required NebuCtx behavior |
|------|-----------|---------------------------|
| `ctx_read` | A | Read from live local checkout only. Never remote-read source files. |
| `ctx_multi_read` | A | Same as `ctx_read`, batched locally. |
| `ctx_tree` | A | Local directory walk over the checkout. |
| `ctx_shell` | A | Local shell execution only. Cloud receives telemetry, not stdout. |
| `ctx_search` | A | Local search over live checkout. |
| `ctx_benchmark` | B | Benchmark local files/project and sync summary metrics only. |
| `ctx_analyze` | B | Analyze local file entropy and sync summary only. |
| `ctx_cache` | A | Client-local cache control only. |
| `ctx_discover` | A | Inspect local shell history only. |
| `ctx_smart_read` | A | Client-side read-mode selection over local files. |
| `ctx_delta` | A | Local incremental diff against client cache. |
| `ctx_edit` | A | Local file mutation tool. Never cloud-write project files. |
| `ctx_dedup` | B | Local cache dedup. May sync shared-block fingerprints only. |
| `ctx_fill` | A | Local token-budget packing over local file reads. |
| `ctx_response` | A | Local text compression utility. |
| `ctx_execute` | A | Local sandbox execution only. |
| `ctx_symbol` | A | Local symbol extraction. |
| `ctx_outline` | A | Local file outline extraction. |
| `ctx_compress_memory` | A | Local memory-file compression only. |
| `ctx_callers` | B | Local call graph query; may sync derived graph edges only. |
| `ctx_callees` | B | Local call graph query; may sync derived graph edges only. |
| `ctx_routes` | B | Reimplemented to inspect the local project, not .NET host routes. Current server-side `ctx_routes` is removed from canonical surface. |

### 8.2 Hybrid local-first tools

These tools execute in the client first and then push or pull typed cloud state.

| Tool | Sync rule | Required NebuCtx behavior |
|------|-----------|---------------------------|
| `ctx_compress` | C | Client submits local session delta; cloud returns canonical project/session checkpoint. |
| `ctx_intent` | B | Client sends query plus current project metadata; cloud stores and returns structured intent. |
| `ctx_context` | C | Client merges local cache state with cloud session state into one response. |
| `ctx_graph` | B | Graph build/query runs locally; client syncs graph metadata to cloud under `project_id`. |
| `ctx_share` | C | Client uploads selected cached contexts to cloud share store and can pull them back. |
| `ctx_overview` | B | Client builds overview from local project plus cloud wake-up data. |
| `ctx_preload` | B | Client computes task preload locally and syncs preload summary to cloud. |
| `ctx_prefetch` | B | Client prewarms local cache and syncs derived prefetch metadata. |
| `ctx_handoff` | C | Client creates the handoff bundle; cloud stores, lists, and distributes it. |
| `ctx_impact` | B | Impact analysis runs on local graph and syncs impact summary to cloud. |
| `ctx_architecture` | B | Architecture analysis runs locally and syncs architecture summary to cloud. |
| `ctx_semantic_search` | B | Client queries a local semantic index and may use cloud-stored baseline metadata for warm start. Phase 1 remains local-first. |
| `ctx_graph_diagram` | B | Diagram generation runs locally from graph data; cloud stores only diagram metadata or artifact references if explicitly requested. |
| `ctx_expand` | C | Client dispatches by archive namespace: local archive IDs resolve locally, cloud archive IDs resolve via NebuCtx Cloud. |

### 8.3 Cloud-owned tools

These tools execute on NebuCtx Cloud and store project-scoped shared state.

| Tool | Sync source | Required NebuCtx behavior |
|------|-------------|---------------------------|
| `ctx_session` | C | Canonical cross-session memory for the project. |
| `ctx_knowledge` | C | Canonical persistent project knowledge, gotchas, wakeup, timeline, search. |
| `ctx_agent` | C | Canonical multi-agent coordination and diary store. |
| `ctx_task` | C | Canonical cross-agent task orchestration. |
| `ctx_workflow` | C | Canonical workflow rails and evidence store. |
| `ctx_metrics` | A/B | Aggregated tool metrics from local and cloud telemetry. |
| `ctx_wrapped` | A/B | Periodic savings report from aggregated telemetry. |
| `ctx_cost` | A/B | Cost attribution derived from telemetry and model pricing. |
| `ctx_gain` | A/B | Gain reporting derived from telemetry and pricing models. |
| `ctx_feedback` | A/B | Canonical feedback threshold state aggregated per project and client. |
| `ctx_heatmap` | A/B | Canonical project-relative file heatmap from client tool events. |

### 8.4 Compatibility alias

| Tool | Status | Required NebuCtx behavior |
|------|--------|---------------------------|
| `ctx_brain` | compatibility alias only | Route to `ctx_knowledge` subset. `status` maps to `ctx_knowledge status`. `store` maps to `ctx_knowledge remember` in the `brain` category. `recall` maps to `ctx_knowledge recall`. Do not add new `ctx_brain` features. |

## 9. Dashboard Contract

The NebuCtx Cloud dashboard must mirror the lean-ctx cloud role, adapted to project-based data.

Canonical ownership rule:

- there is exactly one dashboard in the target product
- that dashboard is the existing server-hosted dashboard runtime under `server/`
- the Rust client must not ship or keep a parallel local dashboard as a first-class surface
- local stats files may exist only as transient transport state, never as the dashboard source of truth

Required views:

- Overview
- Daily Stats
- Commands
- Performance
- Knowledge
- Gotchas
- Adaptive Models
- Buddy
- Settings

Required data sourcing:

- Overview: aggregated telemetry by `project_id`
- Daily Stats: time-series metrics from tool calls
- Commands: cloud view of local and cloud tool telemetry
- Performance: gain, cost, feedback, compression statistics
- Knowledge: `ctx_knowledge`
- Gotchas: `ctx_knowledge` gotcha records
- Adaptive Models: cloud-stored feedback thresholds and model predictor summaries
- Buddy: synced buddy payload when available
- Settings: cloud endpoint, auth state, project bindings, sync settings

Operational constraints that remain unchanged during realignment:

- server-hosted dashboard on port `3333`
- MCP HTTP on port `4242`
- token file persistence at `/data/auth_token`
- existing env vars remain valid

Prohibited target state:

- no `client/src/local_dashboard/`-style product dashboard remains active
- no client-local `dashboard`, `gain`, `watch`, or similar analytics UI remains backed by local canonical stats
- no local analytics API becomes a second source of truth beside NebuCtx Cloud

## 10. Implementation Work Packages

### Execution rule

The work packages below are not optional branches.

Execution policy:

- implement WP0 through WP7 in order
- do not stop after WP1 just because the CLI naming is in place
- do not treat individual WPs as independent side quests
- each WP unlocks the next one and the expectation is continuous forward execution until the realignment is complete
- if a WP exposes a blocker, resolve that blocker and then continue with the next WP rather than reopening the product-definition discussion

### WP0: Freeze the target surface

Deliverables:

- This plan checked in under `docs/`
- explicit decision that `reference/` is read-only
- issue/task list derived from this plan rather than from the old migration plan

Exit criteria:

- No active implementation task uses the old product definition
- All new work references this document for naming and ownership decisions

### WP1: Re-establish the Rust client from the lean-ctx runtime baseline

Client changes:

- replace the reduced `client/src/` implementation with a lean-ctx-derived runtime baseline from `reference/rust/src/`
- keep `client/src-old/` as a donor/reference only
- adapt package metadata, binary names, and crate layout so the runtime builds as `nebu-ctx`
- keep cloud-facing behavior pointed at the existing `.NET` cloud runtime under `server/`

Exit criteria:

- `client/src/` is again a runtime-first client instead of a thin-client skeleton
- the crate builds from the lean-ctx-derived source tree
- `client/src-old/` is no longer the active implementation path

### WP2: Restore cloud-first UX without breaking current users

Client changes:

- add `cloud` command group in `client/src/cli.rs`
- keep `server` command group as an alias to the same handlers
- change help text, docs output, and JSON status labels to prefer `cloud`
- keep persisted connection format backward compatible

Exit criteria:

- `cloud connect/status/bind/disconnect` work
- `server ...` aliases still work
- help text leads with `cloud`, not `server`

### WP3: Rename workspace binding to checkout binding

Client changes:

- rename public Rust model terminology from `workspace_binding` to `checkout_binding`
- keep serde aliases so the old field still deserializes

Cloud changes:

- add canonical DTO/property name `checkout_binding`
- accept `workspace_binding` as input alias only
- update storage naming, logs, and dashboard copy to use `checkout`

Exit criteria:

- no new code introduces `workspace` as the identity boundary
- old clients can still resolve projects

### WP4: Audit and complete the local tool surface from the lean-ctx baseline

Client work:

- **Remove `client/src/cloud_server/` and `client/src/cloud_server_main.rs` entirely.** These are the original lean-ctx cloud backend (axum HTTP server, auth, DB, SMTP, `leanctx.com` routes). Our cloud is the existing `.NET` host under `server/`. There must be exactly one cloud implementation. The Rust cloud server module must not ship in any form — not as a binary, not as dead code, not as a feature flag.
- remove or reroute any client-local dashboard runtime and local analytics UI surface, including `client/src/local_dashboard/` and CLI/dashboard entry points that present local stats as the primary source
- audit the restored lean-ctx runtime against the canonical tool catalog in section 8
- remove or reroute only the pieces that belong in NebuCtx Cloud
- reimplement local `ctx_routes` to inspect the project, not the cloud host
- introduce client-side archive namespacing for `ctx_expand`
- keep client manifest merging behavior so local and cloud tools appear as one catalog

Implementation guidance:

- follow lean-ctx tool names and argument shapes unless this plan explicitly overrides them
- default to keeping a lean-ctx runtime feature unless there is a concrete reason to move or remove it
- keep local execution local; do not shortcut by proxying local tools through cloud
- if a feature currently renders or serves analytics from local files, move that responsibility to NebuCtx Cloud instead of preserving a second dashboard path

Exit criteria:

- connected manifest shows all canonical local and hybrid tools
- disconnected local-only mode still works for local tools that do not require cloud state
- no client-local dashboard remains as a first-class product surface

### WP5: Complete the cloud-owned shared-state surface in the existing .NET cloud

Cloud work:

- extend the existing .NET host and its current handlers, stores, and contracts; do not create a second cloud runtime

- implement `ctx_session`
- implement `ctx_knowledge`
- implement `ctx_agent`
- implement `ctx_task`
- implement `ctx_workflow`
- implement `ctx_metrics`
- implement `ctx_wrapped`
- implement `ctx_cost`
- implement `ctx_gain`
- implement `ctx_feedback`
- implement `ctx_heatmap`
- keep `ctx_brain` only as alias routing

Cloud tool policy:

- cloud tools own persistent state
- cloud tools never depend on local absolute paths
- cloud tools store project-relative paths only when needed

Exit criteria:

- all cloud-owned tools are callable from `/v1/tools/call`
- `ctx_brain` alias works without being part of the canonical docs path

### WP6: Complete the hybrid sync pipeline against the existing cloud runtime

Required sync endpoints or equivalent contracts:

- project resolve and bind
- tool call telemetry ingest
- project metadata ingest
- graph metadata ingest
- semantic metadata ingest
- share/handoff/session archive operations

Required client behavior:

- every local and hybrid tool emits Rule A telemetry
- Rule B tools emit derived metadata only
- Rule C tools emit explicit shared payloads only
- client-local stats persistence, if retained, is only a delivery buffer or retry spool and is never treated as canonical analytics state
- client analytics commands are either removed or rerouted to cloud-backed data

Privacy constraint:

- automatic sync must be covered by tests proving that raw file bodies and raw shell output are not uploaded

Exit criteria:

- dashboard metrics update from client activity
- cloud-owned tools can use synced project metadata
- privacy tests are green
- client no longer presents local telemetry files as the primary dashboard or stats source

### WP7: Bring the existing dashboard to parity around project-scoped cloud data

Cloud work:

- map existing dashboard pages to the new cloud-backed project data without replacing the dashboard runtime
- preserve current runtime ports and token handling
- ensure all dashboard aggregates are keyed by `project_id`
- support multiple checkouts for one project without duplicating project totals
- absorb any remaining client-local analytics views into the existing cloud dashboard rather than keeping dual implementations

Exit criteria:

- dashboard shows one project with multiple checkouts correctly
- commands and performance views combine local and cloud telemetry
- there is no competing client-local dashboard path left in product UX

### WP8: Packaging and add-on stabilization

Required invariants:

- .NET cloud host remains the deployed runtime
- no Rust server runtime ships in container or add-on path
- Home Assistant packaging stays aligned with `3333`, `4242`, and `/data/auth_token`

Exit criteria:

- standalone container works
- add-on works
- connected Rust client works against the packaged cloud runtime

## 11. File-Level Execution Map

The next implementation sessions should treat these areas as the primary change surfaces.

### Client-side primary targets

- `reference/rust/src/` as the read-only implementation baseline
- `client/src/cli.rs`
- `client/src/models.rs`
- `client/src/server_client.rs`
- `client/src/local_tools.rs`
- `client/src/local_symbols.rs`
- `client/src-old/` as donor-only input when Nebu-specific compatibility is needed
- new client modules for missing local and hybrid tools as needed

### Cloud-side primary targets

- `server/src/NebuCtx.Contracts/`
- `server/src/NebuCtx.Application/`
- `server/src/NebuCtx.Tools/`
- `server/src/NebuCtx.Storage/`
- `server/src/NebuCtx.Dashboard/`
- `server/src/NebuCtx.Server.Host/`

### Test targets

- `client/tests/`
- `server/tests/`
- top-level `tests/`

## 12. Validation Requirements

This realignment is not complete until all of the following are validated.

### 12.1 Tool catalog parity

Required checks:

- connected manifest exposes 47 canonical lean-ctx tools plus `ctx_brain` alias
- current local subset regression tests remain green
- cloud tool list matches section 8 exactly

### 12.2 Project identity correctness

Required checks:

- checkout A and checkout B of the same repo resolve to the same `project_id`
- path move does not create a new project
- unrelated repo does not collide
- all telemetry and dashboard rollups aggregate by `project_id`

### 12.3 UX compatibility

Required checks:

- `cloud ...` commands work
- `server ...` aliases work
- help output prefers `cloud`
- old persisted connection state still loads

### 12.4 Privacy boundary

Required checks:

- Rule A and Rule B payload tests prove no raw file body upload
- `ctx_shell` telemetry proves no raw stdout upload
- only Rule C tools can upload user content

### 12.5 Tool behavior correctness

Required checks:

- `ctx_routes` returns project routes, not cloud host routes
- `ctx_brain` alias round-trips into `ctx_knowledge`
- `ctx_expand` can retrieve both local and cloud archives via namespaced IDs
- `ctx_metrics`, `ctx_wrapped`, `ctx_cost`, `ctx_gain`, `ctx_feedback`, and `ctx_heatmap` reflect real local tool usage after sync

### 12.6 Deployment correctness

Required checks:

- dashboard on `3333`
- MCP HTTP on `4242`
- token file behavior unchanged
- container and add-on flows both work

### 12.7 Session Log: 2026-04-23 (Windows)

Completed:

- setup/bootstrap/rules/home-path regressions were fixed so the active client again behaves like the intended local Rust edge
- `cargo test --manifest-path client/Cargo.toml --test setup_ci_smoke -- --nocapture` is green and remains the current baseline validation for this realignment stream
- the client now enforces the cloud-only analytics boundary in the user-facing paths that mattered most for architectural correctness — committed as `51bdb9a` (`refactor: enforce cloud-only analytics boundary`)
- the full lean-ctx runtime source tree was merged into `client/src/` via `738eb60` (`[WIP] refactor client`)
- dist refreshed and committed

Known remaining debt at end of this session:

- `client/src/cloud_server/` directory exists but has zero references — safe to delete
- `client/src/local_dashboard/` and `client/src/heatmap.rs` remain present; still exported in `lib.rs`; still have unreachable match arms in `dispatch.rs` (lines ~215 and ~589) that must be removed before the module deletes will compile
- `client/src/lib.rs` still exports `local_dashboard` and `heatmap` modules
- `IWorkspaceBindingStore`, the SQL table `workspace_bindings`, and the DI registration in `Program.cs` still use workspace terminology — WP3 server-side is not complete
- WP5 cloud tools (ctx_session, ctx_knowledge, etc.) not yet implemented server-side
- build on Linux not yet verified

### 12.8 Session Log: 2026-04-23 (Linux — fresh Arch machine)

Completed:

- Installed Rust + .NET 10 + aspnet-runtime-10.0 via `sudo pacman` on fresh Arch Linux machine
- WP1 verified: `cargo test --manifest-path client/Cargo.toml --test setup_ci_smoke` green (4/4)
- **WP4 complete**: removed `cloud_server/`, `local_dashboard/`, `tui/`, top-level `heatmap.rs`; cleaned dead match arms from both `main.rs` and `dispatch.rs`; removed `ratatui`, `crossterm`; removed `embeddings` from default features — smoke still 4/4
- Fixed Cargo target dir: `.cargo/config.toml` keeps `target-dir = "client/target"`, `.gitignore` correctly excludes `/client/target/`
- **WP3 complete**: renamed `WorkspaceBinding` → `CheckoutBinding` and `IWorkspaceBindingStore` → `ICheckoutBindingStore` across full .NET server (contracts, storage, Postgres store, schema, StoreFactory, ProjectRegistry, ProjectApiEndpoints, McpContracts, ProjectResolutionContracts, Program.cs); renamed file `PostgresWorkspaceBindingStore.cs` → `PostgresCheckoutBindingStore.cs`; added idempotent Postgres migration (`ALTER TABLE workspace_bindings RENAME TO checkout_bindings`); deleted entire `Sqlite/` storage directory; removed SQLite exclusion from csproj; updated all tests
- Documented PostgreSQL-only rule as decision #25 in Section 3
- **WP5 complete**: implemented `ctx_knowledge` (remember/recall/status/remove/categories) and `ctx_session` (task/finding/decision/save/load/reset/list/cleanup) as full .NET tool handlers with Postgres storage (`knowledge_entries` and `session_state` tables), application services, store interfaces and Postgres implementations — 18/18 .NET tests passing
- **WP2 complete**: implemented real `sync` command in `client/src/cli/cloud.rs` — discovers git context, calls `/v1/projects/resolve`, updates checkout binding on cloud, outputs JSON summary; added `sync` to `cmd_cloud` dispatcher and help text

Commits this session: `830992d`, `9e8385a`, `e390ccc`, `34a3ab9`, `b34d750`, `21b1f1f`, `d8982bf`

Remaining at end of session:

- WP6: hybrid sync pipeline (client telemetry buffer → server ingestion) — not started
- WP7: dashboard parity with project-scoped knowledge/session/brain data — not started
- WP8: validate add-on container on Linux (`scripts/server/refresh-dist.sh` + `podman build` + `tests/local-addon-test.sh`)

### 12.9 Session Log: 2026-04-24 (Linux — WP8 validation)

Completed:

- fixed `scripts/server/build-image.sh` so Linux publish passes `-p:AllowMissingPrunePackageData=true`
- refreshed `server/dist/linux/` successfully
- built `nebu-ctx-addon-dev` from `homeassistant/Dockerfile`
- passed `bash tests/local-addon-test.sh` against the new image

Remaining at end of session:

- WP6: hybrid sync pipeline (client telemetry buffer → server ingestion) — not started
- WP7: dashboard parity with project-scoped knowledge/session/brain data — not started

## 13. Current Session Guide

This section reflects the state after the 2026-04-23 Linux session. WP0–WP5 are complete. Next sessions should begin with WP8 (easiest), then WP6/WP7.

### Execution constraints

- Do not start by renaming folders or doing large namespace churn.
- Do not start by building a second cloud/server runtime. The existing .NET host is the cloud runtime.
- Do not start from `client/src-old/`. It is a donor, not the base.
- PostgreSQL is the only supported store. Do not add SQLite back.

### Step 0 — Environment bootstrap (Linux)

See **Section 16** for the full Linux bootstrap procedure. Verify both toolchains:

```bash
cargo test --manifest-path client/Cargo.toml --test setup_ci_smoke -- --nocapture
dotnet vstest server/tests/*/bin/Debug/net10.0/*.dll --logger:"console;verbosity=detailed"
```

Both must be green before any code changes.

### Step 1 — WP8: Validate add-on container on Linux

```bash
bash scripts/server/refresh-dist.sh
podman build -t nebu-ctx-addon-dev -f homeassistant/Dockerfile .
bash tests/local-addon-test.sh
```

Fix any failures before moving to WP6/WP7.

### Step 2 — WP6: Hybrid sync pipeline

Design and implement client-side telemetry buffering and server-side ingestion endpoint. Refer to the non-negotiable decisions in Section 3 (decisions 17–24) for scope constraints.

### Step 3 — WP7: Dashboard parity

Route project-scoped `ctx_knowledge`, `ctx_session`, and `ctx_brain` data into the dashboard views served by the .NET host.

### Step 3 — Re-run baseline after cleanup

```bash
cargo test --manifest-path client/Cargo.toml --test setup_ci_smoke -- --nocapture
```

Must stay green. If it breaks, diagnose before continuing.

### Step 4 — WP3 server-side: rename workspace → checkout in .NET

This is a focused rename, not a large refactor. Change these in one commit:

- `IWorkspaceBindingStore` → `ICheckoutBindingStore` (interface name in `NebuCtx.Storage`)
- All implementing classes renamed accordingly (Sqlite and Postgres stores)
- SQL table/schema init: `workspace_bindings` → `checkout_bindings`
- DI registration in `server/src/NebuCtx.Server.Host/Program.cs`
- Any dashboard copy, log messages, and variable names using `workspace_binding` in the server tree

Accept `workspace_binding` as an inbound JSON alias but never produce it as output.

Commit: `refactor: rename workspace_binding to checkout_binding in server storage`

Run `dotnet test server/NebuCtx.slnx` and verify green before continuing.

### Step 5 — WP5: First cloud tool — `ctx_knowledge`

Add `KnowledgeToolHandler` in `server/src/NebuCtx.Tools/Knowledge/KnowledgeToolHandler.cs`:
- Behavioral spec: the lean-ctx local implementation at `client/src/tools/ctx_knowledge.rs` and `client/src/core/knowledge.rs`
- State must be keyed by `project_id`, never by local path
- Register it in `ToolRegistration.cs`

Run `dotnet test server/NebuCtx.slnx` and verify green.

### Step 6 — WP5: Second cloud tool — `ctx_session`

Add `SessionToolHandler` in `server/src/NebuCtx.Tools/Session/SessionToolHandler.cs`:
- Behavioral spec: the lean-ctx local implementation at `client/src/tools/ctx_session.rs` and `client/src/core/session.rs`
- State keyed by `project_id`
- Register in `ToolRegistration.cs`

### Step 7 — WP2: Implement real `sync` command

`cmd_sync()` in `client/src/cli/cloud.rs` currently calls `removed_cloud_command("sync")`. Implement real sync behavior:
- Emit Rule A telemetry to the configured cloud endpoint
- The minimum viable sync is a POST to the cloud with the current checkout binding and a session heartbeat

### Step 8 — Continue WP5 remaining cloud tools

After `ctx_knowledge` and `ctx_session` are working, continue with:
`ctx_agent`, `ctx_task`, `ctx_workflow`, `ctx_metrics`, `ctx_wrapped`, `ctx_cost`, `ctx_gain`, `ctx_feedback`, `ctx_heatmap`

Each follows the same pattern: handler class → behavioral spec from lean-ctx local tool → state keyed by `project_id` → register in `ToolRegistration.cs` → tests green.

### Step 9 — WP6, WP7, WP8

Do not start these until Steps 1–8 are green. See the corresponding WP sections for details.

## 14. Definition Of Done

This realignment is done only when every statement below is true.

- NebuCtx feels like lean-ctx again from the user perspective.
- The active Rust client is derived from the lean-ctx runtime, not from the old reduced Nebu client.
- The canonical remote UX is `cloud`, with `server` retained only as compatibility alias or removed later.
- The connected manifest exposes the canonical lean-ctx tool surface again.
- Local tools stay local.
- Cloud tools own shared project state.
- Hybrid tools are local-first and sync only allowed payload classes.
- `project_id` is the only persistence identity.
- `checkout_binding` replaces `workspace_binding` as the canonical concept.
- `ctx_routes` analyzes the project, not the cloud host.
- `ctx_brain` is only an alias, not the primary product vocabulary.
- Dashboard and analytics are project-scoped and cloud-backed.
- Container and add-on packaging still honor the existing runtime contracts.
- The existing .NET host under `server/` serves as NebuCtx Cloud; no second cloud runtime was introduced.
- `client/src-old/` remained a donor/reference path and did not become the active implementation base.

## 15. Explicitly Out Of Scope Until After Realignment

The following work is deferred until the realignment is complete:

- renaming the physical `server/` repository folder
- large namespace churn in the .NET codebase
- cloud-initiated reverse RPC into the client
- automatic upload of full source code to the cloud
- feature invention that does not exist in lean-ctx or in this plan

This plan is intended to be executable without reopening the product definition debate.

## 16. Linux Environment Bootstrap

This section is required on any fresh Linux (Arch-based) machine before any build or test work.

### Install Rust

```bash
sudo pacman -S rustup
rustup default stable
rustc --version   # verify
cargo --version   # verify
```

Do not use `sudo pacman -S rust` — it installs the system Rust which lags behind stable and cannot be managed with rustup.

### Install .NET 10

The server solution targets `net10.0`. Verify the correct version:

```bash
sudo pacman -S dotnet-sdk-10.0
dotnet --version   # must print 10.x.x
```

If `dotnet-sdk-10.0` is not available in the main repos, install from the AUR:

```bash
# with yay or paru:
yay -S dotnet-sdk-10.0
```

Or use the Microsoft feed script:

```bash
curl -sSL https://dot.net/v1/dotnet-install.sh | bash -s -- --channel 10.0
```

Then add to your shell profile:
```bash
export DOTNET_ROOT="$HOME/.dotnet"
export PATH="$PATH:$HOME/.dotnet:$HOME/.dotnet/tools"
```

### Verify baseline build

```bash
# Rust client smoke test — must be green before any work starts
cargo test --manifest-path client/Cargo.toml --test setup_ci_smoke -- --nocapture

# .NET server tests
dotnet test server/NebuCtx.slnx
```

If the Rust smoke test fails: check compile errors before continuing. The lean-ctx runtime in `client/src/` was merged as a WIP commit (`738eb60`) and the Linux build has not been verified yet. Fix any compile errors, do not skip past them.

### Optional: PostgreSQL for local server testing

The server's primary storage is PostgreSQL. For local connected-flow testing:

```bash
sudo pacman -S postgresql
sudo systemctl enable --now postgresql
# or use docker/podman:
podman run -d --name nebu-pg -e POSTGRES_PASSWORD=dev -p 5432:5432 postgres:16
```

Set env vars:
```bash
export NEBULA_STORE=postgres
export DATABASE_URL=postgres://postgres:dev@localhost:5432/nebu_ctx
```
