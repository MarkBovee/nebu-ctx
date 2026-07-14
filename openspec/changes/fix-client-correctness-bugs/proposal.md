## Why

Three independent, unrelated issues in the Rust client, bundled because each is small and none depends on the others: (1) `ctx_overview.rs` slices a `String` by byte index to truncate a long directory path for display, which panics at runtime when the computed offset doesn't land on a UTF-8 character boundary — a real risk since directory paths routinely contain non-ASCII characters; (2) seven call sites across `mcp_server/mod.rs` and `dispatch.rs` discard session/registry-save and rule-injection results via `let _ = ...`, silently swallowing any failure with no diagnostic trail; (3) `client/src/mcp_server/execute.rs` and `client/src/tools/ctx_execute.rs` are confirmed dead code (zero external callers, `cargo build` already emits 4 `dead_code` warnings for the former), and the latter additionally contains a real-but-unreachable shell-injection bug that would need fixing if ever resurrected.

## What Changes

- Extract a new `truncate_dir_display()` helper in `ctx_overview.rs` that truncates on a UTF-8 character boundary instead of a raw byte offset.
- Convert all 7 silent `let _ = session.save()` / `registry.save()` / `inject_all_rules(...)` sites to `tracing::warn!` on failure.
- Delete `client/src/mcp_server/execute.rs` and `client/src/tools/ctx_execute.rs` entirely, along with their `mod execute;` / `pub mod ctx_execute;` declarations.
- **Not BREAKING**: none of these are public API changes; the deleted modules have zero external callers (verified via full-repo grep).

## Capabilities

### New Capabilities
- `client-reliability`: defines that the client must not panic on valid-but-unusual filesystem input (e.g. non-ASCII directory names) and must surface persistence/rule-injection failures via logging rather than silently discarding them.

### Modified Capabilities

## Impact

- **Code**: `client/src/tools/ctx_overview.rs`, `client/src/mcp_server/mod.rs`, `client/src/mcp_server/dispatch.rs`, `client/src/mcp_server/execute.rs` (deleted), `client/src/tools/ctx_execute.rs` (deleted), `client/src/tools/mod.rs` (remove one `pub mod` line).
- **No public API/tool-surface change**: none of these are public API changes; the deleted modules have zero external callers (verified via full-repo grep). The `client-reliability` capability above tracks the underlying reliability property being restored, not a new user-facing feature.
- Full technical detail (exact current code, line numbers, and exact diffs) already captured in `plans/004-fix-client-correctness-bugs.md` — this proposal is the OpenSpec-tracked counterpart of that plan.
