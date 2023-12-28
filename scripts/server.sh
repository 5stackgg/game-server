#!/bin/bash

copy_directories=(
  "game/csgo/cfg"
  "game/csgo/addons"
  "game/csgo/gameinfo.gi"
)

for dir in "${copy_directories[@]}"; do
    cp -R "$BASE_SERVER_DIR/$dir" "$INSTANCE_SERVER_DIR/$dir"
done

make_directories=(
  "game/csgo/maps"
  "game/csgo/addons"
  "game/csgo/maps/cfg"
  "game/csgo/maps/soundcache"
  "game/csgo/logs"
  "game/csgo/resource/overviews"
)

for dir in "${make_directories[@]}"; do
    mkdir -p "$INSTANCE_SERVER_DIR/$dir"
done

echo "---Create Symbolic Links---"
IGNORE_DIRECTORIES="game/csgo/addons"

create_symlinks() {
    local source_path="$1"
    local destination_path="$2"

    # Iterate through each file in the source directory
    for file in "$source_path"/*; do

        # Extract the relative path of the file from the source directory
        relative_path="${file#$source_path/}"

        # Build the corresponding destination path
        destination_file="$destination_path/$relative_path"

        # Check if it's a regular file
        if [ -f "$file" ]; then
          # Check if the symlink or file does not exist in the destination
          if [ ! -e "$destination_file" ]; then
              # Create the symlink
              ln -s "$file" "$destination_file"
              echo "Symlink created: $destination_file"
          fi
        elif [ -d "$file" ]; then
            # Check if the destination directory exists
            if [ ! -e "$destination_file" ]; then
                # Create the symlink
                ln -s "$file" "$destination_file"
                echo "Symlink created: $destination_file"
            fi
            # Recursively call the function
            create_symlinks "$file" "$destination_file"
        fi
    done
}

create_symlinks "$BASE_SERVER_DIR" "$INSTANCE_SERVER_DIR"

cp "/opt/server-cfg/server.cfg" "$INSTANCE_SERVER_DIR/game/csgo/cfg"
cp "/opt/server-cfg/subscribed_file_ids.txt" "$INSTANCE_SERVER_DIR/game/csgo"

echo "---Install Metamod---"
cp -R /opt/metamod/addons "${INSTANCE_SERVER_DIR}/game/csgo"

echo "---Check Metamod Install---"
gameinfo_path="${INSTANCE_SERVER_DIR}/game/csgo/gameinfo.gi"
new_line="                        Game    csgo/addons/metamod"

echo "Checking if $new_line exists in $gameinfo_path"
if ! grep -qFx "$new_line" "$gameinfo_path"; then
    echo "---Adding Metamod---"
    # If the line doesn't exist, add it
    line_number=$(awk '/Game_LowViolence/{print NR; exit}' "$gameinfo_path")
    echo "Found Game_LowViolence at line $line_number"

    sed -i "${line_number}a\\$new_line" "$gameinfo_path"
fi

echo "---Install CounterStrikeSharp---"
cp -R /opt/counterstrikesharp/addons "${INSTANCE_SERVER_DIR}/game/csgo"

# Create plugins directory
mkdir -p "${INSTANCE_SERVER_DIR}/game/csgo/addons/counterstrikesharp/plugins"

echo "---Install PlayCS---"
cp -R /opt/mod "${INSTANCE_SERVER_DIR}/game/csgo/addons/counterstrikesharp/plugins/PlayCS"

echo "---Permissions...---"
chown -R ${UID}:${GID} ${DATA_DIR}

echo "---Prepare Server---"
if [ ! -f "${DATA_DIR}/.steam/sdk64/steamclient.so" ]; then
    if [ ! -d "${DATA_DIR}/.steam" ]; then
        mkdir "${DATA_DIR}/.steam"
    fi
    if [ ! -d "${DATA_DIR}/.steam/sdk64" ]; then
        mkdir "${DATA_DIR}/.steam/sdk64"
    fi
    cp -R "${STEAMCMD_DIR}/linux64/"* "${DATA_DIR}/.steam/sdk64/"
fi

chmod -R "${DATA_PERM}" "${DATA_DIR}"


echo "---Starting Server...--"
cd ${INSTANCE_SERVER_DIR}
${INSTANCE_SERVER_DIR}/game/bin/linuxsteamrt64/cs2 ${GAME_PARAMS}

if [ $? -ne 0 ]; then
    echo "Exit code: $?"
    exit 1
fi
