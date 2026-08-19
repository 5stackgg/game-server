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
# it points at -- the node-wide custom-plugins volume itself. Keeping every
# directory real keeps every write inside the instance.
# True when any gated path lives beneath this directory.
subtree_has_skips() {
  case "${LINK_TREE_SKIP:-}" in
    *"|$1/"*) return 0 ;;
  esac

  return 1
}

# LINK_TREE_SKIP is "|a/b|c/d|" -- paths relative to the ROOT of this walk that
# must not be linked. A directory named in it is skipped whole.
link_tree() {
  local source_path="$1"
  local destination_path="$2"
  local prefix="${3:-}"

  local file
  for file in "$source_path"/* "$source_path"/.[!.]*; do
    if [ ! -e "$file" ]; then
      continue
    fi

    local relative_path="${file#$source_path/}"
    local rooted_path="${prefix:+$prefix/}$relative_path"
    local destination_file="$destination_path/$relative_path"

    if [ -n "${LINK_TREE_SKIP:-}" ]; then
      case "$LINK_TREE_SKIP" in
        *"|$rooted_path|"*) continue ;;
      esac
    fi

    if [ -d "$file" ]; then
      # Nothing under here is gated, so the directory can stay a symlink. That
      # is what lets a plugin write a config at runtime and have it land on the
      # node volume instead of dying with the pod -- the behaviour this
      # directory has always had. Only split one into a real directory when
      # something inside it has to be left out.
      if ! subtree_has_skips "$rooted_path"; then
        if [ ! -e "$destination_file" ]; then
          ln -s "$file" "$destination_file"
          continue
        fi

        # Already points out of the instance. Leaving it alone keeps the
        # write-through; descending into it is what used to corrupt the shared
        # volume.
        if [ -L "$destination_file" ]; then
          continue
        fi
      fi

      local created_here=false
      if [ -L "$destination_file" ]; then
        materialize_dir "$destination_file"
      elif [ ! -d "$destination_file" ]; then
        mkdir -p "$destination_file"
        created_here=true
      fi

      link_tree "$file" "$destination_file" "$rooted_path"

      # A directory whose every entry was gated out leaves an empty shell the
      # runtime would scan and complain about. rmdir only succeeds when it is
      # genuinely empty, and only a directory this pass created is a candidate.
      if [ "$created_here" = true ]; then
        rmdir "$destination_file" 2>/dev/null || true
      fi
    elif [ ! -e "$destination_file" ]; then
      ln -s "$file" "$destination_file"
    fi
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


# Links a plugins directory into a server instance, leaving out managed plugins
# the match's mode did not ask for. Managed and hand-placed files share this
# directory, so the index written at install time is the only way to tell them
# apart -- anything absent from it is hand-placed and always links, which is
# exactly how this directory behaved before plugins were managed at all.
plugin_skip_list() {
  local plugins_root="$1"
  local index="$plugins_root/.5stack-plugins/index"

  if [ ! -f "$index" ]; then
    return 0
  fi

  local enabled=",${ENABLED_PLUGINS},"
  local slug version file
  while IFS="$(printf '\t')" read -r slug version file; do
    if [ -z "$file" ]; then
      continue
    fi

    case "$enabled" in
      *",$slug@$version,"*) continue ;;
    esac

    printf '%s\n' "$file"
  done < "$index"
}

link_plugins() {
  local plugins_root="$1"
  local destination="$2"

  if [ ! -d "$plugins_root" ]; then
    return 0
  fi

  # The index describes the directory; it is not a plugin file itself.
  LINK_TREE_SKIP="|.5stack-plugins|"

  local file
  while IFS= read -r file; do
    if [ -n "$file" ]; then
      LINK_TREE_SKIP="${LINK_TREE_SKIP}${file}|"
      echo "---Plugin not selected by this mode, skipping: ${file}---"
    fi
  done <<EOF
$(plugin_skip_list "$plugins_root")
EOF

  link_tree "$plugins_root" "$destination"

  LINK_TREE_SKIP=""
}
