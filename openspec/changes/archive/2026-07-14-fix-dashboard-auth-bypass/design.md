## Context

`BearerAuthMiddleware.IsAuthExempt()` is the single auth gate for the whole HTTP pipeline. Today it exempts every `/api/*` path (all methods) on the dashboard port whenever `DashboardDisableAuth=true` — a flag `docker-entrypoint.sh` sets unconditionally for every Home Assistant add-on deployment. This means the shipped default configuration allows unauthenticated destructive requests (delete/clear project memory) to anyone who can reach the dashboard port. `StartupValidator` already has a loopback+token rule for `McpHost` but no equivalent for `DashboardHost`.

## Goals / Non-Goals

**Goals:**
- Close the unauthenticated-mutation vector completely: every non-safe-method `/api/*` request always requires a valid token, regardless of `DashboardDisableAuth`.
- Add a startup guard so a non-loopback dashboard bind with no token fails fast instead of silently serving everything unauthenticated.
- Make the residual read-only exposure (GET-based, e.g. `/api/auth-token`) an explicit, documented tradeoff rather than a silent one.

**Non-Goals:**
- Fully eliminating the read-only GET-based exposure on the dashboard port when `DashboardDisableAuth=true`. Doing so would require reliably distinguishing "arrived via Home Assistant's ingress proxy" from "arrived from an arbitrary network peer" — this repo has no `ForwardedHeaders` middleware or ingress-trust mechanism today, and guessing at HA Supervisor's internal network topology risks silently breaking the primary supported deployment. Deferred to a follow-up change once ingress connection behavior is confirmed empirically.
- Any change to `docker-entrypoint.sh`'s default (`DashboardDisableAuth=1` for the add-on) — that default itself is intentional for ingress-viewing convenience.
- Any frontend (`dashboard.html`) change — not required (see Decisions below).

## Decisions

- **Restrict by HTTP method, not by IP/loopback.** Considered gating the exemption on `RemoteIpAddress` being loopback, but Home Assistant's ingress proxy connects over Docker's internal bridge network, not literal loopback — an IP-based fix would likely break the primary supported deployment. Restricting to safe methods (`GET`/`HEAD`) instead closes the actual mutation vector without depending on unconfirmed network topology assumptions.
- **No frontend change required.** `dashboard.html`'s `apiFetch()` never attaches an `Authorization` header on any call, including its own `DELETE` buttons — those already only work today because of the middleware's blanket exemption. Restricting the exemption to GET/HEAD doesn't remove any capability the frontend actually has; it removes an implicit grant the frontend was never designed to rely on for security.
- **`StartupValidator` rule scoped narrowly.** The new rule only fires when `DashboardHost` is non-loopback **and** no token exists at all — never when `DashboardDisableAuth=true` alone. Confirmed via `docker-entrypoint.sh`'s `configure_addon_mode()` that the HA add-on always auto-generates and persists a token on first run, so this rule cannot fire for the stock add-on config.

## Risks / Trade-offs

- [Risk] Residual GET-based read-only exposure remains when `DashboardDisableAuth=true` on a reachable port (e.g. `/api/auth-token` discloses the live MCP bearer token to anyone who can reach that port). → Mitigation: documented explicitly on `ServerOptions.DashboardDisableAuth` and in operator README(s); tracked as a follow-up, not silently accepted.
- [Risk] A future change to `dashboard.html` that starts attaching an `Authorization` header on some-but-not-all calls could mask a real regression if this middleware change is misread as "no auth needed for the dashboard UI." → Mitigation: STOP condition documented in the underlying plan; any executor must re-check `apiFetch()` behavior before assuming the fix is still correct.
