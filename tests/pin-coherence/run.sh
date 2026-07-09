#!/usr/bin/env bash
# Fixture for scripts/check-pin-coherence.py — the gate that compares every Renovate-annotated
# version pin against the newest version live on the org feed (.github#263, epic #266).
#
# The gate exists because the FS.GG.SDD.Cli validator pin froze twice — at 0.2.1 across three minors
# (#127), and again at 0.5.0 across three more (#263) — while the registry row asserted `coherent:
# true` on the strength of that pin tracking feed-newest. Both times a human found it, not a gate.
#
# So this fixture spends nearly all its length on the FAILURE legs. It proves the gate goes red when
# the pin is behind the feed, ahead of it, ordered by substring, unbumpable for want of a feed token,
# invisible to the manager's regex, absent, unresolvable, or pointed at a feed that 404s or is empty.
#
# Every negative leg asserts the REASON, not just a non-zero exit — the .github#266 vacuous-failure
# defect (SDD#299) was a "must fail" test whose non-zero exit came from a path guard rather than from
# the thing under test. `must_fail` therefore takes a required pattern, and every mutate helper
# refuses a no-op edit, so a fixture that stops exercising its own claim breaks loudly.
#
# Throwaway git repos under a temp dir, no network (the gate's --fixture flag serves a canned feed).
# Each case builds a real repo because the gate scans `git ls-files` — exactly what Renovate sees.
# Mirrors tests/feed-coherence/run.sh.

set -euo pipefail

# The gate imports scripts/fsgg_feed.py, which would otherwise litter scripts/__pycache__ into a
# repo that has no .gitignore.
export PYTHONDONTWRITEBYTECODE=1

# `--fixture` is locked to this harness: the gate refuses a canned feed unless this is set, so a
# stray `--fixture` in CI fails rather than silently reporting green. See the gate's docstring.
export FSGG_PIN_FIXTURE_OK=1

# The gate must never fall back to a real feed read from a developer's ambient credentials.
unset GITHUB_TOKEN GH_TOKEN || true

HERE="$(cd "$(dirname "$0")" && pwd)"
REPO_ROOT="$(cd "$HERE/../.." && pwd)"
GATE="$REPO_ROOT/scripts/check-pin-coherence.py"

WORK="$(mktemp -d "${TMPDIR:-/tmp}/pin-coherence-fixture.XXXXXX")"
trap 'rm -rf "$WORK"' EXIT

pass=0; failcount=0
ok()  { echo "PASS  $1"; pass=$((pass+1)); }
bad() { echo "FAIL  $1"; [ -n "${2:-}" ] && printf '%s\n' "$2" | sed 's/^/    | /'; failcount=$((failcount+1)); }

# The one path REQUIRED_PINS names. The green baseline must reproduce it exactly, or the gate's
# required-pin assertion is what we would be testing instead of the freshness comparison.
PIN_FILE=".github/workflows/contract-coherence.yml"

# The feed. FS.GG.SDD.Cli deliberately serves BOTH 0.9.0 and 0.9.0-preview.1 (the substring trap),
# listed out of version order (the feed returns creation order, not version order).
FEED="$WORK/feed.json"
cat > "$FEED" <<'JSON'
{
  "FS.GG.SDD.Cli":   ["0.5.0", "0.9.0", "0.9.0-preview.1", "0.8.0", "0.6.0", "0.7.0"],
  "FS.GG.Contracts": ["1.4.0"]
}
JSON

