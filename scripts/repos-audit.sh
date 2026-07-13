#!/usr/bin/env bash
# repos-audit.sh — participation audit for the org repo roster (ADR-0019 follow-up).
#
# The org fabrics are OPT-IN: a receiver participates by wiring something in its own CI.
# registry/repos.yml declares who SHOULD participate (`receives: <cap>`), but nothing verifies each
# such repo ACTUALLY wired it. This closes that loop — it gives the roster's `receives` teeth:
# declaring a capability now means you are AUDITED for wiring it.
#
# The audit reads its whole mandate from the roster's `capabilities:` block — which capabilities exist
# and HOW each is detectable in a receiver. It then reads every rostered repo's .github/workflows/*
# ONCE and compares what the repo REALLY wires against what it DECLARES, in both directions:
#
#   declared + wired      ok
#   declared + not wired  a GAP — the repo promised to participate and did not (exit 1)
#   wired + not declared  DRIFT — an adopted-but-unrostered capability (exit 1). The roster is what
#                         every org fabric iterates, so a repo that adopts a fabric without saying so
#                         is invisible to all of them, and to this audit's forward check especially:
#                         `list --receives <cap>` trusts the very declaration that is missing.
#
# A capability declares ONE detector, because "how would I know?" has more than one answer (#628):
#
#   workflow: <f>.yml   the receiver calls the authority's reusable workflow  (a `uses:` of it)
#   script:   <f>.sh    the receiver INLINES a job that runs the authority's script. There is no
#                       reusable workflow to `uses:`, so the workflow detector is structurally blind
#                       to it — which is how `build-config` came to be enforced by four repos, in a
#                       REQUIRED check, and audited by nothing at all, for months.
#   push:     true      the AUTHORITY writes it into the receiver (apply-labels.sh reads this roster
#                       and pushes the labels in). Nothing is wired at the receiver, so there is
#                       nothing to detect there and this sweep skips it.
#
# AND NOTHING MAY BE RECEIVED THAT CANNOT BE DETECTED. That closure is the load-bearing half. Before
# it, a capability could simply have no `capabilities:` row and then it was swept in NEITHER
# direction — not findable as unwired, not findable as an unrostered adopter — while still being a
# legal `receives:` word. The roster's header promised in its own words that the list "can no longer
# rot without a red check"; that was true for three of five capabilities, and the next reader trusted
# it for all five. #626 did exactly that: it read `build-config`'s empty `receives` rows as
# "propagates to nobody", shipped on the conclusion, and four repos went red within twenty minutes.
# An unaudited registry row is not a neutral gap — it is a false negative that reads like a licence.
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
# cannot be enumerated, a capability that names no receiver, a capability that is RECEIVED but has no
# detector (#628), or a bad invocation.
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

