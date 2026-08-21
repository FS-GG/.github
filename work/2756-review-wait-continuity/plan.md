---
schemaVersion: 1
workId: 2756-review-wait-continuity
title: Durable review wait and critic continuity
stage: plan
changeTier: tier1
status: planned
sourceSpec: work/2756-review-wait-continuity/spec.md
sourceClarifications: work/2756-review-wait-continuity/clarifications.md
sourceChecklist: work/2756-review-wait-continuity/checklist.md
publicOrToolFacingImpact: true
---

# Durable review wait and critic continuity Plan

Prose status: planned

## Source Snapshot
- spec: work/2756-review-wait-continuity/spec.md sha256:24ea22bef992f6a555914eaeff93e118907250684a861c4cd2d8a294d3028e97 schemaVersion:1
- clarifications: work/2756-review-wait-continuity/clarifications.md sha256:6da26f230bee9069c85a2ce5a5414cc1a97c16462bea42f872c0a8ae828ea9af schemaVersion:1
- checklist: work/2756-review-wait-continuity/checklist.md sha256:e97b500fa045b23ecfbabee50dbd870a90023680a64a071e3328ebeea34f784b schemaVersion:1

## Plan Scope
- Work item 2756-review-wait-continuity is planned from the current specification, clarification, and checklist facts.
- Requirement count: 5.
- Clarification decision count: 4.
- Checklist result count: 5.

## Plan Decisions
- PD-001 [AC-001] [FR-001] complete: Define one `WaitReceipt(item, claimGeneration, reviewGeneration, kind, enteredAt, expiresAt, evidenceRef)` vocabulary in `independent-review.md`; the receipt is durable review state and never a lease extension.
- PD-002 [AC-002] [FR-002] complete: Define the current receipt plus open PR as reservation evidence, while requiring claim-generation validation or reacquisition before mutation.
- PD-003 [AC-003] [FR-003] complete: Define completion, cancellation, and timeout as idempotent terminal dispositions; a losing race re-reads the winning state and timeout projects explicit recovery.
- PD-004 [AC-004] [FR-004] complete: Replace exceptional same-critic succession with ordinary fresh-successor dispatch after a repair wait; preserve the durable digest chain and require a full exact-head review with no inherited clearance.
- PD-005 [AC-005] [FR-005] complete: Add production-code xUnit witnesses for entry/write round-trip, wait beyond the active lease, changed claim generation, bounded expiry, completion/timeout racing, and maximum duration; keep the mirrored contract gate as the reached prose boundary and independently invert `writes` to `may write`.

## Contract Impact
- PC-001 [PD-001] reviewProtocol: `ReviewWait` owns the append-only marker codec and deterministic projection; `review wait` is the authoritative writer and live `review` parses the PR ledger. Structured review admits an ordinary fresh successor only at a confirmation following `changes-required` while preserving legacy explicit grants.

## Verification Obligations
- VO-001 [PD-001] [PC-001] semanticTest: Run the six `ReviewWaitTests` behavioral witnesses plus the compiled e2e writer, the succession wire/inversion fixture, and `tests/skill-quality/review-round-contract.py`; independently mutate `writes` to `may write` and show the focused fixture red.

## Performance Intent
No performance intent is declared for this work item.

## Migration Posture
- PM-001 [PC-001] additive: Existing review ledgers remain readable; the durable wait contract governs newly entered waits and does not reinterpret an expired claim as live.

## Generated View Impact
- GV-001 [PD-001] skillProjection: Keep `.agents` and `.claude` independent-review references byte-identical; adjust critic route projections only where ordinary successor dispatch changes their instructions.

## Accepted Deferrals
No accepted plan deferrals recorded.

## Planning Findings
No blocking planning findings recorded.

## Advisory Notes
- Optional Governance pointers remain compatibility facts only.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd tasks --work 2756-review-wait-continuity`.
