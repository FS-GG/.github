---
schemaVersion: 1
workId: 2380-feedback-report-materialization
title: Feedback Report Materialization
stage: clarify
changeTier: tier1
status: clarified
sourceSpec: work/2380-feedback-report-materialization/spec.md
publicOrToolFacingImpact: true
---

# Feedback Report Materialization Clarifications

## Source Specification
- work/2380-feedback-report-materialization/spec.md

## Clarification Questions

- AMB-001: `.github#2380` states that only one product tree has been measured, so it is unknown whether
  the defect is profile-specific, skill-specific, or a broader materializer gap — and implies a second
  tree must be measured to tell. Must a second product tree be measured before a cause can be
  established?
- AMB-002: The established cause admits more than one fix route (extend `.github`'s existing
  byte-transport, or stand up a Rendering-owned product channel mirroring the game one). Does this
  package choose the route?
- AMB-003: `.github#2380` acceptance criterion 4 asks that `registry/repos.yml`'s `sir` row link this
  issue, but `registry/repos.yml` is not in the item's declared `Paths:`. Does this package widen to
  reach it?

## Answers

- AMB-001: No. The question `#2380` posed — profile-specific, skill-specific, or broader — is a
  question about the *distribution* of a symptom across trees. Measuring the producers answers the
  *mechanism* instead, and the mechanism subsumes the distribution: a delivery channel that does not
  exist for a provider family cannot deliver to any tree in that family, for any profile or any skill.
  The one claim a second tree could contest — that `fs-gg-fable-game` emits no skills — is settled
  from the template definition itself (`sources` is a single `./`→`./` entry, `postActions: null`,
  naming-only `symbols`), not from any tree.
- AMB-002: No. ADR-0063 already decided the governing principle (owner-sourced, delivered, pinned,
  content-addressed) and deliberately left transport to be decided per class — `.github#1300` for
  drivers, `.github#1299`/`#1308` for the game class. Choosing this class's transport is a design
  decision with a real ADR-0058 consistency argument on both sides, and it belongs to the row that
  will implement it.
- AMB-003: No. The `sir` row's correct content depends on the outcome of the remediation decision,
  which is a human's to make and which this package routes rather than settles. Widening to edit a
  shared registry file in order to write a line whose content is not yet determined would also put
  this lane on a file other lanes may hold, for no gain.

## Decisions

- DEC-001 [AMB:AMB-001]: Establish the cause from producer-side artifacts and do **not** measure a
  second product tree. The record states this explicitly and justifies it (spec F8), and it also
  states what a second tree *would* establish — blast radius — which this record deliberately does not
  claim.
- DEC-002 [AMB:AMB-003]: Do not widen. `.github#2380` acceptance criterion 4 is discharged by carrying
  it on the decision row `.github#2548` (its acceptance criteria 3 and 4) rather than by leaving it
  silent, which is the failure `#2380` itself was filed about.

## Accepted Deferrals

- DEC-003 [AMB:AMB-002]: The fix-route choice is **deferred to `.github#2545`**, where it is that
  row's first acceptance criterion. Both candidate routes are named there with the ADR-0058/ADR-0063
  consistency argument that distinguishes them, so the deferral hands over the analysis rather than
  the bare question.

## Remaining Ambiguity

None. AMB-001 and AMB-003 are decided; AMB-002 is an accepted deferral with a named owning row.

One thing is **unknown and deliberately not claimed**, which is not an ambiguity in this work's scope:
the number of product trees affected. Only `EHotwagner/S.I.R.` has been measured, and this record
claims a mechanism, not a population.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd checklist --work 2380-feedback-report-materialization`.
