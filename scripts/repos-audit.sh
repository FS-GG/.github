#!/usr/bin/env bash
# shellcheck source-path=SCRIPTDIR
# repos-audit.sh — participation audit for the org repo roster (ADR-0019 follow-up).
#
# The org fabrics are OPT-IN: a receiver participates by wiring something in its own CI.
# registry/repos.yml declares who SHOULD participate (`receives: <cap>`), but nothing verifies each
# such repo ACTUALLY wired it. This closes that loop — it gives the roster's `receives` teeth:
# declaring a capability now means you are AUDITED for wiring it.
#
# The audit reads its whole mandate from the roster's `capabilities:` block — which capabilities exist
# and HOW each is detectable in a receiver. It then reads every rostered repo's receiver-side
# evidence once (workflows, plus any detector-specific manifest) and compares what the repo REALLY
# wires against what it DECLARES, in both directions:
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
#   materializer: <id>  the receiver explicitly opts into a package materializer AND CI reruns it and
#                       fails on drift. `build-config` requires both the FS.GG.Kit receiver-project
#                       opt-in and workflow enforcement; either half alone is incomplete adoption.
#   caller:   <id>      the receiver calls one of the authority's reusable workflows AGAINST A
#                       PARTICULAR SUBJECT, and the `uses:` alone cannot say which subject. Compound:
#                       `skill-union` requires a call pointed at the receiver's OWN repository root
#                       over all three ADR-0011 roots, AND a trigger that fires when any of those
#                       roots changes. Either half alone is not a gate on the committed roots (#1504).
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
# IT ALSO CARRIES THE SPARSE-CHECKOUT CLOSURE RULE ACROSS THE ROSTER (#1529). That is a second
# mandate rather than a second detector, and it lives here for one reason: it needs to read TEN
# repositories, and this is the only tool in the org that already has a roster, a repo filter, a
# retry ladder and a network budget. See THE SPARSE-CHECKOUT CLOSURE SWEEP below.
#
# AND IT SWEEPS EVERY coordination-kit RECEIVER'S FS.GG.Kit PIN AGAINST THE PUBLISHED KIT (#1540,
# #1560). A third mandate, here for the same reason and one more: participation is not a one-off. A
# repo can wire the detector perfectly and still be running a kit two minors old, and until #1540
# nothing in the org could say so until that repo happened to PUSH — at which point its own `main`
# went red, in a place that blocks nothing and pages no one. See THE KIT-PIN FRESHNESS SWEEP below.
#
# AND IT SWEEPS EVERY RECEIVER THAT DECLARES A GENERATED VIEW SKILL ROOT FOR THE TARGET THAT
# GENERATES IT (#1759). A fourth mandate, here for the same reason and one more: the subject is a
# file in somebody else's repository, and the gate that would otherwise catch it CANNOT BE REACHED
# FROM THAT REPOSITORY. `kit-materialize.yml` is a `uses:` of this repo's reusable workflow — it
# checks the caller out and runs the materialize there, and a caller cannot add a step to a callee
# (#1715 blocker B5). So a receiver that declares a view root and forgets the generate is not subtly
# wrong: its next Renovate kit bump reds on a tree nobody touched, with no file it owns in which to
# fix it. Seven receivers each hand-copied that target and nothing compared them until this sweep.
# See THE VIEW-ROOT GENERATE SWEEP below.
#
# AND IT RE-DERIVES, DAILY, WHETHER AN UNEXCUSED VIEW-ROOT ASSERTION OR MATERIALIZE PATH IS REQUIRED
# (#1785, corrected by #1869). A fifth mandate, and the first one whose subject is a claim held in
# the GitHub API rather than in a file. The historical roster field remains `absence-cover`, but
# #1869 measured that the receiver generate repairs absent/dangling roots and refuses a text-file
# root before the assertion: this sweep grades the detected path's REQUIREDNESS, not those verdicts'
# reachability. Adding a required context, removing one, moving the path between jobs, or renaming a job changes
# the answer. The union of classic protection and rulesets is compared with the receiver's own
# committed jobs. See THE ABSENCE-COVER SWEEP below.
#
# Usage:
#   repos-audit.sh [--registry <file>] [--repos-sh <path>]
# Exit:
#   0 = every declared receiver is wired, and every coordination-kit receiver pins the published kit
#   1 = at least one gap — a declared receiver is unwired, a repo adopted a capability it never
#       rostered, a cross-repo sparse-checkout enumerates a file, a coordination-kit receiver's
#       FS.GG.Kit pin is behind the newest published one, or a receiver declares a view skill root
#       that nothing generates before FsggKitCheckSkillView (#1759), or a receiver whose real branch
#       protection contradicts the roster's `absence-cover:` word for it — or that declares none
#       (#1785)
#   2 = no verdict, RETRYABLE — receiver evidence could not be read (rate limit, auth, outage), the
#       NuGet feed the kit pins are graded against could not be read, the git TREE a cross-repo
#       sparse-checkout fetches could not be read, so rule (4) did not run for it (#1556), or a
#       receiver project would not read, or a receiver's branch protection / rulesets would not read
#       (#1785 — unread protection cannot establish whether the detected path is required)
#   3 = no verdict, PERMANENT — a roster that cannot be enumerated, a capability that names no
#       receiver, a capability that is RECEIVED but has no detector (#628), a cross-repo
#       sparse-checkout whose SHAPE the closure rule refuses to grade, a coordination-kit receiver
#       whose FS.GG.Kit pin cannot be located or contradicts itself, a view-root generate whose
#       ORDERING against FsggKitCheckSkillView cannot be read (#1759), a receiver whose view-root path
#       requirement cannot be graded — no `administration: read` credential, a workflow that will not parse, or a
#       matrix whose check-run names cannot be enumerated (#1785) — or a bad invocation
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

# THE SPARSE-CHECKOUT CLOSURE RULE, IMPORTED RATHER THAN RETYPED (#1529, closing #1522's reach).
#
# `check-sparse-checkout-closure.py` grades one repository's workflows — whichever `--root` names,
# which in CI is FS-GG/.github and nothing else. Its own docstring says so, under THE REACH OF THIS
# GATE, and names the hole: a sibling repo that hand-rolls `actions/checkout` + `sparse-checkout`
# against the authority is a live instance of the class the gate was built to end, over which this
# repo's CI stays green. "No sibling hand-rolls this" was true of the tree and enforced by nothing.
#
# The rule is IMPORTED FROM THAT FILE, never restated here. A second copy of it would be the same
# defect one level up — a hand-maintained duplicate of a rule, in a file that cannot execute the
# thing it duplicates — and #1522 exists because exactly that drifted. So `grade_pattern`,
# `patterns_of`, `cone_mode_of`, `sparse_steps`, `origin_repository` and `tracked_paths` are loaded
# out of the shipped gate and applied verbatim to workflows fetched from other repositories; the
# helpers are already repo-agnostic, because the gate was written not to care where a workflow came
# from. A rule change lands in ONE place and both readers move together, by construction.
SPARSE_RULE="$HERE/check-sparse-checkout-closure.py"
# The required-context gate, imported by the absence-cover sweep (#1785) for the same reason the
# sparse rule is: it already knows that GitHub keeps required checks in TWO stores and that the
# required set is their UNION (#574), and it already knows what GitHub calls a job's check run. A
# second copy of either would be the defect one level up — this audit's whole subject is a claim
# about protection that nothing re-derives.
CONTEXT_RULE="$HERE/check-required-contexts.py"
# The tree whose git INDEX resolves rule (4) — "does this pattern select any tracked path?". It is
# the checkout this script is running out of, derived from the script's own location rather than
# spelled, exactly as REPOS_SH is. That tree is the AUTHORITY's, so rule (4) runs for precisely the
# checkouts that fetch the authority — which is the motivating case: a sibling naming
# `scripts/check-foo.py` in the DEFAULT cone mode is invisible to rules (1)-(3) and caught only by
# asking this index. For a fetch of any other repository the audit does not hold the tree, so rule
# (4) does not run and the step is reported UNGRADED rather than ok (#266).
AUTHORITY_TREE="$(cd "$HERE/.." && pwd)"

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
# The `caller:` detector reads a receiver's workflow STRUCTURALLY — YAML to JSON via yq-or-PyYAML, then a
# stdlib verdict program — because the question it asks ("what was the assertion pointed at, and does the
# gate run when the roots change?") is a question about structure and a line-grep got it wrong five ways
# (#1504). So python3 is a hard dependency, and it is asserted UP FRONT rather than discovered per
# workflow: a missing interpreter must be a permanent no-verdict about the audit, never a fabricated
# "this receiver is not wired" about eight repos at once (#320).
command -v python3 >/dev/null 2>&1 \
  || die "python3 not found (required to evaluate a caller: detector against a receiver's workflows)."
command -v yq >/dev/null 2>&1 || python3 -c 'import yaml' >/dev/null 2>&1 \
  || die "need yq or python3+pyyaml to read a receiver's workflow YAML (the same ladder scripts/repos.sh uses)."
[ -x "$REPOS_SH" ] || [ -f "$REPOS_SH" ] || die "repos.sh not found at $REPOS_SH."
# The sweep's rule is a file, so its absence is a missing dependency and NOT a clean sweep. Asserted
# up front for the same reason python3 is: a rule that cannot be loaded must be a permanent
# no-verdict about the audit, never a silent "no repository enumerates anything" over ten repos.
[ -f "$SPARSE_RULE" ] \
  || die "the sparse-checkout closure rule is not at $SPARSE_RULE. It is IMPORTED, not restated here (#1529/#1522), so without it this audit cannot grade a single cross-repo checkout — and reporting that as a clean sweep is the fail-open both items exist to end."
[ -f "$CONTEXT_RULE" ] \
  || die "the required-context gate is not at $CONTEXT_RULE. The absence-cover sweep (#1785) IMPORTS its union-of-both-protection-stores read (#574) and GitHub's job-naming rule rather than restating either, so without it no receiver's absence cover can be graded — and a sweep that grades nothing must never report a clean org."
# The kit-pin sweep's version ORDERING and feed reader, imported for exactly the reason the sparse
# rule is: fsgg_feed.py's own docstring says two copies of NuGet version ordering is how the two
# copies drift, and a gate that orders versions wrongly reports green on a stale pin. Asserted up
# front, so a missing module is a permanent no-verdict about the audit rather than a surprise in the
# middle of a sweep that has already printed half a verdict.
KIT_FEED_LIB="$HERE/fsgg_feed.py"
[ -f "$KIT_FEED_LIB" ] \
  || die "the NuGet feed reader is not at $KIT_FEED_LIB. The kit-pin freshness sweep (#1540) IMPORTS its version ordering and its nuget.org reader from there rather than restating either, so without it no receiver's pin can be compared to the published kit — and reporting that as a clean sweep is the same fail-open."

# The capabilities and their detectors come from the ROSTER (`capabilities:`), not from a constant
# here. They used to be a `wf_for_cap` case statement plus an `AUDITED_CAPS` string — two
# hand-maintained copies of a fact the registry already owns, and exactly the redeclared-by-hand
# disease registry/repos.yml exists to cure. A capability the roster gained was audited only if
# somebody also remembered to edit this file, and "a capability with no mapping here is simply not
# audited" meant forgetting was silent (#503). `repos.sh validate` now checks the mapping instead: a
# `workflow:` must exist and be `workflow_call`-able, a `script:` must exist in scripts/, a
# `materializer:` must name a supported compound detector, a `push:` must carry a reason — and
# nothing a repo RECEIVES may lack a row entirely (#628).

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
# The sparse sweep's ledger, and a FILE for the same subshell reason: the grading happens inside
# repo_calls, which every caller reads through `$(…)`, so a counter incremented there would never
# reach the parent. One tab-separated record per event, ACCUMULATED across every repo (unlike the two
# files above, which are per-call): `workflow`, `finding`, `refusal`, `ungraded`, `unresolved`,
# `unparseable`, `noverdict`, and one `counts` line per workflow read.
SPARSE_FILE="$(mktemp)"
# Where the sparse sweep caches the ROSTERED repositories' path sets, so rule (4) can be asked about a
# tree this checkout does not hold (#1556). One `<slug>.paths` (a JSON array) or `<slug>.err`
# (`<kind><TAB><reason>`) per repository, written ONCE and read by every later workflow — a FILE for
# the same subshell reason as the ledger, and a CACHE because the alternative is one tree fetch per
# workflow per repository. See THE ROSTER'S TREES.
SPARSE_TREE_DIR="$(mktemp -d)"
# The kit-pin freshness sweep's ledger (#1540), same line-oriented shape as SPARSE_FILE: `finding`,
# `refusal`, `undetermined`, `ok`, and one `published` line naming the version everything was graded
# against. A FILE for the same reason — the grading happens in a `$( )`.
KITPIN_FILE="$(mktemp)"
# Where that sweep stages the pin-bearing files it fetched, so the verdict program can parse XML
# instead of bash grepping it. One subdirectory per repo; see THE KIT-PIN FRESHNESS SWEEP.
KITPIN_DIR="$(mktemp -d)"
# The view-root generate sweep's ledger (#1759), same line-oriented shape: `finding`, `refusal`,
# `undetermined`, `ok`, `none`. It RIDES the kit-pin sweep's staging rather than fetching anything of
# its own — the file it grades is `.config/kit/FS.GG.Kit.receiver.proj`, which that sweep already
# stages for every package receiver. See THE VIEW-ROOT GENERATE SWEEP.
VIEWGEN_FILE="$(mktemp)"
# The bump-offer sweep's ledger (#1768), same line-oriented shape: `offer-current`, `offer-superseded`,
# `offer-ratelimited`, `offer-none`, `offer-undetermined`. It RIDES the kit-pin sweep — it grades ONLY
# the receivers that sweep already found BEHIND — so a fully current org pays it nothing. See THE
# BUMP-OFFER SWEEP.
OFFER_FILE="$(mktemp)"
# Where that sweep stages each behind receiver's open pull requests, its branch list, and the pin file
# read AT a candidate bump branch's head. Same staging-then-grade split as the kit-pin sweep, for the
# same reason: the verdict program parses XML and JSON, and bash must not.
OFFER_DIR="$(mktemp -d)"
# The engine offer sweep (#1803) uses the same five-state verdict program but its own staging: its
# subject is every coordination-kit receiver's JSON tool pin, not package receivers' XML kit pin.
ENGOFFER_FILE="$(mktemp)"
ENGOFFER_DIR="$(mktemp -d)"
# The engine-manifest sweep's ledger (#1615), same line-oriented shape: `finding`, `refusal`,
# `undetermined`, `ok`. This one FETCHES its own subject rather than riding the kit-pin staging: it
# grades `.config/dotnet-tools.json`, which no other sweep reads, and its receiver set is WIDER than
# the kit-pin sweep's (every coordination-kit receiver, not only the package-delivery ones), so
# riding that staging would silently narrow it. See THE ENGINE-MANIFEST SWEEP.
ENGMAN_FILE="$(mktemp)"
# Where it stages each receiver's tool manifest, so the verdict program parses JSON and bash does not.
ENGMAN_DIR="$(mktemp -d)"
# The absence-cover sweep's ledger (#1785), same line-oriented shape: `finding`, `refusal`,
# `undetermined`, `ok`. See THE ABSENCE-COVER SWEEP.
ABSENTOK_FILE="$(mktemp)"
# Where EVERY rostered repo's workflow YAML is staged as the detector pass reads it, so that sweep
# can ask "which jobs reach the kit's view-root assertion" without a second fetch of bytes this
# script already held. Same ride-the-existing-pass economy as the view-root generate sweep (#1759).
ABSENTOK_DIR="$(mktemp -d)"
DISPATCH_FILE="$(mktemp)"
trap 'rm -rf "$GH_ERR_FILE" "$CALLS_ERR_FILE" "$SPARSE_FILE" "$SPARSE_TREE_DIR" "$KITPIN_FILE" "$KITPIN_DIR" "$VIEWGEN_FILE" "$OFFER_FILE" "$OFFER_DIR" "$ENGOFFER_FILE" "$ENGOFFER_DIR" "$ENGMAN_FILE" "$ENGMAN_DIR" "$ABSENTOK_FILE" "$ABSENTOK_DIR" "$DISPATCH_FILE"' EXIT
# Tabs are squeezed out along with newlines: this string is interpolated into the tab-separated
# kit-pin ledger (#1540), and a gh error carrying a tab would shift every field to its right.
gh_last_err()    { tr -s '\n\t' '  ' < "$GH_ERR_FILE" | sed 's/[[:space:]]*$//'; }
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
get_repo_file() {   # <repo> <repo-relative path> -> raw text; rc per gh_api
  gh_api -H "Accept: application/vnd.github.raw" "repos/$1/contents/$2"
}
# The same read, at a NAMED REF (#1768). The bump-offer sweep needs the pin as it stands on a bump
# branch, not on `main`, and `?ref=` is how the contents API says that. Kept as its own function
# rather than a defaulted third argument to `get_repo_file`, so nothing that reads a repo's DEFAULT
# branch can silently acquire a ref by passing one argument too many.
get_repo_file_ref() {  # <repo> <repo-relative path> <ref> -> raw text; rc per gh_api
  gh_api -H "Accept: application/vnd.github.raw" "repos/$1/contents/$2?ref=$3"
}

# --- THE ROSTER'S TREES — RULE (4) BEYOND THE AUTHORITY (#1556) ----------------------------------
#
# WHAT WAS OPEN. #1529 gave the closure rule reach across the roster, but rule (4) — *does this
# pattern select any tracked path in the repository it fetches?* — asks a git INDEX, and this audit
# holds exactly one: its own checkout, the authority's. So a cross-repo checkout of ANY OTHER
# repository got rules (1)-(3) only. In CONE MODE — `actions/checkout`'s DEFAULT, where git reads a
# pattern as a rooted directory prefix and a file name is simply a directory that turns out to be
# empty — rules (1)-(3) cannot detect enumeration at all. Nothing about the pattern STRING tells a
# file from a directory. So the default shape of the defect, between two repositories NEITHER of
# which is the authority, was ungraded by everything in the org: the local gate cannot see it (wrong
# repo) and the sweep could not (no index). #1529's own comment named the hole; this closes it.
#
# THE INDEX COMES FROM THE API. `GET /repos/{owner}/{repo}/git/trees/HEAD?recursive=1` is the tracked
# path set of the default branch, which is the same thing `git ls-files` gives for the tree we hold.
# It is fed to the SHARED rule's `grade_pattern(tracked=…)` exactly as the local index is — no second
# copy of rule (4) is written here, for the reason #1522 exists.
#
# BLOBS AND SUBMODULES, BECAUSE THAT IS WHAT `git ls-files` LISTS. A submodule is a `commit` entry in
# the tree API and a real line in `ls-files`, so dropping it would make a directory containing only a
# submodule read as empty — a fabricated finding. `tree` entries are dropped: git has no empty
# directories, so every directory is implied by a path under it, and `selects_anything` asks for a
# path strictly UNDER the prefix. Including them would answer "yes" for the directory itself and
# defeat the one rule that can see a cone-mode file name.
#
# LAZY, AND THE LAZINESS IS THE COST CONTROL (#1556 criterion 5). Nothing is fetched until a step
# actually names a repository whose tree we do not hold. A roster with no cross-repo sparse-checkout,
# or one where every such checkout fetches the authority, pays ZERO additional API calls — the weekly
# audit must not gain ten round-trips for a hole that is usually empty. `sparse_grade` is what makes
# that true: it runs the verdict program, and only re-runs it if the FIRST pass asked for a tree.
#
# THE ROSTER IS THE BOUNDARY. A fetch of a repository that is not rostered stays UNGRADED and says
# so. Reaching past the roster would be this audit claiming a subject it was never given, and the
# roster is the one place that says what the org is.
#
# EVERY FAILURE IS A NO-VERDICT, INCLUDING A 404. A tree we could not read leaves those steps
# ungraded and makes the RUN a no-verdict (exit 2) — never a green, and never a finding. A 404 is
# grouped with the unreachable rather than treated as an answer, unlike `list_workflows` where "no
# workflows directory" genuinely is one: there is no such thing as a repository with no index that we
# may then reason about, so "the tree is not there" is only ever "we do not have the tree".
#
# AND A TRUNCATED TREE IS A FAILURE, WHICH IS THE ONE THAT LOOKS LIKE SUCCESS. The endpoint sets
# `"truncated": true` and returns a PARTIAL array for a large repository, at HTTP 200. A partial index
# makes `selects_anything` answer "no" for directories that do exist, so believing it would
# manufacture findings against innocent receivers — a green-looking read that produces a red verdict
# about the wrong subject. It is refused, and the reason is named.
#
# WHAT IS STILL NOT ASKED: the step's `ref:`. This resolves the DEFAULT branch, because that is the
# analogue of the tree this audit holds for the authority, and because a sparse-checkout that only
# selects something on a side branch is not a shape worth certifying. A step pinning `ref:` to a tag
# whose layout differs from the default branch is therefore graded against the default branch, and
# nothing says so per-step.
get_repo_tree() {   # <repo> -> the default branch's recursive tree as JSON; rc per gh_api
  gh_api "repos/$1/git/trees/HEAD?recursive=1"
}

# The rostered repositories, lowercased, one per line — the BOUNDARY above, as data. Assigned once
# beside `all_repos`, which is where the roster is first read; empty here so that a caller reaching
# `sparse_tree_ensure` before then refuses rather than silently treating the whole org as off-roster.
SPARSE_ROSTER=""

# Turn one tree response into the cache's path set. Writes the JSON array to argv[1] (via a temp +
# rename, so a failure NEVER leaves a partial path set behind for the grader to believe), or exits
# non-zero with the reason on stderr.
#
# JSON, NOT LINES. A git path may contain a NEWLINE — `git ls-files -z` exists for exactly that — and
# a newline-delimited cache would split one such path into two, each of which is a PREFIX of nothing
# real but might be a prefix of something the grader is asking about. That is a fail-OPEN: a pattern
# that selects nothing would read as selecting something. `json.dumps` has no delimiter to collide
# with, and the reader does not have to know the rule.
SPARSE_TREE_PY="$(cat <<'PY'
import json, os, sys

TARGET = sys.argv[1]

try:
    document = json.load(sys.stdin)
except ValueError:
    sys.exit("its git tree response did not parse as JSON")
if not isinstance(document, dict):
    sys.exit("its git tree response was not a JSON object")
# THE TRUNCATION FLAG, CHECKED BEFORE THE ARRAY IS BELIEVED. HTTP 200 with a short array is how this
# endpoint reports a repository too large to list, and a short array is indistinguishable from a small
# repository by inspection.
# ANYTHING BUT AN EXPLICIT `false` IS TRUNCATED, which is deliberately not the obvious `is True`.
# Believing a partial tree manufactures findings against innocent receivers; refusing a complete one
# costs a retryable no-verdict. Those are not symmetric, so the test is written to fail in the second
# direction: a `"truncated": "false"` string, a `1`, or anything else this file did not expect lands
# on the refusal. Absent is treated as complete — the field is always present on this endpoint
# (verified live), so its absence means we are not talking to it and `tree` will not be there either.
if document.get("truncated", False) is not False:
    sys.exit("the git tree endpoint TRUNCATED its answer (or did not say plainly that it had not), "
             "so the path set is INCOMPLETE — grading rule (4) against it would report directories "
             "that DO exist as selecting nothing")
entries = document.get("tree")
if not isinstance(entries, list):
    sys.exit("its git tree response carried no `tree` array")
paths = sorted({
    entry["path"] for entry in entries
    if isinstance(entry, dict) and entry.get("type") in ("blob", "commit")
    and isinstance(entry.get("path"), str) and entry.get("path")
})
if not paths:
    sys.exit("its git tree listed no blobs or submodules, so there is no index to ask")
staging = TARGET + ".partial"
with open(staging, "w", encoding="utf-8") as handle:
    json.dump(paths, handle)
os.replace(staging, TARGET)
PY
)"

