## Why

`BearerAuthMiddleware.IsAuthExempt()` exempts **every** path under `/api` (including 13 destructive routes such as `DELETE /api/projects/{id}`, brain/knowledge delete, and clear endpoints) from authentication whenever `DashboardDisableAuth=true` and the request lands on the dashboard's port — with no HTTP-method check and no remote-address check. `docker-entrypoint.sh` sets this flag unconditionally for every official Home Assistant add-on deployment, so this is a shipped default, not a hypothetical misconfiguration: anyone who can reach the dashboard port can delete or clear any project's brain/knowledge data with zero authentication today. Separately, `StartupValidator` validates the MCP host/token combination but has no equivalent rule for the dashboard host, so a non-loopback dashboard bind with no token configured passes startup silently.

## What Changes

- Restrict the dashboard-port auth exemption in `BearerAuthMiddleware.IsAuthExempt()` to safe HTTP methods (`GET`/`HEAD`) for `/api/*` paths. Static dashboard assets (`/`, `/index.html`, `/dashboard`, `/logo.png`, `/favicon.ico`) and `/health` remain exempt exactly as today, method-independent.
- Add a `StartupValidator` rule requiring an auth token when `DashboardHost` is bound to a non-loopback address, mirroring the existing `McpHost` rule.
- Document the residual read-only exposure tradeoff on `ServerOptions.DashboardDisableAuth` and in operator-facing README docs where `DASHBOARD_DISABLE_AUTH` is already mentioned.
- **Not BREAKING** for the documented use case: the dashboard frontend's `apiFetch()` never attaches an `Authorization` header on any request, including its own delete/clear buttons — those calls work only because of today's blanket exemption. Restricting the exemption to GET/HEAD requires zero frontend change because the frontend never legitimately needed write methods exempted; and the HA add-on's `docker-entrypoint.sh` always auto-generates a token, so the new `StartupValidator` rule never fires for the stock add-on config.

## Capabilities

### New Capabilities

### Modified Capabilities
- `dashboard`: adds a new requirement that mutating dashboard API requests always require a valid bearer token, even when `DashboardDisableAuth` is set, and that a non-loopback dashboard bind without a configured token fails startup instead of serving unauthenticated.

## Impact

- **Code**: `server/src/NebuCtx.Server.Core/Auth/BearerAuthMiddleware.cs`, `server/src/NebuCtx.Server.Core/Validation/StartupValidator.cs`, `server/src/NebuCtx.Contracts/Configuration/ServerOptions.cs` (doc comment only).
- **Tests**: `server/tests/NebuCtx.ContractTests/BearerAuthMiddlewareTests.cs`, `server/tests/NebuCtx.ContractTests/StartupValidatorTests.cs`.
- **Docs**: `README.md` / `homeassistant/README.md` wherever `DASHBOARD_DISABLE_AUTH` is documented.
- **Out of scope**: `docker-entrypoint.sh`'s default itself, `dashboard.html`, per-route changes in `DashboardEndpoints.cs`, and fully closing the residual GET-based read-only exposure (deferred — see plan's Maintenance notes, would need a way to verify a request arrived via HA's ingress proxy).
- Full technical detail (exact current code, exploit chain, and exact diffs) already captured in `plans/001-fix-dashboard-auth-bypass.md` — this proposal is the OpenSpec-tracked counterpart of that plan.
