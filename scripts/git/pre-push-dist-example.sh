#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(git rev-parse --show-toplevel)"

requires_dist_refresh() {
    local upstream_ref

    if ! upstream_ref="$(git -C "$ROOT_DIR" rev-parse --abbrev-ref --symbolic-full-name @{upstream} 2>/dev/null)"; then
        return 0
    fi

    git -C "$ROOT_DIR" diff --quiet "$upstream_ref"..HEAD -- \
        server/src \
        server/Directory.Build.props \
        server/NebuCtx.slnx
}

if requires_dist_refresh; then
    printf '[pre-push-dist] Skipping dist refresh: no publish inputs changed in pending push.\n'
    exit 0
fi

"$ROOT_DIR/scripts/server/refresh-dist.sh"

if ! git diff --quiet -- server/dist/linux; then
    printf '[pre-push-dist] server/dist/linux changed. Commit the refreshed dist before pushing.\n' >&2
    git status --short -- server/dist/linux >&2
    exit 1
fi