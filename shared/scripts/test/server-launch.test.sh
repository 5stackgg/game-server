#!/bin/bash
# Tests for the command line each image's server.sh starts the game server with.
# Run: bash shared/scripts/test/server-launch.test.sh

set -u

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_DIR="$(cd "${SCRIPT_DIR}/../../.." && pwd)"

failures=0

fail() {
  echo "  FAIL: $1" >&2
  failures=$((failures + 1))
}

# Runs server.sh beside a crash-report.sh whose run_server only prints the
# command it was handed, one argument per line.
launch_args() {
  local server_sh="$1" game_id="$2" server_type="$3" workdir
  workdir="$(mktemp -d)"

  cp "$server_sh" "$workdir/server.sh"
  cat > "$workdir/crash-report.sh" << 'EOF'
run_server() {
  printf '%s\n' "$@"
}
EOF
  mkdir -p "$workdir/instance" "$workdir/steamcmd/linux64"

  env -i PATH="$PATH" \
    GAME_ID="$game_id" \
    SERVER_TYPE="$server_type" \
    INSTANCE_SERVER_DIR="$workdir/instance" \
    STEAMCMD_DIR="$workdir/steamcmd" \
    SERVER_PORT=27015 \
    TV_PORT=27020 \
    RCON_PASSWORD=hunter2 \
    EXTRA_GAME_PARAMS="-maxplayers 13 +map de_overpass" \
    bash "$workdir/server.sh" 2> /dev/null | sed '1,/^---Starting Server/d'

  rm -rf "$workdir"
}

has_arg() {
  printf '%s\n' "$1" | grep -qxF -- "$2"
}

for server_sh in "$REPO_DIR/apps/counterstrikesharp/scripts/server.sh" "$REPO_DIR/apps/swiftly/scripts/server.sh"; do
  name="${server_sh#"$REPO_DIR"/}"

  for server_type in Ranked Competitive; do
    echo "$name lets a $server_type cs2 server write and load round backup files"
    args="$(launch_args "$server_sh" 730 "$server_type")"
    if ! has_arg "$args" "-allow_round_backup_files"; then
      fail "$name does not start a $server_type cs2 server with -allow_round_backup_files: $(printf '%s' "$args" | tr '\n' ' ')"
    fi
  done

  for server_type in Casual Wingman Deathmatch ArmsRace Retake Practice Custom; do
    echo "$name keeps round backup files off on a $server_type server"
    if has_arg "$(launch_args "$server_sh" 730 "$server_type")" "-allow_round_backup_files"; then
      fail "$name starts a $server_type server with -allow_round_backup_files"
    fi
  done

  echo "$name keeps the cs2-only flag off csgo"
  if has_arg "$(launch_args "$server_sh" 740 Ranked)" "-allow_round_backup_files"; then
    fail "$name starts csgo with -allow_round_backup_files"
  fi

  echo "$name still passes the match's own params"
  args="$(launch_args "$server_sh" 730 Ranked)"
  if ! has_arg "$args" "de_overpass"; then
    fail "$name dropped EXTRA_GAME_PARAMS: $(printf '%s' "$args" | tr '\n' ' ')"
  fi
done

if [ "$failures" -gt 0 ]; then
  echo "$failures failure(s)" >&2
  exit 1
fi

echo "all passed"
