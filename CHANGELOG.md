# Changelog

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