# sparse_tree_ensure <repository, exactly as the workflow spelled it>
#
# Idempotent, and that is the whole cache: the first call decides, every later one returns at once.
# It ALWAYS leaves the repository decided — a `.paths` or an `.err` — so the grader's second pass
# cannot ask for the same tree again and loop.
sparse_tree_ensure() {
  local repo="$1" key slug body rc=0
  key="$(printf '%s' "$repo" | tr '[:upper:]' '[:lower:]')"
  # Defence in depth: the grader only ASKS for a literal `owner/name` (it classifies anything else
  # itself, with no tree involved), but this string came out of somebody else's workflow file and is
  # about to become a URL path. Refuse anything that is not the shape, rather than trusting the
  # caller — the two checks are in different languages and only one of them is looking at a URL.
  #
  # It is also what makes the SLUG safe. The cache key is the repository with `/` doubled to `__`,
  # and a name carrying `/`, `..` or anything outside the classes below could escape the cache
  # directory.
  #
  # AND THE SLUG IS INJECTIVE, which this comment used to CLAIM and the guard did not deliver
  # (#1608). While `_` was inside the OWNER class, `owner__x/repo` and `owner/x__repo` both slugged
  # to `owner__x__repo` — two repositories on one cache entry, which would grade one repo's patterns
  # against another repo's tree, the worst answer available. Owner and name do not share a class any
  # more: an owner login is alphanumerics and hyphens only, so `_` cannot appear in it, so no two
  # keys can slug the same. That is now a property of the guard rather than a sentence about it.
  #
  # The NAME class is the wider one, and it MAY BEGIN WITH A DOT — `FS-GG/.github`, the authority,
  # is the repository this org fetches most and the guards used to reject it outright. The optional
  # leading dot must be followed by a non-dot, so `.`, `..` and `...` are not names.
  #
  # Nothing is WRITTEN on refusal, deliberately: the grader classifies a non-literal `repository:`
  # itself and never asks for it, so reaching here at all is a defect in this file rather than a fact
  # about anyone's workflow. Leaving the repository undecided makes the grader ask a second time, and
  # `sparse_grade`'s loop guard turns that into a loud no-verdict — which is what a defect here
  # should look like, and is strictly better than inventing a reason.
  if ! [[ "$key" =~ ^[a-z0-9][a-z0-9-]*/[.]?[a-z0-9_-][a-z0-9._-]*$ ]] || [[ "$key" == *..* ]]; then
    return 0
  fi
  slug="${key//\//__}"
  [ -e "$SPARSE_TREE_DIR/$slug.paths" ] && return 0
  [ -e "$SPARSE_TREE_DIR/$slug.err" ]   && return 0

  # AN EMPTY ROSTER IS NOT AN OFF-ROSTER VERDICT. `SPARSE_ROSTER` is assigned once, beside
  # `all_repos`; if this is ever reached before that (a reordering, a new caller) every repository in
  # the org would silently read as "not on the roster" — an UNGRADED that costs nothing and looks
  # deliberate, which is the quietest possible way to switch this whole feature off. It is a
  # no-verdict instead, because "I do not know what the roster is" is not "you are not on it".
  if [ -z "$SPARSE_ROSTER" ]; then
    printf 'unreadable\tthe roster was not available when its git tree was needed, so whether this audit may reach for it could not be decided\n' \
      > "$SPARSE_TREE_DIR/$slug.err"
    return 0
  fi
  # A HERESTRING, NOT A PIPE, and this file spends twenty lines on why — see `repo_calls`, under
  # "`grep -q` IS FED BY A HERESTRING, NEVER BY A PIPE, AND THAT IS NOT STYLE". `grep -q` exits the
  # instant it matches; if the writer is still blocked on the pipe buffer it takes SIGPIPE, and
  # `pipefail` then reports the PIPELINE as 141 — so the `if` reads FALSE even though grep MATCHED,
  # 7 times in 10 on the incident that produced the rule. Here that misreading declares a ROSTERED
  # repository off-roster: UNGRADED at exit 0, the fail-open direction, non-deterministically.
  #
  # The roster is a few hundred bytes at ~8 repos, so it cannot fire today and no runtime fixture
  # could catch it — the fixture asserts it over the SOURCE instead. #1608.
  if ! grep -qxF "$key" <<< "$SPARSE_ROSTER"; then
    printf 'offroster\tit is not on this audit%s roster, and the roster is the boundary of what this audit may claim to know about (#1556)\n' \
      "'s" > "$SPARSE_TREE_DIR/$slug.err"
    return 0
  fi

  body="$(get_repo_tree "$repo")" || rc=$?
  if [ "$rc" -ne 0 ]; then
    printf 'unreadable\tits git tree would not read — %s\n' "$(gh_last_err)" > "$SPARSE_TREE_DIR/$slug.err"
    return 0
  fi
  if ! printf '%s' "$body" | python3 -c "$SPARSE_TREE_PY" "$SPARSE_TREE_DIR/$slug.paths" \
         2>"$SPARSE_TREE_DIR/$slug.why"; then
    printf 'unreadable\t%s\n' \
      "$(tr -s '\n\t' '  ' < "$SPARSE_TREE_DIR/$slug.why" | sed 's/[[:space:]]*$//')" \
      > "$SPARSE_TREE_DIR/$slug.err"
  fi
  rm -f "$SPARSE_TREE_DIR/$slug.why" "$SPARSE_TREE_DIR/$slug.paths.partial"
}

# XML comments may span lines, so stripping only whole-line `<!-- … -->` comments would let a prose
# example satisfy an opt-in detector. This small streaming filter removes comments while preserving
# any real XML before/after them; it deliberately does not attempt to "repair" malformed XML.
strip_xml_comments() {
  awk '
    BEGIN { in_comment = 0 }
    {
      line = $0
      out = ""
      while (length(line) > 0) {
        if (in_comment) {
          finish = index(line, "-->")
          if (finish == 0) { line = ""; break }
          line = substr(line, finish + 3)
          in_comment = 0
        } else {
          start = index(line, "<!--")
          if (start == 0) { out = out line; line = ""; break }
          out = out substr(line, 1, start - 1)
          line = substr(line, start + 4)
          in_comment = 1
        }
      }
      print out
    }'
}

# Does one executable YAML literal `run: |` block enforce the package-era build-config contract?
#
# The block boundary is load-bearing. A materialize command in one step/job and a diff in another can
# run in different clean checkouts, so finding both anywhere in one workflow file proves nothing.
# The failure path is load-bearing too: `git diff ... || true`, or an `if ! git diff; then` branch that
# only prints, observes drift and deliberately lets it pass. The four real receivers all use the
# strict shape this recognizes: materialize, then `if ! git diff --quiet -- <both props>; then`, then
# a non-zero `exit` inside that guard.
workflow_enforces_build_config() {
  awk '
    function reset_block() {
      materialized = 0
      in_diff_guard = 0
      diff_guard_fails = 0
    }
    function finish_block() {
      if (materialized && diff_guard_fails) found = 1
      reset_block()
    }
    function indentation(s,    n) {
      match(s, /[^ ]/)
      n = RSTART
      return n == 0 ? length(s) : n - 1
    }
    BEGIN {
      in_run = 0
      found = 0
      reset_block()
    }
    /^[ ]*(-[ ]+)?run:[ ]*[|][-+]?[ ]*$/ {
      if (in_run) finish_block()
      in_run = 1
      run_indent = indentation($0)
      next
    }
    {
      if (!in_run) next
      if ($0 !~ /^[ ]*$/ && indentation($0) <= run_indent) {
        finish_block()
        in_run = 0
        next
      }

      line = $0
      if (line ~ /^[[:space:]]*#/) next
      if (line ~ /^[[:space:]]*dotnet[[:space:]]+build[[:space:]].*-t:FsggKitMaterialize([[:space:]]|$)/) {
        materialized = 1
      }
      if (materialized && line ~ /^[[:space:]]*if[[:space:]]+![[:space:]]+git[[:space:]]+diff[[:space:]]+--quiet[[:space:]]+--[[:space:]]+Directory\.Build\.props[[:space:]]+Directory\.Packages\.props[[:space:]]*;[[:space:]]*then[[:space:]]*$/) {
        in_diff_guard = 1
        next
      }
      if (in_diff_guard && line ~ /^[[:space:]]*exit[[:space:]]+[1-9][0-9]*([[:space:];]|$)/) {
        diff_guard_fails = 1
      }
      if (in_diff_guard && line ~ /^[[:space:]]*fi([[:space:];]|$)/) {
        in_diff_guard = 0
      }
    }
    END {
      if (in_run) finish_block()
      exit(found ? 0 : 1)
    }'
}

# THE DETECTOR PARSES YAML STRUCTURALLY, AND THAT IS NOT A STYLE CHOICE (#1504 review).
#
# This started as two line-oriented awk scanners, and a review broke them five ways in one sitting —
# every one of them legal YAML that GitHub Actions accepts:
#
#   with: {product-path: "artifacts/x"}     a FLOW mapping puts the key mid-line, so the anchored
#                                           `/^[ ]*product-path:/` never matched: "absent ⇒ defaults ⇒
#                                           pass", and a GENERATED-PRODUCT call certified as the
#                                           committed-root gate. The exact fail-open this row exists for.
#   with: … then uses: …                    YAML mappings are UNORDERED. A scanner that collects inputs
#                                           only AFTER the `uses:` line saw none. Same fail-open.
#   - run: "true"   # was: uses: FS-GG/…    an INLINE comment is not a whole-line comment, so a repo that
#                                           DELETED its caller and left a note audited as wired — the one
#                                           thing the `commented` fixture leg exists to catch.
#   on: {pull_request: {paths: [src/**]}}   flow again: read as "inline form, therefore unfiltered,
#                                           therefore armed". An unarmed gate, green.
#   paths:                                  a sequence at its KEY's indentation is ordinary YAML, and the
#   - ".claude/skills/**"                   scanner required entries strictly deeper — so it reported a
#                                           correctly-armed gate as a gap, and told the operator to add
#                                           the filter that was already there.
#
# Those are not five bugs; they are one. A `paths:` filter and a `with:` block are STRUCTURE, and the
# question this detector asks — *what was the assertion pointed at, and does the gate run when the roots
# change?* — is a question about structure. A line-grep cannot express it, and each patch to the scanner
# would have bought one more dialect. So the YAML is parsed (`yq`, else `python3`+PyYAML — the ladder
# scripts/repos.sh already uses) and the verdict is computed over the parsed document, where key order,
# flow style, comments, anchors and indentation are the parser's problem and not ours.
#
# `paths:` COVERAGE IS GLOB MATCHING, not a prefix test. `.claude/**` and `**/skills/**` genuinely fire
# on a root change and must pass; `.claude/skills-archive/**` genuinely does not and must fail; a
# `!`-negated entry SUBTRACTS. A prefix test got all four wrong, two of them fail-open.
#
# --- the CALLER detector: skill-union (#1504) -----------------------------------------------------
#
# `skill-union-assert.yml` is SUBJECT-PARAMETERISED, and that is what makes a bare `uses:` detector
# fail open here. The reusable assertion audits whatever `product-path:` names: FS.GG.Templates
# legitimately calls it against GENERATED composition products (FS.GG.Templates#49), and that call
# says nothing at all about Templates' own committed `.claude/.codex/.agents` roots. A `wf:` token
# would count it anyway — certifying the full-union capability off a call that never looks at the
# repository's own roots. That is #628's "a green nobody earned", one layer in: for this capability the
# detector's subject is not the workflow, it is what the workflow was POINTED AT.
#
# So the receiver contract is compound, and both halves must live in ONE workflow file:
#
#   1. THE CALL is aimed at the receiver's own repository root, over all three ADR-0011 roots.
#      `product-path:` absent (the input's default is ".") or `.`/`./`; `roots:` absent (the workflow's
#      default is ADR-0011's three) or naming all three as whitespace-separated tokens. A narrowed
#      `roots:` is a smaller audit than the capability claims — a tree that intentionally supports fewer
#      runtimes is a different, DECLARED thing (docs/coordination/skill-union-assertion.md), not this
#      capability. A `product-path` that is an EXPRESSION (`${{ … }}`) is not a value this detector can
#      resolve, so it does not satisfy the call: unknown fails CLOSED, deliberately (#266).
#   2. THE TRIGGER fires when any committed root changes, ON A PULL REQUEST. A gate that never runs is
#      not a gate, and #332/#334/#880 are four hand-repairs of exactly that shape in this repo alone:
#      the check is fine and its `paths:` filter is what fails open. `pull_request` and not `push`,
#      because only a PR check can be a REQUIRED context (check-required-contexts.py states the same
#      rule for the same reason); `pull_request_target` is NOT it either — it checks out the BASE ref,
#      so the assertion would audit the tree the change is not in.
#
# YAML on stdin -> JSON on stdout. Same ladder as scripts/repos.sh (yq first, PyYAML second), because
# this script already depends on that script's ability to read YAML — there is no new dependency here,
# only a second caller of the one that existed.
yaml_text2json() {
  if command -v yq >/dev/null 2>&1; then
    yq -o=json '.' - 2>/dev/null
  else
    python3 -c 'import sys,yaml,json; json.dump(yaml.safe_load(sys.stdin), sys.stdout, default=str)' 2>/dev/null
  fi
}

# The verdict program. Stdin: one workflow as JSON. Argv: the authority repo. Stdout, always exactly
# one line: `call=<0|1> trigger=<0|1>`.
#
# `python3` and STDLIB ONLY — no PyYAML. The YAML was already turned into JSON by the ladder above, so
# this half runs on a box where only `yq` can read YAML. That split is why the ladder is not simply
# "use python for everything".
CALLER_VERDICT_PY="$(cat <<'PY'
import json, re, sys

AUTHORITY = sys.argv[1]
ROOTS = (".claude/skills", ".agents/skills")
# One representative CHANGED FILE per root. GitHub matches a `paths:` filter against changed file
# paths, so "does this filter fire when a skill changes?" is "does it match a file inside a skill".
# A skill's SKILL.md is the minimal such file, and it is two levels down — which is exactly why
# `.claude/skills/*` (a single `*` does not span `/`) correctly does NOT arm this gate.
PROBES = tuple(r + "/probe-skill/SKILL.md" for r in ROOTS)

USES_RE = re.compile(
    r"^" + re.escape(AUTHORITY) + r"/\.github/workflows/skill-union-assert\.ya?ml(@.*)?$")


def norm(p):
    p = str(p).strip()
    return p[2:] if p.startswith("./") else p


def glob_re(pat):
    """GitHub filter glob -> regex. `**` spans `/`, `*` and `?` do not."""
    out, i = [], 0
    while i < len(pat):
        if pat[i] == "*":
            if pat[i:i + 2] == "**":
                out.append(".*")
                i += 2
            else:
                out.append("[^/]*")
                i += 1
        elif pat[i] == "?":
            out.append("[^/]")
            i += 1
        else:
            out.append(re.escape(pat[i]))
            i += 1
    return re.compile("^" + "".join(out) + "$")


def hits(pattern, path):
    try:
        return glob_re(norm(pattern)).match(path) is not None
    except re.error:
        return False


def on_block(wf):
    # YAML 1.1 reads a bare `on:` as the BOOLEAN true (PyYAML does; yq, being YAML 1.2, keeps the
    # string). JSON keys are strings either way, so both spellings have to be looked for — the same
    # `wf.get(True, wf.get("on"))` dance tests/repos-registry/run.sh already does.
    for key in ("on", "true", "True"):
        if isinstance(wf, dict) and key in wf:
            return wf[key]
    return None


def triggers_on_roots(wf):
    on = on_block(wf)
    if isinstance(on, str):                      # `on: pull_request`
        return on.strip() == "pull_request"
    if isinstance(on, list):                     # `on: [pull_request, push]`
        return any(isinstance(e, str) and e.strip() == "pull_request" for e in on)
    if not isinstance(on, dict) or "pull_request" not in on:
        return False                             # no PR trigger: it reports nothing on a PR
    cfg = on["pull_request"] or {}
    if not isinstance(cfg, dict):
        return False
    paths = cfg.get("paths")
    if paths is not None:
        if not isinstance(paths, list) or not paths:
            return False                         # an empty filter matches nothing
        keep = [p for p in paths if isinstance(p, str) and not p.startswith("!")]
        drop = [p[1:] for p in paths if isinstance(p, str) and p.startswith("!")]
        for probe in PROBES:
            if not any(hits(p, probe) for p in keep):
                return False
            if any(hits(p, probe) for p in drop):
                return False                     # a `!` entry SUBTRACTS the root
    # A `paths-ignore:` is not a coverage claim — unfiltered is WIDER than covered, so it passes unless
    # an entry actually names a root. (GitHub rejects paths + paths-ignore on one event; handling both
    # costs nothing and assumes nothing.)
    ignore = cfg.get("paths-ignore")
    if isinstance(ignore, list):
        for probe in PROBES:
            if any(hits(p, probe) for p in ignore if isinstance(p, str)):
                return False
    return True


def calls_own_roots(wf):
    jobs = wf.get("jobs") if isinstance(wf, dict) else None
    if not isinstance(jobs, dict):
        return False
    for job in jobs.values():
        # A reusable workflow is called by a JOB's `uses:`, never a step's — so reading the structure
        # also refuses a `- uses:` inside `steps:`, which Actions rejects and a text grep counted.
        if not isinstance(job, dict) or not isinstance(job.get("uses"), str):
            continue
        if not USES_RE.match(job["uses"].strip()):
            continue
        with_ = job.get("with")
        with_ = with_ if isinstance(with_, dict) else {}
        if "product-path" in with_:
            pp = with_["product-path"]
            if not isinstance(pp, str) or norm(pp).rstrip("/") not in ("", "."):
                continue                         # a subdirectory, or an expression we cannot resolve
        if "roots" in with_:
            rt = with_["roots"]
            if not isinstance(rt, str):
                continue
            if not set(ROOTS) <= {norm(t) for t in rt.split()}:
                continue                         # a narrowed root set is a smaller audit
        return True
    return False


try:
    doc = json.load(sys.stdin)
except Exception:
    print("call=0 trigger=0")
    sys.exit(0)
if not isinstance(doc, dict):
    print("call=0 trigger=0")
    sys.exit(0)
print("call=%d trigger=%d" % (calls_own_roots(doc), triggers_on_roots(doc)))
PY
)"

# caller_verdict — workflow TEXT on stdin -> `call=<0|1> trigger=<0|1>` on stdout.
#
# A workflow this cannot PARSE yields `call=0 trigger=0` and a warning, not a no-verdict for the whole
# repo. That is a deliberate reading of #320's rule rather than an exception to it: GitHub will not run a
# workflow it cannot parse, so an unparseable file cannot be the live gate this capability requires —
# "not the gate" is an ANSWER about it, not a failure to look. It is also still swept textually by the
# `wf:` and `script:` detectors, so nothing about it goes unexamined.
caller_verdict() {
  local json
  json="$(yaml_text2json)" || json=""
  if [ -z "$json" ] || [ "$json" = "null" ]; then
    printf 'call=0 trigger=0'
    return 0
  fi
  python3 -c "$CALLER_VERDICT_PY" "$AUTHORITY" <<< "$json" 2>/dev/null || printf 'call=0 trigger=0'
}

# --- THE SPARSE-CHECKOUT CLOSURE SWEEP (#1529) ---------------------------------------------------
#
# WHAT IT ASSERTS. Exactly what `check-sparse-checkout-closure.py` asserts, over every rostered
# repository's `.github/workflows/**` instead of just this one's: for every `actions/checkout` that
# names a `repository:` AND declares a `sparse-checkout:`, every pattern must be an anchored, literal
# directory that selects something. The four rules, their cone-mode exemptions, their refusals and
# their wording all come from that file — this adds REACH and nothing else.
#
# WHY IT IS HERE AND NOT THERE. The local gate's virtue is that it is pure, offline and f(tree); a
# gate that reaches the network is a different animal with different failure modes, and #1522 filed
# the reach rather than smuggling it in. This script already owns the roster, the repo filter, the
# retry ladder and the rate-limit budget, and — decisively — it ALREADY FETCHES every rostered
# repo's workflows once, for the participation detectors. The sweep rides that existing pass and
# costs no additional API call.
#
# WHAT IT CAN SEE, AND WHERE THAT STOPS (#1556 reopened this paragraph). Rule (4) asks a git index.
# This audit HOLDS exactly one — the authority's, its own checkout — and it now FETCHES the rest from
# the API for any ROSTERED repository a step actually names; see THE ROSTER'S TREES above for the
# mechanism, the laziness and the failure modes. So a cone-mode cross-repo checkout between two
# siblings, NEITHER of which is the authority, is graded: that was the residue #1529 left, and it is
# the default shape of the defect, because `sparse-checkout-cone-mode` defaults to true and nothing
# about a pattern STRING distinguishes a file from a directory.
#
# What is left is the boundary and the failures, and neither is ever an ok. A fetch of a repository
# that is NOT on the roster is UNGRADED and says so — the roster is what this audit was given. A
# `repository:` that is an expression is UNGRADED: the runner resolves it, from values not in the
# file. A tree that would not READ is UNGRADED too, and additionally makes the whole run a
# no-verdict at exit 2, because a read that failed is not a read that passed (#266).
#
# WHAT IS SHARED, AND WHAT IS HONESTLY NOT. Every RULE is imported: the four graded rules and their
# wording (`grade_pattern`), how the runner splits the input and which empty spellings are refused
# (`patterns_of`), the cone-mode default (`cone_mode_of`), which steps are subjects at all
# (`sparse_steps`), and how "ours" and the tracked set are derived (`origin_repository`,
# `tracked_paths`). None of it is restated below — grep for the rule's own wording and it is not
# here, which is what the fixture's first sparse leg pins.
#
# THE NO-VERDICT TYPES ARE PART OF THAT SURFACE, and #1599 is the cost of leaving them off this list.
# A borrowed rule does not only return answers; it REFUSES, and the refusal arrives as an exception
# whose class is as much of the interface as the function name. Two are borrowed: `GateError` (the
# gate's, raised by `grade_pattern`) and `SparseRefusal` (`scripts/lib/sparse.py`'s, raised by
# `patterns_of`/`cone_mode_of`, and deliberately NOT a subclass so each caller maps it to its own
# exit-code contract). Both are named in `BORROWED` and both are caught at every call site below.
#
# The per-step ORCHESTRATION is borrowed too: `grade_document` owns the parse/refusal/full-clone/
# enumeration decisions. This caller supplies only a resolver for the roster's cached trees, then
# renders the structured verdict into its ledger and retry protocol (#1555).
#
# Stdin: one workflow as JSON. Argv: the rule file, the authority tree, the `where` prefix, and the
# roster tree cache (#1556).
# Stdout: tab-separated records, one per line. Every message is flattened to a single line, because
# the ledger is line-oriented and a finding that spanned lines would be read as several records.
#
# TWO OUTPUT MODES, AND ONLY ONE OF THEM IS A VERDICT (#1556). If any step needs the tracked paths of
# a repository the cache does not yet hold, this prints `want\t<repo>` lines and NOTHING ELSE, and
# `sparse_grade` fetches them and runs it again. That is why the records are BUFFERED rather than
# printed as they are found: a half-verdict written to the ledger and then superseded would be
# counted twice. The alternative — a separate discovery pass — would put "which steps need a tree"
# in two places, and the second copy is the one that stops matching.
SPARSE_VERDICT_PY="$(cat <<'PY'
import importlib.util, json, os, re, sys

RULE, TREE, WHERE, TREE_CACHE = sys.argv[1], sys.argv[2], sys.argv[3], sys.argv[4]

# By PATH, not by name: the module's filename has hyphens, so it is not a legal `import` target, and
# putting scripts/ on sys.path to reach it by some alias would be a second name for one file. Loading
# it does not run its main() — it is guarded by `if __name__ == "__main__"` — and its own top-level
# sys.path insert is what makes its `lib.gate` import resolve, wherever this is run from.
spec = importlib.util.spec_from_file_location("sparse_closure_rule", RULE)
rule = importlib.util.module_from_spec(spec)
spec.loader.exec_module(rule)

# THE BORROWED SURFACE, NAMED. Importing across a file boundary makes these eight names an interface,
# and an interface nobody wrote down is one the next refactor is entitled to break. #1530 hoisted the
# PARSE (`patterns_of`, `cone_mode_of`) into scripts/lib/sparse.py, and its criterion 2 was that
# neither caller retains a private copy — so this was never a hypothetical.
#
# BOTH NO-VERDICT TYPES ARE NAMED, and that is the lesson of #1599 rather than a tidiness. A name
# check that lists only `GateError` cannot see the drift that actually happened: no symbol
# DISAPPEARED, the EXCEPTION `patterns_of` raises changed identity — from `GateError` to
# `lib.sparse.SparseRefusal`, which is deliberately not a subclass of it — and the handler written to
# catch it stopped catching anything. Listing `SparseRefusal` here does not by itself catch that
# class of drift, but it makes the NEXT move of the no-verdict type a refusal to grade instead of a
# traceback out of the middle of the loop.
#
# Asserted up front, and by NAME, so the failure is a sentence an operator can act on rather than an
# AttributeError from somewhere in the loop. It fails CLOSED: the caller records a refusal and the
# audit exits 3, because a rule that cannot be loaded must never round to "no repository enumerates
# anything" across ten repositories.
BORROWED = ("grade_document", "repository_matches",
            "origin_repository", "tracked_paths", "GateError", "SparseRefusal")
missing = [name for name in BORROWED if not hasattr(rule, name)]
if missing:
    sys.exit("%s no longer exposes %s. repos-audit.sh borrows the sparse-checkout closure rule from "
             "it rather than restating it (#1529). If the rule MOVED — #1530 hoisted the parse into "
             "scripts/lib/sparse.py, and the gate re-exports it — retarget this loader at its new "
             "home. If a no-verdict TYPE went (`GateError`, `SparseRefusal`), the fix is not a "
             "retarget: find what the parse and the grade raise now and catch it below, because an "
             "exception this program does not catch kills it before its `counts` record and the "
             "sweep then reports that it found no cross-repo checkout at all (#1599). Refusing to "
             "grade, because a missing rule must not look like a clean org."
             % (RULE, ", ".join(missing)))


records = []
wants = []


def emit(kind, text):
    records.append("%s\t%s" % (kind, " ".join(str(text).split())))


# --- the roster's trees, as the grader sees them (#1556) -----------------------------------------
#
# A literal `owner/name` and nothing else. `repository: ${{ inputs.repo }}` is resolved by the RUNNER
# from values this file cannot see, so there is no repository to ask for a tree — that is a permanent
# boundary of the audit, reported UNGRADED, and NOT a read failure to retry.
#
# THE TWO COMPONENTS DO NOT SHARE A CHARACTER CLASS, and writing one class twice is what broke this
# (#1608). GitHub's rule: an OWNER login is alphanumerics and hyphens, and may not begin with a
# hyphen; a repository NAME may also carry `.` and `_`, and MAY BEGIN WITH A DOT. Requiring the name
# to begin with an alphanumeric rejected `FS-GG/.github` — the authority, the repository every
# sibling in this org fetches — so every cross-repo step naming it was reported "not a literal
# `owner/name`", a permanent boundary at exit 0, about the one repository whose `repository:` is the
# most literal thing in the file. Any dot-prefixed repo an org adds (`.allstar`, `.github-private`)
# was silently unreachable the same way, so rule (4) never ran for it.
#
# The optional leading dot must be FOLLOWED by a non-dot, so `.`, `..` and `...` are not names.
#
# THE OWNER CLASS IS ALSO WHAT MAKES THE CACHE SLUG INJECTIVE. The slug is the key with `/` doubled
# to `__`; two keys collide only if one owner is a prefix of the other followed by `_`, and an owner
# cannot contain `_`. See the matching guard in `sparse_tree_ensure`, which states the same thing on
# the side that turns the slug into a path.
LITERAL_REPOSITORY = re.compile(r"^[A-Za-z0-9][A-Za-z0-9-]*/[.]?[A-Za-z0-9_-][A-Za-z0-9._-]*$")

# The reason, once, because TWO places now answer with it: `_resolve_foreign`, and the grading loop —
# which has to settle the shape BEFORE it asks whose index applies, so that an expression stays a
# permanent boundary even on a run whose own origin would not read.
NOT_LITERAL = ("its `repository:` is not a literal `owner/name` — the runner resolves it from values "
               "this audit cannot see, so there is no tree to ask")


def is_literal_repository(repository):
    """Is this a repository GitHub could really resolve, rather than a runner expression?

    `..` is refused separately from the pattern: a name may carry dots, but the slug this becomes is
    turned into a filesystem path on both sides of the cache.
    """
    return bool(LITERAL_REPOSITORY.match(repository)) and ".." not in repository


_foreign = {}


