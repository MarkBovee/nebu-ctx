## 1. Rust client contract

- [x] 1.1 In `client/src/mcp_server/mod.rs`, reduce `PUBLIC_TOOL_NAMES` to `["ctx_read", "ctx_search", "ctx_tree", "ctx"]` and update the invalid-params error message to mention only the four remaining public tools.
- [x] 1.2 Update the `public_tool_count_is_exactly_five` test (and any other count/name assertion) to expect four tools and the new name list.
- [x] 1.3 Remove the `ctx_shell_public_schema_uses_shell_path_and_not_shell` test, the `ctx_shell_does_not_prepend_auto_context` test, the `ctx_shell_allows_per_call_shell_override` test, and the `ctx_shell_allows_per_call_fish_override` test.
- [x] 1.4 Update the `unified_tool_count` test assertion from 5 to 4.
- [x] 1.5 Update the `public_manifest_contains_only_public_tools` assertion that checks the manifest tool count from 5 to 4.

## 2. Rust client implementation removal

- [x] 2.1 Delete `client/src/tools/ctx_shell.rs` entirely.
- [x] 2.2 Remove the `pub mod ctx_shell;` line from `client/src/tools/mod.rs` and any related `use` statements.
- [x] 2.3 In `client/src/mcp_server/dispatch.rs`, remove the `"ctx_shell" => { ... }` arm and any helper code that calls `ctx_shell::validate_command` or `ctx_shell::handle`.
- [x] 2.4 In `client/src/shell.rs`, remove the call to `crate::tools::ctx_shell::normalize_command_for_shell` and the `ctx_shell::contains_auth_flow` check; inline or drop the now-unused logic.
- [x] 2.5 In `client/src/mcp_server/execute.rs`, drop the `crate::tools::ctx_shell::normalize_command_for_flag` call; use the shell flag directly or remove the helper if no longer needed.
- [x] 2.6 In `client/src/hook_handlers.rs`, remove the comment line that lists `ctx_shell` as a tool preference.
- [x] 2.7 In `client/src/core/workflow/types.rs`, remove the two `tool:ctx_shell` evidence references and any step that requires evidence from `ctx_shell`.
- [x] 2.8 In `client/src/core/stats.rs`, remove the two messages that reference `ctx_shell` in the "agent may be using native" hint.
- [x] 2.9 In `client/src/core/editor_registry/writers.rs`, remove `ctx_shell` from the `vec!["ctx_read", ..., "ctx_shell", ...]` list and from the `assert!(tools.contains(&"ctx_shell"))` test.
- [x] 2.10 In `client/src/instructions.rs`, remove the `ctx_shell over Shell` line in the tool preference string.
- [x] 2.11 In `client/src/core/loop_detection.rs`, remove the `record_call("ctx_shell", ...)` test calls and the `assert!(!LoopDetector::is_search_tool("ctx_shell"))` assertion.
- [x] 2.12 In `client/src/core/patterns/git.rs`, update the diff-output test fixture to no longer mention `client/src/tools/ctx_shell.rs`.
- [x] 2.13 In `client/src/bin/seed_observatory.rs`, remove the `("ctx_shell", ...)` benchmark rows and the `Some("ctx_shell")` register row.

## 3. Rust tool definitions and guidance

- [x] 3.1 In `client/src/tool_defs/mod.rs`, remove `"ctx_shell"` from `CORE_TOOL_NAMES` and from the case in `tool_def_for` that returns its definition.
- [x] 3.2 In `client/src/tool_defs/granular.rs`, remove the `"ctx_shell"` entry from the granular tool enumeration.
- [x] 3.3 In `client/src/public_guidance.rs`, remove every mention of `ctx_shell` in the static guidance strings, the public surface enumeration, the issue-failure policy paragraph, the example call list, and the assertion `assert!(text.contains("ctx_shell"))`.

## 4. Rust tests cleanup

- [x] 4.1 Delete `client/tests/shell_and_agent_tests.rs` (all three tests are about `ctx_shell` shell override behavior).
- [x] 4.2 In `client/src/rules_inject.rs`, remove or rewrite the assertion `assert!(content.contains("ctx_shell"))` so it does not expect a removed tool.
- [x] 4.3 In `tests/intensive_benchmarks.rs`, remove the `"ctx_shell"` entry from the benchmark tool list.

## 5. .NET server changes

- [x] 5.1 In `server/src/NebuCtx.Server.Core/ToolRegistry.cs`, remove `"ctx_shell"` from `MetadataOnlyPublicToolNames` and from the `case "ctx_shell" => new ToolDefinition { ... }` branch.
- [x] 5.2 In `server/tests/NebuCtx.IntegrationTests/McpEndpointTests.cs`, remove the four tests that reference `ctx_shell`: the two hosted-endpoint rejection tests, the public manifest test, and the public-tools-not-advertised test.
- [x] 5.3 In `server/src/NebuCtx.Server.Host/Dashboard/dashboard.html`, update the empty-state message so it no longer mentions `ctx_shell`.

## 6. Documentation and changelogs

- [x] 6.1 In `README.md`, remove the `ctx_shell` row from the public tools table, the `### ctx_shell` section, and any prose paragraph that references it.
- [x] 6.2 In `CHANGELOG.md`, add a new entry for this change under a "Removed" heading that records the removal of `ctx_shell` from the public surface; leave historical entries untouched.
- [x] 6.3 In `homeassistant/CHANGELOG.md`, add a corresponding add-on changelog entry that bundles the same `ctx_shell` removal.
- [x] 6.4 In `.claude/rules/nebu-ctx.md`, remove the `ctx_shell` row from the tool mapping table and any paragraph that recommends `ctx_shell` over the native shell.

## 7. OpenSpec and root docs

- [x] 7.1 In `openspec/specs/public-guidance/spec.md`, update the scenario in the issue-filing requirement to enumerate only `ctx_read`, `ctx_search`, `ctx_tree`, and `ctx` (no `ctx_shell`).
- [x] 7.2 In `AGENTS.md`, update the `Public MCP surface` line to list only the four remaining public tools and the tool-routing architecture block accordingly.

## 8. Verification

- [x] 8.1 Run `cargo test --manifest-path client/Cargo.toml` and ensure it passes with no remaining references to `ctx_shell` in source or test code.
- [x] 8.2 Run `dotnet test server/NebuCtx.slnx -p:AllowMissingPrunePackageData=true` and ensure all tests pass with the removed `ctx_shell` cases.
- [x] 8.3 Run `rg "ctx_shell" client server tests openspec README.md CHANGELOG.md homeassistant/CHANGELOG.md AGENTS.md .claude/` and confirm that the only remaining matches (if any) are in historical changelog entries explicitly marked as past releases.
- [x] 8.4 Run `openspec status --change "remove-ctx-shell"` and confirm the change is ready to archive after implementation.
