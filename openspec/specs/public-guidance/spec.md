# public-guidance Specification

## Purpose
Define one canonical public-guidance policy for nebu-ctx so all instruction and rules surfaces stay aligned on tool mapping, no-bypass behavior, and reproducible bug handling.

## Requirements
### Requirement: Public nebu-ctx guidance has one canonical policy source
The client SHALL define public nebu-ctx guidance semantics from one canonical policy source and SHALL render all supported instruction and rules outputs from that source instead of maintaining separate independent policy text.

#### Scenario: Multiple guidance surfaces are generated
- **WHEN** the client builds MCP instructions, injected rules files, or hook-managed guidance templates
- **THEN** each surface SHALL derive its public-tool mapping and failure-policy semantics from the same canonical guidance policy
- **AND** format-specific differences SHALL be limited to rendering constraints such as length, markdown shape, or client-specific wording

#### Scenario: Guidance policy changes
- **WHEN** the public-tool guidance policy is updated in the canonical source
- **THEN** the generated instruction and rules surfaces SHALL reflect that updated policy without requiring manual copy updates in multiple independent source strings

### Requirement: Public guidance mandates automatic issue filing for reproducible public-tool bugs
Public nebu-ctx guidance SHALL instruct agents to automatically create or update a GitHub issue for a reproducible bug in the public nebu-ctx tool surface before final handoff.

#### Scenario: Reproducible public-tool bug remains after retry
- **WHEN** an agent reproduces a bug in `ctx_read`, `ctx_search`, `ctx_tree`, or `ctx(...)` after one retry where environmental failure was plausible
- **THEN** the guidance SHALL direct the agent to create or update a nebu-ctx GitHub issue before final handoff
- **AND** the guidance SHALL not require the user to explicitly ask for issue creation first

#### Scenario: Bug cannot be filed automatically
- **WHEN** automatic issue filing cannot complete because authentication, privacy, or connectivity constraints block submission
- **THEN** the guidance SHALL direct the agent to report that issue filing did not complete
- **AND** it SHALL preserve the rule that reproducible public-tool bugs are treated as product issues rather than silent native fallbacks

### Requirement: Public guidance forbids native fallback for buggy public-tool paths
Public nebu-ctx guidance SHALL distinguish between an unavailable public path and a buggy public path, and SHALL forbid bypassing a reproducibly buggy public path to native equivalents without first using the supported raw or repo-built nebu-ctx path and issue-report flow.

#### Scenario: Public path exists but misbehaves
- **WHEN** a public nebu-ctx tool exists for the requested action but behaves incorrectly
- **THEN** the guidance SHALL direct the agent to stay inside supported nebu-ctx paths such as retry, raw mode, or repo-built client validation
- **AND** it SHALL not present the native equivalent as the primary fallback for that bug case

#### Scenario: Public path truly does not exist
- **WHEN** no public nebu-ctx path exists for the requested mutation or workflow
- **THEN** the guidance MAY direct the agent to use native tools
- **AND** it SHALL keep that exception separate from the buggy-public-path case
