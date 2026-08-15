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
# `.effectiveParameters` arrives in EITHER of two encodings of the same map — the object form, or the
# `{key,value}` array FS.GG.SDD.Artifacts actually emits — and the gate must be unable to tell them
# apart; anything else is a fail-closed exit 2 that names what it got (.github#2546, section 7j-7o).
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
{ "roots": [".claude/skills", ".agents/skills"],
  "skills": [
    { "id": "alpha", "scope": "process", "sha256": "$(digest "$GOOD/.claude/skills/alpha")" },
    { "id": "beta",  "scope": "product", "sha256": "$(digest "$GOOD/.claude/skills/beta")" },
    { "id": "omega", "scope": "product", "sha256": "0000000000000000000000000000000000000000000000000000000000000000" }
  ] }
EOF

# Additive v2 shape: the legacy sha256 remains for old readers while new readers close over the
# complete directory, including reference bytes and executable mode.
V2_MANIFEST="$WORK/manifest-v2.json"
alpha_skill="$(sha256sum "$GOOD/.claude/skills/alpha/SKILL.md" | cut -d' ' -f1)"
alpha_ref="$(sha256sum "$GOOD/.claude/skills/alpha/references/notes.md" | cut -d' ' -f1)"
beta_skill="$(sha256sum "$GOOD/.claude/skills/beta/SKILL.md" | cut -d' ' -f1)"
beta_ref="$(sha256sum "$GOOD/.claude/skills/beta/references/notes.md" | cut -d' ' -f1)"
cat > "$V2_MANIFEST" <<EOF
{ "schemaVersion": 2, "skills": [
  { "id": "alpha", "sha256": "$alpha_skill", "files": [
    { "path": "SKILL.md", "sha256": "$alpha_skill", "executable": false },
    { "path": "references/notes.md", "sha256": "$alpha_ref", "executable": false }
  ] },
  { "id": "beta", "sha256": "$beta_skill", "files": [
    { "path": "SKILL.md", "sha256": "$beta_skill", "executable": false },
    { "path": "references/notes.md", "sha256": "$beta_ref", "executable": false }
  ] }
] }
EOF

# The canonical digest must equal raw `sha256sum SKILL.md` FOR THIS FIXTURE — the producers' shipped
# algorithm (Fsgg.SkillMirror / fs-gg-ui manifest, verified byte-for-byte in .github#120).
#
# THE EQUALITY IS CONDITIONAL, AND THE CONDITION IS WHY THIS FIXTURE STILL HOLDS (.github#1547): the
# canonical digest strips a leading BOM and folds CRLF->LF, so it equals `sha256sum` only for a
# BOM-free, LF-only body. `alpha`'s SKILL.md is exactly that, deliberately — this vector's job is to
# pin that the algorithm adds NOTHING for the ordinary case a producer actually ships. The cases
# where the two DIVERGE are `digestVectors` in skillmirror.fixtures.json, measured against the
# library itself; asserting them here against `sha256sum` would just re-hardcode the wrong answer.
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

# AN UNREADABLE SKILL.md IS A FAILURE, NEVER A DIGEST (.github#1547, epic #266).
#
# THIS PINS A FAIL-OPEN #1547 ITSELF SHIPPED AND AN ADVERSARIAL REVIEW CAUGHT. The first cut of the
# CRLF fold read the body with `body="$(cat "$f"; printf x)"`. A command substitution's exit status is
# that of its LAST command, so `printf`'s success MASKED `cat`'s failure: a SKILL.md the gate could not
# read produced sha256("") — e3b0c442… — and exit 0. A confident digest for bytes nobody read is the
# #266 shape exactly, and it silently killed the `if ! got="$(skill_digest …)"` arm that reports
# `[drifted] … has no SKILL.md to digest`. `--digest` must exit non-zero and print no digest.
UNREADABLE="$WORK/unreadable/skill"; mkdir -p "$UNREADABLE"
printf '# alpha skill\n' > "$UNREADABLE/SKILL.md"
chmod 000 "$UNREADABLE/SKILL.md"
if [ "$(id -u)" = "0" ]; then
  # root reads anything, so the case cannot be staged — say so rather than printing a PASS that
  # exercised nothing (a skip reported as a pass is the same disease one level up).
  echo "SKIP  --digest on an unreadable SKILL.md (running as root; chmod 000 is not a barrier)"
else
  # `|| unreadable_rc=$?`, not a bare assignment then `$?`: this file runs under `set -e`, so a
  # failing command substitution aborts the suite before the check can grade it — which would read as
  # the fixture vanishing rather than as the refusal it is testing for.
  unreadable_rc=0
  unreadable_out="$(digest "$UNREADABLE" 2>/dev/null)" || unreadable_rc=$?
  if [ "$unreadable_rc" -ne 0 ] && [ -z "$unreadable_out" ]; then
    echo "PASS  (expected fail) --digest refuses an unreadable SKILL.md (rc=$unreadable_rc, no digest)"; pass=$((pass+1))
  else
    echo "FAIL  --digest on an unreadable SKILL.md returned rc=$unreadable_rc out='$unreadable_out' — a digest for bytes it never read"; failcount=$((failcount+1))
  fi
fi
chmod 644 "$UNREADABLE/SKILL.md"

# --- 1. coherent union, no manifest → PASS ---
expect_pass "coherent union (content-equality only)" "$GOOD"

# --- 2. coherent union WITH manifest (incl. declared-but-absent 'omega') → PASS ---
expect_pass "coherent union (+ superset-catalog manifest)" "$GOOD" --manifest "$MANIFEST"
expect_pass "coherent union (+ v2 whole-directory manifest)" "$GOOD" --manifest "$V2_MANIFEST"

# --- 3. divergent bytes: one root's skill body differs → FAIL ---
DIV="$WORK/divergent"; build_good "$DIV"
printf '# alpha skill TAMPERED\n' > "$DIV/.agents/skills/alpha/SKILL.md"
expect_fail "divergent root (bytes differ across roots)" divergent "$DIV"

