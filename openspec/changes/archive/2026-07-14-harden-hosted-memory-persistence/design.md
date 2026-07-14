## Context

Server already persists `session_state` with `CloudSessionState` (JSONB). Client already has `OutboxEntry` with `attempts`/`last_error`, `deterministic_promotion_identity()` for fact promotion, lifecycle flushes at stop/idle/pre-compact via `sync_session_memory_to_server()`, and `shared_memory_project_context()` for multi-project sharing. This change fills the remaining gaps — versioned session envelopes, operation identity on every outbound write (not just promotion), and sync observability.

## Goals / Non-Goals

**Goals:**
- Add `schema_version` to session state, with a legacy decoder for unversioned rows and migration-on-write.
- Extend the outbox so every write kind (session, brain-ingest, knowledge-promote, telemetry) carries a deterministic operation identity, and extend server write handlers to return typed accepted/duplicate/stale outcomes.
- Expose sync health (pending, failed, accepted, duplicate, stale counts with timestamps) via a server endpoint.
- Add dashboard panel showing sync health for the current identity.

**Non-Goals:**
- Rebuilding the outbox mechanism or lifecycle flush — both already work.
- Adding server-side shared-memory scope — deferred; client-side `shared_memory_project_context()` remains sufficient.
- Storing raw journal, prompt, or transcript data on server — already client-local.
- Redesigning memory ranking, embeddings, or brain/knowledge lifecycle algorithms — existing behavior kept.
- Adding operator authentication for the dashboard health endpoint — deferred; existing dashboard auth model applies.

## Decisions

### 1. Versioned session envelope — additive JSONB field

Add a `schema_version` column or JSONB envelope field to `session_state`. Current rows get `null`/absent, treated as `schema_version = 0` by the legacy reader. On write, the client includes `schema_version: 1` in the envelope. The server rejects writes with an envelope older than the current accepted version (stale-write protection).

**Alternative considered:** add strongly-typed columns per field. Rejected — session state evolves frequently and JSONB already works.

### 2. Operation identity — UUID derived from content

Each outbound write carries an `operation_id: String` field computed deterministically from (kind, project_id, content_hash). The server stores the `operation_id` alongside the write result and returns `{ status: "accepted" | "duplicate" | "stale" | "rejected", operation_id }`. The client clears the outbox entry only on `accepted` or `duplicate`.

**Alternative considered:** server-assigned IDs. Rejected — client needs to generate the ID before the first attempt so retries after restart produce the same ID.

### 3. Sync health — aggregated at query time

Server endpoint `/sync/health` (or under `/dashboard/`) returns bounded counts aggregated from brain, knowledge, session stores plus a new `operation_log` or from telemetry. No separate log table — derive from existing store metadata where possible; otherwise add a lightweight `sync_operations` table with `operation_id`, `project_id`, `status`, `attempted_at`, `acknowledged_at`, `last_error`.

### 4. Dashboard sync health — new panel

Add a read-only panel to the existing dashboard showing counts and timestamps. No bearer-token exposure, no full outbox payloads.

### 5. Tests — E2E without live Postgres

Use the existing `NebuCtxTestFactory` in-memory stores for server-side session + dedup tests. Use a simulated-client harness for outbox replay + restart scenarios.

## Risks / Trade-offs

- **[Risk]** Adding `operation_id` to every write increases payload size slightly. **Mitigation:** UUID-sized field (~36 bytes), negligible.
- **[Risk]** Stale-write rejection could reject legitimate out-of-order deliveries from parallel sessions. **Mitigation:** operation ID is unique per (project, session, kind, content), so parallel sessions produce different IDs and are never stale.
- **[Risk]** Legacy unversioned session rows cannot be migrated eagerly. **Mitigation:** migration-on-write — the first save after deploy upgrades the row; old rows remain readable via legacy decoder indefinitely.
