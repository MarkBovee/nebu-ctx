# nebu-ctx

Installable Rust client for the `nebu-ctx` .NET MCP server and dashboard stack, plus the server, Docker, and Home Assistant packaging needed to run it.

> `nebu-ctx` is the current product name for this context-engineering MCP server and dashboard stack. The published Cargo package installs the thin Rust client. The server runtime, dashboard, Docker image, and Home Assistant add-on live in this same repository.

[Getting started](docs/getting-started.md) | [Server setup](docs/server-setup.md) | [Technical architecture](docs/technical-architecture.md) | [Home Assistant add-on](homeassistant/README.md) | [Roadmap](docs/plans/nebula-server-roadmap.md)

## Why nebu-ctx

`nebu-ctx` targets the same context-engineering problem space, but with a more persistent and deployable operating model.

- published `nebu-ctx` binary is a thin Rust client that talks to the remote .NET MCP server
- first-run client onboarding prompts for server URL and auth token, then persists the connection locally
- client keeps local project-aware tools such as reads, tree, search, outline, callers, and callees
- remote tools such as `ctx_brain` run against the shared PostgreSQL-backed server state
- repository still contains the deployable .NET server, dashboard, Docker image, and Home Assistant add-on

## What this fork adds

This repository is not just a cosmetic rename.

It combines the original context-reduction model with earlier Nebula work around:

- PostgreSQL-backed persistence
- brain-memory flows and checkpointing
- long-running HTTP MCP deployment
- Docker packaging and operator workflows
- Home Assistant add-on support

If you want the current deployable stack with persistent memory and Home Assistant packaging, this repository is `nebu-ctx`.

## Canonical Layout

The repository is intentionally split by ownership and artifact lifecycle:

| Path | Purpose |
|------|---------|
| `client/` | installable Rust thin client package |
| `client/src/` | client source |
| `client/tests/` | client-owned tests |
| `client/target/` | Cargo build output, disposable |
| `server/src/` | .NET host, dashboard, contracts, storage, and tools |
| `server/tests/` | .NET tests |
| `server/dist/linux/` | committed publish payload used by Docker and Home Assistant |
| `scripts/server/` | publish and image scripts for the .NET server |
| `tests/` | cross-stack smoke, add-on, release, and repo-level validation |

Artifact rule:

- `client/target/` stays a normal tool-managed build folder
- `server/dist/linux/` stays the curated publish folder consumed by packaging

## Get Started In 3 Steps

The main local install flow is now: install the client, start the .NET server against PostgreSQL, then verify live MCP calls and the dashboard against the same database.

### 1. Install `nebu-ctx`

Pick one supported path:

```bash
# Install from crates.io
cargo install nebu-ctx

# Or install from a local clone while working in this repo
git clone https://github.com/MarkBovee/nebu-ctx.git
cd nebu-ctx
cargo install --path client --bin nebu-ctx --force
```

### 2. Start or use a running server

For the preferred local review flow, run the .NET server directly against the PostgreSQL database you want to inspect:

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

Then connect the client:

```bash
nebu-ctx server connect --endpoint http://127.0.0.1:4242 --token nctx_local_dev
```

The client normalizes a trailing `/mcp` automatically, but the current .NET server contract is rooted at `/health`, `/v1/manifest`, `/v1/tools`, and `/v1/tools/call`.

### 3. Restart and verify

Verify the saved connection and make a real MCP call.

```bash
nebu-ctx server status
nebu-ctx tools list
nebu-ctx server bind
nebu-ctx ctx_brain action=recall query=status
```

Detailed setup is in [docs/getting-started.md](docs/getting-started.md) and [docs/server-setup.md](docs/server-setup.md). The local client/server smoke script is [tests/local-server-cli-test.sh](tests/local-server-cli-test.sh).

## Additional Install Paths

### Docker

Use this when you want a containerized HTTP MCP server.

```bash
docker build -t nebu-ctx-local -f homeassistant/Dockerfile .

docker run --rm \
  -p 3333:3333 \
  -p 4242:4242 \
  -e NEBULA_STORE=postgres \
  -e DATABASE_URL='postgres://user:pass@host:5432/nebula' \
  -e NEBULA_CTX_HTTP_TOKEN='replace-me' \
  -e NEBULA_CTX_DASHBOARD_DISABLE_AUTH='1' \
  nebu-ctx-local
```

