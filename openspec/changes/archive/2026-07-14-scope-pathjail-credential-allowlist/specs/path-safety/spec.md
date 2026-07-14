## ADDED Requirements

### Requirement: Cross-tool config directory access is scoped to session-state only
The path jail's allow-list for known IDE/agent config directories (`IDE_CONFIG_DIRS`) MUST only grant access to each tool's own `session-state` subdirectory. It MUST NOT grant access to any other file or subdirectory directly under a tool's config directory, including that tool's credential files.

#### Scenario: Session-state file under an IDE config directory is allowed
- **WHEN** a path resolves to `<home>/<tool-config-dir>/session-state/...` for one of the known `IDE_CONFIG_DIRS` entries
- **THEN** the path jail SHALL allow the path

#### Scenario: Credential file sibling to session-state is rejected
- **WHEN** a path resolves to a file directly under `<home>/<tool-config-dir>/` (e.g. `.credentials.json`) that is outside the `session-state` subdirectory
- **THEN** the path jail SHALL reject the path

#### Scenario: ctx_read cannot disclose another tool's credentials
- **WHEN** the `ctx_read` MCP tool is invoked with a path such as `~/.claude/.credentials.json`
- **THEN** the request SHALL be rejected by the path jail before any file content is read
