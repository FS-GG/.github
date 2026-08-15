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
must_pass "a non-kit PR is not evaluated" "none of them a staging-owned package input"

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
must_pass "a sibling path sharing a kit source's prefix is not a kit edit" "none of them a staging-owned package input"

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
#
# .github#2402 moved `declared_kit_version` off a raw `<Version>` regex onto the SAME
# `dotnet msbuild -getProperty:Version` evaluation `check-engine-freshness.py` /
# `check-engine-release-notes.py` already use ("never a grep" — a regex would misread the coherent
# set's `<Version>$(FsggCoherentSetVersion)</Version>` reference as the literal token
# `$(FsggCoherentSetVersion)`, not a version). The three fail-closed shapes below are re-expressed
# against what REAL evaluation actually does, verified directly against `dotnet msbuild` rather than
# assumed: a missing project file is an MSBuild error (still no-verdict); a project with no `<Version>`
# property evaluates cleanly to an EMPTY string (still no-verdict, now for a different reason); and
# "two `<Version>` elements" stopped being an ambiguity MSBuild can even see — a real project resolves
# duplicate property definitions by ITS OWN well-defined last-definition-wins rule (verified: two
# `<PropertyGroup>`s each declaring `<Version>` evaluate to the LAST one, not an error) — so that case
# is replaced with the shape that IS still a real MSBuild failure: `<Version>` declared directly under
# `<Project>`, outside any `<PropertyGroup>`, which MSBuild itself rejects as unrecognized (MSB4067).
csproj 0.9.0
prarm 0.8.1 --csproj "$WORK/no-such.csproj"
must_fail "an unreadable csproj is a no-verdict" "could not evaluate"

printf '<Project></Project>\n' > "$CSPROJ"
prarm 0.8.1
must_fail "a csproj with no <Version> is a no-verdict" "evaluates to an empty Version"

printf '<Project>\n<Version>1.0.0</Version>\n</Project>\n' > "$CSPROJ"
prarm 0.8.1
must_fail "a <Version> declared outside any PropertyGroup is a no-verdict" "could not evaluate"

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

# MUTATION FOR .github#1910. Build the topology GitHub's pull_request checkout presents: the PR
# itself changes only prose, main advances with a kit-source commit, and the checked-out merge ref
# contains both. A stale event SHA falsely attributes main's kit commit to the PR; resolving against
# the fetched current base ref excludes it. Then mutate the PR side to touch the kit and prove the
# same arm reds for a real obligation.
csproj 0.8.1
MUT_REPO="$WORK/pr-base-mutation"
git init -q -b main "$MUT_REPO"
git -C "$MUT_REPO" config user.name fixture
git -C "$MUT_REPO" config user.email fixture@example.invalid
mkdir -p "$MUT_REPO/scripts" "$MUT_REPO/docs" "$MUT_REPO/.claude/skills/check-board"
cp "$GATE" "$MUT_REPO/scripts/check-kit-published-coherence.py"
cp "$HERE/../../scripts/fsgg_feed.py" "$MUT_REPO/scripts/fsgg_feed.py"
printf 'initial\n' > "$MUT_REPO/scripts/fsgg-coord"
printf 'initial\n' > "$MUT_REPO/docs/note.md"
printf 'initial\n' > "$MUT_REPO/.claude/skills/check-board/SKILL.md"
git -C "$MUT_REPO" add .
git -C "$MUT_REPO" commit -q -m initial
STALE_BASE="$(git -C "$MUT_REPO" rev-parse HEAD)"
git -C "$MUT_REPO" branch pr-head
git -C "$MUT_REPO" switch -q pr-head
printf 'pr prose\n' >> "$MUT_REPO/docs/note.md"
git -C "$MUT_REPO" commit -qam "PR changes prose"
git -C "$MUT_REPO" switch -q main
printf 'base-owned kit change\n' >> "$MUT_REPO/scripts/fsgg-coord"
git -C "$MUT_REPO" commit -qam "main changes kit"
git -C "$MUT_REPO" update-ref refs/remotes/origin/main HEAD
git -C "$MUT_REPO" switch -q -c merge-ref
git -C "$MUT_REPO" merge -q --no-edit --no-ff pr-head

set +e
out="$(python3 "$MUT_REPO/scripts/check-kit-published-coherence.py" --pr-arm \
  --base "$STALE_BASE" --csproj "$CSPROJ" --kit-sources "$SRC" \
  --published-version 0.8.1 2>&1)"; rc=$?
set -e
must_fail "mutation control: a stale event base falsely attributes main's kit commit to the PR" \
  "Refresh or rebase the PR"

set +e
out="$(python3 "$MUT_REPO/scripts/check-kit-published-coherence.py" --pr-arm \
  --base refs/remotes/origin/main --csproj "$CSPROJ" --kit-sources "$SRC" \
  --published-version 0.8.1 2>&1)"; rc=$?
set -e
must_pass "a docs-only PR stays green after the base advances with a kit commit" \
  "none of them a staging-owned package input"

git -C "$MUT_REPO" switch -q pr-head
printf 'PR-owned kit change\n' >> "$MUT_REPO/.claude/skills/check-board/SKILL.md"
git -C "$MUT_REPO" commit -qam "PR changes kit"
git -C "$MUT_REPO" switch -q main
git -C "$MUT_REPO" switch -q -c merge-ref-with-kit
git -C "$MUT_REPO" merge -q --no-edit --no-ff pr-head
set +e
out="$(python3 "$MUT_REPO/scripts/check-kit-published-coherence.py" --pr-arm \
  --base refs/remotes/origin/main --csproj "$CSPROJ" --kit-sources "$SRC" \
  --published-version 0.8.1 2>&1)"; rc=$?
set -e
must_fail "the resolved-base arm stays red when the PR itself touches a kit source" \
  ".claude/skills/check-board/SKILL.md"

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

REAL_PACKED_INPUTS=$(python3 - "$HERE/../../registry/repos.yml" "$HERE/../../src/FS.GG.Kit/FS.GG.Kit.csproj" <<'PY'
import sys
import xml.etree.ElementTree as ET
import yaml
roster, csproj = sys.argv[1:]
inputs = {row["source"].rstrip("/") for row in yaml.safe_load(open(roster))["kit"]}
project_dir = csproj.rsplit("/", 1)[0]
for item in ET.parse(csproj).getroot().iter():
    if item.tag.rsplit("}", 1)[-1] == "None" and item.attrib.get("Pack", "").lower() == "true":
        inputs.add(project_dir + "/" + item.attrib["Include"])
inputs.update({csproj, "src/FS.GG.Kit/stage-kit.sh"})
print(len(inputs))
PY
)
if grep -q "($REAL_PACKED_INPUTS input(s) considered)" <<<"$(python3 "$GATE" --pr-arm --csproj "$HERE/../../src/FS.GG.Kit/FS.GG.Kit.csproj" \
     --roster "$HERE/../../registry/repos.yml" --changed-files /dev/null --published-version 0.8.1 2>&1)"; then
  ok "every staging-owned input is derived from the real roster and pack project ($REAL_PACKED_INPUTS)"
else
  bad "every staging-owned input is derived from the real roster and pack project ($REAL_PACKED_INPUTS)"
fi

# #1692: these bytes are packed by the project but were invisible when the arm considered only
# `kit:` rows. A build-logic edit owes a republish; a nearby gate implementation does not.
printf 'src/FS.GG.Kit/build/FS.GG.Kit.targets\n' > "$CHANGED"
set +e
out="$(python3 "$GATE" --pr-arm --csproj "$HERE/../../src/FS.GG.Kit/FS.GG.Kit.csproj" \
  --roster "$HERE/../../registry/repos.yml" --changed-files "$CHANGED" --published-version 999.0.0 2>&1)"; rc=$?
set -e
must_fail "a packaged build-logic byte owes a republish" "FS.GG.Kit.targets"

printf 'scripts/check-kit-published-coherence.py\n' > "$CHANGED"
set +e
out="$(python3 "$GATE" --pr-arm --csproj "$HERE/../../src/FS.GG.Kit/FS.GG.Kit.csproj" \
  --roster "$HERE/../../registry/repos.yml" --changed-files "$CHANGED" --published-version 999.0.0 2>&1)"; rc=$?
set -e
must_pass "a non-input checker edit owes no republish" "none of them a staging-owned package input"

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
assert 'refs/remotes/origin/${{ github.base_ref }}' in job, (
    "pr-arm does not derive its base from the fetched current base branch"
)
assert "pull_request.base.sha" not in job, (
    "pr-arm still mixes an event base SHA with actions/checkout's recomputed merge ref"
)
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
echo "== tag-arm (.github#1784, every release namespace since .github#1790) =="

# `#1772` made `kit/v<version>` the ref a receiver's bump-shape rule is resolved from. A tag is a
# MUTABLE ref and a published package is not, so the question these legs are about is never "does the
# tag exist" — it is "does the tag still resolve to the commit that produced the published package".
# The comparand is each version's published .nuspec `<repository commit=...>`, canned here.
#
# Both canned inputs mirror the REAL wire shapes: the tag list is literal `git ls-remote` output, so
# these legs exercise the shipped parser rather than a hand-written mirror of it (the #1780 review
# lesson — a fixture that mirrors its subject proves only the mirror).
#
# The canned flags are now PREFIX-QUALIFIED (`kit/v=<file>`) and repeatable, because the arm's subject
# is five namespaces rather than one. The legs below that name only `kit/v` are therefore scoped to
# `kit/v`: when anything is canned, the arm's subject IS the canned namespaces, so a leg cannot fall
# through to a live read of the four it did not mention.
TAGPUB="$WORK/tag-published.tsv"
TAGREFS="$WORK/tag-refs.txt"

C1=$(printf '1%.0s' {1..40})
C2=$(printf '2%.0s' {1..40})
C3=$(printf '3%.0s' {1..40})

tagarm() { # extra args appended
  set +e
  out="$(python3 "$GATE" --tag-arm \
    --tag-arm-published "kit/v=$TAGPUB" --tag-arm-tags "kit/v=$TAGREFS" "$@" 2>&1)"
  rc=$?
  set -e
}

# The healthy fleet: every published version tagged, every tag on its artifact's commit.
{
  printf '0.16.0\t%s\n' "$C1"
  printf '0.17.0\t%s\n' "$C2"
} > "$TAGPUB"
{
  printf '%s\trefs/tags/kit/v0.16.0\n' "$C1"
  printf '%s\trefs/tags/kit/v0.17.0\n' "$C2"
} > "$TAGREFS"
tagarm
must_pass "the declared repository's https remote is accepted" "every tag resolves (peeled) to its artifact's commit"

tagarm --remote "https://evil.example/FS-GG/.github.git"
must_fail "a foreign --remote is refused before matching canned tags" "is not github.com/FS-GG/.github"

tagarm --remote "git@github.com:FS-GG/.github.git"
must_pass "the declared repository's ssh remote is accepted" "every tag resolves (peeled) to its artifact's commit"
must_pass "every published version's tag resolves to its artifact's commit" \
  "every tag resolves (peeled) to its artifact's commit"

# PEELING IS LOAD-BEARING, not a detail. An ANNOTATED tag's own object id is not the commit the #1772
# resolver checks the rule out at; comparing the wrong one would red every annotated release — 8 of
# the 23 live tags are annotated. `refs/tags/X^{}` must beat `refs/tags/X`.
{
  printf '%s\trefs/tags/kit/v0.16.0\n' "$C1"
  printf '%s\trefs/tags/kit/v0.17.0\n' "$C3"
  printf '%s\trefs/tags/kit/v0.17.0^{}\n' "$C2"
} > "$TAGREFS"
tagarm
must_pass "an annotated tag is compared by its PEELED commit, not its tag object" \
  "every tag resolves (peeled) to its artifact's commit"

# ...and in the REVERSED emission order. The row above is git's real order, so on its own it cannot
# tell "peeled always wins" apart from "the last row wins" — a single-dict rewrite would pass it.
{
  printf '%s\trefs/tags/kit/v0.16.0\n' "$C1"
  printf '%s\trefs/tags/kit/v0.17.0^{}\n' "$C2"
  printf '%s\trefs/tags/kit/v0.17.0\n' "$C3"
} > "$TAGREFS"
tagarm
must_pass "the peeled commit wins whichever order ls-remote emits the two rows in" \
  "every tag resolves (peeled) to its artifact's commit"

# HOLE 2 — measured on the live fleet as 0.1.0 and 0.4.0: published, no tag. A receiver pinned there
# cannot have its rule resolved at all, so this is the leg that decides whether old pins are gradable.
{
  printf '%s\trefs/tags/kit/v0.16.0\n' "$C1"
} > "$TAGREFS"
tagarm
must_fail "a published version with NO tag is red" "MISSING  kit/v0.17.0"
grep -q "git tag kit/v0.17.0 $C2" <<<"$out" \
  && ok "the missing-tag remedy names the commit the ARTIFACT was packed from" \
  || bad "the missing-tag remedy names the artifact's commit" "$out"

# HOLE 1 — the tag is mutable and nothing re-checked it. This is the leg that could not exist before
# the nuspec was found to bind a commit: without an immutable comparand there is nothing to move
# AGAINST, and the check would degrade to "is it in a list someone maintains".
{
  printf '%s\trefs/tags/kit/v0.16.0\n' "$C1"
  printf '%s\trefs/tags/kit/v0.17.0\n' "$C3"
} > "$TAGREFS"
tagarm
must_fail "a tag MOVED after publication is red" "MOVED    kit/v0.17.0 resolves to $C3"
grep -q "was packed from $C2" <<<"$out" \
  && ok "the moved-tag report names both the tag's commit and the artifact's" \
  || bad "the moved-tag report names both commits" "$out"

# Missing and moved are different defects with different remedies and must not collapse into one.
{
  printf '%s\trefs/tags/kit/v0.17.0\n' "$C3"
} > "$TAGREFS"
tagarm
must_fail "missing and moved are reported separately" "1 MISSING, 1 MOVED"

# A tag naming a version the feed does not serve is the NORMAL state of a release in flight:
# release-kit.yml pushes the tag, then nuget.org indexes the package. Redding it would make every
# release red main on its way through, so it is reported and never an error.
{
  printf '0.16.0\t%s\n' "$C1"
} > "$TAGPUB"
{
  printf '%s\trefs/tags/kit/v0.16.0\n' "$C1"
  printf '%s\trefs/tags/kit/v0.18.0\n' "$C2"
} > "$TAGREFS"
tagarm
must_pass "a tag whose version is not published yet is reported, never red" "name no published version"

# A `kit/v*` ref outside the resolver's bare-x.y.z grammar can never be selected by a pin. It is
# skipped, not parsed into a version this arm would then demand a package for.
{
  printf '%s\trefs/tags/kit/v0.16.0\n' "$C1"
  printf '%s\trefs/tags/kit/vnext\n' "$C2"
  printf '%s\trefs/tags/kit/v0.16\n' "$C3"
} > "$TAGREFS"
tagarm
must_pass "a kit/v* ref outside the bare x.y.z grammar is ignored, not invented into a version" \
  "every tag resolves (peeled) to its artifact's commit"
grep -q "name no published version" <<<"$out" \
  && bad "an unparsable kit/v ref must not be reported as an untagged version" "$out" \
  || ok "an unparsable kit/v ref is not reported as an untagged version"

# UNRESOLVED IS NOT VALID (#266). Every way this arm can fail to READ its subject is red.
printf '0.16.0\t%s\n' "$C1" > "$TAGPUB"
printf 'refs/tags/kit/v0.16.0\n' > "$TAGREFS"   # one field, not `<sha>\t<ref>`
tagarm
must_fail "a malformed ls-remote row is unresolved, not empty" "is not \`<sha>\\\\t<ref>\`"

printf '0.16.0\n' > "$TAGPUB"                   # one field, not `<version>\t<commit>`
printf '%s\trefs/tags/kit/v0.16.0\n' "$C1" > "$TAGREFS"
tagarm
must_fail "a malformed published row is unresolved, not empty" "is not \`<version>\\\\t<commit>\`"
printf '0.16.0\t%s\n' "$C1" > "$TAGPUB"

printf 'NOTASHA\trefs/tags/kit/v0.16.0\n' > "$TAGREFS"
tagarm
must_fail "a non-sha object id is unresolved" "non-sha object id"

printf '%s\trefs/tags/kit/v0.16.0\n' "$C1" > "$TAGREFS"
printf '0.16.0\tnothex\n' > "$TAGPUB"
tagarm
must_fail "a published row whose commit is not 40-hex is unresolved" "names no 40-hex commit"

# An artifact that binds NO commit has no fixed point for its mutable tag to be measured against.
# That is an unanswerable question, and an unanswerable question is red, never a pass.
printf '0.16.0\t-\n' > "$TAGPUB"
tagarm
must_fail "a published version whose nuspec binds no commit is unresolved" "cannot anchor its own tag"

printf '0.16.0\t%s\n' "$C1" > "$TAGPUB"

# THE FAIL-OPEN REVIEW CAUGHT IN THIS PULL REQUEST'S FIRST DRAFT. "an empty subject is not a pass"
# was stated only inside the LIVE branch, so an empty canned list produced zero comparisons and then
# printed `ok: all 0 stable version(s) ... has not moved since publication` — a check reporting a
# measurement it never took, at exit 0. Both branches now share one guard; these are its legs.
: > "$TAGPUB"
tagarm
must_fail "an empty published set is unresolved, not a pass" "resolved ZERO published"