# --- 3b. divergent NON-SKILL.md bytes: references/** differs in one root → FAIL (the multi-file
#         remainder is covered by cross-root identity, not the SKILL.md digest) ---
DIVREF="$WORK/divergent-ref"; build_good "$DIVREF"
printf 'tampered reference\n' > "$DIVREF/.codex/skills/alpha/references/notes.md"
expect_fail "divergent root (references/** differ, SKILL.md identical)" divergent "$DIVREF" --manifest "$MANIFEST"

# --- v2 closed-directory fixtures ---------------------------------------------------------------
MISSING_REF="$WORK/missing-ref"; build_good "$MISSING_REF"
for r in $ROOTS; do rm "$MISSING_REF/$r/alpha/references/notes.md"; done
expect_fail "v2 manifest: missing declared reference in every root" drifted "$MISSING_REF" --manifest "$V2_MANIFEST"

STALE_MODE="$WORK/stale-mode"; build_good "$STALE_MODE"
for r in $ROOTS; do chmod a+x "$STALE_MODE/$r/alpha/references/notes.md"; done
expect_fail "v2 manifest: stale executable mode in every root" drifted "$STALE_MODE" --manifest "$V2_MANIFEST"

EXTRA_FILE="$WORK/extra-file"; build_good "$EXTRA_FILE"
for r in $ROOTS; do printf 'undeclared\n' > "$EXTRA_FILE/$r/alpha/extra.txt"; done
expect_fail "v2 manifest: extra undeclared file in every root" drifted "$EXTRA_FILE" --manifest "$V2_MANIFEST"

ONE_ROOT_META="$WORK/one-root-metadata"; build_good "$ONE_ROOT_META"
mkdir -p "$ONE_ROOT_META/.agents/skills/alpha/agents"
printf 'interface:\n  display_name: Alpha\n' > "$ONE_ROOT_META/.agents/skills/alpha/agents/openai.yaml"
expect_fail "one-root-only agents/openai.yaml partitions directory bytes" divergent "$ONE_ROOT_META" --manifest "$V2_MANIFEST"

# --- 4. partitioned: a skill missing from one root → FAIL (with the manifest supplied:
#        declared∧present-in-SOME-roots is still a partition, not a catalog skip) ---
PART="$WORK/partitioned"; build_good "$PART"
rm -rf "$PART/.agents/skills/beta"
expect_fail "partitioned root (skill absent from one root)" partitioned "$PART"
expect_fail "partitioned root (declared skill absent from one root, manifest supplied)" partitioned "$PART" --manifest "$MANIFEST"

# --- 4b. THE KIT-COHERENT SUBSET IS NOT THE UNION (#1504) ----------------------------------------
#
# This is the negative fixture behind the `skill-union` receiver capability, and it reproduces the exact
# state three real trees were in on `origin/main`: Governance `.claude=15 .codex=4 .agents=4`, Rendering
# `.claude=50 .codex=4 .agents=50`, SDD `.claude=32 .codex=21 .agents=4`. Nothing had DRIFTED — every
# skill present in more than one root was byte-identical. Projections were MISSING, and the four
# kit-owned coordination skills (`registry/repos.yml`'s `kit:` block: cross-repo-coordination,
# intra-repo-parallel-work, check-board, pnext-item) were coherent in all three roots the whole time.
#
# So `coordination-coherence` — whose subject IS that four-skill subset — was green, correctly, on trees
# where codex and agent runtimes received a fraction of Claude's instruction set. ADR-0065's receiver
# rollout then read that subset green as restored three-root coherence, and the co-tenant process and
# product skills those trees were populated with by earlier writers survived the migration unexamined.
#
# The two legs are one claim, and it only holds because BOTH are asserted: the kit subset alone PASSES,
# and adding a partitioned co-tenant to that same coherent subset FAILS. Assert only the failure and the
# fixture proves nothing about the subset; assert only the pass and it proves nothing about the union.
KITSUB="$WORK/kit-subset"
for r in $ROOTS; do
  mk_skill "$KITSUB/$r" cross-repo-coordination  "# cross-repo-coordination"
  mk_skill "$KITSUB/$r" intra-repo-parallel-work "# intra-repo-parallel-work"
  mk_skill "$KITSUB/$r" check-board              "# check-board"
  mk_skill "$KITSUB/$r" pnext-item               "# pnext-item"
done
expect_pass "kit-owned four-skill subset is coherent across all three roots" "$KITSUB"

# A CO-TENANT PROCESS skill, in .claude only — Governance's eleven partitioned Speckit skills.
KITSUB_PROC="$WORK/kit-subset-partitioned-process"
cp -R "$KITSUB" "$KITSUB_PROC"
mk_skill "$KITSUB_PROC/.claude/skills" speckit-plan "# co-tenant process skill, .claude only"
expect_fail "a kit-coherent subset cannot green the union when a co-tenant PROCESS skill is partitioned" \
  partitioned "$KITSUB_PROC"

# A co-tenant PRODUCT skill in .claude + .agents but not .codex — Rendering's forty-six.
KITSUB_PROD="$WORK/kit-subset-partitioned-product"
cp -R "$KITSUB" "$KITSUB_PROD"
mk_skill "$KITSUB_PROD/.claude/skills" fs-gg-scene "# co-tenant product skill"
mk_skill "$KITSUB_PROD/.agents/skills" fs-gg-scene "# co-tenant product skill"
expect_fail "a kit-coherent subset cannot green the union when a co-tenant PRODUCT skill is partitioned" \
  partitioned "$KITSUB_PROD"

# And the partition is found even when the partitioned skill's bytes are IDENTICAL wherever it appears —
# the defect is missing projections, not divergent bytes, so a checker that only compared bytes across
# the roots that HAVE a skill would report green on all three real trees.
if bash "$ASSERT" --product "$KITSUB_PROD" --roots "$ROOTS" >"$WORK/out" 2>&1; then
  echo "FAIL  (expected [partitioned] failure, got pass) a byte-identical partition must still fail"
  sed 's/^/    | /' "$WORK/out"; failcount=$((failcount+1))
elif grep -q 'divergent' "$WORK/out"; then
  echo "FAIL  a MISSING projection was reported as divergent bytes"; sed 's/^/    | /' "$WORK/out"; failcount=$((failcount+1))
