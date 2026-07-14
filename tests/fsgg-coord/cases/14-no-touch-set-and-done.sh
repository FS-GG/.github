#!/usr/bin/env bash
# case: no touch set and done
# tier: full
# covers: lint done epic_rollup
#
# Lifted VERBATIM from the fsgg-coord monolith. The world it runs against — fixtures, the counting
# `gh` stub, the seeders, the ADR-0027 parallel-work board and its pre-existing claims — comes from
# lib/harness.sh, which is the monolith's own prelude. Nothing here was rewritten to make it pass.
set -euo pipefail
CASE_NAME="14-no-touch-set-and-done"
. "$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)/lib/harness.sh"

# --- state the monolith inherited from an earlier section; stated explicitly here ---
lint_json="$(run lint --json 2>/dev/null || true)"

# ---- NO-TOUCH-SET (#496): the rule whose absence let lint be GREEN over a DEAD queue ------------
# Every other lint rule validates the epic roll-up graph. None asked the only question a worker has:
# CAN ANYONE PICK THIS UP? A Ready/Backlog item with no `Paths:` is refused by batch/take — correctly
# — so it sits on the board looking like work and is invisible to every worker who asks for work.
# Nine had accumulated in `.github` while lint said `0 error(s)`.
#
# The NEGATIVES matter more than the positives here. A rule that fires on every touch-set-less item
# would fire on every epic, and a gate that is always red is a gate nobody reads — which is how the
# original state (always green) and the naive fix (always red) fail in exactly the same way.
nts() { jq -r "[.[] | select(.code==\"NO-TOUCH-SET\") | .id | sub(\"^[^/]+/\";\"\")] | sort | join(\",\")" <<<"$lint_json"; }
assert_eq "lint: NO-TOUCH-SET fires on EXACTLY the unschedulable items" \
  "FS.GG.SDD#420,FS.GG.SDD#421" "$(nts)"

# #420 — Ready, real work, forgot its touch-set. The whole point.
assert_contains "lint: NO-TOUCH-SET says nobody can ever pick it up" "no worker can ever pick it up" \
  "$(jq -r '.[] | select(.code=="NO-TOUCH-SET" and (.id|test("420"))) | .detail' <<<"$lint_json")"
assert_contains "lint: NO-TOUCH-SET offers the sentinel by name" "Paths: none" \
  "$(jq -r '.[] | select(.code=="NO-TOUCH-SET" and (.id|test("420"))) | .detail' <<<"$lint_json")"

# #421 — its ONLY Paths: line is FENCED. The scheduler cannot see it, so the item declares nothing.
# A checker that did not track fences would call it healthy while `take` kept refusing it — the gate
# failing open on the one state it exists to catch. Same fence rule, both sides (#277).
assert_contains "lint: a FENCED-only Paths: line is no declaration at all (fails closed)" \
  "FS.GG.SDD#421" "$(nts)"

# --- the negatives ---
# #400 is an epic carrying `Paths: none`. It is unschedulable and that is CORRECT — the sentinel is
# what makes the absence deliberate. If it fired here, every epic on the board would be an error.
case "$(nts)" in *400*) bad "lint: NO-TOUCH-SET must NOT fire on an epic that declares 'Paths: none'" "$(nts)" ;;
                 *)     ok  "lint: 'Paths: none' suppresses NO-TOUCH-SET — the sentinel is the whole point" ;; esac
case "$(nts)" in *422*) bad "lint: NO-TOUCH-SET must NOT fire on a decision item declaring 'Paths: none'" "$(nts)" ;;
                 *)     ok  "lint: a decision item declaring 'Paths: none' is clean" ;; esac
# #407 declares a real touch-set.
case "$(nts)" in *407*) bad "lint: NO-TOUCH-SET must NOT fire on an item with a real Paths: line" "$(nts)" ;;
                 *)     ok  "lint: an item with a real touch-set is clean" ;; esac
