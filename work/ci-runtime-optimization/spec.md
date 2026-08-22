---
schemaVersion: 1
workId: ci-runtime-optimization
title: CI Runtime Optimization Without Coverage Loss
stage: specify
changeTier: tier1
status: specified
publicOrToolFacingImpact: true
---

# CI Runtime Optimization Without Coverage Loss Specification

Prose status: specified

## User Value
Reduce pull-request CI critical-path and aggregate runner time without omitting live production, safety, mutation-accounting, or non-vacuity assertions.

## Scope
- SB-001: Optimize signature-doc mutation execution, shell-lint fixture scheduling and subject separation, coord-engine non-vacuity accounting, and add independent regression evidence for unchanged verdict semantics.

## Non-Goals
- SB-002: Do not implement later lifecycle commands or Governance enforcement in this specification.

## User Stories
- US-001 (P1): As a user, I can reduce pull-request CI critical-path and aggregate runner time without omitting live production, safety, mutation-accounting, or non-vacuity assertions.

## Acceptance Scenarios
- AC-001 [US-001] [FR-001]: Given CI Runtime Optimization Without Coverage Loss is available, when the user exercises it, then they can reduce pull-request CI critical-path and aggregate runner time without omitting live production, safety, mutation-accounting, or non-vacuity assertions.

## Functional Requirements
- FR-001: On an unrelated non-source PR, the signature-doc mutation sweep must be skipped while its stable check reports an explicit measured reason. (Stories: US-001; Acceptance: AC-001)
- FR-002: Every src project, .fs, .fsi, signature-doc checker, fixture, allowlist, or workflow change must run the complete signature-doc mutation population. (Stories: US-001; Acceptance: AC-001)
- FR-003: The signature-doc sweep must retain one green control, enumerate the same mutant population, and require every executed mutant to be killed or explicitly justified. (Stories: US-001; Acceptance: AC-001)
- FR-004: Live shell lint must continue to inspect extensionless shebang files, shell files, workflow run blocks, and composite actions. (Stories: US-001; Acceptance: AC-001)
- FR-005: The synthetic shell fixture must not repeat the authoritative live-tree lint and must run whenever its checker, installer, extractor, filters, fixture, or workflow changes. (Stories: US-001; Acceptance: AC-001)
- FR-006: Unknown shell change classification must run both live lint and the fixture rather than skip. (Stories: US-001; Acceptance: AC-001)
- FR-007: CLI, BoardOps, and Kernel suites must execute once each and non-vacuity floors must be read from their original TRX results. (Stories: US-001; Acceptance: AC-001)
- FR-008: A missing, malformed, empty, or below-floor TRX result must fail closed. (Stories: US-001; Acceptance: AC-001)
- FR-009: Required context names remain stable and no temporal, claim, publication, replay, parity, or external-state gate is removed. (Stories: US-001; Acceptance: AC-001)
- FR-010: Focused fixtures and gate inversions must prove each optimized branch can still reject its bounded defect. (Stories: US-001; Acceptance: AC-001)
- FR-011: The final SDD verify and ship stages must report ready with zero blocking diagnostics. (Stories: US-001; Acceptance: AC-001)

## Ambiguities
No material ambiguities recorded.

## Public Or Tool-Facing Impact
- This specification is an SDD lifecycle artifact and command-report contract input.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd clarify --work ci-runtime-optimization`.
