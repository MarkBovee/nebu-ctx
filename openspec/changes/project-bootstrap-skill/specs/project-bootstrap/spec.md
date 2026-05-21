## ADDED Requirements

### Requirement: Project bootstrap is a user-initiated skill workflow
`nebu-ctx` MUST provide a user-invokable project bootstrap workflow that maps a repository before attempting to persist project knowledge.

#### Scenario: User asks to map the project
- **WHEN** a user explicitly asks the agent to map, scan, or bootstrap a project
- **THEN** the agent SHALL be able to invoke a dedicated `nebu-ctx` project-bootstrap skill
- **AND** the workflow SHALL summarize the project using existing project signals instead of dumping the entire repository tree or raw file contents into memory

### Requirement: Project bootstrap is preview-first
The bootstrap workflow MUST preview candidate project facts before writing them into canonical memory.

#### Scenario: Bootstrap preview is generated
- **WHEN** the project bootstrap workflow gathers stack, entrypoint, test, infra, module, and workflow signals
- **THEN** it SHALL present proposed facts and supporting evidence in a reviewable preview
- **AND** it SHALL NOT silently persist those facts only because preview ran

#### Scenario: User confirms bootstrap apply
- **WHEN** a user explicitly confirms that proposed bootstrap facts should be stored
- **THEN** the workflow SHALL write them through canonical memory paths with deterministic identity and provenance metadata

### Requirement: Bootstrap workflow is discoverable in docs and help
The project bootstrap workflow MUST be visible in the normal docs/help surfaces that users rely on.

#### Scenario: User checks docs or CLI help
- **WHEN** a user reads the README, memory docs, or CLI workflow help
- **THEN** those surfaces SHALL describe how to start a project mapping/bootstrap flow
- **AND** they SHALL explain that bootstrap is explicit and preview-first rather than automatic background memory capture
