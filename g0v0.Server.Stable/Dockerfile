# Build stage
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src

# The stable csproj references the sibling repo g0v0.Server.Common via ProjectReference
# (..\..\g0v0.Server.Common\g0v0.Server.Common\g0v0.Server.Common.csproj), so the two repos must
# sit side-by-side inside the build container, mirroring the on-disk layout:
#   /src/g0v0.Server.Stable/g0v0.Server.Stable/   <- this repo
#   /src/g0v0.Server.Common/                       <- named build context 'common'
# Build with:
#   docker buildx build --build-context common=/path/to/g0v0.Server.Common -f g0v0.Server.Stable/Dockerfile .
COPY global.json ./
COPY g0v0.Server.Stable/g0v0.Server.Stable.csproj g0v0.Server.Stable/g0v0.Server.Stable/
COPY --from=common g0v0.Server.Common/g0v0.Server.Common.csproj g0v0.Server.Common/g0v0.Server.Common/
RUN dotnet restore g0v0.Server.Stable/g0v0.Server.Stable/g0v0.Server.Stable.csproj

COPY g0v0.Server.Stable/ g0v0.Server.Stable/g0v0.Server.Stable/
COPY --from=common . g0v0.Server.Common/
WORKDIR /src/g0v0.Server.Stable/g0v0.Server.Stable
RUN dotnet publish ./g0v0.Server.Stable.csproj -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

# Trim unneeded artifacts BEFORE copying to the runtime image, so the image stays small:
# - runtimes/ ships native binaries for every OS/architecture; the server only needs the managed linux-x64 runtime.
# - osu.Game.Resources.dll is the full osu! game resource pack, not needed by the headless server.
# - satellite culture resource assemblies, debug symbols and XML docs are not needed at runtime.
RUN rm -rf /app/publish/runtimes \
    && rm -f /app/publish/osu.Game.Resources.dll \
    && find /app/publish -maxdepth 1 -mindepth 1 -type d ! -name config ! -name rulesets ! -name storage -exec rm -rf {} + \
    && rm -f /app/publish/*.pdb /app/publish/*.xml

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
EXPOSE 8080

# Runtime data lives in mounted volumes, never baked into the image.
#   config/   - JSON configuration files (general.json, game.json, storage.json)
#   rulesets/ - custom osu! ruleset DLLs
#   storage/  - local file storage (replays, screenshots etc.)
VOLUME ["/app/config", "/app/rulesets", "/app/storage"]

COPY --from=build /app/publish ./

USER $APP_UID
ENTRYPOINT ["dotnet", "g0v0.Server.Stable.dll"]