def foreign_tracked(repository):
    """(kind, payload) for a repository this checkout does not hold.

    'ok' + the tracked path set; 'want' + None (fetch it and ask again); 'ungraded' + a reason that
    is a permanent boundary; 'noverdict' + a reason that is a failure to READ, which must make the
    whole run a no-verdict rather than a green (#266).
    """
    if repository not in _foreign:
        _foreign[repository] = _resolve_foreign(repository)
    return _foreign[repository]


def _resolve_foreign(repository):
    # Kept even though the caller has already settled the shape: this is the function that turns a
    # repository into a PATH, and defence in depth on the two sides of that is the whole reason the
    # bash guard exists too.
    if not is_literal_repository(repository):
        return ("ungraded", NOT_LITERAL)
    slug = repository.lower().replace("/", "__")
    paths_at = os.path.join(TREE_CACHE, slug + ".paths")
    error_at = os.path.join(TREE_CACHE, slug + ".err")
    if os.path.exists(paths_at):
        try:
            with open(paths_at, encoding="utf-8") as handle:
                tracked = set(json.load(handle))
        except (OSError, ValueError) as problem:
            return ("noverdict", "its fetched git tree could not be read back (%s)" % problem)
        if not tracked:
            return ("noverdict", "its fetched git tree held no paths")
        return ("ok", tracked)
    if os.path.exists(error_at):
        try:
            with open(error_at, encoding="utf-8") as handle:
                kind, _, reason = handle.read().strip().partition("\t")
        except OSError as problem:
            return ("noverdict", "why its git tree is unavailable could not be read (%s)" % problem)
        # An UNRECOGNISED kind is a no-verdict, not a boundary: the fail-closed direction, so a kind
        # added on the bash side and not here cannot quietly stop making the run incomplete.
        return ("ungraded" if kind == "offroster" else "noverdict",
                reason or "its git tree is unavailable and no reason was recorded")
    return ("want", None)


try:
    document = json.load(sys.stdin)
except ValueError:
    document = None

# Resolved ONCE, and only if some step actually needs it: `git ls-files` per workflow across ten
# repositories is a cost paid for nothing on the overwhelming majority of files, which contain no
# cross-repo checkout at all.
_tree = {}


def tree():
    if not _tree:
        _tree["ours"] = rule.origin_repository(TREE)
        _tree["tracked"] = rule.tracked_paths(TREE)
    return _tree


cross = graded = pattern_count = clones = ungraded = 0
# #1556 criterion 3: how much reach rule (4) actually HAD, as a fraction the reader can check.
# `rule4_subjects` is every cross-repo step that got as far as being graded (so: not a full clone,
# not a refused shape); `rule4_ran` is how many of those had a tracked path set to ask.
rule4_ran = rule4_subjects = 0

def resolve(repository):
    """Give the shared grader this audit's answer for rule (4)."""
    resolved = tree()
    if not is_literal_repository(repository):
        return "ungraded", None, NOT_LITERAL
    if resolved["ours"] is None:
        return ("noverdict", None,
                "this audit could not read its own checkout's origin, so whether that is the tree it "
                "holds — and therefore whose git index answers rule (4) — could not be decided")
    if rule.repository_matches(repository, resolved["ours"]):
        if resolved["tracked"] is None:
            return ("noverdict", None,
                    "it fetches the tree this audit holds (%s), whose git index would not answer" % resolved["ours"])
        return "ok", resolved["tracked"], None
    kind, payload = foreign_tracked(repository)
    if kind == "ok":
        return "ok", payload, None
    if kind == "want":
        return "want", None, None
    return kind, None, payload


for verdict in rule.grade_document(document, WHERE, resolver=resolve):
    cross += 1
    if verdict.resolution_kind == "want":
        # A request invalidates this whole pass: `sparse_grade` fetches every wanted tree, then
        # reruns the shared grader and writes only that complete verdict.
        wants.append(verdict.repository)
        continue
    if verdict.full_clone:
        clones += 1
        continue
    if verdict.refusal:
        emit("refusal", verdict.refusal)
        continue

    assert verdict.patterns is not None and verdict.cone is not None
    pattern_count += len(verdict.patterns)
    rule4_subjects += 1
    if verdict.resolvable:
        rule4_ran += 1
    for finding in verdict.findings:
        emit("finding", finding)
    if verdict.enumeration_checked:
        graded += 1
    else:
        ungraded += 1
        emit("ungraded", "%s: cone mode against %r, whose tracked paths this audit could not obtain "
                         "(%s) — NOTHING was asserted about whether these patterns name files or "
                         "directories" % (verdict.where, verdict.repository, verdict.resolution_reason))
    if verdict.resolution_reason:
        emit("unresolved", "%s: rule (4), the existence of its directories in %r, was NOT checked — "
                           "%s" % (verdict.where, verdict.repository, verdict.resolution_reason))
        if verdict.resolution_kind == "noverdict":
            emit("noverdict", "%s: %r — %s" % (verdict.where, verdict.repository,
                                                 verdict.resolution_reason))

# APPENDED RAW, never through `emit` — that helper flattens whitespace so a multi-line finding cannot
# be read as several records, and it would collapse this line's TABS into spaces. The awk that sums
# these splits on tabs, so every field but the first would vanish into `$2` and the counts would read
# as zero: a sweep that graded ten steps reporting that it graded none.
records.append("counts\t%d\t%d\t%d\t%d\t%d\t%d\t%d"
               % (cross, graded, pattern_count, clones, ungraded, rule4_ran, rule4_subjects))

# THE VERDICT IS PRINTED ONLY WHEN IT IS COMPLETE. A pass that wants a tree prints its requests and
# no records at all, so nothing partial can reach the ledger.
if wants:
    for repository in dict.fromkeys(wants):
        print("want\t%s" % repository)
else:
    for record in records:
        print(record)
PY
)"

