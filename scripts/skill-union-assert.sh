#!/usr/bin/env bash
# Reusable skill-union assertion — FS-GG/.github#111 (ADR-0014 P3.G3.1, epic #110).
#
# The one shared check that the agent-skill roots of a scaffolded product hold the SAME,
# CORRECT set of skills — the byte-identical union of process + product skills across every
# AGENT_SKILL_ROOTS root. It is the CONSUMER-side arm of ADR-0014's "content-addressed,
# verified in every lane" design: producers (SDD `mirror`, the fs-gg-ui template's single
# materialize) write the roots; this asserts they did so correctly. It is deliberately the
# mirror of the reusable `contract-coherence` / `dispatch-sender` pattern — one script authored
# in FS-GG/.github, wrapped by a `workflow_call` workflow, callable from any consumer CI (the
# FS.GG.Templates composition gate is the first caller — FS.GG.Templates#49 / roadmap T3.2).
#
# It checks CONTENT, not presence — the gap ADR-0014 F2 found (doctor/composition asserted only
# `Option.isSome`). For every skill present under any root it asserts:
#   1. present        — the skill directory exists in EVERY configured root (else: partitioned);
#   2. byte-identical — its bytes are identical across every root it IS PRESENT IN (else: divergent);
#   3. matches-manifest (only when --manifest is given) — every declared file, digest, and executable
#      bit matches. Legacy v1 rows that have no `files` retain the SKILL.md-only check.
#      the producer's skill-manifest declares (else: drifted), and every root skill is either
#      declared by the manifest or matches a --co-tenants pattern (else: dangling).
#
# Checks 1-2 are self-contained and enforce with nothing but the product tree. Check 3 follows
# the PRODUCERS' manifest semantics (aligned with the shipped manifests per .github#120 —
# `Fsgg.SkillMirror` in FS.GG.Contracts 1.4.0 / SDD#61 and fs-gg-ui-template 0.1.61-preview.1 /
# Rendering#43 are ADR-0014's "one implementation"; the assertion follows them):
#   - digest: canonical-body sha256 of the skill's SKILL.md ONLY (equal to `sha256sum SKILL.md`
#     for a BOM-free, LF-only body; a leading UTF-8 BOM is stripped and CRLF is folded to LF first,
#     matching the producer algorithm `Fsgg.SkillMirror.sha256` — .github#1547, which means a CRLF
#     file and its LF twin have the SAME canonical digest). Multi-file skills (SKILL.md +
#     references/**) are covered by the cross-root identity of checks 1-2, not by the digest.
#   - set: the manifest is a superset CATALOG, an upper bound — emission is lifecycle/profile-
#     conditioned, so declared∧present ⇒ digest must match; declared∧absent-everywhere ⇒ fine
#     (skipped, counted); present∧undeclared ⇒ dangling UNLESS the id matches a --co-tenants
#     glob (process skills from a co-tenant producer, e.g. "fs-gg-sdd-* speckit-*").
# CHECKS 1, 2 AND 3 ARE INDEPENDENT QUESTIONS, AND ALL THREE ARE ALWAYS ASKED (.github#1506, #1513).
# `SkillMirror.verify` returns THREE facts on ONE `SkillDrift` record — `MissingRoots`, `Divergent` and
# `HashMismatchRoots` — computed independently of one another, and this script is defined to follow it
# (#120). It short-circuited twice. Check 1 `continue`d a [partitioned] id past the byte comparison, so
# the bytes of a skill that WAS in two roots were never compared (#1506); then checks 1-2 `continue`d
# past the manifest check, so an id that was BOTH partitioned AND digest-mismatched reported only
# [partitioned] and its declared digest was never read at all (#1513). An id can be [partitioned] AND
# [divergent] AND [drifted], and all three are reported.
#
# CHECK 3 IS PER-ROOT, LIKE `HashMismatchRoots`, NOT PER-REPRESENTATIVE-ROOT (.github#1513). It used to
# digest ONE root's copy as representative of the id, which is only safe for an id that is whole and
# coherent — the exact ids the short-circuit meant were the only ones ever to reach it. Measured against
# the library over the shared vector table (tests/skill-union/skillmirror.fixtures.json): a tree whose
# `.claude` and `.codex` copies match the manifest and whose `.agents` copy has DRIFTED yields
# `HashMismatchRoots=[".agents"]` from the library and NOTHING AT ALL from a representative-root check,
# because the representative is the clean one. That is a fail-OPEN (#266) and it is why this is per-root.
#
# THE SUMMARY REPORTS EVERY COUNT WITH THE POPULATION IT WAS TAKEN OVER, for the same reason: a count
# whose denominator is invisible is a claim about coverage the reader supplies for themselves, and they
# supply the generous one. `byte-identical=4` over a 50-id union read as "the union is byte-coherent"
# and two downstream issues were sized against it while 30 comparable pairs differed, uncompared. The
# MANIFEST counts were left bare by that same fix and carried the identical defect one check over
# (#1513): `manifest-matched=1` over a 2-id union, when one of the two was never examined against the
# manifest at all. They now carry their populations too.
#
# (4) condition-aware — only when BOTH --manifest and --params <scaffold-provenance.json> are given
# (ADR-0017). The manifest entry may carry a `materializes-when` predicate over the scaffold
# parameters (absent ⇒ `always`); --params supplies the scaffold's `effectiveParameters`. This
# turns the superset catalog's "declared ∧ absent" from BLANKET-tolerated into JUSTIFIED —
# absence is legitimate only when the condition is false. Two new classes close the manifest→disk
# blind spot (the direction checks 1-3 never enforced):
#   - declared ∧ condition TRUE ∧ absent-everywhere → [missing]   (FAIL — the dropped-skill case,
#     e.g. fs-gg-project; was silently tolerated by the superset rule);
#   - present ∧ condition FALSE                     → [unexpected] (FAIL — materialized off-profile);
#   - declared ∧ condition FALSE ∧ absent           → legitimate (justified off-profile omission);
#   - declared ∧ condition TRUE  ∧ present          → proceed to the digest check (3) as before.
# Without --params the gate keeps today's superset semantics EXACTLY (opt-in per caller), so no
# consumer is forced to change at once. The predicate grammar is deliberately tiny (==, !=,
# `in [..]`, and, or, true/false, always) — evaluable without a real expression engine, so the
# shell gate and the typed validator (Fsgg.Registry) never drift.
#
# Usage:
#   skill-union-assert.sh --product <dir> [--roots "<r1> <r2> ..."] [--manifest <file.json>]
#                         [--co-tenants "<glob> <glob> ..."] [--params <scaffold-provenance.json>]
#   skill-union-assert.sh --digest <skill-dir>   # print the canonical SKILL.md digest and exit
#   skill-union-assert.sh --eval-when <predicate> [--params <p.json>]  # print true/false, then exit
#
# WHICH ROOTS (.github#517) — ADR-0065 gives scaffolded products and kit consumers the same default:
# `.claude`, `.codex`, and `.agents`. A tree may still DECLARE an intentional override, and the
# resolution order is:
#   1. --roots            (explicit; what CI's reusable workflow passes)
#   2. $AGENT_SKILL_ROOTS (env; the same knob `coordination-sync` reads)
#   3. <product>/.agent-skill-roots  (checked in — the tree states its own root set)
#   4. ADR-0011's three   (the scaffolded-product default)
# An absent root stays a HARD exit-2 at every level: the gate's whole job is catching a root a
# producer failed to materialize (ADR-0011's origin bug), so absence must never degrade to a skip.
# Declaring roots narrows WHAT IS ASKED FOR; it never weakens the answer. Roots are resolved
# relative to --product. --params requires --manifest. Exit 0 = union coherent; 1 = at least one
# violation (each printed with its class); 2 = misconfiguration. `-h`/`--help` prints usage.

