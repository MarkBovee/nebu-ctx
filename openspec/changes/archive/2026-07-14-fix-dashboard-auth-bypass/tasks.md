## 1. Middleware fix

- [ ] 1.1 Restrict `BearerAuthMiddleware.IsAuthExempt()` so `/api/*` is exempt only for `GET`/`HEAD` when `DashboardDisableAuth` is enabled; keep static dashboard assets and `/health` exempt unconditionally as today
- [ ] 1.2 Build: `dotnet build server/NebuCtx.slnx -p:AllowMissingPrunePackageData=true` → 0 errors, 0 warnings

## 2. Startup validation

- [ ] 2.1 Add a `StartupValidator` rule requiring `AuthToken` when `DashboardHost` is non-loopback, mirroring the existing `McpHost` rule, placed immediately after it
- [ ] 2.2 Build: `dotnet build server/NebuCtx.slnx -p:AllowMissingPrunePackageData=true` → 0 errors, 0 warnings

## 3. Documentation

- [ ] 3.1 Expand the `DashboardDisableAuth` XML doc comment on `ServerOptions.cs` to describe the residual GET-based read-only exposure tradeoff
- [ ] 3.2 Add the same warning next to existing `DASHBOARD_DISABLE_AUTH`/`NEBULA_CTX_DASHBOARD_DISABLE_AUTH` mentions in `README.md` and/or `homeassistant/README.md` (search first with `grep -rln "DASHBOARD_DISABLE_AUTH" README.md homeassistant/README.md`; do not create a new doc file)

## 4. Tests

- [ ] 4.1 Add `InvokeAsync_DeleteOnDashboardPortWithDisabledAuth_StillRequiresToken` to `BearerAuthMiddlewareTests.cs` — asserts `DELETE /api/projects/...` on the dashboard port with `DashboardDisableAuth=true` returns 401 without a token
- [ ] 4.2 Add `InvokeAsync_GetOnDashboardPortWithDisabledAuth_StillSkipsBearerValidation` — asserts `GET /api/projects` on the dashboard port still skips auth
- [ ] 4.3 Re-verify the existing `InvokeAsync_DashboardPortWithDisabledAuth_SkipsBearerValidation` test still passes unmodified
- [ ] 4.4 Add `Validate_DashboardNonLoopbackWithoutToken_ReturnsError` to `StartupValidatorTests.cs`
- [ ] 4.5 Add `Validate_DashboardNonLoopbackWithToken_IsValid` to `StartupValidatorTests.cs`
- [ ] 4.6 Test: `dotnet test server/NebuCtx.slnx -p:AllowMissingPrunePackageData=true --filter "FullyQualifiedName~BearerAuthMiddlewareTests|FullyQualifiedName~StartupValidatorTests"` → all pass

## 5. Full verification

- [ ] 5.1 `dotnet test server/NebuCtx.slnx -p:AllowMissingPrunePackageData=true` (full suite) → all pass, 0 failed
- [ ] 5.2 `git status` shows only the in-scope files changed (see proposal's Impact section)
