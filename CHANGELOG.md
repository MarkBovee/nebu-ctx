# Changelog

## 0.8.32

- Reduce the Rust client further toward a thin public boundary by physically removing dead private `ctx_*` tool-definition surface, legacy handoff/edit/delta dispatch paths, and stale internal guidance that still advertised non-public tools.
- Finish the 4-agent install/rules cleanup by removing leftover Gemini rule injection and aligning setup tests, workflow allowlists, and generated guidance with `claude`, `codex`, `copilot`, and `opencode` only.
- Keep public `ctx(domain=analytics, action="feedback")` and `ctx(domain=context, action="prefetch")` routes functional while migrating internal eval coverage off direct private tool names.

## 0.8.33

- Fix hosted HTTP MCP discovery so `/v1/manifest` and `/v1/tools` advertise only executable hosted tools, avoiding `invoke` failures when clients call metadata-only public tools directly.
- Make direct `nebu-ctx -c` preserve raw JSON/stdout for non-TTY and inline consumer pipes, and add `report-issue` automation that searches duplicates, updates matching issues, or creates drafts/submissions through `gh`.
- Keep project-scoped memory flows isolated by removing `ctx_search` auto-context prepend, scoping wake-up/preload/read session lookups to current project, and restoring canonical knowledge promotion plus integration-test rate-limit retries.

## 0.8.31

- Make public `ctx_search` regex mode use a ripgrep-compatible engine so valid matches no longer collapse into false `0 matches` responses, and return explicit timeout or invalid-regex failures when search cannot be trusted.
- Centralize public nebu-ctx guidance in one canonical renderer so instructions, injected rules, and hook-managed templates stay aligned on the 5-tool contract and no-bypass bug handling.
- Add automation-first `report-issue` duplicate detection and update/create flows so reproducible public-tool bugs can open or reuse GitHub issues without waiting for a separate user prompt.

## 0.8.30

- Strip noisy `config.toml` and dead `Dashboard port 3333` checks from `nebu-ctx doctor`, and remove unused `TcpListener` import with stale comment numbering.

## 0.8.29

- Add hosted durable memory lifecycle management with bounded wake-up selection, canonical promotion and consolidation routes, lifecycle upkeep metadata, and replay-safe promoted-memory batches.
- Add server-backed durable memory candidate review flow plus dashboard inspection and review actions for queued candidates, lifecycle health, wake-up composition, and triage visibility.
- Archive completed OpenSpec changes for project bootstrap, brain-facts memory routing, durable memory candidates, and optimized memory lifecycle behavior.

## 0.8.28

- Make the public `ctx_shell` contract use `shell_path` as the canonical override parameter while still accepting legacy `shell` client-side, avoiding tool-search/discovery drift around shell override calls.
- Harden the hosted HTTP MCP endpoint so metadata-only public tools such as `ctx_shell` return a clear client-routing error instead of pretending to be directly executable from `/v1/tools/call`.
- Add regression coverage for `ctx_shell` fish overrides, public schema export, and hosted manifest/tool-call behavior.

## 0.8.27

- Roll up the remaining client cleanup that accompanied the 0.8.26 alias-drift fix, covering project bootstrap CLI polish, workspace-scoped memory/session sync readability cleanup, path-safe MCP read handling, and related shell/uninstall test updates.

## 0.8.26

- Remove remaining legacy MCP alias drift in shared agent config writers and Crush setup so stale `lean-ctx` entries are rewritten to `nebu-ctx` instead of lingering beside the canonical server key.
- Refresh Kiro steering guidance to the public 5-tool surface and version the generated steering file so old `mcp_lean_ctx_*` references are replaced on the next setup run.
- Add regression coverage for the alias migration and Kiro steering refresh paths to keep `multi_tool_use.parallel`-style tool discovery failures from creeping back in through non-Copilot installs.

## 0.8.25

- Preserve exact output for git inspection commands in wrapper flows, including `git status --short/--porcelain` and `git diff --name-only/--name-status/--stat/--numstat`, so staging and commit review steps can trust the real file list.
- Keep normal git patch compression behavior for full diffs while adding regression coverage for wrapper and `ctx_shell` inspection paths.

## 0.8.24

- Make `ctx_shell` expose the actual shell used for each call via a `[shell: ...]` header and add a per-call `shell` override so Windows PowerShell/cmd semantics are visible and controllable.
- Update public tool definitions, docs, and agent guidance so `ctx_shell` behavior is explicit across platforms.
- Teach nebu-ctx guidance/rule templates to automatically file a GitHub issue in `MarkBovee/nebu-ctx` when agents hit a reproducible public `ctx_*` / `ctx(...)` bug.
- Clarify release-flow instructions that every version bump must include release notes in both changelog files.

## 0.8.23

- Fix VS Code / Copilot MCP config to register the server under the camelCase `nebuCtx` key, which avoids invalid tool identifiers like `mcp_nebu-ctx_*` that broke `multi_tool_use.parallel` after tool discovery.
- Migrate existing VS Code / Copilot MCP configs from legacy `nebu-ctx` and `lean-ctx` keys to `nebuCtx` while keeping doctor/setup/uninstall compatibility.
- Add regression coverage for Copilot MCP config migration and uninstall cleanup.

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
