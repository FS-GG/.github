#!/usr/bin/env bash
# Fixture for scripts/skillmirror-redrive.py — .github#2521, epic #266.
#
# THE REFUSAL LEGS ARE THE POINT. This driver is the only thing in the repo permitted to WRITE the
# SkillMirror conformance table, and the oracle it drives is deliberately a checker and not a writer
# precisely because "a generator that rewrote its own expectations from the implementation would
# green any divergence the moment it was regenerated". Every guarantee that keeps automation from
# becoming that generator is a refusal, so a suite that only watched the happy path pass would be
# testing the one behaviour whose failure is harmless.
#
# OFFLINE, AND THE EXPECTED VALUES ARE COMPUTED WITHOUT THE DRIVER'S OWN LOGIC.
#   * Every expected digest comes from `sha256sum`, never from importing anything under test. A
#     fixture that asked the driver what the answer should be and then asked whether it agreed would
#     be measuring nothing.
#   * `dotnet` is STUBBED ON PATH and the REAL `tests/skill-union/skillmirror-oracle.sh` wrapper is
#     the thing invoked — so the legs exercise the real derivation path (its `--lib` validation, its
#     `#load` driver, its exit-code contract) rather than a convenience shim standing in for it. The
#     stub scripts the oracle's VERDICT, which is the only thing the driver is entitled to read.
#   * The stub answers per CALL, so the leg that matters most — a write whose re-run STILL disagrees
#     must be REVERTED — can be expressed at all. That check is what makes the driver's message
#     classifier a heuristic it is safe to be wrong about.
#   * The synthetic table names `Fixture-Org/Fixture.Lib` and `src/Fixture.Contracts/…`, never the
#     real subject: a driver that spelled the repository or the path itself instead of reading
#     `derivedFrom` would fail every leg here.
#
# THE SHAPE OF THE SYNTHETIC TABLE IS LOAD-BEARING, NOT DECORATION. `derivedOn` sits AFTER the nested
# `libraryFiles` object, exactly as the real table has it, because the surgical writer must search a
# block AROUND its nested children rather than through them — and `libraryFiles`' values are digests
# of exactly the shape it is matching on. The `_comment` prose carries decoy `commit`/date text for
# the same reason: a writer that reflowed or re-matched prose would be caught here rather than in a
# review of a 400-line diff.
set -uo pipefail

HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT="$(cd "$HERE/../.." && pwd)"
DRIVER="$ROOT/scripts/skillmirror-redrive.py"
PY="${PYTHON:-python3}"

WORK="$(mktemp -d "${TMPDIR:-/tmp}/skillmirror-redrive-fixture.XXXXXX")"
trap 'rm -rf "$WORK"' EXIT

export PYTHONDONTWRITEBYTECODE=1

pass=0
failcount=0
ok() { printf 'PASS  %s\n' "$1"; pass=$((pass + 1)); }
bad() {
  printf 'FAIL  %s\n' "$1"
  [ -n "${2:-}" ] && printf '%s\n' "$2" | sed 's/^/    | /'
  failcount=$((failcount + 1))
}

NEW_COMMIT="1111111111111111111111111111111111111111"
OLD_COMMIT="0123456789abcdef0123456789abcdef01234567"
NEW_DATE="2030-01-02"
OLD_DATE="2029-12-31"
STALE_DIGEST="00000000000000000000000000000000000000000000000000000000000000ff"

# ---------------------------------------------------------------------------------------------
# The `dotnet` stub. Serves a WORLD directory, one scripted answer per CALL:
#   $ORACLE_WORLD/call-<n>   first line = exit code, remaining lines = what the oracle printed
#   $ORACLE_WORLD/call-default   used for any call with no numbered script
# It asserts the invocation rather than answering anything asked of it: a driver that stopped going
# through the real wrapper, or that invoked something other than `dotnet fsi`, fails here.
# ---------------------------------------------------------------------------------------------
STUB="$WORK/stub"
mkdir -p "$STUB"
cat >"$STUB/dotnet" <<'STUB'
#!/usr/bin/env bash
set -uo pipefail
[ "${1:-}" = "fsi" ] || { echo "stub: expected \`dotnet fsi\`, got: $*" >&2; exit 9; }
[ -n "${ORACLE_WORLD:-}" ] || { echo "stub: ORACLE_WORLD is unset" >&2; exit 9; }
# The wrapper writes a driver that `#load`s the library and the oracle body; if that stopped
# happening the derivation being scripted here would not be the derivation the repo ships.
grep -q 'skillmirror-oracle.fsx' "$2" || { echo "stub: driver does not #load the oracle body" >&2; exit 9; }
n="$(cat "$ORACLE_WORLD/count" 2>/dev/null || echo 0)"
n=$((n + 1))
echo "$n" >"$ORACLE_WORLD/count"
script="$ORACLE_WORLD/call-$n"
[ -f "$script" ] || script="$ORACLE_WORLD/call-default"
[ -f "$script" ] || { echo "stub: no scripted answer for call $n" >&2; exit 9; }
code="$(head -1 "$script")"
tail -n +2 "$script" >&2
exit "$code"
STUB
chmod +x "$STUB/dotnet"
export PATH="$STUB:$PATH"

