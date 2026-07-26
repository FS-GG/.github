# Backlog triage between reconciliation and dispatch

`Backlog` means parked, not irrelevant. A plain `batch`/`take` wave intentionally reads `Ready`, so the
host must classify parked work before sizing workers. This phase belongs to the host; workers still
self-select only through the typed scheduler.

## Ordered planning boundary

For every wave, in this order:

1. Run [check-board](../../check-board/SKILL.md) and consume its complete four-part result:
   mechanical changes; queued or failed writes; judgement findings; and the fresh post-apply result.
   Flush queued writes and re-run the fresh pass. A failed or unreadable part stops planning.
2. Read the current Backlog inventory with
   `scripts/fsgg-coord ready --status Backlog --json`. Read each relevant issue and its comments; do not
   reuse the inventory captured before the preceding worker wave.
3. Give every row exactly one classification below. Apply only evidence-supported board writes, then
   verify them with a fresh read.
4. Only now size Ready lanes with `scripts/fsgg-coord batch --repo <repo> -n <cap> --json`. Workers run
   bare `scripts/fsgg-coord take --repo <repo> --json`; never pass them item numbers and never use
   `--include-backlog` to skip this phase.

## Exhaustive classifications

### Promote to Ready

Promote with `scripts/fsgg-coord set-field <ref> Status Ready` only when live evidence says the issue is
open, implementable now, has a valid declared touch-set, has no unresolved implementation dependency,
has no active claim or PR, and requires no human choice. Re-read the row before counting it in a wave.
Promotion exposes the item to normal repo-directed `batch`/`take`; it does not assign it to a worker.

### Retain in Backlog

Retain only with a concrete reason already supported by the issue or its comments, such as an explicit
future milestone or deliberate parking decision. Record the ref and that reason in the wave report.
Do not invent a priority or edit vague prose merely to manufacture a reason. A row without an evidenced
reason is untriaged and therefore becomes an awaiting-human judgement, not silently retained work.

### Set Blocked

Set `Blocked` only when a parseable issue reference names a live implementation dependency and the
dependency must land before this item can be authored. Use
`scripts/fsgg-coord set-field <ref> Status Blocked`, preserve the valid `Blocked by:` edge, and verify
the fresh row. Topical relationships, temporary overlap, unreadable refs, and guessed blocker meaning
do not qualify; surface those as judgement findings.

### Await human judgement

Surface the row without a status guess when actionability depends on missing or ambiguous touch-sets,
blocker meaning, priority, epic discharge, scope, acceptance criteria, or another decision the evidence
does not answer. Carry forward the exact question and source evidence. This preserves `check-board`'s
mechanical-versus-human boundary.

## Wave-to-wave behavior

After workers finish, discard the old inventory. Verify their results, run the complete reconcile pass
again, and re-read Backlog before sizing another wave. A follow-up filed by the preceding wave is
therefore classified immediately: actionable work is promoted and becomes eligible for the next
repo-directed `batch`/`take`; parked or ambiguous work is reported.

An empty Ready batch is not completion while any Backlog row is actionable or untriaged. Conversely,
when a fresh pass leaves only deliberately parked rows with evidenced reasons or awaiting-human rows,
report them and allow the unattended run to stop. Do not spin on the same unchanged classification.
