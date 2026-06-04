# memory-browsing Specification

## Purpose
TBD - created by archiving change memory-system-enhancements. Update Purpose after archive.
## Requirements
### Requirement: Memory Listing Command
The system MUST provide a `ctx memory list` command to browse memories with filtering options.

#### Scenario: List all memories with default limit
- **WHEN** a caller invokes `ctx memory list`
- **THEN** the system SHALL return memories sorted by relevance/recency
- **AND** it SHALL limit results to a configurable default (e.g., 20 entries)
- **AND** it SHALL include key metadata: key, category, confidence, source_type, created_at

#### Scenario: Filter memories by category
- **WHEN** a caller invokes `ctx memory list --category root_cause`
- **THEN** the system SHALL return only memories matching the specified category
- **AND** it SHALL respect sorting and limiting as with unfiltered lists

#### Scenario: Filter memories by time range
- **WHEN** a caller invokes `ctx memory list --since 7d`
- **THEN** the system SHALL return memories created within the last 7 days
- **AND** it SHALL support standard time suffixes (h, d, w, m, y)
- **AND** it SHALL treat the filter as inclusive of the start boundary

#### Scenario: Filter memories by source type
- **WHEN** a caller invokes `ctx memory list --source-type tool_activity`
- **THEN** the system SHALL return only memories with the specified source type
- **AND** it SHALL combine with other filters when multiple are specified

#### Scenario: Limit and pagination
- **WHEN** a caller invokes `ctx memory list --limit 5 --offset 10`
- **THEN** the system SHALL return 5 memories starting from the 11th entry
- **AND** it SHALL respect filtering when combined with limit/offset
- **AND** it SHALL validate that limit and offset are non-negative integers

#### Scenario: Sort memories by different criteria
- **WHEN** a caller invokes `ctx memory list --sort created:desc`
- **THEN** the system SHALL sort memories by creation date descending
- **AND** it SHALL support sort fields: created, updated, confidence, retrieval_count
- **AND** it SHALL support ascending (asc) and descending (desc) directions
- **AND** it SHALL default to relevance-based sorting when no sort specified

#### Scenario: Handle empty results gracefully
- **WHEN** a caller invokes `ctx memory list` with filters that match no memories
- **THEN** the system SHALL return an empty list with count: 0
- **AND** it SHALL not return an error for valid filter combinations yielding no results

### Requirement: Memory Listing Response Format
The system MUST return memory listings in a consistent, parseable format.

#### Scenario: Successful list response structure
- **WHEN** memory listing succeeds
- **THEN** the system SHALL return a JSON object with:
  - `memories`: array of memory objects
  - `count`: integer count of returned memories
  - `total`: integer count of total matching memories (before limit/offset)
  - `filters_applied`: object describing active filters
  - `sort_applied`: object describing applied sort criteria

#### Scenario: Memory object structure
- **WHEN** returning a memory in a list
- **THEN** each memory object SHALL contain:
  - `key`: string memory key
  - `value`: string memory value (truncated if overly long in list view)
  - `category`: string memory category
  - `confidence`: float confidence score (0.0-1.0)
  - `source_type`: string indicating how memory was created
  - `source_scope`: string indicating scope of origin
  - `created_at`: ISO 8601 timestamp
  - `updated_at`: ISO 8601 timestamp
  - `retrieval_count`: integer times memory has been recalled
  - `confirmation_count`: integer times memory has been confirmed/retrieved with positive feedback
  - `lifecycle_score`: float composite score for lifecycle ranking
  - `lifecycle_status`: string (current, stale, superseded, archived)

### Requirement: Backward Compatibility with Recall
The system MUST maintain existing recall functionality while adding listing capabilities.

#### Scenario: Recall unchanged by listing addition
- **WHEN** existing `ctx memory recall` functionality is used
- **THEN** it SHALL continue to work exactly as before
- **AND** listing capabilities SHALL not alter recall behavior or performance
- **AND** both interfaces SHALL operate on the same underlying memory store

