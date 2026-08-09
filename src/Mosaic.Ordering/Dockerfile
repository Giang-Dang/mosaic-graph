# The Ordering subgraph's container image.
#
# The same file as src/Mosaic.Catalog/Dockerfile with the project name changed.
# Six copies of it is not duplication worth removing: two services that build
# and run the same way are two services one team can operate, and six that do
# are a platform.

# ---------------------------------------------------------------------------
# Build stage
# ---------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY global.json Directory.Build.props Directory.Packages.props ./
COPY src/Mosaic.ServiceDefaults/Mosaic.ServiceDefaults.csproj src/Mosaic.ServiceDefaults/
COPY src/Mosaic.Ordering/Mosaic.Ordering.csproj src/Mosaic.Ordering/
RUN dotnet restore src/Mosaic.Ordering/Mosaic.Ordering.csproj

COPY src/ src/
RUN dotnet publish src/Mosaic.Ordering/Mosaic.Ordering.csproj \
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

ENTRYPOINT ["dotnet", "Mosaic.Ordering.dll"]