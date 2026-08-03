---
schemaVersion: 1
workId: 2132-release-train-state-machine
title: "Coord release: resumable cross-repo NuGet train state machine"
stage: plan
changeTier: tier1
status: planned
sourceSpec: work/2132-release-train-state-machine/spec.md
sourceClarifications: work/2132-release-train-state-machine/clarifications.md
sourceChecklist: work/2132-release-train-state-machine/checklist.md
publicOrToolFacingImpact: true
---

# Coord release: resumable cross-repo NuGet train state machine Plan

## Source Snapshot
- spec: work/2132-release-train-state-machine/spec.md
- clarifications: work/2132-release-train-state-machine/clarifications.md
- checklist: work/2132-release-train-state-machine/checklist.md

## Plan Scope
- Add a versioned JSON release-run document and a deterministic `inspect`, `plan`,
  `advance`, and `verify` command surface in the release-train tooling.
- Import the existing audit, workflow, and package verification reports as evidence;
  preserve those producers and their repository-specific checks.
- Derive the roster, packables, release order, package counts and registry gate from
  those facts plus explicit, evidence-bound decisions.

## Plan Decisions
- PD-001 [FR-001] complete: Persist run id, source digests, ordered releases,
  evidence and next action in a schema-versioned state document. Re-running any
  command over unchanged inputs is idempotent.
- PD-002 [FR-002] complete: Require every consumer release to name verified
  producer artifacts and pins before its action can be returned.
- PD-003 [FR-003] complete: Treat a missing feed, payload mismatch, tag mismatch,
  package-count mismatch, stale registry, missing materialized consumer, or failed
  downstream propagation as a named non-success state; partial feed states require
  a human escalation rather than a retry action.
- PD-004 [FR-004] complete: Exercise the required release states with offline JSON
  fixtures, so no test makes a publish or queries a feed.

## Contract Impact
- PC-001 [PD-001] command report: `release-train-state.fsx` exposes
  `inspect|plan|advance|verify` and emits one JSON action/receipt projection.

## Verification Obligations
- VO-001 [PD-004] [PC-001] semanticTest: Cover no release, topological producer
  ordering, missing package, partial feed publication, tag mismatch, stale registry,
  missing consumer embedding, and a complete train.

## Performance Intent
No performance intent is declared for this work item.

## Migration Posture
- PM-001 [PC-001] additive: Existing audit, workflow, verification, and status
  commands remain valid; this coordinator consumes their JSON output.

## Generated View Impact
No generated view change is required.

## Accepted Deferrals
No accepted plan deferrals recorded.

## Planning Findings
No blocking planning findings recorded.

## Advisory Notes
No advisory notes recorded.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd tasks --work 2132-release-train-state-machine`.
