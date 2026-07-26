# Workspace Backlog triage between reconciliation and dispatch

`Backlog` means parked, not irrelevant. Plain workspace `batch`/`take` scheduling intentionally reads
`Ready`, so the host must classify parked work before sizing a wave. Triage changes eligibility; the
typed scheduler still owns touch-set-disjoint selection inside this single repository.

## Runtime boundary and ordered planning

First run the coordination-wiring preconditions in [deep detail](deep-detail.md). Missing board env,
kit skills, shim, auth, or readable engine state stops non-zero with the documented retrofit or
`work-roadmap` guidance, without mutating the board. Never interpret missing wiring or a failed read as
an empty workspace.

For every board-capable wave, in this order:

1. Run workspace-scoped [check-board](../../check-board/SKILL.md) and consume its complete four-part
   result: mechanical changes; queued or failed writes; judgement findings; and the fresh post-apply
   result. Flush queued writes and repeat the fresh pass. An unreadable part stops planning.
2. Read the current inventory with
   `scripts/fsgg-coord ready --repo <this-repo> --status Backlog --json`. Read each relevant issue and
   its comments; discard any inventory captured before the preceding worker wave.
3. Give every row exactly one classification below. Apply only evidence-supported board writes, then
   verify them with another fresh workspace read.
4. Only then size a maximal disjoint Ready wave with
   `scripts/fsgg-coord batch --repo <this-repo> -n <cap> --json`. Each isolated worker mints its own
   identity and runs bare `scripts/fsgg-coord take --repo <this-repo> --json`. Never hand-pick Backlog
   items or use `--include-backlog` to bypass triage.

## Exhaustive classifications

### Promote to Ready

Promote with `scripts/fsgg-coord set-field <ref> Status Ready` only when live evidence says the issue is
open, implementable now, has a valid declared touch-set, has no unresolved implementation dependency,
has no active claim or PR, and requires no human choice. Re-read the row before counting it in a wave.

Promotion changes eligibility, not assignment. The item enters the normal single-repo `batch`/`take`
collision boundary, which remains responsible for selecting a touch-set-disjoint set. Every promoted
item still runs inside the existing pnext-item envelope: the simple-versus-complex SDD lifecycle branch
is selected by item complexity, and the worker finalizes the schema-v2 development-feedback report
before its PR.

### Retain in Backlog

Retain only with a concrete reason already supported by the issue or its comments, such as an explicit
future milestone or deliberate parking decision. Record the ref and reason in the wave report. Do not
invent priority or rewrite vague prose merely to manufacture a reason. A row without an evidenced
reason is awaiting human judgement, not silently retained work.

### Set Blocked

Set `Blocked` only when a parseable issue reference names a live implementation dependency that must
land before this item can be authored. Preserve the valid `Blocked by:` edge, run
`scripts/fsgg-coord set-field <ref> Status Blocked`, and verify the fresh row. Topical relationships,
temporary path overlap, unreadable refs, and guessed blocker meaning do not qualify.

### Await human judgement

Surface the row without guessing status when actionability depends on missing or ambiguous touch-sets,
blocker meaning, priority, epic discharge, scope, acceptance criteria, or another decision the evidence
does not answer. Carry the exact question and source evidence into the wave report. This preserves
`check-board`'s mechanical-versus-human authority boundary.

## Fresh follow-ups and termination

After workers finish, verify their PR, done stamp, claim release, pending writes, and schema-v2 feedback,
then discard the old inventory. Run the complete workspace reconcile pass again and re-read Backlog
before sizing another wave. A follow-up filed by the preceding wave is classified immediately:
actionable work is promoted and becomes eligible for the next disjoint `batch`/`take`; parked or
ambiguous work is reported.

An empty Ready batch is not completion while any Backlog row is actionable or untriaged. When a fresh
pass leaves only deliberately parked rows with evidenced reasons or awaiting-human rows, report them and
allow the unattended run to stop. Do not spin on the same unchanged classification.
