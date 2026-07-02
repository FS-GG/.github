#!/usr/bin/env bash
# Fixture for the reusable skill-union assertion — FS-GG/.github#111 (ADR-0014 P3.G3.1).
#
# Proves the assertion PASSES on a byte-identical union and FAILS (non-zero) on each violation
# class ADR-0014 must catch: divergent bytes, a partitioned root, a dangling (undeclared) skill,
# and a manifest-digest drift. This is the "a fixture proves it fails" acceptance line of #111.
#
# Check 3 follows the producers' shipped manifest semantics (.github#120): digest =
# canonical-body sha256 of SKILL.md only; manifest = superset catalog (declared∧absent is fine);
# undeclared co-tenant process skills are admitted via --co-tenants globs.
#
# Self-contained: builds throwaway product trees under a temp dir, no network, no other repos.

set -euo pipefail

HERE="$(cd "$(dirname "$0")" && pwd)"
ASSERT="$HERE/../../scripts/skill-union-assert.sh"  # always invoked as `bash "$ASSERT"`

WORK="$(mktemp -d "${TMPDIR:-/tmp}/skill-union-fixture.XXXXXX")"
trap 'rm -rf "$WORK"' EXIT

ROOTS=".claude/skills .codex/skills .agents/skills"
pass=0; failcount=0

# expect_pass <name> <product-dir> [extra assertion args...]
expect_pass() {
  local name="$1" prod="$2"; shift 2
  local args=(--product "$prod" --roots "$ROOTS" "$@")
  if bash "$ASSERT" "${args[@]}" >"$WORK/out" 2>&1; then
    echo "PASS  (expected pass) $name"; pass=$((pass+1))
  else
    echo "FAIL  (expected pass, got failure) $name"; sed 's/^/    | /' "$WORK/out"; failcount=$((failcount+1))
  fi
}

# expect_fail <name> <class> <product-dir> [extra assertion args...]
# A genuine violation must (a) exit 1 — NOT 2, which is a misconfiguration `die` (a bad --flag,
# a missing root) that never exercised the violation path — and (b) emit the expected class tag
# (::error::[partitioned]/[divergent]/[dangling]/[drifted]). Asserting both stops a mere non-zero
# exit (esp. an exit-2 setup error) from masquerading as the violation class under test (review M2).
expect_fail() {
  local name="$1" class="$2" prod="$3"; shift 3
  local args=(--product "$prod" --roots "$ROOTS" "$@")
  local rc=0
  bash "$ASSERT" "${args[@]}" >"$WORK/out" 2>&1 || rc=$?
  if [ "$rc" -eq 0 ]; then
    echo "FAIL  (expected [$class] failure, got pass) $name"; sed 's/^/    | /' "$WORK/out"; failcount=$((failcount+1))
  elif [ "$rc" -eq 2 ]; then
    echo "FAIL  (expected [$class] failure, got misconfiguration die exit 2) $name"; sed 's/^/    | /' "$WORK/out"; failcount=$((failcount+1))
  elif ! grep -q "::error::\[$class\]" "$WORK/out"; then
    echo "FAIL  (expected [$class] failure, exit $rc but no [$class] tag) $name"; sed 's/^/    | /' "$WORK/out"; failcount=$((failcount+1))
  else
    echo "PASS  (expected [$class] fail) $name — $(grep -m1 "::error::\[$class\]" "$WORK/out" | sed 's/::error:://' || true)"
    pass=$((pass+1))
  fi
}

# --- build a coherent baseline product: 2 skills, identical across all 3 roots ---
mk_skill() { # <root-dir> <id> <body>
  mkdir -p "$1/$2"
  printf '%s\n' "$3" > "$1/$2/SKILL.md"
  mkdir -p "$1/$2/references"
  printf 'shared reference for %s\n' "$2" > "$1/$2/references/notes.md"
}
build_good() { # <product-dir>
  local p="$1"
  for r in $ROOTS; do
    mk_skill "$p/$r" alpha "# alpha skill"
    mk_skill "$p/$r" beta  "# beta skill"
  done
}

GOOD="$WORK/good"; build_good "$GOOD"

# Manifest declaring both skills with their real digests — computed by the assertion's own
# canonical `--digest` generator (canonical-body sha256 of SKILL.md, the producers' shipped
# algorithm), so the fixture and the checker can never drift. It also declares `omega`, a skill
# materialized NOWHERE — the manifest is a superset catalog, so that must be legitimate.
digest() { bash "$ASSERT" --digest "$1"; } # <skill-dir>
MANIFEST="$WORK/manifest.json"
cat > "$MANIFEST" <<EOF
{ "roots": [".claude/skills", ".codex/skills", ".agents/skills"],
  "skills": [
    { "id": "alpha", "scope": "process", "sha256": "$(digest "$GOOD/.claude/skills/alpha")" },
    { "id": "beta",  "scope": "product", "sha256": "$(digest "$GOOD/.claude/skills/beta")" },
    { "id": "omega", "scope": "product", "sha256": "0000000000000000000000000000000000000000000000000000000000000000" }
  ] }
