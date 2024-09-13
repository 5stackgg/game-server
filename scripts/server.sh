#!/bin/bash

make_directories=(
  "game/csgo/cfg"
  "game/csgo/addons"
  "game/csgo/maps/soundcache"
  "game/csgo/logs"
)

for dir in "${make_directories[@]}"; do
    mkdir -p "$INSTANCE_SERVER_DIR/$dir"
done

copy_directories=(
  "game/bin"
  "game/csgo/cfg"
)

for dir in "${copy_directories[@]}"; do
    cp -R "$BASE_SERVER_DIR/$dir" "$INSTANCE_SERVER_DIR/$dir"
done

echo "---Create Symbolic Links---"

create_symlinks() {
    local source_path="$1"
    local destination_path="$2"

    for file in "$source_path"/*; do
        relative_path="${file#$source_path/}"
        destination_file="$destination_path/$relative_path"

        if [ -f "$file" ]; then
          if [ ! -e "$destination_file" ]; then
              ln -s "$file" "$destination_file"
          fi
        elif [ -d "$file" ]; then
            if [ ! -e "$destination_file" ]; then
                ln -s "$file" "$destination_file"
            fi
            create_symlinks "$file" "$destination_file"
        fi
    done
}

create_symlinks "$BASE_SERVER_DIR" "$INSTANCE_SERVER_DIR"

cp "/opt/server-cfg/server.cfg" "$INSTANCE_SERVER_DIR/game/csgo/cfg"
cp "/opt/server-cfg/base.cfg" "$INSTANCE_SERVER_DIR/game/csgo/cfg"
cp "/opt/server-cfg/warmup.cfg" "$INSTANCE_SERVER_DIR/game/csgo/cfg"
cp "/opt/server-cfg/knife.cfg" "$INSTANCE_SERVER_DIR/game/csgo/cfg"
cp "/opt/server-cfg/live.cfg" "$INSTANCE_SERVER_DIR/game/csgo/cfg"
cp "/opt/server-cfg/subscribed_file_ids.txt" "$INSTANCE_SERVER_DIR/game/csgo"

echo "---Install Addons---"
cp -r "/opt/addons" "${INSTANCE_SERVER_DIR}/game/csgo"

echo "---Install 5Stack---"
if [ "${DEV_SWAPPED}" == "1" ]; then
  ln -s "/opt/dev" "${INSTANCE_SERVER_DIR}/game/csgo/addons/counterstrikesharp/plugins/FiveStack"
else
  ln -s "/opt/mod" "${INSTANCE_SERVER_DIR}/game/csgo/addons/counterstrikesharp/plugins/FiveStack"
fi

cp "/opt/server-cfg/core.json" "$INSTANCE_SERVER_DIR/game/csgo/addons/counterstrikesharp/configs"

echo "---Check Metamod Install---"
gameinfo_path="${INSTANCE_SERVER_DIR}/game/csgo/gameinfo.gi"
new_line="                        Game    csgo/addons/metamod"

if ! grep -qFx "$new_line" "$gameinfo_path"; then
    echo "---Adding Metamod Loader ---"
    # If the line doesn't exist, add it
    line_number=$(awk '/Game_LowViolence/{print NR; exit}' "$gameinfo_path")
    echo "Found Game_LowViolence at line $line_number"

    sed -i "${line_number}a\\$new_line" "$gameinfo_path"
fi

echo "---Prepare Server---"
mkdir -p /root/.steam/sdk64
cp -R "${STEAMCMD_DIR}/linux64/"* "/root/.steam/sdk64/"

echo "---Starting Server...--"
cd ${INSTANCE_SERVER_DIR}
${INSTANCE_SERVER_DIR}/game/bin/linuxsteamrt64/cs2 -ip 0.0.0.0 -port ${SERVER_PORT} +tv_port ${TV_PORT} -dedicated -dev +map de_inferno -usercon +rcon_password ${RCON_PASSWORD} +sv_password ${SERVER_PASSWORD} ${EXTRA_GAME_PARAMS}
