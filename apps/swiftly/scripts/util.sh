#!/bin/bash

create_directories() {
  local base_dir="$1"
  shift

  for dir in "$@"; do
    mkdir -p "$base_dir/$dir"
  done
}

create_symlinks() {
  local source_path="$1"
  local destination_path="$2"

  for file in "$source_path"/*; do
    local relative_path="${file#$source_path/}"
    local destination_file="$destination_path/$relative_path"

    if [ -f "$file" ]; then
      if [ ! -e "$destination_file" ]; then
        ln -s "$file" "$destination_file"
      fi
    elif [ -d "$file" ]; then
      if [ ! -e "$destination_file" ]; then
        ln -s "$file" "$destination_file"
      fi
      create_symlinks "$file" "$destination_file"
    fi
  done
}

