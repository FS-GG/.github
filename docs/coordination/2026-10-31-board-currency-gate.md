# BOARD-CURRENCY-01: make "the board is current and triaged" a merge-time gate

Design, 2026-10-31. Owning repository `FS-GG/.github`; owning Outcome
[#4730](https://github.com/FS-GG/.github/issues/4730) (COORD-EFF-02). Root (Programme/root) owns
approval, integration, the guarded Status writes and every acceptance decision. Implementation phase 1
(the rule evaluator and the shadow report) follows in a separate PR.

## Why

The user asked for this directly:

- "we need to make check board that everything is current and triaged mandatory and deterministic.
  maybe automatic part of merge process?"
- "why is the work of the last day not put down as deliveries on the coord board"
- "more honestly reflect the amount of work still open … most outcomes have all their deliveries done
  yet still are open, that is very uninformative."

Today the board is current only if Root remembers each guarded Status write
([lifecycle contract](../../.agents/skills/work-board/references/lifecycle-contract.md), same-turn
rule). The hourly [board-drift observer](../../.github/workflows/board-drift-observer.yml) (#4973,
issue #4985) detects some misses, but its rules are mostly heuristics. Nothing at merge time checks that
the work being merged is on the board. A read-only shadow evaluation of the rules below against live
Project 3 at 2026-10-31T06:45Z (98 cards, 145 PRs) found:

- 108 routine PRs merged in the previous 24 hours. 94 of them referenced no Project 3 Delivery, almost
  all of them because they say `Refs #4730`, which is an Outcome. Another 12 referenced a Delivery
  with no parent Outcome on the board, and 2 referenced more than one Delivery. Of the 20 open routine PRs, 15 reference no Delivery.
- 10 open Outcomes are In progress. 9 open Outcomes have every sub-issue closed. 3 have no open
  Delivery on the board. That is the "uninformative" board the user describes.
- 9 Blocked cards name no blocker anywhere a machine can read.

The cause is structural, not a lapse of attention: the merge path never asks which Delivery a PR
delivers, and nothing turns "every child is closed" into a decision. This design fixes both
deterministically and leaves judgement with Root.

## 1. Rule set `fsgg:board-currency-rules/v1`

Inputs are native state only:

- issue and PR state and `stateReason`;
- the PR body;
- native parent, sub-issue and blocked-by links;
- the Project 3 fields Status, Work level and Track;
- Root-login marker comments, a closed grammar listed below.

The evaluator's clock is the snapshot's own `fetchedAt`, so one snapshot always gives the same
findings. A finding ID is `<rule>:<subject>:<reason>`. It never contains an age or a count, so an
unchanged situation keeps its ID from one run to the next. The parameters are part of the version:
changing any of them means a new rule-set version.

| Parameter | v1 value | Why |
|---|---|---|
| `activityHours` | 48 | Twice a working day. The observer runs hourly, so a stall is visible within two days, not seven. |
| `mergedWindowHours` | 24 | The shadow window that measures how many merged PRs R1 would have refused. |
| `effectiveFrom` | 2026-10-31T00:00Z | R2's acceptance-marker leg does not condemn Outcomes closed before the marker existed. |
| `rootLogins` | `EHotwagner` | The only login trusted for markers. Every agent authenticates as this one account, so a marker proves "Root's account", not "Root's hands". |

### The rules

| ID | Applies to | Exact inputs | Fails when | Reasons (finding ID suffix) |
|---|---|---|---|---|
| R1 | Routine PR (body carries `fsgg:routine-development/v1`): open and non-draft, or merged inside the window | PR body refs (`Refs` and closing keywords, ignoring code spans and fences); each ref's Project 3 item; that item's Work level, state, Status and native parent | The PR references zero or more than one P3 Delivery. Or the Delivery has no parent that is a P3 Outcome. Or, while the PR is open, the Delivery is closed or Done. Referencing the parent Outcome as well is allowed. | `no-delivery`, `multiple-deliveries`, `no-parent-outcome`, `delivery-closed`, `delivery-done` |
| R2 | Every issue card | state, `stateReason`, `closedAt`, Status, Work level, Root comments | A closed card is not Done. A Done card is open. An Outcome closed as COMPLETED on or after `effectiveFrom` lacks `fsgg:outcome-accepted/v1`. | `closed-not-done`, `done-open`, `outcome-unaccepted` |
| R3 | Open card with Status Blocked | native `blockedBy` (with its total), newest Root `fsgg:blocked-on/v1` marker, those blockers' states; for an Outcome, its open child Deliveries | No open blocker is named. An Outcome passes if one of its open child Deliveries is Blocked. | `no-blocker`, `blockers-closed` |
| R4 | Open card In progress or In review | Delivery: PRs referencing it, item/Status/comment/PR timestamps. Outcome: open children's Status. | A Delivery In progress has no open PR (a draft counts) and no activity within `activityHours`. A Delivery In review has no open non-draft PR and no such activity. An Outcome In progress has no open child In progress or In review. An Outcome In review has an open child that is not In review. The Outcome legs apply only when the Outcome has an open Delivery; otherwise R5 or R6 already names the cause. | `stale-active`, `outcome-no-active-child`, `outcome-review-open-child` |
| R5 | Open Outcome | native sub-issues (complete list) | It has at least one sub-issue, and every one is closed. | `children-complete` |
| R6 | Open Outcome that is not already R5 | native sub-issues, their P3 Work level | No open sub-issue is a P3 Delivery. | `no-open-delivery` |
| R7 | Every card | Status, Work level, Track, content type | A field is missing. Or the card is a draft, which is not a native issue, so the other rules cannot reason about it. | `missing-<fields>`, `draft` |
| R8 | Delivery | PRs referencing it | The Delivery is open with an open non-draft PR but is Backlog, Ready or Blocked. Or it is closed and an open non-draft PR still references it. | `open-pr-pre-action`, `pr-open-on-closed` |
| R9 | Open Delivery; Outcome | native parent and its P3 Work level | A Delivery has no parent, its parent is off the board, or its parent is not an Outcome. Or an Outcome is a sub-issue of a Delivery. | `orphan-delivery`, `parent-off-board`, `parent-not-outcome`, `outcome-under-delivery` |
| U | Any of the above | n/a | An input needed for a verdict could not be read: the card content, the comments, a blocker's state, or a truncated sub-issue or blocked-by list. Unknown is never a pass ([.github#266](https://github.com/FS-GG/.github/issues/266)). | `unreadable`, `r1-reference-unreadable`, `r2-comments-unread`, `r3-blockers-unread`, `r6-children-unread` |

### Why each rule, and how it refines the brief

- **R1 is the rule the merge gate enforces.** "Exactly one Delivery" makes each merge attributable,
  and that is what puts "the work of the last day" on the board. Outcome refs stay allowed beside the
  Delivery, because PRs legitimately cite context. A Delivery with no Outcome parent fails, because
  otherwise the work exists but rolls up nowhere.
- **R2 records acceptance differently per level.** A Delivery's acceptance is its native closure
  (pnext-item: "close only its fulfilled promise with the correct native reason"), so no new Delivery
  marker is needed. An Outcome's closure has repeatedly happened without the whole promise being
  checked, so it needs an explicit Root marker. The marker leg is not retroactive.
- **R3 uses native blocked-by first.** GitHub's issue dependencies are native, machine-readable and
  writable (`addBlockedBy`). The marker is a transition aid for blockers outside GitHub, and even
  then the blocker must be an issue. "Every blocker is closed" is the actionable signal: the card
  should move.
- **R4 is the deterministic form of drift rule (c),** with 48 hours instead of 7 days. "In review"
  with only merged PRs and no activity is exactly the "deliveries done yet still open" case: acceptance
  is owed.
- **R5 and R6 are disjoint,** so one cause gives one finding. R5 cannot decide between "propose
  closure" and "remaining Deliveries are missing"; that decision is Root's. The finding names both
  resolutions. A Root `fsgg:outcome-closure-proposed/v1` marker keeps the finding but tags it
  `pending: closure-proposed`, which separates findings waiting on Root from findings nobody has
  triaged.
- **R7 adds drafts,** because a draft has no state, parent or links and cannot be held to any other
  rule.
- **R8 keeps only the deterministic parts of the brief's R8.** "All PRs merged means Done" is not
  deterministic: a Delivery may need another PR, or a consumer effect after the merge. Closure decides
  Done (R2). The merge-time transition in §2(b) writes the deterministic part.
- **R9 is new.** Without hierarchy integrity, R5 and R6 undercount: a Delivery that is not linked to
  its Outcome makes the Outcome look empty.

### Marker grammar

Markers are HTML comments. They count only in a comment by a `rootLogins` author, and only outside a
code span or fence.

| Marker | Meaning |
|---|---|
| `<!-- fsgg:outcome-accepted/v1 -->` | Root accepts the Outcome's whole promise (R2). |
| `<!-- fsgg:outcome-closure-proposed/v1 -->` | Root or a worker proposes closure; the finding stays, tagged pending (R5, R6). |
| `<!-- fsgg:blocked-on/v1 OWNER/REPO#N … -->` | The blockers that are not native links. The newest marker wins, and an empty marker clears (R3). |
| `<!-- fsgg:delivery-continues/v1 -->` | In a PR body: after this merge the Delivery stays In progress instead of In review (§2b). |
| `<!-- fsgg:board-gate-override/v1 rule=R1 reason="…" -->` | In a PR body: a recorded override of the R1 gate (§3). |

## 2. Enforcement points

### (a) PR check `board-linkage`

This is R1 evaluated for one PR. It runs as one more step inside the existing `routine-eligibility`
job in `FS-GG/.github`, so it adds no job. It reads the PR body and resolves each ref on Project 3
with a read-only App token: organization-projects, issues and pull-requests read, the same mint as
`board-drift-read`. `routine-eligibility` already runs on `edited`, so fixing the body re-runs the
check.

- In advisory mode it writes a notice and the step stays green.
- In required mode an R1 failure or Unknown fails the step, unless the body carries the override marker.

Product repositories do not get this check in this design, because the receiver workflow has no App
secret. They are covered by (b), which every Root merge passes through.

### (b) Merge-time gate in `tools/routine-delivery.py`

`summarize()` already re-reads the live PR before it merges. Add one gate before the merge effect, used
only when `--apply` is given and the PR carries the routine marker:

1. Evaluate R1 from live reads: the PR body, plus one GraphQL query resolving each ref's P3 item, Work
   level, state, Status and parent.
2. If R1 fails, return `outcome: refused` with reason `board-linkage R1 <reason>`. Make no merge
   attempt and leave `attempts` unchanged.
3. If the read fails, return `outcome: indeterminate` with reason `board-linkage unknown`. This is
   fail-closed: no merge.
4. If the PR carries the override marker, merge and record `boardGate: overridden` in the result.

After the existing merged-PR and merge-commit readback, make exactly one deterministic Delivery
Status transition through the guarded-write semantics of the
[Status lifecycle](../../.agents/skills/work-board/references/status-lifecycle.md):

| After merge | Target Status |
|---|---|
| The Delivery is natively closed as COMPLETED (closing keyword) | Done |
| Refs-only, no `delivery-continues` marker | In review (implementation merged; acceptance or consumer effects remain) |
| Refs-only with `fsgg:delivery-continues/v1` | In progress |

The write follows these steps:

1. Take the preimage from a fresh snapshot.
2. Treat an equal value as a no-op.
3. Refuse to write if the preimage is Done or unreadable. Report `boardTransition: refused-preimage`.
   The merge stands and the observer surfaces the card.
4. Record possible-send in the result, then send one `gh project item-edit`.
5. Read back independently and verify with `require_preimage` and `verify_field_transition` from
   `scripts/board_v2_native_results.py`.
6. If the send or readback fails or is uncertain, report `boardTransition: uncertain`. Never resend.

This makes the same-turn Status rule automatic for the one transition a merge determines. The parent
Outcome is never rolled up automatically.

### (c) The hourly observer

It evaluates every rule over a full snapshot. It maintains one tracking issue holding the current
finding set, and its run fails, red, when a finding ID appears that was not in the previous set. That
is how it fails loudly on new findings without re-alarming on known ones. In phase 1 it only writes its
job summary. See §4 for how it converges with board-drift.

### Which writes are automatic

| Write | Who | Automatic? |
|---|---|---|
| Delivery Status after merge (table above) | `routine-delivery.py` as the merging credential | Yes: deterministic, guarded, read back |
| Status Done when an issue closes | GitHub's built-in project workflow "Item closed" (now disabled) | Proposed: enable it. It is deterministic and costs no CI |
| Sub-issue added to the project | GitHub's built-in "Auto-add sub-issues to project" (enabled) | Yes, already |
| Accepting an Outcome (marker plus close) | Root | **No.** This is a Root decision |
| Closing a Delivery | Root, or a closing keyword the PR author chose | No, except through the closing keyword |
| Any other Status (Blocked, Ready, Backlog, Outcome Status) | Root | No. Findings tell Root; Root writes |
| Creating the remaining Deliveries (R5, R6) | Root, through canonical intake and enrollment | No |
| Tracking issue | Observer, using `GITHUB_TOKEN` with `issues: write` only | Yes (report only) |

## 3. Authority and safety

- **Project field writes.** Phase 3 uses the credential that already performs the merge: Root's PAT
  through `gh`, which already makes Root's guarded Status writes. No new writer identity is introduced.
  The App `fs-gg-cross-repo-dispatch` holds `organization_projects: write` (pinned in
  `permission-coherence.yml`) but stays read-only for this design. Its board use here is read-only
  mints for the PR check and the observer, so a CI job never writes a board field.
- **Guarded writes.** Use the existing guard semantics (preimage, then send, then independent
  readback), the existing cooperative single-writer window, and no automatic retry.
- **Rate limits.**
  - Merge gate: about 1 GraphQL point before the merge, plus 1 read, 1 mutation and 1 readback after
    it. That is about 4 points of the PAT's 5,000 per hour per merge, or about 450 points a day at the
    current rate of about 110 merges a day.
  - Observer: runs on the App installation budget. Per hour it costs 2 to 3 item pages of about 2
    points, 2 search calls, about 25 comment reads and at most one batched state read.
  - PR check: about 1 point per `routine-eligibility` run.
- **Failed reads.**
  - Gate: an Unknown refuses the merge, which is fail-closed.
  - Observer: a failed read is a red run that leaves the tracking issue untouched (the board-drift
    precedent).
  - PR check: an Unknown is a notice while advisory and a failure once required.
- **Override.** `enforce_admins: true` means there is no admin bypass, so the override is data, not
  privilege. It is the body marker above, with a reason. Its effects:
  - (a) and (b) honour it.
  - The gate records `boardGate: overridden`.
  - The observer keeps an R1 finding open for the PR, with reason `overridden`, until a Delivery is
    linked.

  An override is therefore never silent and never permanent. The marker cannot prove who wrote it,
  because every agent shares one account. That fits the repository's stated trust model: "prevent
  accidental mistakes and drift", not adversarial writers.

## 4. Rollout

| Phase | What runs | Entry | Exit |
|---|---|---|---|
| 1, shadow (this delivery) | Pure evaluator; `board-currency-shadow` runs after each `board-drift-observer` completion and writes only its job summary | This design posted on #4730 | Root reviews about 3 days of counts and false positives; the parallel enrollment lands |
| 2, advisory | (a) as a notice step; (b) logs the would-refuse verdict without refusing; (c) moves to the tracking issue | Root approval on #4730 | Open-PR R1 near zero for 2 days; the override path exercised once in a test |
| 3, required | (a) fails the step; (b) refuses and performs the post-merge transition; (c) fails the run on new IDs | Root approval | n/a |

Drift rules map onto these rules as follows:

| board-drift rule | board-currency rule |
|---|---|
| a | R8 `open-pr-pre-action` |
| b | R2 |
| c | R4 |
| d | R7 |
| x | U |

Rule e, the ruling heuristic, has no deterministic counterpart. In phase 2 the two observers converge on
one tracking issue: either issue #4985 is renamed, or a "Board currency" issue replaces it. Drift rule
s from #4990 (schedule watch; open at the time of writing, not merged) stays with the drift workflow.

## 5. Interaction with existing pieces

- **Auto-add (built-in, enabled).** It adds sub-issues of P3 items without Work level or Track. R7
  flags them, which is the triage signal. The rules never add or remove membership. Enrollment stays
  with the protected cohort wrappers (`docs/coordination/*-enrollment.md`).
- **Board-drift observer.** It is unchanged in phase 1. The shadow workflow chains on its completion
  through `workflow_run`. GitHub drops scheduled runs and fdev dispatches the observer hourly, so this
  gets the same cadence with no second schedule or dispatcher.
- **`routine-eligibility`.** It hosts (a) as a step and keeps its existing envelope. Its refusals and
  this gate's refusals stay independent.
- **`routine-delivery.py`.** It hosts (b). The gate runs before the merge effect, and the transition
  runs after the existing readback. Neither changes publication, telemetry or evidence behaviour.
- **CI load.** Phase 1 adds one light job per observer run and one step in the existing
  `board-drift-observer-selftest` fixture job. Phase 2 adds one step in `routine-eligibility`. No new
  PR-triggered job.

## 6. Decisions requested from Root

1. Approve rule set v1, or name changes. Each change is a version bump.
2. Approve enabling the built-in "Item closed → Done" project workflow.
3. Choose the tracking issue for phase 2: rename #4985, or create a new issue.
4. Approve the post-merge transition defaults: In review, or In progress with the marker.
5. This delivery itself has no Delivery card on Project 3 yet. Every PR for it says `Refs #4730`,
   which is exactly the R1 `no-delivery` finding. Enrolling a Delivery under #4730 is left to the
   parallel enrollment pass.