# The capabilities and their detectors come from the ROSTER (`capabilities:`), not from a constant
# here. They used to be a `wf_for_cap` case statement plus an `AUDITED_CAPS` string — two
# hand-maintained copies of a fact the registry already owns, and exactly the redeclared-by-hand
# disease registry/repos.yml exists to cure. A capability the roster gained was audited only if
# somebody also remembered to edit this file, and "a capability with no mapping here is simply not
# audited" meant forgetting was silent (#503). `repos.sh validate` now checks the mapping instead: a
# `workflow:` must exist and be `workflow_call`-able, a `script:` must exist in scripts/, a `push:`
# must carry a reason — and nothing a repo RECEIVES may lack a row entirely (#628).

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
# Prints one TOKEN per line, each tagged with the detector kind that found it, so a single pass over a
# repo's workflows answers for EVERY capability regardless of how that capability is delivered (#628):
#
#   wf:<file>.yml      a `uses:` of the AUTHORITY's reusable workflow  (the `workflow:` detector)
#   script:<file>.sh   a reference to one of the AUTHORITY's scripts   (the `script:` detector)
#
# One pass, both kinds. Re-walking a repo's workflows once per detector kind would double the API
# traffic against the rate limit this script already treats as its main adversary — the same reasoning
# that made this function repo-major rather than cap-major in the first place.
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
    #
    # The optional quote is load-bearing. YAML lets a receiver write `uses: "FS-GG/.github/…"`, and
    # Actions honours it — but the unquoted-only pattern this grew from MISSES it, and the two
    # directions fail in opposite ways: a DECLARED receiver that quotes its `uses:` is reported as a
    # false gap (loud, and wrong), while an UNDECLARED one sails past the drift check (silent, and
    # exactly the adopted-but-unrostered capability this sweep exists to catch). A detector with a
    # quoting-dependent blind spot is a fail-open in the guard against fail-open.
    printf '%s' "$text" \
      | grep -oE "uses:[[:space:]]*[\"']?${AUTHORITY//./\\.}/\.github/workflows/[A-Za-z0-9._-]+\.ya?ml" \
      | sed -E 's#.*/##; s#^#wf:#' || true

    # The SCRIPT detector (#628). A script-delivered capability has no reusable workflow to `uses:` —
    # the receiver checks the authority out and runs the script from an inlined job — so the pattern
    # above cannot see it, and `build-config` was therefore received by four repos and audited by
    # nothing.
    #
    # It must be exactly as hard to satisfy DISHONESTLY as the `uses:` detector, and getting there
    # takes two guards that the `uses:` form gets for free from its own syntax.
    #
    # 1. PROVENANCE. `uses: FS-GG/.github/.github/workflows/x.yml` NAMES the authority, so a match is
    #    proof the artifact is the authority's. A `run:` of a script names only a PATH, and a path
    #    looks the same whether the file came from a checkout of .github or was copied into the repo
    #    and committed. So a basename alone would certify a receiver that VENDORED the script — a
    #    fork, which is precisely NOT participation, and precisely what a gate whose own name is
    #    "sync-not-fork drift check" exists to prevent. It would also make the AUTHORITY a phantom
    #    adopter of every script it hosts (it names `sync-build-config.sh` in its own workflows,
    #    because it owns it) — the failure the `uses:` detector refuses by name just above.
    #
    #    So the script must be reached THROUGH a checkout of the authority, in the same workflow
    #    file. All four real receivers do exactly this, identically:
    #        uses: actions/checkout@v7
    #        with:
    #          repository: FS-GG/.github
    #          path: _org-build          # Governance; the others check out to .github/
    #    That `repository:` line is the provenance the path cannot carry, and it is what the receiver
    #    already writes — this reads what is there, it does not ask for anything new.
    #
    # 2. PROSE IS NOT WIRING. A whole-line comment mentioning the script — `# we used to run
    #    sync-build-config.sh here` — would otherwise satisfy the detector, so a receiver that DELETED
    #    its drift job and left the comment behind would audit as wired, and the gap this detector
    #    exists to find would report green. The codebase already refuses this exact class one file
    #    over, for `workflow_call:`: "a check whose subject is 'can this really be called?' must not
    #    be satisfiable by prose about calling." Comments are stripped before either guard looks.
    #
    # Within those two guards the BASENAME is the right key, and the path prefix is deliberately not:
    # receivers check .github out wherever they like, and genuinely differ (Governance runs it from
    # `_org-build/`, the others from `.github/`), so anchoring on any one prefix would report the rest
    # as false gaps. `/` is outside the character class, so the basename falls out of the greedy run.
    # The authority is skipped OUTRIGHT, above the provenance guard rather than leaning on it. In
    # practice provenance already excludes it — .github has no reason to check ITSELF out — but that
    # is a fact about .github's current workflows, not a rule, and the day a propagate job checks the
    # authority out for some unrelated reason, the phantom adopter comes back. The rule does not
    # depend on that: running your own script is not participating in your own fabric.
    # `grep -q` IS FED BY A HERESTRING, NEVER BY A PIPE, AND THAT IS NOT STYLE.
    #
    # `printf '%s' "$body" | grep -qE …` is silently, NON-DETERMINISTICALLY WRONG under this script's
    # `set -o pipefail`. `grep -q` exits the instant it matches; if the writer is still blocked on a
    # full pipe buffer (64KiB) it takes SIGPIPE and dies with 141 — and pipefail then reports the
    # PIPELINE as 141, so the `if` is FALSE *even though grep matched*. Whether it happens depends on
    # how much of the body fits in the buffer before the match, i.e. on a race.
    #
    # Measured on FS.GG.Game's real gate.yml (19.5 KiB after comment-stripping): the pipeline form
    # returned 0 three times and 141 seven times out of ten. In the audit that surfaced as
    # `FS.GG.Game receives 'build-config' but nothing in its workflows references …` — a definite,
    # confident GAP, on a correctly wired repo, with `0 undetermined`, appearing in roughly a third of
    # runs. A wrong verdict that only shows up sometimes is worse than one that always does: it is
    # unreproducible for whoever gets paged by it, and it teaches the operator that the gate is flaky
    # and can be re-run until green. That is the #320 lesson ("a failed read is not an answer") one
    # layer down — here the read SUCCEEDED and the *comparison* lied.
    #
    # A herestring is a file descriptor, not a pipe: nothing can SIGPIPE, and the status is grep's own.
    # The `grep -oE` forms below are safe either way (they read to EOF and never exit early), but the
    # rule is easier to keep than the exception, so both are fed the same way.
    if [ "$repo" != "$AUTHORITY" ]; then
      local body
      body="$(sed 's/^[[:space:]]*#.*$//' <<< "$text")"
      if grep -qE "repository:[[:space:]]*[\"']?${AUTHORITY//./\\.}[\"']?[[:space:]]*\$" <<< "$body"; then
        grep -oE "[A-Za-z0-9._-]+\.sh" <<< "$body" | sed -E 's#^#script:#' || true
      fi
    fi
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

