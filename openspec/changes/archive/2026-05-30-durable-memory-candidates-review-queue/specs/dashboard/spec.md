## MODIFIED Requirements

### Requirement: Project memory inspection
The server MUST expose per-project memory data for dashboard and admin workflows.

#### Scenario: Project memory is requested
- **WHEN** a caller requests `/api/dashboard/projects/{projectId}/memory`
- **THEN** the server SHALL return the selected project identifier and name
- **AND** the response SHALL include persisted knowledge entries for that project
- **AND** the response SHALL include persisted brain entries for that project

#### Scenario: Project memory includes candidate review data
- **WHEN** a caller requests `/api/dashboard/projects/{projectId}/memory` for a project that has persisted memory candidates
- **THEN** the response SHALL include bounded candidate review data for that project
- **AND** each candidate entry SHALL expose review or promotion status, confidence, classification, and supporting evidence metadata

#### Scenario: Project memory includes promotion outcomes
- **WHEN** a project has auto-promoted or manually accepted durable memory candidates
- **THEN** the response SHALL expose promotion outcome summaries that distinguish review-queue items from canonical knowledge facts

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
