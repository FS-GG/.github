---
schemaVersion: 1
workId: 2496
title: "pnext-item names the live delivery <ref> --pr N call point"
stage: specify
changeTier: tier1
status: specified
publicOrToolFacingImpact: true
---

# pnext-item names the live delivery <ref> --pr N call point Specification

Prose status: specified

## User Value
A worker following pnext-item's documented flow writes a current fsgg:pr-authorization marker on every item/<n>-* PR it merges, with no manual delivery invocation required to reach that write.

## Scope
- SB-001: Add one documented step to `pnext-item`'s merge section (`.claude/skills/pnext-item/SKILL.md`, or a `references/` file it loads) naming the exact point in the ordinary worker flow where a live, non-`--snapshot` `delivery <ref> --pr N` call is made, for every `item/<n>-*` PR the flow produces.
- SB-002: Justify the chosen point against write/scan cost and against the ADR-0019/`.github#2332` credential boundary (the call must run from a worker's own credentialed shell, never CI, because the live path's first action is `Board.bootstrapCached`, a Projects-v2 read no CI credential in this org carries).
- SB-003: Document what the worker does when the live call fails, and whether a worker without a live claim may make it at all.
- SB-004: Demonstrate, on a freshly opened `item/<n>-*` PR carried through the documented flow with no manual `delivery` invocation, that `claim-generation` passes.

## Non-Goals
- SB-005: Do not implement the merge-election `opkey=`/`grant=` marker fields (`.github#1858`'s later slice) — out of scope, unrelated machinery.
- SB-006: Do not route the live call through CI — refuted by ADR-0019 §1/`.github#2332`'s credential-boundary decision.
- SB-007: Do not call the live `delivery` form on every push/commit — the design must bound the call to a small, justified number of invocations per item, not a per-push network write.

## User Stories
- US-001 (P1): As a worker carrying an `item/<n>-*` PR through `pnext-item`, I can follow one documented step that makes the live `delivery <ref> --pr N` call at a specific point in the flow, so the PR I merge carries a current `fsgg:pr-authorization` marker without me having to know to invoke it by hand.
- US-002 (P1): As a worker whose live `delivery` call fails (network error, transient GitHub failure), I can follow documented failure handling that keeps the failure from silently blocking or silently disappearing.
- US-003 (P2): As a future reader of `pnext-item`, I can see the write-cost and credential-boundary rationale for the chosen call point recorded in place, so the decision does not need to be rediscovered.

## Acceptance Scenarios
- AC-001 [US-001] [FR-001]: Given a worker has an `item/<n>-*` PR open and green, when the worker follows `pnext-item`'s documented merge step, then the worker runs a live `delivery <ref> --pr N` call (no `--snapshot`) at the exact head about to be merged, with no other step in the documented flow required to reach that call.
- AC-002 [US-001] [FR-002]: Given the live `delivery <ref> --pr N` call has already made the marker current for the PR's current head, when the worker (or the engine) calls it again for the same head, then the call is a zero-cost no-op PATCH (idempotent), so the documented step does not add an unbounded per-push write.
- AC-003 [US-002] [FR-003]: Given the live `delivery <ref> --pr N` call fails, when the worker is following the documented merge step, then the worker reports the failure rather than silently proceeding, and the failure does not block the merge, because `claim-generation` is advisory-only by construction.
- AC-004 [US-003] [FR-004]: Given the documented step names a call point, when a reader asks "why here, not on every push, not from CI", then the documented step itself answers write/scan cost and the ADR-0019/`.github#2332` credential boundary in place, without requiring a second document.
- AC-005 [US-001] [FR-005]: Given a freshly opened `item/<n>-*` PR is carried through the documented flow with no worker hand-invoking `delivery` outside the documented step, when the PR merges, then `gh pr checks` on that PR shows `claim-generation` passing (or the PR's `fsgg:pr-authorization` marker is observably current for the merged head).

## Functional Requirements
- FR-001: `pnext-item` names one specific point in the ordinary merge flow (§6, immediately after `landable` reports green for the exact head SHA and immediately before the merge REST call) where a live, non-`--snapshot` `delivery <ref> --pr N` call is made, for every `item/<n>-*` PR the flow produces. (Stories: US-001; Acceptance: AC-001)
- FR-002: The documented step calls the live form exactly once per item, at the head about to be merged — never on every push — because `Client.ensureAuthorization`'s `rebindAuthorization` decision makes a call against an already-current marker a zero-cost no-op, and a call against a stale head is the one case that must still happen before merge. (Stories: US-001; Acceptance: AC-002)
- FR-003: The documented step states that a failed live `delivery` call is reported by the worker and does not block the merge — `claim-generation`'s `[missing]`/`[stale]` conclusion is excluded from `landable`'s rollup by name (`Landable.fs` `advisoryCheckNames`) until the check is armed into branch protection — and that only the worker currently holding the item's live claim marker may make the call (the live form itself refuses with "no live claim marker can authorize delivery" otherwise). (Stories: US-002; Acceptance: AC-003)
- FR-004: The documented step records, in place, why this point and not another: bounded to one call per item (riding the warm 90-second `Cache.Scheduling` scan `take`/`done` already pay for in the common case, never a per-push write), and why it cannot run from CI (the live path's first action is `Board.bootstrapCached`, a Coordination Projects-v2 GraphQL read no CI credential in this org carries — ADR-0019 §1, `.github#2332`). (Stories: US-003; Acceptance: AC-004)
- FR-005: A freshly opened `item/<n>-*` PR, carried through the documented flow with no worker hand-invoking `delivery` outside the documented step, merges with `claim-generation` passing (or an equivalent observable: the PR's `fsgg:pr-authorization` marker is current for the merged head). (Stories: US-001; Acceptance: AC-005)

## Ambiguities
- AMB-001: Where exactly in the worker's ordinary flow should the live call go — right after opening the PR (earliest live-claim + PR-number availability), on every push (freshest but highest cost), or right before the merge (latest, cheapest, but only covers the final head)? This is the open design question the reviewing critic on `.github#2488` declined to resolve as a third repair round.
- AMB-002: What should the documented step say a worker does when the live call errors, and does it gate the merge?
- AMB-003: May a worker who does not currently hold the item's live claim marker make this call (e.g., a critic, or a worker after `done --flip` released the claim)?

## Public Or Tool-Facing Impact
- This changes `.claude/skills/pnext-item/SKILL.md`, a kit-mirrored surface (ADR-0019) read by every worker in every FS-GG repository. A wrong placement propagates fleet-wide on the next kit release.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd clarify --work 2496`.
