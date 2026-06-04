# memory-portability Specification

## Purpose
Enable export and import of project memories for knowledge transfer between environments, backup, and sharing.

## ADDED Requirements

### Requirement: Memory Export Command
The system MUST provide a `ctx memory export` command to produce a JSON representation of memories.

#### Scenario: Export all memories
- **WHEN** a caller invokes `ctx memory export`
- **THEN** the system SHALL return a JSON object containing all memories in the project
- **AND** it SHALL include metadata about the export (timestamp, version, project info)
- **AND** it SHALL preserve all memory fields: key, value, category, confidence, lifecycle fields, source info, timestamps, counts
- **AND** it SHALL be valid JSON that can be parsed and re-imported

#### Scenario: Export memories with filtering
- **WHEN** a caller invokes `ctx memory export --category root_cause --since 1m`
- **THEN** the system SHALL return only memories matching the specified filters
- **AND** it SHALL support the same filtering options as `ctx memory list`
- **AND** it SHALL include export metadata indicating filters applied

#### Scenario: Export specific memory types
- **WHEN** a caller invokes `ctx memory export --type brain` or `--type knowledge`
- **THEN** the system SHALL export only memories of the specified type (brain session or knowledge)
- **AND** it SHALL determine type based on source_type, lifecycle_status, or other appropriate markers
- **AND** it SHALL default to exporting both types when not specified

#### Scenario: Export format structure
- **WHEN** memory export succeeds
- **THEN** the system SHALL return a JSON object with:
  - `export_info`: object containing:
    - `timestamp`: ISO 8601 export timestamp
    - `version`: string version of the export format
    - `project_id`: string project identifier (if available)
    - `filters_applied`: object describing any filters used
    - `memory_counts`: object with counts by type (brain, knowledge, total)
  - `memories`: array of memory objects in the same format used by listing
  - `schema_version`: string indicating the memory schema version for compatibility

#### Scenario: Handle large export sets
- **WHEN** exporting a large number of memories
- **THEN** the system SHALL not fail due to response size limits
- **AND** it SHALL allow chunking or streaming if necessary for very large sets
- **AND** it SHALL provide guidance on using limits/filtering for manageable exports

### Requirement: Memory Import Command
The system MUST provide a `ctx memory import` command to load memories from a JSON export.

#### Scenario: Import memories from export
- **WHEN** a caller invokes `ctx memory import` with a valid export JSON
- **THEN** the system SHALL parse the import and add the memories to the project
- **AND** it SHALL respect existing memories (not overwrite unless specified)
- **AND** it SHALL return a summary of what was imported: added, skipped, updated counts
- **AND** it SHALL preserve all memory fields and metadata from the export

#### Scenario: Import with conflict resolution
- **WHEN** importing a memory with a key that already exists in the project
- **AND** the caller specifies `--overwrite`
- **THEN** the system SHALL replace the existing memory with the imported one
- **AND** it SHALL update all fields including timestamps and lifecycle data
- **AND** it SHALL count such actions as "updated" in the import summary

#### Scenario: Import without overwriting (default)
- **WHEN** importing a memory with a key that already exists
- **AND** the caller does NOT specify `--overwrite`
- **THEN** the system SHALL skip the imported memory and keep the existing one
- **AND** it SHALL count such actions as "skipped" in the import summary
- **AND** it SHALL not modify the existing memory in any way

#### Scenario: Import validation and error handling
- **WHEN** the import JSON is invalid or missing required fields
- **THEN** the system SHALL return a clear error message
- **AND** it SHALL not partially import memories on failure
- **AND** it SHALL validate that the export format version is compatible

#### Scenario: Import metadata preservation
- **WHEN** importing memories from a valid export
- **THEN** the system SHALL preserve all original metadata:
  - confidence, lifecycle_score, lifecycle_status
  - retrieval_count, confirmation_count
  - created_at, updated_at timestamps
  - source_type, source_scope, promotion_identity, logical_key
  - any custom fields present in the export

### Requirement: Round-trip Fidelity
The system MUST ensure that export followed by import preserves memory fidelity.

#### Scenario: Export-import roundtrip
- **WHEN** a caller exports memories then imports them back to the same project
- **THEN** the system SHALL ensure that all imported memories match the exported ones
- **AND** it SHALL preserve all fields exactly (except perhaps server-generated timestamps like updated_at on import)
- **AND** it SHALL not create duplicate memories unless the export contained duplicates
- **AND** it SHALL maintain the same lifecycle status and scoring

### Requirement: Integration with Existing Memory System
The system MUST integrate with existing memory storage and tool handlers.

#### Scenario: Imported memories work with existing tools
- **WHEN** memories are imported via `ctx memory import`
- **THEN** they SHALL be immediately available for `ctx memory recall` and listing
- **AND** they SHALL be subject to the same lifecycle processes (upkeep, promotion, etc.)
- **AND** they SHALL appear in analytics and dashboard as native memories

#### Scenario: Export includes all accessible memories
- **WHEN** exporting memories
- **THEN** the export SHALL include all memories accessible through normal memory tools
- **AND** it SHALL not include internal-only or transient caching data
- **AND** it SHALL respect the same access controls and project scoping as normal memory operations