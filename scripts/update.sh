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

echo "BUILD_ID: ${BUILD_ID}"
echo "CURRENT_BUILD: ${CURRENT_BUILD}"
echo "BUILD_MANIFESTS: ${BUILD_MANIFESTS}"

if [ -n "${BUILD_MANIFESTS:-}" ]; then
    echo "${BUILD_ID}" > "${BUILD_TRACK_FILE}"

    if [ -z "${BUILD_ID:-}" ] || [ "${BUILD_ID}" != "${CURRENT_BUILD}" ]; then
        rm -rf "${BASE_SERVER_DIR}/game"
        rm -rf "${STEAMCMD_DIR}/linux32/steamapps"
    fi
else
    if [ -f "${BUILD_TRACK_FILE}" ]; then
        rm -f "${BUILD_TRACK_FILE}"
        rm -rf "${STEAMCMD_DIR}/linux32/steamapps"
    fi
fi

rm -rf "${BASE_SERVER_DIR}/steamapps"

# Update Server
if [ -n "${BUILD_MANIFESTS}" ]; then
    echo "---Pinning Server To Version ${BUILD_ID}---"
    STEAMCMD_ARGS="+force_install_dir \"${BASE_SERVER_DIR}\" +login \"${STEAM_USER}\" \"${STEAM_PASSWORD}\""

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
    depotTotal="${#depots[@]}"
    depotIndex=0
    for depotId in "${depots[@]}"; do
        depotIndex=$((depotIndex + 1))
        gid="${depot_gids[${depotId}]}"
        depotDir="${STEAMCMD_DIR}/linux32/steamapps/content/app_${GAME_ID}/depot_${depotId}"
        depotMarker="${depotDir}.complete"

        if [ -f "${depotMarker}" ] && [ "$(cat "${depotMarker}" 2>/dev/null)" = "${gid}" ] && [ -d "${depotDir}" ] && [ "$(ls -A "${depotDir}")" ]; then
            echo "---Depot ${depotId} (${depotIndex}/${depotTotal}) already downloaded (manifest ${gid}), skipping---"
            continue
        fi
        rm -f "${depotMarker}"

        echo "---Downloading Depot ${depotId} (${depotIndex}/${depotTotal}) manifest ${gid}---"
        LINUX_SERVER="${STEAMCMD_ARGS} +download_depot ${GAME_ID} ${depotId} ${gid} +quit"
        echo "steamcmd.sh +force_install_dir \"${BASE_SERVER_DIR}\" +login \"${STEAM_USER}\" [redacted] +download_depot ${GAME_ID} ${depotId} ${gid} +quit"

        # steamcmd prints nothing while download_depot runs, so report
        # progress from the depot dir size until it finishes
        (
            while true; do
                sleep 15
                size=$(du -sh "${depotDir}" 2>/dev/null | cut -f1)
                fileCount=$(find "${depotDir}" -type f 2>/dev/null | wc -l | tr -d ' ')
                echo "[depot ${depotId}] ${size:-0} downloaded so far (${fileCount} files)..."
            done
        ) &
        progressPid=$!

        depotLog="${STEAMCMD_DIR}/depot_${depotId}_download.log"
        eval "${STEAMCMD_DIR}/steamcmd.sh" ${LINUX_SERVER} 2>&1 | tee "${depotLog}"

        kill "${progressPid}" 2>/dev/null
        wait "${progressPid}" 2>/dev/null

        if ! grep -q "Depot download complete" "${depotLog}"; then
            echo "Depot ${depotId} download did not complete. Exiting."
            rm -f "${depotLog}"
            exit 1
        fi
        rm -f "${depotLog}"
        echo "${gid}" > "${depotMarker}"
        echo "---Depot ${depotId} (${depotIndex}/${depotTotal}) complete: $(du -sh "${depotDir}" 2>/dev/null | cut -f1)---"
    done

    echo "---Syncing Depots to ServerFiles---"

    # sync downloaded depots to server files
    for depotId in "${depots[@]}"; do
        depotDir="${STEAMCMD_DIR}/linux32/steamapps/content/app_${GAME_ID}/depot_${depotId}"
        if [ -d "${depotDir}" ] && [ "$(ls -A "${depotDir}")" ]; then
            echo "---Syncing Depot ${depotId} ($(du -sh "${depotDir}" 2>/dev/null | cut -f1)) to ServerFiles---"

            # no --delete: depots merge into the same dir, and it would remove
            # the other depots' files plus the .5stack.build track file
            syncStart=$(date +%s)
            rsync -a --info=stats1 \
                "${depotDir}/" \
                "${BASE_SERVER_DIR}/"
            echo "---Synced Depot ${depotId} in $(( $(date +%s) - syncStart ))s---"

        else
            echo "Depot directory ${depotDir} does not exist or is empty. Exiting."
            exit 1
        fi
    done

    mkdir -p "${BASE_SERVER_DIR}/steamapps"

    cat > "${BASE_SERVER_DIR}/steamapps/appmanifest_730.acf" << EOF
"AppState"
{
        "buildid"               "${BUILD_ID}"
}
EOF

    echo "${BUILD_ID}" > "${BUILD_TRACK_FILE}"

    echo "---Done Updating Server To Version ${BUILD_ID}---"

else
    echo "---Update Server To Latest Version---"
    "${STEAMCMD_DIR}/steamcmd.sh" +force_install_dir "${BASE_SERVER_DIR}" +login anonymous +app_update "${GAME_ID}" ${VALIDATE:+validate} +quit
fi