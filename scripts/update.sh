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
"${STEAMCMD_DIR}/steamcmd.sh" +login anonymous +quit

# Remove old steamapps
rm -rf "${BASE_SERVER_DIR}/steamapps"

# Update Server
STEAMCMD_ARGS="+force_install_dir \"${BASE_SERVER_DIR}\" +login anonymous"
if [ -n "${BUILD_ID}" ]; then
    LINUX_SERVER="${STEAMCMD_ARGS} +download_depot ${GAME_ID} ${DEPO_ID} ${BUILD_ID} +quit"
    LINUX_COMMON_SERVER="${STEAMCMD_ARGS} +download_depot ${GAME_ID} ${COMMON_DEPOT_ID} ${BUILD_ID} +quit"

    echo "---Update Linux Server To Specific Version---"
    eval "${STEAMCMD_DIR}/steamcmd.sh" ${LINUX_SERVER}
    
    echo "---Update Linux Common Server To Latest Version---"
    eval "${STEAMCMD_DIR}/steamcmd.sh" ${LINUX_COMMON_SERVER}

    return
fi

echo "---Update Server To Latest Version---"

STEAMCMD_ARGS="${STEAMCMD_ARGS} +app_update \"${GAME_ID}\""
[ -n "${VALIDATE}" ] && STEAMCMD_ARGS="${STEAMCMD_ARGS} validate"
STEAMCMD_ARGS="${STEAMCMD_ARGS} +quit"

echo "${STEAMCMD_DIR}/steamcmd.sh" ${STEAMCMD_ARGS}
eval "${STEAMCMD_DIR}/steamcmd.sh" ${STEAMCMD_ARGS}