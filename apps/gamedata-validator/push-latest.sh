#!/usr/bin/env bash
set -euo pipefail

IMAGE="ghcr.io/5stackgg/gamedata-validator"
CACHE_REF="${IMAGE}:buildcache"
SHA="$(git rev-parse HEAD)"

if [ "$#" -gt 0 ]; then
  TAGS=( "$@" )
else
  # shellcheck disable=SC2206
  TAGS=( ${TAGS:-latest} )
fi

cd "$(dirname "$0")"

tag_args=()
for t in "${TAGS[@]}"; do
  tag_args+=( --tag "${IMAGE}:${t}" )
done
tag_args+=( --tag "${IMAGE}:${SHA}" )

echo "building + pushing ${IMAGE} with tags: ${TAGS[*]} ${SHA}"
docker buildx build \
  --platform linux/amd64 \
  --push \
  "${tag_args[@]}" \
  --cache-from "type=registry,ref=${CACHE_REF}" \
  --cache-to "type=registry,ref=${CACHE_REF},mode=max" \
  .