else
  echo "PASS  (expected [partitioned] fail) a partition of byte-identical skills is [partitioned], never [divergent]"
  pass=$((pass+1))
fi

# ...and that partition's ACCOUNTING is honest in the other direction too: the byte comparison DID happen
# over all five comparable ids and found them identical, so `byte-identical=5/5` is a claim the gate
# actually established, while `partitioned=1` carries the failure. Pinning the summary here is what stops
# a future "fix" from buying divergence coverage by reclassifying a partition (see 4c).
if [ "$(grep -m1 'skill(s) —' "$WORK/out" | sed 's/.*— //')" \
   = "in-every-root=4/5 partitioned=1 | byte-comparable=5 byte-compared=5 byte-identical=5/5 byte-differing=0 single-root=0" ]; then
  echo "PASS  a byte-identical partition is accounted partitioned=1 byte-differing=0, over a stated population"; pass=$((pass+1))
else
  echo "FAIL  byte-identical-partition summary accounting"; sed 's/^/    | /' "$WORK/out"; failcount=$((failcount+1))
fi

# --- 4c. PARTITIONED **AND** DIVERGENT — BOTH FACTS, AND A SUMMARY THAT CANNOT OVERSTATE (#1506) ---
#
# THE DEFECT THIS PINS. Check 1 SHORT-CIRCUITED: a `[partitioned]` id was `continue`d straight past the
# byte comparison, so its copies in the roots that DID hold it were never diffed — and `byte-identical=`
# then counted only the ids that survived to the comparison. On FS.GG.Rendering@main the gate printed
#
#     skill-union-assert: 50 skill(s) — present=4 byte-identical=4
#
# beside 46 `[partitioned]` ids, 30 of which DIFFERED between `.claude/skills` and `.agents/skills` —
# bytes the gate held in its hand and never compared. "Nothing is divergent; every skill present in more
# than one root is byte-identical" was read off that line and became the stated central premise of two
# downstream issues (FS.GG.Rendering#1080), whose whole repair plan was sized against it. This is the
# #266 family exactly: a comparison that did not happen, rendered as a confident negative answer.
#
# The tree below is that one in miniature: the four kit skills coherent in all three roots, plus six
# co-tenant ids present in `.claude` + `.agents`, ABSENT from `.codex`, and DIFFERING between the two
# roots they do occupy. Pre-fix it printed `present=4 byte-identical=4` and ZERO [divergent] lines — so
# this fixture fails against the original script, which is the property that makes it a regression test.
BOTH="$WORK/partitioned-and-divergent"
for r in $ROOTS; do
  mk_skill "$BOTH/$r" cross-repo-coordination  "# cross-repo-coordination"
  mk_skill "$BOTH/$r" intra-repo-parallel-work "# intra-repo-parallel-work"
  mk_skill "$BOTH/$r" check-board              "# check-board"
  mk_skill "$BOTH/$r" pnext-item               "# pnext-item"
done
for n in 1 2 3 4 5 6; do
  mk_skill "$BOTH/.claude/skills" "fs-gg-part-$n" "# fs-gg-part-$n as .claude holds it"
  mk_skill "$BOTH/.agents/skills" "fs-gg-part-$n" "# fs-gg-part-$n as .agents holds it — DIFFERENT BYTES"
done

rc=0; bash "$ASSERT" --product "$BOTH" --roots "$ROOTS" >"$WORK/out" 2>&1 || rc=$?
part_n="$(grep -c "::error::\[partitioned\]" "$WORK/out" || true)"
div_n="$(grep -c "::error::\[divergent\]" "$WORK/out" || true)"
sumline="$(grep -m1 'skill(s) —' "$WORK/out" || true)"

# (a) BOTH classes, for the SAME id — the conjunction the short-circuit made unreachable. Neither
# diagnostic may replace the other: the partition names a projection that was never written, the
# divergence names bytes that drifted, and a repair plan needs to know which of the two it faces.
if [ "$rc" -eq 1 ] \
   && grep -q "::error::\[partitioned\] skill 'fs-gg-part-1' is absent from root(s): .codex/skills" "$WORK/out" \
   && grep -q "::error::\[divergent\] skill 'fs-gg-part-1' differs between root '.claude/skills' and root(s): .agents/skills" "$WORK/out"; then
  echo "PASS  (expected [partitioned]+[divergent]) a partitioned skill whose present copies DIFFER reports BOTH facts, exit 1"; pass=$((pass+1))
else
  echo "FAIL  (want rc=1 + [partitioned] AND [divergent] on the same id) partitioned+divergent, got rc=$rc"; sed 's/^/    | /' "$WORK/out"; failcount=$((failcount+1))
fi

# (b) EVERY partitioned id is compared, not merely the first one that happens to be reached.
if [ "$part_n" -eq 6 ] && [ "$div_n" -eq 6 ]; then
  echo "PASS  all six partitioned ids were byte-compared: 6 [partitioned] + 6 [divergent] (pre-fix: 6 + 0)"; pass=$((pass+1))
else
  echo "FAIL  want 6 [partitioned] + 6 [divergent], got $part_n + $div_n"; sed 's/^/    | /' "$WORK/out"; failcount=$((failcount+1))
fi

# (c) THE SUMMARY'S HONESTY, which is the defect — not merely the ordering of the two checks. Every count
# carries the population it was taken over, so a partial comparison cannot be read as a complete one.
# Asserted on the exact bytes, because the reader who trusted `present=4 byte-identical=4` never opened
# the log: `byte-comparable` beside `byte-compared` says whether anything comparable went unexamined, and
# `byte-identical=4/10` cannot be read as covering ten ids.
want_sum="in-every-root=4/10 partitioned=6 | byte-comparable=10 byte-compared=10 byte-identical=4/10 byte-differing=6 single-root=0"
if [ "$(printf '%s' "$sumline" | sed 's/.*— //')" = "$want_sum" ]; then
  echo "PASS  summary states examined-vs-total: $want_sum"; pass=$((pass+1))
else
  echo "FAIL  summary must state examined-vs-total"; echo "    | want: … — $want_sum"; echo "    | got:  $sumline"; failcount=$((failcount+1))
