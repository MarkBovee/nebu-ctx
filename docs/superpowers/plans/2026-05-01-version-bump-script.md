# Version Bump Script Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a small bash utility that bumps the repo version consistently and use it to move the repo to `0.7.2`.

**Architecture:** Keep the version bump logic in one focused shell script under `scripts/release/`, using exact text replacement against the three required version sources already enforced by CI. Validate inputs early, fail hard on unexpected file content, and verify the resulting version sync with targeted readback and existing release checks.

**Tech Stack:** Bash, Cargo, existing repo release/version files, existing GitHub Actions version sync rules

---

### Task 1: Add a focused version bump script

**Files:**
- Create: `scripts/release/bump-version.sh`
- Modify: `docs/superpowers/specs/2026-05-01-version-bump-script-design.md` only if implementation reveals a spec mismatch

- [ ] **Step 1: Write the failing command expectation**

Use these manual expectations as the failing contract before implementation:

```bash
bash scripts/release/bump-version.sh 0.7.2
```

Expected before implementation:
- command fails because `scripts/release/bump-version.sh` does not exist

- [ ] **Step 2: Verify the failing state**

Run: `test -f scripts/release/bump-version.sh`
Expected: non-zero exit status

- [ ] **Step 3: Write the minimal implementation**

Create `scripts/release/bump-version.sh` with logic equivalent to this structure:

```bash
#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
CLIENT_TOML="$ROOT_DIR/client/Cargo.toml"
ADDON_CONFIG="$ROOT_DIR/homeassistant/config.yaml"
TOOL_REGISTRY="$ROOT_DIR/server/src/NebuCtx.Server.Core/ToolRegistry.cs"

usage() {
    printf 'Usage: %s [x.y.z]\n' "${0##*/}" >&2
}

fail() {
    printf '[bump-version] %s\n' "$1" >&2
    exit 1
}

require_file() {
    [ -f "$1" ] || fail "Missing required file: $1"
}

validate_semver() {
    [[ "$1" =~ ^[0-9]+\.[0-9]+\.[0-9]+$ ]] || fail "Invalid version '$1' (expected x.y.z)"
}

extract_version() {
    local value
    value="$(grep '^version:' "$ADDON_CONFIG" | head -1 | sed 's/version: *"\(.*\)"/\1/' | tr -d '\r')"
    [ -n "$value" ] || fail "Could not read current version from $ADDON_CONFIG"
    validate_semver "$value"
    printf '%s\n' "$value"
}

next_patch_version() {
    local major minor patch
    IFS=. read -r major minor patch <<<"$1"
    printf '%s.%s.%s\n' "$major" "$minor" "$((patch + 1))"
}

replace_once() {
    local file="$1"
    local pattern="$2"
    local replacement="$3"
    local count
    count="$(grep -c "$pattern" "$file")"
    [ "$count" -eq 1 ] || fail "Expected exactly one match for pattern '$pattern' in $file"
    perl -0pi -e "$replacement" "$file"
}

main() {
    [ "$#" -le 1 ] || { usage; fail "Too many arguments"; }

    require_file "$CLIENT_TOML"
    require_file "$ADDON_CONFIG"
    require_file "$TOOL_REGISTRY"

    local old_version new_version
    old_version="$(extract_version)"

    if [ "$#" -eq 1 ]; then
        new_version="$1"
        validate_semver "$new_version"
    else
        new_version="$(next_patch_version "$old_version")"
    fi

    replace_once "$CLIENT_TOML" '^version = "[0-9]\+\.[0-9]\+\.[0-9]\+"$' "s/^version = \"[0-9]+\.[0-9]+\.[0-9]+\"$/version = \"$new_version\"/m"
    replace_once "$ADDON_CONFIG" '^version: "[0-9]\+\.[0-9]\+\.[0-9]\+"$' "s/^version: \"[0-9]+\.[0-9]+\.[0-9]+\"$/version: \"$new_version\"/m"
    replace_once "$TOOL_REGISTRY" 'public const string Current = "[0-9]\+\.[0-9]\+\.[0-9]\+";' "s/public const string Current = \"[0-9]+\.[0-9]+\.[0-9]+\";/public const string Current = \"$new_version\";/"

    cargo update --manifest-path "$CLIENT_TOML"

    printf '[bump-version] %s -> %s\n' "$old_version" "$new_version"
}

main "$@"
```

Make the script executable.

- [ ] **Step 4: Verify the script is present and executable**

Run: `test -x scripts/release/bump-version.sh`
Expected: zero exit status

### Task 2: Use the script to bump to 0.7.2

**Files:**
- Modify: `client/Cargo.toml`
- Modify: `homeassistant/config.yaml`
- Modify: `server/src/NebuCtx.Server.Core/ToolRegistry.cs`
- Modify: `Cargo.lock` if `cargo update` refreshes it

- [ ] **Step 1: Run the bump script with the target version**

Run: `bash scripts/release/bump-version.sh 0.7.2`
Expected: success output showing the old and new version

- [ ] **Step 2: Read back the three required version locations**

Run these commands:

```bash
grep '^version = ' client/Cargo.toml
grep '^version:' homeassistant/config.yaml
grep 'public const string Current = ' server/src/NebuCtx.Server.Core/ToolRegistry.cs
```

Expected:
- each command shows `0.7.2`

- [ ] **Step 3: Check git diff for only the expected bump-related changes**

Run: `git diff -- client/Cargo.toml homeassistant/config.yaml server/src/NebuCtx.Server.Core/ToolRegistry.cs Cargo.lock scripts/release/bump-version.sh`
Expected: only the new script and the version bump changes appear

### Task 3: Verify the bump flow still matches repo release rules

**Files:**
- Verify: `.github/workflows/auto-release.yml`
- Verify: `client/Cargo.toml`
- Verify: `homeassistant/config.yaml`
- Verify: `server/src/NebuCtx.Server.Core/ToolRegistry.cs`

- [ ] **Step 1: Re-run the same sync checks CI relies on**

Run:

```bash
CARGO_VERSION=$(grep '^version = ' client/Cargo.toml | head -1 | sed 's/version = "\(.*\)"/\1/' | tr -d '\r')
ADDON_VERSION=$(grep '^version:' homeassistant/config.yaml | head -1 | sed 's/version: *"\(.*\)"/\1/' | tr -d '\r')
TOOL_VERSION=$(grep 'public const string Current = ' server/src/NebuCtx.Server.Core/ToolRegistry.cs | head -1 | sed 's/.*Current = "\(.*\)".*/\1/' | tr -d '\r')
test "$CARGO_VERSION" = "$ADDON_VERSION" && test "$CARGO_VERSION" = "$TOOL_VERSION"
```

Expected: zero exit status

- [ ] **Step 2: Confirm the repo is ready for the existing rebase/push flow**

Run: `git status --short --branch`
Expected: the new script plus the version bump files appear as local changes; no unexpected generated files beyond `Cargo.lock` if updated

- [ ] **Step 3: Commit after review**

```bash
git add scripts/release/bump-version.sh client/Cargo.toml homeassistant/config.yaml server/src/NebuCtx.Server.Core/ToolRegistry.cs Cargo.lock docs/superpowers/specs/2026-05-01-version-bump-script-design.md docs/superpowers/plans/2026-05-01-version-bump-script.md
git commit -m "chore: add version bump script and bump 0.7.2"
```
