# 0.8.0 Release Bump And Changelog Design

## Goal

Publish the current public MCP surface reduction work as `0.8.0` with a full, technical changelog that clearly marks the release as a breaking public contract change.

## Scope

- Bump the three required version locations to `0.8.0`
- Regenerate `Cargo.lock` so the client package version is in sync
- Add a new `CHANGELOG.md` entry for `0.8.0`
- Document the public 5-tool MCP surface change, routing cleanup, and active guidance cleanup
- Include explicit upgrade notes for prompts, rules, and client integrations that still refer to old public tool names

## Release Positioning

`0.8.0` is the right release boundary because the public MCP contract changed in a breaking way:

- the public surface is now exactly five tools
- public `ctx(tool=...)` usage was removed in favor of `ctx(domain, action, ...)`
- `ctx_read` and `ctx_search` now absorb functionality that used to appear as separate public tools
- runtime instructions, templates, and docs were updated to match the new model

This should be presented as a deliberate simplification release rather than as a normal maintenance bump.

## Changelog Structure

The new `0.8.0` section should stay consistent with the repo's existing plain Markdown style, but it should be more detailed than earlier entries. It should contain:

- a short opening line framing the release as the public MCP surface simplification release
- a `Breaking changes` subsection
- a `Client and routing` subsection
- a `Docs and guidance` subsection
- an `Upgrade notes` subsection

## Content To Capture

### Breaking changes

- Public MCP surface is now exactly:
  - `ctx_read`
  - `ctx_search`
  - `ctx_tree`
  - `ctx_shell`
  - `ctx`
- Public `ctx(tool=...)` calls are no longer accepted
- Public semantic search is now reached through `ctx_search(mode="semantic")`
- Public symbol, outline, archive, and multi-file reads are now reached through `ctx_read(target=...)`

### Client and routing

- Rust client now enforces the 5-tool public manifest, tool list, and HTTP listing
- `ctx_read(target="symbol", path=...)` path scoping is correctly translated into symbol file scoping
- Public analytics actions are translated into valid internal handlers instead of exposing mismatched action names
- Public memory recall/store flows now route through supported public memory behavior instead of the server-only `ctx_brain` path
- Public runtime instructions no longer leak private `ctx_edit`

### Docs and guidance

- README and `docs/TOOLS.md` now describe only the 5-tool public surface
- Active templates and rule sources now recommend only the public surface in user-facing guidance
- Internal/private tool names remain documented only where they matter as implementation details

### Upgrade notes

- Replace public `ctx(tool="...")` usage with `ctx(domain="...", action="...", ...)`
- Replace old public semantic-search tool references with `ctx_search(mode="semantic", ...)`
- Replace old public multi-read/symbol/outline/archive tool references with `ctx_read(target=..., ...)`
- Update any prompt/rule packs that still recommend old public `ctx_*` tools directly

## Verification

Before calling the release-note update complete, verify:

- the three version locations all say `0.8.0`
- `Cargo.lock` is updated
- relevant client contract tests still pass
- the new changelog section is at the top of `CHANGELOG.md`
