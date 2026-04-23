#!/usr/bin/env bash

fail_msg() {
    printf '%s\n' "$*" >&2
    return 1
}

detect_container_tool() {
    if [ -n "${CONTAINER_TOOL:-}" ]; then
        printf '%s\n' "$CONTAINER_TOOL"
        return 0
    fi

    if command -v podman >/dev/null 2>&1; then
        printf '%s\n' podman
        return 0
    fi

    if command -v docker >/dev/null 2>&1; then
        printf '%s\n' docker
        return 0
    fi

    fail_msg "Neither podman nor docker is available."
}

load_repo_postgres_env() {
    local project_root="${1:?project root is required}"
    local env_file="${ENV_FILE:-$project_root/.env}"

    if [ ! -f "$env_file" ]; then
        fail_msg "Missing env file: $env_file"
        return 1
    fi

    if ! command -v python3 >/dev/null 2>&1; then
        fail_msg "python3 is required to parse DATABASE_URL"
        return 1
    fi

    set -a
    # shellcheck disable=SC1090
    . <(tr -d '\r' < "$env_file")
    set +a

    if [ -n "${NEBULA_STORE:-}" ] && [ "${NEBULA_STORE}" != "postgres" ]; then
        fail_msg "Expected NEBULA_STORE=postgres in $env_file"
        return 1
    fi

    if [ -z "${DATABASE_URL:-}" ]; then
        fail_msg "DATABASE_URL is required in $env_file"
        return 1
    fi
}

parse_database_url_exports() {
    local database_url="${1:?database url is required}"

    python3 - "$database_url" <<'PY'
import shlex
import sys
from urllib.parse import unquote, urlsplit

parts = urlsplit(sys.argv[1])
if parts.scheme not in {"postgres", "postgresql"}:
    raise SystemExit("DATABASE_URL must use postgres:// or postgresql://")

host = parts.hostname or ""
port = parts.port or 5432
user = unquote(parts.username or "")
password = unquote(parts.password or "")
database = unquote(parts.path.lstrip("/"))

if not host:
    raise SystemExit("DATABASE_URL is missing a host")
if not user:
    raise SystemExit("DATABASE_URL is missing a username")
if not database:
    raise SystemExit("DATABASE_URL is missing a database name")

values = {
    "PG_HOST": host,
    "PG_PORT": str(port),
    "PG_USER": user,
    "PG_PASSWORD": password,
    "PG_DATABASE": database,
}

for key, value in values.items():
    print(f"{key}={shlex.quote(value)}")
PY
}

mask_database_url() {
    local database_url="${1:?database url is required}"

    python3 - "$database_url" <<'PY'
import sys
from urllib.parse import unquote, urlsplit

parts = urlsplit(sys.argv[1])
user = unquote(parts.username or "")
host = parts.hostname or ""
port = parts.port or 5432
database = unquote(parts.path.lstrip("/"))

print(f"postgres://{user}:****@{host}:{port}/{database}")
PY
}