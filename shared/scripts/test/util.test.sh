#!/bin/bash
# Regression tests for the plugin-store helpers in util.sh.
# Run: bash shared/scripts/test/util.test.sh

set -u

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
source "${SCRIPT_DIR}/../util.sh"

failures=0
workdir=""

fail() {
  echo "  FAIL: $1" >&2
  failures=$((failures + 1))
}

assert_equals() {
  if [ "$1" != "$2" ]; then
    fail "$3 (expected '$2', got '$1')"
  fi
}

assert_missing() {
  if [ -e "$1" ]; then
    fail "$2"
  fi
}

setup() {
  workdir="$(mktemp -d)"

  # Stands in for /opt/5stack/custom-plugins: managed installs and hand-placed
  # files share this one directory now, which is what the index exists to tell
  # apart.
  mkdir -p "$workdir/plugins/addons/swiftlys2/configs/plugins/Existing"
  echo "SHARED-ORIGINAL" > "$workdir/plugins/addons/swiftlys2/configs/plugins/Existing/config.jsonc"

  mkdir -p "$workdir/plugins/addons/swiftlys2/plugins/InventorySimulator"
  echo "INVSIM" > "$workdir/plugins/addons/swiftlys2/plugins/InventorySimulator/InventorySimulator.dll"

  mkdir -p "$workdir/plugins/addons/swiftlys2/plugins/Retakes"
  echo "RETAKES" > "$workdir/plugins/addons/swiftlys2/plugins/Retakes/Retakes.dll"

  # Absent from the index: dropped in by hand, so it always loads.
  mkdir -p "$workdir/plugins/addons/swiftlys2/plugins/HandPlaced"
  echo "HAND" > "$workdir/plugins/addons/swiftlys2/plugins/HandPlaced/HandPlaced.dll"

  mkdir -p "$workdir/plugins/.5stack-plugins"
  printf 'inventory-simulator\t3.1.0\taddons/swiftlys2/plugins/InventorySimulator/InventorySimulator.dll\n' \
    > "$workdir/plugins/.5stack-plugins/index"
  printf 'retakes\t1.2.0\taddons/swiftlys2/plugins/Retakes/Retakes.dll\n' \
    >> "$workdir/plugins/.5stack-plugins/index"

  # The instance starts with a directory symlink pointing back at the shared
  # volume, which is the shape that used to let a later pass write through it
  # and mutate node-wide state. Only this one directory is pre-linked -- linking
  # the whole tree up front would mask whether link_plugins gates anything.
  mkdir -p "$workdir/instance/game/csgo/addons/swiftlys2"
  ln -s "$workdir/plugins/addons/swiftlys2/configs" \
    "$workdir/instance/game/csgo/addons/swiftlys2/configs"
}

teardown() {
  if [ -n "$workdir" ]; then
    rm -rf "$workdir"
  fi
}

echo "link_plugins links a managed plugin the mode selected"
setup
ENABLED_PLUGINS="inventory-simulator@3.1.0" \
  link_plugins "$workdir/plugins" "$workdir/instance/game/csgo" > /dev/null 2>&1
assert_equals "$(cat "$workdir/instance/game/csgo/addons/swiftlys2/plugins/InventorySimulator/InventorySimulator.dll")" \
  "INVSIM" "selected plugin was not linked"
teardown

# The whole reason the index exists: a ranked match must not inherit whatever
# happens to be installed on the node.
echo "link_plugins leaves out a managed plugin the mode did not select"
setup
ENABLED_PLUGINS="inventory-simulator@3.1.0" \
  link_plugins "$workdir/plugins" "$workdir/instance/game/csgo" > /dev/null 2>&1
assert_missing "$workdir/instance/game/csgo/addons/swiftlys2/plugins/Retakes/Retakes.dll" \
  "unselected plugin was linked anyway"
# An empty plugin directory is still a directory the runtime scans and warns
# about, so a fully gated plugin should leave nothing behind at all.
assert_missing "$workdir/instance/game/csgo/addons/swiftlys2/plugins/Retakes" \
  "gated plugin left an empty directory behind"
teardown

echo "link_plugins links nothing managed when no mode is selected"
setup
ENABLED_PLUGINS="" \
  link_plugins "$workdir/plugins" "$workdir/instance/game/csgo" > /dev/null 2>&1
assert_missing "$workdir/instance/game/csgo/addons/swiftlys2/plugins/InventorySimulator/InventorySimulator.dll" \
  "managed plugin linked with no mode selected"
assert_missing "$workdir/instance/game/csgo/addons/swiftlys2/plugins/Retakes/Retakes.dll" \
  "managed plugin linked with no mode selected"
teardown

# Anything absent from the index predates managed installs or was placed by
# hand. It has always loaded and must keep loading.
echo "link_plugins always links a hand-placed plugin"
setup
ENABLED_PLUGINS="" \
  link_plugins "$workdir/plugins" "$workdir/instance/game/csgo" > /dev/null 2>&1
