## Context

`nebu-ctx` heeft nu een bruikbare memorybasis: lokale session capture, server-backed canonical knowledge, promote/consolidate flows, en dashboard inspection. De huidige vorm is nog vooral gericht op correctness en parity tussen editors, niet op langdurig memorybeheer voor projecten die honderden of duizenden facts en session-derived memories gaan opbouwen.

`mempalace` laat drie patronen zien die hier relevant zijn:

- layered wake-up in plaats van "laad alles"
- temporal facts met invalidation in plaats van blinde overwrite
- idempotente ingest en resume-safe replay in plaats van beste-gok retries

Belangrijke constraints:

- de publieke 5-tool MCP surface mag niet breken
- client-local fallback moet blijven bestaan wanneer er geen host is
- server moet canonical memory owner zijn zodra een host actief is
- testbare serverpaden moeten via de bestaande integration harness zonder Postgres kunnen lopen

## Goals / Non-Goals

**Goals:**
- bounded memory startup invoeren via gelaagde wake-up context
- canonical server memory scoren en onderhouden zodra de dataset groeit
- een expliciete hosted memory triageflow bieden voor dedup, merge, rescoring, en cleanup van vermoedelijke junk/test/demo-memory
- promoted memory replay idempotent maken
- superseded facts historisch kunnen behouden in plaats van alleen overschrijven
- dashboard operators zicht geven op memory health en onderhoudsstatus
- OpenCode hooks en plugin dezelfde lifecycle-selectie laten gebruiken als de hosted memorylaag aanbiedt

**Non-Goals:**
- direct een volledige graph-native memory engine bouwen als vervanging van de huidige knowledge store
- alle lokale `ctx_knowledge` acties server-side dupliceren in deze change
- nieuwe externe managed memory dependencies toevoegen
- bestaande editor hook parity opnieuw ontwerpen buiten memory selection en upkeep

## Decisions

### 1. Server blijft canonical owner, maar lifecycle blijft hybrid ingest

De server wordt de eigenaar van canonical memory scoring, upkeep, invalidation en wake-up selection. De client blijft verantwoordelijk voor het capturen van lokale transcript/session context en het aanleveren van explicit promotion candidates.

Waarom:
- de client ziet editor- en session-bound context die de server niet rechtstreeks heeft
- de server is de juiste plek voor cross-device consistency en operator tooling

Alternatieven:
- alles client-local houden: afgewezen, omdat consistency en dashboard health dan blijven afdrijven
- alles direct server-side extraheren: afgewezen, omdat editor/session capture lokaal ontstaat en niet altijd volledig server-visible is

### 2. Memory retrieval wordt gelaagd naar mempalace-achtig model

We introduceren een bounded retrieval model:

- L0: routing + active memory identity for the project/session
- L1: bounded wake-up summary met hoogste prioriteit facts en recente locked decisions
- L2: category/topic scoped recall voor expliciete onderwerpen
- L3: deep recall/search voor brede of moeilijke queries

Waarom:
- startup context moet klein en voorspelbaar blijven
- veel memories moeten niet automatisch prompt-bloat worden

Alternatieven:
- alleen sorteren op confidence en top-N teruggeven: afgewezen, omdat dat geen onderscheid maakt tussen wake-up, targeted recall en deep recall

### 2b. OpenCode plugin consumeert lifecycle outputs actief

De OpenCode plugin moet de nieuwe hosted wake-up en lifecycle outputs actief gebruiken in zijn startup-, compacting-, idle- en continuation-hooks, in plaats van alleen generieke memory parity te behouden.

Waarom:
- anders blijft de nieuwe lifecycle alleen server-intern zichtbaar en niet merkbaar in dagelijkse editorflows
- OpenCode heeft nu al lifecycle hook-oppervlak dat geschikt is voor wake-up en continuation injection

Alternatieven:
- OpenCode pas later aansluiten: afgewezen, omdat dan de nieuwe memorylaag inconsistent aanvoelt tussen dashboard, server en editor

### 3. Promotion en replay worden idempotent met deterministische memory identities

Promoted candidates krijgen een stabiele identity gebaseerd op source scope, category en logical key. Replay van dezelfde batch mag canonical knowledge niet dupliceren of laten divergeren.

Waarom:
- outbox replay, hook retries en repeated consolidations zijn normaal gedrag
- memory systems falen hard op usefulness zodra retries duplicates produceren

Alternatieven:
- best-effort upsert alleen op category/key: onvoldoende voor batch provenance en temporal replacement flows

### 4. Superseded facts worden onderhouden via temporal lifecycle, niet alleen overwrite

