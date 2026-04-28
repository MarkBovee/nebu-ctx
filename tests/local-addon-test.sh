#!/usr/bin/env bash
set -euo pipefail

PROJECT_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
# shellcheck source=tests/lib/postgres_env.sh
source "$PROJECT_ROOT/tests/lib/postgres_env.sh"

load_repo_postgres_env "$PROJECT_ROOT"
eval "$(parse_database_url_exports "$DATABASE_URL")"

CONTAINER_TOOL="$(detect_container_tool)"
IMAGE_NAME="${IMAGE_NAME:-nebu-ctx-addon-smoke:local}"
ADDON_DOCKERFILE="${ADDON_DOCKERFILE:-homeassistant/Dockerfile}"
HOST_DASHBOARD_PORT="${HOST_DASHBOARD_PORT:-3333}"
HOST_MCP_PORT="${HOST_MCP_PORT:-4242}"
CONTAINER_NAME="${CONTAINER_NAME:-nebu-ctx-addon-smoke}"
BUILD_CONTEXT="$PROJECT_ROOT"
PROJECT_ROOT_PATH="${ADDON_PROJECT_ROOT:-/share}"
SMOKE_MARKER="addon-smoke-$(date +%s)"
TEST_DATA="$(mktemp -d)"

cleanup() {
    "$CONTAINER_TOOL" logs "$CONTAINER_NAME" 2>/dev/null || true
    "$CONTAINER_TOOL" rm -f "$CONTAINER_NAME" 2>/dev/null || true
    rm -rf "$TEST_DATA"
}

trap cleanup EXIT

wait_for_http() {
    local url="$1"
    shift

    for _ in $(seq 1 90); do
        if curl -fsS "$@" "$url" >/dev/null 2>&1; then
            return 0
        fi
        sleep 1
    done

    fail_msg "Timed out waiting for $url"
}

assert_json() {
    python3 -c 'import json,sys; json.load(sys.stdin)' >/dev/null
}

printf 'Using database %s\n' "$(mask_database_url "$DATABASE_URL")"
cd "$PROJECT_ROOT"

bash "$PROJECT_ROOT/scripts/server/refresh-dist.sh"

printf '\n=== Building Home Assistant image (%s) ===\n' "$ADDON_DOCKERFILE"
"$CONTAINER_TOOL" build \
    -t "$IMAGE_NAME" \
    -f "$PROJECT_ROOT/$ADDON_DOCKERFILE" \
    "$BUILD_CONTEXT"

mkdir -p "$TEST_DATA/share"

cat > "$TEST_DATA/options.json" <<EOF
{
  "postgres_host": "${PG_HOST}",
  "postgres_port": ${PG_PORT},
  "postgres_database": "${PG_DATABASE}",
  "postgres_username": "${PG_USER}",
  "postgres_password": "${PG_PASSWORD}",
  "log_level": "info",
  "project_root": "${PROJECT_ROOT_PATH}"
}
EOF

printf '\n=== Starting Home Assistant add-on container ===\n'
"$CONTAINER_TOOL" rm -f "$CONTAINER_NAME" >/dev/null 2>&1 || true
"$CONTAINER_TOOL" run -d --rm \
    --name "$CONTAINER_NAME" \
    -p "$HOST_DASHBOARD_PORT:3333" \
    -p "$HOST_MCP_PORT:4242" \
    -v "$TEST_DATA:/data:rw" \
    -v "$TEST_DATA/share:/share:rw" \
    -v "$TEST_DATA/share:/config:rw" \
    "$IMAGE_NAME" >/dev/null

for _ in $(seq 1 90); do
    if [ -s "$TEST_DATA/auth_token" ]; then
        break
    fi
    sleep 1
done

[ -s "$TEST_DATA/auth_token" ] || fail_msg "Auth token was not generated"
TOKEN="$(tr -d '\r\n' < "$TEST_DATA/auth_token")"

wait_for_http "http://127.0.0.1:${HOST_DASHBOARD_PORT}/"
wait_for_http "http://127.0.0.1:${HOST_MCP_PORT}/health" -H "Authorization: Bearer ${TOKEN}"

printf '\n=== Validating dashboard routes ===\n'
dashboard_html="$(curl -fsS "http://127.0.0.1:${HOST_DASHBOARD_PORT}/")"
grep -Eqi '<html|<!doctype html' <<<"$dashboard_html"

for path in \
    /api/version \
    /api/stats \
    /api/agents \
    /api/buddy \
    /api/session \
    /api/pipeline-stats \
    /api/context-ledger \
    /api/intent \
    '/api/search?q=ctx&limit=1'
do
    response="$(curl -fsS "http://127.0.0.1:${HOST_DASHBOARD_PORT}${path}")"
    printf '%s' "$response" | assert_json
done

auth_json="$(curl -fsS "http://127.0.0.1:${HOST_DASHBOARD_PORT}/api/auth-token")"
printf '%s' "$auth_json" | assert_json
python3 - "$TOKEN" <<'PY' <<<"$auth_json"
import json
import sys

expected = sys.argv[1]
payload = json.load(sys.stdin)
assert payload["token"] == expected, payload
PY

favicon_code="$(curl -sS -o /dev/null -w '%{http_code}' "http://127.0.0.1:${HOST_DASHBOARD_PORT}/favicon.ico")"
[ "$favicon_code" = "204" ] || fail_msg "Expected /favicon.ico to return 204, got $favicon_code"

printf '\n=== Validating MCP routes ===\n'
unauthorized_code="$(curl -sS -o /dev/null -w '%{http_code}' "http://127.0.0.1:${HOST_MCP_PORT}/v1/tools")"
[ "$unauthorized_code" = "401" ] || fail_msg "Expected unauthorized /v1/tools to return 401, got $unauthorized_code"

manifest_json="$(curl -fsS -H "Authorization: Bearer ${TOKEN}" "http://127.0.0.1:${HOST_MCP_PORT}/v1/manifest")"
printf '%s' "$manifest_json" | assert_json

tools_json="$(curl -fsS -H "Authorization: Bearer ${TOKEN}" "http://127.0.0.1:${HOST_MCP_PORT}/v1/tools")"
printf '%s' "$tools_json" | assert_json
printf '%s' "$tools_json" | grep -q 'ctx_brain'

store_body="$(cat <<EOF
{"name":"ctx_brain","arguments":{"action":"store","key":"${SMOKE_MARKER}","value":"${SMOKE_MARKER}"}}
EOF
)"

curl -fsS \
    -H "Authorization: Bearer ${TOKEN}" \
    -H 'Content-Type: application/json' \
    -d "$store_body" \
    "http://127.0.0.1:${HOST_MCP_PORT}/v1/tools/call" | assert_json

recall_body="$(cat <<EOF
{"name":"ctx_brain","arguments":{"action":"recall","query":"${SMOKE_MARKER}","limit":5}}
EOF
)"

recall_json="$(curl -fsS \
    -H "Authorization: Bearer ${TOKEN}" \
    -H 'Content-Type: application/json' \
    -d "$recall_body" \
    "http://127.0.0.1:${HOST_MCP_PORT}/v1/tools/call")"
printf '%s' "$recall_json" | assert_json
printf '%s' "$recall_json" | grep -q "$SMOKE_MARKER"

printf '\nDashboard + MCP add-on smoke passed.\n'
