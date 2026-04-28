#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
SERVER_PROJECT="${SERVER_PROJECT:-$ROOT_DIR/server/src/NebuCtx.Server.Host/NebuCtx.Server.Host.csproj}"
DIST_DIR="${DIST_DIR:-$ROOT_DIR/server/dist/linux}"
DOCKERFILE_PATH="${DOCKERFILE_PATH:-$ROOT_DIR/Dockerfile}"
IMAGE_NAME="${IMAGE_NAME:-nebu-ctx-server:local}"
BUILD_CONTEXT="${BUILD_CONTEXT:-$ROOT_DIR}"
CONFIGURATION="${CONFIGURATION:-Release}"
CONTAINER_TOOL="${CONTAINER_TOOL:-}"
PUBLISH_ONLY="${PUBLISH_ONLY:-0}"
BUILD_ONLY="${BUILD_ONLY:-0}"

log() {
    printf '[build-image] %s\n' "$1"
}

fail() {
    printf '[build-image] %s\n' "$1" >&2
    exit 1
}

resolve_runtime_id() {
    if [ -n "${RUNTIME_ID:-}" ]; then
        printf '%s\n' "$RUNTIME_ID"
        return 0
    fi

    case "$(uname -m)" in
        x86_64|amd64) printf '%s\n' 'linux-x64' ;;
        aarch64|arm64) printf '%s\n' 'linux-arm64' ;;
        *) printf '%s\n' 'linux-x64' ;;
    esac
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

publish_dist() {
    local runtime_id="$1"

    command -v dotnet >/dev/null 2>&1 || fail 'dotnet is required to publish the server dist'
    [ -f "$SERVER_PROJECT" ] || fail "Server project not found: $SERVER_PROJECT"

    log "Publishing server dist ($runtime_id -> $DIST_DIR)"
    rm -rf "$DIST_DIR"
    mkdir -p "$DIST_DIR"

    NEBULA_ALLOW_MNT_DOTNET=1 dotnet publish "$SERVER_PROJECT" \
        -c "$CONFIGURATION" \
        -r "$runtime_id" \
        --self-contained false \
        -o "$DIST_DIR" \
        -p:UseAppHost=false \
        -p:AllowMissingPrunePackageData=true

    [ -f "$DIST_DIR/NebuCtx.Server.Host.dll" ] || fail "Expected $DIST_DIR/NebuCtx.Server.Host.dll after publish"
}

build_image() {
    local container_tool="$1"

    [ -f "$DOCKERFILE_PATH" ] || fail "Dockerfile not found: $DOCKERFILE_PATH"
    [ -d "$BUILD_CONTEXT" ] || fail "Build context not found: $BUILD_CONTEXT"
    [ -d "$DIST_DIR" ] || fail "Dist directory not found: $DIST_DIR"

    log "Building image $IMAGE_NAME from $DOCKERFILE_PATH"
    "$container_tool" build -t "$IMAGE_NAME" -f "$DOCKERFILE_PATH" "$BUILD_CONTEXT"
}

main() {
    local runtime_id
    local container_tool=''

    runtime_id="$(resolve_runtime_id)"

    if [ "$BUILD_ONLY" != "1" ]; then
        publish_dist "$runtime_id"
    fi

    if [ "$PUBLISH_ONLY" != "1" ]; then
        container_tool="$(resolve_container_tool)"
        build_image "$container_tool"
    fi

    log 'Done'
}

main "$@"