# sparse_grade <repo> <workflow filename>; workflow TEXT on stdin. Appends to $SPARSE_FILE.
#
# An UNPARSEABLE workflow is recorded and not graded, on the same reading of #320 that caller_verdict
# states one function up: GitHub will not run a workflow it cannot parse, so it cannot fetch anything
# and cannot under-fetch anything either. "It cannot run" is an ANSWER about it, not a failure to
# look — and it is recorded rather than dropped, so the summary's arithmetic still adds up.
#
# A verdict program that DIES, by contrast, is a genuine failure to look, and is recorded as a
# refusal so it cannot round to a clean sweep.
sparse_grade() {
  # `wanted_repo` IS DECLARED LOCAL, AND THE NAME IS NOT `repo` ON PURPOSE. bash locals are
  # DYNAMICALLY scoped: this function is called from `repo_calls`, whose own `local repo` is the
  # repository whose workflows it is walking, and a bare `while read -r repo` here would assign
  # straight through to it. It was not a hypothetical — it was the first thing the fixture caught.
  # The damage is silent and total: after the first cross-repo tree fetch, `repo_calls` continues its
  # loop reading `.github/workflows/<f>` from whatever repository this loop last saw, and since
  # `read` sets its variable EMPTY when it hits EOF, the repository it actually asked for was the
  # empty string. Every remaining workflow in that repo 404s, so the repo reads as having two
  # workflows instead of four — a smaller sweep that still reports success. A distinct name plus
  # `local` is two independent guards, and the comment is the third.
  local json out attempt wanted wanted_repo
  local where="$1/.github/workflows/$2"
  printf 'workflow\t%s\n' "$where" >> "$SPARSE_FILE"
  json="$(yaml_text2json)" || json=""
  if [ -z "$json" ] || [ "$json" = "null" ]; then
    printf 'unparseable\t%s\n' "$where" >> "$SPARSE_FILE"
    return 0
  fi
  # AT MOST TWO PASSES, AND THE SECOND ONLY WHEN A TREE IS ACTUALLY NEEDED (#1556 criterion 5).
  # Pass one grades everything it can and prints `want` lines for any rostered repository whose
  # tracked paths it does not have; if there are none — the case for every workflow with no
  # cross-repo checkout, and for every one that fetches the authority — its verdict is final and no
  # API call is made. Pass two runs against a cache in which every wanted repository is DECIDED
  # (`sparse_tree_ensure` always leaves a `.paths` or an `.err`), so it cannot want anything again.
  for attempt in 1 2; do
    out="$(python3 -c "$SPARSE_VERDICT_PY" "$SPARSE_RULE" "$AUTHORITY_TREE" "$where" \
             "$SPARSE_TREE_DIR" <<< "$json")" || {
      printf 'refusal\t%s: the shared sparse-checkout closure rule could not be evaluated against this workflow\n' \
        "$where" >> "$SPARSE_FILE"
      return 0
    }
    wanted="$(printf '%s\n' "$out" | sed -n 's/^want'$'\t''//p')"
    if [ -z "$wanted" ]; then
      printf '%s\n' "$out" >> "$SPARSE_FILE"
      return 0
    fi
    [ "$attempt" -eq 1 ] || break
    while IFS= read -r wanted_repo; do
      [ -n "$wanted_repo" ] || continue
      sparse_tree_ensure "$wanted_repo"
    done <<< "$wanted"
  done
  # A second pass that still wants a tree is a DEFECT IN THIS FILE — the cache guarantees otherwise —
  # so it is a loud no-verdict rather than a silent third attempt. Fail closed: the alternative is a
  # loop, and the alternative to the loop is grading the step as if rule (4) had passed.
  printf 'noverdict\t%s: the sweep asked twice for the git tree of %s and still did not have it. That is a defect in repos-audit.sh, not in anyone%s workflow; nothing was asserted about this step.\n' \
    "$where" "$(printf '%s' "$wanted" | tr '\n' ' ')" "'s" >> "$SPARSE_FILE"
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
#   materializer:<id>  BOTH package opt-in and CI enforcement          (the `materializer:` detector)
#   caller:<id>        BOTH an own-subject call and a trigger over it  (the `caller:` detector)
#
# One pass, all kinds. Re-walking a repo's workflows once per detector kind would multiply the API
# traffic against the rate limit this script already treats as its main adversary — the same reasoning
# that made this function repo-major rather than cap-major in the first place.
# --- THE KIT-PIN FRESHNESS SWEEP (#1540, closing #1560's criterion 4) ----------------------------
#
# WHAT IT ASSERTS. Every `receives: coordination-kit` repo pins an FS.GG.Kit version EQUAL to the
# newest stable FS.GG.Kit published on nuget.org. A receiver below that is STALE and reds HERE.
#
# WHY IT EXISTS. `coordination-coherence` already catches a stale receiver — but only in the
# RECEIVER'S OWN `main`, and only once that repo happens to push. Between a kit republish and the
# receiver's next push, the receiver is stale and every check anyone can see is green. That window is
# not theoretical: FS.GG.Audio's gate read green for over a day across the 0.7.0 AND 0.8.0
# republishes, purely because it had not pushed since before either. A check that cannot see the
# thing it is reporting on is the epic #266 defect, and the gate that REPORTS (the receiver's) is not
# the gate that can ACT (this one) — #1560's criterion 4 in as many words. This sweep is f(roster,
# feed): it needs no push from anybody, so the answer changes the moment the kit is republished.
#
# NOT A SECOND OPINION ON COHERENCE. It grades the PIN, never the materialized bytes.
# `coordination-coherence` owns the bytes and is strictly better at it. The pin is what this can see
# from here without cloning eight repos, and a stale pin is the CAUSE the byte drift is a symptom of.
#
# THE COMPARAND IS THE FEED, NOT THIS TREE. `src/FS.GG.Kit/FS.GG.Kit.csproj`'s `<Version>` is what
# the authority intends to ship next; nuget.org is what a receiver can actually restore. Grading
# against the csproj would demand a bump no receiver could make in the window between a version
# commit and its release tag. "Canonical source moved and the PACKAGE has not" is a real defect and
# it already has an owner — `check-kit-published-coherence.py` — so the two gates compose: that one
# says PUBLISH IT, this one says RECEIVERS, TAKE IT. Neither restates the other.
#
# WHERE THE PIN LIVES IS DERIVED, NOT LISTED. There is no per-repo pin-path column in
# registry/repos.yml and this sweep does not add one — a hand-maintained list of which file holds
# which repo's pin is precisely the thing that rots (the roster header's own warning). Instead all
# three shapes the org actually uses are READ, and the file that carries a version literal is the
# answer:
#   .config/kit/FS.GG.Kit.receiver.proj   an inline `Version=` on the PackageReference (no CPM).
#                                         FS.GG.Templates is the only receiver in this shape today.
#   Directory.Packages.local.props        CPM, `build-config` receivers — their root props file is
#                                         the DISTRIBUTED baseline, so repo-owned pins go here.
#   Directory.Packages.props              CPM, the repos that hand-author their own build config
#                                         (net, audio).
# A repo may legitimately have only one of these. What it may NOT have is none, or two that
# disagree: both are REFUSALS (exit 3), never a pass. "I could not find this repo's pin" must not
# render as "this repo's pin is current" — and it very nearly did, because the two-file assumption
# (`Directory.Packages*.props` only) reports FS.GG.Templates as having no pin at all, which is how
# this item was originally briefed and is wrong.
#
# Argv: the staging dir and its manifest. Stdout: tab-separated ledger records, one per line.
KIT_PIN_PY=$(cat <<'PY'
import os
import sys
import xml.etree.ElementTree as ET

sys.path.insert(0, os.environ["FSGG_SCRIPTS_DIR"])
import fsgg_feed  # noqa: E402

PACKAGE = "FS.GG.Kit"

# The flat-container base the published version is resolved from. Unset in CI, so the sweep reads
# the real nuget.org through fsgg_feed's own reader — the SAME registry the org preset routes
# FS.GG.* to, and therefore the same one Renovate compares against. The fixture points this at a
# local flat-container tree over file://, which exercises this exact fetch/parse/compare path rather
# than a mock of it: only the base URL differs between the test and production.
_base = os.environ.get("FSGG_NUGET_ORG_BASE", "").strip()
if _base:
    fsgg_feed.NUGET_ORG = _base.rstrip("/")

out = []


def emit(kind, *fields):
    out.append("\t".join([kind, *[str(f).replace("\t", " ").replace("\n", " ") for f in fields]]))


def localname(tag):
    # MSBuild files may or may not carry the 2003 namespace; both spellings are live in this org.
    return tag.rsplit("}", 1)[-1]


def versions_in(text):
    """Every FS.GG.Kit version literal in one MSBuild file. Raises on XML that will not parse."""
    root = ET.fromstring(text)
    found = []
    for el in root.iter():
        if localname(el.tag) not in ("PackageReference", "PackageVersion"):
            continue
        include = el.get("Include") or el.get("Update") or ""
        # NuGet package ids are case-insensitive; comparing them case-sensitively is how a gate
        # misses a real pin and reports the repo as unpinned.
        if include.strip().lower() != PACKAGE.lower():
            continue
        # VersionOverride BEATS the central PackageVersion under CPM, so a repo can carry
        # `<PackageReference Include="FS.GG.Kit" VersionOverride="0.6.0" />` while its
        # Directory.Packages.props says 0.8.0 — and reading only `Version` grades the version that
        # is NOT restored and calls a stale receiver current. That is this sweep's own failure mode
        # one attribute over, so the override is read, and it WINS rather than being reported as a
        # second contradictory pin: MSBuild is not ambiguous here, and refusing a shape the build
        # resolves deterministically would be a red nobody could act on.
        override = el.get("VersionOverride")
        if override is not None and override.strip():
            found.append((override.strip(), True))
            continue
        version = el.get("Version")
        if version is None:
            child = next((c for c in el if localname(c.tag) == "Version"), None)
            version = child.text if child is not None else None
        if version is None:
            continue          # a version-less PackageReference IS the CPM shape: pin is elsewhere.
        version = version.strip()
        if not version:
            continue
        found.append((version, False))
    return found


staging, manifest = sys.argv[1], sys.argv[2]

repos = {}
order = []
with open(manifest, encoding="utf-8") as fh:
    for line in fh:
        line = line.rstrip("\n")
        if not line:
            continue
        repo, repopath, local = line.split("\t", 2)
        if repo not in repos:
            repos[repo] = []
            order.append(repo)
        if local != "-":
            repos[repo].append((repopath, os.path.join(staging, local)))

# Resolve the comparand ONCE, before grading anybody. A feed we cannot read is an undetermined run,
# not a clean one: without it there is no comparand, so every repo below would otherwise be graded
# against nothing and pass. fsgg_feed raises rather than returning [] precisely so this cannot be
# fumbled into a silent green.
# `except Exception`, not `except GateError`, and the width is deliberate. fsgg_feed wraps
# HTTPError/URLError/ValueError, but a socket timeout, an SSL error or a ConnectionReset mid-read
# escapes all three — and an escaping traceback makes this program exit non-zero, which the shell
# turns into `die` -> exit 3, the PERMANENT no-verdict. A network blip would then be reported as
# "a human must fix a file" and never retried: #335 exactly backwards. Every failure to reach the
# feed is retryable, so every one of them lands on the same undetermined row.
try:
    live = fsgg_feed.nuget_org_versions(PACKAGE)
    stable = []
    unparsable = []
    for v in live:
        # Filtered one at a time: nuget.org serving a single version this ordering cannot parse
        # must not make the whole sweep undetermined forever. One bad entry is refused BY NAME and
        # the rest still yield a comparand — but only if some stable version survived.
        try:
            if not fsgg_feed.is_prerelease(v):
                stable.append(v)
        except fsgg_feed.GateError:
            unparsable.append(v)
    if not stable:
        raise fsgg_feed.GateError(
            f"nuget.org serves {len(live)} version(s) of {PACKAGE} and none is a usable stable "
            f"release (unparsable: {unparsable or 'none'}), so there is no version a receiver "
            f"could pin."
        )
    published = fsgg_feed.newest(stable)
except Exception as e:
    emit("undetermined", "*",
         f"could not resolve the published {PACKAGE} version: {type(e).__name__}: {e}")
    print("\n".join(out))
    sys.exit(0)

emit("published", published)

for repo in order:
    literals = []
    for repopath, local in repos[repo]:
        try:
            with open(local, encoding="utf-8") as fh:
                text = fh.read()
        except (OSError, UnicodeDecodeError) as e:
            # A staged file we cannot decode is a file we did not really read. Retryable in the
            # same sense as any other unread evidence, and emphatically not a verdict.
            emit("undetermined", repo, f"staged {repopath} unreadable: {type(e).__name__}: {e}")
            break
        try:
            for v, is_override in versions_in(text):
                literals.append((repopath, v, is_override))
        except ET.ParseError as e:
            emit("refusal", repo,
                 f"{repopath} is not parsable XML ({e}), so this repo's {PACKAGE} pin could not be "
                 f"read. Unparsable is not unpinned and it is not current.")
            break
    else:
        # A VersionOverride wins outright, so it collapses the set rather than contradicting it.
        overrides = [(p, v) for p, v, o in literals if o]
        if overrides:
            literals = [(p, v, True) for p, v in overrides]
        distinct = {v for _, v, _ in literals}
        where = ", ".join(
            f"{p} -> {v}{' (VersionOverride)' if o else ''}" for p, v, o in literals
        )
        if not literals:
            emit("refusal", repo,
                 f"no {PACKAGE} version literal in any of the three pin shapes "
                 f"(.config/kit/{PACKAGE}.receiver.proj inline Version, Directory.Packages.local.props, "
                 f"Directory.Packages.props). Either this repo takes the kit some way this sweep does "
                 f"not know how to read, or it declares 'receives: coordination-kit' and pins nothing. "
                 f"Both are unanswered, and neither is 'current'.")
        elif len(distinct) > 1:
            emit("refusal", repo,
                 f"{PACKAGE} is pinned to more than one version in this repo ({where}). The effective "
                 f"pin is a restore-order accident, so no single version can be graded.")
        else:
            pin = literals[0][1]
            try:
                behind = fsgg_feed.parse_version(pin) < fsgg_feed.parse_version(published)
                ahead = fsgg_feed.parse_version(pin) > fsgg_feed.parse_version(published)
            except fsgg_feed.GateError as e:
                # Covers an MSBuild property (`$(FsggKitVersion)`), a floating version (`0.8.*`) and
                # a range (`[0.8.0,)`) as well as outright garbage. All of them mean the same thing
                # here — this sweep cannot say what version is restored — and all of them refuse
                # rather than pass. Resolving a property against the file's own PropertyGroup would
                # narrow this and is worth doing; it is NOT done here, and this message says so
                # instead of pretending the shape is malformed.
                emit("refusal", repo,
                     f"{PACKAGE} pin {pin!r} ({where}) is not a single literal NuGet version this "
                     f"sweep can order ({e}). An MSBuild property, a floating version or a range "
                     f"reaches this branch: the pin may well be fine, but its effective value is "
                     f"not readable from the file, so nothing is asserted about it.")
                continue
            if behind:
                emit("finding", repo,
                     f"pins {PACKAGE} {pin} ({where}) but {published} is published on nuget.org. The "
                     f"receiver's materialized kit is whatever {pin} shipped, so coordination-coherence "
                     f"will red on its `main` the next time it pushes — and reads green until then.")
                # A MACHINE-READABLE twin of the sentence above, and the whole input to the bump-offer
                # sweep (#1768). It carries the pin PATH as well as the version because that sweep
                # re-reads the same file at a candidate bump branch's head, and re-deriving which of
                # the three shapes this repo uses would be a second copy of the rule above — the thing
                # #1522 exists to stop. It is not printed: the shell's report loop cases on the kinds
                # it knows and ignores the rest, exactly as it already does for `published`.
                emit("behind", repo, pin, published, literals[0][0])
            elif ahead:
                emit("finding", repo,
                     f"pins {PACKAGE} {pin} ({where}), which is AHEAD of the newest published {published}. "
                     f"No receiver can restore that version; this pin does not resolve.")
            else:
                emit("ok", repo, f"pins {PACKAGE} {pin} ({where})")

print("\n".join(out))
PY
)

# --- THE VIEW-ROOT GENERATE SWEEP (#1759) --------------------------------------------------------
#
# WHAT IT ASSERTS. A receiver whose `.config/kit/FS.GG.Kit.receiver.proj` declares a non-empty
# `<FsggKitViewSkillRoots>` also declares a target that GENERATES that view, ordered to run BEFORE
# `FsggKitCheckSkillView`. A receiver that declares the root and not the generate reds HERE.
#
# WHY IT EXISTS, AND WHY IT IS THIS SCRIPT'S JOB. A view root is untracked and git-ignored by
# construction (ADR-0067 §6), so it is ABSENT in every fresh checkout. `FsggKitCheckSkillView` runs
# on every `FsggKitMaterialize`, and the kit does not establish its own precondition (#1710) — so
# the generate is receiver-side wiring, hand-copied into seven repositories, with nothing comparing
# them. Drop it from one receiver and NOTHING says so until that repo's next Renovate kit bump reds
# on a tree nobody touched.
#
# THE GATE THAT WOULD OTHERWISE FIND IT CANNOT BE REACHED FROM THE RECEIVER. `kit-materialize.yml`
# is a `uses:` of this repo's reusable workflow: it checks the CALLER out and runs the materialize
# there, and a caller cannot add a step to a callee. That is blocker B5's shape (#1715) on a second
# gate — the question #1759 was filed to answer. Measured 2026-07-28 on a bare clone of all seven
# receivers' `main`: every one is GREEN, and green *because* each carries its own
# `Fsgg<Repo>GenerateSkillView`. Delete that one target from a copy of the tree and the same command
# reds with `view skill root '.agents/skills' is ABSENT or a DANGLING link`. So the affected set is
# empty TODAY and is held empty by seven independent hand-copies — which is a fact about seven
# commits, not an invariant, until something sweeps it. This is that something.
#
# NOT A SECOND OPINION ON THE MATERIALIZE. `FsggKitCheckSkillView` grades the view's CONTENT, in the
# receiver's own CI, once that receiver pushes. This grades whether the receiver can ever reach that
# assertion green from a cold checkout, from here, with no push from anybody — the same f(roster)
# argument the kit-pin sweep is built on, one subject across.
#
# THE ACCEPTED SHAPE IS `BeforeTargets` NAMING THE ASSERTION, and a generate ordered some other way
# is REFUSED rather than passed. All seven receivers spell it `BeforeTargets="FsggKitCheckSkillView"`
# today. A target that runs `skill-view generate` under a different ordering may well be correct —
# but `FsggKitCheckSkillView` is itself `AfterTargets="FsggKitMaterialize"`, so two sibling
# `AfterTargets` run in declaration/import order and this sweep cannot say from the file alone which
# wins. "I cannot grade this ordering" is not "this ordering is fine" (#266) and it is not "this
# receiver forgot" (#320), so it gets the refusal code, exactly as an ungradeable pin does.
#
# A RECEIVER THAT DECLARES NO VIEW ROOT IS `none`, NEVER `ok`. It is outside this sweep's subject —
# there is no view to generate — and counting it as a pass is how a sweep over an empty set comes to
# print a clean bill. The terminal line below is conditional on somebody actually being graded.
#
# Argv: the staging dir and the kit-pin manifest (this sweep re-reads it; see the driver for why).
# Stdout: tab-separated ledger records, one per line.
VIEWGEN_PY=$(cat <<'PY'
import os
import re
import sys
import xml.etree.ElementTree as ET

RECEIVER_PROJ = ".config/kit/FS.GG.Kit.receiver.proj"
ASSERT_TARGET = "FsggKitCheckSkillView"
VIEW_PROP = "FsggKitViewSkillRoots"

out = []


def emit(kind, *fields):
    out.append("\t".join([kind, *[str(f).replace("\t", " ").replace("\n", " ") for f in fields]]))


def localname(tag):
    # MSBuild files may or may not carry the 2003 namespace; both spellings are live in this org.
    return tag.rsplit("}", 1)[-1]


def names_in(attr):
    """The target names in a Before/AfterTargets attribute — semicolon-separated, space-tolerant."""
    return [n.strip() for n in (attr or "").split(";") if n.strip()]


staging, manifest = sys.argv[1], sys.argv[2]

# One row per package receiver, in roster order. The manifest carries every pin candidate; only the
# receiver project can declare a view root, so the rest are skipped — but a repo that appears with
# NO receiver-project row still needs a verdict, or a receiver that stopped shipping the file would
# vanish from this sweep instead of being answered.
seen, order, proj_of = set(), [], {}
with open(manifest, encoding="utf-8") as fh:
    for line in fh:
        line = line.rstrip("\n")
        if not line:
            continue
        repo, repopath, local = line.split("\t", 2)
        if repo not in seen:
            seen.add(repo)
            order.append(repo)
        if repopath == RECEIVER_PROJ and local != "-":
            proj_of[repo] = os.path.join(staging, local)

for repo in order:
    local = proj_of.get(repo)
    if local is None:
        emit("none", repo,
             f"ships no {RECEIVER_PROJ}, so it declares no view skill root and there is nothing to "
             f"assert here. (Whether a coordination-kit package receiver SHOULD ship one is the "
             f"kit-pin sweep's question, not this one.)")
        continue
    try:
        with open(local, encoding="utf-8") as fh:
            text = fh.read()
    except (OSError, UnicodeDecodeError) as e:
        # A staged file we cannot decode is a file we did not really read. Retryable in the same
        # sense as any other unread evidence, and emphatically not a verdict.
        emit("undetermined", repo,
             f"staged {RECEIVER_PROJ} unreadable: {type(e).__name__}: {e}")
        continue
    try:
        root = ET.fromstring(text)
    except ET.ParseError as e:
        emit("refusal", repo,
             f"{RECEIVER_PROJ} is not parsable XML ({e}), so neither its view-root declaration nor "
             f"its generate target could be read. Unparsable is not 'declares no view root'.")
        continue

    # The declaration. An EMPTY element is how a receiver says "no view root" — the kit's own
    # `Condition` guards on the property being non-empty, so an empty one is not a declaration and
    # must not be graded as one.
    declared = []
    for el in root.iter():
        if localname(el.tag) != VIEW_PROP:
            continue
        declared.extend(r.strip() for r in (el.text or "").split(";") if r.strip())
    if not declared:
        emit("none", repo,
             f"declares no non-empty <{VIEW_PROP}>, so it has no generated view root and this sweep "
             f"asserts nothing about it. That is not a clean bill — see #1710 piece 2 for who owns "
             f"a root LEAVING the contract.")
        continue
    roots = " ".join(declared)

    ordered, unordered = [], []
    for el in root.iter():
        if localname(el.tag) != "Target":
            continue
        name = (el.get("Name") or "?").strip()
        if ASSERT_TARGET in names_in(el.get("BeforeTargets")):
            ordered.append(name)
            continue
        # Does it even claim to generate? Read the target's own text, so a target that merely
        # mentions the tool in an attribute elsewhere is not counted as one that runs it.
        body = " ".join(
            " ".join(filter(None, [c.get("Command") or "", c.text or ""])) for c in el.iter()
        )
        if re.search(r"skill-view\b[^\n]*\bgenerate\b", body):
            unordered.append(name)

    if ordered:
        emit("ok", repo,
             f"declares <{VIEW_PROP}>{roots}</{VIEW_PROP}> and generates it in "
             f"{', '.join(ordered)} (BeforeTargets={ASSERT_TARGET})")
    elif unordered:
        emit("refusal", repo,
             f"declares <{VIEW_PROP}>{roots}</{VIEW_PROP}> and has a target that runs "
             f"`skill-view generate` ({', '.join(unordered)}), but it is NOT ordered "
             f"BeforeTargets={ASSERT_TARGET}. {ASSERT_TARGET} is itself AfterTargets=FsggKitMaterialize, "
             f"so two sibling AfterTargets run in declaration order and this sweep cannot say from "
             f"the file which wins. The ordering may be correct; nothing is asserted about it.")
    else:
        emit("finding", repo,
             f"declares <{VIEW_PROP}>{roots}</{VIEW_PROP}> but NO target generates it before "
             f"{ASSERT_TARGET}. A view root is untracked and git-ignored (ADR-0067 §6), so it is "
             f"absent in every fresh checkout: `dotnet build {RECEIVER_PROJ} -t:FsggKitMaterialize` "
             f"reds with \"view skill root is ABSENT or a DANGLING link\". That command is what this "
             f"repo's next Renovate kit bump runs under kit-materialize.yml — a `uses:` of a "
             f"reusable workflow, which checks this repo out and to which this repo CANNOT add a "
             f"generate step (#1715 blocker B5, #1759). The fix is a target in {RECEIVER_PROJ}: "
             f"<Target Name=\"Fsgg…GenerateSkillView\" BeforeTargets=\"{ASSERT_TARGET}\" "
             f"Condition=\"'$({VIEW_PROP})' != ''\"> running `scripts/skill-view generate`.")

print("\n".join(out))
PY
)

# --- THE ABSENCE-COVER SWEEP (HISTORICAL NAME; #1785/#1869) -------------------------------------
#
# WHAT IT ASSERTS. For every coordination-kit package receiver: the roster's `absence-cover:` word
# for that repo is what its REAL branch protection plus its REAL committed workflows say. The word
# has two legal values and they are a statement about strength:
#
#   required    at least one context the DEFAULT BRANCH REQUIRES is produced by a job that reaches an
#               unexcused view-root assertion or materialize path.
#   unrequired  such a path runs, but no REQUIRED context produces it.
#
# The third state is DERIVED ONLY, never declarable: nothing in the repo runs that path at all.
#
# WHY THE NAME NO LONGER DESCRIBES THE SUBJECT (#1869). On the receiver path,
# `Fsgg<Repo>GenerateSkillView` runs `BeforeTargets="FsggKitCheckSkillView"`: absent and dangling
# roots are repaired before the assertion, and a text-file root makes generation refuse first.
# Therefore none of §8's three absence classes can be the receiver assertion's verdict. Their
# mutation-proven home is FS.GG.Kit's no-generate `verify-package.sh` fixtures. This sweep still does
# useful work: it establishes whether a detected unexcused view-root assertion/materialize path is
# branch-required. The separate generate sweep proves a generator is declared and ordered in the
# receiver project; a direct bare `scripts/skill-view check` does not prove generation ran in its job.
# The field and command keep their historical name for schema/CLI compatibility.
#
# IT IS ASKED OF ALL SEVEN because all seven own a receiver assertion/materialize path. The separate
# view-root generate sweep above proves the receiver project declares and orders a generator; this
# sweep joins assertion/materialize job names to live branch protection. Neither sweep claims a bare
# direct check co-runs generation or that the receiver assertion can emit an absence-class verdict.
#
# WHY NOT `check-required-contexts.py`. That gate asks whether every required context is PRODUCIBLE.
# This asks whether a particular THING a context does is still done by a required one. Different
# question, same two API stores — so the STORES are imported from it rather than restated, exactly as
# the sparse-closure rule is (#1529/#1522). `required_contexts()` reads classic protection AND
# rulesets and returns their UNION, because GitHub enforces both and reading one is the vacuous green
# #574 paid for. Its naming primitives are borrowed too, so "what does GitHub call this job" has one
# implementation in this org.
#
# WHAT IT CANNOT SEE, SAID OUT LOUD RATHER THAN PAPERED OVER:
#   - Inside a script a workflow SHELLS OUT to. Templates' invocation lives in
#     `tests/composition/run.sh`; this sweep sees the `run:` block, not the file it runs. So a
#     receiver whose only assertion is inside a called script derives `none` and reds HERE — a
#     false finding that a human resolves in one read.
#   - Inside a composite action (`uses: ./.github/actions/…`). Same shape, same direction.
#   - Whether a step's `if:` actually evaluates true on a given pull request. Templates' composition
#     step is guarded by a docs-only scope gate, so its lane really is absent on a docs-only PR;
#     nothing here models that, and #1508's path-filter reasoning is not re-derived either.
#   - Whether the assertion is REACHED on every path through a job that does run it.
#   - A reusable workflow OTHER than this authority's `kit-materialize.yml`. A receiver that moved
#     its materialize into some third-party callee derives `none` and reds — again the safe way.
#
# Argv: the required-context gate (imported, not restated), the staged-workflow dir, and the
# roster manifest `<repo>\t<declared cover, or "-">`.
# Stdout: tab-separated ledger records, one per line.
ABSENTOK_PY=$(cat <<'PY'
import importlib.util
import os
import re
import sys

RULE, STAGING, MANIFEST, AUTHORITY_WF = sys.argv[1], sys.argv[2], sys.argv[3], sys.argv[4]

# By PATH, not by name: the module's filename has hyphens. Loading it does not run its main() — that
# is guarded by `if __name__ == "__main__"`.
spec = importlib.util.spec_from_file_location("required_contexts_gate", RULE)
gate = importlib.util.module_from_spec(spec)
spec.loader.exec_module(gate)

# THE BORROWED SURFACE, NAMED — because importing across a file boundary makes these an interface,
# and an interface nobody wrote down is one the next refactor is entitled to break (#1599). A missing
# name is a PERMANENT no-verdict here, never a sweep that quietly grades nothing.
BORROWED = ("required_contexts", "load_yaml", "jobs_of", "display_name", "matrix_suffixes",
            "GateError", "Unreachable", "Missing", "Forbidden")
_absent = [n for n in BORROWED if not hasattr(gate, n)]
if _absent:
    sys.stderr.write(
        "absence-cover: the required-context gate no longer exports " + ", ".join(_absent) +
        " — the union-of-both-protection-stores read and GitHub's job-naming rule are IMPORTED from "
        "it, never restated here (#1529/#1522/#1785). Nothing was graded.\n")
    raise SystemExit(3)

BRANCH = "main"
AUTHORITY = "FS-GG/.github"
MATERIALIZE_WF = "kit-materialize.yml"

# What a `run:` block has to contain for its job to REACH the kit's view-root assertion. The first
# two are MSBuild targets: `FsggKitCheckSkillView` is `AfterTargets=FsggKitMaterialize`, so either
# spelling arrives at the same assertion, and neither can carry `--absent-ok` (that is a property of
# the tool invocation, not of the target).
TARGET_RE = re.compile(r"-t:Fsgg(?:KitMaterialize|KitCheckSkillView)\b")
CHECK_RE = re.compile(r"skill-view\b[^\n]*\bcheck\b")
ABSENT_RE = re.compile(r"--absent-ok\b")
# `uses: FS-GG/.github/.github/workflows/kit-materialize.yml@<ref>`, quoted or not — the quoting
# blind spot #503 paid for, on this reader too.
USES_RE = re.compile(
    r"^[\"']?" + re.escape(AUTHORITY) + r"/\.github/workflows/(?P<file>[^/@\"']+)@")

out = []


def emit(kind, *fields):
    out.append("\t".join([kind, *[str(f).replace("\t", " ").replace("\n", " ") for f in fields]]))


def uncommented(text):
    """A `run:` block with its whole-line shell comments removed.

    Load-bearing, not tidiness. FS.GG.Audio's `gate.yml` explains in comments beside the excused
    step that NO required context here runs `-t:FsggKitMaterialize` — and those comments contain the
    literal target name. Reading them as invocations would derive `required` for the one receiver
    whose whole point is that it is NOT, i.e. it would certify the excuse by reading the sentence
    that admits it. A trailing `foo # …` comment on a live line is still counted; that is a known
    over-read in the direction of a false RED.
    """
    return "\n".join(ln for ln in text.splitlines() if not ln.lstrip().startswith("#"))


# A quoted span in a `run:` block. Blanked before anything is read as an INVOCATION, because in this
# subject a quoted string is overwhelmingly likely to be the `--absent-ok` REASON — and the reason is
# a sentence ABOUT the very targets this sweep looks for.
QUOTED_RE = re.compile(r"\"(?:[^\"\\]|\\.)*\"|'(?:[^'\\]|\\.)*'", re.S)


def unquoted(text):
    """A `run:` block with every quoted string blanked, so PROSE cannot be read as a command.

    THIS IS THE WHOLE ITEM, INSIDE THE CHECK FOR IT. FS.GG.Audio's `--absent-ok` reason reads, in
    full and on one line: "NO required context on this repo runs -t:FsggKitMaterialize …". The first
    cut of this sweep matched that literal, concluded that Audio's required `Build + test (locked
    restore, net10.0, headless)` job runs the materialize, and derived `required` for the one
    receiver whose entire point is that it is NOT — certifying the excuse by reading the sentence
    that denies it. That is the same defect as #1785 itself, one level in, and it is why this check
    reads prose NOWHERE: not the reason, not a comment, not a quoted argument.

    The cost is a known over-strip in the false-red direction: an invocation hidden inside a quoted
    `bash -c "…"` is not seen, so its job does not count as covering and the receiver derives a
    WEAKER cover than it has — a false RED a human clears in one read, never a false green.
    """
    return QUOTED_RE.sub(" ", text)


def callee_jobs(filename, subject):
    """The job display names inside one of THIS AUTHORITY's reusable workflows."""
    path = os.path.join(AUTHORITY_WF, filename)
    with open(path, encoding="utf-8") as fh:
        doc = gate.load_yaml(fh.read(), f"{AUTHORITY}/.github/workflows/{filename}")
    return [gate.display_name(jid, j) for jid, j in gate.jobs_of(doc, subject).items()]


def job_contexts(job_id, job, subject):
    """What GitHub calls this job's check run(s) — the shared naming rule, applied per job.

    A job that CALLS a reusable workflow produces one context per callee job, named
    `<caller display> / <callee job display>`; every other job produces its own display name. Matrix
    suffixes come from the caller either way.
    """
    display = gate.display_name(job_id, job)
    suffixes = gate.matrix_suffixes(job, subject)
    uses = job.get("uses")
    if isinstance(uses, str):
        m = USES_RE.match(uses.strip())
        if not m:
            # Some other callee. We do not hold it, so we cannot name its jobs — and a job whose
            # contexts we cannot name must not be counted as covering anything.
            return None
        return [f"{display}{s} / {c}" for s in suffixes for c in callee_jobs(m.group("file"), subject)]
    return [f"{display}{s}" for s in suffixes]


def grade_repo(repo, declared):
    slug = repo.replace("/", "__")
    wfdir = os.path.join(STAGING, slug)
    if not os.path.isdir(wfdir):
        emit("undetermined", repo,
             "no workflow was staged for this receiver, so its unexcused view-root assertion or "
             "materialize path could not be read. That is not 'this repo has no such path'.")
        return

    covering, excused = [], []
    for name in sorted(os.listdir(wfdir)):
        subject = f"{repo} .github/workflows/{name}"
        try:
            with open(os.path.join(wfdir, name), encoding="utf-8") as fh:
                doc = gate.load_yaml(fh.read(), subject)
            jobs = gate.jobs_of(doc, subject)
        except gate.GateError as e:
            # A workflow that will not parse, or declares no jobs, is not evidence that this repo
            # has no detected path. Refuse the repo rather than grade it on the files that did parse.
            emit("refusal", repo,
                 f"{subject} could not be read ({e}), so this receiver's assertion/materialize jobs "
                 f"cannot be enumerated. Unparsable is not 'has no detected path'.")
            return
        for job_id, job in jobs.items():
            steps = job.get("steps")
            is_covering = is_excused = False
            if isinstance(job.get("uses"), str):
                m = USES_RE.match(job["uses"].strip())
                is_covering = bool(m) and m.group("file") == MATERIALIZE_WF
            for step in steps if isinstance(steps, list) else []:
                if not isinstance(step, dict):
                    continue
                run = step.get("run")
                if not isinstance(run, str):
                    continue
                body = uncommented(run)
                # The FLAG is read on the commented-out-but-still-quoted body — `--absent-ok` itself
                # is never inside quotes, only its reason is. Everything read as an INVOCATION is
                # read with the quotes blanked; see unquoted() for the bug that rule exists for.
                excuse_here = bool(ABSENT_RE.search(body))
                body = unquoted(body)
                if TARGET_RE.search(body):
                    is_covering = True
                elif CHECK_RE.search(body):
                    if excuse_here:
                        is_excused = True
                    else:
                        is_covering = True
            if not (is_covering or is_excused):
                continue
            try:
                names = job_contexts(job_id, job, f"{subject} [{job_id}]")
            except gate.GateError as e:
                emit("refusal", repo,
                     f"{subject} [{job_id}] reaches the view-root assertion but its check-run "
                     f"name(s) cannot be derived ({e}). A guessed context that happened to be "
                     f"required would be a vacuous green over an unguarded repo.")
                return
            except OSError as e:
                emit("refusal", repo,
                     f"{subject} [{job_id}] calls a reusable workflow this authority should hold "
                     f"and does not ({e}), so its contexts cannot be named.")
                return
            if names is None:
                continue
            (covering if is_covering else excused).extend(names)

    try:
        required = {str(c.get("context", "")) for c in gate.required_contexts(repo, BRANCH, None, None)}
    except (gate.Unreachable, gate.Missing) as e:
        emit("undetermined", repo,
             f"branch protection / rulesets on {BRANCH} would not read ({e}). #266: a protection "
             f"read that failed is UNREAD, so path requiredness is unknown.")
        return
    except (gate.Forbidden, gate.GateError) as e:
        emit("refusal", repo,
             f"branch protection / rulesets on {BRANCH} are unreadable and re-running will not "
             f"change that ({str(e).splitlines()[0]}). Reading required checks needs "
             f"`administration: read`, which is NOT grantable to a workflow GITHUB_TOKEN — the "
             f"audit must run with the org App's installation token. Nothing about whether this "
             f"detected assertion/materialize path is required was asserted.")
        return

    hits = sorted(set(covering) & required)
    if not covering:
        derived = "none"
    elif hits:
        derived = "required"
    else:
        derived = "unrequired"

    where = f"unexcused assertion/materialize path on: {', '.join(sorted(set(covering))) or '(nothing)'}"
    if excused:
        where += f"; --absent-ok-only lane on: {', '.join(sorted(set(excused)))}"

    if derived == "none":
        emit("finding", repo,
             f"NOTHING in this receiver runs the kit's view-root assertion without `--absent-ok`. "
             f"No unexcused view-root assertion/materialize path was found. The roster says "
             f"`absence-cover: {declared or '(unset)'}` (historical field name). {where}. "
             f"Restore a lane that reaches the unexcused assertion or materialize path.")
        return
    if not declared:
        emit("finding", repo,
             f"declares no `absence-cover:` in registry/repos.yml, so whether its unexcused "
             f"view-root assertion/materialize path is required is unstated. Derived from live protection today: "
             f"{derived}"
             + (f" (required context(s): {', '.join(hits)})" if hits else "") +
             f". Add `absence-cover: {derived}` to this repo's roster row — the word is what makes "
             f"the claim re-checkable tomorrow (#1785).")
        return
    if declared == derived:
        emit("ok", repo,
             f"absence-cover: {derived}"
             + (f" — required context(s) running an unexcused assertion/materialize path: {', '.join(hits)}"
                if hits else " — no REQUIRED context runs it; " + where))
        return
    if declared == "required" and derived == "unrequired":
        emit("finding", repo,
             f"the roster says `absence-cover: required` and live branch protection says otherwise: "
             f"the gate still runs ({where}), but NO context this branch REQUIRES produces it. "
             f"The detected path is weaker than the roster claims: the unexcused view-root "
             f"assertion/materialize path no longer blocks a merge. Put it back on a required "
             f"context, or change the roster word.")
        return
    emit("finding", repo,
         f"the roster says `absence-cover: {declared}` and live branch protection says `{derived}` "
         f"(required context(s) running an unexcused assertion/materialize path: {', '.join(hits) or 'none'}). "
         f"The detected path is required more strongly than the roster claims, but the roster word is what "
         f"the next reader trusts and it is wrong. Update the row.")


with open(MANIFEST, encoding="utf-8") as fh:
    for line in fh:
        line = line.rstrip("\n")
        if not line:
            continue
        repo, declared = line.split("\t", 1)
        grade_repo(repo, "" if declared == "-" else declared)

print("\n".join(out))
PY
)

# --- THE BUMP-OFFER SWEEP (#1768) ----------------------------------------------------------------
#
# WHAT IT ASSERTS. For every receiver the kit-pin sweep just found BEHIND, this says whether a bump to
# the published kit was ever OFFERED — and if one was, whether it offers the version that is actually
# published. Four terminal states, and the remedy for each names a different human action taken by a
# different person.
#
# WHY IT EXISTS. The kit-pin sweep says "receiver R is behind". That one sentence is equally true when
# Renovate proposed a bump and nobody merged it, and when Renovate never proposed anything at all —
# and those need opposite actions. On 2026-07-28 both halves failed on the same morning and neither
# was reported anywhere:
#
#   * FS.GG.Audio and FS.GG.Net had not re-extracted since the #1580 preset fix. Renovate here is
#     predominantly PUSH-triggered and neither repo had been pushed, so neither would ever have
#     proposed. Repaired by hand-ticking each Dependency Dashboard's `<!-- manual job -->` box.
#   * FS.GG.Net's bump was then held by a rate limit: the branch existed, the PR did not, and the
#     dashboard carried `- [ ] <!-- unlimit-branch=renovate/fs.gg.kit-0.x -->`. Ticking that box
#     produced FS.GG.Net#42 four minutes later.
#
# Both states are, from outside, indistinguishable from "this receiver is current" — a bump that is
# never proposed produces no PR, no check, no notification and no row. That is #1533's class exactly:
# a failure whose symptom is a NON-EVENT. Today they were told apart only because a human opened two
# dashboards and read them.
#
# AND THE THIRD MODE, WHICH WAS STRUCTURAL RATHER THAN INCIDENTAL (#1761) AND IS NOW CLOSED (#1923).
# Neither `release-coord-engine.yml` nor `release-kit.yml` calls `dispatch-sender.yml`, and they still
# do not — that mechanism was measured and REFUSED in #1776 (no receiver listens; Renovate is the
# hosted Mend App and a dispatch cannot make it re-scan), and the record is release-kit.yml's header.
# What was missing was not a dispatch; it was ANY push half at all, so CUTTING A RELEASE NOTIFIED
# NOBODY and Renovate's own schedule was the only path.
#
# Both release workflows now run `scripts/dashboard-tick.py` after a successful publish. It ticks the
# receiver's Dependency Dashboard box for the package just published — `unlimit-branch=<branch>` when
# a rate limit holds it, `manual job` when the bot has not re-extracted since — over the roster, and
# it goes RED when a release reaches zero receivers, when a write is refused, or when a `PATCH`
# returns 200 and changes nothing.
#
# THE BEFORE, SO THE AFTER IS CHECKABLE RATHER THAN ASSERTED (#1923 AC5). Measured 2026-07-29 by
# `dashboard-tick.py --dry-run`, over all seven `receives: coordination-kit` + `kit-delivery: package`
# receivers, against the published FS.GG.Kit 0.21.0 and FS.GG.Coord.Cli 0.15.0:
#
#     FS.GG.Kit 0.21.0        SDD ok · Templates ok · Game ok · Audio ok
#                             Rendering#14 HELD  (unlimit-branch=renovate/fs.gg.kit-0.x, unticked)
#                             Net#36       HELD  (unlimit-branch=renovate/fs.gg.kit-0.x, unticked)
#                             Governance#21 BLIND (0.21.0 appears nowhere; pinned 0.19.1)
#     FS.GG.Coord.Cli 0.15.0  SDD ok · Templates ok · Game ok · Audio ok
#                             Net#36       HELD  (unlimit-branch=renovate/fs.gg.coord.cli-0.x)
#                             Rendering#14 BLIND · Governance#21 BLIND
#
# That is delivery at 4 of 7 for each package, with three receivers per package sitting behind a box
# nobody was watching — and the two failure SHAPES the #1768 sweep already names (`offer-none` from a
# repo that never re-extracted, a rate-limited branch that never became a PR) turning out to be
# exactly the two boxes the ticker knows how to press. What the ticker must show is those columns
# collapsing to `offer-current` after the NEXT release of each package. It is not shown yet: nothing
# has been published since it was wired, and asserting the improvement before observing it is the
# thing this sweep exists to refuse.
#
# WHAT THE TICKER STILL CANNOT SEE, so this sweep is not retired by it: a tick that LANDS and a
# Renovate run that then does NOTHING are different failures, and only this sweep can see the second.
# This sweep is f(roster, feed, each receiver's PR list) — like the kit-pin sweep it rides, it needs
# no push from anybody, which is the only reason it can see a mode whose whole nature is that no
# event occurs.
#
# A SUPERSEDED OFFER IS NOT AN OFFER. This is the distinction the sweep would be worthless without,
# and it is not hypothetical: measured 2026-07-28, FS.GG.SDD#773, FS.GG.Rendering#1123,
# FS.GG.Governance#335 and FS.GG.Audio#214 were all open, all named FS.GG.Kit 0.15.1, and the
# published kit was 0.16.0. A check that reported those four as "has a bump" would be reporting a
# green nobody earned: merging all four leaves every one of them behind. So the offered version is
# compared to the published one, and an offer below it is its own state with its own remedy.
#
# THE OFFERED VERSION IS READ FROM THE BRANCH, NEVER FROM THE TITLE. `chore(deps): update dependency
# fs.gg.kit to 0.15.1` is prose Renovate composes and a human may edit; the pin file at the PR's head
# is what would actually land. So the same pin file the kit-pin sweep graded on `main` is re-read at
# the head ref and parsed by the SAME `versions_in` rule — no second copy of "what is a pin", for the
# reason #1522 exists, and no way for a retitle to move this verdict.
#
# A BRANCH IS ONLY AN OFFER IF IT IS AHEAD OF `main`. Renovate does not delete a merged branch here,
# so "a branch exists and no PR does" has TWO causes and only one of them is the rate limit. Measured
# 2026-07-28: FS.GG.Game carried `renovate/fs.gg.kit-0.x` with no open PR, and that branch pinned
# 0.15.1 — exactly what Game's own `main` pinned, because its PR had already merged. Reporting that as
# "rate-limited, go tick unlimit-branch" would have sent someone to a dashboard to tick a box that was
# not there. So a branch counts as a held offer only when its pin is STRICTLY AHEAD of the pin on
# `main`; a branch at or below it is a leftover and says nothing.
#
# WHAT IT CANNOT SEE, WRITTEN DOWN RATHER THAN PAPERED OVER (#266). Renovate's scheduling is not
# observable from this side, and this sweep does not pretend otherwise:
#
#   * WHETHER RENOVATE HAS RUN. Nothing here reads the Dependency Dashboard's timestamp or the bot's
#     job log. "No open PR and no branch ahead" is the same observation for "the dashboard is stale
#     and never re-extracted" (mode 1) and "a release happened and nothing told anyone" (mode 3). The
#     sweep reports them as ONE state, `offer-none`, and its remedy names the tick that repairs both —
#     because the same tick does repair both. It does NOT claim to know which one it is looking at.
#   * WHEN THE NEXT RUN IS. Not observable at all. A receiver reported `offer-none` may have a bump
#     proposed a minute later. This is a report about NOW, and re-running it is the only way to know.
#   * A DELIBERATELY CLOSED BUMP. Closing a Renovate PR adds the dep to the dashboard's ignore list.
#     From here that is indistinguishable from never having been offered one, and the remedy differs
#     (untick the ignore box, not the manual-job box). The `offer-none` text says so rather than
#     asserting the cause it did not check — the #566 discipline, one gate over.
#   * A NON-RENOVATE BUMP IN FLIGHT. A hand-authored PR that moves the pin is FOUND (the head ref is
#     read the same way), but a bump sitting in somebody's local checkout is not a thing this or any
#     other check can see.
#
# THE REMEDY MUST NAME THE HUMAN ACTION. Both of today's repairs were a single checkbox tick that
# nobody knew to make. A check that reports a problem and leaves the reader to work out what to do
# recreates the non-event it was built to end, so every state below names the box AND the issue it is
# on. `- [ ] <!-- manual job -->` and `- [ ] <!-- unlimit-branch=… -->` are Renovate's own markers and
# are quoted verbatim, because that is the string the reader will be searching the dashboard for.
#
# Argv: the staging dir and its manifest. Stdout: tab-separated ledger records, one per line.
OFFER_PY=$(cat <<'PY'
import json
import os
import sys
import xml.etree.ElementTree as ET

sys.path.insert(0, os.environ["FSGG_SCRIPTS_DIR"])
import fsgg_feed  # noqa: E402

PACKAGE = "FS.GG.Kit"
DASHBOARD = "Renovate's Dependency Dashboard issue"

out = []


def emit(kind, *fields):
    out.append("\t".join([kind, *[str(f).replace("\t", " ").replace("\n", " ") for f in fields]]))


def localname(tag):
    return tag.rsplit("}", 1)[-1]


# The SAME rule the kit-pin sweep grades `main` with, so the two cannot disagree about what a pin is.
# Kept identical deliberately: if the manager's shape changes, both must move together or the sweep
# would compare a version it read one way against a version it read another.
def versions_in(text, pinpath):
    if pinpath == ".config/dotnet-tools.json":
        doc = json.loads(text)
        entry = doc.get("tools", {}).get(PACKAGE)
        if not isinstance(entry, dict):
            return []
        version = entry.get("version")
        return [version.strip()] if isinstance(version, str) and version.strip() else []
    root = ET.fromstring(text)
    found = []
    for el in root.iter():
        if localname(el.tag) not in ("PackageReference", "PackageVersion"):
            continue
        include = el.get("Include") or el.get("Update") or ""
        if include.strip().lower() != PACKAGE.lower():
            continue
        override = el.get("VersionOverride")
        if override is not None and override.strip():
            found.append(override.strip())
            continue
        version = el.get("Version")
        if version is None:
            child = next((c for c in el if localname(c.tag) == "Version"), None)
            version = child.text if child is not None else None
        if version is None or not version.strip():
            continue
        found.append(version.strip())
    return found


def read(path):
    with open(path, encoding="utf-8") as fh:
        return fh.read()


staging, manifest = sys.argv[1], sys.argv[2]

# One record per BEHIND receiver, written by the shell: what it pins, what is published, where its pin
# lives, and the staged evidence about its open bumps.
with open(manifest, encoding="utf-8") as fh:
    rows = [line.rstrip("\n").split("\t") for line in fh if line.strip()]

for repo, pin, published, pinpath, slug, package in rows:
    PACKAGE = package
    base = os.path.join(staging, slug)

    # An unread PR list is NOT "no bump was offered". It is a failure to read, and the one thing this
    # sweep must never do is turn a lost API call into the very state it exists to report — that is
    # #266's defect wearing this sweep's clothes.
    err = os.path.join(base, "err")
    if os.path.exists(err):
        emit("offer-undetermined", repo,
             f"could not read this repo's open pull requests or branches ({read(err).strip()}), so "
             f"nothing is known about whether a {PACKAGE} bump was offered. It is behind either way — "
             f"the kit-pin sweep said so — but WHICH remedy applies is unanswered, and 'no bump was "
             f"offered' is emphatically not the safe guess.")
        continue

    # Both were normalized to `<key>\t<ref>\t<slug>` by the shell, which is also where a body of the
    # wrong shape was already turned into the `err` above. Reaching here means both parsed.
    def rows(name):
        path = os.path.join(base, name)
        if not os.path.exists(path):
            return []
        return [ln.split("\t") for ln in read(path).splitlines() if ln.strip()]

    try:
        prs = rows("prs.tsv")
        branches = rows("branches.tsv")
    except (OSError, UnicodeDecodeError) as e:
        emit("offer-undetermined", repo,
             f"the staged pull-request/branch evidence would not read ({type(e).__name__}: {e}), so "
             f"nothing is known about whether a {PACKAGE} bump was offered.")
        continue

    # A candidate is any open PR whose HEAD we managed to read a kit pin from — the shell stages one
    # `head/<n>` blob per open PR whose head ref looks like it could carry one. Reading the pin is
    # what makes it a candidate; the title is never consulted.
    offers = []          # (pr_number, offered_version, head_ref)
    unreadable = []
    for num, ref, _refslug in prs:
        blob = os.path.join(base, "head", num)
        if not os.path.exists(blob):
            continue
        try:
            found = versions_in(read(blob), pinpath)
        except (ET.ParseError, json.JSONDecodeError, OSError, UnicodeDecodeError) as e:
            unreadable.append(f"#{num} ({type(e).__name__})")
            continue
        if not found:
            continue
        offers.append((num, found[0], ref))

    try:
        published_v = fsgg_feed.parse_version(published)
        pin_v = fsgg_feed.parse_version(pin)
    except fsgg_feed.GateError as e:
        emit("offer-undetermined", repo,
             f"cannot order this repo's pin {pin!r} against the published {published!r} ({e}), so an "
             f"offer cannot be graded against either.")
        continue

    def ordered(v):
        try:
            return fsgg_feed.parse_version(v)
        except fsgg_feed.GateError:
            return None

    # An offer AT the published version is the only one that makes the receiver current. Pick the
    # highest readable offer: two open bumps is not an error, and grading the lower one would report a
    # superseded state for a repo that has a current offer sitting right beside it.
    graded = [(n, v, ordered(v), b) for n, v, b in offers]
    readable = [g for g in graded if g[2] is not None]
    if readable:
        best = max(readable, key=lambda g: g[2])
        n, v, ov, _b = best
        if ov >= published_v:
            emit("offer-current", repo,
                 f"is behind ({pin}) and PR #{n} offers {PACKAGE} {v}, which is the published version. "
                 f"Nothing is wrong with the PROPOSAL step here: the bump exists and is current. "
                 f"REMEDY: review and merge {repo}#{n}. No dashboard tick is needed.")
        else:
            emit("offer-superseded", repo,
                 f"is behind ({pin}) and PR #{n} offers {PACKAGE} {v} — but {published} is published, "
                 f"so that PR is SUPERSEDED and merging it leaves this receiver behind. Renovate has "
                 f"not re-proposed since {published} was released, which is what a release that "
                 f"notifies nobody looks like from here (#1761: neither release-coord-engine.yml nor "
                 f"release-kit.yml calls dispatch-sender.yml). REMEDY: on {DASHBOARD} in {repo}, tick "
                 f"`- [ ] <!-- manual job -->` to force a re-extraction now; Renovate will retarget "
                 f"#{n} at {published} within minutes. Merging #{n} first is not wrong, it is just "
                 f"not sufficient.")
        if unreadable:
            emit("offer-undetermined", repo,
                 f"graded the offer above, but {len(unreadable)} other open PR(s) carried a "
                 f"{PACKAGE} pin this sweep could not read ({', '.join(unreadable)}). If one of those "
                 f"is the current offer, the state above is understated.")
        continue

    if unreadable:
        emit("offer-undetermined", repo,
             f"has {len(unreadable)} open PR(s) whose {PACKAGE} pin would not read "
             f"({', '.join(unreadable)}) and no readable offer, so whether a bump was offered is "
             f"unanswered. Not 'no bump': unread evidence is not an answer (#266).")
        continue

    # NO open PR offers a bump. The remaining question is whether Renovate got as far as a BRANCH and
    # was then held — the rate limit — or never proposed at all. Only a branch strictly AHEAD of the
    # pin on `main` counts; see the header on FS.GG.Game's leftover branch.
    held = []
    for _key, bname, bslug in branches:
        blob = os.path.join(base, "branch", bslug)
        if not os.path.exists(blob):
            continue
        try:
            found = versions_in(read(blob), pinpath)
        except (ET.ParseError, json.JSONDecodeError, OSError, UnicodeDecodeError):
            continue
        if not found:
            continue
        bv = ordered(found[0])
        # STRICTLY ahead of `main`, which is the whole discrimination: Renovate does not delete a
        # merged branch here, so a leftover at exactly the pin on `main` is not an offer and must not
        # be reported as a rate-limited one. See FS.GG.Game in the header.
        if bv is not None and bv > pin_v:
            held.append((bname, found[0], bv))

    if held:
        name, v, _ov = max(held, key=lambda h: h[2])
        emit("offer-ratelimited", repo,
             f"is behind ({pin}) and branch `{name}` already carries {PACKAGE} {v} — but NO open pull "
             f"request proposes it. That is Renovate having created the branch and then been held by "
             f"a rate limit, which is the FS.GG.Net failure of 2026-07-28 exactly. REMEDY: on "
             f"{DASHBOARD} in {repo}, tick `- [ ] <!-- unlimit-branch={name} -->`. The PR appears "
             f"within a few minutes. (Do NOT raise prHourlyLimit/branchConcurrentLimit as the fix "
             f"here — that is a separate decision with its own costs and it does not make this state "
             f"visible.)")
        continue

    emit("offer-none", repo,
         f"is behind ({pin}, published {published}) and NO bump has been offered at all — no open pull "
         f"request and no branch ahead of `main`. THIS IS THE STATE NOTHING ELSE IN THIS ORG REPORTS: "
         f"the freshness sweep says only that it is behind, which is equally true of a receiver whose "
         f"bump is sitting open and unmerged. REMEDY: on {DASHBOARD} in {repo}, tick "
         f"`- [ ] <!-- manual job -->` to force a re-extraction. If the dep then appears under "
         f"'Detected dependencies' but no PR follows, it is rate-limited — tick that branch's "
         f"`- [ ] <!-- unlimit-branch=… -->` box next. WHAT THIS SWEEP DID NOT CHECK: whether Renovate "
         f"has run at all since the last preset change, and whether a previous bump here was CLOSED by "
         f"a human (which adds the dep to the dashboard's ignore list and looks identical from "
         f"outside, but is repaired by unticking THAT box instead). Both reach this same state.")

print("\n".join(out))
PY
)

# --- THE ENGINE-MANIFEST SWEEP (#1615) -----------------------------------------------------------
#
# WHAT IT ASSERTS. Every repo with `receives: coordination-kit` has a `.config/dotnet-tools.json`
# that declares `fs.gg.coord.cli` with a usable version. In one sentence: *a repo that receives the
# `fsgg-coord` shim can actually run the engine the shim execs.*
#
# WHY IT EXISTS. This IS #1077's invariant. Until 2026-07-28 it was obtained by ARRANGEMENT: the
# shim and the engine manifest were both `kit:` rows, so their receiver sets were equal by
# construction and `repos.sh validate` refused to let them separate. #1615 (ADR-0068) took the
# manifest off the kit — an engine version bump was editing hashed kit content, which staled
# `registry/repos.lock`, reddened `kit-published-coherence`, and obliged a full republish plus a
# seven-receiver fan-out to move one integer in one JSON file (four of the nine republishes measured
# on 2026-07-27/28 went through that row alone). Renovate's nuget manager bumps the receivers
# directly now: `/(^|/)dotnet-tools\.json$/` is one of its four SHIPPED `managerFilePatterns`.
#
# WHY THE REPLACEMENT IS STRICTLY STRONGER, and this is the point of it rather than a consolation.
# The old rule was `f(this roster)`. It could only ever say which FABRIC two rows rode, and inferred
# the receiver property from that arrangement — so it was blind in exactly the direction that
# matters: a receiver that DELETED its own `.config/dotnet-tools.json` by hand stayed green forever,
# because nothing ever read that repo's tree. This is `f(roster, receiver tree)`. It names the hole
# ("FS.GG.Templates receives the kit and declares no engine") where the construction argument could
# only ever prevent one origin of it and say nothing when it recurred by another route.
#
# THE SUBJECT IS EVERY RECEIVER, NOT EVERY *PACKAGE* RECEIVER — deliberately, and unlike the kit-pin
# sweep beside it. That sweep narrows to `--kit-delivery package` because a byte-copy receiver
# legitimately has no `PackageReference` to grade. There is no equivalent excuse here: a byte-copy
# receiver gets the SAME `scripts/fsgg-coord` shim and needs the same engine to exec, so narrowing
# would carve the #1077 defect's two original victims back out of the check written to replace it.
#
# WHAT IT DOES NOT ASSERT, said out loud so the terminal line is not misread (#266). It does not
# grade the engine VERSION — whether a receiver's `fs.gg.coord.cli` is the newest published one is
# the kit-pin sweep's shape, one package over, and is deliberately not this sweep's question.
# #1077's invariant was never "runs the newest engine"; it was "can run the engine at all", and
# widening it here would red the whole fleet on the day the engine ships a version, which is the
# opposite of what #1615 bought.
MANIFEST_PY=$(cat <<'PY'
import json, os, sys

sys.path.insert(0, os.environ["FSGG_SCRIPTS_DIR"])
import fsgg_feed  # noqa: E402

STAGE, MANIFEST = sys.argv[1], sys.argv[2]
TOOL = "fs.gg.coord.cli"
PATH = ".config/dotnet-tools.json"

_base = os.environ.get("FSGG_NUGET_ORG_BASE", "").strip()
if _base:
    fsgg_feed.NUGET_ORG = _base.rstrip("/")

out = []
def emit(kind, *fields):
    out.append("\t".join([kind, *[str(f).replace("\t", " ").replace("\n", " ") for f in fields]]))

with open(MANIFEST, encoding="utf-8") as fh:
    rows = [ln.rstrip("\n").split("\t") for ln in fh if ln.strip()]

# This declaration sweep deliberately does NOT red merely because a receiver is behind.  It does,
# however, emit a private row for the offer sweep below: that sweep owns the question whether the
# owed bump was proposed.  fake-cli is deliberately out of scope: it is not the coordination engine
# the fsgg-coord shim executes, and no ADR assigns its delivery to this Renovate path.
try:
    versions = [v for v in fsgg_feed.nuget_org_versions(TOOL) if not fsgg_feed.is_prerelease(v)]
    published = fsgg_feed.newest(versions)
except Exception as exc:
    published = None
    emit("undetermined", "*", f"could not resolve published {TOOL}: {type(exc).__name__}: {exc}")

for row in rows:
    repo, rel = row[0], row[1]

    # `-` is the staging loop's word for "the read succeeded and the file is NOT THERE". That is an
    # ANSWER, and it is this sweep's headline finding: the repo receives the shim and holds no tool
    # manifest at all, which is precisely the state #1077 found FS.GG.Templates and FS.GG.Audio in.
    if rel == "-":
        emit("finding", repo,
             f"receives the coordination kit — so it holds the `scripts/fsgg-coord` shim — and has NO "
             f"`{PATH}` at all, so `dotnet tool restore` installs no engine and every `fsgg-coord` "
             f"invocation in that repo dies at resolution. This is #1077's ORIGINAL defect, which used "
             f"to be prevented by construction (the manifest was a kit row) and is now asserted here "
             f"instead (#1615, ADR-0068). REMEDY: add `{PATH}` declaring `{TOOL}`; copy "
             f"`dist/dotnet/.config/dotnet-tools.json` from FS-GG/.github as the starting shape.")
        continue

    with open(os.path.join(STAGE, rel), encoding="utf-8") as fh:
        raw = fh.read()

    # A manifest that will not parse is a REFUSAL, never a finding and never a pass. `dotnet tool
    # restore` would fail on it too, so the repo is certainly broken — but this sweep cannot say
    # whether the tool is DECLARED, and "I could not evaluate this" must not borrow either verdict's
    # words (#266). A re-run reproduces it, so it is a permanent no-verdict, not an undetermined.
    try:
        doc = json.loads(raw)
    except Exception as exc:
        emit("refusal", repo,
             f"`{PATH}` is not valid JSON ({exc}), so this sweep cannot tell whether `{TOOL}` is "
             f"declared in it. Nothing is asserted about this repo's engine. (`dotnet tool restore` "
             f"would also fail on this file, so it is very likely broken — but that is a different "
             f"claim from the one this sweep makes, and it is not this sweep's to make.)")
        continue

    if not isinstance(doc, dict) or not isinstance(doc.get("tools"), dict):
        emit("refusal", repo,
             f"`{PATH}` parses as JSON but has no object at `.tools`, so it is not a tool manifest "
             f"this sweep knows how to read. Nothing is asserted about this repo's engine.")
        continue

    entry = doc["tools"].get(TOOL)
    if entry is None:
        # THE OTHER HEADLINE FINDING, and the one the fabric rule could NEVER have caught: the file
        # exists, `dotnet tool restore` succeeds, and the engine simply is not in it.
        declared = ", ".join(sorted(doc["tools"])) or "nothing at all"
        emit("finding", repo,
             f"receives the coordination kit — so it holds the `scripts/fsgg-coord` shim — and its "
             f"`{PATH}` does NOT declare `{TOOL}`. It declares: {declared}. `dotnet tool restore` "
             f"succeeds and still installs no engine, so the shim dies at resolution: a tool the repo "
             f"receives and cannot run (#1077). NOTE this is the case the OLD fabric rule was blind "
             f"to — it constrained which fabric two rows rode in FS-GG/.github and never read this "
             f"file. REMEDY: add a `{TOOL}` entry with a version and the `fsgg-coord-engine` command.")
        continue

    version = entry.get("version") if isinstance(entry, dict) else None
    if not isinstance(version, str) or not version.strip():
        emit("refusal", repo,
             f"`{PATH}` declares `{TOOL}` with no usable `version` string, so this sweep cannot say "
             f"the repo can restore an engine and will not pretend the declaration alone is enough. "
             f"Nothing is asserted about this repo's engine.")
        continue

    emit("ok", repo, f"declares {TOOL} {version} in {PATH} — it can restore the engine its shim execs.")
    if published is not None:
        try:
            if fsgg_feed.parse_version(version) < fsgg_feed.parse_version(published):
                emit("engine-behind", repo, version, published, PATH)
        except fsgg_feed.GateError:
            emit("undetermined", repo, f"cannot order engine pin {version!r} against published {published!r}")

print("\n".join(out))
PY
)

repo_calls() {
  local repo="$1" f rc=0 frc files text
  local build_config_enforced=0 build_config_opted_in=0 project prc=0 project_code
  local su_verdict su_any_call=0 su_both=0
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
    files=""                              # visible, but no workflows dir: no CI enforcement.
  fi

  # THE ABSENCE-COVER SWEEP'S "I REACHED THIS REPO" MARK (#1785). Created the moment the listing is a
  # real answer — INCLUDING the answer "visible, and it has no workflows at all" — and never when the
  # listing failed, because those two must not share a verdict. An empty directory here means the
  # repo covers an absent view root with nothing, which is a FINDING; a missing directory means we
  # never read it, which is a no-verdict. Collapsing them would let an outage report a receiver as
  # unguarded, or an unguarded receiver report as an outage, and #266/#320 forbid both directions.
  mkdir -p "$ABSENTOK_DIR/${repo//\//__}"

  while IFS= read -r f; do
    [ -n "$f" ] || continue
    frc=0; text="$(get_workflow "$repo" "$f")" || frc=$?
    if [ "$frc" -eq "$RC_UNREACHABLE" ]; then
      printf 'could not determine: reading .github/workflows/%s failed — %s' "$f" "$(gh_last_err)" > "$CALLS_ERR_FILE"
      return 2
    fi
    [ "$frc" -eq 0 ] || continue          # 404: listed, then gone. It calls nothing.

    # THE SPARSE-CHECKOUT CLOSURE SWEEP (#1529) rides this pass. It is graded for EVERY rostered
    # repo, the authority included: the class is "a cross-repo checkout that under-fetches", and it
    # does not care which repository wrote the workflow. It is also deliberately OUTSIDE the
    # `$repo != $AUTHORITY` guard below — that guard exists so the authority is not counted as a
    # phantom ADOPTER of its own fabrics, which is a statement about participation and has nothing
    # to say about whether a checkout under-fetches.
    sparse_grade "$repo" "$f" <<< "$text"

    # THE ABSENCE-COVER SWEEP (#1785) rides this pass too, and stages rather than grades: its
    # question — which of this repo's JOBS reach the kit's view-root assertion un-excused, and is any
    # of their check-run names one the branch REQUIRES — needs the parsed YAML and the protection
    # API, and bash must do neither. Staging here is what keeps its API cost at exactly the two
    # protection reads per receiver: the workflow bytes are already in hand.
    mkdir -p "$ABSENTOK_DIR/${repo//\//__}"
    printf '%s' "$text" > "$ABSENTOK_DIR/${repo//\//__}/$f"
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

      # Package-delivered build config has TWO receiver-side halves, and both must live:
      # materialization alone silently rewrites committed files, while a diff step without the opt-in
      # materializes no build config and passes vacuously. One run block must carry the regeneration,
      # the exact two-file diff, and a non-zero exit in that diff guard; workflow-wide co-occurrence
      # or a swallowed/non-failing diff is not enforcement.
      if workflow_enforces_build_config <<< "$text"; then
        build_config_enforced=1
      fi

      # The CALLER detector (#1504). BOTH halves are evaluated on THIS file, because a trigger in one
      # workflow cannot arm a call in another: a partitioned root that changes must re-run the workflow
      # that AUDITS it. Only a file carrying both counts as the gate; a file carrying one is reported as
      # the half it has, so the diagnostic can name what is missing instead of "not wired".
      #
      # `$text`, NOT the comment-stripped `$body`. Comments do not have to be stripped here and must not
      # be: the YAML PARSER discards them, including the inline ones a line-filter leaves behind — and a
      # trailing `# was: uses: FS-GG/…` is exactly how the old scanner certified a repo that had DELETED
      # its caller. Blanking whole lines before a parse would also, on its own, be able to change the
      # document's meaning rather than only its comments.
      if [ "${NEEDS_CALLER_ID:-}" = skill-union ]; then
        su_verdict="$(caller_verdict <<< "$text")"
        case "$su_verdict" in
          *"call=1"*) su_any_call=1
                      case "$su_verdict" in *"trigger=1"*) su_both=1 ;; esac ;;
        esac
      fi
    fi
  done <<< "$files"

  # A trigger on its own is NOT reported. Half 2 is satisfied by any unfiltered `pull_request:`
  # workflow, which nearly every repo has — so emitting it alone would make every repo in the org an
  # apparent partial adopter of this capability, and the reverse-direction sweep would report the whole
  # roster as drift. The CALL is what makes a repo an adopter at all; the trigger only says whether the
  # call is armed. So the partial token is the call, and a call with no trigger is the reportable half.
  #
  # The token is keyed on the DETECTOR ID the roster asked for, never on a literal: the sweep reads it
  # back as `caller-call:${CAP_CALLER[$cap]}`, so a hardcoded `skill-union` here would emit one id while
  # the reader looked for another the day a second caller kind exists — wired forever reported unwired.
  if [ -n "${NEEDS_CALLER_ID:-}" ] && [ "$repo" != "$AUTHORITY" ]; then
    [ "$su_any_call" -eq 0 ] || echo "caller-call:$NEEDS_CALLER_ID"
    [ "$su_both" -eq 0 ]     || echo "caller:$NEEDS_CALLER_ID"
  fi

  # The opt-in is not a workflow fact: it lives in the package receiver project. Read the project
  # directly rather than grepping the whole repository (which the contents API cannot do and which
  # would count docs/comments as adoption). A missing project is an examined "not opted in"; an API
  # failure is undetermined, because the reverse-direction sweep cannot know whether this repo adopted.
  if [ "${NEEDS_BUILD_CONFIG_MATERIALIZER:-0}" -eq 1 ] && [ "$repo" != "$AUTHORITY" ]; then
    project="$(get_repo_file "$repo" ".config/kit/FS.GG.Kit.receiver.proj")" || prc=$?
    if [ "$prc" -eq "$RC_UNREACHABLE" ]; then
      printf 'could not determine: reading .config/kit/FS.GG.Kit.receiver.proj failed — %s' "$(gh_last_err)" > "$CALLS_ERR_FILE"
      return 2
    fi
    if [ "$prc" -eq 0 ]; then
      project_code="$(strip_xml_comments <<< "$project")"
      if grep -qE '^[[:space:]]*<FsggKitMaterializeBuildConfig>[[:space:]]*true[[:space:]]*</FsggKitMaterializeBuildConfig>[[:space:]]*$' <<< "$project_code" \
          && grep -qE '^[[:space:]]*<PackageReference[[:space:]][^>]*Include[[:space:]]*=[[:space:]]*["'\'']FS\.GG\.Kit["'\''][^>]*/?>[[:space:]]*$' <<< "$project_code"; then
        build_config_opted_in=1
      fi
    fi

    [ "$build_config_opted_in" -eq 0 ] || echo "materializer-opt-in:build-config"
    [ "$build_config_enforced" -eq 0 ] || echo "materializer-enforcement:build-config"
    if [ "$build_config_opted_in" -eq 1 ] && [ "$build_config_enforced" -eq 1 ]; then
      echo "materializer:build-config"
    fi
  fi
  return 0
}

