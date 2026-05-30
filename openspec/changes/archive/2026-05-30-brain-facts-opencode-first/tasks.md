## 1. Contracts And Semantics

- [x] 1.1 Update `docs/MEMORY.md` and related OpenSpec references to redefine brain as fact-only canonical memory and journal as client-local raw storage
- [x] 1.2 Design and add typed hosted brain fact contracts, store interfaces, and persistence models alongside clear legacy-brain handling rules
- [x] 1.3 Define deterministic identities and lifecycle fields for brain fact ingest, supersession, invalidation, and replay-safe canonicalization

## 2. Client Lifecycle Core And Journal

- [x] 2.1 Add a shared client memory lifecycle core that normalizes startup, user turn, assistant completion, tool activity, compaction, idle flush, and stop events
- [x] 2.2 Add client-local journal storage with bounded retention for raw prompts, assistant turns, tool outcomes, and lifecycle markers
- [x] 2.3 Replace direct raw brain writes in hook handlers with journal writes and lifecycle event dispatch

## 3. Fact Extraction And Hosted Brain Ingest

- [x] 3.1 Implement client-side fact candidate extraction from journal, session state, and tool receipts for high-signal memory kinds
- [x] 3.2 Implement hosted brain fact ingest, canonicalization, and temporal lifecycle behavior in server brain services and storage
- [x] 3.3 Replace stop-time and pre-compact session-summary brain writes with derived fact batch ingest and brain-backed wake-up refresh

## 4. OpenCode-First Integration And Public Projection

- [x] 4.1 Update the OpenCode plugin to use the shared lifecycle core as the primary adapter for startup, continuation, compacting, idle, and delete flows
- [x] 4.2 Update startup and continuation memory injection paths to prefer hosted brain-backed wake-up output while preserving local fallback behavior
- [x] 4.3 Rework public `ctx(domain="memory", ...)` recall and wakeup flows so they reflect effective brain-backed project memory without breaking the public contract
- [x] 4.4 Align Claude and Copilot memory adapters with the same shared lifecycle core after OpenCode primary behavior is in place

## 5. Dashboard, Offline Replay, And Verification

- [x] 5.1 Update dashboard project memory payloads and admin routes to show semantic brain facts, lifecycle state, and wake-up composition
- [x] 5.2 Update offline outbox payloads and replay logic so hosted brain fact batches remain durable and idempotent across retries
- [x] 5.3 Add focused client and server tests for OpenCode-first lifecycle flows, journal retention, fact extraction, supersession, invalidation, dashboard semantics, and replay safety
- [x] 5.4 Validate the change with targeted `cargo test`, `dotnet test`, and OpenCode lifecycle or replay checks before handoff
