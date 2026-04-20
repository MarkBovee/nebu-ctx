#!/bin/sh
set -eu

if [ "$#" -gt 0 ]; then
    exec nebula-ctx "$@"
fi

host="${NEBULA_CTX_HTTP_HOST:-}"
port="${NEBULA_CTX_HTTP_PORT:-8099}"
token="${NEBULA_CTX_HTTP_TOKEN:-}"

if [ -z "$host" ]; then
    if [ -n "$token" ]; then
        host="0.0.0.0"
    else
        host="127.0.0.1"
    fi
fi

set -- nebula-ctx serve --host "$host" --port "$port"

if [ -n "$token" ]; then
    set -- "$@" --auth-token "$token"
fi

exec "$@"