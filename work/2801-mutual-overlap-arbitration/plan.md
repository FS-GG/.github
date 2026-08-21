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
- spec: work/2801-mutual-overlap-arbitration/spec.md sha256:cd06975eb08f678c5d438ce38cfecada1d98114dc80849eddc69f09c4e028523 schemaVersion:1
- clarifications: work/2801-mutual-overlap-arbitration/clarifications.md sha256:2b176f372fc1dc74393613799f7a8a0e61fc5fb62ea71b4d5e611d303f148da0 schemaVersion:1
- checklist: work/2801-mutual-overlap-arbitration/checklist.md sha256:ccfeb3e86c4300a094c4d3ea100f3bb57c1a5e8381296b3407d722583d48529c schemaVersion:1

## Plan Scope
- Define narrow versioned receipt/outcome types in `Client.fs/.fsi`, pure validation and update decisions in `Client.fs`, and interpret requested GitHub effects in `Writes.fs/.fsi`.
- Extend existing comment readers and writer primitives; keep claim comments as the only ownership authority and ADR-0051 rooms as the only communication primitive.
- Exercise the pure detector in `ClaimOverlapTests`, the fault-injected writer in `WriteTests`, and the built `scripts/fsgg-coord` route in `writes.sh`.
- Update only the overlap reference, placing the automatic route before manual negotiation and recording class-row folding.

## Plan Decisions
- PD-001 [AC-001] [AC-002] [FR-001] complete: Parse schema-versioned wait receipts, canonicalize participants/tokens, and validate the exact current claim generations and overlap before returning any authoritative edge.
- PD-002 [AC-001] [AC-002] [FR-002] complete: Fold current receipts by directed participant pair, reject duplicate/conflicting edges, and detect only two opposite edges with identical canonical shared tokens; dependency or unrelated-room facts veto automatic arbitration.
- PD-003 [AC-003] [FR-003] complete: Derive a stable cycle digest, write generation-bound freezes to both participants before the reciprocal edge becomes durable, and request room create/reuse plus both back-references. Each frozen generation also gets an issue-body hint, allowing production `widen`/`set-paths` to load the durable freeze ledger only for a currently hinted generation; ordinary clean-path updates retain the pre-existing REST budget. A hinted generation without its matching receipt refuses closed. Production writers refuse removal of a frozen shared token before precedence; precedence narrows only through its dedicated writer and leaves the loser frozen for guarded resume.
- PD-004 [AC-004] [FR-004] complete: Validate a single digest-linked precedence head per cycle. Equal-revision disagreement, missing prior digest, same participant, and missing measured reversal reason return closed conflicts before effects.
- PD-005 [AC-005] [FR-005] complete: Compute loser paths by removing only canonical shared tokens, then have the writer replace the path declaration and re-read the claim marker/path census before reporting narrowed.
- PD-006 [AC-006] [FR-006] complete: Model loser resume as independently observed facts/effects: winner closed; local `origin/main` equals a fresh server `ls-remote` result; that exact fetched ref is an ancestor of `HEAD`; overlap is clear; paths are explicitly re-widened; and any open loser PR has a passing structured review bound to its exact local and remote head.
- PD-007 [AC-003] [AC-004] [AC-005] [AC-006] [AC-007] [FR-007] complete: Give every effect a deterministic key and reconcile ambiguous errors from a complete live re-read. Return applied, already-applied, conflict, stale, unreadable, or retryable states; never infer success from transport completion.
- PD-008 [AC-001] [AC-002] [AC-003] [AC-004] [AC-005] [AC-006] [AC-007] [AC-008] [FR-008] complete: Test every pure predicate and writer boundary, run through the compiled client, and capture bounded independent source inversions that make the focused gate red before restoration.
- PD-009 [AC-003] [AC-008] [FR-009] complete: Add one decision-changing automatic-first paragraph and one class-granularity sentence to the existing overlap reference; avoid generic guidance or per-example child policy.
- PD-010 [AC-009] [AC-010] [FR-010] complete: Anchor every production orchestrator route to the checked-in authority `FS-GG/.github#2801`, rejecting any alternative caller ref before lease IO. Project that authority issue's complete comment census into one live immutable lease and its generation-bound, idempotent external requests. Request ref plus minted caller bind repository/holder identity; a foreign holder can only append the request, while the current holder writes the deterministic first external block to board Severity Critical before ordinary work.
- PD-011 [AC-011] [AC-012] [AC-013] [FR-011] complete: When no lease is live, derive max-generation plus one and acquire it with a lowest-comment-id CAS. Re-read after every boundary; losing contenders remove only their own comments, while stale generations, duplicate live authority, malformed receipts, and incomplete reads refuse.

## Contract Impact
- PC-001 [PD-001] [PD-004] additive typed contract: `Client.fsi` exposes schema-versioned wait/precedence receipts and closed inspection/arbitration outcomes; existing claim and overlap entry points remain compatible.
- PC-002 [PD-003] [PD-005] [PD-006] additive writer contract: `Writes.fsi` exposes room/precedence/narrow/resume effect application with typed observed post-state and idempotency keys.
- PC-003 [PD-010] [PD-011] additive authority contract: `Client.fsi` exposes lease/request snapshots and closed route/acquire/refuse decisions; `Writes.fsi` exposes only the generation CAS needed to elect one authority.

## Verification Obligations
- VO-001 [PD-001] [PD-002] [PC-001] semanticTest: `ClaimOverlapTests` covers valid reciprocal order independence and inversions/negative controls for schema, generations, overlap, direction, shared tokens, dependency, and room predicates.
- VO-002 [PD-003] [PD-004] [PC-001] semanticTest: pure arbitration tests prove stable room identity, current precedence, revision linking, winner/loser distinctness, conflict/stale/unreadable refusal, and predicate inversions.
- VO-003 [PD-005] [PD-006] [PC-002] semanticTest: writer tests prove loser claim retention, exact shared-token narrowing, winner preservation, landing/rebase/re-overlap/re-widen sequencing, and exact-head review gating.
- VO-004 [PD-007] [PC-002] faultInjection: inject response loss/failure before and after each room, back-reference, precedence, and path write; retry and prove one converged post-state.
- VO-005 [PD-008] productionRoute: build the CLI and run `tests/coord-engine-e2e/writes.sh` over the `.github#2772`/`.github#2797` seeded cycle and recovery route.
- VO-006 [PD-008] mutation: independently invert every detector/arbitration predicate in a bounded worktree, observe the targeted gate red, restore, and rerun green.
- VO-007 [PD-009] policy: verify the automatic route precedes manual negotiation and the class-row folding sentence is present in the shipped skill reference.
- VO-008 [PD-010] [PD-011] [PC-003] semanticTest: pure, writer, and compiled-route tests plus five surgical mutations prove active-A refusal, A priority promotion, no-A takeover, stale-A refusal, and one-winner two-B acquisition.

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