# ---------------------------------------------------------------------------------------------
# A synthetic world: a repo root carrying the REAL oracle plus a fake table, and a library checkout.
# ---------------------------------------------------------------------------------------------
LIBREL="src/Fixture.Contracts"

world() {
  # world <name> — prints the root it built.
  local w="$WORK/$1"
  rm -rf "$w"
  mkdir -p "$w/tests/skill-union" "$w/lib/$LIBREL"
  cp "$ROOT/tests/skill-union/skillmirror-oracle.sh" "$w/tests/skill-union/"
  cp "$ROOT/tests/skill-union/skillmirror-oracle.fsx" "$w/tests/skill-union/"
  printf 'module Fixture.Schemas\nlet version = 2\n' >"$w/lib/$LIBREL/Schemas.fs"
  printf 'module Fixture.SkillMirror\nlet verify () = ()\n' >"$w/lib/$LIBREL/SkillMirror.fs"
  table "$1" >"$w/tests/skill-union/skillmirror.fixtures.json"
  mkdir -p "$w/oracle"
  printf '%s' "$w"
}

# The synthetic table. Two arguments' worth of shape matters: `derivedOn` AFTER `libraryFiles`, and
# prose that mentions a commit and a date so a careless writer has something wrong to hit.
table() {
  cat <<JSON
{
  "_comment": [
    "A SYNTHETIC conformance table. Not the real subject: see tests/skillmirror-redrive/run.sh.",
    "Decoy prose — the last re-derive ran at commit $OLD_COMMIT on $OLD_DATE and moved nothing.",
    "A writer that matched this line instead of the field below would be caught by the leg count."
  ],
  "derivedFrom": {
    "library": "Fixture.SkillMirror.verify",
    "repo": "Fixture-Org/Fixture.Lib",
    "path": "$LIBREL/SkillMirror.fs",
    "commit": "$OLD_COMMIT",
    "skillMirrorFsSha256": "$STALE_DIGEST",
    "libraryFiles": {
      "$LIBREL/Schemas.fs": "$STALE_DIGEST",
      "$LIBREL/SkillMirror.fs": "$STALE_DIGEST"
    },
    "derivedOn": "$OLD_DATE",
    "howToRedrive": "bash tests/skill-union/skillmirror-oracle.sh --lib <checkout>/$LIBREL",
    "residualGap": [
      "Prose the driver must never touch, mentioning commit $OLD_COMMIT once more."
    ]
  },
  "digestVectors": {
    "measuredAgainst": {
      "_comment": [
        "A separate provenance record for a separate measured column."
      ],
      "repo": "Fixture-Org/Fixture.Lib",
      "path": "$LIBREL/SkillMirror.fs",
      "commit": "$OLD_COMMIT",
      "skillMirrorFsSha256": "$STALE_DIGEST",
      "measuredOn": "$OLD_DATE",
      "howToRedrive": "bash tests/skill-union/skillmirror-oracle.sh --lib <checkout>/$LIBREL"
    },
    "vectors": []
  },
  "fixtures": []
}
JSON
}

# calls <world-root> — how many times the driver consulted the oracle. A refusal that happened
# BEFORE any write consults it exactly once; a write-then-revert consults it twice. The two are
# materially different verdicts (one never touched the file) and this is what tells them apart.
calls() { cat "$1/oracle/count" 2>/dev/null || echo 0; }

# script <world-root> <call-n|default> <exit> [lines...]
script() {
  local w="$1" which="$2" code="$3"; shift 3
  { echo "$code"; for line in "$@"; do echo "$line"; done; } >"$w/oracle/call-$which"
}

drive() {
  # drive <world-root> [extra args...] — runs the driver, exports the stub world, echoes nothing.
  local w="$1"; shift
  ORACLE_WORLD="$w/oracle" "$PY" "$DRIVER" --root "$w" --checkout "$w/lib" \
    --commit "$NEW_COMMIT" --date "$NEW_DATE" "$@" >"$w/out" 2>&1
}

# field <table> <key>... — one value, walked by EXPLICIT key arguments. Never by splitting a dotted
# path: `libraryFiles`' keys ARE paths and contain dots, so a dotted spelling would silently address
# the wrong node and this fixture would grade something other than what it names.
field() {
  local f="$1"; shift
  "$PY" -c 'import json,sys
d = json.load(open(sys.argv[1]))
for k in sys.argv[2:]:
    d = d[k]
print(d)' "$f" "$@"
}

