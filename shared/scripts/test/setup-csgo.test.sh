#!/bin/bash
# Tests for the addons setup-csgo.sh puts in a CS:GO server.
# Run: bash shared/scripts/test/setup-csgo.test.sh

set -u

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_DIR="$(cd "${SCRIPT_DIR}/../../.." && pwd)"

failures=0

fail() {
  echo "  FAIL: $1" >&2
  failures=$((failures + 1))
}

workdir="$(mktemp -d)"
trap 'rm -rf "$workdir"' EXIT

# The script reads its addons from fixed paths under /opt; the copy reads them
# from the sandbox instead.
sed "s#/opt/#${workdir}/opt/#g" "$REPO_DIR/shared/scripts/setup-csgo.sh" > "$workdir/setup-csgo.sh"
cp "$REPO_DIR/shared/scripts/util.sh" "$workdir/util.sh"

mkdir -p \
  "$workdir/base/csgo/cfg" \
  "$workdir/base/bin/linuxsteamrt64" \
  "$workdir/instance" \
  "$workdir/opt/csgo-sourcemod/cfg/sourcemod" \
  "$workdir/opt/csgo-sourcemod/addons/sourcemod/configs" \
  "$workdir/opt/csgo-metamod/addons/metamod/bin/linux64" \
  "$workdir/opt/csgo-no-lobby-reservation/addons/sourcemod/plugins" \
  "$workdir/opt/server-cfg"

echo "appID=730" > "$workdir/base/csgo/steam.inf"
touch \
  "$workdir/opt/csgo-metamod/addons/metamod.vdf" \
  "$workdir/opt/csgo-metamod/addons/metamod_x64.vdf" \
  "$workdir/opt/csgo-metamod/addons/metamod/bin/server.so" \
  "$workdir/opt/csgo-metamod/addons/metamod/bin/linux64/server.so" \
  "$workdir/opt/server-cfg/mg_public.txt" \
  "$workdir/opt/server-cfg/public.server.cfg"

env -i PATH="$PATH" \
  BASE_SERVER_DIR="$workdir/base" \
  INSTANCE_SERVER_DIR="$workdir/instance" \
  bash "$workdir/setup-csgo.sh" > /dev/null 2>&1

addons="$workdir/instance/csgo/addons"

echo "setup-csgo.sh loads Metamod through its 32-bit loader"
if [ ! -e "$addons/metamod.vdf" ]; then
  fail "metamod.vdf is missing from the CS:GO server"
fi

# srcds_linux is 32-bit, so this loader can only fail, and its "wrong ELF
# class" line in every boot log reads like the reason a server is broken.
echo "setup-csgo.sh leaves out Metamod's 64-bit loader"
if [ -e "$addons/metamod_x64.vdf" ]; then
  fail "metamod_x64.vdf is in the CS:GO server's addons"
fi

if [ "$failures" -gt 0 ]; then
  echo "$failures failure(s)" >&2
  exit 1
fi

echo "all passed"
