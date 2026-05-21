## Why

`nebu-ctx` heeft al sterke read-surface voor projectoriëntatie (`ctx_overview`, `ctx_tree`, `ctx_search`) en een sterkere memory-stack voor canonical facts, maar mist nog een duidelijke, user-initiated bootstrapflow om een project eerst te mappen en daarna pas bewust projectkennis op te bouwen. Automatische memory-writes bij eerste gebruik zijn hiervoor te agressief en maken memory-noise waarschijnlijk.

Tegelijk zijn de oude skill-bestanden niet meer in gebruik. Dat maakt dit een goed moment om een expliciete `project-bootstrap` skill toe te voegen waarmee een gebruiker een agent bewust kan vragen om een repository te scannen, een project map te bouwen, kandidaat-facts voor memory voor te stellen en alleen na bevestiging facts op te slaan.

## What Changes

- voeg een nieuwe `project-bootstrap` skill toe als user-facing startpunt voor repo mapping en memory bootstrap
- laat de skill read-only project mapping uitvoeren via bestaande `ctx_*` surfaces en project metadata/index-signalen samenvatten
- laat de skill memory candidates eerst als preview tonen en pas na expliciete bevestiging canonical memory writes uitvoeren
- voeg een deterministische bootstrap-engine toe onder de skill zodat preview/apply hetzelfde feitmodel gebruiken
- werk README, memory docs, cheatsheet/help en skill-installatiepaden bij zodat deze flow zichtbaar en bruikbaar is

## Capabilities

### New Capabilities
- `project-bootstrap`: user-initiated project mapping and memory bootstrap workflow with preview/apply behavior

### Modified Capabilities
- `memory`: clarify that project bootstrap is an explicit workflow above canonical brain/knowledge storage, not an implicit background write

## Impact

- skill assets and installation wiring under `client/assets/skills/` and/or related hook/setup surfaces
- client bootstrap logic that derives project summary and candidate facts from overview, metadata, and index signals
- docs/help surfaces such as `README.md`, `docs/MEMORY.md`, and CLI cheatsheet/help output
- tests covering preview/apply behavior and skill discoverability
