## ADDED Requirements

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
