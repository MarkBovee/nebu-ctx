#!/usr/bin/env bash
# Pre-release and packaging pre-check for nebu-ctx.
# Run from the repository root or as: bash tests/pre_release_check.sh
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT_DIR"

# shellcheck source=tests/lib/dotnet_dist.sh
source "$ROOT_DIR/tests/lib/dotnet_dist.sh"

PASS=0
FAIL=0
CONTAINER_TOOL="${CONTAINER_TOOL:-podman}"
SKIP_IMAGE_BUILD="${SKIP_IMAGE_BUILD:-0}"
MAIN_IMAGE_TAG="${MAIN_IMAGE_TAG:-nebu-ctx-precheck:local}"
ADDON_IMAGE_TAG="${ADDON_IMAGE_TAG:-nebu-ctx-addon-precheck:local}"
PUBLISHED_ADDON_IMAGE_TAG="${PUBLISHED_ADDON_IMAGE_TAG:-nebu-ctx-addon-published-precheck:local}"

step() { printf "\n\033[1;34m=== %s ===\033[0m\n" "$1"; }
ok()   { printf "  \033[32m✓\033[0m %s\n" "$1"; PASS=$((PASS+1)); }
fail() { printf "  \033[31m✗\033[0m %s\n" "$1"; FAIL=$((FAIL+1)); }

run_check() {
    local label="$1"
    shift
    step "$label"
    if "$@"; then
        ok "$label"
    else
        fail "$label"
    fi
}

validate_files() {
    sh -n docker-entrypoint.sh
    sh -n homeassistant/run.sh
    bash -n tests/local-addon-test.sh
    bash -n tests/local-server-cli-test.sh
    bash -n tests/lib/postgres_env.sh
    python3 -m json.tool repository.json >/dev/null
}

build_images() {
    local build_arch

    if [ "$SKIP_IMAGE_BUILD" = "1" ]; then
        printf "Skipping image builds because SKIP_IMAGE_BUILD=1\n"
        return 0
    fi

    if ! command -v "$CONTAINER_TOOL" >/dev/null 2>&1; then
        printf "%s not found. Install it or re-run with SKIP_IMAGE_BUILD=1\n" "$CONTAINER_TOOL" >&2
        return 1
    fi

    case "$(uname -m)" in
        x86_64) build_arch="amd64" ;;
        aarch64|arm64) build_arch="arm64" ;;
        *) build_arch="amd64" ;;
    esac

    publish_dotnet_server_dist "$ROOT_DIR"

    "$CONTAINER_TOOL" build -t "$MAIN_IMAGE_TAG" -f Dockerfile .

    # The local add-on runtime reuses the same dist-first image as the standalone server.
    "$CONTAINER_TOOL" tag "$MAIN_IMAGE_TAG" "$ADDON_IMAGE_TAG"

    # The published add-on Dockerfile validates the Home Assistant fast-install runtime path.
    "$CONTAINER_TOOL" build \
        --build-arg BUILD_ARCH="$build_arch" \
        -t "$PUBLISHED_ADDON_IMAGE_TAG" \
        -f homeassistant/Dockerfile \
        homeassistant
}

run_check "cargo fmt --check" cargo fmt --check
run_check "cargo test --release --features cloud-server" cargo test --release --features cloud-server
run_check "cargo build --release --features cloud-server" cargo build --release --features cloud-server
run_check "shell and manifest validation" validate_files
run_check "$CONTAINER_TOOL image builds" build_images

printf "\n\033[1;97m=== RESULT: %d passed, %d failed ===\033[0m\n" "$PASS" "$FAIL"

if [ "$FAIL" -gt 0 ]; then
    printf "\033[31mPre-release check FAILED.\033[0m\n"
    exit 1
fi

printf "\033[32mPre-release check PASSED.\033[0m\n"
