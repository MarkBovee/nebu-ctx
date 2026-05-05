# Changelog

## 0.8.0

This release makes the simplified public MCP contract the canonical `nebu-ctx` surface and ships the client, docs, and guidance cleanup needed to enforce it end-to-end.

### Breaking changes

- Reduce the public MCP surface to exactly five tools: `ctx_read`, `ctx_search`, `ctx_tree`, `ctx_shell`, and `ctx`.
- Remove public `ctx(tool="...")` usage in favor of `ctx(domain="...", action="...", ...)`.
- Move public semantic search behind `ctx_search(mode="semantic", ...)` instead of a separate public semantic-search tool.
- Move public multi-file, symbol, outline, and archive reads behind `ctx_read(target=..., ...)` instead of separate public read tools.

### Client and routing

- Make the Rust client the canonical contract boundary for the new 5-tool public MCP model across manifests, MCP tool listings, and HTTP tool listings.
- Route `ctx_read` targets and `ctx_search` modes through the existing internal handlers without re-exposing those handlers as public tools.
- Fix `ctx_read(target="symbol", path=...)` so the public `path` argument is translated into the file-scoped symbol lookup the internal handler actually expects.
- Align public analytics actions with valid internal handler actions so `ctx(domain="analytics", action="report")` and related flows work cleanly.
- Route public memory recall/store flows through supported public memory behavior instead of leaking the server-only `ctx_brain` path into user-facing guidance.
- Remove private `ctx_edit` recommendations from public runtime instructions so generated guidance matches the enforced public contract.

### Docs and guidance

- Rewrite `README.md` and `docs/TOOLS.md` around the 5-tool public MCP surface.
- Update active templates, rule sources, and generated guidance so user-facing instructions recommend only the public surface.
- Keep internal/private `ctx_*` names documented only where they matter as implementation details.

### Upgrade notes

- Replace public `ctx(tool="...")` calls with `ctx(domain="...", action="...", ...)`.
- Replace old public semantic-search references with `ctx_search(mode="semantic", ...)`.
- Replace old public symbol, outline, archive, and multi-read references with `ctx_read(target=..., ...)`.
- Update any prompt packs, rules, or MCP client instructions that still recommend old public `ctx_*` tool names directly.

### Verification

- Add and pass focused client contract tests covering the 5-tool public surface, `ctx(domain, action)` enforcement, symbol path scoping, analytics action mapping, memory recall routing, and runtime instruction cleanup.

## 0.7.8

- Add target-named release archives, including a Windows client asset, so `cargo binstall nebu-ctx` can use published binaries instead of local builds.
- Update client install docs and smoke coverage to prefer `cargo binstall`, keep release assets as the first fallback, and retain `cargo install` as the explicit source-build path.

## 0.7.5

- Fix client MCP path resolution for symlinked workspace aliases such as `/home/.../Work` resolving to `/mnt/work`, so VS Code / Copilot `ctx_*` tools no longer fail with `path escapes project root`.
- Canonicalize detected project roots before storing session state or deriving client caches, which keeps shell-driven sessions, semantic caches, and path jail checks aligned on the same real workspace root.

## 0.7.4

- Fix client installs so `cargo install` stays lightweight and does not require a native C toolchain.
- Improve OpenCode token savings reporting and context rules.

## 0.7.6

- Add a Windows MSVC linker fallback via `rust-lld` to reduce `link.exe` install failures.
- Keep the client install path lightweight and document Windows prerequisites.

## 0.7.3

- Add telemetry reporting for OpenCode shell compression.
- Strengthen nebu-ctx/OpenCode rules and fix missing `LEAN-CTX.md` reference.