set -euo pipefail

PRODUCT="."
DEFAULT_ROOTS=".claude/skills .codex/skills .agents/skills"   # ADR-0011: the scaffolded-product set
ROOTS_DECL_FILE=".agent-skill-roots"   # named in the missing-root hint below; lib/roots.sh reads it
ROOTS=""
ROOTS_ARG=""
ROOTS_SRC=""
ROOTS_FROM_DEFAULT=""
MANIFEST=""
CO_TENANTS=""
PARAMS=""

die() { echo "::error::skill-union-assert: $*" >&2; exit 2; }
# need_val (a flag with no value is a misconfiguration, not the exit-1 "the union is violated" verdict)
# is shared with coordination-sync and repos-audit.sh; see lib/args.sh. Sourced after die, which it uses.
# shellcheck source=lib/args.sh
. "$(dirname "${BASH_SOURCE[0]}")/lib/args.sh"
# resolve_roots / read_roots_decl — the root-set precedence and the `.agent-skill-roots` parser, shared
# with coordination-sync so the script that WRITES the roots resolves them exactly as this one, which
# ASSERTS them, does (#525). Sourced after die, which it uses.
# shellcheck source=lib/roots.sh
. "$(dirname "${BASH_SOURCE[0]}")/lib/roots.sh"

usage() {
  cat <<'EOF'
skill-union-assert.sh — assert a scaffolded product's agent-skill roots are the byte-identical
union of process + product skills (ADR-0014 P3.G3.1, FS-GG/.github#111).

Usage:
  skill-union-assert.sh --product <dir> [--roots "<r1> <r2> ..."] [--manifest <file.json>]
                        [--co-tenants "<glob> <glob> ..."] [--params <scaffold-provenance.json>]
  skill-union-assert.sh --digest <skill-dir>
  skill-union-assert.sh --eval-when <predicate> [--params <scaffold-provenance.json>]

Options:
  --product <dir>         product tree to check (default: ".")
  --roots "<r1> ..."      space-separated skill roots, relative to --product. Resolution order:
                          --roots, then $AGENT_SKILL_ROOTS, then <product>/.agent-skill-roots (a
                          checked-in declaration, for a tree that is not a scaffolded product),
                          then ADR-0011's three: ".claude/skills .codex/skills .agents/skills".
                          An absent root is a misconfiguration (exit 2) at every level.
  --manifest <file.json>  producer skill-manifest; enables the digest cross-check (check 3)
  --co-tenants "<glob>…"  globs of undeclared co-tenant skill ids to admit (only with --manifest)
  --params <file.json>    scaffold-provenance.json; enables the condition-aware check (check 4,
                          ADR-0017) — evaluates each declared skill's materializes-when against
                          .effectiveParameters, adding [missing] / [unexpected]. Requires --manifest.
  --digest <skill-dir>    reference generator: print the canonical-body sha256 of the dir's SKILL.md,
                          then exit (so producers and this assertion never drift)
  --eval-when <predicate> reference evaluator: evaluate a materializes-when <predicate> against the
                          scaffold parameters from --params (absent ⇒ every param is empty), print
                          `true`/`false`, then exit 0 (exit 2 on an unparseable predicate). Exposes
                          the shell grammar evaluator so the cross-impl conformance fixture can pin it
                          against normalize_when / the typed Fsgg.Registry validator and they never
                          drift (tests/skill-union, .github#398). Does NOT require --manifest.
  -h, --help              print this help

Exit: 0 = union coherent; 1 = at least one violation (each printed with its class); 2 = misconfiguration.
EOF
}

DIGEST_ONLY=""
EVAL_WHEN=""
EVAL_WHEN_SET=""

while [ $# -gt 0 ]; do
  case "$1" in
    --product)    need_val "$@"; PRODUCT="$2"; shift 2 ;;
    --roots)      need_val "$@"; ROOTS_ARG="$2"; shift 2 ;;
    --manifest)   need_val "$@"; MANIFEST="$2"; shift 2 ;;
    --co-tenants) need_val "$@"; CO_TENANTS="$2"; shift 2 ;;
    --params)     need_val "$@"; PARAMS="$2"; shift 2 ;;
    --digest)     need_val "$@"; DIGEST_ONLY="$2"; shift 2 ;;
    --eval-when)  need_val "$@"; EVAL_WHEN="$2"; EVAL_WHEN_SET=1; shift 2 ;;
    -h|--help)    usage; exit 0 ;;
    *)            die "unknown argument: $1" ;;
  esac
