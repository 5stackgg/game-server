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

# Check build version and remove steamapps if different
BUILD_TRACK_FILE="${BASE_SERVER_DIR}/.5stack.build"
CURRENT_BUILD=""
[ -f "${BUILD_TRACK_FILE}" ] && CURRENT_BUILD=$(cat "${BUILD_TRACK_FILE}" 2>/dev/null)

if [ -n "${BUILD_MANIFESTS}" ] && ([ -z "${BUILD_ID}" ] || [ "${BUILD_ID}" != "${CURRENT_BUILD}" ]); then
    rm -rf "${BASE_SERVER_DIR}/steamapps"
fi

if [ -n "${BUILD_MANIFESTS}" ]; then
    echo "${BUILD_ID}" > "${BUILD_TRACK_FILE}"
else
    rm "${BUILD_TRACK_FILE}"
fi


# Update Server
if [ -n "${BUILD_MANIFESTS}" ]; then
    echo "---Update Linux Server To Specific Version---"
    STEAMCMD_ARGS="+force_install_dir \"${BASE_SERVER_DIR}\" +login \"${STEAM_USER}\" \"${STEAM_PASSWORD}\""

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

    mkdir -p "${BASE_SERVER_DIR}/steamapps"

    cat > "${BASE_SERVER_DIR}/steamapps/appmanifest_730.acf" << EOF
"AppState"
{
        "buildid"               "${BUILD_ID}"
}
EOF

else
    echo "---Update Server To Latest Version---"
    "${STEAMCMD_DIR}/steamcmd.sh" +force_install_dir "${BASE_SERVER_DIR}" +login anonymous +app_update "${GAME_ID}" ${VALIDATE:+validate} +quit
fi