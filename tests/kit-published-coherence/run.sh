#!/usr/bin/env bash
# Fixture for the published FS.GG.Kit vs canonical tree-manifest gate (.github#1469/#1291).
# No network: both manifests and the scalar declared-source lock are canned. Every failure leg
# asserts its reason so a path/read error cannot accidentally satisfy a content-drift test.

set -euo pipefail
export PYTHONDONTWRITEBYTECODE=1
export FSGG_KIT_COHERENCE_FIXTURE_OK=1

HERE="$(cd "$(dirname "$0")" && pwd)"
GATE="$HERE/../../scripts/check-kit-published-coherence.py"
WORK="$(mktemp -d "${TMPDIR:-/tmp}/kit-coherence-fixture.XXXXXX")"
trap 'rm -rf "$WORK"' EXIT

pass=0; failcount=0
ok()  { echo "PASS  $1"; pass=$((pass+1)); }
bad() { echo "FAIL  $1"; [ -n "${2:-}" ] && printf '%s\n' "$2" | sed 's/^/    | /'; failcount=$((failcount+1)); }

A=$(printf 'a%.0s' {1..64}) # skill SKILL.md — the directory source digest stored in repos.lock
B=$(printf 'b%.0s' {1..64}) # skill auxiliary file — deliberately absent from scalar repos.lock
C=$(printf 'c%.0s' {1..64}) # client
D=$(printf 'd%.0s' {1..64}) # config
OLD=$(printf 'e%.0s' {1..64})

LOCK="$WORK/repos.lock"
{
  printf '%s  .claude/skills/check-board\n' "$A"
  printf '%s  scripts/fsgg-coord\n' "$C"
  printf '%s  dist/dotnet/.config/dotnet-tools.json\n' "$D"
} > "$LOCK"

CANON="$WORK/canonical.tsv"
canonical() {
  {
    printf 'skill\tskills/check-board/SKILL.md\tcheck-board/SKILL.md\t%s\tfalse\n' "$A"
    printf 'skill\tskills/check-board/references/deep-detail.md\tcheck-board/references/deep-detail.md\t%s\tfalse\n' "$B"
    printf 'client\tclient/fsgg-coord\tscripts/fsgg-coord\t%s\ttrue\n' "$C"
    printf 'config\tconfig/dotnet-tools.json\t.config/dotnet-tools.json\t%s\tfalse\n' "$D"
    printf 'build-config\tbuild-config/Directory.Build.props\tDirectory.Build.props\t%s\tfalse\n' "$OLD"
  } > "$CANON"
}
canonical

run() { # $1 published manifest; optional $2 lock; optional $3 canonical
  set +e
  out="$(python3 "$GATE" \
    --lock "${2:-$LOCK}" \
    --fixture-manifest "$1" \
    --canonical-manifest "${3:-$CANON}" 2>&1)"
  rc=$?
  set -e
}

must_pass() {
  if [ "$rc" -ne 0 ]; then bad "$1 (expected exit 0, got $rc)" "$out"; return; fi
  if ! grep -q -- "$2" <<<"$out"; then bad "$1 (did not say: $2)" "$out"; return; fi
  ok "$1"
}

must_fail() {
  if [ "$rc" -eq 0 ]; then bad "$1 (expected non-zero)" "$out"; return; fi
  if ! grep -q -- "$2" <<<"$out"; then bad "$1 (wrong failure; wanted: $2)" "$out"; return; fi
  ok "$1"
}

echo "== check-kit-published-coherence fixture =="

PUBLISHED="$WORK/published.tsv"
cp "$CANON" "$PUBLISHED"
run "$PUBLISHED"
must_pass "an exact multi-file kit is coherent" "exact canonical destinations, bytes, modes, and closed file set"

# The regression: an auxiliary digest is not a scalar repos.lock row, but exact tree parity is green.
if ! grep -q "$B" "$LOCK"; then
  ok "the exact auxiliary file passes without duplicating its digest into repos.lock"
else
  bad "the auxiliary digest must remain absent from the scalar lock"
fi

# Content drift.
sed "s/$B/$OLD/" "$CANON" > "$PUBLISHED"
run "$PUBLISHED"
must_fail "a changed auxiliary byte is stale" "changed (sha256): check-board/references/deep-detail.md"