assert_equals "$(cat "$workdir/instance/game/csgo/addons/swiftlys2/plugins/HandPlaced/HandPlaced.dll")" \
  "HAND" "hand-placed plugin was not linked"
teardown

echo "link_plugins keeps its own bookkeeping out of the game directory"
setup
ENABLED_PLUGINS="inventory-simulator@3.1.0" \
  link_plugins "$workdir/plugins" "$workdir/instance/game/csgo" > /dev/null 2>&1
assert_missing "$workdir/instance/game/csgo/.5stack-plugins" \
  "plugin index was linked into the game directory"
teardown

echo "link_plugins never writes back through a directory symlink"
setup
ENABLED_PLUGINS="inventory-simulator@3.1.0" \
  link_plugins "$workdir/plugins" "$workdir/instance/game/csgo" > /dev/null 2>&1
assert_equals "$(cat "$workdir/plugins/addons/swiftlys2/configs/plugins/Existing/config.jsonc")" \
  "SHARED-ORIGINAL" "wrote through a directory symlink into the shared volume"
teardown

# A plugin writes its own config on first load, inside the running server. That
# has to land on the node volume or it is lost when the pod goes away and is
# regenerated from scratch every match. Before managed installs this worked
# because whole directories were symlinked out; it must keep working.
echo "link_plugins lets a plugin's runtime config reach the node volume"
setup
ENABLED_PLUGINS="inventory-simulator@3.1.0" \
  link_plugins "$workdir/plugins" "$workdir/instance/game/csgo" > /dev/null 2>&1
echo "WRITTEN-AT-RUNTIME" \
  > "$workdir/instance/game/csgo/addons/swiftlys2/configs/plugins/Existing/generated.jsonc"
assert_equals "$(cat "$workdir/plugins/addons/swiftlys2/configs/plugins/Existing/generated.jsonc" 2>/dev/null)" \
  "WRITTEN-AT-RUNTIME" "runtime config did not reach the node volume"
teardown

echo "link_plugins lets a plugin create a new config directory on the node"
setup
ENABLED_PLUGINS="inventory-simulator@3.1.0" \
  link_plugins "$workdir/plugins" "$workdir/instance/game/csgo" > /dev/null 2>&1
mkdir -p "$workdir/instance/game/csgo/addons/swiftlys2/configs/plugins/Fresh"
echo "NEW-PLUGIN-CONFIG" \
  > "$workdir/instance/game/csgo/addons/swiftlys2/configs/plugins/Fresh/config.jsonc"
assert_equals "$(cat "$workdir/plugins/addons/swiftlys2/configs/plugins/Fresh/config.jsonc" 2>/dev/null)" \
  "NEW-PLUGIN-CONFIG" "a new plugin config directory did not reach the node volume"
teardown

echo "write_plugin_configs keeps per-match config inside the instance"
setup
PLUGIN_CONFIGS="$(printf '%s' '{"addons/swiftlys2/configs/plugins/Existing/config.jsonc":"PER-MATCH"}' | base64)" \
  write_plugin_configs "$workdir/instance/game/csgo" > /dev/null 2>&1
assert_equals "$(cat "$workdir/instance/game/csgo/addons/swiftlys2/configs/plugins/Existing/config.jsonc")" \
  "PER-MATCH" "instance did not receive the override"
assert_equals "$(cat "$workdir/plugins/addons/swiftlys2/configs/plugins/Existing/config.jsonc")" \
  "SHARED-ORIGINAL" "per-match config leaked onto the shared node volume"
teardown

echo "write_plugin_configs refuses absolute and traversal paths"
setup
output="$(PLUGIN_CONFIGS="$(printf '%s' '{"/etc/passwd":"x","../escape":"y"}' | base64)" \
  write_plugin_configs "$workdir/instance/game/csgo" 2>&1)"
case "$output" in
  *"refusing unsafe path /etc/passwd"*) ;;
  *) fail "absolute path was not refused" ;;
esac
case "$output" in
  *"refusing unsafe path ../escape"*) ;;
  *) fail "traversal path was not refused" ;;
esac
assert_missing "$workdir/escape" "traversal path escaped the instance"
teardown

echo "write_plugin_configs tolerates malformed input"
setup
PLUGIN_CONFIGS="not-base64!!" write_plugin_configs "$workdir/instance/game/csgo" > /dev/null 2>&1
assert_equals "$?" "0" "invalid base64 should not abort setup"
PLUGIN_CONFIGS="$(printf '%s' 'not json' | base64)" \
  write_plugin_configs "$workdir/instance/game/csgo" > /dev/null 2>&1
assert_equals "$?" "0" "invalid JSON should not abort setup"
teardown

if [ "$failures" -gt 0 ]; then
  echo "$failures assertion(s) failed" >&2
  exit 1
fi

echo "all util.sh tests passed"
