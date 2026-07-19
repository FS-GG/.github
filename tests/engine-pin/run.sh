#!/usr/bin/env bash
# Fixture for scripts/check-engine-pin.py — the gate that asks whether the version the FLEET restores
# (the dist tool-manifest pin) is the newest engine the org has PUBLISHED (the org feed). .github#1196,
# epic #266.
#
# The gate exists because that pin is a scalar no other coherence gate looked at: source/feed/pin
# coherence and engine-freshness all stayed GREEN while the fleet ran a three-releases-stale engine.
# So this fixture spends most of its length on the FAILURE legs: it proves the gate reds when the pin
# is BEHIND or AHEAD of the feed's newest stable version, and ERRORS — never "in sync" — when the
# manifest or the pin it measures is missing/malformed, or the feed is unreadable.
#
# Every negative leg asserts the REASON, not just a non-zero exit — the .github#266 vacuous-failure
# defect (SDD#299) was a "must fail" test whose non-zero exit came from a path guard rather than from
# the thing under test. `must_fail` therefore takes a required pattern.
#
# No network: the gate's --fixture serves a canned feed. Throwaway files under a temp dir. Mirrors
# tests/engine-freshness/run.sh and tests/feed-coherence/run.sh.

set -euo pipefail

# The suite runs the gate by path, which would otherwise litter scripts/__pycache__ into a repo that
# has no .gitignore.
export PYTHONDONTWRITEBYTECODE=1

# `--fixture` is locked to this harness: the gate refuses a canned feed unless this is set, so a stray
# `--fixture` in CI fails rather than silently reporting green. See the gate's docstring.
export FSGG_ENGINE_PIN_FIXTURE_OK=1

HERE="$(cd "$(dirname "$0")" && pwd)"
GATE="$HERE/../../scripts/check-engine-pin.py"

WORK="$(mktemp -d "${TMPDIR:-/tmp}/engine-pin-fixture.XXXXXX")"
trap 'rm -rf "$WORK"' EXIT

pass=0; failcount=0
ok()  { echo "PASS  $1"; pass=$((pass+1)); }
bad() { echo "FAIL  $1"; [ -n "${2:-}" ] && printf '%s\n' "$2" | sed 's/^/    | /'; failcount=$((failcount+1)); }

# Emit a dotnet-tools.json manifest. $1 = file, $2 = fs.gg.coord.cli version (omit for "no version"),
# and it always carries a second tool so "has tools, lacks OUR pin" is distinguishable from "no tools".
manifest() { # $1 = file, $2 = version (optional)
  local f="$1" v="${2-__ABSENT__}"
  if [ "$v" = "__ABSENT__" ]; then
    cat > "$f" <<'JSON'
{ "version": 1, "isRoot": true,
  "tools": { "fake-cli": { "version": "6.1.4", "commands": ["fake"] } } }
JSON
  else
    cat > "$f" <<JSON
{ "version": 1, "isRoot": true,
  "tools": {
    "fake-cli": { "version": "6.1.4", "commands": ["fake"] },
    "fs.gg.coord.cli": { "version": "$v", "commands": ["fsgg-coord-engine"] }
  } }
JSON
  fi
}

feed() { # $1 = file, $2... = versions
  local f="$1"; shift
  local vs=""
  for v in "$@"; do vs="$vs\"$v\","; done
  printf '{"FS.GG.Coord.Cli": [%s]}' "${vs%,}" > "$f"
}

run() { # $1 = manifest, $2 = feed json  -> stdout+stderr, exit code in $rc
  set +e
  out="$(python3 "$GATE" --manifest "$1" --fixture "$2" 2>&1)"
  rc=$?
  set -e
}

must_pass() { # $1 = label, $2 = required stdout pattern
  if [ "$rc" -ne 0 ]; then bad "$1 (expected exit 0, got $rc)" "$out"; return; fi
  if ! grep -q -- "$2" <<<"$out"; then bad "$1 (exit 0 but did not say: $2)" "$out"; return; fi
  ok "$1"
}

must_fail() { # $1 = label, $2 = required reason pattern
  if [ "$rc" -eq 0 ]; then bad "$1 (expected non-zero, got 0)" "$out"; return; fi
  if ! grep -q -- "$2" <<<"$out"; then bad "$1 (failed, but not for the stated reason: $2)" "$out"; return; fi
  ok "$1"
}

echo "== check-engine-pin fixture =="

# ---------------------------------------------------------------------------------------------
# 1. GREEN: the pin equals the newest stable version on the feed.
# ---------------------------------------------------------------------------------------------
M="$WORK/m-ok.json";  manifest "$M" "0.6.0"
F="$WORK/f-ok.json";  feed "$F" 0.3.0 0.4.0 0.5.0 0.6.0
run "$M" "$F"
must_pass "pin == feed-newest is IN SYNC" "the fleet's fs.gg.coord.cli pin is 0.6.0"

# ---------------------------------------------------------------------------------------------
# 2. THE ACCEPTANCE CASE: the pin is BEHIND the feed (the .github#1196 state exactly).
# ---------------------------------------------------------------------------------------------
M="$WORK/m-behind.json"; manifest "$M" "0.3.0"
F="$WORK/f-behind.json"; feed "$F" 0.3.0 0.4.0 0.5.0 0.6.0
run "$M" "$F"
must_fail "a pin BEHIND the feed is RED" "engine pin is BEHIND"
# The message must name the remedy, not merely the fault: a gate that reds without naming the fix is
# one the next worker routes around.
if grep -q 'Bump the pin' <<<"$out" && grep -q 'renovate/fs.gg.coord.cli' <<<"$out"; then
  ok "the RED names the remedy (bump the pin / merge the Renovate PR)"
