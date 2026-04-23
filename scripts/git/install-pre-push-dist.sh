#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(git rev-parse --show-toplevel)"
HOOKS_DIR="$(git -C "$ROOT_DIR" rev-parse --git-path hooks)"
SOURCE_HOOK="$ROOT_DIR/scripts/git/pre-push-dist-example.sh"
TARGET_HOOK="$HOOKS_DIR/pre-push"

[ -f "$SOURCE_HOOK" ] || {
    printf '[install-pre-push] Missing source hook: %s\n' "$SOURCE_HOOK" >&2
    exit 1
}

mkdir -p "$HOOKS_DIR"
cp "$SOURCE_HOOK" "$TARGET_HOOK"
chmod +x "$TARGET_HOOK"

printf '[install-pre-push] Installed %s\n' "$TARGET_HOOK"