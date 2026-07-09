#!/usr/bin/env bash
#
#   ./scripts/tail.sh            pick interactively
#   ./scripts/tail.sh swiftly
set -euo pipefail

cd "$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

container_for() {
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

if ! container="$(container_for "$plugin")"; then
  echo "usage: $0 [counterstrikesharp|swiftly]" >&2
  exit 1
fi

exec codepier tail --container "$container"
