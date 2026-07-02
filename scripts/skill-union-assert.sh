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
#   2. byte-identical — its bytes are identical across all roots (else: divergent);
#   3. matches-manifest (only when --manifest is given) — its SKILL.md digest equals the digest
#      the producer's skill-manifest declares (else: drifted), and every root skill is either
#      declared by the manifest or matches a --co-tenants pattern (else: dangling).
#
# Checks 1-2 are self-contained and enforce with nothing but the product tree. Check 3 follows
# the PRODUCERS' manifest semantics (aligned with the shipped manifests per .github#120 —
# `Fsgg.SkillMirror` in FS.GG.Contracts 1.4.0 / SDD#61 and fs-gg-ui-template 0.1.61-preview.1 /
# Rendering#43 are ADR-0014's "one implementation"; the assertion follows them):
#   - digest: canonical-body sha256 of the skill's SKILL.md ONLY (byte-equivalent to
#     `sha256sum SKILL.md`). Multi-file skills (SKILL.md + references/**) are covered by the
#     cross-root identity of checks 1-2, not by the digest.
#   - set: the manifest is a superset CATALOG, an upper bound — emission is lifecycle/profile-
#     conditioned, so declared∧present ⇒ digest must match; declared∧absent-everywhere ⇒ fine
#     (skipped, counted); present∧undeclared ⇒ dangling UNLESS the id matches a --co-tenants
#     glob (process skills from a co-tenant producer, e.g. "fs-gg-sdd-* speckit-*").
# A skill declared and present in SOME roots but not all still fails check 1 ([partitioned]).
# See docs/coordination/skill-union-assertion.md.
#
# Usage:
#   skill-union-assert.sh --product <dir> [--roots "<r1> <r2> ..."] [--manifest <file.json>]
#                         [--co-tenants "<glob> <glob> ..."]
#   skill-union-assert.sh --digest <skill-dir>   # print the canonical SKILL.md digest and exit
# Roots default to AGENT_SKILL_ROOTS (env) or ADR-0011's three: ".claude/skills .codex/skills
# .agents/skills". Roots are resolved relative to --product. Exit 0 = union coherent; 1 = at least
# one violation (each printed with its class); 2 = misconfiguration. `-h`/`--help` prints usage.

set -euo pipefail

PRODUCT="."
ROOTS="${AGENT_SKILL_ROOTS:-.claude/skills .codex/skills .agents/skills}"
MANIFEST=""
CO_TENANTS=""

die() { echo "::error::skill-union-assert: $*" >&2; exit 2; }

usage() {
  cat <<'EOF'
skill-union-assert.sh — assert a scaffolded product's agent-skill roots are the byte-identical
union of process + product skills (ADR-0014 P3.G3.1, FS-GG/.github#111).

Usage:
  skill-union-assert.sh --product <dir> [--roots "<r1> <r2> ..."] [--manifest <file.json>]
                        [--co-tenants "<glob> <glob> ..."]
  skill-union-assert.sh --digest <skill-dir>

Options:
  --product <dir>         product tree to check (default: ".")
  --roots "<r1> ..."      space-separated skill roots, relative to --product
                          (default: $AGENT_SKILL_ROOTS or ".claude/skills .codex/skills .agents/skills")
  --manifest <file.json>  producer skill-manifest; enables the digest cross-check (check 3)
  --co-tenants "<glob>…"  globs of undeclared co-tenant skill ids to admit (only with --manifest)
  --digest <skill-dir>    reference generator: print the canonical-body sha256 of the dir's SKILL.md,
                          then exit (so producers and this assertion never drift)
  -h, --help              print this help

Exit: 0 = union coherent; 1 = at least one violation (each printed with its class); 2 = misconfiguration.
EOF
}

DIGEST_ONLY=""

while [ $# -gt 0 ]; do
  case "$1" in
    --product)    PRODUCT="${2:?--product needs a value}"; shift 2 ;;
    --roots)      ROOTS="${2:?--roots needs a value}"; shift 2 ;;
    --manifest)   MANIFEST="${2:?--manifest needs a value}"; shift 2 ;;
    --co-tenants) CO_TENANTS="${2:?--co-tenants needs a value}"; shift 2 ;;
    --digest)     DIGEST_ONLY="${2:?--digest needs a skill dir}"; shift 2 ;;
    -h|--help)    usage; exit 0 ;;
    *)            die "unknown argument: $1" ;;
  esac
