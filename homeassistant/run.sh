#!/usr/bin/env bashio

# Home Assistant addon entrypoint for nebula-ctx

STORE=$(bashio::config 'store')
DATABASE_URL=$(bashio::config 'database_url')
AUTH_TOKEN=$(bashio::config 'auth_token')
LOG_LEVEL=$(bashio::config 'log_level')

export NEBULA_CTX_DATA_DIR="/data"

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
    export NEBULA_STORE="postgres"
    export DATABASE_URL="$DATABASE_URL"
    bashio::log.info "Using PostgreSQL backend"
else
    export NEBULA_STORE="sqlite"
    bashio::log.info "Using SQLite backend at /data/nebula-ctx.db"
fi

HOST="127.0.0.1"
if [ -n "$AUTH_TOKEN" ]; then
    HOST="0.0.0.0"
fi

# Start MCP HTTP server
bashio::log.info "Starting nebula-ctx MCP server on ${HOST}:8099"

if [ -n "$AUTH_TOKEN" ]; then
    exec nebula-ctx serve --host "$HOST" --port 8099 --auth-token "$AUTH_TOKEN"
fi

bashio::log.warning "No auth_token configured; server will bind to 127.0.0.1 only"
exec nebula-ctx serve --host "$HOST" --port 8099
