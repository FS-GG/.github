#!/usr/bin/env bash
# Fixture for scripts/check-kit-published-coherence.py — the gate that asks whether the coordination
# kit the FLEET MATERIALIZES (the newest published FS.GG.Kit's shipped bytes) still matches canonical
# (registry/repos.lock). .github#1291, epic #266.
#
# The gate exists because the published package's CONTENT is a scalar no other gate looked at:
# verify-package.sh proves the kit is derived-correct at PACK time, and coordination-coherence checks
# each RECEIVER — but nothing rechecked the published artifact as canonical moved. So a kit-source bump
# (#660: fs.gg.coord.cli 0.6.0 -> 0.7.0 in the manifest) landed on main, repos.lock advanced, every
# pack-time gate stayed green, and the published FS.GG.Kit 0.1.0 silently carried the stale bytes until
# the first from-scratch materialize (FS.GG.Net) drifted.
#
# So this fixture spends most of its length on the FAILURE legs: it proves the gate reds when the
# published manifest carries a digest canonical no longer contains (the #1291 state exactly), and
# ERRORS — never "coherent" — when the lock or manifest it measures is missing/empty/malformed.
#
# Every negative leg asserts the REASON, not just a non-zero exit — the .github#266 vacuous-failure
# defect (SDD#299) was a "must fail" test whose non-zero exit came from a path guard rather than the
# thing under test. `must_fail` therefore takes a required pattern.
#
# No network: the gate's --fixture-manifest + --lock serve canned files. Throwaway files under a temp
# dir. Mirrors tests/engine-pin/run.sh.

set -euo pipefail

# The suite runs the gate by path, which would otherwise litter scripts/__pycache__ into a repo that
# has no .gitignore.
export PYTHONDONTWRITEBYTECODE=1

# `--fixture-manifest` is locked to this harness: the gate refuses a canned manifest unless this is
# set, so a stray `--fixture-manifest` in CI fails rather than silently reporting green.
export FSGG_KIT_COHERENCE_FIXTURE_OK=1

HERE="$(cd "$(dirname "$0")" && pwd)"
GATE="$HERE/../../scripts/check-kit-published-coherence.py"

WORK="$(mktemp -d "${TMPDIR:-/tmp}/kit-coherence-fixture.XXXXXX")"
trap 'rm -rf "$WORK"' EXIT

pass=0; failcount=0
ok()  { echo "PASS  $1"; pass=$((pass+1)); }
bad() { echo "FAIL  $1"; [ -n "${2:-}" ] && printf '%s\n' "$2" | sed 's/^/    | /'; failcount=$((failcount+1)); }

# 64-hex digests standing in for real sha256s. Distinct letters so a mismatch is unambiguous.
A=$(printf 'a%.0s' {1..64}); B=$(printf 'b%.0s' {1..64}); C=$(printf 'c%.0s' {1..64})
C_OLD=$(printf 'd%.0s' {1..64})   # a stale config digest canonical no longer contains

lock() { # $1 = file, then <sha> <path> pairs — repos.lock is `<sha>  <path>`
  local f="$1"; shift
  : > "$f"
  while [ "$#" -ge 2 ]; do printf '%s  %s\n' "$1" "$2" >> "$f"; shift 2; done
}

run() { # $1 = lock, $2 = manifest  -> stdout+stderr, exit code in $rc
  set +e
  out="$(python3 "$GATE" --lock "$1" --fixture-manifest "$2" 2>&1)"
  rc=$?
  set -e
}

must_pass() { # $1 = label, $2 = required stdout pattern
  if [ "$rc" -ne 0 ]; then bad "$1 (expected exit 0, got $rc)" "$out"; return; fi
  if ! grep -q -- "$2" <<<"$out"; then bad "$1 (exit 0 but did not say: $2)" "$out"; return; fi
  ok "$1"
}

must_fail() { # $1 = label, $2 = required reason pattern
  if [ "$rc" -eq 0 ]; then bad "$1 (expected non-zero, got 0)" "$out"; return; fi
  if ! grep -q -- "$2" <<<"$out"; then bad "$1 (failed, but not for the stated reason: $2)" "$out"; return; fi
  ok "$1"
}

echo "== check-kit-published-coherence fixture =="

# A canonical lock carrying the CURRENT digests A/B/C.
LOCK="$WORK/repos.lock"
lock "$LOCK" "$A" ".claude/skills/check-board" "$B" "scripts/fsgg-coord" "$C" "dist/dotnet/.config/dotnet-tools.json"

