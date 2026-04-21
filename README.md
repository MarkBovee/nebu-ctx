# nebula-ctx

Rust MCP server and CLI for context engineering, brain memory, and PostgreSQL-backed persistence.

## Installation

There are four supported installation paths today.

### Install From Source

Use this when you want the local CLI and the HTTP MCP server directly on your machine.

```bash
git clone https://github.com/MarkBovee/nebula-ctx.git
cd nebula-ctx
cargo build --release --features cloud-server
```

The compiled binary will be available at `./target/release/nebula-ctx`.

### Install As A Cargo Binary

Use this if you want the binary installed into Cargo's bin directory.

From a local clone:

```bash
cargo install --path . --bin nebula-ctx --features cloud-server
```

Directly from GitHub:

```bash
cargo install --git https://github.com/MarkBovee/nebula-ctx --bin nebula-ctx --features cloud-server
```

### Install With Docker

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

### Install As A Home Assistant Add-on

Use this when you want Home Assistant ingress for the dashboard plus an optional HTTP MCP endpoint.

1. Add this repository to Home Assistant as a custom add-on repository.
2. Install the `nebula-ctx` add-on.
3. Configure the add-on options under `homeassistant/`.
4. Use `Open Web UI` for the dashboard.
5. Expose `4242/tcp` only if you want external MCP HTTP access.

## Status

Validated locally on 2026-04-20:

- `cargo build --release --features cloud-server` passes
- `nebula-ctx db status`, `db init`, and `db test` pass against the Postgres settings in `.env`
- `nebula-ctx serve` responds on `/health`, `/v1/tools`, and `/v1/tools/call`
- `ctx_brain` `status`, `store`, and `recall` work over HTTP with `NEBULA_STORE=postgres`

The current production path is: one `nebula-ctx` binary, PostgreSQL selected with `NEBULA_STORE=postgres`, and MCP served from `src/http_server/mod.rs`.

## Quick Start

### Build

```bash
cargo build --release --features cloud-server
```

### Load Environment

If your `.env` file came from Windows, source it on Linux with CRLF removed:

```bash
set -a
source <(tr -d '\r' < .env)
set +a
```

### Verify Postgres

```bash
./target/release/nebula-ctx db status
./target/release/nebula-ctx db init
./target/release/nebula-ctx db test
```

### Start the HTTP MCP Server

```bash
./target/release/nebula-ctx serve \
  --host 127.0.0.1 \
  --port 4242 \
  --auth-token local-test-token
```

### Smoke Test the Server

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

## Key Commands

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
./target/release/nebula-ctx dashboard --port=4747
```

## Storage Model

- `NEBULA_STORE=sqlite` or unset: local SQLite-backed operation
- `NEBULA_STORE=postgres`: Postgres-backed `ContextStore`
- `DATABASE_URL`: required when `NEBULA_STORE=postgres`

Current validated Postgres-backed tool path: `ctx_brain`.

## Docker

```bash
docker build -t nebula-ctx .

docker run --rm \
  -p 4242:4242 \
  -e NEBULA_STORE=postgres \
  -e DATABASE_URL='postgres://user:pass@host:5432/nebula' \
  -e NEBULA_CTX_HTTP_TOKEN='replace-me' \
  nebula-ctx
```

The container entrypoint binds to `0.0.0.0` automatically when `NEBULA_CTX_HTTP_TOKEN` is set. Without a token it stays on `127.0.0.1` for safety.

## Home Assistant Addon

The add-on under `homeassistant/` is now structured as a standalone Home Assistant add-on package.

- Dashboard access is through Home Assistant ingress on the add-on `Open Web UI` action
- MCP access is separate on `4242/tcp`
- Set `auth_token` if you want the MCP endpoint reachable outside the add-on container
- Set `project_root` to a mounted path such as `/share` or `/config`

See [homeassistant/README.md](homeassistant/README.md) for the settings review and dashboard/MCP connection model.

## Architecture Notes

- `src/main.rs`: CLI entry point, stdio MCP, HTTP serve mode, dashboard, proxy, and utility commands
- `src/http_server/mod.rs`: MCP HTTP server with `/health`, `/v1/manifest`, `/v1/tools`, and `/v1/tools/call`
- `src/core/store/`: `ContextStore`, `SqliteStore`, and `PostgresStore`
- `src/tools/ctx_brain.rs`: brain-memory tool surface used in tonight's HTTP validation
- `src/cloud_server_main.rs`: separate legacy LeanCTX-style cloud API binary, not the main HTTP MCP server

## Roadmap And Docs

- Full execution plan: [docs/plans/nebula-server-roadmap.md](docs/plans/nebula-server-roadmap.md)
- Operator setup: [docs/server-setup.md](docs/server-setup.md)
- Codebase walkthrough: [docs/technical-architecture.md](docs/technical-architecture.md)

## Development

```bash
cargo test
cargo test --features cloud-server --test brain_memory_tests
cargo test --features cloud-server --test http_server_streamable
```
