#!/bin/bash

# Check if SteamCMD is installed
if [ ! -f "${STEAMCMD_DIR}/steamcmd.sh" ]; then
    echo "SteamCMD not found!"
    curl -sSL -o "${STEAMCMD_DIR}/steamcmd_linux.tar.gz" http://media.steampowered.com/client/steamcmd_linux.tar.gz
    tar --directory "${STEAMCMD_DIR}" -xvzf "${STEAMCMD_DIR}/steamcmd_linux.tar.gz"
    rm "${STEAMCMD_DIR}/steamcmd_linux.tar.gz"

    # Check if installation was successful
    if [ ! -f "${STEAMCMD_DIR}/steamcmd.sh" ]; then
        echo "Failed to install SteamCMD. Exiting."
        exit 1
    fi
fi

# Update SteamCMD
echo "---Update SteamCMD---"
if [ -z "${USERNAME}" ]; then
    "${STEAMCMD_DIR}/steamcmd.sh" +login anonymous +quit
else
    "${STEAMCMD_DIR}/steamcmd.sh" +login "${USERNAME}" "${PASSWRD}" +quit
fi

# Update Server
echo "---Update Server---"
if [ -z "${USERNAME}" ]; then
    echo "---Validating installation---"
    "${STEAMCMD_DIR}/steamcmd.sh" +force_install_dir "${SERVER_DIR}" +login anonymous +app_update "${GAME_ID}" ${VALIDATE:+validate} +quit
else
    echo "---Validating installation---"
    "${STEAMCMD_DIR}/steamcmd.sh" +force_install_dir "${SERVER_DIR}" +login "${USERNAME}" "${PASSWRD}" +app_update "${GAME_ID}" ${VALIDATE:+validate} +quit
fi

# Prepare Server
echo "---Prepare Server---"
if [ ! -f "${DATA_DIR}/.steam/sdk64/steamclient.so" ]; then
    if [ ! -d "${DATA_DIR}/.steam" ]; then
        mkdir "${DATA_DIR}/.steam"
    fi
    if [ ! -d "${DATA_DIR}/.steam/sdk64" ]; then
        mkdir "${DATA_DIR}/.steam/sdk64"
    fi
    cp -R "${STEAMCMD_DIR}/linux64/"* "${DATA_DIR}/.steam/sdk64/"
fi

# Set permissions
chmod -R "${DATA_PERM}" "${DATA_DIR}"
