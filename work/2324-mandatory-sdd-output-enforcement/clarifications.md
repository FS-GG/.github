---
schemaVersion: 1
workId: 2324-mandatory-sdd-output-enforcement
title: Mandatory Sdd Output Enforcement
stage: clarify
changeTier: tier1
status: clarified
sourceSpec: work/2324-mandatory-sdd-output-enforcement/spec.md
publicOrToolFacingImpact: true
---

# Mandatory Sdd Output Enforcement Clarifications

## Source Specification
- work/2324-mandatory-sdd-output-enforcement/spec.md

## Clarification Questions
- Q-001: The filed body offers two remedies and explicitly leaves the choice open ("Left to whoever scopes this"), and the delivery-route receipt records that the touch-set deliberately spans both arms so this phase decides. Which arm does this item take: (a) auto-declare `work/<workId>/` + `readiness/<workId>/` into the item's `Paths:` at record/claim time, or (b) treat them as expected route output that `verify-paths` does not report as drift?
- Q-002: If arm (b) is taken, does declaring those directories become ILLEGAL — the way ADR-0044 made a generated artifact illegal to declare — or merely unnecessary?
- Q-003: What does `verify-paths` do when it cannot read the implemented item's delivery-route receipt at all?

## Answers
- A-001: Arm (b). The measured cost this item exists to remove is not the declaration itself but what obtaining it requires: `widen`/`set-paths` are the only writers of a `Paths:` line, both are gated on holding the item's claim, and both are gated on a live board-wide `activeCollisions` scan before the PATCH. Arm (a) therefore buys a reservation over two directories derived from the item's own id — which only that item's claim holder can ever author — at the price of one board scan and one issue-body PATCH per `sdd-required` item, in the protocol's most contended write path.
- A-002: Merely unnecessary. ADR-0044's refusal rests on "nobody authors them"; an SDD package is authored, by the claim holder. Four live items already declare theirs and must keep working byte-unchanged.
- A-003: It subtracts nothing and says so on stderr. "I could not ask what the route obliges" and "the route obliges nothing" are opposite facts, and only one of them is safe to act on.

## Decisions
- DEC-001 [AMB:AMB-001]: Take remedy arm (b) — a read-side exemption in `verify-paths`, bound to the implemented item's own current `sdd-required` receipt — and record arm (a)'s refusal, with its evidence, as `Rejected Alternative` RA-001..RA-004 in `spec.md` rather than leaving it as an unexamined option.
- DEC-002 [AMB:AMB-002]: Declaring the package directories remains legal (spec SB-004); the exemption removes the OBLIGATION to declare them, never the ability.
- DEC-003 [AMB:AMB-003]: The receipt read fails closed (spec FR-004) and is issued only when the drift set is non-empty (spec FR-007), mirroring the existing ADR-0044 `generatedPaths` subtraction's own two rules rather than inventing a second policy beside it.

## Accepted Deferrals
No accepted deferrals recorded.

## Remaining Ambiguity
No blocking ambiguity remains.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd checklist --work 2324-mandatory-sdd-output-enforcement`.
