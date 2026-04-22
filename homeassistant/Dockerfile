FROM docker.io/library/debian:bookworm-slim AS runtime

ARG BUILD_ARCH=amd64
ARG NEBULA_CTX_VERSION=latest
ARG NEBULA_CTX_REPOSITORY=https://github.com/MarkBovee/nebu-ctx.git

WORKDIR /app

RUN apt-get update \
    && apt-get install -y --no-install-recommends jq ca-certificates curl libssl3 \
    && rm -rf /var/lib/apt/lists/*

RUN case "${BUILD_ARCH}" in \
        amd64) RELEASE_ARCH="amd64" ;; \
        aarch64|arm64) RELEASE_ARCH="arm64" ;; \
        *) echo "Unsupported BUILD_ARCH: ${BUILD_ARCH}" >&2; exit 1 ;; \
    esac \
    && if [ "${NEBULA_CTX_VERSION}" = "latest" ]; then \
        DOWNLOAD_URL="https://github.com/MarkBovee/nebu-ctx/releases/latest/download/nebu-ctx-${RELEASE_ARCH}"; \
        VERSION_LABEL="latest"; \
    else \
        DOWNLOAD_URL="https://github.com/MarkBovee/nebu-ctx/releases/download/${NEBULA_CTX_VERSION}/nebu-ctx-${RELEASE_ARCH}"; \
        VERSION_LABEL="${NEBULA_CTX_VERSION}"; \
    fi \
    && test -n "${DOWNLOAD_URL}" \
    && curl -fsSL "${DOWNLOAD_URL}" -o /usr/local/bin/nebu-ctx \
    && chmod +x /usr/local/bin/nebu-ctx \
    && /usr/local/bin/nebu-ctx --help >/dev/null \
    && printf '%s\n' "${VERSION_LABEL}" > /app/nebula_ctx_commit.txt

COPY run.sh /run.sh

RUN sed -i 's/\r$//' /run.sh \
    && chmod +x /run.sh \
    && mkdir -p /data

EXPOSE 4242 3333

CMD ["/run.sh"]