caps_list() {  # id<TAB>workflow<TAB>script<TAB>push<TAB>receivers<TAB>reason, one audited capability per line
  if [ -n "$REGISTRY" ]; then bash "$REPOS_SH" caps --registry "$REGISTRY"
  else bash "$REPOS_SH" caps; fi
}

received_list() {  # cap<TAB>the repos that receive it — every capability the ROSTER actually claims
  if [ -n "$REGISTRY" ]; then bash "$REPOS_SH" received --registry "$REGISTRY"
  else bash "$REPOS_SH" received; fi
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

declare -A CAP_TOKEN CAP_SUBJ CAP_PUSH CAP_NONE CAP_ROSTER CAP_N CAP_WIRED CAP_GAPS CAP_UNDET
CAPS_ORDER=""
rostered_total=0
# TAB IS IFS-*WHITESPACE* IN BASH: `IFS=$'\t' read` collapses runs of tabs and DROPS empty fields, so
# a capability with an empty `workflow` (every script:/push: row) would shift its `script` left into
# `wf` and be audited as a reusable workflow that does not exist. Re-delimit with a unit separator,
# which is not IFS whitespace and preserves empties. jq's @tsv escapes literal tabs in values, so the
# substitution cannot corrupt a `reason:`.
while IFS= read -r capline; do
  [ -n "$capline" ] || continue
  IFS=$'\x1f' read -r cap wf script push recv reason <<< "${capline//$'\t'/$'\x1f'}"
  [ -n "$cap" ] || continue
  # `repos.sh validate` already enforces exactly-one-detector, and CI runs it — but this script must
  # not INFER that it ran. An unvalidated roster reaching a gate that assumes validation is how a
  # fail-open starts, so re-assert it here rather than trusting a check that lives elsewhere.
  #
  # CAP_TOKEN is what a receiver's workflows must contain for this capability to count as wired, in
  # the tagged form repo_calls emits. CAP_SUBJ is the same thing in human words, for the diagnostics.
  if [ -n "$wf" ]; then
    CAP_TOKEN["$cap"]="wf:$wf"
    CAP_SUBJ["$cap"]="$AUTHORITY/.github/workflows/$wf"
  elif [ -n "$script" ]; then
    CAP_TOKEN["$cap"]="script:$script"
    CAP_SUBJ["$cap"]="$AUTHORITY's scripts/$script"
  elif [ "$push" = true ]; then
    # PUSH: the authority writes this INTO the receiver, so there is NOTHING receiver-side to detect
    # and both sweep directions are meaningless for it. This is the one honest way to be unauditable,
    # and it is only honest because it had to be WRITTEN DOWN, with a reason, in a row that `repos.sh
    # validate` refuses to leave blank. It is emphatically NOT the old behaviour it replaces — which
    # was to have no row at all, and be swept in neither direction while nobody had said anything.
    CAP_PUSH["$cap"]=1
    CAP_TOKEN["$cap"]=""
    CAP_SUBJ["$cap"]="(pushed by the authority — nothing to detect at the receiver)"
  else
    die "capability '$cap' declares no detector (workflow:/script:/push:) — there is nothing to audit it by, so it would be swept in NEITHER direction while remaining a legal 'receives' word. That is #628 exactly. Fix registry/repos.yml (repos.sh validate catches this)."
  fi
  CAPS_ORDER="$CAPS_ORDER $cap"
  CAP_WIRED["$cap"]=0; CAP_GAPS["$cap"]=0; CAP_UNDET["$cap"]=0

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
    echo "note: $cap (${CAP_SUBJ[$cap]}) — recorded as having NO receivers: $reason"
  elif [ -n "${CAP_PUSH[$cap]:-}" ]; then
    # A PUSH capability HAS receivers (labels has seven) — they just do not wire anything, so the
    # per-capability wiring counters below stay at zero for it by construction. It must therefore be
    # excluded from the wiring sweep rather than counted as seven gaps. It is NOT excluded from the
    # roster's own non-vacuity rule: a push capability nobody receives is a fabric pushing into the
    # void, which is still worth failing on.
    [ "$n" -ne 0 ] \
      || die "capability '$cap' declares 'push: true' but has 0 rostered receivers — the authority would push it to nobody. Roster its receivers, or record 'receivers: none' with a reason."
    echo "note: $cap — PUSHED by $AUTHORITY to its $n rostered receiver(s); nothing is wired at the receiver, so there is nothing to detect there: $reason"
  else
    # PER-CAPABILITY non-vacuity (#503). `for repo in ∅` proves nothing, and the old guard summed the
    # examined pairs across every capability, so a sibling with receivers proved this one's case for
    # it. Each capability now stands or falls on its own name — and it fails LOUDLY rather than
    # reporting a green it did not earn.
    [ "$n" -ne 0 ] \
      || die "capability '$cap' (${CAP_SUBJ[$cap]}) has 0 rostered receivers, so auditing it would examine nothing and prove nothing. Either roster its real adopters, or record 'receivers: none' with a reason in registry/repos.yml. Examining nothing is a failure to audit, not a clean audit."
    rostered_total=$((rostered_total + n))
  fi
done <<< "$caps"

# The backstop, for the case every per-capability guard passes and the audit still examines nothing:
# a roster on which EVERY capability records `receivers: none`. Each leg is individually honest, the
# aggregate is a gate that checks the org's participation by looking at no repo at all.
[ "$rostered_total" -ne 0 ] \
  || die "audited 0 receiver-capability pair(s) over [$(echo "$CAPS_ORDER" | sed 's/^ //')] — every capability here is either 'receivers: none' or 'push: true', so this sweep would examine no repo at all. Examining nothing is a failure to audit, not a clean audit. At least one capability must be verifiable AT a receiver."

# --- CLOSURE: nothing may be RECEIVED that this audit cannot DETECT (#628) ------------------------
#
# `repos.sh validate` asserts this too, and CI runs it — but this script must not INFER that it ran
# (the same rule the detector check above applies). It is re-asserted here because it is the one that
# makes the whole audit's claim TRUE: without it, "every declared receiver is wired" is a statement
# about the capabilities that happen to have a row, and says NOTHING about a capability that has none
# — while the roster's header promises the list "can no longer rot without a red check".
#
# That gap is not hypothetical. `build-config` was a legal `receives:` word with no row: four repos
# declared it, four repos really enforced it, and this audit reported green over all of them for as
# long as they existed. #626 then read those empty rows as "propagates to nobody" and shipped on it.
#
# LAST of the roster-side guards, deliberately. The per-capability vacuity checks above are STRICTLY
# MORE SPECIFIC — "you declared coordination-kit and rostered nobody for it" tells an operator exactly
# which row to fix — and a roster that trips both should say the sharper thing. Running this first
# would replace those diagnostics with a broader one and send people to the wrong line.
received="$(received_list)" \
  || die "cannot enumerate what the roster's repos receive — repos.sh received failed. The roster is unreadable, which is not the same as empty."
while IFS= read -r recvline; do
  [ -n "$recvline" ] || continue
  IFS=$'\x1f' read -r cap by <<< "${recvline//$'\t'/$'\x1f'}"
  [ -n "$cap" ] || continue
  case " $CAPS_ORDER " in
    *" $cap "*) ;;
    *) die "repo(s) [$by] receive '$cap', but the roster declares no 'capabilities:' row for it — so this audit has no detector for it and sweeps it in NEITHER direction: it can be found neither unwired nor adopted-but-unrostered. A capability that is legal to receive and impossible to check is a green nobody earned (#628). Give '$cap' a detector row (workflow:/script:/push:) in registry/repos.yml." ;;
  esac
