# Home Assistant Add-on: nebu-ctx

This add-on packages `nebu-ctx` for Home Assistant.

This add-on packages the current `nebu-ctx` MCP server, dashboard, and persistence wrappers for Home Assistant.

## What the add-on provides

- Home Assistant ingress dashboard through `Open Web UI`
- internal dashboard service on port `3333`
- optional MCP-over-HTTP endpoint on port `4242`
- PostgreSQL-backed server operation
- auto-generated MCP auth token surfaced in the dashboard
- configurable `project_root` for mounted paths such as `/share` or `/config`

## Install in Home Assistant

1. Add `https://github.com/MarkBovee/nebu-ctx` as a custom add-on repository in Home Assistant.
2. Install the `nebu-ctx` add-on.
3. Configure the add-on options.
4. Start the add-on.
5. Use `Open Web UI` to open the dashboard through ingress.

The published add-on install path is optimized for normal Home Assistant use: it downloads the tagged `nebu-ctx` source archive and publishes the .NET host inside the image build.

## Runtime model

The add-on starts two services inside the same container:

- dashboard service on `0.0.0.0:3333` for Home Assistant ingress
- MCP HTTP service on `0.0.0.0:4242` with a persisted bearer token

That split is intentional:

- the dashboard is meant to stay inside Home Assistant ingress
- the MCP endpoint is only exposed externally when you explicitly configure it

## Settings

| Option | Purpose | Notes |
|--------|---------|-------|
| `postgres_host` | PostgreSQL host name | add-on is PostgreSQL-backed only |
| `postgres_port` | PostgreSQL port | default `5432` |
| `postgres_database` | PostgreSQL database name | default `nebula_ctx` |
| `postgres_username` | PostgreSQL user | default `postgres` |
| `postgres_password` | PostgreSQL password | required for most deployments |
| `log_level` | Sets `RUST_LOG` | typical values: `debug`, `info`, `warn`, `error` |
| `project_root` | Default path root for MCP and dashboard actions | recommended: `/share` or `/config` |

## Dashboard access

Use Home Assistant `Open Web UI`.

- ingress routes to the internal dashboard service on port `3333`
- the dashboard uses ingress-safe relative API paths
- the add-on disables dashboard self-auth because Home Assistant ingress already fronts it

Do not expose the dashboard as a host port. The intended path is ingress only.

## MCP access

The MCP HTTP endpoint is separate from the dashboard.

To use it from external clients:

1. Open the dashboard and copy the generated MCP token.
2. Expose `4242/tcp` in the Home Assistant network settings.
3. Connect to the Home Assistant host on port `4242` with `Authorization: Bearer <token>`.

The add-on generates the MCP token on first start, persists it in `/data/auth_token`, and shows it in the dashboard so you do not need to manage it in the add-on settings.

Example endpoints:

- `http://homeassistant.local:4242/health`
- `http://homeassistant.local:4242/v1/tools`
- `http://homeassistant.local:4242/v1/tools/call`

## Local image validation with podman

If you want to validate the add-on container yourself outside Home Assistant, use podman.

### Local-source validation build

This build uses your current checkout and is the recommended packaging pre-check:

```bash
bash scripts/server/refresh-dist.sh
podman build -t nebu-ctx-addon-local -f homeassistant/Dockerfile .
```

This is the build path used by `tests/pre_release_check.sh` and both local smoke scripts.

For a full local smoke test, run `tests/local-addon-test.sh`. It loads PostgreSQL settings from the repo `.env`, starts the add-on, reads the generated token from `/data/auth_token`, and verifies both the dashboard and the MCP HTTP endpoint.

### Published add-on Dockerfile build

The shipped add-on Dockerfile can also be built directly:

```bash
bash scripts/server/refresh-dist.sh
podman build -t nebu-ctx-addon -f homeassistant/Dockerfile .
```

That build uses the committed server payload under `server/dist/linux`, so it validates the same single Dockerfile that the add-on uses.

The repository now uses a single dist-first Dockerfile for add-on packaging.

The smoke test refreshes `server/dist/linux` before building the image, so the normal validation path is simply:

```bash
bash tests/local-addon-test.sh
```

## Operational notes

- PostgreSQL is required for the add-on runtime.
- `project_root` only works for mounted paths that the add-on can actually access.
- the MCP token is generated automatically on first start and persisted in `/data/auth_token`.
- the dashboard and MCP services are supervised separately inside the add-on container.