printf '   \n\n' > "$TAGPUB"
tagarm
must_fail "a whitespace-only published set is unresolved, not a pass" "resolved ZERO published"

# A PRERELEASE IS A SUBJECT, and .github#1790 changed this deliberately. `#1784` filtered prereleases
# out, inherited from the arms above where stable-ness decides what "newest" means. For tag integrity
# it decides nothing — "does this tag still name the commit that produced this artifact?" is exactly
# as well-posed for a prerelease — and the filter was hiding a real disagreement:
# `new-sdd-fullstack/v0.1.1-preview.1` is the ONLY version that package ever published, so a
# stable-only subject reports that whole namespace as having nothing to check.
{
  printf '0.16.0-preview.1\t%s\n' "$C1"
} > "$TAGPUB"
{
  printf '%s\trefs/tags/kit/v0.16.0-preview.1\n' "$C1"
} > "$TAGREFS"
tagarm --namespace new-sdd-workspace/v --tag-arm-published "new-sdd-workspace/v=$TAGPUB" \
  --tag-arm-tags "new-sdd-workspace/v=$TAGREFS"
must_fail "a prerelease-only namespace is MEASURED, not reported as having nothing to check" \
  "MISSING  new-sdd-workspace/v0.16.0-preview.1"

# ...and the prerelease tag is compared, not merely counted: a prerelease whose tag disagrees is red
# in exactly the same words as a stable one. This is the leg the live `new-sdd-fullstack` finding
# would have needed, and the one #1784's filter made impossible.
{
  printf '%s\trefs/tags/new-sdd-workspace/v0.16.0-preview.1\n' "$C3"
} > "$TAGREFS"
tagarm --namespace new-sdd-workspace/v --tag-arm-published "new-sdd-workspace/v=$TAGPUB" \
  --tag-arm-tags "new-sdd-workspace/v=$TAGREFS"
must_fail "a PRERELEASE tag that disagrees with its artifact is red" \
  "MOVED    new-sdd-workspace/v0.16.0-preview.1 resolves to $C3"

# The kit keeps the NARROWER grammar, and that is not an oversight: the #1772 resolver accepts a bare
# x.y.z and nothing else, so a `kit/v0.16.0-preview.1` ref can never be selected by a receiver's pin.
# Same canned refs, different namespace, opposite verdict — which is what makes the per-namespace
# grammar a real property rather than a comment.
{
  printf '%s\trefs/tags/kit/v0.16.0-preview.1\n' "$C1"
} > "$TAGREFS"
tagarm
must_fail "the kit's BARE_TRIPLE grammar still ignores a prerelease ref no pin could resolve" \
  "MISSING  kit/v0.16.0-preview.1"
grep -q "name no published version" <<<"$out" \
  && bad "a prerelease kit/v ref must not be reported as an untagged version" "$out" \
  || ok "the kit grammar drops a prerelease ref instead of parsing it into a version"

printf '%s\trefs/tags/kit/v0.16.0\n' "$C1" > "$TAGREFS"

{
  printf '0.16.0\t%s\n' "$C1"
  printf '0.16.0\t%s\n' "$C2"
} > "$TAGPUB"
tagarm
must_fail "a duplicate canned version is refused, not silently last-wins" "repeats version"

# Parsed at READ time on purpose: `sorted(..., key=parse_version)` inside the failure report would
# raise while rendering a real verdict and throw the diagnosis away.
printf 'notaversion\t%s\n' "$C1" > "$TAGPUB"
tagarm
must_fail "a canned version that is not a NuGet version is refused at read time" \
  "which is not a NuGet version"

printf '0.16.0\t%s\n' "$C1" > "$TAGPUB"
set +e
out="$(python3 "$GATE" --tag-arm --tag-arm-published "kit/v=$TAGPUB" \
  --tag-arm-tags "kit/v=$WORK/no-such-refs.txt" 2>&1)"; rc=$?
set -e
must_fail "an unreadable tag list is unresolved" "cannot read the canned ls-remote tag list"

set +e
out="$(python3 "$GATE" --tag-arm --tag-arm-published "kit/v=$WORK/no-such-pub.tsv" \
  --tag-arm-tags "kit/v=$TAGREFS" 2>&1)"; rc=$?
set -e
must_fail "an unreadable published list is unresolved" "cannot read the canned published-version list"

# ...and an unreadable subject is UNRESOLVED, never clean (#266). The report must say so in the
# headline as well as the error stream, because a namespace that could not be measured appearing in a
# list of measured ones is the exact confusion the epic is about.
grep -q "UNRESOLVED — NOT MEASURED" <<<"$out" \
  && ok "an unreadable namespace is reported NOT MEASURED, never as clean" \
  || bad "an unreadable namespace is reported NOT MEASURED" "$out"

# THE NUSPEC READER ITSELF. The canned rows above hand this arm the RESULT of the nuspec read, so
# without these legs the one genuinely new parser in the change would have no coverage at all — the
# shape of the eight could-not-fail checks found in this repository today. These drive the SHIPPED
# function against real nuspec bytes.
if python3 - "$GATE" <<'PY'
import importlib.util
import sys

spec = importlib.util.spec_from_file_location("gate", sys.argv[1])
gate = importlib.util.module_from_spec(spec)
# Register before executing: the module defines a @dataclass, and dataclasses resolves annotations
# through sys.modules[cls.__module__] — which is None for a module that is not registered yet.
sys.modules["gate"] = gate
spec.loader.exec_module(gate)
COMMIT = "8b2e6cd9593203e6b7a0abcea5c9324b00f621ec"
REPO = "FS-GG/.github"


def nuspec(body: str) -> bytes:
    return (
        '<?xml version="1.0" encoding="utf-8"?>'
        '<package xmlns="http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd">'
        f"<metadata><id>FS.GG.Kit</id>{body}</metadata></package>"
    ).encode()


# The real shape, namespaced exactly as nuget.org serves it.
good = nuspec(f'<repository type="git" url="https://github.com/FS-GG/.github" commit="{COMMIT}" />')
assert gate.nuspec_repository_commit("0.17.0", good, repository=REPO, package="FS.GG.Kit", prefix="kit/v") == COMMIT, "real nuspec shape"

# Same, with a .git suffix and a trailing slash — both are the same repository.
for url in ("https://github.com/FS-GG/.github.git", "https://github.com/FS-GG/.github/"):
    variant = nuspec(f'<repository type="git" url="{url}" commit="{COMMIT}" />')
    assert gate.nuspec_repository_commit("0.17.0", variant, repository=REPO, package="FS.GG.Kit", prefix="kit/v") == COMMIT, url


def refuses(body: str, wanted: str, what: str) -> None:
    try:
        gate.nuspec_repository_commit("0.17.0", nuspec(body), repository=REPO, package="FS.GG.Kit", prefix="kit/v")
    except gate.GateError as e:
        assert wanted in str(e), f"{what}: wrong reason: {e}"
        return
    raise AssertionError(f"{what}: accepted what it must refuse")


# THE REASON THIS IS PARSED AS XML AND NOT GREPPED. A commented-out element is not an element; a
# regex over markup matches it and binds the artifact to a commit nobody published.
refuses(
    f"<!-- <repository type=\"git\" url=\"https://github.com/FS-GG/.github\" commit=\"{COMMIT}\" /> -->",
    "carries 0 <repository> element(s)",
    "a commented-out repository element",
)
refuses("", "carries 0 <repository> element(s)", "no repository element")
refuses(
    f'<repository url="https://github.com/FS-GG/.github" commit="{COMMIT}" />'
    f'<repository url="https://github.com/FS-GG/.github" commit="{"a" * 40}" />',
    "carries 2 <repository> element(s)",
    "two repository elements",
)
refuses(
    '<repository type="git" url="https://github.com/FS-GG/.github" />',
    "names no 40-hex commit",
    "a repository element with no commit",
)
refuses(
    '<repository type="git" url="https://github.com/FS-GG/.github" commit="v1.2.3" />',
    "names no 40-hex commit",
    "a non-sha commit",
)
# A package packed from a FORK names a history whose tags are not the ones the fleet resolves
# against, so its commit cannot anchor a FS-GG/.github tag.
refuses(
    f'<repository type="git" url="https://github.com/someone/.github" commit="{COMMIT}" />',
    "not github.com/FS-GG/.github",
    "a package packed from another repository",
)
# THE THREE WAYS A SUFFIX MATCH IS NOT AN IDENTITY CHECK. The first draft tested
# `endswith("/" + slug)`; review demonstrated it accepting all three of these. The comparison is
# now (host, owner/name) compared WHOLE.
for hostile, why in [
    # the prefix trap, the same one touched_kit_sources() spells out
    ("https://github.com/x/evil-FS-GG/.github", "a slug that merely ENDS WITH the real one"),
    # the real slug, on someone else's server
    ("https://evil.example.com/FS-GG/.github", "the real slug on a FOREIGN HOST"),
    # the real slug hidden in a fragment, so the url ends with it while resolving elsewhere
    ("https://github.com/attacker/mirror#https://github.com/FS-GG/.github",
     "the real slug buried in a URL FRAGMENT"),
]:
    refuses(
        f'<repository type="git" url="{hostile}" commit="{COMMIT}" />',
        "not github.com/FS-GG/.github",
        why,
    )
# ...while the forms a REAL publish can legitimately produce are still accepted. Lowercasing has to
# happen before the `.git` strip, or a `.GIT` suffix survives and reds a genuine package.
for benign in (
    "https://github.com/FS-GG/.github",
    "HTTPS://GitHub.com/FS-GG/.github.GIT",
    "git@github.com:FS-GG/.github.git",
):
    ok_url = nuspec(f'<repository type="git" url="{benign}" commit="{COMMIT}" />')
    assert gate.nuspec_repository_commit("0.17.0", ok_url, repository=REPO, package="FS.GG.Kit", prefix="kit/v") == COMMIT, benign
try:
    gate.nuspec_repository_commit("0.17.0", b"<package", repository=REPO, package="FS.GG.Kit", prefix="kit/v")
except gate.GateError as e:
    assert "not parsable XML" in str(e), e
else:
    raise AssertionError("unparsable XML accepted")
PY
then
  ok "the shipped nuspec reader binds a commit, and refuses every shape that cannot bind one"
else
  bad "the shipped nuspec reader binds a commit, and refuses every shape that cannot bind one"
fi

# The canned inputs are LOCKED behind the same switch as every other arm's, and for the same reason:
# each replaces a read of this arm's subject with an answer supplied on the command line.
#
# Each flag is tested ALONE, deliberately: supplying both would still pass if the lock's flag list
# had lost one of them. And each assertion names THE FLAG, not just "Refusing to run" — that phrase
# is shared with the misdirection refusal, and if the lock were ever removed these commands would
# fall through to a live read whose DNS/timeout failure would otherwise read as the leg passing.
for flag_pair in "--tag-arm-published=kit/v=$TAGPUB" "--tag-arm-tags=kit/v=$TAGREFS"; do
  flag="${flag_pair%%=*}"
  value="${flag_pair#*=}"
  set +e
  out="$(env -u FSGG_KIT_COHERENCE_FIXTURE_OK python3 "$GATE" --tag-arm "$flag" "$value" 2>&1)"
  rc=$?
  set -e
  must_fail "$flag is refused without the fixture opt-in" \
    "$flag read canned input and are NOT a coherence signal"
done

# The two lock-refusal branches the OTHER arms take. Neither was covered, and each is a distinct
# message naming a distinct running arm.
set +e
out="$(python3 "$GATE" --tag-arm --changed-files "$CHANGED" 2>&1)"; rc=$?
set -e
must_fail "a pr-arm input on the tag arm is refused, not ignored" \
  "are --pr-arm inputs and mean nothing to the tag arm"

set +e
out="$(python3 "$GATE" --pr-arm --tag-arm-tags "kit/v=$TAGREFS" 2>&1)"; rc=$?
set -e
must_fail "a tag-arm input on the pr arm is refused, not ignored" \
  "are --tag-arm inputs and mean nothing to the PR arm"

set +e
out="$(python3 "$GATE" --tag-arm-tags "kit/v=$TAGREFS" --lock "$LOCK" --fixture-manifest "$CANON" \
  --canonical-manifest "$CANON" 2>&1)"; rc=$?
set -e
must_fail "a tag-arm input on the published arm is refused, not ignored" \
  "are --tag-arm inputs and mean nothing to the published-package arm"

# Both refusals below use the phrase "different arms with different subjects", so each asserts the
# PAIR it names as well — otherwise either leg would pass on the other's message.
set +e
out="$(python3 "$GATE" --pr-arm --tag-arm 2>&1)"; rc=$?
set -e
# The refusal now LISTS the arms it was given rather than naming a hard-coded pair, because
# .github#2533 made three arms mutually exclusive and a pairwise message cannot say which three.
# The assertion still names both arms, so it cannot pass on the obligation arm's message.
must_fail "the pr and tag arms refuse to run at once" \
  "--pr-arm, --tag-arm are different arms with different subjects"

set +e
out="$(python3 "$GATE" --tag-arm --fixture-manifest "$CANON" --canonical-manifest "$CANON" 2>&1)"; rc=$?
set -e
must_fail "the tag arm and the manifest fixture refuse to run at once" \
  "--tag-arm and the manifest fixture flags are different arms"

echo
echo "== tag-arm: every release namespace (.github#1790) =="

# AGGREGATION. `#1784`'s arm measured ONE namespace, so nothing could tell it to keep going after a
# red. With five, a namespace that reds must not abort the four behind it — that is how a single
# defect hides four more, and .github#1790 exists because four namespaces went unlooked-at for a year.
NSPUB2="$WORK/ns2-published.tsv"
NSREFS2="$WORK/ns2-refs.txt"
printf '0.16.0\t%s\n' "$C1" > "$TAGPUB"
printf '%s\trefs/tags/kit/v0.16.0\n' "$C1" > "$TAGREFS"
printf '0.9.0\t%s\n' "$C2" > "$NSPUB2"
printf '%s\trefs/tags/drivers/v0.9.0\n' "$C3" > "$NSREFS2"   # MOVED
set +e
out="$(python3 "$GATE" --tag-arm \
  --tag-arm-published "kit/v=$TAGPUB"      --tag-arm-tags "kit/v=$TAGREFS" \
  --tag-arm-published "drivers/v=$NSPUB2"  --tag-arm-tags "drivers/v=$NSREFS2" 2>&1)"; rc=$?
set -e
must_fail "a red namespace does not abort the others" "MOVED    drivers/v0.9.0"
grep -q "kit/v\*  FS.GG.Kit: 1 published version(s), every one anchored by its own .nuspec — every tag resolves" <<<"$out" \
  && ok "the clean namespace beside a red one is still measured and still reported" \
  || bad "the clean namespace beside a red one is still reported" "$out"
grep -q "2 of 5 declared release-tag namespace(s) selected in .*, 2 measured over 2 published version(s)" <<<"$out" \
  && ok "the headline counts namespaces measured and versions compared" \
  || bad "the headline counts namespaces measured and versions compared" "$out"

# A canned namespace must supply BOTH halves. One canned and one live is a leg that reaches the
# network from a run whose author believed it was offline — and whose DNS failure then reads as the
# leg passing. That is the #1008 shape, in a fixture.
set +e
out="$(python3 "$GATE" --tag-arm --tag-arm-published "kit/v=$TAGPUB" \
  --tag-arm-tags "drivers/v=$NSREFS2" 2>&1)"; rc=$?
set -e
must_fail "a half-canned namespace is refused, not silently read live" "has only one half"

set +e
out="$(python3 "$GATE" --tag-arm --tag-arm-published "no-such/v=$TAGPUB" \
  --tag-arm-tags "no-such/v=$TAGREFS" 2>&1)"; rc=$?
set -e
must_fail "an unknown namespace prefix is refused, not applied to nothing" \
  "names the unknown release namespace"

set +e
out="$(python3 "$GATE" --tag-arm --tag-arm-published "$TAGPUB" --tag-arm-tags "$TAGREFS" 2>&1)"; rc=$?
set -e
must_fail "an unqualified canned path is refused" "takes .PREFIX=FILE."

set +e
out="$(python3 "$GATE" --tag-arm --tag-arm-published "kit/v=$TAGPUB" \
  --tag-arm-published "kit/v=$NSPUB2" --tag-arm-tags "kit/v=$TAGREFS" 2>&1)"; rc=$?
set -e
must_fail "the same namespace canned twice is refused, not last-wins" "twice; the second would"

set +e
out="$(python3 "$GATE" --tag-arm --namespace nope/v 2>&1)"; rc=$?
set -e
must_fail "--namespace naming nothing is refused, not an empty green" \
  "which are not declared release namespaces"

# ...and a selection that is individually valid but intersects to NOTHING is refused too. Both parts
# name real things, so the refusal above cannot see it; without this the arm would measure zero
# namespaces and report a pass.
set +e
out="$(python3 "$GATE" --tag-arm --namespace kit/v \
  --tag-arm-published "drivers/v=$NSPUB2" --tag-arm-tags "drivers/v=$NSREFS2" 2>&1)"; rc=$?
set -e
must_fail "a selection that intersects to nothing is refused, not an empty green" \
  "no release namespace matches"

