# Changelog

## 0.8.24

- Bundle client/server release that makes `ctx_shell` report the actual shell used for each call and supports per-call shell overrides.
- Ship updated nebu-ctx guidance that tells agents to open GitHub issues automatically for reproducible public `ctx_*` / `ctx(...)` bugs.

## 0.8.23

- Bundle the client release that fixes VS Code / Copilot MCP registration by switching Copilot-facing MCP config to the camelCase `nebuCtx` server key.
- Keep add-on shipped MCP behavior aligned with the main client release by migrating legacy Copilot config aliases during setup.
