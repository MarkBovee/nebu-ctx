## Why

`ctx_shell` voegt te weinig compressiewinst op voor zijn complexiteit en maakt de agent-ervaring zwaarder dan nodig. De tokenbesparing die pattern compression oplevert weegt niet op tegen de extra protocolruis, de extra validatie- en dispatchlagen, en de wrijving die agents ondervinden bij het aanroepen van een shell via een meta-tool. Door `ctx_shell` uit de publieke MCP-surface te schrappen kunnen agents direct de native shell/bash tools gebruiken die ze al kennen, terwijl de resterende vier publieke tools (read/search/tree/ctx) de kernwaarde van nebu-ctx blijven leveren.

## What Changes

- **BREAKING**: `ctx_shell` wordt verwijderd uit de publieke MCP-tool surface. De surface gaat van vijf naar vier tools: `ctx_read`, `ctx_search`, `ctx_tree`, en `ctx`.
- De implementatie van `ctx_shell` (inclusief command-validatie, auth-flow detectie, shell-normalisatie, en output-compressie) wordt verwijderd.
- Alle dispatch-, tool-definitie-, en hook-handler code die `ctx_shell` routeert of exposeert wordt verwijderd.
- De public-guidance templates verwijzen niet langer naar `ctx_shell`; in plaats daarvan adviseren ze expliciet native shell/bash.
- Documentatie, changelogs, README, en agent-rules worden bijgewerkt zodat geen oppervlak nog `ctx_shell` aanbeveelt.
- Tests die specifiek voor `ctx_shell` zijn geschreven worden verwijderd; tellingen en assertions rond het aantal publieke tools worden op vier gezet.

## Capabilities

### New Capabilities
- `public-tool-surface`: definieert de vernauwde publieke MCP-tool surface (zonder `ctx_shell`) en de bijbehorende guidance-regels.

### Modified Capabilities
- `public-guidance`: REQUIREMENTS veranderen omdat de guidance niet langer `ctx_shell` noemt als onderdeel van het publieke oppervlak, en niet langer instrueert om native shell te vermijden ten gunste van een publieke shell-tool.

## Impact

- **Rust client (`client/`)**: verwijdering van `client/src/tools/ctx_shell.rs`, de `pub mod ctx_shell;` registratie, en dispatch-arm in `client/src/mcp_server/dispatch.rs`. Aanpassingen in `client/src/mcp_server/mod.rs` (public surface validatie, tests), `client/src/tool_defs/mod.rs`, `client/src/tool_defs/granular.rs`, `client/src/shell.rs`, `client/src/hook_handlers.rs`, `client/src/core/loop_detection.rs`, `client/src/core/workflow/types.rs`, `client/src/core/stats.rs`, `client/src/core/editor_registry/writers.rs`, `client/src/instructions.rs`, `client/src/public_guidance.rs`, `client/src/mcp_server/execute.rs`, en de binary `client/src/bin/seed_observatory.rs`.
- **Test suites**: `client/tests/shell_and_agent_tests.rs` wordt verwijderd; test-modules in `client/src/tools/ctx_shell.rs` (verwijderd) en `client/src/mcp_server/mod.rs` worden bijgewerkt; `tests/intensive_benchmarks.rs` en `client/src/rules_inject.rs` verliezen hun ctx_shell assertions.
- **.NET server (`server/`)**: tool-registratie in `server/src/NebuCtx.Server.Core/ToolRegistry.cs` verliest `ctx_shell`; integratietests in `server/tests/NebuCtx.IntegrationTests/McpEndpointTests.cs` verliezen hun `ctx_shell`-cases; dashboard string in `server/src/NebuCtx.Server.Host/Dashboard/dashboard.html` verliest de verwijzing.
- **Documentatie en changelogs**: `README.md`, `CHANGELOG.md`, `homeassistant/CHANGELOG.md`, en `.claude/rules/nebu-ctx.md` worden bijgewerkt.
- **OpenSpec specs**: `openspec/specs/public-guidance/spec.md` wordt aangepast zodat `ctx_shell` niet langer in de publieke surface-lijsten voorkomt.
- **Gebruikers en agents**: bestaande agents die `ctx_shell` aanroepen krijgen een duidelijke "unknown tool" foutmelding; ze schakelen over op de native shell/bash tooling. Geen data-migratie nodig omdat `ctx_shell` geen persistente state bijhoudt buiten de sessie.
