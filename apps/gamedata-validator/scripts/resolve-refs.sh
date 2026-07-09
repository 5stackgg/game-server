#!/usr/bin/env bash
# Resolve the CounterStrikeSharp / SwiftlyS2 versions our game servers actually
# ship, straight out of their Dockerfiles. Validating gamedata from a release we
# don't run reports breaks we don't have, and hides the ones we do.
#
# Prints `KEY=value` lines suitable for `>> "$GITHUB_ENV"` or `eval`.
set -euo pipefail

GAME_SERVER_REF="${GAME_SERVER_REF:-main}"
SWIFTLY_GAME_SERVER_REF="${SWIFTLY_GAME_SERVER_REF:-main}"

dockerfile() {
  local repo="$1" ref="$2"
  curl -sfL "https://raw.githubusercontent.com/5stackgg/${repo}/${ref}/Dockerfile" ||
    curl -sfL "https://raw.githubusercontent.com/5stackgg/${repo}/main/Dockerfile"
}

ccs_ref="$(
  dockerfile game-server "$GAME_SERVER_REF" |
    sed -n 's#.*CounterStrikeSharp/releases/download/\([^/]*\)/.*#\1#p' | head -1
)"

swiftly_ref="$(
  dockerfile swiftly-game-server "$SWIFTLY_GAME_SERVER_REF" |
    sed -n 's#.*SWIFTLYS2_VERSION="\{0,1\}\([^"]*\)"\{0,1\}.*#\1#p' | head -1
)"

if [ -z "$ccs_ref" ] || [ -z "$swiftly_ref" ]; then
  echo "could not resolve refs (ccs='${ccs_ref}' swiftly='${swiftly_ref}')" >&2
  exit 1
fi

echo "CCS_GAMEDATA_REF=${ccs_ref}"
echo "SWIFTLYS2_REF=${swiftly_ref}"
