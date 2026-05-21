# Memory System

`nebu-ctx` gebruikt 3 memory-lagen met elk eigen doel:

- `Session memory`: lokale, werkende state van huidige run. Snel, tijdelijk, project-scoped.
- `Knowledge memory`: canonieke project-feiten. Duurzamer, opgeschoond, terugzoekbaar.
- `Brain memory`: ruwe episodische breadcrumbs. Handig voor prompts, sessiesamenvattingen, recente intent.

In productie leeft hosted memory in PostgreSQL. Client houdt daarnaast lokale project-state en offline fallback bij onder `~/.nebu-ctx/` of `NEBU_CTX_DATA_DIR`.

## Layers

| Layer | Primary store | Scope | Used for |
|:---|:---|:---|:---|
| Session | local `sessions/` | current project + run | task, findings, decisions, touched files, intent, compact/resume |
| Knowledge | local `knowledge/<project-hash>/knowledge.json` and hosted Postgres | project | durable facts, recall, wakeup briefing, lifecycle/triage |
| Brain | hosted Postgres | project | raw user prompts, assistant outputs, session summaries |

## Public vs Internal Surface

Publieke MCP clients praten voor memory via `ctx(domain="memory", action=...)`.

Client routeert dat intern zo:

- `task`, `finding`, `decision`, `save`, `load`, `status`, `reset`, `list`, `cleanup` -> `ctx_session`
- `store`, `set`, `remember`, `recall`, `search`, `categories`, `timeline`, `consolidate`, `promote`, `upkeep`, `triage`, `wakeup`, `remove` -> `ctx_knowledge`

`ctx_brain` is intern/server-only. Hooks gebruiken die direct voor episodische brain entries. Externe clients horen via publieke surface niet rechtstreeks `ctx_brain` aan te roepen.

## What Gets Stored

### Session memory

Lokale `SessionState` bewaart onder meer:

- current task
- findings
- decisions
- files touched
- next steps
- inferred and explicit intents
- tool receipts and evidence hashes
- tool-call counters and token stats

Deze state wordt gebatcht opgeslagen tijdens normaal toolgebruik en expliciet via `ctx(memory, action="save")` of hooks zoals `Stop`.

### Knowledge memory

Knowledge bewaart canonieke facts met lifecycle-data:

- `category`
- `key`
- `value`
- `confidence`
- confirmation and retrieval counters
- validity/history fields
- lifecycle status and score
- source metadata (`source_type`, `source_scope`, `promotion_identity`)

Knowledge is memory die bedoeld is om later terug te halen via recall/search/wakeup.

### Brain memory

Brain bewaart losse key/value entries zonder knowledge-lifecycle:

- raw user prompts
- assistant outputs
- per-session summary lines

Brain is meer episodisch logboek dan canonieke kennislaag.

## Trigger And Hook Flow

### 1. `SessionStart`

Command: `nebu-ctx hook session-start`

Bronnen:

- Claude Code `SessionStart`
- startup/resume/compact events

Doet:

- injecteert routing guidance zodat agent `ctx_*` tools prefereert
- bouwt `<session_state>` XML uit laatste lokale session-state
- voegt knowledge toe aan snapshot
- probeert hosted wakeup briefing te lezen via `ctx_knowledge(action="wakeup")`
- valt terug op lokale high-confidence facts als hosted wakeup niet beschikbaar is

Uitlezen:

- local session: laatste session voor project root
- hosted knowledge wakeup: top current facts uit canonical knowledge

Resultaat:

- agent krijgt na startup/resume/compact weer task, decisions, modified files, next steps en relevante knowledge mee

### 2. `UserPromptSubmit`

Command: `nebu-ctx hook user-prompt-submit`

Bron:

- Claude Code `UserPromptSubmit`

Doet:

- pakt ruwe user prompt
- filtert hook/system-injecties eruit
- slaat prompt op in hosted brain via `ctx_brain(action="store")`

Opslag:

- key: `user-prompt-<timestamp>`
- value prefix: `user_prompt: ...`
- value wordt afgekapt op 800 chars

Doel:

- recente user intent beschikbaar maken voor brain recall en future compact/resume flows

### 3. `AssistantOutputSubmit`

Command: `nebu-ctx hook assistant-output-submit`

Bron:

- editor/plugin message-part events, o.a. OpenCode plugin

Doet:

- pakt assistant tekst
- filtert system/hook output eruit
- slaat tekst op in hosted brain via `ctx_brain(action="store")`

Opslag:

