#!/usr/bin/env bash
# Fixture for scripts/fsgg-skill-registry-check — the cross-repo guard on `registry = manifest =
# bytes` (.github#247, ADR-0017). Proves the tool catches the three drift shapes that actually bit
# us, off a temp registry + temp producer checkouts: a stale `sha256` (fs-gg-audio, ADR-0024 step 4),
# a `source:` that no longer exists (a renamed/relocated skill), and a stale predicate.
# Also proves --write reconciles only the stale digest, leaves the hand-aligned YAML otherwise
# byte-identical, and refuses to claim success while a non-digest finding remains.
#
# Cases 9-13 cover the CONVERSE direction (.github#289, epic #266): a skill a producer manifest
# declares but the registry never lists was not a finding, it was NOTHING — four shipped product
# skills were invisible to this gate. They prove `declared-completeness` reports the absent row and
# `--write` appends it; that a producer whose manifest VANISHED fails closed rather than silently
# dropping every skill it declares; that an entry with no `supplied-by` is reported and NOT appended
# (no invented `source:`); that a producer the registry names nowhere is still read; and that a
# non-producer checkout is not mistaken for one.
# Pure-stdlib + PyYAML; no network. Mirrors tests/surface-impact/run.sh in shape.

set -euo pipefail

HERE="$(cd "$(dirname "$0")" && pwd)"
TOOL="$HERE/../../scripts/fsgg-skill-registry-check"

python3 "$HERE/catalog-metadata.py"

WORK="$(mktemp -d "${TMPDIR:-/tmp}/skill-registry-fixture.XXXXXX")"
trap 'rm -rf "$WORK"' EXIT

ROOT="$WORK/repos"
mkdir -p "$ROOT/Producer.One/skills/good" \
         "$ROOT/Producer.One/skills/stale" \
         "$ROOT/Producer.Two/skills/owned"

printf 'good body\n'   > "$ROOT/Producer.One/skills/good/SKILL.md"
printf 'stale body\n'  > "$ROOT/Producer.One/skills/stale/SKILL.md"
printf 'owned body\n'  > "$ROOT/Producer.Two/skills/owned/SKILL.md"

sha() { python3 -c "import hashlib,sys;print(hashlib.sha256(open(sys.argv[1],'rb').read()).hexdigest())" "$1"; }

GOOD="$(sha "$ROOT/Producer.One/skills/good/SKILL.md")"
OWNED="$(sha "$ROOT/Producer.Two/skills/owned/SKILL.md")"
ACTUAL_STALE="$(sha "$ROOT/Producer.One/skills/stale/SKILL.md")"
WRONG="deadbeef00000000000000000000000000000000000000000000000000000000"

# THE SIBLING ROSTER EVERY REGISTRY IN $WORK SHARES (.github#2547). `roster_reachable` reads
# `repos.yml` beside the registry under judgement, and it FAILS CLOSED on a missing or unreadable
# one — a gate whose expected population is unknown would otherwise run over whatever happened to be
# checked out, which is the exact defect that arm exists to close. Every registry this fixture builds
# lives in $WORK, so one file serves them all.
#
# IT DELIBERATELY ROSTERS NO `FS-GG/` ROW. `roster_reachable` quantifies over FS-GG repositories
# only, so an empty FS-GG set makes the arm inert for the forty-odd cases below, whose roots
# (ROOT/DROOT/SROOT/XROOT) hold different producers and could not all satisfy one roster. The arm's
# own firing and clearing are proved by cases 65-67, which build a registry + roster + root of their
# own. Non-FS-GG rows are here rather than an empty `repos:` so this file also pins the FILTER: a
# roster row that is not `FS-GG/…` must not become an expectation.
cat > "$WORK/repos.yml" <<'YAML'
schemaVersion: 1
repos:
  - { id: one, full: Fixture/Producer.One, role: framework }
  - { id: two, full: Fixture/Producer.Two, role: framework }
YAML

REG="$WORK/skills.yml"

# THE DELIVERY-CHANNEL DECLARATION EVERY FIXTURE REGISTRY NEEDS (.github#2545). The
# `delivery-channel` arm asks where each `(owner, scope)` class's BYTES come from, reads
# `<registry-stem>.delivery-channels.yml` beside the registry under judgement, and FAILS CLOSED on a
# missing one — a gate that cannot see the declaration would otherwise pass every class by not
# asking, which is the exact ADR-0063 fail-open it exists to close. So each synthetic catalog below
# carries its own, named after itself, and `write_registry` resets BOTH together: the arm's verdict
# runs in both directions, so a registry reset without a declaration reset would leave the previous
# case's extra class behind as a dead entry. Cases 68-76 exercise the arm's own firing and clearing.
write_channels() {  # write_channels <registry-path>; the `classes:` entries on stdin
  local reg="$1"
  { printf 'schemaVersion: 1\nclasses:\n'; cat; } > "${reg%.yml}.delivery-channels.yml"
}

# add_channel <registry-path> <owner> <scope> — declare one more class on the CURRENT declaration.
# Used by the cases below where `--write` RECONCILES IN a row of a class the registry has never
# carried: the arm then reds until that class has a disposition, which is the arm working. Case 71
# asserts that red directly, so declaring it here is not hiding it.
add_channel() {
  printf '  - { owner: %s, scope: %s, disposition: delivered, kind: package, channel: Fixture.Reconciled, evidence: tests/skill-registry/run.sh }\n' \
    "$2" "$3" >> "${1%.yml}.delivery-channels.yml"
}

