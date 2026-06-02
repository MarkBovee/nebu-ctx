# ripgrep-backed-search Specification

## Purpose
Define ripgrep-compatible regex search behavior for the public `ctx_search` surface so real matches, ignore handling, and failures are reported reliably.

## Requirements
### Requirement: `ctx_search` regex mode uses ripgrep-compatible search behavior
The public `ctx_search` regex mode SHALL use a ripgrep-compatible search engine for regex execution, path traversal, ignore handling, and binary-safe file filtering while preserving the existing public command contract.

#### Scenario: Regex hit exists in tracked source files
- **WHEN** a caller runs `ctx_search` in regex mode against a workspace path where ripgrep would return matches for the same pattern
- **THEN** `ctx_search` SHALL return matches for that pattern instead of a zero-match response
- **AND** the returned matches SHALL respect the same workspace path scope and ignore behavior for that invocation

#### Scenario: Caller disables gitignore filtering
- **WHEN** a caller invokes `ctx_search` with gitignore filtering disabled
- **THEN** the search engine SHALL include files that would otherwise be excluded by gitignore-aware traversal
- **AND** the result SHALL still preserve binary-safe search handling and the existing compact response shape

### Requirement: `ctx_search` never masks execution failures as trustworthy zero matches
The public `ctx_search` regex mode SHALL return an explicit error or timeout response whenever it cannot produce a reliable search result.

#### Scenario: Invalid regex input
- **WHEN** a caller passes an invalid regex pattern to `ctx_search`
- **THEN** `ctx_search` SHALL return an explicit invalid-regex error
- **AND** it SHALL not report `0 matches`

#### Scenario: Search backend cannot produce a reliable result
- **WHEN** the underlying regex search engine, result collection, or formatting pipeline fails before a reliable result can be produced
- **THEN** `ctx_search` SHALL return an explicit failure or timeout response
- **AND** it SHALL not represent that failure as a normal zero-match success

### Requirement: `ctx_search` output formatting preserves discovered matches
The compact output layer for `ctx_search` SHALL format structured search results without dropping discovered matches silently.

#### Scenario: Matches require compact formatting
- **WHEN** `ctx_search` collects matches and applies its compact-output formatting or token-saving transformations
- **THEN** every reported match count and displayed result SHALL be derived from the collected structured matches
- **AND** the formatter SHALL not reduce the result to `0 matches` unless the collected match set is actually empty

#### Scenario: Search hits exceed max-results limit
- **WHEN** the collected match set exceeds the requested maximum result count
- **THEN** `ctx_search` SHALL return a bounded subset that reflects the configured limit
- **AND** the reported output SHALL still represent a non-empty match result rather than an empty-success response