done <<< "$received"

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
    # Charge the miss to every capability this repo was supposed to be audited FOR, so the per-capability
    # line below still adds up to its rostered count. Without this it reports "6 rostered receiver(s):
    # 5 wired, 0 gap(s)" and simply loses the sixth — a summary that reads like a complete accounting
    # of a run that did not complete. The exit code already says "no verdict"; the report must not
    # quietly disagree with it.
    for cap in $CAPS_ORDER; do
      [ -n "${CAP_PUSH[$cap]:-}" ] && continue     # never wired at the receiver; nothing was missed
      if grep -qxF "$repo" <<< "${CAP_ROSTER[$cap]}"; then
        CAP_UNDET["$cap"]=$(( ${CAP_UNDET[$cap]} + 1 ))
      fi
    done
    continue
  fi

  for cap in $CAPS_ORDER; do
    # A PUSH capability has no receiver-side artifact, so BOTH directions are meaningless for it: it
    # cannot be "unwired" (there is nothing to wire) and it cannot be an "unrostered adopter" (there
    # is nothing to adopt). Sweeping it would report every receiver as a gap. Its honesty comes from
    # `repos.sh validate` refusing the row without a reason, not from this sweep.
    [ -n "${CAP_PUSH[$cap]:-}" ] && continue

    subj="${CAP_SUBJ[$cap]}"; token="${CAP_TOKEN[$cap]}"
    declared=0; calls_it=0
    # Herestrings, not pipes — the same rule repo_calls explains at length. These three are SAFE
    # today only because a roster line and a token list are far under the 64KiB pipe buffer, so the
    # writer always finishes before `grep -q` exits. That is a property of the DATA, not of the code,
    # and it is exactly the kind of accident that stops being true quietly. One rule, no exceptions.
    if grep -qxF "$repo"  <<< "${CAP_ROSTER[$cap]}"; then declared=1; fi
    if grep -qxF "$token" <<< "$calls";              then calls_it=1; fi

    if [ "$declared" -eq 1 ] && [ "$calls_it" -eq 1 ]; then
      echo "ok: $repo wires $subj (receives: $cap)"
      audited=$((audited + 1)); wired=$((wired + 1)); CAP_WIRED["$cap"]=$(( ${CAP_WIRED[$cap]} + 1 ))
    elif [ "$declared" -eq 1 ]; then
      echo "::error::repos-audit: $repo receives '$cap' but nothing in its workflows references $subj"
      audited=$((audited + 1)); gaps=$((gaps + 1)); CAP_GAPS["$cap"]=$(( ${CAP_GAPS[$cap]} + 1 ))
    elif [ "$calls_it" -eq 1 ]; then
      # The reverse direction (#503). `lockfile-sync` sat like this for six repos: really adopted,
      # never rostered, so `list --receives lockfile-sync` returned nothing and the audit believed
      # the capability had no receivers — while the thing it was supposed to be watching ran, and
      # broke, unwatched (FS.GG.Game#137: 119 consecutive startup_failed, no gate said a word).
      #
      # This is the direction that would have caught #628 the day it started: `build-config` was
      # enforced by four repos and rostered by none. It could not fire, because a capability with no
      # detector row was not swept at all.
      if [ -n "${CAP_NONE[$cap]:-}" ]; then
        echo "::error::repos-audit: $repo references $subj, but registry/repos.yml records capability '$cap' as having NO receivers. That recorded claim is now FALSE — roster $repo under 'receives: $cap' and delete the 'receivers: none' claim."
      else
        echo "::error::repos-audit: $repo references $subj but does not declare 'receives: $cap' — an adopted-but-unrostered capability. Every org fabric iterates the roster, so an unrostered adopter is invisible to all of them. Add '$cap' to $repo's receives in registry/repos.yml."
      fi
      drift=$((drift + 1))
    fi
  done
