#!/usr/bin/env bash
# Fixture for src/FS.GG.Kit/stage-kit.sh — the PRODUCER of the FS.GG.Kit package (ADR-0062), and the
# artifact every receiver is now verified against (#1584). It is here for one defect (.github#1614):
#
#   stage-kit.sh consumed scripts/repos.sh through process substitutions — `done < <(read_kit …)` and
#   `paste <(…) <(…) | while …`. `set -euo pipefail` does not see a failure inside `<(...)`, so a
#   repos.sh that DIED produced an empty list, every loop body ran ZERO times, and the kit staged with
#   no skills, no fsgg-coord client and no engine manifest — EXIT 0. The only floor was
#   `[ -s kit-manifest.tsv ]`, and the build-config rows (derived from sync-build-config.sh, not from
#   the registry) satisfied it on their own. Measured before the fix: `staged 2 kit file(s)`, exit 0,
#   both rows build-config.
#
# THE FAILURE LEGS ARE THE POINT (epic #266: a gate that cannot say NO is not a gate). "I could not ask
# the registry" must never reach a caller as "the kit has no skills", so every read is driven to its
# failing state here. The reads are exercised through a FAKE SOURCE ROOT holding a stubbed repos.sh —
# stage-kit.sh resolves its inputs relative to its OWN location, which is what makes that possible, and
# it is the same shape tests/coordination-sync/run.sh uses for the same reader.
#
# ASSERT ON THE MESSAGE, NEVER ON rc ALONE. Every misconfiguration in stage-kit.sh is rc 2, and the fake
# root has no kit sources — so a run that gets PAST the reads also exits 2 ("canonical kit skill source
# missing"). rc alone cannot tell "died at the read" from "died after it", and would stay green if the
# fix were reverted. Leg 7 pins that distinction directly.
#
# Leg 0 runs the REAL script over the REAL checkout and requires a complete kit. Without it every leg
# below is synthetic, and stage-kit.sh could satisfy this fixture by refusing unconditionally.
set -uo pipefail

HERE="$(cd "$(dirname "$0")" && pwd)"
REPO_ROOT="$(cd "$HERE/../.." && pwd)"
KIT_SRC="$REPO_ROOT/src/FS.GG.Kit/stage-kit.sh"

WORK="$(mktemp -d "${TMPDIR:-/tmp}/kit-package-fixture.XXXXXX")"
trap 'rm -rf "$WORK"' EXIT

pass=0; failcount=0
ok()  { echo "PASS  $1"; pass=$((pass+1)); }
bad() { echo "FAIL  $1"; [ -n "${2:-}" ] && printf '%s\n' "$2" | sed 's/^/    | /'; failcount=$((failcount+1)); }

echo "kit-package fixture — work='$WORK'"

# ---- 0. THE REAL SCRIPT, THE REAL TREE: a complete kit, exit 0. ----------------------------------
REAL_OUT="$WORK/real"
real_out="$(bash "$KIT_SRC" "$REAL_OUT" 2>&1)"; real_rc=$?
if [ "$real_rc" -eq 0 ]; then ok "real tree: stages green (rc=0)"; else bad "real tree: stages green" "rc=$real_rc: $real_out"; fi
REAL_MANIFEST="$REAL_OUT/kit-manifest.tsv"

# #1886: this packed file is now read by receiver CI. Its shape is a versioned receiver contract,
# not merely an implementation detail of the materializer.
python3 - "$REAL_MANIFEST" <<'PY'
import re, sys
rows = [x.rstrip('\n').split('\t') for x in open(sys.argv[1], encoding='utf-8') if x.strip()]
assert rows, 'manifest is empty'
for row in rows:
    assert len(row) == 5, f'expected 5 columns: {row!r}'
    assert row[0] in {'skill', 'client', 'config', 'build-config'}, f'unknown kind: {row[0]!r}'
    assert re.fullmatch(r'[0-9a-f]{64}', row[3]), f'bad digest: {row[3]!r}'
    assert row[4] in {'true', 'false'}, f'bad executable: {row[4]!r}'
print('kit-manifest receiver contract v1: OK')
PY
for kind in skill client config build-config; do
  n=$(grep -c "^$kind$(printf '\t')" "$REAL_MANIFEST" 2>/dev/null || true)
  [ "${n:-0}" -gt 0 ] && ok "real tree: manifest carries $kind row(s) ($n)" \
    || bad "real tree: manifest carries no $kind row" "$(cut -f1 "$REAL_MANIFEST" 2>/dev/null | sort | uniq -c)"
done

