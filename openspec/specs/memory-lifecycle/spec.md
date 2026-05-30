# memory-lifecycle Specification

## Purpose
Define lifecycle scoring, bounded wake-up selection, upkeep, triage, and temporal maintenance rules for canonical project memory.

## Requirements
### Requirement: Layered wake-up memory selection
The system MUST build startup memory from bounded layers instead of emitting an unbounded list of raw facts.

#### Scenario: Startup wake-up stays within budget
- **WHEN** a session starts for a project with many persisted memories
- **THEN** the system SHALL emit a bounded wake-up snapshot built from the highest-priority startup layers
- **AND** it SHALL obey the startup memory budget defined by that activation path
- **AND** it SHALL avoid loading the full memory corpus into the initial prompt

#### Scenario: Deeper memory is loaded on demand
- **WHEN** a caller asks for a topic-specific or broad historical memory lookup after startup
- **THEN** the system SHALL use deeper recall layers instead of widening the startup snapshot
- **AND** the startup wake-up selection SHALL remain stable unless upkeep recomputes it

#### Scenario: Wake-up selection stays stable between lifecycle changes
- **WHEN** startup memory activation runs multiple times against the same effective canonical memory state
- **THEN** the system SHALL return the same bounded wake-up composition
- **AND** it SHALL not widen or reshuffle the startup snapshot unless promotion, consolidation, supersession, or upkeep changed the effective lifecycle state

### Requirement: Memory lifecycle scoring
Canonical project memory MUST be rescored as the memory set grows so recall and wake-up selection prefer current, reinforced, and recently relevant facts.

#### Scenario: Upkeep rescoring runs
- **WHEN** memory upkeep runs for a project with persisted knowledge entries
- **THEN** the system SHALL recompute lifecycle ranking signals for those entries
- **AND** it SHALL preserve enough metadata to distinguish stronger current facts from stale or weak ones

### Requirement: Memory upkeep recomputation
The system MUST support lifecycle upkeep that refreshes canonical memory ranking and summary state without duplicating or corrupting stored project memory.

#### Scenario: Explicit or scheduled upkeep recomputes lifecycle state
- **WHEN** lifecycle upkeep runs for a project with persisted canonical knowledge
- **THEN** the system SHALL recompute lifecycle ranking and summary state for that project
- **AND** it SHALL preserve canonical identity for unchanged facts

#### Scenario: Upkeep without meaningful changes stays stable
- **WHEN** lifecycle upkeep runs and no promoted or superseding knowledge changed the effective project memory set
- **THEN** the effective wake-up composition SHALL remain stable
- **AND** the system SHALL avoid emitting misleading lifecycle change signals

#### Scenario: Upkeep results are observable
- **WHEN** lifecycle upkeep updates scoring, staleness, supersession state, or wake-up composition
- **THEN** the resulting maintenance state SHALL be visible through project memory inspection

### Requirement: Memory triage analysis and cleanup safety
The system MUST support project-wide memory triage that can identify cleanup candidates while preserving canonical provenance and historical recoverability.

#### Scenario: Triage groups mergeable or duplicate memories
- **WHEN** triage analyzes a project with overlapping or near-duplicate canonical memories
- **THEN** it SHALL group candidate memories that appear mergeable, duplicate, or superseding
- **AND** it SHALL provide the reasoning or signals behind each proposed grouping

#### Scenario: Triage flags likely junk or test memories
- **WHEN** triage analyzes a project that contains likely test, demo, placeholder, or otherwise low-value memories
- **THEN** it SHALL mark those entries as cleanup candidates rather than silently deleting them
- **AND** it SHALL keep those candidates distinguishable from valid historical memory until an explicit apply step confirms the cleanup

#### Scenario: Triage apply preserves auditability
- **WHEN** triage apply changes the effective canonical memory set
- **THEN** the system SHALL preserve enough lifecycle metadata to explain whether each affected memory was merged, superseded, ignored, or removed as junk
- **AND** it SHALL keep the resulting memory state safe for later wake-up selection and dashboard inspection

### Requirement: Temporal memory maintenance
The system MUST preserve superseded facts as historical memory instead of only overwriting the latest value.

#### Scenario: A newer fact supersedes an older fact
- **WHEN** canonical memory promotion stores a new fact that replaces an existing current fact for the same project concept
- **THEN** the older fact SHALL stop being treated as current memory
- **AND** the system SHALL retain enough temporal history to support later timeline or audit-style recall
