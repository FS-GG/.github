#!/usr/bin/env bash
# Fixture for scripts/check-feed-coherence.py — the gate that compares the registry's
# `package-version` against the newest version live on the org feed (.github#267, epic #266).
#
# The gate exists because a check that passes when its subject is missing manufactures confidence.
# So this fixture spends most of its length on the FAILURE legs: it proves the gate goes red when
# the registry is behind the feed, ahead of it, matched by substring, pointed at a 404, handed an
# empty feed, or handed a package-bearing contract nobody mapped.
#
# Every negative leg asserts the REASON, not just a non-zero exit — the .github#266 vacuous-failure
# defect (SDD#299) was a "must fail" test whose non-zero exit came from a path guard rather than
# from the thing under test. `must_fail` therefore takes a required pattern, and `mutate` refuses a
# no-op edit, so a fixture that stops exercising its own claim breaks loudly instead of passing.
#
# Throwaway trees under a temp dir, no network (the gate's --fixture flag serves a canned feed).
# Mirrors tests/repos-registry/run.sh.

set -euo pipefail

# The suite imports the gate by path (importlib), which would otherwise litter scripts/__pycache__
# into a repo that has no .gitignore.
export PYTHONDONTWRITEBYTECODE=1

# `--fixture` is locked to this harness: the gate refuses a canned feed unless this is set, so a
# stray `--fixture` in CI fails rather than silently reporting green. See the gate's docstring.
export FSGG_FEED_FIXTURE_OK=1

HERE="$(cd "$(dirname "$0")" && pwd)"
GATE="$HERE/../../scripts/check-feed-coherence.py"
REPO_ROOT="$(cd "$HERE/../.." && pwd)"

WORK="$(mktemp -d "${TMPDIR:-/tmp}/feed-coherence-fixture.XXXXXX")"
trap 'rm -rf "$WORK"' EXIT

pass=0; failcount=0
ok()  { echo "PASS  $1"; pass=$((pass+1)); }
bad() { echo "FAIL  $1"; [ -n "${2:-}" ] && printf '%s\n' "$2" | sed 's/^/    | /'; failcount=$((failcount+1)); }

# A well-formed registry naming every contract the gate maps, and a feed that serves each of them
# at exactly the declared version. This is the GREEN baseline; every case below mutates one thing.
BASE="$WORK/base.yml"
cat > "$BASE" <<'YAML'
schemaVersion: 1
updated: "2026-07-09"
contracts:
  - { id: fsgg-contracts,                version: "1.4.0",           package-version: "1.4.0" }
  - { id: governance-reference-gate-set, version: "1.2.1.1",         package-version: "1.2.1.1" }
  - { id: fs-gg-ui-template,             version: "0.4.0",           package-version: "0.4.0" }
  - { id: game-sim-core,                 version: "0.2.0",           package-version: "0.2.0" }
  - { id: game-scene-adapter,            version: "0.2.0",           package-version: "0.2.0" }
  - { id: fs-gg-audio,                   version: "0.1.0-preview.1", package-version: "0.1.0-preview.1" }
  # .github's own engine (.github#1067). Carried here because the gate's ORPHAN check is live: a
  # CONTRACT_PACKAGES entry with no contract in the registry under test is an error, so every real
  # mapping must appear in this synthetic registry too.
  - { id: coord-engine,                  version: "0.3.0",           package-version: "0.3.0" }
  # .github's second producer (.github#1067 → SDD#508 → .github#1114). Carried here for the same
  # ORPHAN-check reason as coord-engine above: a CONTRACT_PACKAGES mapping with no contract in the
  # registry under test is an error, so every real mapping appears in this synthetic registry too.
  - { id: new-sdd-workspace,             version: "0.3.0",           package-version: "0.3.0" }
  # FS.GG.Net's six-package coherent set (ADR-0052). Same ORPHAN-check reason: every CONTRACT_PACKAGES
  # mapping must have a contract in the registry under test.
  - { id: fs-gg-net,                     version: "0.1.0",           package-version: "0.1.0" }
  # .github#2070/#2639: FS.GG.Templates' renamed package (FS.GG.Workspace.Template) and the Game
  # and Rendering owner-sourced skill-delivery packages. Same ORPHAN-check reason as the rows above.
  - { id: fs-gg-workspace-template,      version: "0.8.0",           package-version: "0.8.0" }
  - { id: game-skills,                   version: "0.7.0",           package-version: "0.7.0" }
  - { id: rendering-skills,              version: "0.1.0",           package-version: "0.1.0" }
  - { id: shared-build-config,           version: "1.0.0" }
YAML

