#!/usr/bin/env bash
# Boots the api with simulated game servers, plays every scenario in
# scenarios.sh and reports what the api made of each.
#
#   ./run.sh                          api image ghcr.io/5stackgg/api:latest
#   ./run.sh --api-checkout ../api    an api built from a checkout
#   ./run.sh --api-image IMAGE        any other api image
#   ./run.sh --speed 60 --keep --verbose bo3-2-1 bo5-3-2
#
# --keep leaves the stack up afterwards (docker compose -p 5stack-sim-e2e down -v).
set -euo pipefail

cd "$(dirname "$0")"
ROOT="$(cd ../../.. && pwd)"

API_IMAGE="ghcr.io/5stackgg/api:latest"
API_CHECKOUT=""
SIM_SPEED=90
KEEP=false
VERBOSE=false
ONLY=()

while [ $# -gt 0 ]; do
    case "$1" in
        --api-image) API_IMAGE="$2"; shift 2 ;;
        --api-checkout) API_CHECKOUT="$2"; shift 2 ;;
        --speed) SIM_SPEED="$2"; shift 2 ;;
        --keep) KEEP=true; shift ;;
        --verbose) VERBOSE=true; shift ;;
        *) ONLY+=("$1"); shift ;;
    esac
done

source ./lib.sh
source ./scenarios.sh

SIM_IMAGE="5stack-sim-e2e:local"
echo "building the simulator"
docker build -q -f "$ROOT/apps/simulator/Dockerfile" -t "$SIM_IMAGE" "$ROOT" >/dev/null

if [ -n "$API_CHECKOUT" ]; then
    echo "building the api from $API_CHECKOUT"
    API_IMAGE="5stack-sim-e2e-api:local"
    docker build -q -t "$API_IMAGE" "$API_CHECKOUT" >/dev/null
else
    docker pull -q "$API_IMAGE" >/dev/null
fi

export API_IMAGE SIM_IMAGE SIM_SPEED

# shellcheck disable=SC2329
teardown() {
    if [ "$KEEP" = false ]; then
        docker compose down -v --remove-orphans >/dev/null 2>&1 || true
    fi
}
trap teardown EXIT

docker compose down -v --remove-orphans >/dev/null 2>&1 || true
echo "starting the api"
if ! docker compose up -d --wait api >/dev/null 2>&1; then
    docker compose logs --tail 50
    exit 1
fi
sql -f - <seed.sql
docker compose up -d sim-1 sim-2 sim-3 >/dev/null 2>&1

wanted() {
    local only
    [ "${#ONLY[@]}" -eq 0 ] && return 0
    for only in "${ONLY[@]}"; do
        [ "$only" = "$1" ] && return 0
    done
    return 1
}

RESULTS="$(mktemp -d)"
SERVERS=3

# Scenarios run one after another on each server, the servers side by side.
run_queue() {
    local server=$1 index=0 entry name
    for entry in "${SCENARIOS[@]}"; do
        read -r name _ <<<"$entry"
        if ! wanted "$name"; then
            continue
        fi
        if [ $((index % SERVERS + 1)) -eq "$server" ]; then
            local started=$SECONDS
            read -r _ function args <<<"$entry"
            # shellcheck disable=SC2086
            if "$function" "$server" $args >"$RESULTS/$name.log" 2>&1; then
                echo "pass $((SECONDS - started))s" >"$RESULTS/$name.result"
            else
                echo "FAIL $((SECONDS - started))s" >"$RESULTS/$name.result"
            fi
        fi
        index=$((index + 1))
    done
}

echo "playing at x$SIM_SPEED on $SERVERS servers"
for server in $(seq 1 "$SERVERS"); do
    run_queue "$server" &
done
wait

failed=0
for entry in "${SCENARIOS[@]}"; do
    read -r name _ <<<"$entry"
    [ -f "$RESULTS/$name.result" ] || continue
    read -r verdict took <"$RESULTS/$name.result"
    echo "$verdict  $name ($took)"
    if [ "$verdict" != pass ]; then
        failed=1
    fi
    if [ "$verdict" != pass ] || [ "$VERBOSE" = true ]; then
        cat "$RESULTS/$name.log"
    fi
done

rm -rf "$RESULTS"
exit "$failed"
