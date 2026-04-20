#!/usr/bin/env bashio

# Home Assistant addon entrypoint for nebula-ctx

STORE=$(bashio::config 'store')
DATABASE_URL=$(bashio::config 'database_url')
AUTH_TOKEN=$(bashio::config 'auth_token')
LOG_LEVEL=$(bashio::config 'log_level')

export NEBULA_CTX_DATA_DIR="/data"
export NEBULA_CTX_HTTP_PORT="8099"

if [ -n "$AUTH_TOKEN" ]; then
    export NEBULA_CTX_HTTP_TOKEN="$AUTH_TOKEN"
fi

if [ -n "$LOG_LEVEL" ]; then
    export RUST_LOG="$LOG_LEVEL"
fi

# Initialize store
bashio::log.info "Initializing nebula-ctx with store: $STORE"

if [ "$STORE" = "postgres" ]; then
    if [ -z "$DATABASE_URL" ]; then
        bashio::log.error "PostgreSQL store selected but no database_url configured"
        bashio::exit.nok
    fi
    export NEBULA_CTX_STORE="postgres"
    export DATABASE_URL="$DATABASE_URL"
    bashio::log.info "Using PostgreSQL backend"
else
    export NEBULA_CTX_STORE="sqlite"
    bashio::log.info "Using SQLite backend at /data/nebula-ctx.db"
fi

# Start MCP HTTP server
bashio::log.info "Starting nebula-ctx MCP server on port 8099"
exec nebula-ctx serve
