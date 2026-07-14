#!/usr/bin/env bash
# case: superseded run 720
# tier: full
# covers: adopt landable
#
# Lifted VERBATIM from the fsgg-coord monolith. The world it runs against — fixtures, the counting
# `gh` stub, the seeders, the ADR-0027 parallel-work board and its pre-existing claims — comes from
# lib/harness.sh, which is the monolith's own prelude. Nothing here was rewritten to make it pass.
set -euo pipefail
CASE_NAME="31-superseded-run-720"
. "$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)/lib/harness.sh"

# ---- #720: a SUPERSEDED run is not a RED one — and `adopt` refused GREEN, force-pushed work. -----
#
# `pr_landable` aggregated every check-run on the head SHA. But the same workflow can run TWICE on one
# SHA — a second trigger starts a fresh run and `concurrency: cancel-in-progress` CANCELS the first —
# and BOTH runs' check-runs stay attached to that SHA forever. The raw aggregate sees `cancelled` and
# calls a GREEN PR red.
#
# It is `adopt` that pays. Its whole population is PRs whose author was ITERATING when their harness
# died — i.e. FORCE-PUSHED PRs, which are exactly the ones carrying superseded runs. So the tool
# pointed at green, mergeable, finished work and refused to land it: #697's remedy failing precisely
# where #697 was aimed. Measured on .github#718 — 20 check-runs, 4 cancelled from two superseded
# suites, while GitHub itself said `mergeable_state=clean`.
#
# The verdict is now scored over WORKFLOW RUNS, because supersession is a property of the RUN and is
# not visible on a check-run at all. Every leg below is a state a real PR reaches.
lnd() { PATH="$STUB:$PATH" GH_BOARD_SET=pw GH_ISSUES_FROM_STORE=1 \
        bash "$COORD" --worker heron-720 landable "$1" --repo FS-GG/FS.GG.SDD 2>/dev/null || true; }
# every leg's PR is mergeable; only its runs and checks differ
lnd_pr() { jq -n --argjson n "$1" --arg s "$2" '{number:$n, mergeable:true, head:{ref:"item/1-x", sha:$s}}' \
           >"$FIXTURES/pr-$1.json"; }
# lnd_wf <sha> <suite> <rn> <status> <conclusion> [event] -> one workflow_run, as GitHub shapes it
lnd_wf() { jq -n --arg su "$2" --argjson rn "$3" --arg st "$4" --arg c "$5" --arg e "${6:-pull_request}" \
  '{path:".github/workflows/gate.yml", event:$e, head_branch:"item/1-x", run_number:$rn,
    check_suite_id:($su|tonumber), status:$st, conclusion:(if $c=="" then null else $c end),
    pull_requests:(if $e=="pull_request" then [{number:1}] else [] end)}'; }
lnd_runs() { local s="$1"; shift; jq -n --slurpfile r /dev/stdin '{workflow_runs:$r}' >"$FIXTURES/runs-$s.json"; }
# lnd_cr <suite> <status> <conclusion> [app] -> one check_run, as GitHub shapes it
lnd_cr() { jq -n --arg su "$1" --arg st "$2" --arg c "$3" --arg a "${4:-github-actions}" \
  '{name:"job", check_suite:{id:($su|tonumber)}, status:$st,
    conclusion:(if $c=="" then null else $c end), app:{slug:$a}}'; }
lnd_checks() { local s="$1"; shift; jq -n --slurpfile c /dev/stdin '{check_runs:$c}' >"$FIXTURES/checks-$s.json"; }

# 1. THE BUG, IN THE SHAPE .github#718 ACTUALLY CARRIED. A cancelled run REPLACED by a later run of its
#    own concurrency group is SUPERSEDED — it is evidence of nothing — and SO ARE ITS CHECK-RUNS, which
#    stay attached to the SHA forever. Both must be dropped, and the PR is GREEN.
lnd_pr 801 sha801
{ lnd_wf sha801 111 1 completed cancelled; lnd_wf sha801 222 2 completed success; } | lnd_runs sha801
{ lnd_cr 111 completed cancelled;          lnd_cr 222 completed success;          } | lnd_checks sha801
assert_eq "#720: a cancelled run REPLACED by a later run of its own group is superseded — the PR is GREEN" \
  "green" "$(lnd 801)"

