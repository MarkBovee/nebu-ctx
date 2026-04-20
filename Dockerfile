# Multi-stage build for nebula-ctx
# Stage 1: Build
FROM rust:1.95-slim-bookworm AS builder

RUN apt-get update && apt-get install -y \
    pkg-config \
    libssl-dev \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /usr/src/nebula-ctx
COPY . .

# Build release binary with all features for server deployment
RUN cargo build --release --features cloud-server -p nebula-ctx

# Stage 2: Runtime
FROM debian:bookworm-slim

RUN apt-get update && apt-get install -y \
    ca-certificates \
    libssl3 \
    curl \
    && rm -rf /var/lib/apt/lists/*

# Create non-root user
RUN useradd -m -s /bin/bash nebula

# Copy binary from builder
COPY --from=builder /usr/src/nebula-ctx/target/release/nebula-ctx /usr/local/bin/nebula-ctx
COPY docker-entrypoint.sh /usr/local/bin/docker-entrypoint.sh

# Create data directory
RUN mkdir -p /data && chown nebula:nebula /data
RUN chmod +x /usr/local/bin/docker-entrypoint.sh

USER nebula
WORKDIR /data

# Default: SQLite store. If NEBULA_CTX_HTTP_TOKEN is set, the entrypoint binds
# on 0.0.0.0; otherwise it stays on 127.0.0.1 for safety.
ENV NEBULA_CTX_DATA_DIR=/data
ENV NEBULA_STORE=sqlite
ENV NEBULA_CTX_HTTP_PORT=8099

EXPOSE 8099

# Health check
HEALTHCHECK --interval=30s --timeout=5s --retries=3 \
    CMD-SHELL curl -fsS http://127.0.0.1:${NEBULA_CTX_HTTP_PORT:-8099}/health || exit 1

ENTRYPOINT ["/usr/local/bin/docker-entrypoint.sh"]