done

command -v sha256sum >/dev/null 2>&1 || die "sha256sum not found (required for content hashing)."

# Per-skill digest (see header): canonical-body sha256 of SKILL.md only — the producers' shipped
# algorithm (`Fsgg.SkillMirror`, FS.GG.Contracts 1.4.0; verified in .github#120).
#
# TWO NORMALIZATIONS, AND BOTH ARE THE LIBRARY'S, NOT THIS SCRIPT'S (.github#1547):
#   1. a leading UTF-8 BOM is stripped — the library's callers decode the file as UTF-8 and the
#      decoder consumes the BOM, so it can never enter the digest on the producing side; without
#      this a BOM'd SKILL.md would hash BOM-free in the Python registry check but WITH the BOM here,
#      a spurious [drifted] (.github#384);
#   2. CRLF is folded to LF — `Fsgg.SkillMirror.sha256` does `body.Replace("\r\n", "\n")` before
#      hashing (feature 070), so an LF-authored body does not spuriously drift on a CRLF checkout.
#
# #1547 IS WHY (2) IS HERE. The canonical digest had THREE implementations — this one, the Python
# `canonical_digest` in scripts/fsgg-skill-registry-check, and the library — and only the library
# folded CRLF, so on a Windows/`eol=crlf` checkout the two shells reported a drift the library did
# not. ADR-0014 names `Fsgg.SkillMirror` the ONE implementation and #120 records that this script
# FOLLOWS it, so the shells were the drifted copies; the 2026-07-27 decision on #1547 aligned both to
# the library. CONSEQUENCE, STATED RATHER THAN LEFT TO BE INFERRED: a CRLF file and its LF twin now
# hash IDENTICALLY, so this check is deliberately slightly more permissive than a byte comparison.
# Cross-root byte-identity (check 2) is unaffected — it compares raw bytes and still reports a
# CRLF/LF pair as [divergent].
#
# IT REPLACES THE PAIR, NOT THE CHARACTER, and that distinction is the whole of the implementation.
# `tr -d '\r'` would delete LONE carriage returns too, which the library keeps — and that is not a
# theoretical difference: measured over the vector table, `# beta skill\r` and `# beta skill` hash
# the SAME under `tr -d '\r'` and DIFFERENTLY under the library. Two distinct files sharing a digest
# is a fail-OPEN (#266), so the fold is done with bash's own pair-wise substitution and pinned by the
# `lone-cr`, `cr-cr-lf` and `trailing-lone-cr` vectors in tests/skill-union/skillmirror.fixtures.json.
#
# `--digest <skill-dir>` exposes it as a reference generator so producers and this assertion never
# drift; `fsgg-skill-registry-check --digest <file>` is the Python seam's counterpart, and
# tests/skill-union/skillmirror-conformance.sh drives BOTH plus the library's measured column over
# one shared table. Multi-file remainder is covered by checks 1-2.
skill_digest() {
  local dir="$1" body
  [ -f "$dir/SKILL.md" ] || return 1
  # The `x` sentinel, then `%x`, is what makes this read the file's REAL bytes: `$(...)` strips
  # EVERY trailing newline, so without it a body ending "\n\n\n" would hash as one ending "\n" and
  # the `many-trailing-lf` vector would be indistinguishable from the plain `lf` one.
  if [ "$(head -c 3 "$dir/SKILL.md" | od -An -tx1 | tr -d ' \n')" = efbbbf ]; then
    body="$(tail -c +4 "$dir/SKILL.md"; printf x)"
  else
    body="$(cat "$dir/SKILL.md"; printf x)"
  fi
  body="${body%x}"
  printf '%s' "${body//$'\r\n'/$'\n'}" | sha256sum | cut -d' ' -f1
}

# Does <id> match any --co-tenants glob?
is_co_tenant() {
  local id="$1" pat
  for pat in $CO_TENANTS; do
    # shellcheck disable=SC2254
    case "$id" in $pat) return 0 ;; esac
  done
  return 1
}

# ---- materializes-when predicate evaluator (ADR-0017) ------------------------------------------
# A deliberately TINY boolean language over the scaffold parameters, mirrored 1:1 by the typed
# validator so shell and F# never drift:  always | <clause> [ (and|or) <clause> ]...  where a
# clause is  <param> == <value> | <param> != <value> | <param> in [<v>, <v>, ...].  No parentheses;
# `and` binds tighter than `or` (evaluated: OR of ANDs of clauses). Params are bare tokens; a value
# is a bare token ([A-Za-z0-9_-], plus true/false) OR a double-quoted literal `"..."`. A param absent
# from --params reads as the empty string, so `== <v>` is false and `!= <v>` is true. PARAM[] is
# populated from .effectiveParameters below.
#
# A value's quotes are DELIMITERS, not bytes: `normalize_when` (fsgg-skill-registry-check) preserves
# a quoted literal verbatim into the manifest precisely so this gate parses it, and PARAM[] values
# arrive UNQUOTED (jq `tostring`) — so a quoted literal must be compared by its INNER bytes. `unquote`
# strips one matched surrounding pair; without it `profile == "game"` compares `game` against the
# 6-byte string `"game"` and is ALWAYS FALSE — a fail-open for the [missing]/[unexpected] classes.
# (The ` and `/` or ` clause split and the `in [...]` comma split are not literal-aware, so a value
# containing those separators must be a bare token — every real registry row already is.)
declare -A PARAM=()