PROV_LINE='DISAGREES  derivedFrom.libraryFiles[Schemas.fs] is stale.'
PROV_LINE_2='DISAGREES  derivedFrom.skillMirrorFsSha256 is stale — this table was derived from a DIFFERENT library revision.'
PROV_LINE_3='DISAGREES  digestVectors.measuredAgainst.skillMirrorFsSha256 (abc) is not the library measured here (def) — re-run the oracle and reconcile the digest column.'

# =============================================================================================
# LEG 1 — the oracle agrees and the provenance is already current: nothing is written at all.
# =============================================================================================
w="$(world agree)"
# Pre-pin the table to what this library actually is, so there is genuinely nothing to move.
SCHEMAS_SHA="$(sha256sum "$w/lib/$LIBREL/Schemas.fs" | cut -d' ' -f1)"
MIRROR_SHA="$(sha256sum "$w/lib/$LIBREL/SkillMirror.fs" | cut -d' ' -f1)"
"$PY" - "$w/tests/skill-union/skillmirror.fixtures.json" "$NEW_COMMIT" "$NEW_DATE" "$SCHEMAS_SHA" "$MIRROR_SHA" "$LIBREL" <<'PRE'
import re, sys
path, commit, date, schemas, mirror, librel = sys.argv[1:7]
text = open(path).read()
text = text.replace('"commit": "0123456789abcdef0123456789abcdef01234567"', f'"commit": "{commit}"')
text = re.sub(r'("(?:skillMirrorFsSha256|' + re.escape(librel) + r'/SkillMirror\.fs)": ")[0-9a-f]{64}"', r'\g<1>' + mirror + '"', text)
text = re.sub(r'("' + re.escape(librel) + r'/Schemas\.fs": ")[0-9a-f]{64}"', r'\g<1>' + schemas + '"', text)
text = text.replace('"derivedOn": "2029-12-31"', f'"derivedOn": "{date}"').replace('"measuredOn": "2029-12-31"', f'"measuredOn": "{date}"')
open(path, "w").write(text)
PRE
cp "$w/tests/skill-union/skillmirror.fixtures.json" "$w/before.json"
script "$w" default 0 "verify vectors: 0, digest vectors: 0, disagreements: 0"
drive "$w" --write
rc=$?
if [ "$rc" -ne 0 ]; then
  bad "leg 1: an agreeing oracle with current provenance must exit 0" "$(cat "$w/out")"
elif ! cmp -s "$w/before.json" "$w/tests/skill-union/skillmirror.fixtures.json"; then
  bad "leg 1: nothing moved, yet the table was rewritten" "$(diff "$w/before.json" "$w/tests/skill-union/skillmirror.fixtures.json")"
else
  ok "leg 1: agreeing oracle + current provenance -> exit 0, byte-identical table"
fi

# =============================================================================================
# LEG 2 — provenance-only disagreement, DRY RUN: the verdict is reached and nothing is written.
# =============================================================================================
w="$(world dryrun)"
cp "$w/tests/skill-union/skillmirror.fixtures.json" "$w/before.json"
script "$w" default 1 "$PROV_LINE"
drive "$w"
rc=$?
if [ "$rc" -ne 0 ]; then
  bad "leg 2: a dry run over a provenance-only disagreement must exit 0" "$(cat "$w/out")"
elif ! cmp -s "$w/before.json" "$w/tests/skill-union/skillmirror.fixtures.json"; then
  bad "leg 2: a dry run WROTE the table"
elif ! grep -q "STALE PROVENANCE ONLY" "$w/out"; then
  bad "leg 2: the dry run did not report the verdict it reached" "$(cat "$w/out")"
else
  ok "leg 2: provenance-only, no --write -> exit 0, verdict reported, nothing written"
fi

# =============================================================================================
# LEG 3 — provenance-only, --write: exactly the eight provenance fields move, and nothing else.
# The expected digests are `sha256sum`'s, and the changed-line COUNT is asserted, so a writer that
# also touched prose, `howToRedrive`, or `repo`/`path` fails here rather than in review.
# =============================================================================================
w="$(world write)"
cp "$w/tests/skill-union/skillmirror.fixtures.json" "$w/before.json"
SCHEMAS_SHA="$(sha256sum "$w/lib/$LIBREL/Schemas.fs" | cut -d' ' -f1)"
MIRROR_SHA="$(sha256sum "$w/lib/$LIBREL/SkillMirror.fs" | cut -d' ' -f1)"
script "$w" 1 1 "$PROV_LINE" "$PROV_LINE_2" "$PROV_LINE_3"
script "$w" 2 0 "verify vectors: 0, digest vectors: 0, disagreements: 0"
drive "$w" --write
rc=$?
T="$w/tests/skill-union/skillmirror.fixtures.json"
if [ "$rc" -ne 0 ]; then
  bad "leg 3: a provenance-only re-pin must exit 0" "$(cat "$w/out")"
