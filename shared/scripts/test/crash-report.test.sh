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
  CONSOLE_PIPE_DIR="$workdir"
  CONSOLE_TAIL_LINES=2000
  INSTANCE_SERVER_DIR="$workdir/instance"

  mkdir -p "$MINIDUMP_DIR" "$INSTANCE_SERVER_DIR/game/csgo"
  printf 'ClientVersion=2000921\r\nPatchVersion=1.41.0.6\r\nProductName=cs2\r\n' \
    > "$INSTANCE_SERVER_DIR/game/csgo/steam.inf"

  export SERVER_ID="5df7aa6c-13ef-410f-9230-95e791aef48e"
  export MATCH_ID="0b2d1c6e-7a51-4c1f-9d0e-3f6a8b2c4d5e"
  export SERVER_TYPE="Custom"
  export GAME_ID="730"
  export GAME_SERVER_IMAGE="game-server-sw"
  export RELEASE_VERSION="0.0.87"
  export ENABLED_PLUGINS="deathmatch@1.1.2,map-chooser@1.3.2"
  export EXTRA_GAME_PARAMS="-maxplayers 16 +map de_cache +game_type 0 +sv_password hunter2 +game_mode 1"
  export MINIDUMP_DIR INSTANCE_SERVER_DIR
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
if [ -n "${UPDATE_GAME:-}" ]; then
  printf 'PatchVersion=1.41.9.0\r\n' > "$INSTANCE_SERVER_DIR/game/csgo/steam.inf"
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
output="$(LINES_BEFORE_CHANGE=3 EXIT_STATUS=1 WRITE_DUMP=1 UPDATE_GAME=1 run_server "$server" -dedicated)"
status=$?
assert_equals "$status" "1" "the server's exit status was not passed through"
report="$(only_report)"
crash="$(cat "$report/crash.txt" 2> /dev/null)"
assert_contains "$crash" "server_id=$SERVER_ID" "report does not name the server"
assert_contains "$crash" "match_id=$MATCH_ID" "report does not name the match"
assert_contains "$crash" "server_type=Custom" "report does not record the server type"
assert_contains "$crash" "exit_status=1" "report does not record the exit status"
assert_contains "$crash" "map=de_ancient_night" "report does not record the map the server was on"
assert_contains "$crash" "image=game-server-sw" "report does not record which image crashed"
assert_contains "$crash" "image_version=0.0.87" "report does not record the image version"
assert_contains "$crash" "game_version=1.41.0.6" "report does not record the game build the server started on"
assert_contains "$crash" "enabled_plugins=deathmatch@1.1.2,map-chooser@1.3.2" "report does not record the plugins"
assert_contains "$crash" "minidumps=1" "report does not count the minidump"
assert_contains "$crash" "console_saved=true" "report does not say the console was saved"
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

# A stdio program, unlike the bash fakes above, holds its output in a buffer
# that dies with it, which is what the engine did.
echo "run_server keeps the console of a server that died without flushing it"
setup
cat > "$workdir/cs2" << 'EOF'
#!/bin/bash
(sleep 1; kill -SEGV $$) &
exec sed -n p < <(printf 'line 1\nlast words before the crash\n'; exec sleep 5 2> /dev/null)
EOF
chmod +x "$workdir/cs2"
run_server "$workdir/cs2" > /dev/null
status=$?
assert_equals "$status" "139" "the crash signal was not passed through"
assert_equals "$(tail -n 1 "$(only_report)/console.log" 2> /dev/null)" "last words before the crash" \
  "the server's unflushed console was lost"
teardown

echo "run_server leaves stdbuf out of CS:GO's 32-bit server"
setup
GAME_ID="740"
cat > "$workdir/cs2" << 'EOF'
#!/bin/bash
echo "preload=${LD_PRELOAD:-none}"
exit 1
EOF
chmod +x "$workdir/cs2"
run_server "$workdir/cs2" > /dev/null
assert_equals "$(cat "$(only_report)/console.log" 2> /dev/null)" "preload=none" \
  "stdbuf was preloaded into a CS:GO server"
teardown

echo "run_server saves the console when the server leaves a child holding it"
setup
cat > "$workdir/cs2" << 'EOF'
#!/bin/bash
sleep 30 &
echo "last words"
exit 1
EOF
chmod +x "$workdir/cs2"
SECONDS=0
run_server "$workdir/cs2" > /dev/null
elapsed=$SECONDS
report="$(only_report)"
assert_equals "$(cat "$report/console.log" 2> /dev/null)" "last words" \
  "the console was lost to a child the server left behind"
assert_contains "$(cat "$report/crash.txt")" "console_saved=true" "the saved console was not recorded"
if [ "$elapsed" -ge 4 ]; then
  fail "run_server waited ${elapsed}s on a child the server left behind"
fi
teardown

echo "run_server records a console it could not save"
setup
cat > "$workdir/cs2" << 'EOF'
#!/bin/bash
setsid sleep 7 &
sleep 0.5
echo "last words"
exit 1
EOF
chmod +x "$workdir/cs2"
run_server "$workdir/cs2" > /dev/null
report="$(only_report)"
assert_contains "$(cat "$report/crash.txt" 2> /dev/null)" "console_saved=false" \
  "a lost console was not recorded"
assert_missing "$report/console.log" "a console was saved that could not have been"
teardown

echo "run_server stops cleanly on SIGUSR1 without a report"
setup
cat > "$workdir/cs2" << 'EOF'
#!/bin/bash
echo "running"
sleep 2
exit 1
EOF
chmod +x "$workdir/cs2"
(run_server "$workdir/cs2" > /dev/null) &
runner=$!
sleep 1
kill -USR1 "$runner"
wait "$runner"
status=$?
assert_equals "$status" "0" "a stop was not a clean exit"
sleep 1.5
assert_missing "$CRASH_REPORTS_DIR" "a stopped server saved a crash report"
teardown

echo "run_server still saves the report of a crash a stop arrives after"
setup
cat > "$workdir/cs2" << 'EOF'
#!/bin/bash
setsid sleep 7 &
sleep 0.5
echo "last words"
exit 1
EOF
chmod +x "$workdir/cs2"
(run_server "$workdir/cs2" > /dev/null) &
runner=$!
sleep 1
kill -USR1 "$runner"
wait "$runner"
status=$?
assert_equals "$status" "0" "a stop after the crash was not a clean exit"
assert_contains "$(cat "$(only_report)/crash.txt" 2> /dev/null)" "exit_status=1" \
  "the stop hid the crash report"
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