fi

# (d) ...and a DENOMINATOR-FREE byte-identity count must never reach the summary again, on any tree. A
# bare `byte-identical=4` invites the reader to supply the population themselves, and they supply the
# generous one; that string is what two downstream issues were written off.
if printf '%s' "$sumline" | grep -qE 'byte-identical=[0-9]+([^0-9/]|$)'; then
  echo "FAIL  summary printed a denominator-free byte-identity count — the #1506 misreading"; echo "    | $sumline"; failcount=$((failcount+1))
else
  echo "PASS  no denominator-free byte-identity count can reach the summary"; pass=$((pass+1))
fi

# --- 4d. "NOTHING TO COMPARE" MUST NOT RENDER AS "COMPARED, AND IDENTICAL" (#1506 / #266) ----------
# A skill in exactly ONE of three roots has no second copy, so NO byte claim about it is possible. It is
# `[partitioned]`, and it must be counted as `single-root` — never folded into `byte-identical`, which is
# the same substitution of an unmade check for a passed one, one root further down. (This also pins the
# single-root ROOT SET, where the old `${ROOT_ARR[@]:1}` loop was empty and every id was counted
# byte-identical over a comparison that never ran.)
LONE="$WORK/single-root"
for r in $ROOTS; do mk_skill "$LONE/$r" alpha "# alpha skill"; done
mk_skill "$LONE/.claude/skills" speckit-plan "# present in exactly one root — nothing to compare it with"
rc=0; bash "$ASSERT" --product "$LONE" --roots "$ROOTS" >"$WORK/out" 2>&1 || rc=$?
sumline="$(grep -m1 'skill(s) —' "$WORK/out" || true)"
want_sum="in-every-root=1/2 partitioned=1 | byte-comparable=1 byte-compared=1 byte-identical=1/1 byte-differing=0 single-root=1"
if [ "$rc" -eq 1 ] && grep -q "::error::\[partitioned\] skill 'speckit-plan'" "$WORK/out" \
   && [ "$(printf '%s' "$sumline" | sed 's/.*— //')" = "$want_sum" ]; then
  echo "PASS  (expected [partitioned] fail) a one-root skill counts as single-root, never as byte-identical"; pass=$((pass+1))
else
  echo "FAIL  one-root accounting (rc=$rc)"; echo "    | want: … — $want_sum"; echo "    | got:  $sumline"; failcount=$((failcount+1))
fi

# ...and the same thing one level up, for a SINGLE-ROOT ROOT SET — the intentional single-runtime tree
# the missing-root hint itself suggests declaring. With one configured root there is no second copy of
# ANYTHING, so the honest byte-identity count is `0/0`. Pre-fix the "compare against roots 2..n" loop was
# empty here, fell through, and printed `byte-identical=2` — a byte-identity claim for every skill in a
# tree where no comparison was possible at all. Asserted rather than described, because the script's own
# comment and the docs both now claim this is fixed.
SOLO="$WORK/solo-root"
mk_skill "$SOLO/.claude/skills" alpha "# alpha skill"
mk_skill "$SOLO/.claude/skills" beta  "# beta skill"
rc=0; bash "$ASSERT" --product "$SOLO" --roots ".claude/skills" >"$WORK/out" 2>&1 || rc=$?
sumline="$(grep -m1 'skill(s) —' "$WORK/out" || true)"
want_sum="in-every-root=2/2 partitioned=0 | byte-comparable=0 byte-compared=0 byte-identical=0/0 byte-differing=0 single-root=2"
if [ "$rc" -eq 0 ] && [ "$(printf '%s' "$sumline" | sed 's/.*— //')" = "$want_sum" ]; then
  echo "PASS  (expected pass) a single-root root set claims NO byte-identity: 0/0 compared, single-root=2 (pre-fix: byte-identical=2)"; pass=$((pass+1))
else
  echo "FAIL  single-root root set accounting (rc=$rc)"; echo "    | want: … — $want_sum"; echo "    | got:  $sumline"; failcount=$((failcount+1))
fi

# --- 4e. CHECK 3 IS THE THIRD INDEPENDENT FACT, AND IT IS PER-ROOT (#1513) ------------------------
#
# THE DEFECT THIS PINS, and it is #1506's exactly one check further down. Checks 1-2 SHORT-CIRCUITED
# past check 3: `if [ -n "$partitioned" ] || [ -n "$differing" ]; then continue; fi`. So an id that was
# BOTH partitioned AND digest-mismatched reported only `[partitioned]`, and its declared digest was
# never read at all. Measured on `main` at 22461b4, on exactly the tree below:
#
#     ::error::[partitioned] skill 'beta' is absent from root(s): .agents/skills
#     skill-union-assert: 2 skill(s) — … | manifest-matched=1 co-tenant=0 declared-absent=0
#
# `Fsgg.SkillMirror.verify` — ADR-0014's one implementation, which this script is defined to FOLLOW
# (#120) — returns `MissingRoots`, `Divergent` and `HashMismatchRoots` on ONE record, computed
# INDEPENDENTLY. A follower that computes them in a chain can only ever report a prefix. And
# `manifest-matched=1` is the bare-count defect #1506 fixed for the byte counts and left here: a count
# with no population, over a 2-id union, when one of the two was never examined against the manifest.
BOTH3="$WORK/partitioned-and-drifted"; build_good "$BOTH3"
rm -rf "$BOTH3/.agents/skills/beta"                       # beta: partitioned…
MAN_BOTH3="$WORK/man-partitioned-and-drifted.json"
cat > "$MAN_BOTH3" <<EOF
{ "skills": [
  { "id": "alpha", "scope": "process", "sha256": "$(digest "$BOTH3/.claude/skills/alpha")" },
  { "id": "beta",  "scope": "product", "sha256": "1111111111111111111111111111111111111111111111111111111111111111" }
] }
EOF
rc=0; bash "$ASSERT" --product "$BOTH3" --roots "$ROOTS" --manifest "$MAN_BOTH3" >"$WORK/out" 2>&1 || rc=$?
sumline="$(grep -m1 'skill(s) —' "$WORK/out" || true)"

