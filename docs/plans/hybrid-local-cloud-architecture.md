# Hybrid Local/Cloud Architecture for nebula-ctx

> Plan created: 2026-04-20
> Status: **Phase 1.5: LeanCTX Cloud → Local Postgres** — db connect wizard implemented
> Review: [feedback summary (#revision-notes)](#revision-notes)

## Context

nebula-ctx has 48 MCP tools. Some need local filesystem (fast, <50ms). Others benefit from PostgreSQL persistence (cross-session, cross-project, sharing). Currently everything runs in one binary with either SQLite or Postgres — no hybrid mode exists.

**Goal**: Introduce hybrid execution for a small set of persistence-heavy tools. Local runner handles fast/filesystem tools. Cloud server handles persistent tools. Single binary, transparent routing, graceful degradation.

## Tool Classification

### Always-Local (unchanged, never touch network)

| Tool | Why local |
|------|-----------|
| ctx_read, ctx_multi_read, ctx_smart_read, ctx_delta | File I/O |
| ctx_edit | File writing |
| ctx_outline, ctx_symbol | tree-sitter AST parsing |
| ctx_tree | Directory traversal |
| ctx_shell | Shell execution |
| ctx_execute | Sandbox code execution |
| ctx_compress, ctx_compress_memory, ctx_response | Text compression |
| ctx_analyze, ctx_benchmark, ctx_discover | File analysis |
| ctx_routes | File parsing |
| ctx_graph_diagram | Visualization |
| ctx_overview, ctx_prefetch, ctx_fill | Local heuristics |
| ctx_dedup | Cross-file analysis |
| ctx_intent | Pattern matching |

### Cloud-Preferred with Fallback (v1 scope)

| Tool | Cloud benefit | Fallback quality |
|------|---------------|------------------|
| ctx_brain | Persistent memory, scoring, consolidation | **Safe** — local SQLite has subset |
| ctx_knowledge | Cross-project knowledge, temporal tracking | **Safe** — local knowledge still works |
| ctx_semantic_search | pgvector-backed search | **Safe** — local FTS5 still works |

### Cloud-Preferred, Degraded Fallback (v2 — not in scope for v1)

| Tool | Why degraded |
|------|-------------|
| ctx_session | Cross-session state is cloud-essential |
| ctx_agent | Agent diaries/coordination need shared persistence |
| ctx_task | Task orchestration is cross-agent |
| ctx_workflow | State machine needs shared state |
| ctx_handoff | Context ledger is cloud-first |
| ctx_share | Inter-agent sharing is cloud-only by nature |
| ctx_heatmap | Cross-session analytics need cloud aggregation |
| ctx_import | Data migration targets cloud |

### Out of Scope (documented for future reference only)

Local-first with async cloud sync. This is a second system, not a small bonus.
Examples: ctx_cache, ctx_graph, ctx_metrics, ctx_cost, ctx_gain, ctx_feedback.

**Rationale**: Local + async sync implies eventual consistency, race conditions, partial failures, sync retries. That's an entirely different system. Park until v1 proves itself.

## Architecture

### Routing: Separate `route_tool_call()` Function

NOT inline in dispatch. Dedicated routing function returns a routing decision:

```rust
enum RoutingPolicy {
    LocalOnly,           // Tier 1 tools — never touch network
    PreferCloud,         // Try cloud, fallback on transient failure
    PreferCloudDegraded, // Try cloud, fallback has reduced functionality
}

// Routing registry — one place for all routing decisions
fn routing_policy(tool_name: &str, args: &Value) -> RoutingPolicy {
    match tool_name {
        // Tier 1: always local
        "ctx_read" | "ctx_edit" | "ctx_shell" | ... => LocalOnly,

        // Tier 2 v1: cloud-preferred, safe fallback
        "ctx_brain" | "ctx_knowledge" | "ctx_semantic_search" => PreferCloud,

        // Tier 2 v2: cloud-preferred, degraded fallback (not in v1)
        "ctx_session" | "ctx_agent" | "ctx_task" | ... => PreferCloudDegraded,

        _ => LocalOnly,
    }
}
```

Future: can grow to `tool_name + action` granularity:

```rust
// Later: action-aware routing
("ctx_brain", "recall") => PreferCloud,
("ctx_brain", "status") => LocalOnly,  // local cache sufficient
```

### Dispatch Flow

```
Tool call arrives
  │
  route_tool_call(tool_name, args) → RoutingPolicy
  │
  ├─ LocalOnly → existing local handler (unchanged)
  │
  ├─ PreferCloud + cloud available + circuit closed
  │   → remote_call(tool_name, args)
  │   → success? return result
  │   → transient failure? log + fall through to local
  │
  └─ PreferCloud + no cloud / circuit open
      → existing local handler (degraded but available)
```

The existing dispatch match stays intact. `route_tool_call()` wraps it.

### CloudRouter (`src/cloud_router.rs`)

Circuit-breaker HTTP client with error-type awareness.

```rust
pub struct CloudRouter {
    client: reqwest::Client,
    base_url: String,
    api_key: String,
    circuit: Arc<Mutex<CircuitState>>,
}

struct CircuitState {
    consecutive_failures: u32,
    open_until: Option<Instant>,
    last_error_class: Option<ErrorClass>,
}

enum ErrorClass {
    Transient,    // timeout, connection refused, 5xx → counts toward breaker
    Permanent,    // 401, 403, config error → log + open breaker immediately
    Protocol,     // schema mismatch, version incompat → log clearly, don't retry
}
```

**Circuit breaker rules**:
- Transient errors: count toward breaker (open after 3 consecutive, reset after 30s)
- Permanent errors (401, 403): log loudly, open breaker immediately, don't auto-retry
- Protocol errors: log with version info, don't count toward transient threshold

### Cloud Protocol Contract

Local ↔ cloud communication has explicit versioning:

```json
{
  "protocol_version": 1,
  "request_id": "uuid-v4",
  "tool_name": "ctx_brain",
  "arguments": { "action": "recall", "query": "redis" },
  "client_version": "0.5.0"
}
```

Response:

```json
{
  "protocol_version": 1,
  "request_id": "uuid-v4",
  "status": "ok",
  "server_version": "0.5.0",
  "result": { "content": [...] }
}
```

**Incompatibility handling**: If `protocol_version` or tool signature mismatches, return Protocol error. Local side falls back gracefully with clear log message.

### Server State

```rust
pub struct LeanCtxServer {
    // ... existing fields unchanged ...
    pub cloud_router: Option<Arc<CloudRouter>>,  // None = local-only
}
```

Init from `NEBULA_CLOUD_URL` + `NEBULA_CLOUD_KEY` env vars. Absent = local-only, identical to today.

### Cloud Server Endpoint

```
POST /v1/tools/call
Authorization: Bearer <api_key>
Content-Type: application/json

{
  "protocol_version": 1,
  "request_id": "...",
  "tool_name": "ctx_brain",
  "arguments": {...},
  "client_version": "0.5.0"
}
```

Server validates protocol version → authenticates → dispatches to PostgresStore handler → returns versioned response.

## v1 Scope (What We Build)

**Only 3 tools go cloud**: ctx_brain, ctx_knowledge, ctx_semantic_search.

### Phase 1: CloudRouter + Routing Layer
- **NEW** `src/cloud_router.rs` — circuit-breaker HTTP client with error classification
- **NEW** `src/routing.rs` — `routing_policy()` function + `RoutingPolicy` enum
- **MODIFY** `src/server/mod.rs` — add `cloud_router` field to `LeanCtxServer`
- **MODIFY** `src/server/dispatch.rs` — call `route_tool_call()` before existing match

### Phase 2: Cloud Server Endpoint
- **NEW** `src/cloud_server/tool_exec.rs` — versioned tool execution handler
- **MODIFY** `src/cloud_server/mod.rs` — add `/v1/tools/call` route with protocol validation

### Phase 3: Wave 1 Tool Store Injection
- **MODIFY** `src/tools/ctx_brain.rs` — extract `handle_with_store(&dyn ContextStore, args)`
- **MODIFY** `src/tools/ctx_knowledge.rs` — extract `handle_with_store(&dyn ContextStore, args)`
- **MODIFY** `src/tools/ctx_semantic_search.rs` — extract `handle_with_store(&dyn ContextStore, args)`

Each existing `handle()` becomes thin wrapper: creates local SqliteStore, delegates to `handle_with_store()`.
Cloud server calls with PostgresStore.

### Phase 4: Prove It Works
- Test cloud routing for 3 tools
- Test fallback on cloud failure
- Test circuit breaker with different error types
- Test latency targets
- Existing test suite passes unchanged

## Out of Scope for v1

- **Wave 2 tools** (ctx_session, ctx_agent, ctx_task, etc.) — after v1 proves stable
- **Tier 3 hybrid sync** — entirely different system, park indefinitely
- **Action-level routing** — v1 uses tool-name only, action granularity is v2
- **Dashboard integration** — basic health check only
- **Analytics aggregation** — park

## Design Invariants

1. **Zero config for local mode** — no env vars = identical to today
2. **No always-local tool changes** — ctx_read, ctx_edit, etc. never touched
3. **Fallback preserves availability, not result parity** — tool works but results may differ when cloud-only state unavailable
4. **Single binary** — same binary for local stdio, HTTP, and cloud server
5. **Routing in one place** — `routing_policy()` function, not scattered through dispatch
6. **Protocol versioned from day 1** — prevents silent incompatibilities between local/cloud versions
7. **Error classes matter** — transient vs permanent vs protocol errors handled differently

## Verification

1. `cargo test` — all existing tests pass
2. Local-only mode — unset `NEBULA_CLOUD_URL`, all tools work via SQLite
3. Cloud routing — set env vars, call ctx_brain → verify PostgresStore used
4. Transient fallback — kill cloud server → ctx_brain still works via SQLite (possibly different results)
5. Permanent error — bad API key → breaker opens immediately, clear log, local fallback
6. Protocol mismatch — old client vs new server → clear error, no silent corruption
7. Circuit breaker — 3 transient failures → circuit opens, auto-retry after 30s
8. Latency — ctx_read <10ms (unchanged), ctx_brain cloud <200ms, ctx_brain fallback <50ms
9. E2E — Claude Code session, cloud enabled, knowledge persists across sessions

## Files Summary

| File | Action | Phase |
|------|--------|-------|
| `src/cloud_router.rs` | NEW | 1 |
| `src/routing.rs` | NEW | 1 |
| `src/server/dispatch.rs` | MODIFY | 1 |
| `src/server/mod.rs` | MODIFY | 1 |
| `src/cloud_server/tool_exec.rs` | NEW | 2 |
| `src/cloud_server/mod.rs` | MODIFY | 2 |
| `src/tools/ctx_brain.rs` | MODIFY | 3 |
| `src/tools/ctx_knowledge.rs` | MODIFY | 3 |
| `src/tools/ctx_semantic_search.rs` | MODIFY | 3 |

## Revision Notes

Incorporated review feedback:
- Fallback described as availability-preserving, not result-equivalent
- Routing extracted to dedicated `routing_policy()` function, not inline in dispatch
- Error classification (transient/permanent/protocol) in circuit breaker
- Protocol versioning + request IDs from day 1
- Phase 1 trimmed to 3 tools only (ctx_brain, ctx_knowledge, ctx_semantic_search)
- Tier 3 hybrid sync removed from scope entirely
- Tools classified by fallback quality (safe vs degraded)
- Wave 2 tools parked until v1 proves stable

## LeanCTX Cloud Replacement (v1.5)

Since we want to use our own Postgres (not LeanCTX Cloud SaaS), we replace cloud functions with local Postgres:

| LeanCTX Cloud Function | Local Replacement |
|----------------------|-------------------|
| `nebula-ctx login` | `nebula-ctx db connect` (new CLI wizard) |
| `nebula-ctx cloud status` | Show Postgres connection |
| `nebula-ctx sync` | Local Postgres persistence |
| `nebula-ctx contribute` | Local Postgres persistence |
| `cloud pull-models` | Manual model updates (or future feature) |

### New CLI: `nebula-ctx db`

```bash
nebula-ctx db status     # Show database status
nebula-ctx db connect   # Interactive Postgres setup wizard
nebula-ctx db init      # Initialize database schema
nebula-ctx db test     # Test connection
```

### Implementation: Phase 4

- **NEW** `src/cli/db.rs` — Database CLI commands
- **MODIFY** `src/cli/dispatch.rs` — Add `db` command
- **MODIFY** `src/cli/cloud.rs` — Remove/replace LeanCTX Cloud references

### Files to Modify for Phase 4

| File | Action | Description |
|------|--------|-------------|
| `src/cli/db.rs` | **NEW** | Database connection wizard |
| `src/cli/dispatch.rs` | MODIFY | Add `db` command routing |
| `src/cli/cloud.rs` | MODIFY | Deprecate LeanCTX Cloud login, replace with local Postgres |
| `Cargo.toml` | MODIFY | Add `cloud-server` to default features (optional) |

### Environment Variables

| Variable | Description |
|----------|-------------|
| `NEBULA_STORE` | `sqlite` (default) or `postgres` |
| `DATABASE_URL` | PostgreSQL connection URL |

### User Experience

```bash
# First run - guided setup
$ nebula-ctx db connect
=== Database Connection Setup ===
PostgreSQL host [localhost]: 192.168.1.135
Database name [nebula]: nebula
Database user [postgres]: postgres
Database password: ****
Testing connection...
✓ Connection successful!

# Or just use environment variables
export NEBULA_STORE=postgres
export DATABASE_URL=postgres://user:pass@host:5432/db
```
