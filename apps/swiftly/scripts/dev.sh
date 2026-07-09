#!/bin/bash
#
# Dev hot-reload loop.
#
# Runs inside the codepier "dev" container (dotnet SDK image) with the repo
# synced to /opt/5stack. It builds the plugin and copies the output into
# /opt/dev, which the server pod symlinks in as its FiveStack plugin
# (see scripts/setup.sh). SwiftlyS2 "AutoHotReload" (cfg/core.jsonc) then
# reloads the plugin when FiveStack.dll changes.
#
# Chain: dotnet watch build -> apps/swiftly/src/build/net10.0 -> cp -> /opt/dev -> SwiftlyS2 reload

log() { echo "[dev.sh $(date '+%H:%M:%S')] $*"; }

PROJECT="/opt/5stack/apps/swiftly/src/FiveStack.csproj"
BUILD_OUTPUT="/opt/5stack/apps/swiftly/src/build/net10.0"
DEV_DIR="/opt/dev"

log "starting dev hot-reload"
log "  project:      $PROJECT"
log "  build output: $BUILD_OUTPUT"
log "  dev dir:      $DEV_DIR"

log "installing inotify-tools"
if apt-get update -qq && apt-get install -y -qq inotify-tools; then
  log "inotify-tools ready"
else
  log "ERROR: failed to install inotify-tools (are we root?) - aborting"
  exit 1
fi

mkdir -p "$DEV_DIR"

# Copy the current build output into /opt/dev.
sync_to_dev() {
  if [ ! -d "$BUILD_OUTPUT" ]; then
    log "WARNING: $BUILD_OUTPUT missing, nothing to copy (build failed?)"
    return 1
  fi
  if cp -r "$BUILD_OUTPUT"/. "$DEV_DIR"/ 2>/tmp/dev-cp.err; then
    if [ -f "$DEV_DIR/FiveStack.dll" ]; then
      log "synced -> $DEV_DIR (FiveStack.dll $(stat -c%s "$DEV_DIR/FiveStack.dll" 2>/dev/null || echo '?') bytes)"
    else
      log "WARNING: copied but $DEV_DIR/FiveStack.dll is missing"
    fi
  else
    log "ERROR: copy to $DEV_DIR failed: $(cat /tmp/dev-cp.err)"
    return 1
  fi
}

# Variable to store the PID of dotnet watch process
dotnet_watch_pid=""

# Function to kill the dotnet watch build process
kill_dotnet_watch() {
  if [ -n "$dotnet_watch_pid" ]; then
    log "stopping dotnet watch (pid $dotnet_watch_pid)"
    kill "$dotnet_watch_pid" 2>/dev/null
  fi
}
trap kill_dotnet_watch EXIT

log "running initial build"
if dotnet build "$PROJECT"; then
  log "initial build succeeded"
  sync_to_dev
else
  log "ERROR: initial build FAILED - watch will keep retrying on the next change"
fi

log "starting 'dotnet watch build' in background"
dotnet watch build --project "$PROJECT" &
dotnet_watch_pid=$!
log "dotnet watch pid=$dotnet_watch_pid"

# Wait for the output dir to appear (first successful build) before watching it,
# otherwise inotifywait errors immediately and spins.
until [ -d "$BUILD_OUTPUT" ]; do
  log "waiting for $BUILD_OUTPUT to exist (build in progress / failing)..."
  sleep 2
done

log "watching $BUILD_OUTPUT for changes"
while true; do
  event=$(inotifywait -r -e modify,create,delete,move --format '%e %w%f' "$BUILD_OUTPUT" 2>/dev/null)
  log "change detected: ${event:-<none>}"
  sync_to_dev
done
