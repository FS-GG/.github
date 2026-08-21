---
schemaVersion: 1
workId: 2807-review-escalation-head-progression
title: Review Escalation Head Progression
stage: plan
changeTier: tier1
status: planned
sourceSpec: work/2807-review-escalation-head-progression/spec.md
sourceClarifications: work/2807-review-escalation-head-progression/clarifications.md
sourceChecklist: work/2807-review-escalation-head-progression/checklist.md
publicOrToolFacingImpact: true
---

# Review Escalation Head Progression Plan

Prose status: planned

## Source Snapshot
- spec: work/2807-review-escalation-head-progression/spec.md sha256:e00aaf330f15437ccca35af08df03ece4beadd106d843b86eedd486fb8c827cb schemaVersion:1
- clarifications: work/2807-review-escalation-head-progression/clarifications.md sha256:e6a988340d17b96623a09c1be814ea01b8b661581b9fcd80969faea2d1cce76c schemaVersion:1
- checklist: work/2807-review-escalation-head-progression/checklist.md sha256:25c054cfc58428e7b1ac330245e0194e3c6278b6956b510314e7a5a4f2803212 schemaVersion:1

## Plan Scope
- Work item 2807-review-escalation-head-progression is planned from the current specification, clarification, and checklist facts.
- Requirement count: 5.
- Clarification decision count: 4.
- Checklist result count: 5.

## Plan Decisions
- PD-001 [AC-001] [FR-001] complete: In the exhausted-claim turnover validator, replace the all-record-heads-equal-final-head predicate with an explicit terminal-record check: exactly initial plus confirmations 1/2/3, with confirmation 3 at the draft/live/wait head.
- PD-002 [AC-002] [FR-002] complete: Keep structured parser validation responsible for each decision's own exact reviewed head and the ordered predecessor URL, digest, critic, and contiguous round chain; do not normalize or rewrite historical heads.
- PD-003 [AC-003] [FR-003] complete: Retain the existing exact final-head, completed wait, legacy backlink, current fresh claim, duplicate, and decision-kind fences unchanged; add explicit progressed-chain negative controls for missing/noncontiguous rounds and malformed backlink.
- PD-004 [AC-004] [FR-004] complete: Change the #2797 production writer fixture to deterministic distinct heads for initial and confirmations 1/2/3, prove the valid cross-claim escalation writes exactly once, and prove each added/changed gate reds under one subject mutation before restoration.
- PD-005 [AC-005] [FR-005] complete: Advance the coherent set to 0.69.0 on the source head, bind one mapped coherent-set-release obligation to that exact reviewed head, then retain the claim after guarded merge through dual-feed byte identity, clean public install, registry evidence, and the S.I.R. handoff.

## Contract Impact
- PC-001 [PD-001] [PD-002] reviewWriter: `fsgg-coord review record` accepts one previously rejected valid chain shape without changing command syntax, marker schema, round limits, or fail-closed mutation semantics.

## Verification Obligations
- VO-001 [PD-001] [PD-002] [PD-003] [PD-004] [PC-001] semanticTest: Run the compiled production-writer fixture for the exact valid four-head route and its stale-head, round-chain, backlink, claim-freshness, duplicate, and round-four controls; perform one subject mutation per touched gate and record the observed red.

## Performance Intent
No performance intent is declared for this work item.

## Migration Posture
- PM-001 [PC-001] additive: Existing single-head fixtures and all marker/ledger bytes remain compatible; only genuine ordered head progression gains authorization.

## Generated View Impact
- GV-001 [PD-004] workModel: SDD readiness views bind the focused proof and release deferral honestly; they make no pre-merge publication claim.

## Accepted Deferrals
No accepted plan deferrals recorded.

## Planning Findings
No blocking planning findings recorded.

## Advisory Notes
- Optional Governance pointers remain compatibility facts only.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd tasks --work 2807-review-escalation-head-progression`.
