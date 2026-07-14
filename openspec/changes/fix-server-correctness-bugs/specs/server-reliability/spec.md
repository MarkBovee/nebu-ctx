## ADDED Requirements

### Requirement: Paginated list queries dispose their database commands
The server MUST dispose every `NpgsqlCommand` it creates when listing brain or knowledge entries, including the count query used for pagination totals.

#### Scenario: Brain entries are listed with a filter
- **WHEN** `PostgresBrainStore.ListFilteredAsync` executes its count and data queries
- **THEN** both `NpgsqlCommand` instances SHALL be disposed via `await using` before the method returns

#### Scenario: Knowledge entries are listed with a filter
- **WHEN** `PostgresKnowledgeStore.ListFilteredAsync` executes its count and data queries
- **THEN** both `NpgsqlCommand` instances SHALL be disposed via `await using` before the method returns

### Requirement: Fire-and-forget telemetry persistence observes and logs faults
The server MUST log a warning when a fire-and-forget telemetry persist operation faults, without blocking or delaying the caller that triggered it.

#### Scenario: Persist callback throws during a tool-call recording
- **WHEN** `TelemetryStore.RecordToolCall` fires its persist callback and that callback throws
- **THEN** the calling method SHALL NOT throw or block waiting for the persist result
- **AND** the fault SHALL be logged as a warning

#### Scenario: Persist callback throws during event ingestion
- **WHEN** `TelemetryStore.IngestEvent` fires its persist callback and that callback throws
- **THEN** the fault SHALL be logged as a warning without affecting the caller

### Requirement: Bulk knowledge import respects cancellation for every write
The server MUST propagate the caller's cancellation token to every write performed during a bulk knowledge import, including the final remember/write call for each item.

#### Scenario: Client cancels a bulk import mid-loop
- **WHEN** a bulk knowledge import request is cancelled while processing an item
- **THEN** the in-flight `RememberAsync` write for that item SHALL observe the cancellation token
- **AND** the import loop SHALL NOT continue writing further items after cancellation is observed
