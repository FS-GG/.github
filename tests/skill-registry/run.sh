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
grep -q "materializes-when: always }" "$REG" || { echo "FAIL: default predicate not emitted bare"; grep stranger "$REG"; exit 1; }
rm -rf "$ROOT/Producer.Three"
echo "   ok"

echo "skill-registry fixture: all checks passed"
