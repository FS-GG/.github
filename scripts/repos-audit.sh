#!/usr/bin/env bash
# repos-audit.sh — participation audit for the org repo roster (ADR-0019 follow-up).
#
# The org fabrics are OPT-IN: a receiver participates by calling a reusable .github workflow via
# `workflow_call`. registry/repos.yml declares who SHOULD participate (`receives: <cap>`), but nothing
# verifies each such repo ACTUALLY wired the matching workflow. This closes that loop — it gives the
# roster's `receives` teeth: declaring a capability now means you are AUDITED for wiring it.
#
# For every capability that maps to a reusable workflow (WF_MAP below), it lists the repos that
# `receives` it (via scripts/repos.sh) and checks each repo's .github/workflows/* for a
# `uses: FS-GG/.github/.github/workflows/<wf>` call. A declared-but-unwired repo fails the audit.
# This is the AUTHORITY-side (central) check, complementing the per-receiver pull coherence gate:
# .github audits participation across the roster on a schedule.
#
# Reads other repos over the GitHub API (gh) — the FS-GG repos are public, so the run-scoped
# GITHUB_TOKEN reads them cross-repo (exactly as contract-coherence.yml reads FS.GG.SDD). The gh
# calls are isolated behind list_workflows/get_workflow so the fixture can stub them.
#
# Usage:
#   repos-audit.sh [--registry <file>] [--repos-sh <path>]
# Exit: 0 = every declared receiver is wired; 1 = at least one gap; 2 = misconfig, which includes a
# roster that cannot be enumerated and an audit that examined nothing. "I could not check" must never
# share an exit code with "I checked, and it's fine" (#266).

set -euo pipefail

HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPOS_SH="$HERE/repos.sh"
REGISTRY=""                       # empty => repos.sh default
AUTHORITY="FS-GG/.github"         # the repo whose reusable workflows receivers call

die() { echo "::error::repos-audit: $*" >&2; exit 2; }

while [ $# -gt 0 ]; do
  case "$1" in
    --registry) REGISTRY="${2:?--registry needs a value}"; shift 2 ;;
    --repos-sh) REPOS_SH="${2:?--repos-sh needs a value}"; shift 2 ;;
    -h|--help)  sed -n '2,20p' "$0" | sed 's/^# \{0,1\}//; s/^#$//'; exit 0 ;;
    *)          die "unknown arg '$1'." ;;
  esac
done
command -v gh >/dev/null 2>&1 || die "gh not found (required to read receiver workflows)."
[ -x "$REPOS_SH" ] || [ -f "$REPOS_SH" ] || die "repos.sh not found at $REPOS_SH."

# Capabilities that correspond to a reusable .github workflow a receiver must call. Extend as more
# fabrics are represented in the roster; a capability with no mapping here is simply not audited.
wf_for_cap() {
  case "$1" in
    coordination-kit)    echo "coordination-coherence.yml" ;;
    contract-coherence)  echo "contract-coherence.yml" ;;
    lockfile-sync)       echo "lockfile-sync.yml" ;;
    *)                   echo "" ;;
  esac
}
AUDITED_CAPS="coordination-kit contract-coherence lockfile-sync"

# --- gh-isolated fetchers (stubbed in the fixture) ---------------------------------------------
list_workflows() {  # <repo> -> workflow filenames, one per line
  gh api "repos/$1/contents/.github/workflows" --jq '.[]?.name' 2>/dev/null | grep -E '\.ya?ml$' || true
}
get_workflow() {    # <repo> <file> -> raw workflow text
  gh api -H "Accept: application/vnd.github.raw" "repos/$1/contents/.github/workflows/$2" 2>/dev/null || true
}

# Does <repo> call the reusable workflow <wf> from the authority in any of its workflows?
repo_wires() {
  local repo="$1" wf="$2" wf_re f
  wf_re="${wf//./\\.}"
  while IFS= read -r f; do
    [ -n "$f" ] || continue
    if get_workflow "$repo" "$f" \
        | grep -qE "uses:[[:space:]]*${AUTHORITY//./\\.}/\.github/workflows/${wf_re}"; then
      return 0
    fi
  done < <(list_workflows "$repo")
  return 1
}

roster_list() {  # <cap> -> receiver full names; non-zero if the roster cannot be enumerated
  if [ -n "$REGISTRY" ]; then bash "$REPOS_SH" list --receives "$1" --registry "$REGISTRY"
  else bash "$REPOS_SH" list --receives "$1"; fi
}

audited=0; wired=0; gaps=0
for cap in $AUDITED_CAPS; do
  wf="$(wf_for_cap "$cap")"
  [ -n "$wf" ] || continue
  # Enumerate into a variable, not `< <(roster_list)`: a process substitution's failure never trips
  # `set -e` and nothing checked its rc, so a `repos.sh` that died printed nothing, the loop ran zero
  # times, and the audit called that "no receivers" (#316).
  roster="$(roster_list "$cap")" \
    || die "cannot enumerate receivers of '$cap' — repos.sh list failed. The roster is unreadable, which is not the same as empty."
  while IFS= read -r repo; do
    [ -n "$repo" ] || continue
    audited=$((audited + 1))
    if repo_wires "$repo" "$wf"; then
      echo "ok: $repo wires $wf (receives: $cap)"; wired=$((wired + 1))
    else
      echo "::error::repos-audit: $repo receives '$cap' but no workflow calls $AUTHORITY/.github/workflows/$wf"
      gaps=$((gaps + 1))
    fi
  done <<< "$roster"
done

# Non-vacuity. `for x in ∅` proves nothing, and this gate used to report it as proof. The guard above
# catches today's enumerator failure; this one holds however a future enumerator finds to return
# empty. The org's fabrics have receivers, so auditing none of them means we audited the wrong thing.
[ "$audited" -ne 0 ] \
  || die "audited 0 receiver-capability pair(s) over [$AUDITED_CAPS] — either no capability maps to a reusable workflow, or no rostered repo receives one. Examining nothing is a failure to audit, not a clean audit."

echo "repos-audit: $audited receiver-capability pair(s) — $wired wired, $gaps gap(s)"
if [ "$gaps" -ne 0 ]; then
  echo "::error::repos-audit: $gaps declared receiver(s) have not wired their reusable workflow." >&2
  exit 1
fi
echo "repos-audit: OK — every declared receiver is wired."
