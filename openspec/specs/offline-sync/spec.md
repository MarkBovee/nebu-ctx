# offline-sync Specification

## Purpose
TBD - created by archiving change productionize-nebu-ctx-platform. Update Purpose after archive.
## Requirements
### Requirement: Durable client sync outbox
The client MUST persist retryable sync work locally when the server is unreachable.

#### Scenario: Telemetry send fails
- WHEN the client cannot deliver telemetry to the server
- THEN the telemetry event SHALL be written to a local outbox
- AND it SHALL not be silently dropped

### Requirement: Best-effort outbox drain
The client MUST retry queued sync work during normal runtime.

#### Scenario: Client regains server connectivity
- WHEN the server becomes reachable again
- THEN queued outbox entries SHALL be retried automatically
- AND successful entries SHALL be removed from the outbox

#### Scenario: Mixed outbox replay succeeds
- WHEN telemetry, server tool calls, and code index sync entries are queued while offline
- AND a reachable server later accepts their corresponding API calls
- THEN a flush SHALL replay each operation type
- AND the outbox SHALL be empty after successful replay

### Requirement: Ordered replay metadata
The sync outbox MUST retain enough metadata to support retries and inspection.

#### Scenario: Outbox entry persists after a failed retry
- WHEN an outbox entry fails during replay
- THEN the client SHALL record the attempt count
- AND it SHALL keep the entry available for future retries

### Requirement: Code index replay
The client MUST queue code index sync payloads when the server cannot accept them immediately.

#### Scenario: Code index sync cannot reach the server
- WHEN a project code index is built
- AND the server is unavailable or not configured
- THEN the index sync payload SHALL be stored in the local outbox
- AND it SHALL be replayable by the normal outbox drain path

