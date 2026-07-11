#!/usr/bin/env bash
# repos-audit.sh — participation audit for the org repo roster (ADR-0019 follow-up).
#
# The org fabrics are OPT-IN: a receiver participates by calling a reusable .github workflow via
# `workflow_call`. registry/repos.yml declares who SHOULD participate (`receives: <cap>`), but nothing
# verifies each such repo ACTUALLY wired the matching workflow. This closes that loop — it gives the
# roster's `receives` teeth: declaring a capability now means you are AUDITED for wiring it.
#
# The audit reads its whole mandate from the roster's `capabilities:` block — which capabilities map
# to a reusable workflow, and which workflow each is wired by. It then reads every rostered repo's
# .github/workflows/* ONCE and compares what the repo REALLY calls against what it DECLARES, in both
# directions:
#
#   declared + wired      ok
#   declared + not wired  a GAP — the repo promised to participate and did not (exit 1)
#   wired + not declared  DRIFT — an adopted-but-unrostered capability (exit 1). The roster is what
#                         every org fabric iterates, so a repo that adopts a fabric without saying so
#                         is invisible to all of them, and to this audit's forward check especially:
#                         `list --receives <cap>` trusts the very declaration that is missing.
#
# This is the AUTHORITY-side (central) check, complementing the per-receiver pull coherence gate:
# .github audits participation across the roster on a schedule.
#
# EVERY CAPABILITY IS AUDITED ON ITS OWN (#503). The non-vacuity guard used to sum the examined pairs
# across all capabilities, so one populated leg satisfied it for all of them: `coordination-kit` had
# six receivers, `lockfile-sync` and `contract-coherence` had zero rostered each, and the gate
# reported "every declared receiver is wired" having audited one third of its own mandate — while six
# repos had really adopted `lockfile-sync` and nobody had noticed the roster never caught up. `for
# repo in ∅` proves nothing, and it must not be able to hide behind a sibling that proved something.
# So the guard is now per-capability and keys on the ROSTER, before any API call: a capability with no
# rostered receiver fails on its own name. A capability that genuinely has none says so out loud
# (`receivers: none` + a reason), and even that is not taken on trust — the drift check above scans
# for a real adopter anyway, so a false claim goes red rather than quietly muting the leg.
#
# Reads other repos over the GitHub API (gh) — the FS-GG repos are public, so the run-scoped
# GITHUB_TOKEN reads them cross-repo (exactly as contract-coherence.yml reads FS.GG.SDD). The gh
# calls are isolated behind list_workflows/get_workflow so the fixture can stub them.
#
# Usage:
#   repos-audit.sh [--registry <file>] [--repos-sh <path>]
# Exit: 0 = every declared receiver is wired; 1 = at least one gap (a declared receiver is unwired,
# or a repo adopted a capability it never rostered); 2 = no verdict, RETRYABLE — a receiver whose
# workflows could not be read (rate limit, auth, outage); 3 = no verdict, PERMANENT — a roster that
# cannot be enumerated, a capability that names no receiver, or a bad invocation.
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

# The capabilities that map to a reusable workflow come from the ROSTER (`capabilities:`), not from a
# constant here. They used to be a `wf_for_cap` case statement plus an `AUDITED_CAPS` string — two
# hand-maintained copies of a fact the registry already owns, and exactly the redeclared-by-hand
# disease registry/repos.yml exists to cure. A capability the roster gained was audited only if
# somebody also remembered to edit this file, and "a capability with no mapping here is simply not
# audited" meant forgetting was silent (#503). `repos.sh validate` now checks the mapping instead:
# the workflow must exist and must actually be `workflow_call`-able.

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
# repo_calls PRINTS the workflows a repo calls, so its caller reads it through `$(…)` too — and its
# failure reason is subject to the very same subshell trap. Same fix, for the same reason.
CALLS_ERR_FILE="$(mktemp)"
trap 'rm -f "$GH_ERR_FILE" "$CALLS_ERR_FILE"' EXIT
gh_last_err()    { tr -s '\n' ' ' < "$GH_ERR_FILE" | sed 's/[[:space:]]*$//'; }
calls_last_err() { cat "$CALLS_ERR_FILE"; }

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