# 2. ...and the drop rule must not become a hole. A cancelled run that NOBODY re-ran is a finding.
lnd_pr 802 sha802
lnd_wf sha802 111 1 completed cancelled | lnd_runs sha802
lnd_cr 111 completed cancelled          | lnd_checks sha802
assert_eq "#720: a cancelled run with NO later run of its group is still RED — the drop rule is not a hole" \
  "red" "$(lnd 802)"

# 3. THE TRAP THE OBVIOUS FIX FALLS INTO. A `workflow_dispatch` run on the same branch shares the SHA
#    and the workflow PATH and carries a HIGHER run_number — but it is a different `github.ref`, so it
#    supersedes NOTHING. And a gate job that is `if: github.event_name == 'pull_request'` SKIPS inside
#    it and still concludes `success`. Keying supersession on the path alone would let that vacuous
#    green license the drop of a real cancelled run, and the PR would merge unchecked (#703).
lnd_pr 803 sha803
{ lnd_wf sha803 111 1 completed cancelled
  lnd_wf sha803 333 9 completed success workflow_dispatch; } | lnd_runs sha803
{ lnd_cr 111 completed cancelled; lnd_cr 333 completed success; } | lnd_checks sha803
assert_eq "#720: a workflow_dispatch run does NOT supersede the PR run it shares a SHA with — still RED" \
  "red" "$(lnd 803)"

# 4. #606 SURVIVES THE REWRITE. Zero runs is an EMPTY SUBJECT, not a clean one — and it is the state a
#    CONFLICTED PR is permanently in, since GitHub cannot build refs/pull/N/merge while it conflicts.
lnd_pr 804 sha804
echo '{"workflow_runs":[]}' >"$FIXTURES/runs-sha804.json"
echo '{"check_runs":[]}'    >"$FIXTURES/checks-sha804.json"
assert_eq "#720: ZERO workflow runs is a FINDING, not a pass — #606 survives the rewrite" \
  "red" "$(lnd 804)"

# 5. A run still going is not a run that passed.
lnd_pr 805 sha805
lnd_wf sha805 111 1 in_progress "" | lnd_runs sha805
lnd_cr 111 in_progress ""          | lnd_checks sha805
assert_eq "#720: an in-flight run is PENDING, never green" "pending" "$(lnd 805)"

# 6. THE FAIL-OPEN THE FIX ITSELF COULD HAVE INTRODUCED. Workflow runs see GitHub ACTIONS AND NOTHING
#    ELSE. This function USED to read check-runs, which see EVERY app — so scoring runs alone would
#    have SILENTLY DROPPED third-party coverage and returned `green` while ignoring the one check that
#    failed. That is #266's signature, introduced by the fix for it. A foreign app's suite is never in
#    the runs list, so it is never in `dead`, so it is scored by construction — no special case.
lnd_pr 806 sha806
lnd_wf sha806 111 1 completed success | lnd_runs sha806
{ lnd_cr 111 completed success; lnd_cr 999 completed failure codecov; } | lnd_checks sha806
assert_eq "#720: a FAILING third-party check still reds the PR — the Actions rollup must not go blind" \
  "red" "$(lnd 806)"

# 7. A RUN CAN FAIL WITH NO CHECK RUNS AT ALL. `startup_failure` — malformed workflow YAML — concludes
#    the RUN `failure` and never creates a job, so there is no check-run to find.
#
#    THE SECOND WORKFLOW IS THE ASSERTION. A PR with a broken workflow still has OTHER workflows, and
#    theirs are green — so a check-runs-only rollup sees an all-green set and merges a PR whose
#    workflow never even parsed. Without that green sibling this leg passes for the WRONG reason: the
#    check-run set would be EMPTY, and #606's empty-subject rule would red it anyway, proving nothing
#    about the run half. (Caught by mutating the rollup to check-runs-only and watching this leg stay
#    green — the fixture, not the code, was the thing at fault.)
lnd_pr 807 sha807
{ lnd_wf sha807 111 1 completed startup_failure
  lnd_wf sha807 222 1 completed success;         } | lnd_runs sha807
lnd_cr 222 completed success                       | lnd_checks sha807
assert_eq "#720: a startup_failure run reds the PR — even though it produced NO check runs, and its sibling workflow is green" \
  "red" "$(lnd 807)"

