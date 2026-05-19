# Design

## Goals

Move `nebu-ctx` toward a production-grade context runtime with:

- a much simpler dashboard information architecture
- stronger project memory behavior across Claude Code and OpenCode
- durable offline capture and replay for client-side activity
- server-visible project memory with admin operations

## Architecture direction

### Dashboard

The current dashboard is a single large HTML shell that hydrates itself with many overlapping `/api/*` calls. The long-term direction is:

- `Overview`
- `Live`
- `Memory`

This change narrows the operator surface to the views that are still actively useful: overview, live status, memory, agents, and token access. Legacy code-intelligence, context-pressure, and learning panels are removed from the primary navigation instead of being kept as dead weight.

This change begins that migration by introducing:

- `/api/dashboard/overview`
- `/api/dashboard/projects/{projectId}/memory`
- matching typed top-level contracts where practical
- a Memory Admin panel that uses the project-scoped endpoint for knowledge and brain inspection/deletion

Legacy `/api/*` endpoints remain in place during migration.

### Memory

Memory is split by role:

- `Knowledge`: durable project facts, conventions, gotchas, decisions
- `Brain`: episodic and prompt/session-oriented memory
- `Editor memory activation`: startup recall and hook-driven persistence

This change improves activation first:

- Claude setup now installs all relevant hooks
- `SessionStart` now injects startup memory, not only routing
- OpenCode now uses plugin hooks for startup system injection, shell interception, output compression, prompt capture, pre-compaction context injection, and idle-time session persistence
- prompt and session memory writes are queued when the server is offline

### Offline sync

The repo already persists local sessions and local knowledge, but server sync paths drop data on failure. This change introduces a durable outbox under `NEBU_CTX_DATA_DIR/sync/outbox`.

Initial operation types:

- telemetry ingest
- queued server tool calls for `ctx_knowledge` and `ctx_brain`

Each queued item is retried later when the client regains connectivity.

The CLI exposes the outbox through:

- `nebu-ctx sync status [--json]`
- `nebu-ctx sync flush [--json]`

`status --json` and `doctor` surface the same outbox health so users can see queued offline work without inspecting files manually.

`doctor` also reports dashboard port state and probes the configured host `/health` endpoint when a server connection is saved.

## Risks

- dashboard migration can drift if new contracts do not stay aligned with the current UI
- queued memory replay must not block interactive hooks
- partial sync is still better than silent drop, but full offline parity requires later phases

## Validation

- Rust unit tests for the outbox and hook config generation
- Rust CLI smoke coverage for `sync status --json`
- .NET integration coverage for the new dashboard endpoints
- targeted `cargo test` and `dotnet test` runs
