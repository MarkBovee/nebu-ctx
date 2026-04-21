# Multi-stage build for nebu-ctx
# Stage 1: Build
FROM docker.io/library/rust:1.95-slim-bookworm AS builder

RUN apt-get update && apt-get install -y \
    pkg-config \
    libssl-dev \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /usr/src/nebu-ctx
COPY . .

# Build release binary with all features for server deployment
RUN cargo build --release --features cloud-server --bin nebu-ctx

# Stage 2: Runtime
FROM docker.io/library/debian:bookworm-slim

RUN apt-get update && apt-get install -y \
    ca-certificates \
    libssl3 \
    curl \
    && rm -rf /var/lib/apt/lists/*

# Create non-root user
RUN useradd -m -s /bin/bash nebu

# Copy binary from builder
COPY --from=builder /usr/src/nebu-ctx/target/release/nebu-ctx /usr/local/bin/nebu-ctx
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

EXPOSE 4242

# Health check
HEALTHCHECK --interval=30s --timeout=5s --retries=3 CMD ["/bin/sh", "-c", "curl -fsS http://127.0.0.1:${NEBULA_CTX_HTTP_PORT:-4242}/health || exit 1"]

ENTRYPOINT ["/usr/local/bin/docker-entrypoint.sh"]
