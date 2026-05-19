## ADDED Requirements

### Requirement: Idempotent promoted-memory replay
The client and server MUST treat replayed promoted-memory batches as idempotent operations.

#### Scenario: Promote batch replays more than once
- **WHEN** the sync outbox replays the same promoted memory batch multiple times after a transient failure
- **THEN** canonical project knowledge SHALL converge to a single logical fact per promoted item
- **AND** replay SHALL not create duplicated memory entries for the same promoted source

#### Scenario: Failed promote replay remains safe to retry
- **WHEN** a promoted memory batch fails partway through hosted replay
- **THEN** the outbox SHALL keep the batch available for retry
- **AND** a later retry SHALL remain safe because the hosted ingest path is idempotent
