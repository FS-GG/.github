#!/usr/bin/env bash
# Fixture for scripts/check-release-coherence.py — the gate that finds the newest FULLY-TAGGED
# coherent-set release of FS.GG.Kit / FS.GG.Drivers / FS.GG.Coord.Cli (all three release-tag
# namespaces sharing one commit, exactly what .github#2409's sibling-tag precondition already
# proves) and asserts every one of the three actually serves it, on both the org GitHub Packages
# feed and nuget.org (.github#2445, residual of .github#2409).
#
# THE COMPARAND IS THE TAGGED TRIO, NOT "EACH PACKAGE'S OWN NEWEST" — and this fixture's baseline
# encodes why, because comparing bare newest-against-newest was tried against the LIVE feeds while
# building this gate and found permanently red for a reason that is not a defect: the three packages
# carried independent version histories before .github#2402, so their own newest versions disagree by
# default and will keep disagreeing between coordinated releases. The subject that actually exists is
# narrower: a version is a genuine coordinated release attempt iff all three release tags exist at ONE
# shared commit — precisely what the precondition checks before any of the three publishes. So this
# fixture's failure legs mutate the FEED (a tagged trio whose publish did not complete for one
# member), never the tags-vs-siblings' bare newest shape, and it asserts the BOOTSTRAP state (no
# coordinated release has ever completed) is reported loudly but never reds.
#
# Every negative leg asserts the REASON, not just a non-zero exit (the .github#266 vacuous-failure
# defect class). Throwaway trees under a temp dir, no network (the gate's
# --fixture-org/--fixture-nuget/--fixture-tags flags serve canned subjects). Mirrors
# tests/feed-coherence/run.sh and tests/kit-published-coherence/run.sh.

set -euo pipefail
export PYTHONDONTWRITEBYTECODE=1
export FSGG_RELEASE_COHERENCE_FIXTURE_OK=1

HERE="$(cd "$(dirname "$0")" && pwd)"
GATE="$HERE/../../scripts/check-release-coherence.py"

WORK="$(mktemp -d "${TMPDIR:-/tmp}/release-coherence-fixture.XXXXXX")"
trap 'rm -rf "$WORK"' EXIT

pass=0; failcount=0
ok()  { echo "PASS  $1"; pass=$((pass+1)); }
bad() { echo "FAIL  $1"; [ -n "${2:-}" ] && printf '%s\n' "$2" | sed 's/^/    | /'; failcount=$((failcount+1)); }

SHA_A="1111111111111111111111111111111111111111"   # the coordinated 0.23.0 release commit
SHA_B="2222222222222222222222222222222222222222"   # an OLDER coordinated release commit (0.22.0)
SHA_KIT_LONE="3333333333333333333333333333333333333333"     # Kit's own pre-.github#2402 history
SHA_DRIVERS_LONE="4444444444444444444444444444444444444444"
SHA_COORD_LONE="5555555555555555555555555555555555555555"

# The GREEN baseline: two fully-coherent trios (0.22.0, then 0.23.0 — the newer wins), plus each
# package's own pre-convention tag history (present but never shared across all three, so it is
# never a target). All three feeds serve 0.23.0.
BASE_TAGS="$WORK/tags-base.json"
cat > "$BASE_TAGS" <<JSON
{
  "kit/v":          {"0.22.0": "$SHA_B", "0.23.0": "$SHA_A", "0.49.0": "$SHA_KIT_LONE"},
  "drivers/v":       {"0.22.0": "$SHA_B", "0.23.0": "$SHA_A", "0.18.0": "$SHA_DRIVERS_LONE"},
  "coord-engine/v":  {"0.22.0": "$SHA_B", "0.23.0": "$SHA_A", "0.9.0":  "$SHA_COORD_LONE"}
}
JSON
BASE_ORG="$WORK/org-base.json"
cat > "$BASE_ORG" <<'JSON'
{
  "FS.GG.Kit":       ["0.22.0", "0.23.0", "0.49.0"],
  "FS.GG.Drivers":   ["0.18.0", "0.22.0", "0.23.0"],
  "FS.GG.Coord.Cli": ["0.9.0", "0.22.0", "0.23.0"]
}
JSON
BASE_NUGET="$WORK/nuget-base.json"
cp "$BASE_ORG" "$BASE_NUGET"

