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
write_registry() {
  cat > "$REG" <<YAML
schemaVersion: 1
updated: "2026-07-08"
skills:
  - { id: good,    scope: process, owner: producer-one, source: Producer.One/skills/good/SKILL.md,    sha256: $GOOD,  materializes-when: always }
  - { id: stale,   scope: process, owner: producer-one, source: Producer.One/skills/stale/SKILL.md,   sha256: $WRONG, materializes-when: always }
  - { id: owned,   scope: product, owner: fs-gg-game,   source: Producer.Two/skills/owned/SKILL.md,   sha256: $OWNED, materializes-when: "profile in [game]" }
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
  { "id": "owned", "scope": "product", "sha256": "$OWNED", "supplied-by": "skills/owned/", "materializes-when": "profile in [game]" }${2:+,}
  ${2:-}
] }
JSON
}
write_manifests

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

echo "== 5. a retired frozen mirror is simply absent, not a finding =="
# NOTE: the run still exits 1 (the `stale` row is deliberately stale), so assert on the
# ABSENCE of the frozen-mirror finding rather than on the overall exit code.
rm "$ROOT/Mirror.Repo/template/product-skills/owned/SKILL.md"
out="$(run --registry "$REG" --repos-root "$ROOT" || true)"
grep -q "\[frozen-mirror\]" <<<"$out" && { echo "FAIL: absent mirror reported as a finding"; exit 1; }
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
  { "id": "owned", "scope": "product", "sha256": "$OWNED", "supplied-by": "skills/owned/", "materializes-when": $1 }
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
sed -i 's|source: Producer.Two/skills/owned/SKILL.md,   sha256: '"$OWNED"', materializes-when: "profile in \[game\]" }|source: Producer.Two/skills/owned/SKILL.md,   sha256: '"$OWNED"' }|' "$REG"
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
# So the retire must additionally require POSITIVE proof of coherence (a read-only check that exits 0).
# This is asserted structurally because the condition lives in YAML, not in the composer — and both a
# broken block scalar and the fail-open above shipped green through the fixture before this case existed.
WF="$HERE/../../.github/workflows/skill-registry-autofix.yml"
python3 - "$WF" <<'PY' || exit 1
import sys, json, yaml
wf = yaml.safe_load(open(sys.argv[1]))          # parses at all — a dedented block scalar fails HERE
steps = {s.get("name"): s for s in wf["jobs"]["autofix"]["steps"] if s.get("name")}

proof = next((n for n in steps if n.startswith("Prove the registry is coherent")), None)
assert proof, "no step establishes positive evidence of coherence"

retire = next((n for n in steps if n.startswith("Retire")), None)
assert retire, "no retire step"
cond = steps[retire]["if"]
assert "steps.coherent.outputs.coherent == 'true'" in cond, \
    f"retire is not gated on proven coherence — an empty diff from a CRASHED reconcile would close a needed PR: {cond}"
assert "steps.diff.outputs.changed == 'false'" in cond, f"retire lost its empty-diff guard: {cond}"

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

print("   (workflow: retire demands proof; stamp is not shell-interpolated; push cannot use github.token)")
PY
echo "   ok"

echo "skill-registry fixture: all checks passed"
