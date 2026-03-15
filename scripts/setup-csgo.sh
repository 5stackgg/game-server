#!/bin/bash

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
source "${SCRIPT_DIR}/util.sh"

echo "---Setup CS:GO (740) - Non Synlinkable Files and Directories---"

make_directories=(
  "csgo/cfg"
  "csgo/addons"
  "csgo/maps/soundcache"
  "csgo/logs"
  "bin/linuxsteamrt64"
)

create_directories "$INSTANCE_SERVER_DIR" "${make_directories[@]}"

cp -R "$BASE_SERVER_DIR/csgo/cfg" "$INSTANCE_SERVER_DIR/csgo"
rm -f "$INSTANCE_SERVER_DIR/csgo/cfg/server.cfg"

if [ ! -d "$BASE_SERVER_DIR/bin/linuxsteamrt64/steamapps" ]; then
    mkdir -p "$BASE_SERVER_DIR/bin/linuxsteamrt64/steamapps"
fi

for file in "$BASE_SERVER_DIR/bin/linuxsteamrt64"/*; do
    if [ -f "$file" ]; then
        cp "$file" "$INSTANCE_SERVER_DIR/bin/linuxsteamrt64/"
    elif [ -d "$file" ]; then
        if [[ "$file" != *"steamapps"* ]]; then
            cp -R "$file" "$INSTANCE_SERVER_DIR/bin/linuxsteamrt64/"
        fi
    fi
done

cp -R "/opt/csgo-sourcemod/cfg" "${INSTANCE_SERVER_DIR}/csgo"
cp -R "/opt/csgo-sourcemod/addons" "${INSTANCE_SERVER_DIR}/csgo"
cp -R "/opt/csgo-metamod/addons" "${INSTANCE_SERVER_DIR}/csgo"
cp -R "/opt/csgo-no-lobby-reservation/addons" "${INSTANCE_SERVER_DIR}/csgo"

echo "---Create Symbolic Links---"
create_symlinks "$BASE_SERVER_DIR" "$INSTANCE_SERVER_DIR"

# Update steam.inf with correct app ID (4465480)
STEAM_INF="${INSTANCE_SERVER_DIR}/csgo/steam.inf"
sed -i 's/appID=740/appID=4465480/' "$STEAM_INF"