else
  bad "the RED names the remedy (bump the pin / merge the Renovate PR)" "$out"
fi

# ---------------------------------------------------------------------------------------------
# 3. The other direction: the pin is AHEAD of the feed — names a version no receiver can restore.
# ---------------------------------------------------------------------------------------------
M="$WORK/m-ahead.json"; manifest "$M" "0.7.0"
F="$WORK/f-ahead.json"; feed "$F" 0.5.0 0.6.0
run "$M" "$F"
must_fail "a pin AHEAD of the feed is RED" "engine pin is AHEAD"

# ---------------------------------------------------------------------------------------------
# 4. A STRAY PRERELEASE on the feed is NOT the comparison point (release-coord-engine ships no
#    prerelease). Newest STABLE is 0.6.0, so a 0.6.0 pin is IN SYNC despite a later 0.7.0-preview.1.
#    This is the publish-before-flip-safe behaviour: the feed, filtered to stable, is ground truth.
# ---------------------------------------------------------------------------------------------
M="$WORK/m-pre.json"; manifest "$M" "0.6.0"
F="$WORK/f-pre.json"; feed "$F" 0.5.0 0.6.0 0.7.0-preview.1
run "$M" "$F"
must_pass "a stray prerelease is not the comparison point" "pin is 0.6.0"

# ...and a PRERELEASE pin is BEHIND its own stable release (NuGet order, not substring).
M="$WORK/m-prepin.json"; manifest "$M" "0.6.0-preview.1"
F="$WORK/f-prepin.json"; feed "$F" 0.6.0
run "$M" "$F"
must_fail "a prerelease pin is BEHIND its stable release" "engine pin is BEHIND"

# ---------------------------------------------------------------------------------------------
# 5. FAIL CLOSED — the manifest is missing / not JSON / has no tools object.
# ---------------------------------------------------------------------------------------------
F="$WORK/f-fc.json"; feed "$F" 0.6.0
run "$WORK/does-not-exist.json" "$F"
must_fail "a missing manifest is an ERROR" "cannot read the tool manifest"

echo "{ not json" > "$WORK/m-badjson.json"
run "$WORK/m-badjson.json" "$F"
must_fail "a non-JSON manifest is an ERROR" "not valid JSON"

echo '{ "version": 1 }' > "$WORK/m-notools.json"
run "$WORK/m-notools.json" "$F"
must_fail "a manifest with no tools object is an ERROR" "no .tools. object"

# ---------------------------------------------------------------------------------------------
# 6. FAIL CLOSED — the pin this gate measures is ABSENT (has tools, but not ours), or has no version.
# ---------------------------------------------------------------------------------------------
M="$WORK/m-nopin.json"; manifest "$M"          # no fs.gg.coord.cli tool at all
run "$M" "$F"
must_fail "a manifest with no fs.gg.coord.cli pin is an ERROR" "declares no 'fs.gg.coord.cli' tool"

cat > "$WORK/m-noversion.json" <<'JSON'
{ "version": 1, "isRoot": true,
  "tools": { "fs.gg.coord.cli": { "commands": ["fsgg-coord-engine"] } } }
JSON
run "$WORK/m-noversion.json" "$F"
must_fail "a pin with no version string is an ERROR" "carries no string .version"

# ---------------------------------------------------------------------------------------------
# 7. FAIL CLOSED — the feed has no such package / zero versions / only prereleases / unreadable.
# ---------------------------------------------------------------------------------------------
M="$WORK/m-good.json"; manifest "$M" "0.6.0"
printf '{"Some.Other.Package": ["1.0.0"]}' > "$WORK/f-absent.json"
run "$M" "$WORK/f-absent.json"
must_fail "a package absent from the feed is an ERROR" "not on the org feed"

printf '{"FS.GG.Coord.Cli": []}' > "$WORK/f-empty.json"
run "$M" "$WORK/f-empty.json"
must_fail "a feed serving zero versions is an ERROR" "zero versions"

F="$WORK/f-onlypre.json"; feed "$F" 0.6.0-preview.1
run "$M" "$F"
must_fail "a feed with only prereleases is an ERROR" "no stable version"

run "$M" "$WORK/does-not-exist-feed.json"
must_fail "an unreadable fixture is an ERROR" "cannot read fixture"

# ---------------------------------------------------------------------------------------------
# 8. THE FIXTURE HOOK IS LOCKED, and the live path with no token ERRORS rather than skipping.
# ---------------------------------------------------------------------------------------------
M="$WORK/m-lock.json"; manifest "$M" "0.6.0"
F="$WORK/f-lock.json"; feed "$F" 0.6.0
set +e
out="$(env -u FSGG_ENGINE_PIN_FIXTURE_OK python3 "$GATE" --manifest "$M" --fixture "$F" 2>&1)"; rc=$?
set -e
must_fail "--fixture is REFUSED without the harness opt-in" "Refusing to run"

set +e
out="$(env -u GITHUB_TOKEN -u GH_TOKEN python3 "$GATE" --manifest "$M" 2>&1)"; rc=$?
set -e
must_fail "a missing token is an ERROR, not a skip" "not skip it"

echo
echo "engine-pin fixture: $pass passed, $failcount failed"
[ "$failcount" -eq 0 ] || exit 1