# #423 is In progress — already claimed, never a scheduling candidate. Firing here would red the board
# for every item somebody is actively working.
case "$(nts)" in *423*) bad "lint: NO-TOUCH-SET must NOT fire on an In progress item" "$(nts)" ;;
                 *)     ok  "lint: NO-TOUCH-SET is scoped to Ready/Backlog — not items in flight" ;; esac
# #424 is CLOSED. Nobody needs to pick it up.
case "$(nts)" in *424*) bad "lint: NO-TOUCH-SET must NOT fire on a CLOSED issue" "$(nts)" ;;
                 *)     ok  "lint: NO-TOUCH-SET does not fire on a closed issue" ;; esac

# EPIC-UNLINKED-CHILD: the epic's body declares a child the sub-issue graph does not contain, so
# rollup cannot see it (FS-GG/.github#325). #409 declares #414; only #413 is linked.
assert_eq "lint: EPIC-UNLINKED-CHILD names the declared-but-unlinked child" "FS.GG.SDD#414" \
  "$(run lint --json 2>/dev/null | jq -r '.[] | select(.code=="EPIC-UNLINKED-CHILD") | .detail' \
     | sed 's/.*graph, so rollup cannot see them: //')"
assert_eq "lint: EPIC-UNLINKED-CHILD fires on exactly the one epic that has one" "FS-GG/FS.GG.SDD#409" \
  "$(run lint --json 2>/dev/null | jq -r '.[] | select(.code=="EPIC-UNLINKED-CHILD") | .id')"
# #415 is named in the epic's prose, not in a task-list line. A mention is not a declaration.
assert_eq "lint: a bare prose mention does not count as a declared child" "0" \
  "$(run lint --json 2>/dev/null | jq -r '[.[] | select(.detail|test("415"))] | length')"
# #404's body declares #499, but its child list is TRUNCATED — "unlinked" is unknowable there, and a
# gate that guesses is the very defect this rule exists to prevent (FS-GG/.github#266).
assert_eq "lint: a truncated epic yields no unlinked-child verdict" "0" \
  "$(run lint --json 2>/dev/null | jq -r '[.[] | select(.code=="EPIC-UNLINKED-CHILD" and .id=="FS-GG/FS.GG.Rendering#404")] | length')"
# #409's body ALSO cites `PR #418` on a task-list line. A PR can never be a sub-issue, so it is not
# an unlinked child — the fix re-resolves the ref, sees a pull request, and drops it (FS-GG/.github#346).
# #414 (a real issue) survives, so the finding still fires and still names ONLY #414, never #418.
assert_eq "lint: a body-declared PR ref is NOT reported as an unlinked child (#346)" "0" \
  "$(run lint --json 2>/dev/null | jq -r '[.[] | select(.detail|test("#418"))] | length')"
assert_eq "lint: a genuine unlinked ISSUE still fires alongside a skipped PR ref" "FS.GG.SDD#414" \
  "$(run lint --json 2>/dev/null | jq -r '.[] | select(.code=="EPIC-UNLINKED-CHILD") | .detail' \
     | sed 's/.*graph, so rollup cannot see them: //')"
# The internal scratch field used to carry the refs for the PR probe must not leak into the schema.
assert_eq "lint: the PR-probe scratch field is not exposed in --json output" "0" \
  "$(run lint --json 2>/dev/null | jq -r '[.[] | select(has("unlinked"))] | length')"
# Fail closed (FS-GG/.github#266): a ref the probe cannot resolve is KEPT, never silently dropped —
# "I could not check" is not "it is a PR". Force the #414 lookup to 502; the finding must survive.
assert_eq "lint: an unresolvable unlinked ref is kept, not dropped (fail closed, #266)" "FS.GG.SDD#414" \
  "$(GH_FAIL_ISSUE_GET=414 run lint --json 2>/dev/null | jq -r '.[] | select(.code=="EPIC-UNLINKED-CHILD") | .detail' \
     | sed 's/.*graph, so rollup cannot see them: //')"

