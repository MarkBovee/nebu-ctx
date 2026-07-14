## ADDED Requirements

### Requirement: Deterministic operation identity on every write
Every client-hosted brain, knowledge, session, or telemetry write MUST carry a deterministic `operation_id` sufficient for the server to distinguish a new operation, a retry, a duplicate, and a stale update — even after client restart.

#### Scenario: Same operation is retried after restart
- **WHEN** a network failure causes the client to retry the same logical memory operation after a restart (new process, new request ID)
- **THEN** the server SHALL recognize the deterministic `operation_id`
- **AND** it SHALL return a duplicate acknowledgement instead of creating a second row

### Requirement: Server returns typed write outcomes
Server write handlers MUST return `{ status, operation_id }` where status is one of `accepted`, `duplicate`, `stale`, or `rejected`.

#### Scenario: Operation succeeds
- **WHEN** the server accepts a new memory write
- **THEN** the response SHALL include `status: "accepted"` and the matching `operation_id`

#### Scenario: Operation is a duplicate
- **WHEN** the server receives an `operation_id` it has already processed
- **THEN** the response SHALL include `status: "duplicate"` and the matching `operation_id`
- **AND** the server SHALL NOT create a second row or change the existing row

### Requirement: Sync health endpoint
The server MUST expose a typed sync-health endpoint returning aggregated counts and timestamps for accepted, duplicate, stale, pending, and failed operations.

#### Scenario: Operator checks sync health
- **WHEN** an authorized operator requests memory sync health
- **THEN** the response SHALL include pending count, failed count, accepted count, duplicate count, stale count, last successful sync timestamp, last failure timestamp, and latest session snapshot time
- **AND** the response SHALL NOT include bearer tokens, raw prompts, assistant transcripts, or full outbox payloads

### Requirement: Outbox clears only on accept or duplicate
The client outbox MUST retain an entry until it receives an `accepted` or `duplicate` acknowledgement; `stale` and transient-failure entries remain retryable.

#### Scenario: Stale operation is not cleared
- **WHEN** the server returns `stale` for an outbox operation
- **THEN** the client SHALL retain the entry with updated metadata
- **AND** it SHALL NOT retry the stale operation unless the update marker changes
