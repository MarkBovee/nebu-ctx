# Home Assistant Add-on: nebula-ctx

`nebula-ctx` can run as a Home Assistant add-on with two separate surfaces:

- Home Assistant ingress dashboard on the internal dashboard port
- Optional MCP-over-HTTP endpoint on port `4242`

## Installation

1. Add this repository to Home Assistant as a custom add-on repository.
2. Install the `nebula-ctx` add-on.
3. Configure the options in the add-on settings.
4. Start the add-on.
5. Use `Open Web UI` to open the dashboard through Home Assistant ingress.

## Settings Review

- `store`
  - `sqlite` stores state under `/data`
  - `postgres` enables the Postgres-backed store path

- `database_url`
  - Required only when `store=postgres`
  - Expected format: `postgres://user:pass@host:5432/database`

- `auth_token`
  - Optional, but required if you want direct access to the MCP HTTP port from outside the add-on container
  - When omitted, the MCP server stays bound to `127.0.0.1` inside the container for safety

- `log_level`
  - Controls `RUST_LOG`
  - Typical values: `info`, `debug`, `warn`, `error`

- `project_root`
  - Default root used by the MCP server and dashboard when resolving relative project paths
  - Recommended values: `/share` for shared files or `/config` for Home Assistant configuration files

## Dashboard Connection

The dashboard is intended to be opened through Home Assistant ingress, not through a separately exposed dashboard port.

- Use Home Assistant `Open Web UI`
- The add-on routes ingress to the internal dashboard server on port `3333`
- The dashboard now uses ingress-safe relative API URLs, so requests stay under the Home Assistant ingress path

## MCP Connection

The MCP endpoint is separate from the dashboard.

To use it from external MCP clients:

1. Set `auth_token`
2. In the add-on `Network` settings, expose `4242/tcp`
3. Use the Home Assistant host URL with the MCP HTTP endpoint

Example URLs:

- `http://homeassistant.local:4242/health`
- `http://homeassistant.local:4242/v1/tools`
- `http://homeassistant.local:4242/v1/tools/call`

## Operational Notes

- The dashboard is always started so ingress remains functional.
- The dashboard is not exposed as a host port by default.
- The MCP HTTP endpoint and the dashboard are separate processes inside the add-on container.
- `project_root` only works for paths that are actually mounted into the add-on, such as `/share` and `/config`.