FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG TARGETARCH
WORKDIR /src
COPY src/ .
RUN dotnet publish ObjeX.Api/ObjeX.Api.csproj \
    -c Release \
    -a $TARGETARCH \
    --no-self-contained \
    -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
RUN mkdir -p /data/db /data/blobs
COPY --from=build /app/publish .
ENV ASPNETCORE_URLS=http://+:8080
ENV ConnectionStrings__DefaultConnection="Data Source=/data/db/objex.db"
ENV Storage__BasePath="/data/blobs"
VOLUME ["/data"]
EXPOSE 8080
ENTRYPOINT ["dotnet", "ObjeX.Api.dll"]
