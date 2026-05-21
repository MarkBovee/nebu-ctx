## MODIFIED Requirements

### Requirement: Project memory inspection
The server MUST expose per-project memory data for dashboard and admin workflows using semantic brain facts and their lifecycle state instead of raw hosted brain log strings as the primary brain view.

#### Scenario: Project memory is requested
- **WHEN** a caller requests `/api/dashboard/projects/{projectId}/memory`
- **THEN** the server SHALL return the selected project identifier and name
- **AND** the response SHALL include persisted knowledge or projection entries for that project
- **AND** the response SHALL include semantic brain facts with lifecycle metadata for that project

### Requirement: Project memory admin routes
The server MUST expose admin operations for project memory management that align with semantic brain facts.

#### Scenario: Brain fact is deleted or cleared
- **WHEN** a caller deletes a project brain fact entry or clears project brain memory through the dashboard admin surface
- **THEN** the matching semantic brain fact data SHALL be removed when it exists
- **AND** the operation SHALL target canonical brain fact storage rather than transcript-style brain log data

## ADDED Requirements

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
