## Context

Three small, unrelated client-side issues surfaced during a full-repo audit: a UTF-8 byte-slice panic risk in directory-path display truncation, seven silent `let _ = ...` error-swallowing sites around session/registry persistence and rule injection, and two dead modules (`execute.rs`, `ctx_execute.rs`) with zero external callers, one of which also contains a real but unreachable shell-escaping bug.

## Goals / Non-Goals

**Goals:**
- Eliminate the panic risk with a minimal, named, testable helper rather than an inline fix.
- Restore a diagnostic trail for persistence/rule-injection failures without changing the fire-and-forget nature of those calls.
- Remove genuinely dead code rather than fixing a bug in code nothing calls.

**Non-Goals:**
- Fixing `ctx_execute.rs`'s shell-escaping bug — the module is being deleted, not repaired.
- Any other `let _ =` pattern elsewhere in the client crate not already enumerated (7 known sites) — if more are found during implementation, that's a scope-growth signal, not something to silently absorb.
- Any change to `client/src/tools/mod.rs`'s path-jail logic (separate concern, covered by `scope-pathjail-credential-allowlist`).

## Decisions

- **Extract a named helper (`truncate_dir_display`) rather than inlining a fix.** Matches the repo's existing small-function convention in the same file (`short_path`) and makes the fix independently unit-testable.
- **Delete dead code rather than fix it.** `execute.rs` already triggers 4 compiler `dead_code` warnings; `ctx_execute.rs`'s functions are `pub` so the lint doesn't fire, but a full-repo grep confirmed zero external callers of either module. Fixing the shell-escaping bug in unreachable code provides no benefit.
- **`tracing::warn!` over swallowing or propagating.** These calls happen in fire-and-forget/best-effort contexts (background rule injection, session autosave) where propagating an error further up would require a larger refactor; logging preserves today's resilient behavior while adding visibility.

## Risks / Trade-offs

- [Risk] Deleting `execute.rs`/`ctx_execute.rs` could break a caller the grep missed. → Mitigation: build failure would surface it immediately; STOP condition documented to report rather than resurrecting the modules blindly.
- [Risk] More than 7 `let _ = .*.save()` sites might exist. → Mitigation: STOP condition requires reporting additional sites found, rather than silently expanding scope.
