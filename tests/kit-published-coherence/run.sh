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
echo "kit-published-coherence fixture: $pass passed, $failcount failed"
[ "$failcount" -eq 0 ] || exit 1
