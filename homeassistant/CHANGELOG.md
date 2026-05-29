# Changelog

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
