## Context

We willen een expliciete, user-initiated bootstrapflow voor projectkennis. Die flow moet agents helpen een repository snel te begrijpen zonder stilzwijgend memory te vervuilen. `nebu-ctx` heeft hiervoor al de bouwstenen: project metadata, overview/index tooling, public memory routing en canonical brain/knowledge opslag. Wat ontbreekt is een goede orkestratielaag plus een veilige preview/apply scheiding.

## Goals / Non-Goals

**Goals:**
- een skill toevoegen die gebruikers bewust kunnen starten om een project te laten mappen
- bestaande `ctx_*` read-tools hergebruiken in plaats van nieuwe ad-hoc shell workflows te introduceren
- kandidaat-facts eerst tonen en pas na expliciete bevestiging opslaan
- docs/help/cheatsheet zodanig bijwerken dat de workflow vindbaar is

**Non-Goals:**
- project bootstrap stilzwijgend op elke nieuwe repo automatisch uitvoeren
- raw README- of file-tree-dumps als memory opslaan
- een volledig nieuwe publieke MCP top-level tool introduceren als skill-orkestratie genoeg is

## Decisions

### 1. Skill is user-facing voordeur; bootstrap engine is onderliggende uitvoerlaag

De skill wordt het primaire instappunt. Onderliggend mag een deterministische bootstrap-engine preview- en apply-data genereren zodat de skill niet alleen prompttekst is.

Waarom:
- skill sluit aan op natuurlijke user-intent
- engine voorkomt dat preview en apply uit elkaar lopen

### 2. Preview-first, apply-explicit

De bootstrapflow produceert eerst een project map en voorgestelde facts. Pas bij expliciete bevestiging schrijft de workflow naar canonical memory.

Waarom:
- voorkomt memory-noise
- laat gebruiker facts reviewen
- sluit aan op wens om bootstrap bewust te initieren

### 3. Bestaande signals eerst, geen full-repo dump

De eerste versie gebruikt vooral bestaande signalen:
- `ctx_overview`
- project metadata (markers, talen, file counts)
- code-index / entrypoint signalen waar beschikbaar
- beperkte gerichte searches voor test/infra/build conventions

Waarom:
- klein en betrouwbaar
- snelle implementatie met bestaande primitives

### 4. Canonical writes blijven via bestaande memory ownership lopen

Als apply wordt bevestigd, schrijft bootstrap facts via bestaande canonical brain/knowledge paden met deterministische identities en provenance.

Waarom:
- geen tweede memory-silo
- replay-safe en dashboard-compatibel

## Risks / Trade-offs

- skill zonder engine zou te los kunnen worden -> daarom preview/apply engine onder skill voorzien
- te veel heuristiek geeft rommel -> v1 beperken tot stack, entrypoints, tests, infra, modules, workflow conventions
- docs vergeten mee te nemen maakt feature onzichtbaar -> docs/help expliciet onderdeel van change

## Migration Plan

1. Voeg change-artifacts en requirements toe voor skill + preview/apply bootstrap.
2. Voeg skill asset en installatiepad toe.
3. Bouw preview/apply bootstrap engine met bestaande project-signalen.
4. Koppel apply aan canonical memory writes.
5. Werk docs/help/cheatsheet en tests bij.
