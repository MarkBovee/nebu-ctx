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

replace_exact_once() {
    local file="$1"
    local from="$2"
    local to="$3"
    local count

    count="$(grep -Fxc "$from" "$file" || true)"
    [ "$count" -eq 1 ] || fail "Expected exactly one match for '$from' in $file"

    perl -0pi -e "s/\Q$from\E/$to/" "$file"
}

main() {
    [ "$#" -le 1 ] || {
        usage
        fail "Too many arguments"
    }

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

    replace_exact_once "$CLIENT_TOML" "version = \"$old_version\"" "version = \"$new_version\""
    replace_exact_once "$ADDON_CONFIG" "version: \"$old_version\"" "version: \"$new_version\""
    replace_exact_once "$TOOL_REGISTRY" "    public const string Current = \"$old_version\";" "    public const string Current = \"$new_version\";"

    cargo update --manifest-path "$CLIENT_TOML"

    printf '[bump-version] %s -> %s\n' "$old_version" "$new_version"
}

main "$@"
