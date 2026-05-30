## Context

`nebu-ctx` already has the right broad memory shape: the client captures session state and brain events, the server hosts canonical knowledge, and the dashboard exposes project memory inspection. The gap is between session evidence and durable project facts. Useful debugging conclusions are discovered during real work, but the current flow depends too much on explicit `remember` or manual promotion, so the hosted knowledge layer under-represents root causes, runtime caveats, and live verification outcomes.

This change is cross-cutting because it touches client-side extraction, server-side promotion policy, canonical knowledge ranking, and dashboard memory ergonomics. The design must preserve the existing `brain -> hosted knowledge` direction rather than introducing a second canonical store.

## Goals / Non-Goals

**Goals:**
- Extract high-value durable memory candidates from normal debugging and implementation sessions without requiring explicit manual storage calls.
- Auto-promote only high-confidence candidates into hosted canonical knowledge.
- Preserve medium-confidence candidates in a server-side review queue so memory capture stays low-friction without becoming noisy.
- Add durable debugging-oriented classification, deduplication, and recall ranking so useful conclusions surface reliably when a project is reopened.
- Expose candidate and promotion status through existing dashboard project-memory flows.

**Non-Goals:**
- Replace hosted knowledge with a new canonical memory store.
- Introduce an LLM-dependent extraction pipeline as the first implementation.
- Persist raw transcript snippets as canonical project memory.
- Expand the public MCP surface beyond the current memory routing model unless needed to expose candidate review.

## Decisions

### Keep hosted knowledge as the single canonical project-fact store
Hosted `ctx_knowledge` remains the source of truth for durable project facts. `ctx_brain` continues to act as ingest, evidence, and timeline support.

Rationale:
- The server already projects typed brain facts into hosted knowledge.
- Canonical recall, lifecycle, wake-up, and dashboard composition already center on knowledge entries.
- Keeping one canonical store avoids conflicting ranking and contradiction rules.

Alternatives considered:
- Brain-first canonical model: rejected because it duplicates lifecycle and recall responsibilities already implemented in knowledge.
- Hybrid equal stores: rejected because it adds ambiguity for promotion, triage, and dashboard behavior.

### Introduce a server-side candidate review queue
Medium-confidence candidates will be persisted on the server as reviewable memory candidates, scoped per project, with status such as `pending_review`, `auto_promoted`, `accepted`, `rejected`, and `superseded`.

Rationale:
- Candidate state needs to survive session boundaries.
- Dashboard inspection becomes much stronger if operators can see what was auto-promoted versus what still needs review.
- The queue enables later feedback loops for heuristics without polluting canonical knowledge.

Alternatives considered:
- Transient candidate payload only: rejected because candidates disappear too easily and cannot support review workflow or metrics.
- Local-only candidate queue: rejected because hosted project memory is already the collaboration surface across editors and sessions.

### Use deterministic heuristic extraction before semantic or LLM extraction
The first implementation will use explicit heuristics over session findings, decisions, assistant turns, journal events, and verification evidence. Extraction will look for durable-conclusion phrasing, causal language, concrete subsystem references, and verification signals.

Rationale:
- Easy to test and explain during review.
- Stable offline and server-safe behavior.
- Lower complexity for initial rollout while still solving the issue directly.

Alternatives considered:
- Embeddings-first extraction: rejected because similarity helps recall and dedupe, but not reliable initial fact generation.
- LLM summarization-first extraction: rejected for cost, explainability, and determinism concerns.

### Split promotion by confidence threshold
Candidates will be split into three bands:
- high confidence: auto-promote to hosted knowledge and mark candidate `auto_promoted`
- medium confidence: store as pending review candidate only
- low confidence: retain only as brain/session evidence, not as queued candidate or canonical fact

Initial thresholds should be configurable but start with a narrow default such as:
- auto-promote: `>= 0.92`
- review queue: `0.78 - 0.91`
- below queue threshold: no candidate persistence

Rationale:
- Matches the desired low-friction behavior without flooding canonical knowledge.
- Keeps review workload bounded.

Alternatives considered:
- Manual review only: rejected because it keeps too much friction.
- Auto-promote everything above weak confidence: rejected because it risks noisy project memory.

### Add debugging-oriented fact typing and ranking boosts
Knowledge entries and review candidates should carry durable memory type metadata such as `root_cause`, `runtime_caveat`, `verified_behavior`, `contract_decision`, and `live_verification`. Recall and wake-up ranking will boost those types above generic facts when query tokens match their subsystem or evidence.

Rationale:
- The issue is specifically about debugging truths, not generic project inventory.
- Ranking needs stronger semantic priority than plain confidence to make the feature look smart in real use.

Alternatives considered:
- Keep generic categories only: rejected because root causes and caveats would remain hard to distinguish from ordinary facts.

### Add candidate deduplication before canonical promotion
Candidate deduplication will happen before promotion using a deterministic fingerprint built from normalized category, type, logical subject, and simplified claim text. Near-identical candidates should converge to one queued or promoted record.

Rationale:
- Prevents spam from repeated restatements across multiple turns or stop/idle flushes.
- Aligns with replay-safe promotion identity design already used by hosted knowledge.

Alternatives considered:
- Deduplicate only after canonical promotion: rejected because queue and dashboard noise would remain high.

### Extend existing dashboard project memory payload instead of creating separate candidate pages
`/api/dashboard/projects/{projectId}/memory` should include candidate review queue data and promotion summaries alongside knowledge, brain, health, triage, and wake-up.

Rationale:
- Existing endpoint already acts as the per-project memory control plane.
- Keeps operator workflow in one place.

Alternatives considered:
- New dashboard-only endpoint for candidates: rejected because it fragments the memory story and adds extra UI load.

## Risks / Trade-offs

- [Heuristics miss valuable facts] -> Start with explicit debugging phrases and verification cues, then refine with stored candidate outcomes.
- [Heuristics produce noisy candidates] -> Keep the auto-promote threshold strict, queue only medium-confidence items, and add candidate dedupe.
- [New server-side queue adds schema and lifecycle complexity] -> Keep candidate state minimal and reuse existing knowledge identity patterns where possible.
- [Ranking changes may hide useful generic facts] -> Apply boosts rather than absolute filtering and keep wake-up bounded with mixed diversity rules.
- [Dashboard payload growth] -> Return bounded candidate lists and summary counts rather than full unbounded history.
- [Offline promotion gaps] -> Reuse current outbox behavior for client-to-server candidate submission and promotion actions.

## Migration Plan

1. Add server-side candidate review queue storage and contracts behind new hosted memory actions.
2. Add client-side candidate extraction and submission during stop/idle flush, using existing outbox fallback when the server is unavailable.
3. Update hosted knowledge promotion flow to auto-promote only high-confidence candidates and keep medium-confidence items in review state.
4. Extend recall, wake-up, and dashboard payloads to include candidate-aware ranking and review visibility.
5. Add integration and client tests for extraction, dedupe, promotion replay, ranking, and dashboard responses.

Rollback strategy:
- Disable candidate submission and queue-backed actions while leaving existing brain and knowledge behavior intact.
- Candidate queue data can remain stored without affecting canonical knowledge if the new ranking and review paths are turned off.

## Open Questions

- Should review actions live only in dashboard/admin endpoints, or also on the public `ctx(domain="memory")` route for agent-driven approval flows?
- Should evidence text be stored inline on canonical knowledge entries or remain linked primarily through candidate and brain records?
- Should candidate ranking consider per-project category quotas so one noisy root-cause cluster cannot dominate the review queue?
