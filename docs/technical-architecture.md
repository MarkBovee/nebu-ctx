# nebula-ctx Technical Architecture

This document describes the code as it exists now, not the earlier aspirational design. The main points to keep straight are:

- the primary production path is `nebula-ctx serve`
- Postgres is selected with `NEBULA_STORE=postgres`
- the `cloud_server` binary is a separate service surface, not the main MCP HTTP server
- `ctx_brain` is the most clearly validated Postgres-backed MCP path today

## Entry Points

### Main Binary

`src/main.rs` is the entry point for the `nebula-ctx` binary.

Important modes:

- default: stdio MCP server
- `serve`: HTTP MCP server from `src/http_server/mod.rs`
- `dashboard`: dashboard HTTP UI
- `db`: Postgres and SQLite operator commands
- other CLI commands: setup, shell, proxy, gain, report tools, and utilities

The helper `run_async()` in `src/main.rs` creates a Tokio runtime for async command paths such as `serve` and `dashboard`.

### Legacy Cloud API Binary

`src/cloud_server_main.rs` starts the separate `cloud_server` application.

That binary serves LeanCTX-style cloud auth and sync endpoints out of `src/cloud_server/`. It is not the same as the main MCP server used by Claude Code or other MCP clients.

## High-Level Runtime Shapes

### Stdio MCP

```text
client -> stdin/stdout transport -> LeanCtxServer -> tool handlers
```

### HTTP MCP

```text
client -> src/http_server/mod.rs -> ContextEngine -> LeanCtxServer -> tool handlers
```

### Cloud API

```text
browser or sync client -> src/cloud_server/mod.rs -> auth/stats/sync handlers -> Postgres
```

## Main HTTP MCP Server

`src/http_server/mod.rs` owns the production HTTP MCP surface.

Routes currently exposed:

- `GET /health`
- `GET /v1/manifest`
- `GET /v1/tools`
- `POST /v1/tools/call`

There is also a streamable HTTP fallback service from `rmcp` for MCP transport compatibility.

### Request Flow

```text
HTTP request
  -> auth middleware
  -> rate limit middleware
  -> concurrency middleware
  -> route handler
  -> ContextEngine
  -> LeanCtxServer
  -> tool implementation
```

### Important Behavior

- non-loopback binds require auth
- `NEBULA_CTX_HTTP_TOKEN` is used only as a convenience source for `serve` if `--auth-token` is not passed
- the actual port is controlled by the `serve --port` flag

## LeanCtxServer And Tool Dispatch

`src/tools/mod.rs` defines `LeanCtxServer`, which holds shared runtime state such as:

- session cache
- session state
- tool call history
- workflow state
- ledger and pipeline stats

`src/server/mod.rs` implements the `rmcp` server trait for `LeanCtxServer`.

Tool dispatch still happens by matching the tool name and calling the relevant module under `src/tools/`.

There is no hybrid cloud router in the main dispatch path today.

## Core Areas

### Cache And Session State

Key shared state lives under `src/core/` and is usually held behind `Arc<RwLock<T>>`.

Examples:

- `src/core/cache.rs`
- `src/core/session.rs`
- `src/core/context_ledger.rs`
- `src/core/pipeline.rs`

This is the standard Rust pattern used throughout the server for shared mutable async state.

### Compression And Context Reduction

Compression and context-reduction behavior lives across:

- `src/core/compressor.rs`
- `src/compound_lexer.rs`
- `src/token_report.rs`
- various `ctx_*` tools that decide which compression mode or projection to apply

The codebase still carries the lean-ctx focus on token efficiency, but those systems are independent from the Postgres work verified tonight.

## Storage Layer

`src/core/store/mod.rs` defines `ContextStore` and the shared data models for persistent state.

Current implementations:

- `src/core/store/sqlite.rs`
- `src/core/store/postgres.rs`

Store selection is process-wide:

- unset or `NEBULA_STORE=sqlite` -> SQLite
- `NEBULA_STORE=postgres` -> Postgres

