## 1. Create shared helper

- [x] 1.1 Create `server/src/NebuCtx.Tools/Analytics/AnalyticsSnapshotHelpers.cs` with `internal static readonly JsonSerializerOptions IndentedJson` and `internal static GetCommands(...)` (moved verbatim from `CostToolHandler`)
- [x] 1.2 Verify: `dotnet build server/NebuCtx.slnx -p:AllowMissingPrunePackageData=true` → 0 errors, 0 warnings (new file compiles standalone)

## 2. Replace duplicated IndentedJson (4 files)

- [x] 2.1 In `CostToolHandler.cs`, `GainToolHandler.cs`, `HeatmapToolHandler.cs`, `StatsToolHandler.cs`: add `using NebuCtx.Tools.Analytics;`, delete the local `IndentedJson` field, change the use-site to `AnalyticsSnapshotHelpers.IndentedJson`
- [x] 2.2 Verify: `grep -rn "private static readonly JsonSerializerOptions IndentedJson" server/src/NebuCtx.Tools/` → no matches

## 3. Replace duplicated GetCommands (Cost and Gain only)

- [x] 3.1 In `CostToolHandler.cs` and `GainToolHandler.cs`: delete the local `GetCommands` method, change all call sites to `AnalyticsSnapshotHelpers.GetCommands(...)`
- [x] 3.2 Verify: `grep -rn "private static IReadOnlyDictionary<string, TelemetryStore.CommandTelemetrySnapshot> GetCommands" server/src/NebuCtx.Tools/` → no matches

## 4. Full verification

- [x] 4.1 `dotnet build server/NebuCtx.slnx -p:AllowMissingPrunePackageData=true` → 0 errors, 0 warnings
- [x] 4.2 `dotnet test server/NebuCtx.slnx -p:AllowMissingPrunePackageData=true` → all pass, 0 failed
- [x] 4.3 `git status` shows only the in-scope files changed