# --repo scopes the scan. FS.GG.Templates holds only #405 — a NOTE — so lint passes, and --strict fails.
assert_eq "lint --repo templates: notes alone do not fail" "0" \
  "$(run lint --repo templates >/dev/null 2>&1; echo $?)"
assert_eq "lint --repo templates --strict: a note becomes fatal" "1" \
  "$(run lint --repo templates --strict >/dev/null 2>&1; echo $?)"
assert_eq "lint --repo rendering: only #404's finding is in scope" "EPIC-CHILDREN-TRUNCATED" \
  "$(run lint --repo rendering --json 2>/dev/null | jq -r '.[].code' | sort -u | tr '\n' ' ' | sed 's/ $//')"

# (9) epic_rollup counts a child finished only when the board says Done AND the issue is CLOSED.
#     Board-Done alone flipped an epic over a still-open child on the live board (FS-GG/.github#235).
: >"$GH_LOG"
hold="$(run done 'FS.GG.SDD#42' --pr 7 --flip 2>/dev/null)"
assert_contains "done --flip: the child itself stamps DONE"      "FSGG-DONE   FS.GG.SDD#42" "$hold"
assert_contains "rollup: HOLDS when a child is board-Done but still OPEN" \
  "1/2 children Done+closed — holding" "$hold"
# Exactly one Status write: the child. The epic must NOT be flipped.
assert_eq "rollup: holding writes Status once (the child), never the epic" "1" \
  "$(grep -c -- '--field-id PVTSSF_status' "$GH_LOG" || true)"

# ================================================================================================
# THE DONE-STAMP'S THREE HOLES (#558, #543, #583)
# ================================================================================================
#
# (1) #558 / #543 leg 1 — THE STAMP MUST NOT GO RED ON CORRECT, MERGED, GREEN WORK.
# The recipe's own `gh pr create --fill` routes a closing keyword in the commit SUBJECT to the PR
# TITLE, where `closingIssuesReferences` never looks. The squash commit still closes the issue, so
# everything succeeds and the stamp goes red — PERMANENTLY, because the link cannot be created after
# the merge. GitHub's own CLOSED_EVENT `closer` is the record of what actually closed it.
: >"$GH_LOG"
subj_rc=0
subj="$(run done 'FS.GG.SDD#165' --flip 2>&1)" || subj_rc=$?
assert_contains "#558: a keyword in the commit SUBJECT still earns the stamp (GitHub's own closer)" \
  "FSGG-DONE   FS.GG.SDD#165" "$subj"
assert_eq "#558: ...and exits 0 — the work IS done, and a red stamp on done work is how red becomes noise" \
  "0" "$subj_rc"
# The closer can be the COMMIT rather than the PR (a squash). Same record, same verdict.
commit_closer="$(run done 'FS.GG.SDD#166' --flip 2>&1 || true)"
assert_contains "#558: a COMMIT closer resolves through to its PR" \
  "FSGG-DONE   FS.GG.SDD#166" "$commit_closer"

# (2) #543 leg 2 — `--pr` MUST NOT LAUNDER A MENTION INTO A STAMP.
# It used to select by NUMBER alone, skipping the provenance check #342 added — so the documented
# escape hatch from the bug above was a SOUNDNESS HOLE: point it at any merged PR that merely mentions
# the issue and the stamp went green. PR 97 closes #70, not #96.
mention_rc=0
mention="$(run done 'FS.GG.SDD#96' --pr 97 --flip 2>&1)" || mention_rc=$?
assert_contains "#543: --pr must REFUSE a PR that only MENTIONS the issue (#342, through the --pr hole)" \
  "FSGG-NOT-DONE   FS.GG.SDD#96" "$mention"
