## Why

Three independent, unrelated correctness issues in the .NET server, bundled because each is small and none depends on the others: (1) `PostgresBrainStore.ListFilteredAsync` and `PostgresKnowledgeStore.ListFilteredAsync` both create a `countCmd` without `await using`, leaking the command object on every paginated list call (a hot dashboard path); (2) `TelemetryStore.RecordToolCall`/`IngestEvent` fire-and-forget persist telemetry with no exception observation, so a persist failure is invisible with zero diagnostic trail; (3) `KnowledgeToolHandler.ExecuteImportAsync` hardcodes `CancellationToken.None` on its final `RememberAsync` call, so a client-cancelled bulk import cannot actually stop write operations partway through.

## What Changes

- Wrap both `countCmd` declarations in `await using`, matching the sibling `cmd` a few lines below in the same methods.
- Add an optional `ILogger<TelemetryStore>?` constructor parameter to `TelemetryStore` and observe the fire-and-forget persist `Task` via `ContinueWith(..., TaskContinuationOptions.OnlyOnFaulted, ...)`, logging a warning on fault.
- Propagate the method's own `cancellationToken` to the final `RememberAsync` call in `KnowledgeToolHandler.ExecuteImportAsync` instead of hardcoding `CancellationToken.None`.
- **Not BREAKING**: the optional logger parameter keeps `new TelemetryStore()` (used by existing tests) compiling unchanged; the fire-and-forget design itself is preserved, only the missing exception observation is added.

## Capabilities

### New Capabilities
- `server-reliability`: defines that server-side resource usage (database commands), asynchronous fire-and-forget work (telemetry persistence), and cancellation propagation (bulk import) behave correctly — no leaked resources, no silently swallowed exceptions, and cancellation actually stops in-flight write loops.

### Modified Capabilities

## Impact

- **Code**: `server/src/NebuCtx.Storage/Postgres/PostgresBrainStore.cs`, `server/src/NebuCtx.Storage/Postgres/PostgresKnowledgeStore.cs`, `server/src/NebuCtx.Server.Core/TelemetryStore.cs`, `server/src/NebuCtx.Tools/Knowledge/KnowledgeToolHandler.cs`.
- **Tests**: new test in `server/tests/NebuCtx.IntegrationTests/TelemetryStoreTests.cs` proving `RecordToolCall` never throws even when the persist callback always fails.
- **Explicitly excluded**: a fourth originally-reported finding ("log level too low in `TelemetryHydrationService.cs`") was investigated and found to already use `LogWarning` with an inline comment documenting the choice as deliberate — not included as a fix here.
- Full technical detail (exact current code, line numbers, and exact diffs) already captured in `plans/005-fix-server-correctness-bugs.md` — this proposal is the OpenSpec-tracked counterpart of that plan.