# ---------------------------------------------------------------------------------------------
# 1. GREEN: every coordination-kit member the published kit ships is in canonical repos.lock.
# ---------------------------------------------------------------------------------------------
M="$WORK/m-ok.tsv"
{ printf 'skill\tskills/check-board/SKILL.md\tcheck-board/SKILL.md\t%s\n' "$A"
  printf 'client\tclient/fsgg-coord\tscripts/fsgg-coord\t%s\n' "$B"
  printf 'config\tconfig/dotnet-tools.json\t.config/dotnet-tools.json\t%s\n' "$C"; } > "$M"
run "$LOCK" "$M"
must_pass "a kit whose members are all canonical is COHERENT" "every one byte-identical to canonical"

# ---------------------------------------------------------------------------------------------
# 2. THE ACCEPTANCE CASE: the published kit ships a config digest canonical no longer has (#1291).
# ---------------------------------------------------------------------------------------------
M="$WORK/m-stale.tsv"
{ printf 'skill\tskills/check-board/SKILL.md\tcheck-board/SKILL.md\t%s\n' "$A"
  printf 'client\tclient/fsgg-coord\tscripts/fsgg-coord\t%s\n' "$B"
  printf 'config\tconfig/dotnet-tools.json\t.config/dotnet-tools.json\t%s\n' "$C_OLD"; } > "$M"
run "$LOCK" "$M"
must_fail "a kit carrying a non-canonical digest is STALE (RED)" "is STALE"
# The message must name the drifted member AND the remedy — a gate that reds without naming the fix
# is one the next worker routes around.
if grep -q '.config/dotnet-tools.json' <<<"$out" && grep -q 'release-kit' <<<"$out"; then
  ok "the RED names the drifted member and the remedy (bump <Version> + release-kit)"
else
  bad "the RED names the drifted member and the remedy" "$out"
fi

# ---------------------------------------------------------------------------------------------
# 3. build-config members are EXCLUDED — they carry no repos.lock row (verify-package.sh §1), so a
#    build-config digest absent from the lock must NOT red the gate.
# ---------------------------------------------------------------------------------------------
M="$WORK/m-bc.tsv"
{ printf 'skill\tskills/check-board/SKILL.md\tcheck-board/SKILL.md\t%s\n' "$A"
  printf 'client\tclient/fsgg-coord\tscripts/fsgg-coord\t%s\n' "$B"
  printf 'config\tconfig/dotnet-tools.json\t.config/dotnet-tools.json\t%s\n' "$C"
  printf 'build-config\tbuild-config/Directory.Build.props\tDirectory.Build.props\t%s\n' "$(printf 'e%.0s' {1..64})"; } > "$M"
run "$LOCK" "$M"
must_pass "a build-config member absent from the lock is IGNORED, not a drift" "every one byte-identical to canonical"

# ---------------------------------------------------------------------------------------------
# 4. FAIL CLOSED — the canonical lock is missing or empty.
# ---------------------------------------------------------------------------------------------
run "$WORK/no-such.lock" "$M"
must_fail "a missing lock is an ERROR" "cannot read the canonical lock"

: > "$WORK/empty.lock"
run "$WORK/empty.lock" "$M"
must_fail "an empty lock is an ERROR" "no digests"

# ---------------------------------------------------------------------------------------------
# 5. FAIL CLOSED — the published manifest is missing / malformed / carries a non-sha digest / names
#    zero coordination-kit members.
# ---------------------------------------------------------------------------------------------
run "$LOCK" "$WORK/no-such.tsv"
must_fail "a missing manifest is an ERROR" "cannot read fixture manifest"

printf 'skill\tonly\tthree-fields\n' > "$WORK/m-3col.tsv"
run "$LOCK" "$WORK/m-3col.tsv"
must_fail "a manifest row without 4 fields is an ERROR" "4-field"

printf 'config\tconfig/dotnet-tools.json\t.config/dotnet-tools.json\tNOTHEX\n' > "$WORK/m-nothex.tsv"
run "$LOCK" "$WORK/m-nothex.tsv"
must_fail "a non-sha256 digest is an ERROR" "non-sha256 digest"

printf 'build-config\tbuild-config/Directory.Build.props\tDirectory.Build.props\t%s\n' "$C" > "$WORK/m-nocoord.tsv"
run "$LOCK" "$WORK/m-nocoord.tsv"
must_fail "a manifest with zero coordination members is an ERROR" "zero coordination-kit members"

# ---------------------------------------------------------------------------------------------
# 6. THE FIXTURE HOOK IS LOCKED — --fixture-manifest is refused without the harness opt-in.
# ---------------------------------------------------------------------------------------------
set +e
out="$(env -u FSGG_KIT_COHERENCE_FIXTURE_OK python3 "$GATE" --lock "$LOCK" --fixture-manifest "$M" 2>&1)"; rc=$?
set -e
must_fail "--fixture-manifest is REFUSED without the harness opt-in" "Refusing to run"

echo
echo "kit-published-coherence fixture: $pass passed, $failcount failed"
[ "$failcount" -eq 0 ] || exit 1