# Which of the AUTHORITY's reusable workflows does <repo> call? Prints one filename per line (the set
# may legitimately be empty). 0 = read it, 2 = could not determine (reason in $CALLS_ERR_FILE).
#
# This asks the repo-major question — "what does this repo call?" — where the old repo_wires asked
# the cap-major one, "does it call THIS workflow?", and early-returned on the first hit. Answering
# once per repo instead of once per (repo, capability) is what makes the unrostered-adopter check
# affordable: the drift direction needs every call a repo makes, not just the ones we expected, and
# re-fetching a repo's workflows once per capability to find that out would triple the API traffic
# against the rate limit this script already treats as its main adversary.
repo_calls() {
  local repo="$1" f rc=0 frc files text
  : > "$CALLS_ERR_FILE"

  files="$(list_workflows "$repo")" || rc=$?
  if [ "$rc" -eq "$RC_UNREACHABLE" ]; then
    printf 'could not determine: listing .github/workflows failed — %s' "$(gh_last_err)" > "$CALLS_ERR_FILE"
    return 2
  fi
  # rc = RC_MISSING is a genuine, examined answer — the repo has no workflows dir, so it calls
  # nothing — but ONLY if we can see the repo. A private, renamed, or deleted receiver 404s here
  # exactly like an empty one, and calling that a wiring gap is this bug over again, one status
  # code across. Probe the repo before believing its silence.
  if [ "$rc" -eq "$RC_MISSING" ]; then
    if ! repo_visible "$repo"; then
      printf 'could not determine: %s is not readable (private, renamed, or gone?) — %s' \
        "$repo" "$(gh_last_err)" > "$CALLS_ERR_FILE"
      return 2
    fi
    return 0                              # visible, but no workflows dir: it calls nothing.
  fi

  while IFS= read -r f; do
    [ -n "$f" ] || continue
    frc=0; text="$(get_workflow "$repo" "$f")" || frc=$?
    if [ "$frc" -eq "$RC_UNREACHABLE" ]; then
      printf 'could not determine: reading .github/workflows/%s failed — %s' "$f" "$(gh_last_err)" > "$CALLS_ERR_FILE"
      return 2
    fi
    [ "$frc" -eq 0 ] || continue          # 404: listed, then gone. It calls nothing.
    # Only a call to the AUTHORITY's copy counts. A repo's own local `uses: ./.github/workflows/x.yml`
    # is deliberately NOT matched: .github runs contract-coherence.yml on itself exactly that way, and
    # running your own workflow is not participating in somebody else's fabric. Matching it would make
    # the authority a phantom adopter of every capability it hosts.
    printf '%s' "$text" \
      | grep -oE "uses:[[:space:]]*${AUTHORITY//./\\.}/\.github/workflows/[A-Za-z0-9._-]+\.ya?ml" \
      | sed -E 's#.*/##' || true
  done <<< "$files"
  return 0
}

roster_list() {  # <cap> -> receiver full names; non-zero if the roster cannot be enumerated
  if [ -n "$REGISTRY" ]; then bash "$REPOS_SH" list --receives "$1" --registry "$REGISTRY"
  else bash "$REPOS_SH" list --receives "$1"; fi
}

all_repos_list() {  # every rostered repo, receives or not — the drift check's starting set
  if [ -n "$REGISTRY" ]; then bash "$REPOS_SH" list --all --registry "$REGISTRY"
  else bash "$REPOS_SH" list --all; fi
}

caps_list() {  # id<TAB>workflow<TAB>receivers<TAB>reason, one audited capability per line
  if [ -n "$REGISTRY" ]; then bash "$REPOS_SH" caps --registry "$REGISTRY"
  else bash "$REPOS_SH" caps; fi
}

# --- the mandate, read from the roster BEFORE any API call ---------------------------------------
# Every failure in this phase is a PERMANENT no-verdict (exit 3): it is a deterministic read of
# registry/repos.yml, so a re-run reproduces it exactly. Doing it up front is also what keeps the
# per-capability vacuity guard HONEST — it keys on what the roster DECLARES, never on how many pairs
# we managed to examine. Keying it on the latter would let an outage (a retryable no-verdict) render
# as "this capability has no receivers" (a permanent one): the audit would blame the roster for a
# rate limit, and send an operator to edit a file that was never wrong.

caps="$(caps_list)" \
  || die "cannot enumerate audited capabilities — repos.sh caps failed. The roster is unreadable, which is not the same as empty."
[ -n "$caps" ] \
  || die "the roster declares no audited capabilities (registry 'capabilities:' is missing or empty). This audit's entire mandate comes from there, so it would examine nothing. Examining nothing is a failure to audit, not a clean audit."

