#!/usr/bin/env bash
# Fixture for scripts/fsgg-skill-registry-check — the cross-repo guard on `registry = manifest =
# bytes` (.github#247, ADR-0017). Proves the tool catches the three drift shapes that actually bit
# us, off a temp registry + temp producer checkouts: a stale `sha256` (fs-gg-audio, ADR-0024 step 4),
# a `source:` that no longer exists (a renamed/relocated skill), and a frozen mirror that has
# diverged from the canonical body (fs-gg-game-core — the mirror was RIGHT and the source stale).
# Also proves --write reconciles only the stale digest, leaves the hand-aligned YAML otherwise
# byte-identical, and refuses to claim success while a non-digest finding remains.
#
# Cases 9-13 cover the CONVERSE direction (.github#289, epic #266): a skill a producer manifest
# declares but the registry never lists was not a finding, it was NOTHING — four shipped product
# skills were invisible to this gate. They prove `declared-completeness` reports the absent row and
# `--write` appends it; that a producer whose manifest VANISHED fails closed rather than silently
# dropping every skill it declares; that an entry with no `supplied-by` is reported and NOT appended
# (no invented `source:`); that a producer the registry names nowhere is still read; and that a
# non-producer checkout (the frozen mirror) is not mistaken for one.
# Pure-stdlib + PyYAML; no network. Mirrors tests/surface-impact/run.sh in shape.

set -euo pipefail

HERE="$(cd "$(dirname "$0")" && pwd)"
TOOL="$HERE/../../scripts/fsgg-skill-registry-check"

WORK="$(mktemp -d "${TMPDIR:-/tmp}/skill-registry-fixture.XXXXXX")"
trap 'rm -rf "$WORK"' EXIT

ROOT="$WORK/repos"
mkdir -p "$ROOT/Producer.One/skills/good" \
         "$ROOT/Producer.One/skills/stale" \
         "$ROOT/Producer.Two/skills/owned" \
         "$ROOT/Mirror.Repo/template/product-skills/owned"

printf 'good body\n'   > "$ROOT/Producer.One/skills/good/SKILL.md"
printf 'stale body\n'  > "$ROOT/Producer.One/skills/stale/SKILL.md"
printf 'owned body\n'  > "$ROOT/Producer.Two/skills/owned/SKILL.md"
# The frozen mirror starts byte-identical to its canonical body.
printf 'owned body\n'  > "$ROOT/Mirror.Repo/template/product-skills/owned/SKILL.md"

sha() { python3 -c "import hashlib,sys;print(hashlib.sha256(open(sys.argv[1],'rb').read()).hexdigest())" "$1"; }

GOOD="$(sha "$ROOT/Producer.One/skills/good/SKILL.md")"
OWNED="$(sha "$ROOT/Producer.Two/skills/owned/SKILL.md")"
ACTUAL_STALE="$(sha "$ROOT/Producer.One/skills/stale/SKILL.md")"
WRONG="deadbeef00000000000000000000000000000000000000000000000000000000"

REG="$WORK/skills.yml"
# `owned` carries `mirrored: true` (.github#658): Mirror.Repo ships a SECOND copy of it, and a body
# that exists twice is one somebody must classify. The verdict is the ADR-0022 §6 OBLIGATION, declared
# by the owner and reconciled onto the row — not an observation that a same-named file exists.
write_registry() {
  cat > "$REG" <<YAML
schemaVersion: 1
updated: "2026-07-08"
skills:
  - { id: good,    scope: process, owner: producer-one, source: Producer.One/skills/good/SKILL.md,    sha256: $GOOD,  materializes-when: always }
  - { id: stale,   scope: process, owner: producer-one, source: Producer.One/skills/stale/SKILL.md,   sha256: $WRONG, materializes-when: always }
  - { id: owned,   scope: product, owner: fs-gg-game,   source: Producer.Two/skills/owned/SKILL.md,   sha256: $OWNED, mirrored: true, materializes-when: "profile in [game]" }
YAML
}
write_registry

# The producer manifests the registry claims to have been reconciled FROM. Both publish locations
# are exercised: Producer.One emits to `.agents/skills/` (SDD's shape, no `supplied-by`), Producer.Two
# publishes from `template/skill-manifest/` (Rendering + Game's shape, with `supplied-by`).
# `write_manifests <extra-one-json> <extra-two-json>` splices extra entries in for cases 9-13.
write_manifests() {
  mkdir -p "$ROOT/Producer.One/.agents/skills" "$ROOT/Producer.Two/template/skill-manifest"
  cat > "$ROOT/Producer.One/.agents/skills/skill-manifest.json" <<JSON
{ "schemaVersion": 1, "skills": [
  { "id": "good",  "scope": "process", "sha256": "$GOOD" },
  { "id": "stale", "scope": "process", "sha256": "$ACTUAL_STALE" }${1:+,}
  ${1:-}
] }
JSON
  cat > "$ROOT/Producer.Two/template/skill-manifest/skill-manifest.json" <<JSON
{ "schemaVersion": 1, "skills": [
  { "id": "owned", "scope": "product", "sha256": "$OWNED", "mirrored": true, "supplied-by": "skills/owned/", "materializes-when": "profile in [game]" }${2:+,}
  ${2:-}
] }
JSON
}
write_manifests

# Producer.Two's manifest with a chosen mirror verdict for `owned` — `true`, `false`, a malformed
# value, or `omit` for a manifest that states none. REPLACES the manifest, because `write_manifests`'s
# $2 APPENDS: passing an `owned` entry there leaves TWO of them, and the tool would then be answering a
# duplicate-id contradiction rather than the question under test. (That is not hypothetical — the first
# draft of cases 44-51 spliced, and the collapse it accidentally exercised was a real bug in the tool.
# It is now case 52, on purpose.)
write_two_owned_mirror() {
  local verdict=""
  [ "$1" = "omit" ] || verdict="\"mirrored\": $1, "
  cat > "$ROOT/Producer.Two/template/skill-manifest/skill-manifest.json" <<JSON
{ "schemaVersion": 1, "skills": [
  { "id": "owned", "scope": "product", "sha256": "${2:-$OWNED}", ${verdict}"supplied-by": "skills/owned/", "materializes-when": "profile in [game]" }
] }
JSON
}

# The tool derives the frozen-mirror path from FS.GG.Rendering; point it at our stand-in.
run() { python3 - "$@" <<'PY'
import runpy, sys, os
tool = os.environ["TOOL"]
sys.argv = [tool] + sys.argv[1:]
import importlib.util
spec = importlib.util.spec_from_loader("t", loader=None)
src = open(tool).read().replace('FROZEN_MIRROR_REPO = "FS.GG.Rendering"', 'FROZEN_MIRROR_REPO = "Mirror.Repo"')
g = {"__name__": "__main__", "__file__": tool}
try:
    exec(compile(src, tool, "exec"), g)
except SystemExit as e:
    sys.exit(e.code or 0)
PY
}
export TOOL

echo "== 1. a stale sha256 is reported, exit 1 =="
out="$(run --registry "$REG" --repos-root "$ROOT" || true)"
grep -q "\[digest-matches\] stale" <<<"$out" || { echo "FAIL: stale digest not reported"; echo "$out"; exit 1; }
grep -q "\[digest-matches\] good"  <<<"$out" && { echo "FAIL: good row reported"; exit 1; }
run --registry "$REG" --repos-root "$ROOT" >/dev/null 2>&1 && { echo "FAIL: expected exit 1"; exit 1; }
echo "   ok"

echo "== 2. --write reconciles ONLY the stale digest, byte-preserving the rest =="
cp "$REG" "$WORK/before.yml"
run --registry "$REG" --repos-root "$ROOT" --write >/dev/null
grep -q "sha256: $ACTUAL_STALE" "$REG" || { echo "FAIL: stale row not reconciled"; exit 1; }
# Every line except the reconciled one must be byte-identical.
if [ "$(diff "$WORK/before.yml" "$REG" | grep -c '^[<>]')" -ne 2 ]; then
  echo "FAIL: --write touched more than the one stale sha256"; diff "$WORK/before.yml" "$REG"; exit 1
fi
run --registry "$REG" --repos-root "$ROOT" >/dev/null || { echo "FAIL: not coherent after --write"; exit 1; }
echo "   ok"

echo "== 3. a missing source is reported and --write cannot paper over it =="
write_registry
mv "$ROOT/Producer.One/skills/good/SKILL.md" "$ROOT/Producer.One/skills/good/RENAMED.md"
out="$(run --registry "$REG" --repos-root "$ROOT" || true)"
grep -q "\[source-exists\] good" <<<"$out" || { echo "FAIL: missing source not reported"; echo "$out"; exit 1; }
run --registry "$REG" --repos-root "$ROOT" --write >/dev/null 2>&1 && { echo "FAIL: --write should exit 1 while a source is missing"; exit 1; }
mv "$ROOT/Producer.One/skills/good/RENAMED.md" "$ROOT/Producer.One/skills/good/SKILL.md"
echo "   ok"

echo "== 4. a diverged frozen mirror is reported (the fs-gg-game-core shape) =="
write_registry
printf 'owned body DRIFTED\n' > "$ROOT/Mirror.Repo/template/product-skills/owned/SKILL.md"
out="$(run --registry "$REG" --repos-root "$ROOT" || true)"
grep -q "\[frozen-mirror\] owned" <<<"$out" || { echo "FAIL: mirror divergence not reported"; echo "$out"; exit 1; }
echo "   ok"

echo "== 5. a VANISHED frozen mirror is a finding — retiring it means FLIPPING THE VERDICT (#658) =="
# THIS CASE USED TO ASSERT THE OPPOSITE, and the opposite was the fail-open. It removed the copy and
# demanded SILENCE: "a row whose frozen copy has been retired simply has none." But the tool could not
# tell a RETIRED mirror from a DROPPED one — it compared only `if Rendering holds a file`, so an
# obligation whose copy had been deleted, renamed, or never vendored matched nothing and reported
# GREEN. That is a missing subject reporting green (epic #266) inside the arm written to catch it, and
# it is green on the REAL registry today: delete FS.GG.Rendering's fs-gg-audio copy on `main` and the
# check still says "coherent".
#
# The verdict is what separates the two, which is the whole point of declaring it. A mirror is retired
# when the OWNER says it is (`mirrored: false`, the ADR-0022 P6 retirement) — not when a file goes
# missing behind the owner's back.
# NOTE: the run still exits 1 (the `stale` row is deliberately stale), so assert on the finding, not
# on the overall exit code.
rm "$ROOT/Mirror.Repo/template/product-skills/owned/SKILL.md"
out="$(run --registry "$REG" --repos-root "$ROOT" || true)"
grep -q "\[frozen-mirror\] owned" <<<"$out" || { echo "FAIL: a live obligation with NO copy reported green — the #658 fail-open"; echo "$out"; exit 1; }
grep -q "there is NONE" <<<"$out" || { echo "FAIL: the finding does not say the copy is missing"; echo "$out"; exit 1; }

# ...and the RETIREMENT path: the owner flips the verdict, the copy goes, and the gate is quiet.
write_two_owned_mirror false
sed -i 's/\(id: owned,.*\) mirrored: true,/\1 mirrored: false,/' "$REG"
out="$(run --registry "$REG" --repos-root "$ROOT" || true)"
grep -q "\[frozen-mirror\]" <<<"$out" && { echo "FAIL: a properly retired mirror still reported"; echo "$out"; exit 1; }
grep -q "\[mirror-" <<<"$out" && { echo "FAIL: a retired mirror left a mirror finding behind"; echo "$out"; exit 1; }
printf 'owned body\n' > "$ROOT/Mirror.Repo/template/product-skills/owned/SKILL.md"
write_manifests
echo "   ok"

echo "== 6. a BOM never enters the digest =="
printf '\xef\xbb\xbfowned body\n' > "$ROOT/Producer.Two/skills/owned/SKILL.md"
out="$(run --registry "$REG" --repos-root "$ROOT" || true)"
grep -q "\[digest-matches\] owned" <<<"$out" && { echo "FAIL: BOM changed the digest"; exit 1; }
echo "   ok"

echo "== 7. a malformed digest is a finding, not a crash (unquoted all-digit YAML parses as int) =="
printf 'owned body\n' > "$ROOT/Producer.Two/skills/owned/SKILL.md"
write_registry
sed -i "s|sha256: $WRONG|sha256: 0000000000000000000000000000000000000000000000000000000000000000|" "$REG"
out="$(run --registry "$REG" --repos-root "$ROOT" || true)"
grep -q "\[digest-shape\] stale" <<<"$out" || { echo "FAIL: malformed digest not reported"; echo "$out"; exit 1; }
echo "   ok"

