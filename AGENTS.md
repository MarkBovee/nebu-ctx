# AGENTS

## Purpose

`nebu-ctx` is a Rust MCP server and dashboard stack with three main runtime shapes:

- stdio MCP via the main `nebu-ctx` binary
- HTTP MCP via `nebu-ctx serve`
- dashboard HTTP UI via `nebu-ctx dashboard`

There is also a separate legacy cloud API surface in `src/cloud_server_main.rs`. Do not treat that as the same runtime as the main MCP HTTP server.

## Main Surfaces

- `src/main.rs`: primary CLI and MCP entrypoint
- `src/http_server/`: main authenticated MCP-over-HTTP server
- `src/dashboard/`: dashboard UI and handlers
- `src/tools/`: MCP tool definitions and dispatch
- `src/core/`: storage, cache, memory, graph, embeddings, and shared runtime state
- `src/cloud_server/`: legacy cloud API, auth, sync, and stats service
- `homeassistant/`: Home Assistant add-on packaging and runtime wrapper
- `Dockerfile` and `docker-entrypoint.sh`: standalone container packaging for the MCP HTTP server

## Storage Model

- Default local mode uses SQLite.
- Postgres mode is selected with `NEBULA_STORE=postgres` and `DATABASE_URL`.
- The most validated Postgres-backed MCP path today is `ctx_brain`.
- The store abstraction still has async/sync technical debt; avoid broad store refactors unless the task requires them.

## Product Naming

- The current product/package/binary name is `nebu-ctx`.
- Compatibility aliases remain in places that would otherwise break users or tests abruptly.
- Internal names like `LeanCtxServer` and older env vars still exist in the codebase. Treat them as compatibility debt, not the preferred product surface.

## Home Assistant Add-on

The add-on runs two processes in one container:

- dashboard on port `3333`
- MCP HTTP server on port `4242`

The add-on wrapper is `homeassistant/run.sh`. Keep these files aligned whenever you change add-on behavior:

- `homeassistant/run.sh`
- `homeassistant/config.yaml`
- `homeassistant/README.md`
- `tests/local-addon-test.sh`

Current add-on behavior:

- supports `store=sqlite|postgres`
- accepts `database_url` or builds one from `postgres_*` fields
- accepts `auth_token`, or generates and persists a token in `/data/auth_token`
- logs the active token at startup for local and HA setup flows

## Release Flow

- `homeassistant/config.yaml` version and `Cargo.toml` version should stay in sync.
- `auto-release.yml` tags main when the version changes and no matching tag exists.
- `release.yml` builds tagged binaries and publishes release assets.
- The published Home Assistant Dockerfile builds from tagged source, not release-asset downloads.

If you change package, binary, or image names, update all of these together:

- `Cargo.toml`
- `.github/workflows/release.yml`
- `.github/workflows/auto-release.yml`
- `Dockerfile`
- `homeassistant/Dockerfile*`
- local smoke scripts under `tests/`

## Build And Validation

Use these commands as the default validation baseline:

```bash
cargo fmt --check
cargo test --release --features cloud-server
cargo build --release --features cloud-server --bin nebu-ctx
```

For add-on validation:

```bash
podman build -t nebu-ctx-addon-dev -f homeassistant/Dockerfile.dev .
bash tests/local-addon-test.sh
```

For the standalone container:

```bash
podman build -t nebu-ctx-server -f Dockerfile .
```

## Practical Guidance

- Prefer fixing runtime wrappers and release wiring at the root instead of adding more fallback docs.
- Do not conflate the main HTTP MCP server with the separate cloud API binary.
- When changing branding, prioritize user-facing surfaces first: docs, workflows, image names, package metadata, CLI/help text, and add-on metadata.
- When changing shell scripts on Windows checkouts, preserve LF line endings. `.gitattributes` exists for this; container builds also normalize shell scripts defensively.
- If a task touches Postgres-backed behavior, validate `ctx_brain` over HTTP before claiming the server path is healthy.