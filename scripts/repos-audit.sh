#!/usr/bin/env bash
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
# Usage:
#   repos-audit.sh [--registry <file>] [--repos-sh <path>]
# Exit:
#   0 = every declared receiver is wired, and every coordination-kit receiver pins the published kit
#   1 = at least one gap — a declared receiver is unwired, a repo adopted a capability it never
#       rostered, a cross-repo sparse-checkout enumerates a file, or a coordination-kit receiver's
#       FS.GG.Kit pin is behind the newest published one
#   2 = no verdict, RETRYABLE — receiver evidence could not be read (rate limit, auth, outage), or
#       the NuGet feed the kit pins are graded against could not be read
#   3 = no verdict, PERMANENT — a roster that cannot be enumerated, a capability that names no
#       receiver, a capability that is RECEIVED but has no detector (#628), a cross-repo
#       sparse-checkout whose SHAPE the closure rule refuses to grade, a coordination-kit receiver
#       whose FS.GG.Kit pin cannot be located or contradicts itself, or a bad invocation
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
# `unparseable`, and one `counts` line per workflow read.
SPARSE_FILE="$(mktemp)"
# The kit-pin freshness sweep's ledger (#1540), same line-oriented shape as SPARSE_FILE: `finding`,
# `refusal`, `undetermined`, `ok`, and one `published` line naming the version everything was graded
# against. A FILE for the same reason — the grading happens in a `$( )`.
KITPIN_FILE="$(mktemp)"
# Where that sweep stages the pin-bearing files it fetched, so the verdict program can parse XML
# instead of bash grepping it. One subdirectory per repo; see THE KIT-PIN FRESHNESS SWEEP.
KITPIN_DIR="$(mktemp -d)"
trap 'rm -rf "$GH_ERR_FILE" "$CALLS_ERR_FILE" "$SPARSE_FILE" "$KITPIN_FILE" "$KITPIN_DIR"' EXIT
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
get_repo_file() {   # <repo> <repo-relative path> -> raw text; rc per gh_api
  gh_api -H "Accept: application/vnd.github.raw" "repos/$1/contents/$2"
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
ROOTS = (".claude/skills", ".codex/skills", ".agents/skills")
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
# WHAT IT CANNOT SEE, SAID OUT LOUD. Rule (4) asks a git index, and this audit holds exactly one
# tree: the authority's. So a cross-repo checkout of any OTHER repository gets rules (1)-(3) only,
# and if it is in CONE MODE — actions/checkout's default, where a file name is simply a directory
# that turns out to be empty — rules (1)-(3) cannot detect enumeration at all. Such a step is counted
# UNGRADED and named on its own line; it is never printed ok and never folded into the sweep's claim.
# The motivating case is unaffected: a sibling hand-rolling a fetch of FS-GG/.github is a fetch of
# the tree this audit DOES hold, so rule (4) runs and a cone-mode `scripts/check-foo.py` reds.
#
# WHAT IS SHARED, AND WHAT IS HONESTLY NOT. Every RULE is imported: the four graded rules and their
# wording (`grade_pattern`), how the runner splits the input and which empty spellings are refused
# (`patterns_of`), the cone-mode default (`cone_mode_of`), which steps are subjects at all
# (`sparse_steps`), and how "ours" and the tracked set are derived (`origin_repository`,
# `tracked_paths`). None of it is restated below — grep for the rule's own wording and it is not
# here, which is what the fixture's first sparse leg pins.
#
# What IS restated is the per-step ORCHESTRATION — roughly fifteen lines deciding `resolvable` and
# `enumeration_checked` and keeping counts — because the gate applies its rules inline in `main()`,
# interleaved with argparse, its own tree walk and its own printing, and there is no seam to import.
# That is a smaller instance of the same disease and it is not pretended otherwise: it is recorded as
# a follow-up rather than fixed here, because hoisting a seam means editing
# check-sparse-checkout-closure.py, which #1530 holds. This comment is the marker, not the excuse.
#
# Stdin: one workflow as JSON. Argv: the rule file, the authority tree, and the `where` prefix.
# Stdout: tab-separated records, one per line. Every message is flattened to a single line, because
# the ledger is line-oriented and a finding that spanned lines would be read as several records.
SPARSE_VERDICT_PY="$(cat <<'PY'
import importlib.util, json, sys

RULE, TREE, WHERE = sys.argv[1], sys.argv[2], sys.argv[3]

# By PATH, not by name: the module's filename has hyphens, so it is not a legal `import` target, and
# putting scripts/ on sys.path to reach it by some alias would be a second name for one file. Loading
# it does not run its main() — it is guarded by `if __name__ == "__main__"` — and its own top-level
# sys.path insert is what makes its `lib.gate` import resolve, wherever this is run from.
spec = importlib.util.spec_from_file_location("sparse_closure_rule", RULE)
rule = importlib.util.module_from_spec(spec)
spec.loader.exec_module(rule)

# THE BORROWED SURFACE, NAMED. Importing across a file boundary makes these seven names an interface,
# and an interface nobody wrote down is one the next refactor is entitled to break. #1530 is in
# flight to hoist the PARSE (`patterns_of`, `cone_mode_of`) into scripts/lib/sparse.py, and its
# criterion 2 is that neither caller retains a private copy — so this is not a hypothetical.
#
# Asserted up front, and by NAME, so the failure is a sentence an operator can act on rather than an
# AttributeError from somewhere in the loop. It fails CLOSED: the caller records a refusal and the
# audit exits 3, because a rule that cannot be loaded must never round to "no repository enumerates
# anything" across ten repositories.
BORROWED = ("sparse_steps", "patterns_of", "cone_mode_of", "grade_pattern",
            "origin_repository", "tracked_paths", "GateError")
missing = [name for name in BORROWED if not hasattr(rule, name)]
if missing:
    sys.exit("%s no longer exposes %s. repos-audit.sh borrows the sparse-checkout closure rule from "
             "it rather than restating it (#1529); if the rule moved — #1530 hoists the parse into "
             "scripts/lib/sparse.py — retarget this loader at its new home. Refusing to grade, "
             "because a missing rule must not look like a clean org."
             % (RULE, ", ".join(missing)))


def emit(kind, text):
    print("%s\t%s" % (kind, " ".join(str(text).split())))


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

for job_id, params in rule.sparse_steps(document):
    where = "%s (job `%s`)" % (WHERE, job_id)
    cross += 1
    # Refusals are COLLECTED, never raised on sight — the rule file's own discipline: one unreadable
    # step must not suppress every real finding in the rest of the org.
    try:
        patterns = rule.patterns_of(params, where)
        if patterns is None:
            clones += 1          # a full clone under-fetches nothing, so it is not a subject
            continue
        cone = rule.cone_mode_of(params, where)
    except rule.GateError as error:
        emit("refusal", error)
        continue

    repository = str(params.get("repository") or "").strip()
    resolved = tree()
    resolvable = (
        resolved["ours"] is not None
        and resolved["tracked"] is not None
        and repository.casefold() == resolved["ours"].casefold()
    )
    # Identical to the rule file's own reasoning: in non-cone mode the trailing-slash rule decides
    # enumeration from the pattern alone; in cone mode ONLY rule (4) can, so a cone-mode fetch of a
    # tree we do not hold has NOTHING asserted about it.
    enumeration_checked = (not cone) or resolvable

    step_findings = []
    try:
        for pattern in patterns:
            pattern_count += 1
            step_findings.extend(rule.grade_pattern(
                pattern, cone=cone, where=where,
                tracked=resolved["tracked"] if resolvable else None))
    except rule.GateError as error:
        emit("refusal", error)
        continue

    for finding in step_findings:
        emit("finding", finding)
    if enumeration_checked:
        graded += 1
    else:
        ungraded += 1
        emit("ungraded", "%s: cone mode against %r, a tree this audit does not hold — NOTHING was "
                         "asserted about whether these patterns name files or directories"
                         % (where, repository))
    if not resolvable:
        emit("unresolved", "%s: fetches %r, which is not the tree this audit holds (%s) — rule (4), "
                           "the existence of its directories, was NOT checked"
                           % (where, repository, resolved["ours"] or "origin unreadable"))

print("counts\t%d\t%d\t%d\t%d\t%d" % (cross, graded, pattern_count, clones, ungraded))
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
  local json
  local where="$1/.github/workflows/$2"
  printf 'workflow\t%s\n' "$where" >> "$SPARSE_FILE"
  json="$(yaml_text2json)" || json=""
  if [ -z "$json" ] || [ "$json" = "null" ]; then
    printf 'unparseable\t%s\n' "$where" >> "$SPARSE_FILE"
    return 0
  fi
  python3 -c "$SPARSE_VERDICT_PY" "$SPARSE_RULE" "$AUTHORITY_TREE" "$where" <<< "$json" \
      >> "$SPARSE_FILE" \
    || printf 'refusal\t%s: the shared sparse-checkout closure rule could not be evaluated against this workflow\n' \
         "$where" >> "$SPARSE_FILE"
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
        version = el.get("Version")
        if version is None:
            child = next((c for c in el if localname(c.tag) == "Version"), None)
            version = child.text if child is not None else None
        if version is None:
            continue          # a version-less PackageReference IS the CPM shape: pin is elsewhere.
        version = version.strip()
        if not version:
            continue
        found.append(version)
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
try:
    live = fsgg_feed.nuget_org_versions(PACKAGE)
    stable = [v for v in live if not fsgg_feed.is_prerelease(v)]
    if not stable:
        raise fsgg_feed.GateError(
            f"nuget.org serves {len(live)} version(s) of {PACKAGE} and every one is a prerelease, "
            f"so there is no stable version a receiver could pin."
        )
    published = fsgg_feed.newest(stable)
except fsgg_feed.GateError as e:
    emit("undetermined", "*", f"could not resolve the published {PACKAGE} version: {e}")
    print("\n".join(out))
    sys.exit(0)

emit("published", published)

for repo in order:
    literals = []
    for repopath, local in repos[repo]:
        try:
            with open(local, encoding="utf-8") as fh:
                text = fh.read()
        except OSError as e:
            emit("undetermined", repo, f"staged {repopath} unreadable: {e}")
            break
        try:
            for v in versions_in(text):
                literals.append((repopath, v))
        except ET.ParseError as e:
            emit("refusal", repo,
                 f"{repopath} is not parsable XML ({e}), so this repo's {PACKAGE} pin could not be "
                 f"read. Unparsable is not unpinned and it is not current.")
            break
    else:
        distinct = {v for _, v in literals}
        where = ", ".join(f"{p} -> {v}" for p, v in literals)
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
                emit("refusal", repo, f"{PACKAGE} pin {pin!r} ({where}) is not a parsable NuGet version: {e}")
                continue
            if behind:
                emit("finding", repo,
                     f"pins {PACKAGE} {pin} ({where}) but {published} is published on nuget.org. The "
                     f"receiver's materialized kit is whatever {pin} shipped, so coordination-coherence "
                     f"will red on its `main` the next time it pushes — and reads green until then.")
            elif ahead:
                emit("finding", repo,
                     f"pins {PACKAGE} {pin} ({where}), which is AHEAD of the newest published {published}. "
                     f"No receiver can restore that version; this pin does not resolve.")
            else:
                emit("ok", repo, f"pins {PACKAGE} {pin} ({where})")

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
    CAP_SUBJ["$cap"]="a $AUTHORITY/.github/workflows/skill-union-assert.yml caller aimed at this repo's OWN committed .claude/.codex/.agents skill roots"
    CAP_ARM["$cap"]="a pull_request trigger covering .claude/skills/**, .codex/skills/** and .agents/skills/** (or no paths: filter at all)"
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
kit_roster="$(roster_list coordination-kit)" \
  || die "cannot enumerate the coordination-kit receivers — repos.sh list --receives coordination-kit failed."
: > "$KITPIN_DIR/manifest.tsv"
while IFS= read -r repo; do
  [ -n "$repo" ] || continue
  kitpin_receivers=$((kitpin_receivers + 1))
  slug="${repo//\//__}"
  mkdir -p "$KITPIN_DIR/$slug"
  kit_repo_ok=1; kit_n=0
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
    printf '%s\t%s\t%s\n' "$repo" "$candidate" "$slug/$kit_n" >> "$KITPIN_DIR/manifest.tsv"
  done
  if [ "$kit_repo_ok" -eq 1 ]; then
    kitpin_read=$((kitpin_read + 1))
    # A receiver whose every candidate 404'd still needs a row, or the verdict program never sees it
    # and a repo that pins NOWHERE would vanish from the sweep instead of being refused.
    [ "$kit_n" -gt 0 ] || printf '%s\t-\t-\n' "$repo" >> "$KITPIN_DIR/manifest.tsv"
  fi
done <<< "$kit_roster"

# The sweep is only as meaningful as the set it covered. A roster with no coordination-kit receiver
# at all must not print the same clean line a real sweep prints (#503's per-capability vacuity
# argument, one sweep over) — but it is not this sweep's job to REFUSE it either: "coordination-kit
# is declared and nobody receives it" is already a hard failure in the per-capability guard above,
# and duplicating that verdict here would report the same defect twice under a wrong name. So it
# says out loud that it asserted nothing, and grades nobody.
if [ "$kitpin_receivers" -eq 0 ]; then
  echo "repos-audit: kit-pin freshness (#1540) — the roster names NO coordination-kit receivers, so NO pin was graded against the published FS.GG.Kit. NOTHING was asserted about this class; that is not a clean bill."
  kitpin_findings=0; kitpin_refusals=0; kitpin_undet=0
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

  if [ -z "$kitpin_published" ]; then
    echo "repos-audit: kit-pin freshness (#1540) — the published FS.GG.Kit version could not be resolved, so NOTHING was asserted about any receiver's pin. That is not a clean sweep."
  else
    echo "repos-audit: kit-pin freshness (#1540) — newest stable FS.GG.Kit published on nuget.org is $kitpin_published; graded $kitpin_read of $kitpin_receivers coordination-kit receiver(s): $kitpin_ok current, $kitpin_findings stale-or-unresolvable, $kitpin_refusals refusal(s), $kitpin_undet undetermined. This is f(roster, feed) — no receiver has to push for it to change."
  fi
fi

# Fold into the run's verdict on the same argument the two sweeps above use: an unread receiver is
# retryable, a shape we refuse to grade is permanent, a graded receiver that is behind is a finding.
undetermined=$((undetermined + kitpin_undet))

# --- the sparse-checkout closure sweep's report (#1529) ------------------------------------------
# Read back from the ledger, because the grading happened inside a subshell. `grep -c` exits 1 on no
# match, which `set -e` would take as fatal, so every count is guarded — and the guard must not
# swallow the number, which is why it is `|| true` on the pipeline rather than a conditional.
sparse_count() { grep -cE "^$1"$'\t' "$SPARSE_FILE" 2>/dev/null || true; }
sparse_findings="$(sparse_count finding)"
sparse_refusals="$(sparse_count refusal)"
sparse_workflows="$(sparse_count workflow)"
sparse_unparseable="$(sparse_count unparseable)"
IFS=' ' read -r sp_cross sp_graded sp_patterns sp_clones sp_ungraded <<< "$(
  awk -F'\t' '$1 == "counts" { c += $2; g += $3; p += $4; f += $5; u += $6 }
              END { printf "%d %d %d %d %d", c, g, p, f, u }' "$SPARSE_FILE")"

# Findings and refusals are ::error:: annotations — the operator reads the annotation list, not the
# log. The UNGRADED and note lines are not: they say what was NOT asserted, which is information, not
# a defect in anyone's tree.
while IFS=$'\t' read -r kind message; do
  case "$kind" in
    finding)     echo "::error::repos-audit: sparse-checkout closure — $message" ;;
    refusal)     echo "::error::repos-audit: sparse-checkout closure REFUSED a shape it cannot grade — $message" ;;
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
  echo "repos-audit: sparse-checkout closure (#1529) — $sp_patterns sparse pattern(s) over $sp_graded of $sp_cross cross-repo checkout(s) fully graded, in $sparse_workflows workflow(s) across $sparse_repos of $rostered rostered repo(s) ($sparse_unread NOT audited, $sparse_unparseable unparseable); $sp_clones full clone(s) not graded; $sp_ungraded step(s) UNGRADED; $sparse_findings finding(s), $sparse_refusals refusal(s). Rule (4) — do the named directories exist? — ran only for checkouts of the ONE tree this audit holds, its own; every other repository's is named above as unresolved, and in cone mode that leaves the step UNGRADED."
