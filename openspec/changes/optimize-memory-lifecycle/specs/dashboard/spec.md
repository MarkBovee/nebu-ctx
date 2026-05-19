## MODIFIED Requirements

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

#### Scenario: Dashboard shows lifecycle changes
- **WHEN** scoring, supersession, or wake-up recomputation changes the effective project memory set
- **THEN** the dashboard SHALL expose lifecycle-oriented indicators that make those changes visible to an operator
- **AND** it SHALL allow an operator to tell whether memory behavior changed because of promotion, upkeep, or temporal supersession

#### Scenario: Dashboard shows triage findings
- **WHEN** hosted memory triage has been previewed or applied for a project
- **THEN** the dashboard SHALL expose triage-oriented summary data for duplicate groups, stale/junk candidates, and applied cleanup actions
- **AND** it SHALL allow an operator to distinguish preview-only triage findings from already applied triage changes
