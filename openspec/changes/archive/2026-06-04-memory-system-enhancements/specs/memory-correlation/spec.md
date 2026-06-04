# memory-correlation Specification

## Purpose
Show traceability links between brain session events and promoted knowledge facts to understand how memories evolve from temporary session data to canonical project knowledge.

## ADDED Requirements

### Requirement: Enhanced Knowledge Recall with Provenance
The system MUST enhance knowledge fact recall to show traceability links to source brain events when available.

#### Scenario: Knowledge fact includes promotion traceability
- **WHEN** a knowledge fact was promoted from a brain session event
- **AND** a caller invokes `ctx memory recall` for that fact's key
- **THEN** the returned memory object SHALL include a `promotion_trace` field
- **AND** the `promotion_trace` SHALL contain:
  - `source_session_id`: string ID of the brain session where the event originated
  - `source_brain_key`: string key of the original brain memory entry
  - `source_brain_value`: string value of the original brain memory entry (truncated)
  - `source_brain_category`: string category of the original brain memory
  - `source_timestamp`: ISO 8601 timestamp when the brain event was created
  - `promotion_action`: string describing what action promoted it (e.g., "manual_promote", "auto_promote", "consolidation")
  - `promotion_timestamp`: ISO 8601 timestamp when it was promoted to knowledge

#### Scenario: Knowledge fact without promotion trace
- **WHEN** a knowledge fact was added directly (not promoted from brain)
- **AND** a caller invokes `ctx memory recall` for that fact's key
- **THEN** the returned memory object SHALL either omit the `promotion_trace` field or set it to null
- **AND** the system SHALL not return an error for missing traceability

#### Scenario: Multiple promotion paths
- **WHEN** a knowledge fact has been updated through multiple promotion events
- **THEN** the `promotion_trace` SHALL show the most recent promotion path
- **AND** the system MAY include a `promotion_history` array with previous promotion events

### Requirement: Brain Event Context in Knowledge Listing
The system MUST include promotion traceability in memory listing results for knowledge facts when available.

#### Scenario: Listing shows promotion trace
- **WHEN** a caller invokes `ctx memory list` 
- **AND** a memory in the results is a knowledge fact with promotion trace
- **THEN** the memory object SHALL include the `promotion_trace` field as described above
- **AND** the inclusion SHALL not significantly increase response size for facts without traceability

### Requirement: Brain-to-Knowledge Impact Analysis
The system MUST provide a way to see what knowledge facts originated from specific brain sessions or events.

#### Scenario: Find knowledge promoted from brain session
- **WHEN** a caller invokes `ctx memory list --promoted-from-session <session-id>`
- **THEN** the system SHALL return knowledge facts that were promoted from the specified brain session
- **AND** it SHALL include the promotion trace for each returned fact
- **AND** it SHALL respect all other filtering and sorting options

#### Scenario: Find knowledge promoted from specific brain event
- **WHEN** a caller invokes `ctx memory list --promoted-from-brain-key <brain-key>`
- **THEN** the system SHALL return knowledge facts that were promoted from the specified brain memory key
- **AND** it SHALL include the promotion trace showing the connection
- **AND** it SHALL respect all other filtering and sorting options

### Requirement: Backward Compatibility
The system MUST maintain existing memory functionality while adding traceability.

#### Scenario: Existing recall unchanged when no trace
- **WHEN** a memory has no promotion traceability
- **AND** a caller invokes `ctx memory recall` for that memory
- **THEN** the response SHALL be identical to before this enhancement
- **AND** no new fields SHALL be present in the response

#### Scenario: Listing performance with traceability
- **WHEN** listing memories with traceability fields
- **THEN** the system SHALL optimize to avoid performance degradation
- **AND** traceability data SHALL be fetched lazily or joined efficiently
- **AND** the system SHALL maintain existing response time characteristics