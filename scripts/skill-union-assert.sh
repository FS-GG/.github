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
# `Option.isSome`). For every union skill it asserts:
#   1. present        — the skill directory exists in EVERY configured root (else: partitioned);
#   2. byte-identical — its bytes are identical across all roots (else: divergent);
#   3. matches-manifest (only when --manifest is given) — its content digest equals the digest
#      the producer's skill-manifest declares, and no root carries a skill the manifest does not
#      declare (else: drifted / dangling).
#
# Checks 1-2 are self-contained and enforce today (they need nothing but the product tree — the
# highest-value, currently-unchecked property). Check 3 activates the moment a producer ships a
# skill-manifest with per-skill digests (FS.GG.SDD#60 / FS.GG.Rendering#43, ADR-0014 P0/P2);
# until then the assertion runs the content-equality half and skips the manifest cross-check.
# This is publish-before-flip: the reusable mechanism lands and can enforce cross-root identity
# now; the manifest side wires in when the manifest exists.
#
# The per-skill content digest (check 3) is a deterministic tree hash so it survives multi-file
# skills (SKILL.md + references/**): sha256 over the C-locale-sorted stream of "<relpath>\n<sha256
# of that file>\n" for every regular file under the skill dir. A producer's manifest MUST emit
# `sha256` with the SAME algorithm. See docs/coordination/skill-union-assertion.md.
#
# Usage:
#   skill-union-assert.sh --product <dir> [--roots "<r1> <r2> ..."] [--manifest <file.json>]
# Roots default to AGENT_SKILL_ROOTS (env) or ADR-0011's three: ".claude/skills .codex/skills
# .agents/skills". Roots are resolved relative to --product. Exit 0 = union coherent; non-zero =
# at least one violation (each printed with its class).

set -euo pipefail

PRODUCT="."
ROOTS="${AGENT_SKILL_ROOTS:-.claude/skills .codex/skills .agents/skills}"
MANIFEST=""

die() { echo "::error::skill-union-assert: $*" >&2; exit 2; }

DIGEST_ONLY=""

while [ $# -gt 0 ]; do
  case "$1" in
    --product)  PRODUCT="${2:?--product needs a value}"; shift 2 ;;
    --roots)    ROOTS="${2:?--roots needs a value}"; shift 2 ;;
    --manifest) MANIFEST="${2:?--manifest needs a value}"; shift 2 ;;
    --digest)   DIGEST_ONLY="${2:?--digest needs a skill dir}"; shift 2 ;;
    -h|--help)  sed -n '2,40p' "$0"; exit 0 ;;
    *)          die "unknown argument: $1" ;;
  esac
done

command -v sha256sum >/dev/null 2>&1 || die "sha256sum not found (required for content hashing)."

# Deterministic per-skill content tree hash (see header). This is the CANONICAL digest algorithm
# a producer's skill-manifest must use for its per-skill `sha256`; `--digest <skill-dir>` exposes
# it as a reference generator so producers and this assertion never drift.
skill_digest() {
  local dir="$1" stream
  stream="$(cd "$dir" && find . -type f | LC_ALL=C sort | while IFS= read -r f; do
    printf '%s\n' "$f"
    sha256sum "$f" | cut -d' ' -f1
  done)"
  printf '%s' "$stream" | sha256sum | cut -d' ' -f1
}

if [ -n "$DIGEST_ONLY" ]; then
  [ -d "$DIGEST_ONLY" ] || die "skill dir not found: $DIGEST_ONLY"
  skill_digest "$DIGEST_ONLY"
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

# Union of skill ids = every subdirectory across all roots, plus every manifest id.
union_ids() {
  {
    for r in "${ROOT_ARR[@]}"; do
      find "$PRODUCT/$r" -mindepth 1 -maxdepth 1 -type d -exec basename {} \; 2>/dev/null
    done
    for id in $MANIFEST_IDS; do printf '%s\n' "$id"; done
  } | LC_ALL=C sort -u
}

fail=0
present_ct=0; identical_ct=0; manifest_ct=0; skill_ct=0

echo "skill-union-assert: product='$PRODUCT' roots='${ROOT_ARR[*]}'${MANIFEST:+ manifest='$MANIFEST'}"

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

  # (3) matches-manifest — only when a manifest is supplied
  if [ -n "$MANIFEST" ]; then
    if [ -z "${MANIFEST_SHA[$id]+x}" ]; then
      echo "::error::[dangling] skill '$id' is present in the roots but the manifest does not declare it"
      fail=1
      continue
    fi
    want="${MANIFEST_SHA[$id]}"
    if [ -n "$want" ]; then
      got="$(skill_digest "$PRODUCT/$ref/$id")"
      if [ "$got" != "$want" ]; then
        echo "::error::[drifted] skill '$id' digest $got != manifest $want"
        fail=1
        continue
      fi
    fi
    manifest_ct=$((manifest_ct + 1))
  fi
done < <(union_ids)

if [ "$skill_ct" -eq 0 ]; then
  die "no skills found under any root — expected at least one skill in the union."
fi

echo "skill-union-assert: $skill_ct skill(s) — present=$present_ct byte-identical=$identical_ct${MANIFEST:+ manifest-matched=$manifest_ct}"
if [ "$fail" -ne 0 ]; then
  echo "::error::skill-union-assert: FAILED — the roots are not the byte-identical union (see above)."
  exit 1
fi
echo "skill-union-assert: OK — all roots hold the byte-identical union."
