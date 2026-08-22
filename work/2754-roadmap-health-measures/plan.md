---
schemaVersion: 1
workId: 2754-roadmap-health-measures
title: Derive and score roadmap health measures
stage: plan
changeTier: tier1
status: planned
sourceSpec: work/2754-roadmap-health-measures/spec.md
sourceClarifications: work/2754-roadmap-health-measures/clarifications.md
sourceChecklist: work/2754-roadmap-health-measures/checklist.md
publicOrToolFacingImpact: true
---

# Derive and score roadmap health measures Plan

Prose status: planned

## Source Snapshot
- spec: work/2754-roadmap-health-measures/spec.md sha256:4e1da5cfb7600341fa821e471ccb0b685dce86ebe28b418e1915f0a7305f4fce schemaVersion:1
- clarifications: work/2754-roadmap-health-measures/clarifications.md sha256:80e63e71e184eeada45e5398839a4c8e20a2a369e0ea3df3ab48c8721872bf20 schemaVersion:1
- checklist: work/2754-roadmap-health-measures/checklist.md sha256:1a98af9896858d654759f7ae94b16c0cfdbbad9e1c98e175d05b0a91ae414336 schemaVersion:1

## Plan Scope
- Work item 2754-roadmap-health-measures is planned from the current specification, clarification, and checklist facts.
- Requirement count: 1.
- Clarification decision count: 0.
- Checklist result count: 1.

## Plan Decisions
- PD-001 [AC-001] [FR-001] complete: Implement a repository-local Python reporter that reads the roadmap's stated baseline and current repository evidence, emits each of the seven measures with a verdict, and emits `unverified` rather than inventing a value for measure 2.

## Contract Impact
- PC-001 [PD-001] command report: `scripts/report-roadmap-health.py --format json` is a deterministic, machine-readable report containing seven named measures, their evidence window, and a legal explicit `unverified` verdict.

## Verification Obligations
- VO-001 [PD-001] [PC-001] semanticTest: Add a focused fixture-driven test that proves all seven measures are emitted and that removing a required source fixture fails the reporter rather than producing a confident result.

## Performance Intent
No performance intent is declared for this work item.

## Migration Posture
- PM-001 [PC-001] additive: The reporter is additive; the roadmap is rescored from its reported facts and preserves historical baseline prose as the comparison authority.

## Generated View Impact
- GV-001 [PD-001] workModel: Refresh the SDD readiness views after the authored plan and task evidence are current; no other generated repository projection is changed.

## Accepted Deferrals
No accepted plan deferrals recorded.

## Planning Findings
No blocking planning findings recorded.

## Advisory Notes
- Optional Governance pointers remain compatibility facts only.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd tasks --work 2754-roadmap-health-measures`.