Wanneer nieuwe promoted knowledge bestaande current facts vervangt, markeert de server de oudere fact als niet-current/superseded in lifecycle metadata in plaats van die context volledig te verliezen.

Waarom:
- historical recall en timeline/debugging blijven dan mogelijk
- dit sluit aan op het `mempalace` invalidation patroon zonder direct een volledige triple-store te vereisen

Alternatieven:
- direct full temporal graph: afgewezen voor deze change, te groot en te cross-cutting
- overwrite only: afgewezen omdat onderhoud en historical reasoning dan snel onbetrouwbaar worden

### 5. Upkeep wordt een expliciete server capability met dashboard health en lifecycle surfacing

Memory scoring, staleness marking, wake-up summary refresh en retention-ready consolidation worden server-side upkeep stappen. De resultaten worden zichtbaar in dashboard memory health, wake-up composition, en lifecycle change indicators in plaats van verborgen maintenance.

Waarom:
- operators moeten kunnen zien wanneer memory groeit, stale wordt, opnieuw opgebouwd is, of inhoudelijk verschuift door rescoring en supersession
- onderhoud zonder observability is praktisch niet beheerbaar

### 6. Hosted memory triage is expliciet, server-owned, en dry-run-first

Naast automatische upkeep krijgt de hosted memorylaag een expliciete triageflow die de volledige canonical project memoryset kan inspecteren en beoordelen. Die triage kan near-duplicates groeperen, logisch overlappende facts samenvoegen of supersession voorstellen, zwakke/stale facts herwaarderen, en vermoedelijke test/demo/noise memories markeren.

Triage levert standaard eerst een preview op met voorgestelde acties, redenen, en verwachte wake-up impact. Een apply-pad moet expliciet zijn. Waar mogelijk gebruikt apply lifecycle-safe acties zoals merge, supersede, ignore, of junk marking in plaats van stille hard deletes.

Waarom:
- operators en agents hebben een gericht instrument nodig om memorykwaliteit te herstellen zodra projecten veel facts opbouwen
- dry-run-first voorkomt dat agressieve cleanup nuttige historische context verwijdert
- dit sluit aan op mempalace-patronen zoals dedup preview/statistieken en geplande stale/contradiction workflows, zonder die ruwe tooling 1-op-1 over te nemen

Alternatieven:
- alleen background upkeep: afgewezen, omdat operators dan geen inspecteerbare cleanup- en reviewstap hebben
- direct destructieve dedup/prune: afgewezen, omdat memoryverlies moeilijk detecteerbaar en duur is

## Risks / Trade-offs

- [Meer metadata in knowledge storage] -> Houd eerste versie compact: lifecycle fields toevoegen die direct nodig zijn voor score, status en supersession; geen full graph schema in deze change
- [Wake-up selectie kan belangrijke facts missen] -> Houd deep recall en category recall beschikbaar en test startup snapshots tegen budget + relevance cases
- [Idempotente replay kan lastig zijn over oudere outbox payloads] -> Houd backward-compatible parsing aan en default deterministic keys af uit bestaande category/key/value combinaties
- [Temporal maintenance kan bestaande dashboards of recall output veranderen] -> Surface nieuwe health fields additief en houd bestaande knowledge listing backward-compatible
- [Triage kan fout-positieve cleanup voorstellen] -> Maak preview standaard, preserveer provenance, en gebruik apply alleen voor expliciet bevestigde lifecycle-veranderingen

## Migration Plan

1. Voeg lifecycle metadata en deterministische promotion identities toe aan server contracts/stores met veilige defaults.
2. Implementeer server upkeep + layered wake-up selection zonder bestaande recall/remove flows te breken.
3. Laat client promote/replay payloads overstappen op deterministische batches.
4. Voeg hosted triage preview/apply flows toe bovenop canonical memory scoring en supersession.
5. Breid dashboard memory inspection uit met health, maintenance status, wake-up composition, lifecycle change signals, en triage resultaten.
6. Deploy server eerst, daarna client, zodat nieuwe actions en wake-up outputs beschikbaar zijn voordat OpenCode en andere clients ze prefereren.
7. Rollback: client kan terugvallen op huidige lokale memory actions; server additions zijn additive en kunnen genegeerd worden door oudere clients.

## Open Questions

- Moet L1 wake-up summary fysiek persisted worden of on-demand rebuilt blijven zolang de dataset nog beperkt is?
- Welke lifecycle signals tellen mee in score v1: confidence, recency, reaffirmation count, retrieval count, source type?
- Willen we temporal maintenance als aparte tool action (`upkeep`) exposen of alleen intern/background first houden?
