# Maintain current board Status

The root integrator owns Status decisions and their guarded native updates. Workers supply evidence;
inspection reads facts without deciding or writing Status. The exceptions are automatic writers: the merge
gate's one post-merge Delivery transition (In review, or In progress with `fsgg:delivery-continues/v1`),
the project workflow "Item closed → Done", and the bounded automatic Status writes of `board-v2 tend`
(S1, S3, S3b, S4, S9, S9r and O1), admitted by the
[lifecycle contract](lifecycle-contract.md#board-tend-automatic-writes) while Root's enable file lists them. Status describes the current scoped promise,
not read health, PR existence, capacity allocation or permission to act.

## Reconcile at lifecycle boundaries

Observe results → reconcile acceptance/blockers → update Status and verify readback → select/admit work
→ dispatch → update Status and verify readback.

Apply this order after worker returns, CI/runtime completion, reviews, supplier replies and accepted
closure. Reconcile before yielding or handing off. Update the Delivery before reassessing its parent
Outcome. A periodic freshness sweep catches missed changes; it does not replace boundary updates.
Preserve existing ownership, attempts, worktrees, reservations and full acceptance while reconciling.

| Status | Determining condition |
|---|---|
| Backlog | No concrete next action is admitted. |
| Ready | A concrete next action is executable. Waiting for capacity also belongs here. |
| In progress | An accountable owner is actively executing, including a bounded wait on a named running operation. |
| In review | Implementation and required validation are complete; review or acceptance remains. Under tend's S3 a strictly linked non-draft PR shows its Delivery In review. |
| Blocked | No meaningful next action can proceed without a named prerequisite or decision. |
| Done | Complete scoped acceptance is established. |

Read the whole remaining promise and determining live evidence, not just the latest worker message or
PR label. A stopped or completed operation is no longer an active wait. A known in-scope repair can
leave executable work Ready; unfinished required validation cannot establish In review, so a PR whose
required validation is unfinished stays draft, which tend's S3b shows as In progress, and is marked ready
for review only on a head without known-red required gates. If the facts
needed for judgment are unavailable, retain the uncertainty and its owner rather than inventing progress.

Outcomes describe their whole remaining promise. Active child work can make an Outcome In progress
despite blocked portions. One child PR cannot make the whole Outcome In review; completed children
cannot automatically establish Done. The tend roll-up O1 keeps to these limits: it moves an Outcome only
among Backlog, Ready and In progress, over a complete child list; it never writes Blocked, In review or Done to an Outcome. Accepted children remain accepted without narrowing the parent.

## Guard the native effect

Use the maintained qualified native planning route, independently of the inspection command. For the
organization route this uses the qualified installed caller's read-only `board-v2 migration-prepare`
snapshot and the maintained guards in `scripts/board_v2_native_results.py`: `require_preimage` and
`verify_field_transition`. Send only the selected native Status field effect; do not revive legacy
claim/status automation or add Status writes to inspection.

1. Resolve the exact project, item, Status field and six existing option identities from fresh native
   snapshots. Join the selected issue and its current acceptance/owner/blocker evidence. Record the root's
   desired Status and reason in existing operation custody; do not create a second status ledger.
2. An equal value is a no-op. For a correction, retain its exact populated preimage and intended option.
   Refuse missing, conflicting or unreadable identities/preimages rather than overwriting a guess.
3. Within the admitted finite single-writer window, take an adjacent complete snapshot and require it
   to equal the prepared preimage. Preserve the reported field schema/options, views, memberships and
   every unrelated value. The participating writer lock and comparisons are cooperative, not server CAS.
4. Record possible-send custody before the one exact native `gh project item-edit` Status update. Take
   independent readback and use `verify_field_transition` to prove the intended transition plus full
   reported preservation. Keep opaque or hidden native contents outside the proof's stated coverage.
5. A failed or uncertain send/readback is not permission to resend. Retain the original effect identity
   and reconcile readback only. Withhold the affected dependent dispatch until Status is resolved;
   independently authorized work with reconciled Status can continue.

Status reconciliation does not dispatch work, release reservations, retry uncertain effects, weaken
acceptance or close issues. Native issue closure remains a separately authorized effect; closing an accepted Delivery,
including by tend rule C1, and the S1 Done backstop follow [rule 3](lifecycle-contract.md#file-the-delivery-issue-at-dispatch). A discrepancy
between closure, Done and full acceptance requires reconciliation, not automatic closure or parent rollup
beyond C1 and O1.