echo "== 8. --json is machine-readable =="
printf 'owned body\n' > "$ROOT/Producer.Two/skills/owned/SKILL.md"
write_registry
# `run --json` exits 1 because there ARE findings — that is the point of the check. Capture it
# rather than piping, so `set -o pipefail` does not abort the fixture on a correct non-zero exit.
json="$(run --registry "$REG" --repos-root "$ROOT" --json || true)"
python3 -c "
import json,sys
d=json.loads(sys.argv[1])
ids=sorted(f['id'] for f in d['findings'])
assert ids==['stale'], ids
print('   ok')
" "$json"

echo "== 9. a manifest-declared skill with NO registry row is a finding (the .github#289 shape) =="
write_registry
mkdir -p "$ROOT/Producer.Two/skills/newbie"
printf 'newbie body\n' > "$ROOT/Producer.Two/skills/newbie/SKILL.md"
NEWBIE="$(sha "$ROOT/Producer.Two/skills/newbie/SKILL.md")"
NEWBIE_ENTRY='{ "id": "newbie", "scope": "product", "sha256": "'"$NEWBIE"'", "supplied-by": "skills/newbie/", "materializes-when": "profile in [game]" }'
write_manifests "" "$NEWBIE_ENTRY"
out="$(run --registry "$REG" --repos-root "$ROOT" || true)"
grep -q "\[declared-completeness\] newbie" <<<"$out" || { echo "FAIL: absent row not reported"; echo "$out"; exit 1; }
echo "   ok"

echo "== 10. --write APPENDS the missing row, reconciled from the manifest =="
run --registry "$REG" --repos-root "$ROOT" --write >/dev/null || { echo "FAIL: --write should exit 0 once it can append"; exit 1; }
grep -q "id: newbie" "$REG" || { echo "FAIL: missing row not appended"; cat "$REG"; exit 1; }
grep -q "source: Producer.Two/skills/newbie/SKILL.md" "$REG" || { echo "FAIL: source not derived from supplied-by"; exit 1; }
grep -q 'materializes-when: "profile in \[game\]"' "$REG" || { echo "FAIL: predicate not carried over (and quoted)"; exit 1; }
# The appended row must be coherent on its own terms — a re-run finds nothing.
run --registry "$REG" --repos-root "$ROOT" >/dev/null || { echo "FAIL: not coherent after --write appended"; exit 1; }
echo "   ok"

echo "== 11. an entry with no supplied-by is REPORTED, never appended with an invented source =="
write_registry
# `orphan` has no `supplied-by` — exactly SDD's process-manifest shape.
write_manifests '{ "id": "orphan", "scope": "process", "sha256": "'"$GOOD"'" }' ""
out="$(run --registry "$REG" --repos-root "$ROOT" || true)"
grep -q "\[declared-completeness\] orphan" <<<"$out" || { echo "FAIL: orphan not reported"; echo "$out"; exit 1; }
grep -q "cannot append it" <<<"$out" || { echo "FAIL: did not say why it cannot append"; echo "$out"; exit 1; }
run --registry "$REG" --repos-root "$ROOT" --write >/dev/null 2>&1 && { echo "FAIL: --write must exit 1 while orphan is unappendable"; exit 1; }
grep -q "id: orphan" "$REG" && { echo "FAIL: --write invented a source for orphan"; exit 1; }
echo "   ok"

echo "== 12. a producer whose manifest VANISHED fails closed (it would hide every skill it declares) =="
write_registry
write_manifests
rm "$ROOT/Producer.One/.agents/skills/skill-manifest.json"
out="$(run --registry "$REG" --repos-root "$ROOT" || true)"
grep -q "\[manifest-found\] Producer.One" <<<"$out" || { echo "FAIL: vanished manifest not reported"; echo "$out"; exit 1; }
# ...and a manifest that is present but unparseable is equally a finding, not a skip.
write_manifests
printf 'not json\n' > "$ROOT/Producer.One/.agents/skills/skill-manifest.json"
out="$(run --registry "$REG" --repos-root "$ROOT" || true)"
grep -q "\[manifest-found\] Producer.One" <<<"$out" || { echo "FAIL: unreadable manifest not reported"; echo "$out"; exit 1; }
echo "   ok"

echo "== 13. a producer the registry names NOWHERE is still read; the frozen mirror is not a producer =="
write_registry
write_manifests
# Producer.Three has no row pointing at it, so a producer set derived from `source:` alone cannot
# see it — and every skill it declares would be invisible. It must still be read.
mkdir -p "$ROOT/Producer.Three/template/skill-manifest" "$ROOT/Producer.Three/skills/stranger"
printf 'stranger body\n' > "$ROOT/Producer.Three/skills/stranger/SKILL.md"
STRANGER="$(sha "$ROOT/Producer.Three/skills/stranger/SKILL.md")"
cat > "$ROOT/Producer.Three/template/skill-manifest/skill-manifest.json" <<JSON
{ "schemaVersion": 1, "skills": [
  { "id": "stranger", "scope": "product", "sha256": "$STRANGER", "supplied-by": "skills/stranger/" }
] }
JSON
out="$(run --registry "$REG" --repos-root "$ROOT" || true)"
grep -q "\[declared-completeness\] stranger" <<<"$out" || { echo "FAIL: unnamed producer not read"; echo "$out"; exit 1; }
# Mirror.Repo carries no manifest and is named by no `source:` — it is not a producer, so its
# absence of a manifest must NOT be reported.
grep -q "\[manifest-found\] Mirror.Repo" <<<"$out" && { echo "FAIL: frozen mirror mistaken for a producer"; exit 1; }
# `always` is written as a bare token, never quoted — the ADR-0017 default round-trips.
run --registry "$REG" --repos-root "$ROOT" --write >/dev/null || { echo "FAIL: --write should append stranger"; exit 1; }
# Scope the assertion to the APPENDED row: `good`/`stale` already end `materializes-when: always }`,
# so an unscoped grep passes no matter what format_row emitted.
grep "id: stranger" "$REG" | grep -q "materializes-when: always }" \
  || { echo "FAIL: default predicate not emitted bare"; grep "id: stranger" "$REG"; exit 1; }
rm -rf "$ROOT/Producer.Three"
echo "   ok"

echo "== 14. a skill declared by TWO producers is reported, never attributed by sort order =="
write_registry
# The frozen-mirror shape: the alphabetically FIRST repo is the copy, not the owner. Guessing would
# write `owner: producer-one` and a `source:` pointing at the mirror.
DUP='{ "id": "dup", "scope": "product", "sha256": "'"$GOOD"'", "supplied-by": "skills/good/" }'
write_manifests "$DUP" "$DUP"
out="$(run --registry "$REG" --repos-root "$ROOT" || true)"
grep -q "\[declared-completeness\] dup" <<<"$out" || { echo "FAIL: duplicate-declared skill not reported"; echo "$out"; exit 1; }
grep -q "declared by 2 producer manifests" <<<"$out" || { echo "FAIL: ambiguity not named"; echo "$out"; exit 1; }
run --registry "$REG" --repos-root "$ROOT" --write >/dev/null 2>&1 && { echo "FAIL: --write must exit 1 on an ambiguous owner"; exit 1; }
grep -q "id: dup" "$REG" && { echo "FAIL: --write guessed an owner for dup"; grep "id: dup" "$REG"; exit 1; }
echo "   ok"

echo "== 15. a manifest whose entries are not objects is a finding, not a traceback =="
write_registry
write_manifests
printf '{ "schemaVersion": 1, "skills": ["fs-gg-oops"] }\n' > "$ROOT/Producer.One/.agents/skills/skill-manifest.json"
out="$(run --registry "$REG" --repos-root "$ROOT" 2>&1 || true)"
grep -q "\[manifest-found\] Producer.One" <<<"$out" || { echo "FAIL: non-object entry not reported"; echo "$out"; exit 1; }
grep -q "Traceback" <<<"$out" && { echo "FAIL: crashed instead of reporting"; echo "$out"; exit 1; }
echo "   ok"

echo "== 16. a predicate containing a quote round-trips (no early-terminated YAML scalar) =="
write_registry
mkdir -p "$ROOT/Producer.Two/skills/quoted"
printf 'quoted body\n' > "$ROOT/Producer.Two/skills/quoted/SKILL.md"
QUOTED="$(sha "$ROOT/Producer.Two/skills/quoted/SKILL.md")"
write_manifests "" '{ "id": "quoted", "scope": "product", "sha256": "'"$QUOTED"'", "supplied-by": "skills/quoted/", "materializes-when": "name == \"Acme\"" }'
run --registry "$REG" --repos-root "$ROOT" --write >/dev/null || { echo "FAIL: --write should append quoted"; exit 1; }
python3 -c "
import yaml,sys
rows={r['id']: r for r in yaml.safe_load(open(sys.argv[1]))['skills']}
got = rows['quoted']['materializes-when']
assert got == 'name == \"Acme\"', repr(got)
" "$REG" || { echo "FAIL: quoted predicate did not round-trip"; grep "id: quoted" "$REG"; exit 1; }
echo "   ok"

# Cases 17-21 cover a row's CONTENT where it is neither bytes nor presence (.github#292, epic #266):
# the `materializes-when` predicate. It is what `skill-union-assert.sh --params` evaluates to decide
# whether a skill's ABSENCE from a scaffold is legitimate, so a stale one makes the union gate wrong
# in a direction it cannot detect — while every digest stays green. `fs-gg-testing` sat at
# `profile == governed` for six days after Rendering widened it to five profiles (Rendering#90).

# Producer.Two's manifest with a chosen `materializes-when` for `owned` ($1 is a JSON *value*, so a
# string must arrive with its quotes: '"profile in [game]"').
write_two_owned_when() {
  cat > "$ROOT/Producer.Two/template/skill-manifest/skill-manifest.json" <<JSON
{ "schemaVersion": 1, "skills": [
  { "id": "owned", "scope": "product", "sha256": "$OWNED", "mirrored": true, "supplied-by": "skills/owned/", "materializes-when": $1 }
] }
JSON
}

echo "== 17. a diverged materializes-when is reported (the fs-gg-testing shape) =="
write_registry
write_manifests
# The registry narrows what the manifest widened — every digest still matches, as it did on main.
sed -i 's|materializes-when: "profile in \[game\]"|materializes-when: "profile == governed"|' "$REG"
out="$(run --registry "$REG" --repos-root "$ROOT" || true)"
grep -q "\[predicate-matches\] owned" <<<"$out" || { echo "FAIL: diverged predicate not reported"; echo "$out"; exit 1; }
grep -q 'profile in \[game\]' <<<"$out" || { echo "FAIL: finding does not name the manifest's predicate"; echo "$out"; exit 1; }
grep -q "\[digest-matches\] owned" <<<"$out" && { echo "FAIL: a predicate divergence is not a digest finding"; exit 1; }
echo "   ok"

echo "== 18. --write rewrites ONLY the predicate, byte-preserving the rest =="
write_registry
write_manifests
# Clear the deliberately-stale digest first, so the predicate is the registry's ONLY defect.
run --registry "$REG" --repos-root "$ROOT" --write >/dev/null
cp "$REG" "$WORK/before-pred.yml"
sed -i 's|materializes-when: "profile in \[game\]"|materializes-when: "profile == governed"|' "$REG"
run --registry "$REG" --repos-root "$ROOT" --write >/dev/null || { echo "FAIL: --write should reconcile the predicate"; exit 1; }
# Restoring the manifest's predicate must reproduce the file EXACTLY — alignment, comments and all.
cmp -s "$WORK/before-pred.yml" "$REG" || { echo "FAIL: --write did not byte-restore the row"; diff "$WORK/before-pred.yml" "$REG"; exit 1; }
run --registry "$REG" --repos-root "$ROOT" >/dev/null || { echo "FAIL: not coherent after --write"; exit 1; }
echo "   ok"

echo "== 19. list spacing is not drift — whitespace is normalized before comparing =="
write_registry
write_manifests
sed -i 's|materializes-when: "profile in \[game\]"|materializes-when: "profile  in  [ game ]"|' "$REG"
out="$(run --registry "$REG" --repos-root "$ROOT" || true)"   # exits 1 on the `stale` digest
grep -q "\[predicate-matches\]" <<<"$out" && { echo "FAIL: reformatting reported as drift"; echo "$out"; exit 1; }
echo "   ok"

echo "== 20. C-style grammar is a DIVERGENCE, never normalized away =="
# `(profile == "game")` is semantically `profile == game`, but the ADR-0017 evaluator cannot parse
# it and reads it as FALSE (the grammar Rendering shipped before Rendering#77). Absorbing it here
# would make a producer's regression to an unevaluable predicate report green — the fails-open shape
# epic #266 exists to close. It must surface. (Rejecting non-canonical grammar outright is #290.)
write_registry
sed -i 's|materializes-when: "profile in \[game\]"|materializes-when: "profile == game"|' "$REG"
write_manifests
write_two_owned_when '"(profile == \"game\")"'
out="$(run --registry "$REG" --repos-root "$ROOT" || true)"
grep -q "\[predicate-matches\] owned" <<<"$out" || { echo "FAIL: C-style grammar silently normalized — the gate fails open"; echo "$out"; exit 1; }
echo "   ok"

