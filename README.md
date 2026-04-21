# nebula-ctx

Rust MCP server and CLI for context engineering, token-efficient file and shell access, brain memory, PostgreSQL-backed persistence, and deployable HTTP runtimes.

> `nebula-ctx` is a fork of [lean-ctx](https://github.com/yvgude/lean-ctx). It keeps the lean-ctx context-engineering core and combines it with selected pieces from earlier Nebula projects, especially PostgreSQL persistence, brain-memory workflows, remote HTTP serving, and Home Assistant deployment.

[Upstream: lean-ctx](https://github.com/yvgude/lean-ctx) | [Server setup](docs/server-setup.md) | [Technical architecture](docs/technical-architecture.md) | [Home Assistant add-on](homeassistant/README.md) | [Roadmap](docs/plans/nebula-server-roadmap.md)

## Why nebula-ctx

`lean-ctx` is a strong local-first context engine for AI coding tools. `nebula-ctx` keeps that base, but pushes it toward persistent and deployable workflows.

- single Rust binary for stdio MCP, HTTP MCP, dashboard, setup, reporting, and operator commands
- 52 granular MCP tools in the current tool surface
- cached reads with 10 read modes, including `full`, `map`, `signatures`, `diff`, `task`, and `lines:N-M`
- compressed shell execution with 90+ command patterns
- SQLite for local use, PostgreSQL for persistent server-backed memory
- `ctx_brain` for memory-oriented workflows on top of the store layer
- HTTP MCP server for remote clients and server deployment
- Home Assistant add-on packaging with ingress dashboard support

## What this fork adds

This repository is not just `lean-ctx` renamed.

It combines the original lean-ctx context-reduction model with earlier Nebula work around:

- PostgreSQL-backed persistence
- brain-memory flows and checkpointing
- long-running HTTP MCP deployment
- Docker packaging and operator workflows
- Home Assistant add-on support

If you want the original upstream project, use [lean-ctx](https://github.com/yvgude/lean-ctx). If you want the same general context-engineering direction with persistent memory and deployable server surfaces, this fork is the purpose of `nebula-ctx`.

## Installation

There are four supported installation paths today.

### Install from source

Use this when you want the local CLI and the HTTP MCP server directly on your machine.

```bash
git clone https://github.com/MarkBovee/nebula-ctx.git
cd nebula-ctx
cargo build --release --features cloud-server
```

The compiled binary will be available at `./target/release/nebula-ctx`.

### Install as a Cargo binary

Use this if you want the binary installed into Cargo's bin directory.

From a local clone:

```bash
cargo install --path . --bin nebula-ctx --features cloud-server
```

Directly from GitHub:

```bash
cargo install --git https://github.com/MarkBovee/nebula-ctx --bin nebula-ctx --features cloud-server
```

### Install with Docker

Use this when you want a containerized HTTP MCP server.

```bash
docker build -t nebula-ctx .

docker run --rm \
  -p 4242:4242 \
  -e NEBULA_STORE=postgres \
  -e DATABASE_URL='postgres://user:pass@host:5432/nebula' \
  -e NEBULA_CTX_HTTP_TOKEN='replace-me' \
  nebula-ctx
```

### Install as a Home Assistant add-on

Use this when you want Home Assistant ingress for the dashboard plus an optional HTTP MCP endpoint.

1. Add `https://github.com/MarkBovee/nebula-ctx` to Home Assistant as a custom add-on repository.
2. Install the `nebula-ctx` add-on.
3. Configure the add-on options in Home Assistant.
4. Use `Open Web UI` for the dashboard.
5. Expose `4242/tcp` only if you want external MCP HTTP access.

## Quick start

### 1. Build with PostgreSQL support

```bash
cargo build --release --features cloud-server
```

### 2. Load environment variables

If your `.env` file came from Windows, remove CRLF on source:

```bash
set -a
source <(tr -d '\r' < .env)
set +a
```

### 3. Verify the database path

```bash
./target/release/nebula-ctx db status
./target/release/nebula-ctx db init
./target/release/nebula-ctx db test
```

### 4. Start the HTTP MCP server

```bash
./target/release/nebula-ctx serve \
  --host 127.0.0.1 \
  --port 4242 \
  --auth-token local-test-token
```

### 5. Smoke test the server

```bash
curl -H 'Authorization: Bearer local-test-token' \
  http://127.0.0.1:4242/health

curl -H 'Authorization: Bearer local-test-token' \
  http://127.0.0.1:4242/v1/tools

curl -X POST \
  -H 'Authorization: Bearer local-test-token' \
  -H 'Content-Type: application/json' \
  http://127.0.0.1:4242/v1/tools/call \
  -d '{
    "name": "ctx_brain",
    "arguments": {
      "action": "status",
      "brain_id": "default"
    }
  }'
```

### 6. Start the dashboard

```bash
./target/release/nebula-ctx dashboard
```

By default the dashboard serves on `http://127.0.0.1:3333`.

## Common commands

```bash
# Default stdio MCP mode
./target/release/nebula-ctx

# Guided client setup
./target/release/nebula-ctx setup

# Postgres lifecycle
./target/release/nebula-ctx db connect
./target/release/nebula-ctx db status
./target/release/nebula-ctx db init
./target/release/nebula-ctx db test

# HTTP MCP mode
./target/release/nebula-ctx serve --host 127.0.0.1 --port 4242 --auth-token local-test-token

# Dashboard
./target/release/nebula-ctx dashboard
```

## Feature overview

| Area | What it provides |
|------|------------------|
| Context tools | Cached file reads, tree views, regex search, shell compression, edit helpers, benchmarking, metrics, and context packing |
| Memory tools | Session memory, knowledge storage, agent coordination, task exchange, sharing, and `ctx_brain` workflows |
| Storage | SQLite by default, PostgreSQL when `NEBULA_STORE=postgres` |
| Server runtime | HTTP MCP server with `/health`, `/v1/manifest`, `/v1/tools`, and `/v1/tools/call` |
| Dashboard | Local web dashboard plus Home Assistant ingress support |
| Operator flow | `setup`, `doctor`, `gain`, `report`, DB commands, Docker, and add-on packaging |

## Deployment surfaces

| Surface | Command or location | Purpose |
|---------|---------------------|---------|
| Local CLI | `nebula-ctx` | stdio MCP for editor and agent integration |
| HTTP MCP server | `nebula-ctx serve --port 4242` | remote MCP access and long-running service mode |
| Dashboard | `nebula-ctx dashboard` | local dashboard on port `3333` by default |
| Docker | `Dockerfile` and `docker-entrypoint.sh` | containerized HTTP deployment |
| Home Assistant add-on | `homeassistant/` | ingress dashboard plus optional `4242/tcp` MCP exposure |
| Legacy cloud API | `nebula-ctx-cloud-api` | separate legacy surface, not the main MCP HTTP server |

## Storage and memory model

- `NEBULA_STORE=sqlite` or unset: local SQLite-backed operation
- `NEBULA_STORE=postgres`: PostgreSQL-backed `ContextStore`
- `DATABASE_URL`: required when `NEBULA_STORE=postgres`

Current validated Postgres-backed tool path: `ctx_brain`.

`ctx_brain` currently supports these core actions:

- `store`
- `recall`
- `consolidate`
- `activate`
- `checkpoint`
- `status`

## Home Assistant add-on

The add-on under `homeassistant/` is packaged as a standalone Home Assistant add-on.

- dashboard access is through Home Assistant ingress using `Open Web UI`
- dashboard traffic is routed to the internal dashboard server on port `3333`
- MCP access is separate on `4242/tcp`
- set `auth_token` if you want the MCP endpoint reachable outside the add-on container
- set `project_root` to a mounted path such as `/share` or `/config`

See [homeassistant/README.md](homeassistant/README.md) for the settings review and dashboard/MCP connection model.

## Upstream and lineage

`nebula-ctx` started from [lean-ctx](https://github.com/yvgude/lean-ctx), and that upstream project is the source of the context-engineering core that this repository builds on.

This fork then layers in selected pieces from earlier Nebula work, especially:

- persistent PostgreSQL-backed operation
- brain-memory tooling and workflows
- server-first deployment paths
- Home Assistant packaging and ingress integration

The result is a fork with a different operating model: less purely local-first, more persistent and deployable.

## Current status

Validated locally on `2026-04-20`:

- `cargo build --release --features cloud-server` passes
- `nebula-ctx db status`, `db init`, and `db test` pass against the Postgres settings in `.env`
- `nebula-ctx serve` responds on `/health`, `/v1/tools`, and `/v1/tools/call`
- `ctx_brain` `status`, `store`, and `recall` work over HTTP with `NEBULA_STORE=postgres`

The current production path is: one `nebula-ctx` binary, PostgreSQL selected with `NEBULA_STORE=postgres`, and MCP served from `src/http_server/mod.rs`.

## Documentation

- [docs/server-setup.md](docs/server-setup.md): operator setup, environment variables, Docker, and MCP registration
- [docs/technical-architecture.md](docs/technical-architecture.md): architecture and runtime walkthrough
- [docs/plans/nebula-server-roadmap.md](docs/plans/nebula-server-roadmap.md): current roadmap and hardening backlog
- [homeassistant/README.md](homeassistant/README.md): Home Assistant add-on settings and connection model

## Development

```bash
cargo test
cargo test --features cloud-server --test brain_memory_tests
cargo test --features cloud-server --test http_server_streamable
```

If you are working on the deployable PostgreSQL-backed path, build with:

```bash
cargo build --release --features cloud-server
```

## License

Apache 2.0. See [LICENSE](LICENSE).

Upstream attribution matters here: this project is derived from [lean-ctx](https://github.com/yvgude/lean-ctx), then extended with earlier Nebula work into the current `nebula-ctx` fork.