# The feed. FS.GG.UI.Template deliberately serves BOTH 0.4.0 and 0.4.0-preview.1 (the substring
# trap), and lists them out of version order (the feed returns creation order, not version order).
FEED="$WORK/feed.json"
cat > "$FEED" <<'JSON'
{
  "FS.GG.Contracts":                   ["1.2.0", "1.4.0", "1.1.1", "1.0.1"],
  "FS.GG.Governance.ReferenceGateSet": ["1.2.1.1"],
  "FS.GG.UI.Template":                 ["0.4.0", "0.4.0-preview.1", "0.3.1-preview.1", "0.2.0-preview.1"],
  "FS.GG.Game.Core":                   ["0.2.0", "0.1.0-preview.1"],
  "FS.GG.Game.Render":                 ["0.2.0", "0.1.0-preview.1"],
  "FS.GG.Audio.Core":                  ["0.1.0-preview.1"],
  "FS.GG.Audio.Host":                  ["0.1.0-preview.1"],
  "FS.GG.Audio.Engine":                ["0.1.0-preview.1"],
  "FS.GG.Audio.Elmish":                ["0.1.0-preview.1"],
  "FS.GG.Coord.Cli":                   ["0.3.0", "0.2.0", "0.1.1", "0.1.0"],
  "FS.GG.NewSddWorkspace":             ["0.3.0", "0.3.0-preview.1"],
  "FS.GG.Net.Core":                    ["0.1.0"],
  "FS.GG.Net.WebSocket":               ["0.1.0"],
  "FS.GG.Net.WebSocket.Server":        ["0.1.0"],
  "FS.GG.Net.Protobuf":                ["0.1.0"],
  "FS.GG.Net.Grpc":                    ["0.1.0"],
  "FS.GG.Net.Elmish":                  ["0.1.0"],
  "FS.GG.Workspace.Template":          ["0.8.0"],
  "FS.GG.Game.Skills":                 ["0.7.0"],
  "FS.GG.Rendering.Skills":            ["0.1.0"]
}
JSON

# mutate <name> <contract-id> <new-package-version> — copy BASE, set one package-version, echo path.
# Refuses a no-op: a mutation that changes nothing yields a fixture that tests nothing.
mutate() {
  local out="$WORK/$1.yml"
  python3 - "$BASE" "$out" "$2" "$3" <<'PY'
import sys, yaml
src, out, cid, new = sys.argv[1:5]
doc = yaml.safe_load(open(src))
hit = [c for c in doc["contracts"] if c.get("id") == cid]
if not hit:
    sys.exit(f"vacuous fixture: contract {cid!r} is not in the base registry")
if str(hit[0].get("package-version")) == new:
    sys.exit(f"vacuous fixture: {cid}.package-version is already {new!r} — this mutation is a no-op")
hit[0]["package-version"] = new
yaml.safe_dump(doc, open(out, "w"))
PY
  printf '%s' "$out"
}

# feed_without <name> <package-id> — copy FEED, drop one package (simulates a 404).
# Refuses to drop a package that was never there, which would test nothing.
feed_without() {
  local out="$WORK/$1.json"
  python3 - "$FEED" "$out" "$2" <<'PY'
import sys, json
src, out, pkg = sys.argv[1:4]
d = json.load(open(src))
if pkg not in d:
    sys.exit(f"vacuous fixture: {pkg!r} is not in the base feed")
del d[pkg]
json.dump(d, open(out, "w"))
PY
  printf '%s' "$out"
}

# feed_with <name> <package-id> <json-version-list> — copy FEED, replace one package's versions.
feed_with() {
  local out="$WORK/$1.json"
  python3 - "$FEED" "$out" "$2" "$3" <<'PY'
import sys, json
src, out, pkg, versions = sys.argv[1:5]
d = json.load(open(src))
if pkg not in d:
    sys.exit(f"vacuous fixture: {pkg!r} is not in the base feed")
new = json.loads(versions)
if d[pkg] == new:
    sys.exit(f"vacuous fixture: {pkg} already serves {new!r} — this mutation is a no-op")
d[pkg] = new
json.dump(d, open(out, "w"))
PY
  printf '%s' "$out"
}

gate() { python3 "$GATE" --fixture "$2" --fixture-nuget "${3:-$2}" "$1" 2>&1; }

# must_pass <label> <registry> <feed>
must_pass() {
  local out rc
  out="$(gate "$2" "$3")" && rc=0 || rc=$?
  if [ "$rc" -eq 0 ]; then ok "$1"; else bad "$1 (expected exit 0, got $rc)" "$out"; fi
}