echo "== 21. producers that contradict each other are reported; --write refuses to pick a side =="
write_registry
write_manifests
# Producer.One re-declares `owned` with a different predicate. The row's `owner:` (fs-gg-game) names
# NEITHER producer, so there is no authoritative side to reconcile from.
write_manifests '{ "id": "owned", "scope": "product", "sha256": "'"$OWNED"'", "supplied-by": "skills/owned/", "materializes-when": "profile in [sample-pack]" }' ""
out="$(run --registry "$REG" --repos-root "$ROOT" || true)"
grep -q "\[predicate-matches\] owned" <<<"$out" || { echo "FAIL: contradicting declarers not reported"; echo "$out"; exit 1; }
grep -q "disagree on" <<<"$out" || { echo "FAIL: contradiction not named"; echo "$out"; exit 1; }
run --registry "$REG" --repos-root "$ROOT" --write >/dev/null 2>&1 && { echo "FAIL: --write must exit 1 while producers contradict"; exit 1; }
grep -q 'id: owned.*materializes-when: "profile in \[game\]"' "$REG" || { echo "FAIL: --write changed a row it cannot reconcile"; grep 'id: owned' "$REG"; exit 1; }
echo "   ok"

echo "== 22. the owning producer's manifest wins over a frozen mirror's re-declaration =="
write_registry
# `good` is owned by producer-one and declared by Producer.One (always). Producer.Two re-declares it
# with a different predicate — the frozen-mirror shape. `owner:` says Producer.One's word is law, so
# the mirror's disagreement must NOT be reported against the row.
write_manifests "" '{ "id": "good", "scope": "process", "sha256": "'"$GOOD"'", "supplied-by": "skills/good/", "materializes-when": "profile in [game]" }'
out="$(run --registry "$REG" --repos-root "$ROOT" || true)"
grep -q "\[predicate-matches\] good" <<<"$out" && { echo "FAIL: a mirror's predicate overrode the owner's"; echo "$out"; exit 1; }
echo "   ok"

echo "== 23. normalization never reaches inside a quoted string literal =="
# `name == "a,b"` is a predicate over a VALUE containing a comma. Regularizing list spacing must not
# rewrite it to `"a, b"` — `--write` persists this text into the registry, and the union gate parses
# it. Layout outside the literal is still normalized.
write_registry
sed -i 's|materializes-when: "profile in \[game\]"|materializes-when: "profile == governed"|' "$REG"
write_two_owned_when '"name ==  \"a,b\""'
run --registry "$REG" --repos-root "$ROOT" --write >/dev/null 2>&1 || true
python3 -c "
import yaml,sys
rows={r['id']: r for r in yaml.safe_load(open(sys.argv[1]))['skills']}
got = rows['owned']['materializes-when']
assert got == 'name == \"a,b\"', repr(got)   # collapsed the double space, kept the literal's bytes
" "$REG" || { echo "FAIL: normalization mangled a quoted literal"; grep 'id: owned' "$REG"; exit 1; }
echo "   ok"

echo "== 24. a row with NO materializes-when field is reported, not crashed on =="
# Absent ⇒ `always` (ADR-0017), so this is a real divergence — but there is no VALUE to rewrite in
# place, and inserting one would re-align the hand-formatted flow map. Report, never abort.
write_registry
write_manifests
# Only `owned` carries a quoted predicate — `good`/`stale` end `materializes-when: always }` — so this
# strips the field from that row alone. (Anchored on the predicate, not on the whole row: the row's
# field list grew a `mirrored:` in #658, and a shape-hardcoded sed silently matches nothing.)
sed -i 's|, materializes-when: "profile in \[game\]" }| }|' "$REG"
grep -q 'id: owned.*materializes-when' "$REG" && { echo "FAIL: fixture could not strip the field"; grep 'id: owned' "$REG"; exit 1; }
out="$(run --registry "$REG" --repos-root "$ROOT" 2>&1 || true)"
grep -q "\[predicate-matches\] owned" <<<"$out" || { echo "FAIL: absent field not reported as a divergence"; echo "$out"; exit 1; }
grep -q "declares no .materializes-when:. field" <<<"$out" || { echo "FAIL: did not say why it cannot rewrite"; echo "$out"; exit 1; }
wout="$(run --registry "$REG" --repos-root "$ROOT" --write 2>&1 || true)"
grep -q "Traceback" <<<"$wout" && { echo "FAIL: --write crashed instead of reporting"; echo "$wout"; exit 1; }
run --registry "$REG" --repos-root "$ROOT" --write >/dev/null 2>&1 && { echo "FAIL: --write must exit 1 while the row cannot be reconciled"; exit 1; }
grep -q 'id: owned.*materializes-when' "$REG" && { echo "FAIL: --write inserted a field it should not have"; exit 1; }
echo "   ok"


# ─────────────────────────────────────────────────────────────────────────────────────────────────
# Cases 25-30 cover the RESPONSE half's PR BODY (.github#425, epic #266) — `scripts/skill-registry-
# autofix-body`, which composes the standing reconcile PR.
#
# The body is the artifact that misled: PR #414 asserted in the present tense that it had reconciled
# the registry, listed no residual section (at cut time there was none), and called its own post-write
# re-check "the verification of record" — while two producer merges had landed behind it, so merging
# it would have left `main` RED. The PR's own `skill-registry-coherence` job was structurally prevented
# from running on it (GITHUB_TOKEN pushes do not re-trigger `on: pull_request`), so nothing could
# contradict the body. These cases pin the properties that make it honest: it STAMPS the producer
# commits it was computed from, and it states a VERDICT.
#
# #514 pushed the branch with the App token, so the gate now DOES run on the PR. That inverts the
# third property rather than deleting it: the body must no longer disclaim a currency it CAN now have
# (case 27), because a body that under-claims sends the reader off to re-derive by hand what the
# checks already decide. The stamp survives — a running gate makes a stale reconcile RED, but only the
# stamp makes it DIAGNOSABLE (which producer moved, and past what).
BODY="$HERE/../../scripts/skill-registry-autofix-body"
SHA_ONE="1111111111111111111111111111111111111111"
SHA_TWO="2222222222222222222222222222222222222222"
SHA_ONE_MOVED="9999999999999999999999999999999999999999"
PRODUCERS="{\"Producer.One\": \"$SHA_ONE\", \"Producer.Two\": \"$SHA_TWO\"}"

# `stale` is a mechanical digest rewrite; `gone` is a judgement case `--write` refuses to touch;
# `orphan` is a declared-completeness with NO derivable `row` — which must count as RESIDUAL, not as
# an append (a non-derivable row stays a red finding).
cat > "$WORK/findings.json" <<JSON
{"findings": [
  {"id": "stale",  "check": "digest-matches", "declared": "$WRONG", "actual": "$ACTUAL_STALE", "detail": "sha256 differs"},
  {"id": "gone",   "check": "source-exists",  "detail": "source no longer exists"},
  {"id": "orphan", "check": "declared-completeness", "detail": "declared by a manifest with no supplied-by"}
]}
JSON
cat > "$WORK/findings-clean.json" <<JSON
{"findings": [
  {"id": "stale", "check": "digest-matches", "declared": "$WRONG", "actual": "$ACTUAL_STALE", "detail": "sha256 differs"}
]}
JSON

body() { python3 "$BODY" --registry "$REG" --repos-root "$ROOT" --now "2026-07-11T06:42:00Z" "$@"; }

echo "== 25. the PR body STAMPS the producer commits the reconcile was computed from =="
write_registry
out="$(body --findings "$WORK/findings.json" --producers "$PRODUCERS")"
grep -q "### Computed from" <<<"$out" || { echo "FAIL: no provenance stamp — the #425 defect"; echo "$out"; exit 1; }
grep -q "2026-07-11T06:42:00Z" <<<"$out" || { echo "FAIL: body does not say WHEN it was reconciled"; exit 1; }
grep -q '`Producer.One` | `11111111`' <<<"$out" || { echo "FAIL: producer commit not stamped in the table"; echo "$out"; exit 1; }
# The machine-readable marker must carry the FULL sha: the table shortens to 8 chars for humans, and
# the next run compares full commits. A marker that stored the truncated form would report every
# producer as "moved" on every run — a staleness banner that always fires is one nobody reads.
grep -q "<!-- fsgg:autofix-stamp .*$SHA_ONE" <<<"$out" || { echo "FAIL: stamp marker missing or truncated"; echo "$out"; exit 1; }
echo "   ok"

echo "== 26. the body states a VERDICT — residual findings mean merging leaves main RED =="
out="$(body --findings "$WORK/findings.json" --producers "$PRODUCERS")"
grep -q "would leave \`main\` RED" <<<"$out" || { echo "FAIL: no verdict; #414 read as a fix because a section was ABSENT"; echo "$out"; exit 1; }
# `orphan` has no derivable row, so it is a JUDGEMENT case, not an append: it must be counted in the
# residual verdict (2 = `gone` + `orphan`), never silently classified as reconciled.
grep -q "2 finding(s) below are judgement cases" <<<"$out" || { echo "FAIL: non-derivable declared-completeness not counted as residual"; echo "$out"; exit 1; }
grep -q "Still needs a human" <<<"$out" || { echo "FAIL: residual section missing"; exit 1; }
out="$(body --findings "$WORK/findings-clean.json" --producers "$PRODUCERS")"
grep -q "merging this greens" <<<"$out" || { echo "FAIL: a clean reconcile must SAY it greens the gate, not imply it"; echo "$out"; exit 1; }
grep -q "Still needs a human" <<<"$out" && { echo "FAIL: residual section on a clean reconcile"; exit 1; }
# The checklist must only ask for work the PR actually contains. An "appended rows" item on a PR that
# appended nothing points at a section that was never emitted — standing noise, and the reader who
# learns to tick through it is the reader who ticks past the currency re-read directly above it.
grep -q "appended row" <<<"$out" && { echo "FAIL: checklist asks to confirm appended rows on a PR with none"; echo "$out"; exit 1; }
cat > "$WORK/findings-appended.json" <<JSON
{"findings": [
  {"id": "newrow", "check": "declared-completeness", "detail": "declared by a manifest, absent from the registry",
   "row": {"id": "newrow", "scope": "product", "owner": "producer-two", "source": "Producer.Two/skills/owned/SKILL.md", "sha256": "$OWNED", "materializes-when": "always"}}
]}
JSON
out="$(body --findings "$WORK/findings-appended.json" --producers "$PRODUCERS")"
grep -q "CONFIRM the owner before merge" <<<"$out" || { echo "FAIL: appended rows not surfaced for confirmation"; echo "$out"; exit 1; }
grep -q "appended row" <<<"$out" || { echo "FAIL: checklist omits the appended-row confirmation it DOES need"; echo "$out"; exit 1; }
# An append is a manifest GUESS at the owner, so it is not a clean "this greens the gate" either.
grep -q "greens the gate only once the appended rows are confirmed" <<<"$out" || { echo "FAIL: an appended row must not read as an unconditional green"; echo "$out"; exit 1; }
echo "   ok"

echo "== 27. the body claims neither a currency it cannot have, nor a staleness it no longer has =="
out="$(body --findings "$WORK/findings-clean.json" --producers "$PRODUCERS")"
# The exact phrase #425 indicted. It asserted the post-write re-check settled the question; it
# settles only the instant the job ran, and nothing re-checks it afterwards.
grep -qi "verification of record" <<<"$out" && { echo "FAIL: the body still claims to be the verification of record"; exit 1; }
# ...and the #514 INVERSE, which is the failure this case now has to catch too. The branch is pushed
# with the App token, so `skill-registry-coherence` DOES run on the PR. The old disclosure ("does not
# run on it") and the by-hand `git ls-remote` currency check it prescribed are now FALSE — and a body
# that UNDER-claims is misleading in the other direction: it would send a reader to re-derive by hand
# what a red check on their screen already told them, which is the manual step #514 exists to delete.
grep -q "does not run on it" <<<"$out" && { echo "FAIL: body still disclaims checks that now RUN (#514)"; echo "$out"; exit 1; }
grep -q "git ls-remote" <<<"$out" && { echo "FAIL: body still asks a human to settle currency by hand (#514)"; echo "$out"; exit 1; }
grep -q "runs on this PR" <<<"$out" || { echo "FAIL: body must say the gate runs on it, so a stale reconcile goes red by itself"; echo "$out"; exit 1; }
echo "   ok"

