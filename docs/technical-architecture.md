# nebu-ctx Technical Architecture

This document describes the cleaned top-level architecture that the repo is now organized around.

## Core Model

`nebu-ctx` has two product surfaces that intentionally live in the same repository:

1. a thin Rust client under `client/`
2. a .NET MCP host and dashboard under `server/`

The client is installable.

The server is deployable.

The server and dashboard share the same PostgreSQL-backed state.

## Top-Level Layout

| Path | Role |
|------|------|
| `client/src/` | Rust CLI entrypoint and client flows |
| `client/tests/` | client-owned tests |
| `server/src/` | .NET host, dashboard, contracts, storage, tools, and project identity |
| `server/tests/` | .NET contract, integration, and project identity tests |
| `server/dist/linux/` | committed publish payload used by packaging |
| `scripts/server/` | publish and image build helpers |
| `tests/` | cross-stack smoke, add-on, and release validation |

## Runtime Shape

### Thin Client

The Rust client is installed from `client/` and is responsible for:

- saving server connection details locally
- binding the current workspace to a server-side project
- listing and calling remote MCP tools
- keeping the local install experience lightweight

### .NET Host

The .NET host lives at `server/src/NebuCtx.Server.Host/`.

It exposes:

- `GET /health`
- `GET /v1/manifest`
- `GET /v1/tools`
- `POST /v1/tools/call`
- `GET /api/*` dashboard endpoints on the dashboard port

The .NET host also owns:

- bearer auth
- request timeout, rate limit, and concurrency middleware
- project resolution and workspace binding
- server-side telemetry used by the dashboard

## Server Projects

The main .NET solution is split into focused projects under `server/src/`:

| Project | Responsibility |
|---------|----------------|
| `NebuCtx.Server.Host` | ASP.NET Core host and route mapping |
| `NebuCtx.Dashboard` | dashboard HTML and dashboard endpoint payloads |
| `NebuCtx.Application` | tool dispatch, telemetry, and application services |
| `NebuCtx.Contracts` | request and response contracts |
| `NebuCtx.Projects` | canonical project identity and workspace binding |
| `NebuCtx.Storage` | PostgreSQL-backed persistence |
| `NebuCtx.Tools` | MCP tool handlers such as `ctx_brain` and route inspection |
| `NebuCtx.Hosting` | environment binding, startup validation, and middleware |

## Storage Model

The main supported runtime path is PostgreSQL:

- `NEBULA_STORE=postgres`
- `DATABASE_URL=...`

The most validated shared-state path is `ctx_brain`.

The dashboard, project registry, and tool handlers are designed around that same server-side store.

## Request Flow

```text
Rust client or HTTP caller
  -> .NET host middleware
  -> project resolution / auth / limits
  -> tool registry
  -> tool handler
  -> PostgreSQL-backed stores
  -> dashboard telemetry
```

## Packaging Model

The packaging contract is dist-first:

- publish .NET output into `server/dist/linux/`
- build containers from `homeassistant/Dockerfile`
- keep `server/dist/linux/` committed and current

This is intentionally different from the client build contract:

- `client/target/` is disposable Cargo output
- `server/dist/linux/` is curated publish output

## Local Review Flow

For live inspection work, the preferred loop is:

1. run the .NET host locally against the target PostgreSQL database
2. connect the installed Rust client to that local host
3. store and recall a known marker through `ctx_brain`
4. review the dashboard on the same database and server instance

That keeps client behavior, server behavior, database writes, and dashboard screens aligned in one loop.

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
