FROM registry.gitlab.steamos.cloud/steamrt/sniper/platform:latest-container-runtime-depot AS build

# Install .NET SDK 10.0
RUN apt-get update && \
    apt-get install -y --no-install-recommends \
    wget \
    ca-certificates \
    && wget https://dot.net/v1/dotnet-install.sh -O dotnet-install.sh \
    && chmod +x dotnet-install.sh \
    && ./dotnet-install.sh --channel 10.0 --install-dir /usr/share/dotnet \
    && rm -f dotnet-install.sh \
    && rm -rf /var/lib/apt/lists/*

ENV PATH="/usr/share/dotnet:${PATH}"
ENV DOTNET_ROOT="/usr/share/dotnet"

WORKDIR /mod

COPY Directory.Build.props ./
COPY apps/counterstrikesharp/src/FiveStack.csproj apps/counterstrikesharp/src/

RUN dotnet restore apps/counterstrikesharp/src/FiveStack.csproj

COPY shared shared
COPY apps/counterstrikesharp apps/counterstrikesharp

ARG RELEASE_VERSION
ENV RELEASE_VERSION=${RELEASE_VERSION}

RUN sed -i "s/__RELEASE_VERSION__/${RELEASE_VERSION}/" apps/counterstrikesharp/src/FiveStackPlugin.cs

RUN dotnet build -c Release apps/counterstrikesharp/src/FiveStack.csproj -o release

RUN rm /mod/release/CounterStrikeSharp.API.dll

COPY apps/counterstrikesharp/src/lang /mod/release/lang

# New stage for creating the zip file
FROM debian:bookworm-slim AS zip-creator

WORKDIR /zip-content

COPY --from=build /mod/release ./addons/counterstrikesharp/plugins/FiveStack/./

RUN apt-get update && \
    apt-get install -y --no-install-recommends zip && \
    zip -r /mod-release.zip . && \
    rm -rf /var/lib/apt/lists/*

FROM registry.gitlab.steamos.cloud/steamrt/sniper/platform:latest-container-runtime-depot

ENV DATA_DIR="/serverdata"
ENV STEAMCMD_DIR="${DATA_DIR}/steamcmd"
ENV BASE_SERVER_DIR="${DATA_DIR}/serverfiles"
ENV INSTANCE_SERVER_DIR="/opt/instance"

ENV LD_LIBRARY_PATH="/opt/instance/game/bin/linuxsteamrt64:/opt/instance/bin:"

ENV AUTOLOAD_PLUGINS=true
ENV PLUGINS_DIR="/opt/custom-plugins"

ENV INSTALL_5STACK_PLUGIN=true

ENV GAME_ID="730"
ENV GAME_PARAMS=""
ENV GAME_PORT=27015
ENV VALIDATE=false
ENV USER=steam

ENV STEAM_USER="anonymous"
ENV STEAM_PASSWORD=""

ENV SERVER_ID=""
ENV DEFAULT_MAP="de_inferno"

ENV STEAM_RELAY="false"

ENV SERVER_TYPE="Ranked"

ENV METAMOD_URL=https://github.com/alliedmodders/metamod-source/releases/download/2.0.0.1402/mmsource-2.0.0-git1402-linux.tar.gz
ENV COUNTER_STRIKE_SHARP_URL=https://github.com/roflmuffin/CounterStrikeSharp/releases/download/v1.0.369/counterstrikesharp-with-runtime-linux-1.0.369.zip
ENV CS2FOW_URL=https://github.com/karola3vax/CS2FOW/releases/download/v0.1.1-preview/cs2fow-0.1.1-preview-linux-x86_64.zip
ENV CS2FOW_SHA256=517eff95d5bae15b0aa9fb79c47a6eebe3bb7033228bfe9d7d397fc9446750d6
# prebake pack v0.1.0-preview is upstream-declared compatible with plugin v0.1.1-preview; bump both pins together after checking upstream release notes
ENV CS2FOW_PREBAKE_URL=https://github.com/karola3vax/CS2FOW/releases/download/v0.1.0-preview/cs2fow-0.1.0-preview-official-maps.zip
ENV CS2FOW_PREBAKE_SHA256=66114d74a13f933f38f81a79d2fa10ad89e2cf254b45afc5c589c8782067215c
ENV CS2FOW_PREBAKE_VERSION=0.1.0-preview

ENV METAMOD_CSGO_URL=https://mms.alliedmods.net/mmsdrop/1.12/mmsource-1.12.0-git1219-linux.tar.gz
ENV SOURCEMOD_CSGO_URL=https://sm.alliedmods.net/smdrop/1.13/sourcemod-1.13.0-git7297-linux.tar.gz
ENV NO_LOBBY_RESERVATION_URL=https://github.com/nuxencs/NoLobbyReservation/releases/download/v0.0.1/NoLobbyReservation.zip

RUN apt-get update && \
	apt-get install -y --no-install-recommends \
	wget locales procps jq ca-certificates curl unzip rsync \
	lib32gcc-s1 lib32stdc++6 lib32z1 lsof libicu-dev && \
	echo "en_US.UTF-8 UTF-8" > /etc/locale.gen && \
	locale-gen && \
	rm -rf /var/lib/apt/lists/*

ENV LANG=en_US.UTF-8
ENV LANGUAGE=en_US:en
ENV LC_ALL=en_US.UTF-8

RUN mkdir -p $DATA_DIR $STEAMCMD_DIR $BASE_SERVER_DIR $INSTANCE_SERVER_DIR && \
	useradd -d $DATA_DIR -s /bin/bash $USER && \
	ulimit -n 2048

RUN mkdir -p /opt/metamod /opt/counterstrikesharp && \
	wget -q $METAMOD_URL -O /tmp/metamod.tar.gz && \
	tar -xz -C /opt/metamod -f /tmp/metamod.tar.gz && \
	rm /tmp/metamod.tar.gz && \
	wget -q $COUNTER_STRIKE_SHARP_URL -O /tmp/counterstrikesharp.zip && \
	unzip -q /tmp/counterstrikesharp.zip -d /opt/counterstrikesharp && \
	rm /tmp/counterstrikesharp.zip

RUN mkdir -p /opt/csgo-metamod /opt/csgo-sourcemod /opt/csgo-no-lobby-reservation && \
	wget -q $METAMOD_CSGO_URL -O /tmp/metamod-csgo.tar.gz && \
	tar -xz -C /opt/csgo-metamod -f /tmp/metamod-csgo.tar.gz && \
	rm /tmp/metamod-csgo.tar.gz && \
	wget -q $SOURCEMOD_CSGO_URL -O /tmp/sourcemod-csgo.tar.gz && \
	tar -xz -C /opt/csgo-sourcemod -f /tmp/sourcemod-csgo.tar.gz && \
	rm /tmp/sourcemod-csgo.tar.gz && \
	wget -q $NO_LOBBY_RESERVATION_URL -O /tmp/no-lobby-reservation.zip && \
	unzip -q /tmp/no-lobby-reservation.zip -d /opt/csgo-no-lobby-reservation && \
	rm /tmp/no-lobby-reservation.zip

COPY shared/cfg /opt/server-cfg
COPY apps/counterstrikesharp/cfg /opt/server-cfg
COPY shared/scripts /opt/scripts
COPY apps/counterstrikesharp/scripts /opt/scripts
COPY --from=build /mod/release /opt/mod

RUN mv /opt/metamod/addons /opt/addons && \
	cp -R /opt/counterstrikesharp/addons/metamod /opt/addons && \
	cp -R /opt/counterstrikesharp/addons/counterstrikesharp /opt/addons && \
	mkdir -p /opt/addons/counterstrikesharp/plugins && \
	rm -rf /opt/metamod /opt/counterstrikesharp

RUN mkdir -p /opt/cs2fow /opt/cs2fow-tools && \
	wget -q $CS2FOW_URL -O /tmp/cs2fow.zip && \
	echo "${CS2FOW_SHA256}  /tmp/cs2fow.zip" | sha256sum -c - && \
	unzip -q /tmp/cs2fow.zip -d /opt/cs2fow && \
	rm /tmp/cs2fow.zip && \
	cp -R /opt/cs2fow/addons/cs2fow /opt/addons && \
	cp /opt/cs2fow/addons/metamod/cs2fow.vdf /opt/addons/metamod/ && \
	cp -R /opt/cs2fow/tools/. /opt/cs2fow-tools/ && \
	rm -rf /opt/cs2fow

ENTRYPOINT ["/bin/bash", "-c", "/opt/scripts/setup.sh && /opt/scripts/server.sh"]