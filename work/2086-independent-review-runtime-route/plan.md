---
schemaVersion: 1
workId: 2086-independent-review-runtime-route
title: Require critic production-route execution evidence
stage: plan
changeTier: tier1
status: planned
sourceSpec: work/2086-independent-review-runtime-route/spec.md
sourceClarifications: work/2086-independent-review-runtime-route/clarifications.md
sourceChecklist: work/2086-independent-review-runtime-route/checklist.md
publicOrToolFacingImpact: true
---

# Require critic production-route execution evidence Plan

Prose status: planned

## Source Snapshot
- spec: work/2086-independent-review-runtime-route/spec.md sha256:cb3c18d5182da42f9eb24f5a5b74bd3f104424c347c0ee1d8eb601eb9abe83f9 schemaVersion:1
- clarifications: work/2086-independent-review-runtime-route/clarifications.md sha256:412024357517f8a6a82b4748fcb5328770d043e992b27cf7b86eb7d7b3ae3fd2 schemaVersion:1
- checklist: work/2086-independent-review-runtime-route/checklist.md sha256:c3168148bc840d099dcd02bac354570ac63ac244fbfe5279876ab903fedc5fa5 schemaVersion:1

## Plan Scope
- Extend the canonical `pnext-item` independent-review contract in both supported skill roots.
- Extend the live `Driver.parseReviewComments` acceptance boundary so a passing review marker carries
  a machine-readable route-applicability decision and the corresponding evidence shape.
- Require runtime-route comparison only when the PR's review subject has meaningful behavior reachable
  through more than one route; retain source review as an independent requirement.
- Add the worker handoff expectation, a portable Rogue3-derived example, and a falsifiable contract
  fixture that rejects source-only review evidence for this class of claim.
- Regenerate/reconcile the skill registry and package the receiver-visible contract as a Kit minor.

## Plan Decisions
- PD-001 [AC-001] [FR-001] complete: A critic compares a production route with a meaningful alternate
  route against the built artifact, records executable measurement evidence, and treats source-only
  certification as incomplete; a no-comparison boundary must be explicit rather than silently passing.
- PD-002 [AC-001] [FR-001] complete: Phrase the Rogue3 audio-route observation as a reusable example
  of route divergence, not a specialized audio rule.
- PD-003 [AC-001] [FR-001] complete: Require every passing initial/confirmation marker to declare
  `route-applicability`; the meaningful case carries the built artifact, executed command or
  measurement, compared routes, and observed result, while the not-meaningful case carries a bounded
  reason. Reject missing, duplicate, empty, unknown, or cross-case fields in the typed parser.

## Contract Impact
- PC-001 [PD-001] skill and marker contract: Materialized `pnext-item` gives workers, critics, and hosts
  a new mandatory review-evidence obligation; the live marker parser enforces its applicability/evidence
  union; `FS.GG.Kit` advances 0.30.0 to 0.31.0.

## Verification Obligations
- VO-001 [PD-001] [PC-001] semanticTest: `review-round-contract.py` proves both authored roots match
  and Core/CLI tests prove meaningful and not-meaningful positive cases plus source-only, incomplete,
  duplicate, empty, unknown, and mismatched-shape negative cases at the live acceptance parser.
- VO-002 [PD-003] [PC-001] integrationTest: Run `tests/skill-quality/run.sh`, generated projection
  freshness, registry reconciliation/check, and `FS.GG.Kit` package verification.

## Performance Intent
No performance intent is declared for this work item.

## Migration Posture
- PM-001 [PC-001] additive: Existing reviews retain their source-review obligations; newly materialized
  kit consumers receive the additional executable-evidence rule through the normal versioned update path.

## Generated View Impact
- GV-001 [PD-001] workModel: Refresh SDD readiness and keep `.agents`/`.claude` skill payloads,
  producer manifests, and registry digest projections coherent.

## Accepted Deferrals
No accepted plan deferrals recorded.

## Planning Findings
No blocking planning findings recorded.

## Advisory Notes
- Optional Governance pointers remain compatibility facts only.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd tasks --work 2086-independent-review-runtime-route`.
