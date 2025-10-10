FROM mcr.microsoft.com/dotnet/sdk:8.0-bookworm-slim AS build

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

RUN apt-get update && apt-get install -y zip

RUN zip -r /mod-release.zip .

FROM debian:bookworm-slim

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

ENV METAMOD_URL=https://mms.alliedmods.net/mmsdrop/2.0/mmsource-2.0.0-git1366-linux.tar.gz
ENV COUNTER_STRIKE_SHARP_URL=https://github.com/roflmuffin/CounterStrikeSharp/releases/download/v1.0.340/counterstrikesharp-with-runtime-linux-1.0.340.zip

RUN  echo "deb http://deb.debian.org/debian bookworm contrib non-free non-free-firmware" >> /etc/apt/sources.list && \
	apt-get update && apt-get -y upgrade && \
	apt-get -y install --no-install-recommends wget locales procps jq && \
	touch /etc/locale.gen && \
	echo "en_US.UTF-8 UTF-8" >> /etc/locale.gen && \
	locale-gen && \
	apt-get -y install --reinstall ca-certificates && \
	rm -rf /var/lib/apt/lists/*

ENV LANG=en_US.UTF-8
ENV LANGUAGE=en_US:en
ENV LC_ALL=en_US.UTF-8

RUN apt-get update && \
	apt-get -y install --no-install-recommends curl unzip lib32gcc-s1 lib32stdc++6 lib32z1 lsof libicu-dev && \
	rm -rf /var/lib/apt/lists/*

RUN mkdir $DATA_DIR && \
	mkdir $STEAMCMD_DIR && \
	mkdir $BASE_SERVER_DIR && \
    mkdir $INSTANCE_SERVER_DIR && \
	useradd -d $DATA_DIR -s /bin/bash $USER && \
	ulimit -n 2048

RUN mkdir /opt/metamod
ADD $METAMOD_URL /tmp/metamod.tar.gz
RUN tar -xz -C /opt/metamod -f /tmp/metamod.tar.gz && rm /tmp/metamod.tar.gz

RUN mkdir /opt/counterstrikesharp
ADD $COUNTER_STRIKE_SHARP_URL /tmp/counterstrikesharp.zip
RUN unzip /tmp/counterstrikesharp.zip -d /opt/counterstrikesharp && rm /tmp/counterstrikesharp.zip

COPY /cfg /opt/server-cfg
COPY /scripts /opt/scripts
COPY --from=build /mod/release /opt/mod

RUN mv /opt/metamod/addons /opt/addons

RUN cp -R /opt/counterstrikesharp/addons/metamod /opt/addons
RUN cp -R /opt/counterstrikesharp/addons/counterstrikesharp /opt/addons
RUN mkdir -p /opt/addons/counterstrikesharp/plugins

RUN rm -rf /opt/metamod
RUN rm -rf /opt/counterstrikesharp

ENTRYPOINT ["/bin/bash", "-c", "/opt/scripts/setup.sh && /opt/scripts/server.sh"]