FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build

ARG BUILD_ARCH=amd64
ARG NEBULA_CTX_VERSION=latest
ARG NEBULA_CTX_REPOSITORY=https://github.com/MarkBovee/nebu-ctx.git

WORKDIR /src

RUN apt-get update \
    && apt-get install -y --no-install-recommends git ca-certificates \
    && rm -rf /var/lib/apt/lists/*

RUN git clone "${NEBULA_CTX_REPOSITORY}" nebu-ctx

WORKDIR /src/nebu-ctx

RUN if [ "${NEBULA_CTX_VERSION}" != "latest" ]; then git checkout "${NEBULA_CTX_VERSION}"; fi \
    && printf '%s\n' "${NEBULA_CTX_VERSION}" > /tmp/nebula_ctx_commit.txt \
    && dotnet publish src/server/src/NebuCtx.Server.Host/NebuCtx.Server.Host.csproj \
        -c Release \
        -o /app/publish \
        /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime

WORKDIR /app

RUN apt-get update \
    && apt-get install -y --no-install-recommends jq ca-certificates curl \
    && rm -rf /var/lib/apt/lists/*

COPY --from=build /app/publish /app
COPY --from=build /tmp/nebula_ctx_commit.txt /app/nebula_ctx_commit.txt

COPY run.sh /run.sh

RUN sed -i 's/\r$//' /run.sh \
    && chmod +x /run.sh \
    && mkdir -p /data

EXPOSE 4242 3333

CMD ["/run.sh"]
