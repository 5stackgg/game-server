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

        depotLog="${STEAMCMD_DIR}/depot_${depotId}_download.log"
        rm -f "${depotLog}"

        # steamcmd prints nothing while download_depot runs, so report
        # progress from the depot dir size until it finishes; the total comes
        # from steamcmd's "Downloading depot X (N files, M MB)" line in the log
        (
            totalMB=""
            while true; do
                sleep 15
                if [ -z "${totalMB}" ]; then
                    totalMB=$(sed -nE "s/.*Downloading depot ${depotId} \(([0-9]+) files, ([0-9]+) MB\).*/\2/p" "${depotLog}" 2>/dev/null | head -n1)
                fi
                curMB=$(du -sm "${depotDir}" 2>/dev/null | cut -f1)
                fileCount=$(find "${depotDir}" -type f 2>/dev/null | wc -l | tr -d ' ')
                if [ -n "${totalMB}" ] && [ "${totalMB}" -gt 0 ] 2>/dev/null; then
                    pct=$(( ${curMB:-0} * 100 / totalMB ))
                    [ "${pct}" -gt 100 ] && pct=100
                    echo "[depot ${depotId}] ${curMB:-0} MB / ${totalMB} MB (${pct}%) downloaded (${fileCount} files)..."
                else
                    echo "[depot ${depotId}] ${curMB:-0} MB downloaded so far (${fileCount} files)..."
                fi
            done
        ) &
        progressPid=$!

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
    depotIndex=0
    for depotId in "${depots[@]}"; do
        depotIndex=$((depotIndex + 1))
        depotDir="${STEAMCMD_DIR}/linux32/steamapps/content/app_${GAME_ID}/depot_${depotId}"
        if [ -d "${depotDir}" ] && [ "$(ls -A "${depotDir}")" ]; then
            echo "---Syncing Depot ${depotId} (${depotIndex}/${depotTotal}, $(du -sh "${depotDir}" 2>/dev/null | cut -f1)) to ServerFiles---"

            # no --delete: depots merge into the same dir, and it would remove
            # the other depots' files plus the .5stack.build track file
            # progress2 emits \r-updated lines; convert to one line per 5%
            syncStart=$(date +%s)
            rsync -a --info=progress2,stats1 --no-inc-recursive \
                "${depotDir}/" \
                "${BASE_SERVER_DIR}/" \
                | tr '\r' '\n' \
                | awk -v depot="${depotId}" '
                    $2 ~ /^[0-9]+%$/ {
                        pct = $2; sub(/%/, "", pct)
                        if (pct + 0 >= last + 5 || (pct + 0 == 100 && last != 100)) {
                            last = pct + 0
                            printf "[depot %s sync] %s%% (%s, %s)\n", depot, pct, $1, $3
                            fflush()
                        }
                        next
                    }
                    NF { print; fflush() }
                '
            if [ "${PIPESTATUS[0]}" -ne 0 ]; then
                echo "Failed syncing depot ${depotId} to server files. Exiting."
                exit 1
            fi
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

if [ "${GAME_ID}" = "730" ] && [ -n "${CS2FOW_PREBAKE_URL:-}" ]; then
    CS2FOW_MAPS_DIR="${DATA_DIR}/cs2fow/maps"
    CS2FOW_MARKER="${DATA_DIR}/cs2fow/.prebake-version"

    if [ ! -f "${CS2FOW_MARKER}" ] || [ "$(cat "${CS2FOW_MARKER}" 2>/dev/null)" != "${CS2FOW_PREBAKE_VERSION}" ]; then
        echo "---Downloading CS2FOW Official Map Prebakes (${CS2FOW_PREBAKE_VERSION})---"
        mkdir -p "${CS2FOW_MAPS_DIR}"
        if curl -sSL -o /tmp/cs2fow-prebakes.zip "${CS2FOW_PREBAKE_URL}" \
            && echo "${CS2FOW_PREBAKE_SHA256}  /tmp/cs2fow-prebakes.zip" | sha256sum -c - ; then
            # -j flattens paths so this works regardless of the zip's internal layout
            if unzip -o -j -q /tmp/cs2fow-prebakes.zip "*.bvh8" -d "${CS2FOW_MAPS_DIR}"; then
                echo "${CS2FOW_PREBAKE_VERSION}" > "${CS2FOW_MARKER}"
                echo "---CS2FOW prebakes installed: $(ls "${CS2FOW_MAPS_DIR}" | wc -l) files---"
            else
                echo "---CS2FOW prebake unzip failed; auto-bake will cover maps---"
            fi
        else
            echo "---CS2FOW prebake download failed; auto-bake will cover maps---"
        fi
        rm -f /tmp/cs2fow-prebakes.zip
    fi
fi