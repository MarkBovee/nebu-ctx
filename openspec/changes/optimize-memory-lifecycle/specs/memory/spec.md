## MODIFIED Requirements

### Requirement: Startup memory activation
The client MUST inject project memory context at session startup using a bounded wake-up selection that prefers canonical hosted memory when available and falls back locally when needed.

#### Scenario: Startup with prior hosted memory
- **WHEN** a startup `SessionStart` or equivalent OpenCode startup hook fires for a project with persisted memory and a healthy server connection
- **THEN** the hook SHALL emit routing guidance
- **AND** it SHALL emit a bounded wake-up snapshot selected from canonical hosted memory
- **AND** it SHALL avoid loading the full hosted memory set into startup context

#### Scenario: Startup without hosted memory availability
- **WHEN** startup memory activation runs and the server is unavailable or not configured
- **THEN** the client SHALL fall back to local session and knowledge state
- **AND** it SHALL keep the wake-up snapshot within the startup memory budget

### Requirement: OpenCode lifecycle uses hosted memory selection
The OpenCode plugin MUST actively use the hosted memory lifecycle outputs during startup, compaction, idle persistence, and continuation flows.

#### Scenario: OpenCode startup uses hosted wake-up
- **WHEN** the OpenCode plugin handles the first request for a project with hosted canonical memory available
- **THEN** it SHALL request or consume the bounded hosted wake-up selection
- **AND** it SHALL inject that bounded wake-up output into the OpenCode system/continuation context

#### Scenario: OpenCode compaction refreshes continuation memory
- **WHEN** OpenCode compacts a session after memory lifecycle upkeep or promotion changed the effective wake-up set
- **THEN** the plugin SHALL inject refreshed continuation memory derived from the hosted lifecycle outputs
- **AND** it SHALL avoid reusing stale startup memory when a fresher hosted selection exists

#### Scenario: OpenCode idle flow persists promotable memory
- **WHEN** OpenCode becomes idle after prompts or tool activity produced new promotable memory
- **THEN** the plugin SHALL flush the relevant hosted memory promotion or consolidation path
- **AND** later startup or continuation hooks SHALL be able to observe the updated canonical memory state

## ADDED Requirements

### Requirement: Server-backed memory promotion
The server MUST accept explicit promoted memory candidates and persist them as canonical project knowledge.

#### Scenario: Client promotes memory candidates
- **WHEN** a caller invokes the public memory capability to promote explicit knowledge candidates for a project with hosted memory available
- **THEN** the server SHALL persist valid candidates into canonical project knowledge
- **AND** it SHALL return how many candidates were promoted or skipped

### Requirement: Server-backed session consolidation
The server MUST be able to consolidate the latest persisted session state into canonical project knowledge.

#### Scenario: Hosted consolidate uses latest session
- **WHEN** a caller invokes the public memory capability to consolidate a project whose latest persisted session includes hosted task, findings, or decisions
- **THEN** the server SHALL derive promoted knowledge from the latest persisted session state
- **AND** it SHALL write the resulting session, finding, and decision memories into canonical project knowledge

### Requirement: Hosted memory triage requests
The public memory capability MUST support explicit hosted memory triage for projects whose canonical memory is managed by the server.

#### Scenario: Triage preview analyzes canonical memory safely
- **WHEN** a caller requests hosted memory triage for a project with canonical hosted memory
- **THEN** the server SHALL analyze the effective project memory set for duplicate, overlapping, stale, superseded, or junk-like entries
- **AND** it SHALL return a preview of recommended triage actions without mutating canonical memory by default

#### Scenario: Triage apply requires explicit intent
- **WHEN** a caller explicitly requests hosted memory triage apply-mode
- **THEN** the server SHALL apply only the confirmed triage actions for that request
- **AND** it SHALL report which actions were applied, skipped, or rejected
