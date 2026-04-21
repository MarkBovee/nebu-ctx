ARG TARGETARCH
FROM docker.io/library/debian:bookworm-slim AS runtime

ARG NEBULA_CTX_VERSION=latest
ARG TARGETARCH

WORKDIR /app

RUN apt-get update \
    && apt-get install -y --no-install-recommends jq ca-certificates curl libssl3 \
    && rm -rf /var/lib/apt/lists/*

# Download pre-built binary from GitHub release
RUN BINARY_ARCH="${TARGETARCH}" \
    && if [ "${NEBULA_CTX_VERSION}" = "latest" ]; then \
         DOWNLOAD_URL=$(curl -sL "https://api.github.com/repos/MarkBovee/nebula-ctx/releases/latest" \
           | jq -r ".assets[] | select(.name == \"nebula-ctx-${BINARY_ARCH}\") | .browser_download_url"); \
       else \
         DOWNLOAD_URL="https://github.com/MarkBovee/nebula-ctx/releases/download/${NEBULA_CTX_VERSION}/nebula-ctx-${BINARY_ARCH}"; \
       fi \
    && echo "Downloading: ${DOWNLOAD_URL}" \
    && curl -sL "${DOWNLOAD_URL}" -o /usr/local/bin/nebula-ctx \
    && chmod +x /usr/local/bin/nebula-ctx

COPY run.sh /run.sh

RUN sed -i 's/\r$//' /run.sh \
    && chmod +x /run.sh \
    && mkdir -p /data

EXPOSE 4242 3333

CMD ["/run.sh"]
