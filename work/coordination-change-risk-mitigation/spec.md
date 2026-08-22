---
schemaVersion: 1
workId: coordination-change-risk-mitigation
title: Coordination Change Risk Mitigation
stage: specify
changeTier: tier1
status: specified
publicOrToolFacingImpact: true
---

# Coordination Change Risk Mitigation Specification

Prose status: specified

## User Value

A coordination-engine contributor can change a command or lifecycle rule at one typed authority boundary and
receive an actionable structural failure before expensive CI or independent review. Operators can trust that
review, merge, completion, self-hosting, and release recovery advance only from exact, durable evidence rather
than from duplicated projections or issue state.

## Scope
- SB-001: Introduce one kernel-owned command catalogue for declarative command metadata and one BoardOps handler
  binding list.
- SB-002: Introduce complete fact and decision types for disputed review, delivery, merge, completion, and recovery
  transitions, consumed by both read and write paths.
- SB-003: Make completion receipt-gated and reconcile GitHub issue and Projects projections from that receipt.
- SB-004: Introduce digest-bound self-host bootstrap receipts with host acceptance and post-merge replay.
- SB-005: Add a bounded `change-completeness` gate before expensive mutation jobs and critic dispatch.
- SB-006: Add bounded model-based Review and Delivery conformance with effective consumer-divergence mutations.
- SB-007: Preserve immutable release recovery and add append-only receiver delivery receipts.
- SB-008: Deliver the change through independently mergeable compatibility slices with baseline and monthly metrics.

## Non-Goals
- SB-009: Do not generate the `Command` union or heterogeneous parser behavior.
- SB-010: Do not replace independent tests with catalogue self-assertion or remove full/mutation/release suites.
- SB-011: Do not treat issue closure, Projects status, comments, dashboards, or candidate-engine output as completion
  authority.
- SB-012: Do not rewrite immutable release assets or broaden bootstrap reasons to business-rule disagreement.
- SB-013: Do not delete predecessor paths until parity and an effective inversion have passed.

## User Stories
- US-001 (P1): As a command author, I add declarative metadata once and receive named failures for any missing parser,
  renderer, handler, documentation, or write-classification behavior.
- US-002 (P1): As a lifecycle operator, I see the same transition verdict on projections and writers because both
  consume one complete typed decision.
- US-003 (P1): As a delivery owner, I know `Done` means the exact merge and every declared obligation are receipted,
  reachable, and free of pending board writes.
- US-004 (P1): As a host accepting an engine self-change, I can audit the exact candidate bytes, refusal reason,
  evidence, decision keys, and post-merge replay.
- US-005 (P2): As a reviewer, structural omissions are rejected before my bounded review round is consumed.
- US-006 (P1): As a release operator, I can resume receiver delivery after promotion without mutating immutable assets.

## Acceptance Scenarios
- AC-001 [US-001] [FR-001]: Given a nullary command addition, when change completeness runs, then one authored
  descriptor and one handler binding derive all metadata and positive cases, while removal of any required behavior
  produces a named red.
- AC-002 [US-002] [FR-002]: Given every bounded review verdict, check state, round, head, wait, and claim generation,
  when projection, writer admission, and reducer transition are evaluated, then all three return the same decision.
- AC-003 [US-003] [FR-003]: Given a merged item with absent, stale, contradictory, or incomplete obligation evidence,
  when completion is reconciled, then no valid completion receipt is appended and `Done` is refused or corrected.
- AC-004 [US-004] [FR-004]: Given a shared engine that refuses for an enumerated bootstrap reason, when a candidate
  proposes a transition, then no write occurs without a digest-bound receipt, stable-verifier acceptance, accountable
  host acceptance, and post-merge replay.
- AC-005 [US-005] [FR-005]: Given an authored structural omission, when CI is scheduled, then `change-completeness`
  reports the actionable red before critic dispatch and targets completion within five minutes.
