#!/bin/bash

if [ "${GAME_ID}" = "740" ]; then
  SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
  bash "${SCRIPT_DIR}/setup-csgo.sh"
  exit $?
fi

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
source "${SCRIPT_DIR}/util.sh"

echo "---Setup Non Synlinkable Files and Directories---"

make_directories=(
  "game/csgo/cfg"
  "game/csgo/addons"
  "game/csgo/maps/soundcache"
  "game/csgo/logs"
  "game/bin/linuxsteamrt64"
)

create_directories "$INSTANCE_SERVER_DIR" "${make_directories[@]}"

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

if [ "$SERVER_TYPE" = "Ranked" ]; then
  cp "/opt/server-cfg/ranked.server.cfg" "$INSTANCE_SERVER_DIR/game/csgo/cfg/server.cfg"
  cp "/opt/server-cfg/5stack.base.cfg" "$INSTANCE_SERVER_DIR/game/csgo/cfg"
  cp "/opt/server-cfg/5stack.competitive.cfg" "$INSTANCE_SERVER_DIR/game/csgo/cfg"
  cp "/opt/server-cfg/5stack.duel.cfg" "$INSTANCE_SERVER_DIR/game/csgo/cfg"
  cp "/opt/server-cfg/5stack.knife.cfg" "$INSTANCE_SERVER_DIR/game/csgo/cfg"
  cp "/opt/server-cfg/5stack.lan.cfg" "$INSTANCE_SERVER_DIR/game/csgo/cfg"
  cp "/opt/server-cfg/5stack.live.cfg" "$INSTANCE_SERVER_DIR/game/csgo/cfg"
  cp "/opt/server-cfg/5stack.warmup.cfg" "$INSTANCE_SERVER_DIR/game/csgo/cfg"
  cp "/opt/server-cfg/5stack.wingman.cfg" "$INSTANCE_SERVER_DIR/game/csgo/cfg"
  cp "/opt/server-cfg/valve-rulebook.cfg" "$INSTANCE_SERVER_DIR/game/csgo/cfg"
fi

if [ ! -d "/opt/custom-plugins/addons/swiftlys2/configs" ]; then
  mkdir -p "/opt/custom-plugins/addons/swiftlys2/configs"
fi

if [ -d "${INSTANCE_SERVER_DIR}/game/csgo/addons/swiftlys2/configs" ]; then
  if [ "$(ls -A "${INSTANCE_SERVER_DIR}/game/csgo/addons/swiftlys2/configs")" ]; then
    cp -r "${INSTANCE_SERVER_DIR}/game/csgo/addons/swiftlys2/configs/." "/opt/custom-plugins/addons/swiftlys2/configs"
  fi
  rm -rf "${INSTANCE_SERVER_DIR}/game/csgo/addons/swiftlys2/configs"
fi

ln -s "/opt/custom-plugins/addons/swiftlys2/configs" "${INSTANCE_SERVER_DIR}/game/csgo/addons/swiftlys2/configs"

if [ "$SERVER_TYPE" != "Ranked" ]; then
  if [ ! -d "/opt/custom-plugins" ]; then
    mkdir -p "/opt/custom-plugins"
  fi

  if [ ! -d "/opt/custom-plugins/maps" ]; then
    mkdir -p "/opt/custom-plugins/maps"
  fi

  if [ ! -e "/opt/custom-plugins/maps/mg_public.txt" ]; then
    cp "/opt/server-cfg/mg_public.txt" "/opt/custom-plugins/maps/mg_public.txt"
  fi

  if [ ! -d "/opt/custom-plugins/cfg" ]; then
    mkdir -p "/opt/custom-plugins/cfg"
  fi

  if [ ! -e "/opt/custom-plugins/cfg/server.cfg" ]; then
    cp "/opt/server-cfg/public.server.cfg" "/opt/custom-plugins/cfg/server.cfg"
  fi

  if [ ! -e "/opt/custom-plugins/addons/swiftlys2/configs/core.jsonc" ]; then
    cp "/opt/server-cfg/core.jsonc" "/opt/custom-plugins/addons/swiftlys2/configs/core.jsonc"
  fi

  create_symlinks "/opt/custom-plugins" "${INSTANCE_SERVER_DIR}/game/csgo"
fi

