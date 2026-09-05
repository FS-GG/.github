---
schemaVersion: 1
workId: 3242-ignore-generated-sdd-artifacts
title: Accept independently regenerated ignored SDD artifacts
stage: specify
changeTier: tier1
status: specified
publicOrToolFacingImpact: true
---

# Accept independently regenerated ignored SDD artifacts Specification

Prose status: specified

## User Value
Roadmap-unit acceptance can seal a standard SDD candidate whose generated readiness outputs are ignored, while preserving independent exact-candidate verification.

## Scope
- SB-001: The live roadmap acceptance observer and focused transaction tests.

## Non-Goals
- SB-002: Do not weaken lifecycle, qualification, review, pull-request, revision-binding, or independent SDD verification authority.

## User Stories
- US-001 (P1): As a roadmap driver, I can accept an immutable candidate using independently regenerated SDD outputs without committing ignored generated files.

## Acceptance Scenarios
- AC-001 [US-001] [FR-001] [FR-002]: Given a candidate with standard ignored readiness outputs, when pinned SDD analyze, verify, and ship independently regenerate canonical matching observations and a complete work model at the exact candidate, then live acceptance succeeds without remote file-at-ref copies.
- AC-002 [US-001] [FR-003]: Given mismatched or missing regenerated observations, incomplete tasks, a moved or dirty checkout, or a qualification artifact mismatch, when live acceptance runs, then it refuses.

## Functional Requirements
- FR-001: Remove only the redundant remote file-at-ref reads for generated analysis.json, verify.json, ship-verdict.json, and work-model.json. (Stories: US-001; Acceptance: AC-001)
- FR-002: Retain pinned fsgg-sdd 1.5.0 execution in an independently fetched exact-candidate checkout, canonical observation comparison, complete work-model validation, exact HEAD verification, and clean-status verification. (Stories: US-001; Acceptance: AC-001)
- FR-003: Retain qualification artifact binding and every existing live claim, lifecycle, review, PR, merge, protected-main, preparation, and revision-binding refusal. (Stories: US-001; Acceptance: AC-002)
- FR-004: Add a regression proving live authority no longer requires generated readiness files from GitHub while the independent observer remains mandatory. (Stories: US-001; Acceptance: AC-001, AC-002)

## Ambiguities
No material ambiguities recorded.

## Public Or Tool-Facing Impact
- Compatibility-preserving repair to the roadmap unit acceptance authority boundary.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd clarify --work 3242-ignore-generated-sdd-artifacts`.