else
  problems=""
  # check <expected> <key>... — the expected value first, so the walked key path stays variadic.
  check() {
    local want="$1"; shift
    local got; got="$(field "$T" "$@")"
    [ "$got" = "$want" ] || problems="$problems\n  $* is '$got', expected '$want'"
  }
  check "$NEW_COMMIT" derivedFrom commit
  check "$MIRROR_SHA" derivedFrom skillMirrorFsSha256
  check "$SCHEMAS_SHA" derivedFrom libraryFiles "$LIBREL/Schemas.fs"
  check "$MIRROR_SHA" derivedFrom libraryFiles "$LIBREL/SkillMirror.fs"
  check "$NEW_DATE" derivedFrom derivedOn
  check "$NEW_COMMIT" digestVectors measuredAgainst commit
  check "$MIRROR_SHA" digestVectors measuredAgainst skillMirrorFsSha256
  check "$NEW_DATE" digestVectors measuredAgainst measuredOn
  check "Fixture-Org/Fixture.Lib" digestVectors measuredAgainst repo
  check "bash tests/skill-union/skillmirror-oracle.sh --lib <checkout>/$LIBREL" derivedFrom howToRedrive
  moved="$(diff "$w/before.json" "$T" | grep -c '^[<>]' || true)"
  # 8 fields move, so 8 removed + 8 added lines. `skillMirrorFsSha256` appears twice (once per
  # provenance block) and `libraryFiles` carries two entries.
  [ "$moved" = "16" ] || problems="$problems\n  $moved changed diff lines, expected 16:\n$(diff "$w/before.json" "$T")"
  if [ -n "$problems" ]; then
    bad "leg 3: the re-pin did not write exactly the provenance" "$(printf '%b' "$problems")"
  else
    ok "leg 3: provenance-only + --write -> exactly the 8 provenance fields, measured by sha256sum"
  fi
fi

# =============================================================================================
# LEG 4 — A MOVED VECTOR IS NEVER RE-PINNED AROUND. This is the whole safety argument: the library's
# behaviour changed, and no bot may resolve that by updating the record of what it was measured at.
# =============================================================================================
w="$(world moved)"
cp "$w/tests/skill-union/skillmirror.fixtures.json" "$w/before.json"
# The second call is scripted to AGREE. So nothing but the classifier's refusal can stop this write:
# a driver that wrote first and reverted afterwards would write, re-measure clean, and pass — which is
# how an earlier version of this leg survived deleting the refusal outright.
script "$w" 1 1 "$PROV_LINE" "DISAGREES  whole-and-coherent-and-matching" "  table:   x" "  library: y"
script "$w" 2 0 "verify vectors: 0, digest vectors: 0, disagreements: 0"
drive "$w" --write
rc=$?
if [ "$rc" -ne 1 ]; then
  bad "leg 4: a moved VECTOR must be a finding (exit 1), got $rc" "$(cat "$w/out")"
elif ! cmp -s "$w/before.json" "$w/tests/skill-union/skillmirror.fixtures.json"; then
  bad "leg 4: a moved vector was RE-PINNED AROUND — the table was written" \
      "$(diff "$w/before.json" "$w/tests/skill-union/skillmirror.fixtures.json")"
elif ! grep -q 'whole-and-coherent-and-matching' "$w/out"; then
  bad "leg 4: the finding does not name the vector that moved" "$(cat "$w/out")"
elif [ "$(calls "$w")" != "1" ]; then
  bad "leg 4: the oracle was consulted $(calls "$w") times — the refusal must precede any write"
else
  ok "leg 4: a moved vector -> exit 1 before any write, table untouched, the vector named"
fi

# =============================================================================================
# LEG 5 — an UNRECOGNISED disagreement is treated as a moved vector, not as provenance. This is the
# fail-towards-refusal property: a future change to the oracle's wording must make the driver refuse,
# never make it write.
# =============================================================================================
w="$(world unknown)"
cp "$w/tests/skill-union/skillmirror.fixtures.json" "$w/before.json"
script "$w" 1 1 "DISAGREES  some future check nobody has written yet"
script "$w" 2 0 "verify vectors: 0, digest vectors: 0, disagreements: 0"
drive "$w" --write
rc=$?
if [ "$rc" -ne 1 ]; then
  bad "leg 5: an unclassifiable disagreement must be a finding (exit 1), got $rc" "$(cat "$w/out")"
