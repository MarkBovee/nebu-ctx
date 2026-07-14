## 1. Fix UTF-8 byte-slice panic risk

- [x] 1.1 Add `truncate_dir_display(dir: &str, max_len: usize) -> String` helper in `ctx_overview.rs`, cutting on a UTF-8 char boundary via `char_indices()`
- [x] 1.2 Replace the inline `format!("...{}", &dir[dir.len() - 47..])` call site with `truncate_dir_display(dir, 50)`
- [x] 1.3 Add unit tests: multi-byte UTF-8 case (no panic, starts with `"..."`) and ASCII-only happy-path case
- [x] 1.4 Verify: `cargo build --manifest-path client/Cargo.toml` → 0 errors

## 2. Log silent persistence/rule-injection failures

- [x] 2.1 Convert all 4 `let _ = session.save();` sites in `mcp_server/mod.rs` to `if let Err(e) = session.save() { tracing::warn!(...) }`
- [x] 2.2 Convert both `let _ = registry.save();` sites (`mod.rs`, `dispatch.rs`) the same way
- [x] 2.3 Convert the `let _ = crate::rules_inject::inject_all_rules(&home);` site to check `inject_result.errors` and log via `tracing::warn!` if non-empty
- [x] 2.4 Verify: `grep -n "let _ = session.save()\|let _ = registry.save()\|let _ = crate::rules_inject::inject_all_rules" client/src/mcp_server/mod.rs client/src/mcp_server/dispatch.rs` → no matches

## 3. Remove dead code

- [x] 3.1 Delete `client/src/mcp_server/execute.rs` and remove `mod execute;` from `mcp_server/mod.rs`
- [x] 3.2 Delete `client/src/tools/ctx_execute.rs` and remove `pub mod ctx_execute;` from `client/src/tools/mod.rs`
- [x] 3.3 Verify: `cargo build --manifest-path client/Cargo.toml` → 0 errors, 0 warnings (the 4 pre-existing `dead_code` warnings are gone)
- [x] 3.4 Verify: `grep -rn "mod execute\b|ctx_execute" client/src --include="*.rs"` → no matches

## 4. Full verification

- [x] 4.1 `cargo test --manifest-path client/Cargo.toml` → all pass, including the new truncation tests
- [x] 4.2 `git status` shows only the in-scope files changed
