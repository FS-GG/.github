#!/usr/bin/env bash
# ============================================================================================
# DO NOT EDIT. GENERATED FILE.
#
# Generated from scripts/skill-union-assert.sh + scripts/lib/* by
# scripts/generate-skill-union-bundle. `skill-union-bundle` in CI fails on any drift, so an edit
# here is reverted by the next regeneration — edit the SOURCE and regenerate.
#
# THIS FILE IS THE SUPPORTED STANDALONE ARTIFACT (.github#843). It is self-contained BY
# CONSTRUCTION: it sources no siblings, so fetching this one file at a pinned commit SHA is a
# complete, deterministic, content-addressed gate. Adding a `source` to the source script inlines
# it here; it can no longer break a consumer who fetched one file.
#
#   curl -fsSL https://raw.githubusercontent.com/FS-GG/.github/<40-char-sha>/dist/skill-union-assert.sh
#
# Consumers reaching the assertion through the reusable skill-union-assert.yml workflow do NOT need
# this file — that path checks out the repo and runs scripts/skill-union-assert.sh directly.
# ============================================================================================
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
#   2. byte-identical — its bytes are identical across all roots (else: divergent);
#   3. matches-manifest (only when --manifest is given) — its SKILL.md digest equals the digest
#      the producer's skill-manifest declares (else: drifted), and every root skill is either
#      declared by the manifest or matches a --co-tenants pattern (else: dangling).
#
# Checks 1-2 are self-contained and enforce with nothing but the product tree. Check 3 follows
# the PRODUCERS' manifest semantics (aligned with the shipped manifests per .github#120 —
# `Fsgg.SkillMirror` in FS.GG.Contracts 1.4.0 / SDD#61 and fs-gg-ui-template 0.1.61-preview.1 /
# Rendering#43 are ADR-0014's "one implementation"; the assertion follows them):
#   - digest: canonical-body sha256 of the skill's SKILL.md ONLY (equal to `sha256sum SKILL.md`
#     for a BOM-free body; a leading UTF-8 BOM is stripped first, matching the producer algorithm).
#     Multi-file skills (SKILL.md + references/**) are covered by the
#     cross-root identity of checks 1-2, not by the digest.
#   - set: the manifest is a superset CATALOG, an upper bound — emission is lifecycle/profile-
#     conditioned, so declared∧present ⇒ digest must match; declared∧absent-everywhere ⇒ fine
#     (skipped, counted); present∧undeclared ⇒ dangling UNLESS the id matches a --co-tenants
#     glob (process skills from a co-tenant producer, e.g. "fs-gg-sdd-* speckit-*").
# A skill declared and present in SOME roots but not all still fails check 1 ([partitioned]).
# See docs/coordination/skill-union-assertion.md.
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
# ---- BEGIN INLINED: scripts/lib/args.sh ----------------------------------------------------
# Inlined from scripts/lib/args.sh by scripts/generate-skill-union-bundle. In the repo this is a
# sourced fragment shared with coordination-sync (#525); in this bundle it is inlined so the
# artifact is a single self-contained file. Edit scripts/lib/args.sh and regenerate.
# shellcheck shell=bash
# args.sh — argument-parsing guards shared across scripts/ (FS-GG/.github#356).
#
# A SOURCED fragment, not an executable: no shebang, no top-level effects. Source it AFTER
# defining `die`. The guards report through the caller's own `die`, so each script keeps its
# own message prefix and exit code — the part that is legitimately per-script — while sharing
# the guard body that was not:
#
#   die() { echo "myscript: $*" >&2; exit 2; }
#   . "$(dirname "${BASH_SOURCE[0]}")/lib/args.sh"
#
# need_val was byte-identical in coordination-sync, repos-audit.sh, and skill-union-assert.sh
# before it was hoisted here (#356); the point is ONE place a change to its semantics lands,
# rather than four copies where the next editor finds three and misses the fourth.
#
# scripts/repos.sh deliberately does NOT source this: its need_val takes a <subcommand> as its
# first argument (see the note at its own definition) and is a different function, not a copy.

