# Handoff

Updated: 2026-04-23
Repo: nebu-ctx
Status: cleaned structure documented, local live-review session pending

## Current State

- The top-level layout is now canonically split into `client/`, `server/`, `scripts/`, and repo-level `tests/`.
- The installable product is the Rust thin client in `client/`.
- The deployable server is the .NET host in `server/src/`.
- Docker and Home Assistant packaging consume the committed publish payload in `server/dist/linux/`.
- The empty root `dist/` directory is no longer the intended publish contract.

## Current Product Contract

- `client/target/` is disposable Cargo output.
- `server/dist/linux/` is curated publish output and part of the packaging flow.
- `homeassistant/Dockerfile` is the single dist-first container build path.
- `tests/` should stay focused on cross-stack, add-on, smoke, and release checks.

## What Was Updated In Documentation

- README now describes the canonical top-level layout.
- A new `docs/getting-started.md` documents the preferred same-database local loop.
- `docs/server-setup.md` now points to the .NET host instead of the old Rust server runtime.
- `DEPLOYMENT.md` now documents the dist-first server path.
- `AGENTS.md` and `docs/technical-architecture.md` now describe the cleaned client/server split.

## Next Live Session Goal

When returning, use one local loop against the same PostgreSQL database we want to inspect:

1. start the .NET host locally with the target `DATABASE_URL`
2. connect the installed Rust client to `http://127.0.0.1:4242`
3. verify `server bind`, `tools list`, and a `ctx_brain` store/recall roundtrip
4. open the dashboard on `http://127.0.0.1:3333/`
5. review the screens one by one against live data

## Recommended Commands For Next Session

PowerShell server start:

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

Client install and connect:

```bash
cargo install --path client --bin nebu-ctx --force
nebu-ctx server connect --endpoint http://127.0.0.1:4242 --token nctx_local_dev
nebu-ctx server status
nebu-ctx tools list
nebu-ctx server bind
nebu-ctx ctx_brain action=store key=local-review-marker value=ok
nebu-ctx ctx_brain action=recall query=local-review-marker
```

## Risks / Caveats

- The working tree is still dirty; do not assume packaging-only changes.
- There are substantial in-progress .NET files under `server/src/` and `server/tests/`.
- The local live-review session should validate real data flow before making UI judgments.

## Resume Point

Resume in `e:\Projects\Personal\nebu-ctx`.

Immediate next action when returning:

- run the same-database local review loop and inspect the dashboard screen by screen