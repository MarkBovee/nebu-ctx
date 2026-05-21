## MODIFIED Requirements

### Requirement: Startup memory activation
The client MUST inject project memory context at session startup using a bounded brain-backed wake-up selection that prefers canonical hosted brain facts when available and falls back locally when needed.

#### Scenario: Startup with prior hosted brain facts
- **WHEN** a startup `SessionStart` or equivalent OpenCode startup hook fires for a project with hosted brain facts and a healthy server connection
- **THEN** the hook SHALL emit routing guidance
- **AND** it SHALL emit a bounded wake-up snapshot derived from canonical hosted brain facts or their public projection
- **AND** it SHALL avoid loading raw transcript or the full hosted memory set into startup context

#### Scenario: Startup without hosted brain availability
- **WHEN** startup memory activation runs and the server is unavailable or not configured
- **THEN** the client SHALL fall back to local session state, local knowledge fallback, or both
- **AND** it SHALL keep the startup memory snapshot within the configured budget

### Requirement: OpenCode lifecycle memory parity
The OpenCode plugin MUST act as a primary memory lifecycle adapter that uses hosted brain-backed wake-up and continuation selection during startup, compaction, idle persistence, and continuation flows.

#### Scenario: OpenCode session starts with prior memory
- **WHEN** an OpenCode session sends its first model request for a project with stored hosted brain facts or local fallback memory
- **THEN** the plugin SHALL inject routing guidance into the system prompt
- **AND** it SHALL inject a compact memory snapshot derived from hosted brain-backed wake-up selection when available

#### Scenario: OpenCode session compacts
- **WHEN** OpenCode compacts a session
- **THEN** the plugin SHALL inject additional compaction context derived from fresh brain-backed continuation memory
- **AND** the next model turn after compaction SHALL receive a refreshed continuation snapshot rather than stale startup memory

#### Scenario: OpenCode session becomes idle after writes
- **WHEN** an OpenCode session has captured prompts, assistant turns, or tool activity and later becomes idle
- **THEN** the plugin SHALL flush local journal and derived fact extraction through the shared lifecycle path
- **AND** offline writes SHALL continue to rely on the local sync outbox when the server is unavailable

### Requirement: Offline-safe memory writes
Client-driven hosted brain fact writes and related memory projection writes MUST not be silently dropped when the server is unavailable.

#### Scenario: Hosted brain fact batch is queued while offline
- **WHEN** a derived project fact batch should be written to the server and the server is unavailable
- **THEN** the write SHALL be queued in the local sync outbox
- **AND** it SHALL be retried later without creating duplicate active facts solely due to replay

## ADDED Requirements

### Requirement: Shared memory lifecycle core
The client MUST route supported editor and hook integrations through a shared memory lifecycle core instead of embedding separate brain-write semantics in each adapter.

#### Scenario: Different editors trigger the same lifecycle event
- **WHEN** OpenCode, Claude Code, or Copilot trigger equivalent startup, compaction, idle, or stop lifecycle phases
- **THEN** the client SHALL normalize those phases into shared lifecycle events
- **AND** it SHALL use the same journal, fact extraction, and hosted brain ingest logic for each adapter

### Requirement: Public memory retrieval stays brain-backed without contract break
The public `ctx(domain="memory", action=...)` surface MUST keep serving recall and wake-up workflows while its effective canonical source moves to hosted brain facts or their projection.

#### Scenario: Public memory wakeup is requested after brain fact ingest
- **WHEN** a caller requests `ctx(domain="memory", action="wakeup")` after new hosted brain facts have been ingested
- **THEN** the result SHALL reflect the effective active brain-backed project memory
- **AND** callers SHALL not need to change their public tool contract to receive that updated memory
