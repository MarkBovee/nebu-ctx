## MODIFIED Requirements

### Requirement: Durable client sync outbox
The client MUST persist retryable hosted brain fact ingest work locally when the server is unreachable.

#### Scenario: Hosted brain fact ingest fails
- **WHEN** the client cannot deliver a derived brain fact batch to the server
- **THEN** the hosted brain ingest request SHALL be written to the local outbox
- **AND** it SHALL not be silently dropped

### Requirement: Best-effort outbox drain
The client MUST retry queued hosted brain fact ingest work during normal runtime.

#### Scenario: Client regains server connectivity after brain fact queueing
- **WHEN** the server becomes reachable again after hosted brain fact batches were queued
- **THEN** queued hosted brain fact operations SHALL be retried automatically
- **AND** successful entries SHALL be removed from the outbox

#### Scenario: Mixed outbox replay succeeds with brain fact batches
- **WHEN** telemetry, hosted brain fact batches, server tool calls, and code index sync entries are queued while offline
- **AND** a reachable server later accepts their corresponding API calls
- **THEN** a flush SHALL replay each operation type
- **AND** the outbox SHALL be empty after successful replay

### Requirement: Ordered replay metadata
The sync outbox MUST retain enough metadata to support idempotent retries for hosted brain fact ingest.

#### Scenario: Brain fact outbox entry persists after a failed retry
- **WHEN** a queued hosted brain fact ingest entry fails during replay
- **THEN** the client SHALL record the attempt count
- **AND** it SHALL keep the entry available for future retries with the deterministic metadata needed for idempotent canonicalization
