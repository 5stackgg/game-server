#!/usr/bin/env bash
#
# Tail the game server's logs. The pod also runs codepier's `dev` container and a
# secrets sidecar, so the server container has to be named explicitly.
#
#   ./scripts/tail.sh            pick interactively
#   ./scripts/tail.sh swiftly
set -euo pipefail

workload_for() {
  case "$1" in
    counterstrikesharp | css) echo "dev-game-server" ;;
    swiftly | sw) echo "dev-swiftly-game-server" ;;
    *) return 1 ;;
  esac
}

plugin="${1:-}"

if [ -z "$plugin" ]; then
  PS3="plugin: "
  select plugin in counterstrikesharp swiftly; do
    [ -n "$plugin" ] && break
  done
fi

if ! workload="$(workload_for "$plugin")"; then
  echo "usage: $0 [counterstrikesharp|swiftly]" >&2
  exit 1
fi

# the container carries the same name as its workload
exec codepier tail --deployment "$workload" --container "$workload"
