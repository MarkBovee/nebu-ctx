#!/bin/sh
set -eu

OPTIONS_FILE="/data/options.json"
MCP_PID=""
DASHBOARD_PID=""

log() {
    printf '[nebula-ctx] %s\n' "$1"
}

cleanup() {
    if [ -n "$DASHBOARD_PID" ]; then
        kill "$DASHBOARD_PID" 2>/dev/null || true
    fi
    if [ -n "$MCP_PID" ]; then
        kill "$MCP_PID" 2>/dev/null || true
    fi
}

trap cleanup INT TERM EXIT

if [ ! -f "$OPTIONS_FILE" ]; then
    log "Missing $OPTIONS_FILE"
    exit 1
fi

STORE="$(jq -r '.store // "sqlite"' "$OPTIONS_FILE")"
DATABASE_URL="$(jq -r '.database_url // ""' "$OPTIONS_FILE")"
AUTH_TOKEN="$(jq -r '.auth_token // ""' "$OPTIONS_FILE")"
LOG_LEVEL="$(jq -r '.log_level // "info"' "$OPTIONS_FILE")"
PROJECT_ROOT="$(jq -r '.project_root // "/share"' "$OPTIONS_FILE")"

if [ -z "$PROJECT_ROOT" ] || [ "$PROJECT_ROOT" = "null" ]; then
    PROJECT_ROOT="/share"
fi

export NEBULA_CTX_DATA_DIR="/data"
export NEBULA_CTX_DASHBOARD_PROJECT="$PROJECT_ROOT"
export NEBULA_CTX_DASHBOARD_DISABLE_AUTH="1"

if [ -n "$LOG_LEVEL" ] && [ "$LOG_LEVEL" != "null" ]; then
    export RUST_LOG="$LOG_LEVEL"
fi

log "Initializing nebula-ctx add-on"
log "Project root: $PROJECT_ROOT"

if [ "$STORE" = "postgres" ]; then
    if [ -z "$DATABASE_URL" ]; then
        log "PostgreSQL store selected but database_url is empty"
        exit 1
    fi
    export NEBULA_STORE="postgres"
    export DATABASE_URL="$DATABASE_URL"
    log "Using PostgreSQL backend"
else
    export NEBULA_STORE="sqlite"
    log "Using SQLite backend under /data"
fi

MCP_HOST="127.0.0.1"
if [ -n "$AUTH_TOKEN" ] && [ "$AUTH_TOKEN" != "null" ]; then
    MCP_HOST="0.0.0.0"
fi

if [ -f "/app/nebula_ctx_commit.txt" ]; then
    log "Image source commit: $(cat /app/nebula_ctx_commit.txt)"
fi

log "Starting dashboard ingress service on 0.0.0.0:3333"
nebula-ctx dashboard --host=0.0.0.0 --port=3333 &
DASHBOARD_PID="$!"

log "Starting MCP HTTP service on ${MCP_HOST}:4242"
if [ "$MCP_HOST" = "0.0.0.0" ]; then
    nebula-ctx serve --host "$MCP_HOST" --port 4242 --project-root "$PROJECT_ROOT" --auth-token "$AUTH_TOKEN" &
else
    log "No auth_token configured; MCP will stay on loopback inside the add-on container"
    nebula-ctx serve --host "$MCP_HOST" --port 4242 --project-root "$PROJECT_ROOT" &
fi
MCP_PID="$!"

while :; do
    if ! kill -0 "$DASHBOARD_PID" 2>/dev/null; then
        wait "$DASHBOARD_PID"
        exit 1
    fi
    if ! kill -0 "$MCP_PID" 2>/dev/null; then
        wait "$MCP_PID"
        exit 1
    fi
    sleep 2
done