- key: `assistant-output-<timestamp>`
- value prefix: `assistant_output: ...`
- value wordt afgekapt op 800 chars

### 4. `PreCompact`

Command: `nebu-ctx hook pre-compact`

Bron:

- Claude Code `PreCompact`

Doet:

- flushes pending telemetry/server outbox
- bouwt compacte `<session_state>` XML uit lokale session + knowledge
- post huidige session summary naar brain
- post promoted knowledge facts naar hosted knowledge
- geeft XML terug als `additionalContext`

Uitlezen:

- local session state
- hosted wakeup briefing als beschikbaar, anders lokale knowledge facts

Opslag:

- brain: session summary via `ctx_brain(action="store")`
- knowledge: promoted local facts via `ctx_knowledge(action="promote")`

### 5. `Stop`

Command: `nebu-ctx hook stop`

Bronnen:

- Claude Code `Stop`
- Copilot CLI `postSession`

Doet:

- flushes pending telemetry/server outbox
- draait lokale consolidation van laatste session
- promoted lokale facts gaan naar hosted knowledge
- session summary gaat altijd naar hosted brain

Opslag:

- knowledge: alleen current high-confidence facts uit lokale `knowledge.json`
- brain: altijd 1 session-summary entry per saved session

### 6. `PostToolUse`

Command: `nebu-ctx hook post-tool-use`

Bronnen:

- Claude Code `PostToolUse`
- Copilot CLI `postToolUse`

Doet:

- stuurt telemetry naar server
- niet direct knowledge/brain opslag

Wel relevant:

- normale MCP tool-calls updaten tijdens runtime wél lokale session-state
- tool receipts, intent inference en autosave lopen in `client/src/mcp_server/mod.rs`

## Non-hook Memory Writes During Tool Calls

Niet alles loopt via hooks. Gewoon MCP-gebruik schrijft ook memory:

### `ctx(memory, action="task"|"finding"|"decision")`

Schrijft direct naar lokale `SessionState`.

### Elke tool-call via client MCP router

Tijdens dispatch wordt session-state bijgewerkt met:

- tool receipt hashes
- inferred/explicit intents
- autosave na genoeg unsaved changes

### Auto-consolidation

Als autonomy aan staat:

- na configured aantal tool calls draait lokale consolidation
- daarna post client promoted facts naar hosted `ctx_knowledge`

## Read Paths

### Session read

`ctx(memory, action="load")` of compact/resume flows lezen:

- laatste session voor project root
- of specifieke session-id

Gebruik:

- restore current task
- restore decisions/findings/next steps
- build compaction snapshots

### Knowledge read

`ctx(memory, action="recall"|"search")` leest canonical knowledge.

Gebruik:

- direct memory recall vanuit agents
- wakeup briefing bij session start / pre-compact
- dashboard project memory view

Extra hosted reads:

- `categories`
- `timeline`
- `status`
- `wakeup`
- `triage`

### Brain read

`ctx_brain(action="recall")` leest episodische brain entries. Dit is vooral bedoeld voor internal/dashboard/service usage, niet publieke MCP-contract calls.

Gebruik:

- recente prompts / outputs / session summaries terugvinden
- dashboard brain memory lijst

## Hosted Sync Rules

Client probeert server-calls direct te doen. Als server niet bereikbaar is:

- server tool-call gaat naar lokale outbox onder `sync/outbox/`
- outbox wordt later gereplayed

Belangrijk gevolg:

- `UserPromptSubmit`, `AssistantOutputSubmit`, `PreCompact`, `Stop` verliezen memory niet direct bij tijdelijke offline host
- hosted brain/knowledge kan kort achterlopen op lokale state tot replay gebeurt

## Consolidation Pipeline

Flow van werkgeheugen naar duurzame knowledge:

1. Session verzamelt task/findings/decisions/files/intents.
2. Local consolidation haalt bruikbare facts uit laatste session.
3. Facts landen lokaal in `knowledge.json`.
4. Alleen current high-confidence facts worden gepost naar hosted `ctx_knowledge(action="promote")`.
5. Hosted knowledge lifecycle doet ranking, history, stale marking, wakeup, triage.

Praktisch:

- session = working memory
- local knowledge = client-side staging + fallback
- hosted knowledge = canonical project memory
- brain = raw episodic breadcrumbs

## Dashboard Today

Huidige relevante dashboard endpoints:

