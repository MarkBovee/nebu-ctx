FROM mcr.microsoft.com/dotnet/aspnet:10.0

RUN apt-get update && apt-get install -y \
    ca-certificates \
    curl \
    jq \
    && rm -rf /var/lib/apt/lists/*

# Create non-root user
RUN useradd -m -s /bin/bash nebu

COPY dist/server/linux/ /app/
COPY docker-entrypoint.sh /usr/local/bin/docker-entrypoint.sh

# Create data directory
RUN mkdir -p /data && chown nebu:nebu /data
RUN sed -i 's/\r$//' /usr/local/bin/docker-entrypoint.sh \
    && chmod +x /usr/local/bin/docker-entrypoint.sh

USER nebu
WORKDIR /data

# Single runtime image for standalone and Home Assistant add-on flows.
# The entrypoint switches mode based on whether /data/options.json exists.
ENV NEBULA_CTX_DATA_DIR=/data
ENV NEBU_CTX_DATA_DIR=/data
ENV NEBULA_STORE=sqlite
ENV NEBULA_CTX_HTTP_PORT=4242
ENV NEBULA_CTX_PORT=3333
ENV ASPNETCORE_ENVIRONMENT=Production
ENV DOTNET_EnableDiagnostics=0

EXPOSE 4242 3333

HEALTHCHECK --interval=30s --timeout=5s --retries=3 CMD ["/bin/sh", "-c", "curl -fsS http://127.0.0.1:${NEBULA_CTX_HTTP_PORT:-4242}/health || exit 1"]

ENTRYPOINT ["/usr/local/bin/docker-entrypoint.sh"]
