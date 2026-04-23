#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
BUILD_SCRIPT="${BUILD_SCRIPT:-$ROOT_DIR/scripts/server/build-image.sh}"

[ -f "$BUILD_SCRIPT" ] || {
    printf '[refresh-dist] Build script not found: %s\n' "$BUILD_SCRIPT" >&2
    exit 1
}

printf '[refresh-dist] Refreshing server/dist/linux\n'
PUBLISH_ONLY=1 "$BUILD_SCRIPT"