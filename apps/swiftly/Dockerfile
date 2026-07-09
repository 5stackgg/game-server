FROM registry.gitlab.steamos.cloud/steamrt/sniper/platform:latest-container-runtime-depot AS build

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
COPY apps/swiftly/src/FiveStack.csproj apps/swiftly/src/

RUN dotnet restore apps/swiftly/src/FiveStack.csproj

COPY shared shared
COPY apps/swiftly apps/swiftly

ARG RELEASE_VERSION
ENV RELEASE_VERSION=${RELEASE_VERSION}

RUN sed -i "s/__RELEASE_VERSION__/${RELEASE_VERSION}/" apps/swiftly/src/FiveStack.cs

RUN dotnet publish -c Release apps/swiftly/src/FiveStack.csproj -o /mod/release

FROM debian:bookworm-slim AS zip-creator

WORKDIR /zip-content

COPY --from=build /mod/release ./addons/swiftlys2/plugins/FiveStack/./

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

ENV SWIFTLYS2_VERSION="v1.4.2-beta.25"
ENV SWIFTLYS2_URL=https://github.com/swiftly-solution/swiftlys2/releases/download/${SWIFTLYS2_VERSION}/swiftlys2-linux-${SWIFTLYS2_VERSION}-with-runtimes.zip

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

RUN wget -q $SWIFTLYS2_URL -O /tmp/swiftlys2.zip && \
	unzip -q /tmp/swiftlys2.zip -d /opt/swiftlys2 && \
	rm /tmp/swiftlys2.zip

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
COPY apps/swiftly/cfg /opt/server-cfg
COPY shared/scripts /opt/scripts
COPY apps/swiftly/scripts /opt/scripts
COPY --from=build /mod/release /opt/mod

RUN cp -R /opt/swiftlys2/swiftlys2-linux-${SWIFTLYS2_VERSION}-with-runtimes/addons /opt/addons && \
    rm -rf /opt/swiftlys2

ENTRYPOINT ["/bin/bash", "-c", "/opt/scripts/setup.sh && /opt/scripts/server.sh"]
