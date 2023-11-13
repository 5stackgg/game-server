# Use the official .NET SDK 7.0 image on Alpine Linux
FROM mcr.microsoft.com/dotnet/sdk:7.0-alpine AS build

# Set the working directory
WORKDIR /mod

# Copy the .csproj file to the container
COPY *.csproj .

# Restore the dependencies
RUN dotnet restore

# Copy the remaining files to the container
COPY . .

# Build the application
RUN dotnet build -c Release -o release

RUN rm /mod/release/CounterStrikeSharp.API.dll

FROM debian:bookworm-slim

ENV DATA_DIR="/serverdata"
ENV STEAMCMD_DIR="${DATA_DIR}/steamcmd"
ENV SERVER_DIR="${DATA_DIR}/serverfiles"
ENV GAME_ID="730"
ENV GAME_NAME="counter-strike"
ENV GAME_PARAMS=""
ENV GAME_PORT=27015
ENV VALIDATE=false
ENV UMASK=000
ENV UID=99
ENV GID=100
ENV USERNAME=""
ENV PASSWRD=""
ENV USER="steam"
ENV DATA_PERM=770
ENV METAMOD_DOWNLOAD_LINK=""
ENV COUNTER_STRIKE_SHARP_URL=""

RUN  echo "deb http://deb.debian.org/debian bookworm contrib non-free non-free-firmware" >> /etc/apt/sources.list && \
	apt-get update && apt-get -y upgrade && \
	apt-get -y install --no-install-recommends wget locales procps && \
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
	mkdir $SERVER_DIR && \
	useradd -d $DATA_DIR -s /bin/bash $USER && \
	chown -R $USER $DATA_DIR && \
	ulimit -n 2048

RUN mkdir /opt/metamod
ADD https://mms.alliedmods.net/mmsdrop/2.0/mmsource-2.0.0-git1258-linux.tar.gz /tmp/metamod.tar.gz
RUN tar -xz -C /opt/metamod -f /tmp/metamod.tar.gz && rm /tmp/metamod.tar.gz

RUN mkdir /opt/counterstrikesharp
ADD https://github.com/roflmuffin/CounterStrikeSharp/releases/download/v50/counterstrikesharp-with-runtime-build-50-linux-414710d.zip /tmp/counterstrikesharp.zip
RUN unzip /tmp/counterstrikesharp.zip -d /opt/counterstrikesharp && rm /tmp/counterstrikesharp.zip

COPY /cfg /opt/server-cfg
COPY /scripts /opt/scripts
COPY --from=build /mod/release /opt/mod

RUN chmod -R 770 /opt/scripts/

#Server Start
ENTRYPOINT ["/opt/scripts/start.sh"]