# with_json_version <src.json> <out-name> <top-key> <json-value> — copy, set one top-level key.
# Refuses a no-op mutation, which would test nothing.
with_json_version() {
  local out="$WORK/$2.json"
  python3 - "$1" "$out" "$3" "$4" <<'PY'
import sys, json
src, out, key, value = sys.argv[1:5]
d = json.load(open(src))
if key not in d:
    sys.exit(f"vacuous fixture: {key!r} is not in the base document")
new = json.loads(value)
if d[key] == new:
    sys.exit(f"vacuous fixture: {key} already is {new!r} — this mutation is a no-op")
d[key] = new
json.dump(d, open(out, "w"))
PY
  printf '%s' "$out"
}

without_key() {
  local out="$WORK/$2.json"
  python3 - "$1" "$out" "$3" <<'PY'
import sys, json
src, out, key = sys.argv[1:4]
d = json.load(open(src))
if key not in d:
    sys.exit(f"vacuous fixture: {key!r} is not in the base document")
del d[key]
json.dump(d, open(out, "w"))
PY
  printf '%s' "$out"
}

gate() { python3 "$GATE" --fixture-org "$1" --fixture-nuget "$2" --fixture-tags "$3" 2>&1; }

# must_pass <label> <org.json> <nuget.json> <tags.json>
must_pass() {
  local out rc
  out="$(gate "$2" "$3" "$4")" && rc=0 || rc=$?
  if [ "$rc" -eq 0 ]; then ok "$1"; else bad "$1 (expected exit 0, got $rc)" "$out"; fi
}

# must_fail <label> <org.json> <nuget.json> <tags.json> <required-pattern>
must_fail() {
  local out rc
  out="$(gate "$2" "$3" "$4")" && rc=0 || rc=$?
  if [ "$rc" -eq 0 ]; then
    bad "$1 (expected non-zero exit, got 0)" "$out"
  elif ! grep -Eqi -- "$5" <<<"$out"; then
    bad "$1 (failed, but not for the stated reason: /$5/)" "$out"
  else
    ok "$1"
  fi
}

echo "--- the green baseline: newest fully-coherent trio (0.23.0) served by all three, both feeds ---"
must_pass "all three serve the newest coordinated release on both feeds" "$BASE_ORG" "$BASE_NUGET" "$BASE_TAGS"
out="$(gate "$BASE_ORG" "$BASE_NUGET" "$BASE_TAGS")"
if grep -q "newest coordinated coherent-set release: 0.23.0" <<<"$out"; then
  ok "the NEWER of two fully-coherent trios (0.23.0, not 0.22.0) is chosen as the target"
else
  bad "the NEWER of two fully-coherent trios (0.23.0, not 0.22.0) is chosen as the target" "$out"
fi

echo
echo "--- the defect the gate was built for: precondition passed, one sibling's publish did not ---"
# All three release tags for 0.23.0 exist at the SAME commit (the precondition already proved that
# before any of the three could publish) — but Drivers's own "Push to feeds" steps never ran.
DRIVERS_STALL_ORG="$(with_json_version "$BASE_ORG" drivers-stall-org FS.GG.Drivers '["0.18.0", "0.22.0"]')"
DRIVERS_STALL_NUGET="$(with_json_version "$BASE_NUGET" drivers-stall-nuget FS.GG.Drivers '["0.18.0", "0.22.0"]')"
must_fail "Drivers's precondition passed but its own publish never completed" \
  "$DRIVERS_STALL_ORG" "$DRIVERS_STALL_NUGET" "$BASE_TAGS" \
  "FS\.GG\.Drivers:.*does not serve the coherent-set release '0\.23\.0'.*commit ${SHA_A:0:12}"

