#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(git rev-parse --show-toplevel)"
"$ROOT_DIR/scripts/server/refresh-dist.sh"

if ! git diff --quiet -- server/dist/linux; then
    printf '[pre-push-dist] server/dist/linux changed. Commit the refreshed dist before pushing.\n' >&2
    git status --short -- server/dist/linux >&2
    exit 1
fi