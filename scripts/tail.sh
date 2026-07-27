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

# SwiftlyS2 bug: the game process stops emitting to stdout after the first
# plugin hot reload, but its own log files keep flowing — so for swiftly,
# merge the managed log file into the stream alongside container stdout.
#
# Both streams carry the same plugin lines while stdout is alive, so the file
# tail is gated on a liveness stamp the stdout side touches: its lines are only
# printed once stdout has been quiet for STDOUT_QUIET_SECONDS, i.e. once the
# reload bug has actually bitten. Otherwise every plugin line shows up twice.
STDOUT_QUIET_SECONDS=10

if [ "$workload" = "dev-swiftly-game-server" ] && command -v kubectl >/dev/null 2>&1; then
  export KUBECONFIG="${KUBECONFIG:-$HOME/.kube/5stackgg}"
  pod=$(kubectl -n 5stack get pods -l app="$workload" \
    --field-selector=status.phase=Running \
    --sort-by=.metadata.creationTimestamp \
    -o jsonpath='{.items[-1].metadata.name}' 2>/dev/null || true)
  if [ -n "$pod" ]; then
    stdout_stamp="$(mktemp -t tail-stdout-stamp)"
    date +%s > "$stdout_stamp"

    kubectl -n 5stack exec "$pod" -c "$workload" -- sh -c \
      'tail -n 5 -F /opt/instance/game/csgo/addons/swiftlys2/logs/managed/*.log 2>/dev/null' \
      | while IFS= read -r line; do
          last="$(cat "$stdout_stamp" 2>/dev/null || echo 0)"
          if [ "$(( $(date +%s) - last ))" -ge "$STDOUT_QUIET_SECONDS" ]; then
            printf '[sw-log] %s\n' "$line"
          fi
        done &
    file_tail_pid=$!
    trap 'kill "$file_tail_pid" 2>/dev/null; rm -f "$stdout_stamp"' EXIT
  fi
fi

# the container carries the same name as its workload
# (no exec: the EXIT trap must fire to reap the background file tail)
if [ -n "${stdout_stamp:-}" ]; then
  codepier tail --deployment "$workload" --container "$workload" \
    | while IFS= read -r line; do
        date +%s > "$stdout_stamp"
        printf '%s\n' "$line"
      done
else
  codepier tail --deployment "$workload" --container "$workload"
fi