# (a) BOTH facts, for the SAME id, and the drift names EVERY root it was found in — the shape of
# `HashMismatchRoots`, not one representative root's answer.
if [ "$rc" -eq 1 ] \
   && grep -q "::error::\[partitioned\] skill 'beta' is absent from root(s): .agents/skills" "$WORK/out" \
   && grep -q "::error::\[drifted\] skill 'beta' SKILL.md digest != manifest 1111111111111111111111111111111111111111111111111111111111111111 in root(s): .claude/skills=.* .codex/skills=" "$WORK/out"; then
  echo "PASS  (expected [partitioned]+[drifted]) a partitioned skill whose declared digest mismatches reports BOTH facts, per root"; pass=$((pass+1))
else
  echo "FAIL  (want rc=1 + [partitioned] AND per-root [drifted] on the same id) got rc=$rc"; sed 's/^/    | /' "$WORK/out"; failcount=$((failcount+1))
fi

# (b) …and the MANIFEST counts carry their populations, asserted on the exact bytes for the same reason
# the byte counts are: `manifest-matched=1` beside an unexamined id is a coverage claim the reader
# supplies for themselves. `manifest-comparable` beside `manifest-examined` is what makes a
# re-introduced short-circuit visible rather than silent.
want_sum="in-every-root=1/2 partitioned=1 | byte-comparable=2 byte-compared=2 byte-identical=2/2 byte-differing=0 single-root=0 | manifest-declared=2/2 manifest-comparable=2 manifest-examined=2 manifest-matched=1/2 manifest-no-reference=0 undeclared-rejected=0/0 co-tenant=0/0 declared-absent=0/2"
if [ "$(printf '%s' "$sumline" | sed 's/.*— //')" = "$want_sum" ]; then
  echo "PASS  manifest counts state examined-vs-total: manifest-matched=1/2, not a bare 1"; pass=$((pass+1))
else
  echo "FAIL  manifest counts must state their populations"; echo "    | want: … — $want_sum"; echo "    | got:  $sumline"; failcount=$((failcount+1))
fi

# (c) …and a DENOMINATOR-FREE manifest-match count must never reach the summary again, on any tree —
# the same regex guard #1506 put on `byte-identical=`, which is the count this one was left behind by.
if printf '%s' "$sumline" | grep -qE 'manifest-matched=[0-9]+([^0-9/]|$)'; then
  echo "FAIL  summary printed a denominator-free manifest-match count — #1506's defect, one check over"; echo "    | $sumline"; failcount=$((failcount+1))
else
  echo "PASS  no denominator-free manifest-match count can reach the summary"; pass=$((pass+1))
fi

# (d) THE PER-ROOT DECISION, ASSERTED AS THE FAIL-OPEN IT PREVENTS. Two roots match the manifest and the
# third has DRIFTED. Check 3 used to digest the first PRESENT root as representative of the id — and
# here that root is one of the clean ones, so a representative-root check reports no drift AT ALL and
# `.agents` is invisible. The library returns `HashMismatchRoots=[".agents"]`. This is #266's family
# again: a check that was not made, rendered as a check that passed. Measured against the real library
# in tests/skill-union/skillmirror.fixtures.json (vector `divergent-and-the-REFERENCE-root-is-the-clean-one`).
REFCLEAN="$WORK/ref-root-clean"; build_good "$REFCLEAN"
printf '# alpha skill DRIFTED\n' > "$REFCLEAN/.agents/skills/alpha/SKILL.md"
rc=0; bash "$ASSERT" --product "$REFCLEAN" --roots "$ROOTS" --manifest "$MANIFEST" >"$WORK/out" 2>&1 || rc=$?
if [ "$rc" -eq 1 ] \
   && grep -q "::error::\[divergent\] skill 'alpha'" "$WORK/out" \
   && grep -q "::error::\[drifted\] skill 'alpha' SKILL.md digest != manifest .* in root(s): .agents/skills=" "$WORK/out" \
   && ! grep -q "in root(s): .claude/skills" "$WORK/out"; then
  echo "PASS  (expected [divergent]+[drifted]) a drift in a NON-reference root is caught and named (pre-fix: reported nothing)"; pass=$((pass+1))
else
  echo "FAIL  per-root digest: a drift outside the reference root must still be found and named (rc=$rc)"; sed 's/^/    | /' "$WORK/out"; failcount=$((failcount+1))
fi

# (e) …and the converse, which is the half a "report more drifts" change would break: a partition whose
# present copies DO match the manifest must emit NO [drifted] at all. Independence must not become
# manufacture — the same discipline as "a byte-identical partition is [partitioned], never [divergent]".
PARTOK="$WORK/partitioned-but-matching"; build_good "$PARTOK"
rm -rf "$PARTOK/.agents/skills/beta"
rc=0; bash "$ASSERT" --product "$PARTOK" --roots "$ROOTS" --manifest "$MANIFEST" >"$WORK/out" 2>&1 || rc=$?
if [ "$rc" -eq 1 ] && grep -q "::error::\[partitioned\] skill 'beta'" "$WORK/out" \
   && ! grep -q '\[drifted\]' "$WORK/out" && ! grep -q '\[divergent\]' "$WORK/out"; then
  echo "PASS  (expected [partitioned] only) a partition of manifest-MATCHING copies manufactures no [drifted]"; pass=$((pass+1))
else
  echo "FAIL  a partition of matching copies must not manufacture a drift (rc=$rc)"; sed 's/^/    | /' "$WORK/out"; failcount=$((failcount+1))
fi

# (f) A declared id with NEITHER a `files` array NOR a `sha256` has nothing to compare against, and must
# be counted apart rather than folded into `manifest-matched`. "Nothing to compare" must not render as
# "compared, and matching" — the #266 substitution #1506 closed for byte-identity, closed here too.
NOREF="$WORK/manifest-no-reference"; build_good "$NOREF"
MAN_NOREF="$WORK/man-no-reference.json"
cat > "$MAN_NOREF" <<EOF
{ "skills": [
  { "id": "alpha", "scope": "process", "sha256": "$(digest "$NOREF/.claude/skills/alpha")" },
  { "id": "beta",  "scope": "product", "sha256": "" }
] }
EOF
rc=0; bash "$ASSERT" --product "$NOREF" --roots "$ROOTS" --manifest "$MAN_NOREF" >"$WORK/out" 2>&1 || rc=$?
sumline="$(grep -m1 'skill(s) —' "$WORK/out" || true)"
if [ "$rc" -eq 0 ] && printf '%s' "$sumline" | grep -q 'manifest-matched=1/1 manifest-no-reference=1'; then
  echo "PASS  a declared id with no reference digest counts as no-reference, never as manifest-matched"; pass=$((pass+1))