roster_list() {  # <cap> -> receiver full names; non-zero if the roster cannot be enumerated
  if [ -n "$REGISTRY" ]; then bash "$REPOS_SH" list --receives "$1" --registry "$REGISTRY"
  else bash "$REPOS_SH" list --receives "$1"; fi
}

# The capability whose receivers the kit-pin sweep grades. Named once, and asserted against the
# roster's own capability list before the sweep runs — see THE KIT-PIN FRESHNESS SWEEP.
KIT_CAP="coordination-kit"
roster_absence_cover() {  # `<full>\t<absence-cover or ->` per KIT_CAP package receiver (#1785)
  if [ -n "$REGISTRY" ]; then bash "$REPOS_SH" absence-cover --registry "$REGISTRY"
  else bash "$REPOS_SH" absence-cover; fi
}

roster_list_pkg() {  # the KIT_CAP receivers that take the kit AS A PACKAGE (ADR-0062)
  if [ -n "$REGISTRY" ]; then bash "$REPOS_SH" list --receives "$KIT_CAP" --kit-delivery package --registry "$REGISTRY"
  else bash "$REPOS_SH" list --receives "$KIT_CAP" --kit-delivery package; fi
}

all_repos_list() {  # every rostered repo, receives or not — the drift check's starting set
  if [ -n "$REGISTRY" ]; then bash "$REPOS_SH" list --all --registry "$REGISTRY"
  else bash "$REPOS_SH" list --all; fi
}