# make_repo <name> [pin-version] — a minimal repo carrying the real org preset, a renovate.json with
# the feed hostRules token, and an annotated FS.GG.SDD.Cli pin at the given version. Echoes its path.
make_repo() {
  local name="$1"
  local version="${2:-0.9.0}"
  local root="$WORK/$name"
  mkdir -p "$root/.github/workflows"

  # The REAL preset — the gate reads the manager's regex from it, so a copy would let this fixture
  # keep passing after someone edits default.json in a way that stops matching the pin.
  cp "$REPO_ROOT/default.json" "$root/default.json"

  cat > "$root/renovate.json" <<'JSON'
{
  "extends": ["github>FS-GG/.github"],
  "hostRules": [
    { "matchHost": "nuget.pkg.github.com", "hostType": "nuget", "token": "{{ secrets.FSGG_PACKAGES_READ_TOKEN }}" }
  ]
}
JSON

  cat > "$root/$PIN_FILE" <<YAML
name: contract-coherence
jobs:
  coherence:
    steps:
      - run: |
          # renovate: datasource=nuget depName=FS.GG.SDD.Cli
          dotnet tool install --global FS.GG.SDD.Cli --version $version
YAML

  git -C "$root" init -q
  git -C "$root" add -A
  printf '%s' "$root"
}

# repin <repo> <new-version> — rewrite the pin literal. Refuses a no-op.
repin() {
  local root="$1" new="$2"
  python3 - "$root/$PIN_FILE" "$new" <<'PY'
import re, sys
path, new = sys.argv[1:3]
text = open(path, encoding="utf-8").read()
m = re.search(r"--version (\S+)", text)
if not m:
    sys.exit("vacuous fixture: no --version literal to rewrite")
if m.group(1) == new:
    sys.exit(f"vacuous fixture: the pin is already {new!r} — this mutation is a no-op")
open(path, "w", encoding="utf-8").write(text[:m.start(1)] + new + text[m.end(1):])
PY
  git -C "$root" add -A
}

# edit_json <file> <python-expr-over-`d`> — mutate a JSON file in place. Refuses a no-op.
edit_json() {
  local path="$1" expr="$2"
  python3 - "$path" "$expr" <<'PY'
import json, sys
path, expr = sys.argv[1:3]
before = open(path, encoding="utf-8").read()
d = json.loads(before)
exec(expr)  # noqa: S102 — fixture-local, mutates `d`
after = json.dumps(d, indent=2)
if json.loads(after) == json.loads(before):
    sys.exit(f"vacuous fixture: {expr!r} changed nothing")
open(path, "w", encoding="utf-8").write(after)
PY
}

# feed_with <name> <json-version-list> — a feed serving those versions for FS.GG.SDD.Cli.
feed_with() {
  local out="$WORK/feed-$1.json"
  python3 -c 'import json,sys; json.dump({"FS.GG.SDD.Cli": json.loads(sys.argv[1])}, open(sys.argv[2],"w"))' "$2" "$out"
  printf '%s' "$out"
}

gate() { python3 "$GATE" --root "$1" --fixture "${2:-$FEED}" 2>&1; }

# must_pass <label> <repo> [feed]
must_pass() {
  local out rc
  out="$(gate "$2" "${3:-$FEED}")" && rc=0 || rc=$?
  if [ "$rc" -eq 0 ]; then ok "$1"; else bad "$1 (expected exit 0, got $rc)" "$out"; fi
}

# must_fail <label> <repo> <feed> <required-pattern>
# The pattern is REQUIRED: exit 1 alone does not prove the gate failed for the reason claimed.
must_fail() {
  local out rc
  out="$(gate "$2" "$3")" && rc=0 || rc=$?
  if [ "$rc" -eq 0 ]; then
    bad "$1 (expected non-zero exit, got 0)" "$out"
  elif printf '%s' "$out" | grep -qF -- "$4"; then
    ok "$1"
  else
    bad "$1 (failed, but not for the stated reason: no match for '$4')" "$out"
  fi
}

echo "--- the green baseline: the pin equals feed-newest ---"
BASE="$(make_repo base 0.9.0)"
must_pass "a pin at feed-newest passes" "$BASE"

