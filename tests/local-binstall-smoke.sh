#!/usr/bin/env bash
set -euo pipefail

VERSION="${1:-0.7.8}"
ROOT="$(mktemp -d)"
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"

cleanup() {
    rm -rf "$ROOT"
}

trap cleanup EXIT

if [ "$#" -eq 0 ] && [ -f "$PROJECT_ROOT/client/Cargo.toml" ]; then
    repo_version="$(python3 - <<'PY' "$PROJECT_ROOT/client/Cargo.toml"
import pathlib
import re
import sys

content = pathlib.Path(sys.argv[1]).read_text()
match = re.search(r'^version = "([^"]+)"$', content, re.MULTILINE)
if not match:
    raise SystemExit('Unable to determine client version from client/Cargo.toml')
print(match.group(1))
PY
)"

    if [ "$repo_version" != "$VERSION" ]; then
        printf '%s\n' "Default binstall smoke version $VERSION drifts from client/Cargo.toml version $repo_version. Pass an explicit version or update tests/local-binstall-smoke.sh." >&2
        exit 1
    fi
fi

detect_install_command() {
    if command -v cargo >/dev/null 2>&1 && cargo binstall --help >/dev/null 2>&1; then
        printf '%s\n' cargo
        return 0
    fi

    if [ -n "${USERPROFILE:-}" ] && [ -x "${USERPROFILE}\\.cargo\\bin\\cargo.exe" ] && "${USERPROFILE}\\.cargo\\bin\\cargo.exe" binstall --help >/dev/null 2>&1; then
        printf '%s\n' "${USERPROFILE}\\.cargo\\bin\\cargo.exe"
        return 0
    fi

    if [ -n "${HOME:-}" ] && [ -x "${HOME}/.cargo/bin/cargo" ] && "${HOME}/.cargo/bin/cargo" binstall --help >/dev/null 2>&1; then
        printf '%s\n' "${HOME}/.cargo/bin/cargo"
        return 0
    fi

    if [ -n "${HOME:-}" ] && [ -x "/mnt/c/Users/$(basename "$HOME")/.cargo/bin/cargo.exe" ] && "/mnt/c/Users/$(basename "$HOME")/.cargo/bin/cargo.exe" binstall --help >/dev/null 2>&1; then
        printf '%s\n' "/mnt/c/Users/$(basename "$HOME")/.cargo/bin/cargo.exe"
        return 0
    fi

    printf '%s\n' "cargo with binstall support is required" >&2
    return 1
}

INSTALL_CMD="$(detect_install_command)"

printf 'Installing nebu-ctx %s into %s\n' "$VERSION" "$ROOT"
"$INSTALL_CMD" binstall nebu-ctx --version "$VERSION" --root "$ROOT" --no-confirm

INSTALLED_BIN="$ROOT/bin/nebu-ctx"
if [ ! -x "$INSTALLED_BIN" ] && [ -x "$ROOT/bin/nebu-ctx.exe" ]; then
    INSTALLED_BIN="$ROOT/bin/nebu-ctx.exe"
fi

if [ ! -x "$INSTALLED_BIN" ]; then
    printf '%s\n' "Expected installed binary at $ROOT/bin/nebu-ctx or $ROOT/bin/nebu-ctx.exe" >&2
    exit 1
fi

"$INSTALLED_BIN" --version >/dev/null

version_output="$("$INSTALLED_BIN" --version)"
printf '%s\n' "$version_output" | grep -Eq "^nebu-ctx ${VERSION}([[:space:]]|$)"

printf 'Installed binary: %s\n' "$INSTALLED_BIN"
