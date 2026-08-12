---
schemaVersion: 1
workId: 2417-critic-despawn-review-recovery
title: Critic Despawn Review Recovery
stage: plan
changeTier: tier1
status: planned
sourceSpec: work/2417-critic-despawn-review-recovery/spec.md
sourceClarifications: work/2417-critic-despawn-review-recovery/clarifications.md
sourceChecklist: work/2417-critic-despawn-review-recovery/checklist.md
publicOrToolFacingImpact: true
---

# Critic Despawn Review Recovery Plan

Prose status: planned

## Source Snapshot
- spec: work/2417-critic-despawn-review-recovery/spec.md sha256:c07bc754cceca32c92b33929cf658d2a90efc9b4e441e187978d4b22dc564bae schemaVersion:1
- clarifications: work/2417-critic-despawn-review-recovery/clarifications.md sha256:20a4a5d5cd527c78b08fae8401389240e83765af48ca88bcae4093f6c1722585 schemaVersion:1
- checklist: work/2417-critic-despawn-review-recovery/checklist.md sha256:2990be29fbec6836e3fd66449515870d47a5c7bb4a5c5040bb4ddd656b991b53 schemaVersion:1

## Plan Scope
- Work item 2417-critic-despawn-review-recovery is planned from the current specification, clarification, and checklist facts.
- Requirement count: 7.
- Clarification decision count: 1.
- Checklist result count: 7.

## Plan Decisions
- PD-001 [AC-001] [FR-001] complete: Leave `Review.classify`'s existing ordinary- and repair-phase `Some _` arms (the "new commit landed after changes-required" case) untouched on the no-receipt path — the new `CriticSuccessionGranted: CriticSuccessionReceipt option` field on `Review.Facts` defaults through every existing construction site to behavior identical to today, so `ResumeSameCritic` remains the answer whenever no receipt is supplied.
- PD-002 [AC-002] [AC-005] [FR-002] complete: Add `Review.CriticSuccessionReceipt` (`OriginalCriticIdentity`, `SuccessorCriticIdentity`, `GrantedBy`, `Reason`, `CandidateHeadSha`) and `Review.NextAction.EnterCriticSuccession of CriticSuccessionReceipt`; add a private `criticSuccessionValid binding facts currentCritic` guard in `Review.fs` consulted from both the ordinary `AwaitingSameCriticConfirmation` arm and the repair-phase `RepairPhaseActive` arm at the identical branch point, so the two phases share one guard rather than two copies.
- PD-003 [AC-003] [FR-003] complete: `criticSuccessionValid` requires `receipt.OriginalCriticIdentity = currentCritic` and `receipt.CandidateHeadSha = binding.HeadSha`; either mismatch returns `None` from the guard, which the classify arms treat exactly as "no receipt supplied" (`ResumeSameCritic`), per DEC-001 with a distinguishing reason string.
- PD-004 [AC-004] [FR-004] complete: `criticSuccessionValid` additionally requires `receipt.SuccessorCriticIdentity <> binding.ImplementerIdentity` and `receipt.GrantedBy <> binding.ImplementerIdentity` (plus both non-blank) — an implementer-authored receipt never passes the guard.
- PD-005 [AC-006] [FR-005] complete: In `ReviewApplication.fs`, add a `criticSuccessionGranted` reader that treats an absent key or a JSON `null` as `None` (mirroring `optionalProperty`, a new helper distinct from the existing `required`-based `repairPhaseGranted` reader, which throws on absence) so every existing `--snapshot` payload with no such key keeps parsing unchanged; add the matching `enterCriticSuccession` case to `actionName` and a `criticSuccessionReceipt` output field to the JSON payload alongside the existing `repairPhaseReceipt` field.
- PD-006 [AC-007] [FR-006] complete: Add a new subsection to `independent-review.md` (edited once, then byte-copied to the `.agents/skills` kit mirror) directly after the same-critic/repair-phase contract text, naming the exact typed fact and guard conditions, the successor critic's obligation to perform a full fresh review rather than a "confirmation," and an explicit sentence that `landable`/`.github#2360` and the host-acceptance marker are unaffected — this recovery path changes who may produce an accepted chain, never what gates the merge.
- PD-007 [AC-008] [FR-007] complete: No change to `Review.advance`/`makeVerdict`/`freshnessToken`/`actionKey` — `EnterCriticSuccession` is a normal `NextAction` case, and the existing digest-over-`%A{state}\n%A{action}` mechanism already covers it exactly as it covers `EnterRepairPhase`; a dedicated restart-replay test pins this rather than changing the mechanism.

## Contract Impact
- PC-001 [PD-002] [PD-005] public-surface: `FS.GG.Coord.Core.Review`'s `Facts`, `NextAction`, and the `fsgg.coord.review/1` JSON wire contract (`ReviewApplication.fs`) gain one additive optional fact/case/field each; no existing field, case, or required JSON key changes shape, so every current producer and consumer (the live `review <ref> --pr N` path in `Client.fs`, which continues to omit the fact exactly as it already omits `RepairPhaseGranted`, and any existing `--snapshot` caller) keeps compiling and parsing unchanged.

## Verification Obligations
- VO-001 [PD-002] [PD-003] [PD-004] [PD-007] semanticTest: `tests/FS.GG.Coord.Core.Tests/ReviewTests.fs` covers, at minimum: (1) no receipt supplied leaves `ResumeSameCritic` unchanged (regression, already covered by an existing case); (2) a valid matching receipt yields `EnterCriticSuccession` in the ordinary phase; (3) the same in the repair phase; (4) a receipt naming the wrong original critic is refused; (5) a receipt bound to a stale head is refused; (6) a receipt naming the implementer as successor is refused; (7) a receipt naming the implementer as granter is refused; (8) `advance` re-converges idempotently on a granted `EnterCriticSuccession` verdict. `dotnet test tests/FS.GG.Coord.Core.Tests` must be green with every new case exercised (not merely present) — each guard case is authored by first asserting the case WITHOUT the guard reaches `EnterCriticSuccession` wrongly (gate-inversion evidence), then confirming the guard blocks it.

## Performance Intent
No performance intent is declared for this work item — a pure, in-memory classification function with no new IO, allocation-bound only by the existing `Facts`/`Binding` shapes.

## Migration Posture
- PM-001 [PC-001] additive-no-migration: The new `Facts` field and `NextAction` case are additive; no existing serialized snapshot, marker text, or stored artifact requires migration. The kit-mirrored `independent-review.md` edit requires a `FS.GG.Kit` `<Version>` bump and republish before merge (content-addressed kit source), which is a release step, not a data migration.

## Generated View Impact
- GV-001 [PD-002] [PD-005] [PD-006] workModel: `readiness/2417-critic-despawn-review-recovery/work-model.json` and `readiness/2417-critic-despawn-review-recovery/analysis.json` refresh from the plan/tasks/evidence sources produced by this work item's own SDD lifecycle; no OTHER work item's generated view is impacted, since `Review.fs`/`ReviewApplication.fs`/`independent-review.md` carry no generated-view projection of their own.

## Accepted Deferrals
No accepted plan deferrals recorded.

## Planning Findings
No blocking planning findings recorded.

## Advisory Notes
- Optional Governance pointers remain compatibility facts only.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd tasks --work 2417-critic-despawn-review-recovery`.