# must_fail <label> <registry> <feed> <required-pattern>
# The pattern is REQUIRED: exit 1 alone does not prove the gate failed for the reason claimed.
must_fail() {
  local out rc
  out="$(gate "$2" "$3")" && rc=0 || rc=$?
  if [ "$rc" -eq 0 ]; then
    bad "$1 (expected non-zero exit, got 0)" "$out"
  elif ! grep -Eqi -- "$4" <<<"$out"; then
    bad "$1 (failed, but not for the stated reason: /$4/)" "$out"
  else
    ok "$1"
  fi
}

# must_fail_dual <label> <registry> <org-feed> <nuget-feed> <required-pattern>
must_fail_dual() {
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

echo "--- the green baseline ---"
must_pass "coherent registry == newest on the feed" "$BASE" "$FEED"

echo
echo "--- the drift the gate was built for (both directions) ---"
must_fail "BEHIND: a release published, the registry was never flipped" \
  "$(mutate behind fs-gg-ui-template 0.3.1-preview.1)" "$FEED" "BEHIND both feeds"
must_fail "AHEAD: registry advertises a version consumers cannot restore" \
  "$(mutate ahead fsgg-contracts 9.9.9)" "$FEED" "AHEAD of the feed"

# The live .github#2580 shape: org accepted 0.52.0, nuget.org did not. The old gate called this
# BEHIND and instructed an unsafe registry flip. The repaired gate must name the incomplete release.
PARTIAL_NUGET="$(feed_with partial-nuget FS.GG.Coord.Cli '["0.2.0", "0.1.1", "0.1.0"]')"
must_fail_dual "org-ahead/nuget-behind is a PARTIAL RELEASE, never registry BEHIND" \
  "$BASE" "$FEED" "$PARTIAL_NUGET" "PARTIAL RELEASE.*do not flip the registry"

# Prove the new arm did not disable the old one: when BOTH feeds have advanced, this really is a
# completed release whose registry row is behind and should still demand publish-before-flip step 2.
must_fail_dual "a completed dual-feed release still reports registry BEHIND" \
  "$(mutate both-behind coord-engine 0.2.0)" "$FEED" "$FEED" "BEHIND both feeds"

echo
echo "--- version ORDER, never substring (the .github#268 defect class) ---"
# 0.4.0 is a substring of 0.4.0-preview.1. A substring check calls this coherent; it is not.
# The baseline (which declares 0.4.0 against a feed serving both) is the converse leg: a release
# must not be reported as behind its own prerelease.
must_fail "0.4.0-preview.1 declared while the feed's newest is 0.4.0" \
  "$(mutate substring fs-gg-ui-template 0.4.0-preview.1)" "$FEED" "BEHIND both feeds.*0\.4\.0"

# The feed returns creation order. Newest must come from version order, so the same set in a
# different order must reach the same verdict — and a newest that is not first must still win.
must_pass "newest is chosen by version order, not by the feed's ordering" \
  "$BASE" "$(feed_with reordered FS.GG.Contracts '["1.0.1", "1.1.1", "1.4.0", "1.2.0"]')"
must_fail "a stale declaration is caught even when the feed lists newest last" \
  "$(mutate order-stale fsgg-contracts 1.2.0)" \
  "$(feed_with tail-newest FS.GG.Contracts '["1.0.1", "1.2.0", "1.4.0"]')" "BEHIND both feeds"

# A prerelease-only package is coherent at its newest prerelease (fs-gg-audio's real shape).
must_pass "a prerelease-only feed is coherent at its newest prerelease" \
  "$BASE" "$(feed_with pre-only FS.GG.Audio.Core '["0.1.0-preview.1", "0.0.9-preview.1"]')"
# A 4-segment NuGet version (governance-reference-gate-set, ADR-0007) is not SemVer, and must
# still order correctly against a 3-segment sibling.
must_fail "4-segment 1.2.1.1 orders above 1.2.1 (not string-compared)" \
  "$(mutate four-seg governance-reference-gate-set 1.2.1)" \
  "$FEED" "BEHIND both feeds.*1\.2\.1\.1"

echo
echo "--- fails CLOSED: an absent/unreadable/unmapped subject is an ERROR, not a skip ---"
must_fail "a package missing from the feed (404) fails, not skips" \
  "$BASE" "$(feed_without no404 FS.GG.Game.Core)" "not on the org feed|fixture: absent"
must_fail "a package the feed serves zero versions for fails" \
  "$BASE" "$(feed_with empty FS.GG.Audio.Host '[]')" "zero versions"
# fs-gg-audio's four packages ship as one set at one version; a partial publish must be reported
# rather than hidden behind .Core, which is why every member is compared.
must_fail "a partial coherent-set publish is reported (one member stale)" \
  "$BASE" "$(feed_with partial FS.GG.Audio.Elmish '["0.0.9"]')" "AHEAD of the feed"

# A new package-bearing contract nobody mapped is the next unchecked subject. It must be loud.
UNMAPPED="$WORK/unmapped.yml"
python3 - "$BASE" "$UNMAPPED" <<'PY'
import sys, yaml
doc = yaml.safe_load(open(sys.argv[1]))
doc["contracts"].append({"id": "brand-new-contract", "version": "1.0.0", "package-version": "1.0.0"})
yaml.safe_dump(doc, open(sys.argv[2], "w"))
PY
must_fail "a package-bearing contract with no mapped package id fails" \
  "$UNMAPPED" "$FEED" "no package id is mapped"

# ...and the reverse: a mapping whose contract vanished is stale, and stale mappings hide subjects.
ORPHAN="$WORK/orphan.yml"
python3 - "$BASE" "$ORPHAN" <<'PY'
import sys, yaml
doc = yaml.safe_load(open(sys.argv[1]))
doc["contracts"] = [c for c in doc["contracts"] if c.get("id") != "fs-gg-audio"]
yaml.safe_dump(doc, open(sys.argv[2], "w"))
PY
must_fail "a CONTRACT_PACKAGES entry with no registry contract fails" \
  "$ORPHAN" "$FEED" "stale mapping"

# ...and the state BETWEEN those two, which had no check at all until .github#2567. The row is STILL
# PRESENT, so its id is `known` and `stale_mappings` does not see it; its `package-version` is gone,
# so `subjects` does not see it either. It left BOTH halves' scope in one edit. Measured on the
# pre-fix gate (3fd4951, this fixture's own BASE with coord-engine's key deleted): exit 0, "comparing
# 10 package-bearing contract(s)" instead of 11, the closing `ok:` line printed, and the string
# `coord-engine` absent from the output entirely — the gate reported success while checking less.
#
# The pattern requires the ROW'S ID and the reason together: a non-zero exit that did not name the
# row would not tell an operator which row stopped being checked, which is criterion 1.
KEYLESS="$WORK/keyless.yml"
python3 - "$BASE" "$KEYLESS" <<'PY'
import sys, yaml
doc = yaml.safe_load(open(sys.argv[1]))
row = next(c for c in doc["contracts"] if c.get("id") == "coord-engine")
if "package-version" not in row:
    sys.exit("vacuous fixture: coord-engine already carries no `package-version` in BASE")
del row["package-version"]
yaml.safe_dump(doc, open(sys.argv[2], "w"))
PY
must_fail "a MAPPED row still present but stripped of package-version fails" \
  "$KEYLESS" "$FEED" "coord-engine.*leaves BOTH scopes at once"

echo
echo "--- the subject COUNT is not evidence: assert the SET's IDENTITY (.github#2567 criterion 5) ---"
# Construct the world a count cannot see: one MAPPED row loses its `package-version` and a different
# row GAINS one. Eleven subjects before, eleven after. A fixture asserting "the gate compares 11
# package-bearing contracts" ratifies it, and both the dropped row (never compared again) and the
# arrived row (compared against a package nobody mapped) are exactly the epic #266 unchecked subject.
#
# This is the item's own failure mode in miniature, which is why it gets a leg rather than a comment:
# the original defect was two checks agreeing on a NUMBER of rows while disagreeing about WHICH.
SWAP="$WORK/swap.yml"
python3 - "$BASE" "$SWAP" <<'PY'
import sys, yaml
doc = yaml.safe_load(open(sys.argv[1]))
gone = next(c for c in doc["contracts"] if c.get("id") == "coord-engine")
arrived = next(c for c in doc["contracts"] if c.get("id") == "shared-build-config")
if "package-version" not in gone or "package-version" in arrived:
    sys.exit("vacuous fixture: BASE is not the shape this swap assumes")
del gone["package-version"]
arrived["package-version"] = "1.0.0"
yaml.safe_dump(doc, open(sys.argv[2], "w"))
PY

# First prove the premise rather than asserting it: the counts really are equal and the sets really
# do differ. A leg that skipped this could be testing a swap that silently changed the count, and
# would then prove nothing about counting.
if python3 - "$BASE" "$SWAP" "$GATE" <<'PY'
import importlib.util, sys, yaml
spec = importlib.util.spec_from_file_location("gate", sys.argv[3])
gate = importlib.util.module_from_spec(spec); spec.loader.exec_module(gate)
ids = lambda p: sorted(str(c.get("id", "")).strip()
                       for c in gate.subjects(yaml.safe_load(open(p))["contracts"]))
base, swap = ids(sys.argv[1]), ids(sys.argv[2])
if len(base) != len(swap):
    sys.exit(f"vacuous fixture: the swap changed the COUNT ({len(base)} -> {len(swap)}); this leg "
             f"only means something while the count is blind to it")
if base == swap:
    sys.exit("vacuous fixture: the swap did not change the subject SET either")
if "coord-engine" not in base or "coord-engine" in swap:
    sys.exit(f"vacuous fixture: coord-engine did not leave the subject set: {base} -> {swap}")
if "shared-build-config" in base or "shared-build-config" not in swap:
    sys.exit(f"vacuous fixture: shared-build-config did not enter the subject set: {base} -> {swap}")
PY
then ok "the swap keeps the subject COUNT identical while changing the subject SET"
else bad "the swap keeps the subject COUNT identical while changing the subject SET"
fi

# And now the gate on that same registry. It must red for the DEPARTED row specifically — the
# arrived row raises its own (pre-existing) unmapped-subject error, and accepting that one as the
# verdict would let this leg pass on a gate that still cannot see a row leave.
must_fail "a count-preserving subject swap fails, naming the row that LEFT" \
  "$SWAP" "$FEED" "coord-engine.*leaves BOTH scopes at once"

echo
echo "--- why a row can no longer fall between the two checks: the partition is exhaustive ---"
# The claim in classify_mappings' docstring, asserted rather than trusted: every id in
# CONTRACT_PACKAGES lands in exactly one of checked/unkeyed/stale — none in two, none in none — so
# the three buckets are a permutation of the map's keys under every registry shape, including the two
# damaged ones and both damages at once. That is the structural reason the .github#2567 gap cannot
# reopen: it existed because two checks asked different questions and neither owned the leftovers.
if python3 - "$BASE" "$KEYLESS" "$ORPHAN" "$GATE" <<'PY'
import os, sys, yaml
sys.path.insert(0, os.path.dirname(os.path.abspath(sys.argv[4])))
import registry_packages as rp

def rows(path):
    return yaml.safe_load(open(path))["contracts"]

both = [c for c in rows(sys.argv[2]) if c.get("id") != "fs-gg-audio"]   # key removed AND row removed
shapes = {"healthy": rows(sys.argv[1]), "key-removed": rows(sys.argv[2]),
          "row-removed": rows(sys.argv[3]), "both": both}
keys = set(rp.CONTRACT_PACKAGES)
for name, contracts in shapes.items():
    checked, unkeyed, stale = rp.classify_mappings(contracts)
    buckets = checked + unkeyed + stale
    if sorted(buckets) != sorted(keys):
        sys.exit(f"{name}: buckets are not a permutation of CONTRACT_PACKAGES: {sorted(buckets)}")
    if len(buckets) != len(set(buckets)):
        sys.exit(f"{name}: an id landed in more than one bucket: {buckets}")
    # `checked` must agree with subjects() exactly — the two must not be able to disagree again.
    subject_ids = {str(c.get("id", "")).strip() for c in rp.subjects(contracts)}
    if set(checked) != subject_ids & keys:
        sys.exit(f"{name}: classify_mappings' `checked` disagrees with subjects(): "
                 f"{sorted(checked)} vs {sorted(subject_ids & keys)}")
    # The two named accessors are thin views onto this classifier, and a view that drifted from what
    # it views would put the module right back to two checks answering differently.
    if rp.unkeyed_subjects(contracts) != unkeyed:
        sys.exit(f"{name}: unkeyed_subjects() disagrees with classify_mappings' unkeyed bucket")
    if rp.stale_mappings(contracts) != stale:
        sys.exit(f"{name}: stale_mappings() disagrees with classify_mappings' stale bucket")
    # ...and the message builders must name exactly those ids, since the messages are what an
    # operator acts on. A refusal that named the wrong row would be worse than the silence.
    if len(rp.unkeyed_problems(contracts)) != len(unkeyed) or \
       any(cid not in msg for cid, msg in zip(unkeyed, rp.unkeyed_problems(contracts))):
        sys.exit(f"{name}: unkeyed_problems() does not name exactly {unkeyed}")
    if rp.mapping_problems(contracts) != rp.unkeyed_problems(contracts) + rp.stale_problems(contracts):
        sys.exit(f"{name}: mapping_problems() is not the concatenation of its two halves")
# And the damaged shapes must actually populate the error buckets, or the loop above proved nothing.
if rp.classify_mappings(shapes["key-removed"])[1] != ["coord-engine"]:
    sys.exit("the key-removed shape did not produce exactly the coord-engine unkeyed bucket")
if rp.classify_mappings(shapes["row-removed"])[2] != ["fs-gg-audio"]:
    sys.exit("the row-removed shape did not produce exactly the fs-gg-audio stale bucket")
if rp.classify_mappings(shapes["both"])[1:] != (["coord-engine"], ["fs-gg-audio"]):
    sys.exit("the doubly-damaged shape did not report BOTH kinds at once")
# The healthy shape must produce NO messages at all — otherwise every leg above could be satisfied
# by a classifier that reports everything, and the gate would red on a clean registry.
if rp.mapping_problems(shapes["healthy"]):
    sys.exit(f"the healthy shape produced messages: {rp.mapping_problems(shapes['healthy'])}")
PY
then ok "every mapped id lands in exactly one of checked/unkeyed/stale, under all four shapes"
else bad "every mapped id lands in exactly one of checked/unkeyed/stale, under all four shapes"
fi

# An unparsable literal must never be silently treated as "no opinion".
must_fail "an unparsable registry version literal fails" \
  "$(mutate junk game-sim-core 'not-a-version')" "$FEED" "cannot parse version"

# An UNQUOTED version is YAML-coerced (1.10 -> the float 1.1) before the gate sees it, so the
# literal compared would not be the one written. Require the quotes. Built from BASE (not a minimal
# registry) so the orphan-mapping check does not fire first and mask what this case is testing.
UNQUOTED="$WORK/unquoted.yml"
sed 's/\(id: game-sim-core.*\)package-version: "0.2.0"/\1package-version: 1.10/' "$BASE" > "$UNQUOTED"
python3 - "$UNQUOTED" <<'PY'
import sys, yaml
doc = yaml.safe_load(open(sys.argv[1]))
got = next(c["package-version"] for c in doc["contracts"] if c["id"] == "game-sim-core")
if isinstance(got, str):
    sys.exit(f"vacuous fixture: the sed did not unquote the literal (YAML gave the str {got!r})")
if str(got) != "1.1":
    sys.exit(f"vacuous fixture: expected YAML to coerce 1.10 -> 1.1, got {got!r}")
PY
must_fail "an unquoted (YAML-coerced) package-version fails" \
  "$UNQUOTED" "$FEED" "not a quoted string"

# A registry with no package-version at all is malformed or the wrong file — not "coherent".
EMPTY="$WORK/nosubjects.yml"
printf 'schemaVersion: 1\ncontracts:\n  - { id: shared-build-config, version: "1.0.0" }\n' > "$EMPTY"
must_fail "a registry with zero package-bearing contracts fails" \
  "$EMPTY" "$FEED" "no contract in the registry carries"

echo
echo "--- the live feed reader (stubbed transport; --fixture never reaches this code) ---"
# Not `python3 … | sed`: the pipeline would report sed's exit status and this leg would pass
# unconditionally — the fail-open shape the gate itself exists to prevent.
reader_out="$(python3 "$HERE/feed_reader_cases.py" "$GATE" 2>&1)" && reader_rc=0 || reader_rc=$?
printf '%s\n' "$reader_out" | sed 's/^/  /'
if [ "$reader_rc" -eq 0 ]; then
  ok "the live reader fails closed on 401/403/404/500/network/empty/malformed"
else
  bad "the live reader fails closed on 401/403/404/500/network/empty/malformed"
fi

echo
echo "--- fails CLOSED on an unreadable feed (no --fixture, no token) ---"
out="$(env -u GITHUB_TOKEN -u GH_TOKEN python3 "$GATE" "$BASE" 2>&1)" && rc=0 || rc=$?
if [ "${rc:-0}" -ne 0 ] && grep -q "no GITHUB_TOKEN" <<<"$out"; then
  ok "a missing token fails the gate rather than skipping it"
else
  bad "a missing token fails the gate rather than skipping it" "$out"
fi

echo
echo "--- fixture mode announces itself, and is locked to this harness ---"
if gate "$BASE" "$FEED" | grep -q "FIXTURE MODE"; then
  ok "fixture mode prints a banner"
else
  bad "fixture mode prints a banner"
fi
# A --fixture that anyone could pass would be a supported way to make the gate report green
# without reading the feed. Outside this harness it must refuse.
out="$(env -u FSGG_FEED_FIXTURE_OK python3 "$GATE" --fixture "$FEED" "$BASE" 2>&1)" && rc=0 || rc=$?
if [ "${rc:-0}" -ne 0 ] && grep -q "Refusing to run" <<<"$out"; then
  ok "--fixture refuses to run without FSGG_FEED_FIXTURE_OK"
else
  bad "--fixture refuses to run without FSGG_FEED_FIXTURE_OK" "$out"
fi

echo
echo "--- CI guard on the real registry (no network: mapping completeness only) ---"
# The live comparison runs in feed-coherence.yml. Here we assert only what is checkable offline:
# every package-bearing contract in the CHECKED-IN registry has a package id mapped. This is the
# leg that catches "someone added a contract and the gate silently stopped covering it".
#
# .github#2567 ALSO closed this leg's own copy of the defect. It compared `subjects - map` (nothing
# unmapped) and `map - PRESENT ids` (nothing orphaned) — two conditions that a mapped row present
# WITHOUT a `package-version` satisfies BOTH of, because it is in `present` and absent from
# `subjects`. So the guard on the real registry had exactly the hole the gate had, and could not have
# caught the key going missing from the file it is pointed at. The assertion is now the SET EQUALITY
# `subject ids == CONTRACT_PACKAGES keys` — identity, not size, so swapping one row for another is
# caught too — with the three directions still reported separately so a failure says which happened.
if python3 - "$REPO_ROOT/registry/dependencies.yml" "$GATE" <<'PY'
import importlib.util, os, sys, yaml
spec = importlib.util.spec_from_file_location("gate", sys.argv[2])
gate = importlib.util.module_from_spec(spec); spec.loader.exec_module(gate)
sys.path.insert(0, os.path.dirname(os.path.abspath(sys.argv[2])))
import registry_packages as rp

doc = yaml.safe_load(open(sys.argv[1], encoding="utf-8"))
contracts = doc.get("contracts") or []
subjects = {str(c["id"]).strip() for c in contracts if c.get("package-version") is not None}
if not subjects:
    sys.exit("the checked-in registry has no package-bearing contracts")
checked, unkeyed, stale = rp.classify_mappings(contracts)
missing = sorted(subjects - set(gate.CONTRACT_PACKAGES))
if missing:
    sys.exit(f"unmapped package-bearing contract(s): {missing}")
if unkeyed:
    sys.exit(f"mapped contract(s) present in the registry but carrying no `package-version` — "
             f"neither a subject nor a stale mapping, the .github#2567 state: {unkeyed}")
if stale:
    sys.exit(f"stale CONTRACT_PACKAGES entr(ies): {stale}")
if subjects != set(gate.CONTRACT_PACKAGES):
    sys.exit(f"the subject set and the mapping's key set are not IDENTICAL: "
             f"only-subject={sorted(subjects - set(gate.CONTRACT_PACKAGES))} "
             f"only-mapped={sorted(set(gate.CONTRACT_PACKAGES) - subjects)}")
for c in contracts:
    if c.get("package-version") is not None:
        gate.parse_version(str(c["package-version"]))   # every declared literal must parse
PY
then ok "registry/dependencies.yml: the subject set is IDENTICAL to CONTRACT_PACKAGES, and parses"
else bad "registry/dependencies.yml: the subject set is IDENTICAL to CONTRACT_PACKAGES, and parses"
fi

echo
echo "--- P4 Typed SDD registry contract and gate-inversion controls ---"
# This is the release-specific acceptance guard: feed coherence proves package reality, while this
# guard proves the additive P4 identities/default/vocabulary that a schema-valid YAML mutation could
# otherwise change silently. Every negative case mutates a throwaway copy and must fail for its named
# field, so a vacuous or wrong-subject failure cannot satisfy the control.
p4_contract() {
  python3 - "$1" <<'PY'
import sys
import yaml

doc = yaml.safe_load(open(sys.argv[1], encoding="utf-8"))
rows = [row for row in doc.get("contracts", []) if row.get("id") == "fs-gg-ui-template"]
if len(rows) != 1:
    raise SystemExit(f"expected exactly one fs-gg-ui-template row, found {len(rows)}")
row = rows[0]
expected = {
    "version": "0.28.0",
    "package-version": "0.28.0",
    "package-tag": "fs-gg-ui-template/v0.28.0",
}
for key, value in expected.items():
    if str(row.get(key)) != value:
        raise SystemExit(f"P4 identity mismatch: {key}={row.get(key)!r}, expected {value!r}")
floor = row.get("minimum-fsgg-sdd") or {}
if str(floor.get("version")) != "1.4.0-preview.1":
    raise SystemExit(f"P4 floor mismatch: {floor.get('version')!r}, expected '1.4.0-preview.1'")
lifecycle = ((row.get("parameters") or {}).get("lifecycle") or {})
if lifecycle.get("type") != "choice (spec-kit|sdd|typed-sdd|none)":
    raise SystemExit(f"P4 lifecycle vocabulary mismatch: {lifecycle.get('type')!r}")
if lifecycle.get("default") != "sdd":
    raise SystemExit(f"P4 default mismatch: {lifecycle.get('default')!r}, expected 'sdd'")
PY
}

p4_mutation() {
  local name="$1" field="$2" value="$3" needle="$4"
  local target="$WORK/p4-$name.yml" out rc=0
  python3 - "$REPO_ROOT/registry/dependencies.yml" "$target" "$field" "$value" <<'PY'
import sys
import yaml

src, target, field, value = sys.argv[1:5]
doc = yaml.safe_load(open(src, encoding="utf-8"))
row = next(row for row in doc["contracts"] if row.get("id") == "fs-gg-ui-template")
if field in {"default", "type"}:
    subject = row["parameters"]["lifecycle"]
elif field == "floor":
    subject, field = row["minimum-fsgg-sdd"], "version"
else:
    subject = row
if str(subject.get(field)) == value:
    raise SystemExit(f"vacuous P4 mutation: {field} is already {value!r}")
subject[field] = value
yaml.safe_dump(doc, open(target, "w", encoding="utf-8"), sort_keys=False)
PY
  out="$(p4_contract "$target" 2>&1)" || rc=$?
  if [ "$rc" -ne 0 ] && grep -qF "$needle" <<<"$out"; then
    ok "P4 $name mutation makes the registry contract guard red"
  else
    bad "P4 $name mutation makes the registry contract guard red" "rc=$rc output=$out"
  fi
}

if p4_contract "$REPO_ROOT/registry/dependencies.yml"; then
  ok "P4 registry identities, lifecycle vocabulary, floor, and Standard SDD default agree"
else
  bad "P4 registry identities, lifecycle vocabulary, floor, and Standard SDD default agree"
fi
p4_mutation wrong-default default typed-sdd "P4 default mismatch"
p4_mutation lifecycle-loss type 'choice (spec-kit|sdd|none)' "P4 lifecycle vocabulary mismatch"
p4_mutation rendering-identity version 0.27.0 "P4 identity mismatch"
p4_mutation orchestrator-floor floor 1.3.0-preview.3 "P4 floor mismatch"

echo
echo "--- CI guard on hand-authored prose (.github#2070 repair round 3): does the prose that names"
echo "    fs-gg-workspace-template/game-skills versions still agree with the registry's corrected"
echo "    pins, rather than the pre-repair 0.8.0/0.7.0 this item shipped for two commits? ---"
# Round 1 of #2070 pinned fs-gg-workspace-template/game-skills at the wrong field's discipline
# (consumer-adopted, not feed-newest); round 3 corrected the registry rows to 0.8.1/0.8.0 but left
# docs/architecture.md and profile/README.md asserting the superseded 0.8.0/0.7.0 for two more
# commits — a silent self-contradiction three lines from architecture.md's own generated versions
# table, exactly the failure class closed issue .github#913 predicted for this kind of hand-authored
# site. Checks the REAL committed files, not a synthetic fixture (same shape as the mapping-
# completeness leg immediately above), so a future version bump that forgets the prose sites reds
# here rather than only in a human reviewer's eye.
ARCH="$REPO_ROOT/docs/architecture.md"
PROFILE_README="$REPO_ROOT/profile/README.md"
if grep -qF 'FS.GG.Workspace.Template` 0.9.0' "$ARCH" \
  && grep -qF 'package-version` **0.9.0**' "$ARCH" \
  && grep -qF 'package-version` **0.8.0**' "$ARCH" \
  && ! grep -qF 'FS.GG.Workspace.Template` 0.8.0 package' "$ARCH" \
  && ! grep -qF 'FS.GG.Game.Skills` 0.7.0 owner package' "$ARCH" \
  && ! grep -qF 'coherent release pending' "$PROFILE_README" \
  && [ "$(grep -c registry-active "$PROFILE_README")" -ge 4 ] \
  && grep -qF 'pinning `FS.GG.Workspace.Template` 0.8.0' "$ARCH" \
  && grep -qF 'its consumed version' "$ARCH"
then ok "docs/architecture.md and profile/README.md name the corrected fs-gg-workspace-template/game-skills pins"
else bad "docs/architecture.md and profile/README.md name the corrected fs-gg-workspace-template/game-skills pins"
fi

echo
echo "$pass passed, $failcount failed."
[ "$failcount" -eq 0 ]