echo "== 28. --read-stamp round-trips the marker, and fails SOFT on a body without one =="
body --findings "$WORK/findings-clean.json" --producers "$PRODUCERS" > "$WORK/pr-body.md"
got="$(python3 "$BODY" --read-stamp "$WORK/pr-body.md")"
python3 -c "
import json,sys
got = json.loads(sys.argv[1])
assert got == {'Producer.One': sys.argv[2], 'Producer.Two': sys.argv[3]}, got
" "$got" "$SHA_ONE" "$SHA_TWO" || { echo "FAIL: stamp did not round-trip: $got"; exit 1; }
# A first-ever run has no standing PR, and a hand-edited body may have lost the marker. Both must
# yield "no prior stamp" rather than killing the job — the body only ENRICHES with a supersession
# note, and a failure to read the OLD PR must never starve a genuine reconcile of its NEW one.
printf 'a body with no marker\n' > "$WORK/nomarker.md"
[ "$(python3 "$BODY" --read-stamp "$WORK/nomarker.md")" = "{}" ] || { echo "FAIL: a body with no marker must read as {}"; exit 1; }
printf '<!-- fsgg:autofix-stamp {not json} -->\n' > "$WORK/badmarker.md"
[ "$(python3 "$BODY" --read-stamp "$WORK/badmarker.md")" = "{}" ] || { echo "FAIL: a malformed marker must read as {}, not crash"; exit 1; }
[ "$(python3 "$BODY" --read-stamp "$WORK/does-not-exist.md")" = "{}" ] || { echo "FAIL: a missing body must read as {}, not crash"; exit 1; }
echo "   ok"

echo "== 29. a snapshot that supersedes a stale one SAYS so, and names the producer that moved =="
prev="{\"Producer.One\": \"$SHA_ONE_MOVED\", \"Producer.Two\": \"$SHA_TWO\"}"
out="$(body --findings "$WORK/findings-clean.json" --producers "$PRODUCERS" --prev-producers "$prev")"
grep -q "supersedes a stale snapshot" <<<"$out" || { echo "FAIL: a superseded snapshot is not named — this IS the #414 decay"; echo "$out"; exit 1; }
grep -q "moved from \`99999999\`" <<<"$out" || { echo "FAIL: the moved producer is not named"; echo "$out"; exit 1; }
# Producer.Two did NOT move, so it must not be reported as having moved.
grep -q "\`Producer.Two\` | \`22222222\` |" <<<"$out" || { echo "FAIL: an unmoved producer was annotated as moved"; echo "$out"; exit 1; }
# An unreadable prior stamp degrades to "no prior stamp" — never a crash, never a false banner.
out="$(body --findings "$WORK/findings-clean.json" --producers "$PRODUCERS" --prev-producers 'not json')"
grep -q "supersedes a stale snapshot" <<<"$out" && { echo "FAIL: a malformed prior stamp manufactured a supersession banner"; exit 1; }
echo "   ok"

echo "== 30. the body is deterministic — same inputs, same bytes =="
# The composer must not read the clock or the environment: the fixture asserts on exact bytes, and a
# body that drifts run-to-run would make the supersession diff above meaningless.
a="$(body --findings "$WORK/findings.json" --producers "$PRODUCERS")"
b="$(body --findings "$WORK/findings.json" --producers "$PRODUCERS")"
[ "$a" = "$b" ] || { echo "FAIL: composer is not deterministic"; diff <(echo "$a") <(echo "$b") || true; exit 1; }
echo "   ok"

echo "== 31. the autofix workflow cannot RETIRE on an unverified registry, nor PUSH with a token that disables its own checks =="
# The retire step is the only branch in this fabric that DESTROYS state (it closes a PR). Its trigger,
# `changed == 'false'`, is derived from a step ending in `|| true` — so a CRASHED reconcile leaves the
# registry untouched and produces exactly the same empty diff as a coherent one. Retiring on that
# signal would close a NEEDED reconcile while commenting that the registry is clean: the epic-#266
# fail-open, reintroduced inside the fix for it.
#
# So the retire must additionally require POSITIVE evidence. It always did — but until #537 it demanded
# the WRONG evidence: FULL COHERENCE (the check exits 0). `--write` refuses judgement cases by design,
# so while one is outstanding the registry can never be coherent and the retire could NEVER fire, however
# obsolete the PR had become — the guard was disabled in exactly the state that produces obsolete PRs
# (#414 and #521 both had to be closed by hand). The evidence it needs is "this RECONCILE is finished",
# which `skill-registry-retire-gate` decides; case 32 drives that decision over its full truth table.
#
# This case pins the WIRING, structurally, because the condition lives in YAML and cannot be unit-tested:
# a fail-open here ships green through a fixture that only tests the script.
WF="$HERE/../../.github/workflows/skill-registry-autofix.yml"
python3 - "$WF" <<'PY' || exit 1
import sys, json, yaml
wf = yaml.safe_load(open(sys.argv[1]))          # parses at all — a dedented block scalar fails HERE
steps = {s.get("name"): s for s in wf["jobs"]["autofix"]["steps"] if s.get("name")}

proof = next((n for n in steps if n.startswith("Decide whether the reconcile is finished")), None)
assert proof, "no step establishes positive evidence that the reconcile is finished"
assert steps[proof]["id"] == "settled", "the retire's `if` reads steps.settled.outputs.settled"
# The gate must read the PAYLOAD, not the exit code: the check exits 1 both when it merely found
# something and when it CRASHED, so a gate keyed on the exit code cannot tell those apart — which is
# the whole trap. Assert it runs the script that reads findings JSON.
assert "skill-registry-retire-gate" in steps[proof]["run"], \
    "the settled decision is not delegated to the tested retire-gate script"
assert "--json" in steps[proof]["run"], "the gate is not given machine-readable findings to judge"

retire = next((n for n in steps if n.startswith("Retire")), None)
assert retire, "no retire step"
cond = steps[retire]["if"]
assert "steps.settled.outputs.settled == 'true'" in cond, \
    f"retire is not gated on the reconcile being finished — an empty diff from a CRASHED reconcile would close a needed PR: {cond}"
assert "steps.diff.outputs.changed == 'false'" in cond, f"retire lost its empty-diff guard: {cond}"
# The retire COMMENT must not assert a coherence nobody proved (#537). With judgement cases outstanding
# "the registry is now coherent" is simply false, and the old comment said it anyway — which is the #425
# defect (an artifact asserting a state nothing verified) reappearing in the step that cleans up after it.
assert "coherent with the" not in steps[retire]["run"], \
    "the retire comment still claims the registry is COHERENT — it is only finished; judgement cases may remain (#537)"
assert "retire-note.md" in steps[retire]["run"], \
    "the retire comment does not name the outstanding judgement cases (#537)"

# EVERY `steps.<id>.outputs.*` REFERENCE MUST RESOLVE TO A STEP THAT EXISTS.
#
# GitHub resolves a reference to a step id that does not exist as the EMPTY STRING. It does not warn,
# and the run stays green — so renaming a step silently turns every `if:` and every status line that
# referenced its old id into a dead branch that can never be taken.
#
# This is not hypothetical: renaming `coherent` -> `settled` in this very PR left the "Dry-run notice"
# step reading `steps.coherent.outputs.coherent`, which meant a dry run would have reported "a real run
# would touch nothing" even when a real run would now RETIRE. A status message asserting work that never
# ran is the exact defect this workflow exists to remove, so a rename must not be able to reintroduce it.
import re
raw = open(sys.argv[1]).read()
declared = {s["id"] for s in wf["jobs"]["autofix"]["steps"] if s.get("id")}
referenced = set(re.findall(r"steps\.([A-Za-z0-9_-]+)\.outputs", raw))
dangling = referenced - declared
assert not dangling, (
    f"workflow references step id(s) that do not exist: {sorted(dangling)} (declared: {sorted(declared)}). "
    "GitHub resolves these to the EMPTY STRING silently, so the branch is dead and the run still goes green."
)

# The recovered stamp comes out of a PR BODY (writable), so it must reach the shell through the
# environment. `${{ }}` is substituted textually BEFORE bash parses the line: a single quote in the
# stamp would escape the argument and execute, in a job holding `contents: write`.
compose = steps["Compose PR body"]
assert "PREV_STAMP" in compose.get("env", {}), "prev stamp must be passed via env, not inlined"
assert "${{ steps.prev.outputs.stamp }}" not in compose["run"], \
    "prev stamp is interpolated into the run script — shell-injection via a hand-edited PR body"

# THE PUSH MUST NOT REVERT TO github.token (#514). Which token pushes the branch is what decides
# whether `skill-registry-coherence` runs on the standing PR at all: GitHub's recursion guard drops
# `on: pull_request` for a GITHUB_TOKEN push, so reverting this would fail the gate OPEN — silently,
# and looking EXACTLY like a healthy workflow. There is no runtime signal to catch that (the run goes
# green; only the PR's absent checks would show it, which is the very thing nobody was looking at), so
# it is asserted structurally, here, where the revert would have to be written.
mint = steps.get("Mint the cross-repo-dispatch App token")
assert mint, "no App-token mint step — an unminted push cannot re-trigger the PR's own checks (#514)"
assert mint.get("id") == "app-token", "the mint step's id is what every other step references"

# The checkout persists the credential that `git push` later uses. A checkout that does not take the
# App token leaves GITHUB_TOKEN as the pusher — the same fail-open, one step removed.
checkout = next(
    s for s in wf["jobs"]["autofix"]["steps"] if str(s.get("uses", "")).startswith("actions/checkout")
)
assert checkout.get("with", {}).get("token") == "${{ steps.app-token.outputs.token }}", \
    "checkout does not take the App token — the reconcile push falls back to GITHUB_TOKEN (#514)"
# Taking the token and PERSISTING it are two different things: `persist-credentials: false` is a
# common hardening reflex (actions/checkout's own docs encourage it) and would leave `token:` sitting
# there looking correct while writing no credential at all, so the push would fail. Loud, not
# fail-open — but the credential path is only half-pinned if this half is left unasserted.
assert checkout.get("with", {}).get("persist-credentials") is True, \
    "checkout does not persist the App token — `git push` would have no credential at all (#514)"

pr = steps["Open or update the standing PR"]
assert "git push" in pr["run"], "the standing-PR step no longer pushes — has the fabric moved?"
assert pr.get("env", {}).get("GH_TOKEN") == "${{ steps.app-token.outputs.token }}", \
    "the standing PR is not opened/updated with the App token (#514)"
assert "github.token" not in json.dumps([pr.get("env", {}), pr["run"]]), \
    "the reconcile push/PR reaches for github.token — the recursion guard would stop the PR's own " \
    "checks from running, which fails the gate OPEN (#514)"

# Belt and braces: deny GITHUB_TOKEN the scope such a revert would need to succeed at all. With
# `contents: read`, a github.token push 403s LOUDLY instead of quietly producing a checkless PR.
assert wf["permissions"]["contents"] == "read", \
    "GITHUB_TOKEN holds contents: write — a revert to a github.token push would silently succeed (#514)"

# THE MERGE MUST BE GATED, AND IT MUST NOT BE `--auto` (#642). A green PR nobody merges lands nothing,
# so the bot now merges its own standing PR — which makes MERGE the second branch here that destroys
# state (it lands a commit on `main`). Two things are asserted structurally, because both would fail
# SILENTLY and GREEN:
#
#   * The merge is gated on the TESTED script's verdict, not on an inline "are the checks green?" block.
#   * It is a DIRECT merge, never `gh pr merge --auto`. Native auto-merge waits only for REQUIRED
#     checks, and `registry-coherence` is NOT required on `main` (and must not become one: its verdict
#     depends on other repos' mains, so requiring it would let a producer's merge deadlock every open
#     `.github` PR — #549). So `--auto` would merge straight past a RED registry-coherence, i.e. land a
#     snapshot its own gate calls obsolete. That is #425's inversion, automated — and a run doing it
#     would be GREEN. There is no runtime signal to catch it, so it is pinned here, where the revert
#     would have to be written.
merge = next((n for n in steps if n.startswith("Merge the standing PR")), None)
assert merge, "nothing merges the standing PR — a green PR that nobody merges lands nothing (#642)"
mcond = steps[merge]["if"]
assert "steps.merge-gate.outputs.merge == 'true'" in mcond, \
    f"the merge is not gated on the tested merge-gate script's verdict: {mcond}"
assert "--auto" not in steps[merge]["run"], \
    "the merge arms GitHub's NATIVE auto-merge, which waits only for REQUIRED checks — and " \
    "`registry-coherence` is not one, so this would merge past the single check that can prove the " \
    "snapshot obsolete (#642, #425)"
