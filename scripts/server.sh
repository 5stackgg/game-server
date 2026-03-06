#!/bin/bash

echo "---Prepare Server---"
mkdir -p /root/.steam/sdk64
cp -R "${STEAMCMD_DIR}/linux64/"* "/root/.steam/sdk64/"

echo "---Starting Server...--"
cd ${INSTANCE_SERVER_DIR}

if [ "${GAME_ID}" = "740" ]; then
    # ensure 128 tickrate for 740
    case " ${EXTRA_GAME_PARAMS} " in
        *" -tickrate "*)
            ;; # already set, do nothing
        *)
            EXTRA_GAME_PARAMS="${EXTRA_GAME_PARAMS} -tickrate 128"
            ;;
    esac
    SERVER_BINARY="${INSTANCE_SERVER_DIR}/srcds_linux"
    GAME_ARGS="-game csgo"
else
    SERVER_BINARY="${INSTANCE_SERVER_DIR}/game/bin/linuxsteamrt64/cs2"
    GAME_ARGS=""
fi

"${SERVER_BINARY}" ${GAME_ARGS} -ip 0.0.0.0 -port ${SERVER_PORT} +tv_port ${TV_PORT} -dedicated -dev -usercon +rcon_password ${RCON_PASSWORD} ${EXTRA_GAME_PARAMS}  &

CS_PID=$!

trap 'echo "Received signal to stop the match"; exit 0' SIGUSR1

wait $CS_PID