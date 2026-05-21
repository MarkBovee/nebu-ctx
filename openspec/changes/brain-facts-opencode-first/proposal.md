## Why

`nebu-ctx` gebruikt `brain` nu als server-side logboek voor raw prompts, assistant output en sessiesummaries, terwijl startup en wake-up gedrag vooral op knowledge leunen. Dat botst met de bedoelde rol van brain als echte project-memory en voelt extra verkeerd nu OpenCode onze primaire IDE en lifecycle-driver is.

## What Changes

- herdefinieer `brain` als server-owned, fact-only canonical memorylaag voor projectfeiten, beslissingen, constraints, voorkeuren, hypotheses en correcties
- verplaats raw transcript- en eventcapture naar een client-local journal in plaats van server-side brain entries
- voeg een gedeelde editor-onafhankelijke memory lifecycle core toe zodat OpenCode, Claude en Copilot dezelfde memory-events en fact-ingest gebruiken
- maak OpenCode de primaire lifecycle-integratie voor startup, continuation, compacting, idle flush en stop-time memory refresh
- vervang directe brain log-writes vanuit hooks en plugin-events door fact-candidate extractie, canonicalization en projection naar publieke recall/wakeup paden
- laat publieke memory acties en dashboard memory-inspectie op een brain-backed projection steunen zonder de 5-tool MCP surface te breken
- voeg temporal fact state, supersession en invalidation toe zodat nieuwere feiten oudere feiten kunnen corrigeren zonder blind overwrite gedrag

## Capabilities

### New Capabilities
- `brain-facts`: canonical typed fact memory for projects, including provenance, temporal state, supersession, invalidation, and fact-only ingest from client lifecycle events
- `local-journal`: client-local raw lifecycle journal for prompts, assistant turns, tool outcomes, and compaction markers that never promotes raw transcript directly to hosted brain

### Modified Capabilities
- `memory`: replace transcript-style brain behavior with fact-only brain ingest, OpenCode-first lifecycle activation, brain-backed wakeup selection, and canonical/public projection behavior
- `dashboard`: change project memory inspection so operators see semantic brain facts, lifecycle state, and wakeup composition instead of raw brain log entries
- `offline-sync`: keep queued hosted brain fact ingest and projection refresh idempotent when OpenCode or hook-driven lifecycle events replay after offline periods

## Impact

- client lifecycle and hook orchestration in `client/src/hook_handlers.rs`, `client/src/templates/opencode-plugin.ts`, and related client memory modules
- new client-local journal storage, retention, and fact extraction paths under `client/src/core/`
- server brain contracts, storage, and services in `server/src/NebuCtx.Storage/`, `server/src/NebuCtx.Server.Core/Services/`, and `server/src/NebuCtx.Tools/Brain/`
- public memory routing in `server/src/NebuCtx.Tools/Ctx/CtxToolHandler.cs` and any brain-backed projection work in knowledge services
- dashboard memory endpoints and payloads in `server/src/NebuCtx.Server.Host/Dashboard/`
- OpenSpec docs and tests covering OpenCode-first startup, continuation, idle persistence, fact supersession, and offline replay