write_registry() {
  cat > "$REG" <<YAML
schemaVersion: 1
updated: "2026-07-08"
# The vocabulary the cases below actually evaluate: profile throughout, plus name for the
# quoted-literal predicates of cases 16 and 23. Declared because parameter-vocabulary
# (.github#2547) is a live arm over EVERY case in this file, not only over the two that name it --
# omit name here and case 16's --write exits 1 on the undeclared parameter. (No backticks: this
# heredoc is unquoted, so a backtick would be command substitution.)
parameters: [profile, name]
skills:
  - { id: good,    scope: process, owner: producer-one, source: Producer.One/skills/good/SKILL.md,    sha256: $GOOD,  materializes-when: always }
  - { id: stale,   scope: process, owner: producer-one, source: Producer.One/skills/stale/SKILL.md,   sha256: $WRONG, materializes-when: always }
  - { id: owned,   scope: product, owner: fs-gg-game,   source: Producer.Two/skills/owned/SKILL.md,   sha256: $OWNED, materializes-when: "profile in [game]" }
YAML
  write_channels "$REG" <<'CHANNELS'
  - { owner: producer-one, scope: process, disposition: delivered, kind: in-code, channel: fixture-inline, evidence: tests/skill-registry/run.sh }
  - { owner: fs-gg-game,   scope: product, disposition: delivered, kind: package, channel: Fixture.Game,   evidence: tests/skill-registry/run.sh }
CHANNELS
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

run() { python3 "$TOOL" "$@"; }

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
# A RECONCILE THAT INTRODUCES A CLASS MUST DECLARE ITS CHANNEL FIRST (.github#2545). `newbie` is
# owned by Producer.Two, so appending it creates a `(producer-two, product)` class this registry has
# never carried -- and `--write`s own re-check reds on `delivery-channel` until the declaration
# answers for it. That is the arm working as designed, not the fixture working around it: case 71
# asserts that exact red directly, by doing this append WITHOUT the declaration below.
add_channel "$REG" producer-two product
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

echo "== 13. a producer the registry names NOWHERE is still read; an unrelated checkout is not =="
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
mkdir -p "$ROOT/Unrelated.Repo"
out="$(run --registry "$REG" --repos-root "$ROOT" || true)"
grep -q "\[declared-completeness\] stranger" <<<"$out" || { echo "FAIL: unnamed producer not read"; echo "$out"; exit 1; }
# Unrelated.Repo carries no manifest and is named by no `source:` — it is not a producer, so its
# absence of a manifest must NOT be reported.
grep -q "\[manifest-found\] Unrelated.Repo" <<<"$out" && { echo "FAIL: unrelated checkout mistaken for a producer"; exit 1; }
# `always` is written as a bare token, never quoted — the ADR-0017 default round-trips.
add_channel "$REG" producer-three product
run --registry "$REG" --repos-root "$ROOT" --write >/dev/null || { echo "FAIL: --write should append stranger"; exit 1; }
# Scope the assertion to the APPENDED row: `good`/`stale` already end `materializes-when: always }`,
# so an unscoped grep passes no matter what format_row emitted.
grep "id: stranger" "$REG" | grep -q "materializes-when: always }" \
  || { echo "FAIL: default predicate not emitted bare"; grep "id: stranger" "$REG"; exit 1; }
rm -rf "$ROOT/Producer.Three"
echo "   ok"

echo "== 14. a skill declared by TWO producers is reported, never attributed by sort order =="
write_registry
# The alphabetically first declarer is not necessarily the owner. Guessing would write
# `owner: producer-one` and a source pointing at whichever checkout sorts first.
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
add_channel "$REG" producer-two product
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

echo "== 22. the owning producer's manifest wins over a non-owner's re-declaration =="
write_registry
# `good` is owned by producer-one and declared by Producer.One (always). Producer.Two re-declares it
# with a different predicate. `owner:` says Producer.One's word is law, so the non-owner's
# disagreement must NOT be reported against the row.
write_manifests "" '{ "id": "good", "scope": "process", "sha256": "'"$GOOD"'", "supplied-by": "skills/good/", "materializes-when": "profile in [game]" }'
out="$(run --registry "$REG" --repos-root "$ROOT" || true)"
grep -q "\[predicate-matches\] good" <<<"$out" && { echo "FAIL: a non-owner's predicate overrode the owner's"; echo "$out"; exit 1; }
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
# field list may grow, and a shape-hardcoded sed can silently match nothing.)
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

gate = next((n for n in steps if n.startswith("Is the standing PR landable?")), None)
assert gate, "no step establishes that the standing PR is safe to merge"
assert steps[gate]["id"] == "merge-gate", "the merge's `if` reads steps.merge-gate.outputs.merge"

# THE GATE IS THE TOOL, NOT A COPY OF IT (#737, #724). This bot carried the LAST hand-rolled rollup —
# the #710 copy, which read its own superseded runs as red and refused to merge the PR it had just
# pushed. The rollup has been wrong four times in five copies, and every fix edited a copy, because
# nothing executes a recipe and nothing tested the inline blocks. `landable` is the one implementation
# and the one place a test can hold it, so this step may NAME it and nothing else.
assert "fsgg-coord landable" in steps[gate]["run"], \
    "the merge decision is not delegated to `fsgg-coord landable` — a merge lands a commit on `main`, " \
    "so its truth table may not live in an untestable inline block, and may not be a sixth copy of a " \
    "rollup that has already been wrong four times (#724, #737)"
for endpoint, what in (("actions/runs", "workflow runs"), ("check-runs", "check runs")):
    assert endpoint not in steps[gate]["run"], \
        f"the gate reads {what} itself — that IS the hand-rolled merge gate, re-grown (#724, #737)"

# --require registry-coherence: THE ASSERTION THIS BOT EXISTS FOR, and the reason `--auto` cannot be
# used (see the merge assertions above). `registry-coherence` is not required on `main` and must not
# become one (#549), so NOTHING but this flag will ever look at it — and an ABSENT check reads exactly
# like a passing one to any "is anything red?" rollup (#606). Drop the flag and the bot silently goes
# back to merging snapshots whose subject never reported: green, vacuous, and landing bad digests.
assert "--require registry-coherence" in steps[gate]["run"], \
    "the gate no longer asserts `registry-coherence` BY NAME. It is not a required check, so without " \
    "--require nothing looks at it, and a renamed job or a narrowed `paths:` filter makes 'all checks " \
    "green' true and vacuous — merging a snapshot that was never verified (#606, #425, #737)"

# --sha: the head we MEAN to gate. `steps.pr.outputs.sha` is `git rev-parse HEAD` of the branch this
# run force-pushed, read locally BECAUSE the PR object's head SHA is eventually consistent. Without
# this the gate would score whatever `pulls/{n}` happens to name — routinely the PREVIOUS commit,
# whose checks are green and are about code that would not be merged.
assert "--sha" in steps[gate]["run"], \
    "the gate does not pin the head SHA it is gating. `pulls/{n}` lags a force-push, so `landable` " \
    "would score the PREVIOUS commit's green checks and merge one nothing ever checked (#737)"
assert steps[gate].get("env", {}).get("SHA") == "${{ steps.pr.outputs.sha }}", \
    "the gate's SHA is not the locally-resolved head this run pushed — reading it from the API is the " \
    "very race --sha exists to close (#737)"

# EVERY read the gate makes is scoped by a grant in THIS FILE. `landable` takes ONE token, so the old
# App-token/GITHUB_TOKEN split had to collapse to one; it collapsed onto the read-only one, which is
# the direction the `actions: read` rationale already argued for. An App token here would re-open it:
# the App's permissions are configured outside this repo and cannot be asserted from inside it.
assert steps[gate].get("env", {}).get("GITHUB_TOKEN") == "${{ github.token }}", \
    "the gate does not read under GITHUB_TOKEN — a scope this FILE grants is a scope it can prove (#737)"
assert "app-token" not in str(steps[gate].get("env", {})), \
    "the gate holds an App token. It only READS; the App token is for the merge alone (#514, #737)"
for scope in ("actions: read", "checks: read"):
    assert scope in raw, \
        f"`{scope}` is not granted, but the merge gate's reads need it — GITHUB_TOKEN would 403, " \
        f"`landable` would return `unknown`, and the bot would refuse its own PR forever (#700, #737)"

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
  {"check": "source-exists", "id": "fs-gg-persistence", "detail": "source path does not exist"}
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
  {"check": "source-exists",   "id": "fs-gg-persistence", "detail": "source path does not exist"},
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
# reconciles (a stale sha256, a diverged predicate) and JUDGEMENT ones it refuses (a vanished source or
# two producers contradicting each other, #295). A judgement case cannot be inferred safely, so once it
# exists it stands until a human supplies the missing fact.
#
# With one all-or-nothing verdict, that standing red sat on the autofix bot's OWN PR and failed it for
# the findings the bot had correctly refused to touch. The bot ran daily, reconciled the digests, opened
# its PR — and the PR could never be green. Eight stale digests accumulated behind it while #595 sat open
# and permanently red: a daily bot that runs, succeeds, and achieves nothing (epic #416), inside the very
# gate meant to catch that.
write_registry
write_manifests
mv "$ROOT/Producer.One/skills/good/SKILL.md" "$ROOT/Producer.One/skills/good/RENAMED.md"
cp "$REG" "$WORK/base.yml"          # the merge base: a stale digest AND a vanished source

echo "== 33. WITHOUT a baseline the verdict is ABSOLUTE — main and the schedule stay red =="
run --registry "$REG" --repos-root "$ROOT" >/dev/null 2>&1 && { echo "FAIL: an absolute run must exit 1"; exit 1; }
out="$(run --registry "$REG" --repos-root "$ROOT" || true)"
grep -q "\[source-exists\] good" <<<"$out" || { echo "FAIL: the standing missing source must stay visible"; echo "$out"; exit 1; }
echo "   ok"

echo "== 34. the bot's PR: it fixed what it could, and the judgement case does NOT fail it =="
# Exactly the autofix's move: --write reconciles the stale digest and refuses to invent a source.
run --registry "$REG" --repos-root "$ROOT" --write >/dev/null 2>&1 || true
grep -q "sha256: $ACTUAL_STALE" "$REG" || { echo "FAIL: --write did not reconcile the stale digest"; exit 1; }
out="$(run --registry "$REG" --repos-root "$ROOT" --baseline-registry "$WORK/base.yml" || true)"
run --registry "$REG" --repos-root "$ROOT" --baseline-registry "$WORK/base.yml" >/dev/null 2>&1 \
  || { echo "FAIL: the bot's PR introduced nothing and must be GREEN — this is the jam #422 names"; echo "$out"; exit 1; }
echo "   ok"

echo "== 35. ...and the pre-existing finding is REPORTED, never hidden =="
grep -q "PRE-EXISTING" <<<"$out" || { echo "FAIL: the inherited finding must be printed"; echo "$out"; exit 1; }
grep -q "\[source-exists\] good" <<<"$out" || { echo "FAIL: it must name the finding it is not failing on"; echo "$out"; exit 1; }
grep -qi "main" <<<"$out" || { echo "FAIL: it must say main's own run stays RED on it"; echo "$out"; exit 1; }
echo "   ok"

echo "== 36. A PRE-EXISTING FINDING MAY NOT LAUNDER A NEW ONE — the fail-open this must not become =="
# The whole risk of a baseline gate: that it becomes a way to smuggle a fresh break in behind a standing
# one. A NEW finding on a DIFFERENT row must still fail, with the missing source still inherited.
write_registry                       # `stale` is wrong again — but it is wrong on the BASE too
sed -i "s|sha256: $OWNED,|sha256: $WRONG,|" "$REG"  # ...and now `owned` is wrong, which the base was NOT
out="$(run --registry "$REG" --repos-root "$ROOT" --baseline-registry "$WORK/base.yml" || true)"
run --registry "$REG" --repos-root "$ROOT" --baseline-registry "$WORK/base.yml" >/dev/null 2>&1 \
  && { echo "FAIL: a NEWLY broken row must fail the PR even behind a standing judgement case"; echo "$out"; exit 1; }
grep -q "introduced by this change" <<<"$out" || { echo "FAIL: the new finding must be named as introduced"; echo "$out"; exit 1; }
grep -q "\[digest-matches\] owned" <<<"$out" || { echo "FAIL: the newly-broken row must be the one reported"; echo "$out"; exit 1; }
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
assert ("source-exists", "good") in inh, f"missing source should be inherited: {d}"
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
mv "$ROOT/Producer.One/skills/good/RENAMED.md" "$ROOT/Producer.One/skills/good/SKILL.md"
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

echo "== 43. the merge gate lives in the ENGINE, not here (#737) =="
# THIS CASE USED TO DRIVE `scripts/skill-registry-merge-gate` OVER ITS WHOLE TRUTH TABLE. That script
# is gone: it was the FIFTH copy of a rollup that has been wrong four times (#547, #606, #698, #710,
# #720), and #724 put the logic in one tested place. The bot now calls `fsgg-coord landable` like
# everything else, so the truth table it used to assert here is asserted where the implementation
# actually lives, and cannot drift from it:
#
#   * the scorer, pure                 tests/FS.GG.Coord.Core.Tests/LandableTests.fs
#     (zero checks is RED not GREEN (#606) · a superseded cancelled run is not a failure (#720) ·
#      an ABSENT --require'd check is never green (#737) · a red outranks a missing required one)
#   * the binary, over a fake GitHub   tests/coord-engine-parity/run.sh cases 31-32
#     (--require and --sha, each proven by a world that is GREEN without the flag and PENDING with it)
#
# What still belongs HERE is the WIRING — that this workflow asks that question, asks it about the
# right subject, and merges only on the answer. Nothing runtime catches a rewiring, so it is pinned
# above, in the structural assertions over the workflow YAML.
echo "   ok (the truth table moved to the engine's corpus; the wiring is asserted above)"

# ─────────────────────────────────────────────────────────────────────────────────────────────────
# Mirror-verdict regressions retired with .github#1862 after Rendering removed the second copies.

echo "== 56. supersession is the ENGINE's rule now (#737) =="
# THE BOT REFUSED TO MERGE THE PR IT HAD JUST PUSHED (#700/#710). It force-pushes the branch (a
# `synchronize` event) and then edits the PR body (an `edited` event); both are `pull_request` events, so
# `github.ref` is `refs/pull/N/merge` for BOTH — ONE `cancel-in-progress` group — and the second run
# cancels the first at the SAME head SHA. A gate that classifies by conclusion alone calls those corpses
# RED and refuses. The bot manufactures that state on every reconcile after the first, so it was the
# standing-PR PATH, not an interleaving.
#
# This case drove those rules through `scripts/skill-registry-merge-gate`. That script is gone (#737):
# the bot calls `fsgg-coord landable`, and the rules are asserted against the implementation instead of
# against a copy of it. Including the two loose repairs that FAIL OPEN, which is why this was never
# "latest run wins":
#
#   * a `workflow_dispatch` run shares the SHA and the workflow but not the REF, so it supersedes
#     nothing — `LandableTests`, "a higher run of a DIFFERENT group does not supersede (#703)"
#   * "latest per NAME" hides a red: job names COLLIDE across workflows (seven `fixture`s from six
#     workflows, measured) — `LandableTests`, "two check-runs SHARE a job name and one FAILS"
#   * a cancelled run nobody re-ran is still a finding; a FAILED run is never dropped — `LandableTests`
#   * the headline superseded-but-green standing PR, end to end over a fake GitHub —
#     `tests/coord-engine-parity/run.sh` case 31
echo "   ok (the rules moved to the engine's corpus, where they hold the implementation)"

# Cases 57-60 cover the FULL sweep (.github#299/#1200): `--write` no longer punts the housekeeping to
# the operator. A new row whose (owner, predicate) siblings are already in the file is HOMED beside
# them rather than left in the tail under a re-home note (the .github#1198 toil); and with `--now`,
# `--write` bumps `updated:` and prepends a dated changelog entry — the two steps it used to only
# print a reminder for.

echo "== 57. --write HOMES a new row beside its (owner, predicate) sibling, not in the tail =="
# `newkid` shares owner (producer-two, DERIVED from its producer repo) and predicate with `sib`, which
# is already in the file — so it is inserted right after `sib`, between it and `tail`, never appended.
mkdir -p "$ROOT/Producer.Two/skills/sib" "$ROOT/Producer.Two/skills/newkid" "$ROOT/Producer.One/skills/tail"
printf 'sib body\n'    > "$ROOT/Producer.Two/skills/sib/SKILL.md"
printf 'newkid body\n' > "$ROOT/Producer.Two/skills/newkid/SKILL.md"
printf 'tail body\n'   > "$ROOT/Producer.One/skills/tail/SKILL.md"
SIB="$(sha "$ROOT/Producer.Two/skills/sib/SKILL.md")"
NEWKID="$(sha "$ROOT/Producer.Two/skills/newkid/SKILL.md")"
TAIL="$(sha "$ROOT/Producer.One/skills/tail/SKILL.md")"
cat > "$REG" <<YAML
schemaVersion: 1
updated: "2026-07-08"
parameters: [profile]
skills:
  - { id: good, scope: process, owner: producer-one, source: Producer.One/skills/good/SKILL.md, sha256: $GOOD, materializes-when: always }
  - { id: sib,  scope: product, owner: producer-two, source: Producer.Two/skills/sib/SKILL.md,  sha256: $SIB,  materializes-when: "profile in [game]" }
  - { id: tail, scope: process, owner: producer-one, source: Producer.One/skills/tail/SKILL.md, sha256: $TAIL, materializes-when: always }
YAML
# This case writes $REG directly rather than through write_registry, so it resets the declaration
# directly too — the arm judges the class set of THIS registry, and the previous case's classes
# would otherwise linger as dead entries.
write_channels "$REG" <<'CHANNELS'
  - { owner: producer-one, scope: process, disposition: delivered, kind: in-code, channel: fixture-inline, evidence: tests/skill-registry/run.sh }
  - { owner: producer-two, scope: product, disposition: delivered, kind: package, channel: Fixture.Two,    evidence: tests/skill-registry/run.sh }
CHANNELS
mkdir -p "$ROOT/Producer.One/.agents/skills" "$ROOT/Producer.Two/template/skill-manifest"
cat > "$ROOT/Producer.One/.agents/skills/skill-manifest.json" <<JSON
{ "schemaVersion": 1, "skills": [
  { "id": "good", "scope": "process", "sha256": "$GOOD" },
  { "id": "tail", "scope": "process", "sha256": "$TAIL" }
] }
JSON
cat > "$ROOT/Producer.Two/template/skill-manifest/skill-manifest.json" <<JSON
{ "schemaVersion": 1, "skills": [
  { "id": "sib",    "scope": "product", "sha256": "$SIB",    "supplied-by": "skills/sib/",    "materializes-when": "profile in [game]" },
  { "id": "newkid", "scope": "product", "sha256": "$NEWKID", "supplied-by": "skills/newkid/", "materializes-when": "profile in [game]" }
] }
JSON
run --registry "$REG" --repos-root "$ROOT" --write >/dev/null || { echo "FAIL: --write should home newkid"; cat "$REG"; exit 1; }
grep -q "id: newkid" "$REG" || { echo "FAIL: newkid not added"; cat "$REG"; exit 1; }
# Homed, not appended: no re-home note is emitted, and the row lands BETWEEN sib and tail.
grep -q "APPENDED by" "$REG" && { echo "FAIL: newkid was tail-appended, not homed"; cat "$REG"; exit 1; }
python3 -c "
import yaml,sys
ids=[r['id'] for r in yaml.safe_load(open(sys.argv[1]))['skills']]
assert ids==['good','sib','newkid','tail'], ids
" "$REG" || { echo "FAIL: newkid not homed immediately after its sibling"; grep 'id:' "$REG"; exit 1; }
run --registry "$REG" --repos-root "$ROOT" >/dev/null || { echo "FAIL: not coherent after homing"; exit 1; }
echo "   ok"

echo "== 58. --write --now bumps updated: and prepends a dated changelog entry =="
# A row whose owner has NO sibling in the file (producer-one has no product row here) still falls back
# to the tail append — the tool homes what it can prove and guesses nothing. Reuse the stale-digest
# fixture so --write has a real change to stamp.
write_registry
write_manifests
CL="$WORK/skills.CHANGELOG.md"
cat > "$CL" <<'MD'
# Skill registry changelog

## Entries

<!-- Prepend new entries here, newest first:
- **YYYY-MM-DD** — HEADER (owner; refs): body
-->

- **2026-07-01** — SEED (owner; refs): the first entry.
MD
run --registry "$REG" --repos-root "$ROOT" --write --now 2026-07-19 --changelog "$CL" >/dev/null \
  || { echo "FAIL: --write --now should reconcile the stale digest"; exit 1; }
grep -q 'updated: "2026-07-19"' "$REG" || { echo "FAIL: updated: not bumped"; grep updated "$REG"; exit 1; }
python3 -c "
import sys
t=open(sys.argv[1]).read()
close=t.index('-->'); new=t.index('- **2026-07-19**'); old=t.index('- **2026-07-01**')
assert close < new < old, (close,new,old)   # after the guidance comment, before the older entry
assert 'RECONCILE (auto' in t, 'auto entry text missing'
" "$CL" || { echo "FAIL: changelog entry not prepended in order"; cat "$CL"; exit 1; }
echo "   ok"

echo "== 59. --now is refused without --write, on a bad date, and stamps nothing on a no-op =="
run --registry "$REG" --repos-root "$ROOT" --now 2026-07-19 >/dev/null 2>&1 \
  && { echo "FAIL: --now must be refused without --write"; exit 1; }
run --registry "$REG" --repos-root "$ROOT" --write --now 07-19-2026 >/dev/null 2>&1 \
  && { echo "FAIL: --now must reject a non-ISO date"; exit 1; }
# The registry is coherent after case 58's write, so a second --write --now changes nothing and must
# NOT stamp a no-op reconcile.
out="$(run --registry "$REG" --repos-root "$ROOT" --write --now 2026-07-25 --changelog "$CL")"
grep -q 'nothing to reconcile' <<<"$out" || { echo "FAIL: a no-op --now did not say so"; echo "$out"; exit 1; }
grep -q 'updated: "2026-07-25"' "$REG" && { echo "FAIL: a no-op --now bumped updated:"; grep updated "$REG"; exit 1; }
grep -q '2026-07-25' "$CL" && { echo "FAIL: a no-op --now wrote a changelog entry"; exit 1; }
# Restore the shared fixture for any case added after this one.
write_registry
write_manifests
rm -rf "$ROOT/Producer.Two/skills/sib" "$ROOT/Producer.Two/skills/newkid" "$ROOT/Producer.One/skills/tail"
echo "   ok"

echo "== 60. a .github-authored DRIVER row reconciles from .github's OWN manifest (ADR-0054) =="
# `.github` is a producer of its own `scope: driver` skills. It is discovered by the THIRD
# MANIFEST_CANDIDATE (registry/driver-skill-manifest.json), its owner slug is `.github` (NOT the
# `-github` the FS.GG.* dot->dash rule would give — the owner_of special-case), and its source
# resolves under the producer checkout like any other. Isolated in its own --repos-root.
DROOT="$WORK/driver-repos"
mkdir -p "$DROOT/.github/.claude/skills/work-roadmap" "$DROOT/.github/registry"
printf 'roadmap driver body\n' > "$DROOT/.github/.claude/skills/work-roadmap/SKILL.md"
DRIVER="$(sha "$DROOT/.github/.claude/skills/work-roadmap/SKILL.md")"
cat > "$DROOT/.github/registry/driver-skill-manifest.json" <<JSON
{ "schemaVersion": 1, "skills": [
  { "id": "work-roadmap", "scope": "driver", "sha256": "$DRIVER", "supplied-by": ".claude/skills/work-roadmap", "materializes-when": "feedback == true and lifecycle == spec-kit" }
] }
JSON
DREG="$WORK/driver-skills.yml"
cat > "$DREG" <<YAML
schemaVersion: 2
updated: "2026-07-19"
parameters: [feedback, lifecycle]
skills:
  - { id: work-roadmap, scope: driver, owner: .github, source: .github/.claude/skills/work-roadmap/SKILL.md, sha256: $DRIVER, materializes-when: "feedback == true and lifecycle == spec-kit" }
YAML
write_channels "$DREG" <<'YAML'
  - { owner: .github, scope: driver, disposition: delivered, kind: package, channel: FS.GG.Drivers, evidence: src/FS.GG.Drivers/stage-drivers.py }
YAML
run --registry "$DREG" --repos-root "$DROOT" >/dev/null \
  || { echo "FAIL: a coherent .github driver row was not accepted"; run --registry "$DREG" --repos-root "$DROOT" || true; exit 1; }

# Discovery + digest actually RUN for the driver class — a wrong digest is caught, not silently passed.
sed -i "s|sha256: $DRIVER|sha256: $WRONG|" "$DREG"
out="$(run --registry "$DREG" --repos-root "$DROOT" || true)"
grep -q "\[digest-matches\] work-roadmap" <<<"$out" || { echo "FAIL: a stale driver digest was not caught"; echo "$out"; exit 1; }

# --write APPENDS a NEW driver the manifest declares: owner `.github` (a `-github` here would be the
# dot->dash bug owner_of guards), source derived from `supplied-by`. A second driver is declared so the
# append has a coherent sibling row to home beside (the tool homes relative to existing rows, ADR #857).
mkdir -p "$DROOT/.github/.claude/skills/work-roadmap-two"
printf 'second driver body\n' > "$DROOT/.github/.claude/skills/work-roadmap-two/SKILL.md"
DRIVER2="$(sha "$DROOT/.github/.claude/skills/work-roadmap-two/SKILL.md")"
sed -i "s|sha256: $WRONG|sha256: $DRIVER|" "$DREG"   # restore the anchor row's digest to coherent
cat > "$DROOT/.github/registry/driver-skill-manifest.json" <<JSON
{ "schemaVersion": 1, "skills": [
  { "id": "work-roadmap",    "scope": "driver", "sha256": "$DRIVER",  "supplied-by": ".claude/skills/work-roadmap",    "materializes-when": "feedback == true and lifecycle == spec-kit" },
  { "id": "work-roadmap-two", "scope": "driver", "sha256": "$DRIVER2", "supplied-by": ".claude/skills/work-roadmap-two", "materializes-when": "feedback == true and lifecycle == spec-kit" }
] }
JSON
run --registry "$DREG" --repos-root "$DROOT" --write >/dev/null 2>&1 || true
grep -qF "id: work-roadmap-two," "$DREG" || { echo "FAIL: --write did not append the second driver row"; cat "$DREG"; exit 1; }
grep -F "id: work-roadmap-two," "$DREG" | grep -qF "owner: .github," || { echo "FAIL: appended driver row lacks owner .github"; cat "$DREG"; exit 1; }
grep -qF "owner: -github" "$DREG" && { echo "FAIL: owner_of(.github) produced the -github dot->dash bug"; cat "$DREG"; exit 1; }
grep -qF "source: .github/.claude/skills/work-roadmap-two/SKILL.md" "$DREG" || { echo "FAIL: driver source not derived from supplied-by"; cat "$DREG"; exit 1; }
echo "   ok"

echo "== 61. a producer publishing at its TRACKED SOURCE root is FOUND (.github#1757) =="
# ADR-0067 §6 makes `.agents/skills` a GENERATED VIEW — untracked, git-ignored, and absent in the bare
# `git clone --depth 1 --filter=blob:none` skill-registry-coherence.yml makes of every named producer.
# FS.GG.SDD moved its process manifest to `.claude/skills/skill-manifest.json` for exactly that reason
# (FS-GG/FS.GG.SDD#771). Before this candidate existed, such a producer had NO manifest here at all and
# the fail-closed `manifest-found` finding fired, taking every row it declares out of digest coverage.
#
# ISOLATED --repos-root, and its own registry, so the shared fixture above is untouched.
SROOT="$WORK/source-root-repos"
mkdir -p "$SROOT/Producer.Src/.claude/skills/srcskill" "$SROOT/Producer.Src/skills/srcskill"
printf 'source-root body\n' > "$SROOT/Producer.Src/skills/srcskill/SKILL.md"
SRCSKILL="$(sha "$SROOT/Producer.Src/skills/srcskill/SKILL.md")"
SREG="$WORK/source-root-skills.yml"
cat > "$SREG" <<YAML
schemaVersion: 1
updated: "2026-07-28"
skills:
  - { id: srcskill, scope: process, owner: producer-src, source: Producer.Src/skills/srcskill/SKILL.md, sha256: $SRCSKILL, materializes-when: always }
YAML
write_channels "$SREG" <<'YAML'
  - { owner: producer-src, scope: process, disposition: delivered, kind: in-code, channel: fixture-inline, evidence: tests/skill-registry/run.sh }
YAML

write_src_manifest_at() {
  rm -f "$SROOT/Producer.Src/.claude/skills/skill-manifest.json" \
        "$SROOT/Producer.Src/.agents/skills/skill-manifest.json"
  mkdir -p "$(dirname "$SROOT/Producer.Src/$1")"
  cat > "$SROOT/Producer.Src/$1" <<JSON
{ "schemaVersion": 1, "skills": [
  { "id": "srcskill", "scope": "process", "sha256": "$SRCSKILL" }
] }
JSON
}

# (a) THE SHIPPED SHAPE: the manifest lives ONLY at the tracked source root. This is the leg that is
# RED without the new candidate — `find_manifest` returns None and `manifest-found` fires.
write_src_manifest_at ".claude/skills/skill-manifest.json"
out="$(run --registry "$SREG" --repos-root "$SROOT" || true)"
grep -q "\[manifest-found\] Producer.Src" <<<"$out" && { echo "FAIL: a manifest at the tracked source root was not found"; echo "$out"; exit 1; }
run --registry "$SREG" --repos-root "$SROOT" >/dev/null \
  || { echo "FAIL: a coherent producer publishing at .claude/skills was not accepted"; run --registry "$SREG" --repos-root "$SROOT" || true; exit 1; }

# (b) AND IT IS REALLY BEING READ, not merely tolerated: break the digest in the registry and the
# tool must catch it. Without this, (a) would also pass for a tool that found the manifest and then
# ignored every entry in it.
sed -i "s|sha256: $SRCSKILL|sha256: $WRONG|" "$SREG"
out="$(run --registry "$SREG" --repos-root "$SROOT" || true)"
grep -q "\[digest-matches\] srcskill" <<<"$out" || { echo "FAIL: the source-root manifest was found but its entries were not graded"; echo "$out"; exit 1; }
sed -i "s|sha256: $WRONG|sha256: $SRCSKILL|" "$SREG"

# (c) THE LEGACY SHAPE STILL WORKS: a producer that has not moved yet publishes at the view root and
# is still found. Adding a candidate must not remove one.
write_src_manifest_at ".agents/skills/skill-manifest.json"
run --registry "$SREG" --repos-root "$SROOT" >/dev/null \
  || { echo "FAIL: a producer still publishing at .agents/skills was broken by the new candidate"; run --registry "$SREG" --repos-root "$SROOT" || true; exit 1; }

# (d) THE MUTATION, AND THE FAIL-CLOSED FLOOR: with NEITHER location present the finding must still
# fire. If it did not, (a) would be indistinguishable from a tool that reports success over nothing.
rm -f "$SROOT/Producer.Src/.claude/skills/skill-manifest.json" \
      "$SROOT/Producer.Src/.agents/skills/skill-manifest.json"
out="$(run --registry "$SREG" --repos-root "$SROOT" || true)"
grep -q "\[manifest-found\] Producer.Src" <<<"$out" || { echo "FAIL: a producer with NO manifest anywhere was not reported"; echo "$out"; exit 1; }
grep -q ".claude/skills/skill-manifest.json" <<<"$out" || { echo "FAIL: the finding does not name the new candidate it looked for"; echo "$out"; exit 1; }

# (e) WHICH ONE WINS when a transitional tree carries BOTH — the tracked source, never the view. A
# link-mode `skill-view generate` makes the view root resolve, so "both present" is a real state, and
# grading a repo against whichever file a checkout step happened to produce is the ADR-0067 §8 shape.
# The two manifests are made DISTINGUISHABLE (the view one declares a different id) so the answer is
# observable rather than assumed.
write_src_manifest_at ".claude/skills/skill-manifest.json"
mkdir -p "$SROOT/Producer.Src/.agents/skills"
cat > "$SROOT/Producer.Src/.agents/skills/skill-manifest.json" <<JSON
{ "schemaVersion": 1, "skills": [
  { "id": "stale-view-only", "scope": "process", "sha256": "$SRCSKILL" }
] }
JSON
out="$(run --registry "$SREG" --repos-root "$SROOT" || true)"
grep -q "stale-view-only" <<<"$out" && { echo "FAIL: the VIEW root's manifest won over the tracked source"; echo "$out"; exit 1; }
run --registry "$SREG" --repos-root "$SROOT" >/dev/null \
  || { echo "FAIL: the tracked-source manifest did not win a both-present tree"; run --registry "$SREG" --repos-root "$SROOT" || true; exit 1; }
echo "   ok"

# =================================================================================================
# CHECK 7 — CROSS-REFERENCES (.github#2366). AC-1: "Add the missing-fs-gg-feedback-report case as a
# negative fixture so the gate is shown to fail, not merely to pass." `xref-driver` plays
# `work-roadmap`'s role (materializes-when: always, references a sibling's path in its
# references/**); `xref-product` plays `fs-gg-feedback-report`'s. Isolated in its own --repos-root,
# and its own registry + `.github` driver-skill-manifest so predicates()/completeness() stay
# coherent throughout and the ONLY finding a mutation below produces is the one under test.
# =================================================================================================
XROOT="$WORK/xref-repos"
mkdir -p "$XROOT/.github/.claude/skills/xref-driver/references" \
         "$XROOT/.github/.claude/skills/xref-product" \
         "$XROOT/.github/.claude/skills/xref-inert/references" \
         "$XROOT/.github/registry"
printf 'xref driver body\n'  > "$XROOT/.github/.claude/skills/xref-driver/SKILL.md"
printf 'xref product body\n' > "$XROOT/.github/.claude/skills/xref-product/SKILL.md"
printf 'xref inert body\n'   > "$XROOT/.github/.claude/skills/xref-inert/SKILL.md"
XDRIVER="$(sha "$XROOT/.github/.claude/skills/xref-driver/SKILL.md")"
XPRODUCT="$(sha "$XROOT/.github/.claude/skills/xref-product/SKILL.md")"
XINERT="$(sha "$XROOT/.github/.claude/skills/xref-inert/SKILL.md")"
XREG="$WORK/xref-skills.yml"

# `xref-inert` (materializes-when: "false", mirroring `lane-steward`) always references an
# UNREGISTERED sibling — this is the vacuous/unsatisfiable-referencer case (case 64 below). Its own
# predicate can never hold, so it must NEVER be flagged, in every state this section exercises.
cat > "$XROOT/.github/.claude/skills/xref-inert/references/notes.md" <<'EOF'
See .agents/skills/xref-nonexistent/HELPME.md for context that never applies.
EOF

# `write_xref_registry <xref-product when>` — the registry AND its manifest declare the SAME
# predicate for xref-product, so mutating it never also trips `predicate-matches`; the only check
# that can fire from these mutations is `cross-references`.
write_xref_registry() {
  local product_when="$1"
  cat > "$XREG" <<YAML
schemaVersion: 3
updated: "2026-08-11"
parameters: [profile, lifecycle, feedback, designSystem]
skills:
  - { id: xref-driver,  scope: driver,   owner: .github, source: .github/.claude/skills/xref-driver/SKILL.md,  sha256: $XDRIVER,  materializes-when: always }
  - { id: xref-product, scope: product,  owner: .github, source: .github/.claude/skills/xref-product/SKILL.md, sha256: $XPRODUCT, materializes-when: "$product_when" }
  - { id: xref-inert,   scope: operator, owner: .github, source: .github/.claude/skills/xref-inert/SKILL.md,   sha256: $XINERT,   materializes-when: "false" }
YAML
  write_channels "$XREG" <<'YAML'
  - { owner: .github, scope: driver,   disposition: delivered, kind: package, channel: FS.GG.Drivers, evidence: src/FS.GG.Drivers/stage-drivers.py }
  - { owner: .github, scope: product,  disposition: delivered, kind: package, channel: FS.GG.Drivers, evidence: src/FS.GG.Drivers/stage-drivers.py }
  - { owner: .github, scope: operator, disposition: withheld,  reason: "authored here, materialized nowhere (ADR-0057)", evidence: src/FS.GG.Drivers/stage-drivers.py }
YAML
  cat > "$XROOT/.github/registry/driver-skill-manifest.json" <<JSON
{ "schemaVersion": 1, "skills": [
  { "id": "xref-driver",  "scope": "driver",   "sha256": "$XDRIVER",  "supplied-by": ".claude/skills/xref-driver",  "materializes-when": "always" },
  { "id": "xref-product", "scope": "product",  "sha256": "$XPRODUCT", "supplied-by": ".claude/skills/xref-product", "materializes-when": "$product_when" },
  { "id": "xref-inert",   "scope": "operator", "sha256": "$XINERT",   "supplied-by": ".claude/skills/xref-inert",   "materializes-when": "false" }
] }
JSON
}

# `write_xref_driver_refs <extra-line-or-empty>` — xref-driver's references/feedback-contract.md
# always references xref-product's path; an optional second line adds an UNREGISTERED reference.
write_xref_driver_refs() {
  {
    echo 'Run: `dotnet fsi .agents/skills/xref-product/scripts/tool.fsx -- validate`'
    if [ -n "${1:-}" ]; then printf '%s\n' "$1"; fi
  } > "$XROOT/.github/.claude/skills/xref-driver/references/feedback-contract.md"
}

echo "== 62. cross-references: an always-referencing skill whose sibling's predicate narrows is caught (.github#2366 AC-1) =="
write_xref_driver_refs ""

# (a) POSITIVE CONTROL: xref-product is ALSO always — both predicates agree, exactly like the real
# work-roadmap -> fs-gg-feedback-report pair on `main` today. Fully coherent: zero findings at all.
write_xref_registry "always"
out="$(run --registry "$XREG" --repos-root "$XROOT" || true)"
run --registry "$XREG" --repos-root "$XROOT" >/dev/null \
  || { echo "FAIL: the coherent always/always xref fixture was not accepted"; echo "$out"; exit 1; }
grep -q "\[cross-references\]" <<<"$out" && { echo "FAIL: cross-references fired on an implied reference"; echo "$out"; exit 1; }

# (b) THE MUTATION — AC-1's named case. Narrow xref-product's predicate away from `always`, the
# missing-fs-gg-feedback-report shape: a driver that is unconditionally present assumes a sibling
# that is not. This is the SUBJECT of check 7 broken, not its predicate: it proves the gate can go
# RED, not merely that it can pass.
write_xref_registry "profile in [game]"
out="$(run --registry "$XREG" --repos-root "$XROOT" || true)"
run --registry "$XREG" --repos-root "$XROOT" >/dev/null 2>&1 \
  && { echo "FAIL: expected non-zero exit once xref-product narrowed away from xref-driver's always"; exit 1; }
grep -q "\[cross-references\] xref-driver" <<<"$out" \
  || { echo "FAIL: narrowing xref-product's predicate did not trip cross-references"; echo "$out"; exit 1; }
grep -q "xref-product" <<<"$out" || { echo "FAIL: the finding does not name the referenced id"; echo "$out"; exit 1; }
grep -q "profile in \[game\]" <<<"$out" \
  || { echo "FAIL: the finding does not echo xref-product's actual (mutated) predicate"; echo "$out"; exit 1; }

# (c) RESTORED: back to always/always, and the finding is gone again — this is what "shown to fail,
# not merely to pass" requires: the SAME fixture, the SAME assertion, both directions.
write_xref_registry "always"
out="$(run --registry "$XREG" --repos-root "$XROOT" || true)"
run --registry "$XREG" --repos-root "$XROOT" >/dev/null \
  || { echo "FAIL: restoring xref-product to always did not restore coherence"; echo "$out"; exit 1; }
grep -q "\[cross-references\]" <<<"$out" && { echo "FAIL: cross-references still fired after the predicate was restored"; echo "$out"; exit 1; }
echo "   ok"

echo "== 63. cross-references: a path reference to an UNREGISTERED sibling id is caught =="
write_xref_registry "always"

# (a) Add a second reference line naming an id NO registry row declares.
write_xref_driver_refs 'Also see `.agents/skills/xref-ghost/notes.md` for more.'
out="$(run --registry "$XREG" --repos-root "$XROOT" || true)"
grep -q "\[cross-references\] xref-driver" <<<"$out" \
  || { echo "FAIL: a reference to an unregistered id was not caught"; echo "$out"; exit 1; }
grep -q "xref-ghost" <<<"$out" || { echo "FAIL: the finding does not name the unregistered id"; echo "$out"; exit 1; }
grep -q "no registry row declares" <<<"$out" || { echo "FAIL: the finding does not explain WHY"; echo "$out"; exit 1; }

# (b) Remove the ghost reference and the finding disappears — both directions, again.
write_xref_driver_refs ""
out="$(run --registry "$XREG" --repos-root "$XROOT" || true)"
run --registry "$XREG" --repos-root "$XROOT" >/dev/null \
  || { echo "FAIL: removing the ghost reference did not restore coherence"; echo "$out"; exit 1; }
grep -q "\[cross-references\]" <<<"$out" && { echo "FAIL: cross-references still fired with no ghost reference"; echo "$out"; exit 1; }
echo "   ok"

echo "== 64. cross-references: an unsatisfiable referencer (materializes-when: false) is NEVER flagged, even beside a REAL violation (.github#2366 AC-3) =="
# xref-inert (materializes-when: "false", mirroring the live lane-steward -> pnext-item shape)
# permanently references an unregistered sibling in its OWN references/notes.md (set up above, never
# touched by this case). If the vacuous-referencer suppression were broken — or simply absent — this
# would ALSO fire, every time. Prove it is silent even in the SAME run where xref-driver's own
# reference is a REAL, live violation, so the suppression is not accidentally swallowing real ones.
write_xref_registry "profile in [game]"   # xref-driver (always) -> xref-product (narrowed): a REAL violation
out="$(run --registry "$XREG" --repos-root "$XROOT" || true)"
grep -q "\[cross-references\] xref-driver" <<<"$out" \
  || { echo "FAIL: the real xref-driver violation went unreported alongside xref-inert"; echo "$out"; exit 1; }
grep -q "\[cross-references\] xref-inert" <<<"$out" \
  && { echo "FAIL: xref-inert (materializes-when: false) was flagged — an unsatisfiable referencer can never surface this failure mode"; echo "$out"; exit 1; }
grep -q "xref-nonexistent" <<<"$out" \
  && { echo "FAIL: xref-inert's reference to the unregistered xref-nonexistent leaked into the findings"; echo "$out"; exit 1; }
# Restore the shared xref fixture to its coherent state for any case added after this one.
write_xref_registry "always"
echo "   ok"

# =================================================================================================
# CASES 65-67 — THE ROSTER ARM AND THE PARAMETER-VOCABULARY ARM (.github#2547).
#
# Both close a fail-open that lived one level ABOVE every case so far. Cases 9-13 prove
# `declared-completeness` reports a manifest-declared skill with no row — over the producers found
# under `--repos-root`. Nothing asked whether that set was the set it should have been, and the de
# facto answer was a hardcoded `for repo in FS.GG.SDD FS.GG.Rendering FS.GG.Game` in
# skill-registry-coherence.yml. FS.GG.Templates shipped a manifest declaring six `scope: product`
# skills, was in neither clone loop, and so its six absent rows were not a finding — they were
# nothing, with every arm green throughout.
#
# These build their OWN registry + roster + root under $WORK/roster-case, because the shared roster
# above deliberately rosters no FS-GG row (see its comment).
# =================================================================================================
RCASE="$WORK/roster-case"
mkdir -p "$RCASE" "$RCASE/repos/FS.GG.Present/skills/rostered"
printf 'rostered body\n' > "$RCASE/repos/FS.GG.Present/skills/rostered/SKILL.md"
RPRESENT="$(sha "$RCASE/repos/FS.GG.Present/skills/rostered/SKILL.md")"
mkdir -p "$RCASE/repos/FS.GG.Present/template/skill-manifest"
cat > "$RCASE/repos/FS.GG.Present/template/skill-manifest/skill-manifest.json" <<JSON
{ "schemaVersion": 1, "skills": [
  { "id": "rostered", "scope": "product", "sha256": "$RPRESENT", "supplied-by": "skills/rostered/", "materializes-when": "profile in [game]" }
] }
JSON
cat > "$RCASE/skills.yml" <<YAML
schemaVersion: 3
updated: "2026-08-14"
parameters: [profile]
skills:
  - { id: rostered, scope: product, owner: fs-gg-present, source: FS.GG.Present/skills/rostered/SKILL.md, sha256: $RPRESENT, materializes-when: "profile in [game]" }
YAML
write_channels "$RCASE/skills.yml" <<'YAML'
  - { owner: fs-gg-present, scope: product, disposition: delivered, kind: package, channel: Fixture.Present, evidence: tests/skill-registry/run.sh }
YAML
write_roster() {
  cat > "$RCASE/repos.yml" <<YAML
schemaVersion: 1
repos:
$1
YAML
}

echo "== 65. a ROSTERED repo absent from --repos-root is a declared-completeness finding =="
# Both rows are rostered; only FS.GG.Present is checked out. FS.GG.Absent is the .github#2547 shape:
# a repository the organisation rosters, that this gate was never pointed at, and whose producer
# manifest — if it has one — is therefore reconciled against nothing.
write_roster '  - { id: present, full: FS-GG/FS.GG.Present, role: framework }
  - { id: absent,  full: FS-GG/FS.GG.Absent,  role: framework }
  - { id: outside, full: Someone/Else.Repo,   role: non-participant }'
out="$(run --registry "$RCASE/skills.yml" --repos-root "$RCASE/repos" || true)"
grep -q "\[declared-completeness\] FS.GG.Absent" <<<"$out" \
  || { echo "FAIL: an unreachable rostered producer was not reported"; echo "$out"; exit 1; }
grep -q "reconciled against NOTHING" <<<"$out" \
  || { echo "FAIL: the finding does not say what the absence COSTS"; echo "$out"; exit 1; }
# The filter is real: a non-FS-GG roster row is not an expectation this organisation can satisfy.
grep -q "Else.Repo" <<<"$out" && { echo "FAIL: a non-FS-GG roster row became an expectation"; exit 1; }
run --registry "$RCASE/skills.yml" --repos-root "$RCASE/repos" >/dev/null 2>&1 \
  && { echo "FAIL: expected exit 1 while a rostered producer is unreachable"; exit 1; }
echo "   ok"

echo "== 66. GATE-INVERSION: with every rostered repo reachable the SAME registry is coherent =="
# The inversion that matters is the arm's own subject, not a mutated assertion: drop the unreachable
# row from the roster and nothing else changes. A leg that only ever reds proves a constant, and an
# arm that reds on a satisfied roster would be unshippable — the two failure modes this pins apart.
write_roster '  - { id: present, full: FS-GG/FS.GG.Present, role: framework }'
run --registry "$RCASE/skills.yml" --repos-root "$RCASE/repos" >/dev/null \
  || { echo "FAIL: a fully-reachable roster still reported"; run --registry "$RCASE/skills.yml" --repos-root "$RCASE/repos" || true; exit 1; }
# A rostered repo that IS present and carries NO manifest stays silently fine — it is a
# non-producer, and `producers()` already skips it. Absence is the finding; emptiness is not.
mkdir -p "$RCASE/repos/FS.GG.Quiet"
write_roster '  - { id: present, full: FS-GG/FS.GG.Present, role: framework }
  - { id: quiet,   full: FS-GG/FS.GG.Quiet,   role: framework }'
run --registry "$RCASE/skills.yml" --repos-root "$RCASE/repos" >/dev/null \
  || { echo "FAIL: a present, manifest-less rostered repo was reported"; exit 1; }
# ...and an UNREADABLE roster fails closed rather than degrading to "no expectations".
mv "$RCASE/repos.yml" "$RCASE/repos.yml.bak"
out="$(run --registry "$RCASE/skills.yml" --repos-root "$RCASE/repos" || true)"
grep -q "\[declared-completeness\] registry/repos.yml" <<<"$out" \
  || { echo "FAIL: a missing roster did not fail closed"; echo "$out"; exit 1; }
python3 "$TOOL" --registry "$RCASE/skills.yml" --producers >/dev/null 2>&1 \
  && { echo "FAIL: --producers must exit non-zero on an unreadable roster, never print nothing"; exit 1; }
mv "$RCASE/repos.yml.bak" "$RCASE/repos.yml"
# --producers is the ONE spelling both CI clone loops read; it must print exactly the FS-GG rows.
got="$(python3 "$TOOL" --registry "$RCASE/skills.yml" --producers)"
[ "$got" = "FS.GG.Present
FS.GG.Quiet" ] || { echo "FAIL: --producers printed '$got'"; exit 1; }
echo "   ok"

echo "== 67. a predicate over an UNDECLARED parameter is a parameter-vocabulary finding =="
# The .github#2547 symptom: the catalog asserted one materialization vocabulary org-wide while a
# second producer's rows evaluated a disjoint one, and the `parameters:` list — the place that
# disagreement was visible — was read by nothing.
sed -i 's|materializes-when: "profile in \[game\]" }|materializes-when: "template in [fable-game]" }|' "$RCASE/skills.yml"
sed -i 's|"materializes-when": "profile in \[game\]"|"materializes-when": "template in [fable-game]"|' \
  "$RCASE/repos/FS.GG.Present/template/skill-manifest/skill-manifest.json"
out="$(run --registry "$RCASE/skills.yml" --repos-root "$RCASE/repos" || true)"
grep -q "\[parameter-vocabulary\] template" <<<"$out" \
  || { echo "FAIL: an undeclared predicate parameter was not reported"; echo "$out"; exit 1; }
grep -q "\[predicate-matches\]" <<<"$out" \
  && { echo "FAIL: the row and its manifest agree — only the vocabulary arm may fire"; echo "$out"; exit 1; }
# GATE-INVERSION: declaring the parameter is what clears it, and nothing else moved.
sed -i 's|^parameters: \[profile\]$|parameters: [profile, template]|' "$RCASE/skills.yml"
run --registry "$RCASE/skills.yml" --repos-root "$RCASE/repos" >/dev/null \
  || { echo "FAIL: declaring the parameter did not clear the finding"; run --registry "$RCASE/skills.yml" --repos-root "$RCASE/repos" || true; exit 1; }
# ...and a registry that declares NO `parameters:` at all while carrying predicates is a finding,
# not a skip — otherwise deleting the list would delete the check with it.
sed -i '/^parameters: /d' "$RCASE/skills.yml"
out="$(run --registry "$RCASE/skills.yml" --repos-root "$RCASE/repos" || true)"
grep -q "\[parameter-vocabulary\] parameters" <<<"$out" \
  || { echo "FAIL: a missing parameters: list did not fail closed"; echo "$out"; exit 1; }
echo "   ok"

echo "== 68. the REAL registry's roster sibling exists, and both CI clone loops read --producers =="
REPO_ROOT="$(cd "$HERE/../.." && pwd)" python3 - <<'PY'
import os, sys
root = os.environ["REPO_ROOT"]
# `roster_reachable` binds to `repos.yml` BESIDE `registry/skills.yml` rather than to a flag,
# precisely so it cannot be quietly stopped being passed. That only holds while the sibling is
# really there — assert the layout the design depends on.
assert os.path.isfile(os.path.join(root, "registry", "repos.yml")), \
    "registry/repos.yml must sit beside registry/skills.yml — roster_reachable reads it as a sibling"
# THE POINT OF `--producers` IS THAT THE EXPECTED SET HAS ONE SPELLING. A clone loop keeping its own
# copy would reintroduce .github#2547's cause with an extra file in the way, so assert that neither
# workflow lists producers itself.
for wf in ("skill-registry-coherence.yml", "skill-registry-autofix.yml"):
    text = open(os.path.join(root, ".github", "workflows", wf)).read()
    body = "\n".join(l for l in text.splitlines() if not l.lstrip().startswith("#"))
    assert "fsgg-skill-registry-check --producers" in body, \
        f"{wf} must derive its producer checkouts from `--producers` (.github#2547)"
    assert "for repo in FS.GG" not in body, \
        f"{wf} still hardcodes a producer list — that is the .github#2547 cause"
    assert "yaml.safe_load(open('registry/skills.yml'))" not in body, \
        f"{wf} still derives producers from the registry it reconciles — self-referential (.github#2547)"
print("   ok")
PY

# =================================================================================================
# THE TRIGGER SET (.github#1606) — this workflow's OTHER gate must be reachable from its own filter.
#
# `skill-registry-coherence.yml` runs `generate-driver-manifest --check`, and it is the ONLY
# workflow anywhere that does. Its `paths:` filter was authored around the reconcile and named none
# of that check's inputs and neither of its outputs, so:
#   * `edc8404b` staled the manifest by editing `.claude/skills/pnext-item/references/…` — four
#     workflows ran on it and not one asked the question;
#   * the red landed 107 minutes later on #1590, a PR touching no skill, purely because #1590
#     matched `registry/skills.yml`;
#   * and #1605, which REPAIRED the manifest, did not run the gate it repaired either.
#
# So this leg asserts the TRIGGER SET, not the check. A fixture that exercises `--check` directly
# closes nothing here: `--check` was correct throughout, and every earlier instance of this class
# (#332, #334, #508, #1519, #1593) had a working gate behind an unreachable filter.
#
# NOTHING BELOW IS RETYPED. The subject comes from the emitter's own `PATHS_SUBJECT`, folded by
# `check-paths-coherence.py`'s `declared_subject()`, and the match is that gate's own `selects()` —
# the same reader, the same matcher, one shape. Widen what the emitter walks and this leg reds the
# filter until it is widened to match.
#
# THIS LIVES HERE RATHER THAN IN `check-paths-coherence.py`'s rule (c), which is its natural home
# and cannot reach: `named_scripts()` links a workflow to a gate script only via a `.py` suffix, and
# this emitter is extensionless. That is `.github#1639`; when it lands, rule (c) subsumes this leg.
#
# The fixture job runs under `setup-policy-python`, so PyYAML is present — the same dependency
# `catalog-metadata.py` above already assumes.
# =================================================================================================
# CASES 69-77 — THE `delivery-channel` ARM (.github#2545, ADR-0063).
#
# Checks 1-8 are predicates over a row's bytes, its presence, its emission condition, or its
# parameters. NOTHING asked where a class's bytes are supposed to COME FROM, so the answer "nowhere"
# was not a finding, it was nothing — and that class shipped three times (`fs-gg-playtest`
# .github#1299, `workRoadmap` .github#1300, `fs-gg-feedback-report` .github#2380/#2545), each found
# by accident rather than by a gate.
#
# These cases build their own registry + declaration pair under $CH so the REAL tree is never
# mutated, and they pass an EMPTY --repos-root throughout: the arm is offline by construction, which
# is what lets it run without producer checkouts at all. The other arms fire freely in that state;
# every assertion below is scoped to `[delivery-channel]` lines, so their noise cannot pass for this
# arm's verdict — and cases 69 and 77 assert the arm's verdict on the REAL shipped pair.
# =================================================================================================
CH="$WORK/channels"
CHROOT="$WORK/channels-repos"
mkdir -p "$CH" "$CHROOT"
REAL_REG="$HERE/../../registry/skills.yml"
REAL_CH="$HERE/../../registry/skills.delivery-channels.yml"

# `dc <registry>` — the `[delivery-channel]` lines only, whatever else the other arms say.
dc() { run --registry "$1" --repos-root "$CHROOT" 2>&1 | grep '\[delivery-channel\]' || true; }

echo "== 69. the SHIPPED registry and its SHIPPED declaration are coherent =="
# The gate this change adds passes on the tree this change lands, and it reaches that verdict with
# no producer checkout at all — $CHROOT is empty.
[ -f "$REAL_CH" ] || { echo "FAIL: registry/skills.delivery-channels.yml is missing"; exit 1; }
out="$(dc "$REAL_REG")"
[ -z "$out" ] || { echo "FAIL: the shipped declaration is not coherent with the shipped registry"; echo "$out"; exit 1; }
echo "   ok"

echo "== 69a. the shipped Rendering class is delivered by its schema-v2 owner package =="
REPO_ROOT="$HERE/../.." python3 - <<'PY'
import os
from pathlib import Path
import yaml

root = Path(os.environ["REPO_ROOT"])
document = yaml.safe_load((root / "registry/skills.delivery-channels.yml").read_text())
entry = next((item for item in document["classes"]
              if item.get("owner") == "fs-gg-rendering" and item.get("scope") == "product"), None)
expected = {
    "disposition": "delivered",
    "kind": "package",
    "channel": "FS.GG.Rendering.Skills",
    "evidence": "registry/dependencies.yml (contract `rendering-skills`, owner rendering, consumers [sdd], schema-v2 package 0.1.1)",
}
if entry is None:
    raise SystemExit("FAIL: the shipped Rendering/product delivery-channel entry is missing")
for key, value in expected.items():
    if entry.get(key) != value:
        raise SystemExit(f"FAIL: Rendering/product {key} expected {value!r}, got {entry.get(key)!r}")
if "tracked-by" in entry or "provider" in entry:
    raise SystemExit("FAIL: delivered Rendering/product entry retained provider-scoped fields")
PY
echo "   ok"

echo "== 70. a class the registry carries and the declaration ignores is a finding, naming its rows =="
cat > "$CH/skills.yml" <<YAML
schemaVersion: 1
updated: "2026-08-15"
parameters: [profile]
skills:
  - { id: alpha, scope: process, owner: owner-one, source: One/alpha/SKILL.md, sha256: $GOOD, materializes-when: always }
  - { id: beta,  scope: product, owner: owner-two, source: Two/beta/SKILL.md,  sha256: $OWNED, materializes-when: "profile in [game]" }
YAML
write_channels "$CH/skills.yml" <<'CHANNELS'
  - { owner: owner-one, scope: process, disposition: delivered, kind: in-code, channel: Fixture, evidence: tests/skill-registry/run.sh }
CHANNELS
out="$(dc "$CH/skills.yml")"
grep -q "\[delivery-channel\] owner-two/product" <<<"$out" \
  || { echo "FAIL: an undeclared class was not reported"; echo "$out"; exit 1; }
grep -q "beta" <<<"$out" || { echo "FAIL: the finding does not name the rows it is about"; echo "$out"; exit 1; }
grep -q "supplied from nowhere" <<<"$out" \
  || { echo "FAIL: the finding does not say what the silence COSTS"; echo "$out"; exit 1; }
run --registry "$CH/skills.yml" --repos-root "$CHROOT" >/dev/null 2>&1 \
  && { echo "FAIL: an undeclared class must exit non-zero"; exit 1; }
echo "   ok"

echo "== 71. GATE-INVERSION on 70: declaring the class clears it, and nothing else did =="
# Case 70's red must be THIS arm's doing. Add the one entry and the same registry goes quiet.
add_channel "$CH/skills.yml" owner-two product
out="$(dc "$CH/skills.yml")"
[ -z "$out" ] || { echo "FAIL: declaring the class did not clear the finding"; echo "$out"; exit 1; }
echo "   ok"

echo "== 72. a declaration entry the registry no longer carries is a dead-entry finding =="
# The converse direction. Without it the declaration rots into a restatement of a class set that has
# moved on — the ADR-0058 failure this arm exists to prevent, reintroduced by the fix for it.
add_channel "$CH/skills.yml" owner-ghost driver
out="$(dc "$CH/skills.yml")"
grep -q "\[delivery-channel\] owner-ghost/driver" <<<"$out" \
  || { echo "FAIL: a dead declaration entry was not reported"; echo "$out"; exit 1; }
grep -q "no such row" <<<"$out" || { echo "FAIL: the dead-entry finding does not say why"; echo "$out"; exit 1; }
echo "   ok"

echo "== 73. a class declared TWICE is a finding — two answers to one question =="
write_channels "$CH/skills.yml" <<'CHANNELS'
  - { owner: owner-one, scope: process, disposition: delivered, kind: in-code, channel: A, evidence: e }
  - { owner: owner-two, scope: product, disposition: delivered, kind: package, channel: B, evidence: e }
  - { owner: owner-two, scope: product, disposition: gap, tracked-by: FS-GG/FS.GG.Rendering#1240 }
CHANNELS
out="$(dc "$CH/skills.yml")"
grep -q "declared twice" <<<"$out" || { echo "FAIL: a duplicated class was not reported"; echo "$out"; exit 1; }
echo "   ok"

echo "== 74. every disposition's required fields are enforced, and the vocabulary is CLOSED =="
enforce() {  # enforce <expect-substring> <entry-yaml-for-owner-two>
  { printf 'schemaVersion: 1\nclasses:\n'
    printf '  - { owner: owner-one, scope: process, disposition: delivered, kind: in-code, channel: A, evidence: e }\n'
    printf '  %s\n' "$2"
  } > "$CH/skills.delivery-channels.yml"
  local got
  got="$(dc "$CH/skills.yml")"
  grep -q "$1" <<<"$got" || { echo "FAIL: expected '$1' for entry: $2"; echo "$got"; exit 1; }
}
enforce "is not one of"                     '- { owner: owner-two, scope: product, disposition: probably-fine, evidence: e }'
enforce "is not one of"                     '- { owner: owner-two, scope: product, channel: A, evidence: e }'
enforce "requires a non-empty .evidence."   '- { owner: owner-two, scope: product, disposition: delivered, kind: package, channel: A }'
enforce "requires a non-empty .channel."    '- { owner: owner-two, scope: product, disposition: delivered, kind: package, evidence: e }'
enforce "permits kind"                      '- { owner: owner-two, scope: product, disposition: delivered, kind: template-payload, channel: A, evidence: e }'
enforce "requires a non-empty .reason."     '- { owner: owner-two, scope: product, disposition: withheld, evidence: e }'
enforce "requires a non-empty .tracked-by." '- { owner: owner-two, scope: product, disposition: gap }'
enforce "requires a non-empty .evidence."   '- { owner: owner-two, scope: product, disposition: provider-scoped, kind: template-payload, provider: p, tracked-by: FS-GG/FS.GG.Rendering#1240 }'
# ...AND `gap`'s ASYMMETRY IS DELIBERATE, so it is pinned here rather than left to be rediscovered by
# probing. `gap` requires `tracked-by` and NOT `evidence:`: it asserts that NO artefact carries these
# bytes, so there is nothing for it to name, and demanding evidence of that negative would only invite
# a pointer to nothing. A reviewer read the surrounding prose, probed this by hand, and found the
# commentary and the schema disagreeing (.github#2545 repair 1, finding 3) -- the schema was right and
# the prose was wrong. This case is what stops them drifting apart again.
{ printf 'schemaVersion: 1\nclasses:\n'
  printf '  - { owner: owner-one, scope: process, disposition: delivered, kind: in-code, channel: A, evidence: e }\n'
  printf '  - { owner: owner-two, scope: product, disposition: gap, tracked-by: FS-GG/FS.GG.Rendering#1240 }\n'
} > "$CH/skills.delivery-channels.yml"
out="$(dc "$CH/skills.yml")"
[ -z "$out" ] || { echo "FAIL: a gap carrying tracked-by and no evidence was reported"; echo "$out"; exit 1; }
echo "   ok"

echo "== 75. a provider-scoped class must name who owes universal reach, or why it does not need it =="
# THE DISPOSITION THIS WHOLE ARM EXISTS FOR. `fs-gg-feedback-report` HAS a channel — Rendering's
# fs-gg-ui template emits it correctly and unconditionally — and lacks only REACH. A two-valued
# has-channel/no-channel vocabulary would let exactly that be written down as green.
PS='- { owner: owner-two, scope: product, disposition: provider-scoped, kind: template-payload, provider: p, evidence: e'
enforce "EXACTLY ONE"        "$PS }"
enforce "it carries neither" "$PS }"
enforce "EXACTLY ONE"        "$PS, tracked-by: FS-GG/FS.GG.Rendering#1240, accepted: \"both at once\" }"
enforce "it carries both"    "$PS, tracked-by: FS-GG/FS.GG.Rendering#1240, accepted: \"both at once\" }"
# ...and a reference a reader cannot resolve names nobody (.github#2107: the board's own <repo>#<n>
# shorthand is not a link GitHub's closing-keyword grammar parses).
enforce "full owner/repo#number" "$PS, tracked-by: .github#1240 }"
enforce "full owner/repo#number" "$PS, tracked-by: \"see the Rendering row\" }"
enforce "full owner/repo#number" "$PS, tracked-by: FS-GG/FS.GG.Rendering#0 }"
# Both accountable forms clear it.
for good in "$PS, tracked-by: FS-GG/FS.GG.Rendering#1240 }" "$PS, accepted: \"provider-scoped reach is what these rows mean\" }"; do
  { printf 'schemaVersion: 1\nclasses:\n'
    printf '  - { owner: owner-one, scope: process, disposition: delivered, kind: in-code, channel: A, evidence: e }\n'
    printf '  %s\n' "$good"
  } > "$CH/skills.delivery-channels.yml"
  out="$(dc "$CH/skills.yml")"
  [ -z "$out" ] || { echo "FAIL: an accountable provider-scoped entry was reported: $good"; echo "$out"; exit 1; }
done
echo "   ok"

echo "== 76. the arm FAILS CLOSED — a missing, mis-shaped, or unknown-schema declaration is a finding =="
# A gate that cannot read the declaration must never answer "every class is fine": that is the
# fail-open (#266) this arm exists to close, one level up from the classes themselves.
rm -f "$CH/skills.delivery-channels.yml"
grep -q "unreadable delivery-channel declaration" <<<"$(dc "$CH/skills.yml")" \
  || { echo "FAIL: a MISSING declaration was not a finding"; exit 1; }
printf 'schemaVersion: 99\nclasses: []\n' > "$CH/skills.delivery-channels.yml"
grep -q "refusing to read a shape it does not know" <<<"$(dc "$CH/skills.yml")" \
  || { echo "FAIL: an unknown schemaVersion was not refused"; exit 1; }
printf 'schemaVersion: 1\n' > "$CH/skills.delivery-channels.yml"
grep -q "no .classes. list" <<<"$(dc "$CH/skills.yml")" \
  || { echo "FAIL: a declaration with no classes list was not a finding"; exit 1; }
printf 'schemaVersion: 1\nclasses:\n  - "not a mapping"\n' > "$CH/skills.delivery-channels.yml"
grep -q "not a mapping" <<<"$(dc "$CH/skills.yml")" \
  || { echo "FAIL: a mis-shaped class entry was not a finding"; exit 1; }
echo "   ok"

echo "== 77. GATE-INVERSION ON THE SHIPPED PAIR: drop the fs-gg-rendering entry and the gate reds =="
# Case 69 proves the shipped declaration is green. This proves that green MEANS something: remove the
# one entry this item is about and the arm names the class, its row count, and fs-gg-feedback-report
# itself — the row whose absence from EHotwagner/S.I.R. started .github#2380.
cp "$REAL_REG" "$CH/inverted.yml"
python3 "$HERE/invert-rendering-channel.py" "$REAL_CH" "$CH/inverted.delivery-channels.yml" \
  || { echo "FAIL: could not build the inverted declaration"; exit 1; }
out="$(dc "$CH/inverted.yml")"
grep -q "\[delivery-channel\] fs-gg-rendering/product" <<<"$out" \
  || { echo "FAIL: removing the entry did not red the gate"; echo "$out"; exit 1; }
grep -q "fs-gg-feedback-report" <<<"$out" \
  || { echo "FAIL: the finding does not name the row this item is about"; echo "$out"; exit 1; }
grep -q "18 row(s)" <<<"$out" \
  || { echo "FAIL: the finding does not report the class's row count"; echo "$out"; exit 1; }
run --registry "$CH/inverted.yml" --repos-root "$CHROOT" >/dev/null 2>&1 \
  && { echo "FAIL: the inverted pair must exit non-zero"; exit 1; }
echo "   ok"

# =================================================================================================
echo "== trigger set: skill-registry-coherence.yml reaches generate-driver-manifest --check"
REPO_ROOT="$(cd "$HERE/../.." && pwd)" python3 - <<'PY'
import importlib.util, os, sys
import yaml

root = os.environ["REPO_ROOT"]
gate = os.path.join(root, "scripts", "check-paths-coherence.py")
emitter = "scripts/generate-driver-manifest"
workflow = ".github/workflows/skill-registry-coherence.yml"

spec = importlib.util.spec_from_file_location("paths_coherence", gate)
cpc = importlib.util.module_from_spec(spec)
spec.loader.exec_module(cpc)

fail = []

# A DECLARATION THAT VANISHED IS A FAILURE, NOT A SKIP. rule (c) is opt-in and silently ignores a
# script that declares nothing — correct for a rule that must not blanket-red unmigrated workflows,
# and exactly wrong here, where the absence would take this whole leg with it (#266).
subject = cpc.declared_subject(os.path.join(root, emitter), workflow)
if not subject:
    sys.exit(f"FAIL: {emitter} declares no PATHS_SUBJECT — this leg would audit nothing (#266)")

doc = yaml.safe_load(open(os.path.join(root, workflow), encoding="utf-8"))
# `on:` is the YAML 1.1 boolean True after safe_load; the gate next door normalises the same way.
on = doc.get("on", doc.get(True))
if not isinstance(on, dict):
    sys.exit(f"FAIL: {workflow} declares no readable `on:` mapping")

filters = {}
for trigger in ("pull_request", "push"):
    raw = (on.get(trigger) or {}).get("paths")
    if not isinstance(raw, list) or not raw:
        sys.exit(f"FAIL: {workflow} declares no `{trigger}.paths` — the gate cannot be reached")
    filters[trigger] = [str(p) for p in raw]

def uncovered(patterns):
    """Subject entries a push under which would NOT trigger this filter."""
    out = []
    for entry in subject:
        # A directory is covered when its CONTENTS are, never by its own name — the same probe
        # rule (c) makes, because a bare `docs` pattern triggers on nothing.
        probe = entry if os.path.isfile(os.path.join(root, entry)) else f"{entry}/x"
        if not cpc.selects(probe, patterns):
            out.append(entry)
    return out

for trigger, patterns in filters.items():
    for entry in uncovered(patterns):
        fail.append(
            f"{workflow}: `{trigger}.paths` does not select {entry!r}, which {emitter} "
            f"declares it reads or writes — the check runs nowhere when that path changes"
        )
    # The LINKAGE itself: an edit to the emitter changes its own output, and is the one path that
    # must re-ask the question on every shape of this defect.
    if not cpc.selects(emitter, patterns):
        fail.append(f"{workflow}: `{trigger}.paths` does not select {emitter!r} itself")

# THE HISTORICAL LEG — the exact file `edc8404b` edited. The general assertion above would pass on a
# filter that covered the subject by accident through some unrelated glob; this one names the commit
# that paid for the rule, so a future narrowing has to walk past it.
STALER = ".claude/skills/pnext-item/references/command-contracts.md"
for trigger, patterns in filters.items():
    if not cpc.selects(STALER, patterns):
        fail.append(
            f"{workflow}: `{trigger}.paths` would not have selected {STALER!r} — the edc8404b "
            f"staling commit would go unchecked again, and red a stranger's PR instead"
        )

# TEETH. A green above is worth having only if a red is reachable: drop the root that carries the
# authored bodies and the assertion must name it. Measured, not assumed (#266).
blinded = [p for p in filters["pull_request"] if p != ".claude/skills/**"]
if ".claude/skills" not in uncovered(blinded):
    fail.append(
        "the coverage assertion is VACUOUS: removing `.claude/skills/**` from the filter did not "
        "produce a finding, so a green above proves nothing. Either PATHS_SUBJECT no longer names "
        "that root, or some BROADER pattern now selects it and this probe must name that one "
        "instead — fail closed and say so rather than let the leg quietly stop measuring (#266)"
    )

if fail:
    for line in fail:
        sys.stderr.write(f"FAIL: {line}\n")
    raise SystemExit(1)
print(
    f"   ok  both filters select all {len(subject)} declared subject path(s), the emitter itself, "
    f"and the edc8404b staling path; the assertion has teeth"
)
PY

echo "skill-registry fixture: all checks passed"
