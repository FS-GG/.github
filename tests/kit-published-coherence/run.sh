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
echo "== tag-arm (.github#1784) =="

# `#1772` made `kit/v<version>` the ref a receiver's bump-shape rule is resolved from. A tag is a
# MUTABLE ref and a published package is not, so the question these legs are about is never "does the
# tag exist" — it is "does the tag still resolve to the commit that produced the published package".
# The comparand is each version's published .nuspec `<repository commit=...>`, canned here.
#
# Both canned inputs mirror the REAL wire shapes: the tag list is literal `git ls-remote` output, so
# these legs exercise the shipped parser rather than a hand-written mirror of it (the #1780 review
# lesson — a fixture that mirrors its subject proves only the mirror).
TAGPUB="$WORK/tag-published.tsv"
TAGREFS="$WORK/tag-refs.txt"

C1=$(printf '1%.0s' {1..40})
C2=$(printf '2%.0s' {1..40})
C3=$(printf '3%.0s' {1..40})

tagarm() { # extra args appended
  set +e
  out="$(python3 "$GATE" --tag-arm --tag-arm-published "$TAGPUB" --tag-arm-tags "$TAGREFS" "$@" 2>&1)"
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
must_pass "every published version's tag resolves to its artifact's commit" "has not moved since publication"

# PEELING IS LOAD-BEARING, not a detail. An ANNOTATED tag's own object id is not the commit the #1772
# resolver checks the rule out at; comparing the wrong one would red every annotated release — 8 of
# the 23 live tags are annotated. `refs/tags/X^{}` must beat `refs/tags/X`.
{
  printf '%s\trefs/tags/kit/v0.16.0\n' "$C1"
  printf '%s\trefs/tags/kit/v0.17.0\n' "$C3"
  printf '%s\trefs/tags/kit/v0.17.0^{}\n' "$C2"
} > "$TAGREFS"
tagarm
must_pass "an annotated tag is compared by its PEELED commit, not its tag object" "has not moved since publication"

# ...and in the REVERSED emission order. The row above is git's real order, so on its own it cannot
# tell "peeled always wins" apart from "the last row wins" — a single-dict rewrite would pass it.
{
  printf '%s\trefs/tags/kit/v0.16.0\n' "$C1"
  printf '%s\trefs/tags/kit/v0.17.0^{}\n' "$C2"
  printf '%s\trefs/tags/kit/v0.17.0\n' "$C3"
} > "$TAGREFS"
tagarm
must_pass "the peeled commit wins whichever order ls-remote emits the two rows in" \
  "has not moved since publication"

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
must_fail "missing and moved are reported separately" "(1 missing, 1 moved)"

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
  "carry a kit/v<version> tag"
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

# The canned branch must NARROW its subject exactly as the live branch does, or a fixture can encode
# a subject production could never produce, and legs start proving things about nothing.
printf '0.16.0-preview.1\t%s\n' "$C1" > "$TAGPUB"
tagarm
must_fail "a prerelease is filtered from the canned subject, exactly as it is from the feed" \
  "resolved ZERO published"

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
tagarm --tag-arm-tags "$WORK/no-such-refs.txt"
must_fail "an unreadable tag list is unresolved" "cannot read the canned ls-remote tag list"

tagarm --tag-arm-published "$WORK/no-such-pub.tsv"
must_fail "an unreadable published list is unresolved" "cannot read the canned published-version list"

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
assert gate.nuspec_repository_commit("0.17.0", good, repository=REPO) == COMMIT, "real nuspec shape"

# Same, with a .git suffix and a trailing slash — both are the same repository.
for url in ("https://github.com/FS-GG/.github.git", "https://github.com/FS-GG/.github/"):
    variant = nuspec(f'<repository type="git" url="{url}" commit="{COMMIT}" />')
    assert gate.nuspec_repository_commit("0.17.0", variant, repository=REPO) == COMMIT, url


def refuses(body: str, wanted: str, what: str) -> None:
    try:
        gate.nuspec_repository_commit("0.17.0", nuspec(body), repository=REPO)
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
    assert gate.nuspec_repository_commit("0.17.0", ok_url, repository=REPO) == COMMIT, benign
try:
    gate.nuspec_repository_commit("0.17.0", b"<package", repository=REPO)
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
for flag_pair in "--tag-arm-published=$TAGPUB" "--tag-arm-tags=$TAGREFS"; do
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
out="$(python3 "$GATE" --pr-arm --tag-arm-tags "$TAGREFS" 2>&1)"; rc=$?
set -e
must_fail "a tag-arm input on the pr arm is refused, not ignored" \
  "are --tag-arm inputs and mean nothing to the PR arm"

set +e
out="$(python3 "$GATE" --tag-arm-tags "$TAGREFS" --lock "$LOCK" --fixture-manifest "$CANON" \
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
    "the published job does not run the tag arm — the kit/v* tags .github#1772 resolves rules from "
    "would be load-bearing and unchecked again:\n" + "\n".join(directives)
)
assert not any("continue-on-error" in ln for ln in directives), (
    "the published job gained continue-on-error; a tag defect would report green"
)
# The tag arm must not be ABORTED by the staleness step above it. They are independent subjects with
# independent remedies, and a stale kit is the state most likely to coincide with tag surgery.
tag_step = re.search(r"(?ms)^      - name: Do the kit/v\* tags still resolve.*?\n(.*?)(?=^      - |\Z)", job)
assert tag_step, "the tag-arm step is not where this assertion expects it"
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
