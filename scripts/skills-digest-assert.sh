#!/usr/bin/env bash
# skills-digest-assert.sh — enforce "registry = manifest = bytes" for registry/skills.yml (.github#247).
#
# Every row in the skill registry declares a `source` (the owning repo's canonical SKILL.md) and the
# `sha256` it was reconciled from. The file header calls those rows "GENERATED / RECONCILED FROM THE
# PRODUCER MANIFESTS — never hand-authored bytes", and every changelog entry restates the invariant.
# Nothing checked it. Each producer drift-guards its OWN manifest against its OWN bodies (FS.GG.SDD,
# FS.GG.Rendering, FS.GG.Game each run a generate-skill-manifest --check in their gate), but no gate
# verified the CROSS-REPO half: that .github's registry still agrees with those bodies. So a producer
# could merge a body change and the registry would silently go stale — which is exactly what happened
# (15 of 32 rows were stale when this gate was first run; see registry/skills.CHANGELOG.md 2026-07-08).
#
# This is the AUTHORITY-side check, mirroring scripts/repos-audit.sh: .github reads the producer repos
# and audits its own registry against them. For each row it asserts
#
#   1. `source` resolves to an FS-GG repo and its `owner` agrees with that repo (a row cannot claim
#      owner: fs-gg-game while sourcing from FS.GG.Rendering),
#   2. `source` still EXISTS at the owner's <ref> (a renamed or moved skill currently goes unnoticed),
#   3. sha256 of the source's bytes equals the declared `sha256` — the digest rule of
#      `scripts/repos.sh digest` / `Fsgg.SkillMirror.sha256`: a plain `sha256sum` of the SKILL.md bytes.
#
# and then, unless --no-mirror, the ADR-0022 §6 two-copies invariant:
#
#   4. where FS.GG.Rendering still ships a FROZEN copy of a `fs-gg-game`-owned body, that copy is
#      byte-identical to Game's canonical one. Self-retiring: when the P6 provider epic deletes the
#      frozen copies, the check finds nothing to compare and goes quiet on its own — no list to prune.
#
# Reads other repos over the GitHub API (gh). The FS-GG repos are public, so the run-scoped
# GITHUB_TOKEN reads them cross-repo (exactly as contract-coherence.yml reads FS.GG.SDD). The gh calls
# are isolated behind fetch_file/fetch_or_die so the fixture can stub them with a PATH shim.
#
# An INFRASTRUCTURE failure is never a registry fact. A 404 means the source is gone; a 403 rate
# limit, a 5xx, a DNS blip or an expired token mean we do not know — and the audit exits 2 saying so
# rather than inventing a verdict in either direction (see fetch_file).
#
# Note the asymmetry that makes this gate meaningful: the registry is read from the WORKING TREE (the
# PR under review), while sources are read at the producer's <ref> (default `main`). A .github PR that
# re-digests a row before the producer has merged the matching body therefore fails — the same
# publish-before-flip ordering the dependency registry obeys.
#
# Usage:
#   skills-digest-assert.sh [--registry <file>] [--ref <git-ref>] [--no-mirror] [--quiet]
# Exit: 0 = every row coherent; 1 = at least one violation (stale digest / missing source / drifted
#       frozen copy); 2 = misconfig OR an infrastructure failure that made the audit inconclusive.

set -euo pipefail

HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REGISTRY="$HERE/../registry/skills.yml"
REF="main"
ORG="FS-GG"
MIRROR=1
QUIET=0
MIRROR_REPO="FS.GG.Rendering"                 # holds the frozen copies (ADR-0022 §6)
MIRROR_OWNER="fs-gg-game"                     # of bodies owned by this producer
MIRROR_PATH="template/product-skills"         # at <MIRROR_REPO>/<MIRROR_PATH>/<id>/SKILL.md

die() { echo "::error::skills-digest-assert: $*" >&2; exit 2; }
say() { [ "$QUIET" -eq 1 ] || echo "$@"; }

while [ $# -gt 0 ]; do
  case "$1" in
    # Explicit guards rather than ${2:?...}: bash's :? exits 1, which a caller cannot tell apart
    # from "the registry is incoherent", and it bypasses die()'s ::error:: annotation entirely.
    --registry)  [ $# -ge 2 ] || die "--registry needs a value."; REGISTRY="$2"; shift 2 ;;
    --ref)       [ $# -ge 2 ] || die "--ref needs a value."; REF="$2"; shift 2 ;;
    --no-mirror) MIRROR=0; shift ;;
    --quiet)     QUIET=1; shift ;;
    -h|--help)   sed -n '2,40p' "$0" | sed 's/^# \{0,1\}//; s/^#$//'; exit 0 ;;
    *)           die "unknown arg '$1'." ;;
  esac
done

command -v jq >/dev/null 2>&1 || die "jq not found (required)."
command -v gh >/dev/null 2>&1 || die "gh not found (required to read producer repos)."
command -v sha256sum >/dev/null 2>&1 || die "sha256sum not found (required)."
[ -f "$REGISTRY" ] || die "registry not found: $REGISTRY"

