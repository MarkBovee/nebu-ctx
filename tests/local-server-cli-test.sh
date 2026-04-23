#!/usr/bin/env bash
set -euo pipefail

PROJECT_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
# shellcheck source=tests/lib/postgres_env.sh
source "$PROJECT_ROOT/tests/lib/postgres_env.sh"

load_repo_postgres_env "$PROJECT_ROOT"
eval "$(parse_database_url_exports "$DATABASE_URL")"

CONTAINER_TOOL="$(detect_container_tool)"
IMAGE_NAME="${IMAGE_NAME:-nebu-ctx-server-smoke:local}"
SERVER_DOCKERFILE="${SERVER_DOCKERFILE:-homeassistant/Dockerfile}"
CONTAINER_NAME="${CONTAINER_NAME:-nebu-ctx-server-smoke}"
HOST_HTTP_PORT="${HOST_HTTP_PORT:-4243}"
SERVER_NETWORK_MODE="${SERVER_NETWORK_MODE:-}"
CLI_ROOT="$(mktemp -d)"
CLI_HOME="$(mktemp -d)"
TOKEN="nctx_smoke_$(od -An -tx1 -N16 /dev/urandom | tr -d ' \n')"
SMOKE_MARKER="server-smoke-$(date +%s)"
BUILD_CONTEXT="$PROJECT_ROOT"

detect_cargo() {
    if command -v cargo >/dev/null 2>&1; then
        command -v cargo
        return 0
    fi

    if [ -n "${USERPROFILE:-}" ] && [ -x "${USERPROFILE}\\.cargo\\bin\\cargo.exe" ]; then
        printf '%s\n' "${USERPROFILE}\\.cargo\\bin\\cargo.exe"
        return 0
    fi

    if [ -n "${HOME:-}" ] && [ -x "${HOME}/.cargo/bin/cargo" ]; then
        printf '%s\n' "${HOME}/.cargo/bin/cargo"
        return 0
    fi

    if [ -n "${HOME:-}" ] && [ -x "/mnt/c/Users/$(basename "$HOME")/.cargo/bin/cargo.exe" ]; then
        printf '%s\n' "/mnt/c/Users/$(basename "$HOME")/.cargo/bin/cargo.exe"
        return 0
    fi

    fail_msg "cargo is required to install the new Rust client for the smoke test"
}

CARGO_BIN="$(detect_cargo)"

cleanup() {
    "$CONTAINER_TOOL" logs "$CONTAINER_NAME" 2>/dev/null || true
    "$CONTAINER_TOOL" rm -f "$CONTAINER_NAME" 2>/dev/null || true
    rm -rf "$CLI_ROOT" "$CLI_HOME"
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

if [ -z "$SERVER_NETWORK_MODE" ]; then
    case "$(uname -s)" in
        Linux) SERVER_NETWORK_MODE="host" ;;
        *) SERVER_NETWORK_MODE="bridge" ;;
    esac
fi

printf 'Using database %s\n' "$(mask_database_url "$DATABASE_URL")"

bash "$PROJECT_ROOT/scripts/server/refresh-dist.sh"

printf '=== Building standalone server image (%s) ===\n' "$SERVER_DOCKERFILE"
"$CONTAINER_TOOL" build \
    -t "$IMAGE_NAME" \
    -f "$PROJECT_ROOT/$SERVER_DOCKERFILE" \
    "$BUILD_CONTEXT"

printf '\n=== Starting standalone server container ===\n'
"$CONTAINER_TOOL" rm -f "$CONTAINER_NAME" >/dev/null 2>&1 || true
if [ "$SERVER_NETWORK_MODE" = "host" ]; then
    "$CONTAINER_TOOL" run -d --rm \
        --name "$CONTAINER_NAME" \
        --network host \
        -e NEBULA_STORE=postgres \
        -e DATABASE_URL="$DATABASE_URL" \
        -e NEBULA_CTX_HTTP_PORT="$HOST_HTTP_PORT" \
        -e NEBULA_CTX_HTTP_TOKEN="$TOKEN" \
        "$IMAGE_NAME" >/dev/null
else
    "$CONTAINER_TOOL" run -d --rm \
        --name "$CONTAINER_NAME" \
        -p "$HOST_HTTP_PORT:4242" \
        -e NEBULA_STORE=postgres \
        -e DATABASE_URL="$DATABASE_URL" \
        -e NEBULA_CTX_HTTP_TOKEN="$TOKEN" \
        "$IMAGE_NAME" >/dev/null