caps_list() {  # id<TAB>workflow<TAB>script<TAB>materializer<TAB>caller<TAB>push<TAB>receivers<TAB>reason
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

declare -A CAP_TOKEN CAP_SUBJ CAP_ARM CAP_NOTE CAP_KIND CAP_MATERIALIZER CAP_CALLER CAP_PUSH CAP_NONE CAP_ROSTER CAP_N CAP_WIRED CAP_GAPS CAP_UNDET
CAPS_ORDER=""
rostered_total=0
NEEDS_BUILD_CONFIG_MATERIALIZER=0
# Empty, or the caller-detector ID the roster asked for. It carries the ID rather than a 0/1 flag so the
# token repo_calls emits and the token the sweep greps for are the SAME string by construction, read off
# the roster once — never two literals that agree today.
NEEDS_CALLER_ID=""
# TAB IS IFS-*WHITESPACE* IN BASH: `IFS=$'\t' read` collapses runs of tabs and DROPS empty fields, so
# a capability with an empty `workflow` (every script:/push: row) would shift its `script` left into
# `wf` and be audited as a reusable workflow that does not exist. Re-delimit with a unit separator,
# which is not IFS whitespace and preserves empties. jq's @tsv escapes literal tabs in values, so the
# substitution cannot corrupt a `reason:`.
while IFS= read -r capline; do
  [ -n "$capline" ] || continue
  IFS=$'\x1f' read -r cap wf script materializer caller push recv reason <<< "${capline//$'\t'/$'\x1f'}"
  [ -n "$cap" ] || continue
  # `repos.sh validate` already enforces exactly-one-detector, and CI runs it — but this script must
  # not INFER that it ran. An unvalidated roster reaching a gate that assumes validation is how a
  # fail-open starts, so re-assert it here rather than trusting a check that lives elsewhere.
  #
  # CAP_TOKEN is what a receiver's workflows must contain for this capability to count as wired, in
  # the tagged form repo_calls emits. CAP_SUBJ is the same thing in human words, for the diagnostics.
  if [ -n "$wf" ]; then
    CAP_KIND["$cap"]="workflow"
    CAP_TOKEN["$cap"]="wf:$wf"
    CAP_SUBJ["$cap"]="$AUTHORITY/.github/workflows/$wf"
  elif [ -n "$script" ]; then
    CAP_KIND["$cap"]="script"
    CAP_TOKEN["$cap"]="script:$script"
    CAP_SUBJ["$cap"]="$AUTHORITY's scripts/$script"
  elif [ -n "$materializer" ]; then
    [ "$materializer" = build-config ] \
      || die "capability '$cap' names unsupported materializer detector '$materializer' (supported: build-config; repos.sh validate catches this)."
    CAP_KIND["$cap"]="materializer"
    CAP_MATERIALIZER["$cap"]="$materializer"
    CAP_TOKEN["$cap"]="materializer:$materializer"
    CAP_SUBJ["$cap"]="FS.GG.Kit '$materializer' materializer opt-in plus CI regeneration/diff enforcement"
    NEEDS_BUILD_CONFIG_MATERIALIZER=1
  elif [ -n "$caller" ]; then
    [ "$caller" = skill-union ] \
      || die "capability '$cap' names unsupported caller detector '$caller' (supported: skill-union; repos.sh validate catches this)."
    CAP_KIND["$cap"]="caller"
    CAP_CALLER["$cap"]="$caller"
    CAP_TOKEN["$cap"]="caller:$caller"
    # THREE strings, not one, because a compound detector has two halves that fail for different reasons
    # and a red must name its own subject (#327/#335). CAP_SUBJ is the CALL, CAP_ARM is the TRIGGER, and
    # CAP_NOTE says what deliberately does NOT count — so the diagnostics below stay kind-generic while
    # still being specific enough to act on.
    CAP_SUBJ["$cap"]="a $AUTHORITY/.github/workflows/skill-union-assert.yml caller aimed at this repo's OWN committed .claude/.agents skill roots"
    CAP_ARM["$cap"]="a pull_request trigger covering .claude/skills/** and .agents/skills/** (or no paths: filter at all)"
    CAP_NOTE["$cap"]="A call aimed at a GENERATED product (product-path: <subdir>), or narrowed with roots:, is a different subject and deliberately does not count."
    NEEDS_CALLER_ID="$caller"
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
    die "capability '$cap' declares no detector (workflow:/script:/materializer:/caller:/push:) — there is nothing to audit it by, so it would be swept in NEITHER direction while remaining a legal 'receives' word. That is #628 exactly. Fix registry/repos.yml (repos.sh validate catches this)."
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
    *) die "repo(s) [$by] receive '$cap', but the roster declares no 'capabilities:' row for it — so this audit has no detector for it and sweeps it in NEITHER direction: it can be found neither unwired nor adopted-but-unrostered. A capability that is legal to receive and impossible to check is a green nobody earned (#628). Give '$cap' a detector row (workflow:/script:/materializer:/caller:/push:) in registry/repos.yml." ;;
  esac
done <<< "$received"

# --- what the repos ACTUALLY adopt ---------------------------------------------------------------
all_repos="$(all_repos_list)" \
  || die "cannot enumerate the roster — repos.sh list --all failed. The roster is unreadable, which is not the same as empty."
# THE TREE FETCHER'S BOUNDARY (#1556), assigned here because this is where the roster is first read
# and nowhere else knows it. Lowercased once, so the membership test matches GitHub's own
# case-insensitive resolution of `owner/name` — a receiver that writes `FS-GG/fs.gg.sdd` fetches the
# same repository the roster spells `FS-GG/FS.GG.SDD`, and treating it as off-roster would silently
# give up rule (4) over a capitalisation.
SPARSE_ROSTER="$(printf '%s\n' "$all_repos" | tr '[:upper:]' '[:lower:]')"

audited=0; wired=0; gaps=0; drift=0; undetermined=0
# The sparse sweep's own denominators (#1529, criterion 5). They are counted HERE, in the parent,
# because "did this repo's workflow walk complete?" is the parent's answer — repo_calls runs in a
# subshell and its rc is the only thing that crosses back. `rostered` is what the sweep was asked to
# cover, `sparse_repos` what it actually covered, and `sparse_unread` the difference: a roster that
# silently collapses is then legible in the report rather than indistinguishable from a clean one.
rostered=0; sparse_repos=0; sparse_unread=0
while IFS= read -r repo; do
  [ -n "$repo" ] || continue
  rostered=$((rostered + 1))
  crc=0; calls="$(repo_calls "$repo")" || crc=$?
  if [ "$crc" -ne 0 ]; then
    # We could not read this repo, so we know neither what it declares-but-skips nor what it
    # adopts-without-saying. Both directions are unexamined; the run is not a verdict. The sparse
    # sweep is unexamined for it too — whatever it graded before the API failed is a PARTIAL read of
    # this repo, so the repo counts as NOT audited even though some of its findings are real.
    echo "::error::repos-audit: $repo — $(calls_last_err)"
    undetermined=$((undetermined + 1))
    sparse_unread=$((sparse_unread + 1))
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
  sparse_repos=$((sparse_repos + 1))

  for cap in $CAPS_ORDER; do
    # A PUSH capability has no receiver-side artifact, so BOTH directions are meaningless for it: it
    # cannot be "unwired" (there is nothing to wire) and it cannot be an "unrostered adopter" (there
    # is nothing to adopt). Sweeping it would report every receiver as a gap. Its honesty comes from
    # `repos.sh validate` refusing the row without a reason, not from this sweep.
    [ -n "${CAP_PUSH[$cap]:-}" ] && continue

    subj="${CAP_SUBJ[$cap]}"; token="${CAP_TOKEN[$cap]}"
    declared=0; calls_it=0; partial_it=0
    # Herestrings, not pipes — the same rule repo_calls explains at length. These three are SAFE
    # today only because a roster line and a token list are far under the 64KiB pipe buffer, so the
    # writer always finishes before `grep -q` exits. That is a property of the DATA, not of the code,
    # and it is exactly the kind of accident that stops being true quietly. One rule, no exceptions.
    if grep -qxF "$repo"  <<< "${CAP_ROSTER[$cap]}"; then declared=1; fi
    if grep -qxF "$token" <<< "$calls";              then calls_it=1; fi
    materializer="${CAP_MATERIALIZER[$cap]:-}"
    if [ "${CAP_KIND[$cap]:-}" = materializer ] \
        && { grep -qxF "materializer-opt-in:$materializer" <<< "$calls" \
             || grep -qxF "materializer-enforcement:$materializer" <<< "$calls"; }; then
      partial_it=1
    fi
    detector_caller="${CAP_CALLER[$cap]:-}"
    if [ "${CAP_KIND[$cap]:-}" = caller ] \
        && grep -qxF "caller-call:$detector_caller" <<< "$calls"; then
      partial_it=1
    fi

    if [ "$declared" -eq 1 ] && [ "$calls_it" -eq 1 ]; then
      echo "ok: $repo wires $subj (receives: $cap)"
      audited=$((audited + 1)); wired=$((wired + 1)); CAP_WIRED["$cap"]=$(( ${CAP_WIRED[$cap]} + 1 ))
    elif [ "$declared" -eq 1 ]; then
      if [ "${CAP_KIND[$cap]:-}" = materializer ]; then
        missing=""
        grep -qxF "materializer-opt-in:$materializer" <<< "$calls" \
          || missing="${missing} FS.GG.Kit package provenance plus explicit <FsggKitMaterializeBuildConfig>true</FsggKitMaterializeBuildConfig> opt-in;"
        grep -qxF "materializer-enforcement:$materializer" <<< "$calls" \
          || missing="${missing} CI FsggKitMaterialize + managed-props diff enforcement;"
        echo "::error::repos-audit: $repo receives '$cap' but is missing:${missing}"
      elif [ "${CAP_KIND[$cap]:-}" = caller ]; then
        # Name the half. "Not wired" is the wrong diagnostic for a repo that DOES call the assertion and
        # merely points it somewhere else, or arms it on nothing — the remedy is different in each case,
        # and #327/#335's rule is that a red must name its own subject.
        if grep -qxF "caller-call:$detector_caller" <<< "$calls"; then
          echo "::error::repos-audit: $repo receives '$cap' and calls ${CAP_SUBJ[$cap]}, but that workflow does not RUN when a committed skill root changes — add ${CAP_ARM[$cap]}. An unarmed gate is not a gate."
        else
          echo "::error::repos-audit: $repo receives '$cap' but nothing in its workflows calls ${CAP_SUBJ[$cap]}. ${CAP_NOTE[$cap]}"
        fi
      else
        echo "::error::repos-audit: $repo receives '$cap' but nothing in its workflows references $subj"
      fi
      audited=$((audited + 1)); gaps=$((gaps + 1)); CAP_GAPS["$cap"]=$(( ${CAP_GAPS[$cap]} + 1 ))
    elif [ "$calls_it" -eq 1 ] || [ "$partial_it" -eq 1 ]; then
      # The reverse direction (#503). `lockfile-sync` sat like this for six repos: really adopted,
      # never rostered, so `list --receives lockfile-sync` returned nothing and the audit believed
      # the capability had no receivers — while the thing it was supposed to be watching ran, and
      # broke, unwatched (FS.GG.Game#137: 119 consecutive startup_failed, no gate said a word).
      #
      # This is the direction that would have caught #628 the day it started: `build-config` was
      # enforced by four repos and rostered by none. It could not fire, because a capability with no
      # detector row was not swept at all.
      # A compound detector is ADOPTED, not merely REFERENCED: the receiver enabled something, rather
      # than naming a file the authority owns. The verb is chosen off the kind so a new compound kind
      # cannot silently fall back to the wrong one, which is what a second `= materializer` test would
      # have kept inviting.
      case "${CAP_KIND[$cap]:-}" in
        materializer|caller) verb="adopts" ;;
        *)                   verb="references" ;;
      esac
      if [ -n "${CAP_NONE[$cap]:-}" ]; then
        echo "::error::repos-audit: $repo $verb $subj, but registry/repos.yml records capability '$cap' as having NO receivers. That recorded claim is now FALSE — roster $repo under 'receives: $cap' and delete the 'receivers: none' claim."
      else
        echo "::error::repos-audit: $repo $verb $subj but does not declare 'receives: $cap' — an adopted-but-unrostered capability. Every org fabric iterates the roster, so an unrostered adopter is invisible to all of them. Add '$cap' to $repo's receives in registry/repos.yml."
      fi
      drift=$((drift + 1))
    fi
  done
done <<< "$all_repos"

# --- repository_dispatch graph (#1919) -----------------------------------------------------------
# `POST /dispatches` answers 204 even when no workflow consumes the event.  Unlike a capability,
# this fabric is a three-place contract: producer, target and event-type.  Read the workflows already
# staged above; a second API pass would create a race and would make an unread receiver look absent.
dispatch_registry="${REGISTRY:-$HERE/../registry/repos.yml}"
dispatch_json="$(yaml_text2json < "$dispatch_registry")" \
  || die "cannot parse registry/repos.yml while enumerating dispatch contracts."
dispatches="$(echo "$dispatch_json" | jq -r '
  if (.dispatches // []) | type != "array" then error("dispatches is not an array") else
    (.dispatches // [])[]
    | if type != "object" or (.producer | type != "string" or length == 0)
         or (.target | type != "string" or length == 0)
         or (."event-type" | type != "string" or length == 0)
      then error("dispatch row has no producer/target/event-type strings")
      else [.producer,.target,."event-type"] | @tsv end
  end' 2>/dev/null)" || die "cannot enumerate dispatch contracts — registry/repos.yml has an invalid dispatches shape."

dispatch_extract() { # <repo> <workflow-file>
  local repo="$1" file="$2" text parsed rc=0
  text="$(cat "$ABSENTOK_DIR/${repo//\//__}/$file")"
  parsed="$(printf '%s' "$text" | yaml_text2json)" || return 1
  printf '%s' "$parsed" | python3 -c '
import json, sys
repo = sys.argv[1]
wf = json.load(sys.stdin)
def on_block(x):
    if not isinstance(x, dict): return None
    return x.get("on", x.get("true", x.get("True")))
on = on_block(wf)
if isinstance(on, dict):
    rd = on.get("repository_dispatch")
    if isinstance(rd, dict):
        ts = rd.get("types")
        if isinstance(ts, list):
            for t in ts:
                if isinstance(t, str) and t: print("L\t%s\t%s" % (repo, t))
jobs = wf.get("jobs") if isinstance(wf, dict) else None
if isinstance(jobs, dict):
    for job in jobs.values():
        if not isinstance(job, dict): continue
        uses = job.get("uses")
        with_ = job.get("with")
        if not isinstance(uses, str) or not isinstance(with_, dict): continue
        if not uses.strip().startswith("FS-GG/.github/.github/workflows/dispatch-sender.yml@") : continue
        target, event = with_.get("target-repo"), with_.get("event-type")
        if isinstance(target, str) and target and isinstance(event, str) and event:
            print("S\t%s\t%s\t%s" % (repo, target, event))
' "$repo"
}

dispatch_findings=0; dispatch_refusals=0; dispatch_declared=0
while IFS=$'\t' read -r dp dt de; do
  [ -n "$dp" ] || continue
  dispatch_declared=$((dispatch_declared + 1))
done <<< "$dispatches"
while IFS= read -r repo; do
  [ -n "$repo" ] || continue
  ddir="$ABSENTOK_DIR/${repo//\//__}"
  [ -d "$ddir" ] || continue
  while IFS= read -r wf; do
    [ -n "$wf" ] || continue
    # A malformed unrelated workflow cannot decide this graph's verdict.  Conversely, a file that
    # names either dispatch surface but will not parse is a no-verdict: it may carry a sender or
    # listener that a text scan cannot safely reconstruct.
    if ! grep -qE 'repository_dispatch|dispatch-sender\.ya?ml' "$ddir/$wf"; then
      continue
    fi
    if ! dispatch_extract "$repo" "$wf" >> "$DISPATCH_FILE"; then
      echo "::error::repos-audit: $repo workflow '$wf' will not parse while extracting repository_dispatch contracts; the graph has no verdict for it."
      dispatch_refusals=$((dispatch_refusals + 1))
    fi
  done < <(find "$ddir" -maxdepth 1 -type f -printf '%f\n')
done <<< "$all_repos"

# Forward: every declared edge has BOTH a real sender and a target listener for the same string.
while IFS=$'\t' read -r dp dt de; do
  [ -n "$dp" ] || continue
  if ! grep -qxF $'S\t'"$dp"$'\t'"$dt"$'\t'"$de" "$DISPATCH_FILE"; then
    echo "::error::repos-audit: declared dispatch $dp -> $dt ($de) has no matching dispatch-sender.yml caller."
    dispatch_findings=$((dispatch_findings + 1))
  fi
  if ! grep -qxF $'L\t'"$dt"$'\t'"$de" "$DISPATCH_FILE"; then
    echo "::error::repos-audit: declared dispatch $dp -> $dt ($de) has no matching repository_dispatch listener."
    dispatch_findings=$((dispatch_findings + 1))
  fi
done <<< "$dispatches"

# Reverse: a sender or listener missing from the roster is drift.  Keep the two halves separate:
# a live listener is evidence even if its producer was deleted, and vice versa.  This executes even
# with zero declarations: an omitted graph must never turn live dispatches into a green blind spot.
while IFS=$'\t' read -r kind a b c; do
  case "$kind" in
    S) if ! grep -qxF "$a"$'\t'"$b"$'\t'"$c" <<< "$dispatches"; then
         echo "::error::repos-audit: live dispatch sender $a -> $b ($c) is not declared in registry/repos.yml."
         dispatch_findings=$((dispatch_findings + 1)); fi ;;
    L) if ! awk -F '\t' -v target="$a" -v event="$b" '$2 == target && $3 == event { found=1 } END { exit !found }' <<< "$dispatches"; then
         echo "::error::repos-audit: live repository_dispatch listener $a ($b) is not declared in registry/repos.yml."
         dispatch_findings=$((dispatch_findings + 1)); fi ;;
  esac
done < "$DISPATCH_FILE"
echo "repos-audit: repository_dispatch graph — $dispatch_declared declared edge(s), $dispatch_findings finding(s), $dispatch_refusals refusal(s)."

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

# --- the kit-pin freshness sweep (#1540) ---------------------------------------------------------
# Fetch each coordination-kit receiver's candidate pin files, then grade them all in ONE verdict
# program — one pass, one feed read, one comparand for the whole roster. A per-repo feed read would
# make the sweep's answer depend on WHEN in the loop a republish landed.
#
# The three candidate paths are tried for every receiver rather than selected per repo, because
# which one a repo uses is exactly the fact that is not written down anywhere (see the section
# header). A 404 is an ANSWER — that shape is simply not how this repo pins — and costs nothing but
# the call. An unreachable read is not: it makes the repo undetermined, like any other unread
# receiver, and the run stops being a verdict.
kitpin_receivers=0; kitpin_read=0
# THE SUBJECT MUST EXIST BEFORE IT CAN BE SWEPT, and this is the one assertion that keeps the sweep
# honest. `repos.sh list --receives <cap>` does NOT validate the id: an unknown capability is a jq
# select that matches nothing, so it prints nothing and exits 0. Without this check, the day
# `coordination-kit` is renamed or retired in the roster, this sweep would cover NOBODY, force its
# own counters to zero, and let the run end on "every coordination-kit receiver pins the published
# FS.GG.Kit" at exit 0 — a green claim about a set that was never looked at.
#
# That is #503 exactly, one sweep over: a hardcoded id here that the roster no longer knows about is
# audited only if somebody also remembers to edit this file, and forgetting is silent. It is NOT
# covered by the per-capability guard above, which checks the ids the roster DOES declare and knows
# nothing about this literal. So the literal is asserted against the roster's own capability list.
kit_cap_declared=0
case " $CAPS_ORDER " in *" $KIT_CAP "*) kit_cap_declared=1 ;; esac

kit_roster=""; kit_all_receivers=""
if [ "$kit_cap_declared" -eq 1 ]; then
  # Narrowed to `--kit-delivery package`, which is not a detail: the roster records that a
  # coordination-kit receiver may take the kit BYTE-COPY (and that absence of the field MEANS
  # byte-copy). Such a receiver legitimately has no FS.GG.Kit PackageReference anywhere, so grading
  # it on a pin would refuse it — exit 3, every day, forever, for a roster state that is correct.
  # Every receiver reads `package` today; the roster's semantics say the next one need not.
  kit_roster="$(roster_list_pkg)" \
    || die "cannot enumerate the $KIT_CAP package receivers — repos.sh list --receives $KIT_CAP --kit-delivery package failed."
  kit_all_receivers="$(roster_list "$KIT_CAP")" \
    || die "cannot enumerate the $KIT_CAP receivers — repos.sh list --receives $KIT_CAP failed."
fi
: > "$KITPIN_DIR/manifest.tsv"
while IFS= read -r repo; do
  [ -n "$repo" ] || continue
  kitpin_receivers=$((kitpin_receivers + 1))
  slug="${repo//\//__}"
  mkdir -p "$KITPIN_DIR/$slug"
  kit_repo_ok=1; kit_n=0
  # Staged to a PER-REPO buffer and only committed to the manifest once every candidate has been
  # read. A partial read must not be graded: if the first candidate says 0.8.0 and the second is
  # unreachable, the repo LOOKS current on the evidence we hold — and the second file may carry a
  # different pin, which is a refusal, not a pass. Appending as we went produced exactly that: an
  # `ok: <repo> pins 0.8.0` line printed beside the `undetermined` for the same repo. The exit code
  # was still 2, so the verdict was never wrong, but the report contradicted it — and a report that
  # says "ok" about a repo the run did not finish reading is the sentence this whole sweep exists to
  # stop anyone writing.
  : > "$KITPIN_DIR/$slug/manifest.part"
  for candidate in ".config/kit/FS.GG.Kit.receiver.proj" "Directory.Packages.local.props" "Directory.Packages.props"; do
    frc=0; body="$(get_repo_file "$repo" "$candidate")" || frc=$?
    if [ "$frc" -eq "$RC_UNREACHABLE" ]; then
      printf 'undetermined\t%s\treading %s failed — %s\n' "$repo" "$candidate" "$(gh_last_err)" >> "$KITPIN_FILE"
      kit_repo_ok=0
      break
    fi
    [ "$frc" -eq 0 ] || continue          # 404: this repo does not pin in this shape. An answer.
    kit_n=$((kit_n + 1))
    printf '%s' "$body" > "$KITPIN_DIR/$slug/$kit_n"
    printf '%s\t%s\t%s\n' "$repo" "$candidate" "$slug/$kit_n" >> "$KITPIN_DIR/$slug/manifest.part"
  done
  if [ "$kit_repo_ok" -eq 1 ]; then
    kitpin_read=$((kitpin_read + 1))
    cat "$KITPIN_DIR/$slug/manifest.part" >> "$KITPIN_DIR/manifest.tsv"
    # A receiver whose every candidate 404'd still needs a row, or the verdict program never sees it
    # and a repo that pins NOWHERE would vanish from the sweep instead of being refused.
    [ "$kit_n" -gt 0 ] || printf '%s\t-\t-\n' "$repo" >> "$KITPIN_DIR/manifest.tsv"
  fi
done <<< "$kit_roster"

# The sweep is only as meaningful as the set it covered. The capability EXISTS (asserted above), so
# the only way to reach zero here is that every one of its receivers takes the kit byte-copy — a
# legitimate roster state in which there is no pin anywhere to grade. That is a real "nothing to
# assert", so it is not a refusal; but it must not end the run on the sentence a real sweep earns
# either, which is why the terminal OK line below is conditional on `kitpin_graded`.
kit_bytecopy=$(( $(printf '%s\n' "$kit_all_receivers" | grep -c . || true) - kitpin_receivers ))
if [ "$kitpin_receivers" -eq 0 ]; then
  if [ "$kit_cap_declared" -eq 0 ]; then
    echo "repos-audit: kit-pin freshness (#1540) — this roster declares no '$KIT_CAP' capability, so this sweep has no subject and graded nothing. NOTHING was asserted about kit freshness."
  else
    echo "repos-audit: kit-pin freshness (#1540) — all $kit_bytecopy $KIT_CAP receiver(s) take the kit byte-copy, so there is no pin to grade anywhere. NOTHING was asserted about kit freshness; that is not a clean bill."
  fi
  kitpin_findings=0; kitpin_refusals=0; kitpin_undet=0; kitpin_graded=0
else
  FSGG_SCRIPTS_DIR="$HERE" python3 -c "$KIT_PIN_PY" "$KITPIN_DIR" "$KITPIN_DIR/manifest.tsv" >> "$KITPIN_FILE" \
    || die "the kit-pin freshness verdict program failed to run. That is not a clean sweep — no receiver's pin was graded."

  kitpin_count() { grep -cE "^$1"$'\t' "$KITPIN_FILE" 2>/dev/null || true; }
  kitpin_findings="$(kitpin_count finding)"
  kitpin_refusals="$(kitpin_count refusal)"
  kitpin_undet="$(kitpin_count undetermined)"
  kitpin_ok="$(kitpin_count ok)"
  kitpin_published="$(awk -F'\t' '$1 == "published" { print $2; exit }' "$KITPIN_FILE")"

  while IFS=$'\t' read -r kind a b; do
    case "$kind" in
      finding)      echo "::error::repos-audit: kit-pin freshness — $a $b" ;;
      refusal)      echo "::error::repos-audit: kit-pin freshness REFUSED a repo it cannot grade — $a: $b" ;;
      undetermined) echo "::error::repos-audit: kit-pin freshness — $a: $b" ;;
      ok)           echo "  ok: $a $b" ;;
    esac
  done < "$KITPIN_FILE"

  # GRADED is read back off the LEDGER, not off the fetch loop. `kitpin_read` counts repos whose
  # every candidate read succeeded, which is a fact about the network, not about how many verdicts
  # were reached — and the two disagreeing is how the summary came to print "graded 0 of 2 … 2
  # current" while that bug was live. A denominator that cannot contradict the numerator is worth
  # the extra line.
  kitpin_graded=$(( kitpin_ok + kitpin_findings + kitpin_refusals ))
  if [ -z "$kitpin_published" ]; then
    echo "repos-audit: kit-pin freshness (#1540) — the published FS.GG.Kit version could not be resolved, so NOTHING was asserted about any receiver's pin. That is not a clean sweep."
  else
    echo "repos-audit: kit-pin freshness (#1540) — newest stable FS.GG.Kit published on nuget.org is $kitpin_published; graded $kitpin_graded of $kitpin_receivers $KIT_CAP package receiver(s) ($kit_bytecopy byte-copy, not graded): $kitpin_ok current, $kitpin_findings stale-or-unresolvable, $kitpin_refusals refusal(s), $kitpin_undet undetermined. This is f(roster, feed) — no receiver has to push for it to change."
  fi
