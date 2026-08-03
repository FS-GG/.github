---
schemaVersion: 1
workId: 2106-auto-publish
title: Auto Publish
stage: plan
changeTier: tier1
status: planned
sourceSpec: work/2106-auto-publish/spec.md
sourceClarifications: work/2106-auto-publish/clarifications.md
sourceChecklist: work/2106-auto-publish/checklist.md
publicOrToolFacingImpact: true
---

# Auto Publish Plan

Prose status: planned

## Source Snapshot
- spec: work/2106-auto-publish/spec.md sha256:c5d635367fdb07917d3da6b1b0c0318c6153e2646b0a510b75069890d25763a6 schemaVersion:1
- clarifications: work/2106-auto-publish/clarifications.md sha256:a44ded72a6fffb17429a08f4266af19c07778d4b7111e6ed74bda7b964b33a93 schemaVersion:1
- checklist: work/2106-auto-publish/checklist.md sha256:2d7db987dd78147a080d66e28fd0ddb22878513f7a90bc67092522b6c3191956 schemaVersion:1

## Plan Scope
- Work item 2106-auto-publish is planned from the current specification, clarification, and checklist facts.
- Requirement count: 1.
- Clarification decision count: 0.
- Checklist result count: 1.

## Plan Decisions
- PD-001 [AC-001] [FR-001] complete: Add one coordinator workflow that serializes scheduled and push triggers on a fixed FS.GG.Kit release key, invokes a pure fixture-tested state machine, and creates a tag only from its single eligible state. `release-kit.yml` remains the only publisher and preserves its one-pack, same-bytes dual-feed structure.
- PD-002 [AC-001] [FR-001] complete: An eligible release is a stable PATCH version strictly greater than both feed observations and reachable from a merged PR whose `kit-published-coherence / pr-arm` passed. Refuse MAJOR/MINOR, prerelease, absent or ambiguous provenance, existing version on either feed, feed disagreement, and unavailable observations.
- PD-003 [AC-001] [FR-001] complete: Classify feeds as not-published, both-published, partial, or unknown. Only not-published may tag; partial and unknown stop without retry and update one marker-addressed sticky escalation carrying its streak for human adjudication.
- PD-004 [AC-001] [FR-001] complete: Open/update one bot evidence PR only after a successful release run. Its body reports immutable run URL/id, resolved version, both package URLs, and the packed nuspec commit. A fixed branch plus concurrency makes branch and PR idempotent.

## Contract Impact
- PC-001 [PD-001] workflow state contract: `scripts/kit-auto-publish.py --json` returns a closed-set state, reason, version, feed observations, provenance verdict, and intended action. Fixture input drives offline tests; it has no feed/tag/PR write path.

## Verification Obligations
- VO-001 [PD-001] [PD-002] [PD-003] [PD-004] [PC-001] semanticTest: Fixture tests prove eligible PATCH planning, every refusal, two-trigger idempotency, partial-publish stop, and structural preservation of exactly one pack before both release pushes.

## Performance Intent
No performance intent is declared for this work item.

## Migration Posture
- PM-001 [PC-001] additive: The coordinator adds an automation entry point while retaining manual `release-kit.yml` dispatch for human recovery. Existing tags and feed versions are never rewritten, deleted, or replayed automatically.

## Generated View Impact
- GV-001 [PD-001] workModel: Generate readiness evidence after implementation; readiness JSON remains generated and is never manually corrected.

## Accepted Deferrals
No accepted plan deferrals recorded.

## Planning Findings
No blocking planning findings recorded.

## Advisory Notes
- Optional Governance pointers remain compatibility facts only.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd tasks --work 2106-auto-publish`.
