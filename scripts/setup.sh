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
cp "/opt/server-cfg/5stack.base.cfg" "$INSTANCE_SERVER_DIR/game/csgo/cfg"
cp "/opt/server-cfg/5stack.warmup.cfg" "$INSTANCE_SERVER_DIR/game/csgo/cfg"
cp "/opt/server-cfg/5stack.knife.cfg" "$INSTANCE_SERVER_DIR/game/csgo/cfg"
cp "/opt/server-cfg/5stack.live.cfg" "$INSTANCE_SERVER_DIR/game/csgo/cfg"
cp "/opt/server-cfg/subscribed_file_ids.txt" "$INSTANCE_SERVER_DIR/game/csgo"

echo "---Install Addons---"
cp -r "/opt/addons" "${INSTANCE_SERVER_DIR}/game/csgo"


if $AUTOLOAD_PLUGINS = true ; then
  echo "---Install Custom Plugins---"
  for plugin_dir in /opt/custom-plugins/plugins/* ; do
      if [ -d "$plugin_dir" ]; then
          plugin_name=$(basename "$plugin_dir")
          ln -s "$plugin_dir" "${INSTANCE_SERVER_DIR}/game/csgo/addons/counterstrikesharp/plugins/$plugin_name"
      fi
  done

  for config_dir in /opt/custom-plugins/configs/* ; do
      if [ -d "$config_dir" ]; then
          config_name=$(basename "$config_dir") 
          ln -s "$config_dir" "${INSTANCE_SERVER_DIR}/game/csgo/addons/counterstrikesharp/configs/$config_name"
      fi
  done
fi

if $INSTALL_5STACK_PLUGIN = true ; then
  echo "---Install 5Stack---"
  if [ "${DEV_SWAPPED}" == "1" ]; then
    ln -s "/opt/dev" "${INSTANCE_SERVER_DIR}/game/csgo/addons/counterstrikesharp/plugins/FiveStack"
  else
    ln -s "/opt/mod" "${INSTANCE_SERVER_DIR}/game/csgo/addons/counterstrikesharp/plugins/FiveStack"
  fi
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