# Missing and extra members prove a closed file set.
grep -v 'references/deep-detail.md' "$CANON" > "$PUBLISHED"
run "$PUBLISHED"
must_fail "a missing auxiliary file is stale" "missing: check-board/references/deep-detail.md"

cp "$CANON" "$PUBLISHED"
printf 'skill\tskills/check-board/references/extra.md\tcheck-board/references/extra.md\t%s\tfalse\n' "$OLD" >> "$PUBLISHED"
run "$PUBLISHED"
must_fail "an extra auxiliary file is stale" "extra: check-board/references/extra.md"

# Receiver destination and executable mode are contract fields, not metadata noise.
sed 's#check-board/references/deep-detail.md#check-board/references/renamed.md#2' "$CANON" > "$PUBLISHED"
run "$PUBLISHED"
must_fail "a changed receiver destination is stale" "missing: check-board/references/deep-detail.md"

sed '\#references/deep-detail.md#s/false$/true/' "$CANON" > "$PUBLISHED"
run "$PUBLISHED"
must_fail "a wrong auxiliary executable mode is stale" "changed (executable): check-board/references/deep-detail.md"

# build-config remains outside coordination-kit parity, as before.
sed "s/$OLD/$A/" "$CANON" > "$PUBLISHED"
run "$PUBLISHED"
must_pass "build-config byte drift remains excluded" "exact canonical destinations, bytes, modes, and closed file set"

# repos.lock remains the declared-source integrity gate.
run "$PUBLISHED" "$WORK/no-such.lock"
must_fail "a missing lock is an error" "cannot read the canonical lock"

: > "$WORK/empty.lock"
run "$PUBLISHED" "$WORK/empty.lock"
must_fail "an empty lock is an error" "yielded no digests"

LOCK_MISSING="$WORK/missing-source.lock"
printf '%s  .claude/skills/not-in-canonical\n' "$OLD" > "$LOCK_MISSING"
run "$PUBLISHED" "$LOCK_MISSING"
must_fail "a lock digest absent from the canonical stage is an error" "does not contain every declared-source digest"

# Manifest subjects fail closed.
run "$WORK/no-such.tsv"
must_fail "a missing published fixture is an error" "cannot read fixture manifest"

printf 'skill\tonly\tthree\tfields\n' > "$PUBLISHED"
run "$PUBLISHED"
must_fail "a legacy/incomplete row is an error" "is not the 5-field"

printf 'skill\tskills/x/SKILL.md\tx/SKILL.md\tNOTHEX\tfalse\n' > "$PUBLISHED"
run "$PUBLISHED"
must_fail "a non-sha digest is an error" "non-sha256 digest"

printf 'skill\tskills/x/SKILL.md\tx/SKILL.md\t%s\tmaybe\n' "$A" > "$PUBLISHED"
run "$PUBLISHED"
must_fail "an invalid executable bit is an error" "invalid executable bit"

{
  printf 'skill\tskills/x/SKILL.md\tx/SKILL.md\t%s\tfalse\n' "$A"
  printf 'skill\tskills/y/SKILL.md\tx/SKILL.md\t%s\tfalse\n' "$A"
} > "$PUBLISHED"
run "$PUBLISHED"
must_fail "duplicate destinations are an error" "more than once"

printf 'build-config\tbuild-config/x\tx\t%s\tfalse\n' "$A" > "$PUBLISHED"
run "$PUBLISHED"
must_fail "zero coordination members is an error" "zero coordination-kit members"

set +e
out="$(env -u FSGG_KIT_COHERENCE_FIXTURE_OK python3 "$GATE" \
  --lock "$LOCK" --fixture-manifest "$CANON" --canonical-manifest "$CANON" 2>&1)"
rc=$?
set -e
must_fail "fixture mode is refused without its opt-in" "Refusing to run"

set +e
out="$(python3 "$GATE" --lock "$LOCK" --fixture-manifest "$CANON" 2>&1)"
rc=$?
set -e
must_fail "a fixture without a canonical comparison is refused" "requires --canonical-manifest"

echo
echo "== pr-arm (.github#1597) =="

