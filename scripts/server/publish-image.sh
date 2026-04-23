#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
BUILD_SCRIPT="${BUILD_SCRIPT:-$ROOT_DIR/scripts/server/build-image.sh}"
IMAGE_REPOSITORY="${IMAGE_REPOSITORY:-localhost/nebu-ctx-local}"
IMAGE_TAG="${IMAGE_TAG:-local}"
SOURCE_IMAGE="${SOURCE_IMAGE:-${IMAGE_REPOSITORY}:${IMAGE_TAG}}"
TARGET_IMAGE="${TARGET_IMAGE:-${IMAGE_REPOSITORY}:${IMAGE_TAG}}"
CONTAINER_TOOL="${CONTAINER_TOOL:-}"
SKIP_BUILD="${SKIP_BUILD:-0}"

log() {
    printf '[publish-image] %s\n' "$1"
}

fail() {
    printf '[publish-image] %s\n' "$1" >&2
    exit 1
}

resolve_container_tool() {
    if [ -n "$CONTAINER_TOOL" ]; then
        printf '%s\n' "$CONTAINER_TOOL"
        return 0
    fi

    if command -v docker >/dev/null 2>&1; then
        printf '%s\n' 'docker'
        return 0
    fi

    if command -v podman >/dev/null 2>&1; then
        printf '%s\n' 'podman'
        return 0
    fi

    fail 'Neither docker nor podman is available'
}

main() {
    local container_tool

    [ -f "$BUILD_SCRIPT" ] || fail "Build script not found: $BUILD_SCRIPT"
    container_tool="$(resolve_container_tool)"

    if [ "$SKIP_BUILD" != "1" ]; then
        log "Building source image $SOURCE_IMAGE"
        IMAGE_NAME="$SOURCE_IMAGE" CONTAINER_TOOL="$container_tool" "$BUILD_SCRIPT"
    fi

    if [ "$SOURCE_IMAGE" != "$TARGET_IMAGE" ]; then
        log "Tagging $SOURCE_IMAGE as $TARGET_IMAGE"
        "$container_tool" tag "$SOURCE_IMAGE" "$TARGET_IMAGE"
    fi

    log "Pushing $TARGET_IMAGE"
    "$container_tool" push "$TARGET_IMAGE"
    log 'Done'
}

main "$@"