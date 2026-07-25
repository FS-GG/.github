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
# Check 4 (ADR-0017, --params): with a scaffold-provenance.json the assertion evaluates each
# declared skill's materializes-when and adds [missing] (declared∧condition-true∧absent — the
# blind spot that shipped a dropped fs-gg-project) and [unexpected] (present∧condition-false).
# Without --params it degrades to the superset semantics above exactly — proven here too.
#
# Self-contained: builds throwaway product trees under a temp dir, no network, no other repos.

set -euo pipefail

HERE="$(cd "$(dirname "$0")" && pwd)"

# THE SUBJECT IS SWAPPABLE, AND THAT IS THE POINT (#843). By default this is the source script. The
# `skill-union-bundle` gate re-runs this ENTIRE suite with SKILL_UNION_ASSERT pointed at the generated
# `dist/skill-union-assert.sh`, from a directory that has no `lib/` siblings — so the bundle external
# consumers actually fetch is proven behaviourally identical to the script this repo runs, case for case,
# rather than merely proven to start. A bundle that only proved it starts is how #843 stayed invisible for
# two weeks: the standalone fetch was never exercised by anything.
ASSERT="${SKILL_UNION_ASSERT:-$HERE/../../scripts/skill-union-assert.sh}"  # always invoked as `bash "$ASSERT"`
[ -f "$ASSERT" ] || { echo "no assertion script at $ASSERT" >&2; exit 2; }

WORK="$(mktemp -d "${TMPDIR:-/tmp}/skill-union-fixture.XXXXXX")"
trap 'rm -rf "$WORK"' EXIT

ROOTS=".claude/skills .codex/skills .agents/skills"
pass=0; failcount=0

# expect_rc <name> <want-rc> [raw assertion args...]
# Invokes the assertion with EXACTLY the args given — no --product/--roots injected — so a usage
# error can be exercised on its own terms. expect_pass/expect_fail cannot express this.
expect_rc() {
  local name="$1" want="$2"; shift 2
  local rc=0
  bash "$ASSERT" "$@" >"$WORK/out" 2>&1 || rc=$?
  if [ "$rc" -eq "$want" ]; then
    echo "PASS  $name"; pass=$((pass+1))
  else
    echo "FAIL  (want rc=$want, got $rc) $name"; sed 's/^/    | /' "$WORK/out"; failcount=$((failcount+1))
  fi
}

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

