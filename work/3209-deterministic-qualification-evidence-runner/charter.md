---
schemaVersion: 1
workId: 3209-deterministic-qualification-evidence-runner
title: Deterministic qualification and evidence runner
stage: charter
changeTier: tier1
status: chartered
policyPointers:
  - .fsgg/sdd.yml
  - .fsgg/agents.yml
  - .fsgg/policy.yml
  - .fsgg/capabilities.yml
  - .fsgg/tooling.yml
---

# Deterministic qualification and evidence runner Charter

## Identity
- Work id: `3209-deterministic-qualification-evidence-runner`
- Lifecycle stage: charter
- Status: chartered

## Principles
- Qualification is a deterministic computation over an exact subject, immutable tool manifest, isolated executor, executed controls, and converged hosted facts.
- Evidence must identify its producer and subject and must be adequate for the exact claim it discharges; assertions and unrelated artifacts are not evidence.
- Every refusal-producing control is tested by an independently implemented inversion that observes the exact typed refusal.
- Semantic adequacy remains an independently reviewed judgement and is never inferred from mechanical gate success.
- All serialized receipts are canonical, content-addressed, replayable, and free of private runtime paths or raw transcripts.

## Scope Boundaries
- Add typed qualification/evidence models and deterministic reducers in `FS.GG.Coord.Core`.
- Add a coherent CLI runner and command contract for isolated local execution and hosted convergence.
- Add only the GitHub reads needed to converge exact run, job, check, and obligation-comment evidence.
- Consume the canonical telemetry, identity, lifecycle, and evidence primitives published by issue #3208; do not restore Python authority or duplicate those contracts.
- Publish a coherent coordination package before any skill adopts the new runner.
- Do not compile roadmap registration or acceptance in this item; that is child #3210.

## Policy Pointers
- SDD policy comes from `.fsgg/sdd.yml` and `.fsgg/agents.yml`.
- Governance files are optional compatibility pointers and are not evaluated by this command.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd specify --work 3209-deterministic-qualification-evidence-runner`.
