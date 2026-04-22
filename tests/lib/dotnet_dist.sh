#!/usr/bin/env bash
set -euo pipefail

resolve_dotnet_dist_rid() {
    if [ -n "${DOTNET_DIST_RID:-}" ]; then
        printf '%s\n' "$DOTNET_DIST_RID"
        return 0
    fi

    case "$(uname -m)" in
        x86_64|amd64) printf '%s\n' 'linux-x64' ;;
        aarch64|arm64) printf '%s\n' 'linux-arm64' ;;
        *) printf '%s\n' 'linux-x64' ;;
    esac
}

publish_dotnet_server_dist() {
    local project_root="${1:?project root is required}"
    local runtime_id
    local output_dir

    if ! command -v dotnet >/dev/null 2>&1; then
        fail_msg "dotnet is required to publish the .NET server dist output"
        return 1
    fi

    runtime_id="$(resolve_dotnet_dist_rid)"
    output_dir="${DOTNET_DIST_DIR:-$project_root/dist/server/linux}"

    printf '=== Publishing local .NET host (%s -> %s) ===\n' "$runtime_id" "$output_dir"
    rm -rf "$output_dir"
    mkdir -p "$output_dir"

    NEBULA_ALLOW_MNT_DOTNET=1 dotnet publish "$project_root/src/server/src/NebuCtx.Server.Host/NebuCtx.Server.Host.csproj" \
        -c Release \
        -r "$runtime_id" \
        --self-contained false \
        -o "$output_dir" \
        /p:UseAppHost=false

    if [ ! -f "$output_dir/NebuCtx.Server.Host.dll" ]; then
        fail_msg "Expected $output_dir/NebuCtx.Server.Host.dll after publish"
        return 1
    fi
}