elif ! cmp -s "$w/before.json" "$w/tests/skill-union/skillmirror.fixtures.json"; then
  bad "leg 5: an unclassifiable disagreement was written past"
elif [ "$(calls "$w")" != "1" ]; then
  bad "leg 5: the oracle was consulted $(calls "$w") times — the refusal must precede any write"
else
  ok "leg 5: an unrecognised disagreement -> exit 1 before any write, table untouched"
fi

# =============================================================================================
# LEG 6 — a MISSING `measuredAgainst` block is structural, not stale, and is deliberately outside the
# provenance allow-list: a bot inventing one would manufacture a record of a measurement nobody made.
# =============================================================================================
w="$(world structural)"
cp "$w/tests/skill-union/skillmirror.fixtures.json" "$w/before.json"
script "$w" 1 1 "DISAGREES  digestVectors has no \`measuredAgainst\` block — its \`digest\` column would be a measurement with no record of what it was measured against."
script "$w" 2 0 "verify vectors: 0, digest vectors: 0, disagreements: 0"
drive "$w" --write
rc=$?
if [ "$rc" -ne 1 ]; then
  bad "leg 6: a missing measuredAgainst block must be a finding (exit 1), got $rc" "$(cat "$w/out")"
elif ! cmp -s "$w/before.json" "$w/tests/skill-union/skillmirror.fixtures.json"; then
  bad "leg 6: a structural disagreement was written past"
elif [ "$(calls "$w")" != "1" ]; then
  bad "leg 6: the oracle was consulted $(calls "$w") times — the refusal must precede any write"
else
  ok "leg 6: a missing provenance BLOCK -> exit 1 before any write, table untouched"
fi

# =============================================================================================
# LEG 7 — THE INDEPENDENT CHECK. If the write does not make the derivation agree, it is REVERTED and
# reported. This is what makes the message classifier a heuristic it is safe to be wrong about: it is
# never the last word on whether a write stands.
# =============================================================================================
w="$(world revert)"
cp "$w/tests/skill-union/skillmirror.fixtures.json" "$w/before.json"
script "$w" default 1 "$PROV_LINE"
drive "$w" --write
rc=$?
if [ "$rc" -ne 1 ]; then
  bad "leg 7: a write whose re-run still disagrees must be a finding (exit 1), got $rc" "$(cat "$w/out")"
elif ! cmp -s "$w/before.json" "$w/tests/skill-union/skillmirror.fixtures.json"; then
  bad "leg 7: the write was NOT reverted" \
      "$(diff "$w/before.json" "$w/tests/skill-union/skillmirror.fixtures.json")"
elif ! grep -q 'REVERTED' "$w/out"; then
  bad "leg 7: the revert was not reported" "$(cat "$w/out")"
else
  ok "leg 7: a re-run that still disagrees -> exit 1, write reverted, table byte-identical"
fi

# =============================================================================================
# LEG 8 — "I COULD NOT MEASURE" IS NOT "I MEASURED, AND IT AGREES" (#266). Two ways to be unable to
# measure, graded separately, because one used to mask the other.
# =============================================================================================
# 8a isolates the ORACLE's exit-2 contract: the library is fully present and the digests are
# measurable, so the ONLY thing wrong is that the derivation could not be run. A driver that read a
# non-{0,1} exit as a verdict would reach the classifier with no `DISAGREES` line and report
# something — a finding or a green — about a measurement that never happened.
w="$(world unrunnable-oracle)"
cp "$w/tests/skill-union/skillmirror.fixtures.json" "$w/before.json"
script "$w" default 2 "skillmirror-oracle: could not load the library"
drive "$w" --write
rc=$?
if [ "$rc" -ne 3 ]; then
  bad "leg 8a: an oracle exiting 2 must be a no-verdict (exit 3), got $rc" "$(cat "$w/out")"
elif ! cmp -s "$w/before.json" "$w/tests/skill-union/skillmirror.fixtures.json"; then
  bad "leg 8a: the table was written from a measurement that never happened"
elif ! grep -q 'neither agreement (0) nor disagreement (1)' "$w/out"; then
  bad "leg 8a: exit 3 was reached by some other route than the unrunnable-oracle refusal" "$(cat "$w/out")"
else
  ok "leg 8a: an oracle that could not run -> exit 3 (no verdict), table untouched"
fi

