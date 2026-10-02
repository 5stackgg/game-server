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

# A dedicated server mounts the node-wide volume at /opt/node-plugins and its
# own directory at /opt/custom-plugins, so a link into the latter is that one
# server's disk. Writing through it keeps the file on the server's Files tab,
# and keeps whatever plugins write at runtime there too; copying it out left
# both inside the container, gone on the next restart.
server_owned_link() {
  local dir="$1"

  if [ ! -L "$dir" ] || [ ! -d "${NODE_PLUGINS_DIR:-/opt/node-plugins}" ]; then
    return 1
  fi

  local own
  own="$(readlink -f "${CUSTOM_PLUGINS_DIR:-/opt/custom-plugins}")"

  if [ -z "$own" ]; then
    return 1
  fi

  case "$(readlink -f "$dir")/" in
    "$own/"*) return 0 ;;
  esac

  return 1
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

      if ! server_owned_link "$current"; then
        materialize_dir "$current"
      fi

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

    # A file linked in from a plugin's own release is the node's copy; writing
    # through the link would hand this server's file to every server there.
    if [ -L "$destination" ]; then
      rm -f "$destination"
    fi

    printf '%s' "$decoded" | jq -r --arg key "$relative" '.[$key]' > "$destination"
    echo "---Plugin Configs: wrote ${relative}---"
  done <<< "$paths"
}

# CS2 has no server-to-client file transfer, so anything a plugin shows or plays
# that the game does not ship -- models, sounds, Panorama layouts -- reaches
# players as a workshop addon, which AddonsManager names to each connecting
# client for Steam to deliver.
#
# Prints each workshop id in its arguments once, in order, one per line. An
# argument may be a comma-separated list. AddonsManager reads its config with
# optional:false and validates it on start, so one entry that is not a bare
# numeric id -- a pasted quote, a URL, a trailing comma -- takes it down along
# with every addon it serves. Those are dropped with a warning instead.
workshop_addon_ids() {
  local seen="," list id
  local -a ids

  for list in "$@"; do
    if [ -z "$list" ]; then
      continue
    fi

    IFS="," read -r -a ids <<< "$list"

    for id in "${ids[@]}"; do
      case "$id" in
        "")
          continue
          ;;
        *[!0-9]*)
          echo "---Workshop Addons: '${id}' is not a workshop id, skipping it---" >&2
          continue
          ;;
      esac

      case "$seen" in
        *",$id,"*) continue ;;
      esac

      seen="${seen}${id},"
      printf '%s\n' "$id"
    done
  done
}

# Links the AddonsManager the SwiftlyS2 image ships into this server and has it
# serve exactly these workshop ids, which must already be bare numbers.
#
# The config is rewritten every boot: the ids belong to this server, and a
# stale copy from an older boot would silently serve the wrong addons.
#
# RedownloadAddonOnMount because addons change -- unlike a map, which is
# published once. AddonsManager only checks that an item is installed, not that
# it is current, so without it a server that cached an older copy serves it
# forever and never picks up a republish.
#
# Through materialize_for_write because configs is a symlink onto the node-wide
# volume: written straight through it, one server's addons land in front of
# every other server on the node -- and AddonsManager watches the file, so a
# running server swaps to them live.
enable_addons_manager() {
  local root="$1"
  shift

  local source="${ADDONS_MANAGER_DIR:-/opt/addons-manager/AddonsManager}"

  if [ ! -d "$source" ]; then
    echo "---Workshop Addons: this image has no AddonsManager, players will not get $*---" >&2
    return 0
  fi

  local plugin_dir="$root/addons/swiftlys2/plugins/AddonsManager"

  if [ ! -e "$plugin_dir" ]; then
    mkdir -p "$(dirname "$plugin_dir")"
    ln -s "$source" "$plugin_dir"
  fi

  local config
  config="$(materialize_for_write "$root" "addons/swiftlys2/configs/plugins/AddonsManager/config.jsonc")"

  # A link here is a copy shared with other servers; replace it, never write
  # through it.
  if [ -L "$config" ]; then
    rm -f "$config"
  fi

  if ! jq -n '{Main: {Addons: $ARGS.positional, RedownloadAddonOnMount: true}}' \
    --args "$@" > "$config"; then
    echo "---Workshop Addons: could not write ${config}---" >&2
    return 0
  fi

  echo "---Workshop Addons: serving $* via AddonsManager---"
}

# Valve keeps the server's SteamAppId 730 in this file, so the relay settings are
# added to it rather than replacing it. The instance copy is a symlink onto the
# node's game files, so the result is moved over the link, never written through.
enable_steam_relay() {
  local file="$1"

  awk '
    convars && /{/ { print; print "\t\t\"net_p2p_listen_dedicated\" \"1\""; convars = 0; next }
    /^[[:space:]]*ConVars[[:space:]]*$/ { convars = 1; seen = 1 }
    /^}/ {
      if (!seen) { print "\tConVars\n\t{\n\t\t\"net_p2p_listen_dedicated\" \"1\"\n\t}" }
      print "\tNetworkSystem\n\t{\n\t\t\"CreateListenSocketP2P\" \"2\"\n\t}"
    }
    { print }
  ' "$file" > "$file.relay"

  mv -f "$file.relay" "$file"
}

# Only whole-line // comments are stripped; a trailing // may be inside a URL.
ensure_command_prefix() {
  local file="$1"
  local key="$2"
  local prefix="$3"
  shift 3

  if [ ! -f "$file" ]; then
    return 0
  fi

  local current
  if ! current="$(grep -v '^[[:space:]]*//' "$file" | jq -e 'objects' 2>/dev/null)"; then
    echo "---Command Prefixes: ${file} is not plain JSON, leaving ${key} as it is---" >&2
    return 0
  fi

  if printf '%s' "$current" | jq -e --arg key "$key" --arg prefix "$prefix" \
    '.[$key] | arrays | any(.[]; . == $prefix)' > /dev/null 2>&1; then
    return 0
  fi

  local updated
  if ! updated="$(printf '%s' "$current" | jq --arg key "$key" --arg prefix "$prefix" \
    '.[$key] = [$prefix] + ((.[$key] | arrays // $ARGS.positional) - [$prefix])' --args "$@")"; then
    echo "---Command Prefixes: could not add ${prefix} to ${key} in ${file}---" >&2
    return 0
  fi

  # Every server on the node reads this file through the configs symlink, so it is
  # swapped in whole rather than truncated and rewritten under a booting server.
  local staged
  if ! staged="$(mktemp "$(dirname "$file")/.$(basename "$file").XXXXXX")"; then
    echo "---Command Prefixes: could not stage ${file}---" >&2
    return 0
  fi

  if ! { cp -p "$file" "$staged" && printf '%s\n' "$updated" > "$staged" && mv -f "$staged" "$file"; }; then
    rm -f "$staged"
    echo "---Command Prefixes: could not write ${file}---" >&2
    return 0
  fi

  echo "---Command Prefixes: added ${prefix} to ${key}---"
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