- AC-006 [US-002] [FR-006]: Given the bounded Review and Delivery state spaces, when any one shared predicate clause
  is removed or one consumer forks the decision, then the model suite fails.
- AC-007 [US-006] [FR-007]: Given a promoted immutable release with partial receiver delivery, when recovery runs,
  then it verifies the existing journal, performs zero release-asset writes, resumes receivers, and appends every
  required receiver receipt outside the immutable asset set.
- AC-008 [US-001] [US-005] [FR-008]: Given each rollout phase, when its predecessor path is considered for removal,
  then byte/behavior parity and an effective inversion have passed, and the phase remains independently reversible.
- AC-009 [US-003] [US-006] [FR-009]: Given at least twenty engine PRs and a monthly measurement window, when metrics
  are reported, then command edit count, first-actionable-red time, structural critic findings, review rounds,
  divergences, premature `Done`, self-host receipts, and release interventions are all measured.

## Functional Requirements
- FR-001: The engine MUST own one exhaustive command catalogue and one BoardOps handler binding list, deriving declarative command metadata and conformance cases while keeping heterogeneous parser arms explicit. (Stories: US-001; Acceptance: AC-001)
- FR-002: Every disputed lifecycle transition MUST expose one complete typed fact input and decision output consumed unchanged by projection and writer admission; writers may add only freshness, idempotency, and effect mechanics. (Stories: US-002; Acceptance: AC-002)
- FR-003: Completion MUST require an exact-merge `DeliveryCompletionReceipt` proving reachability, verified declared obligations, no contradictions, and zero pending board writes before projecting issue closure or `Status=Done`. (Stories: US-003; Acceptance: AC-003)
- FR-004: Candidate-engine writes MUST require an enumerated bootstrap reason, a digest-bound `SelfHostBootstrapReceipt`, stable-verifier validation, accountable host acceptance, and post-merge shared-engine replay. (Stories: US-004; Acceptance: AC-004)
- FR-005: A required `change-completeness` context MUST run structural closure, parity, path, message, provenance, and focused route checks before independent review and expensive mutation work, with a five-minute ordinary-change target. (Stories: US-005; Acceptance: AC-005)
- FR-006: Bounded Review and Delivery models MUST prove projection decision, writer admission, and reducer transition parity, and MUST include effective mutations that make every consumer red on divergence. (Stories: US-002; Acceptance: AC-006)
- FR-007: Immutable release recovery MUST verify existing journal and artifact identity, perform zero asset mutation, resume downstream delivery, and append verified receiver receipts outside the immutable asset set. (Stories: US-006; Acceptance: AC-007)
- FR-008: Rollout MUST use independently mergeable, reversible compatibility slices and MUST retain predecessor paths until behavior parity and an effective inversion pass. (Stories: US-001, US-005; Acceptance: AC-008)
- FR-009: The implementation MUST establish phase-0 baselines and monthly success metrics, with zero divergence, premature `Done`, and unreceipted self-host writes as invariant targets. (Stories: US-003, US-006; Acceptance: AC-009)

## Ambiguities
- AMB-001: Whether the command catalogue should initially coexist with or immediately replace the BoardOps
  `Implementations` record and duplicate commands list.
- AMB-002: Which existing JSON schemas must change for lifecycle decisions and receipts versus translating new
  internal types at current adapter boundaries.
- AMB-003: When `change-completeness` becomes required rather than advisory.
- AMB-004: Where receiver delivery receipts live so they remain append-only and outside immutable release assets.

## Public Or Tool-Facing Impact
- Command-contract, help, render, write-classification, and documentation projections become catalogue-derived.
- New completion, bootstrap, and receiver-receipt contracts become durable tool-facing evidence.
- GitHub close events and Projects automation become reconciliation triggers rather than completion authority.
- CI gains the `change-completeness` context and dependency ordering.
- Existing JSON schemas remain stable during the compatibility phase unless a separately versioned migration is
  explicitly accepted.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd clarify --work coordination-change-risk-mitigation`.
