---
name: nebula-bump
description: Bump nebula-ctx version across all files, write release notes, validate sync, commit and push. Auto-triggers when user says "bump version", "bump to X.Y.Z", "release", or "new version".
---

# nebula-ctx Version Bump

## Trigger

Use this skill when the user:
- Says "bump version", "bump to X.Y.Z", "bump and push"
- Says "release", "new release", "new version"
- Asks to version/release/publish nebula-ctx

## Version files to update (ALL must match)

| File | Field | Format |
|------|-------|--------|
| `client/Cargo.toml` | `version = "X.Y.Z"` | bare semver |
| `homeassistant/config.yaml` | `version: "X.Y.Z"` | bare semver |
| `server/src/NebuCtx.Server.Core/ToolRegistry.cs` | `ServerVersion.Current = "X.Y.Z"` | bare semver |

## Release notes to update (required on every bump)

| File | Purpose |
|------|---------|
| `CHANGELOG.md` | Main repo/client/server release notes for `X.Y.Z` |
| `homeassistant/CHANGELOG.md` | Home Assistant add-on release notes for `X.Y.Z`, even when the change is client-focused |

## Steps

1. **Determine new version**
   - If user specified version (e.g. "bump to 0.3.0"), use that
   - Otherwise, read current version from `homeassistant/config.yaml` and patch-bump (increment last digit)
   - Confirm the version with the user before proceeding

2. **Update all 3 version files**
   - Edit `client/Cargo.toml`: `version = "X.Y.Z"`
   - Edit `homeassistant/config.yaml`: `version: "X.Y.Z"`
   - Edit `server/src/NebuCtx.Server.Core/ToolRegistry.cs`: `ServerVersion.Current = "X.Y.Z"`

3. **Write release notes**
   - Add `X.Y.Z` entry to `CHANGELOG.md`
   - Add `X.Y.Z` entry to `homeassistant/CHANGELOG.md`

4. **Update Cargo.lock**
   - Run `cargo update --manifest-path client/Cargo.toml`

5. **Validate sync**
   - Read all 3 files and confirm versions match
   - If any mismatch, STOP and report the issue

6. **Commit**
   - Stage all 3 version files + both changelogs + Cargo.lock
   - Commit message: `Bump to X.Y.Z`

7. **Push**
   - `git push origin main`
   - Auto-release workflow will tag `vX.Y.Z`, build binaries, and publish release

## What happens automatically after push

1. `.github/workflows/auto-release.yml` detects version file changes
2. Extracts version, creates tag `vX.Y.Z`, pushes tag
3. `.github/workflows/release.yml` builds amd64 + arm64 binaries
4. GitHub Release created with binaries
5. HA addon pulls binary from release on next rebuild

## Safety checks

- NEVER push if `client/Cargo.toml`, `homeassistant/config.yaml`, and `ToolRegistry.cs` versions don't match
- NEVER skip changelog updates in both `CHANGELOG.md` and `homeassistant/CHANGELOG.md`
- NEVER skip `cargo update --manifest-path client/Cargo.toml`
- If `cargo check` fails, do NOT push — fix first
