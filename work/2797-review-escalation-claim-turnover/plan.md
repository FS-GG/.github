---
schemaVersion: 1
workId: 2797-review-escalation-claim-turnover
title: Review Escalation Claim Turnover
stage: plan
changeTier: tier1
status: planned
sourceSpec: work/2797-review-escalation-claim-turnover/spec.md
sourceClarifications: work/2797-review-escalation-claim-turnover/clarifications.md
sourceChecklist: work/2797-review-escalation-claim-turnover/checklist.md
publicOrToolFacingImpact: true
---

# Review Escalation Claim Turnover Plan

Prose status: planned

## Source Snapshot
- spec: work/2797-review-escalation-claim-turnover/spec.md sha256:d27ccba03741b3519075e1533a0a776e6fe309e3dcd51f17f2850e32159e5584 schemaVersion:1
- clarifications: work/2797-review-escalation-claim-turnover/clarifications.md sha256:d4ab78fce38c7d8e8cfef542271ea2671bd7512cb0c88480bc1683cc8f516a5b schemaVersion:1
- checklist: work/2797-review-escalation-claim-turnover/checklist.md sha256:3de48fdf64f60f17a0527412a330454fe3d8e3cb9c0f1f403d49290baee995ee schemaVersion:1

## Plan Scope
- Work item 2797-review-escalation-claim-turnover is planned from the current specification, clarification, and checklist facts.
- Requirement count: 6.
- Clarification decision count: 5.
- Checklist result count: 6.

## Plan Decisions
- PD-001 [AC-001] [FR-001] complete: Add a pure `ReviewWait` escalation-authority classifier over the item, current claim generation, PR/head-bound review generation, completed round-3 receipt, and parsed structured/legacy exhaustion facts; grant one changed-claim escalation without treating the old receipt as generally current.
- PD-002 [AC-002] [FR-002] complete: Key the exception on `StructuredDecision.Escalation` only inside `authorizeReviewRecordWait`; leave initial, confirmation, repair-phase, and acceptance on their existing same-claim wait rules.
- PD-003 [AC-003] [FR-003] complete: Validate exact item, PR-derived subject, head-derived `repair-confirmation:3` generation, round 3, prior digest/backlinks, ordered initial+confirmation1/2/3 chain, completed wait, legacy escalation evidence, no structured escalation, and a distinct fresh current claimant before posting.
- PD-004 [AC-004] [FR-004] complete: Render the completed round-3 changed-claim wait as ordinary exhaustion/repair-phase handoff in `ReviewApplication`, suppressing any dispatch/resume authority that would amount to ordinary round 4 while preserving the typed review ledger unchanged.
- PD-005 [AC-005] [FR-005] complete: Add table-driven pure witnesses in `ReviewWaitTests.fs` for the valid grant and every refusal dimension; extend `writes.sh` with the exact two-claim production route, duplicate replay, and non-escalation mutations that compare comment counts before and after refusal.
- PD-006 [AC-006] [FR-006] complete: After guarded merge, run engine freshness on merged `origin/main`; deduplicate and board the coherent release debt required for S.I.R. installation rather than cutting an unreviewed release inside this source item.

## Contract Impact
- PC-001 [PD-001] [PD-002] reviewWriter: `ReviewWait` exposes a closed escalation-authority result consumed by the live `review record` adapter; no wire schema or marker vocabulary changes, and the exception is unreachable for non-escalation kinds.

## Verification Obligations
- VO-001 [PD-001] [PD-002] [PD-003] [PD-004] [PD-005] [PC-001] semanticTest: Run focused `ReviewWaitTests`, the compiled production-writer e2e, and the exact live-style changed-claim reproduction; mutate each authority subject once and prove the intended test or writer assertion reds before restoring it.

## Performance Intent
No performance intent is declared for this work item.

## Migration Posture
- PM-001 [PC-001] additive: Existing markers and structured ledgers remain byte-compatible; only a previously impossible valid escalation gains authority, while every existing same-claim mutation fence remains fail-closed.

## Generated View Impact
- GV-001 [PD-004] reviewProjection: Live JSON/text review output must identify ordinary exhaustion or repair-phase handoff from the completed round-3 changed-claim route and must never emit a round-4 dispatch/resume action.

## Accepted Deferrals
No accepted plan deferrals recorded.

## Planning Findings
No blocking planning findings recorded.

## Advisory Notes
- Optional Governance pointers remain compatibility facts only.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd tasks --work 2797-review-escalation-claim-turnover`.
