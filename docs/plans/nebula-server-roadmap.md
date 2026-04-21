# Nebula Ctx Roadmap

> Updated: 2026-04-20
> Goal: run and test locally tonight, then harden for production deployment tomorrow

## Status Snapshot

| Area | Status | Notes |
|------|--------|-------|
| Core Rust binary | Done | `cargo build --release --features cloud-server` passes |
| Postgres DB CLI | Done | `db status`, `db init`, `db test` verified against `.env` |
| HTTP MCP server | Done | `/health`, `/v1/tools`, and `/v1/tools/call` verified locally |
| Postgres-backed `ctx_brain` over HTTP | Done | `status`, `store`, and `recall` verified |
| Docker and HA wrappers | In progress | obvious env/port bugs fixed tonight, not fully exercised end-to-end |
| Hybrid local/cloud router | Planned | design exists, implementation does not |
| Legacy LeanCTX cloud API binary | Exists | separate service, not the main MCP HTTP path |

## What Was Verified Tonight

1. Built the release binary with Postgres support.
2. Loaded Postgres settings from `.env` and verified database connectivity.
3. Started the HTTP MCP server with an auth token on `127.0.0.1:4242`.
4. Confirmed the server answered:
   - `/health`
   - `/v1/tools`
   - `/v1/tools/call`
5. Exercised `ctx_brain` over HTTP with `NEBULA_STORE=postgres`:
   - `status`
   - `store`
   - `recall`
6. Added a regression test for the runtime panic path.

## Fixes Landed Tonight

- `src/tools/ctx_brain.rs`
  - wrapped store-backed brain operations so they no longer panic when called from the HTTP Tokio runtime
  - added a regression test for the runtime failure mode
- `src/core/store/postgres.rs`
  - fixed timestamp decoding by casting Postgres time columns to text before deserializing into the existing string-backed structs
- `src/cli/db.rs`
  - cleaned up feature-gated Postgres CLI code and warning-producing branches
- `Dockerfile`
  - fixed runtime image healthcheck prerequisites
  - switched to an explicit entrypoint that starts `serve` on the intended port
- `docker-entrypoint.sh`
  - added environment-aware startup behavior for containerized `serve`
- `homeassistant/run.sh`
  - corrected `NEBULA_STORE` export
  - fixed explicit serve host and port handling

## Current Architecture We Can Rely On Tomorrow

- One `nebula-ctx` binary serves both stdio MCP and HTTP MCP.
- Postgres is selected with `NEBULA_STORE=postgres`.
- The main HTTP MCP server lives in `src/http_server/mod.rs`.
- The separate `cloud_server` binary is not the same thing as the HTTP MCP server.
- `ctx_brain` is the most clearly validated Postgres-backed MCP surface today.

## Production Tasks For Tomorrow

1. Pick the deployment shape.
   - bare binary under `systemd`
   - Docker container
   - Home Assistant addon

2. Normalize environment handling.
   - convert `.env` to LF on Linux, or stop sourcing it raw if it remains CRLF
   - keep `NEBULA_STORE` and `DATABASE_URL` in the runtime environment

3. Lock down HTTP exposure.
   - set a real auth token
   - choose bind host deliberately
   - put TLS and external exposure behind a reverse proxy if this leaves localhost

4. Add an operator smoke script.
   - `db test`
   - `/health`
   - `/v1/tools`
   - one `ctx_brain` call

5. Decide what to do with the legacy `cloud_server` binary.
   - keep it for old sync/auth flows
   - or explicitly de-scope it from tomorrow's deployment

6. Add service-level ops basics.
   - log destination and retention
   - restart policy
   - database backup plan
   - firewall / ingress rules

## Technical Debt And Improvements Backlog

### High Priority

- Convert `ContextStore` to an async-safe design.
  - Today the trait is sync while `PostgresStore` is async internally.
  - `ctx_brain` is now protected for the validated HTTP path, but broader Postgres-backed expansion should not depend on blocking bridges forever.

- Add a real end-to-end HTTP Postgres integration test.
  - The runtime panic and timestamp deserialization bug both escaped until live testing.
  - A single test covering `serve` + `ctx_brain store/recall` would catch both classes of regression.

- Clarify the separation between `serve` and `cloud_server_main`.
  - The docs had drifted into treating them like one system.
  - They are currently different binaries with different responsibilities.

### Medium Priority

- Expand validated Postgres coverage beyond `ctx_brain`.
  - `ctx_knowledge`
  - `ctx_semantic_search`
  - any graph-backed flows that are expected on the server

- Add deployment-level verification.
  - Docker build and live container smoke test
  - Home Assistant addon smoke test

- Keep operator docs synchronized with code.
  - env var naming drift already caused deployment confusion
  - port behavior drift already broke container/add-on startup assumptions

### Low Priority

- Improve `.env` ergonomics on Linux.
  - either normalize the file in the repo
  - or provide a helper wrapper script that loads it safely

- Revisit dashboard and HA integrations once the core server path is stable.

## Hybrid Routing Status

Hybrid local/cloud routing remains a future phase, but its relevant design and technical debt have been folded into this roadmap so there is only one active planning document.

## Validation Commands

```bash
cargo build --release --features cloud-server

set -a
source <(tr -d '\r' < .env)
set +a

./target/release/nebula-ctx db status
./target/release/nebula-ctx db init
./target/release/nebula-ctx db test

./target/release/nebula-ctx serve \
  --host 127.0.0.1 \
  --port 4242 \
  --auth-token local-test-token
```

Then, from a second shell:

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
      "brain_id": "smoke-test"
    }
  }'
```

## Exit Criteria For Tomorrow

- `nebula-ctx` starts cleanly with Postgres configured
- `db test` passes on the target machine
- `/health` and `/v1/tools` are reachable from the intended client path
- authenticated `/v1/tools/call` works without runtime aborts
- one real Postgres-backed `ctx_brain` `store` + `recall` cycle succeeds
