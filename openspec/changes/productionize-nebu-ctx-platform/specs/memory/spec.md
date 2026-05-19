## ADDED Requirements

### Requirement: Complete Claude memory hook installation
The client MUST install the Claude Code hook set needed for routing, startup recall, prompt capture, compaction, and stop-time persistence.

#### Scenario: Claude init writes hook config
- WHEN `nebu-ctx init --agent claude` runs successfully
- THEN the generated Claude hook config SHALL include `PreToolUse`
- AND it SHALL include `PostToolUse`
- AND it SHALL include `SessionStart`
- AND it SHALL include `UserPromptSubmit`
- AND it SHALL include `PreCompact`
- AND it SHALL include `Stop`

### Requirement: Startup memory activation
The client MUST inject project memory context at session startup when local memory exists.

#### Scenario: Startup with prior local memory
- WHEN a startup `SessionStart` hook fires for a project with stored session or knowledge data
- THEN the hook SHALL emit routing guidance
- AND it SHALL emit a compact memory snapshot when one can be built

### Requirement: Offline-safe memory writes
Client-driven server memory writes MUST not be silently dropped when the server is unavailable.

#### Scenario: Brain or knowledge write while offline
- WHEN a prompt or promoted project fact should be written to the server and the server is unavailable
- THEN the write SHALL be queued in the local sync outbox
- AND it SHALL be retried later