fi

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
if [ "$gaps" -ne 0 ] || [ "$drift" -ne 0 ] || [ "$sparse_findings" -ne 0 ] || [ "$kitpin_findings" -ne 0 ]; then
  [ "$gaps"  -eq 0 ] || echo "::error::repos-audit: $gaps declared receiver(s) have not wired their capability detector." >&2
  [ "$drift" -eq 0 ] || echo "::error::repos-audit: $drift repo(s) adopt a capability they do not declare — the roster does not describe the org." >&2
  [ "$sparse_findings" -eq 0 ] || echo "::error::repos-audit: $sparse_findings cross-repo sparse-checkout pattern(s) enumerate a file, are unanchored, glob, or select nothing. The fetched script loses its siblings and the caller's job dies at load, in THEIR pipeline rather than here (#1510/#1515/#1522)." >&2
  [ "$kitpin_findings" -eq 0 ] || echo "::error::repos-audit: $kitpin_findings coordination-kit receiver(s) pin an FS.GG.Kit version that is not the newest published one. Their materialized kit is stale NOW; coordination-coherence will only say so on their next push (#1540/#1560/#266)." >&2
  exit 1
fi
echo "repos-audit: OK — every declared receiver is wired, and every coordination-kit receiver pins the published FS.GG.Kit."