assert steps[merge].get("env", {}).get("GH_TOKEN") == "${{ steps.app-token.outputs.token }}", \
    "the merge does not use the App token (#514)"

gate = next((n for n in steps if n.startswith("Decide whether the standing PR may be merged")), None)
assert gate, "no step establishes that the standing PR is safe to merge"
assert steps[gate]["id"] == "merge-gate", "the merge's `if` reads steps.merge-gate.outputs.merge"
assert "skill-registry-merge-gate" in steps[gate]["run"], \
    "the merge decision is not delegated to the tested merge-gate script — a merge lands a commit on " \
    "`main`, so its truth table may not live in an untestable inline block"

# MERGE AND RETIRE MUST STAY ON MUTUALLY EXCLUSIVE BRANCHES. This is the sequencing #642 flags as the
# thing that makes it more than a copy-paste: a PR that is about to be RETIRED as obsolete must not be
# merged first, and vice versa. They cannot race — merge hangs off `changed == 'true'`, retire off
# `changed == 'false'` — but that is a property of two `if:` strings, and nothing but this assertion
# stops a future edit from quietly moving one of them.
assert "steps.diff.outputs.changed == 'true'" in mcond, \
    f"the merge lost its non-empty-diff guard — it could now race the retire on the same run: {mcond}"
assert "steps.diff.outputs.changed == 'false'" in cond, "the retire lost its empty-diff guard"

# FAILING TO LAND IT IS AN ERROR, NOT A WARNING — the rule both other propagation arms state in their
# own comments, and the one #642 quotes. A run that reconciles the registry, pushes the branch and then
# does NOT merge it has left `main` exactly as stale as it found it. If that run is GREEN it asserts
# work it did not do — which is the defect this workflow exists to remove, reappearing in the step that
# reports on it. A `::warning::` here would be invisible: the run goes green, the schedule goes green,
# and the registry stays stale, which is EXACTLY the state #642 was filed about.
land = next((n for n in steps if n.startswith("The reconcile did not land")), None)
assert land, "nothing reports a reconcile that did not land — a green run would imply it did (#266)"
assert "steps.merge-gate.outputs.merge != 'true'" in steps[land]["if"], \
    f"the did-not-land report is not triggered by the gate refusing: {steps[land]['if']}"
assert "exit 1" in steps[land]["run"], \
    "a reconcile that did not land exits 0 — the run goes GREEN over a registry that is still stale. " \
    "Failing to land it is an ERROR, not a warning (#642, #266): both other propagation arms exit 1."

print("   (workflow: retire demands proof; merge is gated and NOT --auto; the two cannot race;")
print("    stamp is not shell-interpolated; push cannot use github.token)")
PY
echo "   ok"

echo "== 32. the retire gate: FINISHED is not COHERENT, and a blinded check is not a judgement case =="
# The retire is the only branch in this fabric that DESTROYS state, so its decision is driven here over
# the whole truth table — asserting the REASON on every leg, not merely the verdict. Two of these legs
# are the traps that make the decision worth extracting from the YAML at all:
#
#   * a CRASH and a FINDING both exit 1, so the exit code cannot separate them — the gate must read the
#     payload, and the absence of parseable findings IS the crash signal;
#   * a corrupt producer clone arrives as a `manifest-found` FINDING, and it is a "residual" one — so a
#     naive "only judgement cases left?" test waves the #425 fail-open straight through, wearing a
#     finding's clothes.
GATE="$HERE/../../scripts/skill-registry-retire-gate"
gate() { python3 "$GATE" --findings "$1" --note-file "$WORK/note.md" 2>"$WORK/why.txt"; }

# (a) zero findings — the registry is coherent AND the reconcile is finished. Retire.
echo '{"findings": []}' > "$WORK/f.json"
gate "$WORK/f.json" > "$WORK/out.txt" || { echo "FAIL: a coherent registry must retire"; cat "$WORK/why.txt"; exit 1; }
grep -q "settled=true" "$WORK/out.txt" || { echo "FAIL: settled!=true on a coherent registry"; exit 1; }
[ -s "$WORK/note.md" ] && { echo "FAIL: no judgement cases, but a note was emitted"; cat "$WORK/note.md"; exit 1; }

# (b) THE #537 CASE. Only judgement cases remain — `--write` will NEVER resolve them, so they cannot
# make an obsolete snapshot any less obsolete. The old gate refused here, forever, and that is the bug.
cat > "$WORK/f.json" <<'JSON'
{"findings": [
  {"check": "frozen-mirror", "id": "fs-gg-persistence", "detail": "mirror is not byte-identical to the canonical body"}
]}
JSON
gate "$WORK/f.json" > "$WORK/out.txt" || { echo "FAIL: an outstanding judgement case must NOT block the retire (#537)"; cat "$WORK/why.txt"; exit 1; }
grep -q "settled=true" "$WORK/out.txt" || { echo "FAIL: settled!=true with only judgement cases"; exit 1; }
grep -q "residual=1"   "$WORK/out.txt" || { echo "FAIL: the residual count is not reported"; exit 1; }
grep -q "fs-gg-persistence" "$WORK/note.md" || { echo "FAIL: the retire note does not NAME the outstanding judgement case"; cat "$WORK/note.md"; exit 1; }

# (c) THE CRASH. Unparseable findings — the check died. Its exit code (1) is indistinguishable from
# "found something", so the payload is the only evidence, and there is none. Never retire.
printf 'Traceback (most recent call last):\n  File "x", line 1\nValueError: boom\n' > "$WORK/f.json"
gate "$WORK/f.json" > "$WORK/out.txt" && { echo "FAIL: a CRASHED check retired a PR — the #425 fail-open"; exit 1; }
grep -q "settled=false" "$WORK/out.txt" || { echo "FAIL: settled!=false on a crash"; exit 1; }
grep -qi "crashed" "$WORK/why.txt" || { echo "FAIL: the crash was not NAMED as the reason"; cat "$WORK/why.txt"; exit 1; }
# An absent file is the same crash, one step earlier (the redirect never produced anything).
rm -f "$WORK/f.json"
gate "$WORK/f.json" > "$WORK/out.txt" && { echo "FAIL: a missing findings file retired a PR"; exit 1; }

# (d) THE BLINDED CHECK — the trap. `manifest-found` is a residual finding, so "only judgement cases
# remain?" is TRUE for it. But a producer clone that could not be read means those rows were never
# checked, `--write` had nothing to act on, and the empty diff it produced proves nothing at all.
cat > "$WORK/f.json" <<'JSON'
{"findings": [
  {"check": "manifest-found", "id": "Producer.One", "detail": "manifest is unreadable"}
]}
JSON
gate "$WORK/f.json" > "$WORK/out.txt" && { echo "FAIL: a BLINDED check retired a PR — the fail-open in a finding's clothes"; exit 1; }
grep -q "settled=false" "$WORK/out.txt" || { echo "FAIL: settled!=false when a producer manifest could not be read"; exit 1; }
grep -qi "blinded" "$WORK/why.txt" || { echo "FAIL: a blinded check was not named as such"; cat "$WORK/why.txt"; exit 1; }

# (e) THE WRITE DID NOT TAKE. A mechanical finding is still standing after a reconcile that produced no
# diff — `--write` claims it fixes these, so its absence from the diff means it silently failed. The
# registry is not in the state the standing PR would leave it in, so that PR is not obsolete.
cat > "$WORK/f.json" <<'JSON'
{"findings": [
  {"check": "digest-matches", "id": "fs-gg-elmish", "detail": "declared X but source hashes to Y"}
]}
JSON
gate "$WORK/f.json" > "$WORK/out.txt" && { echo "FAIL: retired while a MECHANICAL finding was still standing"; exit 1; }
grep -qi "did not take" "$WORK/why.txt" || { echo "FAIL: a failed write was not named as the reason"; cat "$WORK/why.txt"; exit 1; }

# (f) A judgement case NEXT TO a mechanical one still refuses — (b) must not be a blanket amnesty.
cat > "$WORK/f.json" <<'JSON'
{"findings": [
  {"check": "frozen-mirror",  "id": "fs-gg-persistence", "detail": "mirror diverged"},
  {"check": "digest-matches", "id": "fs-gg-elmish",      "detail": "stale digest"}
]}
JSON
gate "$WORK/f.json" > "$WORK/out.txt" && { echo "FAIL: a judgement case laundered a co-occurring mechanical finding"; exit 1; }

# (g) "Mechanical" is IMPORTED from the composer's own classify(), never re-encoded here or in the YAML
# (#485: one question, five implementations, agreeing in none). An APPENDABLE declared-completeness (it
# carries a derivable `row`) is one --write acts on -> refuse; a NON-derivable one is a judgement call
# the owner must make -> it cannot block the retire forever, exactly like (b).
cat > "$WORK/f.json" <<'JSON'
{"findings": [
  {"check": "declared-completeness", "id": "newbie", "detail": "row missing", "row": {"id": "newbie"}}
]}
JSON
gate "$WORK/f.json" > "$WORK/out.txt" && { echo "FAIL: an APPENDABLE row is mechanical — --write adds it — so it must refuse"; exit 1; }
cat > "$WORK/f.json" <<'JSON'
{"findings": [
  {"check": "declared-completeness", "id": "orphan", "detail": "cannot append it: no derivable source"}
]}
JSON
gate "$WORK/f.json" > "$WORK/out.txt" || { echo "FAIL: a NON-derivable row is a judgement case and must not block the retire"; cat "$WORK/why.txt"; exit 1; }
echo "   ok"

# ==================================================================================================
# THE BASELINE VERDICT (#422) — a PR is judged on what IT did, not on whether the world is perfect.
# ==================================================================================================
# The findings fall into two classes, and `--write` already knows the difference: MECHANICAL ones it
# reconciles (a stale sha256, a diverged predicate) and JUDGEMENT ones it refuses (a vanished source; a
# frozen mirror the canonical has moved past; two producers contradicting each other, #295). A judgement
# case CANNOT be fixed from this repo — the mirror lives in another one — so once it exists it stands
# until a human lands a multi-repo change.
#
# With one all-or-nothing verdict, that standing red sat on the autofix bot's OWN PR and failed it for
# the findings the bot had correctly refused to touch. The bot ran daily, reconciled the digests, opened
# its PR — and the PR could never be green. Eight stale digests accumulated behind it while #595 sat open
# and permanently red: a daily bot that runs, succeeds, and achieves nothing (epic #416), inside the very
# gate meant to catch that.
write_registry
write_manifests
printf 'owned body DRIFTED\n' > "$ROOT/Mirror.Repo/template/product-skills/owned/SKILL.md"   # the judgement case
cp "$REG" "$WORK/base.yml"          # the merge base: a stale digest AND a diverged mirror

echo "== 33. WITHOUT a baseline the verdict is ABSOLUTE — main and the schedule stay red =="
run --registry "$REG" --repos-root "$ROOT" >/dev/null 2>&1 && { echo "FAIL: an absolute run must exit 1"; exit 1; }
out="$(run --registry "$REG" --repos-root "$ROOT" || true)"
grep -q "\[frozen-mirror\] owned" <<<"$out" || { echo "FAIL: the standing mirror divergence must stay visible"; echo "$out"; exit 1; }
echo "   ok"

echo "== 34. the bot's PR: it fixed what it could, and the judgement case does NOT fail it =="
# Exactly the autofix's move: --write reconciles the stale digest and refuses the mirror.
run --registry "$REG" --repos-root "$ROOT" --write >/dev/null 2>&1 || true
grep -q "sha256: $ACTUAL_STALE" "$REG" || { echo "FAIL: --write did not reconcile the stale digest"; exit 1; }
out="$(run --registry "$REG" --repos-root "$ROOT" --baseline-registry "$WORK/base.yml" || true)"
run --registry "$REG" --repos-root "$ROOT" --baseline-registry "$WORK/base.yml" >/dev/null 2>&1 \
  || { echo "FAIL: the bot's PR introduced nothing and must be GREEN — this is the jam #422 names"; echo "$out"; exit 1; }
echo "   ok"

echo "== 35. ...and the pre-existing finding is REPORTED, never hidden =="
grep -q "PRE-EXISTING" <<<"$out" || { echo "FAIL: the inherited finding must be printed"; echo "$out"; exit 1; }
grep -q "\[frozen-mirror\] owned" <<<"$out" || { echo "FAIL: it must name the finding it is not failing on"; echo "$out"; exit 1; }
grep -qi "main" <<<"$out" || { echo "FAIL: it must say main's own run stays RED on it"; echo "$out"; exit 1; }
echo "   ok"

