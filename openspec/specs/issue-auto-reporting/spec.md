# issue-auto-reporting Specification

## Purpose
Define non-interactive, duplicate-aware public bug reporting for nebu-ctx so reproducible public-tool failures can be submitted automatically without losing the manual workflow.

## Requirements
### Requirement: `report-issue` supports non-interactive issue submission
`nebu-ctx report-issue` SHALL support a non-interactive submission mode that accepts structured bug details and submits a report without prompting for manual confirmation.

#### Scenario: Agent submits a reproducible bug report automatically
- **WHEN** an agent invokes `nebu-ctx report-issue` in automation mode with the required bug metadata
- **THEN** the command SHALL build the report body, attempt submission, and exit without asking the user for interactive confirmation
- **AND** it SHALL return a machine-usable success or failure status

#### Scenario: Manual workflow still uses preview and confirmation
- **WHEN** a human invokes `nebu-ctx report-issue` without automation flags
- **THEN** the command SHALL retain the interactive preview and confirmation workflow
- **AND** the automation features SHALL not remove the manual reporting path

### Requirement: Automatic issue submission is duplicate-aware
`nebu-ctx report-issue` automation mode SHALL search for relevant open issues before creating a new issue and SHALL update an existing issue when a confident duplicate is found.

#### Scenario: Matching open issue exists
- **WHEN** automation mode detects an open nebu-ctx issue that confidently matches the same public-tool failure class
- **THEN** the command SHALL update or comment on that existing issue instead of creating a new duplicate issue
- **AND** it SHALL report the reused issue reference back to the caller

#### Scenario: No matching open issue exists
- **WHEN** automation mode does not find a confident duplicate among open nebu-ctx issues
- **THEN** the command SHALL create a new issue with the collected diagnostics
- **AND** it SHALL report the created issue reference back to the caller

### Requirement: Automatic issue submission reports blocked submission clearly
When automatic issue submission cannot complete, `nebu-ctx report-issue` SHALL report the failure clearly and preserve the diagnostic report for later use.

#### Scenario: GitHub authentication is unavailable
- **WHEN** automation mode cannot submit because `gh` is unavailable, unauthenticated, or GitHub access is blocked
- **THEN** the command SHALL report that submission did not complete
- **AND** it SHALL save the generated report locally for later manual submission

#### Scenario: Automatic submission is blocked by privacy constraints
- **WHEN** the automation request cannot safely submit because the generated report would expose data that must not be uploaded automatically
- **THEN** the command SHALL stop before submission
- **AND** it SHALL return an explicit blocked-submission result instead of claiming success