# The three rows of #1597's incident table, plus the window between a bump and its release. Every
# input is canned: no network, no git, no live roster. Kit sources are given as a file so the RULE is
# under test here; the roster READER gets its own legs below.
SRC="$WORK/kit-sources.txt"
{
  printf '.claude/skills/check-board\n'
  printf '.claude/skills/pnext-item\n'
  printf 'scripts/fsgg-coord\n'
  printf 'dist/dotnet/.config/dotnet-tools.json\n'
} > "$SRC"

CSPROJ="$WORK/FS.GG.Kit.csproj"
csproj() { printf '<Project>\n  <PropertyGroup>\n    <Version>%s</Version>\n  </PropertyGroup>\n</Project>\n' "$1" > "$CSPROJ"; }

CHANGED="$WORK/changed.txt"

prarm() { # $1 published version; rest: extra args
  local published="$1"; shift
  set +e
  out="$(python3 "$GATE" --pr-arm \
    --csproj "$CSPROJ" \
    --kit-sources "$SRC" \
    --changed-files "$CHANGED" \
    --published-version "$published" "$@" 2>&1)"
  rc=$?
  set -e
}

# ROW 2 of the table — `0e1c5d0` / #1591. Touches scripts/fsgg-coord; <Version> 0.8.1; published
# 0.8.1. This is the incident the gate exists to catch.
printf 'scripts/fsgg-coord\n' > "$CHANGED"
csproj 0.8.1
prarm 0.8.1
must_fail "0e1c5d0: a kit edit at the already-published version is red" "0.8.1 > 0.8.1 is false"

# ROW 1 of the table — `edc8404` / #1581. Touches TWO kit sources; <Version> 0.8.1; published 0.8.0.
# THE NAIVE "bump the version" RULE AND THIS RULE BOTH GREEN IT, and it must stay green: the bump was
# real and the release followed. It is in the fixture because the naive rule greens ROW 2 as well, and
# only a fixture holding both rows can tell the two rules apart.
{
  printf '.claude/skills/pnext-item/references/command-contracts.md\n'
  printf 'scripts/fsgg-coord\n'
} > "$CHANGED"
csproj 0.8.1
prarm 0.8.0
must_pass "edc8404: a real bump ahead of the feed is green" "is ahead of the newest published"

if grep -q "command-contracts.md" <<<"$out" && grep -q "kit source: .claude/skills/pnext-item" <<<"$out"; then
  ok "a file UNDER a kit source is attributed to that source"
else
  bad "a file under a kit source is attributed to that source" "$out"
fi

# ROW 3 — a PR touching no kit source is NOT EVALUATED, and reads no feed. The canned published
# version is deliberately one that WOULD red, to prove the arm never got that far.
{
  printf 'docs/adr/0062-kit.md\n'
  printf '.github/workflows/coherence.yml\n'
} > "$CHANGED"
csproj 0.8.1
prarm 0.8.1
must_pass "a non-kit PR is not evaluated" "none of them a \`kit:\` source"

# The bump-is-landed-release-is-pending window: legitimately ahead of the feed, and a second
# kit-touching PR rides into the pending release rather than owing a second bump.
printf 'scripts/fsgg-coord\n' > "$CHANGED"
csproj 0.9.0
prarm 0.8.1
must_pass "a second kit PR inside the pending-release window is green" "is ahead of the newest published"

# A LOWER version is red for the same reason an equal one is — the rule is strictly greater, not
# merely different.
csproj 0.7.0
prarm 0.8.1
must_fail "a kit edit BEHIND the published version is red" "0.7.0 > 0.8.1 is false"

# The prefix trap. A bare startswith would attribute this to `.claude/skills/check-board`.
printf '.claude/skills/check-board-notes/README.md\n' > "$CHANGED"
csproj 0.8.1
prarm 0.8.1
must_pass "a sibling path sharing a kit source's prefix is not a kit edit" "none of them a \`kit:\` source"

# A `kind: config` kit row is a FILE, and an exact match on it counts.
printf 'dist/dotnet/.config/dotnet-tools.json\n' > "$CHANGED"
csproj 0.8.1
prarm 0.8.1
must_fail "an exact-match config kit row counts as a kit edit" "0.8.1 > 0.8.1 is false"

