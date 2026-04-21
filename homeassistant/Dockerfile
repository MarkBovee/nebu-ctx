FROM docker.io/library/rust:1.95-slim-bookworm AS build

ARG NEBULA_CTX_VERSION=latest
ARG NEBULA_CTX_REPOSITORY=https://github.com/MarkBovee/nebu-ctx.git

WORKDIR /src

RUN apt-get update \
    && apt-get install -y --no-install-recommends git pkg-config libssl-dev ca-certificates \
    && rm -rf /var/lib/apt/lists/*

RUN if [ "${NEBULA_CTX_VERSION}" = "latest" ]; then \
        git clone --depth 1 "${NEBULA_CTX_REPOSITORY}" nebu-ctx; \
    else \
        git clone --depth 1 --branch "${NEBULA_CTX_VERSION}" "${NEBULA_CTX_REPOSITORY}" nebu-ctx; \
    fi

WORKDIR /src/nebu-ctx

RUN cargo build --release --features cloud-server --bin nebu-ctx \
    && git rev-parse HEAD > /tmp/nebula_ctx_commit.txt

FROM docker.io/library/debian:bookworm-slim AS runtime

WORKDIR /app

RUN apt-get update \
    && apt-get install -y --no-install-recommends jq ca-certificates curl libssl3 \
    && rm -rf /var/lib/apt/lists/*

COPY --from=build /src/nebu-ctx/target/release/nebu-ctx /usr/local/bin/nebu-ctx
COPY --from=build /tmp/nebula_ctx_commit.txt /app/nebula_ctx_commit.txt

RUN chmod +x /usr/local/bin/nebu-ctx \
    && /usr/local/bin/nebu-ctx --help >/dev/null

COPY run.sh /run.sh

RUN sed -i 's/\r$//' /run.sh \
    && chmod +x /run.sh \
    && mkdir -p /data

EXPOSE 4242 3333

CMD ["/run.sh"]
