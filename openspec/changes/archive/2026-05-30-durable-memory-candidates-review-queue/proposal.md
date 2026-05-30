## Why

Real debugging and implementation sessions produce durable project truths, but `nebu-ctx` still relies too heavily on manual promotion to get those truths into reusable memory. That leaves high-value root causes, runtime caveats, and live verification facts trapped in transient session context instead of becoming reliable project memory.

## What Changes

- Add a durable memory candidate workflow that extracts 3-5 high-value project facts from session findings, decisions, assistant conclusions, and verification evidence.
- Auto-promote high-confidence candidates into hosted canonical knowledge while keeping medium-confidence candidates in a server-side review queue.
- Classify promoted facts with durable debugging-oriented categories such as `root_cause`, `runtime_caveat`, `verified_behavior`, `contract_decision`, and `live_verification`.
- Add candidate deduplication, replay-safe promotion identities, and evidence-aware ranking so near-identical conclusions do not spam canonical memory.
- Improve project-root recall and wake-up behavior so durable debugging truths rank ahead of generic project facts.
- Extend dashboard project memory views to expose candidate review status, evidence context, and auto-promotion outcomes.

## Capabilities

### New Capabilities
- None.

### Modified Capabilities
- `memory`: expand durable project memory to extract, classify, auto-promote, queue, dedupe, and recall high-value debugging conclusions with low friction.
- `dashboard`: extend per-project memory inspection to show candidate review queue data, promotion outcomes, and evidence-backed memory ergonomics.

## Impact

- Client memory capture and stop/idle flush paths in `client/src/core/brain_memory.rs`, `client/src/hook_handlers.rs`, and related memory modules.
- Hosted memory services and tool handlers in `server/src/NebuCtx.Server.Core/Services/KnowledgeService.cs`, `BrainService.cs`, and `server/src/NebuCtx.Tools/Knowledge/`.
- Dashboard contracts and payload composition for `/api/dashboard/projects/{projectId}/memory`.
- Integration tests for memory promotion, deduplication, wake-up ranking, and dashboard project-memory responses.