# A prerelease cannot discharge the obligation: receivers would never restore it.
printf 'scripts/fsgg-coord\n' > "$CHANGED"
csproj "0.9.0-beta.1"
prarm 0.8.1
must_fail "a prerelease <Version> cannot discharge a republish obligation" "cannot discharge a republish obligation"

# Fail-closed reads. Each is a NO-VERDICT, and a no-verdict is RED (#1597 AC3).
csproj 0.9.0
prarm 0.8.1 --csproj "$WORK/no-such.csproj"
must_fail "an unreadable csproj is a no-verdict" "cannot read"

printf '<Project></Project>\n' > "$CSPROJ"
prarm 0.8.1
must_fail "a csproj with no <Version> is a no-verdict" "declares 0 <Version> element(s)"

printf '<Project>\n<Version>1.0.0</Version>\n<Version>2.0.0</Version>\n</Project>\n' > "$CSPROJ"
prarm 0.8.1
must_fail "an ambiguous multi-<Version> csproj is a no-verdict" "declares 2 <Version> element(s)"

csproj 0.9.0
set +e
out="$(python3 "$GATE" --pr-arm --csproj "$CSPROJ" --kit-sources "$WORK/no-such-sources.txt" \
  --changed-files "$CHANGED" --published-version 0.8.1 2>&1)"; rc=$?
set -e
must_fail "an unreadable kit-source list is a no-verdict" "cannot read the canned kit-source list"

# The base ref is the PR arm's only door to its subject. No base means no diff, and no diff is NOT
# "touched nothing" — the shape that would have made every PR green.
set +e
out="$(env -u GITHUB_BASE_REF python3 "$GATE" --pr-arm --csproj "$CSPROJ" --kit-sources "$SRC" \
  --published-version 0.8.1 2>&1)"; rc=$?
set -e
must_fail "a missing base ref is a no-verdict, not an empty diff" "no base ref to diff against"

# THE ROSTER READER (AC2): the kit-source list is READ from registry/repos.yml, never restated.
ROSTER="$WORK/repos.yml"
printf 'scripts/fsgg-coord\n' > "$CHANGED"
csproj 0.8.1
roster_run() { # $1 roster path
  set +e
  out="$(python3 "$GATE" --pr-arm --csproj "$CSPROJ" --roster "$1" \
    --changed-files "$CHANGED" --published-version 0.8.1 2>&1)"
  rc=$?
  set -e
}

# The REAL roster, so a `kit:` row landing tomorrow is covered without editing this fixture.
roster_run "$HERE/../../registry/repos.yml"
must_fail "the real registry/repos.yml yields scripts/fsgg-coord as a kit source" "0.8.1 > 0.8.1 is false"

REAL_KIT_ROWS=$(python3 - "$HERE/../../registry/repos.yml" <<'PY'
import sys, yaml
print(len(yaml.safe_load(open(sys.argv[1], encoding="utf-8"))["kit"]))
PY
)
if grep -q "($REAL_KIT_ROWS source(s) considered)" <<<"$(python3 "$GATE" --pr-arm --csproj "$CSPROJ" \
     --roster "$HERE/../../registry/repos.yml" --changed-files /dev/null --published-version 0.8.1 2>&1)"; then
  ok "every kit: row in the real roster is considered ($REAL_KIT_ROWS)"
else
  bad "every kit: row in the real roster is considered ($REAL_KIT_ROWS)"
fi

# A roster the arm cannot establish its subject from is a no-verdict — never an empty kit-source set,
# which would silently switch the whole arm off.
printf 'kit: []\n' > "$ROSTER"
roster_run "$ROSTER"
must_fail "an empty kit: block is a no-verdict" "missing or not a non-empty list"

printf 'repos: []\n' > "$ROSTER"
roster_run "$ROSTER"
must_fail "a roster with no kit: block is a no-verdict" "missing or not a non-empty list"

printf 'kit:\n  - { id: x, kind: skill }\n' > "$ROSTER"
roster_run "$ROSTER"
must_fail "a kit row with no source is a no-verdict" "has no usable \`source\`"

printf 'kit: [ this: is: not: yaml\n' > "$ROSTER"
roster_run "$ROSTER"
must_fail "an unparsable roster is a no-verdict" "not parsable as YAML"

