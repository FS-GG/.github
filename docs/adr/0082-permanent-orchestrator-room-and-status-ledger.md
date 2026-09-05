# ADR-0082: Board orchestrators share one permanent status room

- **Status:** Accepted
- **Date:** 2026-09-05
- **Affects:** `.github` board drivers and coordination-wired product board drivers

## Context

Item claim markers correctly serialize item mutation, but they do not express the supervising
orchestrator's intent. On 2026-09-05, a second orchestrator force-claimed `.github#3210` while the
first orchestrator was actively finishing review repairs in an unpushed worktree. The force transition
sent an item-scoped message only after replacement. The second host had no required place to discover
that the first host was active, and the first host's inbox was not a shared host-presence ledger.

Coordination rooms from ADR-0051 are derived from current item membership and close when their work
ends. That lifecycle is correct for an overlap knot and wrong for an orchestrator control channel whose
identity and history must survive every board item and every host process.

## Decision

1. [`.github#3227`](https://github.com/FS-GG/.github/issues/3227) is the permanent, off-board
   orchestrator room. It is never an implementation item and remains open across board cycles.
2. Orchestrator presence is an append-only status ledger with exactly four states: `active`, `waiting`,
   `yielded`, and `done`. Every record binds worker, qualified item (or `none`), live claim-comment id
   (or `none`), exact head (or `none`), and one bounded note token. The payload worker must equal the
   typed message actor, and the latest status attempt per worker must validate; malformed newer state
   never falls back to an older status. Records are not edited. Comment `5551249226` is the immutable
   authenticated-protocol activation boundary, so older raw lines remain history without authority.
3. Every `drive-board*` and `work-board*` host reads the complete room before dispatch, recovery, or
   force-claim and posts transitions at startup, post-claim, deliberate wait, yield, and completion.
   `active` and `waiting` forbid takeover. Only `yielded` or an explicit room handoff permits it.
4. An unreadable, malformed, or contradictory room/claim state fails closed. Item-scoped messaging and
   an expired item lease cannot be interpreted as permission to ignore a current `waiting` status.

## Consequences

The board remains the scheduling ledger and claim markers remain the mutation lock; this decision adds
host intent rather than replacing either. A crashed orchestrator can now leave a conservative stale
status that requires explicit reconciliation. That may delay recovery, but it cannot silently destroy
unpublished work. `scripts/check-orchestrator-room` machine-enforces actor binding, closed syntax,
pagination, latest-attempt resolution, and the fail-closed activation boundary.

## Verification

Issue `.github#3228` tracks implementation. The canonical `drive-board` and `work-board` skills, which
their `-normal` and `-best` variants inherit, link the procedure; both runtime roots remain byte-identical;
and `scripts/check-skill-quality` verifies mirrors, links, triggers, budgets, forged-actor refusal, and
malformed-newest refusal.
