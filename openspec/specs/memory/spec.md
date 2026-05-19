# memory Specification

## Purpose
Provide durable project memory across sessions and editors through the public memory capability, including startup activation, session capture, recall, consolidation, promotion, and offline-safe persistence.
## Requirements
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

### Requirement: OpenCode lifecycle memory parity
The OpenCode plugin MUST use available plugin lifecycle hooks to preserve the same core memory behaviors delivered through Claude hooks.

#### Scenario: OpenCode session starts with prior memory
- WHEN an OpenCode session sends its first model request for a project with stored session or knowledge data
- THEN the plugin SHALL inject routing guidance into the system prompt
- AND it SHALL inject a compact memory snapshot when one can be built

#### Scenario: OpenCode session compacts
- WHEN OpenCode compacts a session
- THEN the plugin SHALL inject additional compaction context derived from local session and knowledge state
- AND the next model turn after compaction SHALL receive a fresh continuation snapshot

#### Scenario: OpenCode session becomes idle after writes
- WHEN an OpenCode session has captured prompts or tool activity and later becomes idle
- THEN the plugin SHALL flush durable session persistence through the existing nebu-ctx hook path
- AND offline writes SHALL continue to rely on the local sync outbox when the server is unavailable

### Requirement: Offline-safe memory writes
Client-driven server memory writes MUST not be silently dropped when the server is unavailable.

#### Scenario: Brain or knowledge write while offline
- WHEN a prompt or promoted project fact should be written to the server and the server is unavailable
- THEN the write SHALL be queued in the local sync outbox
- AND it SHALL be retried later
