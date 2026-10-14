#!/bin/bash
# Regression tests for the plugin install flags in each image's setup.sh.
# Run: bash shared/scripts/test/setup-flags.test.sh

set -u

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_DIR="$(cd "${SCRIPT_DIR}/../../.." && pwd)"

FLAGS=(
  AUTOLOAD_PLUGINS
  INSTALL_5STACK_PLUGIN
  INSTALL_PLAYER_MANAGEMENT_PLUGIN
  INSTALL_UTILITY_PRACTICE_PLUGIN
)

failures=0

fail() {
  echo "  FAIL: $1" >&2
  failures=$((failures + 1))
}

guards() {
  grep -E "^if .*\b$2\b.*then[[:space:]]*$" "$1"
}

# Runs one guard from setup.sh on its own, in an otherwise empty environment.
# Its output is capped because a guard that runs the flag's value as a command
# runs `yes` forever, which is the hang this exists to catch.
run_guard() {
  local guard="$1" flag="$2" value="$3" err
  err="$(mktemp)"

  if [ "$value" = "<unset>" ]; then
    decision="$(env -i PATH="$PATH" bash -c "$guard echo taken; else echo skipped; fi" \
      < /dev/null 2> "$err" | head -c 64)"
  else
    decision="$(env -i PATH="$PATH" "$flag=$value" bash -c "$guard echo taken; else echo skipped; fi" \
      < /dev/null 2> "$err" | head -c 64)"
  fi

  stderr="$(cat "$err")"
  rm -f "$err"
}

check() {
  local setup="$1" flag="$2" value="$3" expected="$4" guard

  while IFS= read -r guard; do
    run_guard "$guard" "$flag" "$value"

    if [ "$decision" != "$expected" ]; then
      fail "${setup#"$REPO_DIR"/} $flag='$value': expected $expected, got '$(printf '%s' "$decision" | tr '\n' ' ')'"
    fi

    if [ -n "$stderr" ]; then
      fail "${setup#"$REPO_DIR"/} $flag='$value' ran the value as a command: $stderr"
    fi
  done < <(guards "$setup" "$flag")
}

for setup in "$REPO_DIR/apps/counterstrikesharp/scripts/setup.sh" "$REPO_DIR/apps/swiftly/scripts/setup.sh"; do
  for flag in "${FLAGS[@]}"; do
    echo "${setup#"$REPO_DIR"/} $flag installs only on true"

    # Without this a guard rewritten into a shape the grep misses would pass
    # every check below by never being run.
    if [ -z "$(guards "$setup" "$flag")" ]; then
      fail "${setup#"$REPO_DIR"/} has no guard for $flag"
    fi

    # Only the literal "true" installs, as with every other flag these scripts
    # read. The images' ENV sets each flag, so unset only means a blanked one.
    check "$setup" "$flag" "true" "taken"
    check "$setup" "$flag" "false" "skipped"
    check "$setup" "$flag" "<unset>" "skipped"
    check "$setup" "$flag" "" "skipped"
    check "$setup" "$flag" "1" "skipped"
    check "$setup" "$flag" "yes" "skipped"
  done
done

if [ "$failures" -gt 0 ]; then
  echo "$failures assertion(s) failed" >&2
  exit 1
fi

echo "all setup.sh flag tests passed"
