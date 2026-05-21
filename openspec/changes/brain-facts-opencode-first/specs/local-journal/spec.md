## ADDED Requirements

### Requirement: Client keeps raw lifecycle journal locally
The client MUST persist raw prompts, assistant turns, tool outcomes, and lifecycle markers in a local journal store and MUST NOT require hosted persistence for those raw events.

#### Scenario: User prompt and assistant turn are captured
- **WHEN** a supported editor lifecycle captures a user turn and the corresponding assistant completion
- **THEN** the client SHALL append those raw events to local journal storage for the active project or session
- **AND** it SHALL not write the raw event bodies directly into hosted brain memory

### Requirement: Local journal feeds fact extraction
The client MUST be able to derive hosted brain fact candidates from local journal and session state without promoting the raw transcript itself.

#### Scenario: Idle flush derives facts from journal
- **WHEN** a lifecycle idle or stop flush runs after journaled activity
- **THEN** the client SHALL evaluate the relevant local journal and session state for promotable fact candidates
- **AND** it SHALL emit only derived fact candidates toward hosted brain ingest

### Requirement: Local journal has bounded retention
The client MUST keep local journal retention bounded so raw lifecycle storage does not grow without limit.

#### Scenario: Old journal sessions exceed retention policy
- **WHEN** local journal data exceeds configured or default retention thresholds
- **THEN** the client SHALL prune or rotate older journal data
- **AND** it SHALL preserve enough recent journal context for active fact extraction and replay-safe flushing
