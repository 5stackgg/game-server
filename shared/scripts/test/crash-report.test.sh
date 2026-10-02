#!/bin/bash
# Tests for the crash reports server.sh saves when the game server dies.
# Run: bash shared/scripts/test/crash-report.test.sh

set -u

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
source "${SCRIPT_DIR}/../crash-report.sh"

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

assert_contains() {
  case "$1" in
    *"$2"*) ;;
    *) fail "$3 (missing '$2')" ;;
  esac
}

assert_missing() {
  if [ -e "$1" ]; then
    fail "$2"
  fi
}

setup() {
  workdir="$(mktemp -d)"

  CRASH_REPORTS_DIR="$workdir/reports"
  MINIDUMP_DIR="$workdir/dumps"
  CONSOLE_TAIL_LINES=2000
  INSTANCE_SERVER_DIR="$workdir/instance"

  mkdir -p "$MINIDUMP_DIR" "$INSTANCE_SERVER_DIR/game/csgo"
  printf 'ClientVersion=2000921\r\nPatchVersion=1.41.0.6\r\nProductName=cs2\r\n' \
    > "$INSTANCE_SERVER_DIR/game/csgo/steam.inf"

  export SERVER_ID="5df7aa6c-13ef-410f-9230-95e791aef48e"
  export MATCH_ID="0b2d1c6e-7a51-4c1f-9d0e-3f6a8b2c4d5e"
  export SERVER_TYPE="Custom"
  export GAME_ID="730"
  export RELEASE_VERSION="v0.0.87"
  export ENABLED_PLUGINS="deathmatch@1.1.2,map-chooser@1.3.2"
  export EXTRA_GAME_PARAMS="-maxplayers 16 +map de_cache +game_type 0 +sv_password hunter2 +game_mode 1"
  export MINIDUMP_DIR
}

teardown() {
  rm -rf "$workdir"
}

# Stands in for cs2: prints a console, then dies the way the crash handler
# leaves things, mid-line with a minidump behind it.
fake_server() {
  local script="$workdir/cs2"

  cat > "$script" << 'EOF'
#!/bin/bash
echo "Host activate: Loading (de_cache)"
for line in $(seq 1 "$LINES_BEFORE_CHANGE"); do
  echo "console line $line"
done
echo "Host activate: Changelevel (de_ancient_night)"
echo "console after changelevel"
if [ -n "${WRITE_DUMP:-}" ]; then
  echo "minidump" > "$MINIDUMP_DIR/d5e1c3a2-0000-0000-0000-000000000000.dmp"
fi
printf '[AI BT]: Loaded behavior tree %s' "'addons/swiftlys2/plugins/Deathmatch/resources/configs/bt"
exit "${EXIT_STATUS}"
EOF
  chmod +x "$script"
  echo "$script"
}

