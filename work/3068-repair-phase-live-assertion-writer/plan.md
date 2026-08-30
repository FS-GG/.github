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
- spec: work/3068-repair-phase-live-assertion-writer/spec.md sha256:70c2e2cff332223aa8835aea1c38e3975f30cbf3b12f7c2cab51dffea782bf5b schemaVersion:1
- clarifications: work/3068-repair-phase-live-assertion-writer/clarifications.md sha256:b2301022480cdf69c79e9d73099876b691207faef5688763496c08f3fea7bb7a schemaVersion:1
- checklist: work/3068-repair-phase-live-assertion-writer/checklist.md sha256:122c879f8020debe5d805c42bb6beccd5af3044570eba24a49a4e93338b1afb1 schemaVersion:1

## Plan Scope
- Work item 3068-repair-phase-live-assertion-writer is planned from the current specification, clarification, and checklist facts.
- Requirement count: 4.
- Clarification decision count: 0.
- Checklist result count: 4.

## Plan Decisions
- PD-001 [AC-001] [FR-001] complete: Add a host-owned `review assert-repair` live command that derives PR and current review facts, validates the supplied accountable grantor and reason, and appends one canonical comment-shaped assertion through the typed GitHub adapter.
- PD-002 [AC-002] [FR-002] complete: Parse the assertion in the live review snapshot and pass it into every `Review.inspect` call used by inspect, wait-enter, and review-record; preserve the pure reducer as the sole transition authority.
- PD-003 [AC-003] [FR-003] complete: Reject malformed or duplicate markers at read time and reject stale review/head/PR plus implementer/current-critic grantors at write and inspect time, with Lifecycle and shell E2E inversion cases for each boundary.
- PD-004 [AC-004] [FR-004] complete: Extend the public command contract and render an executable `review assert-repair` command when the state needs the accountable fact, followed by the exact repair-confirmation wait and repair-phase record actions after it lands.

## Contract Impact
- PC-001 [PD-001] [PD-004] command report: `fsgg-coord review assert-repair [repair-phase] REF REVIEW-URL REASON --pr N --json` is a new additive command; successful output derives the grantor from the caller identity and binds the canonical comment URL plus exact review/head/purpose facts.

## Verification Obligations
- VO-001 [PD-001] [PD-002] [PD-003] [PD-004] [PC-001] semanticTest: Run the full Lifecycle unit suite and engine E2E, including exhaustion-to-acceptance production-route coverage and explicit stale/wrong/self/duplicate/malformed inversions.

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
- The writer is deliberately narrow: a third-party assertion unblocks only the exact unchanged-head review it names and creates no review verdict by itself.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd tasks --work 3068-repair-phase-live-assertion-writer`.