# need_val <flag> <value…> — a flag given no value is a misconfiguration, not a verdict. Call it
# as `need_val "$@"` from a flag's case arm, before consuming "$2". Reporting through `die` avoids
# `${2:?…}`, whose exit 1 these scripts each reserve for a real finding (drift / an unwired
# receiver / a union violation) — i.e. a verdict rendered without reading a single file.
need_val() { [ $# -ge 2 ] && [ -n "${2:-}" ] || die "$1 needs a value."; }
# ---- END INLINED: scripts/lib/args.sh ------------------------------------------------------
# resolve_roots / read_roots_decl — the root-set precedence and the `.agent-skill-roots` parser, shared
# with coordination-sync so the script that WRITES the roots resolves them exactly as this one, which
# ASSERTS them, does (#525). Sourced after die, which it uses.
# shellcheck source=lib/roots.sh
# ---- BEGIN INLINED: scripts/lib/roots.sh ----------------------------------------------------
# Inlined from scripts/lib/roots.sh by scripts/generate-skill-union-bundle. In the repo this is a
# sourced fragment shared with coordination-sync (#525); in this bundle it is inlined so the
# artifact is a single self-contained file. Edit scripts/lib/roots.sh and regenerate.
# shellcheck shell=bash
# roots.sh — resolve a tree's agent-skill root set, shared across scripts/ (FS-GG/.github#525).
#
# A SOURCED fragment, not an executable: no shebang, no top-level effects. Source it AFTER defining
# `die`. Like lib/args.sh (#356), the guard body is shared and the per-script parts are not — here
# that means the caller supplies its own DEFAULT. Since ADR-0065 every production caller uses the
# same three-root default; the parameter remains for explicit fixtures and future migrations:
#
#   skill-union-assert.sh  product lane — ADR-0011's three (.claude/.codex/.agents), fanned out by fsgg-sdd
#   coordination-sync      kit lane     — the same three, materialized from FS.GG.Kit
#
# It is the PRECEDENCE and the PARSING that must be shared. Before this hoist, only
# the asserter read the tree's `.agent-skill-roots` (#517) and the writer hardcoded its own set, so a
# tree's root set had two sources of truth that agreed only by coincidence of defaults (#525). The
# moment a tree declared anything else they diverged silently, and in the direction that matters:
# the writer would skip a declared root and the asserter would then blame the TREE for the omission.

# read_roots_decl <file> — parse a checked-in roots declaration: whitespace/newline-separated roots,
# `#` comments allowed. `\r` is stripped with the other separators, NOT left on the token: a CRLF
# checkout (Windows, or a .gitattributes `eol=crlf`) would otherwise yield a root '.claude/skills\r',
# whose directory does not exist — reporting "configured root is absent" for a root that is right
# there. That is #517's own failure mode one layer down, and it points at an existing root, so it
# reads even more like drift.
read_roots_decl() {
  sed 's/#.*$//' "$1" | tr '\n\t\r' '   ' | tr -s ' ' | sed 's/^ *//; s/ *$//'
}

# resolve_roots <tree> <default-roots> <default-label> [<explicit-roots>]
#
# Sets ROOTS, ROOTS_SRC (a human label naming which rule won), and ROOTS_FROM_DEFAULT (non-empty iff
# the default was used — callers hint at the declaration when a default-sourced root is missing).
#
# Precedence:  <explicit>  >  $AGENT_SKILL_ROOTS  >  <tree>/.agent-skill-roots  >  <default>
#
# <explicit> is the caller's own flag (`--roots`), already validated by need_val, so a non-empty
# value means "set" and an empty one means "not given" — `--roots ""` cannot reach here. A set-but-
# EMPTY $AGENT_SKILL_ROOTS likewise reads as unset and falls through, exactly as it did when each
# caller wrote this as one `${AGENT_SKILL_ROOTS:-<default>}` expansion.
#
# A declaration that parses to nothing (empty, or all comments) is a MISCONFIGURATION, not an empty
# root set: a tree that bothered to check the file in meant to say something. Falling through to the
# default there would silently substitute a root set the tree explicitly declined — the fail-open
# shape (#266) arriving through a config file that looks authoritative. So: die.
#
# shellcheck disable=SC2034  # ROOTS_SRC / ROOTS_FROM_DEFAULT are this function's OUTPUT, read by the
# caller that sources this fragment — skill-union-assert.sh reads both (its "configured root is absent
# (roots from $ROOTS_SRC)" die, and its banner). A sourced library assigning a variable its consumer
# reads is invisible to a linter pointed at the library alone: shellcheck sees the write and no read,
# and cannot see across the source boundary in that direction. Exporting them to satisfy it would put
# them in the environment of every child process to silence a linter, which is a worse contract than
# the one documented above. #648.
resolve_roots() {
  local tree="$1" default="$2" default_label="$3" explicit="${4:-}"
  local decl="$tree/.agent-skill-roots"

  ROOTS_FROM_DEFAULT=""
  if [ -n "$explicit" ]; then
    ROOTS="$explicit";                ROOTS_SRC="--roots"
  elif [ -n "${AGENT_SKILL_ROOTS:-}" ]; then
    ROOTS="$AGENT_SKILL_ROOTS";       ROOTS_SRC="\$AGENT_SKILL_ROOTS"
  elif [ -f "$decl" ]; then
    ROOTS="$(read_roots_decl "$decl")"
    ROOTS_SRC=".agent-skill-roots"
    [ -n "$ROOTS" ] || die "$decl declares no roots (it is empty, or all comments)."
  else
    ROOTS="$default";                 ROOTS_SRC="$default_label"
    ROOTS_FROM_DEFAULT=1
  fi
}
# ---- END INLINED: scripts/lib/roots.sh ------------------------------------------------------

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
# algorithm (`Fsgg.SkillMirror`, FS.GG.Contracts 1.4.0; equal to `sha256sum SKILL.md` for a
# BOM-free body, verified in .github#120). A leading UTF-8 BOM is stripped before hashing so it can
# never enter the digest on the verifying side either — the exact invariant fsgg-skill-registry-check's
# canonical_digest holds; without it a BOM'd SKILL.md would hash BOM-free in the Python registry check
# but WITH the BOM here, a spurious [drifted] (.github#384). `--digest <skill-dir>` exposes it as a
# reference generator so producers and this assertion never drift. Multi-file remainder is covered by
# checks 1-2.
skill_digest() {
  local dir="$1"
  [ -f "$dir/SKILL.md" ] || return 1
  if [ "$(head -c 3 "$dir/SKILL.md" | od -An -tx1 | tr -d ' \n')" = efbbbf ]; then
    tail -c +4 "$dir/SKILL.md" | sha256sum | cut -d' ' -f1
  else
    sha256sum "$dir/SKILL.md" | cut -d' ' -f1
  fi
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
MANIFEST_IDS=""
if [ -n "$MANIFEST" ]; then
  [ -f "$MANIFEST" ] || die "manifest not found: $MANIFEST"
  command -v jq >/dev/null 2>&1 || die "jq not found (required to read --manifest)."
  jq -e '.skills | type == "array"' "$MANIFEST" >/dev/null 2>&1 \
    || die "manifest has no .skills array: $MANIFEST"
  while IFS=$'\t' read -r id sha when; do
    [ -n "$id" ] || continue
    MANIFEST_SHA["$id"]="$sha"
    MANIFEST_WHEN["$id"]="$when"
    MANIFEST_IDS="$MANIFEST_IDS $id"
  done < <(jq -r '.skills[] | [.id, (.sha256 // ""), (.["materializes-when"] // "always")] | @tsv' "$MANIFEST")
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

echo "skill-union-assert: product='$PRODUCT' roots='${ROOT_ARR[*]}' (from $ROOTS_SRC)${MANIFEST:+ manifest='$MANIFEST'}${CO_TENANTS:+ co-tenants='$CO_TENANTS'}"

while IFS= read -r id; do
  [ -n "$id" ] || continue
  skill_ct=$((skill_ct + 1))

  # (1) present in EVERY root
  missing=""
  for r in "${ROOT_ARR[@]}"; do
    [ -d "$PRODUCT/$r/$id" ] || missing="$missing $r"
  done
  if [ -n "$missing" ]; then
    echo "::error::[partitioned] skill '$id' is absent from root(s):$missing"
    fail=1
    continue
  fi
  present_ct=$((present_ct + 1))

  # (2) byte-identical across roots (reference = first root)
  ref="${ROOT_ARR[0]}"
  divergent=""
  for r in "${ROOT_ARR[@]:1}"; do
    if ! diff -r "$PRODUCT/$ref/$id" "$PRODUCT/$r/$id" >/dev/null 2>&1; then
      divergent="$divergent $r"
    fi
  done
  if [ -n "$divergent" ]; then
    echo "::error::[divergent] skill '$id' differs between root '$ref' and root(s):$divergent"
    fail=1
    continue
  fi
  identical_ct=$((identical_ct + 1))

  # (3) matches-manifest — only when a manifest is supplied. Producer semantics (.github#120):
  # declared∧present ⇒ SKILL.md digest must match; undeclared ⇒ dangling unless co-tenant.
  if [ -n "$MANIFEST" ]; then
    if [ -z "${MANIFEST_SHA[$id]+x}" ]; then
      if is_co_tenant "$id"; then
        cotenant_ct=$((cotenant_ct + 1))
        continue
      fi
      echo "::error::[dangling] skill '$id' is present in the roots but the manifest does not declare it (and it matches no --co-tenants pattern)"
      fail=1
      continue
    fi
    # (4) condition-aware — declared ∧ PRESENT ∧ materializes-when FALSE ⇒ [unexpected]
    # (materialized off-profile). Only with --params; else fall through to the digest check.
    if [ -n "$PARAMS" ] && ! eval_condition "${MANIFEST_WHEN[$id]}"; then
      echo "::error::[unexpected] skill '$id' is materialized but its materializes-when ('${MANIFEST_WHEN[$id]}') is false for the scaffold parameters"
      fail=1
      continue
    fi
    want="${MANIFEST_SHA[$id]}"
    if [ -n "$want" ]; then
      if ! got="$(skill_digest "$PRODUCT/$ref/$id")"; then
        echo "::error::[drifted] skill '$id' has no SKILL.md to digest (manifest declares $want)"
        fail=1
        continue
      fi
      if [ "$got" != "$want" ]; then
        echo "::error::[drifted] skill '$id' SKILL.md digest $got != manifest $want"
        fail=1
        continue
      fi
    fi
    manifest_ct=$((manifest_ct + 1))
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

echo "skill-union-assert: $skill_ct skill(s) — present=$present_ct byte-identical=$identical_ct${MANIFEST:+ manifest-matched=$manifest_ct co-tenant=$cotenant_ct declared-absent=$declared_absent_ct${PARAMS:+ (justified) missing=$missing_ct}}"
if [ "$fail" -ne 0 ]; then
  echo "::error::skill-union-assert: FAILED — the roots are not the byte-identical union (see above)."
  exit 1
fi
echo "skill-union-assert: OK — all roots hold the byte-identical union."
