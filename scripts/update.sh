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
"${STEAMCMD_DIR}/steamcmd.sh" +login "${STEAM_USER}" "${STEAM_PASSWORD}" +quit

# Remove old steamapps
rm -rf "${BASE_SERVER_DIR}/steamapps"

# Update Server
STEAMCMD_ARGS="+force_install_dir \"${BASE_SERVER_DIR}\" +login \"${STEAM_USER}\" \"${STEAM_PASSWORD}\""
if [ -n "${BUILD_MANIFESTS}" ]; then
    echo "---Update Linux Server To Specific Version---"

    rm -rf "${BASE_SERVER_DIR}/serverfiles/*"

    declare -A depot_gids
    depots=()

    while IFS= read -r line; do
        if [ -n "$line" ]; then
            gid=$(echo "$line" | jq -r '.gid')
            depotId=$(echo "$line" | jq -r '.depotId')
            depots+=("${depotId}")
            depot_gids["${depotId}"]="${gid}"
        fi
    done < <(echo "${BUILD_MANIFESTS}" | jq -c '.[]')

    # download depots
    for depotId in "${depots[@]}"; do
        gid="${depot_gids[${depotId}]}"
        depotDir="${STEAMCMD_DIR}/linux32/steamapps/content/app_${GAME_ID}/depot_${depotId}"

        if [ -d "${depotDir}" ] && [ "$(ls -A "${depotDir}")" ]; then
            echo "---Depot ${depotId} already downloaded, skipping download---"
        else
            echo "---Updating Depot ${depotId} with Build ${gid}---"
            LINUX_SERVER="${STEAMCMD_ARGS} +download_depot ${GAME_ID} ${depotId} ${gid} +quit"
            echo "${STEAMCMD_DIR}/steamcmd.sh" ${LINUX_SERVER}
            eval "${STEAMCMD_DIR}/steamcmd.sh" ${LINUX_SERVER}
        fi
    done

    # sync downloaded depots to server files
    for depotId in "${depots[@]}"; do
        depotDir="${STEAMCMD_DIR}/linux32/steamapps/content/app_${GAME_ID}/depot_${depotId}"
        if [ -d "${depotDir}" ] && [ "$(ls -A "${depotDir}")" ]; then
            echo "---Syncing Depot ${depotId} to ServerFiles---"
            cp -r "${depotDir}/"* "${BASE_SERVER_DIR}/"
        else
            echo "Depot directory ${depotDir} does not exist or is empty. Exiting."
            exit 1
        fi
    done

    rm -rf "${STEAMCMD_DIR}/linux32/steamapps/*"

    cat > "${BASE_SERVER_DIR}/steamapps/appmanifest_730.acf" << EOF
"AppState"
{
        "buildid"               "${BUILD_ID}"
}
EOF

else
    echo "---Update Server To Latest Version---"

    STEAMCMD_ARGS="${STEAMCMD_ARGS} +app_update \"${GAME_ID}\""
    [ -n "${VALIDATE}" ] && STEAMCMD_ARGS="${STEAMCMD_ARGS} validate"
    STEAMCMD_ARGS="${STEAMCMD_ARGS} +quit"

    echo "${STEAMCMD_DIR}/steamcmd.sh" ${STEAMCMD_ARGS}
    eval "${STEAMCMD_DIR}/steamcmd.sh" ${STEAMCMD_ARGS}
fi