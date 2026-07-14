## Why

The client already persists session state locally, pushes derived facts to the server, and retries failed writes through a local outbox. Three gaps remain: (1) session snapshots have no schema version, so future envelope changes are unmanageable and existing unversioned rows have no migration path; (2) the outbox replays writes by request ID but has no deterministic operation identity that survives client restart, so a retried write after reboot creates server-side duplicates; (3) the dashboard exposes memory content but provides no visibility into sync health — pending, failed, replayed, or stale operations are invisible to operators.

Public MCP surface stays unchanged. Raw journal entries, prompts, and assistant transcripts remain client-local (already true). The existing outbox mechanism, lifecycle flushes at stop/idle/pre-compact, shared-memory-project workaround, and promotion identities are already in place and are not rebuilt here.

## What Changes

- Add `schema_version` to the server's session state model, with a compatibility reader for existing unversioned rows and migration-on-write when an unversioned session is next saved.
- Extend the client outbox to carry a deterministic operation identity on every write (brain, knowledge, session, telemetry), and extend server write handlers to deduplicate on that identity rather than only on request ID.
- Add a server-side sync-health endpoint and dashboard panel exposing accepted, duplicate, stale, pending, and failed operation counts with bounded metadata (no bearer tokens, no transcripts).
- Add end-to-end tests covering: versioned session save/load/legacy-migration, outbox replay with duplicate delivery after simulated restart, sync health aggregation, and cross-project isolation.

## Capabilities

### Modified Capabilities
- `hosted-session-snapshots`: now versioned with schema envelope, legacy compatibility, and idempotent stale-rejecting updates.
- `memory-sync-observability`: server exposes bounded sync-health state; client outbox carries deterministic operation identity for all write types.

### Removed Capabilities
- `shared-memory-scope`: removed from scope. Client-side `shared_memory_project_context()` synthetic-project workaround remains sufficient for current use; explicit server-side identity-scoped sharing is deferred.

## Impact

- **Client**: `client/src/core/sync_outbox.rs` (operation identity field + determinism), `client/src/server_client.rs` (pass identity on all write types), `client/src/core/session.rs` (versioned envelope).
- **Server**: `server/src/NebuCtx.Contracts/Mcp/SessionContracts.cs` (schema_version), `server/src/NebuCtx.Storage/Postgres/PostgresSessionStore.cs` (legacy reader + migration-on-write), `server/src/NebuCtx.Server.Core/Services/SessionService.cs` (stale rejection), handler-level deduplication in Brain/Knowledge/Session stores, new sync-health endpoint in dashboard.
- **Tests**: new E2E tests in `tests/` (or server integration tests) for versioned session round-trip, duplicate delivery after restart, sync health, and cross-project isolation.
- **No new external dependency.**
