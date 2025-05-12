#!/bin/bash

echo "---Prepare Server---"
mkdir -p /root/.steam/sdk64
cp -R "${STEAMCMD_DIR}/linux64/"* "/root/.steam/sdk64/"

echo "---Starting Server...--"
cd ${INSTANCE_SERVER_DIR}
${INSTANCE_SERVER_DIR}/game/bin/linuxsteamrt64/cs2 -ip 0.0.0.0 -port ${SERVER_PORT} +tv_port ${TV_PORT} -dedicated -dev -usercon +rcon_password ${RCON_PASSWORD} +sv_password ${SERVER_PASSWORD} ${EXTRA_GAME_PARAMS}