echo "== 36. A PRE-EXISTING FINDING MAY NOT LAUNDER A NEW ONE — the fail-open this must not become =="
# The whole risk of a baseline gate: that it becomes a way to smuggle a fresh break in behind a standing
# one. A NEW finding on a DIFFERENT row must still fail, with the mirror divergence still inherited.
write_registry                       # `stale` is wrong again — but it is wrong on the BASE too
sed -i "s|sha256: $GOOD,|sha256: $WRONG,|" "$REG"   # ...and now `good` is wrong, which the base was NOT
out="$(run --registry "$REG" --repos-root "$ROOT" --baseline-registry "$WORK/base.yml" || true)"
run --registry "$REG" --repos-root "$ROOT" --baseline-registry "$WORK/base.yml" >/dev/null 2>&1 \
  && { echo "FAIL: a NEWLY broken row must fail the PR even behind a standing judgement case"; echo "$out"; exit 1; }
grep -q "introduced by this change" <<<"$out" || { echo "FAIL: the new finding must be named as introduced"; echo "$out"; exit 1; }
grep -q "\[digest-matches\] good" <<<"$out" || { echo "FAIL: the newly-broken row must be the one reported"; echo "$out"; exit 1; }
echo "   ok"

echo "== 37. an UNREADABLE baseline is refused, never treated as an empty one =="
# An empty baseline would mark every finding NEW — which fails closed, and is therefore 'safe'. But it
# would fail every PR for a reason that has nothing to do with the PR, and the operator would learn to
# ignore this gate. Name the cause instead.
out="$(run --registry "$REG" --repos-root "$ROOT" --baseline-registry "$WORK/no-such-file.yml" 2>&1 || true)"
grep -qi "not a file" <<<"$out" || { echo "FAIL: a missing baseline must be named, not silently empty"; echo "$out"; exit 1; }
echo "   ok"

echo "== 38. --json separates introduced from inherited, and exits on the introduced =="
write_registry
out="$(run --registry "$REG" --repos-root "$ROOT" --baseline-registry "$WORK/base.yml" --json || true)"
python3 - "$out" <<'PYJSON'
import json, sys
d = json.loads(sys.argv[1])
inh = {(f["check"], f["id"]) for f in d["inherited"]}
intro = {(f["check"], f["id"]) for f in d["introduced"]}
assert ("frozen-mirror", "owned") in inh, f"mirror divergence should be inherited: {d}"
assert ("digest-matches", "stale") in inh, f"the stale row is stale on the base too: {d}"
assert not intro, f"this registry introduces nothing new: {intro}"
PYJSON
echo "   ok"

echo "== 39. --write and --baseline-registry are REFUSED together — a reconcile is not a verdict =="
# `--write` returns before the baseline logic ever runs, so accepting both would consume the flag and
# silently ignore it. An argument that is ignored is indistinguishable, from the caller's side, from
# one that was honoured (the `repos.sh need_val` rule, and the engine's own parser).
out="$(run --registry "$REG" --repos-root "$ROOT" --write --baseline-registry "$WORK/base.yml" 2>&1 || true)"
grep -qi "mutually exclusive" <<<"$out" || { echo "FAIL: --write + --baseline-registry must be refused, not half-ignored"; echo "$out"; exit 1; }
echo "   ok"

echo "== 40. A BLINDED read VOIDS the baseline — it may not be inherited and forgiven (#425) =="
# `manifest-found` means a producer manifest could not be READ, so that producer's rows were never
# checked on EITHER side. And because it depends only on producer bytes — which both sides share — it
# fires identically on base and HEAD, so a (check, id) comparison would ALWAYS class it "inherited".
# Every PR would then report "introduced NO new incoherence" while the gate was blind to a whole
# producer: a claim with no evidence behind it, which is the #266/#425 fail-open rebuilt INSIDE the
# gate that exists to catch it.
write_registry
write_manifests
cp "$REG" "$WORK/blind-base.yml"
printf 'not json at all\n' > "$ROOT/Producer.Two/template/skill-manifest/skill-manifest.json"   # blind us
out="$(run --registry "$REG" --repos-root "$ROOT" --baseline-registry "$WORK/blind-base.yml" 2>&1 || true)"
run --registry "$REG" --repos-root "$ROOT" --baseline-registry "$WORK/blind-base.yml" >/dev/null 2>&1 \
  && { echo "FAIL: a blinded check must NOT pass just because the blindness is 'pre-existing'"; echo "$out"; exit 1; }
grep -qi "BLINDED" <<<"$out" || { echo "FAIL: it must say the comparison is void, and why"; echo "$out"; exit 1; }
grep -qi "never checked on EITHER side" <<<"$out" || { echo "FAIL: it must name the reason a blinded read cannot be inherited"; echo "$out"; exit 1; }
write_manifests   # restore

echo "   ok"

echo "== 41. the blinding set has ONE definition, and the retire gate imports it (#485) =="
python3 - "$HERE/../../scripts/fsgg-skill-registry-check" "$HERE/../../scripts/skill-registry-retire-gate" <<'PYIMP'
import importlib.util, sys
from importlib.machinery import SourceFileLoader
def load(p, n):
    s = importlib.util.spec_from_file_location(n, p, loader=SourceFileLoader(n, p))
    m = importlib.util.module_from_spec(s); s.loader.exec_module(m); return m
check = load(sys.argv[1], "check")
gate  = load(sys.argv[2], "gate")
# Object identity cannot hold: the gate loads its OWN instance of the check module. What must hold is
# that the two AGREE, and that the gate does not carry a literal second copy of the set — which is the
# thing #485 is actually about.
assert gate.BLINDING_CHECKS == check.BLINDING_CHECKS, \
    f"the gate and the emitter disagree about what blinds them: {gate.BLINDING_CHECKS} vs {check.BLINDING_CHECKS}"
assert "manifest-found" in check.BLINDING_CHECKS
src = open(sys.argv[2]).read()
assert '{"manifest-found"}' not in src, \
    "the retire gate RE-ENCODES the blinding set — import it from the tool that emits it (#485)"
assert "fsgg-skill-registry-check" in src, \
    "the retire gate must import the blinding set from fsgg-skill-registry-check (#485)"
PYIMP
echo "   ok"

echo "== 42. a REWRITTEN-but-still-wrong digest is NEW, not inherited — the launder this must not allow =="
# Keyed on (check, id) alone, a PR could change an already-stale digest to a DIFFERENT wrong value and
# be waved through: the row was broken before and is broken now, so the key matches. But that PR made a
# real, wrong edit to skills.yml, and this gate is what is supposed to catch it. The DECLARED value is
# part of the identity.
write_registry
write_manifests
cp "$REG" "$WORK/launder-base.yml"                    # base: `stale` declares $WRONG
OTHER_WRONG="c0ffee00000000000000000000000000000000000000000000000000000000ff"
sed -i "s|sha256: $WRONG,|sha256: $OTHER_WRONG,|" "$REG"   # PR: still wrong, but a DIFFERENT wrong
out="$(run --registry "$REG" --repos-root "$ROOT" --baseline-registry "$WORK/launder-base.yml" || true)"
run --registry "$REG" --repos-root "$ROOT" --baseline-registry "$WORK/launder-base.yml" >/dev/null 2>&1 \
  && { echo "FAIL: rewriting a stale digest to another wrong value must NOT inherit the old finding"; echo "$out"; exit 1; }
grep -q "introduced by this change" <<<"$out" || { echo "FAIL: the rewritten row must be reported as introduced"; echo "$out"; exit 1; }
echo "   ok"

echo "== 43. the merge gate: an absent check is not a passing one, and a NON-required red still refuses =="
# A green PR that nobody merges lands nothing (#642). #640 fixed the reason the standing PR could never
# go green; #595 then went green and SAT there for days while `main` stayed red on the eight stale
# digests it had already fixed. This gate lands it — and, like the retire gate, it is a SCRIPT driven
# over its whole truth table here, because a merge is the other branch in this fabric that DESTROYS
# state: it lands a commit on `main`.
#
# THE TWO LEGS THAT ARE THE WHOLE POINT — both are cases GitHub's NATIVE auto-merge gets WRONG, which
# is why #642's "just copy the propagate arm and pass --auto" is not the fix it looks like:
#
#   * (d) a RED `registry-coherence`. It is NOT a required check on `main` (required: contract-coherence
#     / coherence, projection, roster-closure, drift, reconcile) and it must not become one — its
#     verdict is a function of OTHER repos' mains, so requiring it would let a producer's merge deadlock
#     every open `.github` PR (#549). Native auto-merge waits for REQUIRED checks and merges straight
#     past a red non-required one — i.e. it would land a snapshot whose own coherence check says it is
#     OBSOLETE, writing superseded digests over the registry and REDing the gate it was opened to green.
#     That is #425's inversion, automated.
#
#   * (c) an ABSENT `registry-coherence`. A renamed job or a narrowed `paths:` filter, and the subject
#     simply never reports — at which point "are all checks green?" is TRUE and vacuous. An absent
#     subject reads exactly like a passing one (#606, #266), so it is asserted BY NAME.
MGATE="$HERE/../../scripts/skill-registry-merge-gate"
# `${2-CLEAN}`, not `${2:-CLEAN}`: leg (g) passes an EXPLICITLY EMPTY merge state — the shape a failed
# `gh pr view` leaves behind — and `:-` substitutes CLEAN for an empty argument just as it does for an
# absent one. That would have tested the happy path while LOOKING like it tested the refusal.
mgate() { python3 "$MGATE" --checks "$1" --merge-state "${2-CLEAN}" 2>"$WORK/mwhy.txt"; }
runs() { python3 -c 'import json,sys; print(json.dumps([{"check_runs": json.loads(sys.argv[1])}]))' "$1"; }

# (a) every check green, including the subject, and the PR is CLEAN. Merge.
runs '[{"name":"registry-coherence","status":"completed","conclusion":"success"},
       {"name":"drift","status":"completed","conclusion":"success"},
       {"name":"fixture","status":"completed","conclusion":"skipped"}]' > "$WORK/c.json"
mgate "$WORK/c.json" > "$WORK/mout.txt" || { echo "FAIL: an all-green PR must merge"; cat "$WORK/mwhy.txt"; exit 1; }
grep -q "merge=true" "$WORK/mout.txt" || { echo "FAIL: merge!=true on an all-green PR"; exit 1; }
grep -q "checks=3"  "$WORK/mout.txt" || { echo "FAIL: the check count is not reported"; exit 1; }

# (b) ZERO CHECK RUNS. "Everything passed" and "CI never started" are the SAME EMPTY SET, and only one
# of them is safe (#606). The commonest cause is a CONFLICTED PR: GitHub cannot build refs/pull/N/merge,
# so no workflow is ever scheduled and the head SHA has zero check runs forever. Never a pass.
echo '[]' > "$WORK/c.json"
mgate "$WORK/c.json" > "$WORK/mout.txt" && { echo "FAIL: a PR with ZERO checks was merged — the #606 fail-open"; exit 1; }
grep -q "merge=false" "$WORK/mout.txt" || { echo "FAIL: merge!=false on zero checks"; exit 1; }
grep -qi "zero check runs" "$WORK/mwhy.txt" || { echo "FAIL: an empty check list was not NAMED as the reason"; cat "$WORK/mwhy.txt"; exit 1; }
runs '[]' > "$WORK/c.json"      # the same emptiness, one page-shape in
mgate "$WORK/c.json" > "$WORK/mout.txt" && { echo "FAIL: an empty check_runs page was merged"; exit 1; }

# (c) THE SUBJECT IS ABSENT. Everything that DID report is green — so a naive aggregate says "merge".
mgate <(runs '[{"name":"drift","status":"completed","conclusion":"success"},
               {"name":"projection","status":"completed","conclusion":"success"}]') CLEAN > "$WORK/mout.txt" \
  && { echo "FAIL: merged with registry-coherence ABSENT — the reconcile was never verified"; exit 1; }
grep -qi "did not report" "$WORK/mwhy.txt" || { echo "FAIL: the absent subject was not named"; cat "$WORK/mwhy.txt"; exit 1; }

# (d) THE SUBJECT IS RED — and it is NOT a required check, so native auto-merge would have merged it.
runs '[{"name":"registry-coherence","status":"completed","conclusion":"failure"},
       {"name":"drift","status":"completed","conclusion":"success"}]' > "$WORK/c.json"
mgate "$WORK/c.json" > "$WORK/mout.txt" && { echo "FAIL: merged past a RED registry-coherence — #425's inversion, automated"; exit 1; }
grep -q "registry-coherence=failure" "$WORK/mwhy.txt" || { echo "FAIL: the red check was not named"; cat "$WORK/mwhy.txt"; exit 1; }

# (e) STILL RUNNING. A pending check has not passed. Refuse — the next run re-pushes and re-gates.
runs '[{"name":"registry-coherence","status":"in_progress","conclusion":null},
       {"name":"drift","status":"completed","conclusion":"success"}]' > "$WORK/c.json"