done <<< "$all_repos"

# Per-capability, so a green audit is green FOR A NAMED REASON. The old summary was one aggregate
# line, which is precisely how auditing a third of the mandate looked identical to auditing all of it.
for cap in $CAPS_ORDER; do
  if [ -n "${CAP_NONE[$cap]:-}" ]; then
    echo "repos-audit: $cap (${CAP_SUBJ[$cap]}) — 0 receivers, as recorded; every rostered repo was scanned and none adopts it. The claim holds."
  elif [ -n "${CAP_PUSH[$cap]:-}" ]; then
    # Reported, never counted. A push capability contributes no receiver-capability pairs, and folding
    # it into the totals as "wired" would be claiming an examination that did not happen — the pairs
    # line is a count of things this audit actually LOOKED at.
    echo "repos-audit: $cap — ${CAP_N[$cap]} rostered receiver(s), PUSHED by the authority: not swept (there is no receiver-side artifact to detect). Its honesty rests on the reviewed 'push:' claim in registry/repos.yml, not on this sweep."
  else
    echo "repos-audit: $cap (${CAP_SUBJ[$cap]}) — ${CAP_N[$cap]} rostered receiver(s): ${CAP_WIRED[$cap]} wired, ${CAP_GAPS[$cap]} gap(s), ${CAP_UNDET[$cap]} undetermined"
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
