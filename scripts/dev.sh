#!/usr/bin/env bash
#
# Both sides of the dev loop:
#   on your machine, swap into the plugin's pod with codepier
#   inside that pod, run the plugin's hot-reload build loop
#
#   ./scripts/dev.sh            pick interactively
#   ./scripts/dev.sh swiftly
set -euo pipefail

cd "$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

app_for() {
  case "$1" in
    counterstrikesharp | css) echo "counterstrikesharp" ;;
    swiftly | sw) echo "swiftly" ;;
    *) return 1 ;;
  esac
}

deployment_for() {
  case "$1" in
    counterstrikesharp) echo "dev-game-server" ;;
    swiftly) echo "dev-swiftly-game-server" ;;
  esac
}

running_app() {
  case "${HOSTNAME:-}" in
    dev-swiftly-game-server-*) echo "swiftly" ;;
    dev-game-server-*) echo "counterstrikesharp" ;;
  esac
}

usage() {
  echo "usage: $0 [counterstrikesharp|swiftly]" >&2
  exit 1
}

plugin="${1:-}"

if [ -n "$plugin" ] && ! app_for "$plugin" >/dev/null; then
  usage
fi

# The pod already decided which plugin: it is whichever deployment codepier
# swapped. Building the other one here would hot-reload it into the wrong server.
if [ -n "${KUBERNETES_SERVICE_HOST:-}" ]; then
  running="$(running_app)"

  if [ -n "$running" ] && [ -n "$plugin" ] && [ "$(app_for "$plugin")" != "$running" ]; then
    echo "this is the $running pod; run '$0 $plugin' on your machine to swap pods" >&2
    exit 1
  fi

  if [ -z "$running" ]; then
    [ -n "$plugin" ] || {
      echo "cannot tell which dev pod this is (HOSTNAME=${HOSTNAME:-unset}); pass the plugin" >&2
      usage
    }
    running="$(app_for "$plugin")"
  fi

  exec bash "apps/${running}/scripts/dev.sh"
fi

if [ -z "$plugin" ]; then
  PS3="plugin: "
  select plugin in counterstrikesharp swiftly; do
    [ -n "$plugin" ] && break
  done
fi

app="$(app_for "$plugin")"

if ! command -v codepier >/dev/null; then
  echo "codepier is not installed; see https://docs.5stack.gg" >&2
  exit 1
fi

exec codepier up --deployment "$(deployment_for "$app")"
