---
schemaVersion: 1
workId: 3209-deterministic-qualification-evidence-runner
title: Deterministic qualification and evidence runner
stage: plan
changeTier: tier1
status: planned
sourceSpec: work/3209-deterministic-qualification-evidence-runner/spec.md
sourceClarifications: work/3209-deterministic-qualification-evidence-runner/clarifications.md
sourceChecklist: work/3209-deterministic-qualification-evidence-runner/checklist.md
publicOrToolFacingImpact: true
---

# Deterministic qualification and evidence runner Plan

Prose status: planned

## Source Snapshot
- spec: work/3209-deterministic-qualification-evidence-runner/spec.md sha256:5147abde5ff1bf0f269297d555bcfd8aa5f0dc54f4291091073575de823e4a92 schemaVersion:1
- clarifications: work/3209-deterministic-qualification-evidence-runner/clarifications.md sha256:8dfc276d697537c34c990bfc1ae1500e68605bf9a42a609258e4230959670f9c schemaVersion:1
- checklist: work/3209-deterministic-qualification-evidence-runner/checklist.md sha256:9d51fa3df458114afce08cf81ab23c4a1cc1afa94b7e1937b0493745dd698351 schemaVersion:1

## Plan Scope
- Work item 3209-deterministic-qualification-evidence-runner is planned from the current specification, clarification, and checklist facts.
- Requirement count: 11.
- Clarification decision count: 6 authored decisions (the 1.5.0 report parser does not project manually expanded decision prose into its count).
- Checklist result count: 11.

## Plan Decisions
- PD-001 [AC-001] [FR-001] complete: Add `Qualification` to Core after `Telemetry`, with closed manifest, subject, operation, execution, claim, evidence, mutation, semantic-review, hosted-observation, obligation, and result types. Keep execution and GitHub IO outside Core.
- PD-002 [AC-002] [FR-002] complete: Parse one strict manifest and input envelope, reject unknown/duplicate/malformed identities, and canonicalize the accepted result through the #3208 compact-JSON/digest conventions.
- PD-003 [AC-003] [FR-003] complete: Reduce claims against exact-subject evidence keys and emit a closed finding union for dirty checkout, stale subject, undeclared/wrong tool, wrong executor, unrelated artifact, inadequate evidence, and claim mismatch.
- PD-004 [AC-004] [FR-004] complete: Represent each mutation as expected refusal plus independent fixture provenance; require distinct production/fixture implementation digests and exact refusal equality.
- PD-005 [AC-005] [FR-005] complete: Model fixed-point replay as two canonical operation observations; accept only identical subject, inputs, identities, result bytes, and digest.
- PD-006 [AC-006] [FR-006] complete: Add a pure hosted-set convergence reducer keyed by exact SHA. The GitHub layer supplies complete run/job/check observations; two consecutive stable terminal sets accept, growth, pending, foreign subject, or changed conclusions refuse/pends explicitly.
- PD-007 [AC-007] [FR-007] complete: Parse current PR obligation comments into a typed current-head census. Return `Verified`, `CreateIntent`, or exact missing/duplicate/malformed/stale refusal; execute create intents only through the existing verified comment boundary.
- PD-008 [AC-008] [FR-008] complete: Require a validated independent semantic-review receipt as a separate input to terminal qualification; mechanical evidence cannot manufacture it.
- PD-009 [AC-009] [FR-009] complete: Reuse `RuntimeUsage.TokenCounts` and the canonical JSON/digest implementation introduced by #3208 by exposing the minimal shared helper rather than duplicating serialization or arithmetic.
- PD-010 [AC-010] [FR-010] complete: Add `telemetry qualification run` (local envelope) and hosted/obligation inspection commands to the existing telemetry family; preserve strict argv validation, deterministic stdout, exit taxonomy, and privacy boundary.
- PD-011 [AC-011] [FR-011] complete: Cover every finding with positive and inverted Core tests, CLI contract/parity tests, independent mutation fixtures, GitHub convergence/readback fixtures, exact-clean-checkout package qualification, and coherent release/receiver evidence.