mgate "$WORK/c.json" > "$WORK/mout.txt" && { echo "FAIL: merged while a check was still running"; exit 1; }
grep -qi "not completed" "$WORK/mwhy.txt" || { echo "FAIL: the pending check was not named"; cat "$WORK/mwhy.txt"; exit 1; }

# (f) A completed run with a NULL conclusion is not green either — it is the shape a cancelled or
# still-settling run takes, and `(.conclusion or "") in GREEN` is the only test that catches it.
runs '[{"name":"registry-coherence","status":"completed","conclusion":null}]' > "$WORK/c.json"
mgate "$WORK/c.json" > "$WORK/mout.txt" && { echo "FAIL: a null conclusion was treated as green"; exit 1; }

# (g) EVERY CHECK GREEN, but GitHub says the PR is not mergeable. Asked of the PR, never of branch
# protection — the protection endpoint needs `administration: read`, and a 403 there is neither a
# verdict nor an absence of one (#463). UNSTABLE is the sharp one: mergeable, but something is red.
runs '[{"name":"registry-coherence","status":"completed","conclusion":"success"}]' > "$WORK/c.json"
for bad in BLOCKED UNSTABLE DIRTY BEHIND UNKNOWN ""; do
  mgate "$WORK/c.json" "$bad" > "$WORK/mout.txt" \
    && { echo "FAIL: merged a PR whose mergeStateStatus is '$bad'"; exit 1; }
done
mgate "$WORK/c.json" HAS_HOOKS > /dev/null || { echo "FAIL: HAS_HOOKS is mergeable and must not refuse"; cat "$WORK/mwhy.txt"; exit 1; }

# (h) THE READ FAILED. An unparseable payload means the checks were NOT read — an absence of evidence,
# not an absence of red checks. Same discriminator as the retire gate: the payload, never an exit code.
printf 'gh: could not read check runs\n' > "$WORK/c.json"
mgate "$WORK/c.json" > "$WORK/mout.txt" && { echo "FAIL: merged on an unparseable check payload"; exit 1; }
grep -qi "did not parse" "$WORK/mwhy.txt" || { echo "FAIL: the failed read was not named"; cat "$WORK/mwhy.txt"; exit 1; }
rm -f "$WORK/c.json"            # an absent file is the same failure, one step earlier
mgate "$WORK/c.json" > "$WORK/mout.txt" && { echo "FAIL: merged with NO check payload at all"; exit 1; }
echo "   ok"

# ─────────────────────────────────────────────────────────────────────────────────────────────────
# Cases 44-51: THE MIRROR VERDICT (.github#658, epic #266).
#
# There were THREE readings of "which skills are frozen mirrors" — FS.GG.Game's manifest (a declared
# `mirrored` boolean, Game#280), FS.GG.Rendering's `check-frozen-mirrors.fsx` (derived from this
# registry's `owner:`), and its `check-skill-refs.sh` (a hardcoded list) — and NOTHING asserted they
# agree. They agreed on four. Nothing MADE them, and the drift is fail-open in all three: a body that
# becomes a mirror and is declared nowhere reads as "not mirrored" everywhere, so bare `[[refs]]` in
# it stay legal and it dangles in the mirror with both repos' gates green (Game#273 / Rendering#714).
#
# `.github` is the only reader that sees every producer tree, so the readings can only be held
# together here. These cases pin the two ways that could still have failed open.
restore_mirror() {
  write_registry
  write_manifests
  printf 'owned body\n' > "$ROOT/Producer.Two/skills/owned/SKILL.md"
  mkdir -p "$ROOT/Mirror.Repo/template/product-skills/owned"
  printf 'owned body\n' > "$ROOT/Mirror.Repo/template/product-skills/owned/SKILL.md"
}

echo "== 44. A TWIN NOBODY CLASSIFIED IS RED — undeclared is not \`not mirrored\` (#658) =="
# THE headline fail-open. Strip the verdict from BOTH readings: the manifest stops declaring it and
# the row stops carrying it. A body still exists TWICE — Producer.Two owns it, Mirror.Repo ships a
# copy — and now nothing anywhere says whether the two are obliged to agree.
#
# The pre-#658 tool was structurally unable to notice: it had no concept of a verdict, so "nobody
# said" and "somebody said no" were the same state, and it simply byte-compared whatever file it
# found. The body nobody classified is, by construction, the body nobody thought about — which is
# precisely the body somebody just mirrored.
restore_mirror
write_two_owned_mirror omit
sed -i 's/\(id: owned,.*\) mirrored: true,/\1/' "$REG"
grep -q 'id: owned.*mirrored' "$REG" && { echo "FAIL: fixture could not strip the verdict"; exit 1; }
out="$(run --registry "$REG" --repos-root "$ROOT" || true)"
grep -q "\[mirror-verdict\] owned" <<<"$out" || { echo "FAIL: an unclassified twin reported green — THE #658 fail-open"; echo "$out"; exit 1; }
grep -q "UNDECLARED IS NOT" <<<"$out" || { echo "FAIL: the finding does not name the defaulting bug"; echo "$out"; exit 1; }
# ...and `--write` must NOT resolve it. Classifying a body nobody classified is the guess this whole
# check exists to refuse — the tool would be inventing the very default it is here to reject.
run --registry "$REG" --repos-root "$ROOT" --write >/dev/null 2>&1 && { echo "FAIL: --write must exit 1 while a twin is unclassified"; exit 1; }
grep -q 'id: owned.*mirrored' "$REG" && { echo "FAIL: --write GUESSED a mirror verdict"; grep 'id: owned' "$REG"; exit 1; }
echo "   ok"

echo "== 45. a body with NO twin needs no verdict — the check must not become noise =="
# The converse, and it is what keeps case 44 meaningful. `good`/`stale` have no copy in Mirror.Repo,
# so there is no question to answer and demanding `mirrored: false` on them would be pure ceremony —
# the kind that teaches people to type `false` without reading. Silence is only red where a twin makes
# it load-bearing.
restore_mirror
out="$(run --registry "$REG" --repos-root "$ROOT" || true)"   # exits 1 on the `stale` digest
grep -q "\[mirror-verdict\] good"  <<<"$out" && { echo "FAIL: demanded a verdict from a body with no twin"; echo "$out"; exit 1; }
grep -q "\[mirror-verdict\] stale" <<<"$out" && { echo "FAIL: demanded a verdict from a body with no twin"; echo "$out"; exit 1; }
echo "   ok"

echo "== 46. THE PRE-#280 MANIFEST: a silent manifest may not un-say what the registry knows =="
# `select(.mirrored == true)` reads a row with NO `mirrored` field as false, because `null == true` is
# false. So a manifest that predates the field — a stale artifact, a bad merge, a hand-edit — answers
# "not mirrored" for EVERY body, confidently, and every real mirror goes unguarded. That nearly
# shipped in FS.GG.Game#282.
#
# Here the manifest goes silent while the registry still declares the obligation. The tempting read is
# "the registry drifted, reconcile it down to false". It is the opposite: the PRODUCER regressed, and
# `--write` reconciling the silence would destroy the last copy of the verdict.
restore_mirror
write_two_owned_mirror omit
out="$(run --registry "$REG" --repos-root "$ROOT" || true)"
grep -q "\[mirror-verdict\] owned" <<<"$out" || { echo "FAIL: a manifest that went silent was believed"; echo "$out"; exit 1; }
grep -q "does not SAY does not KNOW" <<<"$out" || { echo "FAIL: the finding does not name the trap"; echo "$out"; exit 1; }
run --registry "$REG" --repos-root "$ROOT" --write >/dev/null 2>&1 && { echo "FAIL: --write must exit 1 on a silent manifest"; exit 1; }
grep -q 'id: owned.*mirrored: true' "$REG" || { echo "FAIL: --write DROPPED the registry's verdict to match a silence"; grep 'id: owned' "$REG"; exit 1; }
echo "   ok"

echo "== 47. a FLIPPED verdict is a pure value rewrite — --write reconciles it (the P6 retirement) =="
# The one mirror finding that IS mechanical: the owner states a verdict, the registry disagrees, and
# the manifest's word is law (registry = manifest = bytes). This is the shape the ADR-0022 P6 mirror
# retirement will arrive in, so the bot must be able to land it unattended.
restore_mirror
out="$(run --registry "$REG" --repos-root "$ROOT" || true)"
# Clear the deliberately-stale `stale` digest first (as cases 2 and 18 do), so the verdict is the
# registry's ONLY defect and the byte-preservation assertion below means what it says.
run --registry "$REG" --repos-root "$ROOT" --write >/dev/null
write_two_owned_mirror false
out="$(run --registry "$REG" --repos-root "$ROOT" || true)"
grep -q "\[mirror-matches\] owned" <<<"$out" || { echo "FAIL: a flipped verdict not reported"; echo "$out"; exit 1; }
cp "$REG" "$WORK/before-mirror.yml"
run --registry "$REG" --repos-root "$ROOT" --write >/dev/null
grep -q 'id: owned.*mirrored: false' "$REG" || { echo "FAIL: --write did not reconcile the verdict"; grep 'id: owned' "$REG"; exit 1; }
# ONE value, nothing else: the hand-aligned YAML is the file's whole readability budget.
[ "$(diff "$WORK/before-mirror.yml" "$REG" | grep -c '^[<>]')" -eq 2 ] \
  || { echo "FAIL: --write touched more than the one verdict"; diff "$WORK/before-mirror.yml" "$REG"; exit 1; }
run --registry "$REG" --repos-root "$ROOT" >/dev/null || { echo "FAIL: not coherent after --write"; exit 1; }
echo "   ok"

echo "== 48. --write APPENDS a new row carrying the manifest's verdict, and NEVER defaults it =="
# A new row is exactly where the undeclared-mirror fail-open would enter. An appended row takes the
# verdict verbatim when the manifest states one, and arrives with NO `mirrored:` field when it does
# not — an absent field is the honest encoding of "not classified", and writing `false` there would be
# the tool inventing the default it exists to refuse.
restore_mirror
mkdir -p "$ROOT/Producer.Two/skills/fresh" "$ROOT/Producer.Two/skills/unsaid"
printf 'fresh body\n'  > "$ROOT/Producer.Two/skills/fresh/SKILL.md"
printf 'unsaid body\n' > "$ROOT/Producer.Two/skills/unsaid/SKILL.md"
FRESH="$(sha "$ROOT/Producer.Two/skills/fresh/SKILL.md")"
UNSAID="$(sha "$ROOT/Producer.Two/skills/unsaid/SKILL.md")"
write_manifests "" '{ "id": "fresh", "scope": "product", "sha256": "'"$FRESH"'", "mirrored": true, "supplied-by": "skills/fresh/", "materializes-when": "always" },
  { "id": "unsaid", "scope": "product", "sha256": "'"$UNSAID"'", "supplied-by": "skills/unsaid/", "materializes-when": "always" }'
# `fresh` declares an obligation, so Mirror.Repo must actually hold the copy — otherwise the appended
# row is correctly red for a DIFFERENT reason (case 5), and this case would pass by accident.
mkdir -p "$ROOT/Mirror.Repo/template/product-skills/fresh"
printf 'fresh body\n' > "$ROOT/Mirror.Repo/template/product-skills/fresh/SKILL.md"
run --registry "$REG" --repos-root "$ROOT" --write >/dev/null || { echo "FAIL: --write should append both rows"; cat "$REG"; exit 1; }
grep "id: fresh," "$REG"  | grep -q "mirrored: true,"  || { echo "FAIL: appended row lost the manifest's verdict"; grep "id: fresh" "$REG"; exit 1; }
grep "id: unsaid," "$REG" | grep -q "mirrored"         && { echo "FAIL: --write INVENTED a verdict for an unclassified skill"; grep "id: unsaid" "$REG"; exit 1; }
run --registry "$REG" --repos-root "$ROOT" >/dev/null || { echo "FAIL: not coherent after --write appended"; exit 1; }
rm -rf "$ROOT/Producer.Two/skills/fresh" "$ROOT/Producer.Two/skills/unsaid" "$ROOT/Mirror.Repo/template/product-skills/fresh"
echo "   ok"