# A namespace that could not be READ must not take the others down with it, and must not be counted
# as measured — the aggregation property, from the UNRESOLVED side rather than the MOVED side.
printf '0.16.0\t%s\n' "$C1" > "$TAGPUB"
printf '%s\trefs/tags/kit/v0.16.0\n' "$C1" > "$TAGREFS"
set +e
out="$(python3 "$GATE" --tag-arm \
  --tag-arm-published "kit/v=$TAGPUB"     --tag-arm-tags "kit/v=$TAGREFS" \
  --tag-arm-published "drivers/v=$NSPUB2" --tag-arm-tags "drivers/v=$WORK/no-such.txt" 2>&1)"; rc=$?
set -e
must_fail "one UNRESOLVED namespace reds the run on its own" "UNRESOLVED — NOT MEASURED"
grep -q "kit/v\*  FS.GG.Kit: 1 published version(s)" <<<"$out" \
  && ok "a namespace beside an UNRESOLVED one is still measured and reported" \
  || bad "a namespace beside an UNRESOLVED one is still measured" "$out"
grep -q "2 of 5 declared release-tag namespace(s) selected in .*, 1 measured" <<<"$out" \
  && ok "an UNRESOLVED namespace is not counted among the measured ones" \
  || bad "an UNRESOLVED namespace is not counted among the measured" "$out"

# A namespace whose CLASSIFICATION raises (not its read) must also stay contained. Two tags that
# canonicalise to one version but resolve to different commits is the shape that does it — and until
# review it was raised OUTSIDE the per-namespace try, so one such namespace aborted all five.
printf '0.9.0\t%s\n' "$C2" > "$NSPUB2"
{
  printf '%s\trefs/tags/drivers/v0.9.0\n' "$C2"
  printf '%s\trefs/tags/drivers/v0.9.0.0\n' "$C3"
} > "$NSREFS2"
set +e
out="$(python3 "$GATE" --tag-arm \
  --tag-arm-published "kit/v=$TAGPUB"     --tag-arm-tags "kit/v=$TAGREFS" \
  --tag-arm-published "drivers/v=$NSPUB2" --tag-arm-tags "drivers/v=$NSREFS2" 2>&1)"; rc=$?
set -e
must_fail "an ambiguous tag set is UNRESOLVED for its namespace alone" "resolve to different commits"
grep -q "kit/v\*  FS.GG.Kit: 1 published version(s)" <<<"$out" \
  && ok "a classification failure does not abort the namespaces beside it" \
  || bad "a classification failure does not abort the namespaces beside it" "$out"

# THE DECISION LOGIC, DRIVEN DIRECTLY. Everything above hands the arm canned READS; these drive the
# shipped classifier and renderer against the REAL RECORDED_DISAGREEMENTS and the REAL namespace
# table, so the two genuinely new behaviours in .github#1790 — the both-commits pin and the UNCOVERED
# branch — are exercised rather than described.
if python3 - "$GATE" <<'PY'
import importlib.util
import sys

spec = importlib.util.spec_from_file_location("gate", sys.argv[1])
gate = importlib.util.module_from_spec(spec)
sys.modules["gate"] = gate
spec.loader.exec_module(gate)

by_prefix = {ns.prefix: ns for ns in gate.RELEASE_NAMESPACES}
assert gate.RECORDED_DISAGREEMENTS, "the record is empty; these legs would prove nothing"

for record in gate.RECORDED_DISAGREEMENTS:
    ns = by_prefix[record.prefix]
    bindings = {record.version: record.nuspec_commit}

    # 1. THE EXACT RECORDED STATE — acknowledged, printed, and NOT red.
    v = gate.classify_namespace(ns, bindings, {record.version: record.tag_commit})
    assert not v.red, f"{record.prefix}{record.version}: the recorded state must not be red"
    assert len(v.recorded) == 1 and not v.moved, f"{record.prefix}{record.version}: not recorded"
    code, stdout, _ = gate.render_tag_arm([v], "FS-GG/.github")
    assert code == 0 and "RECORDED " in stdout, "a recorded disagreement must still be PRINTED"

    # 2. THE TAG MOVES AGAIN — the record stops matching, in EITHER direction. This is the whole
    #    difference between a pinned record and an exemption: an exemption keyed on the version
    #    alone would green all three of these.
    for moved_to, why in [
        ("f" * 40, "moved somewhere new"),
        (record.nuspec_commit, "moved onto the commit the artifact names"),
    ]:
        v = gate.classify_namespace(ns, bindings, {record.version: moved_to})
        if moved_to == record.nuspec_commit:
            assert not v.red and not v.recorded, f"{why}: agreement is agreement, not a record"
        else:
            assert v.red and v.moved and not v.recorded, f"{why}: must fall through to MOVED"

    # 3. THE ARTIFACT'S COMMIT DIFFERS FROM THE RECORD — a republish under the same version, or a
    #    record written against a nuspec nobody re-read. Also red.
    v = gate.classify_namespace(
        ns, {record.version: "e" * 40}, {record.version: record.tag_commit}
    )
    assert v.red and v.moved and not v.recorded, "a record must not speak for a different artifact"

    # 4. THE VERSION LEAVES THE FEED — SPENT, reported, never silently kept and never red.
    v = gate.classify_namespace(ns, {"99.99.99": "a" * 40}, {"99.99.99": "a" * 40})
    assert v.spent and not v.red, "a record describing nothing must be reported SPENT"
    _, stdout, _ = gate.render_tag_arm([v], "FS-GG/.github")
    assert "SPENT " in stdout, "a spent record must be printed"

    # 5. THE DISAGREEMENT IS REPAIRED — also SPENT, and this is the one review caught. Agreement
    #    takes the success path, so a suppression entry for a defect that no longer exists would
    #    otherwise sit in the list forever, unlooked-at, which is precisely the rot the record
    #    claims it cannot have.
    v = gate.classify_namespace(
        ns, {record.version: record.nuspec_commit}, {record.version: record.nuspec_commit}
    )
    assert not v.red and not v.recorded, "agreement is agreement"
    assert v.spent, "a record whose disagreement was REPAIRED must be reported SPENT"
    _, stdout, _ = gate.render_tag_arm([v], "FS-GG/.github")
    assert "SPENT " in stdout, "a repaired record must be printed SPENT"

    # 6. A VERSION LITERAL THAT DIFFERS ONLY BY NUGET NORMALISATION IS THE SAME VERSION. The feed
    #    normalises what it serves and a tag carries what the release author typed, so an exact
    #    string compare reds a `v1.0` tag against a published `1.0.0` — fail-closed, but a false red
    #    on main. Driven on the record, so the join and the pin are exercised together.
    #    The two ways a literal legitimately varies: a stable version gains a padding segment
    #    (`1.0` / `1.0.0` / `1.0.0.0` are one NuGet version), and a prerelease suffix folds case.
    if gate.is_prerelease(record.version):
        head, _, pre = record.version.partition("-")
        alias = f"{head}-{pre.upper()}"
    else:
        alias = record.version + ".0"
    assert alias != record.version, alias
    v = gate.classify_namespace(
        ns, {record.version: record.nuspec_commit}, {alias: record.tag_commit}
    )
    assert v.recorded and not v.missing, (
        f"{alias} and {record.version} are the same NuGet version: {v}"
    )

    # ...but two DIFFERENT commits under one canonical version is an ambiguity, not a join: nothing
    # can say which one a pin resolves, so it is unresolved rather than silently last-wins.
    try:
        gate.classify_namespace(
            ns, {record.version: record.nuspec_commit},
            {record.version: record.tag_commit, alias: "b" * 40},
        )
    except gate.GateError as e:
        assert "resolve to different commits" in str(e), e
    else:
        raise AssertionError("two commits under one canonical version were silently joined")

# THE SUPPRESSION LIST IS PINNED AS A SET, not merely as well-formed entries. Everything above
# proves each record behaves; nothing above notices a THIRD record appearing. For a list whose entire
# justification is "it cannot rot into cover", adding to it must be a deliberate act that fails this
# fixture until someone updates this line — the same discipline as EXPECTED_LEGS below, and the same
# discipline RELEASE_NAMESPACES gets from the workflow-completeness leg.
EXPECTED_RECORDS = {
    ("coord-engine/v", "0.1.0"),
    ("new-sdd-fullstack/v", "0.1.1-preview.1"),
}
actual = {(r.prefix, r.version) for r in gate.RECORDED_DISAGREEMENTS}
assert actual == EXPECTED_RECORDS, (
    "RECORDED_DISAGREEMENTS changed. Every entry SUPPRESSES a red, so a new one is a decision that "
    "belongs in a pull request someone read, not a line that slipped in:\n"
    f"  added:   {sorted(actual - EXPECTED_RECORDS)}\n"
    f"  removed: {sorted(EXPECTED_RECORDS - actual)}"
)
assert len(actual) == len(gate.RECORDED_DISAGREEMENTS), "duplicate (prefix, version) records"

# ...and the CODE refuses a duplicate too, not just this fixture's view of the shipped list. The two
# guard different things: the assertion above sees the list as authored, this sees what happens if a
# duplicate ever reaches the classifier — one entry silently overwriting the other, leaving a
# survivor that speaks for a state nobody wrote.
duplicated = gate.RECORDED_DISAGREEMENTS[0]
saved = gate.RECORDED_DISAGREEMENTS
gate.RECORDED_DISAGREEMENTS = saved + (duplicated,)
try:
    gate.classify_namespace(by_prefix[duplicated.prefix], {}, {})
except gate.GateError as e:
    assert "more than once" in str(e), e
else:
    raise AssertionError("a duplicated record was silently collapsed")
finally:
    gate.RECORDED_DISAGREEMENTS = saved

# EVERY RECORD MUST NAME A DECLARED NAMESPACE, or it silently speaks for nothing.
for record in gate.RECORDED_DISAGREEMENTS:
    assert record.prefix in by_prefix, f"recorded disagreement for unknown namespace {record.prefix}"
    assert gate._HEX40.match(record.tag_commit), record.tag_commit
    assert gate._HEX40.match(record.nuspec_commit), record.nuspec_commit
    assert record.tag_commit != record.nuspec_commit, "a record must describe a DISAGREEMENT"
    assert record.why.strip() and record.issue.strip(), "a record must carry its reason and issue"

# THE UNCOVERED BRANCH. A namespace with no artifact anchor is reported NOT MEASURED — never as
# clean, and never by substituting a weaker comparand for the missing one (#266, .github#1790).
orphan = gate.TagNamespace(
    prefix="unanchored/v", package=None, grammar=gate.NUGET_VERSION, note="a namespace with no feed"
)
v = gate.measure_namespace(
    orphan, remote="file:///nonexistent", repository="FS-GG/.github",
    canned_published=None, canned_tags=None,
)
assert v.uncovered and not v.red, "an uncovered namespace is declared, not a per-namespace failure"
code, stdout, stderr = gate.render_tag_arm([v], "FS-GG/.github")
assert "UNCOVERED — NOT MEASURED" in stdout, stdout
assert "0 measured" in stdout, (
    "an uncovered namespace must not be counted among the measured ones:\n" + stdout
)
# ...AND THE RUN AS A WHOLE IS NOT A PASS. The per-namespace "an empty subject is not a pass" guard
# has an aggregate twin: a run in which NOTHING was measured printed `ok: … 0 measured` and exited 0
# — a check reporting a measurement it never took, at exit 0, which is the whole of epic #266. The
# EXIT CODE is asserted here and not only the words, because the exit code is what CI reads.
assert code == 1, f"a run that measured nothing must not exit 0:\n{stdout}"
assert "NOTHING WAS MEASURED" in stderr, stderr

# The same holds when several namespaces are all uncovered — the guard is on the aggregate, not on
# the count of verdicts.
code, stdout, stderr = gate.render_tag_arm([v, v], "FS-GG/.github")
assert code == 1 and "NOTHING WAS MEASURED" in stderr, stdout + stderr

# ...and one measured namespace beside an uncovered one is NOT "nothing measured".
measured = gate.classify_namespace(
    by_prefix["kit/v"], {"1.0.0": "a" * 40}, {"1.0.0": "a" * 40}
)
code, stdout, _ = gate.render_tag_arm([v, measured], "FS-GG/.github")
assert code == 0 and "1 measured" in stdout, stdout
PY
then
  ok "the recorded-disagreement pin, the SPENT report, and the UNCOVERED branch all behave"
else
  bad "the recorded-disagreement pin, the SPENT report, and the UNCOVERED branch all behave"
fi

# THE TABLE MUST BE COMPLETE, and this is the leg that keeps .github#1790 from being a one-off sweep.
# The gap #1790 closed was not "the check was wrong" — it was "four namespaces were never in it". A
# sixth release workflow can reopen exactly that, silently, and no amount of care inside the arm
# would notice. So the table is compared to the repository's own release triggers.
if python3 - "$GATE" "$HERE/../../.github/workflows" <<'PY'
import glob
import importlib.util
import os
import sys

import yaml

spec = importlib.util.spec_from_file_location("gate", sys.argv[1])
gate = importlib.util.module_from_spec(spec)
sys.modules["gate"] = gate
spec.loader.exec_module(gate)

declared = {ns.prefix for ns in gate.RELEASE_NAMESPACES}

# PARSED AS YAML, NOT MATCHED WITH A REGEX. The first draft used
# `re.findall(r"tags:\s*\[\s*'([^']+?)\*'\s*\]", text)`, which matches ONLY flow-style single-quoted
# triggers. All four release workflows happen to be written that way today, so the leg was green and
# looked complete — while an ordinary block-style trigger
#
#     on:
#       push:
#         tags:
#           - 'foo/v*'
#
# would be invisible, `uncovered` would stay empty, and this leg would PASS while the namespace it
# was written to catch went unchecked. That is the same defect class as the gap #1790 closed, inside
# the leg meant to prevent it (#1790 review). PyYAML is already a declared dependency of this job.
found = {}
for path in sorted(glob.glob(os.path.join(sys.argv[2], "release-*.yml"))):
    doc = yaml.safe_load(open(path, encoding="utf-8"))
    assert isinstance(doc, dict), path
    # `on:` is YAML 1.1's boolean true — PyYAML parses the KEY as True, not as the string "on".
    triggers = doc.get("on", doc.get(True)) or {}
    push = (triggers or {}).get("push") or {}
    tags = push.get("tags") or []
    if isinstance(tags, str):
        tags = [tags]
    for pattern in tags:
        assert pattern.endswith("*"), f"{path}: unexpected tag trigger {pattern!r}"
        found[pattern[:-1]] = os.path.basename(path)
assert found, "no release workflow declares a tag trigger — this leg would prove nothing"
assert len(found) >= 4, f"expected at least 4 tag-triggered release workflows, found {found}"

uncovered = {p: w for p, w in found.items() if p not in declared}
assert not uncovered, (
    "a release workflow publishes into a tag namespace RELEASE_NAMESPACES does not name, so its "
    f"tags are unchecked — the exact gap .github#1790 closed: {uncovered}"
)
# The reverse is NOT an error: `new-sdd-fullstack/v*` is retired and has no workflow, and its
# packages are still served, so it stays in the table. Assert that it is deliberate rather than
# stale by requiring the row to say so.
for ns in gate.RELEASE_NAMESPACES:
    if ns.prefix not in found:
        assert "RETIRED" in ns.note or "retired" in ns.note, (
            f"{ns.prefix} has no release workflow and its note does not say why it is still listed"
        )
PY
then
  ok "every release workflow's tag namespace is in RELEASE_NAMESPACES"
else
  bad "every release workflow's tag namespace is in RELEASE_NAMESPACES"
fi

# THE ARM MUST BE WIRED. A checker nothing runs is the .github#1606 shape, and this one's whole
# purpose is to notice a change made OUTSIDE any pull request — so it belongs on the job whose
# subject is already main-plus-the-feed, not on the PR jobs.
if python3 - "$HERE/../../.github/workflows/kit-published-coherence.yml" <<'PY'
import re
import sys

text = open(sys.argv[1], encoding="utf-8").read()
match = re.search(r"(?ms)^  published:\n(.*?)(?=^  [a-zA-Z0-9_-]+:\n|\Z)", text)
assert match, "published job missing"
job = match.group(1)
directives = [ln for ln in job.splitlines() if ln.strip() and not ln.strip().startswith("#")]
assert any("--tag-arm" in ln for ln in directives), (
    "the published job does not run the tag arm — the release tags .github#1772 and #1075 resolve "
    "against would be load-bearing and unchecked again:\n" + "\n".join(directives)
)
# It must run UNSCOPED. `--namespace` narrows the subject, and a narrowed live run is how four of
# five namespaces went unchecked in the first place (.github#1790).
assert not any("--namespace" in ln for ln in directives), (
    "the live tag arm is scoped with --namespace, so the namespaces it omits are unchecked — the "
    "exact gap .github#1790 closed:\n" + "\n".join(directives)
)
assert not any("continue-on-error" in ln for ln in directives), (
    "the published job gained continue-on-error; a tag defect would report green"
)
# The tag arm must not be ABORTED by the staleness step above it. They are independent subjects with
# independent remedies, and a stale kit is the state most likely to coincide with tag surgery.
tag_step = re.search(r"(?ms)^      - name: Do the release tags still resolve.*?\n(.*?)(?=^      - |\Z)", job)
assert tag_step, "the tag-arm step is not where this assertion expects it"
assert "--remote" not in tag_step.group(1), (
    "the live tag arm supplies --remote, so it can substitute another repository's tags for this "
    "repository's release evidence:\n" + tag_step.group(1)
)
assert "!cancelled()" in tag_step.group(1), (
    "the tag-arm step is not conditioned on !cancelled(), so a red from the staleness step above it "
    "masks a moved tag for as long as the staleness stands"
)
PY
then
  ok "the tag arm is wired on the main/schedule job"
