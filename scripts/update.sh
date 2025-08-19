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
    echo "---Update Linux Server To Specific Version---"

    IFS=',' read -ra DEPOT_ID_ARRAY <<< "${DEPOT_IDS}"
    
    for depotId in "${DEPOT_ID_ARRAY[@]}"; do
        echo "---Updating Depot ${depotId}---"
        LINUX_SERVER="${STEAMCMD_ARGS} +download_depot ${GAME_ID} ${depotId} ${BUILD_ID} +quit"
        eval "${STEAMCMD_DIR}/steamcmd.sh" ${LINUX_SERVER}
    done
else
    echo "---Update Server To Latest Version---"

    STEAMCMD_ARGS="${STEAMCMD_ARGS} +app_update \"${GAME_ID}\""
    [ -n "${VALIDATE}" ] && STEAMCMD_ARGS="${STEAMCMD_ARGS} validate"
    STEAMCMD_ARGS="${STEAMCMD_ARGS} +quit"

    echo "${STEAMCMD_DIR}/steamcmd.sh" ${STEAMCMD_ARGS}
    eval "${STEAMCMD_DIR}/steamcmd.sh" ${STEAMCMD_ARGS}
fi