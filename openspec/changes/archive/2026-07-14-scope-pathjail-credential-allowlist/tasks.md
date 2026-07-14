## 1. Narrow the allow-list

- [ ] 1.1 Add a `SESSION_STATE_SUBDIR: &str = "session-state"` constant with a doc comment explaining why the whole IDE config directory must never be allow-listed
- [ ] 1.2 Change the loop in `allow_paths_from_env()` to join `SESSION_STATE_SUBDIR` onto each `IDE_CONFIG_DIRS` entry before checking `.exists()`
- [ ] 1.3 Verify: `cargo test --manifest-path client/Cargo.toml pathjail` → `allows_copilot_session_state_under_home` still passes

## 2. Regression test

- [ ] 2.1 Add `rejects_sibling_file_outside_session_state_subdir` to the same `#[cfg(test)] mod tests` block, modeled on `allows_copilot_session_state_under_home`, proving a `.credentials.json` sibling file is rejected while a `session-state` file under the same tool directory is still allowed
- [ ] 2.2 Verify: `cargo test --manifest-path client/Cargo.toml pathjail` → 8 tests pass (6 existing + 1 new), 0 failed

## 3. Full verification

- [ ] 3.1 `cargo test --manifest-path client/Cargo.toml` (full suite) → all pass
- [ ] 3.2 `cargo clippy --manifest-path client/Cargo.toml --all-targets` → no new warnings beyond the pre-existing baseline
- [ ] 3.3 `git status` shows only `client/src/core/pathjail.rs` changed
