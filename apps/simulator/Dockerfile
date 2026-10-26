FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0 AS build

WORKDIR /src

COPY Directory.Build.props ./
COPY apps/simulator/src/FiveStack.Simulator.csproj apps/simulator/src/

RUN dotnet restore apps/simulator/src/FiveStack.Simulator.csproj

COPY shared/dotnet/FiveStack.Entities shared/dotnet/FiveStack.Entities
COPY shared/dotnet/FiveStack.Enums shared/dotnet/FiveStack.Enums
COPY apps/simulator/src apps/simulator/src

RUN dotnet publish apps/simulator/src/FiveStack.Simulator.csproj -c Release --no-restore -o /app

# Debian rather than Alpine: the api's node jobs run bash in whatever image
# hosts its game servers.
FROM mcr.microsoft.com/dotnet/runtime:10.0

WORKDIR /app

COPY --from=build /app .
COPY apps/simulator/scripts /opt/scripts

ARG RELEASE_VERSION
ENV RELEASE_VERSION=${RELEASE_VERSION}
ENV GAME_SERVER_IMAGE="game-server-sim"

ENTRYPOINT ["dotnet", "/app/simulator.dll"]
