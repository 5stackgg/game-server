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

GAME_NAME="CS2"
[ "${GAME_ID}" = "740" ] && GAME_NAME="CS:GO"

if [ -n "${BUILD_ID:-}" ] && [ -z "${BUILD_BRANCH:-}" ]; then
    echo "Pinned to build ${BUILD_ID} without its Steam branch. Update the panel, then retry. Exiting."
    exit 1
fi

# Every build a node can pin is one of Steam's branches. Steam keeps the last
# branch it installed, so leaving a pin has to name public.
BRANCH="${BUILD_BRANCH:-public}"

# Pins used to be downloaded beside the install, which left a stub appmanifest,
# a track file and a second copy of the depots. Dropping the stub makes steamcmd
# check the files once and adopt them.
if [ -f "${BASE_SERVER_DIR}/.5stack.build" ]; then
    rm -f "${BASE_SERVER_DIR}/.5stack.build"
    rm -rf "${BASE_SERVER_DIR}/steamapps"
    rm -rf "${STEAMCMD_DIR}/linux32/steamapps"
fi

VALIDATE_ARG=""
[ "${VALIDATE}" = "true" ] && VALIDATE_ARG="validate"

echo "---Update Server To ${BRANCH}${BUILD_ID:+ (build ${BUILD_ID})}---"
UPDATE_LOG="$(mktemp)"
"${STEAMCMD_DIR}/steamcmd.sh" +force_install_dir "${BASE_SERVER_DIR}" +login anonymous +app_update "${GAME_ID}" -beta "${BRANCH}" ${VALIDATE_ARG} +quit 2>&1 | tee "${UPDATE_LOG}"

if grep -q "Success! App '${GAME_ID}'" "${UPDATE_LOG}"; then
    rm -f "${UPDATE_LOG}"
    exit 0
fi

if grep -qE "state is 0x202|[Nn]ot enough disk space" "${UPDATE_LOG}"; then
    rm -f "${UPDATE_LOG}"
    FREE_GB=$(df -Pk "${BASE_SERVER_DIR}" | awk 'NR == 2 { printf "%d", $4 / 1048576 }')
    echo "Not enough disk space to update ${GAME_NAME} (${FREE_GB} GB free). Free up space on the node, then retry."
    exit 1
fi

rm -f "${UPDATE_LOG}"
echo "steamcmd did not finish the ${GAME_NAME} update. Exiting."
exit 1