# 8b is the other half: `libraryFiles` records a digest for a file the checkout does not contain, so
# there is nothing to measure even though the oracle ran cleanly. Also a no-verdict, reached by a
# different route — and it must be a different route, or this leg would only restate 8a.
w="$(world unrunnable-library)"
# `libraryFiles` names a third file that is NOT in the checkout. The oracle wrapper does not police
# that list — it only requires the two files it `#load`s — so this reaches the driver's own digest
# measurement, which is the path being graded. (Deleting one of the `#load`ed files instead would be
# caught by the wrapper's precondition and would land on leg 8a's verdict, which is how an earlier
# draft of this leg tested nothing 8a did not already cover.)
"$PY" - "$w/tests/skill-union/skillmirror.fixtures.json" "$LIBREL" <<'MUT'
import sys
path, librel = sys.argv[1:3]
text = open(path).read()
text = text.replace(f'"{librel}/Schemas.fs": ', f'"{librel}/Absent.fs": "00000000000000000000000000000000000000000000000000000000000000ff",\n      "{librel}/Schemas.fs": ', 1)
open(path, "w").write(text)
MUT
cp "$w/tests/skill-union/skillmirror.fixtures.json" "$w/before.json"
script "$w" default 0 "verify vectors: 0, digest vectors: 0, disagreements: 0"
drive "$w" --write
rc=$?
if [ "$rc" -ne 3 ]; then
  bad "leg 8b: a libraryFiles entry with no file must be a no-verdict (exit 3), got $rc" "$(cat "$w/out")"
elif ! cmp -s "$w/before.json" "$w/tests/skill-union/skillmirror.fixtures.json"; then
  bad "leg 8b: the table was re-pinned to a digest of a file that is not there"
elif ! grep -q 'to measure its digest' "$w/out"; then
  bad "leg 8b: exit 3 was reached by some other route than the unreadable-library refusal" "$(cat "$w/out")"
else
  ok "leg 8b: a libraryFiles entry with no file -> exit 3 (no verdict), table untouched"
fi

# =============================================================================================
# LEG 11 — THE SURGICAL WRITER REFUSES AN AMBIGUOUS OR ABSENT TARGET. A textual edit of a 400-line
# hand-written file is only as trustworthy as what it does when it cannot locate its subject exactly
# once: guessing which of two lines records the provenance, or inventing a field the table never
# carried, are both writes nobody asked for.
# =============================================================================================
w="$(world ambiguous)"
"$PY" - "$w/tests/skill-union/skillmirror.fixtures.json" <<'MUT'
import sys
path = sys.argv[1]
lines = open(path).read().split("\n")
out = []
for line in lines:
    out.append(line)
    if line.strip().startswith('"commit":') and len(out) < 30:
        out.append(line)          # a second, indistinguishable provenance line in the same block
open(path, "w").write("\n".join(out))
MUT
cp "$w/tests/skill-union/skillmirror.fixtures.json" "$w/before.json"
script "$w" 1 1 "$PROV_LINE"
script "$w" 2 0 "verify vectors: 0, digest vectors: 0, disagreements: 0"
drive "$w" --write
rc=$?
if [ "$rc" -ne 3 ]; then
  bad "leg 11a: two candidate provenance lines must be a no-verdict (exit 3), got $rc" "$(cat "$w/out")"
elif ! cmp -s "$w/before.json" "$w/tests/skill-union/skillmirror.fixtures.json"; then
  bad "leg 11a: the writer picked one of two ambiguous lines instead of refusing"
elif ! grep -q 'expected exactly one .* found 2' "$w/out"; then
  bad "leg 11a: exit 3 was reached by some other route than the ambiguity refusal" "$(cat "$w/out")"
else
  ok "leg 11a: an ambiguous provenance line -> exit 3 (no verdict), table untouched"
fi

w="$(world absent)"
"$PY" - "$w/tests/skill-union/skillmirror.fixtures.json" <<'MUT'
import sys
path = sys.argv[1]
kept = [l for l in open(path).read().split("\n") if not l.strip().startswith('"derivedOn":')]
open(path, "w").write("\n".join(kept))
MUT
cp "$w/tests/skill-union/skillmirror.fixtures.json" "$w/before.json"
script "$w" 1 1 "$PROV_LINE"
script "$w" 2 0 "verify vectors: 0, digest vectors: 0, disagreements: 0"
drive "$w" --write
rc=$?
if [ "$rc" -ne 3 ]; then
  bad "leg 11b: a provenance field the table does not carry must be a no-verdict (exit 3), got $rc" "$(cat "$w/out")"
elif ! cmp -s "$w/before.json" "$w/tests/skill-union/skillmirror.fixtures.json"; then
  bad "leg 11b: the writer INVENTED a field the table never carried"
elif ! grep -q 'expected exactly one .* found 0' "$w/out"; then
  bad "leg 11b: exit 3 was reached by some other route than the absent-target refusal" "$(cat "$w/out")"
else
  ok "leg 11b: an absent provenance field -> exit 3 (no verdict), table untouched"
fi