else
  bad "the tag arm is wired on the main/schedule job"
fi

# ════════════════════════════════════════════════════════════════════════════════════════════════
# THE OBLIGATION ARM (--obligation-arm, .github#2533)
#
# Subject: does a `fsgg:delivery-obligation` declared on this PR name an act the MERGE performs?
# `.github#2512` declared a manual coherent-set release, had it reviewed by two independent critics
# across three rounds and host-gated as the session's only irreversible act — and `kit-auto-publish`
# had cut all three tags 8 seconds after the merge. AC2 is the flagged case, AC3 the controlled
# counterpart that must stay quiet, and the two mutations at the end are the evidence that each of
# them is measuring the detection rather than passing by accident.
# ════════════════════════════════════════════════════════════════════════════════════════════════
OBL="$WORK/obligations"
mkdir -p "$OBL"
HEAD_SHA=8de950c37e63f84f87f1a3736eca5847ddc0db97

declaration() { # $1 file stem; $2 id; $3 kind  — marker at byte 0, prose below, the org's own style
  printf '<!-- fsgg:delivery-obligation id=%s kind=%s head=%s -->\n\nProse the marker is not.' \
    "$2" "$3" "$HEAD_SHA" \
    | python3 -c 'import json,sys; print(json.dumps([{"body": sys.stdin.read()}]))' > "$OBL/$1.json"
}

# THE CANDIDATE THE ARM SCORES (.github#2571). The arm asks two questions, not one: does the merge
# START kit-auto-publish, and does kit-auto-publish then CUT anything for the version this PR ships.
# Every leg therefore has to say which version line it is standing on, and the DEFAULT here is the
# PATCH line — the candidate `kit-auto-publish.py` admits — so that every trigger leg below keeps
# asserting the trigger half against a candidate the merge really would publish. The .github#2571
# section reassigns these two for the minor-line legs and restores them afterwards; nothing else does.
OBL_CANDIDATE=0.51.2   # the NEXT PATCH above the frontier: `decide()` returns `tag`
OBL_FRONTIER=0.51.1

obl() { # $1 comments file; $@ extra args — run from the REAL repo root, against the REAL workflows
  local comments="$1"; shift
  set +e
  out="$(python3 "$GATE" --obligation-arm --obligations "$comments" \
    --obligation-candidate-version "$OBL_CANDIDATE" \
    --obligation-published-version "$OBL_FRONTIER" "$@" 2>&1)"
  rc=$?
  set -e
}

obl_in() { # $1 tree to run from; $2 comments file; $@ extra args
  local tree="$1" comments="$2"; shift 2
  set +e
  out="$(cd "$tree" && python3 "$GATE" --obligation-arm --obligations "$comments" \
    --obligation-candidate-version "$OBL_CANDIDATE" \
    --obligation-published-version "$OBL_FRONTIER" "$@" 2>&1)"
  rc=$?
  set -e
}

# ---- AC2: the flagged case, measured against THIS repository's own live kit-auto-publish.yml. The
#      workflow is READ, not restated, so if its trigger ever stops being a merge trigger this leg
#      changes verdict with nothing here to edit — which is the property the arm is built on.
declaration release-obligation coherent-set-0.50.6-release package-release
obl "$OBL/release-obligation.json"
must_fail "AC2: a package-release obligation on a PR whose merge triggers kit-auto-publish is flagged" \
  "obligation id=coherent-set-0.50.6-release kind=package-release"
if grep -q "kit-auto-publish.yml" <<<"$out"; then
  ok "AC2: the finding names the workflow that performs the act"
else
  bad "AC2: the finding names the workflow that performs the act" "$out"
fi
if grep -q "PRE-MERGE gate" <<<"$out"; then
  ok "AC5: the finding says a pre-act stop condition belongs in a pre-merge gate"
else
  bad "AC5: the finding says a pre-act stop condition belongs in a pre-merge gate" "$out"
fi

# Every kind the map declares reaches the same act — naming the artifact does not make it a
# different act, because .github#2409's coherent set publishes from three sibling tags at ONE commit.
for kind in coherent-set-release kit-release coord-engine-release drivers-release; do
  declaration "flag-$kind" "obligation-$kind" "$kind"
  obl "$OBL/flag-$kind.json"
  must_fail "AC2: kind=$kind is flagged too" "kind=$kind"
done

# ---- AC3: THE CONTROLLED COUNTERPART. An obligation that genuinely needs a human is NOT flagged.
#      "Warn always" would be indistinguishable from this arm at the AC2 leg and useless in practice.
declaration registry-record registry-record-0.50.6 registry-record
obl "$OBL/registry-record.json"
must_pass "AC3: an obligation that genuinely requires manual action is not flagged" \
  "name no act that merging this PR performs"

# ════════════════════════════════════════════════════════════════════════════════════════════════
# A TRIGGER IS NOT AN ACT (.github#2571)
#
# The five legs above are the PATCH line, where the merge really does cut the release. On the MINOR
# line — the line every coherent-set release produces (.github#2402) — `kit-auto-publish.yml` fires and
# `kit-auto-publish.py` terminally refuses it with `candidate-not-next-patch` (.github#2442). The act
# is real, manual and owed, and under the trigger-only rule its author had no declarable token for it:
# every kind that NAMED the act was flagged, leaving only mislabelling, silence, or a red PR.
#
# THE PAIRING IS THE EVIDENCE. Each leg here uses the SAME declaration as an AC2 leg above and differs
# only in the version line, so a change that merely stopped flagging things could not produce it — that
# would have disarmed .github#2533 rather than corrected it.
# ════════════════════════════════════════════════════════════════════════════════════════════════
OBL_CANDIDATE=0.52.0   # a coherent-set MINOR, the exact candidate .github#2571 measured
obl "$OBL/release-obligation.json"
must_pass "AC1: a coherent-set MINOR can declare the release it genuinely owes, with a token that NAMES the act" \
  "does NOT perform for the version it ships"
if grep -q "candidate-not-next-patch" <<<"$out"; then
  ok "AC2: the verdict quotes kit-auto-publish.py's OWN decision, not the workflow's trigger"
else
  bad "AC2: the verdict quotes kit-auto-publish.py's OWN decision, not the workflow's trigger" "$out"
fi
if grep -q "0.52.0 against feed frontier 0.51.1" <<<"$out"; then
  ok "AC2: the verdict names the candidate and the frontier it was scored against"
else
  bad "AC2: the verdict names the candidate and the frontier it was scored against" "$out"
fi
# `SCOPE_NOTES` is attached to the DECISION, so carrying it here costs nothing and cannot drift: the
# reader is told, in kit-auto-publish's own words, that this refusal is by design and not a bug.
if grep -q "2442" <<<"$out"; then
  ok "AC2: kit-auto-publish's own scope note reaches the author, unparaphrased"
else
  bad "AC2: kit-auto-publish's own scope note reaches the author, unparaphrased" "$out"
fi
if grep -q "IS merge-triggered" <<<"$out"; then
  ok "AC2: the green says the workflow DOES fire — the pass is about the decision, not the trigger"
else
  bad "AC2: the green says the workflow DOES fire — the pass is about the decision, not the trigger" "$out"
fi

for kind in coherent-set-release kit-release coord-engine-release drivers-release; do
  obl "$OBL/flag-$kind.json"
  must_pass "AC1: kind=$kind is declarable on the minor line too" "kind=$kind"
done

# Not a stable 0.x.y at all: `decide()` refuses `version-not-stable-0x-patch`, so the act is the
# author's. The rail is READ, so this leg needs nothing in the gate to know about 1.0.0.
OBL_CANDIDATE=1.0.0
obl "$OBL/release-obligation.json"
must_pass "AC2: a candidate off the stable 0.x line is declarable, by the same read decision" \
  "version-not-stable-0x-patch"

# At or below the frontier — nothing to publish, so nothing the merge performs.
OBL_CANDIDATE=0.51.1
obl "$OBL/release-obligation.json"
must_pass "AC2: a candidate that does not clear the frontier is declarable" \
  "candidate-not-strictly-newer-than-frontier"

# ---- AC3: THE CONTROLLED COUNTERPART, ASSERTED BESIDE THE NEW LEG. Restore the patch line and the
#      SAME declaration is flagged again. Without this pair the section above would be indistinguishable
#      from deleting the detection.
OBL_CANDIDATE=0.51.2
obl "$OBL/release-obligation.json"
must_fail "AC3: the SAME declaration on the PATCH line is still flagged, exactly as before" \
  "THE MERGE ITSELF PERFORMS"
if grep -q "eligible-authored-unpublished-patch" <<<"$out"; then
  ok "AC3: and the flag, too, quotes the decision rather than only the trigger"
else
  bad "AC3: and the flag, too, quotes the decision rather than only the trigger" "$out"
fi

# ════════════════════════════════════════════════════════════════════════════════════════════════
# THE ENUMERATED WORLDS ARE COMPLETE (.github#2571 round-1 repair)
#
# Calling an act manual means ruling out every post-merge world the feed can still reach — and the
# first draft of this arm ruled out exactly ONE, the frontier measured now, while its docstring claimed
# the verdict held on "any post-merge state of the world". The frontier ADVANCES, `decide()`'s rail is
# `candidate.patch == frontier.patch + 1`, so a forward move flips `candidate-not-next-patch` into
# `tag`. That claim was prose no leg could falsify, which is exactly why it survived authoring and an
# independent read; the repair is not only the fix but moving the invariant somewhere a leg CAN falsify.
# ════════════════════════════════════════════════════════════════════════════════════════════════
SWEEP="$HERE/completion-sweep.py"

# The counterexample, executably. Two patches above the observed frontier: refused today, cut the moment
# anyone publishes the patch in between — so the arm must flag it rather than hand its author a token.
OBL_CANDIDATE=0.58.3
OBL_FRONTIER=0.58.0
obl "$OBL/release-obligation.json"
must_fail "a candidate a REACHABLE frontier would admit is flagged, not declared manual" \
  "THE MERGE ITSELF PERFORMS"
# And the finding must not quote 0.58.2 as though it were on the feed: an author sent to check a number
# that was never measured has been told something false by a control.
if grep -q "not the 0.58.0 on the feed now" <<<"$out"; then
  ok "…and the finding says which frontier is hypothetical, rather than quoting it as measured"
else
  bad "…and the finding says which frontier is hypothetical, rather than quoting it as measured" "$out"
fi
OBL_CANDIDATE=0.51.2
OBL_FRONTIER=0.51.1

# The minor's green must claim the SWEEP, not one evaluation — it is the sentence an author acts on.
OBL_CANDIDATE=0.52.0
obl "$OBL/release-obligation.json"
must_pass "the minor's green rests on every reachable frontier, and says so" \
  "every other frontier the feed can still reach"
OBL_CANDIDATE=0.51.2

# THE INVARIANT ITSELF, GRADED BY BRUTE FORCE rather than asserted in a docstring. For every
# (candidate, observed) pair in a bounded grid, the shipped arm is compared against an exhaustive sweep
# over every reachable frontier AND over `tagExists` and both feed-presence states — facts the builder
# PINS rather than varies, so a pinned fact that turns out not to be uniquely permissive reds here too.
set +e
out="$(python3 "$SWEEP" "$GATE" "$HERE/../../scripts/kit-auto-publish.py" 2>&1)"; rc=$?
set -e
must_pass "the arm's enumerated worlds agree with brute force on every pair in the grid" \
  "agrees with brute force on all"

# ---- THE CANNED FACTS ARE LOCKED, like every other canned input: each one is a way to choose this
#      arm's verdict instead of measuring it, and .github#2571 makes them load-bearing.
for flag_pair in "--obligation-candidate-version=0.52.0" "--obligation-published-version=0.51.1"; do
  flag="${flag_pair%%=*}"
  value="${flag_pair#*=}"
  set +e
  out="$(env -u FSGG_KIT_COHERENCE_FIXTURE_OK python3 "$GATE" --obligation-arm \
    --obligations "$OBL/registry-record.json" "$flag" "$value" 2>&1)"; rc=$?
  set -e
  must_fail "$flag is refused without the fixture opt-in" "Refusing to run"
done

set +e
out="$(python3 "$GATE" --pr-arm --changed-files /dev/null --obligation-candidate-version 0.52.0 2>&1)"
rc=$?
set -e
must_fail "an obligation-arm canned input on another arm refuses rather than being ignored" \
  "mean nothing to the PR arm"

# The no-obligations assertion shares the declaration marker's PREFIX (plural, then a space). It is
# the claim that nothing is owed, not an obligation, and parsing it as one would flag `none`.
printf '[{"body": "<!-- fsgg:delivery-obligations none head=%s -->"}]' "$HEAD_SHA" > "$OBL/none.json"
obl "$OBL/none.json"
must_pass 'the no-obligations assertion is not parsed as an obligation' "carry no \`fsgg:delivery-obligation\`"

# ════════════════════════════════════════════════════════════════════════════════════════════════
# THE SHARED CROSS-LANGUAGE CORPUS (.github#2563). Seven hand-written legs used to sit here — the
# leading-line legs (.github#2544) and the indent-limit legs (its round-1 repair). They are gone, and
# their absence is the point rather than a saving.
#
# `#2544` collapsed a rule that lived in two places INSIDE the engine, and its round-1 repair then
# re-created a weaker version of the same hazard ACROSS the language boundary:
# `DeliveryApplication.leadingLine` and `check-kit-published-coherence.py`'s `_leading_line` each held
# their own copy of the CommonMark indent limit, each side pinned its copy with its OWN legs — the
# seven that were here — and the only coupling was two prose sentences that nothing read.
#
# So a ONE-SIDED edit reddened that side's legs. What was caught by nothing was a COORDINATED
# one-sided edit: moving one language's constant AND updating that same language's legs to match.
# That is not exotic. It is what a careful engineer does when they believe they are fixing a bug, and
# the legs would have agreed with them the whole way.
#
# `tests/delivery-leading-line/corpus.json` is where that boundary is now STATED, once. This arm and
# the F# suite both grade against it and neither keeps a private leg asserting a SINGLE COMMENT BODY's
# declares/inert verdict, so a coordinated edit has nowhere left to hide: move the limit in
# `check-kit-published-coherence.py` and these legs red against the corpus; edit the corpus to restore
# them and `DeliveryApplicationTests.fs` reds instead.
#
# The F# suite DOES retain four `#2544` legs with four-space declaration-form bodies
# (`DeliveryApplicationTests.fs:304`/`:307`/`:318`/`:492`) that the corpus cannot subsume — two are
# multi-comment scenarios, one of them turning on a `fsgg:delivery-receipt` marker this arm never
# parses, and one asserts the engine's diagnostic wording, which this arm does not emit. They make that
# side stricter, never more permissive, and they red alongside the corpus under the same mutation.
#
# These legs drive `--obligation-arm`, i.e. the REAL entry point `obligation_declarations`, not
# `_leading_line` in isolation — the pre-filter is the half `#2544` was actually filed about, and a
# corpus that graded only the helper would leave it ungraded.
CORPUS="$HERE/../delivery-leading-line/corpus.json"
CORPUS_DIR="$WORK/leading-line-corpus"
mkdir -p "$CORPUS_DIR"

# THIS ARM'S OWN COPY OF THE ENTRY COUNT, stated rather than counted from the file it is checking.
# `DeliveryApplicationTests.fs` states its own copy independently, and the corpus itself deliberately
# carries no count: a number stored beside the entries can be edited in the same breath as the entry
# it counts, which is the vacuity .github#2534 (an empty-corpus green) and .github#1768 (157 passing
# legs while the script was dying mid-run) each measured. Adding or removing an entry is therefore a
# deliberate three-file edit.
EXPECTED_CORPUS_ENTRIES=21

set +e
corpus_index="$(python3 - "$CORPUS" "$CORPUS_DIR" 2>&1 <<'PY'
import json, pathlib, sys

corpus, outdir = sys.argv[1], sys.argv[2]
doc = json.loads(pathlib.Path(corpus).read_text(encoding="utf-8"))
for entry in doc["entries"]:
    # The comment shape `gh api .../issues/<n>/comments` serves, so the arm is fed what it is fed live.
    (pathlib.Path(outdir) / f"{entry['name']}.json").write_text(
        json.dumps([{"body": entry["body"]}]), encoding="utf-8"
    )
    print(f"{entry['name']}\t{entry['verdict']}")
PY
)"
corpus_rc=$?
set -e

if [ "$corpus_rc" -ne 0 ]; then
  bad "the shared leading-line corpus is unreadable — an unreadable corpus is a no-verdict, not agreement" "$corpus_index"
  corpus_index=""
fi

corpus_count="$(printf '%s' "$corpus_index" | grep -c . || true)"
if [ "$corpus_count" -eq "$EXPECTED_CORPUS_ENTRIES" ]; then
  ok "the shared leading-line corpus carries all $EXPECTED_CORPUS_ENTRIES declared entries"
else
  bad "the shared leading-line corpus carries $corpus_count entries, not the $EXPECTED_CORPUS_ENTRIES this arm declares — a corpus shorter than the one this arm claims to check is how a cross-language coupling stops coupling silently (.github#2563). If you added or removed an entry deliberately, update EXPECTED_CORPUS_ENTRIES here AND corpusEntryCount in tests/FS.GG.Coord.Cli.Tests/DeliveryApplicationTests.fs."