if $AUTOLOAD_PLUGINS = true ; then
  echo "---Install Custom Plugins---"
  create_symlinks "/opt/custom-plugins" "${INSTANCE_SERVER_DIR}/game/csgo"

  if [ -e "/opt/custom-plugins/addons/swiftlys2/gamedata/cs2/core/offsets.jsonc" ]; then
    echo "---Install Custom Gamedata---"
    rm "${INSTANCE_SERVER_DIR}/game/csgo/addons/swiftlys2/gamedata/cs2/core/offsets.jsonc"  
    cp "/opt/custom-plugins/addons/swiftlys2/gamedata/cs2/core/offsets.jsonc" "${INSTANCE_SERVER_DIR}/game/csgo/addons/swiftlys2/gamedata/cs2/core/offsets.jsonc"
  fi

  if [ -e "/opt/custom-plugins/addons/swiftlys2/gamedata/cs2/core/patches.jsonc" ]; then
    echo "---Install Custom Gamedata---"
    rm "${INSTANCE_SERVER_DIR}/game/csgo/addons/swiftlys2/gamedata/cs2/core/patches.jsonc"  
    cp "/opt/custom-plugins/addons/swiftlys2/gamedata/cs2/core/patches.jsonc" "${INSTANCE_SERVER_DIR}/game/csgo/addons/swiftlys2/gamedata/cs2/core/patches.jsonc"
  fi

  if [ -e "/opt/custom-plugins/addons/swiftlys2/gamedata/cs2/core/signatures.jsonc" ]; then
    echo "---Install Custom Gamedata---"
    rm "${INSTANCE_SERVER_DIR}/game/csgo/addons/swiftlys2/gamedata/cs2/core/signatures.jsonc"  
    cp "/opt/custom-plugins/addons/swiftlys2/gamedata/cs2/core/signatures.jsonc" "${INSTANCE_SERVER_DIR}/game/csgo/addons/swiftlys2/gamedata/cs2/core/signatures.jsonc"
  fi
fi

create_symlinks "$BASE_SERVER_DIR" "$INSTANCE_SERVER_DIR"

if $INSTALL_5STACK_PLUGIN = true ; then
  echo "---Install 5Stack---"
  FIVESTACK_PLUGIN_DIR="${INSTANCE_SERVER_DIR}/game/csgo/addons/swiftlys2/plugins/FiveStack"
  if [ "${DEV_SWAPPED}" == "1" ]; then
    # AutoHotReload's recursive FileSystemWatcher does not follow symlinked plugin
    # dirs, so hot reload needs the dev volume mounted straight onto this path
    # (see the dev deployment). Without the mount, fall back to a symlink.
    mkdir -p "$FIVESTACK_PLUGIN_DIR"
    if mountpoint -q "$FIVESTACK_PLUGIN_DIR"; then
      echo "---5Stack dev: plugin dir is a mounted volume (hot reload enabled)---"
    else
      # css and sw dev builds share the dev volume; each uses its own subfolder
      rmdir "$FIVESTACK_PLUGIN_DIR" 2>/dev/null
      mkdir -p "/opt/dev/sw"
      ln -s "/opt/dev/sw" "$FIVESTACK_PLUGIN_DIR"
      echo "---5Stack dev: symlinked /opt/dev/sw (hot reload OFF; use 'sw plugins reload FiveStack')---"
    fi
  else
    ln -s "/opt/mod" "$FIVESTACK_PLUGIN_DIR"
  fi
fi

if [ ! -e "$INSTANCE_SERVER_DIR/game/csgo/addons/swiftlys2/configs/core.jsonc" ]; then
    cp "/opt/server-cfg/core.jsonc" "$INSTANCE_SERVER_DIR/game/csgo/addons/swiftlys2/configs"
fi

if [ "$SHOW_ELO_RANKS" = "true" ]; then
    echo "---Disabling CS2 Server Guidelines (required for Elo Ranks to render)---"
    core_json="$INSTANCE_SERVER_DIR/game/csgo/addons/swiftlys2/configs/core.jsonc"
    sed -i --follow-symlinks 's/"FollowCS2ServerGuidelines"[[:space:]]*:[[:space:]]*true/"FollowCS2ServerGuidelines": false/' "$core_json"
fi

echo "---Check swiftlys2 Install---"
gameinfo_path="${INSTANCE_SERVER_DIR}/game/csgo/gameinfo.gi"
new_line="                        Game    csgo/addons/swiftlys2"

if ! grep -qFx "$new_line" "$gameinfo_path"; then
    echo "---Adding swiftlys2 Loader ---"
    line_number=$(awk '/Game_LowViolence/{print NR; exit}' "$gameinfo_path")
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