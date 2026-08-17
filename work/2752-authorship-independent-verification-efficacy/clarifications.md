---
schemaVersion: 1
workId: 2752-authorship-independent-verification-efficacy
title: Authorship Independent Verification Efficacy
stage: clarify
changeTier: tier1
status: clarified
sourceSpec: work/2752-authorship-independent-verification-efficacy/spec.md
publicOrToolFacingImpact: true
---

# Authorship Independent Verification Efficacy Clarifications

## Source Specification
- work/2752-authorship-independent-verification-efficacy/spec.md

## Clarification Questions
- CQ-001 [AMB:AMB-001] blocking open: Resolve source ambiguity AMB-001 before checklist.
- CQ-002 [AMB:AMB-002] blocking open: Resolve source ambiguity AMB-002 before checklist.
- CQ-003 [AMB:AMB-003] blocking open: Resolve source ambiguity AMB-003 before checklist.

## Answers
- CQ-001 [AMB:AMB-001] decision: answered — it is a clause of FR-002.
- CQ-002 [AMB:AMB-002] decision: answered — sited inside Gate-inversion evidence.
- CQ-003 [AMB:AMB-003] decision: answered — a discrimination control set.

**CQ-001 — `coverage of a branch is not coverage of its predicate` is a clause of FR-002, not a
fourth requirement.** FR-002 is about the *witness*: which named test went red for this mutation. The
branch/predicate distinction is about *which case* that witness uses. A witness that enters the branch
without reaching its boundary names a test that reds for a reason the mutation did not cause, which is
FR-002's own failure — the witness is not a witness. Making it a separate numbered requirement would
let a sweep satisfy FR-002 with an interior case and then argue the boundary clause separately; folding
it in means the boundary case is what FR-002 asks for in the first place.

The affirmative form is measured: `.github#2395`'s critic reproduced six check-4 legs including a
**boundary** leg — a competing election exactly one id lower — rather than a competing election at an
arbitrary distance. The negative form was measured three times on one row, where a fixture reached a
branch but never its boundary and passed review each time.

**CQ-002 — the new material is sited inside `## Gate-inversion evidence`, as a `###` subsection.**
Three reasons, in order of weight:

1. **It needs no second file.** `pnext-item` `SKILL.md` §3 links `#gate-inversion-evidence` by anchor
   and `references/independent-review.md#gate-inversion-evidence` is also cited from §5's pointer list.
   A subsection inside that section inherits both links. A new top-level section would be reachable
   only by editing `SKILL.md` — a second kit source, outside this row's declared `Paths:`, and the
   host's lane for this item is one file.
2. **It is an extension, not a parallel procedure.** Steps 1–9 measure whether a gate *can* fire. The
   new material measures whether the *thing that graded that measurement* was independent of its
   author. Two sibling top-level sections would invite a critic to run one and not the other.
3. It still gets its own anchor, so it remains citable from a review record.

The cost is accepted and named: the section grows, and its existing sentence bounding the sweep ("one
mutation per touched gate, plus the single non-vacuity leg step 2 names") must be restated to cover the
new legs, or it would be false the moment they land.

**CQ-003 — a prose contract's efficacy is demonstrated by a discrimination control set drawn from
other authors' measurements.** This change adds no executable gate to the repository, so there is no
predicate to invert. Its efficacy claim is therefore: *applied as written, these rules refuse artifacts
independently known to be defective and admit an artifact independently known to be correct.*

Both halves are required. A rule that refuses everything carries as little information as one that
refuses nothing — `.github#266`'s test does not distinguish an always-red gate from an always-green one
in that respect, because neither verdict is a function of its subject.

The control set may not be of this work's own invention, or the demonstration reproduces the mechanism
one level up. So every entry is an artifact **another agent measured and recorded before this work
existed**:

- four refusals from the `§11.2` audit at `.github#1858` comment `5316937299`;
- one refusal from `.github#2719` comment `5319094213`, at the guidance layer;
- one admission: `tests/receiver-validate/run.sh` section F, whose own header states in terms that it
  is *"the check neither slice 2 nor slice 3 had."*

The contract text says this about itself, because a control set whose provenance is invisible is
indistinguishable from one the author chose to fit.

## Decisions
- DEC-001 [CQ-001] [AMB:AMB-001]: answered — it is a clause of FR-002.
- DEC-002 [CQ-002] [AMB:AMB-002]: answered — sited inside Gate-inversion evidence.
- DEC-003 [CQ-003] [AMB:AMB-003]: answered — a discrimination control set.

## Accepted Deferrals
No accepted deferrals recorded.

## Remaining Ambiguity
No blocking ambiguity remains.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd checklist --work 2752-authorship-independent-verification-efficacy`.
