#!/bin/bash
# Fast local test for nebu-ctx HA addon
# Step 1: Build binary on host (incremental after first run)
# Step 2: Package into container via thin Dockerfile
set -euo pipefail

PROJECT_ROOT="$(cd "$(dirname "$0")/.." && pwd)"
CONTAINER_TOOL="${CONTAINER_TOOL:-podman}"
IMAGE_NAME="nebu-ctx-addon-dev"
DOCKERFILE="homeassistant/Dockerfile.dev"
BUILD_ARGS=()

# --- Step 1: Build on host ---
echo "=== Building nebu-ctx binary (host) ==="
cd "${PROJECT_ROOT}"

if command -v cargo >/dev/null 2>&1; then
    CARGO_BUILD_JOBS=2 cargo build --release --features cloud-server --bin nebu-ctx
elif [ -f target/release/nebu-ctx ]; then
    echo "cargo not found in bash PATH, reusing existing target/release/nebu-ctx"
else
    echo "cargo not found in bash PATH and target/release/nebu-ctx is missing"
    echo "falling back to containerized source build via homeassistant/Dockerfile.source"
    DOCKERFILE="homeassistant/Dockerfile.source"
    BUILD_ARGS+=(--build-arg BUILD_VERSION=local-smoke)
fi

if [ "${DOCKERFILE}" = "homeassistant/Dockerfile.dev" ] && [ ! -f target/release/nebu-ctx ]; then
    echo "ERROR: Binary not found at target/release/nebu-ctx"
    exit 1
fi

# --- Step 2: Package into container ---
echo ""
echo "=== Packaging container (thin Dockerfile) ==="
"${CONTAINER_TOOL}" build \
    "${BUILD_ARGS[@]}" \
    -t "${IMAGE_NAME}" \
    -f "${DOCKERFILE}" \
    "${PROJECT_ROOT}"

# --- Step 3: Run with mock HA data ---
TEST_DATA="$(mktemp -d)"
trap 'rm -rf "${TEST_DATA}"' EXIT

cat > "${TEST_DATA}/options.json" <<'EOF'
{
  "store": "sqlite",
  "auth_token": "",
  "log_level": "debug",
  "project_root": "/share"
}
EOF

mkdir -p "${TEST_DATA}/share"

CONTAINER_ID="$("${CONTAINER_TOOL}" run -d --rm \
    --name nebu-ctx-test \
    --memory "2g" \
    -p 3333:3333 \
    -p 4242:4242 \
    -v "${TEST_DATA}:/data:rw" \
    -v "${TEST_DATA}/share:/share:rw" \
    "${IMAGE_NAME}")"

cleanup_container() {
    "${CONTAINER_TOOL}" logs "${CONTAINER_ID}" 2>/dev/null || true
    "${CONTAINER_TOOL}" rm -f "${CONTAINER_ID}" 2>/dev/null || true
}

trap 'cleanup_container; rm -rf "${TEST_DATA}"' EXIT

for _ in $(seq 1 60); do
    if [ -s "${TEST_DATA}/auth_token" ]; then
        break
    fi
    sleep 1
done

if [ ! -s "${TEST_DATA}/auth_token" ]; then
    echo "ERROR: auth token was not generated"
    exit 1
fi

TOKEN="$(cat "${TEST_DATA}/auth_token")"

echo ""
echo "Test data: ${TEST_DATA}"
echo "Dashboard:  http://localhost:3333"
echo "MCP:        http://localhost:4242"
echo "Token:      ${TOKEN}"
echo ""

for _ in $(seq 1 60); do
    if curl -fsS http://127.0.0.1:3333/ >/dev/null \
        && curl -fsS -H "Authorization: Bearer ${TOKEN}" http://127.0.0.1:4242/v1/tools >/dev/null; then
        break
    fi
    sleep 1
done

curl -fsS http://127.0.0.1:3333/ >/dev/null
curl -fsS -H "Authorization: Bearer ${TOKEN}" http://127.0.0.1:4242/health >/dev/null
curl -fsS -H "Authorization: Bearer ${TOKEN}" http://127.0.0.1:4242/v1/tools >/dev/null

REQUEST_BODY='{"name":"ctx_brain","arguments":{"action":"status","brain_id":"default"}}'
TOOL_RESULT="$(curl -fsS \
    -H "Authorization: Bearer ${TOKEN}" \
    -H "Content-Type: application/json" \
    -d "${REQUEST_BODY}" \
    http://127.0.0.1:4242/v1/tools/call)"

printf '%s' "${TOOL_RESULT}" | grep -Eq 'ctx_brain|default|status|content|structuredContent'

echo "Authenticated tool call: ${TOOL_RESULT}"
echo "Full add-on flow validated successfully."
