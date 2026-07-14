## Context

Three independent server-side correctness bugs surfaced during audit: an undisposed `NpgsqlCommand` in two hot-path list queries, unobserved fire-and-forget telemetry-persist exceptions, and a hardcoded `CancellationToken.None` overriding an otherwise-correct cancellation chain in bulk knowledge import.

## Goals / Non-Goals

**Goals:**
- Dispose every database command consistently with the sibling command in the same method.
- Make telemetry persist failures observable via logging without changing the fire-and-forget design.
- Make bulk import actually stoppable mid-loop when the caller cancels.

**Non-Goals:**
- Changing the fire-and-forget *design* of telemetry persistence (e.g. switching to a bounded channel/queue) — intentional, out of scope.
- Fixing `TelemetryHydrationService.cs`'s log level — investigated and found to already be a deliberate, documented choice (`LogWarning` with an explicit inline comment); not a real gap.
- Adding live-Postgres-dependent tests for the `NpgsqlCommand` disposal fix — the repo's test suite runs without live Postgres by design; verification here is build-level (the `await using` keyword is present) plus the existing no-DB test suite passing.

## Decisions

- **Optional `ILogger<TelemetryStore>?` constructor parameter, not required.** `TelemetryStore` is registered via plain `services.AddSingleton<TelemetryStore>()`, and an existing test constructs it via `new TelemetryStore()`. An optional parameter with a `null` default keeps that call site compiling unchanged while DI auto-resolves the logger in production, matching this repo's documented "Constructor Optimization" convention.
- **`ContinueWith(..., TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default)`, not `await`.** Awaiting the persist task would reintroduce the blocking behavior the fire-and-forget design deliberately avoids. `TaskScheduler.Default` is used explicitly so the logging continuation never runs on a captured synchronization context.
- **Single-argument fix for cancellation**, not a broader refactor of `ExecuteImportAsync` — the surrounding calls (`GetFactAsync`, `RemoveAsync`) already pass the token correctly; only the one hardcoded override needed to change.

## Risks / Trade-offs

- [Risk] A third undisposed `NpgsqlCommand` might exist elsewhere in `NebuCtx.Storage/Postgres/` beyond the two found. → Mitigation: full grep performed during planning found exactly 2; STOP condition requires reporting any additional instance found during implementation rather than silently fixing or ignoring it.
- [Risk] Adding a constructor parameter to `TelemetryStore` could break an undiscovered second call site. → Mitigation: grep confirmed exactly one call site (`TelemetryStoreTests.CreateStore()`); STOP condition requires re-confirming this before starting.
