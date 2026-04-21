# Server Setup Guide

`nebula-ctx` currently supports three operator modes:

| Mode | Transport | Primary use |
|------|-----------|-------------|
| Local CLI | stdio | IDE integration and single-machine use |
| HTTP MCP server | HTTP | Remote MCP access and production service mode |
| Home Assistant addon | HTTP | Add-on packaging around the same `serve` command |

## Build

For PostgreSQL-backed operation, build with the cloud feature enabled:

```bash
cargo build --release --features cloud-server
```

## Environment Variables

| Variable | Description |
|----------|-------------|
| `NEBULA_STORE` | `sqlite` or `postgres` |
| `DATABASE_URL` | PostgreSQL connection string when `NEBULA_STORE=postgres` |
| `NEBULA_CTX_DATA_DIR` | Data directory for SQLite state and local files |
| `NEBULA_CTX_HTTP_TOKEN` | Bearer token used by `serve` when you do not pass `--auth-token` |
| `RUST_LOG` | Log level for the binary |

Notes:

- The storage env var is `NEBULA_STORE`, not `NEBULA_CTX_STORE`.
- The HTTP port is controlled by the CLI flag `--port`; there is no runtime port env var in the binary today.

## Local Postgres Workflow

If your repo `.env` file uses Windows line endings, source it on Linux like this:

```bash
set -a
source <(tr -d '\r' < .env)
set +a
```

Then verify the database path:

```bash
./target/release/nebula-ctx db status
./target/release/nebula-ctx db init
./target/release/nebula-ctx db test
```

## Start the HTTP MCP Server

```bash
./target/release/nebula-ctx serve \
  --host 127.0.0.1 \
  --port 8099 \
  --auth-token my-secret-token
```

For non-loopback binds, pass an auth token. The server refuses unsafe `0.0.0.0` binds without authentication.

## Smoke Test

```bash
curl -H 'Authorization: Bearer my-secret-token' \
  http://127.0.0.1:8099/health

curl -H 'Authorization: Bearer my-secret-token' \
  http://127.0.0.1:8099/v1/tools

curl -X POST \
  -H 'Authorization: Bearer my-secret-token' \
  -H 'Content-Type: application/json' \
  http://127.0.0.1:8099/v1/tools/call \
  -d '{
    "name": "ctx_brain",
    "arguments": {
      "action": "status",
      "brain_id": "default"
    }
  }'
```

The `/v1/tools/call` payload must include `name` and `arguments`.

## Remote MCP Client Registration

```json
{
  "mcpServers": {
    "nebula-ctx": {
      "type": "http",
      "url": "http://your-server:8099/v1/tools/call",
      "headers": {
        "Authorization": "Bearer my-secret-token"
      }
    }
  }
}
```

## Docker

Build:

```bash
docker build -t nebula-ctx .
```

Run with PostgreSQL:

```bash
docker run --rm \
  -p 8099:8099 \
  -e NEBULA_STORE=postgres \
  -e DATABASE_URL='postgres://user:pass@db:5432/nebula' \
  -e NEBULA_CTX_HTTP_TOKEN='my-secret-token' \
  nebula-ctx
```

The container entrypoint now starts `nebula-ctx serve --port 8099` automatically. It binds to `0.0.0.0` only when `NEBULA_CTX_HTTP_TOKEN` is set.

## Home Assistant Addon

The add-on under `homeassistant/` is now packaged as a standalone Home Assistant add-on.

Operational guidance:

1. Use `Open Web UI` to access the dashboard through Home Assistant ingress.
2. Set `store` to `postgres` only when `database_url` is configured.
3. Set `auth_token` if you need the MCP port reachable from outside the add-on container.
4. Set `project_root` to a mounted path such as `/share` or `/config` so dashboard and MCP relative paths resolve meaningfully.
5. Expect the MCP service to stay loopback-only when no auth token is provided.

Connection model:

- Dashboard: Home Assistant ingress -> internal dashboard server on port `3333`
- MCP HTTP: direct network exposure on `4242/tcp` when enabled in Home Assistant network settings

## Brain Memory Tool

`ctx_brain` is the main validated Postgres-backed MCP surface today.

| Action | Purpose |
|--------|---------|
| `store` | Persist a new memory |
| `recall` | Recall memories for a brain ID |
| `consolidate` | Extract memories and open loops from session text |
| `activate` | Warm up a session from stored memory |
| `checkpoint` | Persist checkpoint content |
| `status` | Show counts for memories, loops, and latest session |
