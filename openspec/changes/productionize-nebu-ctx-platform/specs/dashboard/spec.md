## ADDED Requirements

### Requirement: Consolidated dashboard overview
The server MUST expose a single dashboard overview endpoint that aggregates the key overview data needed by the UI.

#### Scenario: Overview loads with one primary request
- WHEN the dashboard overview view is opened
- THEN the UI SHALL be able to fetch the primary overview payload from `/api/dashboard/overview`
- AND the payload SHALL include version information
- AND the payload SHALL include aggregated stats information
- AND the payload SHALL include gain information

### Requirement: Project memory inspection
The server MUST expose per-project memory data for dashboard and admin workflows.

#### Scenario: Project memory is requested
- WHEN a caller requests `/api/dashboard/projects/{projectId}/memory`
- THEN the server SHALL return the selected project identifier and name
- AND the response SHALL include persisted knowledge entries for that project
- AND the response SHALL include persisted brain entries for that project

### Requirement: Project memory admin routes
The server MUST expose admin operations for project memory management.

#### Scenario: Brain memory entry is deleted
- WHEN a caller deletes `/api/dashboard/projects/{projectId}/memory/brain/{key}`
- THEN the matching brain entry SHALL be removed when it exists

#### Scenario: Knowledge fact is deleted
- WHEN a caller deletes `/api/dashboard/projects/{projectId}/memory/knowledge/{category}/{key}`
- THEN the matching knowledge fact SHALL be removed when it exists
