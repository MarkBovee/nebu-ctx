## ADDED Requirements

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

## MODIFIED Requirements

### Requirement: Startup memory activation
The client MUST inject project memory context at session startup when local or hosted durable memory exists.

#### Scenario: Startup with prior local memory
- **WHEN** a startup `SessionStart` hook fires for a project with stored session or knowledge data
- **THEN** the hook SHALL emit routing guidance
- **AND** it SHALL emit a compact memory snapshot when one can be built

#### Scenario: Startup with hosted durable debugging memory
- **WHEN** a startup flow can reach hosted project memory that includes promoted debugging facts or accepted review candidates
- **THEN** the startup memory snapshot SHALL prioritize those durable debugging facts in the bounded wake-up briefing
- **AND** it SHALL rank root causes, runtime caveats, and verified behaviors ahead of lower-value generic facts for the same project

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
