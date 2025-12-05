FROM registry.gitlab.steamos.cloud/steamrt/sniper/platform:latest-container-runtime-depot AS build

# Install .NET SDK 8.0
RUN apt-get update && \
    apt-get install -y --no-install-recommends \
    wget \
    ca-certificates \
    && wget https://dot.net/v1/dotnet-install.sh -O dotnet-install.sh \
    && chmod +x dotnet-install.sh \
    && ./dotnet-install.sh --channel 8.0 --install-dir /usr/share/dotnet \
    && rm -f dotnet-install.sh \
    && rm -rf /var/lib/apt/lists/*

ENV PATH="/usr/share/dotnet:${PATH}"
ENV DOTNET_ROOT="/usr/share/dotnet"

WORKDIR /mod

COPY src/*.csproj .

RUN dotnet restore

COPY . .

ARG RELEASE_VERSION
ENV RELEASE_VERSION ${RELEASE_VERSION}

RUN sed -i 's/__RELEASE_VERSION__/'${RELEASE_VERSION}'/' src/FiveStackPlugin.cs

RUN dotnet build -c Release -o release

RUN rm /mod/release/CounterStrikeSharp.API.dll

COPY src/lang /mod/release/lang

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

ENV LD_LIBRARY_PATH="/opt/instance/game/bin/linuxsteamrt64:"

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

ENV COUNTER_STRIKE_SHARP_URL=https://github.com/roflmuffin/CounterStrikeSharp/releases/download/v1.0.347/counterstrikesharp-with-runtime-linux-1.0.347.zip

RUN apt-get update && \
	apt-get install -y --no-install-recommends \
	wget locales procps jq ca-certificates curl unzip \
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

RUN mkdir -p /opt/counterstrikesharp && \
	wget -q $COUNTER_STRIKE_SHARP_URL -O /tmp/counterstrikesharp.zip && \
	unzip -q /tmp/counterstrikesharp.zip -d /opt/counterstrikesharp && \
	rm /tmp/counterstrikesharp.zip

COPY /cfg /opt/server-cfg
COPY /scripts /opt/scripts
COPY --from=build /mod/release /opt/mod

RUN mkdir -p /opt/addons && \
	cp -R /opt/counterstrikesharp/addons/counterstrikesharp /opt/addons && \
	mkdir -p /opt/addons/counterstrikesharp/plugins && \
	rm -rf /opt/counterstrikesharp

ENTRYPOINT ["/bin/bash", "-c", "/opt/scripts/setup.sh && /opt/scripts/server.sh"]