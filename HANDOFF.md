# Handoff

Updated: 2026-04-22
Repo: nebu-ctx
Status: stopped for today

## Current State

- The .NET host, MCP surface, dashboard wiring, and Rust thin client migration work are in progress and the repo has uncommitted changes.
- Dashboard asset ownership was corrected: `dashboard.html` now lives under the .NET dashboard project and is published with the server output.
- A first packaging simplification pass was applied earlier, but it is not the final direction anymore.
- The latest requested direction is:
  - commit the current state first
  - then replace the Docker setup with one simple new root Dockerfile
  - keep `dist/` in the repo root as the publish output
  - use a .NET 10 Alpine runtime image
  - keep runtime startup simple and avoid extra injected runtime behavior

## Important User Intent

- "maak eerst een HANDOFF.md document dan kunnen we morgen verder"
- Before that, the active packaging request was:
  - one Dockerfile if possible
  - dist-first publish flow
  - KISS runtime image
  - no SDK in the runtime image
  - test locally after the redesign

## What Was Already Validated

- The .NET dashboard project now publishes `dashboard.html` from its own project directory.
- Dist-first publish support exists via `tests/lib/dotnet_dist.sh`.
- The current root Dockerfile consumes published output from `dist/server/linux/`.

## What Is Not Finished

- No checkpoint commit has been created yet.
- The requested Docker reset has not started yet.
- Local end-to-end validation for the latest Docker changes was not completed.
- Dashboard parity versus the legacy Rust implementation still needs a final visual/behavior check.

## Working Tree Notes

The working tree currently includes changes in these areas:

- Docker and runtime scripts
  - `.dockerignore`
  - `Dockerfile`
  - `docker-entrypoint.sh`
  - `homeassistant/Dockerfile`
  - `homeassistant/Dockerfile.source`
  - `homeassistant/README.md`
  - `homeassistant/run.sh`
  - `tests/local-addon-test.sh`
  - `tests/local-server-cli-test.sh`
  - `tests/pre_release_check.sh`
  - `tests/lib/dotnet_dist.sh`
- Dashboard and server telemetry work
  - `src/server/src/NebuCtx.Dashboard/*`
  - `src/server/src/NebuCtx.Application/*`
  - `src/server/src/NebuCtx.Application/Routing/*`
  - `src/server/src/NebuCtx.Contracts/*`
  - `src/server/src/NebuCtx.Projects/*`
  - `src/server/src/NebuCtx.Storage/*`
  - `src/server/src/NebuCtx.Tools/*`
  - `src/server/tests/*`
- Rust thin client / hybrid local-tool work
  - `src/client/Cargo.toml`
  - `src/client/src/cli.rs`
  - `src/client/src/git_context.rs`
  - `src/client/src/lib.rs`
  - `src/client/src/models.rs`
  - `src/client/src/server_client.rs`
  - `src/client/src/local_symbols.rs`
  - `src/client/src/local_tools.rs`
  - `src/client/src/project_metadata.rs`
  - `Cargo.lock`
- Misc
  - `.github/workflows/release.yml`
  - `docs/plans/dotnet-10-server-migration-plan.md`
  - `tests/tmp-local-addon-summary.sh`

## Suggested First Steps Tomorrow

1. Run `git status` and review the current working tree before changing anything.
2. Create the checkpoint commit the user asked for.
3. Remove the old multi-Docker setup and replace it with one clean root Dockerfile.
4. Keep the publish flow external and output to root `dist/`.
5. Re-run focused local validation after the first Docker redesign edit.
6. Open the dashboard locally and verify the remaining parity concerns.

## Known Risks / Caveats

- The current root Dockerfile still uses `mcr.microsoft.com/dotnet/aspnet:10.0`, not Alpine.
- The current entrypoint still contains standalone vs Home Assistant mode switching logic; that may be more than the user wants in the final KISS design.
- Some dashboard endpoints exist but still have simplified payloads, especially around heatmap/call-graph richness.
- There is no completed smoke-test result for the latest packaging edits because the earlier add-on validation attempt was canceled.

## Resume Point

Resume in `e:\Projects\Personal\nebu-ctx`.

Immediate next action for tomorrow:

- create the requested checkpoint commit
- then start the Docker simplification from a clean, explicit root design