fi

# --- the bump-offer sweep (#1768) ----------------------------------------------------------------
# For each receiver the sweep above found BEHIND, fetch what would tell a human WHICH remedy applies:
# the repo's open pull requests, its branch list, and the kit pin as it stands at each candidate head.
# Then grade them all in ONE verdict program, as above.
#
# LAZY, AND THE LAZINESS IS THE COST CONTROL. Nothing here is fetched for a receiver that is current.
# An org whose receivers are all on the published kit pays this sweep ZERO additional API calls — the
# same property #1556 criterion 5 asked of the tree fetches, and the reason this rides the freshness
# sweep instead of polling the org. Per BEHIND receiver it costs one PR list, one branch list, and one
# content read per candidate head; today that is seven receivers behind and eleven reads.
offer_subjects=0; offer_fetched=0
: > "$OFFER_DIR/manifest.tsv"

# The behind rows are the kit-pin sweep's own machine-readable output, so this sweep's subject set
# cannot drift from what that one reported. If it graded nobody behind, the loop body never runs.
# Normalize one API list into `<key>\t<slug>` lines, and FAIL rather than abort if the body is not
# the shape this sweep expects. Under `set -euo pipefail` a bare `json.load` in a pipeline takes the
# whole audit down mid-loop — which is not a no-verdict, it is no report at all, and it is exactly the
# fail-shape this script's own header forbids. Every caller below therefore guards this with `||` and
# turns the failure into an `err` file, which the verdict program renders as `offer-undetermined`.
#
# The slug exists because a git ref carries `/` and cannot be a filename.
offer_normalize() {  # <kind: prs|branches> <infile> <outfile> -> `<num-or-name>\t<ref>\t<slug>` lines
  python3 -c '
import json, sys
kind, src, dst = sys.argv[1], sys.argv[2], sys.argv[3]
data = json.load(open(src, encoding="utf-8"))
if not isinstance(data, list):
    raise ValueError("expected a JSON array, got %s" % type(data).__name__)
rows = []
for item in data:
    if kind == "prs":
        key, ref = str(item["number"]), item["headRefName"]
    else:
        key = ref = item["name"]
    rows.append("%s\t%s\t%s" % (key, ref, ref.replace("/", "__")))
open(dst, "w", encoding="utf-8").write("\n".join(rows) + ("\n" if rows else ""))
' "$1" "$2" "$3" 2>"$GH_ERR_FILE"
}

# Would a ref of this name plausibly carry a kit bump? Deliberately GENEROUS, and the asymmetry is
# deliberate too: a ref that carries a bump and is missed here reads as `offer-none` — a false alarm
# that sends someone to a dashboard — whereas a ref matched needlessly costs one content read that
# finds no pin change. So the filter errs wide. It is matched on the REF and never on the PR TITLE,
# because a title is prose Renovate composes and a human may edit, while the ref is what the branch
# actually is. The filter exists at all only so a repo with thirty open PRs does not cost thirty
# content reads to answer one question.
offer_candidate_ref() {
  case "$(printf '%s' "$1" | tr '[:upper:]' '[:lower:]')" in
    *fs.gg.kit*|*fs-gg-kit*|*coordination-kit*|*kit-bump*|*bump-kit*|*fs.gg.coord.cli*|*fs-gg-coord-cli*|*coord-engine*) return 0 ;;
    *) return 1 ;;
  esac
}

# The behind rows are the kit-pin sweep's own machine-readable output, so this sweep's subject set
# cannot drift from what that one reported. If it graded nobody behind, the loop body never runs.
while IFS=$'\t' read -r _kind repo pin published pinpath; do
  [ -n "$repo" ] || continue
  offer_subjects=$((offer_subjects + 1))
  slug="${repo//\//__}"
  mkdir -p "$OFFER_DIR/$slug/head" "$OFFER_DIR/$slug/branch"
  offer_err=""

  # Open PRs, then branches. ANY failure here — the call, or a body that is not the documented shape
  # — makes this receiver UNDETERMINED about its offer. Never `offer-none`: that is a verdict, and
  # manufacturing it out of a lost API call would be this sweep committing the very #266 error it
  # was built to report.
  prc=0; prs="$(gh_api "repos/$repo/pulls?state=open&per_page=100" \
                       --jq '[.[] | {number, headRefName: .head.ref}]')" || prc=$?
  if [ "$prc" -ne 0 ]; then
    offer_err="reading open pull requests failed — $(gh_last_err)"
  else
    printf '%s' "$prs" > "$OFFER_DIR/$slug/prs.json"
    brc=0; branches="$(gh_api "repos/$repo/branches?per_page=100" --jq '[.[] | {name}]')" || brc=$?
    if [ "$brc" -ne 0 ]; then
      offer_err="reading branches failed — $(gh_last_err)"
    else
      printf '%s' "$branches" > "$OFFER_DIR/$slug/branches.json"
      offer_normalize prs "$OFFER_DIR/$slug/prs.json" "$OFFER_DIR/$slug/prs.tsv" \
        || offer_err="the open pull-request list was not the shape this sweep reads — $(gh_last_err)"
      if [ -z "$offer_err" ]; then
        offer_normalize branches "$OFFER_DIR/$slug/branches.json" "$OFFER_DIR/$slug/branches.tsv" \
          || offer_err="the branch list was not the shape this sweep reads — $(gh_last_err)"
      fi
    fi
  fi

  if [ -z "$offer_err" ]; then
    offer_fetched=$((offer_fetched + 1))
    # The pin file AT each candidate head, and at each candidate BRANCH. The branch pass is what sees
    # the rate-limited state at all: a branch held with no PR leaves no other evidence anywhere.
    #
    # A head read that 404s is an ANSWER — that ref does not carry this pin shape — and a head read
    # that fails otherwise simply leaves that candidate out of the set. Neither can manufacture an
    # offer; a receiver whose every candidate went unread lands on `offer-none`, whose own text names
    # "a bump we could not read" among the things it did not check.
    while IFS=$'\t' read -r key ref refslug; do
      [ -n "$key" ] || continue
      offer_candidate_ref "$ref" || continue
      hrc=0; head="$(get_repo_file_ref "$repo" "$pinpath" "$ref")" || hrc=$?
      [ "$hrc" -eq 0 ] || continue
      printf '%s' "$head" > "$OFFER_DIR/$slug/head/$key"
    done < "$OFFER_DIR/$slug/prs.tsv"

    while IFS=$'\t' read -r key ref refslug; do
      [ -n "$key" ] || continue
      offer_candidate_ref "$ref" || continue
      hrc=0; head="$(get_repo_file_ref "$repo" "$pinpath" "$ref")" || hrc=$?
      [ "$hrc" -eq 0 ] || continue
      printf '%s' "$head" > "$OFFER_DIR/$slug/branch/$refslug"
    done < "$OFFER_DIR/$slug/branches.tsv"
  else
    printf '%s' "$offer_err" > "$OFFER_DIR/$slug/err"
  fi

  printf '%s\t%s\t%s\t%s\t%s\tFS.GG.Kit\n' "$repo" "$pin" "$published" "$pinpath" "$slug" \
    >> "$OFFER_DIR/manifest.tsv"
done < <(awk -F'\t' '$1 == "behind"' "$KITPIN_FILE")


if [ "$offer_subjects" -eq 0 ]; then
  # NOT a green, and the sentence says which of the two silences this is. "No receiver is behind" is a
  # real all-clear about the proposal step — there is nothing to propose. "The freshness sweep reached
  # no verdict" is not, and must not borrow the first one's words (#266).
  offer_none=0; offer_current=0; offer_superseded=0; offer_ratelimited=0; offer_undet=0
  if [ "$kitpin_graded" -gt 0 ] && [ "$kitpin_findings" -eq 0 ]; then
    echo "repos-audit: bump-offer (#1768) — no receiver is behind, so there is no bump for anyone to have been offered. This one IS an all-clear about the proposal step."
  else
    echo "repos-audit: bump-offer (#1768) — the kit-pin sweep reached no BEHIND verdict for anybody, so this sweep had no subject and graded nothing. NOTHING was asserted about whether any receiver has been offered a bump."
  fi
else
  FSGG_SCRIPTS_DIR="$HERE" python3 -c "$OFFER_PY" "$OFFER_DIR" "$OFFER_DIR/manifest.tsv" >> "$OFFER_FILE" \
    || die "the bump-offer verdict program failed to run. That is not a clean sweep — no behind receiver's proposal state was graded."

  offer_count() { grep -cE "^$1"$'\t' "$OFFER_FILE" 2>/dev/null || true; }
  offer_none="$(offer_count offer-none)"
  offer_current="$(offer_count offer-current)"
  offer_superseded="$(offer_count offer-superseded)"
  offer_ratelimited="$(offer_count offer-ratelimited)"
  offer_undet="$(offer_count offer-undetermined)"

  while IFS=$'\t' read -r kind a b; do
    case "$kind" in
      # The three states that mean a HUMAN MUST GO TICK SOMETHING are errors in their own right, with
      # their own annotation, so the operator reading the run sees the action and not merely the fact.
      offer-none)         echo "::error::repos-audit: bump-offer — $a $b" ;;
      offer-ratelimited)  echo "::error::repos-audit: bump-offer — $a $b" ;;
      offer-superseded)   echo "::error::repos-audit: bump-offer — $a $b" ;;
      # A current offer is NOT an error here. The receiver is still behind and the kit-pin sweep has
      # already said so with its own red; this line exists to tell the reader that the proposal step
      # worked and the remaining action is a review, not a dashboard tick.
      offer-current)      echo "  bump-offer: $a $b" ;;
      offer-undetermined) echo "::error::repos-audit: bump-offer — $a: $b" ;;
    esac
  done < "$OFFER_FILE"

  echo "repos-audit: bump-offer (#1768) — of $offer_subjects receiver(s) the kit-pin sweep found behind, read the open PRs/branches of $offer_fetched: $offer_current have a CURRENT bump open, $offer_superseded have a SUPERSEDED one, $offer_ratelimited have a branch held by a rate limit, $offer_none have NO bump at all, $offer_undet undetermined. This does NOT observe whether Renovate has run, when it next will, or whether a bump here was closed by hand — see the header on what this sweep cannot see."
fi

# --- the view-root generate sweep (#1759) --------------------------------------------------------
#
# IT RIDES THE KIT-PIN STAGING AND FETCHES NOTHING. The file it grades —
# `.config/kit/FS.GG.Kit.receiver.proj` — is already staged for every package receiver by the loop
# above, so a second fetch loop would double this sweep's API cost against the rate limit this
# script treats as its main adversary, to read bytes it already holds. It reads the same manifest
# and skips the rows that are not the receiver project.
#
# THAT COUPLING IS DELIBERATE AND IT IS ALSO A CONSTRAINT: this sweep can only grade receivers the
# kit-pin loop reached. A repo whose read failed there emitted its own `undetermined` and has no
# manifest row here, so it is absent from BOTH counts rather than silently passing this one — which
# is why the terminal line below reports its denominator instead of claiming the roster.
viewgen_findings=0; viewgen_refusals=0; viewgen_undet=0; viewgen_ok=0; viewgen_none=0; viewgen_graded=0
if [ "$kitpin_receivers" -eq 0 ]; then
  echo "repos-audit: view-root generate (#1759) — no $KIT_CAP package receiver was staged, so this sweep graded nothing. NOTHING was asserted about whether a declared view root can be generated."
else
  python3 -c "$VIEWGEN_PY" "$KITPIN_DIR" "$KITPIN_DIR/manifest.tsv" >> "$VIEWGEN_FILE" \
    || die "the view-root generate verdict program failed to run. That is not a clean sweep — no receiver's view-root wiring was graded."

  viewgen_count() { grep -cE "^$1"$'\t' "$VIEWGEN_FILE" 2>/dev/null || true; }
  viewgen_findings="$(viewgen_count finding)"
  viewgen_refusals="$(viewgen_count refusal)"
  viewgen_undet="$(viewgen_count undetermined)"
  viewgen_ok="$(viewgen_count ok)"
  viewgen_none="$(viewgen_count none)"

  while IFS=$'\t' read -r kind a b; do
    case "$kind" in
      finding)      echo "::error::repos-audit: view-root generate — $a $b" ;;
      refusal)      echo "::error::repos-audit: view-root generate REFUSED a shape it cannot grade — $a: $b" ;;
      undetermined) echo "::error::repos-audit: view-root generate — $a: $b" ;;
      ok)           echo "  ok: $a $b" ;;
      none)         echo "  n/a: $a $b" ;;
    esac
  done < "$VIEWGEN_FILE"

  # GRADED counts only the receivers that HAVE the subject. `none` is not a pass and must not pad
  # this number: a roster where every receiver stopped declaring a view root would otherwise report
  # "N of N" while asserting nothing at all, which is the sentence every sweep in this file exists
  # to stop anyone writing.
  viewgen_graded=$(( viewgen_ok + viewgen_findings + viewgen_refusals ))
  if [ "$viewgen_graded" -eq 0 ]; then
    echo "repos-audit: view-root generate (#1759) — none of the $kitpin_receivers staged $KIT_CAP package receiver(s) declares a non-empty <FsggKitViewSkillRoots>, so there is no view root to generate anywhere. NOTHING was asserted; that is not a clean bill."
  else
    echo "repos-audit: view-root generate (#1759) — graded $viewgen_graded of $kitpin_receivers staged $KIT_CAP package receiver(s) ($viewgen_none declare no view root, not graded): $viewgen_ok generate it before FsggKitCheckSkillView, $viewgen_findings do NOT, $viewgen_refusals refusal(s), $viewgen_undet undetermined. This is f(roster) — a receiver that drops its generate target is reported HERE, not discovered on its next Renovate kit bump."
  fi
fi

# --- the engine-manifest sweep (#1615) -----------------------------------------------------------
#
# #1077's invariant, asserted against the receivers' actual trees instead of arranged for by two kit
# rows sharing a fabric. See THE ENGINE-MANIFEST SWEEP for the full argument.
#
# ITS SUBJECT IS `$kit_all_receivers`, NOT `$kit_roster`. The kit-pin sweep above narrows to
# `--kit-delivery package`; this one must not, because a byte-copy receiver gets the same shim and
# needs the same engine. Using the wrong variable here would carve #1077's two original victims back
# out of the check written to replace it, and the run would still say OK.
engman_findings=0; engman_refusals=0; engman_undet=0; engman_feed_undet=0; engman_ok=0; engman_receivers=0; engman_read=0
engman_graded=0
: > "$ENGMAN_DIR/manifest.tsv"
if [ "$kit_cap_declared" -eq 1 ]; then
  while IFS= read -r repo; do
    [ -n "$repo" ] || continue
    engman_receivers=$((engman_receivers + 1))
    slug="${repo//\//__}"
    mkdir -p "$ENGMAN_DIR/$slug"
    frc=0; body="$(get_repo_file "$repo" ".config/dotnet-tools.json")" || frc=$?
    if [ "$frc" -eq "$RC_UNREACHABLE" ]; then
      # A read that FAILED is not a repo without a manifest. Conflating the two would report the
      # org's most alarming finding — "this receiver cannot run its own shim" — every time GitHub
      # rate-limited us, and would teach the operator to ignore it (#266).
      printf 'undetermined\t%s\treading .config/dotnet-tools.json failed — %s\n' "$repo" "$(gh_last_err)" >> "$ENGMAN_FILE"
      continue
    fi
    engman_read=$((engman_read + 1))
    if [ "$frc" -ne 0 ]; then
      # 404 — the read SUCCEEDED and the file is not there. An answer, and this sweep's headline.
      printf '%s\t-\n' "$repo" >> "$ENGMAN_DIR/manifest.tsv"
      continue
    fi
    printf '%s' "$body" > "$ENGMAN_DIR/$slug/tools.json"
    printf '%s\t%s\n' "$repo" "$slug/tools.json" >> "$ENGMAN_DIR/manifest.tsv"
  done <<< "$kit_all_receivers"
fi

if [ "$kit_cap_declared" -eq 0 ]; then
  echo "repos-audit: engine-manifest (#1615) — this roster declares no '$KIT_CAP' capability, so this sweep has no subject and graded nothing. NOTHING was asserted about whether any repo can run the fsgg-coord shim it receives."
elif [ "$engman_receivers" -eq 0 ]; then
  echo "repos-audit: engine-manifest (#1615) — the roster declares '$KIT_CAP' and lists NO receiver for it, so this sweep graded nothing. NOTHING was asserted; that is not a clean bill."
else
  FSGG_SCRIPTS_DIR="$HERE" python3 -c "$MANIFEST_PY" "$ENGMAN_DIR" "$ENGMAN_DIR/manifest.tsv" >> "$ENGMAN_FILE" \
    || die "the engine-manifest verdict program failed to run. That is not a clean sweep — no receiver's engine declaration was graded."

  engman_count() { grep -cE "^$1"$'\t' "$ENGMAN_FILE" 2>/dev/null || true; }
  engman_findings="$(engman_count finding)"
  engman_refusals="$(engman_count refusal)"
  engman_undet="$(engman_count undetermined)"
  engman_feed_undet="$(awk -F'\t' '$1 == "undetermined" && $2 == "*" { n++ } END { print n+0 }' "$ENGMAN_FILE")"
  engman_ok="$(engman_count ok)"

  while IFS=$'\t' read -r kind a b; do
    case "$kind" in
      finding)      echo "::error::repos-audit: engine-manifest — $a $b" ;;
      refusal)      echo "::error::repos-audit: engine-manifest REFUSED a manifest it cannot grade — $a: $b" ;;
      undetermined) echo "::error::repos-audit: engine-manifest — $a: $b" ;;
      ok)           echo "  ok: $a $b" ;;
    esac
  done < "$ENGMAN_FILE"

  # GRADED counts the receivers this sweep reached a verdict about. An unread receiver is NOT in it,
  # so the denominator and the numerator cannot silently converge on a run that read nothing.
  engman_graded=$(( engman_ok + engman_findings + engman_refusals ))
  if [ "$engman_graded" -eq 0 ]; then
    echo "repos-audit: engine-manifest (#1615) — none of the $engman_receivers $KIT_CAP receiver(s) could be read, so NOTHING was asserted about whether any of them can run the fsgg-coord shim it receives. That is not a clean bill."
  else
    echo "repos-audit: engine-manifest (#1615) — graded $engman_graded of $engman_receivers $KIT_CAP receiver(s): $engman_ok declare fs.gg.coord.cli, $engman_findings do NOT, $engman_refusals refusal(s), $engman_undet undetermined. This is f(roster, receiver tree) and it replaces the repos.sh validate co-fabric rule #1077 used to carry (ADR-0068) — that rule read only THIS repo's roster, so a receiver that deleted its own manifest stayed green. It does NOT grade the engine VERSION; that is the kit-pin sweep's shape, one package over."
  fi
fi

# --- engine bump-offer (#1803): same verdict vocabulary, JSON tool pins -------------------------
engoffer_subjects=0; engoffer_fetched=0; : > "$ENGOFFER_DIR/manifest.tsv"
while IFS=$'\t' read -r _ repo pin published pinpath; do
  [ -n "$repo" ] || continue
  engoffer_subjects=$((engoffer_subjects + 1)); slug="${repo//\//__}"; mkdir -p "$ENGOFFER_DIR/$slug/head" "$ENGOFFER_DIR/$slug/branch"; offer_err=""
  prc=0; prs="$(gh_api "repos/$repo/pulls?state=open&per_page=100" --jq '[.[] | {number, headRefName: .head.ref}]')" || prc=$?
  if [ "$prc" -ne 0 ]; then offer_err="reading open pull requests failed — $(gh_last_err)"; else
    printf '%s' "$prs" > "$ENGOFFER_DIR/$slug/prs.json"; brc=0; branches="$(gh_api "repos/$repo/branches?per_page=100" --jq '[.[] | {name}]')" || brc=$?
    if [ "$brc" -ne 0 ]; then offer_err="reading branches failed — $(gh_last_err)"; else
      printf '%s' "$branches" > "$ENGOFFER_DIR/$slug/branches.json"
      offer_normalize prs "$ENGOFFER_DIR/$slug/prs.json" "$ENGOFFER_DIR/$slug/prs.tsv" || offer_err="the open pull-request list was not the shape this sweep reads — $(gh_last_err)"
      [ -n "$offer_err" ] || offer_normalize branches "$ENGOFFER_DIR/$slug/branches.json" "$ENGOFFER_DIR/$slug/branches.tsv" || offer_err="the branch list was not the shape this sweep reads — $(gh_last_err)"
    fi
  fi
  if [ -z "$offer_err" ]; then
    engoffer_fetched=$((engoffer_fetched + 1))
    while IFS=$'\t' read -r key ref refslug; do [ -n "$key" ] && offer_candidate_ref "$ref" || continue; hrc=0; head="$(get_repo_file_ref "$repo" "$pinpath" "$ref")" || hrc=$?; [ "$hrc" -eq 0 ] && printf '%s' "$head" > "$ENGOFFER_DIR/$slug/head/$key"; done < "$ENGOFFER_DIR/$slug/prs.tsv"
    while IFS=$'\t' read -r key ref refslug; do [ -n "$key" ] && offer_candidate_ref "$ref" || continue; hrc=0; head="$(get_repo_file_ref "$repo" "$pinpath" "$ref")" || hrc=$?; [ "$hrc" -eq 0 ] && printf '%s' "$head" > "$ENGOFFER_DIR/$slug/branch/$refslug"; done < "$ENGOFFER_DIR/$slug/branches.tsv"
  else printf '%s' "$offer_err" > "$ENGOFFER_DIR/$slug/err"; fi
  printf '%s\t%s\t%s\t%s\t%s\tfs.gg.coord.cli\n' "$repo" "$pin" "$published" "$pinpath" "$slug" >> "$ENGOFFER_DIR/manifest.tsv"
done < <(awk -F'\t' '$1 == "engine-behind"' "$ENGMAN_FILE")
if [ "$engoffer_subjects" -eq 0 ]; then
  engoffer_none=0; engoffer_current=0; engoffer_superseded=0; engoffer_ratelimited=0; engoffer_undet=0
  echo "repos-audit: engine bump-offer (#1803) — no coordination-kit receiver is behind fs.gg.coord.cli, so none needed a bump proposed."
else
  FSGG_SCRIPTS_DIR="$HERE" python3 -c "$OFFER_PY" "$ENGOFFER_DIR" "$ENGOFFER_DIR/manifest.tsv" >> "$ENGOFFER_FILE" || die "the engine bump-offer verdict program failed to run."
  engoffer_count() { grep -cE "^$1"$'\t' "$ENGOFFER_FILE" 2>/dev/null || true; }; engoffer_none="$(engoffer_count offer-none)"; engoffer_current="$(engoffer_count offer-current)"; engoffer_superseded="$(engoffer_count offer-superseded)"; engoffer_ratelimited="$(engoffer_count offer-ratelimited)"; engoffer_undet="$(engoffer_count offer-undetermined)"
  while IFS=$'\t' read -r kind a b; do case "$kind" in offer-current) echo "  engine bump-offer: $a $b";; *) echo "::error::repos-audit: engine bump-offer — $a: $b";; esac; done < "$ENGOFFER_FILE"
  echo "repos-audit: engine bump-offer (#1803) — of $engoffer_subjects behind coordination-kit receiver(s), $engoffer_current have a CURRENT bump open, $engoffer_superseded superseded, $engoffer_ratelimited rate-limited, $engoffer_none have NO bump, $engoffer_undet undetermined. fake-cli is deliberately out of scope: this sweep grades only the engine the shim executes."
fi

# --- the view-root path requirement sweep (historical field: absence-cover; #1785/#1869) ---------
#
# IT RIDES THE DETECTOR PASS'S WORKFLOW READS AND ADDS EXACTLY TWO API CALLS PER RECEIVER — the two
# protection stores. Every workflow it grades was staged by `repo_calls` as that pass read it, so the
# YAML costs nothing here; the protection cannot be ridden, because nothing else in this script reads
# it. Fourteen calls a day against the rate limit this script treats as its main adversary.
#
# THE CREDENTIAL IS THE ONE THING THIS SWEEP NEEDS THAT NO OTHER DOES. `branches/<b>/protection`
# requires `administration: read`, which is NOT a valid `permissions:` scope for a workflow's
# GITHUB_TOKEN — declaring it is a startup validation error that produces no check run at all (the
# #478 blind spot). So repos-audit.yml mints the org dispatch App's installation token for this sweep
# alone, exactly as required-context-coherence.yml does, and hands it over in $ABSENTOK_TOKEN.
# WITHOUT IT EVERY RECEIVER IS A REFUSAL, LOUDLY — a rulesets-only read would answer for FS.GG.Audio
# and lie by omission, which is #574's vacuous green with a different subject.
absentok_findings=0; absentok_refusals=0; absentok_undet=0; absentok_ok=0; absentok_graded=0
absentok_manifest="$ABSENTOK_DIR/manifest.tsv"
if [ "$kit_cap_declared" -eq 0 ]; then
  echo "repos-audit: absence-cover (#1785/#1869) — the roster declares no $KIT_CAP capability, so this view-root path requirement sweep had no subject. NOTHING was asserted."
elif ! roster_absence_cover > "$absentok_manifest" 2>/dev/null; then
  die "cannot enumerate the $KIT_CAP package receivers' absence-cover words — repos.sh absence-cover failed. That is not a clean sweep: no assertion/materialize path requirement was graded."
