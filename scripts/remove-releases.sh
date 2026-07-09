#!/bin/bash

# gh auth login --scopes read:packages,write:packages,delete:packages

package="$1"
prefix="$2"

if [[ -z "$package" || -z "$prefix" ]]; then
  echo "usage: $0 <ghcr-package> <tag-prefix>"
  echo "  e.g. $0 game-server-css css"
  echo "       $0 game-server-sw  sw"
  exit 1
fi

latest_tag=$(gh release list | grep "^${prefix}-v" | head -n 1 | awk '{print $1}')

echo "Latest $prefix tag: $latest_tag"

read -p "Enter from version: " from_tag
read -p "Enter to version: " to_tag

from_num=$(echo "$from_tag" | grep -oE '[0-9]+$')
to_num=$(echo "$to_tag" | grep -oE '[0-9]+$')

if [[ -z "$from_num" || -z "$to_num" ]]; then
  echo "Invalid versions"
  exit 1
fi

echo "Removing releases from ${prefix}-v0.0.$from_num to ${prefix}-v0.0.$to_num"
for ((i=from_num; i<=to_num; i++)); do
  if ! gh release delete "${prefix}-v0.0.$i" -y --cleanup-tag; then
    continue
  fi
done

echo "Removing Containers Images"
for ((i=from_num; i<=to_num; i++)); do
  # the image is namespaced by package, so its version tag stays bare
  tag="v0.0.$i"

  image_id=$(gh api \
    -H "Accept: application/vnd.github+json" \
    -H "X-GitHub-Api-Version: 2022-11-28" \
    "/orgs/5stackgg/packages/container/${package}/versions" \
    | jq ".[] | select(.metadata.container.tags[]? == \"$tag\") | .id" | head -n 1)

  if [[ -n "$image_id" ]]; then
    echo "Deleting container image version with tag $tag (id: $image_id)"
    gh api -X DELETE \
      -H "Accept: application/vnd.github+json" \
      -H "X-GitHub-Api-Version: 2022-11-28" \
      "/orgs/5stackgg/packages/container/${package}/versions/$image_id" &
  else
    echo "No container image found for tag $tag"
  fi
done