fi

corpus_declares="$(printf '%s' "$corpus_index" | grep -c 'declares$' || true)"
corpus_inert="$(printf '%s' "$corpus_index" | grep -c 'inert$' || true)"
if [ "$corpus_declares" -gt 0 ] && [ "$corpus_inert" -gt 0 ]; then
  ok "the corpus pins the boundary from BOTH sides ($corpus_declares declares, $corpus_inert inert)"
else
  bad "the corpus carries $corpus_declares declares and $corpus_inert inert — one class alone pins only one side of the boundary, so it could not catch a fail-open in the other direction"
fi

# AND THE DISCRIMINATING SHAPES SURVIVE, which the count alone does not buy. A stated count forces a
# deliberate edit to add or remove an entry, but an author moving the limit could delete exactly the
# entries that discriminate and lower both stated counts, leaving two implementations that disagree over
# a corpus with nothing left to disagree ABOUT. `spaces-3` and `spaces-4` are the two shapes that
# discriminate a limit move in either direction — raise the limit and `spaces-4` changes verdict, lower
# it and `spaces-3` does — so they must be PRESENT.
#
# Presence, deliberately, and not a required verdict or a required disagreement between them. A limit
# that legitimately moved to 8 in BOTH languages is a coherent change this gate must let through, and
# it leaves those two entries agreeing; a leg demanding they disagree would red on exactly the correct
# action, which is how a gate teaches people to edit it out. The DIRECTION lives in the corpus alone —
# restating it here would re-create the second copy .github#2563 exists to remove.
corpus_below="$(printf '%s' "$corpus_index" | awk -F'\t' '$1 == "spaces-3" { print $1 }')"
corpus_at_limit="$(printf '%s' "$corpus_index" | awk -F'\t' '$1 == "spaces-4" { print $1 }')"
if [ -n "$corpus_below" ] && [ -n "$corpus_at_limit" ]; then
  ok "the corpus still carries both shapes that discriminate a limit move (spaces-3, spaces-4)"
else
  bad "the corpus must keep spaces-3 and spaces-4: they are the two shapes either side of the indented-code-block limit, and a corpus that has lost them can no longer tell a moved limit from an unmoved one (spaces-3 present='$corpus_below', spaces-4 present='$corpus_at_limit')"
fi

corpus_ran=0
while IFS="$(printf '\t')" read -r corpus_name corpus_verdict; do
  [ -n "$corpus_name" ] || continue
  obl "$CORPUS_DIR/$corpus_name.json"
  case "$corpus_verdict" in
    declares)
      must_fail "corpus $corpus_name: this body declares, exactly as the engine reads it" \
        "obligation id=$corpus_name kind=package-release"
      ;;
    inert)
      must_pass "corpus $corpus_name: this body is inert, exactly as the engine reads it" \
        "carry no \`fsgg:delivery-obligation\`"
      ;;
    *)
      bad "corpus $corpus_name carries the unknown verdict '$corpus_verdict'; only 'declares' and 'inert' are defined"
      ;;
  esac
  corpus_ran=$((corpus_ran + 1))
done <<EOF
$corpus_index
EOF

# EVERY entry READ was also EXECUTED. `failcount -eq 0` cannot tell "all agreed" from "the loop never
# ran" — .github#1768 is that exact defect, and a herestring loop that read nothing would otherwise be
# indistinguishable from 21 silent agreements.
if [ "$corpus_ran" -eq "$corpus_count" ]; then
  ok "every one of the $corpus_ran corpus entries this arm read was actually executed"
else
  bad "read $corpus_count corpus entries but executed $corpus_ran — the loop stopped early (.github#1768)"
fi

# A comment that DOES open with the marker but whose leading line is not a declaration is a
# no-verdict, not a guess — the engine owns the diagnosis of which field is malformed.
printf '[{"body": "<!-- fsgg:delivery-obligation id=BAD kind=package-release head=%s -->"}]' \
  "$HEAD_SHA" > "$OBL/malformed.json"
obl "$OBL/malformed.json"
must_fail "a marker-prefixed comment that does not parse is a no-verdict" "does not parse as a declaration"

# ---- NO SUBJECT IS NEVER A PASS (#266). Each of these is a distinct way to have read nothing.
set +e
out="$(python3 "$GATE" --obligation-arm 2>&1)"; rc=$?
set -e
must_fail "--obligation-arm with no --obligations refuses" "requires --obligations"

obl "$OBL/does-not-exist.json"
must_fail "an unreadable comments file is a no-verdict" "cannot read the PR comments"

printf 'not json' > "$OBL/bad.json"
obl "$OBL/bad.json"
must_fail "an unparsable comments file is a no-verdict" "not parsable as JSON"

printf '{"comments": []}' > "$OBL/object.json"
obl "$OBL/object.json"
must_fail "a comments payload that is not a list is a no-verdict" "not a list of comments"

printf '[42]' > "$OBL/wrong-shape.json"
obl "$OBL/wrong-shape.json"
must_fail "a comment that is neither a string nor an object with a body is a no-verdict" \
  "neither a string nor an object"

printf '[]' > "$OBL/empty.json"
obl "$OBL/empty.json"
must_pass "a PR with no comments at all declares nothing, and says so" "0 comment(s)"

# The bare-body shape is accepted as well as the `gh api` object shape, because the workflow may
# reasonably hand over either and a shape the arm cannot read must error rather than read as empty.
printf '["<!-- fsgg:delivery-obligation id=bare kind=package-release head=%s -->"]' "$HEAD_SHA" \
  > "$OBL/bare-bodies.json"
obl "$OBL/bare-bodies.json"
must_fail "a plain list of comment bodies is accepted as the subject" "kind=package-release"

# ---- THE TRIGGER IS READ, NEVER ASSUMED. These run against a synthetic tree at the mapped path, so
#      the verdict is a function of the workflow file and of nothing else.
OBL_TREE="$WORK/obligation-tree"
mkdir -p "$OBL_TREE/.github/workflows" "$OBL_TREE/scripts"
MAPPED="$OBL_TREE/.github/workflows/kit-auto-publish.yml"
# BOTH halves of the map are resolved against the tree the arm runs in (.github#2571), so the synthetic
# tree carries the REAL decision program. Copying it rather than stubbing it is the point: these legs
# vary the TRIGGER and must hold the decision constant at what the repository actually ships, or they
# would be measuring a stub's opinion of the frontier rail. `$DECIDER` is varied on its own, further
# down, with the trigger held constant instead.
DECIDER="$OBL_TREE/scripts/kit-auto-publish.py"
cp "$HERE/../../scripts/kit-auto-publish.py" "$DECIDER"

cat > "$MAPPED" <<'YAML'
name: kit-auto-publish
on:
  push:
    branches: [main]
jobs: {}
YAML
obl_in "$OBL_TREE" "$OBL/release-obligation.json"
must_fail "on: push: branches: [main] is a merge trigger" "THE MERGE ITSELF PERFORMS"

cat > "$MAPPED" <<'YAML'
name: kit-auto-publish
on:
  workflow_dispatch:
jobs: {}
YAML
obl_in "$OBL_TREE" "$OBL/release-obligation.json"
must_pass "an act that is NOT merge-triggered stops being flagged, with nothing here to update" \
  "no longer trigger on a merge into main"

