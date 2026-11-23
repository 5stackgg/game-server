#!/bin/bash
# Tests for how update.sh installs, updates and pins a game.
# Run: bash shared/scripts/test/update.test.sh

set -u

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_DIR="$(cd "${SCRIPT_DIR}/../../.." && pwd)"
UPDATE_SH="${UPDATE_SH:-$REPO_DIR/shared/scripts/update.sh}"

failures=0

fail() {
  echo "  FAIL: $1" >&2
  failures=$((failures + 1))
}

root="$(mktemp -d)"
trap 'rm -rf "$root"' EXIT

mkdir -p "$root/bin"
cat > "$root/bin/df" <<'EOF'
#!/bin/bash
echo "Filesystem 1024-blocks Used Available Capacity Mounted on"
echo "/dev/sda1 104857600 92274688 12582912 89% /"
EOF
chmod +x "$root/bin/df"

# A fresh node, its steamcmd stubbed: every call is recorded with whether
# Steam's manifest was there, and an app_update prints what $STEAM_REPLY names
# and installs $STEAM_BUILD.
setup() {
  work="$root/$1"
  mkdir -p "$work/steamcmd" "$work/serverfiles/game/csgo" "$work/serverfiles/steamapps"
  echo vpk > "$work/serverfiles/game/csgo/pak01_000.vpk"
  echo manifest > "$work/serverfiles/steamapps/appmanifest_730.acf"
  cat > "$work/steamcmd/steamcmd.sh" <<'EOF'
#!/bin/bash
calls="$(dirname "$0")/calls"
echo "$*" >> "$calls"
case "$*" in
  *app_update*)
    acf="${BASE_SERVER_DIR}/steamapps/appmanifest_${GAME_ID}.acf"
    [ -f "$acf" ] && echo "manifest present" >> "$calls" || echo "manifest missing" >> "$calls"
    case "${STEAM_REPLY:-ok}" in
      ok)
        mkdir -p "$(dirname "$acf")"
        printf '"AppState"\n{\n\t"buildid"\t\t"%s"\n}\n' "${STEAM_BUILD:-26798866}" > "$acf"
        echo "Success! App '${GAME_ID}' fully installed."
        ;;
      stale)
        # Steam refuses to patch from a build it no longer lists, but checks
        # the files of an install it has no manifest for.
        if [ -f "$acf" ]; then
          echo "Error! App '${GAME_ID}' state is 0x6 after update job."
        else
          mkdir -p "$(dirname "$acf")"
          printf '"AppState"\n{\n\t"buildid"\t\t"%s"\n}\n' "${STEAM_BUILD:-26798866}" > "$acf"
          echo "Success! App '${GAME_ID}' fully installed."
        fi
        ;;
      disk) echo "Error! App '${GAME_ID}' state is 0x202 after update job." ;;
      fail) echo "Error! App '${GAME_ID}' state is 0x602 after update job." ;;
      unterminated)
        mkdir -p "$(dirname "$acf")"
        printf '"AppState"\n{\n\t"buildid"\t\t"%s"\n}\n' "${STEAM_BUILD:-26798866}" > "$acf"
        printf "Success! App '%s' fully installed." "${GAME_ID}"
        ;;
    esac
    ;;
esac
EOF
  chmod +x "$work/steamcmd/steamcmd.sh"
}

run() {
  env -i PATH="$root/bin:$PATH" \
    STEAMCMD_DIR="$work/steamcmd" \
    BASE_SERVER_DIR="$work/serverfiles" \
    GAME_ID="${GAME_ID:-730}" \
    VALIDATE="${VALIDATE:-false}" \
    STEAM_USER=anonymous STEAM_PASSWORD="" \
    STEAM_REPLY="${STEAM_REPLY:-ok}" \
    STEAM_BUILD="${STEAM_BUILD:-26798866}" \
    "$@" bash "$UPDATE_SH" > "$work/out" 2>&1
  echo $? > "$work/exit"
}

app_update() {
  grep "app_update" "$work/steamcmd/calls" 2>/dev/null
}

manifest_at_update() {
  grep -E "^manifest (present|missing)$" "$work/steamcmd/calls" 2>/dev/null
}

echo "an update keeps the install and Steam's manifest, so only changed files download"
setup keep
run
[ "$(cat "$work/exit")" = "0" ] || fail "update exited $(cat "$work/exit")"
[ "$(manifest_at_update)" = "manifest present" ] || fail "appmanifest_730.acf was deleted before steamcmd ran"
[ -f "$work/serverfiles/game/csgo/pak01_000.vpk" ] || fail "the install was deleted"

echo "an unpinned update names the public branch, so it leaves a pin Steam remembers"
app_update | grep -q -- "-beta public" || fail "app_update without -beta public: $(app_update)"

echo "VALIDATE=false does not validate every file"
app_update | grep -qw validate && fail "validate passed with VALIDATE=false"

echo "VALIDATE=true validates"
setup check
VALIDATE=true run
app_update | grep -qw validate || fail "validate not passed with VALIDATE=true"

