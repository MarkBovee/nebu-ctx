#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
MANIFEST_PATH="${MANIFEST_PATH:-$ROOT_DIR/client/Cargo.toml}"
CLIENT_BINARY="${CLIENT_BINARY:-nebu-ctx}"
PROFILE="${PROFILE:-release}"
TARGET="${TARGET:-}"

log() {
    printf '[client-build] %s\n' "$1"
}

fail() {
    printf '[client-build] %s\n' "$1" >&2
    exit 1
}

detect_cargo() {
    if command -v cargo >/dev/null 2>&1; then
        command -v cargo
        return 0
    fi

    if [ -n "${USERPROFILE:-}" ] && [ -x "${USERPROFILE}\\.cargo\\bin\\cargo.exe" ]; then
        printf '%s\n' "${USERPROFILE}\\.cargo\\bin\\cargo.exe"
        return 0
    fi

    if [ -n "${HOME:-}" ] && [ -x "${HOME}/.cargo/bin/cargo" ]; then
        printf '%s\n' "${HOME}/.cargo/bin/cargo"
        return 0
    fi

    if [ -n "${HOME:-}" ] && [ -x "/mnt/c/Users/$(basename "$HOME")/.cargo/bin/cargo.exe" ]; then
        printf '%s\n' "/mnt/c/Users/$(basename "$HOME")/.cargo/bin/cargo.exe"
        return 0
    fi

    return 1
}

CARGO_BIN="$(detect_cargo)" || fail 'cargo is required to build the client'
[ -f "$MANIFEST_PATH" ] || fail "Client manifest not found: $MANIFEST_PATH"

build_cmd=("$CARGO_BIN" build --manifest-path "$MANIFEST_PATH" --bin "$CLIENT_BINARY")

if [ "$PROFILE" = "release" ]; then
    build_cmd+=(--release)
fi

if [ -n "$TARGET" ]; then
    build_cmd+=(--target "$TARGET")
fi

log "Building $CLIENT_BINARY ($PROFILE)"
"${build_cmd[@]}"
log 'Done'