## Contract Impact
- PC-001 [PD-001] additive API: `Qualification` adds new public F# types and functions without changing existing signatures.
- PC-002 [PD-002] new wire contract: `fsgg.qualification.manifest/1`, `fsgg.qualification.input/1`, and `fsgg.qualification.result/1` use strict canonical JSON and reject unknown fields.
- PC-003 [PD-006] new wire contract: hosted observations explicitly carry subject SHA and complete run/job/check identity sets; the reducer never consumes GitHub aggregate status.
- PC-004 [PD-007] compatibility: existing `fsgg:delivery-obligations` and `fsgg:delivery-obligation` markers remain the authority; the runner adds typed inspection/create-intent output and no second marker grammar.
- PC-005 [PD-010] additive CLI: strict `telemetry qualification` subcommands and their argument contract are added; existing telemetry command bytes and exit codes remain unchanged.

## Verification Obligations
- VO-001 [PD-001] [PC-001] semanticTest: Core positive corpus accepts one exact clean qualification and every finding union case is reachable by a focused inversion.
- VO-002 [PD-002] [PC-002] mutationTest: independently reserialize/tamper every bound field and prove canonical/digest, unknown-field, duplicate, path, arithmetic, and subject guards fail exactly.
- VO-003 [PD-004] [PC-002] mutationTest: replace expected refusal, reuse executor, reuse fixture implementation, and omit fixture provenance; each mutation must observe its exact typed refusal.
- VO-004 [PD-005] [PC-002] deterministicReplay: run identical fixed-point inputs twice and compare bytes, then invert each identity/result input and prove refusal.
- VO-005 [PD-006] [PC-003] integrationTest: cover pending, growing set, stale aggregate, foreign SHA, changed conclusion, duplicate identity, stable two-observation convergence, and unreadable GitHub pages.
- VO-006 [PD-007] [PC-004] integrationTest: cover no-obligations, declared obligations, guarded create, verified readback, missing, duplicate, malformed, and stale-head comments.
- VO-007 [PD-010] [PC-005] commandContract: test strict argv, stdin/file decoding, canonical stdout, exit codes, and Python-free parity/e2e invocation.
- VO-008 [PD-010] [PC-005] packageTest: run full solution gates, deterministic pack comparison, coherent-set publication, dual-feed byte identity, receiver restore, and a later GS2 clean-checkout qualification.

## Performance Intent
- Parse and canonicalize each local manifest/envelope once; reducers are linear in operations, claims, evidence, mutations, and hosted identities.
- Hosted convergence pages each authoritative collection once per observation and compares stable keyed maps; it does not poll per check or rescan unrelated repository runs.
- Preserve bounded payloads and return a typed refusal when configured bounds are exceeded.

## Migration Posture
- PM-001 [PC-001] additive: introduce the runner unused by skills, publish and verify it, then let #3210 migrate the roadmap process.
- PM-002 [PC-002] failClosed: malformed or unsupported manifests/results diagnose before execution or write; there is no legacy permissive fallback.
- PM-003 [PC-004] preserveAuthority: obligation comments stay authoritative and append/write behavior remains behind the existing guarded verified GitHub boundary.

## Generated View Impact
- GV-001 [PD-001] workModel: project the qualification schemas, five contract surfaces, eight verification obligations, three migration postures, and their requirement links into `readiness/3209-deterministic-qualification-evidence-runner/work-model.json`; regeneration must be byte-identical and must not embed runtime evidence, local paths, or mutable GitHub observations.

## Accepted Deferrals
No accepted plan deferrals recorded.

## Planning Findings
No blocking planning findings recorded.

## Advisory Notes
- Optional Governance pointers remain compatibility facts only.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd tasks --work 3209-deterministic-qualification-evidence-runner`.