assert_eq "#543: ...with a non-zero exit" "1" "$mention_rc"
case "$mention" in
  *"FSGG-DONE"*) bad "#543: --pr must not be an override of PROVENANCE" "$mention" ;;
  *) ok "#543: --pr overrides WHICH PR, never WHETHER it closed the issue" ;;
esac

# (3) #583 — DONE OVER AN OPEN CHILD. The #322 failure, in the command written to prevent it.
# `epic_rollup` asks this of the item's PARENT and never of the item in hand, so a worker who follows
# pnext-item §4 (split off what you cannot land, `child`-link it) closes the parent over the split-out
# criterion — with a green ✓✓ actively saying otherwise.
: >"$GH_LOG"
kid_rc=0
kid="$(run done 'FS.GG.SDD#507' --flip 2>&1)" || kid_rc=$?
assert_contains "#583: REFUSES to stamp an item that has an OPEN sub-issue" \
  "FSGG-NOT-DONE   FS.GG.SDD#507" "$kid"
assert_contains "#583: ...and NAMES the open child, so the worker can act on it" \
  "FS.GG.SDD#585" "$kid"
assert_eq "#583: ...with a non-zero exit" "1" "$kid_rc"
# THE ONE THAT MATTERS: it must not have written Done to the board on the way to refusing.
assert_eq "#583: ...and writes NO board Status — a green stamp over unfinished work is a board that lies" \
  "0" "$(grep -c -- '--field-id PVTSSF_status' "$GH_LOG" || true)"

# ...and a child list we could not see WHOLE makes "no open children" a claim about a set already
# known to be incomplete. An unverifiable subject must not report green (#266).
trunc_rc=0
trunc="$(run done 'FS.GG.SDD#509' --flip 2>&1)" || trunc_rc=$?
assert_contains "#583: a TRUNCATED child list refuses rather than reporting green (#266)" \
  "only 1 visible" "$trunc"
assert_eq "#583: ...with a non-zero exit" "1" "$trunc_rc"

: >"$GH_LOG"
flip="$(run done 'FS.GG.SDD#44' --pr 9 --flip 2>/dev/null)"
assert_contains "rollup: FLIPS when every child is Done AND closed" "FSGG-DONE   FS.GG.SDD#301 (epic)" "$flip"
assert_contains "rollup: the stamp says Done + closed" "all 2 children Done + closed" "$flip"
# Two Status writes: the child, then the epic it completed.
assert_eq "rollup: flipping writes Status twice (child, then epic)" "2" \
  "$(grep -c -- '--field-id PVTSSF_status' "$GH_LOG" || true)"
# A body declaration is not a hint the rollup may ignore. Epic #302's graph says 1/1 children done —
# it would flip — but the body declares #47, never linked. "All children Done" is then a claim about
# a set we already know is short, so it must not report green (FS-GG/.github#325, #266's rule).
: >"$GH_LOG"
unlinked="$(run done 'FS.GG.SDD#46' --pr 11 --flip 2>/dev/null)"
assert_contains "done --flip: the child still stamps DONE"        "FSGG-DONE   FS.GG.SDD#46" "$unlinked"
assert_contains "rollup: REFUSES when the body declares an unlinked child" \
  "body declares 1 child(ren) the sub-issue graph does not contain" "$unlinked"
assert_contains "rollup: names the unlinked child"                "FS-GG/FS.GG.SDD#47" "$unlinked"
assert_contains "rollup: points at the verb that fixes it"        "fsgg-coord child" "$unlinked"
# #999 is a trailing mention on a declaration line, not a second child.
assert_eq "rollup: a trailing mention on a declaration line is not a child" "0" \
  "$(printf '%s' "$unlinked" | grep -c '999' || true)"
# The child's Status is written; the epic's is NOT. A refusal that still flipped would be the bug.
assert_eq "rollup: refusing writes Status once (the child), never the epic" "1" \
  "$(grep -c -- '--field-id PVTSSF_status' "$GH_LOG" || true)"