only_report() {
  local reports=("$CRASH_REPORTS_DIR/$SERVER_ID"/*/)
  echo "${reports[0]%/}"
}

echo "run_server saves a report keyed by the server that crashed"
setup
server="$(fake_server)"
output="$(LINES_BEFORE_CHANGE=3 EXIT_STATUS=1 WRITE_DUMP=1 run_server "$server" -dedicated)"
status=$?
assert_equals "$status" "1" "the server's exit status was not passed through"
report="$(only_report)"
crash="$(cat "$report/crash.txt" 2> /dev/null)"
assert_contains "$crash" "server_id=$SERVER_ID" "report does not name the server"
assert_contains "$crash" "match_id=$MATCH_ID" "report does not name the match"
assert_contains "$crash" "server_type=Custom" "report does not record the server type"
assert_contains "$crash" "exit_status=1" "report does not record the exit status"
assert_contains "$crash" "map=de_ancient_night" "report does not record the map the server was on"
assert_contains "$crash" "image_version=v0.0.87" "report does not record the image"
assert_contains "$crash" "game_version=1.41.0.6" "report does not record the game build"
assert_contains "$crash" "enabled_plugins=deathmatch@1.1.2,map-chooser@1.3.2" "report does not record the plugins"
assert_contains "$crash" "minidumps=1" "report does not count the minidump"
assert_contains "$crash" "+sv_password <redacted>" "the server password was not redacted"
case "$crash" in
  *hunter2*) fail "the server password reached the report" ;;
esac
assert_equals "$(cat "$report/d5e1c3a2-0000-0000-0000-000000000000.dmp" 2> /dev/null)" "minidump" \
  "the minidump was not moved into the report"
assert_missing "$MINIDUMP_DIR/d5e1c3a2-0000-0000-0000-000000000000.dmp" "the minidump was left in the container"
assert_equals "$(tail -n 1 "$report/console.log" 2> /dev/null)" \
  "[AI BT]: Loaded behavior tree 'addons/swiftlys2/plugins/Deathmatch/resources/configs/bt" \
  "the console's unterminated last line was lost"
assert_contains "$output" "console line 3" "the console did not reach the container log"
assert_contains "$output" "crash report saved to $report" "the container log does not say where the report is"
teardown

echo "run_server keeps only the last console lines"
setup
CONSOLE_TAIL_LINES=2
server="$(fake_server)"
LINES_BEFORE_CHANGE=50 EXIT_STATUS=1 run_server "$server" > /dev/null
report="$(only_report)"
assert_equals "$(wc -l < "$report/console.log" | tr -d ' ')" "2" "the console tail was not bounded"
assert_equals "$(head -n 1 "$report/console.log")" "console after changelevel" \
  "the console tail did not keep the newest lines"
assert_contains "$(cat "$report/crash.txt")" "map=de_ancient_night" \
  "the map was lost once its line left the console tail"
teardown

echo "run_server leaves no report when the server exits cleanly"
setup
server="$(fake_server)"
LINES_BEFORE_CHANGE=1 EXIT_STATUS=0 run_server "$server" > /dev/null
status=$?
assert_equals "$status" "0" "a clean exit was not passed through"
assert_missing "$CRASH_REPORTS_DIR" "a clean exit saved a crash report"
teardown

echo "run_server reports a clean exit that left a minidump"
setup
server="$(fake_server)"
LINES_BEFORE_CHANGE=1 EXIT_STATUS=0 WRITE_DUMP=1 run_server "$server" > /dev/null
assert_contains "$(cat "$(only_report)/crash.txt" 2> /dev/null)" "minidumps=1" \
  "a minidump without an exit status was not reported"
teardown

echo "run_server files a report without a server id under unknown"
setup
SERVER_ID=""
server="$(fake_server)"
LINES_BEFORE_CHANGE=1 EXIT_STATUS=1 run_server "$server" > /dev/null
reports=("$CRASH_REPORTS_DIR/unknown"/*/)
assert_equals "${#reports[@]}" "1" "a report without a server id was not filed under unknown"
teardown

echo "prune_crash_reports keeps the newest reports of one server only"
setup
CRASH_REPORTS_KEPT=2
for stamp in 20261001T100000Z 20261002T090000Z 20261002T132251Z; do
  mkdir -p "$CRASH_REPORTS_DIR/$SERVER_ID/$stamp" "$CRASH_REPORTS_DIR/other-server/$stamp"
done
prune_crash_reports "$CRASH_REPORTS_DIR/$SERVER_ID"
assert_missing "$CRASH_REPORTS_DIR/$SERVER_ID/20261001T100000Z" "the oldest report was kept"
assert_equals "$(ls "$CRASH_REPORTS_DIR/$SERVER_ID" | tr '\n' ' ')" "20261002T090000Z 20261002T132251Z " \
  "the newest reports were not kept"
assert_equals "$(ls "$CRASH_REPORTS_DIR/other-server" | wc -l | tr -d ' ')" "3" \
  "another server's reports were pruned"
CRASH_REPORTS_KEPT=10
teardown

if [ "$failures" -gt 0 ]; then
  echo "$failures failure(s)"
  exit 1
fi

echo "all crash report tests passed"