trim() { local s="$1"; s="${s#"${s%%[![:space:]]*}"}"; s="${s%"${s##*[![:space:]]}"}"; printf '%s' "$s"; }
unquote() { local s="$1"; if [ "${#s}" -ge 2 ] && [ "${s:0:1}" = '"' ] && [ "${s: -1}" = '"' ]; then s="${s:1:${#s}-2}"; fi; printf '%s' "$s"; }

# Populate PARAM[] from a scaffold-provenance.json (.effectiveParameters). Booleans/numbers are
# stringified so `feedback == true` compares against the literal token. Shared by the real gate
# (check 4) and the --eval-when reference evaluator, so the fixture pins the SAME param extraction
# the gate uses, not a test-only re-read that could drift from it.
load_params() {
  local file="$1"
  [ -f "$file" ] || die "params (scaffold-provenance) not found: $file"
  command -v jq >/dev/null 2>&1 || die "jq not found (required to read --params)."
  jq -e '.effectiveParameters | type == "object"' "$file" >/dev/null 2>&1 \
    || die "params has no .effectiveParameters object: $file"
  local k v
  while IFS=$'\t' read -r k v; do
    [ -n "$k" ] || continue
    PARAM["$k"]="$v"
  done < <(jq -r '.effectiveParameters | to_entries[] | [.key, (.value | tostring)] | @tsv' "$file")
}

# Evaluate a single comparison clause. Returns 0 (true) / 1 (false); dies (2) on an unparseable clause.
eval_clause() {
  local c key rhs item; c="$(trim "$1")"
  if [ "$c" = "always" ] || [ "$c" = "true" ]; then return 0; fi
  if [ "$c" = "false" ]; then return 1; fi
  if [[ "$c" == *" in ["* ]]; then
    key="$(trim "${c%% in [*}")"
    rhs="${c#*[}"; rhs="${rhs%]}"                    # contents between [ and ]
    local IFS=',' item_t
    for item in $rhs; do
      item_t="$(unquote "$(trim "$item")")"
      [ -n "$item_t" ] || continue                   # skip empties (a trailing/doubled comma) — an
                                                     # empty token must never match an unset param
      [ "${PARAM[$key]-}" = "$item_t" ] && return 0
    done
    return 1
  fi
  if [[ "$c" == *"!="* ]]; then
    key="$(trim "${c%%!=*}")"; rhs="$(unquote "$(trim "${c#*!=}")")"
    [ "${PARAM[$key]-}" != "$rhs" ]; return
  fi
  if [[ "$c" == *"=="* ]]; then
    key="$(trim "${c%%==*}")"; rhs="$(unquote "$(trim "${c#*==}")")"
    [ "${PARAM[$key]-}" = "$rhs" ]; return
  fi
  die "unparseable materializes-when clause: '$c'"
}

# Evaluate a conjunction (clauses joined by ' and ' — all must hold).
eval_and() {
  local rest="$1" part
  while :; do
    if [[ "$rest" == *" and "* ]]; then part="${rest%% and *}"; rest="${rest#* and }"
    else part="$rest"; rest=""; fi
    eval_clause "$part" || return 1
    [ -n "$rest" ] || break
  done
  return 0
}

# Evaluate a full predicate (conjunctions joined by ' or ' — any may hold). Returns 0=true/1=false.
eval_condition() {
  local rest; rest="$(trim "$1")"
  [ -n "$rest" ] || return 0                          # empty ⇒ always (defensive; absent ⇒ "always" upstream)
  local part
  while :; do
    if [[ "$rest" == *" or "* ]]; then part="${rest%% or *}"; rest="${rest#* or }"
    else part="$rest"; rest=""; fi
    eval_and "$part" && return 0
    [ -n "$rest" ] || break
  done
  return 1
}

if [ -n "$DIGEST_ONLY" ]; then
  [ -d "$DIGEST_ONLY" ] || die "skill dir not found: $DIGEST_ONLY"
  skill_digest "$DIGEST_ONLY" || die "no SKILL.md under: $DIGEST_ONLY"
  exit 0
fi

# Reference evaluator (--eval-when): evaluate one materializes-when predicate against --params and
# print true/false, then exit. It runs the SAME eval_condition the gate's check 4 uses (via the SAME
# load_params), so the cross-impl conformance fixture can pin the shell grammar against normalize_when
# and the typed Fsgg.Registry validator — the anti-drift purpose --digest serves for the digest
# algorithm (.github#398). --manifest is irrelevant here (there is no manifest to read predicates
# from — the predicate is supplied directly), so it is not required.
if [ -n "$EVAL_WHEN_SET" ]; then
  [ -z "$PARAMS" ] || load_params "$PARAMS"
  if eval_condition "$EVAL_WHEN"; then echo true; else echo false; fi
  exit 0
fi

[ -d "$PRODUCT" ] || die "product tree not found: $PRODUCT"

# Resolve the root set (see header). Precedence: --roots > $AGENT_SKILL_ROOTS > the tree's own
# .agent-skill-roots > ADR-0011's three. The precedence and the declaration parser live in
# lib/roots.sh, because `coordination-sync` — the script that MATERIALIZES these roots — must resolve
# them the same way the gate that ASSERTS them does. It did not, and a tree's root set had two sources
# of truth agreeing only by coincidence of defaults (#525). ADR-0065 now makes both defaults identical.
resolve_roots "$PRODUCT" "$DEFAULT_ROOTS" "default (ADR-0011's three)" "$ROOTS_ARG"

# shellcheck disable=SC2206
ROOT_ARR=($ROOTS)
[ "${#ROOT_ARR[@]}" -ge 1 ] || die "no roots configured (AGENT_SKILL_ROOTS / --roots is empty)."

# Every configured root directory must exist — a missing root is itself a partition, and this stays
# a hard exit-2 whatever the roots' source. Name that source: on the default set, an absent root is
# far more often "this tree is not a scaffolded product" than a real partition, and a bare "root is
# absent" is indistinguishable from the twin drift this gate exists to catch (.github#517).
#
# The guidance goes to stderr and the VERDICT through die: `::error::` is a GitHub workflow command,
# parsed only to the first newline, so a multi-line die would annotate line 1 and dump the rest as
# loose log text — the lines that matter would be the ones that fell out of the annotation.
for r in "${ROOT_ARR[@]}"; do
  [ -d "$PRODUCT/$r" ] && continue
  if [ -n "$ROOTS_FROM_DEFAULT" ]; then
    {
      echo "skill-union-assert: the roots came from the scaffolded-product default ('$DEFAULT_ROOTS')."
      echo "  If '$PRODUCT' is NOT a scaffolded product, declare the roots it actually keeps in"
      echo "  $PRODUCT/$ROOTS_DECL_FILE (for example, one root for an intentionally single-runtime tree), and re-run."
      echo "  If it IS a scaffolded product, this is a REAL partition: the producer never materialized '$r'."
    } >&2
  fi
  die "configured root is absent: $PRODUCT/$r (roots from $ROOTS_SRC)"
done

# --params is meaningless without a manifest to read materializes-when from — the conditions live
# on the manifest entries (ADR-0017), --params only supplies the values to evaluate them against.
[ -z "$PARAMS" ] || [ -n "$MANIFEST" ] || die "--params requires --manifest (materializes-when lives on the manifest entries)."

# Manifest ids + their declared digests + materializes-when predicates
# (JSON: {skills:[{id,scope,sha256,materializes-when?},...]}). Requires jq only when --manifest is
# supplied. `materializes-when` defaults to `always` (ADR-0017: absent ⇒ always emitted).
declare -A MANIFEST_SHA=()
declare -A MANIFEST_WHEN=()
declare -A MANIFEST_HAS_FILES=()
declare -A MANIFEST_TREE=()
MANIFEST_IDS=""
MANIFEST_TOTAL=0        # ids the manifest declares — the population the manifest→disk counts are over
if [ -n "$MANIFEST" ]; then
  [ -f "$MANIFEST" ] || die "manifest not found: $MANIFEST"
  command -v jq >/dev/null 2>&1 || die "jq not found (required to read --manifest)."
  jq -e '.skills | type == "array"' "$MANIFEST" >/dev/null 2>&1 \
    || die "manifest has no .skills array: $MANIFEST"
  # UNIT SEPARATOR, NOT TAB (.github#1513). These rows were read as `@tsv` with `IFS=$'\t'`, and TAB is
  # an IFS *whitespace* character: bash COLLAPSES runs of it even when IFS names it explicitly, so an
  # empty field in the MIDDLE of a row silently disappears and every later field shifts left. A skill
  # declared with `"sha256": ""` — which `SkillMirror.ExpectedSkill` defines as "no reference digest",
  # a legitimate row — therefore had its `materializes-when` predicate read as its DIGEST, and its
  # condition read as empty (which `eval_condition` treats as `always`). Two wrong answers from one
  # unnoticed shift: a spurious `[drifted]` on a row that declared no digest, and a false `[missing]`
  # for a legitimately off-profile skill. `\x1f` is not IFS whitespace, so empty fields survive.
  # (A trailing empty field happened to survive the tab form; a middle one never did.)
  while IFS=$'\x1f' read -r id sha when; do
    [ -n "$id" ] || continue
    MANIFEST_SHA["$id"]="$sha"
    MANIFEST_WHEN["$id"]="$when"
    MANIFEST_IDS="$MANIFEST_IDS $id"
    MANIFEST_TOTAL=$((MANIFEST_TOTAL + 1))
  done < <(jq -r '.skills[] | [.id, (.sha256 // ""), (.["materializes-when"] // "always")] | join("\u001f")' "$MANIFEST")
  while IFS=$'\x1f' read -r id has_files tree_sha; do
    [ -n "$id" ] || continue
    MANIFEST_HAS_FILES["$id"]="$has_files"
    MANIFEST_TREE["$id"]="$tree_sha"
  done < <(jq -r '.skills[] | [.id, ((.files | type) == "array" | tostring), (.["tree-sha256"] // "")] | join("\u001f")' "$MANIFEST")
fi

# Scaffold parameters (scaffold-provenance.json → .effectiveParameters), read only with --params.
[ -z "$PARAMS" ] || load_params "$PARAMS"

# Union of skill ids = every subdirectory across all roots. Manifest ids are NOT unioned in:
# the manifest is a superset catalog (emission is lifecycle/profile-conditioned), so an id that
# is declared but materialized nowhere is legitimate — check 3 only cross-checks what exists.
union_ids() {
  for r in "${ROOT_ARR[@]}"; do
    find "$PRODUCT/$r" -mindepth 1 -maxdepth 1 -type d -exec basename {} \; 2>/dev/null
  done | LC_ALL=C sort -u
}

# Is <id> absent from EVERY root? (Present-in-some is handled by the main loop as [partitioned];
# only absent-everywhere is a candidate for [missing] / the legitimate declared-absent count.)
absent_everywhere() {
  local id="$1" r
  for r in "${ROOT_ARR[@]}"; do
    [ -d "$PRODUCT/$r/$id" ] && return 1
  done
  return 0
}

fail=0
present_ct=0; identical_ct=0; manifest_ct=0; cotenant_ct=0; skill_ct=0
# The populations the byte comparison is reported OVER (.github#1506). `comparable` = ids present in at
# least two roots (there is a pair to diff); `compared` = ids this run actually diffed. They must be
# EQUAL, and printing both is what makes a re-introduced short-circuit visible instead of silent.
partitioned_ct=0; comparable_ct=0; compared_ct=0; differing_ct=0; single_root_ct=0
# ...and the SAME discipline for the manifest check (.github#1513), which #1506 left bare. `declared` =
# union ids the manifest declares (what check 3 CAN examine); `comparable` = of those, the ones that
# reached the comparison with a reference to compare against; `examined` = the ones it actually
# compared; `matched` = the ones that came back clean. `no-reference` is the honest home for a declared
# id with neither a `sha256` nor a `files` array: NOTHING was compared for it, and "nothing to compare"
# must never be folded into "compared, and matching" (#266) — the trap #1506 closed for byte-identity.
mdeclared_ct=0; mcomparable_ct=0; mexamined_ct=0; mnoref_ct=0
undeclared_ct=0; dangling_ct=0; unexpected_ct=0

echo "skill-union-assert: product='$PRODUCT' roots='${ROOT_ARR[*]}' (from $ROOTS_SRC)${MANIFEST:+ manifest='$MANIFEST'}${CO_TENANTS:+ co-tenants='$CO_TENANTS'}"

while IFS= read -r id; do
  [ -n "$id" ] || continue
  skill_ct=$((skill_ct + 1))

  # Which roots HAVE this skill and which lack it — collected ONCE, because checks 1 and 2 ask
  # independent questions of the same id and each needs a different half of this answer.
  present_roots=(); missing=""
  for r in "${ROOT_ARR[@]}"; do
    if [ -d "$PRODUCT/$r/$id" ]; then present_roots+=("$r"); else missing="$missing $r"; fi
  done

  # (1) present in EVERY root. No `partitioned=1` flag any more: the ONLY reader it ever had was the
  # `continue` that suppressed check 3 (.github#1513), and a flag kept alive after its last reader is
  # how the next short-circuit gets written back in — the state to hang it on is already there.
  if [ -n "$missing" ]; then
    echo "::error::[partitioned] skill '$id' is absent from root(s):$missing"
    fail=1
    partitioned_ct=$((partitioned_ct + 1))
  else
    present_ct=$((present_ct + 1))
  fi

  # (2) byte-identical across the roots the skill IS PRESENT IN — asked of a [partitioned] id TOO, and
  # that is the whole of .github#1506. Check 1 used to `continue` here, so a partition suppressed the
  # comparison and then the summary counted `byte-identical` over only the survivors. On
  # FS.GG.Rendering@main that printed `present=4 byte-identical=4` next to 46 [partitioned] ids while 30
  # of those 46 DIFFERED between the two roots that both held them — bytes the gate had in hand and never
  # diffed. Absence and drift are independent facts about an id; an id can be BOTH, and both are reported.
  #
  # The reference is the first root that HAS the skill, not ROOT_ARR[0] (which a partitioned id may be
  # absent from). For a whole id the two are the same root, so the [divergent] message is unchanged.
  #
  # An id present in exactly ONE root is genuinely NOT comparable — there is no second copy — and it is
  # counted as `single-root`, never as `byte-identical`. "There was nothing to compare" must never render
  # as "compared, and identical" (#266). Note this also fixes the single-root ROOT SET: with one
  # configured root the old `${ROOT_ARR[@]:1}` loop was empty, fell through, and incremented
  # `byte-identical` for every id — byte-identity asserted over a comparison that never happened.
  # `${...[0]-}`, not `${...[0]}`: an id came out of `union_ids` because some root HAS it, so this
  # array is never empty — but an unguarded index under `set -u` turns any future violation of that
  # assumption into an `unbound variable` abort with exit 1, which is the code reserved for "the union
  # is violated". A crash must not be able to render as a verdict (#266).
  ref="${present_roots[0]-}"
  differing=""
  if [ "${#present_roots[@]}" -ge 2 ]; then
    comparable_ct=$((comparable_ct + 1))
    for r in "${present_roots[@]:1}"; do
      if ! diff -r "$PRODUCT/$ref/$id" "$PRODUCT/$r/$id" >/dev/null 2>&1; then
        differing="$differing $r"
      fi
    done
    compared_ct=$((compared_ct + 1))
    if [ -n "$differing" ]; then
      echo "::error::[divergent] skill '$id' differs between root '$ref' and root(s):$differing"
      fail=1
      differing_ct=$((differing_ct + 1))
    else
      identical_ct=$((identical_ct + 1))
    fi
  else
    single_root_ct=$((single_root_ct + 1))
  fi

  # (3) matches-manifest — only when a manifest is supplied. Producer semantics (.github#120):
  # declared∧present ⇒ SKILL.md digest must match; undeclared ⇒ dangling unless co-tenant.
  #
  # ASKED OF A [partitioned] OR [divergent] ID TOO, and that is .github#1513. Checks 1-2 used to
  # `continue` here, so an id that was both partitioned and digest-mismatched reported ONLY the
  # partition and its declared digest was never read — while `SkillMirror.verify` returns
  # `MissingRoots` and `HashMismatchRoots` on the SAME record, computed independently. A repair plan
  # needs to know it faces both a missing projection AND a body that does not match what the producer
  # declared; being told only the first sends it to rematerialize bytes that will still be wrong.
  #
  # AND IT IS ASKED OF EVERY PRESENT ROOT, not of one representative. See the header: the reference
  # root is the FIRST root that has the skill, and when that one happens to be clean a
  # representative-root digest reports nothing at all about the root that drifted.
  if [ -n "$MANIFEST" ]; then
    if [ -z "${MANIFEST_SHA[$id]+x}" ]; then
      undeclared_ct=$((undeclared_ct + 1))
      if is_co_tenant "$id"; then
        cotenant_ct=$((cotenant_ct + 1))
        continue
      fi
      echo "::error::[dangling] skill '$id' is present in the roots but the manifest does not declare it (and it matches no --co-tenants pattern)"
      fail=1
      dangling_ct=$((dangling_ct + 1))
      continue
    fi
    mdeclared_ct=$((mdeclared_ct + 1))
    # (4) condition-aware — declared ∧ PRESENT ∧ materializes-when FALSE ⇒ [unexpected]
    # (materialized off-profile). Only with --params; else fall through to the digest check.
    if [ -n "$PARAMS" ] && ! eval_condition "${MANIFEST_WHEN[$id]}"; then
      echo "::error::[unexpected] skill '$id' is materialized but its materializes-when ('${MANIFEST_WHEN[$id]}') is false for the scaffold parameters"
      fail=1
      unexpected_ct=$((unexpected_ct + 1))
      continue
    fi
    want="${MANIFEST_SHA[$id]}"
    # A declared id with NEITHER a `files` array NOR a `sha256` carries no reference to compare
    # against. Counted apart, never as a match: `manifest-matched` must mean "compared, and matching".
    if [ "${MANIFEST_HAS_FILES[$id]-false}" != "true" ] && [ -z "$want" ]; then
      mnoref_ct=$((mnoref_ct + 1))
      continue
    fi
    mcomparable_ct=$((mcomparable_ct + 1))
    drifted=""
    if [ "${MANIFEST_HAS_FILES[$id]-false}" = "true" ]; then
      # v2: compare the directory as a closed transport unit. This catches missing and extra files,
      # divergent bytes, and both directions of executable-mode drift. The manifest's own
      # `tree-sha256` is a property of the MANIFEST, not of a root, so it is checked once per id.
      #
      # The per-file comparison runs per PRESENT ROOT, for the same reason the SKILL.md digest does:
      # a divergent id has different bytes in different roots, and auditing one of them is a claim
      # about the id that only one root's evidence supports. The library has no v2 counterpart to
      # follow here (`ActualCopy.Body` is the SKILL.md body alone) — this arm is this repo's own
      # extension, and it takes the same per-root rule rather than inventing a second one.
      if [ -n "${MANIFEST_TREE[$id]-}" ]; then
        tree_got="$(jq -c --arg id "$id" '.skills[] | select(.id == $id) | .files' "$MANIFEST" \
          | tr -d '\n' | sha256sum | cut -d' ' -f1)"
        if [ "$tree_got" != "${MANIFEST_TREE[$id]}" ]; then
          echo "::error::[drifted] skill '$id' file manifest digest $tree_got != tree-sha256 ${MANIFEST_TREE[$id]}"
          fail=1
          drifted=1
        fi
      fi
      # THE DECLARED SET IS A PROPERTY OF THE MANIFEST, SO IT IS READ ONCE — and so is the complaint
      # about an unsafe path in it. Reading it inside the root loop would re-run jq per root and, worse,
      # print `manifest contains unsafe file path` once PER ROOT: a manifest-level fact reported as
      # though it were a per-root finding, which is the same category error this change exists to fix,
      # pointed the other way. `tree-sha256` above is hoisted for exactly the same reason.
      declare -A expected_master=()
      while IFS=$'\x1f' read -r path sha executable; do
        if [ -z "$path" ] || [[ "$path" = /* ]] || [[ "$path" == ../* ]] || [[ "$path" == */../* ]]; then
          echo "::error::[drifted] skill '$id' manifest contains unsafe file path '$path'"
          fail=1
          drifted=1
          continue
        fi
        # \x1f here too, for the reason above: `$sha` is empty in a malformed row and a leading
        # TAB would collapse, shifting `$executable` into `file_want`.
        expected_master["$path"]="$sha"$'\x1f'"$executable"
      done < <(jq -r --arg id "$id" '.skills[] | select(.id == $id) | .files[] | [.path, (.sha256 // ""), (.executable | tostring)] | join("\u001f")' "$MANIFEST")
      for mr in "${present_roots[@]}"; do
        # A fresh COPY per root: the walk below `unset`s each path as it matches, so sharing one table
        # would leave it empty by the second root and report every declared file missing there. Asserted
        # by the multi-root v2 leg in tests/skill-union/run.sh, not left to `declare -A x=()` semantics.
        declare -A expected=()
        for ep in "${!expected_master[@]}"; do expected["$ep"]="${expected_master[$ep]}"; done
        while IFS= read -r -d '' full; do
          path="${full#"$PRODUCT/$mr/$id/"}"
          if [ -z "${expected[$path]+x}" ]; then
            echo "::error::[drifted] skill '$id' in root '$mr' has extra undeclared file '$path'"
            fail=1
            drifted=1
            continue
          fi
          IFS=$'\x1f' read -r file_want exec_want <<< "${expected[$path]}"
          if [ ! -f "$full" ] || [ -L "$full" ]; then
            echo "::error::[drifted] skill '$id' in root '$mr' path '$path' is not a regular file"
            fail=1
            drifted=1
            unset 'expected[$path]'
            continue
          fi
          file_got="$(sha256sum "$full" | cut -d' ' -f1)"
          [ -x "$full" ] && exec_got=true || exec_got=false
          if [ "$file_got" != "$file_want" ]; then
            echo "::error::[drifted] skill '$id' in root '$mr' file '$path' digest $file_got != manifest $file_want"
            fail=1
            drifted=1
          elif [ "$exec_got" != "$exec_want" ]; then
            echo "::error::[drifted] skill '$id' in root '$mr' file '$path' executable=$exec_got != manifest $exec_want"
            fail=1
            drifted=1
          fi
          unset 'expected[$path]'
        done < <(find "$PRODUCT/$mr/$id" -mindepth 1 ! -type d -print0 | LC_ALL=C sort -z)
        for path in "${!expected[@]}"; do
          echo "::error::[drifted] skill '$id' in root '$mr' is missing declared file '$path'"
          fail=1
          drifted=1
        done
      done
    else
      # v1: the canonical SKILL.md digest, taken in EVERY present root and reported as a ROOT LIST —
      # the shape of the library's `HashMismatchRoots`, and one line however many roots drifted
      # (`::error::` is a workflow command parsed only to the first newline).
      mismatch_roots=""
      noskill_roots=""
      for mr in "${present_roots[@]}"; do
        if ! got="$(skill_digest "$PRODUCT/$mr/$id")"; then
          noskill_roots="$noskill_roots $mr"
          continue
        fi
        [ "$got" = "$want" ] || mismatch_roots="$mismatch_roots $mr=$got"
      done
      if [ -n "$noskill_roots" ]; then
        echo "::error::[drifted] skill '$id' has no SKILL.md to digest (manifest declares $want) in root(s):$noskill_roots"
        fail=1
        drifted=1
      fi
      if [ -n "$mismatch_roots" ]; then
        echo "::error::[drifted] skill '$id' SKILL.md digest != manifest $want in root(s):$mismatch_roots"
        fail=1
        drifted=1
      fi
    fi
    mexamined_ct=$((mexamined_ct + 1))
    [ -n "$drifted" ] || manifest_ct=$((manifest_ct + 1))
  fi
done < <(union_ids)

# Manifest→disk direction (the blind spot check 1-3 never enforced). A declared id absent from
# EVERY root is either legitimate (no --params ⇒ superset catalog; or --params ∧ condition FALSE ⇒
# justified off-profile omission) or a [missing] failure (--params ∧ condition TRUE — the dropped-
# skill case, e.g. fs-gg-project). Present-in-some ids were already handled by the main loop.
declared_absent_ct=0
missing_ct=0
if [ -n "$MANIFEST" ]; then
  for id in $MANIFEST_IDS; do
    absent_everywhere "$id" || continue
    if [ -n "$PARAMS" ] && eval_condition "${MANIFEST_WHEN[$id]-always}"; then
      echo "::error::[missing] skill '$id' is declared with a true materializes-when ('${MANIFEST_WHEN[$id]-always}') but is absent from every root"
      fail=1
      missing_ct=$((missing_ct + 1))
    else
      declared_absent_ct=$((declared_absent_ct + 1))
    fi
  done
fi

# An empty union is a misconfiguration (exit 2) — UNLESS check 4 already found real [missing]
# violations (every declared, required skill dropped): those are a genuine coherence failure and
# must exit 1, not be masked by the misconfig die below.
if [ "$skill_ct" -eq 0 ] && [ "$fail" -eq 0 ]; then
  die "no skills found under any root — expected at least one skill in the union."
fi

# NON-VACUITY, ASSERTED RATHER THAN COMMENTED (.github#1506, #266). Every id with a pair of copies must
# have been diffed. If these two counts ever part company again, the summary below is overstating what it
# examined — which is the defect itself, not a cosmetic one — so it is a FAILURE, loudly, and not a note.
if [ "$compared_ct" -ne "$comparable_ct" ]; then
  echo "::error::skill-union-assert: $comparable_ct skill(s) had copies in 2+ roots but only $compared_ct were byte-compared — the summary below would overstate its own coverage (.github#1506)."
  fail=1
fi

# THE SAME NON-VACUITY, FOR CHECK 3 (.github#1513). #1506 asserted it for the byte comparison and left
# the manifest arm asserted by nothing — which is exactly where the next short-circuit was found. Every
# declared, present, condition-true id with a reference to compare against must have been compared.
if [ "$mexamined_ct" -ne "$mcomparable_ct" ]; then
  echo "::error::skill-union-assert: $mcomparable_ct skill(s) were comparable against the manifest but only $mexamined_ct were examined — the summary below would overstate its own coverage (.github#1513)."
  fail=1
fi

# EVERY COUNT CARRIES THE POPULATION IT WAS TAKEN OVER. This line was
# `present=$present_ct byte-identical=$identical_ct`, and it was true and misread every time it mattered:
# `present=4 byte-identical=4` over a 50-id union is a statement about 4 skills that an operator who does
# not open the log reads as "the union was byte-checked and nothing is divergent". Two whole downstream
# issues were written on that reading. A bare count invites the reader to supply the denominator, and they
# supply the generous one — so `byte-identical=20/50` says what was examined, and `byte-comparable` beside
# `byte-compared` says whether anything comparable went unexamined.
#
# The differing count is spelled `byte-differing`, deliberately NOT the class word: `[divergent]` names a
# DIAGNOSTIC line, and a fixture asserting that no such diagnostic was emitted must not be tripped by a
# zero in a summary. THE NEW MANIFEST COUNTS KEEP THAT CARE: none of them is spelled with a class word,
# so `undeclared-rejected` and `off-profile` rather than `dangling` and `unexpected`. A fixture asserting
# that no `[dangling]` diagnostic was emitted must not be tripped by a ZERO — and a plain unbracketed
# `grep -q dangling` is exactly the shape a fixture in this very suite already uses for `divergent`.
# (`missing=` predates this and is left alone; renaming it would be churn, and it is named here so the
# exception is visible rather than looking like an oversight.)
#
# THE MANIFEST HALF CARRIES ITS POPULATIONS TOO (.github#1513). #1506 gave the byte counts their
# denominators and left `manifest-matched=$manifest_ct` bare — a count with no population, printed
# beside an id that was never examined against the manifest at all. Each denominator names a DIFFERENT
# population on purpose, because these counts are not taken over the same set:
#   manifest-declared=<d>/<union>      how much of the on-disk union the manifest even declares
#   manifest-comparable / -examined    what check 3 could examine vs what it did (the #1506 pair)
#   manifest-matched=<m>/<examined>    of what was compared, how much matched
#   manifest-no-reference=<n>          declared, but with nothing to compare against — never a match
#   undeclared-rejected / co-tenant =<n>/<undeclared>  the two dispositions of an undeclared id; they
#                                      sum to <undeclared> exactly, which is the count checking itself
#   declared-absent / missing =<n>/<declared-in-manifest>   the manifest→disk sweep's own population
manifest_summary=""
if [ -n "$MANIFEST" ]; then
  manifest_summary=" | manifest-declared=$mdeclared_ct/$skill_ct manifest-comparable=$mcomparable_ct manifest-examined=$mexamined_ct manifest-matched=$manifest_ct/$mexamined_ct manifest-no-reference=$mnoref_ct undeclared-rejected=$dangling_ct/$undeclared_ct co-tenant=$cotenant_ct/$undeclared_ct declared-absent=$declared_absent_ct/$MANIFEST_TOTAL"
  [ -z "$PARAMS" ] || manifest_summary="$manifest_summary (justified) missing=$missing_ct/$MANIFEST_TOTAL off-profile=$unexpected_ct/$mdeclared_ct"
fi
echo "skill-union-assert: $skill_ct skill(s) — in-every-root=$present_ct/$skill_ct partitioned=$partitioned_ct | byte-comparable=$comparable_ct byte-compared=$compared_ct byte-identical=$identical_ct/$compared_ct byte-differing=$differing_ct single-root=$single_root_ct$manifest_summary"
if [ "$fail" -ne 0 ]; then
  echo "::error::skill-union-assert: FAILED — the roots are not the byte-identical union (see above)."
  exit 1
fi
echo "skill-union-assert: OK — all roots hold the byte-identical union."
