# Server Setup Guide

The current canonical server runtime is the .NET host under `server/src/NebuCtx.Server.Host/`.

Use this guide for:

- local server runs against PostgreSQL
- same-database client and dashboard review
- Docker and add-on packaging prerequisites

## Canonical Server Surfaces

| Surface | Location | Notes |
|------|-----------|-------|
| .NET host source | `server/src/NebuCtx.Server.Host/` | authoritative HTTP MCP and dashboard runtime |
| .NET tests | `server/tests/` | contract and integration coverage |
| publish payload | `server/dist/linux/` | committed output used by Docker and Home Assistant |
| image scripts | `scripts/server/` | refresh/build/publish helpers |

## Environment Variables

| Variable | Description |
|----------|-------------|
| `NEBULA_STORE` | must be `postgres` for the main supported server path |
| `DATABASE_URL` | PostgreSQL connection string |
| `NEBULA_CTX_HOST` | MCP and dashboard bind host, usually `127.0.0.1` locally |
| `NEBULA_CTX_HTTP_PORT` | MCP HTTP port, default `4242` |
| `NEBULA_CTX_PORT` | dashboard port, default `3333` |
| `NEBULA_CTX_HTTP_TOKEN` | MCP bearer token |
| `NEBULA_CTX_DASHBOARD_DISABLE_AUTH` | set to `1` for a local no-auth dashboard review |
| `NEBU_CTX_TOKEN_FILE` / `NEBULA_CTX_TOKEN_FILE` | optional token file path |
| `LOG_LEVEL` | server log level |

Notes:

- The supported production-oriented path is PostgreSQL.
- Non-loopback MCP binds require `NEBULA_CTX_HTTP_TOKEN`.
- The dashboard can stay auth-free locally when `NEBULA_CTX_DASHBOARD_DISABLE_AUTH=1` and you bind it on its own port.

## Local Same-Database Workflow

If your repo `.env` file uses Windows line endings, source it on Linux like this:

```bash
set -a
source <(tr -d '\r' < .env)
set +a
```

Then start the server directly from the .NET host project.

PowerShell:

```powershell
$env:NEBULA_STORE = 'postgres'
$env:DATABASE_URL = 'postgres://user:pass@host:5432/db'
$env:NEBULA_CTX_HOST = '127.0.0.1'
$env:NEBULA_CTX_HTTP_PORT = '4242'
$env:NEBULA_CTX_PORT = '3333'
$env:NEBULA_CTX_HTTP_TOKEN = 'nctx_local_dev'
$env:NEBULA_CTX_DASHBOARD_DISABLE_AUTH = '1'
dotnet run --project server/src/NebuCtx.Server.Host/NebuCtx.Server.Host.csproj
```

Bash:

```bash
export NEBULA_STORE=postgres
export DATABASE_URL='postgres://user:pass@host:5432/db'
export NEBULA_CTX_HOST=127.0.0.1
export NEBULA_CTX_HTTP_PORT=4242
export NEBULA_CTX_PORT=3333
export NEBULA_CTX_HTTP_TOKEN=nctx_local_dev
export NEBULA_CTX_DASHBOARD_DISABLE_AUTH=1
dotnet run --project server/src/NebuCtx.Server.Host/NebuCtx.Server.Host.csproj
```

This is the preferred flow when you want to inspect the exact same PostgreSQL data through both the MCP API and the dashboard.

## Smoke Test

```bash
curl -H 'Authorization: Bearer nctx_local_dev' \
  http://127.0.0.1:4242/health

curl -H 'Authorization: Bearer nctx_local_dev' \
  http://127.0.0.1:4242/v1/tools

curl -X POST \
  -H 'Authorization: Bearer nctx_local_dev' \
  -H 'Content-Type: application/json' \
  http://127.0.0.1:4242/v1/tools/call \
  -d '{
    "name": "ctx_brain",
    "arguments": {
      "action": "status"
    }
  }'
```

Then connect the installed client:

```bash
nebu-ctx server connect --endpoint http://127.0.0.1:4242 --token nctx_local_dev
nebu-ctx server status
nebu-ctx tools list
nebu-ctx server bind
nebu-ctx ctx_brain action=store key=local-review-marker value=ok
nebu-ctx ctx_brain action=recall query=local-review-marker
```

Open the dashboard on `http://127.0.0.1:3333/` and review the screens against the same live data.

## Remote MCP Client Registration

```json
{
  "mcpServers": {
    "nebu-ctx": {
      "type": "http",
      "url": "http://your-server:4242",
      "headers": {
        "Authorization": "Bearer my-secret-token"
      }
    }
  }
}
```

## Docker

The repository uses one dist-first Dockerfile under `homeassistant/Dockerfile`.

Refresh the committed publish payload:

```bash
bash scripts/server/refresh-dist.sh
```

Build the image:

```bash
bash scripts/server/build-image.sh
```

Run with PostgreSQL:

```bash
docker run --rm \
  -p 4242:4242 \
  -p 3333:3333 \
  -e NEBULA_STORE=postgres \
  -e DATABASE_URL='postgres://user:pass@db:5432/nebula' \
  -e NEBULA_CTX_HOST='0.0.0.0' \
  -e NEBULA_CTX_PORT='3333' \
  -e NEBULA_CTX_HTTP_TOKEN='my-secret-token' \
  -e NEBULA_CTX_DASHBOARD_DISABLE_AUTH='1' \
  nebu-ctx-server:local
```

## Home Assistant Addon

The add-on under `homeassistant/` is now packaged as a standalone Home Assistant add-on.

Operational guidance:

1. Use `Open Web UI` to access the dashboard through Home Assistant ingress.
2. Configure PostgreSQL with the split fields: `postgres_host`, `postgres_port`, `postgres_database`, `postgres_username`, and `postgres_password`.
3. The add-on generates the MCP bearer token automatically on first start and shows it in the dashboard.
4. Set `project_root` to a mounted path such as `/share` or `/config` so dashboard and MCP relative paths resolve meaningfully.
5. Expect the add-on to expose MCP on `4242/tcp` only when that port is enabled in Home Assistant network settings.

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
