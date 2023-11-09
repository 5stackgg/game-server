#!/bin/bash
if [ ! -f ${STEAMCMD_DIR}/steamcmd.sh ]; then
    echo "SteamCMD not found!"
    wget -q -O ${STEAMCMD_DIR}/steamcmd_linux.tar.gz http://media.steampowered.com/client/steamcmd_linux.tar.gz
    tar --directory ${STEAMCMD_DIR} -xvzf /serverdata/steamcmd/steamcmd_linux.tar.gz
    rm ${STEAMCMD_DIR}/steamcmd_linux.tar.gz
fi

echo "---Update SteamCMD---"
if [ "${USERNAME}" == "" ]; then
    ${STEAMCMD_DIR}/steamcmd.sh \
    +login anonymous \
    +quit
else
    ${STEAMCMD_DIR}/steamcmd.sh \
    +login ${USERNAME} ${PASSWRD} \
    +quit
fi

echo "---Update Server---"
if [ "${USERNAME}" == "" ]; then
    if [ "${VALIDATE}" == "true" ]; then
    	echo "---Validating installation---"
        ${STEAMCMD_DIR}/steamcmd.sh \
        +force_install_dir ${SERVER_DIR} \
        +login anonymous \
        +app_update ${GAME_ID} validate \
        +quit
    else
        ${STEAMCMD_DIR}/steamcmd.sh \
        +force_install_dir ${SERVER_DIR} \
        +login anonymous \
        +app_update ${GAME_ID} \
        +quit
    fi
else
    if [ "${VALIDATE}" == "true" ]; then
    	echo "---Validating installation---"
        ${STEAMCMD_DIR}/steamcmd.sh \
        +force_install_dir ${SERVER_DIR} \
        +login ${USERNAME} ${PASSWRD} \
        +app_update ${GAME_ID} validate \
        +quit
    else
        ${STEAMCMD_DIR}/steamcmd.sh \
        +force_install_dir ${SERVER_DIR} \
        +login ${USERNAME} ${PASSWRD} \
        +app_update ${GAME_ID} \
        +quit
    fi
fi

echo "---Prepare Server---"
if [ ! -f ${DATA_DIR}/.steam/sdk64/steamclient.so ]; then
	if [ ! -d ${DATA_DIR}/.steam ]; then
    	mkdir ${DATA_DIR}/.steam
    fi
	if [ ! -d ${DATA_DIR}/.steam/sdk64 ]; then
    	mkdir ${DATA_DIR}/.steam/sdk64
    fi
    cp -R ${STEAMCMD_DIR}/linux64/* ${DATA_DIR}/.steam/sdk64/
fi
chmod -R ${DATA_PERM} ${DATA_DIR}

if [ ! -d ${SERVER_DIR}/game/csgo/addons ]; then
  mkdir ${SERVER_DIR}/game/csgo/addons
fi

if [ ! -d ${SERVER_DIR}/game/csgo/addons/metamod ]; then
  echo "---Install Metamod---"
	cp -R /opt/metamod/addons "${SERVER_DIR}/game/csgo"
fi

echo "---Check Metamod Install---"

gameinfo_path="${SERVER_DIR}/game/csgo/gameinfo.gi"
new_line="                        Game    csgo/addons/metamod"

# Check if the line already exists
if ! grep -qFx "$new_line" "$gameinfo_path"; then
    # If the line doesn't exist, add it
    line_number=$(awk '/Game_LowViolence/{print NR; exit}' "$gameinfo_path")
    sed -i "${line_number}a\\$new_line" "$gameinfo_path"
fi

if [ ! -d ${SERVER_DIR}/game/csgo/addons/metamod/counterstrikesharp ]; then
  echo "---Install CounterStrikeSharp---"
  cp -R /opt/counterstrikesharp/addons "${SERVER_DIR}/game/csgo"
fi

if [ ! -d ${SERVER_DIR}/game/csgo/addons/counterstrikesharp/plugins ]; then
  mkdir ${SERVER_DIR}/game/csgo/addons/counterstrikesharp/plugins
fi

if [ ! -d ${SERVER_DIR}/game/csgo/addons/counterstrikesharp/plugins/PlayCS ]; then
  echo "---Install PlayCS---"
  cp -R /opt/mod "${SERVER_DIR}/game/csgo/addons/counterstrikesharp/plugins/PlayCS"
fi

echo "---Server ready---"
cd ${SERVER_DIR}
${SERVER_DIR}/game/bin/linuxsteamrt64/cs2 ${GAME_PARAMS}