# YAML -> JSON on stdout. Same fallback ladder as scripts/repos.sh.
yaml2json() {
  if command -v yq >/dev/null 2>&1; then
    yq -o=json '.' "$1"
  elif command -v python3 >/dev/null 2>&1; then
    python3 -c 'import sys,yaml,json; json.dump(yaml.safe_load(open(sys.argv[1])), sys.stdout, default=str)' "$1"
  else
    die "need yq or python3+pyyaml to read YAML: $1"
  fi
}

WORK="$(mktemp -d "${TMPDIR:-/tmp}/skills-digest.XXXXXX")"
trap 'rm -rf "$WORK"' EXIT

# --- gh-isolated fetchers (stubbed in the fixture) ----------------------------------------------
# Fetch <repo>:<path>@<ref> into <outfile> as RAW BYTES (a file, not $( ), so trailing newlines
# survive into the digest).
#
#   0  -> fetched; <outfile> holds the body (possibly zero bytes — that is legitimate content)
#   44 -> the file genuinely does not exist at <ref> (HTTP 404)
#   2  -> anything else: rate limit, 5xx, DNS, expired token, gh missing
#
# The 404-vs-everything-else split is the whole point. Conflating them is a two-headed bug: in the
# row loop an infra blip would be reported as "the producer deleted this skill" (a confident, false
# diagnosis), and in the mirror loop it would be read as "ADR-0022 P6 retired the frozen copy" and
# SKIPPED — turning a rate-limited nightly run into a permanently green no-op over an unchecked
# invariant. So an infra failure is a hard die(2), never a violation and never a skip.
#
# On a 404 real `gh api` writes the JSON error body to stdout and `gh: Not Found (HTTP 404)` to
# stderr, exiting 1; on a 403 rate limit it writes its own diagnostic to stderr the same way. We
# classify on stderr's `(HTTP <code>)` and fall back to the stdout body's `"status": "404"`.
fetch_file() {  # <repo> <path> <outfile> -> 0 | 44 | 2
  local repo="$1" path="$2" out="$3" err="$WORK/gh.err"
  if gh api -H "Accept: application/vnd.github.raw" "repos/$repo/contents/$path?ref=$REF" \
       > "$out" 2>"$err"; then
    return 0
  fi
  if grep -q 'HTTP 404' "$err" 2>/dev/null || grep -q '"status": *"404"' "$out" 2>/dev/null; then
    : > "$out"          # drop the 404 JSON body so no caller can digest it as content
    return 44
  fi
  return 2
}

# Fetch or die(2) with the underlying gh diagnostic. Returns 0 (fetched) or 44 (absent).
fetch_or_die() {  # <repo> <path> <outfile> -> 0 | 44
  local rc=0
  fetch_file "$1" "$2" "$3" || rc=$?
  if [ "$rc" -eq 2 ]; then
    die "cannot read $1/$2@$REF — $(tr '\n' ' ' < "$WORK/gh.err" | sed 's/[[:space:]]*$//'). This is an infrastructure failure, NOT a registry violation; refusing to guess."
  fi
  return "$rc"
}

# Digest the bytes of a file — the shared rule (repos.sh digest / skill-union-assert's skill_digest).
digest_file() { sha256sum "$1" | cut -d' ' -f1; }

# `FS.GG.SDD` -> `fs-gg-sdd`. Only FS.GG.* roots carry an owner convention; anything else (e.g. a
# future `.github`-owned row) is exempt from the owner cross-check rather than spuriously failing.
owner_for_root() {
  case "$1" in
    FS.GG.*) printf '%s' "$1" | tr '[:upper:]' '[:lower:]' | tr '.' '-' ;;
    *)       printf '' ;;
  esac
}

REG_JSON="$WORK/skills.json"
yaml2json "$REGISTRY" > "$REG_JSON" || die "could not parse $REGISTRY"
jq -e '.skills | type == "array" and length > 0' "$REG_JSON" >/dev/null 2>&1 \
  || die "$REGISTRY: expected a non-empty top-level 'skills' array."
# A row missing a required scalar would otherwise reach the loop as an empty field and be reported as
# a *stale digest* — a coherence lie about a schema problem. Fail closed as misconfig instead.
jq -e '.skills | all(has("id") and has("owner") and has("source") and has("sha256")
                     and (.id and .owner and .source and .sha256))' "$REG_JSON" >/dev/null 2>&1 \
  || die "$REGISTRY: every row needs non-null id, owner, source and sha256."

declared="$(jq -r '.skills | length' "$REG_JSON")"
rows=0; coherent=0; violations=0

# Materialize the rows first. `done < <(jq ...)` would silently swallow a jq failure (process
# substitution is exempt from set -e AND pipefail), so a mid-stream jq error would truncate the audit
# and the script would cheerfully print "OK ... for all N row(s)" having never seen the rest.
jq -r '.skills[] | [.id, .owner, .source, .sha256] | @tsv' "$REG_JSON" > "$WORK/rows.tsv" \
  || die "$REGISTRY: could not enumerate rows (jq failed)."

