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
  "FS.GG.Coord.Cli":                   ["0.3.0", "0.2.0", "0.1.1", "0.1.0"]
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

gate() { python3 "$GATE" --fixture "$2" "$1" 2>&1; }

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

echo "--- the green baseline ---"
must_pass "coherent registry == newest on the feed" "$BASE" "$FEED"

echo
echo "--- the drift the gate was built for (both directions) ---"
must_fail "BEHIND: a release published, the registry was never flipped" \
  "$(mutate behind fs-gg-ui-template 0.3.1-preview.1)" "$FEED" "BEHIND the feed"
must_fail "AHEAD: registry advertises a version consumers cannot restore" \
  "$(mutate ahead fsgg-contracts 9.9.9)" "$FEED" "AHEAD of the feed"

echo
echo "--- version ORDER, never substring (the .github#268 defect class) ---"
# 0.4.0 is a substring of 0.4.0-preview.1. A substring check calls this coherent; it is not.
# The baseline (which declares 0.4.0 against a feed serving both) is the converse leg: a release
# must not be reported as behind its own prerelease.
must_fail "0.4.0-preview.1 declared while the feed's newest is 0.4.0" \
  "$(mutate substring fs-gg-ui-template 0.4.0-preview.1)" "$FEED" "BEHIND the feed.*0\.4\.0"

# The feed returns creation order. Newest must come from version order, so the same set in a
# different order must reach the same verdict — and a newest that is not first must still win.
must_pass "newest is chosen by version order, not by the feed's ordering" \
  "$BASE" "$(feed_with reordered FS.GG.Contracts '["1.0.1", "1.1.1", "1.4.0", "1.2.0"]')"
must_fail "a stale declaration is caught even when the feed lists newest last" \
  "$(mutate order-stale fsgg-contracts 1.2.0)" \
  "$(feed_with tail-newest FS.GG.Contracts '["1.0.1", "1.2.0", "1.4.0"]')" "BEHIND the feed"

# A prerelease-only package is coherent at its newest prerelease (fs-gg-audio's real shape).
must_pass "a prerelease-only feed is coherent at its newest prerelease" \
  "$BASE" "$(feed_with pre-only FS.GG.Audio.Core '["0.1.0-preview.1", "0.0.9-preview.1"]')"
# A 4-segment NuGet version (governance-reference-gate-set, ADR-0007) is not SemVer, and must
# still order correctly against a 3-segment sibling.
must_fail "4-segment 1.2.1.1 orders above 1.2.1 (not string-compared)" \
  "$(mutate four-seg governance-reference-gate-set 1.2.1)" \
  "$FEED" "BEHIND the feed.*1\.2\.1\.1"

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
if python3 - "$REPO_ROOT/registry/dependencies.yml" "$GATE" <<'PY'
import importlib.util, sys, yaml
spec = importlib.util.spec_from_file_location("gate", sys.argv[2])
gate = importlib.util.module_from_spec(spec); spec.loader.exec_module(gate)
doc = yaml.safe_load(open(sys.argv[1], encoding="utf-8"))
contracts = doc.get("contracts") or []
subjects = {str(c["id"]).strip() for c in contracts if c.get("package-version") is not None}
if not subjects:
    sys.exit("the checked-in registry has no package-bearing contracts")
missing = sorted(subjects - set(gate.CONTRACT_PACKAGES))
orphan = sorted(set(gate.CONTRACT_PACKAGES) - {str(c.get("id", "")).strip() for c in contracts})
if missing:
    sys.exit(f"unmapped package-bearing contract(s): {missing}")
if orphan:
    sys.exit(f"stale CONTRACT_PACKAGES entr(ies): {orphan}")
for c in contracts:
    if c.get("package-version") is not None:
        gate.parse_version(str(c["package-version"]))   # every declared literal must parse
PY
then ok "registry/dependencies.yml: every package-bearing contract is mapped and parses"
else bad "registry/dependencies.yml: every package-bearing contract is mapped and parses"
fi

echo
echo "$pass passed, $failcount failed."
[ "$failcount" -eq 0 ]
