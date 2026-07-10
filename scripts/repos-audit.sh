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
# Exit: 0 = every declared receiver is wired; 1 = at least one gap; 2 = no verdict, RETRYABLE — a
# receiver whose workflows could not be read (rate limit, auth, outage); 3 = no verdict, PERMANENT —
# a roster that cannot be enumerated, an audit that examined nothing, or a bad invocation.
#
# "I could not check" must never share an exit code with "I checked, and it's fine" (#266) — nor with
# "I checked, and it's broken" (#320). The same argument applies one level in, which is why 2 and 3
# are not one code (#335): "try again" and "a human must fix a file" are different verdicts, and a
# caller that wants to retry only the transient one must be able to ASK. It used to have to grep this
# script's prose for `could not determine wiring for`, making an English sentence load-bearing — a
# reword silently stopped the retry, or started retrying a malformed roster for 15 minutes.

set -euo pipefail

HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPOS_SH="$HERE/repos.sh"
REGISTRY=""                       # empty => repos.sh default
AUTHORITY="FS-GG/.github"         # the repo whose reusable workflows receivers call

# Every `die` is a PERMANENT no-verdict: a deterministic read of this checkout, or the way we were
# invoked. Re-running it changes nothing, so it exits 3 and its caller must not retry it. The
# retryable no-verdict — a receiver the API would not show us — exits 2, at the bottom of this file.
die() { echo "::error::repos-audit: $*" >&2; exit 3; }

# need_val (a usage error is a permanent no-verdict, not the exit-1 "a declared receiver is unwired"
# finding) is shared with coordination-sync and skill-union-assert.sh; see lib/args.sh. Sourced after
# die, which it uses.
# shellcheck source=lib/args.sh
. "$(dirname "${BASH_SOURCE[0]}")/lib/args.sh"

while [ $# -gt 0 ]; do
  case "$1" in
    --registry) need_val "$@"; REGISTRY="$2"; shift 2 ;;
    --repos-sh) need_val "$@"; REPOS_SH="$2"; shift 2 ;;
    # Print the header block itself — every comment line after the shebang, up to the first line that
    # is not one. A hardcoded `sed -n '2,20p'` used to do this, and it had already rotted: the range
    # stopped one line short of the `Exit:` block, so `--help` documented everything about this script
    # EXCEPT its exit codes — the one thing a caller must know, and the thing #335 is about. A line
    # number coupled by hand to a comment block is the same fail-open this script is fixing.
    -h|--help)  awk 'NR == 1 { next } /^#/ { sub(/^# ?/, ""); print; next } { exit }' "$0"; exit 0 ;;
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
# A failed read is not an answer. These used to end in `2>/dev/null || true`, which turned "I could
# not read this repo" into "this repo has no workflows" — so a rate limit produced a *definite* and
# *fabricated* verdict: "declared receiver, not wired" (#320). Only a 404 is an answer, because the
# path genuinely is not there. Every other failure (403, 5xx, auth, network) means the subject was
# never examined, and the audit must say so instead of inventing a gap.
RC_MISSING=1                                    # gh: 404 — the path does not exist. A real answer.
RC_UNREACHABLE=2                                # gh: anything else — we do not know what is there.
GH_TRIES="${REPOS_AUDIT_TRIES:-3}"              # an isolated 5xx/network blip is retried, with the
GH_RETRY_DELAY="${REPOS_AUDIT_RETRY_DELAY:-2}"  # delay doubling. This cannot outlast a rate limit —
                                                # those reset on the hour — and is not meant to: a run
                                                # that hits one reports `undetermined` and says so.

# gh's own words, for the reason string. This is a FILE, not a variable: every caller reads a fetcher
# through `$(…)`, and a variable assigned inside that subshell never reaches the parent — the reason
# would come back empty, which is how a diagnostic quietly becomes a blank.
GH_ERR_FILE="$(mktemp)"
trap 'rm -f "$GH_ERR_FILE"' EXIT
gh_last_err() { tr -s '\n' ' ' < "$GH_ERR_FILE" | sed 's/[[:space:]]*$//'; }

gh_api() {  # <gh api args…> -> body on stdout; 0 = ok, 1 = missing (404), 2 = unreachable
  local attempt=1 rc out delay="$GH_RETRY_DELAY"
  while :; do
    rc=0; out="$(gh api "$@" 2>"$GH_ERR_FILE")" || rc=$?
    if [ "$rc" -eq 0 ]; then printf '%s' "$out"; return 0; fi
    # Match the HTTP status, not gh's prose: `(HTTP 404)` is the API's contract, "Not Found" is a
    # sentence that a gh release or a locale may reword. Anything we cannot positively identify as a
    # 404 falls through to unreachable — the direction that fails closed.
    grep -qE '\(HTTP 404\)' "$GH_ERR_FILE" && return "$RC_MISSING"
    [ "$attempt" -ge "$GH_TRIES" ] && return "$RC_UNREACHABLE"
    attempt=$((attempt + 1)); sleep "$delay"; delay=$((delay * 2))
  done
}