say "skills-digest-assert: auditing $declared row(s) in ${REGISTRY#"$HERE/../"} against $ORG/*@$REF"
say ""

while IFS=$'\t' read -r id owner source want; do
  rows=$((rows + 1))
  bad=0

  # A row's source must be <RepoDir>/<path-inside-repo>; a bare filename has no owning repo.
  case "$source" in
    */*) : ;;
    *)   echo "::error::skills-digest-assert: $id: source '$source' has no repo-directory prefix."
         violations=$((violations + 1)); continue ;;
  esac
  root="${source%%/*}"
  path="${source#*/}"
  repo="$ORG/$root"

  # 1. owner agrees with the source root.
  expect_owner="$(owner_for_root "$root")"
  if [ -n "$expect_owner" ] && [ "$owner" != "$expect_owner" ]; then
    echo "::error::skills-digest-assert: $id: owner '$owner' disagrees with source root '$root' (expected owner '$expect_owner')."
    bad=1
  fi

  # 2. source exists at the owner's ref. 3. its bytes digest to the declared sha256.
  # An empty body is NOT "missing": a zero-byte SKILL.md is real content and must digest (to
  # e3b0c442…) and mismatch, rather than be misreported as renamed/deleted. Only a 404 is missing.
  rc=0; fetch_or_die "$repo" "$path" "$WORK/body" || rc=$?
  if [ "$rc" -eq 44 ]; then
    echo "::error::skills-digest-assert: $id: source not found at $repo/$path@$REF (renamed, moved, or deleted)."
    bad=1
  else
    got="$(digest_file "$WORK/body")"
    if [ "$got" != "$want" ]; then
      echo "::error::skills-digest-assert: $id: registry sha256 is stale — $repo/$path@$REF digests to $got, registry declares $want."
      bad=1
    fi
  fi

  if [ "$bad" -eq 0 ]; then
    coherent=$((coherent + 1))
    say "ok: $id ($owner) — ${want:0:12}… matches $repo/$path@$REF"
  else
    violations=$((violations + 1))
  fi
done < "$WORK/rows.tsv"

# The audit must have SEEN every declared row. A short read here means the loop exited early.
[ "$rows" -eq "$declared" ] || die "audited $rows of $declared declared row(s) — the row stream was truncated."

# 4. Frozen-copy byte identity (ADR-0022 §6), for rows whose owner still has a mirror in MIRROR_REPO.
# `mirrors` counts copies actually COMPARED, and `mirror_skipped` those whose canonical body was
# missing — so the summary can never claim an invariant held for a copy nothing was compared against.
mirrors=0; mirror_bad=0; mirror_skipped=0
if [ "$MIRROR" -eq 1 ]; then
  say ""
  jq -r --arg o "$MIRROR_OWNER" '.skills[] | select(.owner == $o) | [.id, .source] | @tsv' "$REG_JSON" \
    > "$WORK/mirror-rows.tsv" || die "could not enumerate '$MIRROR_OWNER' rows (jq failed)."
  while IFS=$'\t' read -r id source; do
    frozen="$MIRROR_PATH/$id/SKILL.md"
    # An ABSENT frozen copy (404) is the ADR-0022 P6 end-state — nothing to compare, self-retiring.
    # An infra failure is NOT absence: fetch_or_die refuses to let a rate limit masquerade as P6.
    rc=0; fetch_or_die "$ORG/$MIRROR_REPO" "$frozen" "$WORK/mirror" || rc=$?
    [ "$rc" -eq 44 ] && continue

    root="${source%%/*}"; path="${source#*/}"
    rc=0; fetch_or_die "$ORG/$root" "$path" "$WORK/canon" || rc=$?
    if [ "$rc" -eq 44 ]; then
      mirror_skipped=$((mirror_skipped + 1))   # already reported as a missing source above
      continue
    fi
    mirrors=$((mirrors + 1))
    c="$(digest_file "$WORK/canon")"; m="$(digest_file "$WORK/mirror")"
    if [ "$c" = "$m" ]; then
      say "ok: $id — $MIRROR_REPO frozen copy is byte-identical to $root canonical body"
    else
      echo "::error::skills-digest-assert: $id: $MIRROR_REPO/$frozen@$REF ($m) has drifted from the canonical $root/$path@$REF ($c) — the ADR-0022 §6 two-copies invariant is broken."
      mirror_bad=$((mirror_bad + 1))
    fi
  done < "$WORK/mirror-rows.tsv"
fi

say ""
skipnote=""
[ "$mirror_skipped" -gt 0 ] && skipnote=" ($mirror_skipped not compared — canonical body missing)"
say "skills-digest-assert: $rows row(s) — $coherent coherent, $violations violation(s); $mirrors frozen copy/copies compared — $mirror_bad drifted.$skipnote"

total=$((violations + mirror_bad))
if [ "$total" -ne 0 ]; then
  echo "::error::skills-digest-assert: $total violation(s) — registry/skills.yml does not match the producer bodies. Re-reconcile the affected rows (registry = manifest = bytes)." >&2
  exit 1
fi
echo "skills-digest-assert: OK — registry = manifest = bytes for all $rows row(s)."
