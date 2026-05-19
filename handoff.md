# Handoff

## Status

- Basis voor dit werk is nog steeds `f1ce06a feat: simplify dashboard overview` op `main`.
- Sinds die commit is lokaal verder gewerkt aan dashboard cleanup.
- Huidige lokale wijzigingen:
  - `server/src/NebuCtx.Server.Host/Dashboard/dashboard.html`
  - `server/tests/NebuCtx.IntegrationTests/McpEndpointTests.cs`
- Dashboard overview gebruikt nu direct `auth_token` uit `/api/dashboard/overview` zodra de overview is opgebouwd.
- De overview doet daardoor niet meer standaard een extra `/api/auth-token` roundtrip.
- De update-banner reset nu ook correct naar verborgen wanneer `update_available` false is.
- Extra dode overview-restanten verwijderd:
  - `renderBuddy()` en buddy-overviewcode waren al eerder weggehaald
  - daarna ook `overviewCR`, `fd(...)` en een ongebruikte `sp` in `updateOverview`
- Een dashboard-test is robuuster gemaakt tegen gedeelde fixture-state:
  - `DashboardOverview_ReturnsProjectDailySavingsAndActiveSessions()` filtert nu op het aangemaakte `projectId`
  - hierdoor faalt die test niet meer zodra andere tests ook telemetry/project-daily data hebben opgebouwd

## OpenSpec Change

- Actieve change: `openspec/changes/productionize-nebu-ctx-platform`
- Wat nu feitelijk klaar lijkt:
  - dashboard foundation: alles afgevinkt
  - offline sync: alles afgevinkt
  - production hardening: alles afgevinkt
  - editor memory activation: alles afgevinkt behalve 1 open taak
- Enige nog open task in `tasks.md`:
  - `Add deeper OpenCode lifecycle parity once more editor hook surface is available`

## Wat Nog Openstaat

1. Dashboard verder opschonen buiten overview.
2. Beslissen hoe de OpenSpec change wordt afgemaakt:
   - ofwel die laatste OpenCode parity-taak echt implementeren
   - ofwel expliciet descopen / herformuleren als de benodigde OpenCode hook surface nog niet beschikbaar is
3. Daarna de OpenSpec change afronden en archiveren.

## Aanbevolen Vervolgstappen

1. Check eerst `git status` en laat lokale/untracked configdirs ongemoeid:
   - `.claude/commands/`
   - `.claude/skills/`
   - `.github/prompts/`
   - `.github/skills/openspec-*`
   - `.opencode/`
   - `handoff.md`
2. Als je met dashboard verder wilt:
   - kijk naar de volgende cleanup buiten overview
   - meest logische kandidaten zijn navigatie/dataflow in `dashboard.html` (`loadDomainNav`, memory/brain views, losse legacy fetch-paden)
3. Als je de OpenSpec change wilt afmaken:
   - lees `openspec/changes/productionize-nebu-ctx-platform/tasks.md`
   - bepaal of de laatste open OpenCode parity-taak nu technisch uitvoerbaar is
   - als ja: implementeer die taak en update tests/docs waar nodig
   - als nee: werk `tasks.md` en eventueel proposal/design/specs bij zodat de change de echte afgeronde scope weerspiegelt
4. Na die beslissing: draai bredere validatie
   - `dotnet test server/NebuCtx.slnx -p:AllowMissingPrunePackageData=true`
   - relevante Rust-validatie voor memory/hook/sync als die code is geraakt
5. Als tasks/specs en code weer op elkaar aansluiten:
   - rond de OpenSpec change af
   - archiveer `productionize-nebu-ctx-platform`

## Verified

- Groen:
  - `dotnet test server/NebuCtx.slnx -p:AllowMissingPrunePackageData=true --filter "FullyQualifiedName~DashboardOverview|FullyQualifiedName~DashboardDomains"`
- Eerder al groen in vorige sessie:
  - `dotnet test server/NebuCtx.slnx -p:AllowMissingPrunePackageData=true --filter "FullyQualifiedName~DashboardOverview|FullyQualifiedName~DashboardDomains|FullyQualifiedName~TelemetryStore"`

## Files Touched In Current Local Follow-Up

- `server/src/NebuCtx.Server.Host/Dashboard/dashboard.html`
- `server/tests/NebuCtx.IntegrationTests/McpEndpointTests.cs`

## Notes

- `handoff.md` is bedoeld als lokale overdracht en hoeft normaal niet mee gecommit te worden.
- De actieve OpenSpec change lijkt inhoudelijk bijna klaar; de afronding blokkeert nu vooral op die laatste OpenCode parity-beslissing.
