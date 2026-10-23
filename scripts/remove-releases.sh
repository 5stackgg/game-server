#!/bin/bash

# gh auth login --scopes read:packages,write:packages,delete:packages
#
# remove-releases.sh [css|sw] [build id] [-y]
# Keeps the newest release on a CS2 build and removes the older ones on it. The
# build defaults to the latest release's; -y skips the confirmation, which is
# how Set Min Game Build prunes the build it moves off.

# gh pipes tty output through less; the backgrounded deletes each open one and
# fight over the terminal
export GH_PAGER=cat

confirmed=false
args=()
for arg in "$@"; do
  case "$arg" in
    -y) confirmed=true ;;
    *) args+=("$arg") ;;
  esac
done
prefix="${args[0]:-}"
build_id="${args[1]:-}"

if [[ -z "$prefix" ]]; then
  read -e -p "Which plugin to prune? (css/sw): " prefix
fi
if [[ -n "$build_id" && ! "$build_id" =~ ^[0-9]+$ ]]; then
  echo "Invalid CS2 build id: $build_id"
  exit 1
fi

case "$prefix" in
  css) package="game-server-css" ;;
  sw)  package="game-server-sw" ;;
  *)
    echo "Invalid plugin: pick css or sw"
    exit 1
    ;;
esac

# "<counter>\t<tag>\t<body>", newest first
releases=$(gh api --paginate "repos/{owner}/{repo}/releases?per_page=100" | jq -r --arg prefix "$prefix" '
  .[]
  | select(.draft | not)
  | (.tag_name | capture("^" + $prefix + "-v0\\.0\\.(?<n>[0-9]+)$")) as $m
  | [($m.n | tonumber), .tag_name, ((.body // "") | gsub("\\s"; ""))]
  | @tsv' | sort -rn)

if [[ -z "$releases" ]]; then
  echo "No $prefix releases found"
  exit 1
fi

# the API reads the body as min_game_build_id, and a release without one inherits
# it from the newest older release that has one. CI now writes it on every
# release, but the older ones still lean on that inheritance.
# "<counter>\t<tag>\t<build id>", newest first
resolved=$(sort -n <<<"$releases" | awk -F'\t' -v OFS='\t' '$3 ~ /^[0-9]+$/ { id = $3 } { print $1, $2, id }' | sort -rn)

if [[ -z "$build_id" ]]; then
  IFS=$'\t' read -r _ _ build_id <<<"$(head -n 1 <<<"$resolved")"
fi

if [[ -z "$build_id" ]]; then
  echo "No $prefix release carries a CS2 build id"
  exit 1
fi

# the API hands a node the newest release on its build, so that one supersedes
# every older one on the same build
IFS=$'\t' read -r keep_num keep_tag _ <<<"$(awk -F'\t' -v id="$build_id" '$3 == id' <<<"$resolved" | head -n 1)"

if [[ -z "$keep_tag" ]]; then
  echo "No $prefix release on CS2 build $build_id"
  exit 0
fi

echo "Newest $prefix release on CS2 build $build_id: $keep_tag"

stale_tags=$(awk -F'\t' -v id="$build_id" -v to="$keep_num" '$3 == id && $1 < to { print $2 }' <<<"$resolved")

if [[ -z "$stale_tags" ]]; then
  echo "Nothing to prune"
  exit 0
fi

echo "Keeping $keep_tag on build $build_id and removing:"
echo "$stale_tags" | sed 's/^/  /'

if [[ "$confirmed" != "true" ]]; then
  read -e -p "Continue? (y/N): " confirm
  if [[ "$confirm" != "y" ]]; then
    exit 1
  fi
fi

# before any delete, so a failed edit can't lose the build id
if ! gh release edit "$keep_tag" --notes "$build_id" > /dev/null; then
  echo "Failed to set the build id on $keep_tag"
  exit 1
fi

echo "Removing releases"
for tag in $stale_tags; do
  gh release delete "$tag" -y --cleanup-tag
done

echo "Removing Containers Images"
pids=()
image_versions=$(gh api --paginate \
  -H "Accept: application/vnd.github+json" \
  -H "X-GitHub-Api-Version: 2022-11-28" \
  "/orgs/5stackgg/packages/container/${package}/versions?per_page=100")

for tag in $stale_tags; do
  # the image is namespaced by package, so its version tag stays bare
  image_tag="${tag#"${prefix}"-}"

  image_id=$(jq -r --arg tag "$image_tag" \
    '.[] | select(any(.metadata.container.tags[]?; . == $tag)) | .id' <<<"$image_versions" | head -n 1)

  if [[ -n "$image_id" ]]; then
    echo "Deleting container image version with tag $image_tag (id: $image_id)"
    gh api -X DELETE \
      -H "Accept: application/vnd.github+json" \
      -H "X-GitHub-Api-Version: 2022-11-28" \
      "/orgs/5stackgg/packages/container/${package}/versions/$image_id" &
    pids+=($!)
  else
    echo "No container image found for tag $image_tag"
  fi
done

failed=0
for pid in "${pids[@]}"; do
  wait "$pid" || failed=1
done
if [[ "$failed" -ne 0 ]]; then
  echo "Failed to delete some container image versions"
  exit 1
fi
