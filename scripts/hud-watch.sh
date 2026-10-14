#!/usr/bin/env bash
#
# Mirrors the pod-compiled practice HUD into a local CS2 install as loose files,
# so a layout change can be seen in game without publishing to the Workshop.
#
# Run on the machine with the editor, once `codepier up` has the utility pod
# building: apps/utility-sw/hud/build.sh leaves the compiled tree in hud/dist,
# the sync brings it back here, and this copies it under game/csgo/panorama.
#
# A running client keeps every layout it has loaded, by name, until it exits.
# So a dev build (apps/utility-sw/scripts/dev.sh) also carries a copy of every
# file under a tagged name, and once such a build is fully on the install this
# hands its tag back through hud/dist/hud-live-tag for the plugin to respawn the
# HUD under the new names. Without a tag, CS2 has to be relaunched to see a
# change.
#
#   ./scripts/hud-watch.sh           watch, and remove the files again on exit
#   ./scripts/hud-watch.sh --keep    watch, and leave them installed on exit
#   ./scripts/hud-watch.sh --plain   watch, but never hand the plugin a tag
#   ./scripts/hud-watch.sh clean     remove whatever an earlier run installed
#
# CS2_DIR is the install root; the default is the game PC's share.
#
# A mounted workshop addon wins over a loose file at the same path. On a server
# that serves the published HUD addon (HUD_WORKSHOP_ID set), the client only
# draws these under names the addon does not contain, which is why a layout that
# changes after a publish takes a new name (see HudSlots.cs).
#
# gameinfo.gi is never written. game/csgo is already on the stock search path,
# so loose files need no override, and an edited gameinfo.gi puts the client in
# untrusted mode. A copy of the stock file is kept beside it as
# gameinfo.gi.original, so the original is at hand on the game PC itself; that
# copy is the one thing this leaves behind on exit.
set -euo pipefail

REPO="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
SRC="${REPO}/apps/utility-sw/hud/dist/panorama"
SOURCES="${REPO}/apps/utility-sw/hud/panorama"

CS2_DIR="${CS2_DIR:-/Volumes/Counter-Strike Global Offensive}"
CS2_DIR="${CS2_DIR%/}"
GAME="${CS2_DIR}/game/csgo"
GAMEINFO="${GAME}/gameinfo.gi"
ORIGINAL="${GAMEINFO}.original"
DEST="${GAME}/panorama"
# On the install rather than in the checkout, so `clean` works from any worktree.
MANIFEST="${DEST}/.5stack-hud-dev"
LIVE_TAG="${REPO}/apps/utility-sw/hud/dist/hud-live-tag"

BACKUPS="${HOME}/.5stack/cs2-gameinfo"
STOCK_URL="https://raw.githubusercontent.com/SteamDatabase/GameTracking-CS2/master/game/csgo/gameinfo.gi"

POD_BUILD_TIMEOUT=45

log() { echo "[hud-watch $(date '+%H:%M:%S')] $*"; }
warn() { log "WARNING: $*" >&2; }
die() { log "ERROR: $*" >&2; exit 1; }

keep=false
plain=false
mode="watch"

for arg in "$@"; do
  case "$arg" in
    --keep) keep=true ;;
    --plain) plain=true ;;
    clean) mode="clean" ;;
    *)
      echo "usage: $0 [--keep] [--plain] | clean" >&2
      exit 1
      ;;
  esac
done

if [ -n "${CODEPIER_DEPLOYMENT:-}" ]; then
  echo "this runs on the machine with the CS2 install, not inside the pod" >&2
  exit 1
fi

# A share that dropped can leave its directory behind, so under /Volumes the
# path has to be a live mount point and not merely exist.
is_mount_point() {
  local mounts
  mounts="$(mount)"

  case "$mounts" in
    *" on ${CS2_DIR} ("*) return 0 ;;
    *) return 1 ;;
  esac
}

