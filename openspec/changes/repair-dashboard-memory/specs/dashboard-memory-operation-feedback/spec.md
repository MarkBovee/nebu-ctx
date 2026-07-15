## ADDED Requirements

### Requirement: Memory views distinguish request states
Dashboard memory views MUST distinguish loading, valid-empty, loaded-data, timeout, malformed-response, and server-error states.

#### Scenario: Memory request is loading
- **WHEN** a memory request is in progress
- **THEN** the dashboard SHALL show a loading state
- **AND** it SHALL not show stale data as if it were current

#### Scenario: Memory request fails
- **WHEN** a memory request times out, returns an error, or cannot be parsed
- **THEN** the dashboard SHALL show the failure category
- **AND** it SHALL provide a retry action

### Requirement: Mutating operations provide feedback
Memory delete, clear, maintenance, and review operations MUST show progress and a final success or failure result.

#### Scenario: Operation succeeds
- **WHEN** a memory operation completes successfully
- **THEN** the dashboard SHALL show the outcome count or status
- **AND** it SHALL refresh the affected data

#### Scenario: Operation fails
- **WHEN** a memory operation fails
- **THEN** the dashboard SHALL keep the failure visible
- **AND** it SHALL allow a bounded retry without duplicating a successful operation