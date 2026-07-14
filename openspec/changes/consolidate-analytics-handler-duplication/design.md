## Context

Four analytics tool handlers each independently declare an identical `IndentedJson` field, and two of them (`Cost`, `Gain`) independently declare a byte-for-byte identical `GetCommands` helper. A prior broad audit finding flagged "duplicated logic across analytics handlers" without pinpointing exact instances; on inspection, only these two pieces are genuinely duplicated — the per-domain `Build*` report methods differ meaningfully and should not be merged.

## Goals / Non-Goals

**Goals:**
- Remove the two confirmed byte-for-byte duplicates with a small, internal shared helper class.
- Preserve identical behavior — same JSON options, same command-resolution logic, just relocated.

**Non-Goals:**
- Restructuring or merging the per-domain `Build*` methods (`BuildReport`, `BuildScore`, `BuildDirectory`, etc.) — assessed as genuinely different per domain; forcing them through a shared abstraction would trade minor duplication for a worse, harder-to-change design.
- Touching `HeatmapToolHandler.GetFileAccess`/`GetDirectory` or `StatsToolHandler.GetProjects` — different data shapes, not duplicates of `GetCommands`.
- Any change to `TelemetryStore` itself.

## Decisions

- **New `NebuCtx.Tools.Analytics` namespace, internal static class.** Scoped narrowly to just the two confirmed duplicates, not a general-purpose "analytics utilities" grab-bag that would invite future unrelated additions.
- **Only touch `Cost`/`Gain` for `GetCommands`.** `Heatmap` and `Stats` don't have this method at all — confirmed in the underlying plan's "Current state" — so they are untouched for that part of the change.

## Risks / Trade-offs

- [Risk] If `IndentedJson` or `GetCommands` have silently diverged (different option values or logic) in any of the 4 files since this was scoped, blind consolidation would be a regression. → Mitigation: STOP condition requires reporting a divergence rather than forcing a merge if the "Current state" excerpts in the underlying plan no longer match live code exactly.