# expect_die <name> <product-dir> [extra assertion args...]
# A MISCONFIGURATION must exit 2 (the `die` path — a bad flag combination, a missing file), NOT 1
# (a real violation) or 0. Used for `--params` without `--manifest` (the conditions live on the
# manifest, so there is nothing to evaluate against).
expect_die() {
  local name="$1" prod="$2"; shift 2
  local args=(--product "$prod" --roots "$ROOTS" "$@")
  local rc=0
  bash "$ASSERT" "${args[@]}" >"$WORK/out" 2>&1 || rc=$?
  if [ "$rc" -eq 2 ]; then
    echo "PASS  (expected misconfiguration exit 2) $name"; pass=$((pass+1))
  else
    echo "FAIL  (expected misconfiguration exit 2, got $rc) $name"; sed 's/^/    | /' "$WORK/out"; failcount=$((failcount+1))
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

# A leading UTF-8 BOM must NEVER enter the digest — the byte-for-byte invariant fsgg-skill-registry-check's
# canonical_digest (Fsgg.SkillMirror.sha256) holds. The registry check has a BOM case (tests/skill-registry);
# this is the SHELL-side arm of the same invariant (.github#384). A BOM'd SKILL.md and a BOM-free SKILL.md
# with identical body must produce the SAME digest, equal to sha256sum of the BOM-free bytes.
BOMDIR="$WORK/bom/skill"; mkdir -p "$BOMDIR"
printf '\xef\xbb\xbf# alpha skill\n' > "$BOMDIR/SKILL.md"       # same body as GOOD/alpha, prefixed with a BOM
got_bom="$(digest "$BOMDIR")"
raw_bom="$(sha256sum "$BOMDIR/SKILL.md" | cut -d' ' -f1)"        # hash WITH the BOM — what a naive digest would give
if [ "$got_bom" = "$want_raw" ] && [ "$got_bom" != "$raw_bom" ]; then
  echo "PASS  (expected pass) --digest strips a leading BOM (== BOM-free body, != raw BOM'd bytes)"; pass=$((pass+1))
else
  echo "FAIL  --digest BOM handling: got $got_bom, want BOM-free $want_raw (raw-with-BOM $raw_bom)"; failcount=$((failcount+1))
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

# --- 7. condition-aware classes (ADR-0017, --params) -------------------------------------------
# A scaffold provenance carrying effectiveParameters. profile=game / lifecycle=spec-kit /
# feedback=true — chosen so `profile == game`, `feedback == true and lifecycle == spec-kit` are
# TRUE and `profile == app`, `profile == sample-pack …` are FALSE, exercising both directions.
PROV="$WORK/prov-game.json"
cat > "$PROV" <<'EOF'
{ "effectiveParameters": { "profile": "game", "lifecycle": "spec-kit", "feedback": true } }
EOF

# 7a. [missing]: a declared skill whose materializes-when is TRUE for these params but which is
#     materialized in NO root — the exact fs-gg-project defect ADR-0017 §C2 exists to catch.
MISS="$WORK/cond-missing"; build_good "$MISS"           # alpha + beta present in all roots
MAN_MISS="$WORK/man-missing.json"
cat > "$MAN_MISS" <<EOF
{ "skills": [
  { "id": "alpha", "scope": "process", "sha256": "$(digest "$MISS/.claude/skills/alpha")", "materializes-when": "always" },
  { "id": "beta",  "scope": "product", "sha256": "$(digest "$MISS/.claude/skills/beta")",  "materializes-when": "profile in [app, game]" },
  { "id": "fs-gg-project", "scope": "product", "sha256": "0000000000000000000000000000000000000000000000000000000000000000", "materializes-when": "profile == game" }
] }
EOF
expect_fail "condition-aware [missing] (declared∧true∧absent — the fs-gg-project case)" missing "$MISS" --manifest "$MAN_MISS" --params "$PROV"

# 7b. …and the SAME manifest WITHOUT --params keeps today's superset semantics exactly: the
#     absence is blanket-tolerated → PASS. This is the "opt-in, no caller forced to change" line.
expect_pass "no --params keeps superset semantics (declared∧absent tolerated)" "$MISS" --manifest "$MAN_MISS"

# 7c. [unexpected]: a skill present + identical in every root but whose materializes-when is FALSE
#     for these params (declared for profile==app; we are profile==game) → materialized off-profile.
UNEXP="$WORK/cond-unexpected"; build_good "$UNEXP"
for r in $ROOTS; do mk_skill "$UNEXP/$r" off-profile "# off-profile skill"; done
MAN_UNEXP="$WORK/man-unexpected.json"
cat > "$MAN_UNEXP" <<EOF
{ "skills": [
  { "id": "alpha", "scope": "process", "sha256": "$(digest "$UNEXP/.claude/skills/alpha")", "materializes-when": "always" },
  { "id": "beta",  "scope": "product", "sha256": "$(digest "$UNEXP/.claude/skills/beta")",  "materializes-when": "profile in [app, game]" },
  { "id": "off-profile", "scope": "product", "sha256": "$(digest "$UNEXP/.claude/skills/off-profile")", "materializes-when": "profile == app" }
] }
EOF
expect_fail "condition-aware [unexpected] (present∧false — materialized off-profile)" unexpected "$UNEXP" --manifest "$MAN_UNEXP" --params "$PROV"

# 7d. JUSTIFIED absence + compound-true present: with --params a declared∧condition-FALSE∧absent
#     skill is legitimate (not blanket-tolerated — justified), and a present skill whose compound
#     `feedback == true and lifecycle == spec-kit` predicate is TRUE passes the digest check → PASS.
JUST="$WORK/cond-justified"; build_good "$JUST"
for r in $ROOTS; do mk_skill "$JUST/$r" feedback-capture "# feedback capture skill"; done
MAN_JUST="$WORK/man-justified.json"
cat > "$MAN_JUST" <<EOF
{ "skills": [
  { "id": "alpha", "scope": "process", "sha256": "$(digest "$JUST/.claude/skills/alpha")", "materializes-when": "always" },
  { "id": "beta",  "scope": "product", "sha256": "$(digest "$JUST/.claude/skills/beta")",  "materializes-when": "profile in [app, game]" },
  { "id": "feedback-capture", "scope": "product", "sha256": "$(digest "$JUST/.claude/skills/feedback-capture")", "materializes-when": "feedback == true and lifecycle == spec-kit" },
  { "id": "fs-gg-samples", "scope": "product", "sha256": "0000000000000000000000000000000000000000000000000000000000000000", "materializes-when": "profile == sample-pack and lifecycle == spec-kit" }
] }
EOF
expect_pass "condition-aware justified absence + compound-true present (feedback∧spec-kit)" "$JUST" --manifest "$MAN_JUST" --params "$PROV"

# 7e. --params without --manifest is a misconfiguration (nothing declares the conditions) → exit 2.
expect_die "--params without --manifest is a misconfiguration (exit 2)" "$MISS" --params "$PROV"

# 7f. EVERY declared-required skill dropped (roots exist but are empty) is a genuine coherence
#     failure [missing]/exit 1 — it must NOT be masked by the empty-union misconfiguration die
#     (exit 2). Guards the check-4-vs-skill_ct==0 ordering.
EMPTY="$WORK/cond-empty"
for r in $ROOTS; do mkdir -p "$EMPTY/$r"; done          # roots present but hold no skills
expect_fail "condition-aware [missing] survives an empty union (not masked by the misconfig die)" missing "$EMPTY" --manifest "$MAN_MISS" --params "$PROV"

# --- 7g-7i. predicate-grammar coverage (#385) --------------------------------------------------
# The materializes-when evaluator claims to mirror `normalize_when` 1:1, but three constructs
# (`!=`, `or`, bare true/false) were exercised by no fixture, and a QUOTED string literal was
# ALWAYS FALSE — the RHS kept its surrounding quotes (`"game"`) while PARAM values arrive unquoted
# (`game`), so every quoted predicate mis-evaluated to false. That is a fail-open: a required skill
# reads as not-required. These cases pin each construct in BOTH truth directions.
PROV_G="$WORK/prov-grammar.json"
cat > "$PROV_G" <<'EOF'
{ "effectiveParameters": { "profile": "game", "lifecycle": "spec-kit", "feedback": true, "title": "Acme Corp" } }
EOF

# 7g. TRUE direction: quoted ==, quoted !=, quoted in[], or, bare true, and a quoted value WITH A
#     SPACE (`"Acme Corp"` — only a quoted literal can carry one) all evaluate TRUE, so every
#     present skill passes the digest check → PASS. Before the unquote fix each quoted predicate is
#     false, turning its skill [unexpected] and reddening this PASS — the regression this pins.
GRAM="$WORK/grammar-true"; build_good "$GRAM"
for r in $ROOTS; do
  for s in q-eq q-ne q-in disj lit spc; do mk_skill "$GRAM/$r" "$s" "# $s"; done
done
MAN_GRAM="$WORK/man-grammar-true.json"
cat > "$MAN_GRAM" <<EOF
{ "skills": [
  { "id": "alpha", "scope": "process", "sha256": "$(digest "$GRAM/.claude/skills/alpha")", "materializes-when": "always" },
  { "id": "beta",  "scope": "product", "sha256": "$(digest "$GRAM/.claude/skills/beta")",  "materializes-when": "always" },
  { "id": "q-eq", "scope": "product", "sha256": "$(digest "$GRAM/.claude/skills/q-eq")", "materializes-when": "profile == \"game\"" },
  { "id": "q-ne", "scope": "product", "sha256": "$(digest "$GRAM/.claude/skills/q-ne")", "materializes-when": "profile != \"app\"" },
  { "id": "q-in", "scope": "product", "sha256": "$(digest "$GRAM/.claude/skills/q-in")", "materializes-when": "profile in [\"app\", \"game\"]" },
  { "id": "disj", "scope": "product", "sha256": "$(digest "$GRAM/.claude/skills/disj")", "materializes-when": "profile == app or profile == game" },
  { "id": "lit",  "scope": "product", "sha256": "$(digest "$GRAM/.claude/skills/lit")",  "materializes-when": "true" },
  { "id": "spc",  "scope": "product", "sha256": "$(digest "$GRAM/.claude/skills/spc")",  "materializes-when": "title == \"Acme Corp\"" }
] }
EOF
expect_pass "grammar TRUE: quoted ==, !=, in[], or, bare true, quoted-space all evaluate true (present∧true)" "$GRAM" --manifest "$MAN_GRAM" --params "$PROV_G"

# 7h. Fail-open from the ABSENT direction: a required skill whose quoted-literal predicate is TRUE
#     but which is materialized in NO root must be [missing]. Pre-fix, `profile == "game"` was
#     always false, so the dropped skill read as a justified off-profile omission and shipped
#     silently — the same fail-open as 7g, caught from the opposite side.
QMISS="$WORK/grammar-missing"; build_good "$QMISS"        # alpha + beta present; q-req absent
MAN_QMISS="$WORK/man-grammar-missing.json"
cat > "$MAN_QMISS" <<EOF
{ "skills": [
  { "id": "alpha", "scope": "process", "sha256": "$(digest "$QMISS/.claude/skills/alpha")", "materializes-when": "always" },
  { "id": "beta",  "scope": "product", "sha256": "$(digest "$QMISS/.claude/skills/beta")",  "materializes-when": "always" },
  { "id": "q-req", "scope": "product", "sha256": "0000000000000000000000000000000000000000000000000000000000000000", "materializes-when": "profile == \"game\"" }
] }
EOF
expect_fail "grammar: quoted-literal-true on an ABSENT required skill ⇒ [missing] (fail-open catch)" missing "$QMISS" --manifest "$MAN_QMISS" --params "$PROV_G"

# 7i. FALSE direction, per construct: each present skill whose predicate is FALSE must be flagged
#     [unexpected]. Asserting EACH id (not just that some [unexpected] appears) stops one construct's
#     correct failure from masking another's mis-evaluation — and pins that `unquote` did not invert
#     a quoted `!=` (`profile != "game"` must stay FALSE when profile==game, which the pre-fix code
#     got wrong, reading `game != "game"` as true).
GFALSE="$WORK/grammar-false"; build_good "$GFALSE"
for r in $ROOTS; do
  for s in f-eq f-ne f-or f-lit; do mk_skill "$GFALSE/$r" "$s" "# $s"; done
done
MAN_GFALSE="$WORK/man-grammar-false.json"
cat > "$MAN_GFALSE" <<EOF
{ "skills": [
  { "id": "alpha", "scope": "process", "sha256": "$(digest "$GFALSE/.claude/skills/alpha")", "materializes-when": "always" },
  { "id": "beta",  "scope": "product", "sha256": "$(digest "$GFALSE/.claude/skills/beta")",  "materializes-when": "always" },
  { "id": "f-eq",  "scope": "product", "sha256": "$(digest "$GFALSE/.claude/skills/f-eq")",  "materializes-when": "profile == \"app\"" },
  { "id": "f-ne",  "scope": "product", "sha256": "$(digest "$GFALSE/.claude/skills/f-ne")",  "materializes-when": "profile != \"game\"" },
  { "id": "f-or",  "scope": "product", "sha256": "$(digest "$GFALSE/.claude/skills/f-or")",  "materializes-when": "profile == app or profile == sample-pack" },
  { "id": "f-lit", "scope": "product", "sha256": "$(digest "$GFALSE/.claude/skills/f-lit")", "materializes-when": "false" }
] }
EOF
rc=0
bash "$ASSERT" --product "$GFALSE" --roots "$ROOTS" --manifest "$MAN_GFALSE" --params "$PROV_G" >"$WORK/out" 2>&1 || rc=$?
notflagged=""
for s in f-eq f-ne f-or f-lit; do
  grep -q "::error::\[unexpected\] skill '$s'" "$WORK/out" || notflagged="$notflagged $s"
done
if [ "$rc" -eq 1 ] && [ -z "$notflagged" ]; then
  echo "PASS  (expected [unexpected] fail) grammar FALSE: quoted ==, quoted !=, all-false or, bare false each [unexpected]"; pass=$((pass+1))
else
  echo "FAIL  grammar FALSE direction (want rc=1 + every construct flagged; got rc=$rc, unflagged:$notflagged)"; sed 's/^/    | /' "$WORK/out"; failcount=$((failcount+1))
fi

# --- root-set resolution: --roots > $AGENT_SKILL_ROOTS > .agent-skill-roots > ADR-0011's three ----
# (.github#517) A tree can deliberately override the universal three-root default. This fixture keeps
# proving that override and precedence behavior; ADR-0065 removed the former automatic kit exception.
#
# The load-bearing case is the FIRST: it pins the product gate FAIL-CLOSED. The tempting fix for #517
# was "drop .codex/skills from the default" — that would silently stop catching a product whose
# producer never materialized .codex (ADR-0011's origin bug), turning this gate into the fail-OPEN
# family of #266/#292. Declaring roots narrows WHAT IS ASKED FOR; it must never weaken the answer.
echo "--- root-set resolution (.github#517) ---"
KIT="$WORK/kit"                                    # an intentional two-root tree, no declaration yet
mkdir -p "$KIT/.claude/skills/alpha" "$KIT/.agents/skills/alpha"
printf '# alpha skill\n' > "$KIT/.claude/skills/alpha/SKILL.md"
cp "$KIT/.claude/skills/alpha/SKILL.md" "$KIT/.agents/skills/alpha/SKILL.md"

expect_rc "roots: NO declaration ⇒ ADR-0011's three ⇒ absent .codex is a hard exit 2 (fail-CLOSED)" \
  2 --product "$KIT"

printf '# this tree intentionally supports two runtimes\n.claude/skills\n.agents/skills\n' \
  > "$KIT/.agent-skill-roots"                      # comments + newline-separated must parse

expect_rc "roots: .agent-skill-roots declares an intentional two-root override ⇒ BARE exits 0" \
  0 --product "$KIT"
expect_rc "roots: --roots still overrides the declaration (explicit wins)" \
  2 --product "$KIT" --roots ".claude/skills .codex/skills .agents/skills"

rc=0; AGENT_SKILL_ROOTS=".claude/skills .codex/skills .agents/skills" \
  bash "$ASSERT" --product "$KIT" >"$WORK/out" 2>&1 || rc=$?
if [ "$rc" -eq 2 ]; then
  echo "PASS  roots: \$AGENT_SKILL_ROOTS overrides the declaration (env wins over the file)"; pass=$((pass+1))
else
  echo "FAIL  (want rc=2) roots: \$AGENT_SKILL_ROOTS must override .agent-skill-roots"; sed 's/^/    | /' "$WORK/out"; failcount=$((failcount+1))
fi

# A declared root that is ABSENT is still a misconfiguration — the declaration says which roots this
# tree keeps, it does not excuse one of them going missing.
printf '.claude/skills\n.agents/skills\n.nope/skills\n' > "$KIT/.agent-skill-roots"
expect_rc "roots: a root the declaration NAMES but the tree lacks is still exit 2" 2 --product "$KIT"

printf '# only comments, no roots\n' > "$KIT/.agent-skill-roots"
expect_rc "roots: a comment-only/empty declaration is a misconfiguration (exit 2)" 2 --product "$KIT"

# A CRLF checkout (Windows, or .gitattributes eol=crlf) must not leave a `\r` on the last root of a
# line: the root '.claude/skills\r' does not exist, so the gate would report "configured root is
# absent" for a root that is right there — #517's own failure mode, pointing at an EXISTING root.
printf '.claude/skills\r\n.agents/skills\r\n' > "$KIT/.agent-skill-roots"
expect_rc "roots: a CRLF-checked-out declaration parses (no trailing \\r on the root)" 0 --product "$KIT"

# The PRODUCT gate never consults the subject's declaration. skill-union-assert.yml always passes
# --roots, precisely so a producer-generated tree cannot narrow the audit performed on it: a template
# that emitted a two-root declaration into a scaffolded product must NOT thereby make .codex/skills
# stop being checked. Explicit roots must beat the declaration — the fail-open this whole change
# refuses to introduce, asserted rather than merely commented.
printf '.claude/skills\n.agents/skills\n' > "$KIT/.agent-skill-roots"
expect_rc "roots: a tree's declaration CANNOT narrow an explicit --roots (the product gate's path)" \
  2 --product "$KIT" --roots ".claude/skills .codex/skills .agents/skills"

# ...and a declaration never softens a REAL violation inside the roots it declares.
printf '# alpha skill TAMPERED\n' > "$KIT/.agents/skills/alpha/SKILL.md"
rc=0; bash "$ASSERT" --product "$KIT" >"$WORK/out" 2>&1 || rc=$?
if [ "$rc" -eq 1 ] && grep -q "::error::\[divergent\] skill 'alpha'" "$WORK/out"; then
  echo "PASS  (expected divergent fail) roots: a declared root set still catches real drift (exit 1)"; pass=$((pass+1))
else
  echo "FAIL  (want rc=1 + [divergent]) roots: declaration must not weaken the union check (got rc=$rc)"; sed 's/^/    | /' "$WORK/out"; failcount=$((failcount+1))
fi

# --- a usage error is a misconfiguration (exit 2), never a union violation (exit 1) (#350) ---
# Every flag used to take its value through bash's `${2:?…}`, which exits 1 — the code this script
# reserves for "the union is violated". A typo'd command line reported itself as the very finding the
# script exists to produce. Both forms `${2:?…}` fired on are asserted, for every flag that takes a
# value: absent, and empty-but-present.
for flag in --product --roots --manifest --co-tenants --params --digest --eval-when; do
  expect_rc "usage: $flag with no value exits 2 (misconfig), not 1 (violation)"    2 "$flag"
  expect_rc "usage: $flag with an empty value exits 2, not 1"                      2 "$flag" ""
done
expect_rc "usage: an unknown flag still exits 2" 2 --bogus-flag
expect_rc "usage: --help still exits 0"          0 --help

# --- cross-impl conformance for the materializes-when grammar (ADR-0017, .github#398) -----------
# The grammar has TWO implementations (shell eval_condition here, Python normalize_when — no typed
# Fsgg.Registry leg; that validator is a doc-schema checker, not a predicate evaluator, .github#408)
# and a divergence fails OPEN (#292/#266). conformance.sh drives the shared fixture
# table through the shell evaluator (--eval-when) and round-trips each predicate through Python's
# normalize_when (--normalize-when), asserting neither changes the evaluated truth. Delegated to its
# own harness (own fixture file), folded into this fixture's pass/fail so CI's single entrypoint runs it.
echo "--- materializes-when cross-impl conformance (.github#398) ---"
if bash "$HERE/conformance.sh"; then
  echo "PASS  (cross-impl conformance) shell eval == normalize_when round-trip across the fixture table"; pass=$((pass+1))
else
  echo "FAIL  materializes-when cross-impl conformance diverged (see above)"; failcount=$((failcount+1))
fi

echo "--- skill guidance current-truth regression (.github#1410) ---"
if bash "$HERE/current-truth.sh"; then
  echo "PASS  lane narrowing and publishing current-truth semantics stay coherent"; pass=$((pass+1))
else
  echo "FAIL  skill guidance current-truth regression (see above)"; failcount=$((failcount+1))
fi

echo "--------------------------------------------"
echo "skill-union fixture: $pass passed, $failcount failed"
[ "$failcount" -eq 0 ] || exit 1
