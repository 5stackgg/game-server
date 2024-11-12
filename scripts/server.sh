#!/bin/bash

echo "---Prepare Server---"
mkdir -p /root/.steam/sdk64
cp -R "${STEAMCMD_DIR}/linux64/"* "/root/.steam/sdk64/"

if [ -n "${WORKSHOP_MAP}" ]; then
    echo "---Workshop Map---"
    SERVER_MAP="host_workshop_map ${WORKSHOP_MAP} +workshop_start_map ${WORKSHOP_MAP}"
else
    SERVER_MAP="map ${DEFAULT_MAP}"
fi

echo "---Starting Server...--"
cd ${INSTANCE_SERVER_DIR}
${INSTANCE_SERVER_DIR}/game/bin/linuxsteamrt64/cs2 -ip 0.0.0.0 -port ${SERVER_PORT} +tv_port ${TV_PORT} -dedicated -dev ${SERVER_MAP} -usercon +rcon_password ${RCON_PASSWORD} +sv_password ${SERVER_PASSWORD} ${EXTRA_GAME_PARAMS}
