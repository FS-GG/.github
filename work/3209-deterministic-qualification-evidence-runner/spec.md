---
schemaVersion: 1
workId: 3209-deterministic-qualification-evidence-runner
title: Deterministic qualification and evidence runner
stage: specify
changeTier: tier1
status: specified
publicOrToolFacingImpact: true
---

# Deterministic qualification and evidence runner Specification

Prose status: specified

## User Value
replace prose-supervised qualification with one deterministic typed F# runner and content-addressed evidence result

## Scope
- SB-001: typed qualification and evidence core, CLI operation, exact-checkout execution, immutable tool manifest, GitHub run-job-check convergence, typed PR obligation readback, tests, package and coherent publication

## Non-Goals
- SB-002: Do not compile roadmap unit registration, acceptance sealing, indexing, or roadmap projection; child #3210 owns those boundaries.
- SB-003: Do not replace independent semantic critique with mechanical qualification.
- SB-004: Do not migrate skills to the new runner before its coherent package is published and receiver verification succeeds.

## User Stories
- US-001 (P1): As a user, I can replace prose-supervised qualification with one deterministic typed F# runner and content-addressed evidence result.

## Acceptance Scenarios
- AC-001 [US-001] [FR-001]: Given an exact clean subject checkout and declared operations, when qualification runs, then analyze, verify, ship, hosted, and fixed-point operations execute in lifecycle order and the receipt binds the exact subject revision.
- AC-002 [US-001] [FR-002]: Given a closed immutable manifest, when commands execute, then every recorded command binds its resolved tool identity and version, executor identity, artifact/result digests, and provenance; undeclared tools are refused.
- AC-003 [US-001] [FR-003]: Given a claim and evidence set, when evidence is dirty, stale, unrelated, produced by the wrong tool or executor, or inadequate for that claim, then qualification returns the corresponding typed refusal and no accepted result.
- AC-004 [US-001] [FR-004]: Given every mutation control, when its independently implemented fixture inverts the protected condition, then the production boundary emits the exact declared refusal; a fixture that shares the production implementation is refused as non-independent.
- AC-005 [US-001] [FR-005]: Given a settled fixed-point operation, when replayed with identical immutable inputs, then canonical output bytes and digest are identical and any executor/fixture provenance substitution is refused.
- AC-006 [US-001] [FR-006]: Given GitHub runs, jobs, and checks that grow or contain stale aggregates, when hosted convergence runs, then it accepts only after two stable exact-subject observations and reports typed pending/refusal states otherwise.
- AC-007 [US-001] [FR-007]: Given a PR head, when obligations are declared, then exactly one head-bound typed obligations comment—including explicit none—is created or verified by authoritative readback; missing, duplicate, malformed, or stale declarations are refused.
- AC-008 [US-001] [FR-008]: Given mechanically green evidence without an accepted independent semantic review input, when final qualification is requested, then acceptance is refused.
- AC-009 [US-001] [FR-009]: Given canonical #3208 primitives, when the runner serializes identity, lifecycle, telemetry, or evidence, then it uses those typed contracts and emits byte-identical canonical results without invoking Python authority.
- AC-010 [US-001] [FR-010]: Given a reviewed implementation, when release proceeds, then the coherent package is published to both feeds and receiver verification succeeds before any adoption change.
- AC-011 [US-001] [FR-011]: Given each positive control, when its named inverted fixture executes, then the positive passes, the inversion fails for the exact expected reason, and package/coherent-release plus a later clean GS2 pilot are recorded.

## Functional Requirements
- FR-001: run declared analyze, verify, ship, hosted, and fixed-point operations from an isolated checkout bound to the exact subject revision (Stories: US-001; Acceptance: AC-001)
- FR-002: resolve tools only from a closed immutable manifest and record exact tool identity, version, command, executor, fixture, artifact, result, and digest provenance (Stories: US-001; Acceptance: AC-001)
- FR-003: bind every claim to adequate executed evidence and refuse dirty checkout, unrelated artifacts, wrong tools, wrong executors, stale subjects, and evidence-to-claim mismatch (Stories: US-001; Acceptance: AC-001)
- FR-004: require independently implemented mutation fixtures and observe the exact typed refusal for every mutation control (Stories: US-001; Acceptance: AC-001)
- FR-005: replay fixed-point operations byte-identically and prove independent executor and fixture provenance (Stories: US-001; Acceptance: AC-001)
- FR-006: converge the exact-subject GitHub workflow run, job, and check set instead of trusting a stale aggregate (Stories: US-001; Acceptance: AC-001)
- FR-007: create or verify exactly one head-bound typed PR obligation declaration, including explicit no-obligations, and verify authoritative readback (Stories: US-001; Acceptance: AC-001)
- FR-008: require independently reviewed semantic adequacy as an input that mechanical qualification cannot synthesize (Stories: US-001; Acceptance: AC-001)
- FR-009: consume issue 3208 canonical serialization, identity, lifecycle, telemetry, and evidence primitives without restoring Python authority (Stories: US-001; Acceptance: AC-001)
- FR-010: publish and verify a coherent coordination package before any skill adopts the runner (Stories: US-001; Acceptance: AC-001)
- FR-011: add positive and inverted pure, CLI, independently implemented mutation, dirty-clean, wrong-tool, wrong-executor, fixed-point, claim mismatch, stale-hosted-aggregate, convergence, obligation-readback, package, coherent-release, and later-GS2 clean-checkout fixtures (Stories: US-001; Acceptance: AC-001)

## Ambiguities
No material ambiguities recorded.

## Public Or Tool-Facing Impact
- This specification is an SDD lifecycle artifact and command-report contract input.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd clarify --work 3209-deterministic-qualification-evidence-runner`.
