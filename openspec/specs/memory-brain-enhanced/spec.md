# memory-brain-enhanced Specification

## Purpose
TBD - created by archiving change memory-system-enhancements. Update Purpose after archive.
## Requirements
### Requirement: Enhanced Brain Tool Listing
The system MUST enhance the brain tool handler to support listing capabilities alongside existing store/recall/forget operations.

#### Scenario: Brain tool supports list action
- **WHEN** a caller invokes the brain tool with action `list`
- **THEN** the system SHALL return brain memories with the same format and filtering options as `ctx memory list`
- **AND** it SHALL respect filtering by category, time range, source type, etc.
- **AND** it SHALL support sorting, limiting, and pagination as specified in memory-browsing
- **AND** it SHALL only return memories classified as brain type (session_timeline, task, finding, decision, etc.)

#### Scenario: Brain list excludes knowledge memories
- **WHEN** listing brain memories
- **THEN** the system SHALL not include memories classified as knowledge type
- **AND** it SHALL maintain the brain vs knowledge distinction
- **AND** it SHALL rely on existing classification mechanisms (category, lifecycle_status, source_type)

### Requirement: Accurate Session Tool Call Tracking
The system MUST fix the inconsistency where session metadata shows 0 tool calls despite actual usage being tracked in analytics.

#### Scenario: Session tool calls increment correctly
- **WHEN** any tool is executed in a session
- **THEN** the session's tool_calls counter SHALL increment by 1
- **AND** it SHALL be reflected in the session metadata returned by `ctx memory status`
- **AND** it SHALL match the count recorded in analytics tools
- **AND** it SHALL persist across session saves and reloads

#### Scenario: Tool call tracking works for all tool types
- **WHEN** tracking tool calls for different tool categories (brain, knowledge, shell, read, etc.)
- **THEN** each tool execution SHALL increment the session tool_calls counter
- **AND** no tool type SHALL be excluded from tracking
- **AND** the tracking SHALL work for both local and server-backed tools

#### Scenario: Session state persistence includes tool calls
- **WHEN** a session is saved to persistent storage
- **THEN** the saved state SHALL include the accurate tool_calls count
- **AND** when the session is reloaded, the tool_calls count SHALL be restored correctly
- **AND** it SHALL not be reset to 0 upon reload

### Requirement: Brain Memory Lifecycle Inspection
The system MUST enhance brain memory inspection to show lifecycle details and promotion readiness.

#### Scenario: Brain memory lifecycle stats
- **WHEN** a caller requests lifecycle stats for brain memories
- **THEN** the system SHALL show statistics specific to brain memory types
- **AND** it SHALL include counts by brain memory category (task, finding, decision, session_event, etc.)
- **AND** it SHALL show average confidence, age, and retrieval rates for brain memories
- **AND** it SHALL identify brain memories that are candidates for promotion to knowledge

#### Scenario: Brain memory promotion readiness
- **WHEN** inspecting brain memories for promotion candidates
- **THEN** the system SHALL apply the same promotion thresholds used for knowledge promotion
- **AND** it SHALL consider brain memories with sufficient confidence, retrieval count, and relevance
- **AND** it SHALL show which brain memories are likely to be promoted during consolidation or upkeep

### Requirement: Backward Compatibility
The system MUST maintain existing brain tool functionality while adding new capabilities.

#### Scenario: Existing brain operations unchanged
- **WHEN** using existing brain tool actions (store, ingest, recall, forget, status)
- **THEN** they SHALL work exactly as before this enhancement
- **AND** new capabilities SHALL not alter the behavior or performance of existing operations
- **AND** all brain memories SHULL remain accessible through both old and new interfaces

#### Scenario: Brain tool action validation
- **WHEN** an invalid action is provided to the brain tool
- **THEN** it SHALL return the same error message as before
- **AND** valid actions (including new list action) SHALL be processed correctly
- **AND** the tool SHALL maintain the same action validation logic

