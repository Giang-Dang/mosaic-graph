# The Catalog subgraph's container image.
#
# The same file as src/Mosaic.Api/Dockerfile with the project name changed,
# which is worth noticing rather than deduplicating: two services that build
# and run the same way are two services one team can operate.

# ---------------------------------------------------------------------------
# Build stage
# ---------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY global.json Directory.Build.props Directory.Packages.props ./
COPY src/Mosaic.Catalog/Mosaic.Catalog.csproj src/Mosaic.Catalog/
RUN dotnet restore src/Mosaic.Catalog/Mosaic.Catalog.csproj

COPY src/ src/
RUN dotnet publish src/Mosaic.Catalog/Mosaic.Catalog.csproj \
        --no-restore \
        --configuration Release \
        --output /app/publish

# ---------------------------------------------------------------------------
# Runtime stage
# ---------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime

RUN apt-get update \
    && apt-get install --yes --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /app
COPY --from=build /app/publish ./

ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080

USER app

ENTRYPOINT ["dotnet", "Mosaic.Catalog.dll"]
