## ADDED Requirements

### Requirement: Maintenance findings are visible
The dashboard MUST display findings returned by project memory maintenance or triage operations, including finding kind, affected identity, confidence or severity, and proposed action when available.

#### Scenario: Maintenance analysis returns findings
- **WHEN** the operator runs maintenance in analysis mode
- **THEN** the dashboard SHALL show a bounded summary of findings
- **AND** it SHALL distinguish proposed actions from already applied actions

### Requirement: Maintenance application refreshes memory
The dashboard MUST refresh project memory after maintenance actions are applied and MUST show the resulting counts or action summary.

#### Scenario: Maintenance apply succeeds
- **WHEN** the operator applies confirmed maintenance actions
- **THEN** the dashboard SHALL show the applied result
- **AND** it SHALL reload health, entries, wake-up data, and candidate summaries

#### Scenario: Maintenance request fails
- **WHEN** a maintenance request times out or returns a server error
- **THEN** the dashboard SHALL show an actionable failure state
- **AND** it SHALL provide a retry path without silently discarding the result