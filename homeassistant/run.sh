#!/bin/sh
set -eu

OPTIONS_FILE="/data/options.json"
AUTH_TOKEN_FILE="/data/auth_token"
MCP_PID=""
DASHBOARD_PID=""

log() {
    printf '[nebu-ctx] %s\n' "$1"
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

# --- Read options ---
PG_HOST="$(jq -r '.postgres_host // "homeassistant"' "$OPTIONS_FILE")"
PG_PORT="$(jq -r '.postgres_port // 5432' "$OPTIONS_FILE")"
PG_DB="$(jq -r '.postgres_database // "nebula_ctx"' "$OPTIONS_FILE")"
PG_USER="$(jq -r '.postgres_username // "postgres"' "$OPTIONS_FILE")"
PG_PASS="$(jq -r '.postgres_password // ""' "$OPTIONS_FILE")"
LOG_LEVEL="$(jq -r '.log_level // "info"' "$OPTIONS_FILE")"
PROJECT_ROOT="$(jq -r '.project_root // "/share"' "$OPTIONS_FILE")"

if [ -z "$PROJECT_ROOT" ] || [ "$PROJECT_ROOT" = "null" ]; then
    PROJECT_ROOT="/share"
fi

# --- Server mode: add-on always uses PostgreSQL ---
DATABASE_URL="postgresql://${PG_USER}:${PG_PASS}@${PG_HOST}:${PG_PORT}/${PG_DB}"
export DATABASE_URL
export NEBULA_STORE="postgres"

# --- Auth token: auto-generate once and persist for dashboard/MCP use ---
if [ ! -f "$AUTH_TOKEN_FILE" ] || [ ! -s "$AUTH_TOKEN_FILE" ]; then
    TOKEN="nctx_$(od -An -tx1 -N32 /dev/urandom | tr -d ' \n')"
    printf '%s' "$TOKEN" > "$AUTH_TOKEN_FILE"
    log "Generated new auth token"
fi
AUTH_TOKEN="$(cat "$AUTH_TOKEN_FILE")"

# --- Environment ---
export NEBU_CTX_DATA_DIR="/data"
export NEBULA_CTX_DATA_DIR="/data"
export NEBU_CTX_DASHBOARD_PROJECT="$PROJECT_ROOT"
export NEBULA_CTX_DASHBOARD_PROJECT="$PROJECT_ROOT"
export NEBU_CTX_DASHBOARD_DISABLE_AUTH="1"
export NEBULA_CTX_DASHBOARD_DISABLE_AUTH="1"
export NEBU_CTX_TOKEN_FILE="$AUTH_TOKEN_FILE"
export NEBULA_CTX_TOKEN_FILE="$AUTH_TOKEN_FILE"

if [ -n "$LOG_LEVEL" ] && [ "$LOG_LEVEL" != "null" ]; then
    export RUST_LOG="$LOG_LEVEL"
fi

log "Initializing nebu-ctx add-on"
log "Project root: $PROJECT_ROOT"
log "Store: postgres"
log "PostgreSQL: ${PG_HOST}:${PG_PORT}/${PG_DB}"
log "MCP auth token: $AUTH_TOKEN"

if [ -f "/app/nebula_ctx_commit.txt" ]; then
    log "Image source commit: $(cat /app/nebula_ctx_commit.txt)"
fi

# --- Start dashboard (ingress) ---
log "Starting dashboard ingress service on 0.0.0.0:3333"
nebu-ctx dashboard --host=0.0.0.0 --port=3333 &
DASHBOARD_PID="$!"

# --- Start MCP HTTP (always exposed) ---
log "Starting MCP HTTP service on 0.0.0.0:4242"
nebu-ctx serve --host 0.0.0.0 --port 4242 --project-root "$PROJECT_ROOT" --auth-token "$AUTH_TOKEN" &
MCP_PID="$!"

# --- Health check loop ---
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
