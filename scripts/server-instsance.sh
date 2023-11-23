#!/bin/bash

export INSTANCE_SERVER_DIR="/opt/instance-${SERVER_ID}"

check_success() {
    if [ $? -ne 0 ]; then
        echo "Error: $1 failed with exit code $?"
        exit 1
    fi
}

# Create instance directory
mkdir -p "$INSTANCE_SERVER_DIR"
check_success "Creating instance directory"

# Copy directories
copy_directories=(
  "game/csgo/cfg"
  "game/csgo/sound"
  "game/csgo/models"
  "game/csgo/addons"
)

for dir in "${copy_directories[@]}"; do
    cp -R "$BASE_SERVER_DIR/$dir" "$INSTANCE_SERVER_DIR/$dir"
    check_success "Copying $dir"
done

# Make directories
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
    check_success "Creating directory $dir"
done

# Install Metamod
echo "---Install Metamod---"
cp -R /opt/metamod/addons "${INSTANCE_SERVER_DIR}/game/csgo"
check_success "Installing Metamod"

# Check Metamod Install
echo "---Check Metamod Install---"
gameinfo_path="${INSTANCE_SERVER_DIR}/game/csgo/gameinfo.gi"
new_line="                        Game    csgo/addons/metamod"

# Check if the line already exists
if ! grep -qFx "$new_line" "$gameinfo_path"; then
    echo "---Adding Metamod---"
    # If the line doesn't exist, add it
    line_number=$(awk '/Game_LowViolence/{print NR; exit}' "$gameinfo_path")
    sed -i "${line_number}a\\$new_line" "$gameinfo_path"
fi

# Install CounterStrikeSharp
echo "---Install CounterStrikeSharp---"
cp -R /opt/counterstrikesharp/addons "${INSTANCE_SERVER_DIR}/game/csgo"
check_success "Installing CounterStrikeSharp"

# Create plugins directory
mkdir -p "${INSTANCE_SERVER_DIR}/game/csgo/addons/counterstrikesharp/plugins"

# Install PlayCS
echo "---Install PlayCS---"
cp -R /opt/mod "${INSTANCE_SERVER_DIR}/game/csgo/addons/counterstrikesharp/plugins/PlayCS"
check_success "Installing PlayCS"

# Symbolic links
echo "---Create Symbolic Links---"
IGNORE_DIRECTORIES="game/csgo/addons"

for file in "$BASE_SERVER_DIR"/*; do
  filename=$(basename "$file")
  [[ $IGNORE_DIRECTORIES =~ " $INSTANCE_SERVER_DIR/$filename " ]] && continue
  [[ -L "$file" || -e "$file" ]] && continue
  ln -s "$file" "$INSTANCE_SERVER_DIR"
  check_success "Creating symbolic link for $file"
done

# Permissions
echo "---Permissions...---"
su "$USER" -c "/opt/scripts/permissions.sh"
check_success "Setting permissions"

# Start Server
echo "---Starting Server: ${SERVER_ID}...--"
cd "$INSTANCE_SERVER_DIR"
"${INSTANCE_SERVER_DIR}/game/bin/linuxsteamrt64/cs2" "${GAME_PARAMS[@]}"

if [ $? -ne 0 ]; then
    echo "Exit code: $?"
    exit 1
fi
