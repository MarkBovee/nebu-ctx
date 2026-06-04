## MODIFIED Requirements

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
