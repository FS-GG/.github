#!/usr/bin/env bash
# Fixture for the reusable skill-union assertion — FS-GG/.github#111 (ADR-0014 P3.G3.1).
#
# Proves the assertion PASSES on a byte-identical union and FAILS (non-zero) on each violation
# class ADR-0014 must catch: divergent bytes, a partitioned root, a dangling (undeclared) skill,
# and a manifest-digest drift. This is the "a fixture proves it fails" acceptance line of #111.
#
# Self-contained: builds throwaway product trees under a temp dir, no network, no other repos.

set -euo pipefail

HERE="$(cd "$(dirname "$0")" && pwd)"
ASSERT="$HERE/../../scripts/skill-union-assert.sh"  # always invoked as `bash "$ASSERT"`

WORK="$(mktemp -d "${TMPDIR:-/tmp}/skill-union-fixture.XXXXXX")"
trap 'rm -rf "$WORK"' EXIT

ROOTS=".claude/skills .codex/skills .agents/skills"
pass=0; failcount=0

# expect_pass <name> <product-dir> [manifest]
expect_pass() {
  local name="$1" prod="$2" manifest="${3:-}"
  local args=(--product "$prod" --roots "$ROOTS")
  [ -n "$manifest" ] && args+=(--manifest "$manifest")
  if bash "$ASSERT" "${args[@]}" >"$WORK/out" 2>&1; then
    echo "PASS  (expected pass) $name"; pass=$((pass+1))
  else
    echo "FAIL  (expected pass, got failure) $name"; sed 's/^/    | /' "$WORK/out"; failcount=$((failcount+1))
  fi
}

# expect_fail <name> <product-dir> [manifest]
expect_fail() {
  local name="$1" prod="$2" manifest="${3:-}"
  local args=(--product "$prod" --roots "$ROOTS")
  [ -n "$manifest" ] && args+=(--manifest "$manifest")
  if bash "$ASSERT" "${args[@]}" >"$WORK/out" 2>&1; then
    echo "FAIL  (expected failure, got pass) $name"; sed 's/^/    | /' "$WORK/out"; failcount=$((failcount+1))
  else
    echo "PASS  (expected fail) $name — $(grep -m1 '::error::\[' "$WORK/out" | sed 's/::error:://' || true)"
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
# canonical `--digest` generator, so the fixture and the checker can never drift.
digest() { bash "$ASSERT" --digest "$1"; } # <skill-dir>
MANIFEST="$WORK/manifest.json"
cat > "$MANIFEST" <<EOF
{ "roots": [".claude/skills", ".codex/skills", ".agents/skills"],
  "skills": [
    { "id": "alpha", "scope": "process", "sha256": "$(digest "$GOOD/.claude/skills/alpha")" },
    { "id": "beta",  "scope": "product", "sha256": "$(digest "$GOOD/.claude/skills/beta")" }
  ] }
EOF

# --- 1. coherent union, no manifest → PASS ---
expect_pass "coherent union (content-equality only)" "$GOOD"

# --- 2. coherent union WITH manifest → PASS ---
expect_pass "coherent union (+ manifest digests)" "$GOOD" "$MANIFEST"

# --- 3. divergent bytes: one root's skill body differs → FAIL ---
DIV="$WORK/divergent"; build_good "$DIV"
printf '# alpha skill TAMPERED\n' > "$DIV/.codex/skills/alpha/SKILL.md"
expect_fail "divergent root (bytes differ across roots)" "$DIV"

# --- 4. partitioned: a skill missing from one root → FAIL ---
PART="$WORK/partitioned"; build_good "$PART"
rm -rf "$PART/.agents/skills/beta"
expect_fail "partitioned root (skill absent from one root)" "$PART"

# --- 5. dangling: an extra skill present + identical in EVERY root but not declared by the
#        manifest → FAIL (exercises the [dangling] branch, not the earlier partition check) ---
DANG="$WORK/dangling"; build_good "$DANG"
for r in $ROOTS; do mk_skill "$DANG/$r" gamma "# undeclared gamma"; done
expect_fail "dangling skill (present in all roots but undeclared by manifest)" "$DANG" "$MANIFEST"

# --- 6. manifest drift: bytes identical across roots but != declared digest → FAIL ---
DRIFT="$WORK/drift"; build_good "$DRIFT"
for r in $ROOTS; do printf '# alpha skill v2\n' > "$DRIFT/$r/alpha/SKILL.md"; done  # identical across roots, but no longer matches manifest
expect_fail "manifest drift (identical across roots, != declared digest)" "$DRIFT" "$MANIFEST"

echo "--------------------------------------------"
echo "skill-union fixture: $pass passed, $failcount failed"
[ "$failcount" -eq 0 ] || exit 1