### Home Assistant Add-on

Use this when you want Home Assistant ingress for the dashboard plus an optional HTTP MCP endpoint.

1. Add `https://github.com/MarkBovee/nebu-ctx` as a custom add-on repository.
2. Install the `nebu-ctx` add-on.
3. Configure the PostgreSQL connection options.
4. Start the add-on and use `Open Web UI`.
5. Expose `4242/tcp` only if you want external MCP HTTP access.

The shipped add-on path builds the image from the committed `server/dist/linux` payload through the single Dockerfile in `homeassistant/Dockerfile`. The MCP token is generated on first boot and shown in the dashboard.

Local production-shaped smoke checks:

```bash
bash tests/local-server-cli-test.sh
bash tests/local-addon-test.sh
```

Both scripts load PostgreSQL settings from `.env`. The single server Dockerfile is dist-first, so the normal local flow is: publish the .NET host into `server/dist/linux`, then build the image from that output.

Manual server publish + image build:

```bash
bash scripts/server/build-image.sh
```

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\server\build-image.ps1
```

Manual server image publish:

```bash
IMAGE_REPOSITORY=ghcr.io/your-org/nebu-ctx IMAGE_TAG=v0.2.7 bash scripts/server/publish-image.sh
```

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\server\publish-image.ps1 -ImageRepository ghcr.io/your-org/nebu-ctx -ImageTag v0.2.7
```

Refresh the dist-first server payload without building an image:

```bash
bash scripts/server/refresh-dist.sh
```

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\server\refresh-dist.ps1
```

Install the repo pre-push hook that refreshes `server/dist/linux` and blocks the push when the generated files are dirty:

```bash
bash scripts/git/install-pre-push-dist.sh
```

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\git\install-pre-push-dist.ps1
```

## Local Review Flow

When we want to review live data and walk the screens one by one, use the same-database local flow:

1. point `DATABASE_URL` at the PostgreSQL database you want to inspect
2. run `dotnet run --project server/src/NebuCtx.Server.Host/NebuCtx.Server.Host.csproj`
3. connect the installed client with `nebu-ctx server connect --endpoint http://127.0.0.1:4242 --token ...`
4. verify with `nebu-ctx tools list`, `nebu-ctx server bind`, and `nebu-ctx ctx_brain ...`
5. review the dashboard on `http://127.0.0.1:3333/`

The detailed checklist lives in [docs/getting-started.md](docs/getting-started.md).

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
| Local CLI | `nebu-ctx` | thin Rust client installed from `client/` |
| HTTP MCP server | `dotnet run --project server/src/NebuCtx.Server.Host/NebuCtx.Server.Host.csproj` | local or deployed .NET MCP host |
| Dashboard | same .NET host on port `3333` | dashboard backed by the same PostgreSQL state |
| Docker | `homeassistant/Dockerfile` and `docker-entrypoint.sh` | unified standalone and Home Assistant container packaging |
| Home Assistant add-on | `homeassistant/` | ingress dashboard plus optional `4242/tcp` MCP exposure |
| Legacy cloud API | `nebu-ctx-cloud-api` | separate legacy surface, not the main MCP HTTP server |

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

`nebu-ctx` started from an earlier upstream context-engineering core, and that lineage still shows up in some internal type names and compatibility env vars.

This fork then layers in selected pieces from earlier Nebula work, especially:

- persistent PostgreSQL-backed operation
- brain-memory tooling and workflows
- server-first deployment paths
- Home Assistant packaging and ingress integration

The result is a fork with a different operating model: less purely local-first, more persistent and deployable.

## Current status

Validated locally on `2026-04-20`:

- `cargo build --release --features cloud-server` passes
- `nebu-ctx db status`, `db init`, and `db test` pass against the Postgres settings in `.env`
- `nebu-ctx serve` responds on `/health`, `/v1/tools`, and `/v1/tools/call`
- `ctx_brain` `status`, `store`, and `recall` work over HTTP with `NEBULA_STORE=postgres`

The current production path is: one `nebu-ctx` binary, PostgreSQL selected with `NEBULA_STORE=postgres`, and MCP served from `src/http_server/mod.rs`.

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

The codebase still contains internal compatibility names from earlier iterations, but the current product surface is `nebu-ctx`.