elif [ ! -s "$absentok_manifest" ]; then
  echo "repos-audit: absence-cover (#1785/#1869) — no $KIT_CAP package receiver is rostered, so this sweep graded nothing. NOTHING was asserted about view-root path requiredness."
else
  # The App token, and ONLY for this sweep. Every other read in this script is a public-repo read the
  # run-scoped token makes correctly, and handing them a repo-scoped installation token would 404 the
  # authority itself. Unset => the gate's own Forbidden path fires and every receiver is a refusal.
  GH_TOKEN="${ABSENTOK_TOKEN:-${GH_TOKEN:-}}" \
    python3 -c "$ABSENTOK_PY" "$CONTEXT_RULE" "$ABSENTOK_DIR" "$absentok_manifest" \
            "$HERE/../.github/workflows" >> "$ABSENTOK_FILE" \
    || die "the absence-cover verdict program failed to run. That is not a clean sweep — no assertion/materialize path requirement was graded."

  absentok_count() { grep -cE "^$1"$'\t' "$ABSENTOK_FILE" 2>/dev/null || true; }
  absentok_findings="$(absentok_count finding)"
  absentok_refusals="$(absentok_count refusal)"
  absentok_undet="$(absentok_count undetermined)"
  absentok_ok="$(absentok_count ok)"

  while IFS=$'\t' read -r kind a b; do
    case "$kind" in
      finding)      echo "::error::repos-audit: absence-cover — $a $b" ;;
      refusal)      echo "::error::repos-audit: absence-cover REFUSED to grade $a — $b" ;;
      undetermined) echo "::error::repos-audit: absence-cover could not read $a — $b" ;;
      ok)           echo "  ok: $a $b" ;;
    esac
  done < "$ABSENTOK_FILE"

  absentok_graded=$(( absentok_ok + absentok_findings ))
  absentok_subjects="$(wc -l < "$absentok_manifest" | tr -d ' ')"
  if [ "$absentok_graded" -eq 0 ]; then
    echo "repos-audit: absence-cover (#1785/#1869) — NONE of the $absentok_subjects $KIT_CAP package receiver(s) could be graded ($absentok_refusals refusal(s), $absentok_undet unread). NOTHING was asserted about view-root path requiredness; that is not a clean bill (#266)."
  else
    echo "repos-audit: absence-cover (#1785/#1869) — graded $absentok_graded of $absentok_subjects $KIT_CAP package receiver(s): $absentok_ok match the roster's path-requiredness word, $absentok_findings do NOT, $absentok_refusals refusal(s), $absentok_undet unread. The historical field name does not claim that receiver per-PR checks can emit §8 absence-class verdicts; those are mutation-proven in FS.GG.Kit's no-generate verify suite."
  fi
fi

# DELIBERATELY NOT folded into `$undetermined`. That counter's diagnostic says "could not determine
# WIRING for N repo(s)", and an unreadable Directory.Packages.props is not a wiring question — the
# per-capability lines a few rows up would be saying "0 undetermined" while the exit-2 message
# blamed wiring. A red that names the wrong subject is the defect this workflow's own comments exist
# to prevent (#327/#335). It gets its own counter and its own sentence, at the same exit code.

# --- the sparse-checkout closure sweep's report (#1529) ------------------------------------------
# Read back from the ledger, because the grading happened inside a subshell. `grep -c` exits 1 on no
# match, which `set -e` would take as fatal, so every count is guarded — and the guard must not
# swallow the number, which is why it is `|| true` on the pipeline rather than a conditional.
sparse_count() { grep -cE "^$1"$'\t' "$SPARSE_FILE" 2>/dev/null || true; }
sparse_findings="$(sparse_count finding)"
sparse_refusals="$(sparse_count refusal)"
sparse_workflows="$(sparse_count workflow)"
sparse_unparseable="$(sparse_count unparseable)"
sparse_noverdict="$(sparse_count noverdict)"
IFS=' ' read -r sp_cross sp_graded sp_patterns sp_clones sp_ungraded sp_rule4 sp_rule4_subjects <<< "$(
  awk -F'\t' '$1 == "counts" { c += $2; g += $3; p += $4; f += $5; u += $6; r += $7; s += $8 }
              END { printf "%d %d %d %d %d %d %d", c, g, p, f, u, r, s }' "$SPARSE_FILE")"

# Findings and refusals are ::error:: annotations — the operator reads the annotation list, not the
# log. The UNGRADED and note lines are not: they say what was NOT asserted, which is information, not
# a defect in anyone's tree.
while IFS=$'\t' read -r kind message; do
  case "$kind" in
    finding)     echo "::error::repos-audit: sparse-checkout closure — $message" ;;
    refusal)     echo "::error::repos-audit: sparse-checkout closure REFUSED a shape it cannot grade — $message" ;;
    # An ::error:: because it drives the run to exit 2, exactly as an unreadable receiver does. It is
    # NOT a finding about anyone's workflow — the annotation says so — but a read that failed and was
    # counted as one must be as visible as the thing it prevented us from seeing (#266).
    noverdict)   echo "::error::repos-audit: sparse-checkout closure could NOT read a rostered repository's git tree, so rule (4) did not run — $message" ;;
    ungraded)    echo "  UNGRADED $message" ;;
    unresolved)  echo "  note $message" ;;
    unparseable) echo "  note $message: this workflow would not parse, so GitHub cannot run it and it cannot fetch anything — not graded" ;;
  esac
done < "$SPARSE_FILE"

# The sweep NEVER claims a green it did not earn. When it found no cross-repo checkout at all it says
# that it asserted nothing, rather than reporting the same "clean" a real audit would print — the
# distinction #1522's own gate draws, and the one criterion (3) of #1529 asks for. The repository
# count is in both spellings, so a roster that silently collapsed is legible either way.
if [ "$sp_cross" -eq 0 ]; then
  echo "repos-audit: sparse-checkout closure (#1529) — read $sparse_workflows workflow(s) across $sparse_repos of $rostered rostered repo(s) ($sparse_unread NOT audited, $sparse_unparseable unparseable) and found NO cross-repo \`actions/checkout\` at all. NOTHING was asserted about this class; that is not a clean bill."
else
  echo "repos-audit: sparse-checkout closure (#1529) — $sp_patterns sparse pattern(s) over $sp_graded of $sp_cross cross-repo checkout(s) fully graded, in $sparse_workflows workflow(s) across $sparse_repos of $rostered rostered repo(s) ($sparse_unread NOT audited, $sparse_unparseable unparseable); $sp_clones full clone(s) not graded; $sp_ungraded step(s) UNGRADED; $sparse_findings finding(s), $sparse_refusals refusal(s)."
  # #1556 criterion 3. The old sentence here made a BLANKET claim — "ran only for checkouts of the ONE
  # tree this audit holds" — which is no longer true and, more to the point, was never checkable: it
  # said what the audit could reach in principle, not what it reached on THIS run. A fraction is,
  # and it moves when the reach does. `sp_rule4_subjects` is every graded cross-repo step; a run
  # where the two numbers differ has the difference named above, one line per step, with its reason.
  echo "repos-audit: sparse-checkout closure — rule (4), do the named directories exist?, ran for $sp_rule4 of $sp_rule4_subjects graded cross-repo step(s): the tree this audit holds, plus every ROSTERED repository whose git tree the API served (#1556). Every step it could not run for is named above with the reason; in cone mode that leaves the step UNGRADED, never ok."
fi

# Undetermined outranks a gap: this run is not a verdict, so it must not be read as one. Any genuine
# gap found alongside it was still printed above as its own ::error::, and survives the next run.
#
# This is the RETRYABLE no-verdict, and the only exit 2 in this script: the subject exists and we
# failed to read it, so a later run may well reach a verdict. Callers retry on 2 alone — never by
# matching this sentence, which is a diagnostic, not an interface.
if [ "$undetermined" -ne 0 ] || [ "$kitpin_undet" -ne 0 ] || [ "$sparse_noverdict" -ne 0 ] \
   || [ "$viewgen_undet" -ne 0 ] || [ "$offer_undet" -ne 0 ] || [ "$engoffer_undet" -ne 0 ] || [ "$engman_undet" -ne 0 ] \
   || [ "$absentok_undet" -ne 0 ]; then
  [ "$undetermined"  -eq 0 ] || echo "::error::repos-audit: could not determine wiring for $undetermined repo(s) — the audit is incomplete and its result means nothing. This is an API failure (rate limit, auth, outage), not a wiring gap." >&2
  [ "$kitpin_undet" -eq 0 ] || echo "::error::repos-audit: could not determine the FS.GG.Kit pin freshness of $kitpin_undet repo(s) — either a pin file or nuget.org would not read. Nothing was proven about their kit; this is an API failure, not a stale pin, and not a wiring gap." >&2
  # Its own counter and sentence, for the reason every counter in this block has one: an unreadable
  # receiver project is not a wiring question, and folding it into `$undetermined` would print
  # "could not determine WIRING" about a file that answers a different question (#327/#335).
  [ "$viewgen_undet" -eq 0 ] || echo "::error::repos-audit: could not read the receiver project of $viewgen_undet repo(s), so nothing was proven about whether their declared view skill root can be generated. This is a failure to READ, not a missing generate target." >&2
  # Its own counter and sentence again, and here the wrong subject is the one #266 is FOR: an unread
  # branch protection is not "this assertion/materialize path is unrequired" and must never render that way.
  # A protection we could not read is UNREAD.
  [ "$absentok_undet" -eq 0 ] || echo "::error::repos-audit: could not read the branch protection or rulesets of $absentok_undet receiver(s), so NOTHING was proven about whether their unexcused view-root assertion/materialize paths are required (#266/#1785/#1869)." >&2
  # Its own counter and sentence for the same reason as the two above, and here the wrong subject
  # would be actively dangerous: an unread PR list means we do not know whether a bump was OFFERED,
  # which is a different question from whether the pin is stale (we KNOW it is — that is why this
  # receiver was a subject at all). Folding it into $kitpin_undet would report "nothing was proven
  # about their kit" over a receiver whose kit staleness was in fact proven, and would lose the one
  # fact the operator needs: that the REMEDY, not the finding, is the unknown.
  [ "$offer_undet" -eq 0 ] || echo "::error::repos-audit: could not determine whether $offer_undet behind receiver(s) have been OFFERED a kit bump — their open pull requests, branches, or a candidate head's pin would not read. They are behind either way; what is unknown is which remedy applies, and 'nobody offered them one' is not the safe guess (#1768/#266)." >&2
  # A rostered repository whose git TREE would not read (#1556). It joins the retryable no-verdicts on
  # the same argument the two above are made on: the subject exists, we failed to read it, and a later
  # run may well reach a verdict. It gets its own counter and sentence rather than being folded into
  # `$undetermined`, because that counter's diagnostic says "could not determine WIRING" and an
  # unreadable git tree is not a wiring question — a red that names the wrong subject is the defect
  # this script's own comments exist to prevent (#327/#335).
  # "an API failure" was too narrow and named the wrong subject for two of the three ways this fires
  # (#1608): the API's tree read is one, the LOCAL git index that would not answer is another, and
  # since #1608 an unreadable local ORIGIN is a third. All three are failures to READ — which is the
  # distinction that matters to a caller, and the one this sentence has to keep. Each step's own
  # reason is annotated above; this line must not overwrite it with a guess.
  [ "$sparse_noverdict" -eq 0 ] || echo "::error::repos-audit: could not read the git tree behind $sparse_noverdict cross-repo sparse-checkout step(s), so rule (4) — do the named directories exist? — did not run for them. Nothing was proven about those steps; this is a failure to READ (the API's tree, or this audit's own checkout — each step's reason is named above), not an under-fetching checkout." >&2
  # Its own counter and sentence, for the reason every counter in this block has one: an unreadable
  # tool manifest is not a wiring question, and it must not be reported as a repo that CANNOT RUN its
  # shim — that is this sweep's loudest finding and the one an operator must be able to trust (#266).
  [ "$engoffer_undet" -eq 0 ] || echo "::error::repos-audit: could not determine whether $engoffer_undet behind coordination-kit receiver(s) were offered an fs.gg.coord.cli bump; unread PR, branch, or JSON-head evidence is not 'no offer'." >&2
  [ "$engman_undet" -eq 0 ] || echo "::error::repos-audit: engine-manifest had $engman_undet no-verdict(s): $engman_feed_undet published fs.gg.coord.cli feed resolution failure(s), plus $(( engman_undet - engman_feed_undet )) unread/unorderable receiver manifest(s). Nothing was proven for those subjects; neither disposition is a missing-engine finding." >&2
  exit 2
fi

# A REFUSED SHAPE is a PERMANENT no-verdict, and it outranks a finding for the same reason exit 2
# outranks one: "I could not grade this" must share a code with neither "it is fine" (#266) nor "it
# is broken" (#320). It sits BELOW the exit-2 check deliberately — an incomplete run may not have
# reached the rest of the org yet, and a caller that retries on 2 alone must be allowed to finish the
# read before being told a file needs editing. Any finding alongside it was still printed above as
# its own ::error::, exactly as a gap survives an undetermined run.
#
# This mirrors `check-sparse-checkout-closure.py`, which refuses the same three shapes (a negated
# pattern, a `sparse-checkout:` key supplying nothing, an unreadable cone-mode flag) with the same
# code, for reasons its docstring gives at length. A skip is how a coherence gate fails open.
if [ "$sparse_refusals" -ne 0 ]; then
  echo "::error::repos-audit: $sparse_refusals cross-repo sparse-checkout step(s) have a SHAPE the closure rule refuses to grade, so nothing was asserted about them. Not a wiring gap, and not transient: a re-run reproduces it. The annotations above name each one." >&2
  exit 3
fi

# A receiver whose FS.GG.Kit pin could not be LOCATED joins the permanent no-verdicts, for the same
# reason a refused sparse shape does: the read succeeded and the answer is still unknown, so it is
# neither "current" (#266) nor "stale" (#320), and a re-run reproduces it exactly. The remedy is a
# commit — either the repo pins where this sweep can see it, or the sweep learns the shape.
if [ "$kitpin_refusals" -ne 0 ]; then
  echo "::error::repos-audit: $kitpin_refusals coordination-kit receiver(s) have an FS.GG.Kit pin this sweep REFUSES to grade — pinned nowhere it knows to look, pinned in two places at once, or pinned to something that is not a version. Nothing was asserted about their freshness. Not transient: a re-run reproduces it. The annotations above name each one." >&2
  exit 3
fi

# A receiver whose view-root generate this sweep cannot ORDER joins the permanent no-verdicts on the
# same argument again: the read succeeded, the file parsed, and the answer is still unknown — so it
# is neither "generates it" (#266) nor "forgot it" (#320), and a re-run reproduces it exactly. The
# remedy is a commit: either the receiver spells the ordering the way the other adopters do, or this
# sweep learns to grade the ordering it uses.
if [ "$viewgen_refusals" -ne 0 ]; then
  echo "::error::repos-audit: $viewgen_refusals receiver(s) declare a view skill root and generate it in a way this sweep REFUSES to order against FsggKitCheckSkillView. Nothing was asserted about whether their next materialize can reach a generated view. Not transient: a re-run reproduces it. The annotations above name each one." >&2
  exit 3
fi

# A receiver whose tool manifest this sweep cannot READ AS A MANIFEST joins the permanent
# no-verdicts on the same argument once more: the fetch succeeded, the bytes are in hand, and the
# question "is the engine declared?" still has no answer — so it is neither "can run it" (#266) nor
# "cannot run it" (#320), and a re-run reproduces it exactly. Note this is NOT the same claim as
# "the manifest is broken": a file that will not parse certainly breaks `dotnet tool restore`, but
# saying so is a different assertion from the one this sweep makes, and it is not this sweep's to make.
if [ "$engman_refusals" -ne 0 ]; then
  echo "::error::repos-audit: $engman_refusals coordination-kit receiver(s) have a .config/dotnet-tools.json this sweep REFUSES to grade — it does not parse as JSON, carries no .tools object, or declares fs.gg.coord.cli with no usable version. Nothing was asserted about whether they can run the fsgg-coord shim they receive. Not transient: a re-run reproduces it. The annotations above name each one." >&2
  exit 3
fi

if [ "$dispatch_refusals" -ne 0 ]; then
  echo "::error::repos-audit: $dispatch_refusals workflow(s) could not be structurally read for the repository_dispatch graph. Nothing was proven about their sender/listener contracts; a re-run will not repair YAML." >&2
  exit 3
fi

# A receiver whose path requirement this sweep REFUSES joins the permanent no-verdicts on the same
# argument once more, and the dominant cause is a credential rather than a commit: reading required
# checks needs `administration: read`, which no workflow GITHUB_TOKEN can hold, so an audit run
# without the org App's installation token refuses EVERY receiver rather than grading them from the
# one store a plain token can see. That half-read is #574's vacuous green, and it is refused here for
# the same reason it was there. The other causes are a workflow that will not parse and a matrix
# whose check-run names cannot be enumerated — a guessed context that happened to be required would
# certify an unguarded repo.
if [ "$absentok_refusals" -ne 0 ]; then
  echo "::error::repos-audit: $absentok_refusals receiver(s) had their path requirement REFUSED rather than graded — most often because this run holds no token with \`administration: read\`, so the required-context set could not be read from BOTH stores. Nothing was asserted about whether their unexcused view-root assertion/materialize paths are required. Not transient: a re-run with the same credential reproduces it. The annotations above name each one (#1785/#1869/#574)." >&2
  exit 3
fi

# A gap and an unrostered adopter are one exit code because they are one CLASS: the audit ran to
# completion and found the roster and the real wiring disagreeing. Both are deterministic, neither is
# transient, and both are fixed by a commit — they differ only in WHICH side is wrong, and the
# ::error:: annotations above say which. Splitting them into two codes would buy a caller nothing it
# cannot read, at the cost of a fourth branch in every consumer of this contract.
#
# A sparse-checkout finding joins them on the same argument, one subject across: the audit ran to
# completion and found a receiver's cross-repo checkout under-fetching. Deterministic, not transient,
# fixed by a commit. Splitting it out would buy a caller a fifth branch to read what the annotation
# already says.
#
# A stale kit pin joins them on the same argument again, a third subject across: the audit ran to
# completion and found a receiver behind the published kit. Deterministic, not transient, fixed by a
# commit in that receiver. It is a FINDING and not a no-verdict precisely because the sweep DID
# reach an answer — which is the whole point of it existing here rather than waiting for the
# receiver to push and discover it in its own `main`.
#
# A receiver that declares a view skill root and does not generate it joins them on the same
# argument, a fourth subject across, and it is the reason #1759 exists: the audit ran to completion
# and found a receiver whose next `FsggKitMaterialize` cannot see its own declared root. It is a
# FINDING and not a no-verdict because the sweep DID reach an answer — from here, with no push from
# that receiver — which is the whole point of grading it centrally rather than waiting for a
# Renovate kit bump to red on a tree nobody touched.
#
# A behind receiver that was never OFFERED a bump — or was offered a superseded one, or has a branch a
# rate limit is sitting on — joins them a fifth subject across (#1768). Every one of those is a
# finding on the same argument: the sweep reached an answer from here, with no push from anybody, and
# the remedy is a specific human action named in the annotation.
#
# It is counted SEPARATELY rather than left to ride $kitpin_findings, even though today every offer
# subject is also a kit-pin finding. The two say different things — "this receiver is stale" and
# "nobody has proposed fixing it" — and the second must not be able to vanish silently if the first
# is ever restructured. An alarm whose firing depends on a neighbouring alarm's implementation is the
# leg that survives its own mutation, which is the defect this whole area keeps rediscovering.
#
# A coordination-kit receiver that cannot run the shim it receives joins them a SIXTH subject across,
# and it is the whole of #1615's AC2: the audit ran to completion and found a repo holding
# `scripts/fsgg-coord` with no engine for it to exec. Deterministic, not transient, fixed by a commit
# in that receiver. It is a FINDING and not a no-verdict because the sweep DID reach an answer, from
# here, by reading that repo's tree — which is exactly the capability the rule it replaces never had.
offer_actionable=$(( offer_none + offer_superseded + offer_ratelimited ))
engoffer_actionable=$(( engoffer_none + engoffer_superseded + engoffer_ratelimited ))
if [ "$gaps" -ne 0 ] || [ "$drift" -ne 0 ] || [ "$sparse_findings" -ne 0 ] || [ "$kitpin_findings" -ne 0 ] \
   || [ "$viewgen_findings" -ne 0 ] || [ "$offer_actionable" -ne 0 ] || [ "$engoffer_actionable" -ne 0 ] || [ "$engman_findings" -ne 0 ] \
   || [ "$absentok_findings" -ne 0 ] || [ "$dispatch_findings" -ne 0 ]; then
  [ "$gaps"  -eq 0 ] || echo "::error::repos-audit: $gaps declared receiver(s) have not wired their capability detector." >&2
  [ "$drift" -eq 0 ] || echo "::error::repos-audit: $drift repo(s) adopt a capability they do not declare — the roster does not describe the org." >&2
  [ "$sparse_findings" -eq 0 ] || echo "::error::repos-audit: $sparse_findings cross-repo sparse-checkout pattern(s) enumerate a file, are unanchored, glob, or select nothing. The fetched script loses its siblings and the caller's job dies at load, in THEIR pipeline rather than here (#1510/#1515/#1522)." >&2
  [ "$kitpin_findings" -eq 0 ] || echo "::error::repos-audit: $kitpin_findings coordination-kit receiver(s) pin an FS.GG.Kit version that is not the newest published one. Their materialized kit is stale NOW; coordination-coherence will only say so on their next push (#1540/#1560/#266)." >&2
  [ "$viewgen_findings" -eq 0 ] || echo "::error::repos-audit: $viewgen_findings receiver(s) declare a view skill root that NOTHING generates before FsggKitCheckSkillView. A view root is absent in every fresh checkout (ADR-0067 §6), so their next materialize reds on a tree nobody touched — including under kit-materialize.yml, a \`uses:\` they cannot add a step to (#1715 B5, #1759)." >&2
  [ "$engman_findings" -eq 0 ] || echo "::error::repos-audit: $engman_findings coordination-kit receiver(s) hold the fsgg-coord shim and declare NO fs.gg.coord.cli in .config/dotnet-tools.json — a tool they receive and cannot run (#1077). Since #1615 (ADR-0068) took the engine manifest off the kit, this is the check that asserts that invariant, and it reads the RECEIVER'S TREE rather than this repo's roster." >&2
  [ "$absentok_findings" -eq 0 ] || echo "::error::repos-audit: $absentok_findings receiver(s) do not match the roster's historical \`absence-cover:\` word for them, or declare none. That word records whether an unexcused view-root assertion/materialize path is branch-required, and live workflows plus protection say otherwise (#1785/#1869)." >&2
  [ "$dispatch_findings" -eq 0 ] || echo "::error::repos-audit: $dispatch_findings repository_dispatch graph mismatch(es): a declared sender/listener/event-type is absent, or a live sender/listener is unrostered (#1919)." >&2
  [ "$offer_actionable" -eq 0 ] || echo "::error::repos-audit: $offer_actionable behind receiver(s) need a human to act at the PROPOSAL step, not the merge step — $offer_none have been offered NO kit bump at all, $offer_superseded have only a superseded one, $offer_ratelimited have a branch a rate limit is holding. Each annotation above names the checkbox and the issue. Nothing else in this org reports these states: the freshness sweep says only 'behind', which is equally true when a bump is sitting open and unmerged (#1768/#1533)." >&2
  [ "$engoffer_actionable" -eq 0 ] || echo "::error::repos-audit: $engoffer_actionable coordination-kit receiver(s) are behind fs.gg.coord.cli and need a proposal-step action — $engoffer_none have NO engine bump, $engoffer_superseded only a superseded one, $engoffer_ratelimited a held branch (#1803)." >&2
  exit 1
fi
# The terminal claim is CONDITIONAL on the sweep having graded somebody. A run that graded nobody
# has not earned the second half of that sentence, and printing it anyway is how "I looked at an
# empty set" becomes "everything is current" (#266).
if [ "$kitpin_graded" -gt 0 ]; then
  echo "repos-audit: OK — every declared receiver is wired, and all $kitpin_graded graded $KIT_CAP receiver(s) pin the published FS.GG.Kit."
else
  echo "repos-audit: OK — every declared receiver is wired. NO kit pin was graded, so nothing is claimed about kit freshness."
fi
# Its own line, on the same argument: a sweep that graded nobody has not earned a clean bill, and
# folding this into the sentence above would let "every receiver pins the published kit" carry a
# claim about view roots that no receiver was examined for.
if [ "$viewgen_graded" -gt 0 ]; then
  echo "repos-audit: OK — all $viewgen_graded receiver(s) that declare a view skill root generate it before FsggKitCheckSkillView."
else
  echo "repos-audit: OK — NO receiver's view-root generate was graded, so nothing is claimed about it."
fi
# The bump-offer sweep's terminal line, conditional on the same argument again — with one difference
# that matters. Reaching here means NO receiver is behind, so the claim it earns is not "every
# receiver has been offered a bump" but "no receiver needed one". Those are not the same sentence and
# the stronger one is unearnable from here: this sweep can only ever speak about receivers the
# freshness sweep handed it, and when the org is current it is handed nobody.
if [ "$offer_subjects" -gt 0 ]; then
  echo "repos-audit: OK — all $offer_subjects behind receiver(s) have a CURRENT kit bump open; the proposal step is working and what remains is a review in each repo."
else
  echo "repos-audit: OK — no receiver was behind, so none needed a bump proposed. Nothing is claimed about Renovate's scheduling, which this audit cannot observe."
fi
# Its own line, on the argument every terminal line in this block is made on: a sweep that graded
# nobody has not earned a clean bill, and the claim must name what it covers. "Can run the engine" is
# NOT "runs the newest engine" — this line deliberately does not say the second thing (#1615/#266).
if [ "$engman_graded" -gt 0 ]; then
  echo "repos-audit: OK — all $engman_graded graded $KIT_CAP receiver(s) declare fs.gg.coord.cli, so every repo that receives the fsgg-coord shim can restore the engine it execs (#1077's invariant, asserted per ADR-0068). Nothing is claimed about which engine VERSION they pin."
else
  echo "repos-audit: OK — NO receiver's engine manifest was graded, so nothing is claimed about whether any repo can run the fsgg-coord shim it receives."
fi
