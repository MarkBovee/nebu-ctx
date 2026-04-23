#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
CLIENT_PATH="${CLIENT_PATH:-$ROOT_DIR/client}"
CLIENT_BINARY="${CLIENT_BINARY:-nebu-ctx}"
INSTALL_ROOT="${INSTALL_ROOT:-}"
FORCE_INSTALL="${FORCE_INSTALL:-1}"

log() {
    printf '[client-install] %s\n' "$1"
}

fail() {
    printf '[client-install] %s\n' "$1" >&2
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

CARGO_BIN="$(detect_cargo)" || fail 'cargo is required to install the client'
[ -f "$CLIENT_PATH/Cargo.toml" ] || fail "Client package not found: $CLIENT_PATH"

install_cmd=("$CARGO_BIN" install --path "$CLIENT_PATH" --bin "$CLIENT_BINARY")

if [ "$FORCE_INSTALL" = "1" ]; then
    install_cmd+=(--force)
fi

if [ -n "$INSTALL_ROOT" ]; then
    install_cmd+=(--root "$INSTALL_ROOT")
fi

log "Installing $CLIENT_BINARY from $CLIENT_PATH"
"${install_cmd[@]}"
log 'Done'