# Is <repo> readable at all? GitHub answers 404 — not 403 — for a repo the token cannot see, so a
# 404 on the contents path only means "no workflows dir" once we know the repo itself is visible.
repo_visible() { gh_api "repos/$1" --jq '.full_name' >/dev/null; }

list_workflows() {  # <repo> -> workflow filenames, one per line; rc per gh_api
  local out rc=0
  out="$(gh_api "repos/$1/contents/.github/workflows" --jq '.[]?.name')" || rc=$?
  [ "$rc" -eq 0 ] || return "$rc"
  printf '%s\n' "$out" | grep -E '\.ya?ml$' || true
}
get_workflow() {    # <repo> <file> -> raw workflow text; rc per gh_api
  gh_api -H "Accept: application/vnd.github.raw" "repos/$1/contents/.github/workflows/$2"
}

# Does <repo> call the reusable workflow <wf> from the authority in any of its workflows?
# 0 = wired, 1 = not wired, 2 = could not determine (reason in WIRES_REASON).
WIRES_REASON=""
repo_wires() {
  local repo="$1" wf="$2" wf_re f rc=0 frc files text
  wf_re="${wf//./\\.}"
  WIRES_REASON=""

  files="$(list_workflows "$repo")" || rc=$?
  if [ "$rc" -eq "$RC_UNREACHABLE" ]; then
    WIRES_REASON="could not determine: listing .github/workflows failed — $(gh_last_err)"
    return 2
  fi
  # rc = RC_MISSING is a genuine, examined answer — the repo has no workflows dir, so it wires
  # nothing — but ONLY if we can see the repo. A private, renamed, or deleted receiver 404s here
  # exactly like an empty one, and calling that a wiring gap is this bug over again, one status
  # code across. Probe the repo before believing its silence, then fall through to report the gap.
  if [ "$rc" -eq "$RC_MISSING" ] && ! repo_visible "$repo"; then
    WIRES_REASON="could not determine: $repo is not readable (private, renamed, or gone?) — $(gh_last_err)"
    return 2
  fi

  while IFS= read -r f; do
    [ -n "$f" ] || continue
    frc=0; text="$(get_workflow "$repo" "$f")" || frc=$?
    if [ "$frc" -eq "$RC_UNREACHABLE" ]; then
      WIRES_REASON="could not determine: reading .github/workflows/$f failed — $(gh_last_err)"
      return 2
    fi
    [ "$frc" -eq 0 ] || continue          # 404: listed, then gone. It wires nothing.
    if printf '%s' "$text" \
        | grep -qE "uses:[[:space:]]*${AUTHORITY//./\\.}/\.github/workflows/${wf_re}"; then
      return 0
    fi
  done <<< "$files"
  return 1
}

roster_list() {  # <cap> -> receiver full names; non-zero if the roster cannot be enumerated
  if [ -n "$REGISTRY" ]; then bash "$REPOS_SH" list --receives "$1" --registry "$REGISTRY"
  else bash "$REPOS_SH" list --receives "$1"; fi
}

audited=0; wired=0; gaps=0; undetermined=0
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
    wrc=0; repo_wires "$repo" "$wf" || wrc=$?
    if [ "$wrc" -eq 0 ]; then
      echo "ok: $repo wires $wf (receives: $cap)"; wired=$((wired + 1))
    elif [ "$wrc" -eq 2 ]; then
      echo "::error::repos-audit: $repo receives '$cap' — $WIRES_REASON"
      undetermined=$((undetermined + 1))
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

echo "repos-audit: $audited receiver-capability pair(s) — $wired wired, $gaps gap(s), $undetermined undetermined"

# Undetermined outranks a gap: this run is not a verdict, so it must not be read as one. Any genuine
# gap found alongside it was still printed above as its own ::error::, and survives the next run.
#
# This is the RETRYABLE no-verdict, and the only exit 2 in this script: the subject exists and we
# failed to read it, so a later run may well reach a verdict. Callers retry on 2 alone — never by
# matching this sentence, which is a diagnostic, not an interface.
if [ "$undetermined" -ne 0 ]; then
  echo "::error::repos-audit: could not determine wiring for $undetermined receiver-capability pair(s) — the audit is incomplete and its result means nothing. This is an API failure (rate limit, auth, outage), not a wiring gap." >&2
  exit 2
fi
if [ "$gaps" -ne 0 ]; then
  echo "::error::repos-audit: $gaps declared receiver(s) have not wired their reusable workflow." >&2
  exit 1
fi
echo "repos-audit: OK — every declared receiver is wired."