installed_at_cs2_dir() {
  case "$CS2_DIR" in
    /Volumes/*) is_mount_point || return 1 ;;
  esac

  [ -f "$GAMEINFO" ] && [ -f "${GAME}/steam.inf" ]
}

require_install() {
  case "$CS2_DIR" in
    /Volumes/*)
      is_mount_point || die "${CS2_DIR} is not mounted: connect to the game PC's CS2 share, or set CS2_DIR"
      ;;
  esac

  installed_at_cs2_dir || die "${CS2_DIR} is not a CS2 install: no game/csgo/gameinfo.gi"
}

# Only called once the live file is known to be Valve's, so the copy beside it
# is never an edited one, and it follows the live file across a CS2 update
# instead of going stale.
keep_original_beside() {
  if [ -f "$ORIGINAL" ] && cmp -s "$GAMEINFO" "$ORIGINAL"; then
    return 0
  fi

  if cat "$GAMEINFO" > "$ORIGINAL"; then
    log "gameinfo.gi: original copied to ${ORIGINAL}"
  else
    warn "could not write ${ORIGINAL}"
  fi
}

# Never writes gameinfo.gi itself. Keeps one copy per distinct version it has
# seen and says whether the live one is Valve's, since an edited one is the
# usual reason a client suddenly cannot join anything.
check_gameinfo() {
  local sha short fetched verdict
  sha="$(shasum -a 256 "$GAMEINFO" | cut -d' ' -f1)"
  short="${sha:0:12}"
  mkdir -p "$BACKUPS"

  if [ -f "${BACKUPS}/stock-${short}.gi" ]; then
    keep_original_beside
    log "gameinfo.gi: ${short}, Valve stock (original beside it and in ${BACKUPS})"
    return 0
  fi

  # Only reached for bytes not verified before: a CS2 update, or an edit.
  fetched="$(mktemp -t hud-watch-gameinfo)"

  if ! curl -fsS --max-time 5 "$STOCK_URL" -o "$fetched" 2>/dev/null; then
    verdict="unchecked"
  elif cmp -s <(tr -d '\r' < "$fetched") <(tr -d '\r' < "$GAMEINFO"); then
    verdict="stock"
  else
    verdict="differs"
  fi

  rm -f "$fetched"

  if [ "$verdict" = "stock" ]; then
    cp "$GAMEINFO" "${BACKUPS}/stock-${short}.gi"
    rm -f "${BACKUPS}/unverified-${short}.gi"
    keep_original_beside
    log "gameinfo.gi: ${short}, Valve stock (original beside it and in ${BACKUPS})"
    return 0
  fi

  [ -f "${BACKUPS}/unverified-${short}.gi" ] || cp "$GAMEINFO" "${BACKUPS}/unverified-${short}.gi"

  if [ "$verdict" = "differs" ]; then
    warn "gameinfo.gi ${short} is not Valve's tracked stock: a CS2 update newer than the tracker, or a local edit"
    warn "if it was edited, CS2 runs untrusted: Steam > Verify integrity restores it, or copy ${ORIGINAL##*/} back over it"
  else
    warn "gameinfo.gi ${short} could not be compared against Valve's stock (copy kept in ${BACKUPS})"
  fi
}

# The only paths this ever writes or deletes. Everything else under panorama/
# (fonts, videos) belongs to the install.
in_scope() {
  case "$1" in
    *..*) return 1 ;;
    */custom_game/*_c) return 0 ;;
    *) return 1 ;;
  esac
}

# listed <path> <newline-separated list>
listed() {
  case $'\n'"$2"$'\n' in
    *$'\n'"$1"$'\n'*) return 0 ;;
    *) return 1 ;;
  esac
}

# rmdir only ever takes an empty directory, so this stops at the first one that
# still holds something of the install's.
prune_dirs() {
  local dir
  dir="$(dirname "$1")"

  while [ "$dir" != "." ] && [ "$dir" != "/" ]; do
    rmdir "${DEST}/${dir}" 2>/dev/null || return 0
    dir="$(dirname "$dir")"
  done
}

fingerprint() {
  [ -d "$1" ] || return 0
  { find "$1" -type f -exec cksum {} + 2>/dev/null || true; } | sort
}

announced="unset"

# dev.sh relays this file to the plugin, which then asks clients for that
# build's names. Emptied, never deleted: a file missing here but still in the
# pod would be copied back by the sync.
set_live_tag() {
  if [ "$1" = "$announced" ] || [ ! -d "${LIVE_TAG%/*}" ]; then
    return 0
  fi

  if printf '%s' "$1" > "$LIVE_TAG"; then
    announced="$1"
  else
    warn "could not write ${LIVE_TAG#"${REPO}/"}"
  fi
}

# The tag of the build now on the install, read off the names (stem__tag.ext).
# The newest wins when a sync catches two builds side by side.
installed_tag() {
  local rel stem candidate tag=""

  while IFS= read -r rel; do
    stem="${rel##*/}"
    stem="${stem%.*}"

    case "$stem" in
      *__*) candidate="${stem##*__}" ;;
      *) continue ;;
    esac

    case "$candidate" in
      "" | *[!a-z0-9]*) continue ;;
    esac

    if [[ "$candidate" > "$tag" ]]; then
      tag="$candidate"
    fi
  done <<< "$installed"

  echo "$tag"
}

# One pass of hud/dist/panorama onto the install. Reports through $copied,
# $removed and $installed, and fails if anything could not be written so the
# caller retries.
sync_tree() {
  local file rel dest owned new="" failed=0
  copied=0
  removed=0
  owned="$(cat "$MANIFEST" 2>/dev/null || true)"

  while IFS= read -r file; do
    rel="${file#"${SRC}/"}"
    dest="${DEST}/${rel}"

    if ! in_scope "$rel"; then
      warn "skipping ${rel}: outside custom_game/"
      continue
    fi

    if [ -e "$dest" ] && ! listed "$rel" "$owned"; then
      warn "skipping ${rel}: a file this script did not install is already there"
      continue
    fi

    new="${new}${rel}"$'\n'

    if [ -f "$dest" ] && cmp -s "$file" "$dest"; then
      continue
    fi

    # Recorded before the copy, so a run that dies mid-copy still owns the file.
    # cat, not cp: bytes only, without the Mac's extended attributes.
    if ! {
      mkdir -p "$(dirname "$dest")" \
        && { listed "$rel" "$owned" || echo "$rel" >> "$MANIFEST"; } \
        && cat "$file" > "$dest"
    }; then
      warn "could not write ${rel}"
      failed=1
      continue
    fi

    copied=$((copied + 1))
    log "copied ${rel} ($(($(wc -c < "$file"))) bytes)"
  done < <(find "$SRC" -type f -name '*_c' | sort)

  while IFS= read -r rel; do
    [ -n "$rel" ] || continue
    listed "$rel" "$new" && continue
    in_scope "$rel" || continue

    if rm -f "${DEST}/${rel}"; then
      prune_dirs "$rel"
      removed=$((removed + 1))
      log "removed ${rel}"
    else
      new="${new}${rel}"$'\n'
      failed=1
    fi
  done <<< "$owned"

  if [ -n "$new" ]; then
    printf '%s' "$new" > "$MANIFEST" || failed=1
  else
    rm -f "$MANIFEST"
  fi

  installed="$new"

  return "$failed"
}

