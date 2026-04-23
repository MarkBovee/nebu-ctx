# nebu-ctx Deployment Guide

## Canonical Deployment Model

The repository now deploys around these two artifacts:

- `client/` produces the installable Rust thin client
- `server/dist/linux/` produces the committed .NET server payload used by Docker and Home Assistant

`client/target/` is disposable build output.

`server/dist/linux/` is part of the packaging contract.

## Quick Start

### Refresh the server payload

```bash
bash scripts/server/refresh-dist.sh
```

### Build the container image

```bash
bash scripts/server/build-image.sh
```

### Run against PostgreSQL

```bash
docker run --rm \
	-p 3333:3333 \
	-p 4242:4242 \
	-e NEBULA_STORE=postgres \
	-e DATABASE_URL='postgres://user:pass@host:5432/db' \
	-e NEBULA_CTX_HOST='0.0.0.0' \
	-e NEBULA_CTX_HTTP_TOKEN='replace-me' \
	-e NEBULA_CTX_DASHBOARD_DISABLE_AUTH='1' \
	nebu-ctx-server:local
```

## Configuration

### Environment Variables
| Variable | Description |
|----------|-------------|
| `NEBULA_STORE` | use `postgres` |
| `DATABASE_URL` | PostgreSQL connection URL |
| `NEBULA_CTX_HOST` | host binding for MCP and dashboard |
| `NEBULA_CTX_HTTP_PORT` | MCP port, default `4242` |
| `NEBULA_CTX_PORT` | dashboard port, default `3333` |
| `NEBULA_CTX_HTTP_TOKEN` | MCP bearer token |
| `NEBULA_CTX_DASHBOARD_DISABLE_AUTH` | set to `1` for local dashboard review |

## Local Same-Database Review

When the goal is to inspect live data and walk the dashboard screen by screen, run the .NET host directly instead of the container:

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

Then connect the installed client:

```bash
nebu-ctx server connect --endpoint http://127.0.0.1:4242 --token nctx_local_dev
nebu-ctx server status
nebu-ctx tools list
nebu-ctx server bind
nebu-ctx ctx_brain action=store key=local-review-marker value=ok
nebu-ctx ctx_brain action=recall query=local-review-marker
```

Dashboard URL: `http://127.0.0.1:3333/`

## Validation Commands

```bash
bash tests/local-server-cli-test.sh
bash tests/local-addon-test.sh
```