# =============================================================================================
# LEG 12 — A DEGENERATE PROVENANCE BLOCK IS A NO-VERDICT, NEVER A PARTIAL RE-PIN. With no
# `libraryFiles` map there is no coverage statement at all, and re-pinning `commit` alone would leave
# the table asserting a revision whose files nobody measured — the loudest possible pass over the
# least work, which is #266's signature.
# =============================================================================================
w="$(world degenerate)"
"$PY" - "$w/tests/skill-union/skillmirror.fixtures.json" <<'MUT'
import sys
path = sys.argv[1]
lines = open(path).read().split("\n")
out, dropping = [], False
for line in lines:
    if line.strip().startswith('"libraryFiles":'):
        dropping = True
        continue
    if dropping:
        if line.strip() in ("},", "}"):
            dropping = False
        continue
    out.append(line)
open(path, "w").write("\n".join(out))
MUT
cp "$w/tests/skill-union/skillmirror.fixtures.json" "$w/before.json"
script "$w" 1 1 "$PROV_LINE"
script "$w" 2 0 "verify vectors: 0, digest vectors: 0, disagreements: 0"
drive "$w" --write
rc=$?
if [ "$rc" -ne 3 ]; then
  bad "leg 12: a table with no libraryFiles map must be a no-verdict (exit 3), got $rc" "$(cat "$w/out")"
elif ! cmp -s "$w/before.json" "$w/tests/skill-union/skillmirror.fixtures.json"; then
  bad "leg 12: a degenerate table was partially re-pinned"
elif ! grep -q 'libraryFiles` is not a non-empty object' "$w/out"; then
  bad "leg 12: exit 3 was reached by some other route than the coverage refusal" "$(cat "$w/out")"
else
  ok "leg 12: no libraryFiles map -> exit 3 (no verdict), table untouched"
fi

# =============================================================================================
# LEG 9 — `--subject` VALIDATES BEFORE IT PRINTS. Its output is interpolated into `actions/checkout`'s
# `repository:`/`ref:` inputs, so a table naming something that is not an `owner/name` slug or not a
# resolvable sha must be a no-verdict rather than a checkout target.
# =============================================================================================
w="$(world subject)"
ORACLE_WORLD="$w/oracle" "$PY" "$DRIVER" --root "$w" --subject >"$w/out" 2>&1
rc=$?
if [ "$rc" -ne 0 ] || ! grep -qx "repo=Fixture-Org/Fixture.Lib" "$w/out" \
   || ! grep -qx "commit=$OLD_COMMIT" "$w/out" || ! grep -qx "libdir=$LIBREL" "$w/out"; then
  bad "leg 9a: --subject must print the table's declared subject" "$(cat "$w/out")"
else
  ok "leg 9a: --subject prints repo/commit/libdir read from the table"
fi
for bogus in '"repo": "not a slug"' '"commit": "HEAD"'; do
  w="$(world subject-bad)"
  key="${bogus%%\":*}\""
  "$PY" - "$w/tests/skill-union/skillmirror.fixtures.json" "$key" "$bogus" <<'MUT'
import re, sys
path, key, replacement = sys.argv[1:4]
text = open(path).read()
text = re.sub(re.escape(key) + r': "[^"]*"', replacement, text, count=1)
open(path, "w").write(text)
MUT
  ORACLE_WORLD="$w/oracle" "$PY" "$DRIVER" --root "$w" --subject >"$w/out" 2>&1
  rc=$?
  if [ "$rc" -ne 3 ]; then
    bad "leg 9b: --subject over $bogus must be a no-verdict (exit 3), got $rc" "$(cat "$w/out")"
  elif grep -q '^repo=' "$w/out"; then
    bad "leg 9b: --subject printed a checkout target it had refused" "$(cat "$w/out")"
  else
    ok "leg 9b: --subject refuses $bogus without printing a checkout target"
  fi
done

# =============================================================================================
# LEG 13 — `--assert-derived` IS A DIFFERENT QUESTION FROM THE RE-PIN, AND MUST NOT SOFTEN INTO IT.
# The scheduled repair asks "what does the library return NOW" and forgives stale provenance by
# rewriting it. The PR-time gate asks "is this table what its own pin CLAIMS", so a stale provenance
# field is a FINDING there — a table asserting a derivation that did not happen is the whole defect.
# It must also refuse to grade a checkout that is not the revision the table names, or its agreement
# would be about something nobody asked.
# =============================================================================================
w="$(world assert-agree)"
script "$w" default 0 "verify vectors: 0, digest vectors: 0, disagreements: 0"
cp "$w/tests/skill-union/skillmirror.fixtures.json" "$w/before.json"
ORACLE_WORLD="$w/oracle" "$PY" "$DRIVER" --root "$w" --checkout "$w/lib" --commit "$OLD_COMMIT" \
  --assert-derived >"$w/out" 2>&1
