#!/usr/bin/env bash
# Fixture for scripts/check-pin-coherence.py — the gate that compares every Renovate-annotated
# version pin against the newest version on THE REGISTRY RENOVATE ACTUALLY READS (#263, #576, #266).
#
# The gate exists because the FS.GG.SDD.Cli validator pin froze FOUR times — 0.2.1 (#127), 0.5.0
# (#263), 0.9.0 (#566), 0.10.0 — while the registry row asserted `coherent: true` on the strength of
# that pin tracking registry-newest. Every time, a human found it, and every time it was closed by
# hand-advancing the literal and chasing a credential that was never the problem: the packages are
# PUBLIC on nuget.org, and the preset was routing them to an auth-required feed instead (#576).
#
# So this fixture spends nearly all its length on the FAILURE legs. It proves the gate goes red when
# the pin is behind the registry, ahead of it, ordered by substring, routed at a host the bot cannot
# read, routed at a host the gate cannot classify, invisible to the manager's regex, absent,
# unresolvable, or pointed at a registry that 404s or is empty — and that it does NOT demand a
# credential for a public route, which is the demand that hid the defect for four rounds.
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
#
# `_renovate` is the canned bot-evidence block (#566). When a pin is stale the gate no longer ASSERTS
# a cause — it reads Renovate's own dashboard + PRs and names the one the evidence supports. Offline,
# that evidence comes from here. The default is the LIVE defect: the bot detected the dependency
# (so the manager's regex is fine) and never opened a bump PR — i.e. it is blind to the feed.
#
# `_nuspecs` is the canned nuspec block for the CAP-trigger leg (#943): {pkg: {version: {dep: range}}}.
# The real default.json carries an `allowedVersions` cap on YoloDev.Expecto.TestSdk, and make_repo
# copies the REAL preset — so every leg here re-checks that cap, and the feed must serve its subject
# or the whole fixture reds for a reason that has nothing to do with the leg under test.
#
# The versions and the range are the REAL ones, read from api.nuget.org: 1.0.0 is the only stable
# version the `<1.0.0` cap excludes, and its nuspec really does say Expecto [9.0.0, 10.0.0) — which
# does NOT admit Expecto 11, so the cap is still justified and the baseline is green. That is what
# makes the red legs below mean something: they flip this one measured fact, nothing else.
FEED="$WORK/feed.json"
cat > "$FEED" <<'JSON'
{
  "FS.GG.SDD.Cli":   ["0.5.0", "0.9.0", "0.9.0-preview.1", "0.8.0", "0.6.0", "0.7.0"],
  "FS.GG.Contracts": ["1.4.0"],
  "YoloDev.Expecto.TestSdk": ["0.15.6", "0.16.0", "1.0.0"],
  "_nuspecs": {
    "YoloDev.Expecto.TestSdk": {
      "0.15.6": { "Expecto": "[10.2.2, 11.0.0)" },
      "0.16.0": { "Expecto": "10.2.3" },
      "1.0.0":  { "Expecto": "[9.0.0, 10.0.0)" }
    }
  },
  "_renovate": { "detected": true, "bump_prs": [], "dashboard": 54 }
}
JSON

# feed_evidence <name> <_renovate-json> — the standard feed, with a different bot-evidence block.
feed_evidence() {
  local out="$WORK/feed-ev-$1.json"
  python3 -c '
import json, sys
d = json.load(open(sys.argv[1]))
d["_renovate"] = json.loads(sys.argv[2])
json.dump(d, open(sys.argv[3], "w"))
' "$FEED" "$2" "$out"
  printf '%s' "$out"
}

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

  # The REAL sync script, for the same reason: it DEFINES the synced set the preset must disable
  # (#794), so a stub here would let every leg pass after the two drifted apart.
  mkdir -p "$root/scripts"
  cp "$REPO_ROOT/scripts/sync-build-config.sh" "$root/scripts/sync-build-config.sh"

  # The REAL roster, for the same reason again: registry/repos.yml owns the kit's `kind: config` rows —
  # the SECOND owner of the synced set since #1077 moved .config/dotnet-tools.json here from build-config.
  # A stub would let the #794 union check pass after the roster and the preset drifted apart.
  mkdir -p "$root/registry"
  cp "$REPO_ROOT/registry/repos.yml" "$root/registry/repos.yml"

  cat > "$root/renovate.json" <<'JSON'
{
  "extends": ["github>FS-GG/.github"],
  "hostRules": [
    { "matchHost": "nuget.pkg.github.com", "hostType": "nuget", "token": "{{ secrets.FSGG_PACKAGES_READ_TOKEN }}" }
  ]
}
JSON

  # `versioning=loose` is the CORRECTED (#1119) state: under it the bare literal is a single, bumpable
  # version. Omitting it — the manager default `nuget` — reads the literal as a `>=` floor that never
  # bumps (#576), which the single-version check below now reds; the `versioning`-omitted leg exercises
  # exactly that. So the green baseline must carry the token, or every leg reds on a scheme it is not
  # about. `bare_pin`/`repin_versioning` below vary this line to test the new assertion.
  cat > "$root/$PIN_FILE" <<YAML
name: contract-coherence
jobs:
  coherence:
    steps:
      - run: |
          # renovate: datasource=nuget depName=FS.GG.SDD.Cli versioning=loose
          dotnet tool install --global FS.GG.SDD.Cli --version $version
YAML

  git -C "$root" init -q
  git -C "$root" add -A
  printf '%s' "$root"
}

