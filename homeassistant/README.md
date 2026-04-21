# Home Assistant Add-on: nebu-ctx

This add-on packages `nebu-ctx` for Home Assistant.

This add-on packages the current `nebu-ctx` MCP server, dashboard, and persistence wrappers for Home Assistant.

## What the add-on provides

- Home Assistant ingress dashboard through `Open Web UI`
- internal dashboard service on port `3333`
- optional MCP-over-HTTP endpoint on port `4242`
- SQLite storage under `/data` by default
- PostgreSQL-backed operation when `store=postgres`
- configurable `project_root` for mounted paths such as `/share` or `/config`

## Install in Home Assistant

1. Add `https://github.com/MarkBovee/nebu-ctx` as a custom add-on repository in Home Assistant.
2. Install the `nebu-ctx` add-on.
3. Configure the add-on options.
4. Start the add-on.
5. Use `Open Web UI` to open the dashboard through ingress.

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
| `store` | Selects the persistence backend | `sqlite` or `postgres` |
| `database_url` | PostgreSQL connection string | optional override when `store=postgres` |
| `auth_token` | Bearer token for MCP access | leave empty to auto-generate and persist one in `/data/auth_token` |
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

1. Set `auth_token` in the add-on options, or copy the generated token from the startup log or `/data/auth_token`.
2. Expose `4242/tcp` in the Home Assistant network settings.
3. Connect to the Home Assistant host on port `4242` with `Authorization: Bearer <token>`.

Example endpoints:

- `http://homeassistant.local:4242/health`
- `http://homeassistant.local:4242/v1/tools`
- `http://homeassistant.local:4242/v1/tools/call`

## Local image validation with podman

If you want to validate the add-on container yourself outside Home Assistant, use podman.

### Local-source validation build

This build uses your current checkout and is the recommended packaging pre-check:

```bash
podman build -t nebu-ctx-addon-local -f homeassistant/Dockerfile.dev .
```

This is the build path used by `tests/pre_release_check.sh`.

For a full local smoke test, run `tests/local-addon-test.sh`. It builds the local binary, starts the add-on in SQLite mode, reads the generated token from `/data/auth_token`, and verifies both the dashboard and the MCP HTTP endpoint.

### Published add-on Dockerfile build

The shipped add-on Dockerfile can also be built directly:

```bash
podman build -t nebu-ctx-addon -f homeassistant/Dockerfile homeassistant
```

That build uses the repository URL and ref from `homeassistant/build.yaml`, so it validates the published add-on packaging path rather than your local Rust source tree.

## Operational notes

- SQLite data lives under `/data`.
- `project_root` only works for mounted paths that the add-on can actually access.
- `store=postgres` requires a valid `database_url`.
- without `auth_token`, the MCP endpoint is intentionally not reachable outside the add-on container.
- the dashboard and MCP services are supervised separately inside the add-on container.