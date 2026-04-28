# Local development Dockerfile — fast build using committed server/dist/linux payload (COPY).
# For production/HA smoke tests use: ADDON_DOCKERFILE=homeassistant/Dockerfile bash tests/local-addon-test.sh
# homeassistant/Dockerfile is self-contained (git sparse-checkout) and is what HA Supervisor builds.
FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine

RUN apk add --no-cache \
    ca-certificates \
    curl \
    jq

COPY server/dist/linux/ /app/
COPY docker-entrypoint.sh /usr/local/bin/docker-entrypoint.sh

RUN mkdir -p /data \
    && sed -i 's/\r$//' /usr/local/bin/docker-entrypoint.sh \
    && chmod +x /usr/local/bin/docker-entrypoint.sh

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
