---
schemaVersion: 1
workId: 3209-deterministic-qualification-evidence-runner
title: Deterministic qualification and evidence runner
stage: clarify
changeTier: tier1
status: clarified
sourceSpec: work/3209-deterministic-qualification-evidence-runner/spec.md
publicOrToolFacingImpact: true
---

# Deterministic qualification and evidence runner Clarifications

## Source Specification
- work/3209-deterministic-qualification-evidence-runner/spec.md

## Clarification Questions
- Q-001: What is the qualification unit and canonical serialization boundary?
- Q-002: How is evidence adequacy represented without automating semantic judgement?
- Q-003: When is a hosted GitHub observation converged?
- Q-004: What proves executor and mutation-fixture independence?
- Q-005: How are PR obligations handled without creating a second write boundary?
- Q-006: Which publication and adoption work belongs to this child?

## Answers
- A-001 [Q-001]: One run binds one repository, exact 40-hex subject revision, declared operation manifest, executor identity, and immutable tool manifest. The typed core owns canonical compact JSON and its content digest.
- A-002 [Q-002]: Claims declare required evidence kinds and subjects. Structural matching proves execution and provenance, while semantic adequacy requires a separately accepted independent-review receipt.
- A-003 [Q-003]: Runs, jobs, and checks are keyed to the exact subject SHA and settle only after two consecutive complete observations have identical identities and conclusions with no pending state.
- A-004 [Q-004]: Executor and fixture identities carry distinct role, content, and implementation-provenance digests. Equality, missing provenance, or reuse of production implementation is a refusal.
- A-005 [Q-005]: The pure reducer validates exactly one current-head declaration and returns either verified readback or a guarded create intent. The existing GitHub IO boundary performs and verifies that intent.
- A-006 [Q-006]: This child publishes the typed core, CLI, GitHub convergence reads, tests, and coherent packages. Skill adoption and compiled roadmap registration/acceptance remain #3210 work.

## Decisions
- D-001 [Q-001]: Use a closed typed model and canonical serialization; reject unknown fields, duplicate identities, path escapes, stale subjects, arithmetic errors, and digest mismatch.
- D-002 [Q-002]: Mechanical qualification may never synthesize or upgrade semantic adequacy.
- D-003 [Q-003]: Hosted convergence is a typed fixed point, never a sleep duration or a single aggregate status.
- D-004 [Q-004]: Every mutation declares its expected typed refusal and passes only when an independently implemented fixture observes that exact refusal.
- D-005 [Q-005]: Explicit `none` is a first-class obligation declaration; missing, malformed, duplicated, or stale-head declarations refuse.
- D-006 [Q-006]: Publication and receiver verification precede every consumer migration.

## Accepted Deferrals
No accepted deferrals recorded.

## Remaining Ambiguity
No blocking ambiguity remains.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd checklist --work 3209-deterministic-qualification-evidence-runner`.
