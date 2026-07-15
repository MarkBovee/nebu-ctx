## ADDED Requirements

### Requirement: Project memory health is visible
The dashboard MUST render the health data returned for a project memory view, including current and non-current counts, lifecycle score, density, and maintenance summary when present.

#### Scenario: Project has memory health data
- **WHEN** a project memory response contains health metrics
- **THEN** the dashboard SHALL show the metrics in the project memory view
- **AND** the values SHALL be associated with the selected project

#### Scenario: Project has no memory entries
- **WHEN** a project memory response is valid but contains no entries
- **THEN** the dashboard SHALL show a valid-empty state
- **AND** it SHALL not display a connection failure

### Requirement: Lifecycle states are understandable
The dashboard MUST provide a consistent legend or accessible explanation for lifecycle states shown on knowledge and brain entries.

#### Scenario: Operator inspects a lifecycle badge
- **WHEN** an entry displays a current, stale, superseded, merged, junk, or invalidated state
- **THEN** the dashboard SHALL expose the meaning of that state
- **AND** the state color and label SHALL be consistent across memory sections

### Requirement: Brain entry kinds are normalized
The server and dashboard MUST use one normalized brain entry kind for display, counts, and type-based operations, defaulting missing legacy kinds to `fact`.

#### Scenario: Brain entry has no kind
- **WHEN** a stored brain entry has a null or empty kind
- **THEN** the dashboard payload SHALL expose kind `fact`
- **AND** its count SHALL be included in the `fact` type

#### Scenario: Operator clears a brain type
- **WHEN** an operator clears a displayed brain entry type
- **THEN** the server SHALL apply the same normalized type used by the displayed count
- **AND** the result count SHALL match the entries removed