declare -A CAP_WF CAP_NONE CAP_ROSTER CAP_N CAP_WIRED CAP_GAPS
CAPS_ORDER=""
rostered_total=0
while IFS=$'\t' read -r cap wf recv reason; do
  [ -n "$cap" ] || continue
  # `repos.sh validate` already rejects a capability with no workflow, and CI runs it — but this
  # script must not INFER that it ran. An unvalidated roster reaching a gate that assumes validation
  # is how a fail-open starts, so re-assert it here rather than trusting a check that lives elsewhere.
  [ -n "$wf" ] \
    || die "capability '$cap' declares no workflow — there is nothing to audit it by. Fix registry/repos.yml (repos.sh validate catches this)."
  CAPS_ORDER="$CAPS_ORDER $cap"
  CAP_WF["$cap"]="$wf"; CAP_WIRED["$cap"]=0; CAP_GAPS["$cap"]=0

  # Enumerate into a variable, not `< <(roster_list)`: a process substitution's failure never trips
  # `set -e` and nothing checked its rc, so a `repos.sh` that died printed nothing, the loop ran zero
  # times, and the audit called that "no receivers" (#316).
  roster="$(roster_list "$cap")" \
    || die "cannot enumerate receivers of '$cap' — repos.sh list failed. The roster is unreadable, which is not the same as empty."
  CAP_ROSTER["$cap"]="$roster"
  n="$(printf '%s\n' "$roster" | grep -c . || true)"
  CAP_N["$cap"]="$n"

  if [ "$recv" = none ]; then
    # A recorded, reviewed claim that this capability has NO receiver. It is not a mute button: the
    # drift scan below still looks for a real adopter, so the claim gets falsified rather than
    # trusted. What it buys is that a vacuous leg has to be a DECISION somebody wrote down, with a
    # reason, instead of a row nobody filled in.
    CAP_NONE["$cap"]=1
    [ "$n" -eq 0 ] \
      || die "capability '$cap' declares 'receivers: none', but the roster names $n receiver(s). The registry contradicts itself (repos.sh validate catches this)."
    echo "note: $cap ($wf) — recorded as having NO receivers: $reason"
  else
    # PER-CAPABILITY non-vacuity (#503). `for repo in ∅` proves nothing, and the old guard summed the
    # examined pairs across every capability, so a sibling with receivers proved this one's case for
    # it. Each capability now stands or falls on its own name — and it fails LOUDLY rather than
    # reporting a green it did not earn.
    [ "$n" -ne 0 ] \
      || die "capability '$cap' (workflow $wf) has 0 rostered receivers, so auditing it would examine nothing and prove nothing. Either roster its real adopters, or record 'receivers: none' with a reason in registry/repos.yml. Examining nothing is a failure to audit, not a clean audit."
    rostered_total=$((rostered_total + n))
  fi
done <<< "$caps"

# The backstop, for the case every per-capability guard passes and the audit still examines nothing:
# a roster on which EVERY capability records `receivers: none`. Each leg is individually honest, the
# aggregate is a gate that checks the org's participation by looking at no repo at all.
[ "$rostered_total" -ne 0 ] \
  || die "audited 0 receiver-capability pair(s) over [$(echo "$CAPS_ORDER" | sed 's/^ //')] — every audited capability records 'receivers: none', so no rostered repo receives anything. Examining nothing is a failure to audit, not a clean audit."

# --- what the repos ACTUALLY call ----------------------------------------------------------------
all_repos="$(all_repos_list)" \
  || die "cannot enumerate the roster — repos.sh list --all failed. The roster is unreadable, which is not the same as empty."

