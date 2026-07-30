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
must_fail "the pr and tag arms refuse to run at once" \
  "--pr-arm and --tag-arm are different arms with different subjects"

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

echo
echo "kit-published-coherence fixture: $pass passed, $failcount failed"
[ "$failcount" -eq 0 ] || exit 1

# HOW MANY LEGS ACTUALLY RAN. `$failcount -eq 0` alone cannot tell "every leg passed" from "the
# script stopped early and the ones that ran happened to pass" — .github#1768 is that exact defect,
# 157 passing legs while the script was dying mid-run. `set -e` covers a command that FAILS; it does
# not cover a leg silently skipped because a variable it needed was empty, or an `if` whose python
# heredoc exited 0 without asserting. So the count is asserted, and it must be updated deliberately
# when legs are added — a fixture whose leg count nobody states is one that can quietly shrink.
EXPECTED_LEGS=107
if [ "$pass" -ne "$EXPECTED_LEGS" ]; then
  echo "FAIL  expected $EXPECTED_LEGS passing legs, counted $pass — the fixture ran a different set" \
       "of legs than it was written to run. If you added or removed legs, update EXPECTED_LEGS in" \
       "this file; if you did not, the script stopped early (.github#1768)."
  exit 1
fi
echo "kit-published-coherence fixture: all $EXPECTED_LEGS declared legs ran"
