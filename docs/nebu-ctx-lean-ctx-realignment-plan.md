# NebuCtx Lean-ctx Realignment Plan

Updated: 2026-04-23
Status: Authoritative
Audience: implementation sessions working in this repository

This document is the authoritative redesign plan for NebuCtx product realignment.
It is the only active redesign plan for NebuCtx product realignment.

The goal is explicit:

- NebuCtx must feel like lean-ctx again.
- The shared remote surface is our NebuCtx cloud service, backed by the existing .NET host under `server/`.
- The existing .NET host and dashboard stack already exist. Realignment work extends that runtime and renames its product surface to NebuCtx Cloud; it does not create a second cloud implementation.
- NebuCtx remains project-based, not workspace-based.
- The Rust binary remains the thin local client and local execution layer.
- The `reference/` tree is read-only source material and must never be modified.

## 1. Source Of Truth

This plan is based on these inputs, in this priority order:

1. The original lean-ctx surface in `reference/`.
2. The cloud and architecture notes in `reference/PROJECT.md` and `reference/README.md`.
3. The current NebuCtx implementation under `client/` and `server/`.

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

Target product statement:

- NebuCtx is lean-ctx with NebuCtx Cloud instead of LeanCTX Cloud.
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
7. NebuCtx persistent state is keyed by `project_id`, never by absolute workspace path.
8. The term `workspace` is removed from the product model; the canonical term is `checkout`.
9. The wire/property name `workspace_binding` becomes `checkout_binding` as the canonical name; the old name is accepted only as a backward-compatible alias.
10. The lean-ctx tool catalog becomes the canonical tool catalog again.
11. Nebu-specific tool names are not allowed as new primary surfaces.
12. `ctx_brain` remains supported only as a compatibility alias and is not a canonical long-term tool name.
13. NebuCtx Cloud must never attempt to directly read a developer's local checkout or run shell commands on the developer machine.
14. Hybrid tools are always local-first. The cloud does not initiate reverse RPC into the client in phase 1.
15. Local file contents and raw shell stdout are never uploaded to NebuCtx Cloud automatically.
16. Automatic sync only sends telemetry, hashes, relative paths, and derived metadata. Explicitly shared payload tools are the only exception.
17. `reference/` is read-only and cannot be used as an edit target.

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

- local checkout discovery
- project fingerprint generation
- local tool execution for anything that needs a live working tree
- shell-hook and agent/editor bootstrap integration
- local caches and local archive files
- sync of telemetry and derived metadata to NebuCtx Cloud
- merged manifest/tool listing that combines local and cloud tools
- alias routing and backward-compatible CLI UX

### 5.2 NebuCtx Cloud responsibilities

The existing .NET host under `server/` already owns the runtime baseline and is the cloud runtime to extend:

- auth token validation
- project registry and project resolution
- project-scoped persistent state
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

### 5.4 Primary connected flow

The canonical connected flow is:

1. Client discovers checkout metadata.
2. Client resolves the checkout to a `project_id` in NebuCtx Cloud.
3. Client dispatches the requested tool.
4. Local tools run locally and sync telemetry or derived metadata.
5. Cloud tools run on NebuCtx Cloud.
6. Hybrid tools run locally first, then push or pull typed cloud state.
7. Dashboard and other cloud views aggregate by `project_id`.

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

- dashboard on port `3333`
- MCP HTTP on port `4242`
- token file persistence at `/data/auth_token`
- existing env vars remain valid

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

### WP1: Restore cloud-first UX without breaking current users

Client changes:

- add `cloud` command group in `client/src/cli.rs`
- keep `server` command group as an alias to the same handlers
- change help text, docs output, and JSON status labels to prefer `cloud`
- keep persisted connection format backward compatible

Exit criteria:

- `cloud connect/status/bind/disconnect` work
- `server ...` aliases still work
- help text leads with `cloud`, not `server`

### WP2: Rename workspace binding to checkout binding

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

### WP3: Restore the full local tool surface in the Rust client

Client work:

- expand the local tool registry from the current 8-tool subset to the full local and hybrid tool catalog defined in section 8
- reimplement local `ctx_routes` to inspect the project, not the cloud host
- introduce client-side archive namespacing for `ctx_expand`
- keep client manifest merging behavior so local and cloud tools appear as one catalog

Implementation guidance:

- follow lean-ctx tool names and argument shapes unless this plan explicitly overrides them
- keep local execution local; do not shortcut by proxying local tools through cloud

Exit criteria:

- connected manifest shows all canonical local and hybrid tools
- disconnected local-only mode still works for local tools that do not require cloud state

### WP4: Complete the cloud-owned shared-state surface in the existing .NET cloud

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

### WP5: Complete the hybrid sync pipeline against the existing cloud runtime

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

Privacy constraint:

- automatic sync must be covered by tests proving that raw file bodies and raw shell output are not uploaded

Exit criteria:

- dashboard metrics update from client activity
- cloud-owned tools can use synced project metadata
- privacy tests are green

### WP6: Bring the existing dashboard to parity around project-scoped cloud data

Cloud work:

- map existing dashboard pages to the new cloud-backed project data without replacing the dashboard runtime
- preserve current runtime ports and token handling
- ensure all dashboard aggregates are keyed by `project_id`
- support multiple checkouts for one project without duplicating project totals

Exit criteria:

- dashboard shows one project with multiple checkouts correctly
- commands and performance views combine local and cloud telemetry

### WP7: Packaging and add-on stabilization

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

- `client/src/cli.rs`
- `client/src/models.rs`
- `client/src/server_client.rs`
- `client/src/local_tools.rs`
- `client/src/local_symbols.rs`
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

## 13. Order Of Execution For The Next Session

The next implementation session must start in this order.

1. Implement WP1 in `client/src/cli.rs` so the public UX moves back to `cloud` immediately.
2. Implement WP2 so project terminology becomes `checkout` instead of `workspace`.
3. Expand the local tool registry toward section 8.1 and 8.2, starting with the most visibly missing lean-ctx tools.
4. Add cloud DTOs and handlers for `ctx_session` and `ctx_knowledge` before broader analytics tools.
5. Replace canonical `ctx_routes` behavior so it analyzes the local project.
6. Add telemetry ingest so local tool use starts feeding cloud metrics early.
7. Bring in the remaining cloud-owned analytics and collaboration tools.
8. Finish dashboard parity last, once the data pipelines are real.

Do not start by renaming folders or doing large namespace churn. That work is intentionally deferred.
Do not start by building a second cloud/server runtime. The existing .NET host is the cloud runtime.
Do not stop after step 1. The required behavior is to continue through the list until the realignment workstream is actually advanced across all WPs.

## 14. Definition Of Done

This realignment is done only when every statement below is true.

- NebuCtx feels like lean-ctx again from the user perspective.
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

## 15. Explicitly Out Of Scope Until After Realignment

The following work is deferred until the realignment is complete:

- renaming the physical `server/` repository folder
- large namespace churn in the .NET codebase
- rebuilding a second cloud/server runtime from scratch
- a second remote auth model beyond endpoint plus bearer token
- cloud-initiated reverse RPC into the client
- automatic upload of full source code to the cloud
- feature invention that does not exist in lean-ctx or in this plan

This plan is intended to be executable without reopening the product definition debate.