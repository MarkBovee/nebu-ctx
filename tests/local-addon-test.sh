#!/bin/bash
# Fast local test for nebula-ctx HA addon
# Step 1: Build binary on host (incremental after first run)
# Step 2: Package into container via thin Dockerfile
set -euo pipefail

PROJECT_ROOT="$(cd "$(dirname "$0")/.." && pwd)"
IMAGE_NAME="nebula-ctx-addon-dev"

# --- Step 1: Build on host ---
echo "=== Building nebula-ctx binary (host) ==="
cd "${PROJECT_ROOT}"
CARGO_BUILD_JOBS=2 cargo build --release --features cloud-server -p nebula-ctx

if [ ! -f target/release/nebula-ctx ]; then
    echo "ERROR: Binary not found at target/release/nebula-ctx"
    exit 1
fi

# --- Step 2: Package into container ---
echo ""
echo "=== Packaging container (thin Dockerfile) ==="
podman build \
    -t "${IMAGE_NAME}" \
    -f homeassistant/Dockerfile.dev \
    "${PROJECT_ROOT}"

# --- Step 3: Run with mock HA data ---
TEST_DATA="$(mktemp -d)"
trap 'rm -rf "${TEST_DATA}"' EXIT

cat > "${TEST_DATA}/options.json" <<'EOF'
{
  "store": "sqlite",
  "database_url": "",
  "auth_token": "",
  "log_level": "debug",
  "project_root": "/share"
}
EOF

mkdir -p "${TEST_DATA}/share"

echo ""
echo "Test data: ${TEST_DATA}"
echo "Dashboard:  http://localhost:3333"
echo "MCP:        http://localhost:4242"
echo "Ctrl+C to stop"
echo ""

podman run --rm \
    --name nebula-ctx-test \
    --memory "2g" \
    -p 3333:3333 \
    -p 4242:4242 \
    -v "${TEST_DATA}:/data:rw" \
    -v "${TEST_DATA}/share:/share:rw" \
    "${IMAGE_NAME}"
