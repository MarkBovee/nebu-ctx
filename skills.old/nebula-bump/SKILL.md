---
name: nebula-bump
description: Bump nebula-ctx version across all files, validate sync, commit and push. Auto-triggers when user says "bump version", "bump to X.Y.Z", "release", or "new version".
---

# nebula-ctx Version Bump

## Trigger

Use this skill when the user:
- Says "bump version", "bump to X.Y.Z", "bump and push"
- Says "release", "new release", "new version"
- Asks to version/release/publish nebula-ctx

## Files to update (ALL must match)

| File | Field | Format |
|------|-------|--------|
| `Cargo.toml` | `version = "X.Y.Z"` | bare semver |
| `homeassistant/config.yaml` | `version: "X.Y.Z"` | bare semver |
| `homeassistant/build.yaml` | `NEBULA_CTX_VERSION: "vX.Y.Z"` | v-prefixed semver |

## Steps

1. **Determine new version**
   - If user specified version (e.g. "bump to 0.3.0"), use that
   - Otherwise, read current version from `homeassistant/config.yaml` and patch-bump (increment last digit)
   - Confirm the version with the user before proceeding

2. **Update all 3 files**
   - Edit `Cargo.toml`: `version = "X.Y.Z"`
   - Edit `homeassistant/config.yaml`: `version: "X.Y.Z"`
   - Edit `homeassistant/build.yaml`: `NEBULA_CTX_VERSION: "vX.Y.Z"`

3. **Update Cargo.lock**
   - Run `cargo check -p nebula-ctx` to sync Cargo.lock

4. **Validate sync**
   - Read all 3 files and confirm versions match
   - If any mismatch, STOP and report the issue

5. **Commit**
   - Stage all 3 version files + Cargo.lock
   - Commit message: `Bump to X.Y.Z`

6. **Push**
   - `git push origin main`
   - Auto-release workflow will tag `vX.Y.Z`, build binaries, and publish release

## What happens automatically after push

1. `.github/workflows/auto-release.yml` detects config.yaml change
2. Extracts version, creates tag `vX.Y.Z`, pushes tag
3. `.github/workflows/release.yml` builds amd64 + arm64 binaries
4. GitHub Release created with binaries
5. HA addon pulls binary from release on next rebuild

## Safety checks

- NEVER push if Cargo.toml and config.yaml versions don't match
- NEVER skip `cargo check` — it validates the lock file
- If `cargo check` fails, do NOT push — fix first
