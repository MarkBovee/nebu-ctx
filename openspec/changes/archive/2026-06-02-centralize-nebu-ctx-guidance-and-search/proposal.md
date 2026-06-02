## Why

`ctx_search` can return misleading zero-match results because it uses a bespoke scanner instead of ripgrep-grade search behavior, and nebu-ctx guidance about public-tool failures is duplicated across several instruction surfaces. That combination breaks trust in the public command layer right where the product is supposed to be strongest.

## What Changes

- Replace the current custom regex search path behind `ctx_search` with a ripgrep-backed implementation that keeps the existing public contract but aligns matching behavior with ripgrep semantics.
- Introduce a single canonical guidance policy for the public 5-tool surface, failure handling, and fallback rules, then render all instruction/rules outputs from that central policy instead of maintaining drift-prone copies.
- Extend `nebu-ctx report-issue` so agents can submit reproducible public-tool bugs non-interactively, search for duplicates first, and update an existing issue instead of creating noisy duplicates.
- Update public guidance so agents automatically file or update a nebu-ctx issue for reproducible public-tool bugs before handoff, without waiting for explicit user prompting.

## Capabilities

### New Capabilities
- `public-guidance`: Central authoritative guidance generation for the public nebu-ctx tool surface and reproducible-failure policy.
- `ripgrep-backed-search`: Public `ctx_search` regex behavior backed by ripgrep-compatible matching and failure semantics.
- `issue-auto-reporting`: Non-interactive, duplicate-aware issue filing for reproducible public nebu-ctx tool failures.

### Modified Capabilities
- None.

## Impact

- Affected client code: `client/src/tools/ctx_search.rs`, `client/src/mcp_server/dispatch.rs`, `client/src/instructions.rs`, `client/src/rules_inject.rs`, `client/src/hooks/mod.rs`, `client/src/hooks/agents.rs`, `client/src/report.rs`, and related CLI/help surfaces.
- Likely dependency impact: add ripgrep crates or equivalent embedded ripgrep components to the client crate.
- User-visible behavior impact: `ctx_search` results become more trustworthy, public guidance becomes consistent across editors, and issue reporting becomes automatic for reproducible public-tool bugs.
- Test impact: new regression coverage for search parity, guidance rendering consistency, and auto issue filing / duplicate handling.
