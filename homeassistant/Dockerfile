FROM rust:1.95-slim-bookworm AS build
WORKDIR /src

ARG NEBULA_CTX_REPO_URL=https://github.com/MarkBovee/nebula-ctx.git
ARG NEBULA_CTX_REPO_REF=main
ARG BUILD_VERSION=dev

RUN apt-get update \
    && apt-get install -y --no-install-recommends git pkg-config libssl-dev ca-certificates \
    && rm -rf /var/lib/apt/lists/*

RUN echo "nebula-ctx build version: ${BUILD_VERSION}" \
    && git clone --depth 1 --branch "${NEBULA_CTX_REPO_REF}" "${NEBULA_CTX_REPO_URL}" /src/nebula-ctx \
    && git -C /src/nebula-ctx rev-parse HEAD > /tmp/nebula_ctx_commit.txt

WORKDIR /src/nebula-ctx
RUN cargo build --release --features cloud-server -p nebula-ctx

FROM debian:bookworm-slim AS runtime
WORKDIR /app

RUN apt-get update \
    && apt-get install -y --no-install-recommends jq ca-certificates curl libssl3 \
    && rm -rf /var/lib/apt/lists/*

COPY --from=build /src/nebula-ctx/target/release/nebula-ctx /usr/local/bin/nebula-ctx
COPY --from=build /tmp/nebula_ctx_commit.txt /app/nebula_ctx_commit.txt
COPY run.sh /run.sh

RUN sed -i 's/\r$//' /run.sh \
    && chmod +x /run.sh \
    && mkdir -p /data

EXPOSE 4242 3333

CMD ["/run.sh"]