## ADDED Requirements

### Requirement: Directory display truncation never panics on non-ASCII paths
The client MUST truncate long directory paths for display by cutting on a UTF-8 character boundary, never on an arbitrary byte offset that could split a multi-byte character.

#### Scenario: Overview truncates a long path containing multi-byte UTF-8 characters
- **WHEN** `ctx` overview formats a directory path longer than the display limit that contains multi-byte UTF-8 characters (e.g. accented letters or CJK characters) positioned such that a naive byte-length offset would land mid-character
- **THEN** the truncation SHALL complete without panicking
- **AND** the result SHALL start with `"..."`

#### Scenario: Overview truncates a long ASCII-only path
- **WHEN** `ctx` overview formats a directory path longer than the display limit containing only ASCII characters
- **THEN** the result SHALL equal `"..."` followed by the expected trailing portion of the original path

### Requirement: Session and registry persistence failures are logged, not silently discarded
The client MUST log a warning when session save, agent registry save, or rule injection reports a failure, instead of discarding the result.

#### Scenario: Session save fails
- **WHEN** `Session::save()` returns an error during MCP session handling
- **THEN** the client SHALL emit a `tracing::warn!` log describing the failure
- **AND** the client SHALL continue operating rather than crashing

#### Scenario: Agent registry save fails
- **WHEN** `AgentRegistry::save()` returns an error
- **THEN** the client SHALL emit a `tracing::warn!` log describing the failure

#### Scenario: Rule injection reports errors
- **WHEN** `rules_inject::inject_all_rules()` returns a result with a non-empty `errors` list
- **THEN** the client SHALL emit a `tracing::warn!` log including the reported errors
