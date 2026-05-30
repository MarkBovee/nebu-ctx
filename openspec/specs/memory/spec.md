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

#### Scenario: Startup with hosted durable debugging memory
- **WHEN** a startup flow can reach hosted project memory that includes promoted debugging facts or accepted review candidates
- **THEN** the startup memory snapshot SHALL prioritize those durable debugging facts in the bounded wake-up briefing
- **AND** it SHALL rank root causes, runtime caveats, and verified behaviors ahead of lower-value generic facts for the same project

### Requirement: OpenCode lifecycle memory parity
The OpenCode plugin MUST actively use the hosted memory lifecycle outputs during startup, compaction, idle persistence, and continuation flows.

#### Scenario: OpenCode startup uses hosted wake-up
- **WHEN** the OpenCode plugin handles the first request for a project with hosted canonical memory available
- **THEN** it SHALL request or consume the bounded hosted wake-up selection
- **AND** it SHALL inject that bounded wake-up output into the OpenCode system or continuation context

#### Scenario: OpenCode compaction refreshes continuation memory
- **WHEN** OpenCode compacts a session after memory lifecycle upkeep or promotion changed the effective wake-up set
- **THEN** the plugin SHALL inject refreshed continuation memory derived from the hosted lifecycle outputs
- **AND** it SHALL avoid reusing stale startup memory when a fresher hosted selection exists

#### Scenario: OpenCode idle flow persists promotable memory
- **WHEN** OpenCode becomes idle after prompts or tool activity produced new promotable memory
- **THEN** the plugin SHALL flush the relevant hosted memory promotion or consolidation path
- **AND** later startup or continuation hooks SHALL be able to observe the updated canonical memory state

### Requirement: Offline-safe memory writes
Client-driven server memory writes MUST not be silently dropped when the server is unavailable.

#### Scenario: Brain or knowledge write while offline
- **WHEN** a prompt or promoted project fact should be written to the server and the server is unavailable
- **THEN** the write SHALL be queued in the local sync outbox
- **AND** it SHALL be retried later

#### Scenario: Candidate submission while offline
- **WHEN** extracted project-memory candidates or candidate review actions should be sent to the server and the server is unavailable
- **THEN** the candidate submission or review action SHALL be queued in the local sync outbox
- **AND** replaying the queued action later SHALL preserve deterministic candidate identity and deduplication behavior

### Requirement: Shared memory lifecycle core
The client MUST route supported editor and hook integrations through a shared memory lifecycle core instead of embedding separate brain-write semantics in each adapter.

#### Scenario: Different editors trigger the same lifecycle event
- **WHEN** OpenCode, Claude Code, or Copilot trigger equivalent startup, compaction, idle, or stop lifecycle phases
- **THEN** the client SHALL normalize those phases into shared lifecycle events
- **AND** it SHALL use the same journal, fact extraction, and hosted brain ingest logic for each adapter

### Requirement: Public memory retrieval stays brain-backed without contract break
The public `ctx(domain="memory", action=...)` surface MUST keep serving recall and wake-up workflows while its effective canonical source moves to hosted brain facts or their projection.

#### Scenario: Public memory wakeup is requested after brain fact ingest
- **WHEN** a caller requests `ctx(domain="memory", action="wakeup")` after new hosted brain facts have been ingested
- **THEN** the result SHALL reflect the effective active brain-backed project memory
- **AND** callers SHALL not need to change their public tool contract to receive that updated memory

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

### Requirement: Durable debugging memory candidate extraction
The memory capability MUST extract durable project-memory candidates from active debugging and implementation sessions without requiring manual `remember` calls.

#### Scenario: Stop or idle flush yields durable candidates
- **WHEN** a project session contains findings, decisions, assistant conclusions, or verification evidence that express durable debugging conclusions
- **THEN** the system SHALL derive up to 5 project-memory candidates for that project
- **AND** each candidate SHALL include category or type metadata, confidence, evidence, and deterministic promotion identity inputs

#### Scenario: Low-signal activity is excluded from candidate extraction
- **WHEN** captured session content only reflects routine activity, raw logs, or tool chatter without a durable project conclusion
- **THEN** the system SHALL NOT promote that content into canonical project knowledge
- **AND** it SHALL exclude that content from persisted review candidates

### Requirement: Confidence-based candidate promotion and review
The memory capability MUST split extracted candidates into auto-promoted durable facts and reviewable candidates based on confidence.

#### Scenario: High-confidence candidate auto-promotes
- **WHEN** a derived project-memory candidate meets the configured auto-promotion threshold
- **THEN** the system SHALL persist the candidate outcome for auditability
- **AND** it SHALL promote the candidate into hosted canonical knowledge for the current project without manual review

#### Scenario: Medium-confidence candidate enters review queue
- **WHEN** a derived project-memory candidate does not meet the auto-promotion threshold but does meet the review threshold
- **THEN** the system SHALL persist that candidate in a server-side review queue for the current project
- **AND** it SHALL NOT add the candidate to canonical knowledge until it is accepted or auto-promoted later

#### Scenario: Low-confidence candidate stays non-canonical
- **WHEN** a derived project-memory candidate falls below the review threshold
- **THEN** the system SHALL keep any related evidence only in non-canonical session or brain history
- **AND** it SHALL NOT create a review-queue entry or canonical knowledge fact from that candidate

### Requirement: Candidate deduplication and replay-safe identity
The memory capability MUST deduplicate near-identical durable candidates before canonical promotion or review-queue storage.

#### Scenario: Repeated conclusion reuses candidate identity
- **WHEN** the same durable conclusion is rediscovered during the same or a later session for the same project
- **THEN** the system SHALL match it to the same logical candidate identity when category, subject, and claim normalize to the same durable conclusion
- **AND** it SHALL avoid creating duplicate current review candidates or duplicate current canonical facts

### Requirement: Durable debugging fact classification
The memory capability MUST classify promoted or queued debugging conclusions using durable project-memory types.

#### Scenario: Root cause conclusion is classified
- **WHEN** an extracted candidate states the causal explanation for a bug, runtime mismatch, or debugging outcome
- **THEN** the system SHALL classify that candidate as a durable root-cause fact type
- **AND** it SHALL persist that classification alongside the candidate or promoted fact

#### Scenario: Verified runtime behavior is classified
- **WHEN** an extracted candidate records a confirmed runtime truth, live verification result, or external behavior caveat
- **THEN** the system SHALL classify that candidate as a verified-behavior, runtime-caveat, contract-decision, or live-verification fact type as applicable
