---
schemaVersion: 1
workId: 3068-repair-phase-live-assertion-writer
title: Repair Phase Live Assertion Writer
stage: plan
changeTier: tier1
status: planned
sourceSpec: work/3068-repair-phase-live-assertion-writer/spec.md
sourceClarifications: work/3068-repair-phase-live-assertion-writer/clarifications.md
sourceChecklist: work/3068-repair-phase-live-assertion-writer/checklist.md
publicOrToolFacingImpact: true
---

# Repair Phase Live Assertion Writer Plan

Prose status: planned

## Source Snapshot
- spec: work/3068-repair-phase-live-assertion-writer/spec.md sha256:f7dc0eed94e7261f36961b3c223997a556ea2d616d6e3945967fc9a198961229 schemaVersion:1
- clarifications: work/3068-repair-phase-live-assertion-writer/clarifications.md sha256:b2301022480cdf69c79e9d73099876b691207faef5688763496c08f3fea7bb7a schemaVersion:1
- checklist: work/3068-repair-phase-live-assertion-writer/checklist.md sha256:2a8c39de6dcd1a392177550baa64b81157b4a3b60cc13b38fc3f6b8747c8937c schemaVersion:1

## Plan Scope
- Work item 3068-repair-phase-live-assertion-writer is planned from the current specification, clarification, and checklist facts.
- Requirement count: 4.
- Clarification decision count: 0.
- Checklist result count: 4.

## Plan Decisions
- PD-001 [AC-001] [FR-001] complete: Add a host-owned `review assert-repair` live command that derives PR and current review facts, validates the supplied accountable grantor and reason, and appends one canonical comment-shaped assertion through the typed GitHub adapter.
- PD-002 [AC-002] [FR-002] complete: Parse the assertion in the live review snapshot and pass it into every `Review.inspect` call used by inspect, wait-enter, and review-record; preserve the pure reducer as the sole transition authority.
- PD-003 [AC-003] [FR-003] complete: Reject malformed or duplicate markers at read time and reject stale review/head/PR, implementer/current-critic grantors, and either direction of caller-purpose mismatch before any append, with Lifecycle and black-box shell E2E inversion cases for each boundary and explicit zero assertion/wait mutation proof.
- PD-004 [AC-004] [FR-004] complete: Share one live predecessor/purpose derivation between inspect and the writer, render its executable purpose-bearing `review assert-repair` command, and treat caller syntax only as an assertion that must match the derived purpose; fail closed on unreadable, malformed, ambiguous, or mismatched predecessor evidence before immutable authority is written.

## Contract Impact
- PC-001 [PD-001] [PD-004] command report: `fsgg-coord review assert-repair [repair-phase] REF REVIEW-URL REASON --pr N --json` is a new additive command; successful output derives the grantor from the caller identity and binds the canonical comment URL plus exact review/head/purpose facts.

## Verification Obligations
- VO-001 [PD-001] [PD-002] [PD-003] [PD-004] [PC-001] semanticTest: Run the full Lifecycle unit suite and engine E2E, including exhaustion-to-acceptance production-route coverage, a distinct item/exhausted-PR/current-PR topology, literal emitted-command execution, explicit stale/wrong/self/duplicate/malformed inversions, and black-box wrong-purpose controls in both directions that prove zero assertion and wait mutation.

## Performance Intent
No performance intent is declared for this work item.

## Migration Posture
- PM-001 [PC-001] additive: Existing review commands and ledgers remain readable; the assertion is a new append-only marker and no compatibility-only event gains authority.

## Generated View Impact
- GV-001 [PD-001] [PD-002] workModel: SDD readiness views refresh from the authored package; runtime state remains GitHub comment authority and is never generated from SDD prose.

## Accepted Deferrals
No accepted plan deferrals recorded.

## Planning Findings
No blocking planning findings recorded.

## Advisory Notes
- The writer is deliberately narrow: a third-party assertion unblocks only the exact unchanged-head review it names, derives immutable semantic purpose from live state, and creates no review verdict by itself.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd tasks --work 3068-repair-phase-live-assertion-writer`.