cat > "$MAPPED" <<'YAML'
name: kit-auto-publish
on:
  push:
    branches: [release/*]
jobs: {}
YAML
obl_in "$OBL_TREE" "$OBL/release-obligation.json"
must_pass "a push trigger that does not reach main is not a merge trigger" "no longer trigger"

# GitHub matches `branches:` as GLOBS. A literal compare would read this as not covering `main` and
# report an automated act as manual — the fail-OPEN direction, so it is asserted explicitly.
cat > "$MAPPED" <<'YAML'
name: kit-auto-publish
on:
  push:
    branches: ["ma*"]
jobs: {}
YAML
obl_in "$OBL_TREE" "$OBL/release-obligation.json"
must_fail "a glob that matches main is a merge trigger" "THE MERGE ITSELF PERFORMS"

cat > "$MAPPED" <<'YAML'
name: kit-auto-publish
on:
  push:
    branches-ignore: [main]
jobs: {}
YAML
obl_in "$OBL_TREE" "$OBL/release-obligation.json"
must_pass "branches-ignore: [main] is not a merge trigger" "no longer trigger"

cat > "$MAPPED" <<'YAML'
name: kit-auto-publish
on:
  push:
    branches-ignore: [wip/**]
jobs: {}
YAML
obl_in "$OBL_TREE" "$OBL/release-obligation.json"
must_fail "branches-ignore that does not cover main leaves it a merge trigger" "THE MERGE ITSELF PERFORMS"

cat > "$MAPPED" <<'YAML'
name: kit-auto-publish
on: push
jobs: {}
YAML
obl_in "$OBL_TREE" "$OBL/release-obligation.json"
must_fail "the string spelling of on: is read, not skipped" "THE MERGE ITSELF PERFORMS"

cat > "$MAPPED" <<'YAML'
name: kit-auto-publish
on: [push, pull_request]
jobs: {}
YAML
obl_in "$OBL_TREE" "$OBL/release-obligation.json"
must_fail "the list spelling of on: is read, not skipped" "THE MERGE ITSELF PERFORMS"

# PyYAML resolves the bare key `on` to the boolean True (YAML 1.1). The quoted spelling must read
# identically, or half the repository's workflows would be invisible to this arm.
cat > "$MAPPED" <<'YAML'
name: kit-auto-publish
"on":
  push:
    branches: [main]
jobs: {}
YAML
obl_in "$OBL_TREE" "$OBL/release-obligation.json"
must_fail "the quoted \"on\": key reads the same as the bare one" "THE MERGE ITSELF PERFORMS"

# ---- A PATH-FILTERED MERGE TRIGGER IS A NO-VERDICT WHEN IT MATTERS, AND ONLY THEN. Whether THIS
#      merge fires it is a function of the diff, which this arm does not have.
cat > "$MAPPED" <<'YAML'
name: kit-auto-publish
on:
  push:
    branches: [main]
    paths: ["src/**"]
jobs: {}
YAML
obl_in "$OBL_TREE" "$OBL/release-obligation.json"
must_fail "a path-filtered merge trigger is a no-verdict when a mapped kind is declared" \
  "depends on the diff"
obl_in "$OBL_TREE" "$OBL/registry-record.json"
must_pass "…and is deferred, not raised, for a PR that declares no mapped kind" \
  "name no act that merging this PR performs"

# ---- THE MAP CANNOT ROT IN SILENCE. Every mapped workflow is opened on EVERY run, declarations or
#      not: a renamed or deleted file must be a no-verdict, never an arm that quietly matches nothing.
rm -f "$MAPPED"
obl_in "$OBL_TREE" "$OBL/registry-record.json"
must_fail "a mapped workflow that cannot be opened is a no-verdict, even with nothing declared" \
  "cannot read the mapped workflow"

printf 'name: kit-auto-publish\njobs: {}\n' > "$MAPPED"
obl_in "$OBL_TREE" "$OBL/registry-record.json"
must_fail "a mapped workflow with no on: block is a no-verdict" "no \`on:\` block"

printf 'name: kit-auto-publish\non: 42\njobs: {}\n' > "$MAPPED"
obl_in "$OBL_TREE" "$OBL/registry-record.json"
must_fail "an on: block that is none of the three legal spellings is a no-verdict" \
  "cannot tell what triggers the workflow"

printf 'name: kit-auto-publish\non:\n  push: [main]\njobs: {}\n' > "$MAPPED"
obl_in "$OBL_TREE" "$OBL/release-obligation.json"
must_fail "an on.push that is not a mapping is a no-verdict" "not a mapping"

printf 'name: kit-auto-publish\non:\n  push:\n    branches: main\njobs: {}\n' > "$MAPPED"
obl_in "$OBL_TREE" "$OBL/release-obligation.json"
must_fail "an on.push.branches that is not a list is a no-verdict" "not a list"

printf 'name: kit-auto-publish\non:\n  push:\n    branches: [main]\n    branches-ignore: [wip]\njobs: {}\n' > "$MAPPED"
obl_in "$OBL_TREE" "$OBL/release-obligation.json"
must_fail "branches and branches-ignore together is a no-verdict, not an invented precedence" \
  "rejects"

printf 'name: kit-auto-publish\non: [\njobs: {}\n' > "$MAPPED"
obl_in "$OBL_TREE" "$OBL/registry-record.json"
must_fail "an unparsable mapped workflow is a no-verdict" "not parsable as YAML"

# ---- THE DECISION HALF CANNOT ROT IN SILENCE EITHER (.github#2571), and its every unreadable state is
#      a NO-VERDICT rather than a verdict in either direction. Trigger held constant at a real merge
#      trigger; the DECISION PROGRAM is what varies. The dangerous failure here is not a red — it is a
#      quiet fallback to the trigger-only rule, which would look exactly like a pass.
cat > "$MAPPED" <<'YAML'
name: kit-auto-publish
on:
  push:
    branches: [main]
jobs: {}
YAML

rm -f "$DECIDER"
obl_in "$OBL_TREE" "$OBL/registry-record.json"
must_fail "a mapped decision program that cannot be read is a no-verdict, even with nothing mapped declared" \
  "cannot read the mapped decision program"

printf 'VERSION = 1\n' > "$DECIDER"
obl_in "$OBL_TREE" "$OBL/registry-record.json"
must_fail "a decision program exposing no callable decide is a no-verdict" "exposes no callable"

# `SystemExit(0)`, deliberately, and not a plain exception: it is a BaseException, so a load guarded by
# `except Exception` would let it propagate out of the gate and EXIT IT ZERO — a program that never
# loaded, reported as a clean run. The leg names the fail-OPEN, not merely the failure.
printf 'import sys\nsys.exit(0)\n' > "$DECIDER"
obl_in "$OBL_TREE" "$OBL/registry-record.json"
must_fail "a decision program that exits 0 at import is a no-verdict, not an inherited green" \
  "failed to load"

# The arm reads the mapped program's OWN `patch_tuple` to enumerate the frontiers the feed can still
# reach (.github#2571 round-1 repair), so a program that has `decide` but not that grammar is a
# no-verdict too — the alternative is this file keeping a second copy of what "forward" means.
printf 'def decide(facts):\n    return {"action": "refuse", "reason": "stub"}\n' > "$DECIDER"
obl_in "$OBL_TREE" "$OBL/release-obligation.json"
must_fail "a decision program exposing no callable patch_tuple is a no-verdict" \
  "no callable \`patch_tuple\`"

# Every stub below carries one, so these legs reach `decide()` rather than stopping at the grammar
# read above. It answers a constant, which keeps each stub to the one behaviour its leg is about.
stub_decider() { # $1 body of decide(), as python source lines
  { printf 'def patch_tuple(value):\n    return (0, 0)\n\n'; printf '%b' "$1"; } > "$DECIDER"
}

stub_decider 'def decide(facts):\n    raise ValueError("no idea")\n'
obl_in "$OBL_TREE" "$OBL/release-obligation.json"
must_fail "a decide() that RAISES on the candidate facts is a no-verdict, not a pass" \
  "raised on the candidate fact set"

stub_decider 'import sys\ndef decide(facts):\n    sys.exit(0)\n'
obl_in "$OBL_TREE" "$OBL/release-obligation.json"
must_fail "a decide() that exits 0 is a no-verdict, not an inherited green" \
  "raised on the candidate fact set"

stub_decider 'def decide(facts):\n    return "nope"\n'
obl_in "$OBL_TREE" "$OBL/release-obligation.json"
must_fail "a decide() that returns no typed action is a no-verdict" "carries no string"

# THE FAIL-CLOSED DIRECTION, STATED AS A LEG. An action this file has never classified must not be
# guessed — guessing "performs" would flag a legitimate obligation, guessing "does not" would restore
# .github#2533's defect, and only one of those two is visible to the person it happens to.
stub_decider 'def decide(facts):\n    return {"action": "somethingNobodyMapped", "reason": "new"}\n'
obl_in "$OBL_TREE" "$OBL/release-obligation.json"
must_fail "a decide() action this arm does not classify is a no-verdict, never a guess" \
  "neither performing the act nor declining to"

# THE DECISION IS ACTUALLY CONSULTED, not merely loaded: a stub that unconditionally cuts flags a
# candidate the REAL program refuses. This is the leg that would survive if the call were dropped and
# the load kept, which is exactly the shape a careless refactor produces.
stub_decider 'def decide(facts):\n    return {"action": "tag", "reason": "stub-always-cuts"}\n'
OBL_CANDIDATE=0.52.0
obl_in "$OBL_TREE" "$OBL/release-obligation.json"
must_fail "the mapped program's decide() is CALLED — a stub that always cuts flags a minor candidate" \
  "stub-always-cuts"
OBL_CANDIDATE=0.51.2

# ---- EVERY TOUCH OF THE LOADED PROGRAM IS A PLACE ITS CODE RUNS (.github#2571 round-2 repair).
#      Reading `patch_tuple` from the mapped program — the right call for the round-1 repair — put a
#      NEW call outside the fail-closed boundary, and a `sys.exit(0)` there made this arm exit 0 in
#      total silence. The legs above did not catch it: they cover the `decide()` site, and the only
#      `patch_tuple` leg was the MISSING case, which never reaches the call. A green fixture is not
#      evidence that a guard exists — only a leg that removes the guard is.
cuts_nothing_decide='def decide(facts):\n    return {"action": "refuse", "reason": "stub"}\n'

printf 'import sys\ndef patch_tuple(v):\n    sys.exit(0)\n%b' "$cuts_nothing_decide" > "$DECIDER"
obl_in "$OBL_TREE" "$OBL/release-obligation.json"
must_fail "a patch_tuple that exits 0 is a typed no-verdict, not a silent zero" \
  "reachable-world enumeration failed"

printf 'def patch_tuple(v):\n    raise ValueError("no idea")\n%b' "$cuts_nothing_decide" > "$DECIDER"
obl_in "$OBL_TREE" "$OBL/release-obligation.json"
must_fail "a patch_tuple that RAISES is a typed no-verdict" "reachable-world enumeration failed"
# A raw traceback is not a verdict. It is also the tell that a call escaped the boundary rather than
# being handled by it, so the absence of one is worth asserting rather than assuming.
if ! grep -q "Traceback (most recent call last)" <<<"$out"; then
  ok "…reported as a verdict rather than as an unhandled traceback"
else
  bad "…reported as a verdict rather than as an unhandled traceback" "$out"
fi

printf 'def patch_tuple(v):\n    return 5\n%b' "$cuts_nothing_decide" > "$DECIDER"
obl_in "$OBL_TREE" "$OBL/release-obligation.json"
must_fail "a patch_tuple that returns the wrong shape is a typed no-verdict" \
  "reachable-world enumeration failed"

# PEP 562: a module may define a module-level `__getattr__`, so even `getattr(module, "decide", None)`
# runs the program's code — and the three-argument form swallows only AttributeError. This is the
# WIDEST of the four holes and the one the round-2 review did not name: it fires during the
# UNCONDITIONAL map read, so it greened every subject, including a PR declaring nothing mapped at all,
# printing nothing. The subject here is deliberately the unmapped declaration, so the leg fails if the
# guard is ever narrowed to the per-declaration path.
printf 'import sys\ndef __getattr__(name):\n    sys.exit(0)\n' > "$DECIDER"
obl_in "$OBL_TREE" "$OBL/registry-record.json"
must_fail "a module __getattr__ that exits 0 is a no-verdict, even for a PR declaring nothing mapped" \
  "reading \`decide\` from the mapped decision program"

cp "$HERE/../../scripts/kit-auto-publish.py" "$DECIDER"

# ---- AC5: AND THE CANDIDATE ITSELF. With no canned version the arm evaluates `<Version>` from
#      --csproj exactly as the PR arm does, so a project it cannot evaluate is a no-verdict. This is
#      also the leg that proves the LIVE observation path is wired at all: every other leg here hands
#      the versions in, and a canned-only fixture would pass with the live read deleted.
set +e
out="$(cd "$OBL_TREE" && python3 "$GATE" --obligation-arm \
  --obligations "$OBL/release-obligation.json" --csproj "$WORK/no-such-project.csproj" 2>&1)"; rc=$?
set -e
must_fail "AC5: a candidate <Version> the arm cannot evaluate is a no-verdict, never a pass" \
  "no-such-project.csproj"
if grep -q "dotnet msbuild" <<<"$out"; then
  ok "AC5: …and it says so in terms of the evaluation it could not perform"
else
  bad "AC5: …and it says so in terms of the evaluation it could not perform" "$out"
fi

# THE OBSERVATION IS DEFERRED. An unrelated PR must not pay an MSBuild evaluation and a nuget.org
# round-trip for a question it never asked — the same discipline the PR arm states as "the feed was not
# read". Same unevaluatable project, but nothing mapped is declared, so nothing is observed.
set +e
out="$(cd "$OBL_TREE" && python3 "$GATE" --obligation-arm \
  --obligations "$OBL/registry-record.json" --csproj "$WORK/no-such-project.csproj" 2>&1)"; rc=$?
set -e
must_pass "the candidate is observed only when a mapped kind is declared, not on every run" \
  "name no act that merging this PR performs"

# ---- THE ARMS STAY SEPARATE. A flag silently ignored is a caller who believes they configured a
#      run they did not get.
set +e
out="$(python3 "$GATE" --obligation-arm --pr-arm --obligations "$OBL/none.json" 2>&1)"; rc=$?
set -e
must_fail "--pr-arm and --obligation-arm together refuse" "different arms with different subjects"

set +e
out="$(python3 "$GATE" --pr-arm --obligations "$OBL/none.json" --changed-files /dev/null \
  --published-version 999.0.0 2>&1)"; rc=$?
set -e
must_fail "--obligations supplied to another arm refuses rather than being ignored" \
  "means nothing to the other arms"

set +e
out="$(python3 "$GATE" --obligation-arm --obligations "$OBL/none.json" --changed-files /dev/null 2>&1)"
rc=$?
set -e
must_fail "a --pr-arm canned input supplied to the obligation arm refuses" \
  "mean nothing to the obligation arm"

set +e
out="$(python3 "$GATE" --obligation-arm --obligations "$OBL/none.json" \
  --fixture-manifest /dev/null 2>&1)"; rc=$?
set -e
must_fail "--obligation-arm and the manifest fixture flags refuse together" \
  "different arms with different subjects"

# ════════════════════════════════════════════════════════════════════════════════════════════════
# GATE-INVERSION EVIDENCE (pnext-item §3). Two mutations of the SHIPPED gate, each deleting one half
# of the detection, each proving the leg above it was measuring that half and not passing by
# accident. A gate whose inversion survives is a material finding by definition.
# ════════════════════════════════════════════════════════════════════════════════════════════════
MUT_OBL="$WORK/obligation-mutant"
mkdir -p "$MUT_OBL/scripts"
cp "$HERE/../../scripts/fsgg_feed.py" "$MUT_OBL/scripts/fsgg_feed.py"

mutate() { # $1 destination stem; $2 exact source line; $3 replacement
  cp "$GATE" "$MUT_OBL/scripts/$1.py"
  python3 - "$MUT_OBL/scripts/$1.py" "$2" "$3" <<'PY'
import sys
path, old, new = sys.argv[1:4]
text = open(path, encoding="utf-8").read()
if text.count(old) != 1:
    sys.exit(f"mutation anchor appears {text.count(old)} times, not once: {old!r}")
open(path, "w", encoding="utf-8").write(text.replace(old, new))
PY
}

# M1 — DELETE THE DETECTION ITSELF: never join a declaration to the automation that performs it.
mutate mutant-detection \
  "        automation = mapped.get(declaration.kind)" \
  "        automation = None  # MUTATION: the .github#2533 detection deleted"
set +e
out="$(python3 "$MUT_OBL/scripts/mutant-detection.py" --obligation-arm \
  --obligations "$OBL/release-obligation.json" 2>&1)"; rc=$?
set -e
must_pass "INVERSION M1: with the kind→automation join deleted, the AC2 leg goes GREEN" \
  "name no act that merging this PR performs"

# M2 — DELETE THE FAIL-CLOSED MAP READ: stop opening the mapped workflows, so a renamed or deleted
# one can no longer be noticed.
# The anchor carries the line BELOW the loop header because `_assert_map` (.github#2571) opens with the
# same header, and `mutate` refuses an ambiguous anchor rather than mutating the wrong one.
mutate mutant-maprot \
  "    for automation in MERGE_AUTOMATION:
        parsed[automation.workflow] = workflow_triggers(automation.workflow)" \
  "    for automation in ():  # MUTATION: the map-rot read deleted
        parsed[automation.workflow] = workflow_triggers(automation.workflow)"
rm -f "$MAPPED"
set +e
out="$(cd "$OBL_TREE" && python3 "$MUT_OBL/scripts/mutant-maprot.py" --obligation-arm \
  --obligations "$OBL/registry-record.json" 2>&1)"; rc=$?
set -e
must_pass "INVERSION M2: with the map read deleted, a MISSING mapped workflow goes GREEN" \
  "name no act that merging this PR performs"

# M3 — DELETE THE .github#2571 HALF: never ask the mapped program whether it would cut anything, so the
# verdict falls back to the trigger alone. The minor-line leg that PASSES on the shipped gate goes RED,
# which is the pre-.github#2571 behaviour and the defect itself: a genuinely-owed coherent-set release
# with no declarable token.
mutate mutant-trigger-only \
  "        decision = None
        if automation.decision:" \
  "        decision = None
        if False:  # MUTATION: the .github#2571 decision half deleted"
set +e
out="$(python3 "$MUT_OBL/scripts/mutant-trigger-only.py" --obligation-arm \
  --obligations "$OBL/release-obligation.json" \
  --obligation-candidate-version 0.52.0 --obligation-published-version 0.51.1 2>&1)"; rc=$?
set -e
must_fail "INVERSION M3: with the decision half deleted, the coherent-set MINOR is flagged again" \
  "THE MERGE ITSELF PERFORMS"

# M4 — DELETE THE DECISION HALF'S UNCONDITIONAL LOAD, the counterpart of M2. A renamed or deleted
# `kit-auto-publish.py` then stops being noticed on a run that declares nothing mapped.
mutate mutant-decider-rot \
  "            decision_function(automation.decision)" \
  "            pass  # MUTATION: the decision-map-rot read deleted"
cat > "$MAPPED" <<'YAML'
name: kit-auto-publish
on:
  push:
    branches: [main]
jobs: {}
YAML
rm -f "$DECIDER"
set +e
out="$(cd "$OBL_TREE" && python3 "$MUT_OBL/scripts/mutant-decider-rot.py" --obligation-arm \
  --obligations "$OBL/registry-record.json" 2>&1)"; rc=$?
set -e
must_pass "INVERSION M4: with the decision-map read deleted, a MISSING decision program goes GREEN" \
  "name no act that merging this PR performs"

# M5 — RE-PIN THE FRONTIER TO THE OBSERVED VALUE, which is precisely the round-1 defect: the arm stops
# enumerating the frontiers the feed can still reach and scores only the world as it stands. The sweep
# above must RED on this mutant, or it is not measuring the completeness it claims to measure. This is
# the inversion for the invariant the item exists to make sound, and the one leg that could not be
# written while that invariant lived in a docstring.
mutate mutant-pinned-frontier \
  "    if here and there and here[1] >= 1:" \
  "    if False:  # MUTATION: the frontier re-pinned to the observed value"
set +e
out="$(python3 "$SWEEP" "$MUT_OBL/scripts/mutant-pinned-frontier.py" \
  "$HERE/../../scripts/kit-auto-publish.py" 2>&1)"; rc=$?
set -e
must_fail "INVERSION M5: with the frontier re-pinned, the sweep REDS — so it measures completeness" \
  "UNSOUND"
# Named pairs, not a bare count: a sweep that could only say "something is wrong" would send whoever
# hits it back to re-derive the counterexample this leg already has.
if grep -q "MISSES a reachable cut" <<<"$out"; then
  ok "INVERSION M5: …and it names the pair and the direction of the unsoundness"
else
  bad "INVERSION M5: …and it names the pair and the direction of the unsoundness" "$out"
fi

# M6/M7 — REMOVE THE FAIL-CLOSED BOUNDARY FROM ONE TOUCH OF THE LOADED PROGRAM, and watch this arm
# exit ZERO in silence. These two are the reason `_guarded` exists as one named function rather than a
# `try` repeated at each site: both holes were introduced by adding a perfectly ordinary-looking line,
# and neither reddened a 217-leg fixture. The assertion is deliberately `rc == 0 AND no output`,
# because that pair — a pass leaving no trace of not having looked — is the exact signature of the
# defect, and a leg matching on a message would not have caught it.
cat > "$MAPPED" <<'YAML'
name: kit-auto-publish
on:
  push:
    branches: [main]
jobs: {}
YAML

silent_green() { # $1 leg name; $2 mutant script; $3 comments file
  set +e
  out="$(cd "$OBL_TREE" && python3 "$2" --obligation-arm --obligations "$3" \
    --obligation-candidate-version 0.58.1 --obligation-published-version 0.58.0 2>&1)"
  rc=$?
  set -e
  if [ "$rc" -eq 0 ] && [ -z "$out" ]; then
    ok "$1"
  else
    bad "$1 (expected a SILENT exit 0; got rc=$rc)" "$out"
  fi
}

mutate mutant-unguarded-enumeration \
  "    completions = _guarded(
        f\"{automation.decision}'s reachable-world enumeration failed for {PACKAGE} {candidate} \"
        f\"against frontier {frontier}\",
        lambda: automation.completions(module, candidate, frontier),
    )" \
  "    completions = automation.completions(module, candidate, frontier)  # MUTATION: guard removed"
printf 'import sys\ndef patch_tuple(v):\n    sys.exit(0)\ndef decide(f):\n    return {"action": "refuse", "reason": "s"}\n' > "$DECIDER"
silent_green "INVERSION M6: unguard the patch_tuple read and a sys.exit(0) there greens the arm SILENTLY" \
  "$MUT_OBL/scripts/mutant-unguarded-enumeration.py" "$OBL/release-obligation.json"

mutate mutant-unguarded-attribute \
  "    decide = _guarded(
        f\"reading \`decide\` from the mapped decision program {path!r} failed\",
        lambda: getattr(module, \"decide\", None),
    )" \
  "    decide = getattr(module, \"decide\", None)  # MUTATION: guard removed"
printf 'import sys\ndef __getattr__(name):\n    sys.exit(0)\n' > "$DECIDER"
silent_green "INVERSION M7: unguard the attribute lookup and a PEP 562 __getattr__ greens EVERY subject" \
  "$MUT_OBL/scripts/mutant-unguarded-attribute.py" "$OBL/registry-record.json"

# ---- AND THE SHAPE, NOT ONLY THE FOUR HOLES SOMEBODY THOUGHT OF. Behaviour legs can only cover the
#      ways in that were imagined; this one reads the gate's AST and asserts that every touch of a
#      loaded program sits inside `_guarded`, so the NEXT ordinary-looking line to reach into that
#      program reds here at authoring time instead of in a third review round. Its inversion is free:
#      the M6 mutant above is a real unguarded touch, so the same checker must flag it.
BOUNDARY="$HERE/guarded-boundary.py"
set +e
out="$(python3 "$BOUNDARY" "$GATE" 2>&1)"; rc=$?
set -e
must_pass "every touch of a loaded decision program is inside the fail-closed boundary" \
  "is inside \`_guarded\`"

set +e
out="$(python3 "$BOUNDARY" "$MUT_OBL/scripts/mutant-unguarded-enumeration.py" 2>&1)"; rc=$?
set -e
must_fail "INVERSION: the boundary check FLAGS the unguarded touch M6 introduces" "UNGUARDED"

# ---- .github#2652: THE CHECKER'S OWN BLIND SPOTS. The version of `guarded-boundary.py` that landed
#      with the round-2 repair BLESSED five ordinary-looking edits, each of them a shape a future
#      change would plausibly reach for, and one of them the single mistake `_guarded`'s own API most
#      invites. A device that certifies the next hole as safe is worse than no device, because the
#      round it was added to buy is exactly the round it would be trusted in.
#
#      The eager-argument shape is a GENUINE silent false green at runtime, so it is proved twice:
#      once through the arm itself (M8, asserting the same `rc == 0 AND no output` signature as
#      M6/M7 — the defect's actual shape), and once through the checker that exists to make M8
#      impossible to write. The other four are proved against the checker, which is where they are
#      meant to be caught: at authoring time, before anyone runs the arm.
ANCHOR_COMPLETIONS="    completions = _guarded(
        f\"{automation.decision}'s reachable-world enumeration failed for {PACKAGE} {candidate} \"
        f\"against frontier {frontier}\",
        lambda: automation.completions(module, candidate, frontier),
    )"

boundary_run() { # $1 mutant stem
  set +e
  out="$(python3 "$BOUNDARY" "$MUT_OBL/scripts/$1.py" 2>&1)"
  rc=$?
  set -e
}

blessed_shape() { # $1 leg name; $2 mutant stem; $3 replacement source
  mutate "$2" "$ANCHOR_COMPLETIONS" "$3"
  boundary_run "$2"
  must_fail "$1" "UNGUARDED"
}

# M8 — `_guarded(what, call)` takes a THUNK. Written with a CALL, the inner call is evaluated BEFORE
# `_guarded` runs and therefore outside its `try`: the same hole as M6, wearing the boundary's own
# syntax. The behaviour below is M6's, to the byte.
mutate mutant-eager-guard "$ANCHOR_COMPLETIONS" \
  "    completions = _guarded(  # MUTATION: a call, not a thunk — evaluated OUTSIDE the guard
        \"enumeration failed\",
        automation.completions(module, candidate, frontier),
    )"
printf 'import sys\ndef patch_tuple(v):\n    sys.exit(0)\ndef decide(f):\n    return {"action": "refuse", "reason": "s"}\n' > "$DECIDER"
silent_green "INVERSION M8: a _guarded call written with a CALL instead of a thunk greens the arm SILENTLY" \
  "$MUT_OBL/scripts/mutant-eager-guard.py" "$OBL/release-obligation.json"

boundary_run mutant-eager-guard
must_fail "INVERSION: the boundary check FLAGS the eager _guarded argument M8 introduces" "NOT A THUNK"

# The four remaining blessed shapes. Each is the SAME touch as M6 spelled a way the first checker's
# argument-and-attribute-chain walk could not see, and each returned `ok` from it (measured).
blessed_shape "a loaded program handed over as a KEYWORD argument is a touch" \
  mutant-keyword-touch \
  "    completions = automation.completions(mod=module, candidate=candidate, observed=frontier)  # MUTATION"

blessed_shape "a loaded program handed over through a STARRED element is a touch" \
  mutant-starred-touch \
  "    completions = automation.completions(*[module, candidate, frontier])  # MUTATION"

blessed_shape "a loaded program reached through a SUBSCRIPT is a touch" \
  mutant-subscript-touch \
  "    completions = module.__dict__[\"decide\"]({})  # MUTATION"

blessed_shape "a loaded program reached through an ALIAS is a touch" \
  mutant-alias-touch \
  "    m = module  # MUTATION: the alias the first checker did not follow
    completions = m.decide({})"

# ...and NOT everything is flagged. Without this leg, a checker that reported UNGUARDED on every call
# would pass all four legs above — the shape that makes a gate unkeepable rather than sound. The
# positive control on the shipped gate is the leg above; this one adds an ordinary new line to it.
mutate mutant-boundary-noise "$ANCHOR_COMPLETIONS" \
  "    _noise = str(candidate) + str(frontier)  # MUTATION: an ordinary line reaching no program
$ANCHOR_COMPLETIONS"
boundary_run mutant-boundary-noise
must_pass "the boundary check leaves an ordinary line that reaches no loaded program alone" \
  "is inside \`_guarded\`"

# THE SUBJECT IS DERIVED, NOT DECLARED. `run_obligation_arm` loads a decision program too and was
# absent from the hand-written holder set, so a touch added THERE was invisible to the checker. The
# holders are now derived from the loader calls a function makes, which is why this mutant — a new
# function nobody has told the checker about — is graded.
mutate mutant-new-holder "def _assert_map() -> None:" \
  "def _future_holder(automation):  # MUTATION: a NEW function that loads a decision program
    program = decision_function(automation.decision)
    return program.decide({})


def _assert_map() -> None:"
boundary_run mutant-new-holder
must_fail "a NEW function that loads a decision program is graded without editing the checker" \
  "UNGUARDED"

# ...and the declared floor cannot rot into naming nothing. A subject that silently narrows to the
# empty set is the same defect as an unwired gate: it reports `ok` having looked at nothing.
mutate mutant-holder-rot "def merge_performs_act(" \
  "def merge_performs_act_renamed(  # MUTATION: a declared holder renamed away"
boundary_run mutant-holder-rot
must_fail "a DECLARED holder that no longer exists is MAP ROT, never a quietly smaller subject" \
  "MAP ROT"

# ---- .github#2667: AND THE VALUE THAT ESCAPES THROUGH A CALL RESULT. Every shape above is about
#      RECOGNISING a handover. This one is about PROPAGATING the tracked set through a value that
#      provably came out of the program — a different kind of gap, and the one `LOADERS`' own
#      `decision_function` entry is a hand-placed patch for.
#
#      `decision_function` already lifts the program's own `decide` into a local under the guard, so
#      the hole needed exactly one line nobody had written: a call to it. The lifted callable stopped
#      being tracked at the guard's closing paren, and the invocation was therefore certified safe.
#      Proved twice, on the M8 discipline, because the shape has a measured runtime consequence: once
#      through the ARM (the same `rc == 0 AND no output` signature as M6/M7/M8), and once through the
#      checker that exists to make writing it impossible in the first place.
ANCHOR_RETURN="            f\"merge performs the act, and cannot substitute its own copy of the rule.\"
        )
    return module"

# M9 — invoke the callable the guard handed back. The guard covered the LIFT; nothing covers the CALL.
mutate mutant-lifted-callable "$ANCHOR_RETURN" \
  "            f\"merge performs the act, and cannot substitute its own copy of the rule.\"
        )
    decide({})  # MUTATION: the callable the guard lifted out, invoked outside it
    return module"
# The REAL decision program with ONLY `decide` overridden to exit. A hand-written stub would stop the
# control below earlier, on a missing `patch_tuple`, and a control that reds for a different reason
# than the one under test is not a control.
cp "$HERE/../../scripts/kit-auto-publish.py" "$DECIDER"
printf '\n\nimport sys as _exiting_sys  # MUTATION\n\n\ndef decide(facts):  # MUTATION: overrides the real one\n    _exiting_sys.exit(0)\n' >> "$DECIDER"
silent_green "INVERSION M9: invoke the callable lifted out under the guard and a sys.exit(0) in it greens the arm SILENTLY" \
  "$MUT_OBL/scripts/mutant-lifted-callable.py" "$OBL/release-obligation.json"

# ...and the CONTROL beside it, because M9's silent zero is only evidence if the SHIPPED gate reds on
# the identical decision program and the identical declaration. It does: the arm reaches `decide()`
# through the guard, so the same `sys.exit(0)` becomes a typed no-verdict instead of a pass. This is
# also the first leg to cover a `decide` that EXITS — the .github#2571 legs above cover `patch_tuple`
# and the PEP 562 `__getattr__`, and neither reaches this call.
set +e
out="$(cd "$OBL_TREE" && python3 "$GATE" --obligation-arm --obligations "$OBL/release-obligation.json" \
  --obligation-candidate-version 0.58.1 --obligation-published-version 0.58.0 2>&1)"
rc=$?
set -e
must_fail "CONTROL: through the boundary, that same decide() exit is a typed no-verdict" \
  "decide() raised on the candidate fact set"

boundary_run mutant-lifted-callable
must_fail "INVERSION: the boundary check FLAGS the lifted-callable invocation M9 introduces" \
  "UNGUARDED: 1 touch(es)"
# Exactly one, and it is the CALL — not the guarded lift that produced the value, which is correct
# code. A checker that reported the lift too would be reporting the boundary working.
if grep -q "in decision_function(): decide({})" <<<"$out"; then
  ok "INVERSION M9: …and it names the invocation, not the guarded lift that produced the value"
else
  bad "INVERSION M9: …and it names the invocation, not the guarded lift that produced the value" "$out"
fi

# The same escape spelled without the guard: a bare `getattr` result, bound to a local and invoked
# later. The LIFT here was always flagged — it hands `module` over, which is M7's shape — so a leg
# asserting `UNGUARDED` would pass with or without this repair. The COUNT is what isolates it: the
# invocation is a SECOND finding, and the second is the one the previous checker could not see.
mutate mutant-lifted-getattr "$ANCHOR_RETURN" \
  "            f\"merge performs the act, and cannot substitute its own copy of the rule.\"
        )
    lifted = getattr(module, \"decide\", None)  # MUTATION: lifted by a bare getattr
    lifted({})  # MUTATION: ...and invoked later
    return module"
boundary_run mutant-lifted-getattr
must_fail "a bare getattr result, bound and then INVOKED, is a touch as well as the lift" \
  "UNGUARDED: 2 touch(es)"

lifted_shape() { # $1 leg name; $2 mutant stem; $3 inserted source; $4 needle; $5 pass|fail
  mutate "$2" "$ANCHOR_RETURN" \
    "            f\"merge performs the act, and cannot substitute its own copy of the rule.\"
        )
$3
    return module"
  boundary_run "$2"
  if [ "$5" = pass ]; then must_pass "$1" "$4"; else must_fail "$1" "$4"; fi
}

# ---- A LIFTED VALUE IS TRACKED, NOT SPECIAL-CASED. The first repair on this row tracked the lift in
#      a SECOND, weaker tier that was a touch only when the tracked name sat in func position. That
#      kept the shipped gate green, but it licensed every other way to reach a callable — and each of
#      these is certified `ok` by that rule while producing the same silent rc 0 through the real arm.
#      They are graded here because the rule now grades a lift exactly like the program it came out
#      of; the only narrowing is NON_INVOKING, asserted below.
lifted_shape "handing a lifted callable to a callee that WILL invoke it is a touch (keyword)" \
  mutant-lifted-sorted "    sorted([{}], key=decide)  # MUTATION" "UNGUARDED: 1 touch(es)" fail
lifted_shape "…and positionally — \`map\`, the ordinary refactor of the decide() loop" \
  mutant-lifted-map "    list(map(decide, [{}]))  # MUTATION" "UNGUARDED: 1 touch(es)" fail
lifted_shape "…and an invocation spelled OFF the name rather than through it" \
  mutant-lifted-dunder "    decide.__call__({})  # MUTATION" "UNGUARDED: 1 touch(es)" fail
lifted_shape "…and one deferred through functools.partial, then called" \
  mutant-lifted-partial \
  "    import functools  # MUTATION
    functools.partial(decide, {})()  # MUTATION" "UNGUARDED: 1 touch(es)" fail

# A conditional and a boolean operator BRANCH to an operand rather than transforming it, so an
# expression that could evaluate to the program is one — in either position. Cheap to follow, and
# closing them shrinks the disclosed residual to a single property rather than a list of spellings.
lifted_shape "a tracked reference reached through a CONDITIONAL is a touch" \
  mutant-lifted-ifexp "    (decide if path else decide)({})  # MUTATION" "UNGUARDED: 1 touch(es)" fail
lifted_shape "…and through a BOOLEAN operator, in argument position" \
  mutant-lifted-boolop "    sorted([{}], key=(None or decide))  # MUTATION" "UNGUARDED: 1 touch(es)" fail

# ...and the residual class is asserted OPEN rather than merely described, so the disclosure in
# `WHAT THIS CANNOT SEE` cannot quietly drift from the code. Both need a step the walk does not take:
# indexing into a container, and an unbound call result. If a later change closes either, this leg
# reds and the docstring must be corrected in the same commit — which is the point of asserting it.
lifted_shape "the disclosed residual is genuinely open: indexing into a container is NOT seen" \
  mutant-residual-index "    [decide][0]({})  # MUTATION" "is inside \`_guarded\`" pass
lifted_shape "…nor is an unbound call result, for want of a name to track it under" \
  mutant-residual-unbound \
  "    _guarded(\"x\", lambda: getattr(module, \"decide\", None))({})  # MUTATION" \
  "is inside \`_guarded\`" pass

# ...nor a program-defined dunder reached by SYNTAX, which is the widest of the three and the one that
# looks least like a boundary question. The unit of this check is `ast.Call`, so the SAME hazard is
# graded in its call spelling and unseen in its syntactic one. The other half of that asymmetry is
# already asserted above by `mutant-repr-not-inert` — `repr(decide)` is a touch — and this leg is
# deliberately the f-string that runs the identical `__repr__` without a call. The pair is what pins
# the boundary: move it either way and one of the two reds, forcing `WHAT THIS CANNOT SEE` to be
# corrected in the same commit. A second `repr(decide)` leg here would assert nothing the one above
# does not, so there is not one.
lifted_shape "…nor a dunder reached by SYNTAX rather than by a call: the unit of this check is the call" \
  mutant-residual-fstring "    _m = f\"decide is {decide!r}\"  # MUTATION" "is inside \`_guarded\`" pass

# ...and the runtime half for the CLASS, not only for the direct invocation M9 covers. `map` is the
# most idiomatic of the four — `merge_performs_act` already loops over completions asking `decide`
# about each — and it carries the identical `rc == 0 AND no output` signature.
silent_green "INVERSION M10: a lifted callable reached through map() greens the arm just as SILENTLY" \
  "$MUT_OBL/scripts/mutant-lifted-map.py" "$OBL/release-obligation.json"

# ---- THE ONE NARROWING, LOCKED FROM BOTH SIDES. `NON_INVOKING` exempts three strictly unary builtins
#      from the HANDOVER rule, because the arm's own `if not callable(decide)` is correct code that
#      cannot run the program. An allowlist is exactly the shape that rots into an escape hatch, so it
#      is asserted in both directions: the three entries stay green, and the two rejected candidates
#      plus the non-unary form stay RED. Removing `type` from the set reds the first leg; adding
#      `repr` to it reds the second; dropping `cannot_invoke`'s arity test reds the third.
lifted_shape "a lifted callable handed to a provably inert unary builtin is left alone" \
  mutant-non-invoking "    _kind = type(decide)  # MUTATION: type(x) is Py_TYPE(x)" \
  "is inside \`_guarded\`" pass
lifted_shape "…but repr() is NOT inert — it runs the object's __repr__ — so it stays a touch" \
  mutant-repr-not-inert "    _r = repr(decide)  # MUTATION" "UNGUARDED: 1 touch(es)" fail
lifted_shape "…and the exemption is the UNARY form only: 3-arg type() runs a metaclass" \
  mutant-type-three-arg "    _c = type(\"X\", (), {\"d\": decide})  # MUTATION" \
  "UNGUARDED: 1 touch(es)" fail

cp "$HERE/../../scripts/kit-auto-publish.py" "$DECIDER"

# ---- THE ARM IS WIRED, ON EVERY PULL REQUEST. An unwired gate is the exact defect .github#2533 is
#      about: an artifact that exists, is reviewed, and is not connected to anything.
if python3 - "$HERE/../../.github/workflows/kit-published-coherence.yml" <<'PY'
import re, sys
text = open(sys.argv[1], encoding="utf-8").read()
job = re.search(r"(?ms)^  pr-arm:\n(.*?)(?=^  [a-z]|\Z)", text)
assert job, "the pr-arm job is not where this assertion expects it"
body = job.group(1)
# The KEY, not the substring: this job's own comments discuss `continue-on-error` and why it is not
# used, and a substring test that its own rationale trips is a test nobody can keep green.
assert not re.search(r"(?m)^\s*continue-on-error\s*:", body), (
    "the pr-arm job gained continue-on-error; a finding would report green:\n" + body
)
# The job must stay reportable on EVERY pull request: a `paths:` filter on the trigger, or an `if`
# narrowing this job, is how a gate reports nothing on the PRs that needed it.
trigger = re.search(r"(?ms)^on:\n(.*?)(?=^[a-z])", text)
assert trigger, "the on: block is not where this assertion expects it"
pr_block = re.search(r"(?ms)^  pull_request:\n(.*?)(?=^  [a-z])", trigger.group(1))
# YAML KEYS, not substrings. The block's own comment explains at length why the `paths:` filter that
# used to sit here was removed (.github#1597), so a substring test reds on its own rationale.
directives = [ln for ln in (pr_block.group(1) if pr_block else "").splitlines()
              if ln.strip() and not ln.strip().startswith("#")]
assert not any(re.match(r"\s*paths(-ignore)?\s*:", ln) for ln in directives), (
    "the pull_request trigger regained a paths: filter, so PRs it excludes get no check run at "
    "all:\n" + "\n".join(directives)
)
PY
then
  ok "the obligation arm is wired on the pr-arm job, on every pull request"
else
  bad "the obligation arm is wired on the pr-arm job, on every pull request"
fi

# ════════════════════════════════════════════════════════════════════════════════════════════════
# THE STEP IS EXECUTED, NOT GREPPED (.github#2533 round-1 repairs 1 and 2, ONE cause).
#
# The two legs that used to stand here asserted `"--obligation-arm" in body` and `"issues/" in body
# and "comments" in body` — SUBSTRING checks over the workflow's text. Independent review measured
# what that bought: replacing the fetch with `echo '[]' > pr-comments.json` while leaving
# COMMENTS_URL in place left this suite at 150 passed, 0 failed; and removing the invocation behind
# a `# was: --obligation-arm` decoy comment left the companion green too (the FS.GG.Templates#379
# shape). A substring check reads as an assertion and is not one.
#
# Underneath them was a real defect of the same shape, which is why they are one repair: the step
# piped `gh api` into `jq` with no `shell:` and no `defaults:`, so GitHub ran `bash -e {0}` WITHOUT
# pipefail. `-e` does not abort on a failed pipe HEAD, so a transport failure produced an empty
# stream, `add // []` fabricated `[]`, and the arm exited 0 having read nothing. An empty comment
# list is a LEGAL state, so the gate cannot tell that apart — only the step can.
#
# So both legs now EXTRACT the step's own `run:` body and RUN it, under GitHub's actual default
# invocation, with stubs for `gh` and `python`. Each is followed by the mutation that would have
# defeated its substring predecessor, asserted to change the outcome.
# ════════════════════════════════════════════════════════════════════════════════════════════════
OBL_STEP="$WORK/obligation-step.sh"
OBL_BIN="$WORK/obligation-bin"
STUB_LOG="$WORK/obligation-python-calls.log"
STEP_CWD="$WORK/step-cwd"
mkdir -p "$OBL_BIN" "$STEP_CWD"

python3 - "$HERE/../../.github/workflows/kit-published-coherence.yml" "$OBL_STEP" <<'PY'
import sys
import yaml

wf_path, out_path = sys.argv[1], sys.argv[2]
wf = yaml.safe_load(open(wf_path, encoding="utf-8"))
steps = (wf.get("jobs") or {}).get("pr-arm", {}).get("steps") or []
name = "Does this PR declare a post-merge obligation the merge itself performs?"
step = next((s for s in steps if s.get("name") == name), None)
if step is None:
    sys.exit("the obligation step is GONE from kit-published-coherence.yml's pr-arm job")
# GitHub runs a `run:` step with no `shell:` as `bash -e {0}`. This fixture emulates exactly that, so
# a `shell:` override would make the emulation WRONG rather than merely different — and an emulation
# that supplies a guarantee the runner does not is how a behaviour leg becomes decorative again.
if "shell" in step:
    sys.exit(
        f"the obligation step declares shell: {step['shell']!r}. This fixture runs its body under "
        f"GitHub's default `bash -e`; teach it the new invocation before changing the step."
    )
if "${{" in step["run"]:
    sys.exit(
        "the obligation step's run: body interpolates a GitHub expression, so it cannot be executed "
        "here. Keep the expressions in env:, where this fixture can supply them."
    )
open(out_path, "w", encoding="utf-8").write(step["run"])
PY

# A stub `gh` that can fail at TRANSPORT — the case the pipe used to swallow. Note it fails the way
# a network error does (non-zero, nothing on stdout), not the way a 404 does (error JSON on stdout),
# because the 404 case already failed closed and is exactly what made this one easy to miss.
cat > "$OBL_BIN/gh" <<'SH'
#!/usr/bin/env bash
if [ "${STUB_GH_FAIL:-0}" = "1" ]; then
  echo "stub gh: transport failure" >&2
  exit 1
fi
printf '%s' "${STUB_GH_BODY:-[]}"
SH
# A stub `python` that RECORDS its argv instead of running the gate. What is being measured here is
# the step, not the arm; the arm has its own legs above.
cat > "$OBL_BIN/python" <<'SH'
#!/usr/bin/env bash
printf '%s\n' "$*" >> "$STUB_LOG"
exit 0
SH
chmod +x "$OBL_BIN/gh" "$OBL_BIN/python"

run_obl_step() { # $1 script to run; env STUB_GH_FAIL / STUB_GH_BODY select the stub's behaviour
  : > "$STUB_LOG"
  rm -f "$STEP_CWD"/*.json
  set +e
  out="$(cd "$STEP_CWD" && PATH="$OBL_BIN:$PATH" STUB_LOG="$STUB_LOG" \
    STUB_GH_FAIL="${STUB_GH_FAIL:-0}" STUB_GH_BODY="${STUB_GH_BODY:-[]}" \
    COMMENTS_URL="repos/FS-GG/.github/issues/1/comments" bash -e "$1" 2>&1)"
  rc=$?
  set -e
}

# ---- REPAIR 1's BEHAVIOUR: a failed fetch must red the step, not fabricate an empty subject.
STUB_GH_FAIL=1 run_obl_step "$OBL_STEP"
if [ "$rc" -ne 0 ]; then
  ok "a transport failure in the comment fetch reds the step (exit $rc)"
else
  bad "a transport failure in the comment fetch reds the step (exit $rc)" "$out"
fi
if [ ! -s "$STUB_LOG" ]; then
  ok "…and the gate is never invoked on the subject that failure would have fabricated"
else
  bad "…and the gate is never invoked on the subject that failure would have fabricated" \
    "$(cat "$STUB_LOG")"
fi

# ---- REPAIR 2's BEHAVIOUR: the step really does invoke the arm, with the PR's fetched comments.
STUB_GH_FAIL=0 STUB_GH_BODY='[{"body":"hello"}]' run_obl_step "$OBL_STEP"
if [ "$rc" -eq 0 ]; then
  ok "a successful fetch runs the step to completion"
else
  bad "a successful fetch runs the step to completion (exit $rc)" "$out"
fi
if grep -q -- "--obligation-arm" "$STUB_LOG" && grep -q -- "--obligations" "$STUB_LOG"; then
  ok "the step INVOKES the obligation arm, observed in the recorded argv rather than in the YAML text"
else
  bad "the step INVOKES the obligation arm, observed in the recorded argv rather than in the YAML text" \
    "$(cat "$STUB_LOG")"
fi
# The subject handed to the gate must be the file the fetch wrote, carrying the fetched content.
# Compared as JSON, not as bytes: `jq -s add` re-renders the payload, so a byte compare would be
# asserting jq's formatting rather than the fetch's content.
if [ "$(jq -cS . "$STEP_CWD/pr-comments.json" 2>/dev/null)" = '[{"body":"hello"}]' ]; then
  ok "the file the gate is pointed at holds what the fetch actually returned"
else
  bad "the file the gate is pointed at holds what the fetch actually returned" \
    "$(cat "$STEP_CWD/pr-comments.json" 2>/dev/null)"
fi

# ---- THE TWO MUTATIONS THAT DEFEATED THE SUBSTRING LEGS, now asserted to change the outcome.
#      Each reproduces exactly what independent review did to the shipped workflow.
python3 - "$OBL_STEP" "$WORK/step-echo-subject.sh" <<'PY'
import re, sys
src, dst = sys.argv[1], sys.argv[2]
text = open(src, encoding="utf-8").read()
# The critic's mutation: replace the fetch with a fabricated subject, leaving COMMENTS_URL in place
# so every substring the old leg looked for is still present in the file.
mutated, n = re.subn(r"(?m)^\s*gh api .*$", "echo '[]' > pr-comment-pages.json", text)
if n != 1:
    sys.exit(f"expected exactly one `gh api` line to mutate, found {n}")
open(dst, "w", encoding="utf-8").write(mutated)
PY
STUB_GH_FAIL=1 run_obl_step "$WORK/step-echo-subject.sh"
if [ "$rc" -eq 0 ]; then
  ok "INVERSION: with the fetch replaced by a literal, the transport leg goes GREEN — so it measures the fetch"
else
  bad "INVERSION: with the fetch replaced by a literal, the transport leg goes GREEN — so it measures the fetch" \
    "$out"
fi

python3 - "$OBL_STEP" "$WORK/step-decoy-invocation.sh" <<'PY'
import re, sys
src, dst = sys.argv[1], sys.argv[2]
text = open(src, encoding="utf-8").read()
# The FS.GG.Templates#379 decoy: remove the invocation but leave its flags in a comment, which is
# what defeated a substring check while a bare deletion did not.
mutated, n = re.subn(
    r"(?ms)^\s*python scripts/check-kit-published-coherence\.py.*?--obligations pr-comments\.json\s*$",
    "# was: python scripts/check-kit-published-coherence.py --obligation-arm --obligations pr-comments.json",
    text,
)
if n != 1:
    sys.exit(f"expected exactly one gate invocation to mutate, found {n}")
open(dst, "w", encoding="utf-8").write(mutated)
PY
STUB_GH_FAIL=0 STUB_GH_BODY='[{"body":"hello"}]' run_obl_step "$WORK/step-decoy-invocation.sh"
if [ ! -s "$STUB_LOG" ]; then
  ok "INVERSION: with the invocation behind a decoy comment, nothing is invoked — so the leg measures the call"
else
  bad "INVERSION: with the invocation behind a decoy comment, nothing is invoked — so the leg measures the call" \
    "$(cat "$STUB_LOG")"
fi

echo
echo "kit-published-coherence fixture: $pass passed, $failcount failed"
[ "$failcount" -eq 0 ] || exit 1

# HOW MANY LEGS ACTUALLY RAN. `$failcount -eq 0` alone cannot tell "every leg passed" from "the
# script stopped early and the ones that ran happened to pass" — .github#1768 is that exact defect,
# 157 passing legs while the script was dying mid-run. `set -e` covers a command that FAILS; it does
# not cover a leg silently skipped because a variable it needed was empty, or an `if` whose python
# heredoc exited 0 without asserting. So the count is asserted, and it must be updated deliberately
# when legs are added — a fixture whose leg count nobody states is one that can quietly shrink.
# 107 + 50 obligation-arm legs (.github#2533). Four of the 50 are gate-inversion controls: M1/M2 on
# the arm itself, plus the two round-1 repair controls that execute the workflow step and prove each
# behaviour leg reds when the thing it names is removed.
# The 7 hand-written leading-line/indent legs that lived here (.github#2544 and its round-1 repair)
# are GONE, replaced by the shared cross-language corpus (.github#2563):
# + 21 corpus legs, one per entry of tests/delivery-leading-line/corpus.json, driven through the real
#   `--obligation-arm` entry point. That file is the ONE statement of the leading-line boundary and
#   `tests/FS.GG.Coord.Cli.Tests/DeliveryApplicationTests.fs` grades the engine against the same
#   verdicts, so neither language keeps a private leg asserting a SINGLE COMMENT BODY's declares/inert
#   verdict — which is what makes a COORDINATED one-sided edit (one constant plus that language's own
#   legs) impossible to hide. Four `#2544` engine-only legs with four-space declaration-form bodies do
#   remain in the F# suite; the corpus cannot subsume them (multi-comment scenarios and diagnostic
#   wording), they make that side stricter, and they red alongside the corpus under the same mutation.
# + 4 non-vacuity legs for that corpus: its stated entry count, both verdict classes present, the two
#   shapes either side of the limit still PRESENT (presence only — NOT that they disagree; a limit
#   legitimately moved in both languages leaves them agreeing, and see the note above the leg itself),
#   and every entry READ also EXECUTED. A shared corpus either side could consume zero entries of would be
#   a second way to be green while wrong, not a coupling.
# The arithmetic, so it can be checked rather than trusted: 163 was 107 + 50 obligation-arm legs + 3
# leading-line legs + 3 indent-limit legs. Seven of those are retired here — the 3 leading-line legs
# (newline-led, space-led, fenced), the 3 indent-limit legs (four-space, tab, three-spaces), and
# prose-first, which was one of the 50. That leaves 107 + 49 = 156, plus 21 corpus legs and 3
# non-vacuity legs: 163 - 7 + 25 = 181.
# + 29 legs for .github#2571, which splits "the merge performs this act" into the TRIGGER and the
#   mapped program's own DECISION. 16 are the decision legs proper — the coherent-set MINOR that is now
#   declarable, the four other mapped kinds on that line, the two other refusal reasons the read rail
#   supplies for free, the PATCH counterpart asserted BESIDE them so this cannot read as disarming
#   .github#2533, and the three locking/misdirection legs for the two new canned inputs. 11 are the
#   decision half's fail-closed posture, which is the half that could rot into a silent fallback to the
#   trigger-only rule: a missing, unloadable, `sys.exit(0)`-at-import, decide-less, raising, exiting,
#   untyped or unclassified decision program, the leg proving `decide()` is CALLED and not merely
#   loaded, and the pair proving the live candidate read is wired AND deferred until a mapped kind is
#   declared. 2 are gate-inversion controls (M3/M4), matching M1/M2 above: deleting the decision half
#   flags the minor again, and deleting its unconditional load lets a missing decision program pass.
#   181 + 29 = 210.
# + 7 legs for .github#2571's ROUND-1 REPAIR, which is about the soundness of that decision rather than
#   its existence. The arm pinned the feed frontier to the value observed NOW and claimed the verdict
#   held on any post-merge state of the world; the frontier moves FORWARD, `decide()`'s rail is
#   `candidate.patch == frontier.patch + 1`, and a forward move flips `candidate-not-next-patch` into
#   `tag`. 3 are the behaviour: the counterexample (a candidate two patches above the observed frontier)
#   is flagged, the finding says WHICH frontier is hypothetical rather than quoting it as measured, and
#   the minor's green claims the whole sweep. 1 is the mapped program's `patch_tuple` being read rather
#   than restated, so a program missing it is a no-verdict. 1 is `completion-sweep.py` grading the
#   invariant by BRUTE FORCE over every reachable frontier and over the facts the builder pins — the
#   invariant moved out of a docstring nothing could falsify and into a leg that can. 2 are its
#   gate-inversion control (M5): re-pinning the frontier makes that sweep RED, naming the pair and the
#   direction. 210 + 7 = 217.
# + 7 legs for .github#2571's ROUND-2 REPAIR, whose subject is the boundary rather than the verdict.
#   Reading `patch_tuple` out of the mapped program — the right call for the round-1 repair — put a new
#   call OUTSIDE the fail-closed guard, and a `sys.exit(0)` there made this arm exit 0 in total silence;
#   an audit of every site that touches the loaded module then found a fourth hole nobody had reported,
#   a PEP 562 module `__getattr__` at the `decide` lookup, which greened EVERY subject including a PR
#   declaring nothing at all. 4 are the behaviour: `patch_tuple` exiting 0, raising, and returning the
#   wrong shape are typed no-verdicts (the raising one additionally asserting the absence of a raw
#   traceback, which is the tell that a call escaped the boundary), and the `__getattr__` leg is driven
#   through an UNMAPPED declaration so it fails if the guard is ever narrowed to the per-declaration
#   path. 2 are gate-inversion controls (M6/M7), one per guard, asserting `rc == 0 AND no output` —
#   that pair is the defect's actual signature, and a leg matching on a message would not have caught
#   it. Neither hole reddened a 217-leg fixture, which is why the guard is now ONE named function
#   (`_guarded`) that can be audited by grepping for the sites that do not use it, rather than a `try`
#   repeated by discipline at each site. 217 + 7 = 224.
# + 2 legs for `guarded-boundary.py`, which grades the SHAPE rather than the four holes anybody
#   thought of: it reads the gate's AST and asserts every touch of a loaded program sits inside
#   `_guarded`, so the next ordinary-looking line to reach into that program reds at authoring time
#   instead of in a later review round. Its inversion reuses M6's mutant, which IS a real unguarded
#   touch, so the checker must flag it. 224 + 2 = 226.
# + 9 legs for .github#2652, whose subject is that checker rather than the arm. It certified five
#   ordinary-looking edits as safe, one of them a real silent false green: `_guarded(what, call)`
#   takes a THUNK, and written with a CALL the inner call is evaluated before the guard runs. 1 is
#   that hole through the ARM (M8, the same rc==0-and-no-output signature as M6/M7, because the
#   runtime consequence is what makes this a defect rather than a style note) and 1 is the checker
#   flagging the same mutant. 4 are the other blessed shapes — a keyword argument, a starred element,
#   a subscript, an alias — each the SAME touch as M6 spelled a way the first walk could not see. 1 is
#   the control proving the checker is not simply red on every call, without which those five would
#   pass on a checker that flagged everything. 2 are the subject itself: the holder set is now derived
#   from the loader calls a function makes (the hand-written set had already missed `run_obligation_arm`,
#   a third function that loads a program), so a NEW holder is graded, and a DECLARED holder that has
#   been renamed away is MAP ROT rather than a quietly smaller subject. 226 + 9 = 235.
# + 18 legs for .github#2667, whose subject is that checker again — but a DATAFLOW gap rather than the
#   five SPELLING gaps above. A tracked reference that left through a CALL RESULT stopped being
#   tracked, so `decision_function`'s own `decide`, lifted out under the guard and kept in a local,
#   was certified safe to invoke.
#
#   5 are the hole itself. 1 is it through the ARM (M9, the same rc==0-and-no-output signature as
#   M6/M7/M8) and 1 is its CONTROL — the identical decision program and declaration against the
#   SHIPPED gate, where the same `decide()` exit becomes a typed no-verdict; that is also the first
#   leg here to cover a `decide` that exits, rather than a `patch_tuple` or a `__getattr__`. 2 are the
#   checker flagging M9's mutant: the count (exactly one finding) and the finding naming the
#   INVOCATION rather than the guarded lift that produced the value. 1 more is the same escape spelled
#   as a bare `getattr` result, asserted on the COUNT because the lift alone was always flagged and a
#   leg matching `UNGUARDED` would have passed before the repair as well.
#
#   5 exist because the FIRST repair on this row was too weak, and review caught it. It tracked the
#   lift in a second, lenient tier that was a touch only in func position — green on the shipped gate,
#   but licensing `sorted(xs, key=decide)`, `map(decide, xs)`, `decide.__call__(x)` and
#   `functools.partial(decide, x)()`, each certified `ok` while producing the identical silent rc 0
#   through the real arm. A lift is now graded exactly like the program it came out of, so 4 legs grade
#   those spellings and 1 (M10) proves the runtime half for the CLASS rather than only for M9's direct
#   invocation, using `map` because `merge_performs_act` already loops over completions asking
#   `decide` about each.
#
#   3 lock the ONE narrowing from both sides. `NON_INVOKING` exempts three strictly unary builtins from
#   the HANDOVER rule, because `if not callable(decide)` is correct code that cannot run the program.
#   An allowlist is precisely the shape that rots into an escape hatch, so the entries are asserted
#   green AND the rejected candidates asserted red: `repr` runs `__repr__` and `type(name, bases, ns)`
#   runs a metaclass, so neither is exempt.
#
#   2 more follow a CONDITIONAL and a BOOLEAN operator, which branch to an operand rather than
#   transforming it. They are cheap to walk, and closing them is what lets the disclosure state a
#   single property instead of listing spellings.
#
#   3 assert the residual class is genuinely OPEN — indexing into a container, an unbound call result,
#   and a dunder reached by SYNTAX rather than by a call. A disclosure nothing checks drifts from the
#   code silently, which on a cause already three generations deep is how the fourth generation is
#   born; and a disclosure claiming to be COMPLETE is worse than one that does not, because an
#   enumeration invites the reader to keep looking while a completeness claim tells them to stop. The
#   third of these pins one half of an asymmetry whose other half is the `repr` leg above — the same
#   `__repr__`, once with a call and once without — so moving the call boundary either way reds one of
#   the two. 235 + 18 = 253.
EXPECTED_LEGS=253
if [ "$pass" -ne "$EXPECTED_LEGS" ]; then
  echo "FAIL  expected $EXPECTED_LEGS passing legs, counted $pass — the fixture ran a different set" \
       "of legs than it was written to run. If you added or removed legs, update EXPECTED_LEGS in" \
       "this file; if you did not, the script stopped early (.github#1768)."
  exit 1
fi
echo "kit-published-coherence fixture: all $EXPECTED_LEGS declared legs ran"
