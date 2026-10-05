#!/bin/bash

# gh auth login --scopes read:packages,write:packages,delete:packages

# gh pipes tty output through less; the backgrounded deletes each open one and
# fight over the terminal
export GH_PAGER=cat

read -e -p "Which plugin to prune? (css/sw): " prefix

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

IFS=$'\t' read -r latest_num latest_tag _ <<<"$(head -n 1 <<<"$releases")"

# the API reads the body as min_game_build_id, and a release without one inherits
# it from the newest older release that has one
IFS=$'\t' read -r anchor_num anchor_tag build_id <<<"$(awk -F'\t' '$3 ~ /^[0-9]+$/ { print; exit }' <<<"$releases")"

if [[ -z "$build_id" ]]; then
  echo "No $prefix release carries a CS2 build id"
  exit 1
fi

echo "Latest $prefix release: $latest_tag"
echo "Newest CS2 build id: $build_id (on $anchor_tag)"

if [[ "$anchor_num" == "$latest_num" ]]; then
  echo "Nothing to prune"
  exit 0
fi

stale_tags=$(awk -F'\t' -v from="$anchor_num" -v to="$latest_num" '$1 >= from && $1 < to { print $2 }' <<<"$releases")

echo "Moving build id $build_id to $latest_tag and removing:"
echo "$stale_tags" | sed 's/^/  /'
read -e -p "Continue? (y/N): " confirm

if [[ "$confirm" != "y" ]]; then
  exit 1
fi

# before any delete, so a failed edit can't lose the build id
if ! gh release edit "$latest_tag" --notes "$build_id" > /dev/null; then
  echo "Failed to set the build id on $latest_tag"
  exit 1
fi

echo "Removing releases"
for tag in $stale_tags; do
  gh release delete "$tag" -y --cleanup-tag
done

echo "Removing Containers Images"
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
  else
    echo "No container image found for tag $image_tag"
  fi
done

wait