else
  echo "FAIL  no-reference accounting (rc=$rc)"; echo "    | got:  $sumline"; failcount=$((failcount+1))
fi

# (g) THE v2 ARM IS PER-ROOT TOO, AND ITS PER-ROOT STATE MUST NOT LEAK BETWEEN ROOTS. The declared-file
# table is consumed destructively (each matched path is `unset` as it is found), so the roots after the
# first see a copy, not the original. Nothing in the suite proved that: every other v2 fixture applies
# its defect to EVERY root, so a table that leaked would produce the same verdict and pass. Here exactly
# ONE root drifts. Two things are asserted, and the second is the leak:
#   * the extra file is reported against `.agents/skills` BY NAME — per-root, not "somewhere in the id";
#   * the whole run emits EXACTLY ONE `[drifted]` line for the id, so the two clean roots said nothing.
#
# THE TOTAL IS THE ASSERTION, and the first draft of this leg got it wrong in a way worth recording. It
# counted `is missing declared file` lines, reasoning that an emptied table would report the declared
# files as missing. A mutation test (sharing the table instead of copying it) said otherwise: the walk
# consumes the table on the FIRST root, so the later roots find their real files UNDECLARED and report
# `has extra undeclared file` — a different message entirely, and the leg passed over the live defect.
# Counting every diagnostic for the id closes both symptoms and any third one.
V2ONEROOT="$WORK/v2-one-root-drift"; build_good "$V2ONEROOT"
printf 'undeclared\n' > "$V2ONEROOT/.agents/skills/alpha/extra.txt"
rc=0; bash "$ASSERT" --product "$V2ONEROOT" --roots "$ROOTS" --manifest "$V2_MANIFEST" >"$WORK/out" 2>&1 || rc=$?
extra_n="$(grep -c "::error::\[drifted\] skill 'alpha' in root '.agents/skills' has extra undeclared file 'extra.txt'" "$WORK/out" || true)"
alpha_drift_n="$(grep -c "::error::\[drifted\] skill 'alpha' " "$WORK/out" || true)"
if [ "$rc" -eq 1 ] && [ "$extra_n" -eq 1 ] && [ "$alpha_drift_n" -eq 1 ]; then
  echo "PASS  v2 per-root: only the drifting root is named, and the clean roots say nothing (no table leak)"; pass=$((pass+1))
else
  echo "FAIL  v2 per-root (rc=$rc, extra-in-agents=$extra_n, total [drifted] for alpha=$alpha_drift_n, want 1/1)"; sed 's/^/    | /' "$WORK/out"; failcount=$((failcount+1))
fi

# (h) …and a MANIFEST-level fault is reported ONCE, not once per root. An unsafe declared path is a
# property of the manifest, not of a tree, and printing it three times would be the same category error
# this change fixes, pointed the other way: a per-id fact dressed as a per-root finding.
UNSAFE="$WORK/v2-unsafe-path"; build_good "$UNSAFE"
MAN_UNSAFE="$WORK/man-unsafe.json"
cat > "$MAN_UNSAFE" <<EOF
{ "schemaVersion": 2, "skills": [
  { "id": "alpha", "sha256": "$alpha_skill", "files": [
    { "path": "SKILL.md", "sha256": "$alpha_skill", "executable": false },
    { "path": "references/notes.md", "sha256": "$alpha_ref", "executable": false },
    { "path": "../escape.md", "sha256": "$alpha_ref", "executable": false }
  ] },
  { "id": "beta", "sha256": "$beta_skill", "files": [
    { "path": "SKILL.md", "sha256": "$beta_skill", "executable": false },
    { "path": "references/notes.md", "sha256": "$beta_ref", "executable": false }
  ] }
] }
EOF
rc=0; bash "$ASSERT" --product "$UNSAFE" --roots "$ROOTS" --manifest "$MAN_UNSAFE" >"$WORK/out" 2>&1 || rc=$?
unsafe_n="$(grep -c "manifest contains unsafe file path" "$WORK/out" || true)"
if [ "$rc" -eq 1 ] && [ "$unsafe_n" -eq 1 ]; then
  echo "PASS  a manifest-level unsafe path is reported ONCE, not once per root"; pass=$((pass+1))
else
  echo "FAIL  unsafe-path reporting (rc=$rc, lines=$unsafe_n, want exactly 1)"; sed 's/^/    | /' "$WORK/out"; failcount=$((failcount+1))
fi

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

# --- 7j-7o. the {key,value}-ARRAY effectiveParameters encoding (.github#2546) -------------------
# Every leg above feeds `--params` an OBJECT-shaped `effectiveParameters`, and so did `load_params`'
# only accepted shape: it gated on `.effectiveParameters | type == "object"` and DIED (exit 2) on
# anything else. "Anything else" is what the real producer writes. `FS.GG.SDD.Artifacts` (provenance
# `schemaVersion: 1`) emits an ARRAY of `{"key":…,"value":…}` entries — this repo's own
# `.fsgg/scaffold-provenance.json` carries that shape, as does `EHotwagner/S.I.R.`'s. So check 4, the
# ONE arm that separates a JUSTIFIED off-profile omission from a genuinely dropped skill, had never
# executed against any product of that provider family. It was not answering wrongly; it refused to
# run, and the refusal read as a usage error rather than "this gate does not cover your tree" — while
# .github#1863 kept `materializes-when` on a measurement taken over object-shaped scaffolds only.
# With no predicate evaluated at all, ADR-0017's tolerated-absence rule silently applied to EVERY
# declared skill: a fail-OPEN of the whole condition-aware check for that family (#266).
#
# THESE LEGS DO NOT MERELY ASSERT THAT `--params` STOPPED CRASHING, and that distinction is the whole
# point of the row: a check that has only ever crashed is indistinguishable from one that cannot fail.
# They drive check 4 THROUGH THE ARRAY ENCODING to a RED verdict on a genuinely non-conforming tree
# (7j, 7l) and a GREEN one on a conforming tree (7k), and then pin that the two encodings of the SAME
# map are INDISTINGUISHABLE — identical exit code and byte-identical output (7m, 7n) — so neither
# branch can quietly acquire behaviour the other lacks.
#
# The array files here are AUTHORED, not derived from the object ones by `to_entries`: a derived
# encoding would only ever prove the gate agrees with whatever produced it. (conformance.sh takes the
# derived route deliberately and for the opposite purpose — sweeping all 24 grammar vectors through
# both shapes — so the two fixtures check each other.)
echo "--- {key,value}-array effectiveParameters (.github#2546) ---"

