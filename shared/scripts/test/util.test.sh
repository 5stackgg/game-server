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

  mkdir -p "$workdir/shared/addons/swiftlys2/configs/plugins/Existing"
  echo "SHARED-ORIGINAL" > "$workdir/shared/addons/swiftlys2/configs/plugins/Existing/config.jsonc"

  mkdir -p "$workdir/store/inventory-simulator/3.1.0/addons/swiftlys2/plugins/InventorySimulator"
  echo "INVSIM" > "$workdir/store/inventory-simulator/3.1.0/addons/swiftlys2/plugins/InventorySimulator/InventorySimulator.dll"

  mkdir -p "$workdir/store/retakes/1.2.0/addons/swiftlys2/plugins/Retakes"
  echo "RETAKES" > "$workdir/store/retakes/1.2.0/addons/swiftlys2/plugins/Retakes/Retakes.dll"

  mkdir -p "$workdir/instance/game/csgo/addons"
  create_symlinks "$workdir/shared" "$workdir/instance/game/csgo"
}

teardown() {
  if [ -n "$workdir" ]; then
    rm -rf "$workdir"
  fi
}

echo "install_store_plugins links every installed entry"
setup
ENABLED_PLUGINS="inventory-simulator@3.1.0,retakes@1.2.0" \
  install_store_plugins "$workdir/store" "$workdir/instance/game/csgo" > /dev/null 2>&1
assert_equals "$(cat "$workdir/instance/game/csgo/addons/swiftlys2/plugins/InventorySimulator/InventorySimulator.dll")" \
  "INVSIM" "InventorySimulator.dll not reachable"
assert_equals "$(cat "$workdir/instance/game/csgo/addons/swiftlys2/plugins/Retakes/Retakes.dll")" \
  "RETAKES" "Retakes.dll not reachable"
teardown

echo "install_store_plugins never writes into the node-wide volume or the store"
setup
ENABLED_PLUGINS="inventory-simulator@3.1.0,retakes@1.2.0" \
  install_store_plugins "$workdir/store" "$workdir/instance/game/csgo" > /dev/null 2>&1
assert_missing "$workdir/shared/addons/swiftlys2/plugins" \
  "wrote through a directory symlink into the shared custom-plugins volume"
assert_missing "$workdir/store/inventory-simulator/3.1.0/addons/swiftlys2/plugins/Retakes" \
  "one plugin's tree leaked into another plugin's store directory"
teardown

echo "install_store_plugins rejects traversal and malformed entries"
setup
output="$(ENABLED_PLUGINS="../evil@1,malformed,ghost@9.9.9" \
  install_store_plugins "$workdir/store" "$workdir/instance/game/csgo" 2>&1)"
case "$output" in
  *"refusing unsafe entry ../evil@1"*) ;;
  *) fail "traversal entry was not refused" ;;
esac
case "$output" in
  *"malformed entry malformed"*) ;;
  *) fail "malformed entry was not reported" ;;
esac
case "$output" in
  *"ghost@9.9.9 is not installed"*) ;;
  *) fail "missing plugin was not reported" ;;
esac
teardown

echo "write_plugin_configs keeps per-match config inside the instance"
setup
PLUGIN_CONFIGS="$(printf '%s' '{"addons/swiftlys2/configs/plugins/Existing/config.jsonc":"PER-MATCH"}' | base64)" \
  write_plugin_configs "$workdir/instance/game/csgo" > /dev/null 2>&1
assert_equals "$(cat "$workdir/instance/game/csgo/addons/swiftlys2/configs/plugins/Existing/config.jsonc")" \
  "PER-MATCH" "instance did not receive the override"
assert_equals "$(cat "$workdir/shared/addons/swiftlys2/configs/plugins/Existing/config.jsonc")" \
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
