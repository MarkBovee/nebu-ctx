# dashboard Specification

## Purpose
TBD - created by archiving change productionize-nebu-ctx-platform. Update Purpose after archive.
## Requirements
### Requirement: Consolidated dashboard overview
The server MUST expose a single dashboard overview endpoint that aggregates the key overview data needed by the UI.

#### Scenario: Overview loads with one primary request
- WHEN the dashboard overview view is opened
- THEN the UI SHALL be able to fetch the primary overview payload from `/api/dashboard/overview`
- AND the payload SHALL include version information
- AND the payload SHALL include aggregated stats information
- AND the payload SHALL include gain information

### Requirement: Typed dashboard overview contracts
The consolidated dashboard overview endpoint MUST use concrete response models for version, stats, and gain payloads.

#### Scenario: Overview payload is deserialized by generated clients
- WHEN a generated or strongly typed client reads `/api/dashboard/overview`
- THEN the version, stats, and gain properties SHALL deserialize into concrete dashboard contract types
- AND the legacy JSON property names required by the current dashboard UI SHALL remain available

### Requirement: Dashboard domain consolidation
The dashboard MUST expose a typed domain map that groups detailed panels into fewer operator areas without removing the existing panel identifiers.

#### Scenario: Domain map is requested
- WHEN a caller requests `/api/dashboard/domains`
- THEN the server SHALL return domain groups in display order
- AND each domain SHALL include stable view identifiers for its detailed panels
- AND the map SHALL include overview, memory, and agents areas

### Requirement: Project memory inspection
The server MUST expose per-project memory data for dashboard and admin workflows, including lifecycle health, wake-up composition, and maintenance visibility for larger memory sets.

#### Scenario: Project memory is requested
- **WHEN** a caller requests `/api/dashboard/projects/{projectId}/memory`
- **THEN** the server SHALL return the selected project identifier and name
- **AND** the response SHALL include persisted knowledge entries for that project
- **AND** the response SHALL include persisted brain entries for that project
- **AND** it SHALL include memory health signals needed to understand current memory density and upkeep state
- **AND** it SHALL include enough summary data to show which memory layers or wake-up segments are currently active for that project

#### Scenario: Memory health reflects upkeep state
- **WHEN** project memory inspection is requested after lifecycle upkeep has run
- **THEN** the response SHALL expose enough summary data to distinguish current high-priority memory from stale or superseded memory
- **AND** it SHALL expose the latest known maintenance or summary refresh state for that project

#### Scenario: Project memory includes candidate review data
- **WHEN** a caller requests `/api/dashboard/projects/{projectId}/memory` for a project that has persisted memory candidates
- **THEN** the response SHALL include bounded candidate review data for that project
- **AND** each candidate entry SHALL expose review or promotion status, confidence, classification, and supporting evidence metadata

#### Scenario: Project memory includes promotion outcomes
- **WHEN** a project has auto-promoted or manually accepted durable memory candidates
- **THEN** the response SHALL expose promotion outcome summaries that distinguish review-queue items from canonical knowledge facts

#### Scenario: Dashboard shows lifecycle changes
- **WHEN** scoring, supersession, or wake-up recomputation changes the effective project memory set
- **THEN** the dashboard SHALL expose lifecycle-oriented indicators that make those changes visible to an operator
- **AND** it SHALL allow an operator to tell whether memory behavior changed because of promotion, upkeep, or temporal supersession

#### Scenario: Dashboard shows triage findings
- **WHEN** hosted memory triage has been previewed or applied for a project
- **THEN** the dashboard SHALL expose triage-oriented summary data for duplicate groups, stale or junk candidates, and applied cleanup actions
- **AND** it SHALL allow an operator to distinguish preview-only triage findings from already applied triage changes

### Requirement: Project memory admin routes
The server MUST expose admin operations for project memory management.

#### Scenario: Brain memory entry is deleted
- **WHEN** a caller deletes `/api/dashboard/projects/{projectId}/memory/brain/{key}`
- **THEN** the matching brain entry SHALL be removed when it exists

#### Scenario: Knowledge fact is deleted
- **WHEN** a caller deletes `/api/dashboard/projects/{projectId}/memory/knowledge/{category}/{key}`
- **THEN** the matching knowledge fact SHALL be removed when it exists

#### Scenario: Candidate review decision is applied
- **WHEN** a caller submits an admin review decision for a queued project-memory candidate
- **THEN** the server SHALL mark the candidate with its resulting review status
- **AND** accepting the candidate SHALL promote it into canonical project knowledge
- **AND** rejecting the candidate SHALL keep it out of canonical project knowledge

### Requirement: Dashboard shows brain lifecycle state
The dashboard MUST present brain memory as operator-meaningful facts with enough lifecycle context to understand current, superseded, and invalidated project memory.

#### Scenario: Operator inspects brain memory
- **WHEN** an operator opens the dashboard memory view for a project
- **THEN** the brain section SHALL distinguish active facts from superseded or invalidated facts
- **AND** it SHALL expose provenance or lifecycle context needed to understand why those facts are active or historical

### Requirement: Dashboard shows wake-up composition
The dashboard MUST expose how effective wake-up memory is composed from current project memory.

#### Scenario: Operator reviews wake-up composition
- **WHEN** an operator inspects project memory health or wake-up state
- **THEN** the dashboard SHALL show which current memory items contribute to wake-up or continuation context
- **AND** it SHALL avoid presenting raw transcript lines as equivalent to canonical memory facts