# ---- the fake source root -------------------------------------------------------------------------
# Everything stage-kit.sh reads BESIDE the registry is real, so only the reader is under control: a
# fixture that also faked sync-build-config.sh or dist/dotnet would be asserting against its own
# invention rather than against the producer's actual inputs.
FAKE="$WORK/fakeroot"
mkdir -p "$FAKE/scripts" "$FAKE/registry" "$FAKE/src/FS.GG.Kit" "$FAKE/dist/dotnet"
cp "$KIT_SRC" "$FAKE/src/FS.GG.Kit/stage-kit.sh"
cp "$REPO_ROOT/registry/repos.yml" "$FAKE/registry/repos.yml"       # present, so the [ -f ] guard passes
cp "$REPO_ROOT/scripts/sync-build-config.sh" "$FAKE/scripts/sync-build-config.sh"
# The build-config members are the rows that USED to satisfy the manifest floor on their own. They are
# copied REAL, so the fail-open this fixture guards is reproducible here exactly as it was measured.
mapfile -t BC_FILES < <(sed -n '/^FILES=(/,/^)/{/^FILES=(/d;/^)/d;s/#.*//;s/[[:space:]"]//g;/^$/d;p}' \
                          "$REPO_ROOT/scripts/sync-build-config.sh")
[ "${#BC_FILES[@]}" -gt 0 ] \
  || { echo "::error::fixture: could not derive the build-config FILES set — nothing to assert."; exit 1; }
for rel in "${BC_FILES[@]}"; do
  mkdir -p "$FAKE/dist/dotnet/$(dirname "$rel")"
  cp "$REPO_ROOT/dist/dotnet/$rel" "$FAKE/dist/dotnet/$rel"
done

FAKE_KIT="$FAKE/src/FS.GG.Kit/stage-kit.sh"
OUT="$WORK/out"

# stub_repos_sh <skill-body> <client-body> <config-src-body> <config-dest-body>
# The stub answers `digest` for real (stage-kit.sh uses it for the build-config rows) and dispatches
# `kit` on the --field/--kind PAIR: one body that answered every query would let a nonsense list
# satisfy the assertions below by accident.
stub_repos_sh() {
  { printf '#!/usr/bin/env bash\n'
    printf 'if [ "$1" = digest ]; then sha256sum "$2" | cut -d" " -f1; exit 0; fi\n'
    printf '[ "$1" = kit ] || { echo "stub: unexpected subcommand $1" >&2; exit 2; }\n'
    printf 'field=; kind=\n'
    printf 'while [ $# -gt 0 ]; do case "$1" in\n'
    printf '  --field) field="$2"; shift 2 ;;\n'
    printf '  --kind)  kind="$2";  shift 2 ;;\n'
    printf '  *) shift ;;\n'
    printf 'esac; done\n'
    printf 'case "$field/$kind" in\n'
    printf '  source/skill)  %s ;;\n' "$1"
    printf '  source/client) %s ;;\n' "$2"
    printf '  source/config) %s ;;\n' "$3"
    printf '  dest/config)   %s ;;\n' "$4"
    printf '  *) echo "stub: unexpected kit query $field/$kind" >&2; exit 2 ;;\n'
    printf 'esac\n'
  } > "$FAKE/scripts/repos.sh"
}
DIES='echo "repos.sh: bad registry" >&2; exit 2'
EMPTY='exit 0'
H_SKILL='printf "%s\n" .claude/skills/check-board'
H_CLIENT='printf "%s\n" scripts/fsgg-coord'
H_CFG_SRC='printf "%s\n" dist/dotnet/.config/dotnet-tools.json'
H_CFG_DST='printf "%s\n" .config/dotnet-tools.json'

# expect_stage <name> <want-rc> <want-output-regex> [<script>]
expect_stage() {
  local n="$1" want="$2" re="$3" script="${4:-$FAKE_KIT}" out rc=0
  rm -rf "$OUT"
  out="$(bash "$script" "$OUT" 2>&1)" || rc=$?
  if [ "$rc" -eq "$want" ] && printf '%s' "$out" | grep -qE -- "$re"; then ok "$n"
  else bad "$n" "want rc=$want matching /$re/; got rc=$rc: $out"; fi
}

# ---- 1-2. THE SKILL READ. -------------------------------------------------------------------------
stub_repos_sh "$DIES" "$H_CLIENT" "$H_CFG_SRC" "$H_CFG_DST"
expect_stage "skills: a reader that DIES fails closed (not a skill-less kit)" 2 \
  'could not read the kit skill list'

