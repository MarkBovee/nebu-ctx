# Memory System

`nebu-ctx` gebruikt 4 memory-lagen met elk eigen doel:

- `Session memory`: lokale, werkende state van huidige run. Snel, tijdelijk, project-scoped.
- `Journal memory`: lokale ruwe lifecycle-events. Prompt/assistant/tool-output voor extractie en debug.
- `Brain memory`: hosted canonieke feiten. Typed, fact-only, project-scoped.
- `Knowledge memory`: hosted/public projection voor recall, wake-up, triage, dashboard.
- `Shared memory`: hosted user-wide projection derived from the same durable facts, keyed by the active server token so it follows the user across machines.

In productie leeft hosted memory in PostgreSQL. Client houdt daarnaast lokale project-state en offline fallback bij onder `~/.nebu-ctx/` of `NEBU_CTX_DATA_DIR`.

## Layers

| Layer | Primary store | Scope | Used for |
|:---|:---|:---|:---|
| Session | local `sessions/` | current project + run | task, findings, decisions, touched files, intent, compact/resume |
| Journal | local `journal/<project-hash>/` | current project + recent sessions | raw user prompts, assistant outputs, tool outcomes, lifecycle markers |
| Brain | hosted Postgres | project | derived facts, decisions, constraints, preferences, corrections |
| Knowledge | local `knowledge/<project-hash>/knowledge.json` and hosted Postgres projection | project | public recall, wakeup briefing, lifecycle/triage |

## Public vs Internal Surface

Publieke MCP clients praten voor memory via `ctx(domain="memory", action=...)`.

Client routeert dat intern zo:

- `task`, `finding`, `decision`, `save`, `load`, `status`, `reset`, `list`, `cleanup` -> `ctx_session`
- `store`, `set`, `remember`, `recall`, `search`, `categories`, `timeline`, `consolidate`, `promote`, `upkeep`, `triage`, `wakeup`, `remove` -> `ctx_knowledge`
- `upvote`, `downvote`, `confirm`, `reject` -> `ctx_knowledge` review aliases mapped to the same candidate lifecycle

`ctx_brain` is intern/server-only. Hooks en editor adapters gebruiken die voor typed hosted brain facts. Externe clients horen via publieke surface niet rechtstreeks `ctx_brain` aan te roepen.

## Project Bootstrap Workflow

Project bootstrap is expliciet, user-initiated, en preview-first.

Gebruik:

```bash
nebu-ctx project-bootstrap preview [--path <repo>]
nebu-ctx project-bootstrap apply [--path <repo>]
```

- `preview` bouwt project map + candidate facts uit bestaande signalen zoals markers, talen, entrypoints, tests, infra en modules.
- `preview` schrijft niets weg.
- `apply` schrijft pas na expliciete bevestiging via canonieke knowledge/memory paths met stabiele provenance.

Bootstrap is dus laag bovenop brain/knowledge, niet stilzwijgende background capture.

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

### Journal memory

Lokale journal entries bewaren onder meer:

- raw user prompts
- assistant output parts/completions
- tool outcomes en compressed output hints
- lifecycle markers zoals startup, compact, idle, stop

Journal blijft client-local. Het is geen canonieke server memorylaag.

### Brain memory

Brain bewaart typed canonieke facts met lifecycle-data:

- `kind`
- `category`
- `key`
- `value`
- `confidence`
- `logical_key`
- `promotion_identity`
- `source_type`, `source_scope`
- `lifecycle_status`
- `superseded_by`, `invalidated_by`
- `evidence`

Brain is echte project-memory. Raw transcript hoort hier niet thuis.

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

Knowledge blijft publieke retrieval/projectielaag bovenop canonieke memory:

- wake-up briefing
- recall/search resultaten
- dashboard memory health en triage
- local fallback facts wanneer host niet beschikbaar is

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
- schrijft event naar lokale journal
- laat latere lifecycle flush eventuele afgeleide facts naar hosted brain sturen

Doel:

- recente user intent en constraints beschikbaar maken voor fact extractie zonder raw transcript server-side te bewaren

### 3. `AssistantOutputSubmit`

Command: `nebu-ctx hook assistant-output-submit`

Bron:

- editor/plugin message-part events, o.a. OpenCode plugin

Doet:

- pakt assistant tekst
- filtert system/hook output eruit
- schrijft event naar lokale journal
- laat lifecycle flush er beslissingen, bevestigde findings en correcties uit afleiden

### 4. `PreCompact`

Command: `nebu-ctx hook pre-compact`

Bron:

- Claude Code `PreCompact`

Doet:

- flushes pending telemetry/server outbox
- bouwt compacte `<session_state>` XML uit lokale session + knowledge
- flusht journal -> fact extractie -> hosted brain ingest
- ververst hosted/public knowledge projection
- geeft XML terug als `additionalContext`

Uitlezen:

- local session state
- hosted wakeup briefing als beschikbaar, anders lokale knowledge facts

Opslag:

- brain: afgeleide facts via `ctx_brain(action="ingest")`
- knowledge: brain-backed projection en lokale fallback facts

### 5. `Stop`

Command: `nebu-ctx hook stop`

Bronnen:

- Claude Code `Stop`
- Copilot CLI `postSession`

Doet:

- flushes pending telemetry/server outbox
- draait lokale consolidation van laatste session
- flusht journal -> fact extractie -> hosted brain ingest
- ververst knowledge projection/wakeup

Opslag:

- brain: alleen afgeleide facts, geen raw session-summary logregel
- knowledge: current projection voor publieke recall/wakeup

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

Niet alles loopt via hooks. Gewoon MCP-gebruik schrijft ook session/journal/knowledge memory:

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

`ctx_brain(action="recall")` leest hosted canonieke brain facts. Dit is vooral bedoeld voor internal/dashboard/service usage, niet publieke MCP-contract calls.

Gebruik:

- hosted fact recall voor internal lifecycle flows
- dashboard brain facts lijst
- brain-backed wake-up/projectie-opbouw

## Hosted Sync Rules

Client probeert server-calls direct te doen. Als server niet bereikbaar is:

- server tool-call gaat naar lokale outbox onder `sync/outbox/`
- outbox wordt later gereplayed

Belangrijk gevolg:

- journal writes blijven lokaal beschikbaar als host wegvalt
- hosted brain/knowledge kan kort achterlopen op lokale state tot replay gebeurt

## Consolidation Pipeline

Flow van werkgeheugen naar duurzame knowledge:

1. Session en journal verzamelen task/findings/decisions/files/intents en raw lifecycle-events.
2. Client fact extractie haalt bruikbare facts uit session + journal.
3. Facts landen hosted in `ctx_brain(action="ingest")`.
4. Brain ingest ververst de hosted/public knowledge projection.
5. Hosted knowledge lifecycle doet ranking, history, stale marking, wakeup, triage.

Praktisch:

- session = working memory
- journal = local raw event log
- brain = canonical fact memory
- hosted knowledge = public projection + wakeup/recall surface
