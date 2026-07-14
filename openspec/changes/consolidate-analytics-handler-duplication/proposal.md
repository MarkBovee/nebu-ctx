## Why

Two things are byte-for-byte duplicated across the four analytics tool handlers (`CostToolHandler`, `GainToolHandler`, `HeatmapToolHandler`, `StatsToolHandler`) with no legitimate reason to differ: a `private static readonly JsonSerializerOptions IndentedJson` field declared identically in all 4 files, and a `GetCommands(TelemetryStore.Snapshot, string?)` helper declared identically in both `CostToolHandler` and `GainToolHandler`. Any future change to project-scoped command lookups would need to be made twice today and could silently drift. This plan intentionally covers only this concrete, verified duplication — not the per-domain `Build*` report methods, which are genuinely different per domain and should stay separate.

## What Changes

- Create a new internal `AnalyticsSnapshotHelpers` static class in a new `NebuCtx.Tools.Analytics` namespace, consolidating `IndentedJson` and `GetCommands`.
- Update all 4 handlers to reference the shared `IndentedJson`; update `CostToolHandler` and `GainToolHandler` to reference the shared `GetCommands`.
- **Not BREAKING**: pure refactor, no behavior change — same values, same method bodies, just relocated. No external API or tool-output change.

## Capabilities

### New Capabilities
- `analytics-tooling`: defines that all analytics MCP tool handlers (`ctx_cost`, `ctx_gain`, `ctx_heatmap`, `ctx_stats`) use one shared, consistent JSON-formatting configuration and one shared, consistent project-scoped command-lookup rule, so future changes to either are made once instead of drifting across handlers.

### Modified Capabilities

## Impact

- **Code**: new `server/src/NebuCtx.Tools/Analytics/AnalyticsSnapshotHelpers.cs`; modifies `CostToolHandler.cs`, `GainToolHandler.cs`, `HeatmapToolHandler.cs`, `StatsToolHandler.cs`.
- **Explicitly out of scope**: the per-domain `Build*` report methods (cost math, gain scoring, heatmap directory grouping, project stats) — these differ meaningfully per domain and must not be merged into one shared abstraction; `HeatmapToolHandler.GetFileAccess`/`GetDirectory` and `StatsToolHandler.GetProjects` — different data shapes, not duplicates.
- Full technical detail (exact current code, line numbers, and exact diffs) already captured in `plans/006-consolidate-analytics-handler-duplication.md` — this proposal is the OpenSpec-tracked counterpart of that plan.