EOF

# The canonical digest must equal raw `sha256sum SKILL.md` — the producers' shipped algorithm
# (Fsgg.SkillMirror / fs-gg-ui manifest, verified byte-for-byte in .github#120).
want_raw="$(sha256sum "$GOOD/.claude/skills/alpha/SKILL.md" | cut -d' ' -f1)"
got_gen="$(digest "$GOOD/.claude/skills/alpha")"
if [ "$got_gen" = "$want_raw" ]; then
  echo "PASS  (expected pass) --digest == sha256sum SKILL.md (producer algorithm)"; pass=$((pass+1))
else
  echo "FAIL  --digest ($got_gen) != sha256sum SKILL.md ($want_raw)"; failcount=$((failcount+1))
fi

# --- 1. coherent union, no manifest → PASS ---
expect_pass "coherent union (content-equality only)" "$GOOD"

# --- 2. coherent union WITH manifest (incl. declared-but-absent 'omega') → PASS ---
expect_pass "coherent union (+ superset-catalog manifest)" "$GOOD" --manifest "$MANIFEST"

# --- 3. divergent bytes: one root's skill body differs → FAIL ---
DIV="$WORK/divergent"; build_good "$DIV"
printf '# alpha skill TAMPERED\n' > "$DIV/.codex/skills/alpha/SKILL.md"
expect_fail "divergent root (bytes differ across roots)" divergent "$DIV"

# --- 3b. divergent NON-SKILL.md bytes: references/** differs in one root → FAIL (the multi-file
#         remainder is covered by cross-root identity, not the SKILL.md digest) ---
DIVREF="$WORK/divergent-ref"; build_good "$DIVREF"
printf 'tampered reference\n' > "$DIVREF/.codex/skills/alpha/references/notes.md"
expect_fail "divergent root (references/** differ, SKILL.md identical)" divergent "$DIVREF" --manifest "$MANIFEST"

# --- 4. partitioned: a skill missing from one root → FAIL (with the manifest supplied:
#        declared∧present-in-SOME-roots is still a partition, not a catalog skip) ---
PART="$WORK/partitioned"; build_good "$PART"
rm -rf "$PART/.agents/skills/beta"
expect_fail "partitioned root (skill absent from one root)" partitioned "$PART"
expect_fail "partitioned root (declared skill absent from one root, manifest supplied)" partitioned "$PART" --manifest "$MANIFEST"

# --- 5. dangling: an extra skill present + identical in EVERY root but not declared by the
#        manifest → FAIL (exercises the [dangling] branch, not the earlier partition check) ---
DANG="$WORK/dangling"; build_good "$DANG"
for r in $ROOTS; do mk_skill "$DANG/$r" gamma "# undeclared gamma"; done
expect_fail "dangling skill (present in all roots but undeclared by manifest)" dangling "$DANG" --manifest "$MANIFEST"

# --- 5b. co-tenant: undeclared process skills from a co-tenant producer are admitted by
#         --co-tenants globs; a non-matching undeclared skill still fails ---
COT="$WORK/cotenant"; build_good "$COT"
for r in $ROOTS; do
  mk_skill "$COT/$r" fs-gg-sdd-tasking "# co-tenant sdd process skill"
  mk_skill "$COT/$r" speckit-plan      "# co-tenant spec-kit process skill"
done
expect_pass "co-tenant skills admitted by --co-tenants globs" "$COT" --manifest "$MANIFEST" --co-tenants "fs-gg-sdd-* speckit-*"
for r in $ROOTS; do mk_skill "$COT/$r" gamma "# undeclared gamma"; done
expect_fail "dangling skill still fails alongside admitted co-tenants" dangling "$COT" --manifest "$MANIFEST" --co-tenants "fs-gg-sdd-* speckit-*"

# --- 6. manifest drift: bytes identical across roots but != declared digest → FAIL ---
DRIFT="$WORK/drift"; build_good "$DRIFT"
for r in $ROOTS; do printf '# alpha skill v2\n' > "$DRIFT/$r/alpha/SKILL.md"; done  # identical across roots, but no longer matches manifest
expect_fail "manifest drift (identical across roots, != declared digest)" drifted "$DRIFT" --manifest "$MANIFEST"

echo "--------------------------------------------"
echo "skill-union fixture: $pass passed, $failcount failed"
[ "$failcount" -eq 0 ] || exit 1