echo
echo "--- a frozen pin is caught (the #127 / #263 defect itself) ---"
FROZEN="$(make_repo frozen 0.9.0)"; repin "$FROZEN" 0.5.0
must_fail "pin behind the feed fails" "$FROZEN" "$FEED" "is pinned at '0.5.0' but the org feed's newest is '0.9.0'"
must_fail "...and names the freeze, not a generic mismatch" "$FROZEN" "$FEED" "has frozen"

AHEAD="$(make_repo ahead 0.9.0)"; repin "$AHEAD" 1.0.0
must_fail "pin ahead of the feed fails" "$AHEAD" "$FEED" "AHEAD"

echo
echo "--- versions are compared by ORDER, never by substring (the #268 defect class) ---"
PRE="$(make_repo pre 0.9.0)"; repin "$PRE" 0.9.0-preview.1
must_fail "'0.9.0-preview.1' is not 'equal' to feed-newest '0.9.0'" "$PRE" "$FEED" "has frozen"
must_pass "a release outranks its own prerelease" "$BASE" "$(feed_with onlyrelease '["0.9.0","0.9.0-preview.1"]')"
must_fail "a pin is AHEAD of a feed serving only its prerelease" "$BASE" "$(feed_with onlypre '["0.9.0-preview.1"]')" "AHEAD"

echo
echo "--- the bump MECHANISM is gated, not assumed (the #263 root cause) ---"
NOTOKEN="$(make_repo notoken)"; edit_json "$NOTOKEN/renovate.json" 'del d["hostRules"]'; git -C "$NOTOKEN" add -A
must_fail "renovate.json without the feed hostRules fails" "$NOTOKEN" "$FEED" "declares no \`hostRules\` token for nuget.pkg.github.com"

EMPTYTOK="$(make_repo emptytok)"; edit_json "$EMPTYTOK/renovate.json" 'd["hostRules"][0]["token"] = ""'; git -C "$EMPTYTOK" add -A
must_fail "a hostRules entry with an empty token fails" "$EMPTYTOK" "$FEED" "declares no \`hostRules\` token"

OTHERHOST="$(make_repo otherhost)"; edit_json "$OTHERHOST/renovate.json" 'd["hostRules"][0]["matchHost"] = "nuget.org"'; git -C "$OTHERHOST" add -A
must_fail "a hostRules token for the wrong host fails" "$OTHERHOST" "$FEED" "declares no \`hostRules\` token"

NOCFG="$(make_repo nocfg)"; rm "$NOCFG/renovate.json"; git -C "$NOCFG" add -A
must_fail "a repo with pins and no Renovate config fails" "$NOCFG" "$FEED" "no Renovate configuration"

echo
echo "--- a pin the manager can no longer SEE is a failure, not a shrunken gate ---"
# Cause (1) of #263: the manager's regex stops matching. A gate that scans with that same regex sees
# nothing and would report green on nothing — REQUIRED_PINS is what turns that silence into red.
#
# The repo keeps a SECOND, still-visible pin, so the subject set SHRINKS rather than empties. That is
# the fail-open shape that matters: a repo-wide "zero pins" tripwire would not have caught it, and
# the surviving pin would have reported a cheerful green.
BROKEN="$(make_repo broken)"
cat >> "$BROKEN/$PIN_FILE" <<'YAML'
      - run: |
          # renovate: datasource=nuget depName=FS.GG.Contracts
          dotnet tool install --global FS.GG.Contracts --version 1.4.0
YAML
python3 - "$BROKEN/$PIN_FILE" <<'PY'
import sys
p = sys.argv[1]
t = open(p, encoding="utf-8").read().replace("# renovate: datasource=nuget depName=FS.GG.SDD.Cli", "# (annotation removed)")
open(p, "w", encoding="utf-8").write(t)
PY
git -C "$BROKEN" add -A
must_fail "a required pin gone invisible, while another pin still passes, fails" "$BROKEN" "$FEED" "no longer sees 1 known pin(s): FS.GG.SDD.Cli in .github/workflows/contract-coherence.yml"

