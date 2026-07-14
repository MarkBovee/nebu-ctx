## ADDED Requirements

### Requirement: Versioned session envelope
Every newly written hosted session snapshot MUST use a versioned envelope containing schema version, project identity, session id, update timestamp, source client version, and the structured state payload.

#### Scenario: Current snapshot is written
- **WHEN** a client writes a new-format session snapshot
- **THEN** the server SHALL validate the required envelope metadata
- **AND** it SHALL persist the current schema version
- **AND** a later reader SHALL be able to select the correct decoder from that version

#### Scenario: Legacy snapshot is read
- **WHEN** an existing snapshot has no schema version but matches a supported legacy shape
- **THEN** the server SHALL read it through a compatibility decoder
- **AND** the next successful write SHALL emit the current versioned envelope

### Requirement: Idempotent and ordered session updates
Session snapshot writes MUST be idempotent per operation identity and MUST NOT allow an older client update to overwrite a newer accepted snapshot.

#### Scenario: Snapshot is retried
- **WHEN** the same snapshot operation (matching operation_id) is delivered more than once
- **THEN** the server SHALL converge to one accepted snapshot state
- **AND** the client SHALL receive an accepted or duplicate acknowledgement

#### Scenario: Stale snapshot arrives
- **WHEN** a snapshot arrives with an update marker older than the latest accepted snapshot
- **THEN** the server SHALL preserve the latest accepted state
- **AND** it SHALL return a stale result visible to the client outbox

### Requirement: Session restore after restart
The public memory/session lifecycle MUST be able to load the latest hosted snapshot after server restart, while preserving local fallback when the server is unavailable.

#### Scenario: Hosted session is restored
- **WHEN** a session-start or resume flow requests a project session whose snapshot was persisted before restart
- **THEN** the server SHALL return the latest accepted structured snapshot
- **AND** the client SHALL use it to rebuild working context within the configured startup budget

#### Scenario: Hosted restore is unavailable
- **WHEN** the server cannot be reached during session startup
- **THEN** the client SHALL use its local session state and outbox fallback
- **AND** the failed hosted restore SHALL remain diagnosable without deleting local state
