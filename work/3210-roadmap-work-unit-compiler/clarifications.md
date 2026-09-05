---
schemaVersion: 1
workId: 3210-roadmap-work-unit-compiler
title: Roadmap work-unit registration and acceptance compiler
stage: clarify
changeTier: tier1
status: clarified
sourceSpec: work/3210-roadmap-work-unit-compiler/spec.md
publicOrToolFacingImpact: true
---

# Roadmap work-unit registration and acceptance compiler Clarifications

## Source Specification
- work/3210-roadmap-work-unit-compiler/spec.md

## Clarification Questions
- Q-001: What is the authoritative catalog and selection rule?
- Q-002: How do registration mutations remain idempotent?
- Q-003: What evidence confers acceptance?
- Q-004: Which revision identities are distinct?
- Q-005: What makes the receipt and index an atomic acceptance?
- Q-006: What completes publication and adoption?

## Answers
- A-001 [Q-001]: One input binds one Roadmap v2 source digest and ordered catalog. Select the first unchecked row only when its immediate predecessor is accepted, and require exact unit-id, title, and gate identity equality.
- A-002 [Q-002]: The compiler emits deterministic staged-intake drafts with content identities and delegates apply/reuse to #3105; it performs no GitHub IO itself.
- A-003 [Q-003]: Digest-valid #3208 lifecycle/review receipts and a #3209 qualification result must bind the selected unit and exact subject; observed SDD analyze, verify, and ship executions must satisfy each declared obligation.
- A-004 [Q-004]: Implementation candidate, implementation merge, acceptance candidate, acceptance merge, and protected-main are separate named identities. Each merge binds its candidate ancestry; protected-main equals the observed accepted merge only at final handoff.
- A-005 [Q-005]: The canonical receipt and evidence index bind the same source, unit, identities, obligations, and evidence digests, then share one transaction digest over both unsigned payloads.
- A-006 [Q-006]: Publish and receiver-verify the coherent package, migrate `work-roadmap` to the compiled command, and run one later GS2 unit from preparation through roadmap-close on a clean exact checkout.

## Decisions
- D-001 [Q-001]: Zero, multiple, unknown, duplicate, skipped, or already accepted candidates refuse before rendering.
- D-002 [Q-002]: Existing matching receipts are reused, interrupted transactions resume, conflicts refuse, and no alternate write boundary is permitted.
- D-003 [Q-003]: Prose, authored outcome fields, wrong-role actors, stale subjects, and structurally valid but unrelated artifacts cannot satisfy an obligation.
- D-004 [Q-004]: Identity collapse or phase substitution is a typed refusal.
- D-005 [Q-005]: Missing, extra, substituted, differently ordered, or partially written acceptance members refuse verification.
- D-006 [Q-006]: Record positive and inverted controls plus phase minutes and measured token usage compared with GS2-07.2.

## Accepted Deferrals
No accepted deferrals recorded.

## Remaining Ambiguity
No blocking ambiguity remains.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd checklist --work 3210-roadmap-work-unit-compiler`.
