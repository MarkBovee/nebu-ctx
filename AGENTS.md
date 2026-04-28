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
- `scripts/server/`: refresh/build/publish scripts for the .NET host
- `tests/`: cross-stack smoke, add-on, and release validation
- `homeassistant/`: Home Assistant add-on packaging and runtime wrapper
- `homeassistant/Dockerfile` and `docker-entrypoint.sh`: standalone and add-on container packaging for the .NET host
- `Dockerfile`: multi-stage build (SDK → Alpine runtime); produces the GHCR image and is used for local dev builds

## Layout Rules

- Treat `client/target/` as normal Cargo output.
- `server/dist/` is gitignored — binaries are no longer committed; the server is built in the multi-stage Dockerfile and published to GHCR.
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

The add-on container behavior is driven by `docker-entrypoint.sh`. Keep these files aligned whenever you change add-on behavior:

- `docker-entrypoint.sh`
- `homeassistant/config.yaml`
- `homeassistant/README.md`
- `tests/local-addon-test.sh`

The `homeassistant/Dockerfile` is a thin wrapper (`FROM ghcr.io/markbovee/nebu-ctx:{version}`) used for local testing only. Production HA deployments pull the pre-built GHCR image directly via the `image:` field in `config.yaml`.

Current add-on behavior:

- PostgreSQL only
- builds `DATABASE_URL` from `postgres_*` fields
- generates and persists the MCP token in `/data/auth_token`
- logs the active token at startup for local and HA setup flows

## Release Flow

- All three version locations **must be kept in sync** on every version bump:
  1. `client/Cargo.toml` — Rust client version (e.g. `version = "0.5.1"`)
  2. `homeassistant/config.yaml` — HA addon version (e.g. `version: "0.5.1"`)
  3. `server/src/NebuCtx.Application/ToolRegistry.cs` — `ServerVersion.Current` constant (e.g. `"0.5.1"`)
  Commit all three together. No dist rebuild required — binaries are built in CI.
- `auto-release.yml` tags main when the version changes and no matching tag exists.
- `release.yml` builds tagged binaries, publishes release assets, publishes the crate to crates.io, and builds + pushes the server Docker image to `ghcr.io/markbovee/nebu-ctx`.
- When a version-bumped commit lands on `main`, `auto-release.yml` triggers, verifies all three version locations are in sync (Cargo.toml, config.yaml, AND ToolRegistry.cs), then creates and pushes the tag. The tag push then triggers `release.yml`.
- `release.yml` builds amd64+arm64 client binaries, creates the GitHub release, publishes the crate to crates.io via the `publish-crate` job, and builds + pushes a multi-platform server image to GHCR via the `publish-server-image` job.
- **Required secret:** `CARGO_REGISTRY_TOKEN` must be set in GitHub repo Settings → Secrets → Actions. Generate a token at https://crates.io/settings/tokens with "Publish new crates" and "Publish updates" scopes.
- The GHCR server image is built from the multi-stage `Dockerfile` at the repo root. The `homeassistant/Dockerfile` pulls from GHCR — no SDK or source needed at add-on install time.

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

For add-on validation (builds multi-stage image from source, then smoke-tests it):

```bash
ADDON_DOCKERFILE=Dockerfile bash tests/local-addon-test.sh
```

To build the standalone container for local dev (multi-stage, compiles server from source):

```bash
podman build -t nebu-ctx-server -f Dockerfile .
```

To run the local dev container pointing at the shared PostgreSQL database (same data as HA server):

```bash
podman run -d --name nebu-ctx-eval \
  -p 127.0.0.1:3333:3333 -p 127.0.0.1:4242:4242 \
  --env-file .env \
  nebu-ctx-server
```

The `.env` file must include `NEBULA_CTX_HTTP_TOKEN` (not `AUTH_TOKEN`) and `NEBULA_CTX_HOST=0.0.0.0` for the container to bind on all interfaces and accept the token. Use the same `DATABASE_URL` as the HA server so both instances share telemetry and data.

To test the HA addon flow locally (pulls from GHCR — requires the image to be published):

```bash
NEBU_CTX_VERSION=0.5.4 bash tests/local-addon-test.sh
```

## Practical Guidance

- Prefer fixing runtime wrappers and release wiring at the root instead of adding more fallback docs.
- Prefer the cleaned top-level `client/` and `server/` structure in all docs and scripts.
- When changing shell scripts on Windows checkouts, preserve LF line endings. `.gitattributes` exists for this; container builds also normalize shell scripts defensively.
- If a task touches Postgres-backed behavior, validate `ctx_brain` over HTTP before claiming the server path is healthy.