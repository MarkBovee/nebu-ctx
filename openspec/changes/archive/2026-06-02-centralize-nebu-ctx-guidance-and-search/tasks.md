## 1. Ripgrep-backed Search Foundation

- [x] 1.1 Replace the bespoke regex search path in `client/src/tools/ctx_search.rs` with a ripgrep-compatible search adapter that preserves the current public `ctx_search` contract.
- [x] 1.2 Refactor `ctx_search` internals to collect structured match results before formatting compact output, so formatting cannot silently convert failures into `0 matches`.
- [x] 1.3 Update `client/src/mcp_server/dispatch.rs` error and timeout handling so invalid or unreliable search execution returns explicit failure responses instead of misleading empty-success output.

## 2. Central Public Guidance Policy

- [x] 2.1 Introduce a canonical public-guidance policy module that owns tool mapping, no-bypass rules, and reproducible public-tool failure behavior.
- [x] 2.2 Convert `client/src/instructions.rs` and `client/src/rules_inject.rs` to render from the canonical policy instead of maintaining independent source text.
- [x] 2.3 Convert hook-managed guidance surfaces in `client/src/hooks/mod.rs`, `client/src/hooks/agents.rs`, and any dependent templates to use the canonical policy renderer while preserving per-client formatting constraints.

## 3. Automatic Issue Reporting

- [x] 3.1 Extend `client/src/report.rs` with non-interactive automation flags, machine-usable outcomes, and preserved interactive mode for manual users.
- [x] 3.2 Implement duplicate-aware issue lookup and existing-issue update/comment behavior for automation mode before creating a new issue.
- [x] 3.3 Update CLI/help surfaces and guidance rendering so reproducible public nebu-ctx tool bugs automatically route through the new issue reporting flow.

## 4. Validation

- [x] 4.1 Add regression tests for ripgrep-backed `ctx_search`, including the false-zero-match bug class, invalid regex handling, and ignore-mode behavior.
- [x] 4.2 Add tests for canonical guidance rendering to ensure all supported instruction surfaces share the same policy semantics.
- [x] 4.3 Add tests for automated `report-issue` submission and duplicate handling, then run `cargo check --manifest-path client/Cargo.toml` and targeted client tests before widening as needed.