uninstall() {
  local rel count=0

  if [ ! -f "$MANIFEST" ]; then
    log "no dev HUD files installed"
    return 0
  fi

  while IFS= read -r rel; do
    in_scope "$rel" || continue
    rm -f "${DEST}/${rel}"
    prune_dirs "$rel"
    count=$((count + 1))
  done < "$MANIFEST"

  rm -f "$MANIFEST"
  set_live_tag ""
  log "removed ${count} dev HUD file(s) from ${DEST}"
}

# A share that drops while this runs does not come back by itself, and a watch
# left waiting on it quietly is one nobody notices has stopped working.
volume_gone() {
  if command -v osascript >/dev/null 2>&1; then
    osascript -e 'display notification "Reconnect the CS2 share, then run hud-watch again" with title "hud-watch stopped"' \
      >/dev/null 2>&1 || true
  fi

  die "${CS2_DIR} went away: reconnect the game PC's CS2 share, then run $0 again"
}

on_exit() {
  trap - EXIT INT TERM
  set +e
  echo

  if $keep; then
    log "--keep: dev HUD files left installed, '$0 clean' removes them"
  elif installed_at_cs2_dir; then
    uninstall
  else
    warn "${CS2_DIR} is gone, so anything installed is still there: '$0 clean' removes it once mounted"
  fi

  log "take -insecure back out of CS2's launch options before anything VAC-secured"
}

require_install

if [ "$mode" = "clean" ]; then
  uninstall
  exit 0
fi

log "install:     ${CS2_DIR}"
check_gameinfo
log "watching:    ${SRC#"${REPO}/"}"
# Dev servers run -insecure (k8s/dev-swiftly-game-server.yaml), and Steam's
# launch options are not on the share, so this can only be said.
log "launch CS2 with -insecure to join the dev server"
log "if the server serves the published HUD addon, names it contains load from the addon, not from here"

if [ -f "$MANIFEST" ]; then
  log "taking over $(($(wc -l < "$MANIFEST"))) file(s) an earlier run left installed"
fi

trap on_exit EXIT
trap 'exit 130' INT
trap 'exit 143' TERM

synced=""
previous=""
sources_seen="$(fingerprint "$SOURCES")"
waiting_since=0
announced_empty=false

while true; do
  installed_at_cs2_dir || volume_gone

  current="$(fingerprint "$SRC")"
  sources_now="$(fingerprint "$SOURCES")"

  if [ "$sources_now" != "$sources_seen" ]; then
    sources_seen="$sources_now"
    waiting_since="$(date +%s)"
    log "sources changed, waiting for the pod build"
  fi

  if [ -z "$current" ]; then
    if ! $announced_empty; then
      log "nothing in ${SRC#"${REPO}/"} yet: is ./scripts/dev.sh running in the utility pod?"
      announced_empty=true
    fi
  # Settled across two polls, so a sync landing file by file is copied once.
  elif [ "$current" != "$synced" ] && [ "$current" = "$previous" ]; then
    if sync_tree; then
      synced="$current"
      waiting_since=0

      if $plain; then
        set_live_tag ""
      else
        set_live_tag "$(installed_tag)"
      fi

      if [ "$copied" -gt 0 ] || [ "$removed" -gt 0 ]; then
        if [ -n "$announced" ]; then
          log "${copied} copied, ${removed} removed: build ${announced} handed to the plugin"
        else
          log "${copied} copied, ${removed} removed: relaunch CS2 (-insecure) to load it"
        fi
      else
        log "install already matches the build"
      fi
    else
      # A copy that failed because the share dropped mid-way says so here,
      # not five seconds and another failed copy later.
      installed_at_cs2_dir || volume_gone
      sleep 5
    fi
  fi

  if [ "$waiting_since" -gt 0 ] && [ $(($(date +%s) - waiting_since)) -ge "$POD_BUILD_TIMEOUT" ]; then
    warn "no new build ${POD_BUILD_TIMEOUT}s after the edit: check the codepier shell (/tmp/hud-build.log in the pod)"
    waiting_since=0
  fi

  previous="$current"
  sleep 1
done