# 8. ...AND THE MIRROR: A CHECK RUN CAN FAIL WHILE ITS RUN SUCCEEDS. A JOB-level `continue-on-error:
#    true` concludes the job's check-run `failure` and the workflow run `success`. Branch protection
#    scores CHECK RUNS — so scoring runs alone would call this green on a PR GitHub itself would
#    refuse to merge. The two rollups each see a failure the other cannot; the verdict is their UNION.
lnd_pr 808 sha808
lnd_wf sha808 111 1 completed success | lnd_runs sha808
lnd_cr 111 completed failure          | lnd_checks sha808
assert_eq "#720: a FAILED check-run reds the PR even though its workflow run concluded success" \
  "red" "$(lnd 808)"

# 9. A REAL PAYLOAD, NOT A TOY ONE. The rollup used to hand both lists to jq through `--argjson`, i.e.
#    through ARGV — and Linux caps a SINGLE argv string at 128KB (MAX_ARG_STRLEN, far below the ~2MB
#    ARG_MAX everyone quotes). A workflow_run carries whole `repository` / `head_repository` /
#    `head_commit` objects, so ELEVEN real runs are already ~150KB: jq died with "Argument list too
#    long", pr_landable returned `unknown`, and `adopt` refused every real PR in this repo.
#
#    EVERY UNIT TEST ABOVE STILL PASSED, because a fixture's run is three fields long. Only driving it
#    against a live PR found it. So the fixture here is deliberately FAT — one green run and one green
#    check, each padded past the single-argument ceiling — and it asserts the verdict is still `green`.
#    A rollup that goes back through argv cannot survive this leg.
lnd_pr 809 sha809
# --rawfile, NOT --arg: the pad has to reach jq WITHOUT going through argv, or BUILDING the fixture
# trips the very 128KB ceiling this leg exists to pin. (It did, first time. The limit is real, it is
# low, and it is easy to walk into twice.)
padfile="$WORK/fat-pad.txt"; head -c 200000 /dev/zero | tr '\0' 'x' >"$padfile"
jq -n --rawfile pad "$padfile" \
  '{workflow_runs:[{path:".github/workflows/gate.yml",event:"pull_request",head_branch:"item/1-x",
                    run_number:1,check_suite_id:111,status:"completed",conclusion:"success",
                    pull_requests:[{number:1}],repository:{description:$pad}}]}' >"$FIXTURES/runs-sha809.json"
jq -n --rawfile pad "$padfile" \
  '{check_runs:[{name:"job",check_suite:{id:111},status:"completed",conclusion:"success",
                 app:{slug:"github-actions"},output:{text:$pad}}]}' >"$FIXTURES/checks-sha809.json"
assert_eq "#720: a REAL-SIZED payload still rolls up — the verdict must not go through argv (128KB cap)" \
  "green" "$(lnd 809)"

# 10. The exit CODE carries the decision, so a poll loop can tell "keep waiting" from "stop" without
#    parsing prose. This is the contract `/pnext-item` §5 will be rewritten against (#724).
#
#    `|| rc=$?` is NOT optional: this file runs under `set -e`, and the whole POINT of these legs is
#    that the command exits NON-ZERO. Letting that status escape kills the suite ON A PASSING
#    ASSERTION — silently, and with every later assertion unrun. Grepping the output for FAIL cannot
#    see it (there is no FAIL; there is simply nothing after this line), and the run still LOOKS like
#    519 green assertions. Only the exit code betrays it. Caught exactly that way while writing this.
lnd_rc() {  # <pr> -> the command's exit status, captured rather than inherited
  local rc=0
  PATH="$STUB:$PATH" GH_BOARD_SET=pw GH_ISSUES_FROM_STORE=1 \
    bash "$COORD" --worker heron-720 landable "$1" --repo FS-GG/FS.GG.SDD >/dev/null 2>&1 || rc=$?
  printf '%s' "$rc"
}
assert_eq "#720: landable exits 0 on green" "0" "$(lnd_rc 801)"
assert_eq "#720: landable exits 3 on pending — the ONLY verdict worth retrying" "3" "$(lnd_rc 805)"
assert_eq "#720: landable exits 1 on red — do not merge, and do not wait" "1" "$(lnd_rc 802)"


harness_report
