# Pin to 10.0 — intentional for .NET 10 preview/RC; update when GA lands
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG TARGETARCH
WORKDIR /src

# Copy project files first for layer caching on NuGet restore
COPY src/ObjeX.Api/ObjeX.Api.csproj ObjeX.Api/
COPY src/ObjeX.Core/ObjeX.Core.csproj ObjeX.Core/
COPY src/ObjeX.Infrastructure/ObjeX.Infrastructure.csproj ObjeX.Infrastructure/
COPY src/ObjeX.Migrations.PostgreSql/ObjeX.Migrations.PostgreSql.csproj ObjeX.Migrations.PostgreSql/
COPY src/ObjeX.Web/ObjeX.Web.csproj ObjeX.Web/
RUN dotnet restore ObjeX.Api/ObjeX.Api.csproj -a $TARGETARCH

# Copy remaining source and publish
COPY src/ .
RUN dotnet publish ObjeX.Api/ObjeX.Api.csproj \
    -c Release \
    -a $TARGETARCH \
    --no-self-contained \
    --no-restore \
    -o /app/publish

# Pin to 10.0 — intentional for .NET 10 preview/RC; update when GA lands
FROM mcr.microsoft.com/dotnet/aspnet:10.0

# Install curl for container healthcheck, then clean up
RUN apt-get update && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /app
RUN mkdir -p /data/db /data/blobs && chown -R app:app /data
COPY --from=build --chown=app:app /app/publish .

ENV ASPNETCORE_URLS=http://+:9001
ENV ConnectionStrings__DefaultConnection="Data Source=/data/db/objex.db"
ENV Storage__BasePath="/data/blobs"
VOLUME ["/data"]
EXPOSE 9001
EXPOSE 9000

HEALTHCHECK --interval=30s --timeout=5s --retries=3 \
    CMD curl -f http://localhost:9001/health || exit 1

USER app
ENTRYPOINT ["dotnet", "ObjeX.Api.dll"]