echo
echo "--- matching all three passes (the converse of the case above) ---"
must_pass "Drivers republishes and all three re-agree" "$BASE_ORG" "$BASE_NUGET" "$BASE_TAGS"

echo
echo "--- the OTHER half-completion: one package's own two feeds disagree (dual-publish gap) ---"
# coord-engine, not Kit: Kit's own PRE-CONVENTION history (0.49.0) already outranks the trio target,
# so dropping the target from one of Kit's feeds would not move ITS OWN newest and would test only
# the trio-completion leg above again. coord-engine's lone historical tag (0.9.0) sits BELOW the
# trio target, so its own newest already equals it in the baseline — dropping it from nuget.org here
# moves coord-engine's OWN newest, isolating the dual-publish comparison from trio completion.
COORD_NUGET_STALE="$(with_json_version "$BASE_NUGET" coord-nuget-stale FS.GG.Coord.Cli '["0.9.0", "0.22.0"]')"
must_fail "coord-engine reached the org feed but nuget.org's push did not complete" \
  "$BASE_ORG" "$COORD_NUGET_STALE" "$BASE_TAGS" \
  "FS\.GG\.Coord\.Cli: the org feed.*newest is '0\.23\.0' but nuget\.org newest is '0\.22\.0'"

echo
echo "--- the legitimate BOOTSTRAP state: no coordinated release has ever completed — never a red ---"
# Every package's tags predate the coherent-set convention: nothing is shared across all three at one
# commit, so there is no target. Each package's own two feeds still agree with each other, so this
# is a clean pass — the absence of a trio is reported, not treated as a failure.
NO_TRIO_TAGS="$WORK/tags-no-trio.json"
cat > "$NO_TRIO_TAGS" <<JSON
{
  "kit/v":          {"0.49.0": "$SHA_KIT_LONE"},
  "drivers/v":       {"0.18.0": "$SHA_DRIVERS_LONE"},
  "coord-engine/v":  {"0.9.0":  "$SHA_COORD_LONE"}
}
JSON
NO_TRIO_ORG="$WORK/org-no-trio.json"
cat > "$NO_TRIO_ORG" <<'JSON'
{"FS.GG.Kit": ["0.49.0"], "FS.GG.Drivers": ["0.18.0"], "FS.GG.Coord.Cli": ["0.9.0"]}
JSON
NO_TRIO_NUGET="$WORK/nuget-no-trio.json"
cp "$NO_TRIO_ORG" "$NO_TRIO_NUGET"
must_pass "no coordinated release yet is a clean pass, not a failure" \
  "$NO_TRIO_ORG" "$NO_TRIO_NUGET" "$NO_TRIO_TAGS"
out="$(gate "$NO_TRIO_ORG" "$NO_TRIO_NUGET" "$NO_TRIO_TAGS")"
if grep -q "no coordinated coherent-set tag release found yet" <<<"$out"; then
  ok "the bootstrap state names itself explicitly, so a silent pass is never mistaken for a check"
else
  bad "the bootstrap state names itself explicitly, so a silent pass is never mistaken for a check" "$out"
fi

echo
echo "--- a tag existing on only TWO of three names no target (no shared trio) — still a clean pass ---"
TWO_OF_THREE_TAGS="$(with_json_version "$BASE_TAGS" two-of-three-tags 'coord-engine/v' '{"0.22.0": "'"$SHA_B"'", "0.9.0": "'"$SHA_COORD_LONE"'"}')"
TWO_OF_THREE_ORG="$(with_json_version "$BASE_ORG" two-of-three-org FS.GG.Coord.Cli '["0.9.0", "0.22.0"]')"
TWO_OF_THREE_NUGET="$(with_json_version "$BASE_NUGET" two-of-three-nuget FS.GG.Coord.Cli '["0.9.0", "0.22.0"]')"
must_pass "the newest SHARED trio (0.22.0, since 0.23.0 lost coord-engine) is the target and it is met" \
  "$TWO_OF_THREE_ORG" "$TWO_OF_THREE_NUGET" "$TWO_OF_THREE_TAGS"

