FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build

ARG BUILD_ARCH=amd64
ARG NEBULA_CTX_VERSION=latest
ARG NEBULA_CTX_REPOSITORY=https://github.com/MarkBovee/nebu-ctx.git

WORKDIR /src

RUN apt-get update \
    && apt-get install -y --no-install-recommends ca-certificates curl tar \
    && rm -rf /var/lib/apt/lists/*

RUN repo="${NEBULA_CTX_REPOSITORY%.git}" \
    && if [ "${NEBULA_CTX_VERSION}" = "latest" ]; then archive_url="${repo}/archive/refs/heads/main.tar.gz"; else archive_url="${repo}/archive/refs/tags/${NEBULA_CTX_VERSION}.tar.gz"; fi \
    && mkdir -p /src/nebu-ctx \
    && curl -fsSL "$archive_url" -o /tmp/nebu-ctx.tar.gz \
    && tar -xzf /tmp/nebu-ctx.tar.gz --strip-components=1 -C /src/nebu-ctx

WORKDIR /src/nebu-ctx

RUN printf '%s\n' "${NEBULA_CTX_VERSION}" > /tmp/nebula_ctx_commit.txt \
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

ENV ASPNETCORE_ENVIRONMENT=Production
ENV DOTNET_EnableDiagnostics=0

EXPOSE 4242 3333

CMD ["/run.sh"]
