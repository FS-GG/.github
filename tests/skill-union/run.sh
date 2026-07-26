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
expect_pass "coherent union (+ v2 whole-directory manifest)" "$GOOD" --manifest "$V2_MANIFEST"

# --- 3. divergent bytes: one root's skill body differs → FAIL ---
DIV="$WORK/divergent"; build_good "$DIV"
printf '# alpha skill TAMPERED\n' > "$DIV/.codex/skills/alpha/SKILL.md"
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
