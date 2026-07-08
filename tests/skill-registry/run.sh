#!/usr/bin/env bash
# Fixture for scripts/fsgg-skill-registry-check — the cross-repo guard on `registry = manifest =
# bytes` (.github#247, ADR-0017). Proves the tool catches the three drift shapes that actually bit
# us, off a temp registry + temp producer checkouts: a stale `sha256` (fs-gg-audio, ADR-0024 step 4),
# a `source:` that no longer exists (a renamed/relocated skill), and a frozen mirror that has
# diverged from the canonical body (fs-gg-game-core — the mirror was RIGHT and the source stale).
# Also proves --write reconciles only the stale digest, leaves the hand-aligned YAML otherwise
# byte-identical, and refuses to claim success while a non-digest finding remains.
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
run --registry "$REG" --repos-root "$ROOT" --json | python3 -c "
import json,sys
d=json.load(sys.stdin)
ids=sorted(f['id'] for f in d['findings'])
assert ids==['stale'], ids
print('   ok')
"

echo "skill-registry fixture: all checks passed"
