# GangsOfBharat authoritative game server.
#
# NOT BUILT YET: Docker is unusable in the build sandbox and the base images cannot be pulled
# there. Build on a machine with Docker, from the repository root (the shared core is needed):
#
#   docker buildx build --platform linux/arm64 -f server/GangsOfBharat.Server/Dockerfile -t gangsofbharat-server .
#   docker run --rm -p 7777:7777/udp gangsofbharat-server
#
# linux/arm64 targets Oracle Cloud Always Free (Ampere A1). linux/amd64 works the same way.
# The publish output is framework-dependent IL, so the build stage runs on the build machine's
# own architecture and the runtime stage picks the image for the target platform.

FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY shared/GangsOfBharat.Shared/ shared/GangsOfBharat.Shared/
COPY server/GangsOfBharat.Server/ server/GangsOfBharat.Server/
RUN dotnet publish server/GangsOfBharat.Server/GangsOfBharat.Server.csproj \
    -c Release -o /app /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/runtime:8.0 AS runtime
WORKDIR /app
COPY --from=build /app .
# Non-root user shipped with the .NET 8 images.
USER $APP_UID
# Configuration: GOB_<OPTION> environment variables or command-line flags (see --help).
ENV GOB_PORT=7777 \
    GOB_MAX_PLAYERS=1000 \
    DOTNET_gcConcurrent=1
EXPOSE 7777/udp
ENTRYPOINT ["dotnet", "GangsOfBharat.Server.dll"]
