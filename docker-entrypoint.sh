#!/bin/sh
set -eu

if [ "$#" -gt 0 ]; then
    exec "$@"
fi

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

export NEBULA_CTX_HOST="$host"
export NEBULA_CTX_HTTP_PORT="$port"
export NEBULA_CTX_PORT="$dashboard_port"

exec dotnet /app/NebuCtx.Server.Host.dll