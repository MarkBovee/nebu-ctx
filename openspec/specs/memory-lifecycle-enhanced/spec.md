# memory-lifecycle-enhanced Specification

## Purpose
TBD - created by archiving change memory-system-enhancements. Update Purpose after archive.
## Requirements
### Requirement: Memory Lifecycle Inspection Command
The system MUST provide a `ctx memory lifecycle` command to inspect memory lifecycle state and promotion readiness.

#### Scenario: Show lifecycle statistics
- **WHEN** a caller invokes `ctx memory lifecycle stats`
- **THEN** the system SHALL return counts by lifecycle status (current, stale, superseded, archived)
- **AND** it SHALL show average confidence, retrieval count, and confirmation count per status
- **AND** it SHALL show total memory count and memory score distribution

#### Scenario: Show promotion candidates
- **WHEN** a caller invokes `ctx memory lifecycle promotions`
- **THEN** the system SHALL return memories that meet auto-promotion thresholds
- **AND** it SHALL include confidence, retrieval count, confirmation count, and lifecycle score
- **AND** it SHALL sort by promotion readiness (highest score first)
- **AND** it SHALL respect a configurable limit (default: 10)

#### Scenario: Show stale memories
- **WHEN** a caller invokes `ctx memory lifecycle stale`
- **THEN** the system SHALL return memories approaching staleness thresholds
- **AND** it SHALL include last accessed/update time and days since last activity
- **AND** it SHALL sort by staleness (oldest first)
- **AND** it SHALL respect a configurable limit (default: 10)

#### Scenario: Show memory scoring details
- **WHEN** a caller invokes `ctx memory lifecycle scoring --key <memory-key>`
- **THEN** the system SHALL return detailed scoring breakdown for the specified memory
- **AND** it SHALL show contributing factors: confidence, retrieval count, confirmation count, age
- **AND** it SHALL show the final lifecycle score calculation
- **AND** it SHALL return an error if the memory key doesn't exist

#### Scenario: Combine lifecycle subcommands with filtering
- **WHEN** a caller invokes `ctx memory lifecycle promotions --category root_cause --limit 5`
- **THEN** the system SHALL apply filters before applying the lifecycle-specific logic
- **AND** it SHALL respect all standard filtering options (category, time, source-type, etc.)
- **AND** it SHALL maintain consistent response format with other lifecycle subcommands

### Requirement: Memory Lifecycle Response Format
The system MUST return memory lifecycle information in a consistent, parseable format.

#### Scenario: Successful lifecycle stats response
- **WHEN** memory lifecycle stats succeeds
- **THEN** the system SHALL return a JSON object with:
  - `status_counts`: object with counts per lifecycle status
  - `averages`: object with average values per status (confidence, retrieval_count, etc.)
  - `total_memories`: integer total memory count
  - `score_distribution`: object showing memory count per score range

#### Scenario: Successful promotion candidates response
- **WHEN** memory lifecycle promotions succeeds
- **THEN** the system SHALL return a JSON object with:
  - `candidates`: array of memory objects meeting promotion criteria
  - `count`: integer count of returned candidates
  - `threshold_used`: float confidence threshold used for auto-promotion
  - `eligible_total`: integer count of all memories meeting threshold (before limit)

#### Scenario: Successful stale memories response
- **WHEN** memory lifecycle stale succeeds
- **THEN** the system SHALL return a JSON object with:
  - `stale_memories`: array of memory objects approaching staleness
  - `count`: integer count of returned stale memories
  - `days_threshold_used`: integer days threshold used for staleness
  - `eligible_total`: integer count of all memories past threshold (before limit)

#### Scenario: Successful memory scoring response
- **WHEN** memory lifecycle scoring succeeds for a specific key
- **THEN** the system SHALL return a JSON object with:
  - `key`: string memory key
  - `current_score`: float current lifecycle score
  - `factors`: object showing contribution of each factor to the score
  - `thresholds`: object showing auto-promotion and review thresholds
  - `status`: string current lifecycle status
  - `recommendation`: string suggested action based on score (promote, review, archive)

### Requirement: Integration with Existing Memory System
The system MUST enhance existing memory lifecycle functionality without breaking changes.

#### Scenario: Lifecycle commands work with existing memories
- **WHEN** existing memories are present in the system
- **THEN** lifecycle commands SHALL operate on them correctly
- **AND** they SHALL not require memory modification or migration
- **AND** they SHALL respect existing lifecycle_status and confidence fields

#### Scenario: Backward compatibility with brain/knowledge tools
- **WHEN** existing `ctx memory` brain and knowledge tools are used
- **THEN** they SHALL continue to work exactly as before
- **AND** lifecycle commands SHALL not alter their behavior or performance

