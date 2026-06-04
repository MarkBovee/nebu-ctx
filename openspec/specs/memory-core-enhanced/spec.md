# memory-core-enhanced Specification

## Purpose
TBD - created by archiving change memory-system-enhancements. Update Purpose after archive.
## Requirements
### Requirement: Accurate Session Tool Call Tracking
The system MUST fix the inconsistency where session metadata shows 0 tool calls despite actual usage being tracked in analytics, affecting both brain and knowledge systems.

#### Scenario: Session tool calls increment correctly for all tools
- **WHEN** any tool is executed in a session (brain, knowledge, shell, read, etc.)
- **THEN** the session's tool_calls counter SHALL increment by 1
- **AND** it SHALL be reflected in the session metadata returned by `ctx memory status`
- **AND** it SHALL match the count recorded in analytics tools (`ctx_gain`, `ctx_cost`, etc.)
- **AND** it SHALL persist across session saves and reloads
- **AND** it SHALL work for both local execution and server-backed tool calls

#### Scenario: Tool call tracking works across tool categories
- **WHEN** tracking tool calls for different tool categories
- **THEN** each tool execution SHALL increment the session tool_calls counter
- **AND** no tool type SHALL be excluded from tracking (brain, knowledge, shell, read, search, etc.)
- **AND** the tracking SHALL work for tools executed via ctx meta-tool and direct tool tools
- **AND** it SHALL correctly track tools executed through hooks and automation

#### Scenario: Session state persistence includes tool calls
- **WHEN** a session is saved to persistent storage (via stop, compaction, or manual save)
- **THEN** the saved state SHALL include the accurate tool_calls count
- **AND** when the session is reloaded, the tool_calls count SHALL be restored correctly
- **AND** it SHALL not be reset to 0 upon reload or resume
- **AND** it SHALL survive client restarts and reconnects

### Requirement: Contextual Memory Surfacing via Hooks
The system MUST enhance the hook system to suggest relevant memories during related tool use, improving passive memory recall without explicit user queries.

#### Scenario: Hook-triggered memory suggestion
- **WHEN** a tool is executed that relates to past memories (based on content analysis)
- **AND** the hook system detects contextual relevance
- **THEN** the system SHALL emit a non-intrusive suggestion to recall relevant memories
- **AND** the suggestion SHALL include memory keys and brief previews
- **AND** it SHALL respect user preferences for suggestion frequency and relevance thresholds

#### Scenario: Contextual relevance determination
- **WHEN** analyzing tool execution for contextual memory relevance
- **THEN** the system SHALL consider:
  - Tool type and command patterns (e.g., shell commands related to past issues)
  - File paths being accessed (related to past bug locations)
  - Current task or session context
  - Keywords in prompts or tool parameters
  - Temporal relevance (recent memories weighted higher)
- **AND** it SHALL use scoring algorithms to rank memory relevance
- **AND** it SHALL only suggest memories above a configurable relevance threshold

#### Scenario: Suggestion delivery mechanisms
- **WHEN** a contextual memory suggestion is triggered
- **THEN** the system SHALL deliver suggestions through appropriate channels:
  - Inline suggestions in terminal output (non-blocking)
  - Optional notification systems (desktop notifications if enabled)
  - Dashboard indicators or badges
  - Optional audio cues (if configured)
- **AND** it SHALL allow users to configure suggestion delivery preferences
- **AND** it SHALL provide opt-out mechanisms for users who prefer manual recall

#### Scenario: Suggestion content and actionability
- **WHEN** presenting a contextual memory suggestion
- **THEN** the suggestion SHALL include:
  - Clear indication it's a memory suggestion (not a command)
  - Memory key and category for identification
  - Brief preview of the memory value (truncated appropriately)
  - Relevance score or reason for the suggestion
  - Easy action to recall the full memory (suggested command)
- **AND** it SHALL make it trivial for users to act on the suggestion
- **AND** it SHALL track whether suggestions are acted upon for relevance feedback

#### Scenario: Privacy and performance considerations
- **WHEN** implementing contextual memory surfacing
- **THEN** the system SHALL not send memory content externally for analysis
- **AND** analysis SHALL happen locally or within the trusted server boundary
- **AND** it SHALL minimize performance impact on tool execution
- **AND** it SHALL cache relevance computations where appropriate
- **AND** it SHALL respect any existing privacy or data handling policies

### Requirement: Hook System Integration for Memory Lifecycle
The system MUST enhance hook execution to trigger memory lifecycle events at appropriate times.

#### Scenario: Post-tool-use memory reinforcement
- **WHEN** a tool is executed successfully
- **AND** the post-tool-use hook runs
- **THEN** the system SHALL consider reinforcing related memories
- **AND** it SHALL increase retrieval_count for memories used in the tool context
- **AND** it SHALL potentially increase confidence for consistently validated memories
- **AND** it SHALL respect daily limits to prevent gaming the system

#### Scenario: Pre-compact memory snapshot
- **WHEN** the pre-compact hook runs
- **THEN** the system SHALL create a enhanced session snapshot
- **AND** it SHALL include particularly relevant or recently used memories
- **AND** it SHALL weight memories by their relevance to the current session context
- **AND** it SHALL respect the startup memory budget constraints

#### Scenario: User prompt submit memory context
- **WHEN** the user-prompt-submit hook runs
- **THEN** the system SHALL provide relevant memory context to the prompt
- **AND** it SHALL surface memories related to the incoming prompt
- **AND** it SHALL follow the same relevance rules as contextual surfacing
- **AND** it SHALL respect prompt context size limits

### Requirement: Memory System Health and Monitoring
The system MUST provide insights into memory system health and usage patterns.

#### Scenario: Memory system health metrics
- **WHEN** querying memory system health
- **THEN** the system SHALL provide:
  - Overall memory counts and growth rates
  - Usage statistics (recalls per day, additions per day)
  - Health ratios (confidence distribution, lifecycle status distribution)
  - Performance metrics (average recall time, storage efficiency)
- **AND** it SHALL help identify potential issues like memory bloat or stale data accumulation

#### Scenario: Memory usage analytics integration
- **WHEN** using analytics tools like `ctx_gain report`
- **THEN** the system SHALL show memory-specific usage patterns
- **AND** it SHALL break down memory tool usage by action type (store, recall, list, etc.)
- **AND** it SHALL show which memory categories are most actively used
- **AND** it SHALL correlate memory usage with overall productivity metrics

### Requirement: Backward Compatibility
The system MUST maintain existing core memory functionality while adding new capabilities.

#### Scenario: Existing core memory operations unchanged
- **WHEN** using existing core memory functionality (session start/stop, basic recall/store)
- **THEN** they SHALL work exactly as before this enhancement
- **AND** new capabilities SHALL not alter the behavior or performance of existing operations
- **AND** all existing memory interfaces SHALL remain functional

#### Scenario: Configuration backward compatibility
- **WHEN** upgrading to this enhanced version
- **THEN** existing configurations SHALL continue to work
- **AND** new features SHALL have sensible defaults
- **AND** users SHALL not be required to change their setup to gain basic functionality

