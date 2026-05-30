## Why

`nebu-ctx` kan nu memory vastleggen en promoten, maar er ontbreekt nog een duidelijke lifecycle zodra projecten veel facts, sessions en promoted memories opbouwen. Zonder scoring, onderhoud en bounded wake-up gedrag groeit de memorylaag door tot een rommelige mix van oude, dubbele en slecht prioriteerbare facts.

## What Changes

- voeg server-side memory lifecycle beheer toe voor scoring, promotie, consolidatie en onderhoud van project knowledge
- introduceer bounded layered wake-up behavior zodat startup context klein blijft en diepere memory alleen on-demand wordt opgehaald
- voeg temporal/maintenance primitives toe voor memory updates, including invalidation, staleness handling, and retention-ready summaries
- maak knowledge ingest en replay idempotent zodat repeated hooks, sync flushes, of retries geen memory-spam veroorzaken
- voeg expliciete hosted memory triage toe zodat agents of operators project memory kunnen previewen en opschonen via merge, dedup, rescoring, stale marking, en vermoedelijke test/demo/noise detectie
- breid dashboard/admin zichtbaarheid uit met memory health, density, maintenance state, wake-up composition, and lifecycle change indicators for larger projects
- laat OpenCode startup, compacting, idle, and continuation hooks actief de nieuwe hosted memory lifecycle en bounded wake-up outputs gebruiken

## Capabilities

### New Capabilities
- `memory-lifecycle`: scoring, maintenance, triage, retention, layered wake-up, and health management for large project memory sets

### Modified Capabilities
- `memory`: extend project memory behavior with canonical server-side promotion, temporal maintenance, bounded wake-up selection, explicit upkeep flows, and hosted triage requests
- `dashboard`: extend project memory inspection with memory health, lifecycle visibility, and triage visibility for operators
- `offline-sync`: extend queued server memory replay so retries remain idempotent for promoted knowledge batches

## Impact

- server knowledge handling in `server/src/NebuCtx.Tools/Knowledge/` and `server/src/NebuCtx.Server.Core/Services/`
- client routing and sync behavior in `client/src/mcp_server/` and `client/src/server_client.rs`
- OpenCode plugin behavior in `client/src/templates/opencode-plugin.ts`
- dashboard memory visibility and lifecycle instrumentation in `server/src/NebuCtx.Server.Host/Dashboard/`
- hosted memory triage orchestration and preview/apply handling in `server/src/NebuCtx.Tools/Knowledge/` and `server/src/NebuCtx.Server.Core/Services/`
- storage contracts and Postgres persistence for additional memory lifecycle metadata
- future reuse of ideas from `/home/mark/Work/Projects/Personal/mempalace/`, especially layered wake-up, temporal facts, and idempotent ingest/maintenance patterns
