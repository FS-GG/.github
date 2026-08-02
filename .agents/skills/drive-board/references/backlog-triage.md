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

**Never park a row to reorder a wave.** On 2026-07-27 a driver moved nineteen actionable hardening rows
into `Backlog` for no reason except to push five structural items to the front of the lane pack. It
worked, and it left nineteen rows whose only recorded reason lived in a session transcript — which is
exactly the untriaged state the paragraph above refuses. It also conflated two different facts the board
must keep apart: "not now" and "not until a human decides".

That workaround existed because the scheduler ignored `Phase` and ordered candidates by issue number, so
`Status` was the only priority lever there was. It no longer is. `batch` and `take` pack lanes
priority-greedily by a rank derived from blocking count, `Class`, `Phase` and age
([.github#1598](https://github.com/FS-GG/.github/issues/1598)), and the highest-ranked schedulable item
is always admitted. To raise an item's priority, fix the input that makes it important:

| you want | set this |
|---|---|
| this unblocks other work | the real `Blocked by` edges on the items waiting for it |
| this is broken now | `Class` = `defect` (via its `Class:` body line, then `reconcile --apply`) |
| this comes early in the plan | `Phase` |
| this has waited too long | nothing — a `Ready` row escalates above class and phase on its own |

`scripts/fsgg-coord batch --repo <repo> --explain` prints the ranking, every candidate's inputs, and how
many lanes each admitted item displaced. If an item is not where you expect, that output names the input
to fix. Two `batch` calls over a moving board may legitimately return different sets — rank moves as the
board moves — so size a wave from one read rather than diffing two.

### Set Blocked

Set `Blocked` only when a parseable issue reference names a live implementation dependency and the
dependency must land before this item can be authored. A park is **two writes, not one**: the `Status`
column and the `Blocked by` **board field** — the Projects v2 field, never a body line. `Blocked by:`
written into the issue body is inert: nothing that clears a blocker reads the body, so it looks like a
declaration and does nothing (`.github#1933`) — the exact shape that twice let a fully-resolved,
already-superseded field value survive a park because the real edge had gone into the body instead. Write
both in one call:

```
scripts/fsgg-coord set-field --batch <ref> Status=Blocked "Blocked by=<dependency-ref>"
```

and verify the fresh row afterward — including that the field, not just the body, carries the ref.
Always write both fields yourself in the same call rather than depending on the engine to catch an
omission — this is belt-and-braces, not a substitute for writing the edge. The engine refuses an
incoherent `Blocked` write from `release --status Blocked`, the single-field
`set-field <ref> Status Blocked`, and `set-field --batch` (`.github#2079`, extended to the batch form by
`.github#2098`, merged) — but not from `add --status Blocked`, the filing-time write that boards a new
row already `Blocked`; that gap is real and tracked separately. The instruction to write both fields
predates all of these gates and stands regardless of what any of them catches: the brief that produced
`FS.GG.Templates#348` said to set the dependency field without naming it, and a gate closing does not
make that guidance unnecessary. Topical relationships, temporary overlap, unreadable refs, and guessed
blocker meaning do not qualify; surface those as judgement findings.

### Await human judgement

Surface the row without a status guess when actionability depends on missing or ambiguous touch-sets,
blocker meaning, priority, epic discharge, scope, acceptance criteria, or another decision the evidence
does not answer. Carry forward the exact question and source evidence. This preserves `check-board`'s
mechanical-versus-human boundary.

## Wave-to-wave behavior

After workers finish, discard the old inventory. Verify their results and independent-review evidence,
including that every new review-discovered row is material and no nonmaterial observation was filed.
Run the complete reconcile pass
again, and re-read Backlog before sizing another wave. A follow-up filed by the preceding wave is
therefore classified immediately: actionable work is promoted and becomes eligible for the next
repo-directed `batch`/`take`; parked or ambiguous work is reported.

An empty Ready batch is not completion while any Backlog row is actionable or untriaged. Conversely,
when a fresh pass leaves only deliberately parked rows with evidenced reasons or awaiting-human rows,
report them and allow the unattended run to stop. Do not spin on the same unchanged classification.

Triage the **class** alongside the status. A row whose text carries no `Class:` line is untriaged in the
sense that matters to the stopping rule, and `lint` reports it as `CLASS-UNSET`; a row classed `hardening`
is triaged and deliberately retained, which is a different state and must be reported as one. Class from
evidence — the item's own text, a `[decision]` prefix, a `Blocked on: human/decision` sentinel — and never
from a guess about how bad it looks.