audited=0; wired=0; gaps=0; drift=0; undetermined=0
while IFS= read -r repo; do
  [ -n "$repo" ] || continue
  crc=0; calls="$(repo_calls "$repo")" || crc=$?
  if [ "$crc" -ne 0 ]; then
    # We could not read this repo, so we know neither what it declares-but-skips nor what it
    # adopts-without-saying. Both directions are unexamined; the run is not a verdict.
    echo "::error::repos-audit: $repo — $(calls_last_err)"
    undetermined=$((undetermined + 1))
    continue
  fi

  for cap in $CAPS_ORDER; do
    wf="${CAP_WF[$cap]}"
    declared=0; calls_it=0
    if printf '%s\n' "${CAP_ROSTER[$cap]}" | grep -qxF "$repo"; then declared=1; fi
    if printf '%s\n' "$calls"            | grep -qxF "$wf";   then calls_it=1; fi

    if [ "$declared" -eq 1 ] && [ "$calls_it" -eq 1 ]; then
      echo "ok: $repo wires $wf (receives: $cap)"
      audited=$((audited + 1)); wired=$((wired + 1)); CAP_WIRED["$cap"]=$(( ${CAP_WIRED[$cap]} + 1 ))
    elif [ "$declared" -eq 1 ]; then
      echo "::error::repos-audit: $repo receives '$cap' but no workflow calls $AUTHORITY/.github/workflows/$wf"
      audited=$((audited + 1)); gaps=$((gaps + 1)); CAP_GAPS["$cap"]=$(( ${CAP_GAPS[$cap]} + 1 ))
    elif [ "$calls_it" -eq 1 ]; then
      # The reverse direction (#503). `lockfile-sync` sat like this for six repos: really adopted,
      # never rostered, so `list --receives lockfile-sync` returned nothing and the audit believed
      # the capability had no receivers — while the thing it was supposed to be watching ran, and
      # broke, unwatched (FS.GG.Game#137: 119 consecutive startup_failed, no gate said a word).
      if [ -n "${CAP_NONE[$cap]:-}" ]; then
        echo "::error::repos-audit: $repo calls $AUTHORITY/.github/workflows/$wf, but registry/repos.yml records capability '$cap' as having NO receivers. That recorded claim is now FALSE — roster $repo under 'receives: $cap' and delete the 'receivers: none' claim."
      else
        echo "::error::repos-audit: $repo calls $AUTHORITY/.github/workflows/$wf but does not declare 'receives: $cap' — an adopted-but-unrostered capability. Every org fabric iterates the roster, so an unrostered adopter is invisible to all of them. Add '$cap' to $repo's receives in registry/repos.yml."
      fi
      drift=$((drift + 1))
    fi
  done
done <<< "$all_repos"

# Per-capability, so a green audit is green FOR A NAMED REASON. The old summary was one aggregate
# line, which is precisely how auditing a third of the mandate looked identical to auditing all of it.
for cap in $CAPS_ORDER; do
  if [ -n "${CAP_NONE[$cap]:-}" ]; then
    echo "repos-audit: $cap (${CAP_WF[$cap]}) — 0 receivers, as recorded; every rostered repo was scanned and none adopts it. The claim holds."
  else
    echo "repos-audit: $cap (${CAP_WF[$cap]}) — ${CAP_N[$cap]} rostered receiver(s): ${CAP_WIRED[$cap]} wired, ${CAP_GAPS[$cap]} gap(s)"
  fi
done
echo "repos-audit: $audited receiver-capability pair(s) — $wired wired, $gaps gap(s), $drift unrostered adopter(s), $undetermined undetermined"

# Undetermined outranks a gap: this run is not a verdict, so it must not be read as one. Any genuine
# gap found alongside it was still printed above as its own ::error::, and survives the next run.
#
# This is the RETRYABLE no-verdict, and the only exit 2 in this script: the subject exists and we
# failed to read it, so a later run may well reach a verdict. Callers retry on 2 alone — never by
# matching this sentence, which is a diagnostic, not an interface.
if [ "$undetermined" -ne 0 ]; then
  echo "::error::repos-audit: could not determine wiring for $undetermined repo(s) — the audit is incomplete and its result means nothing. This is an API failure (rate limit, auth, outage), not a wiring gap." >&2
  exit 2
fi
# A gap and an unrostered adopter are one exit code because they are one CLASS: the audit ran to
# completion and found the roster and the real wiring disagreeing. Both are deterministic, neither is
# transient, and both are fixed by a commit — they differ only in WHICH side is wrong, and the
# ::error:: annotations above say which. Splitting them into two codes would buy a caller nothing it
# cannot read, at the cost of a fourth branch in every consumer of this contract.
if [ "$gaps" -ne 0 ] || [ "$drift" -ne 0 ]; then
  [ "$gaps"  -eq 0 ] || echo "::error::repos-audit: $gaps declared receiver(s) have not wired their reusable workflow." >&2
  [ "$drift" -eq 0 ] || echo "::error::repos-audit: $drift repo(s) call a reusable workflow for a capability they do not declare — the roster does not describe the org." >&2
  exit 1
fi
echo "repos-audit: OK — every declared receiver is wired."
