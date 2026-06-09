# Changelog

## 0.10.3

- Bundle the client release that rewrites Copilot hook installs to the working `PreToolUse` / `PostToolUse` / `PostSession` hooks.json schema and preserves unrelated custom hook events during refresh.

## 0.10.2

- Bundle the client/server release that fixes duplicate-basename `ctx_read` labels by showing project-relative paths and adds execution-tier cost summaries so cheap-first routing savings are visible in reports.

## 0.10.1

- Fix Copilot CLI hooks to include a `powershell` runner key alongside `bash` so hooks work on Windows without requiring bash in PATH. Also removes the stale `ctx_shell` entry from the MCP config `autoApprove` list.

## 0.10.0

- Bundle the client/server release that drops `ctx_shell` from the public MCP surface. The add-on now exposes exactly four public tools (`ctx_read`, `ctx_search`, `ctx_tree`, `ctx`); shell execution continues to flow through the native `Shell` / `Bash` path with the nebu-ctx shell hook compressing output.

## 0.9.0

- Bundle the client/server release that ships `memory-system-enhancements`: `nebu-ctx memory list` / `lifecycle` / `export` / `import`, contextual memory surfacing via Claude/Copilot hooks, brain-to-knowledge correlation with promotion traces, accurate session tool-call tracking, and the new `MemoryList`/`MemoryListItem`/`PromotionTrace` MCP contracts. Adds 6 `knowledge_entries` columns + Postgres migration for promotion provenance.

## 0.8.38

- Bundle the client/server follow-up that removes normalized legacy hosted session-summary rows even when old raw brain entries were already reshaped into `fact/general` records with legacy metadata.

## 0.8.37

- Bundle the client/server follow-up that stops legacy hosted session-summary sync, removes old `session-*` / `assistant-output-*` / `user-prompt-*` brain rows during maintenance, hides legacy rows from the canonical dashboard memory stream, and fixes empty-project deletion when durable memory candidates still exist.

## 0.8.36

- Bundle the client/server release that adds hosted memory maintenance, removes the old 1000-row maintenance scan cap, stops raw local journal sync from polluting hosted brain memory, and deletes legacy raw timeline rows during cleanup.

## 0.8.35

- Bundle the client release that adds a Copilot hook guard to block the known deferred-tool `multi_tool_use.parallel` crash path and steer batch reads back to public `ctx_*` calls.

## 0.8.34

- Bundle client release that refreshes generated Copilot/public guidance to avoid the known deferred-tool `multi_tool_use.parallel` wrapper failure by steering agents to direct public `ctx_*` calls and `ctx_read(target="files", paths=[...])` batch reads.

## 0.8.32

- Bundle the client release that strips more dead private `ctx_*` client surface, finishes the 4-agent setup/rules cleanup, and keeps public `ctx(...)` routes working for analytics feedback and context prefetch flows.

## 0.8.33

- Bundle the client/server release that fixes hosted MCP discovery drift, preserves raw piped `nebu-ctx -c` JSON output, adds duplicate-aware `report-issue` automation, and keeps hosted memory promotion and wake-up flows project-scoped and stable.

## 0.8.31

- Bundle the client/server release that makes public `ctx_search` regex behavior ripgrep-compatible, centralizes public guidance policy, and auto-files or updates reproducible public-tool bug reports.

## 0.8.30

- Bundle the client release that cleans up `nebu-ctx doctor` by removing the noisy `config.toml` and dead `Dashboard port 3333` checks.

## 0.8.29

- Bundle the client/server release that adds hosted durable memory lifecycle upkeep, bounded wake-up selection, replay-safe promoted-memory sync, and dashboard review flows for durable memory candidates.

## 0.8.28

- Bundle the client/server fix that canonicalizes `ctx_shell` overrides as `shell_path` and makes hosted MCP responses clearer when public metadata-only tools are called through `/v1/tools/call`.

## 0.8.27

- Bundle follow-up client cleanup from the 0.8.26 release line so add-on users get the same project bootstrap, workspace memory/session, and path-safe MCP handling polish as the standalone client.

## 0.8.26

- Bundle client/server release that rewrites lingering legacy `lean-ctx` MCP aliases to `nebu-ctx` and refreshes Kiro steering guidance to the public 5-tool surface.

## 0.8.25

- Bundle client/server release that preserves exact git inspection output for `git status --short/--porcelain` and `git diff --name-only/--name-status/--stat/--numstat` in wrapper-driven commit workflows.

## 0.8.24

- Bundle client/server release that makes `ctx_shell` report the actual shell used for each call and supports per-call shell overrides.
- Ship updated nebu-ctx guidance that tells agents to open GitHub issues automatically for reproducible public `ctx_*` / `ctx(...)` bugs.

## 0.8.23

- Bundle the client release that fixes VS Code / Copilot MCP registration by switching Copilot-facing MCP config to the camelCase `nebuCtx` server key.
- Keep add-on shipped MCP behavior aligned with the main client release by migrating legacy Copilot config aliases during setup.
