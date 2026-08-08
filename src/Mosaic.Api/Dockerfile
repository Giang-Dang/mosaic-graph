# Mosaic's container image.
#
# The build context is the repository root, not this folder, so every path
# below is written from the root. docker-compose.yml sets that up; building by
# hand means `docker build -f src/Mosaic.Api/Dockerfile .` from the root.

# ---------------------------------------------------------------------------
# Build stage
# ---------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore needs more than the csproj. global.json pins the SDK feature band,
# Directory.Build.props carries the target framework, and central package
# management puts every version in Directory.Packages.props. Copying those four
# files first, restoring, and only then copying the source keeps the restore
# layer cached until a dependency actually changes.
COPY global.json Directory.Build.props Directory.Packages.props ./
COPY src/Mosaic.Api/Mosaic.Api.csproj src/Mosaic.Api/
RUN dotnet restore src/Mosaic.Api/Mosaic.Api.csproj

# The source changes on every commit, so it arrives after the cached restore.
COPY src/ src/
RUN dotnet publish src/Mosaic.Api/Mosaic.Api.csproj \
        --no-restore \
        --configuration Release \
        --output /app/publish

# ---------------------------------------------------------------------------
# Runtime stage
# ---------------------------------------------------------------------------
# The full aspnet image, not one of the -chiseled variants. Chiseled has no
# shell, and the healthcheck in docker-compose.yml runs an HTTP client inside
# this container. The full image does have a shell but ships neither curl nor
# wget, so we add curl ourselves.
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime

RUN apt-get update \
    && apt-get install --yes --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /app
COPY --from=build /app/publish ./

# Kestrel binds 8080 in the container. docker-compose.yml publishes that as
# 5100 on the host so http://localhost:5100/graphql is the right URL whether
# you started the app with `dotnet run` or with `docker compose up`.
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080

# The aspnet images already define an unprivileged "app" user (uid 1654).
# Nothing here writes to disk, so the published files stay root-owned and
# read-only to the process.
USER app

ENTRYPOINT ["dotnet", "Mosaic.Api.dll"]
