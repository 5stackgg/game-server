#!/bin/bash

# On the node's demos volume (/opt/5stack/demos) because it is the one volume
# both match and dedicated servers mount, and it outlives the container. The
# demo uploader only ever touches .dem files.
CRASH_REPORTS_DIR="/opt/demos/.crash-reports"

# Per server, so a crash loop cannot fill the demos volume.
CRASH_REPORTS_KEPT=10

CONSOLE_TAIL_LINES=2000

# Where the engine's crash handler writes minidumps, inside the container.
MINIDUMP_DIR="/tmp/dumps"

# Runs the server and, when it crashes, saves a report before the container
# exits and takes /tmp with it.
run_server() {
  local started_at
  started_at="$(date -u +%s)"

  local console_pipe
  console_pipe="$(mktemp -u /tmp/console.XXXXXX)"
  mkfifo "$console_pipe"

  local console_file="${console_pipe}.log"
  local map_file="${console_pipe}.map"

  console_tail "$console_file" "$map_file" < "$console_pipe" &
  local console_pid=$!

  # The engine's stdout is block buffered into a pipe, so without this a crash
  # loses the last few KB of console, which is where the cause is. CS:GO's
  # srcds_linux is 32-bit and cannot preload the 64-bit libstdbuf.
  if [ "${GAME_ID}" = "740" ]; then
    "$@" > "$console_pipe" 2>&1 &
  else
    stdbuf -oL "$@" > "$console_pipe" 2>&1 &
  fi
  local server_pid=$!

  wait "$server_pid"
  local status=$?

  await_console_tail "$console_pid"
  rm -f "$console_pipe"

  if server_crashed "$status"; then
    write_crash_report "$status" "$started_at" "$console_file" "$map_file"
  fi

  return "$status"
}

# Passes the console through to the container log a line at a time, keeping
# the last lines and the map, which are written once the console closes. mawk
# holds lines back until its read buffer fills without -W interactive.
console_tail() {
  local console_file="$1"
  local map_file="$2"

  mawk -W interactive -v keep="$CONSOLE_TAIL_LINES" -v console_file="$console_file" -v map_file="$map_file" '
    { print; ring[NR % keep] = $0 }
    /^Host activate: (Loading|Changelevel) \(/ {
      map = $0
      sub(/^[^(]*\(/, "", map)
      sub(/\).*$/, "", map)
    }
    END {
      first = NR > keep ? NR - keep + 1 : 1
      for (line = first; line <= NR; line++) {
        print ring[line % keep] > console_file
      }
      if (map != "") {
        print map > map_file
      }
    }
  '
}

# Bounded: a child the server left behind can hold the console open past its
# exit, and the container must still stop.
await_console_tail() {
  local console_pid="$1"
  local waited=0

  while kill -0 "$console_pid" 2> /dev/null; do
    if [ "$waited" -ge 50 ]; then
      kill "$console_pid" 2> /dev/null
      return 1
    fi

    sleep 0.1
    waited=$((waited + 1))
  done

  wait "$console_pid" 2> /dev/null
}

server_crashed() {
  local status="$1"

  [ "$status" -ne 0 ] || compgen -G "${MINIDUMP_DIR}/*" > /dev/null
}

# Keyed by SERVER_ID so a report traces back to the server that crashed. A
# match server's id is an on-demand slot that is reused, so the match is
# recorded alongside it.
write_crash_report() {
  local status="$1"
  local started_at="$2"
  local console_file="$3"
  local map_file="$4"

  local crashed_at
  crashed_at="$(date -u +%s)"

  local server_dir="${CRASH_REPORTS_DIR}/${SERVER_ID:-unknown}"
  local report_dir
  report_dir="${server_dir}/$(date -u -d "@${crashed_at}" +%Y%m%dT%H%M%SZ)"

  if ! mkdir -p "$report_dir"; then
    echo "---Server exited with status ${status}; unable to save a crash report to ${report_dir}---"
    return 1
  fi

  if [ -f "$console_file" ]; then
    mv "$console_file" "${report_dir}/console.log"
  fi

  local map=""
  if [ -f "$map_file" ]; then
    map="$(cat "$map_file")"
    rm -f "$map_file"
  fi

  local minidumps=0
  local dump
  for dump in "${MINIDUMP_DIR}"/*; do
    if [ -f "$dump" ]; then
      mv "$dump" "${report_dir}/"
      minidumps=$((minidumps + 1))
    fi
  done

  {
    echo "server_id=${SERVER_ID:-}"
    echo "match_id=${MATCH_ID:-}"
    echo "server_type=${SERVER_TYPE:-}"
    echo "host=$(hostname)"
    echo "exit_status=${status}"
    echo "started_at=$(date -u -d "@${started_at}" +%Y-%m-%dT%H:%M:%SZ)"
    echo "crashed_at=$(date -u -d "@${crashed_at}" +%Y-%m-%dT%H:%M:%SZ)"
    echo "map=${map}"
    echo "image_version=${RELEASE_VERSION:-}"
    echo "game_id=${GAME_ID:-}"
    echo "game_version=$(game_version)"
    echo "enabled_plugins=${ENABLED_PLUGINS:-}"
    echo "launch_params=$(redact_passwords "${EXTRA_GAME_PARAMS:-}")"
    echo "minidumps=${minidumps}"
  } > "${report_dir}/crash.txt"

  prune_crash_reports "$server_dir"

  echo "---Server exited with status ${status}; crash report saved to ${report_dir}---"
}

game_version() {
  local steam_inf

  for steam_inf in "${INSTANCE_SERVER_DIR}/game/csgo/steam.inf" "${INSTANCE_SERVER_DIR}/csgo/steam.inf"; do
    if [ -f "$steam_inf" ]; then
      sed -n 's/^PatchVersion=//p' "$steam_inf" | tr -d '\r'
      return
    fi
  done
}

redact_passwords() {
  printf '%s' "$1" | sed -E 's/(\+[a-z_]*password)( +)[^ ]+/\1\2<redacted>/g'
}

# Report directories are named by UTC time, so name order is age order.
prune_crash_reports() {
  local server_dir="$1"
  local reports=("$server_dir"/*/)
  local excess=$((${#reports[@]} - CRASH_REPORTS_KEPT))
  local index

  for ((index = 0; index < excess; index++)); do
    rm -rf "${reports[$index]}"
  done
}