# THE MEASURED PRE-FIX STATE, asserted on the ARTIFACT rather than on any message: repos.sh dead for
# EVERY kit read — the "PyYAML removed" reproduction on .github#1614 — which staged `2 kit file(s)`,
# both build-config, and exited 0. Every other leg here would still pass a script that merely died a
# bit later for an unrelated reason; this one fails if and only if the fail-open is back.
stub_repos_sh "$DIES" "$DIES" "$DIES" "$DIES"
rm -rf "$OUT"
allout="$(bash "$FAKE_KIT" "$OUT" 2>&1)"; allrc=$?
staged_kinds="$(cut -f1 "$OUT/kit-manifest.tsv" 2>/dev/null | sort -u | tr '\n' ',')"
if [ "$allrc" -ne 0 ] && [ "$staged_kinds" != "build-config," ]; then
  ok "a repos.sh that is dead for EVERY read stages nothing and exits non-zero"
else
  bad "a wholly dead repos.sh still staged a package" \
      "rc=$allrc kinds='$staged_kinds' — this IS #1614: $allout"
fi

stub_repos_sh "$EMPTY" "$H_CLIENT" "$H_CFG_SRC" "$H_CFG_DST"
expect_stage "skills: an EMPTY skill list fails closed (not an empty loop)" 2 \
  "no 'kind: skill' kit item declared"

# ---- 3-4. THE CLIENT READ. ------------------------------------------------------------------------
stub_repos_sh "$H_SKILL" "$DIES" "$H_CFG_SRC" "$H_CFG_DST"
expect_stage "client: a reader that DIES fails closed" 2 'could not read the kit client list'
stub_repos_sh "$H_SKILL" "$EMPTY" "$H_CFG_SRC" "$H_CFG_DST"
expect_stage "client: an EMPTY client list fails closed" 2 "no 'kind: client' kit item declared"

# ---- 5-6. THE CONFIG PAIR — the `paste <(…) <(…)` that swallowed BOTH reads. -----------------------
stub_repos_sh "$H_SKILL" "$H_CLIENT" "$DIES" "$H_CFG_DST"
expect_stage "config: a dead SOURCE read fails closed" 2 'could not read the kit config sources'
stub_repos_sh "$H_SKILL" "$H_CLIENT" "$H_CFG_SRC" "$DIES"
expect_stage "config: a dead DEST read fails closed" 2 'could not read the kit config dests'

# ---- 7. THE CONTROL: a healthy reader still gets PAST the reads. -----------------------------------
# Without this, every leg above is satisfied by a script that refuses unconditionally — and each of them
# is rc 2, so only the MESSAGE can tell them apart. The fake root has no .claude/skills, so a healthy
# read must land on the kit-source check that lives AFTER the reads.
stub_repos_sh "$H_SKILL" "$H_CLIENT" "$H_CFG_SRC" "$H_CFG_DST"
expect_stage "control: a healthy reader reaches the staging step" 2 'canonical kit skill source missing'

# ---- 8. THE MANIFEST FLOOR, on its own. -----------------------------------------------------------
# The empty-list guards above now catch a silent registry before the floor is reached, so the floor is
# defence in depth — and defence in depth that is never driven is a claim, not a check. Drive it by
# REMOVING those guards from a copy of the real script (keyed on the message they carry, not on
# layout), then feeding it the exact pre-fix state: reads that succeed and enumerate nothing.
#
# `[ -s kit-manifest.tsv ]` alone WOULD pass here — the build-config rows are staged, and this leg
# asserts they are on disk, so the old floor's satisfaction is demonstrated rather than assumed.
guards=$(grep -c 'kit item declared in \$REG' "$KIT_SRC" || true)
if [ "$guards" -eq 2 ]; then
  ok "floor: located both empty-list guards to remove ($guards)"
else
  bad "floor: expected 2 empty-list guards in stage-kit.sh, found $guards" \
      "re-derive this mutation — the guards were reworded, so the floor leg below is not testing what it claims"
fi
# The mutant must sit at <fake root>/<dir>/<dir>/stage-kit.sh, or it resolves a different source root.
MUTDIR="$FAKE/src/FS.GG.Kit.mutant"
mkdir -p "$MUTDIR"
grep -v 'kit item declared in \$REG' "$KIT_SRC" > "$MUTDIR/stage-kit.sh"
stub_repos_sh "$EMPTY" "$EMPTY" "$EMPTY" "$EMPTY"
expect_stage "floor: build-config rows ALONE no longer clear the manifest floor" 2 \
  "carries no 'skill' row" "$MUTDIR/stage-kit.sh"
[ -f "$OUT/build-config/${BC_FILES[0]}" ] \
  && ok "floor: ...and the build-config rows that used to satisfy it WERE staged" \
  || bad "floor: build-config rows were not staged" "leg proves nothing without them: $(ls -R "$OUT" 2>&1)"

echo "kit-package fixture: $pass passed, $failcount failed"
[ "$failcount" -eq 0 ] || exit 1
