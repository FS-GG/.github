---
schemaVersion: 1
workId: 2725-cli-kernel-extraction
title: Cli Kernel Extraction
stage: clarify
changeTier: tier1
status: clarified
sourceSpec: work/2725-cli-kernel-extraction/spec.md
publicOrToolFacingImpact: true
---

# Cli Kernel Extraction Clarifications

## Source Specification
- work/2725-cli-kernel-extraction/spec.md

## Clarification Questions
- CQ-001 [AMB:AMB-001] blocking answered: Does `Snapshot` belong in the Kernel?
- CQ-002 [AMB:AMB-002] blocking answered: Do the bindings that were `private` in `Client` become `public` on the Kernel, or `internal` behind `InternalsVisibleTo`?
- CQ-003 [AMB:AMB-003] blocking answered: Does `Client` retain re-export aliases for the declarations that move?

## Answers
- A-001 [CQ-001]: No, not in this work. `Snapshot` (1,169 lines) is a pure model depending only on
  `Json`, and moving it would let `SnapshotTests.fs`, `ScanRoundTripTests.fs` and `RuleSubsetTests.fs`
  (1,804 lines) move too, which is a real attraction. Three facts argue against it. The row's scope
  names five modules and does not name it, and its own line-count estimate (~3,000 lines) matches the
  five without it and not with it. It is consumed by the *scheduling* verbs — `scan`, `next`, `ready`,
  `batch`, `driver` — rather than by every command family, so it is a candidate for the family project
  those verbs land in rather than for the shared base. And enlarging the first cut on the strength of
  a test-movement win is precisely the failure mode this row was filed to avoid: choosing the boundary
  by what is convenient for the test project. Recorded as an identified next seam rather than acted
  on.
- A-002 [CQ-002]: `public`. An assembly boundary has no `private`-to-a-friend, so the choice is
  `public` or `internal` + `InternalsVisibleTo`. `InternalsVisibleTo` would keep the count down while
  making the Kernel's real contract invisible to the compiler and to the four rows that must consume
  it — a surface that is not held by a signature file is exactly what `.github#2724` measured the cost
  of. `public` behind a `.fsi` states the shared base honestly and is the form `.github#2726`–`#2729`
  will depend on.
- A-003 [CQ-003]: No aliases. An alias layer would keep every call site spelled as it is today,
  including every test's, which sounds like less risk and is in fact the failure this row exists to
  prevent: `Client` would keep the exports, the tests would keep binding to `Client`, no test could
  move, and SB-004 and FR-003 would be unsatisfiable. The re-spelling is mechanical, the compiler
  finds every site, and the alternative is a Kernel with no test client at all.

## Decisions
- DEC-001 [AMB:AMB-001]: `Snapshot` stays in `FS.GG.Coord.Cli` for this work. It is named in the plan
  as the identified next seam so the information is not lost.
- DEC-002 [AMB:AMB-002]: Bindings that were `private` in `Client` and are needed by `Client` become
  `public` on the Kernel, each with a signature entry authored from its own prose under rule 4 of the
  Documentation Siting Rule.
- DEC-003 [AMB:AMB-003]: No re-export aliases. Call sites are re-spelled to the Kernel qualifier, and
  `Client` reaches the unqualified names by `open FS.GG.Coord.Cli.Kernel` so that the ~350 `eprint`,
  ~143 `fail` and ~100 exit-literal sites inside `Client.fs` itself keep the spelling they have.

## Accepted Deferrals
No accepted deferrals recorded.

## Remaining Ambiguity
None. All three carried ambiguities are resolved by decision above.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd checklist --work 2725-cli-kernel-extraction`.
