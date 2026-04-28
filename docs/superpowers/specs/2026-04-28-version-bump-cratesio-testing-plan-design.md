# Design: v0.5.3 Version Bump, crates.io Publishing, HA Icon Fix, and Testing Plan

**Date:** 2026-04-28  
**Status:** Approved (autonomous)

---

## 1. Problem Statement

The codebase is at v0.5.2. Since that tag was set, 5 critical bugs were fixed and 2 features were added (all in the current session). This work needs to be:

1. Versioned as v0.5.3 across all three version locations
2. Released to crates.io so `cargo install nebu-ctx` installs the patched binary
3. Documented and scripted so future maintainers can reproduce the publish flow
4. Bundled with a fix for the missing HA addon sidebar icon
5. Followed by a structured testing and self-improvement plan

---

## 2. Scope

### 2a. Version Bump (0.5.2 → 0.5.3)

Three files must be kept in sync per AGENTS.md:

| File | Field | Current | Target |
|------|-------|---------|--------|
| `client/Cargo.toml` | `version` | `"0.5.2"` | `"0.5.3"` |
| `homeassistant/config.yaml` | `version:` | `"0.5.2"` | `"0.5.3"` |
| `server/src/NebuCtx.Application/ToolRegistry.cs` | `ServerVersion.Current` | `"0.5.2"` | `"0.5.3"` |

After bumping: run `bash scripts/server/refresh-dist.sh` and commit all four changes together.

### 2b. crates.io Publishing

**Decision:** Publish full crate (binary + lib `lean_ctx`).

Cargo.toml already has all required metadata: description, license, homepage, repository, documentation, readme, keywords, categories. No metadata changes needed.

**Release workflow addition (`release.yml`):**

Add a `publish` job that:
- Runs after `release` job completes
- Checks out code, installs Rust stable
- Runs `cargo publish --manifest-path client/Cargo.toml --token ${{ secrets.CARGO_REGISTRY_TOKEN }}`
- Uses `continue-on-error: false` (publish failures are blocking)
- Skips if the version is already published (ureq `--no-verify` is NOT used; we trust the crate)

**Required GitHub secret:** `CARGO_REGISTRY_TOKEN` — a crates.io API token. Must be added manually in repo Settings → Secrets.

**Auto-release workflow addition (`auto-release.yml`):**

Add a verify step that checks crates.io to warn (not block) if the version is already published there.

### 2c. Documentation and Scripts

**AGENTS.md** — add crates.io to the release flow section:
> After bumping all three version files and rebuilding dist, the tag push triggers `auto-release.yml` → `release.yml` which builds binaries, publishes the GitHub release, AND publishes to crates.io via `CARGO_REGISTRY_TOKEN` secret.

**HANDOVER.md** — update "What Is Not Done Yet" to reflect the new publish capability and note the secret requirement.

**`scripts/server/`** — no new scripts needed; the crates.io step lives entirely in CI.

### 2d. HA Addon Icon Fix

**Root cause:** No `icon.png` file exists in `homeassistant/`. HA requires a PNG icon for the sidebar panel to render reliably. The `panel_icon: "mdi:brain"` MDI string alone is unreliable across HA versions.

**Fix:**
1. Generate `homeassistant/icon.png` (128×128 PNG) — a simple branded icon in nebu-ctx's green/dark color scheme using a neural/brain motif. Generated programmatically with Python (no external assets).
2. Keep `panel_icon: "mdi:brain"` — MDI icon is used as fallback in some HA versions.

### 2e. Server and Client Update

After the version bump:
1. `bash scripts/server/refresh-dist.sh` — rebuild `server/dist/linux/` at v0.5.3
2. `podman build -t nebu-ctx-server:0.5.3` — rebuild local container
3. `cargo install --path client` — install v0.5.3 binary locally
4. Restart `nebu-ctx-eval` container with new image

---

## 3. Testing and Self-Improvement Plan

### 3a. Test Categories

| Category | Tools/Items | Current Status |
|----------|-------------|----------------|
| **Cloud tools** | ctx_brain, ctx_knowledge, ctx_session | ✅ Fixed Content-Type; needs E2E verification |
| **Local tools** | ctx_read, ctx_shell, ctx_overview, ctx_search | Not tested in this session |
| **Shell hooks** | -t tracking, -c compression | ✅ Fixed telemetry; needs manual test |
| **Dashboard** | All 22+ API endpoints | ✅ All 200 OK |
| **Dashboard UI** | All nav views render correctly | Partially checked |
| **Project resolution** | Client → server project binding | Not tested |
| **Session management** | ctx_session save/load | Not tested |
| **Knowledge management** | ctx_knowledge remember/recall/pattern/gotcha | Not tested |
| **MCP protocol** | Manifest, tool list, tool call schema | Not tested |

### 3b. Self-Improvement Cycle

For each test category:
1. **Test** — exercise the feature via MCP call or API
2. **Evaluate** — check response correctness, telemetry appears on dashboard
3. **Fix** — if broken, fix and rebuild
4. **Verify** — confirm fix works
5. **Improve** — if working, identify any UX, performance, or completeness improvements

### 3c. Priority Order

1. Cloud tools end-to-end (ctx_brain, ctx_knowledge, ctx_session) — critical path  
2. Shell hook wrappers (bash/fish/zsh integration) — high visibility  
3. Project resolution and binding flow  
4. Local tool smoke (ctx_read, ctx_shell, ctx_overview)  
5. Dashboard visual inspection of all nav views  
6. Session management via ctx_session  
7. MCP manifest/schema validation  

---

## 4. Constraints

- `CARGO_REGISTRY_TOKEN` secret must be set in GitHub before the publish step runs. Script and CI will fail gracefully with a clear error if missing.
- icon.png is generated programmatically — no external image assets committed.
- All three version locations must stay in sync; `auto-release.yml` already verifies Cargo ↔ HA config, but does NOT check ToolRegistry.cs — we add that check.

---

## 5. Out of Scope

- Changing the crate's public API surface
- Automated crates.io version conflict resolution
- Dashboard restyle or mobile layout