### Current Design Constraint

`ContextStore` is still synchronous, but `PostgresStore` is naturally async because it uses `deadpool-postgres` and `tokio-postgres`.

That mismatch is the main architectural debt in the persistence layer.

## Brain Memory Path

The brain-memory implementation is split across:

- `src/tools/ctx_brain.rs`
- `src/core/brain/activation.rs`
- `src/core/brain/consolidation.rs`
- `src/core/brain/scoring.rs`
- `src/core/store/*`

Supported actions today:

- `store`
- `recall`
- `consolidate`
- `activate`
- `checkpoint`
- `status`

### Validated Postgres Flow

The validated path from tonight looks like this:

```text
POST /v1/tools/call
  -> name=ctx_brain
  -> ctx_brain::handle()
  -> open_store()
  -> PostgresStore
  -> brain_* tables in Postgres
```

### Runtime Fix Added Tonight

The original HTTP Postgres path crashed because synchronous store access tried to `block_on` inside an active Tokio runtime.

`src/tools/ctx_brain.rs` now wraps the brain-store path in a runtime-safe blocking bridge so HTTP calls no longer abort the server on the validated code path.

That fix is good enough for the current deployment target, but it is not the final abstraction we want for broader Postgres coverage.

### Postgres Timestamp Fix Added Tonight

The second production bug was in `src/core/store/postgres.rs`: timestamp columns were being read directly into `String` fields.

That now works by casting time columns to `TEXT` in SQL before deserializing into the existing Rust structs.

## Postgres Schema Surfaces In Use

The current `PostgresStore` initializes and uses at least these tables for validated brain-memory operation:

- `brain_memories`
- `brain_sessions`
- `brain_checkpoints`
- `open_loops`
- `knowledge`
- `nodes`
- `edges`

The separate cloud API binary uses its own schema setup in `src/cloud_server/db.rs` for auth, stats, and sync tables.

## Deployment Wrappers

### Docker

The container build is defined in `Dockerfile`.

Important current behavior:

- release build with `cloud-server` feature enabled
- runtime image includes `curl` for healthchecks
- startup goes through `docker-entrypoint.sh`
- the entrypoint chooses host and port for `serve`

`docker-entrypoint.sh` binds to `0.0.0.0` only when a token is provided, otherwise it stays on `127.0.0.1`.

### Home Assistant Addon

The add-on wrapper lives in:

- `homeassistant/config.yaml`
- `homeassistant/run.sh`

Tonight's fixes corrected two mismatches:

- `NEBULA_STORE` is now exported correctly
- `serve` is now started explicitly with `--port 8099`

## Tests That Matter For This Path

The most relevant validated tests for tonight's work are:

- `tests/brain_memory_tests.rs`
- `tests/http_server_streamable.rs`
- `tools::ctx_brain::tests::postgres_errors_are_reported_without_runtime_panic`

The first two are existing integration tests. The third was added tonight to guard the runtime-panic failure mode.

## Current Gaps

These are the most important architectural gaps still open:

1. `ContextStore` should become async-safe instead of relying on blocking bridges.
2. More Postgres-backed tools need the same level of end-to-end verification as `ctx_brain`.
3. Docker and Home Assistant wrappers have been corrected, but they still need live deployment smoke tests.
4. The split between the main MCP HTTP server and the separate `cloud_server` service should stay explicit in future docs and code changes.

## Practical Mental Model

If you are debugging tomorrow, use this mental model first:

```text
Is the client talking to the main MCP server?
  -> yes: debug src/http_server/mod.rs, LeanCtxServer, tool handlers, and ContextStore
  -> no: if it is the legacy cloud API, debug src/cloud_server/* instead

Is the store set to postgres?
  -> yes: verify NEBULA_STORE and DATABASE_URL first

Is the failing tool ctx_brain?
  -> yes: inspect src/tools/ctx_brain.rs and src/core/store/postgres.rs first
```

That will get you to the real code path faster than treating the whole repo as one undifferentiated server.