echo "a pin updates the same install in place through its Steam branch"
setup pin
STEAM_BUILD=25218825 run BUILD_ID=25218825 BUILD_BRANCH=1.41.8.1 BUILD_MANIFESTS='[{"gid":"2053759441494650084","depotId":"2347770"}]'
[ "$(cat "$work/exit")" = "0" ] || fail "pinned update exited $(cat "$work/exit")"
app_update | grep -q -- "-beta 1.41.8.1" || fail "app_update without -beta 1.41.8.1: $(app_update)"
app_update | grep -q -- "+force_install_dir $work/serverfiles" || fail "pin not installed into serverfiles"
grep -q "download_depot" "$work/steamcmd/calls" && fail "pin still downloads depots beside the install"
[ -f "$work/serverfiles/game/csgo/pak01_000.vpk" ] || fail "pinning deleted the install"
[ "$(manifest_at_update)" = "manifest present" ] || fail "pinning deleted Steam's manifest"

echo "a pin fails when its branch installs some other build"
setup moved
STEAM_BUILD=26798866 run BUILD_ID=25218825 BUILD_BRANCH=public
[ "$(cat "$work/exit")" = "1" ] || fail "pin on the wrong build exited $(cat "$work/exit")"
last="$(tail -n 1 "$work/out")"
[ "$last" = "Steam branch public installed build 26798866, not pinned build 25218825. Exiting." ] \
  || fail "last line: $last"

echo "a pin without its branch stops instead of installing the latest build"
setup nobranch
run BUILD_ID=25218825
[ "$(cat "$work/exit")" = "1" ] || fail "pin without a branch exited $(cat "$work/exit")"
[ -z "$(app_update)" ] || fail "pin without a branch still ran app_update"

echo "an install pinned the old way drops its stub manifest and depot copy, and keeps its files"
setup legacy
echo 25218825 > "$work/serverfiles/.5stack.build"
mkdir -p "$work/steamcmd/linux32/steamapps/content/app_730/depot_2347770"
echo chunk > "$work/steamcmd/linux32/steamapps/content/app_730/depot_2347770/pak01_000.vpk"
run
[ "$(cat "$work/exit")" = "0" ] || fail "legacy update exited $(cat "$work/exit")"
[ -e "$work/serverfiles/.5stack.build" ] && fail ".5stack.build was kept"
[ "$(manifest_at_update)" = "manifest missing" ] || fail "the stub appmanifest was kept"
[ -e "$work/steamcmd/linux32/steamapps" ] && fail "the old depot copy was kept"
[ -f "$work/serverfiles/game/csgo/pak01_000.vpk" ] || fail "the old pinned install was deleted"

echo "an install on a build Steam no longer lists is checked file by file instead"
setup stale
STEAM_REPLY=stale run
[ "$(cat "$work/exit")" = "0" ] || fail "stale update exited $(cat "$work/exit")"
[ "$(manifest_at_update | tr '\n' ' ')" = "manifest present manifest missing " ] \
  || fail "manifest at each app_update: $(manifest_at_update | tr '\n' ' ')"
[ "$(app_update | wc -l | tr -d ' ')" = "2" ] || fail "app_update ran $(app_update | wc -l | tr -d ' ') times"
[ -f "$work/serverfiles/game/csgo/pak01_000.vpk" ] || fail "the file check deleted the install"

echo "a pin whose old build Steam no longer lists still lands on its build"
setup stalepin
STEAM_REPLY=stale STEAM_BUILD=25218825 run BUILD_ID=25218825 BUILD_BRANCH=1.41.8.1
[ "$(cat "$work/exit")" = "0" ] || fail "stale pinned update exited $(cat "$work/exit")"

echo "no room for the changed files fails with how much space is free"
setup disk
STEAM_REPLY=disk run
[ "$(cat "$work/exit")" = "1" ] || fail "disk-full update exited $(cat "$work/exit")"
last="$(tail -n 1 "$work/out")"
[ "$last" = "Not enough disk space to update CS2 (12 GB free). Free up space on the node, then retry." ] \
  || fail "last line: $last"
[ -f "$work/serverfiles/game/csgo/pak01_000.vpk" ] || fail "a disk-full update deleted the install"

echo "CS:GO names itself when it runs out of room"
setup csgo
GAME_ID=740 STEAM_REPLY=disk run
tail -n 1 "$work/out" | grep -q "update CS:GO (12 GB free)" || fail "last line: $(tail -n 1 "$work/out")"

echo "steamcmd's last line counts even without a newline"
setup unterminated
STEAM_REPLY=unterminated run
[ "$(cat "$work/exit")" = "0" ] || fail "an unterminated success line exited $(cat "$work/exit")"

echo "any other steamcmd failure fails the job"
setup other
STEAM_REPLY=fail run
[ "$(cat "$work/exit")" = "1" ] || fail "failed update exited $(cat "$work/exit")"
[ "$(app_update | wc -l | tr -d ' ')" = "2" ] || fail "a failing update ran app_update $(app_update | wc -l | tr -d ' ') times, not once plus one file check"
tail -n 1 "$work/out" | grep -q "steamcmd did not finish the CS2 update" || fail "last line: $(tail -n 1 "$work/out")"

if [ "$failures" -gt 0 ]; then
  echo "$failures failure(s)" >&2
  exit 1
fi

echo "all passed"
