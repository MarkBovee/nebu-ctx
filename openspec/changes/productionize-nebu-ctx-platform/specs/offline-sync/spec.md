## ADDED Requirements

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
