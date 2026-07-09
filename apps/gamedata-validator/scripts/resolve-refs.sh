#!/usr/bin/env bash
# Resolve the CounterStrikeSharp / SwiftlyS2 versions our game servers actually
# ship, straight out of the sources in this repo. Validating gamedata from a
# release we don't run reports breaks we don't have, and hides the ones we do.
#
# Prints `KEY=value` lines suitable for `>> "$GITHUB_ENV"` or `eval`.
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../../.." && pwd)"

ccs_ref="$(
  sed -n 's#.*CounterStrikeSharp/releases/download/\([^/]*\)/.*#\1#p' \
    "${REPO_ROOT}/apps/counterstrikesharp/Dockerfile" | head -1
)"

# the nuget version, not the Dockerfile's runtime tag: it is what main.py resolves
# gamedata against, and it drops the leading "v".
swiftly_version="$(
  sed -n 's#.*Include="SwiftlyS2.CS2" Version="\([^"]*\)".*#\1#p' \
    "${REPO_ROOT}/apps/swiftly/src/FiveStack.csproj" | head -1
)"

if [ -z "$ccs_ref" ] || [ -z "$swiftly_version" ]; then
  echo "could not resolve refs (ccs='${ccs_ref}' swiftly='${swiftly_version}')" >&2
  exit 1
fi

echo "CCS_GAMEDATA_REF=${ccs_ref}"
echo "SWIFTLYS2_VERSION=${swiftly_version}"
