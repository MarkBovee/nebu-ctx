## Why

`client/src/core/pathjail.rs`'s `IDE_CONFIG_DIRS` allow-list (`.claude`, `.cursor`, `.copilot`, etc.) grants the entire home-directory config folder for each tool as an alternate root the path jail accepts, with no filename/subpath restriction. This is wired to the live, MCP-exposed `ctx_read` tool, so `ctx_read(path="~/.claude/.credentials.json")` succeeds today, disclosing another CLI tool's live OAuth credential file to whatever agent is driving the session — a realistic prompt-injection-driven exfiltration path, since the calling agent (not a human) decides which paths to read. The only actual, tested purpose of this allow-list is letting nebu-ctx read its own prior session-state artifacts from each tool's home directory (see the existing `allows_copilot_session_state_under_home` test, which only ever exercises `<tool>/session-state/...`).

## What Changes

- Narrow the `IDE_CONFIG_DIRS` allow-list in `allow_paths_from_env()` to only the `session-state` subdirectory of each tool's config directory, via a new `SESSION_STATE_SUBDIR` constant.
- Add a regression test proving both the fix (a sibling credentials file is now rejected) and the non-regression (the existing session-state test still passes unchanged).
- **Not BREAKING**: the one documented, tested use case (reading nebu-ctx's own session-state artifacts) is preserved exactly; only the unintended whole-directory grant is removed.

## Capabilities

### New Capabilities
- `path-safety`: defines the constraint that nebu-ctx's cross-tool config-directory allow-list only ever grants access to each tool's own `session-state` subdirectory, never the whole config directory (which may contain that tool's live credentials).

### Modified Capabilities

## Impact

- **Code**: `client/src/core/pathjail.rs` only.
- **Tests**: new regression test in the same file's `#[cfg(test)] mod tests` block.
- **Out of scope**: `client/src/tools/mod.rs` (read-only reference to confirm `jail_path` reachability, not modified), the `NEBU_CTX_ALLOW_PATH`/`LCTX_ALLOW_PATH` explicit operator-opted-in env-var branch (different trust model, left as-is), and a filename-based credential denylist (considered, deferred — the structural fix already makes the concrete exploit unreachable).
- Full technical detail (exact current code, exploit path, and exact diff) already captured in `plans/002-scope-pathjail-credential-allowlist.md` — this proposal is the OpenSpec-tracked counterpart of that plan.
