#!/bin/sh
set -eu

OPTIONS_FILE="/data/options.json"
AUTH_TOKEN_FILE="/data/auth_token"

log() {
    printf '[nebu-ctx] %s\n' "$1"
}

require_postgres_configuration() {
    configured_store="${NEBULA_STORE:-postgres}"

    if [ "$configured_store" != "postgres" ]; then
        log "NEBULA_STORE=$configured_store is not supported. Only postgres is supported."
        exit 1
    fi

    if [ -z "${DATABASE_URL:-}" ]; then
        log "DATABASE_URL is required. SQLite is no longer supported."
        exit 1
    fi

    export NEBULA_STORE="postgres"
}

map_log_level() {
    case "$1" in
        debug) printf '%s' 'Debug' ;;
        info) printf '%s' 'Information' ;;
        warn) printf '%s' 'Warning' ;;
        error) printf '%s' 'Error' ;;
        *) printf '%s' 'Information' ;;
    esac
}

configure_standalone_mode() {
    host="${NEBULA_CTX_HOST:-}"
    port="${NEBULA_CTX_HTTP_PORT:-4242}"
    dashboard_port="${NEBULA_CTX_PORT:-3333}"
    token="${NEBULA_CTX_HTTP_TOKEN:-}"

    if [ -z "$host" ]; then
        if [ -n "$token" ]; then
            host="0.0.0.0"
        else
            host="127.0.0.1"
        fi
    fi

    export NEBU_CTX_DATA_DIR="${NEBU_CTX_DATA_DIR:-/data}"
    export NEBULA_CTX_DATA_DIR="${NEBULA_CTX_DATA_DIR:-/data}"
    export NEBULA_CTX_HOST="$host"
    export NEBULA_CTX_HTTP_PORT="$port"
    export NEBULA_CTX_PORT="$dashboard_port"

    require_postgres_configuration

    log "Starting standalone runtime on ${host}:${dashboard_port} (dashboard) and ${host}:${port} (MCP)"
}

configure_addon_mode() {
    if [ ! -f "$OPTIONS_FILE" ]; then
        log "Missing $OPTIONS_FILE"
        exit 1
    fi

    pg_host="$(jq -r '.postgres_host // "homeassistant"' "$OPTIONS_FILE")"
    pg_port="$(jq -r '.postgres_port // 5432' "$OPTIONS_FILE")"
    pg_database="$(jq -r '.postgres_database // "nebula_ctx"' "$OPTIONS_FILE")"
    pg_user="$(jq -r '.postgres_username // "postgres"' "$OPTIONS_FILE")"
    pg_password="$(jq -r '.postgres_password // ""' "$OPTIONS_FILE")"
    log_level="$(jq -r '.log_level // "info"' "$OPTIONS_FILE")"
    project_root="$(jq -r '.project_root // "/share"' "$OPTIONS_FILE")"

    if [ -z "$project_root" ] || [ "$project_root" = "null" ]; then
        project_root="/share"
    fi

    if [ ! -f "$AUTH_TOKEN_FILE" ] || [ ! -s "$AUTH_TOKEN_FILE" ]; then
        token="nctx_$(od -An -tx1 -N32 /dev/urandom | tr -d ' \n')"
        printf '%s' "$token" > "$AUTH_TOKEN_FILE"
        log "Generated new auth token"
    fi
    token="$(cat "$AUTH_TOKEN_FILE")"

    export DATABASE_URL="postgresql://${pg_user}:${pg_password}@${pg_host}:${pg_port}/${pg_database}"
    export NEBULA_STORE="postgres"
    export NEBU_CTX_DATA_DIR="/data"
    export NEBULA_CTX_DATA_DIR="/data"
    export NEBU_CTX_DASHBOARD_PROJECT="$project_root"
    export NEBULA_CTX_DASHBOARD_PROJECT="$project_root"
    export NEBU_CTX_DASHBOARD_DISABLE_AUTH="1"
    export NEBULA_CTX_DASHBOARD_DISABLE_AUTH="1"
    export NEBU_CTX_TOKEN_FILE="$AUTH_TOKEN_FILE"
    export NEBULA_CTX_TOKEN_FILE="$AUTH_TOKEN_FILE"
    export NEBULA_CTX_HOST="0.0.0.0"
    export NEBULA_CTX_HTTP_PORT="4242"
    export NEBULA_CTX_PORT="3333"
    export NEBULA_CTX_HTTP_TOKEN="$token"

    require_postgres_configuration

    if [ -n "$log_level" ] && [ "$log_level" != "null" ]; then
        export Logging__LogLevel__Default="$(map_log_level "$log_level")"
    fi

    log "Initializing Home Assistant mode"
    log "Project root: $project_root"
    log "Store: postgres"
    log "PostgreSQL: ${pg_host}:${pg_port}/${pg_database}"
    log "MCP auth token: $token"
}

if [ "$#" -gt 0 ]; then
    exec "$@"
fi

if [ -f /app/nebula_ctx_commit.txt ]; then
    log "Image source commit: $(cat /app/nebula_ctx_commit.txt)"
fi

if [ -f "$OPTIONS_FILE" ]; then
    configure_addon_mode
else
    configure_standalone_mode
fi

exec dotnet /app/NebuCtx.Server.Host.dll