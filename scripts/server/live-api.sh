#!/usr/bin/env bash
set -euo pipefail

PROJECT_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
ENV_FILE="${ENV_FILE:-$PROJECT_ROOT/.env}"
METHOD="GET"
ENDPOINT="${ENDPOINT:-http://192.168.1.135:4242}"
BODY_FILE=""
OUTPUT_FILE=""

usage() {
    cat <<'EOF'
Usage: scripts/server/live-api.sh [options] <path>

Options:
  -X, --method <method>       HTTP method (default: GET)
  -d, --body-file <file>      JSON request body file
  -o, --output <file>         Write response body to file
  -e, --endpoint <url>        Override endpoint (default: http://192.168.1.135:4242)
  --env-file <file>           Override env file (default: repo .env)

Examples:
  scripts/server/live-api.sh /v1/manifest
  scripts/server/live-api.sh -X POST -d /tmp/request.json /v1/tools/call
  scripts/server/live-api.sh -o /tmp/projects.json /api/projects
EOF
}

while [[ $# -gt 0 ]]; do
    case "$1" in
        -X|--method)
            METHOD="$2"
            shift 2
            ;;
        -d|--body-file)
            BODY_FILE="$2"
            shift 2
            ;;
        -o|--output)
            OUTPUT_FILE="$2"
            shift 2
            ;;
        -e|--endpoint)
            ENDPOINT="$2"
            shift 2
            ;;
        --env-file)
            ENV_FILE="$2"
            shift 2
            ;;
        -h|--help)
            usage
            exit 0
            ;;
        --)
            shift
            break
            ;;
        -*)
            printf 'Unknown option: %s\n' "$1" >&2
            usage >&2
            exit 1
            ;;
        *)
            break
            ;;
    esac
done

if [[ $# -ne 1 ]]; then
    usage >&2
    exit 1
fi

PATH_SUFFIX="$1"

if [[ ! -f "$ENV_FILE" ]]; then
    printf 'Env file not found: %s\n' "$ENV_FILE" >&2
    exit 1
fi

set -a
source "$ENV_FILE"
set +a

if [[ -z "${NEBULA_CTX_HTTP_TOKEN:-}" ]]; then
    printf 'NEBULA_CTX_HTTP_TOKEN missing in %s\n' "$ENV_FILE" >&2
    exit 1
fi

URL="${ENDPOINT%/}${PATH_SUFFIX}"
CURL_ARGS=(
    -fsS
    -X "$METHOD"
    -H "Authorization: Bearer ${NEBULA_CTX_HTTP_TOKEN}"
)

if [[ -n "$BODY_FILE" ]]; then
    CURL_ARGS+=(
        -H "Content-Type: application/json"
        --data-binary "@$BODY_FILE"
    )
fi

if [[ -n "$OUTPUT_FILE" ]]; then
    mkdir -p "$(dirname "$OUTPUT_FILE")"
    curl "${CURL_ARGS[@]}" "$URL" > "$OUTPUT_FILE"
else
    curl "${CURL_ARGS[@]}" "$URL"
fi