# route_to <repo> <registry-url> — repoint the preset's FS.GG.* rule at another registry.
#
# The gate's invariant is no longer "a token is declared for THE feed" — it is "every host the
# preset routes FS.GG.* to is one Renovate can read" (.github#576). So the token legs below need a
# repo that is actually routed at an auth-required host; against the real preset, which now routes
# to public nuget.org, a missing token is not a defect and must not be failed.
route_to() {
  local root="$1" url="$2"
  edit_json "$root/default.json" "
for r in d['packageRules']:
    if any('FS.GG' in str(n) or 'FS\\\\.GG' in str(n) for n in r.get('matchPackageNames', [])) and 'registryUrls' in r:
        r['registryUrls'] = ['$url']
"
  git -C "$root" add -A
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

# set_versioning <repo> <value|""> — rewrite the pin's `# renovate:` annotation to carry
# `versioning=<value>`, or REMOVE the token (→ the manager default, nuget) when <value> is "". Refuses
# a no-op. This is the #576/#1122 subject: the token is what decides single-version vs `>=` floor.
set_versioning() {
  local root="$1" val="$2"
  python3 - "$root/$PIN_FILE" "$val" <<'PY'
import re, sys
path, val = sys.argv[1:3]
text = open(path, encoding="utf-8").read()
m = re.search(r"^(\s*#\s*renovate:.*)$", text, re.MULTILINE)
if not m:
    sys.exit("vacuous fixture: no `# renovate:` annotation line to edit")
line = m.group(1)
without = re.sub(r"\s+versioning=\S+", "", line)
new = without + (f" versioning={val}" if val else "")
if new == line:
    sys.exit(f"vacuous fixture: the annotation is already {'versioning='+val if val else 'versioning-less'} — no-op")
open(path, "w", encoding="utf-8").write(text[:m.start(1)] + new + text[m.end(1):])
PY
  git -C "$root" add -A
}

# drift_annotation <repo> — reproduce the #1236 shape: slip a version-bearing COMMENT between the
# `# renovate:` annotation and its pin, so the manager's look-ahead captures the phantom from prose
# instead of the pin literal. The `0.14.0` here is exactly the class of version-shaped string that
# shadowed the real SDD.Cli pin on origin/main until #1237 seated the annotation on the pin.
drift_annotation() {
  local root="$1"
  python3 - "$root/$PIN_FILE" <<'PY'
import re, sys
path = sys.argv[1]
text = open(path, encoding="utf-8").read()
m = re.search(r"^(\s*)#\s*renovate:.*$", text, re.MULTILINE)
if not m:
    sys.exit("vacuous fixture: no `# renovate:` annotation to drift from its pin")
indent = m.group(1)
inject = f"\n{indent}# note: 0.14.0 parses as >=0.14.0 under nuget versioning, not a peg"
open(path, "w", encoding="utf-8").write(text[:m.end()] + inject + text[m.end():])
PY
  git -C "$root" add -A
}

# precomment_annotation <repo> — put the same version-bearing comment ABOVE the annotation, the shape
# #1237 moved to. The manager scans FORWARD from the annotation, so prose before it cannot shadow the
# pin — this is the fixture that proves the fix's placement is safe, not merely that the drift reds.
precomment_annotation() {
  local root="$1"
  python3 - "$root/$PIN_FILE" <<'PY'
import re, sys
path = sys.argv[1]
text = open(path, encoding="utf-8").read()
m = re.search(r"^(\s*)#\s*renovate:.*$", text, re.MULTILINE)
if not m:
    sys.exit("vacuous fixture: no `# renovate:` annotation to precede")
indent = m.group(1)
inject = f"{indent}# note: 0.14.0 parses as >=0.14.0 under nuget versioning, not a peg\n"
open(path, "w", encoding="utf-8").write(text[:m.start()] + inject + text[m.start():])
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

# feed_with <name> <json-version-list> — the standard feed, serving those versions for FS.GG.SDD.Cli.
#
# It DERIVES from $FEED rather than building a fresh dict, so the blocks every leg needs but no leg
# is about — `_nuspecs` for the real preset's cap (#943), `_renovate` for bot evidence — come along.
# Built from scratch, a feed here would omit the cap's subject and red every caller for a reason
# unrelated to the version list it exists to vary.
feed_with() {
  local out="$WORK/feed-$1.json"
  python3 -c '
import json, sys
d = json.load(open(sys.argv[1]))
d["FS.GG.SDD.Cli"] = json.loads(sys.argv[2])
json.dump(d, open(sys.argv[3], "w"))
' "$FEED" "$2" "$out"
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

# The #1160 distinction, made testable: a FINDING (a real stale/ahead pin, exit 1) and a NO-VERDICT
# (the gate could not look — an unreadable feed/config/preset, exit 3) must NOT share an exit code, or
# a transient outage reads as a stale pin and a human hand-advances a pin that was fine. `must_fail`
# above only checks "non-zero", which cannot tell 1 from 3; these assert the exact code.
# expect_code <code> <label> <repo> <feed> <required-pattern>
expect_code() {
  local want="$1" out rc
  out="$(gate "$3" "$4")" && rc=0 || rc=$?
  if [ "$rc" -ne "$want" ]; then
    bad "$2 (expected exit $want, got $rc)" "$out"
  elif printf '%s' "$out" | grep -qF -- "$5"; then
    ok "$2"
  else
    bad "$2 (exit $want, but not for the stated reason: no match for '$5')" "$out"
  fi
}
must_finding()   { expect_code 1 "$@"; }   # a real, actionable problem with the subject
must_noverdict() { expect_code 3 "$@"; }   # the gate could not complete its check (#1160)

echo "--- the green baseline: the pin equals feed-newest ---"
BASE="$(make_repo base 0.9.0)"
must_pass "a pin at feed-newest passes" "$BASE"

echo
echo "--- a frozen pin is caught (the #127 / #263 defect itself) ---"
FROZEN="$(make_repo frozen 0.9.0)"; repin "$FROZEN" 0.5.0
must_fail "pin behind the feed fails" "$FROZEN" "$FEED" "is pinned at '0.5.0' but the newest on the registry Renovate reads is '0.9.0'"
must_fail "...and names the freeze, not a generic mismatch" "$FROZEN" "$FEED" "BOT IS BLIND"

AHEAD="$(make_repo ahead 0.9.0)"; repin "$AHEAD" 1.0.0
must_fail "pin ahead of the feed fails" "$AHEAD" "$FEED" "AHEAD"

echo
echo "--- versions are compared by ORDER, never by substring (the #268 defect class) ---"
PRE="$(make_repo pre 0.9.0)"; repin "$PRE" 0.9.0-preview.1
must_fail "'0.9.0-preview.1' is not 'equal' to feed-newest '0.9.0'" "$PRE" "$FEED" "is pinned at '0.9.0-preview.1' but the newest on the registry Renovate reads is '0.9.0'"
must_pass "a release outranks its own prerelease" "$BASE" "$(feed_with onlyrelease '["0.9.0","0.9.0-preview.1"]')"
must_fail "a pin is AHEAD of a feed serving only its prerelease" "$BASE" "$(feed_with onlypre '["0.9.0-preview.1"]')" "AHEAD"

echo
echo "--- a pin's versioning scheme must make its literal a SINGLE version, not a >= floor (#576/#1122) ---"
#
# The blind spot that hid #576 for four rounds: under `nuget` versioning a BARE literal `0.9.0` is
# `>=0.9.0` — every newer release satisfies it, so the bot proposes nothing while the pin sits at
# exactly newest and passes freshness. The annotation manager's versioningTemplate DEFAULT is now
# `loose` (#1135, the sibling of #1131's FsGgUiVersion fix), so a bare literal at the default is a
# SINGLE, bumpable version — the freeze-by-omission is retired. The range that reds this gate is now
# reachable only by an EXPLICIT `versioning=nuget`; these legs flip only that token.

# versioning= OMITTED => the manager default is `loose` (#1135) => bare literal is a SINGLE version
# => GREEN. This is the #1135 behaviour change: a pin can no longer freeze by forgetting `versioning=`.
DEFAULTVER="$(make_repo defaultver 0.9.0)"; set_versioning "$DEFAULTVER" ""
must_pass "a pin left at the manager default is now single-version and bumpable (#1135 retires the freeze-by-omission)" \
  "$DEFAULTVER" "$FEED"

# an EXPLICIT versioning=nuget over a bare literal is a >= floor => RED. This is the range the gate
# still catches; making the DEFAULT loose did not remove the check, only the way a pin fell into it by
# omission. (An author who genuinely wants range semantics opts in here, and the gate holds them to it.)
EXPLICITNUGET="$(make_repo explicitnuget 0.9.0)"; set_versioning "$EXPLICITNUGET" "nuget"
must_fail "an explicit versioning=nuget over a bare literal fails (bare literal = >= floor)" "$EXPLICITNUGET" "$FEED" \
  "can NEVER bump"
must_fail "...and it says so though the pin equals feed-newest (the #576 blind spot)" "$EXPLICITNUGET" "$FEED" \
  "equals newest TODAY"
must_fail "...naming the explicit scheme that made it a range" "$EXPLICITNUGET" "$FEED" \
  "versioning='nuget', which reads it as a RANGE"

# versioning=loose (the baseline) is also GREEN — now belt-and-suspenders over the loose default.
must_pass "an explicit versioning=loose makes the bare literal a single, bumpable version (#1119)" "$BASE"

# an UNVERIFIED scheme fails CLOSED — the gate must not assume a scheme it never drove is 'single'.
BOGUSVER="$(make_repo bogusver 0.9.0)"; set_versioning "$BOGUSVER" "maven"
must_fail "an unverified versioning scheme fails closed, not assumed-fine (#266)" "$BOGUSVER" "$FEED" \
  "has not verified against renovate"

echo
echo "--- the bump MECHANISM is gated, not assumed (the #263 / #576 root cause) ---"
#
# The invariant is: EVERY host the preset routes FS.GG.* to must be one Renovate can actually read.
#
# It used to be "renovate.json declares a hostRules token for nuget.pkg.github.com", full stop. That
# demanded a credential unconditionally, and so it could never say the true thing — that the packages
# are PUBLIC on nuget.org and the credential was never needed. Four freezes were closed by chasing
# that token (#127, #263, #566, and again at 0.10.0). The legs below therefore assert BOTH halves:
# an auth-routed repo still needs its token (the #263 protection survives), and a public-routed one
# does not (the #576 fix is what makes the loop end).

# --- routed at the org feed: the token is still mandatory, exactly as before ---
NOTOKEN="$(make_repo notoken)"; route_to "$NOTOKEN" "https://nuget.pkg.github.com/FS-GG/index.json"
edit_json "$NOTOKEN/renovate.json" 'del d["hostRules"]'; git -C "$NOTOKEN" add -A
must_fail "an auth-routed preset with no hostRules token fails" "$NOTOKEN" "$FEED" \
  "routes FS.GG.* to nuget.pkg.github.com, which REQUIRES a credential"

# ...and the failure must SAY that the packages may simply be public, so the fifth freeze is not
# closed by chasing the credential a fifth time. This is the finding, encoded as a test.
must_fail "...and it points at nuget.org before the credential" "$NOTOKEN" "$FEED" \
  "check whether these packages are simply PUBLIC on nuget.org"

# --- routed at nuget.org: NO token is required, and demanding one is the bug (#576) ---
PUBLIC="$(make_repo publicroute)"; edit_json "$PUBLIC/renovate.json" 'del d["hostRules"]'
git -C "$PUBLIC" add -A
must_pass "a public-routed preset needs no hostRules token at all" "$PUBLIC"

# --- a host the gate does not know must fail CLOSED, not read as fine ---
UNKNOWN="$(make_repo unknownhost)"; route_to "$UNKNOWN" "https://packages.example.invalid/v3/index.json"
must_fail "a preset routed at an UNKNOWN host fails closed" "$UNKNOWN" "$FEED" \
  "does not know how to read"

# --- an UNNAMED nuget-wide registryUrls reroutes FS.GG.* too, and must NOT be invisible ---
#
# A rule with `registryUrls` and NO `matchPackageNames` applies to every nuget package — FS.GG.*
# included. A gate that only looked for rules NAMING FS.GG would report green while the bot was
# routed somewhere it cannot read: the #266 fails-open shape, rebuilt inside the fix for it.
WIDE="$(make_repo widerule)"
edit_json "$WIDE/default.json" "
d['packageRules'].append({
    'description': 'an unnamed nuget-wide route — reaches FS.GG.* without naming it',
    'matchDatasources': ['nuget'],
    'registryUrls': ['https://nuget.pkg.github.com/FS-GG/index.json'],
})
"
edit_json "$WIDE/renovate.json" 'del d["hostRules"]'; git -C "$WIDE" add -A
must_fail "an UNNAMED nuget-wide route to an auth host is still caught" "$WIDE" "$FEED" \
  "routes FS.GG.* to nuget.pkg.github.com, which REQUIRES a credential"

echo
echo "--- a STALE pin is DIAGNOSED, not merely reported (#566) --------------------------------------"
#
# This is the whole of #566. The gate used to print "the annotation manager did not bump it" for
# every stale pin — a cause it never checked, and one that reads identically whether the bot is BLIND
# to the feed or simply has not run since the release shipped. Three causes, three verdicts, and the
# RIGHT ONE has to come out, because the wrong one sends you to hand-bump the literal, which is what
# .github#127 and .github#263 both did — and the pin froze again both times.
#
# `hostRules` is PRESENT in every one of these repos. That is the point: presence is not resolution,
# and the old MECHANISM check reports green in all three.

# (a) BLIND — the bot detected the dep and never opened a PR.
BLIND_EV='{"detected": true, "bump_prs": [], "dashboard": 54}'
must_fail "stale + detected + no bump PR ever ⇒ the bot is BLIND to the feed" \
  "$FROZEN" "$(feed_evidence blind "$BLIND_EV")" \
  "THE BOT IS BLIND TO THE FEED"
must_fail "...and says NOT to hand-bump (the .github#127/#263 paper-over)" \
  "$FROZEN" "$(feed_evidence blind "$BLIND_EV")" \
  "do NOT hand-bump this literal"

# The REMEDIATION must depend on whether a credential is even in the path — naming one that is not
# there is naming a cause the gate did not check, which is #566 rebuilt inside the fix for #566.

# (a1) routed at the PUBLIC registry (the world after #576): there is no token to blame, and the
#      gate must not send anyone to the Mend dashboard. It must instead name the causes that remain
#      — including the CASE-SENSITIVE matcher that let `fs.gg.coord.cli` bump for months while
#      `FS.GG.SDD.Cli` froze, which is the tell that solved #576.
must_fail "a BLIND bot on a PUBLIC route is NOT diagnosed as a credential problem" \
  "$FROZEN" "$(feed_evidence blind "$BLIND_EV")" \
  "it is NOT a credential problem"
must_fail "...and it names the CASE-SENSITIVE matcher as a live cause" \
  "$FROZEN" "$(feed_evidence blind "$BLIND_EV")" \
  "CASE-SENSITIVE"

# (a2) routed at the AUTH feed (the world before #576): the credential IS the live hypothesis, and
#      the #263 diagnosis must survive — but it must now be preceded by "check nuget.org first",
#      because chasing the credential is what closed #127, #263 and #566 without fixing anything.
FROZEN_AUTH="$(make_repo frozenauth 0.9.0)"; repin "$FROZEN_AUTH" 0.5.0
route_to "$FROZEN_AUTH" "https://nuget.pkg.github.com/FS-GG/index.json"
must_fail "a BLIND bot on an AUTH route still names the CREDENTIAL (#263 survives)" \
  "$FROZEN_AUTH" "$(feed_evidence blind "$BLIND_EV")" \
  "is not the credential RESOLVING"
must_fail "...but tells you to check nuget.org BEFORE chasing it (#576)" \
  "$FROZEN_AUTH" "$(feed_evidence blind "$BLIND_EV")" \
  "CHECK WHETHER THE PACKAGE IS PUBLIC ON nuget.org"

# (b) BENIGN — the bot works; it opened a PR and nobody merged it. Hand-bumping here would be WRONG,
#     and the old gate told you to do exactly that.
must_fail "stale + detected + an open bump PR ⇒ benign, merge the PR" \
  "$FROZEN" "$(feed_evidence benign '{"detected": true, "bump_prs": [[91, "open", "chore(deps): update dependency fs.gg.sdd.cli to 0.9.0"]], "dashboard": 54}')" \
  "The bot IS working"
must_fail "...and points at the PR rather than the literal" \
  "$FROZEN" "$(feed_evidence benign '{"detected": true, "bump_prs": [[91, "open", "chore(deps): update dependency fs.gg.sdd.cli to 0.9.0"]], "dashboard": 54}')" \
  "#91 (open)"

# (b2) GROUPED — the bot opened no PR for THIS dep, but it plainly reaches the feed, because it has
#      opened other FS.GG.* bumps. Calling that BLIND would send someone to chase a working token.
#      This is not hypothetical: default.json carries `groupName: "FS.GG.UI coherent set"`, and a
#      grouped PR's title names the GROUP, not the member — so a member pin's name appears in no PR
#      title anywhere, exactly like a bot that never ran.
GROUPED='{"detected": true, "bump_prs": [], "feed_prs": [[77, "merged", "chore(deps): update fs.gg.ui coherent set"]], "dashboard": 54}'
must_fail "stale + no PR for this dep + OTHER FS.GG.* PRs ⇒ NOT blind (grouped/exempt)" \
  "$FROZEN" "$(feed_evidence grouped "$GROUPED")" \
  "The bot can SEE the feed"
out="$(gate "$FROZEN" "$(feed_evidence grouped "$GROUPED")")" || true
printf '%s' "$out" | grep -qF "BOT IS BLIND" \
  && bad "a GROUPED bump is not mistaken for a blind bot (the false-BLIND trap)" "$out" \
  || ok "a GROUPED bump is not mistaken for a blind bot (the false-BLIND trap)"

# (c) MANAGER BROKE — the dep is not in the dashboard at all, so the bot never saw the pin. A
#     different root cause with a different fix, and it must not be confused with a blind token.
must_fail "stale + NOT detected ⇒ the annotation manager's regex stopped matching" \
  "$FROZEN" "$(feed_evidence undetected '{"detected": false, "bump_prs": [], "dashboard": 54}')" \
  "does NOT list this dependency"

# (d) The three verdicts must be DISTINGUISHABLE, or the diagnosis is theatre. A blind bot must not
#     be described as a working one.
out="$(gate "$FROZEN" "$(feed_evidence benign '{"detected": true, "bump_prs": [[91, "open", "x fs.gg.sdd.cli x"]], "dashboard": 54}')")" || true
printf '%s' "$out" | grep -qF "BOT IS BLIND" \
  && bad "a WORKING bot is not reported as blind" "$out" \
  || ok "a WORKING bot is not reported as blind"

# (e) FAILS CLOSED. No evidence ⇒ the gate says the cause is UNVERIFIED. It must never fall back to
#     asserting one — that is the very defect (#566), and rebuilding it inside the fix would be a
#     bleak sort of joke.
NOEV="$WORK/feed-noevidence.json"
python3 -c 'import json,sys; d=json.load(open(sys.argv[1])); d.pop("_renovate",None); json.dump(d,open(sys.argv[2],"w"))' "$FEED" "$NOEV"
must_fail "no bot evidence ⇒ the CAUSE is UNVERIFIED, never guessed" "$FROZEN" "$NOEV" "the CAUSE is UNVERIFIED"
out="$(gate "$FROZEN" "$NOEV")" || true
printf '%s' "$out" | grep -qE "BOT IS BLIND|bot IS working" \
  && bad "an unverifiable cause is not reported as a verified one" "$out" \
  || ok "an unverifiable cause is not reported as a verified one"

# These three are all AUTH-routed: an empty token, a token for the wrong host, and a token hidden in
# a lower-precedence file are only defects when a credential is actually in the path. Routed at
# public nuget.org they are non-events, and failing them there would be the gate demanding a secret
# it does not need — which is #576 itself.
EMPTYTOK="$(make_repo emptytok)"; route_to "$EMPTYTOK" "https://nuget.pkg.github.com/FS-GG/index.json"
edit_json "$EMPTYTOK/renovate.json" 'd["hostRules"][0]["token"] = ""'; git -C "$EMPTYTOK" add -A
must_fail "an auth-routed repo with an empty token fails" "$EMPTYTOK" "$FEED" "declares no \`hostRules\` token"

OTHERHOST="$(make_repo otherhost)"; route_to "$OTHERHOST" "https://nuget.pkg.github.com/FS-GG/index.json"
edit_json "$OTHERHOST/renovate.json" 'd["hostRules"][0]["matchHost"] = "nuget.org"'; git -C "$OTHERHOST" add -A
must_fail "an auth-routed repo whose token names the wrong host fails" "$OTHERHOST" "$FEED" \
  "declares no \`hostRules\` token"

NOCFG="$(make_repo nocfg)"; rm "$NOCFG/renovate.json"; git -C "$NOCFG" add -A
must_fail "a repo with pins and no Renovate config fails" "$NOCFG" "$FEED" "no Renovate configuration"

# --- prose in a Renovate config is a TEMPLATE, not documentation ---
#
# Renovate interpolates {{ }} in EVERY config value, `description` included. A `{{ secrets.X }}` in
# a description either fails config-validation with "Unknown secrets name" — taking the WHOLE repo
# config down, so Renovate silently does nothing — or splices a live secret into a non-credential
# field. `renovate-config-validator` reports such a config as VALID, because it never interpolates.
#
# This is the defect the #576 fix itself shipped: it removed the hostRules token and then QUOTED the
# template in the description explaining why. Only actually running Renovate caught it. The token
# position stays legal; everywhere else is a bug.
STRAY="$(make_repo straysecret)"
edit_json "$STRAY/renovate.json" "d['description'] = 'we removed the {{ secrets.FSGG_PACKAGES_READ_TOKEN }} block'"
git -C "$STRAY" add -A
must_fail "a {{ secrets }} template in a DESCRIPTION fails" "$STRAY" "$FEED" \
  "template outside a hostRules token"

LEGALTOK="$(make_repo legaltok)"; route_to "$LEGALTOK" "https://nuget.pkg.github.com/FS-GG/index.json"
must_pass "a {{ secrets }} template in a hostRules TOKEN is legal" "$LEGALTOK"

echo
echo "--- the config file read is the one Renovate would read (resolution order) ---"
# Renovate resolves .github/renovate.json BEFORE .renovaterc. Reading the wrong one answers a
# question about a config the bot never uses — and a token-bearing .renovaterc masking a token-less
# .github/renovate.json would report green while the bot goes on 401'ing.
# Auth-routed on purpose: against a public route the token is not required, so this leg would pass
# whether or not the gate ever found it — a green tick over no subject, which is the #266 shape.
DOTGH="$(make_repo dotgh)"
route_to "$DOTGH" "https://nuget.pkg.github.com/FS-GG/index.json"
rm "$DOTGH/renovate.json"
cat > "$DOTGH/.github/renovate.json" <<'JSON'
{ "extends": ["github>FS-GG/.github"],
  "hostRules": [ { "matchHost": "nuget.pkg.github.com", "hostType": "nuget", "token": "{{ secrets.FSGG_PACKAGES_READ_TOKEN }}" } ] }
JSON
printf '{ "extends": ["github>FS-GG/.github"] }' > "$DOTGH/.renovaterc"
git -C "$DOTGH" add -A
must_pass "the token in .github/renovate.json is found (it outranks .renovaterc)" "$DOTGH"

MASKED="$(make_repo masked)"
route_to "$MASKED" "https://nuget.pkg.github.com/FS-GG/index.json"
rm "$MASKED/renovate.json"
printf '{ "extends": ["github>FS-GG/.github"] }' > "$MASKED/.github/renovate.json"
cat > "$MASKED/.renovaterc" <<'JSON'
{ "hostRules": [ { "matchHost": "nuget.pkg.github.com", "hostType": "nuget", "token": "{{ secrets.FSGG_PACKAGES_READ_TOKEN }}" } ] }
JSON
git -C "$MASKED" add -A
must_fail "a token in the LOWER-precedence .renovaterc cannot mask a token-less .github/renovate.json" \
  "$MASKED" "$FEED" "declares no \`hostRules\` token"

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
echo "--- an unreadable or unresolvable feed is a NO-VERDICT, not a finding (#1160) ---"
# The heart of #1160: a feed that could not be read is exit 3 (no verdict), NOT exit 1 (a stale pin).
# Before the harness migration both came back as 1, so a transient nuget.org outage was indistinguishable
# from a frozen pin and a human would hand-advance a pin that was fine.
must_noverdict "a feed serving zero versions is a NO-VERDICT (exit 3), not a stale-pin finding" \
  "$BASE" "$(feed_with empty404 '[]')" "zero versions"
NOPKG="$WORK/feed-nopkg.json"; printf '{}' > "$NOPKG"
must_noverdict "a feed that does not serve the package is a NO-VERDICT (exit 3)" \
  "$BASE" "$NOPKG" "not on the registry"
# ...and the message says out loud that this is not a stale pin, so nobody bumps one on the strength of it.
must_noverdict "the no-verdict says it is NOT a stale pin" \
  "$BASE" "$(feed_with empty404b '[]')" "NOT a stale pin"
# The other side of the distinction: a genuinely frozen pin, read against a HEALTHY feed, stays a
# FINDING (exit 1). The two codes are what a caller keys on to tell "fix this pin" from "look again".
FROZEN1160="$(make_repo frozen1160 0.9.0)"; repin "$FROZEN1160" 0.5.0
must_finding "a frozen pin against a healthy feed stays a FINDING (exit 1), not a no-verdict" \
  "$FROZEN1160" "$FEED" "is pinned at '0.5.0'"

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
# `versioning=loose` so this pin is unambiguously a SINGLE version and reaches the FS.GG-resolution
# check — the property under test here. Since #1135 the manager default is `loose` too, so the token
# is belt-and-suspenders rather than load-bearing; kept explicit so this leg's subject stays the
# resolution check and not the versioning scheme, whatever a future default becomes.
cat >> "$EXTRA/$PIN_FILE" <<'YAML'
      - run: |
          # renovate: datasource=nuget depName=Expecto versioning=loose
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

# `=''`, not `= ` — identical (the var is still set, still empty, still only for this command), but
# `FOO= cmd` is also exactly how a typo'd `FOO=$BAR cmd` looks once the value goes missing, so a
# linter cannot tell this deliberate empty from that accident (SC1007). Say it explicitly. #648
out="$(FSGG_PIN_FIXTURE_OK='' python3 "$GATE" --root "$BASE" --fixture "$FEED" 2>&1)" && rc=0 || rc=$?
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
echo "--- the synced receiver file must be UNMANAGED, and by the right mechanism (#678) ---"
# Renovate bumping a receiver's SYNCED .config/dotnet-tools.json opens a PR the build-config drift
# gate must reject, in every receiver, forever (FS.GG.Game#278). The rule that stops it is easy to
# write in two ways that are silently wrong, and renovate-config-validator calls both valid — so
# each wrong way gets a leg here.

# The trap #678 itself proposed. ignorePaths matches by SUBSTRING (`file.includes(ignorePath)`), so
# this ALSO un-manages dist/dotnet/.config/dotnet-tools.json — the org source of truth, and the one
# pin Renovate has ever bumped in this repo (#660). It would freeze the baseline while looking like
# a fix.
IGN="$(make_repo ignorepaths)"
edit_json "$IGN/default.json" 'd["ignorePaths"] = [".config/dotnet-tools.json"]'
must_fail "ignorePaths on a synced file is refused (it swallows dist/dotnet/ by substring)" \
  "$IGN" "$FEED" "matches by SUBSTRING"

# The over-broad glob. `**/` reaches dist/dotnet/ exactly as ignorePaths did, and this is the shape
# someone reaches for when the anchored one "looks too specific".
BROAD="$(make_repo broad-glob)"
edit_json "$BROAD/default.json" '
for r in d["packageRules"]:
    p = r.get("matchFileNames") or []
    if ".config/dotnet-tools.json" in p:
        p[p.index(".config/dotnet-tools.json")] = "**/.config/dotnet-tools.json"
'
must_fail "a leading **/ on the synced-file rule is refused (it reaches dist/dotnet/)" \
  "$BROAD" "$FEED" "reaches beyond the receiver's copy"

# The rule deleted outright — the un-mergeable receiver PR returns.
NORULE="$(make_repo no-synced-rule)"
edit_json "$NORULE/default.json" '
d["packageRules"] = [r for r in d["packageRules"]
                     if ".config/dotnet-tools.json" not in (r.get("matchFileNames") or [])]
'
must_fail "deleting the synced-file rule is refused" \
  "$NORULE" "$FEED" "declares no \`matchFileNames:"

# SHORTER ignorePaths entries are STRICTLY MORE dangerous, because the match is a substring of the
# FILE, not of the entry. A gate that only catches the one spelling #678 proposed would wave every
# one of these through — each freezes dist/dotnet/ (measured against renovate 43.265.2).
for e in ".config" "dist/" ".json" "/"; do
  SUB="$(make_repo "substr-$(printf '%s' "$e" | tr -c '[:alnum:]' '-')")"
  edit_json "$SUB/default.json" "d[\"ignorePaths\"] = [\"$e\"]"
  must_fail "ignorePaths ['$e'] is refused (it is a substring of the source-of-truth path)" \
    "$SUB" "$FEED" "is a SUBSTRING of dist/dotnet/.config/dotnet-tools.json"
done

# A glob spelling that HAPPENS to work (it escapes the substring branch and anchors) is still
# refused — resting the baseline on that coincidence is the thing being prevented. This leg exists
# to pin the reason: the gate must reject it for naming the file, not for "not working".
GLOB="$(make_repo glob-ignorepath)"
edit_json "$GLOB/default.json" 'd["ignorePaths"] = ["[.]config/dotnet-tools.json"]'
must_fail "even a WORKING ignorePaths glob is refused (fragile by coincidence)" \
  "$GLOB" "$FEED" "whose only safe home is a matchFileNames packageRule"

# Renovate merges packageRules IN ORDER and the last match wins. A later rule re-enabling the file
# leaves every earlier `enabled: false` sitting in the config looking correct.
REEN="$(make_repo reenabled-later)"
edit_json "$REEN/default.json" '
d["packageRules"].append({"matchFileNames": [".config/dotnet-tools.json"], "enabled": True})
'
must_fail "a LATER rule re-enabling the synced file is refused (last rule wins)" \
  "$REEN" "$FEED" "is the LAST rule matching"

# A narrowed disable still proposes the un-mergeable PR for everything it no longer covers.
NARROW="$(make_repo narrowed-rule)"
edit_json "$NARROW/default.json" '
for r in d["packageRules"]:
    if ".config/dotnet-tools.json" in (r.get("matchFileNames") or []):
        r["matchUpdateTypes"] = ["major"]
'
must_fail "narrowing the disable with an extra matcher is refused" \
  "$NARROW" "$FEED" "Every additional key NARROWS the rule"

echo
echo "--- the .props half: disabled in receivers, MANAGED in the source of truth (#794) ---"
# Directory.{Packages,Build}.props are the other two files sync-build-config.sh manages. #925 read
# them as INEXPRESSIBLE here and routed them to a re-enable in this repo's own renovate.json,
# because the SAME root path is a receiver's synced copy and this repo's own authored build config
# — and .github dogfoods this preset. `matchRepositories: ["!FS-GG/.github"]` expresses it in one
# place. Every leg below is valid config that renovate-config-validator passes, and silent.

# The #794 trap, and the reason the exclusion is REQUIRED rather than optional: name the .props
# unconditionally and Renovate stops proposing bumps for THIS repo's own pins (FSharp.Core,
# Spectre.Console, xunit) and for the dist/dotnet/ baseline every receiver is synced from (#753).
# That is #576 — a config sentence that silently stops a bot, with the gate green.
NOEXCL="$(make_repo no-source-exclusion)"
edit_json "$NOEXCL/default.json" '
for r in d["packageRules"]:
    if "Directory.Packages.props" in (r.get("matchFileNames") or []):
        del r["matchRepositories"]
'
must_fail "dropping matchRepositories is refused (it would freeze this repo's own pins)" \
  "$NOEXCL" "$FEED" "NO \`matchRepositories\`"

# WHAT THIS GATE ASSERTS ABOUT `matchRepositories` CHANGED IN #1552, AND THE LEGS BELOW ARE THE
# CHANGE. It used to require the exact literal `["!FS-GG/.github"]`. That negation says "every repo
# except the author", which is NOT "every repo that RECEIVES these files" — and the two stopped
# being equal once FS.GG.Templates, FS.GG.Audio and FS.GG.Net were onboarded without build-config,
# zeroing two repos' entire NuGet surface. The preset now carries a POSITIVE per-fabric allow-list
# derived from registry/repos.yml, gated by check-preset-repo-scope-coherence.py.
#
# This gate's subject narrowed to the one thing it can answer with no registry at all: THE AUTHORITY
# IS NEVER CAUGHT. Which receivers the list must name is the other gate's red, so that one red does
# not carry two unrelated meanings (#1538).

# A `!`-NEGATION MATCHES EVERY REPO IT DOES NOT NAME — including this one. That is how the old
# spelling could not express "only these four", and it is why any negation is now refused outright
# rather than pattern-matched for safety.
EXTRA="$(make_repo negated-repo-match)"
edit_json "$EXTRA/default.json" '
for r in d["packageRules"]:
    if "Directory.Packages.props" in (r.get("matchFileNames") or []):
        r["matchRepositories"] = ["!FS-GG/.github", "!FS-GG/FS.GG.Game"]
'
must_fail "a \`!\`-negation in matchRepositories is refused (it matches whatever it does not name)" \
  "$EXTRA" "$FEED" "must be a plain positive"

# A GLOB MATCHES THE AUTHORITY TOO, and `*` is the shape that reads most like "all the receivers".
GLOB="$(make_repo glob-repo-match)"
edit_json "$GLOB/default.json" '
for r in d["packageRules"]:
    if "Directory.Packages.props" in (r.get("matchFileNames") or []):
        r["matchRepositories"] = ["FS-GG/*"]
'
must_fail "a GLOB in matchRepositories is refused (it catches the source of truth)" \
  "$GLOB" "$FEED" "must be a plain positive"

# NAMING THE AUTHORITY OUTRIGHT. This repo AUTHORS the .props; listing it freezes its own pins.
INV="$(make_repo authority-listed)"
edit_json "$INV/default.json" '
for r in d["packageRules"]:
    if "Directory.Packages.props" in (r.get("matchFileNames") or []):
        r["matchRepositories"] = ["FS-GG/.github", "FS-GG/FS.GG.Game"]
'
must_fail "naming FS-GG/.github in matchRepositories is refused (it AUTHORS these paths)" \
  "$INV" "$FEED" "AUTHORS that path"

# AN EMPTY LIST MATCHES NOTHING — the un-mergeable receiver PR comes back everywhere, and it looks
# like a scoping that simply has not been filled in yet.
EMPTY="$(make_repo empty-repo-match)"
edit_json "$EMPTY/default.json" '
for r in d["packageRules"]:
    if "Directory.Packages.props" in (r.get("matchFileNames") or []):
        r["matchRepositories"] = []
'
must_fail "an EMPTY matchRepositories is refused (it matches nothing)" \
  "$EMPTY" "$FEED" "not a non-empty list"

# Each .props dropped from the list — the un-mergeable receiver PR returns for that file. This is
# the leg that keeps the list COMPLETE against sync-build-config.sh's FILES.
for f in "Directory.Packages.props" "Directory.Build.props"; do
  DROP="$(make_repo "drop-$(printf '%s' "$f" | tr -c '[:alnum:]' '-')")"
  edit_json "$DROP/default.json" "
for r in d['packageRules']:
    p = r.get('matchFileNames') or []
    if '$f' in p:
        p.remove('$f')
"
  must_fail "dropping $f from the disable list is refused" \
    "$DROP" "$FEED" "declares no \`matchFileNames:"
done

# The disable list is a ROSTER, and sync-build-config.sh owns the real one. A fourth synced file
# would otherwise land a fourth un-mergeable PR in every receiver with both the preset and this gate
# green, because neither knows it exists — the census rot #902 fixed in three copies at once.
GROW="$(make_repo synced-set-grew)"
python3 - "$GROW/scripts/sync-build-config.sh" <<'PY'
import sys
p = sys.argv[1]
s = open(p, encoding="utf-8").read()
# The FILES array's last entry since #1077 moved the manifest onto the kit — the two remaining
# managed files are both .props.
old = '  "Directory.Packages.props"\n)'
assert old in s, "fixture no longer matches sync-build-config.sh's FILES block"
open(p, "w", encoding="utf-8").write(s.replace(old, old[:-1] + '  "nuget.config"\n)', 1))
PY
must_fail "a file added to sync-build-config.sh but not to the preset is refused" \
  "$GROW" "$FEED" "that this gate does not disable"

# ...and the mirror: a file the script no longer syncs is one a receiver AUTHORS now, so leaving it
# disabled silently freezes that receiver's own pin — the #576 direction of the same drift.
SHRINK="$(make_repo synced-set-shrank)"
python3 - "$SHRINK/scripts/sync-build-config.sh" <<'PY'
import sys
p = sys.argv[1]
s = open(p, encoding="utf-8").read()
old = '  "Directory.Build.props"\n'
assert old in s, "fixture no longer matches sync-build-config.sh's FILES block"
open(p, "w", encoding="utf-8").write(s.replace(old, "", 1))
PY
must_fail "a file the sync script dropped but the preset still disables is refused" \
  "$SHRINK" "$FEED" "no owner syncs any more"

# ignorePaths reaching the .props source of truth by substring — the #678 trap, now for the files
# #794 adds. "Directory.Packages.props" occurs inside dist/dotnet/Directory.Packages.props.
for e in "Directory.Packages.props" "Directory.Build.props"; do
  PSUB="$(make_repo "psub-$(printf '%s' "$e" | tr -c '[:alnum:]' '-')")"
  edit_json "$PSUB/default.json" "d[\"ignorePaths\"] = [\"$e\"]"
  must_fail "ignorePaths ['$e'] is refused (it swallows dist/dotnet/ by substring)" \
    "$PSUB" "$FEED" "is a SUBSTRING of dist/dotnet/$e"
done

echo
echo "--- a CAP's expiry trigger is EXECUTED, not merely written down (#943, #850) ---"
#
# The defect: an `allowedVersions` cap states its own expiry condition in its `description`, the
# condition comes true, and nothing reads prose. FS.GG.Rendering's Expecto cap outlived its reason by
# months exactly that way. So the gate now reads a `fsgg-cap-expires-when:` annotation out of the
# rule and re-checks it daily against the registry.
#
# These legs flip ONE measured fact at a time against the real preset's real cap. The baseline above
# is already green with the real ranges (see $FEED), which is what makes a red here mean something.

# cap_edit <repo> <python-stmt-over-`c`> — mutate the preset's allowedVersions cap. Refuses a no-op.
cap_edit() {
  local root="$1" stmt="$2"
  edit_json "$root/default.json" "
for c in d['packageRules']:
    if 'allowedVersions' in c:
        $stmt
        break
else:
    raise SystemExit('vacuous fixture: the preset carries no allowedVersions cap to mutate')
"
  git -C "$root" add -A
}

# feed_nuspec <name> <python-stmt-over-`n`> — the standard feed with a mutated _nuspecs block.
feed_nuspec() {
  local out="$WORK/feed-ns-$1.json"
  python3 - "$FEED" "$2" "$out" <<'PY'
import json, sys
src, stmt, dst = sys.argv[1:4]
d = json.load(open(src))
n = d["_nuspecs"]
before = json.dumps(d, sort_keys=True)
exec(stmt)  # noqa: S102 — fixture-local, mutates `n`
if json.dumps(d, sort_keys=True) == before:
    sys.exit(f"vacuous fixture: {stmt!r} changed nothing")
json.dump(d, open(dst, "w"))
PY
  printf '%s' "$out"
}

out="$(gate "$BASE")"
printf '%s' "$out" | grep -qF "still justified — no version it excludes admits Expecto 11.0.0" \
  && ok "the real cap is re-checked, and says WHY it still holds" \
  || bad "the real cap is re-checked, and says WHY it still holds" "$out"

# THE EVENT THE CAP IS WAITING FOR: YoloDev ships an adapter above 1.0.0 whose nuspec admits
# Expecto 11. This is the exact thing the prose ACTION line asks a human to notice, and didn't.
must_fail "a cap whose excluded version starts admitting the dep is EXPIRED" "$BASE" \
  "$(feed_nuspec admits 'n["YoloDev.Expecto.TestSdk"]["1.0.0"]["Expecto"] = "[9.0.0, 12.0.0)"')" \
  "CAP EXPIRED"

# THE #850 DEFECT ITSELF, on real data: a cap that excludes 0.16.0 — the version whose nuspec really
# does say `Expecto 10.2.3` (a bare MINIMUM, so it admits 11). That bare-minimum spelling is the one
# whose meaning is invisible in its punctuation, and it is what actually retired Rendering's cap.
EXPIRED="$(make_repo expired)"; cap_edit "$EXPIRED" "c['allowedVersions'] = '<0.16.0'"
must_fail "the #850 condition (a bare-MINIMUM range) retires a cap" "$EXPIRED" "$FEED" \
  "CAP EXPIRED"
must_fail "...and it names the version and the range that retired it" "$EXPIRED" "$FEED" \
  "declares Expecto '10.2.3', which ADMITS Expecto 11.0.0"

# The half that keeps the house rule true: a cap written with no trigger is red on the PR adding it.
NOTRIG="$(make_repo notrig)"
cap_edit "$NOTRIG" "c['description'] = 'Cap it below 1.0.0. ACTION WHEN a version above 1.0.0 ships: delete this cap.'"
must_fail "a cap with NO trigger is refused (prose is not a trigger)" "$NOTRIG" "$FEED" \
  "declares NO expiry trigger"

BADTRIG="$(make_repo badtrig)"
cap_edit "$BADTRIG" "c['description'] = 'fsgg-cap-expires-when: when Expecto 11 works'"
must_fail "an unreadable trigger is refused, not ignored" "$BADTRIG" "$FEED" \
  "is not readable"

# A trigger is a WHOLE description unit, never a substring of the prose. The real cap's description
# EXPLAINS the annotation, so it quotes the spelling — including the `manual` one. Under the first
# spelling of this gate, a regex searched the joined text and took whichever match came first, so
# that quoted example was a live trigger: reordering the description array silently reclassified the
# real cap as `manual`, i.e. never checked again. That is #850 rebuilt inside the fix for #850, and
# it is the #919 trap (a gate parsing a quoted sample as a real invocation) one repo over.
#
# The real preset is the subject on purpose — this asserts the SHIPPING description cannot shadow
# its own trigger, whatever order its paragraphs end up in.
SHADOW="$(make_repo shadow)"
edit_json "$SHADOW/default.json" "
for c in d['packageRules']:
    if 'allowedVersions' in c:
        c['description'] = list(reversed(c['description']))
        break
"
git -C "$SHADOW" add -A
must_pass "prose QUOTING the annotation cannot shadow the real trigger" "$SHADOW"
out="$(gate "$SHADOW")"
printf '%s' "$out" | grep -qF "still justified — no version it excludes admits Expecto 11.0.0" \
  && ok "...and the reordered description still resolves to the REAL trigger, not the quoted one" \
  || bad "...and the reordered description still resolves to the REAL trigger, not the quoted one" "$out"

TWOTRIG="$(make_repo twotrig)"
cap_edit "$TWOTRIG" "c['description'] = ['fsgg-cap-expires-when: dependency=Expecto admits=11.0.0', 'fsgg-cap-expires-when: manual — and also this']"
must_fail "TWO triggers are refused (which one wins must not depend on prose order)" "$TWOTRIG" "$FEED" \
  "declares 2 expiry triggers"

# The sentinel: a cap whose trigger is NOT a fact about a nuspec (FS.GG.Audio's FSharp.Core cap is
# this shape — its trigger is an org coordination decision). Reported UNCHECKED, never as green.
MANUAL="$(make_repo manual)"
cap_edit "$MANUAL" "c['description'] = 'fsgg-cap-expires-when: manual — the org majority pins 10.1.x; raising this ceiling is a cross-repo decision, not a fact about a nuspec'"
must_pass "the manual sentinel is accepted" "$MANUAL"
out="$(gate "$MANUAL")"
printf '%s' "$out" | grep -qF "MANUAL" && printf '%s' "$out" | grep -qF "unchecked — the org majority" \
  && ok "...and is reported as UNCHECKED, with its reason, not as green" \
  || bad "...and is reported as UNCHECKED, with its reason, not as green" "$out"

BARE="$(make_repo bare)"
cap_edit "$BARE" "c['description'] = 'fsgg-cap-expires-when: manual'"
must_fail "a bare 'manual' with no reason is refused (that is the silence, in costume)" "$BARE" "$FEED" \
  "is not readable"

# Every way the check can fail to LOOK is an error, never a green (epic #266).
VACUOUS="$(make_repo vacuous)"; cap_edit "$VACUOUS" "c['allowedVersions'] = '<99.0.0'"
must_fail "a cap that excludes NOTHING has no subject, and must not pass" "$VACUOUS" "$FEED" \
  "excludes NO published stable version"

REGEXCAP="$(make_repo regexcap)"; cap_edit "$REGEXCAP" "c['allowedVersions'] = '/^0\\\\./'"
must_fail "a REGEX cap is refused (its excluded set cannot be enumerated)" "$REGEXCAP" "$FEED" \
  "is a REGEX"

REGEXPKG="$(make_repo regexpkg)"; cap_edit "$REGEXPKG" "c['matchPackageNames'] = ['/^YoloDev\\\\./']"
must_fail "a cap matching packages by REGEX is refused" "$REGEXPKG" "$FEED" \
  "matches packages by REGEX"

NONAMES="$(make_repo nonames)"; cap_edit "$NONAMES" "c.pop('matchPackageNames')"
must_fail "a cap with no matchPackageNames is refused" "$NONAMES" "$FEED" \
  "applies to EVERY package"

must_fail "an UNREADABLE nuspec is an error, never 'declares no constraint'" "$BASE" \
  "$(feed_nuspec gone 'n["YoloDev.Expecto.TestSdk"].pop("1.0.0")')" \
  "must not read as 'no constraint'"

# A version that stops depending on the dep at all no longer constrains it — so the cap's reason is
# gone. Real, not hypothetical: it is how a package drops a peer it used to pin.
must_fail "an excluded version that drops the dep ENTIRELY retires the cap" "$BASE" \
  "$(feed_nuspec dropped 'n["YoloDev.Expecto.TestSdk"]["1.0.0"] = {}')" \
  "declares NO dependency on Expecto at all"

# A nuspec declares its dependencies once per target-framework group. YoloDev.Expecto.TestSdk 1.0.0
# really does declare Expecto TWICE — identically, so the two collapse to the one fact they carry
# (that dedupe is what keeps the baseline green). Groups that genuinely DISAGREE carry two facts,
# and there is no honest way to pick one: the cap might be retired on one framework and needed on
# the other. Refuse, rather than answer at random.
must_pass "identical per-framework groups collapse to the one fact they carry" "$BASE" \
  "$(feed_nuspec dup 'n["YoloDev.Expecto.TestSdk"]["1.0.0"]["Expecto"] = ["[9.0.0, 10.0.0)", "[9.0.0, 10.0.0)"]')"
must_fail "DISAGREEING per-framework groups are refused, not guessed between" "$BASE" \
  "$(feed_nuspec disagree 'n["YoloDev.Expecto.TestSdk"]["1.0.0"]["Expecto"] = ["[9.0.0, 10.0.0)", "[9.0.0, 12.0.0)"]')" \
  "DISAGREEING ranges"

# --- the annotation must sit ON its pin, not be shadowed by a version in a comment (#1236) ---
# When a comment carrying a version-shaped string slips between the `# renovate:` annotation and the
# pin, the manager's look-ahead captures the PHANTOM out of the prose. Renovate and this gate both
# track it; a bump PR rewrites the comment and the real pin freezes — the exact #263/#576/#1121
# freeze family, caught here STRUCTURALLY (no feed) so a future drift reds instead of freezing.
DRIFT="$(make_repo drift 0.9.0)"
drift_annotation "$DRIFT"
must_finding "an annotation shadowed by a version in a comment reds (#1236 drift)" \
  "$DRIFT" "$FEED" "captured its version from a COMMENT"
must_finding "...and it captured the phantom 0.14.0 from the comment, not the 0.9.0 pin" \
  "$DRIFT" "$FEED" "0.14.0"
must_finding "...and it names the fix: seat the annotation immediately above the pin" \
  "$DRIFT" "$FEED" "Move the annotation to sit immediately above the pin"

# The masquerade #1236 is really about: even when the feed's newest EQUALS the phantom — so the naive
# "captured value == newest" comparison would report GREEN — the drift still reds, because it is
# caught STRUCTURALLY, before the feed is read. Without that ordering the phantom passes as coherent.
must_finding "a drift reds even when the feed's newest equals the phantom (caught before the compare)" \
  "$DRIFT" "$(feed_with phantom '["0.14.0"]')" "captured its version from a COMMENT"

# The SAME version-shaped comment ABOVE the annotation is harmless: the manager scans FORWARD, so
# prose before the annotation cannot shadow the pin. This is exactly the shape #1237 moved the block
# to — the fix must PASS, or the gate would forbid its own remedy.
ABOVE="$(make_repo above 0.9.0)"
precomment_annotation "$ABOVE"
must_pass "a version-shaped comment ABOVE the annotation does not shadow the pin (#1236 fix shape)" "$ABOVE"

echo
echo "--- CI guard on the real repo (no network: structure only) ---"
# Proves REQUIRED_PINS still names a pin that exists here, and that this repo's own routing is one
# Renovate can actually read — the two things a refactor of this repo could quietly break. Feed
# comparison needs network and is covered by the workflow's live run.
#
# It also asserts the ROUTE itself, because that is the fact #576 turned on: if someone repoints the
# preset back at nuget.pkg.github.com, this repo needs a credential again and the pin can freeze a
# fifth time. That must break a test, not a release.
out="$(python3 - "$REPO_ROOT" "$GATE" <<'PY' 2>&1
import importlib.util, sys
root, gate_path = sys.argv[1:3]
spec = importlib.util.spec_from_file_location("gate", gate_path)
gate = importlib.util.module_from_spec(spec); spec.loader.exec_module(gate)
cfg, hosts = gate.check_bump_mechanism(root, f"{root}/default.json")
assert hosts, "the preset routes FS.GG.* nowhere at all"
unreadable = [h for h in hosts if h not in gate.PUBLIC_HOSTS and h not in gate.AUTH_HOSTS]
assert not unreadable, f"the preset routes FS.GG.* to unreadable host(s): {unreadable}"
rx, mp, vt = gate.load_annotation_manager(f"{root}/default.json")
pins = gate.scan_pins(root, rx, mp)
# The real preset's manager must resolve to a scheme the gate knows, and the real SDD.Cli pin must be
# a single version under it — the #576/#1122 property, asserted against the real tree so a dropped
# `versioning=loose` breaks a test, not a release.
for _p in pins:
    _scheme = gate.resolve_versioning(vt, _p.versioning)
    assert gate.is_single_version(_scheme, _p.current_value), (
        f"{_p.dep_name} at {_p.current_value!r} is NOT a single version under versioning={_scheme!r} "
        f"— it can never bump (#576)"
    )
    # ...and each annotation must actually sit on its pin, not read a phantom out of a nearby comment
    # (#1236). is_single_version above passes a phantom like `0.14.0` happily — this is what catches
    # the drift, and it is asserted against the real tree so a re-separated annotation reds a test.
    assert gate.prose_capture_problem(_p) is None, gate.prose_capture_problem(_p)
gate.assert_required_pins(pins)
assert pins, "no pins found in the real repo"

# The REAL preset must disable every synced receiver file, anchored (#678).
gate.assert_synced_files_unmanaged(f"{root}/default.json")

# ...and the list must still equal the one sync-build-config.sh actually syncs (#794, #902). A
# fourth FILES entry would otherwise land a fourth un-mergeable PR in every receiver, silently.
gate.assert_synced_list_is_complete(root)

# ...and the org source of truth must still be a pin this gate can SEE. The whole hazard of #678 is
# a rule that un-manages dist/dotnet/ while looking like a fix, so assert the positive half against
# the real tree rather than trusting the shape check alone: sync-build-config.sh's canonical copy
# must exist and carry the tool pin Renovate bumps here (#660).
# Named, not indexed. This leg json.loads the file and reads `.tools`, which is true of exactly ONE
# member of SYNCED_RECEIVER_FILES — it rode on [0] back when the tuple had a single entry, and #794
# grew it to three. Sorting that tuple would hand this line an XML .props file and a JSONDecodeError
# dressed up as a repo-structure failure.
_tools_rel = ".config/dotnet-tools.json"
assert _tools_rel in gate.SYNCED_RECEIVER_FILES, f"{_tools_rel} is no longer a synced file"
tools = f"{root}/dist/dotnet/{_tools_rel}"
import json as _json, os as _os
assert _os.path.exists(tools), f"the org source of truth is missing: {tools}"
_pins = _json.load(open(tools, encoding="utf-8")).get("tools") or {}
assert _pins, f"{tools} declares no tools — nothing for Renovate to keep fresh (#660)"
print(f"ok: {len(pins)} pin(s); every REQUIRED_PINS entry present; FS.GG.* routed to {', '.join(hosts)}")
print(f"ok: receiver {', '.join(gate.SYNCED_RECEIVER_FILES)} disabled; dist/dotnet/ still declares "
      f"{len(_pins)} managed tool pin(s)")
PY
)" && rc=0 || rc=$?
if [ "${rc:-0}" -eq 0 ]; then ok "real repo: required pins present, bump mechanism configured"; else bad "real repo structure" "$out"; fi

echo
echo "$pass passed, $failcount failed."
[ "$failcount" -eq 0 ]