rc=$?
if [ "$rc" -ne 0 ]; then
  bad "leg 13a: an agreeing oracle at the recorded revision must exit 0, got $rc" "$(cat "$w/out")"
elif ! cmp -s "$w/before.json" "$w/tests/skill-union/skillmirror.fixtures.json"; then
  bad "leg 13a: --assert-derived WROTE the table"
else
  ok "leg 13a: --assert-derived over an agreeing oracle -> exit 0, nothing written"
fi

w="$(world assert-stale)"
script "$w" default 1 "$PROV_LINE"
cp "$w/tests/skill-union/skillmirror.fixtures.json" "$w/before.json"
ORACLE_WORLD="$w/oracle" "$PY" "$DRIVER" --root "$w" --checkout "$w/lib" --commit "$OLD_COMMIT" \
  --assert-derived >"$w/out" 2>&1
rc=$?
if [ "$rc" -ne 1 ]; then
  bad "leg 13b: a PROVENANCE-only disagreement must still be a finding here (exit 1), got $rc" "$(cat "$w/out")"
elif ! cmp -s "$w/before.json" "$w/tests/skill-union/skillmirror.fixtures.json"; then
  bad "leg 13b: --assert-derived re-pinned instead of reporting"
elif ! grep -q 'is NOT derived from' "$w/out"; then
  bad "leg 13b: the finding does not say the table is not derived from the revision it names" "$(cat "$w/out")"
else
  ok "leg 13b: --assert-derived over stale provenance -> exit 1, nothing written"
fi

w="$(world assert-wrong-rev)"
script "$w" default 0 "verify vectors: 0, digest vectors: 0, disagreements: 0"
ORACLE_WORLD="$w/oracle" "$PY" "$DRIVER" --root "$w" --checkout "$w/lib" --commit "$NEW_COMMIT" \
  --assert-derived >"$w/out" 2>&1
rc=$?
if [ "$rc" -ne 1 ]; then
  bad "leg 13c: grading a checkout the table does not name must be a finding (exit 1), got $rc" "$(cat "$w/out")"
elif ! grep -q 'is not the revision this table names' "$w/out"; then
  bad "leg 13c: the finding does not name the mismatch" "$(cat "$w/out")"
elif [ "$(calls "$w")" != "0" ]; then
  bad "leg 13c: the oracle was run against a revision the table does not claim"
else
  ok "leg 13c: --assert-derived over a foreign revision -> exit 1 before the oracle is run"
fi

w="$(world assert-write)"
script "$w" default 0 "verify vectors: 0, digest vectors: 0, disagreements: 0"
ORACLE_WORLD="$w/oracle" "$PY" "$DRIVER" --root "$w" --checkout "$w/lib" --commit "$OLD_COMMIT" \
  --assert-derived --write >"$w/out" 2>&1
rc=$?
if [ "$rc" -ne 3 ]; then
  bad "leg 13d: --assert-derived --write must be refused as a no-verdict (exit 3), got $rc" "$(cat "$w/out")"
else
  ok "leg 13d: --assert-derived --write is refused rather than silently preferring one"
fi

# =============================================================================================
# LEG 10 — THIS SUITE IS WIRED, AND THE ASSERTION IS DERIVED RATHER THAN SPELLED. `scripts/test`
# builds its answer by PARSING the workflows' own triggers and steps; it has no list of its own. So
# asking it whether this suite is known — and whether it lands in the UNWIRED class it reports for
# `tests/` directories no workflow references — is a question about the workflow graph, not a
# substring search for this directory's name in a YAML file.
# =============================================================================================
if ! "$PY" "$ROOT/scripts/test" --list --all --root "$ROOT" >"$WORK/selector.out" 2>&1; then
  bad "leg 10: scripts/test could not enumerate the suites — that is a no-verdict, not a pass" \
      "$(cat "$WORK/selector.out")"
elif ! grep -q "bash tests/skillmirror-redrive/run.sh" "$WORK/selector.out"; then
  bad "leg 10: no workflow runs this suite — it would never execute in CI" "$(cat "$WORK/selector.out")"
elif grep -q "^  tests/skillmirror-redrive/$" "$WORK/selector.out"; then
  bad "leg 10: scripts/test reports this suite as UNWIRED" "$(cat "$WORK/selector.out")"
else
  ok "leg 10: a workflow runs this suite, derived from the workflow graph by scripts/test"
fi

printf '\n%s\n' "----------------------------------------------------------------------"
printf 'skillmirror-redrive fixture: %d passed, %d failed\n' "$pass" "$failcount"
[ "$failcount" -eq 0 ] || exit 1
