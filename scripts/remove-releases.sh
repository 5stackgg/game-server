#!/bin/bash

latest_tag=$(gh release list | head -n 1 | awk '{print $1}')

echo "Latest tag: $latest_tag"

read -p "Enter from version: " from_tag
read -p "Enter to version: " to_tag

from_num=$(echo "$from_tag" | grep -oE '[0-9]+$')
to_num=$(echo "$to_tag" | grep -oE '[0-9]+$')

if [[ -z "$from_num" || -z "$to_num" ]]; then
  echo "Invalid versions"
  exit 1
fi

echo "Removing releases from v0.0.$from_num to v0.0.$to_num"
for ((i=from_num; i<=to_num; i++)); do
  tag="v0.0.$i"
  if ! gh release delete "$tag" -y --cleanup-tag; then
    continue
  fi
done