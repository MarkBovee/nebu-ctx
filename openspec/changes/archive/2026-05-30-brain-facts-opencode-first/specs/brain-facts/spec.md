## ADDED Requirements

### Requirement: Hosted brain stores canonical facts only
The server MUST store only derived, typed project facts in hosted brain memory and MUST NOT treat raw transcript or session-log strings as canonical brain entries.

#### Scenario: Client ingests derived fact batch
- **WHEN** the client submits a hosted brain ingest request containing derived fact candidates for a project
- **THEN** the server SHALL persist the accepted entries as typed brain facts with canonical identity and lifecycle metadata
- **AND** it SHALL not require raw prompt or assistant transcript payloads to do so

#### Scenario: Raw transcript is not a valid hosted brain write
- **WHEN** a caller attempts to store a raw prompt, assistant output, or plain session-summary string as hosted brain memory
- **THEN** the hosted brain path SHALL reject or ignore that payload as non-canonical brain data

### Requirement: Hosted brain facts carry temporal and provenance metadata
Each hosted brain fact MUST include enough metadata to support recall, supersession, invalidation, and operator inspection.

#### Scenario: Stored brain fact is inspected
- **WHEN** a caller retrieves or inspects a hosted brain fact
- **THEN** the fact SHALL expose its kind, logical identity, confidence, source metadata, lifecycle state, and timestamps needed for temporal reasoning

### Requirement: Hosted brain canonicalization is idempotent
The server MUST canonicalize repeated brain fact ingest so offline replay, repeated lifecycle flushes, or duplicate event batches do not create divergent canonical facts.

#### Scenario: Same fact batch replays after offline period
- **WHEN** the same derived fact batch is delivered to hosted brain more than once for the same project
- **THEN** the server SHALL preserve a stable canonical identity for the matching facts
- **AND** it SHALL not create duplicate active facts solely because the batch replayed

### Requirement: Hosted brain supports supersession and invalidation
The hosted brain MUST allow newer facts to supersede or invalidate older active facts without destroying historical state.

#### Scenario: New fact corrects prior fact
- **WHEN** a new derived fact for the same logical subject contradicts or replaces an existing active brain fact
- **THEN** the server SHALL mark the older fact as superseded or invalidated
- **AND** it SHALL preserve enough history for recall and dashboard inspection
