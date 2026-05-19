## 1. Memory Lifecycle Foundations

- [x] 1.1 Extend knowledge storage/contracts with lifecycle metadata and deterministic promotion identities
- [x] 1.2 Add migration or defaulting logic so existing knowledge entries remain valid after lifecycle metadata is introduced

## 2. Hosted Memory Lifecycle

- [x] 2.1 Implement server-side lifecycle scoring and upkeep for canonical project knowledge
- [x] 2.2 Implement temporal supersession or invalidation handling for facts that are replaced by newer promoted memory
- [x] 2.3 Implement bounded layered wake-up selection on top of canonical hosted memory
- [x] 2.4 Expose or wire hosted memory lifecycle actions needed for promote, consolidate, and upkeep-safe wake-up flows
- [x] 2.5 Implement hosted memory triage preview/apply flows for merge, dedup, stale marking, and suspected junk/test/demo cleanup candidates

## 3. Client Routing And Replay

- [x] 3.1 Update client routing so hosted memory lifecycle actions prefer the server while local-only actions remain explicit fallbacks
- [x] 3.2 Update memory promotion payloads and outbox replay to use deterministic, idempotent promoted-memory batches
- [x] 3.3 Update startup or compaction memory injection to consume bounded layered wake-up output
- [x] 3.4 Update the OpenCode plugin hooks so startup, compacting, idle, and continuation flows actively consume hosted memory lifecycle outputs
- [x] 3.5 Expose hosted memory triage through the public memory capability or equivalent MCP-facing route

## 4. Dashboard And Verification

- [x] 4.1 Extend dashboard memory inspection with lifecycle health, density, maintenance summary, wake-up composition, and lifecycle change indicators
- [x] 4.2 Extend dashboard or admin inspection with triage previews, applied-action summaries, and suspected junk/test/demo-memory visibility
- [x] 4.3 Add focused server and client tests for scoring, wake-up selection, supersession, triage safety, and idempotent replay
- [x] 4.4 Validate the change with targeted `cargo test`, `dotnet test`, and hosted replay/triage checks
