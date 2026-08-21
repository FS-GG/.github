---
schemaVersion: 1
workId: 2801-mutual-overlap-arbitration
title: Automatic mutual-overlap arbitration
stage: plan
changeTier: tier1
status: planned
sourceSpec: work/2801-mutual-overlap-arbitration/spec.md
sourceClarifications: work/2801-mutual-overlap-arbitration/clarifications.md
sourceChecklist: work/2801-mutual-overlap-arbitration/checklist.md
publicOrToolFacingImpact: true
---

# Automatic mutual-overlap arbitration Plan

Prose status: planned

## Source Snapshot
- spec: work/2801-mutual-overlap-arbitration/spec.md sha256:87d4eabcd6dfefd3770fbef5153a4d33cfc15dda9465807aac23b0e7337d3e01 schemaVersion:1
- clarifications: work/2801-mutual-overlap-arbitration/clarifications.md sha256:4a1a4493d365c0797a04e9e171d20ed83a845b34a1d995f5b2612324c62a1c36 schemaVersion:1
- checklist: work/2801-mutual-overlap-arbitration/checklist.md sha256:f975e64cdb78d3e9e62945d5f9292b2e8c1ac4c462386062f26762d7ad7661fe schemaVersion:1

## Plan Scope
- Define narrow versioned receipt/outcome types in `Client.fs/.fsi`, pure validation and update decisions in `Client.fs`, and interpret requested GitHub effects in `Writes.fs/.fsi`.
- Extend existing comment readers and writer primitives; keep claim comments as the only ownership authority and ADR-0051 rooms as the only communication primitive.
- Exercise the pure detector in `ClaimOverlapTests`, the fault-injected writer in `WriteTests`, and the built `scripts/fsgg-coord` route in `writes.sh`.
- Update only the overlap reference, placing the automatic route before manual negotiation and recording class-row folding.

## Plan Decisions
- PD-001 [AC-001] [AC-002] [FR-001] complete: Parse schema-versioned wait receipts, canonicalize participants/tokens, and validate the exact current claim generations and overlap before returning any authoritative edge.
- PD-002 [AC-001] [AC-002] [FR-002] complete: Fold current receipts by directed participant pair, reject duplicate/conflicting edges, and detect only two opposite edges with identical canonical shared tokens; dependency or unrelated-room facts veto automatic arbitration.
- PD-003 [AC-003] [FR-003] complete: Derive a stable cycle digest and request room create/reuse plus both back-references. Freeze means shared-token mutations return the cycle state until precedence applies.
- PD-004 [AC-004] [FR-004] complete: Validate a single digest-linked precedence head per cycle. Equal-revision disagreement, missing prior digest, same participant, and missing measured reversal reason return closed conflicts before effects.
- PD-005 [AC-005] [FR-005] complete: Compute loser paths by removing only canonical shared tokens, then have the writer replace the path declaration and re-read the claim marker/path census before reporting narrowed.
- PD-006 [AC-006] [FR-006] complete: Model loser resume as explicit facts/effects: winner merged, current base fetched, loser rebased, overlap clear, paths re-widened, and review current for the resulting head where required.
- PD-007 [AC-003] [AC-004] [AC-005] [AC-006] [AC-007] [FR-007] complete: Give every effect a deterministic key and reconcile ambiguous errors from a complete live re-read. Return applied, already-applied, conflict, stale, unreadable, or retryable states; never infer success from transport completion.
- PD-008 [AC-001] [AC-002] [AC-003] [AC-004] [AC-005] [AC-006] [AC-007] [AC-008] [FR-008] complete: Test every pure predicate and writer boundary, run through the compiled client, and capture bounded independent source inversions that make the focused gate red before restoration.
- PD-009 [AC-003] [AC-008] [FR-009] complete: Add one decision-changing automatic-first paragraph and one class-granularity sentence to the existing overlap reference; avoid generic guidance or per-example child policy.

## Contract Impact
- PC-001 [PD-001] [PD-004] additive typed contract: `Client.fsi` exposes schema-versioned wait/precedence receipts and closed inspection/arbitration outcomes; existing claim and overlap entry points remain compatible.
- PC-002 [PD-003] [PD-005] [PD-006] additive writer contract: `Writes.fsi` exposes room/precedence/narrow/resume effect application with typed observed post-state and idempotency keys.

## Verification Obligations
- VO-001 [PD-001] [PD-002] [PC-001] semanticTest: `ClaimOverlapTests` covers valid reciprocal order independence and inversions/negative controls for schema, generations, overlap, direction, shared tokens, dependency, and room predicates.
- VO-002 [PD-003] [PD-004] [PC-001] semanticTest: pure arbitration tests prove stable room identity, current precedence, revision linking, winner/loser distinctness, conflict/stale/unreadable refusal, and predicate inversions.
- VO-003 [PD-005] [PD-006] [PC-002] semanticTest: writer tests prove loser claim retention, exact shared-token narrowing, winner preservation, landing/rebase/re-overlap/re-widen sequencing, and exact-head review gating.
- VO-004 [PD-007] [PC-002] faultInjection: inject response loss/failure before and after each room, back-reference, precedence, and path write; retry and prove one converged post-state.
- VO-005 [PD-008] productionRoute: build the CLI and run `tests/coord-engine-e2e/writes.sh` over the `.github#2772`/`.github#2797` seeded cycle and recovery route.
- VO-006 [PD-008] mutation: independently invert every detector/arbitration predicate in a bounded worktree, observe the targeted gate red, restore, and rerun green.
- VO-007 [PD-009] policy: verify the automatic route precedes manual negotiation and the class-row folding sentence is present in the shipped skill reference.

## Performance Intent
No performance intent is declared for this work item.

## Migration Posture
- PM-001 [PC-001] additive: existing comments without the new typed markers remain ordinary coordination data and authorize no automatic arbitration; no migration or backfill is inferred.

## Generated View Impact
- GV-001 [PD-001] workModel: readiness artifacts capture implementation and verification evidence but never become live overlap authority.

## Accepted Deferrals
No accepted plan deferrals recorded.

## Planning Findings
No blocking planning findings recorded.

## Advisory Notes
- Optional Governance pointers remain compatibility facts only.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd tasks --work 2801-mutual-overlap-arbitration`.
