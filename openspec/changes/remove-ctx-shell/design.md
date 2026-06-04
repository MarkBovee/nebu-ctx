## Context

`nebu-ctx` heeft vandaag een publieke MCP-tool surface van vijf tools: `ctx_read`, `ctx_search`, `ctx_tree`, `ctx_shell`, en `ctx`. `ctx_shell` is geïmplementeerd in `client/src/tools/ctx_shell.rs` en biedt:

- Command-validatie die gevaarlijke file-write commando's blokkeert.
- Detectie van OAuth device-code flows zodat gevoelige output niet wordt gecomprimeerd.
- Pattern-based compressie van shell-output (git, search, cargo, npm).
- `shell_path` override zodat de agent per call een shell kan forceren.

De gebruiker ervaart de netto winst van deze functionaliteit als negatief: de pattern compression levert in de praktijk te weinig tokens op, agents lopen vast op validatie- en dispatch-regels die ze niet verwachten, en de extra protocolruis weegt niet op tegen de besparing. De gekozen aanpak is verwijdering, niet verfijning.

De huidige implementatie verspreidt `ctx_shell`-verwijzingen over minstens 20 bronbestanden (client, server, tests, guidance-templates, documentatie). De wijziging is dus een cross-cutting verwijdering met expliciete impact op zowel de Rust thin client als de .NET MCP host.

## Goals / Non-Goals

**Goals:**

- Volledige verwijdering van `ctx_shell` uit de publieke MCP-tool surface.
- Volledige verwijdering van de implementatie, dispatch, en testcode voor `ctx_shell`.
- Bijgewerkte guidance zodat agents standaard native shell/bash tools gebruiken en niet langer naar een niet-bestaande publieke shell-tool zoeken.
- Consistente documentatie, changelogs, en OpenSpec specs die de nieuwe 4-tool surface weerspiegelen.
- Behoud van gedrag voor de overige vier publieke tools.

**Non-Goals:**

- Het verbeteren of behouden van command-validatie, auth-flow detectie, of pattern compression via een ander kanaal. Deze functionaliteit verdwijnt met de tool.
- Migratie van bestaande sessie-state: `ctx_shell` schreef geen persistente data die na verwijdering behouden moet blijven.
- Wijzigingen aan de overige publieke tools (`ctx_read`, `ctx_search`, `ctx_tree`, `ctx`).
- Nieuwe features, dependency updates, of refactoringen buiten het verwijderen van `ctx_shell`.

## Decisions

- **Geen deprecation-fase, directe verwijdering.** De tool is klein genoeg en de gebruiker wil hem volledig kwijt. Een soft-deprecation pad (tool blijft bestaan met waarschuwing) zou de compressie- en validatielogica nog langer in stand houden en dat druist in tegen het doel.
  - *Alternatief overwogen*: een feature-flag om `ctx_shell` uit te zetten. Verworpen omdat de hele code dan als dode tak in de codebase blijft hangen.

- **Publieke surface van 5 naar 4 tools, geen "experimental" status.** `ctx_read`, `ctx_search`, `ctx_tree`, en `ctx` blijven de canonieke publieke surface. Geen hernoeming van `ctx_shell` naar een privaat of server-only tool.
  - *Alternatief overwogen*: `ctx_shell` als `SERVER_ONLY_TOOL` op de host zetten zodat agents hem via server routing kunnen blijven gebruiken. Verworpen omdat de gebruiker expliciet van de tool af wil, niet alleen uit de client.

- **Guidance noemt expliciet native shell/bash.** De guidance-templates verliezen niet alleen de `ctx_shell`-vermelding, maar krijgen waar nodig een aanbeveling om native shell/bash te gebruiken voor shell-acties. Dit voorkomt dat agents blijven zoeken naar een vervangende publieke tool.
  - *Alternatief overwogen*: guidance zwijgt over shell. Verworpen omdat dat agents in verwarring laat; een expliciete aanwijzing is duidelijker.

- **Auth-flow detectie gaat verloren.** De `contains_auth_flow` logica in `ctx_shell.rs` wordt met de tool verwijderd. Compressie van device-code flows is geen onderdeel meer van nebu-ctx.
  - *Alternatief overwogen*: auth-flow detectie verplaatsen naar een generieke `compress_output` post-processor. Verworpen omdat dit extra complexiteit toevoegt voor een niche-randgeval dat alleen relevant was via `ctx_shell`.

- **Geen CLI-subcommando voor shell als vervanger.** De CLI blijft zoals hij is; agents gebruiken hun eigen native shell tool. `nebu-ctx` heeft al voldoende CLI-oppervlak en een shell-subcommando zou dezelfde wrijving veroorzaken als `ctx_shell`.
  - *Alternatief overwogen*: een `nebu-ctx shell` subcommando. Verworpen om dezelfde reden als hierboven.

## Risks / Trade-offs

- **[Risico] Bestaande agents die `ctx_shell` aanroepen falen** → Mitigatie: de foutmelding die de MCP-server geeft voor onbekende tools is duidelijk genoeg; de guidance wordt bijgewerkt zodat nieuwe agents direct de native shell gebruiken. Geen backwards-compatibility belofte voor een tool die de gebruiker volledig wil verwijderen.
- **[Risico] Verlies van pattern compression voor shell-output** → Mitigatie: accepteer. De gebruiker weegt de besparing lichter dan de wrijving. Andere tools (zoals `ctx_read`) hebben hun eigen compressie waar dat zinvol is.
- **[Risico] Verlies van command-validatie** → Mitigatie: accepteer. Validatie tegen file-write patronen was nuttig maar voegt extra ruis toe in dispatch. Agents die per ongeluk files wegschrijven via shell kunnen dat via de native tool net zo goed (of fout) doen; nebu-ctx claimt hier geen verantwoordelijkheid meer.
- **[Risico] Verwijdering raakt veel bestanden, dus grotere diff en meer kans op missers** → Mitigatie: tasks.md wordt opgesplitst in kleine, verifieerbare stappen met expliciete `cargo test` en `dotnet test` checkpoints.
- **[Trade-off] Minder differentiator t.o.v. kale MCP setups** → accepteer. De kernwaarde van nebu-ctx blijft `ctx_read` (caching + compressie), `ctx_search`, `ctx_tree`, en het `ctx` meta-tool voor memory/context/analytics/agents.
