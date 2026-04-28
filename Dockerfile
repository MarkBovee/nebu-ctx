# Multi-stage build: SDK compiles the server, then copies into a lean Alpine runtime image.
# This Dockerfile is used for both GHCR image publishing (via release.yml) and local dev builds.
# Production HA addon: image is pulled directly from ghcr.io/markbovee/nebu-ctx (see homeassistant/config.yaml).

# ── Stage 1: build ──────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY server/ .
RUN dotnet publish src/NebuCtx.Server.Host/NebuCtx.Server.Host.csproj \
    -c Release \
    -p:AllowMissingPrunePackageData=true \
    -o /app/publish

# ── Stage 2: runtime ─────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine AS runtime

ARG GIT_COMMIT=unknown

RUN apk add --no-cache \
    ca-certificates \
    curl \
    jq

COPY --from=build /app/publish /app/
COPY docker-entrypoint.sh /usr/local/bin/docker-entrypoint.sh

RUN mkdir -p /data \
    && sed -i 's/\r$//' /usr/local/bin/docker-entrypoint.sh \
    && chmod +x /usr/local/bin/docker-entrypoint.sh \
    && echo "${GIT_COMMIT}" > /app/nebula_ctx_commit.txt

WORKDIR /data

ENV NEBULA_CTX_DATA_DIR=/data
ENV NEBU_CTX_DATA_DIR=/data
ENV NEBULA_STORE=postgres
ENV NEBULA_CTX_HTTP_PORT=4242
ENV NEBULA_CTX_PORT=3333
ENV ASPNETCORE_ENVIRONMENT=Production
ENV DOTNET_EnableDiagnostics=0

EXPOSE 4242 3333

HEALTHCHECK --interval=30s --timeout=5s --retries=3 CMD ["/bin/sh", "-c", "curl -fsS http://127.0.0.1:${NEBULA_CTX_HTTP_PORT:-4242}/health || exit 1"]

ENTRYPOINT ["/usr/local/bin/docker-entrypoint.sh"]
