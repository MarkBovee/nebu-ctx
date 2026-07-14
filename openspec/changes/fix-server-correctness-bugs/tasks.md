## 1. Dispose count-query commands

- [x] 1.1 Wrap `countCmd` in `await using` in `PostgresBrainStore.ListFilteredAsync`
- [x] 1.2 Wrap `countCmd` in `await using` in `PostgresKnowledgeStore.ListFilteredAsync`
- [x] 1.3 Verify: `grep -n "var countCmd = new NpgsqlCommand" server/src/NebuCtx.Storage/Postgres/*.cs` → no matches

## 2. Observe fire-and-forget telemetry persist failures

- [x] 2.1 Add optional `ILogger<TelemetryStore>? logger = null` constructor parameter and `_logger` field to `TelemetryStore`
- [x] 2.2 Replace both `_ = Task.Run(() => callback(...));` sites (in `RecordToolCall` and `IngestEvent`) with a version that adds `.ContinueWith(t => _logger?.LogWarning(t.Exception, ...), CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default)`
- [x] 2.3 Add `RecordToolCall_PersistCallbackThrows_DoesNotThrowOrCrash` test to `TelemetryStoreTests.cs`
- [x] 2.4 Verify: `grep -n "_ = Task.Run(() => callback" server/src/NebuCtx.Server.Core/TelemetryStore.cs` → no matches

## 3. Propagate cancellation token in bulk import

- [x] 3.1 Change the final `RememberAsync(...)` call in `KnowledgeToolHandler.ExecuteImportAsync` from `CancellationToken.None` to `cancellationToken`
- [x] 3.2 Verify: `grep -n "RememberAsync.*CancellationToken.None" server/src/NebuCtx.Tools/Knowledge/KnowledgeToolHandler.cs` → no matches

## 4. Full verification

- [x] 4.1 `dotnet build server/NebuCtx.slnx -p:AllowMissingPrunePackageData=true` → 0 errors, 0 warnings
- [x] 4.2 `dotnet test server/NebuCtx.slnx -p:AllowMissingPrunePackageData=true --filter "FullyQualifiedName~TelemetryStoreTests"` → all pass, including the new test
- [x] 4.3 `dotnet test server/NebuCtx.slnx -p:AllowMissingPrunePackageData=true` (full suite) → all pass
- [x] 4.4 `git status` shows only the in-scope files changed
