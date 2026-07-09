#!/usr/bin/env bash
#
# Run inside the pod, after `codepier up` picked a workload. Builds and hot-reloads
# whichever plugin that pod serves — codepier exports CODEPIER_DEPLOYMENT into the shell.
set -euo pipefail

cd "$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

case "${CODEPIER_DEPLOYMENT:-}" in
  dev-game-server) app="counterstrikesharp" ;;
  dev-swiftly-game-server) app="swiftly" ;;
  "")
    echo "not inside a codepier pod; run 'codepier up' from the repo root first" >&2
    exit 1
    ;;
  *)
    echo "no plugin for workload '${CODEPIER_DEPLOYMENT}'" >&2
    exit 1
    ;;
esac

exec bash "apps/${app}/scripts/dev.sh"