- `GET /api/dashboard/projects/{projectId}/memory`
- `POST /api/dashboard/projects/{projectId}/memory/triage?mode=preview|apply`
- `DELETE /api/dashboard/projects/{projectId}/memory/brain/{key}`
- `DELETE /api/dashboard/projects/{projectId}/memory/brain`
- `DELETE /api/dashboard/projects/{projectId}/memory/knowledge/{category}/{key}`
- `DELETE /api/dashboard/projects/{projectId}/memory/knowledge`
- `GET /api/brain`

Belangrijk:

- brain-entry delete bestaat al in backend
- brain clear-per-project bestaat al in backend
- knowledge delete en knowledge clear-per-project bestaan al in backend
- project delete bestaat in store-laag, maar niet als dashboard endpoint/UI-flow

## Proposal: Brain Memory Dashboard

Doel: brain/memory scherm bruikbaar maken voor opschonen van test-projecten, lege projecten en rommelige episodische entries.

### P1. Maak project cleanup expliciet

Voeg project-level acties toe in dashboard:

- `Delete project`
- `Clear brain`
- `Clear knowledge`
- `Clear project metadata`

Waarom:

- test-projecten en lege projecten blijven nu hangen
- memory cleanup zit verspreid in losse endpoints, niet in 1 operator-flow

Backend nodig:

- nieuwe dashboard endpoint voor `DELETE /api/dashboard/projects/{projectId}`
- die moet project record verwijderen plus bijbehorende brain, knowledge, sessions, code index en checkout bindings opruimen of cascade-en

### P2. Geef eerst project health, dan entries

Laat bovenaan per project zien:

- brain entry count
- knowledge fact count
- current vs non-current facts
- source file count / last indexed
- created at / last updated
- markers voor `empty`, `test/demo`, `duplicate slug`, `duplicate fingerprint`

Waarom:

- operator moet eerst weten of project rommel is, pas daarna individuele entries bekijken

### P3. Voeg filters toe voor brain triage

Brain view filters:

- only empty/small projects
- only recent prompts
- only assistant outputs
- only session summaries
- date range
- project selector

Datamodel hint:

- huidige brain keys en value prefixes bevatten al genoeg signaal (`user-prompt-*`, `assistant-output-*`, `session-*`)
- UI kan zonder schema-migratie al type-badges afleiden uit key/value prefix

### P4. Voeg bulk-acties toe

Bulk-acties in brain screen:

- delete selected entries
- clear all prompts for project
- clear all assistant outputs for project
- clear all session summaries for project

Hiervoor zou backend idealiter krijgen:

- delete-by-prefix of delete-by-predicate endpoints

### P5. Maak lege/test-projecten snel verwijderbaar

Voeg smart CTA toe als project vermoedelijk weg kan:

- brain = 0 en knowledge = 0
- metadata leeg of bijna leeg
- slug matcht `test`, `tmp`, `demo`, `scratch`

Actie:

- `Delete empty project`
- `Archive/Hide project` als softer alternatief gewenst is

### P6. Koppel knowledge triage en brain cleanup

Per project memory page:

- eerst knowledge triage preview
- daarna brain cleanup suggesties

Voorbeelden:

- `3 duplicate facts found`
- `14 brain entries older than 30 days`
- `project has no knowledge and only 2 prompt breadcrumbs`

### Recommended order

1. Project-level delete + cleanup UI
2. Project health summary and filters
3. Brain type badges and bulk actions
4. Smart empty/test-project suggestions

## Code References

- client hook wiring: `client/src/hooks/agents.rs`
- hook handlers: `client/src/hook_handlers.rs`
- public memory routing: `client/src/mcp_server/mod.rs`
- local session actions: `client/src/tools/ctx_session.rs`
- local session store: `client/src/core/session.rs`
- local knowledge store: `client/src/core/knowledge.rs`
- hosted brain calls from client: `client/src/server_client.rs`
- hosted knowledge handler: `server/src/NebuCtx.Tools/Knowledge/KnowledgeToolHandler.cs`
- hosted knowledge logic: `server/src/NebuCtx.Server.Core/Services/KnowledgeService.cs`
- hosted brain handler/service: `server/src/NebuCtx.Tools/Brain/BrainToolHandler.cs`, `server/src/NebuCtx.Server.Core/Services/BrainService.cs`
- dashboard memory endpoints: `server/src/NebuCtx.Server.Host/Dashboard/DashboardDataEndpoints.cs`
- dashboard brain/knowledge payloads: `server/src/NebuCtx.Server.Host/Dashboard/DashboardPayloadFactory.cs`
