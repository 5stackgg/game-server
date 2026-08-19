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


# Mirrors directories as real directories and symlinks only leaf files.
# create_symlinks symlinks whole directories, which is correct for the read-only
# game files but not here: composing several plugin sources into one tree makes a
# later pass descend through an earlier directory symlink and write into whatever
# it points at -- the node-wide custom-plugins volume, or another plugin's store
# directory. Keeping every directory real keeps every write inside the instance.
link_tree() {
  local source_path="$1"
  local destination_path="$2"

  local file
  for file in "$source_path"/* "$source_path"/.[!.]*; do
    if [ ! -e "$file" ]; then
      continue
    fi

    local relative_path="${file#$source_path/}"
    local destination_file="$destination_path/$relative_path"

    if [ -d "$file" ]; then
      if [ -L "$destination_file" ]; then
        materialize_dir "$destination_file"
      elif [ ! -d "$destination_file" ]; then
        mkdir -p "$destination_file"
      fi

      link_tree "$file" "$destination_file"
    elif [ ! -e "$destination_file" ]; then
      ln -s "$file" "$destination_file"
    fi
  done
}

# ENABLED_PLUGINS is "slug@version,slug@version", ordered by the mode's load order.
# create_symlinks never overwrites an existing destination, so earlier entries --
# and anything hand-placed in custom-plugins -- win any collision.
install_store_plugins() {
  local store_root="$1"
  local destination="$2"

  if [ -z "$ENABLED_PLUGINS" ]; then
    return 0
  fi

  local entry
  for entry in ${ENABLED_PLUGINS//,/ }; do
    local slug="${entry%@*}"
    local version="${entry#*@}"

    case "$slug/$version" in
      *..*)
        echo "---Plugin Store: refusing unsafe entry ${entry}---" >&2
        continue
        ;;
    esac

    if [ -z "$slug" ] || [ -z "$version" ] || [ "$slug" = "$entry" ]; then
      echo "---Plugin Store: malformed entry ${entry}, expected slug@version---" >&2
      continue
    fi

    local plugin_path="$store_root/$slug/$version"

    if [ ! -d "$plugin_path" ]; then
      echo "---Plugin Store: ${slug}@${version} is not installed on this node---" >&2
      continue
    fi

    echo "---Plugin Store: linking ${slug}@${version}---"
    link_tree "$plugin_path" "$destination"
  done
}

# A directory reached through custom-plugins is a symlink onto the node-wide
# volume, so writing under it leaks into every other server on the node and
# survives this match. Swap it for a real directory seeded from the same
# contents before writing.
materialize_dir() {
  local dir="$1"

  if [ ! -L "$dir" ]; then
    mkdir -p "$dir"
    return 0
  fi

  local target
  target="$(readlink -f "$dir")"

  rm "$dir"
  mkdir -p "$dir"

  if [ -d "$target" ]; then
    cp -R "$target/." "$dir/"
  fi
}

materialize_for_write() {
  local root="$1"
  local relative="$2"
  local current="$root"
  local directory
  directory="$(dirname "$relative")"

  if [ "$directory" != "." ]; then
    local remaining="$directory"

    while [ -n "$remaining" ]; do
      local segment="${remaining%%/*}"

      current="$current/$segment"
      materialize_dir "$current"

      if [ "$remaining" = "$segment" ]; then
        remaining=""
      else
        remaining="${remaining#*/}"
      fi
    done
  fi

  printf '%s/%s\n' "$current" "$(basename "$relative")"
}

# PLUGIN_CONFIGS is base64 JSON mapping a game/csgo-relative path to its contents.
write_plugin_configs() {
  local root="$1"

  if [ -z "$PLUGIN_CONFIGS" ]; then
    return 0
  fi

  local decoded
  if ! decoded="$(printf '%s' "$PLUGIN_CONFIGS" | base64 -d 2>/dev/null)"; then
    echo "---Plugin Configs: PLUGIN_CONFIGS is not valid base64---" >&2
    return 0
  fi

  local paths
  if ! paths="$(printf '%s' "$decoded" | jq -r 'keys[]' 2>/dev/null)"; then
    echo "---Plugin Configs: PLUGIN_CONFIGS is not a valid JSON object---" >&2
    return 0
  fi

  local relative
  while IFS= read -r relative; do
    if [ -z "$relative" ]; then
      continue
    fi

    case "$relative" in
      /*|*..*)
        echo "---Plugin Configs: refusing unsafe path ${relative}---" >&2
        continue
        ;;
    esac

    local destination
    destination="$(materialize_for_write "$root" "$relative")"

    printf '%s' "$decoded" | jq -r --arg key "$relative" '.[$key]' > "$destination"
    echo "---Plugin Configs: wrote ${relative}---"
  done <<< "$paths"
}
