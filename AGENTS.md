# AGENTS

## Purpose

`nebu-ctx` is now organized around a thin Rust client plus a .NET MCP host and dashboard stack.

- Rust thin client installed from `client/`
- .NET MCP HTTP host under `server/src/NebuCtx.Server.Host/`
- dashboard HTTP UI served by that same .NET host on its dashboard port

There is older Rust runtime code in the repository, but the cleaned product layout and packaging contract now center on the top-level `client/` and `server/` trees.

## Main Surfaces

- `client/src/main.rs`: thin-client CLI entrypoint
- `client/tests/`: client-owned tests
- `server/src/`: .NET host, dashboard, contracts, storage, project registry, and tool handlers
- `server/tests/`: .NET contract, integration, and project identity tests
- `server/dist/linux/`: committed publish payload used by Docker and the add-on
- `scripts/server/`: refresh/build/publish scripts for the .NET host
- `tests/`: cross-stack smoke, add-on, and release validation
- `homeassistant/`: Home Assistant add-on packaging and runtime wrapper
- `homeassistant/Dockerfile` and `docker-entrypoint.sh`: standalone and add-on container packaging for the .NET host

## Layout Rules

- Treat `client/target/` as normal Cargo output.
- Treat `server/dist/linux/` as the canonical publish payload.
- Keep cross-stack and repo-level tests in top-level `tests/` only.
- Prefer updating docs and scripts to the cleaned top-level layout instead of preserving old path aliases.

## Storage Model

- The main supported server path is PostgreSQL via `NEBULA_STORE=postgres` and `DATABASE_URL`.
- The most validated Postgres-backed MCP path today is `ctx_brain`.
- For live review sessions, run the .NET host locally against the same PostgreSQL database you want to inspect in the dashboard.

## Product Naming

- The current product/package/binary name is `nebu-ctx`.
- Compatibility aliases remain in places that would otherwise break users or tests abruptly.
- Internal names like `LeanCtxServer` and older env vars still exist in the codebase. Treat them as compatibility debt, not the preferred product surface.

## Home Assistant Add-on

The add-on runs two processes in one container:

- dashboard on port `3333`
- MCP HTTP server on port `4242`

The add-on container behavior is driven by `docker-entrypoint.sh` and `homeassistant/Dockerfile`. Keep these files aligned whenever you change add-on behavior:

- `docker-entrypoint.sh`
- `homeassistant/Dockerfile`
- `homeassistant/config.yaml`
- `homeassistant/README.md`
- `tests/local-addon-test.sh`

Current add-on behavior:

- PostgreSQL only
- builds `DATABASE_URL` from `postgres_*` fields
- generates and persists the MCP token in `/data/auth_token`
- logs the active token at startup for local and HA setup flows

## Release Flow

- `homeassistant/config.yaml` version and `client/Cargo.toml` version should stay in sync.
- `auto-release.yml` tags main when the version changes and no matching tag exists.
- `release.yml` builds tagged binaries and publishes release assets.
- The Home Assistant container builds from committed `server/dist/linux`, not release-asset downloads.

If you change package, binary, or image names, update all of these together:

- `Cargo.toml`
- `client/Cargo.toml`
- `.github/workflows/release.yml`
- `.github/workflows/auto-release.yml`
- `homeassistant/Dockerfile`
- local smoke scripts under `tests/`

## Build And Validation

Use these commands as the default validation baseline:

```bash
cargo test --manifest-path client/Cargo.toml
dotnet test server/NebuCtx.slnx
bash tests/local-server-cli-test.sh
```

For add-on validation:

```bash
bash scripts/server/refresh-dist.sh
podman build -t nebu-ctx-addon-dev -f homeassistant/Dockerfile .
bash tests/local-addon-test.sh
```

For the standalone container:

```bash
bash scripts/server/refresh-dist.sh
podman build -t nebu-ctx-server -f homeassistant/Dockerfile .
```

## Practical Guidance

- Prefer fixing runtime wrappers and release wiring at the root instead of adding more fallback docs.
- Prefer the cleaned top-level `client/` and `server/` structure in all docs and scripts.
- When changing shell scripts on Windows checkouts, preserve LF line endings. `.gitattributes` exists for this; container builds also normalize shell scripts defensively.
- If a task touches Postgres-backed behavior, validate `ctx_brain` over HTTP before claiming the server path is healthy.