GONE="$(make_repo gone)"; rm "$GONE/$PIN_FILE"; git -C "$GONE" add -A
must_fail "zero pins repo-wide fails rather than reporting green" "$GONE" "$FEED" "matched ZERO pins"

echo
echo "--- the manager itself must exist and be readable ---"
NOMGR="$(make_repo nomgr)"; edit_json "$NOMGR/default.json" 'del d["customManagers"]'; git -C "$NOMGR" add -A
must_fail "a preset with no customManagers fails" "$NOMGR" "$FEED" "declares no \`customManagers\`"

NOANNO="$(make_repo noanno)"
edit_json "$NOANNO/default.json" 'd["customManagers"] = [m for m in d["customManagers"] if not any("(?<depName>" in s for s in m.get("matchStrings", []))]'
git -C "$NOANNO" add -A
must_fail "a preset with no annotation-driven manager fails" "$NOANNO" "$FEED" "no annotation-driven custom manager"

BADJSON="$(make_repo badjson)"; printf '{ not json' > "$BADJSON/default.json"; git -C "$BADJSON" add -A
must_fail "an unparsable preset fails" "$BADJSON" "$FEED" "is not valid JSON"

echo
echo "--- an unreadable or unresolvable feed fails closed ---"
must_fail "a package absent from the feed fails (404)" "$BASE" "$(feed_with empty404 '[]')" "zero versions"
NOPKG="$WORK/feed-nopkg.json"; printf '{}' > "$NOPKG"
must_fail "a feed that does not serve the package fails" "$BASE" "$NOPKG" "not on the org feed"

echo
echo "--- a pin this gate cannot resolve is an error, never a skip ---"
NONNUGET="$(make_repo nonnuget)"
python3 - "$NONNUGET/$PIN_FILE" <<'PY'
import sys
p = sys.argv[1]
t = open(p, encoding="utf-8").read().replace("datasource=nuget", "datasource=github-releases")
open(p, "w", encoding="utf-8").write(t)
PY
git -C "$NONNUGET" add -A
must_fail "an unresolvable datasource fails" "$NONNUGET" "$FEED" "is not one this gate can resolve"

THIRDPARTY="$(make_repo thirdparty)"
python3 - "$THIRDPARTY/$PIN_FILE" <<'PY'
import sys
p = sys.argv[1]
t = open(p, encoding="utf-8").read()
t = t.replace("depName=FS.GG.SDD.Cli", "depName=Expecto").replace("--global FS.GG.SDD.Cli", "--global Expecto")
open(p, "w", encoding="utf-8").write(t)
PY
git -C "$THIRDPARTY" add -A
# Two failures are correct here and both matter: the required pin vanished, AND a non-FS.GG package
# cannot be resolved against the org feed. The first fires first; assert it, then assert the second
# in isolation by adding the third-party pin ALONGSIDE the required one.
must_fail "renaming the required pin away fails" "$THIRDPARTY" "$FEED" "no longer sees 1 known pin(s): FS.GG.SDD.Cli in .github/workflows/contract-coherence.yml"

EXTRA="$(make_repo extra)"
cat >> "$EXTRA/$PIN_FILE" <<'YAML'
      - run: |
          # renovate: datasource=nuget depName=Expecto
          dotnet tool install --global Expecto --version 10.2.1
YAML
git -C "$EXTRA" add -A
must_fail "a non-FS.GG package cannot be resolved against the org feed" "$EXTRA" "$FEED" "is not an FS.GG.* package"

echo
echo "--- the gate scans exactly what the bot scans (Renovate's ignorePaths) ---"
# default.json extends config:recommended -> :ignoreModulesAndTests, whose ignorePaths exclude
# **/tests/** and friends. A pin there is one Renovate will never bump, so reddening over it would
# make the gate stricter than the bot. This fixture is itself a `.sh` file matching the manager's
# managerFilePatterns, and its heredocs carry annotation-shaped pins — which is how this was found.
IGNORED="$(make_repo ignored)"
mkdir -p "$IGNORED/tests/fixtures"
cat > "$IGNORED/tests/fixtures/sample.sh" <<'YAML'
# renovate: datasource=nuget depName=Expecto
dotnet tool install --global Expecto --version 10.2.1
YAML
git -C "$IGNORED" add -A
must_pass "a pin under tests/ is skipped, as Renovate skips it" "$IGNORED"

