#!/bin/bash

make_directories=(
  "game/csgo/cfg"
  "game/csgo/addons"
  "game/csgo/maps/soundcache"
  "game/csgo/logs"
  "game/bin/linuxsteamrt64"
)

for dir in "${make_directories[@]}"; do
    mkdir -p "$INSTANCE_SERVER_DIR/$dir"
done

cp -R "$BASE_SERVER_DIR/game/csgo/cfg" "$INSTANCE_SERVER_DIR/game/csgo"
rm "$INSTANCE_SERVER_DIR/game/csgo/cfg/server.cfg"

if [ ! -d "$BASE_SERVER_DIR/game/bin/linuxsteamrt64/steamapps" ]; then
    mkdir -p "$BASE_SERVER_DIR/game/bin/linuxsteamrt64/steamapps"
fi

for file in "$BASE_SERVER_DIR/game/bin/linuxsteamrt64"/*; do
    if [ -f "$file" ]; then
        cp "$file" "$INSTANCE_SERVER_DIR/game/bin/linuxsteamrt64/"
    elif [ -d "$file" ]; then
        if [[ "$file" != *"steamapps"* ]]; then
            cp -R "$file" "$INSTANCE_SERVER_DIR/game/bin/linuxsteamrt64/"
        fi
    fi
done

echo "---Install Addons---"
cp -r "/opt/addons" "${INSTANCE_SERVER_DIR}/game/csgo"

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

if [ "$SERVER_TYPE" = "Ranked" ]; then
  cp "/opt/server-cfg/ranked.server.cfg" "$INSTANCE_SERVER_DIR/game/csgo/cfg/server.cfg"
  cp "/opt/server-cfg/5stack.lan.cfg" "$INSTANCE_SERVER_DIR/game/csgo/cfg"
  cp "/opt/server-cfg/5stack.base.cfg" "$INSTANCE_SERVER_DIR/game/csgo/cfg"
  cp "/opt/server-cfg/5stack.warmup.cfg" "$INSTANCE_SERVER_DIR/game/csgo/cfg"
  cp "/opt/server-cfg/5stack.knife.cfg" "$INSTANCE_SERVER_DIR/game/csgo/cfg"
  cp "/opt/server-cfg/5stack.live.cfg" "$INSTANCE_SERVER_DIR/game/csgo/cfg"
  cp "/opt/server-cfg/5stack.duel.cfg" "$INSTANCE_SERVER_DIR/game/csgo/cfg"
  cp "/opt/server-cfg/5stack.wingman.cfg" "$INSTANCE_SERVER_DIR/game/csgo/cfg"
  cp "/opt/server-cfg/5stack.competitive.cfg" "$INSTANCE_SERVER_DIR/game/csgo/cfg"
fi

if [ "$SERVER_TYPE" != "Ranked" ]; then
  if [ ! -d "/opt/custom-data" ]; then
    mkdir -p "/opt/custom-data"
  fi

  if [ ! -d "/opt/custom-data/maps" ]; then
    mkdir -p "/opt/custom-data/maps"
  fi

  if [ ! -e "/opt/custom-data/maps/mg_public.txt" ]; then
    cp "/opt/server-cfg/mg_public.txt" "/opt/custom-data/maps/mg_public.txt"
  fi

  if [ ! -d "/opt/custom-data/cfg" ]; then
    mkdir -p "/opt/custom-data/cfg"
  fi

  if [ ! -e "/opt/custom-data/cfg/server.cfg" ]; then
    cp "/opt/server-cfg/public.server.cfg" "/opt/custom-data/cfg/server.cfg"
  fi

  if [ ! -e "/opt/custom-data/addons/counterstrikesharp/configs/core.json" ]; then
    if [ ! -d "/opt/custom-data/addons/counterstrikesharp/configs" ]; then
      mkdir -p "/opt/custom-data/addons/counterstrikesharp/configs"
    fi
    cp "/opt/custom-data/core.json" "/opt/custom-data/addons/counterstrikesharp/configs/core.json"
  fi
  create_symlinks "/opt/custom-data" "${INSTANCE_SERVER_DIR}/game/csgo"
fi

if $AUTOLOAD_PLUGINS = true ; then
  echo "---Install Custom Plugins---"
  create_symlinks "/opt/custom-plugins" "${INSTANCE_SERVER_DIR}/game/csgo"

  if [ -e "/opt/custom-plugins/addons/counterstrikesharp/gamedata/gamedata.json" ]; then
    echo "---Install Custom Gamedata---"
    rm "${INSTANCE_SERVER_DIR}/game/csgo/addons/counterstrikesharp/gamedata/gamedata.json"  
    cp "/opt/custom-plugins/addons/counterstrikesharp/gamedata/gamedata.json" "${INSTANCE_SERVER_DIR}/game/csgo/addons/counterstrikesharp/gamedata/gamedata.json"
  fi
fi

create_symlinks "$BASE_SERVER_DIR" "$INSTANCE_SERVER_DIR"

if $INSTALL_5STACK_PLUGIN = true ; then
  echo "---Install 5Stack---"
  if [ "${DEV_SWAPPED}" == "1" ]; then
    ln -s "/opt/dev" "${INSTANCE_SERVER_DIR}/game/csgo/addons/counterstrikesharp/plugins/FiveStack"
  else
    ln -s "/opt/mod" "${INSTANCE_SERVER_DIR}/game/csgo/addons/counterstrikesharp/plugins/FiveStack"
  fi
fi

if [ ! -e "$INSTANCE_SERVER_DIR/game/csgo/addons/counterstrikesharp/configs/core.json" ]; then
    cp "/opt/server-cfg/core.json" "$INSTANCE_SERVER_DIR/game/csgo/addons/counterstrikesharp/configs"
fi

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

gameinfo_branchspecific_path="${INSTANCE_SERVER_DIR}/game/csgo/gameinfo_branchspecific.gi"

if [ "$STEAM_RELAY" = "true" ]; then
  rm "$gameinfo_branchspecific_path"

  echo "---Enable Steam Relay---"
echo '"GameInfo"
{
    // this file is intentionally empty for generating depot signatures
    FileSystem
    {
      EmptyFileSystemValue 1
    }

    ConVars
    {
      "net_p2p_listen_dedicated" "1"
    }

    NetworkSystem
    {
      "CreateListenSocketP2P" "2"
    }
}' > "$gameinfo_branchspecific_path"  
fi