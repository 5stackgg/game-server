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

FROM ich777/debian-baseimage:bullseye_amd64 as runtime

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
RUN curl -L https://mms.alliedmods.net/mmsdrop/2.0/mmsource-2.0.0-git1256-linux.tar.gz | tar -xz -C "/opt/metamod"

RUN mkdir /opt/counterstrikesharp
ADD https://github.com/roflmuffin/CounterStrikeSharp/releases/download/v30/counterstrikesharp-with-runtime-build-30-fe23680.zip /tmp/counterstrikesharp.zip
RUN unzip /tmp/counterstrikesharp.zip -d /opt/counterstrikesharp && rm /tmp/counterstrikesharp.zip

COPY /cfg /opt/server-cfg
COPY /scripts /opt/scripts
COPY --from=build /mod/release /opt/mod

RUN chmod -R 770 /opt/scripts/

#Server Start
ENTRYPOINT ["/opt/scripts/start.sh"]