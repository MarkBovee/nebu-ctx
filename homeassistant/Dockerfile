FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine

ARG NEBULA_CTX_VERSION=main
ARG NEBULA_CTX_REPOSITORY=https://github.com/MarkBovee/nebu-ctx.git

RUN apk add --no-cache \
    ca-certificates \
    curl \
    jq \
    git

# Fetch only the dist payload and entrypoint from the repository.
# Sparse checkout avoids pulling the full source tree at build time.
RUN git clone --depth=1 --filter=blob:none --sparse \
        --branch "${NEBULA_CTX_VERSION}" \
        "${NEBULA_CTX_REPOSITORY}" /tmp/nebu-ctx \
    && git -C /tmp/nebu-ctx sparse-checkout init --no-cone \
    && git -C /tmp/nebu-ctx sparse-checkout set 'server/dist/linux/**' 'docker-entrypoint.sh' \
    && git -C /tmp/nebu-ctx checkout \
    && mkdir -p /app \
    && cp -r /tmp/nebu-ctx/server/dist/linux/. /app/ \
    && cp /tmp/nebu-ctx/docker-entrypoint.sh /usr/local/bin/docker-entrypoint.sh \
    && rm -rf /tmp/nebu-ctx

# Create data directory and normalise entrypoint line endings (Windows checkouts)
RUN mkdir -p /data \
    && sed -i 's/\r$//' /usr/local/bin/docker-entrypoint.sh \
    && chmod +x /usr/local/bin/docker-entrypoint.sh

WORKDIR /data

# Single runtime image for standalone and Home Assistant add-on flows.
# The entrypoint switches mode based on whether /data/options.json exists.
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