# The producer's REAL envelope, not just the inner array: `schemaVersion` and `generator` exactly as
# FS.GG.SDD.Artifacts writes them, so this is the file a live scaffolded product actually carries.
# The map is the same one $PROV holds — profile=game, lifecycle=spec-kit, feedback=true.
PROV_ARR="$WORK/prov-game-array.json"
cat > "$PROV_ARR" <<'EOF'
{ "schemaVersion": 1,
  "generator": { "id": "FS.GG.SDD.Artifacts", "version": "1.0.0" },
  "effectiveParameters": [
    { "key": "profile",   "value": "game" },
    { "key": "lifecycle", "value": "spec-kit" },
    { "key": "feedback",  "value": true }
  ] }
EOF

# 7j. RED on a non-conforming tree: the fs-gg-project case (declared ∧ condition TRUE ∧ absent from
#     every root) must be [missing]/exit 1 when the parameters arrive in the producer's encoding.
expect_fail "array params: [missing] on a genuinely non-conforming tree (the fs-gg-project case)" \
  missing "$MISS" --manifest "$MAN_MISS" --params "$PROV_ARR"

# 7k. GREEN on a conforming tree: justified off-profile absence plus a compound-TRUE present skill.
#     Paired with 7j this is what makes the check falsifiable rather than merely non-crashing.
expect_pass "array params: green on a conforming tree (justified absence + compound-true present)" \
  "$JUST" --manifest "$MAN_JUST" --params "$PROV_ARR"

# 7l. RED from the other direction: present ∧ condition FALSE ⇒ [unexpected].
expect_fail "array params: [unexpected] on a skill materialized off-profile" \
  unexpected "$UNEXP" --manifest "$MAN_UNEXP" --params "$PROV_ARR"

# 7m/7n. ENCODING EQUIVALENCE. The two shapes denote the same map, so the gate must be unable to tell
# them apart — same exit code AND the same bytes on stdout+stderr, diagnostics and summary counts
# included. `want-rc` is asserted too: without it, two runs that both died at exit 2 would agree
# perfectly and "pass".
same_verdict() { # <name> <want-rc> <product> <manifest> <params-a> <params-b>
  local name="$1" want="$2" prod="$3" man="$4" pa="$5" pb="$6"
  local rc_a=0 rc_b=0
  bash "$ASSERT" --product "$prod" --roots "$ROOTS" --manifest "$man" --params "$pa" >"$WORK/enc-a" 2>&1 || rc_a=$?
  bash "$ASSERT" --product "$prod" --roots "$ROOTS" --manifest "$man" --params "$pb" >"$WORK/enc-b" 2>&1 || rc_b=$?
  if [ "$rc_a" -eq "$want" ] && [ "$rc_b" -eq "$want" ] && cmp -s "$WORK/enc-a" "$WORK/enc-b"; then
    echo "PASS  (encodings indistinguishable, rc=$want) $name"; pass=$((pass+1))
  else
    echo "FAIL  (want both rc=$want and identical output; got a=$rc_a b=$rc_b) $name"
    { diff "$WORK/enc-a" "$WORK/enc-b" || true; } | sed 's/^/    | /'
    failcount=$((failcount+1))
  fi
}

same_verdict "object vs {key,value}-array params agree on the [missing] tree" \
  1 "$MISS"  "$MAN_MISS"  "$PROV" "$PROV_ARR"
same_verdict "object vs {key,value}-array params agree on the conforming tree" \
  0 "$JUST"  "$MAN_JUST"  "$PROV" "$PROV_ARR"
same_verdict "object vs {key,value}-array params agree on the [unexpected] tree" \
  1 "$UNEXP" "$MAN_UNEXP" "$PROV" "$PROV_ARR"

# 7n. The EMPTY map in both encodings. `[]` is not a hypothetical: it is what this repo's own
# `.fsgg/scaffold-provenance.json` carries, and under the old object-only gate it was an exit 2 —
# so a scaffold with no effective parameters could not be condition-checked at all. It must mean
# "every parameter is unset", exactly as `{}` does: against $MAN_MISS that leaves `always` true
# (alpha, present ⇒ fine), `profile in [app, game]` false on a PRESENT beta ⇒ [unexpected], and
# `profile == game` false on an absent fs-gg-project ⇒ justified. rc=1, and identical either way —
# a non-vacuous verdict rather than a pair of empty runs agreeing about nothing.
PROV_EMPTY_OBJ="$WORK/prov-empty-object.json"
printf '%s\n' '{ "effectiveParameters": {} }' > "$PROV_EMPTY_OBJ"
PROV_EMPTY_ARR="$WORK/prov-empty-array.json"
printf '%s\n' '{ "effectiveParameters": [] }' > "$PROV_EMPTY_ARR"
same_verdict "an EMPTY object and an EMPTY array both mean 'every parameter unset'" \
  1 "$MISS" "$MAN_MISS" "$PROV_EMPTY_OBJ" "$PROV_EMPTY_ARR"