echo "== 49. the verdict, not the FILESYSTEM, decides — a divergent twin declared \`false\` is not a mirror =="
# THE REGRESSION THE OLD DERIVATION WOULD HAVE CAUSED, and the reason `owner:` had to stop being the
# classifier. The pre-#658 tool asked "is this owned by fs-gg-game AND does Rendering hold a file by
# this name?" — the NAME-COLLISION test FS.GG.Game's generator explicitly warns against, and the one
# FS.GG.Rendering#541 got wrong (eight mirrors, not four).
#
# It gave the right answer only by the grace of an `owner:` assignment: the four P6 bodies
# (collision/grids/line-drawing/visibility) DO have same-named Rendering counterparts, were rewritten
# against the `.fsi`, and DELIBERATELY diverge — and the registry happens to home them with
# `owner: fs-gg-rendering`. Re-home one to its real owner and the old tool starts demanding
# byte-identity of two bodies nobody promises agree. Driven against the real trees, that is exactly
# what it does.
restore_mirror
printf 'a body that DELIBERATELY diverges from the copy\n' > "$ROOT/Producer.Two/skills/owned/SKILL.md"
DIVERGED="$(sha "$ROOT/Producer.Two/skills/owned/SKILL.md")"
sed -i "s/sha256: $OWNED, mirrored: true,/sha256: $DIVERGED, mirrored: false,/" "$REG"
write_two_owned_mirror false "$DIVERGED"
out="$(run --registry "$REG" --repos-root "$ROOT" || true)"
grep -q "\[frozen-mirror\] owned" <<<"$out" && { echo "FAIL: byte-identity demanded of a body declared NOT mirrored"; echo "$out"; exit 1; }
grep -q "\[mirror-" <<<"$out" && { echo "FAIL: a correctly-declared divergent twin reported"; echo "$out"; exit 1; }
echo "   ok"

echo "== 50. a MALFORMED verdict is UNKNOWN, never \`false\` =="
# `"mirrored": "false"` is a string, and every truthiness test in every language reads it as TRUE
# while `== true` reads it as false. Neither is an answer. It is not classified, and it says so.
restore_mirror
write_two_owned_mirror '"true"'
out="$(run --registry "$REG" --repos-root "$ROOT" 2>&1 || true)"
grep -q "\[mirror-verdict\] owned" <<<"$out" || { echo "FAIL: a non-boolean manifest verdict was believed"; echo "$out"; exit 1; }
grep -q "Traceback" <<<"$out" && { echo "FAIL: crashed instead of reporting"; echo "$out"; exit 1; }
# ...and the same in the REGISTRY, which is the half a hand-edit reaches. `"true"` and not `yes`:
# YAML 1.1 parses a bare `yes` as the BOOLEAN true, so it is a real verdict, not a malformed one — a
# quoted scalar is the shape a hand-edit actually leaves behind.
restore_mirror
sed -i 's/\(id: owned,.*\) mirrored: true,/\1 mirrored: "true",/' "$REG"
out="$(run --registry "$REG" --repos-root "$ROOT" 2>&1 || true)"
grep -q "\[mirror-verdict\] owned" <<<"$out" || { echo "FAIL: a non-boolean registry verdict was believed"; echo "$out"; exit 1; }
grep -q "Traceback" <<<"$out" && { echo "FAIL: crashed instead of reporting"; echo "$out"; exit 1; }
echo "   ok"

echo "== 51. the mirror family splits along RECONCILABLE vs JUDGEMENT — the bot may not claim a fix =="
# `skill-registry-autofix-body.classify` decides what the standing PR reports as "reconciled" and what
# it reports as "still needs a human", and the retire gate reads the same split. A judgement case filed
# under "what I fixed" is the bot asserting work it did not do (#425) — on the very finding this gate
# exists to raise. So the split is pinned, structurally, at the definition.
python3 - <<'PY' || exit 1
import importlib.util, pathlib, sys
here = pathlib.Path(__file__).parent if "__file__" in dir() else pathlib.Path(".")
root = pathlib.Path("tests/skill-registry").resolve().parent.parent
spec = importlib.util.spec_from_loader("body", loader=None)
mod = type(sys)("body")
src = (root / "scripts/skill-registry-autofix-body").read_text()
exec(compile(src, "skill-registry-autofix-body", "exec"), mod.__dict__)

assert "mirror-matches" in mod.RECONCILED_CHECKS, \
    "a flipped verdict is a pure value rewrite — the bot must be able to land it unattended"
assert "mirror-verdict" not in mod.RECONCILED_CHECKS, \
    "an unclassified twin is a JUDGEMENT case: listing it as 'reconciled' would have the bot report " \
    "a fix for the one finding no reconcile can ever make"

reconciled, appended, residual = mod.classify([
    {"id": "a", "check": "mirror-matches", "actual": False},
    {"id": "b", "check": "mirror-verdict", "detail": "nobody classified a twin"},
])
assert [f["id"] for f in reconciled] == ["a"], reconciled
assert [f["id"] for f in residual] == ["b"], residual
print("   (mirror-matches reconciles; mirror-verdict needs a human)")
PY
echo "   ok"

echo "== 52. ONE manifest declaring an id TWICE with OPPOSITE verdicts is a contradiction, not a vote =="
# A real bug this fixture caught in its own author's first draft. The verdicts were collected into a
# dict keyed by REPO — so two entries from the SAME manifest overwrote each other, and the tool
# resolved a flat contradiction by silently taking whichever row came LAST. Six cases passed while it
# did, because the shape only arises when one manifest declares an id twice.
#
# FS.GG.Game's generator makes this impossible to EMIT ("a contradiction in the catalog resolving
# itself, quietly, in a file whose entire job since #280 is to state the verdict out loud"). But this
# reader consumes EVERY producer's manifest, including ones with no such guard, so the collapse had to
# be impossible to READ as well. Verdicts are now a set over ENTRIES, as `predicates()` already did.
restore_mirror
cat > "$ROOT/Producer.Two/template/skill-manifest/skill-manifest.json" <<JSON
{ "schemaVersion": 1, "skills": [
  { "id": "owned", "scope": "product", "sha256": "$OWNED", "mirrored": true,  "supplied-by": "skills/owned/", "materializes-when": "profile in [game]" },
  { "id": "owned", "scope": "product", "sha256": "$OWNED", "mirrored": false, "supplied-by": "skills/owned/", "materializes-when": "profile in [game]" }
] }
JSON
out="$(run --registry "$REG" --repos-root "$ROOT" || true)"
grep -q "\[mirror-verdict\] owned" <<<"$out" || { echo "FAIL: a duplicated id with OPPOSITE verdicts silently resolved itself"; echo "$out"; exit 1; }
grep -q "disagree on \`mirrored\`" <<<"$out" || { echo "FAIL: the contradiction is not named"; echo "$out"; exit 1; }
run --registry "$REG" --repos-root "$ROOT" --write >/dev/null 2>&1 && { echo "FAIL: --write must exit 1 while the verdicts contradict"; exit 1; }
grep -q 'id: owned.*mirrored: true' "$REG" || { echo "FAIL: --write picked a side"; grep 'id: owned' "$REG"; exit 1; }
echo "   ok"

echo "== 53. a BOM-only difference is NOT a mirror divergence — the finding must not contradict itself =="
# The frozen-mirror arm compared RAW BYTES while REPORTING canonical digests, which are BOM-free. So a
# copy differing only by a leading BOM was reported as "not byte-identical (d1833a67… vs d1833a67…)" —
# the same digest, twice, offered as proof that the two differ. A finding that contradicts its own
# evidence is one the next reader learns to wave past, which is how a real divergence gets ignored.
# The comparison now runs on the same canonical body every other reader of these files hashes.
restore_mirror
printf '\xef\xbb\xbfowned body\n' > "$ROOT/Mirror.Repo/template/product-skills/owned/SKILL.md"
out="$(run --registry "$REG" --repos-root "$ROOT" || true)"   # exits 1 on the `stale` digest
grep -q "\[frozen-mirror\]" <<<"$out" && { echo "FAIL: a BOM was reported as a mirror divergence"; echo "$out"; exit 1; }
# ...and a REAL divergence still fires, BOM or no BOM.
printf '\xef\xbb\xbfowned body DRIFTED\n' > "$ROOT/Mirror.Repo/template/product-skills/owned/SKILL.md"
out="$(run --registry "$REG" --repos-root "$ROOT" || true)"
grep -q "\[frozen-mirror\] owned" <<<"$out" || { echo "FAIL: a BOM now HIDES a real divergence"; echo "$out"; exit 1; }
echo "   ok"

echo "== 54. a row with NO \`mirrored:\` whose manifest DECLARES one is a JUDGEMENT case, not a claimed fix =="
# `--write` rewrites a VALUE in place, and a row that omits the field has none to rewrite (inserting
# one would re-align the hand-formatted flow map — the rule `predicate-matches` already follows). So
# this needs a human. It must therefore NOT be filed as `mirror-matches`: that name is in the autofix
# bot's RECONCILED_CHECKS, and `classify()` splits on the check NAME alone — so a `mirror-matches` the
# write never touched would appear in the standing PR's "Reconciled (mechanical)" section as a fix the
# bot did not make (#425), inside the gate that exists to raise exactly that.
#
# This is the shape that arrives the day FS.GG.Rendering or SDD starts declaring `mirrored` for rows
# this registry already carries, so it is not a corner: it is the next migration.
restore_mirror
write_two_owned_mirror true
sed -i 's/\(id: owned,.*\) mirrored: true,/\1/' "$REG"
grep -q 'id: owned.*mirrored' "$REG" && { echo "FAIL: fixture could not strip the verdict"; exit 1; }
out="$(run --registry "$REG" --repos-root "$ROOT" || true)"
grep -q "\[mirror-matches\] owned" <<<"$out" && { echo "FAIL: an UNRECONCILABLE finding filed under the bot's 'reconciled' name"; echo "$out"; exit 1; }
grep -q "\[mirror-verdict\] owned" <<<"$out" || { echo "FAIL: the missing field was not reported at all"; echo "$out"; exit 1; }
grep -q "add it by hand" <<<"$out" || { echo "FAIL: did not say why --write cannot fix it"; echo "$out"; exit 1; }
run --registry "$REG" --repos-root "$ROOT" --write >/dev/null 2>&1 && { echo "FAIL: --write must exit 1 while the row cannot be reconciled"; exit 1; }
grep -q 'id: owned.*mirrored' "$REG" && { echo "FAIL: --write inserted a field it should not have"; exit 1; }
# EVERY `mirror-matches` carries an `actual` — RECONCILED_CHECKS is true by CONSTRUCTION, not by
# convention. This is the assertion that keeps case 51's split honest as the code moves.
json="$(run --registry "$REG" --repos-root "$ROOT" --json || true)"
python3 -c "
import json,sys
d = json.loads(sys.argv[1])
bad = [f for f in d['findings'] if f['check'] == 'mirror-matches' and 'actual' not in f]
assert not bad, f'a mirror-matches with no actual would be reported as reconciled: {bad}'
print('   (every mirror-matches is reconcilable)')
" "$json"
echo "   ok"

echo "== 55. the MIRROR HOLDER cannot be obliged to mirror its own canonical =="
# The one mirror branch with no coverage until now — and structurally unreachable from the cases
# above, because the shim renames FROZEN_MIRROR_REPO to Mirror.Repo and no row here is owned by it.
# A row whose owner IS the mirror repo has its `source:` AT the frozen-mirror path, so the "copy" and
# the canonical are the same file: `mirrored: true` on it is not an obligation, it is a tautology, and
# byte-comparing a file with itself would report green forever. Say so instead.
mkdir -p "$ROOT/Mirror.Repo/template/product-skills/selfmirror" "$ROOT/Mirror.Repo/template/skill-manifest"
printf 'self body\n' > "$ROOT/Mirror.Repo/template/product-skills/selfmirror/SKILL.md"
SELF="$(sha "$ROOT/Mirror.Repo/template/product-skills/selfmirror/SKILL.md")"
cat > "$ROOT/Mirror.Repo/template/skill-manifest/skill-manifest.json" <<JSON
{ "schemaVersion": 1, "skills": [
  { "id": "selfmirror", "scope": "product", "sha256": "$SELF", "mirrored": true, "supplied-by": "template/product-skills/selfmirror/", "materializes-when": "always" }
] }
JSON
cat > "$REG" <<YAML
schemaVersion: 1
updated: "2026-07-14"
skills:
  - { id: selfmirror, scope: product, owner: mirror-repo, source: Mirror.Repo/template/product-skills/selfmirror/SKILL.md, sha256: $SELF, mirrored: true, materializes-when: always }
YAML
out="$(run --registry "$REG" --repos-root "$ROOT" || true)"
grep -q "\[mirror-verdict\] selfmirror" <<<"$out" || { echo "FAIL: the mirror repo was allowed to mirror its own canonical"; echo "$out"; exit 1; }
grep -q "own canonical" <<<"$out" || { echo "FAIL: the finding does not name the tautology"; echo "$out"; exit 1; }
grep -q "\[frozen-mirror\] selfmirror" <<<"$out" && { echo "FAIL: byte-compared a file against ITSELF"; echo "$out"; exit 1; }
rm -rf "$ROOT/Mirror.Repo/template/skill-manifest" "$ROOT/Mirror.Repo/template/product-skills/selfmirror"
write_registry
echo "   ok"

echo "skill-registry fixture: all checks passed"