fi

wait_for_http "http://127.0.0.1:${HOST_HTTP_PORT}/health" -H "Authorization: Bearer ${TOKEN}"

printf '\n=== Validating server endpoints ===\n'
health_code="$(curl -sS -o /dev/null -w '%{http_code}' "http://127.0.0.1:${HOST_HTTP_PORT}/health")"
[ "$health_code" = "200" ] || fail_msg "Expected /health to return 200, got $health_code"

unauthorized_code="$(curl -sS -o /dev/null -w '%{http_code}' "http://127.0.0.1:${HOST_HTTP_PORT}/v1/tools")"
[ "$unauthorized_code" = "401" ] || fail_msg "Expected unauthorized /v1/tools to return 401, got $unauthorized_code"

manifest_json="$(curl -fsS -H "Authorization: Bearer ${TOKEN}" "http://127.0.0.1:${HOST_HTTP_PORT}/v1/manifest")"
printf '%s' "$manifest_json" | assert_json

tools_json="$(curl -fsS -H "Authorization: Bearer ${TOKEN}" "http://127.0.0.1:${HOST_HTTP_PORT}/v1/tools")"
printf '%s' "$tools_json" | assert_json
printf '%s' "$tools_json" | grep -q 'ctx_brain'

store_body="$(cat <<EOF
{"name":"ctx_brain","arguments":{"action":"store","key":"${SMOKE_MARKER}","value":"${SMOKE_MARKER}"}}
EOF
)"

store_json="$(curl -fsS \
    -H "Authorization: Bearer ${TOKEN}" \
    -H 'Content-Type: application/json' \
    -d "$store_body" \
    "http://127.0.0.1:${HOST_HTTP_PORT}/v1/tools/call")"
printf '%s' "$store_json" | assert_json

recall_body="$(cat <<EOF
{"name":"ctx_brain","arguments":{"action":"recall","query":"${SMOKE_MARKER}","limit":5}}
EOF
)"

recall_json="$(curl -fsS \
    -H "Authorization: Bearer ${TOKEN}" \
    -H 'Content-Type: application/json' \
    -d "$recall_body" \
    "http://127.0.0.1:${HOST_HTTP_PORT}/v1/tools/call")"
printf '%s' "$recall_json" | assert_json
printf '%s' "$recall_json" | grep -q "$SMOKE_MARKER"

printf '\n=== Installing CLI and connecting to the server ===\n'
"$CARGO_BIN" install --path "$PROJECT_ROOT/client" --bin nebu-ctx --root "$CLI_ROOT" --force

HOME="$CLI_HOME" USERPROFILE="$CLI_HOME" "$CLI_ROOT/bin/nebu-ctx" server connect --endpoint "http://127.0.0.1:${HOST_HTTP_PORT}" --token "$TOKEN" >/dev/null

status_output="$(HOME="$CLI_HOME" USERPROFILE="$CLI_HOME" "$CLI_ROOT/bin/nebu-ctx" server status)"
printf '%s\n' "$status_output"
printf '%s' "$status_output" | grep -q '"saved": true'

bind_output="$(HOME="$CLI_HOME" USERPROFILE="$CLI_HOME" "$CLI_ROOT/bin/nebu-ctx" server bind)"
printf '%s' "$bind_output" | grep -q '"project"'

client_store_json="$(HOME="$CLI_HOME" USERPROFILE="$CLI_HOME" "$CLI_ROOT/bin/nebu-ctx" ctx_brain action=store key="$SMOKE_MARKER" value="$SMOKE_MARKER")"
printf '%s' "$client_store_json" | assert_json

client_recall_json="$(HOME="$CLI_HOME" USERPROFILE="$CLI_HOME" "$CLI_ROOT/bin/nebu-ctx" ctx_brain action=recall query="$SMOKE_MARKER")"
printf '%s' "$client_recall_json" | assert_json
printf '%s' "$client_recall_json" | grep -q "$SMOKE_MARKER"

python3 - "$CLI_HOME/.nebu-ctx/cloud/server_connection.json" "http://127.0.0.1:${HOST_HTTP_PORT}" <<'PY'
import json
import pathlib
import sys

path = pathlib.Path(sys.argv[1])
expected = sys.argv[2]
data = json.loads(path.read_text())
assert data["endpoint"] == expected, data
assert data["token"], data
PY

printf '\nStandalone server container + new Rust client smoke passed.\n'