# `+` and `*` are task-list bullets too. A matcher that knows only `-` reads epic #303 as declaring
# nothing and rolls it up — the gate failing OPEN on a formatting choice.
bullets="$(run done 'FS.GG.SDD#48' --pr 13 --flip 2>/dev/null)"
assert_contains "rollup: '+' and '*' task-list bullets declare children too" \
  "body declares 3 child(ren)" "$bullets"
# ...and the three are joined with ", " — `paste -sd', '` would CYCLE the delimiters ("a,b c").
# `unique` sorts the refs, so the cross-repo one leads; the point of the assertion is the separator.
assert_contains "rollup: multiple unlinked children join on ', ' (no delimiter cycling)" \
  "FS-GG/FS.GG.Rendering#51, FS-GG/FS.GG.SDD#49, FS-GG/FS.GG.SDD#50" "$bullets"
assert_eq "rollup: a '+' epic is never flipped" "0" \
  "$(printf '%s' "$bullets" | grep -c 'FS.GG.SDD#303 (epic)' || true)"

# The mirror image of #302: epic #55's graph is complete (its one child #52 is Done + closed), and
# its body's only extra ref is `PR #920` on a task-list line. A PR can never be a sub-issue, so it is
# not a missing child — the rollup re-resolves it, drops it, and FLIPS, where before it refused
# forever (FS-GG/.github#346). `mkpr` seeds the probe target as a pull request.
: >"$GH_LOG"
mkpr 920
flippr="$(run done 'FS.GG.SDD#52' --pr 19 --flip 2>/dev/null)"
assert_contains "rollup: FLIPS over a body-cited PR ref (a PR is not an unlinked child, #346)" \
  "FSGG-DONE   FS.GG.SDD#55 (epic)" "$flippr"
case "$flippr" in
  *"does not contain"*) bad "rollup: a body-cited PR must not read as an unlinked child" "$flippr" ;;
  *) ok "rollup: a body-cited PR does not block the rollup" ;;
esac
# Two Status writes: the child, then the epic the PR-cite no longer wedges.
assert_eq "rollup: flipping over a PR ref writes Status twice (child, then epic)" "2" \
  "$(grep -c -- '--field-id PVTSSF_status' "$GH_LOG" || true)"

# (10) PR provenance (FS-GG/.github#342): with no `--pr`, `done` stamps the PR that actually CLOSED
#      the issue — the latest-merged among true closers — and refuses a mere prose mention, where the
#      old code stamped the first (lowest-numbered) merged reference of any kind.
prov84="$(run done 'FS.GG.SDD#84' 2>/dev/null || true)"
assert_contains "done: stamps the PR that CLOSED the issue, not an earlier prose mention" \
  "merged PR #92 @ 09c836e" "$prov84"
assert_eq "done: the mentioning PR #85 (which closed #74) is never stamped" "0" \
  "$(printf '%s' "$prov84" | grep -c '#85\|410843e' || true)"

refuse86="$(run done 'FS.GG.SDD#86' 2>/dev/null || true)"
assert_contains "done: REFUSES when a merged PR only mentions the issue (no closer)" \
  "no merged PR closes this issue" "$refuse86"
assert_contains "done: the refusal is a red NOT-DONE stamp" "FSGG-NOT-DONE   FS.GG.SDD#86" "$refuse86"
assert_eq "done: a board-Done issue with only a mention still fails (green needs a real closer)" "1" \
  "$(run done 'FS.GG.SDD#86' >/dev/null 2>&1; echo $?)"

prov88="$(run done 'FS.GG.SDD#88' 2>/dev/null || true)"
assert_contains "done: among two closers, the LATEST-merged wins (not the lowest-numbered)" \
  "merged PR #95 @ 2222bbb" "$prov88"
assert_eq "done: the earlier-merged, lower-numbered closer #89 is not stamped" "0" \
  "$(printf '%s' "$prov88" | grep -c '#89\|1111aaa' || true)"


harness_report