# ...but the skip must never swallow a REQUIRED pin. Move the operative pin into an ignored path and
# leave a second, visible pin behind, so the repo is not merely empty: the zero-pins tripwire must not
# be what saves us here. Only REQUIRED_PINS can catch a pin that was parked somewhere unscanned.
IGNOREDREQ="$(make_repo ignoredreq)"
mkdir -p "$IGNOREDREQ/tests"
mv "$IGNOREDREQ/$PIN_FILE" "$IGNOREDREQ/tests/contract-coherence.yml"
cat > "$IGNOREDREQ/.github/workflows/other.yml" <<'YAML'
      - run: |
          # renovate: datasource=nuget depName=FS.GG.Contracts
          dotnet tool install --global FS.GG.Contracts --version 1.4.0
YAML
git -C "$IGNOREDREQ" add -A
must_fail "an ignored path cannot hide a REQUIRED pin" "$IGNOREDREQ" "$FEED" "no longer sees 1 known pin(s)"

echo
echo "--- fixture mode announces itself, and is locked to this harness ---"
out="$(gate "$BASE")"
printf '%s' "$out" | grep -q "FIXTURE MODE" \
  && ok "fixture mode prints a banner" \
  || bad "fixture mode prints a banner" "$out"

out="$(FSGG_PIN_FIXTURE_OK= python3 "$GATE" --root "$BASE" --fixture "$FEED" 2>&1)" && rc=0 || rc=$?
if [ "${rc:-0}" -ne 0 ] && printf '%s' "$out" | grep -q "Refusing to run"; then
  ok "--fixture refuses to run without FSGG_PIN_FIXTURE_OK"
else
  bad "--fixture refuses to run without FSGG_PIN_FIXTURE_OK" "$out"
fi

echo
echo "--- fails CLOSED on an unreadable feed (no --fixture, no token) ---"
out="$(python3 "$GATE" --root "$BASE" 2>&1)" && rc=0 || rc=$?
if [ "${rc:-0}" -ne 0 ] && printf '%s' "$out" | grep -q "no GITHUB_TOKEN/GH_TOKEN"; then
  ok "a missing token fails the gate rather than skipping it"
else
  bad "a missing token fails the gate rather than skipping it" "$out"
fi

echo
echo "--- CI guard on the real repo (no network: structure only) ---"
# Proves REQUIRED_PINS still names a pin that exists here, and that renovate.json still carries the
# token — the two things a refactor of this repo could quietly break. Feed comparison needs network
# and is covered by the workflow's live run.
out="$(python3 - "$REPO_ROOT" "$GATE" <<'PY' 2>&1
import importlib.util, sys
root, gate_path = sys.argv[1:3]
spec = importlib.util.spec_from_file_location("gate", gate_path)
gate = importlib.util.module_from_spec(spec); spec.loader.exec_module(gate)
gate.check_bump_mechanism(root)
rx, mp = gate.load_annotation_manager(f"{root}/default.json")
pins = gate.scan_pins(root, rx, mp)
gate.assert_required_pins(pins)
assert pins, "no pins found in the real repo"
print(f"ok: {len(pins)} pin(s); every REQUIRED_PINS entry present; hostRules token present")
PY
)" && rc=0 || rc=$?
if [ "${rc:-0}" -eq 0 ]; then ok "real repo: required pins present, bump mechanism configured"; else bad "real repo structure" "$out"; fi

echo
echo "$pass passed, $failcount failed."
[ "$failcount" -eq 0 ]
