# Getting Started

This repository now has one canonical local flow:

1. install the Rust client from `client/`
2. run the .NET server from `server/src/` against PostgreSQL
3. verify the dashboard and MCP calls against the same database

## Canonical Repo Layout

| Path | Role |
|------|------|
| `client/` | installable Rust thin client package |
| `client/src/` | client source |
| `client/tests/` | client-owned tests |
| `client/target/` | Cargo-managed build output, disposable |
| `server/src/` | .NET host, dashboard, contracts, storage, tools |
| `server/tests/` | .NET contract, integration, and project identity tests |
| `server/dist/linux/` | committed publish payload used by Docker and the add-on |
| `scripts/server/` | publish and image scripts for the .NET server |
| `scripts/git/` | repo hooks such as stale-dist enforcement |
| `tests/` | cross-stack smoke, e2e, add-on, and release validation |

Artifact rule:

- `client/target/` is a normal tool-managed build folder and is not part of the product contract
- `server/dist/linux/` is a curated publish output and is part of the packaging contract

## 1. Install The Client

From a local checkout:

```bash
cargo install --path client --bin nebu-ctx --force
```

Or install into an isolated root while testing:

```bash
cargo install --path client --bin nebu-ctx --root .tmp/nebu-ctx-cli --force
```

## 2. Start The Server Locally

The local review flow should point to the same PostgreSQL database you want to inspect in the dashboard.

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

Expected local endpoints:

- dashboard: `http://127.0.0.1:3333/`
- MCP health: `http://127.0.0.1:4242/health`
- MCP tools: `http://127.0.0.1:4242/v1/tools`

## 3. Connect The Client

```bash
nebu-ctx server connect --endpoint http://127.0.0.1:4242 --token nctx_local_dev
nebu-ctx server status
nebu-ctx tools list
nebu-ctx server bind
```

## 4. Verify Shared Data Flow

Store and recall one marker through the same live server:

```bash
nebu-ctx ctx_brain action=store key=local-review-marker value=ok
nebu-ctx ctx_brain action=recall query=local-review-marker
```

This is the minimum proof that:

- the client is connected to the correct server
- the server is writing to the intended PostgreSQL database
- the dashboard can be reviewed against live data instead of fixture data

## 5. Review The Dashboard Screen By Screen

Use the dashboard on `http://127.0.0.1:3333/` and walk the UI in this order:

1. overview
2. live observatory
3. routes
4. knowledge graph
5. search and symbols
6. token and auth surface

For the next live session, prefer this same-database local flow over Docker unless you are validating packaging specifically.

## Packaging Commands

Refresh the committed server payload:

```bash
bash scripts/server/refresh-dist.sh
```

Build the server image from the committed payload:

```bash
bash scripts/server/build-image.sh
```

Run the cross-stack smoke check:

```bash
bash tests/local-server-cli-test.sh
```