roster_run "$WORK/no-such-roster.yml"
must_fail "an unreadable roster is a no-verdict" "cannot read the kit roster"

# The canned inputs are LOCKED, exactly like --fixture-manifest. Each one, left open, is a way to make
# the arm answer without reading its subject.
for flag_pair in "--changed-files=$CHANGED" "--kit-sources=$SRC" "--published-version=0.8.1"; do
  flag="${flag_pair%%=*}"
  value="${flag_pair#*=}"
  set +e
  out="$(env -u FSGG_KIT_COHERENCE_FIXTURE_OK python3 "$GATE" --pr-arm --csproj "$CSPROJ" \
    "$flag" "$value" 2>&1)"
  rc=$?
  set -e
  must_fail "$flag is refused without the fixture opt-in" "Refusing to run"
done

set +e
out="$(python3 "$GATE" --kit-sources "$SRC" --lock "$LOCK" --fixture-manifest "$CANON" \
  --canonical-manifest "$CANON" 2>&1)"; rc=$?
set -e
must_fail "a pr-arm input on the published arm is refused, not ignored" "mean nothing to the published-package arm"

set +e
out="$(python3 "$GATE" --pr-arm --fixture-manifest "$CANON" --canonical-manifest "$CANON" 2>&1)"; rc=$?
set -e
must_fail "the two arms refuse to run at once" "different arms with different subjects"

# The PR arm must actually be WIRED, on a trigger that can see a kit source. A checker nothing runs on
# the diff is the #1606 shape this issue is the inverse of.
if python3 - "$HERE/../../.github/workflows/kit-published-coherence.yml" <<'PY'
import re
import sys

text = open(sys.argv[1], encoding="utf-8").read()
match = re.search(r"(?ms)^  pr-arm:\n(.*?)(?=^  [a-zA-Z0-9_-]+:\n|\Z)", text)
assert match, "pr-arm job missing"
job = match.group(1)
assert "--pr-arm" in job, "pr-arm job does not run the checker's PR arm"
assert "fetch-depth: 0" in job, "pr-arm job cannot resolve a merge base"
assert "uses: ./.github/actions/setup-policy-python" in job, (
    "pr-arm job does not install the pinned YAML policy dependency it needs to read repos.yml"
)

trigger = re.search(r"(?ms)^on:\n(.*?)^permissions:", text)
assert trigger, "cannot read the trigger block"
pr = re.search(r"(?ms)^  pull_request:\n(.*?)(?=^  [a-z_]+:\n|\Z)", trigger.group(1))
assert pr, "no pull_request trigger"
# COMMENTS ARE NOT FILTERS. The block deliberately EXPLAINS at length why it carries no `paths:`, so
# a bare substring test reads its own rationale as the thing it forbids — it did, on the first run.
directives = [ln for ln in pr.group(1).splitlines() if ln.strip() and not ln.strip().startswith("#")]
assert not any(ln.strip().startswith("paths") for ln in directives), (
    "the pull_request trigger regained a paths:/paths-ignore: filter — a kit-source PR would not "
    "start this workflow at all, which is the defect .github#1597 closed:\n"
    + "\n".join(directives)
)
PY
then
  ok "the pr-arm job is wired on an unfiltered pull_request trigger"
else
  bad "the pr-arm job is wired on an unfiltered pull_request trigger"
fi

# The live job must declare the YAML reader required by `scripts/repos.sh validate`. A bare
# setup-python step made the fixed gate deterministically red on a clean runner (#1469 review).
if python3 - "$HERE/../../.github/workflows/kit-published-coherence.yml" <<'PY'
import re
import sys

text = open(sys.argv[1], encoding="utf-8").read()
match = re.search(r"(?ms)^  published:\n(.*?)(?=^  [a-zA-Z0-9_-]+:\n|\Z)", text)
assert match, "published job missing"
assert "uses: ./.github/actions/setup-policy-python" in match.group(1), (
    "published job does not install the pinned YAML policy dependency"
)
PY
then
  ok "the live job declares its pinned YAML dependency"
else
  bad "the live job declares its pinned YAML dependency"
fi

echo
echo "kit-published-coherence fixture: $pass passed, $failcount failed"
[ "$failcount" -eq 0 ] || exit 1