done

command -v sha256sum >/dev/null 2>&1 || die "sha256sum not found (required for content hashing)."

# Per-skill digest (see header): canonical-body sha256 of SKILL.md only — the producers' shipped
# algorithm (`Fsgg.SkillMirror`, FS.GG.Contracts 1.4.0; byte-equivalent to `sha256sum SKILL.md`,
# verified in .github#120). `--digest <skill-dir>` exposes it as a reference generator so
# producers and this assertion never drift. Multi-file remainder is covered by checks 1-2.
skill_digest() {
  local dir="$1"
  [ -f "$dir/SKILL.md" ] || return 1
  sha256sum "$dir/SKILL.md" | cut -d' ' -f1
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

if [ -n "$DIGEST_ONLY" ]; then
  [ -d "$DIGEST_ONLY" ] || die "skill dir not found: $DIGEST_ONLY"
  skill_digest "$DIGEST_ONLY" || die "no SKILL.md under: $DIGEST_ONLY"
  exit 0
fi

[ -d "$PRODUCT" ] || die "product tree not found: $PRODUCT"

# shellcheck disable=SC2206
ROOT_ARR=($ROOTS)
[ "${#ROOT_ARR[@]}" -ge 1 ] || die "no roots configured (AGENT_SKILL_ROOTS / --roots is empty)."

# Every configured root directory must exist — a missing root is itself a partition.
for r in "${ROOT_ARR[@]}"; do
  [ -d "$PRODUCT/$r" ] || die "configured root is absent: $PRODUCT/$r"
done

# Manifest ids + their declared digests (JSON: {skills:[{id,scope,sha256},...]}). Requires jq
# only when --manifest is supplied.
declare -A MANIFEST_SHA=()
MANIFEST_IDS=""
if [ -n "$MANIFEST" ]; then
  [ -f "$MANIFEST" ] || die "manifest not found: $MANIFEST"
  command -v jq >/dev/null 2>&1 || die "jq not found (required to read --manifest)."
  jq -e '.skills | type == "array"' "$MANIFEST" >/dev/null 2>&1 \
    || die "manifest has no .skills array: $MANIFEST"
  while IFS=$'\t' read -r id sha; do
    [ -n "$id" ] || continue
    MANIFEST_SHA["$id"]="$sha"
    MANIFEST_IDS="$MANIFEST_IDS $id"
  done < <(jq -r '.skills[] | [.id, (.sha256 // "")] | @tsv' "$MANIFEST")
fi

# Union of skill ids = every subdirectory across all roots. Manifest ids are NOT unioned in:
# the manifest is a superset catalog (emission is lifecycle/profile-conditioned), so an id that
# is declared but materialized nowhere is legitimate — check 3 only cross-checks what exists.
union_ids() {
  for r in "${ROOT_ARR[@]}"; do
    find "$PRODUCT/$r" -mindepth 1 -maxdepth 1 -type d -exec basename {} \; 2>/dev/null
  done | LC_ALL=C sort -u
}

fail=0
present_ct=0; identical_ct=0; manifest_ct=0; cotenant_ct=0; skill_ct=0

echo "skill-union-assert: product='$PRODUCT' roots='${ROOT_ARR[*]}'${MANIFEST:+ manifest='$MANIFEST'}${CO_TENANTS:+ co-tenants='$CO_TENANTS'}"

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

# Declared-but-absent-everywhere ids are fine (superset catalog) — surface the count for signal.
declared_absent_ct=0
if [ -n "$MANIFEST" ]; then
  for id in $MANIFEST_IDS; do
    [ -d "$PRODUCT/${ROOT_ARR[0]}/$id" ] || declared_absent_ct=$((declared_absent_ct + 1))
  done
fi

if [ "$skill_ct" -eq 0 ]; then
  die "no skills found under any root — expected at least one skill in the union."
fi

echo "skill-union-assert: $skill_ct skill(s) — present=$present_ct byte-identical=$identical_ct${MANIFEST:+ manifest-matched=$manifest_ct co-tenant=$cotenant_ct declared-absent=$declared_absent_ct}"
if [ "$fail" -ne 0 ]; then
  echo "::error::skill-union-assert: FAILED — the roots are not the byte-identical union (see above)."
  exit 1
fi
echo "skill-union-assert: OK — all roots hold the byte-identical union."