# 7o. AN UNRECOGNISED SHAPE STILL FAILS CLOSED — exit 2, the `die` path — but it must now say what it
# received and what is supported. The old message asserted the file "has no .effectiveParameters
# object", which was FALSE for the array form and is what sent .github#2366 and .github#2380 to
# hand-check by inspection what this gate exists to answer; that exact wording is asserted ABSENT so
# it cannot come back. These run through the REAL gate (--manifest --params), not `--eval-when`, so
# it is check 4's own param load being exercised.
bad_shape() { # <name> <provenance-json> <fragment the message must carry>...
  local name="$1" json="$2"; shift 2
  printf '%s\n' "$json" > "$WORK/prov-bad.json"
  local rc=0
  bash "$ASSERT" --product "$MISS" --roots "$ROOTS" --manifest "$MAN_MISS" --params "$WORK/prov-bad.json" \
    >"$WORK/out" 2>&1 || rc=$?
  local miss="" frag
  for frag in "$@"; do grep -qF -- "$frag" "$WORK/out" || miss="$miss [$frag]"; done
  if [ "$rc" -eq 2 ] && [ -z "$miss" ] && ! grep -qF 'has no .effectiveParameters object' "$WORK/out"; then
    echo "PASS  (fail-closed exit 2, shape named) $name"; pass=$((pass+1))
  else
    echo "FAIL  (want exit 2 naming the shape received; got rc=$rc, unmentioned:$miss) $name"
    sed 's/^/    | /' "$WORK/out"; failcount=$((failcount+1))
  fi
}
bad_shape "unsupported shape: an array of bare strings" \
  '{ "effectiveParameters": ["profile", "game"] }' \
  'entries are not all {key,value} objects' 'supported:'
bad_shape "unsupported shape: {key,value} entries without a value" \
  '{ "effectiveParameters": [{ "key": "profile" }] }' \
  'entries are not all {key,value} objects' 'supported:'
bad_shape "unsupported shape: a {key,value} array declaring one key twice (no correct answer to guess)" \
  '{ "effectiveParameters": [{ "key": "profile", "value": "game" }, { "key": "profile", "value": "app" }] }' \
  'declaring the same key more than once (profile)' 'supported:'
bad_shape "unsupported shape: .effectiveParameters absent entirely" \
  '{ "schemaVersion": 1 }' \
  'is absent or null' 'supported:'
bad_shape "unsupported shape: .effectiveParameters is a JSON string" \
  '{ "effectiveParameters": "profile=game" }' \
  'is a JSON string' 'supported:'
bad_shape "params that is not valid JSON at all is named as such, not as a shape" \
  '{ "effectiveParameters": [' \
  'params is not valid JSON'

# --- root-set resolution: --roots > $AGENT_SKILL_ROOTS > .agent-skill-roots > ADR-0065's two ------
# (.github#517) A tree can deliberately override the universal default. This fixture keeps proving
# that override and precedence behavior; ADR-0065 removed the former automatic kit exception, and
# ADR-0067 §5 (.github#1636) narrowed the DEFAULT from three roots to two.
#
# The load-bearing case is still the FIRST, and narrowing the default did NOT weaken it: an absent
# root the default NAMES is a hard exit 2, never a skip. That is ADR-0011's origin bug (a producer
# that never materialized a root) and the fail-OPEN family of #266/#292. What changed is only WHICH
# absence proves it — `.codex/skills` is no longer asked for, so the probe is a tree missing
# `.agents/skills`. Declaring roots narrows WHAT IS ASKED FOR; it must never weaken the answer.
echo "--- root-set resolution (.github#517) ---"
ONEROOT="$WORK/oneroot"                            # a tree missing a root the DEFAULT names
mkdir -p "$ONEROOT/.claude/skills/alpha"
printf '# alpha skill\n' > "$ONEROOT/.claude/skills/alpha/SKILL.md"

expect_rc "roots: NO declaration ⇒ ADR-0065's two ⇒ absent .agents is a hard exit 2 (fail-CLOSED)" \
  2 --product "$ONEROOT"

KIT="$WORK/kit"                                    # the two-root tree the default now describes
mkdir -p "$KIT/.claude/skills/alpha" "$KIT/.agents/skills/alpha"
printf '# alpha skill\n' > "$KIT/.claude/skills/alpha/SKILL.md"
cp "$KIT/.claude/skills/alpha/SKILL.md" "$KIT/.agents/skills/alpha/SKILL.md"

expect_rc "roots: NO declaration ⇒ ADR-0065's two ⇒ a matching tree exits 0 with no declaration" \
  0 --product "$KIT"

printf '# this tree states the roots it keeps\n.claude/skills\n.agents/skills\n' \
  > "$KIT/.agent-skill-roots"                      # comments + newline-separated must parse

expect_rc "roots: .agent-skill-roots restating the two-root set ⇒ BARE exits 0" \
  0 --product "$KIT"
expect_rc "roots: --roots still overrides the declaration (explicit wins) — the RETIRED .codex root is absent, so asking for it is exit 2" \
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

# --- cross-impl conformance against Fsgg.SkillMirror (#120, .github#1513) -----------------------
# #120 settled that `Fsgg.SkillMirror` is ADR-0014's ONE implementation and that this shell assertion
# FOLLOWS it. NOTHING ENFORCED THAT SENTENCE, and the two diverged three times — #1506 twice, #1513 once
# — each found by a person reading the code after it had misdirected real work. `verify` returns THREE
# INDEPENDENT FACTS on one record (`MissingRoots`, `Divergent`, `HashMismatchRoots`), so a follower that
# computes them in a chain can only report a prefix of them, and no single-fact fixture notices.
# skillmirror-conformance.sh drives a SHARED VECTOR TABLE whose expectations were MEASURED by running
# the library's own source (skillmirror-oracle.sh), and compares each fact independently. Delegated to
# its own harness, folded in here so CI's single entrypoint runs it — the #398 arrangement exactly.
echo "--- Fsgg.SkillMirror three-facts conformance (#120, .github#1513) ---"
if bash "$HERE/skillmirror-conformance.sh"; then
  echo "PASS  (cross-impl conformance) the shell reports all three of SkillMirror.verify's facts, per root"; pass=$((pass+1))
else
  echo "FAIL  shell vs Fsgg.SkillMirror three-facts conformance diverged (see above)"; failcount=$((failcount+1))
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