echo
echo "--- fails CLOSED: an absent/empty/prerelease-only/unreadable subject is an ERROR, not a skip ---"
must_fail "a package missing from the org feed (404) fails, not skips" \
  "$(without_key "$BASE_ORG" no404-org FS.GG.Kit)" "$BASE_NUGET" "$BASE_TAGS" \
  "not on the org feed|fixture: absent"
must_fail "a package the feed serves zero versions for fails" \
  "$(with_json_version "$BASE_ORG" empty-org FS.GG.Drivers '[]')" "$BASE_NUGET" "$BASE_TAGS" \
  "zero versions"
must_fail "a package with only prereleases on a feed fails (none of the three ever ships one)" \
  "$(with_json_version "$BASE_ORG" preonly-org FS.GG.Coord.Cli '["0.24.0-preview.1"]')" "$BASE_NUGET" "$BASE_TAGS" \
  "no stable version"
must_fail "an unreadable tag namespace (git ls-remote failure equivalent) fails" \
  "$BASE_ORG" "$BASE_NUGET" "$(without_key "$BASE_TAGS" no-tags-prefix 'drivers/v')" \
  "no canned tag data for prefix"

echo
echo "--- fails CLOSED on a missing token (no --fixture-*, live path) ---"
out="$(env -u GITHUB_TOKEN -u GH_TOKEN python3 "$GATE" 2>&1)" && rc=0 || rc=$?
if [ "${rc:-0}" -ne 0 ] && grep -q "no GITHUB_TOKEN" <<<"$out"; then
  ok "a missing token fails the gate rather than skipping it"
else
  bad "a missing token fails the gate rather than skipping it" "$out"
fi

echo
echo "--- --remote is refused when it names a different repository (offline: checked before any read) ---"
out="$(env GITHUB_TOKEN=dummy GITHUB_REPOSITORY=FS-GG/.github python3 "$GATE" --remote 'https://gitlab.com/someone/else.git' 2>&1)" && rc=0 || rc=$?
if [ "${rc:-0}" -ne 0 ] && grep -qi "is not github.com/FS-GG/.github" <<<"$out"; then
  ok "a --remote naming a different repository is refused before any network read"
else
  bad "a --remote naming a different repository is refused before any network read" "$out"
fi

echo
echo "--- fixture mode announces itself, requires ALL THREE flags together, and is locked to this harness ---"
if gate "$BASE_ORG" "$BASE_NUGET" "$BASE_TAGS" | grep -q "FIXTURE MODE"; then
  ok "fixture mode prints a banner"
else
  bad "fixture mode prints a banner"
fi
out="$(python3 "$GATE" --fixture-org "$BASE_ORG" --fixture-nuget "$BASE_NUGET" 2>&1)" && rc=0 || rc=$?
if [ "${rc:-0}" -ne 0 ] && grep -qi "must be given together" <<<"$out"; then
  ok "two of three fixture flags without the third refuses rather than reading one subject live"
else
  bad "two of three fixture flags without the third refuses rather than reading one subject live" "$out"
fi
out="$(env -u FSGG_RELEASE_COHERENCE_FIXTURE_OK python3 "$GATE" --fixture-org "$BASE_ORG" --fixture-nuget "$BASE_NUGET" --fixture-tags "$BASE_TAGS" 2>&1)" && rc=0 || rc=$?
if [ "${rc:-0}" -ne 0 ] && grep -q "Refusing to run" <<<"$out"; then
  ok "fixture flags refuse to run without FSGG_RELEASE_COHERENCE_FIXTURE_OK"
else
  bad "fixture flags refuse to run without FSGG_RELEASE_COHERENCE_FIXTURE_OK" "$out"
fi

echo
echo "$pass passed, $failcount failed."
[ "$failcount" -eq 0 ]
