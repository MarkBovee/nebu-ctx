FROM mcr.microsoft.com/dotnet/sdk:10.0 AS builder

WORKDIR /src/nebu-ctx
COPY . .

RUN dotnet publish src/server/src/NebuCtx.Server.Host/NebuCtx.Server.Host.csproj \
    -c Release \
    -o /app/publish \
    /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0

RUN apt-get update && apt-get install -y \
    ca-certificates \
    curl \
    && rm -rf /var/lib/apt/lists/*

# Create non-root user
RUN useradd -m -s /bin/bash nebu

COPY --from=builder /app/publish /app
COPY docker-entrypoint.sh /usr/local/bin/docker-entrypoint.sh

# Create data directory
RUN mkdir -p /data && chown nebu:nebu /data
RUN sed -i 's/\r$//' /usr/local/bin/docker-entrypoint.sh \
    && chmod +x /usr/local/bin/docker-entrypoint.sh

USER nebu
WORKDIR /data

# Default: SQLite store. If NEBULA_CTX_HTTP_TOKEN is set, the entrypoint binds
# on 0.0.0.0; otherwise it stays on 127.0.0.1 for safety.
ENV NEBULA_CTX_DATA_DIR=/data
ENV NEBULA_STORE=sqlite
ENV NEBULA_CTX_HTTP_PORT=4242
ENV NEBULA_CTX_PORT=3333

EXPOSE 4242 3333

HEALTHCHECK --interval=30s --timeout=5s --retries=3 CMD ["/bin/sh", "-c", "curl -fsS http://127.0.0.1:${NEBULA_CTX_HTTP_PORT:-4242}/health || exit 1"]

ENTRYPOINT ["/usr/local/bin/docker-entrypoint.sh"]
