---
schemaVersion: 1
workId: 2730-doc-comments-to-signatures
title: Doc Comments Sited Where The Compiler Keeps Them
stage: specify
changeTier: tier1
status: specified
publicOrToolFacingImpact: true
---

# Doc Comments Sited Where The Compiler Keeps Them Specification

Prose status: specified

## User Value

A caller of `FS.GG.Coord.Core` or `FS.GG.Coord.GitHub` — reading the generated XML documentation, an
IDE tooltip, or the signature file itself — sees the contract prose that today exists only in the
implementation file and reaches no consumer at all. A future contributor who writes such a comment
learns so from a red check on their own pull request rather than from nobody, ever.

**The defect, measured at `0ddd4b88` rather than quoted.** When an F# module has a signature file, the
`///` documentation comments in the *implementation* are discarded: the signature's documentation is
what reaches the generated XML file, the IDE tooltip, and every downstream reader. Across
`src/FS.GG.Coord.Core` and `src/FS.GG.Coord.GitHub` there are **2,970** `///` lines in 37
implementation files that have a sibling `.fsi`; **2,511** of those lines are substantive and written
*only* there, and **zero** of them reach the generated XML.

**The measurement is a controlled experiment, because an empty result set is not a negative finding.**
The whole-population figure above is a grep that found nothing, and on its own that is worth nothing —
it is indistinguishable from a grep that could not have found anything. So one declaration was used as
a two-legged control: `FS.GG.Coord.Core/IntakeReceipt.validate`, one `dotnet build … -c Release`, one
`grep -c` over `bin/Release/net10.0/FS.GG.Coord.Core.xml`.

| leg | sentinel written in | hits in the generated XML |
|---|---|---|
| negative | `IntakeReceipt.fs` | **0** |
| positive | `IntakeReceipt.fsi` | **1** |

The positive leg is what makes the negative leg mean anything: the same file, the same build, the same
grep, differing only in which of the two files the sentence was typed into.

**The drift is not hypothetical, and the row's worked example still holds** — re-derived at `0ddd4b88`,
where the line numbers have moved because `.github#2712` edited the file since. In
`src/FS.GG.Coord.Cli/Client.fs` (a file this work does **not** touch), lines 718–752 are a 35-line
`///` block explaining an implementation of `boardBlockingCounts` that no longer exists; the
declaration it now attaches to, at line **753**, is the one-line forward
`let boardBlockingCounts = BoardFactsApplication.blockingCounts`. Immediately below it, lines 755–770
are a second `///` block describing `enrichBoardFacts` — defined 120 lines *earlier*, at line 635 —
which now binds to `let mutable private generatedPathCollector` at line 778. The compiler cannot warn:
`///` binds silently to the next declaration, and the Release build of all three projects emits
**0 warnings** over 3,905 such lines.

## Scope

- SB-001: Every `.fs` file under `src/FS.GG.Coord.Core` (27 files, 1,847 `///` lines) and
  `src/FS.GG.Coord.GitHub` (10 files, 1,117 lines) that has a sibling `.fsi`.
- SB-002: The sibling `.fsi` files under those two projects, insofar as they receive moved contract
  prose. Existing signature prose is not re-authored.
- SB-003: A new gate — `scripts/check-signature-doc-siting.py` — with its fixture and baseline under
  `tests/signature-doc-siting/` and its workflow `.github/workflows/signature-doc-siting.yml`.
- SB-004: This SDD package under `work/2730-doc-comments-to-signatures/` and
  `readiness/2730-doc-comments-to-signatures/`.

## Non-Goals

- SB-005: **`src/FS.GG.Coord.Cli` is not swept.** Its 941 `///` lines across 12 files are outside this
  item's declared `Paths:`; `.github#2724` holds `Client.fs`/`Client.fsi` right now, and the
  extraction programme (`.github#2724`, `.github#2731` onward) gives each extracted module a proper
  `.fsi` with its prose moved as part of that work. Sweeping here would collide with every extraction
  lane for no benefit. The residue is recorded in the gate's baseline as exact per-file counts rather
  than dropped, so it is visible and shrinking rather than unstated.
- SB-006: **No `.fsi` prose is rewritten where it already carries the contract.** Where a signature
  already says what an implementation block says, the duplicate is dropped and enumerated in the pull
  request; the signature text itself is left alone. `TouchSet.fsi`, `Writes.fsi` and `Landable.fsi` are
  the standard this work moves prose *toward*, and nothing here asks them to change.
- SB-007: **No behaviour changes.** Not one executable line moves. The only compiled artifact that may
  differ is the generated XML documentation, and it may only gain.
- SB-008: **This gate is not added to branch protection.** Making a check a required context is a
  separate, owner-held decision; this ships as an ordinary workflow, exactly as
  `.github/workflows/pipefail-assertions.yml` (`.github#2689`) does.
- SB-009: **No blocker edge is added onto `.github#2724` or `.github#2731`.** They move this work's
  denominator, which is a sequencing preference with a reason and not a dependency. This session
  measured `.github#2653` held blocked by an undocumented edge onto `.github#2106`, and the cost of
  that was real.

## User Stories

- US-001 (P1): As a caller of `FS.GG.Coord.Core` or `FS.GG.Coord.GitHub`, I read the contract from the
  generated XML or an IDE tooltip and it is complete, so that I do not have to open an implementation
  file to learn what a function refuses.
- US-002 (P1): As a contributor writing a doc comment in an implementation file that has a signature
  file, I am told so by a red check on my own pull request, so that the comment is not silently
  discarded by the compiler and left to drift.
- US-003 (P1): As a maintainer of an implementation, I keep writing prose about *why this
  implementation* in the file that implements it, and this policy does not push it into the signature
  file, so that the signature stays a statement of contract and does not become worse.
- US-004 (P1): As a reviewer of this change, I can confirm that no contract prose was lost, so that
  "moved" is a verifiable claim about ~3,000 lines rather than an assurance.
- US-005 (P1): As a reader of the gate, I can see it fail on demand and see that it refuses to report a
  pass when its subject is missing, so that it is not the `.github#266` class it exists to close.
- US-006 (P2): As a maintainer of the `Cli` extraction programme, I can see exactly how much residue is
  left and in which files, so that it shrinks under measurement rather than being forgotten.

## Acceptance Scenarios

- AC-001 [US-001] [FR-001]: Given every `.fs` file under `src/FS.GG.Coord.Core` or
  `src/FS.GG.Coord.GitHub` that has a sibling `.fsi`, when the gate is run over the working tree, then
  it finds zero `///` comments in them.
- AC-002 [US-003] [FR-002]: Given a `///` block in such a file, when it is classified, then it is moved
  into the sibling `.fsi` if and only if a caller who never opens the `.fs` could act on it; otherwise
  it stays exactly where it is, with its wording unchanged, as a `//` comment.
- AC-003 [US-004] [FR-002]: Given the pull request, when a reviewer reads it, then every `///` block is
  accounted for in one of exactly three dispositions — moved to the `.fsi`, demoted in place to `//`
  with wording unchanged, or dropped as a duplicate of prose the `.fsi` already carries — and the
  dropped ones are enumerated.
- AC-004 [US-001] [FR-006]: Given the generated XML documentation of both projects built at
  `-c Release`, when the before and after files are compared, then every `<member>` present before is
  present after and no documentation text present before is absent after.
- AC-005 [US-002] [FR-003]: Given a swept file, when a single `///` comment is reintroduced into it,
  then the gate exits non-zero and its message names that file, that line, and the reason — and the
  test asserting this asserts the reason, not merely a non-zero exit.
- AC-006 [US-005] [FR-004]: Given a tree in which the gate discovers zero `.fs` files with a sibling
  `.fsi`, or in which its baseline is unreadable, then the gate reports **no verdict** on a distinct
  exit code and the workflow fails — never a pass.
- AC-007 [US-005] [FR-004]: Given the gate run against the real repository tree, when it reports, then
  it states how many files it discovered and how many carry a sibling signature file, so that a
  silently-empty subject is visible in the log rather than inferred.
- AC-008 [US-006] [FR-005]: Given `src/FS.GG.Coord.Cli`'s residue, when the baseline is read, then it
  carries one `<count> <path>` line per offending file whose counts must match the tree **exactly**:
  more is a new offender, fewer is a stale baseline, and both are red.
- AC-009 [US-003] [FR-007]: Given an implementation file that has **no** sibling `.fsi`, when the gate
  runs, then its `///` comments are not reported — the compiler keeps them there, so they are correct.
- AC-010 [US-003] [FR-007]: Given a `///` sequence that is not a doc comment — inside a `(* … *)` block
  comment, inside a string or triple-quoted literal, or spelled with four or more slashes, which F#
  does not treat as XML documentation — when the gate runs, then it is not reported.
- AC-011 [US-005] [FR-008]: Given each assertion this work adds, when the behaviour it asserts is
  inverted, then the suite reds, and the exact mutation and observed red are recorded on the pull
  request.
- AC-012 [US-002] [FR-009]: Given the gate's own fixture, when it runs, then it exercises the gate
  against a synthetic tree **and** asserts that the shipped baseline still describes the real tree, so
  a fixture passing on synthetic strings while the baseline rots is not a reachable state.

## Functional Requirements

Each requirement is one physical line, because the checklist coverage scan reads one physical line and does not join continuations.

- FR-001: No `.fs` file under `src/FS.GG.Coord.Core` or `src/FS.GG.Coord.GitHub` that has a sibling `.fsi` MUST contain an F# XML documentation comment. (Stories: US-001; Acceptance: AC-001)
- FR-002: Every existing such comment MUST receive exactly one of three dispositions — moved to the sibling `.fsi`, demoted in place to `//` with its wording unchanged, or dropped as a duplicate of prose the `.fsi` already carries — decided by whether a caller who never opens the `.fs` could act on it, and the dropped set MUST be enumerated in the pull request. (Stories: US-003, US-004; Acceptance: AC-002, AC-003)
- FR-003: A gate under `tests/` MUST fail when an F# XML documentation comment appears in a `.fs` file that has a sibling `.fsi`, naming the file, the line and the reason. (Stories: US-002; Acceptance: AC-005)
- FR-004: The gate MUST report a distinct no-verdict outcome, never a pass, when it discovers no subject files or cannot read its baseline, and MUST state its discovered subject counts on every run. (Stories: US-005; Acceptance: AC-006, AC-007)
- FR-005: The gate MUST carry a baseline of exact per-file counts for the accepted `src/FS.GG.Coord.Cli` residue, where a higher count is a new offender and a lower count is a stale baseline, both red. (Stories: US-006; Acceptance: AC-008)
- FR-006: The generated XML documentation of both projects MUST retain every member and every documentation text it carried before the change. (Stories: US-001; Acceptance: AC-004)
- FR-007: The gate MUST NOT report a `///` comment in a `.fs` file with no sibling `.fsi`, nor a `///` sequence that F# does not lex as an XML documentation comment. (Stories: US-003; Acceptance: AC-009, AC-010)
- FR-008: Every assertion this work adds MUST be inverted at authoring time and its observed red recorded. (Stories: US-005; Acceptance: AC-011)
- FR-009: The gate's fixture MUST assert both synthetic behaviour and the shipped baseline's agreement with the real tree. (Stories: US-002; Acceptance: AC-012)

## Ambiguities

- AMB-001: The row asks that contract prose be moved to the `.fsi`. It does not say what happens to
  prose that is genuinely *about the implementation* — why this loop, which incident produced this
  branch. Where is the line, and what test decides which side a block falls on?
- AMB-002: A gate that enforces a placement policy is exactly the thing people learn to suppress if it
  ever fires on correct code. What guarantees this one cannot fire on the correct side of AMB-001's
  line?
- AMB-003: The row names `tests/source-coherence` as the gate's home, but that directory is already the
  fixture for `scripts/check-source-coherence.py`, an unrelated registry-versus-source gate. Where does
  this gate live?
- AMB-004: `src/FS.GG.Coord.Cli` is out of lane but inside any honest whole-repository subject. Does
  the gate's subject shrink to the two swept projects, or does the residue enter a baseline?
- AMB-005: A per-file baseline means `.github#2724` and `.github#2731` will stale it when they land.
  Is that a conflict to avoid, or the mechanism working?
- AMB-006: What exactly is an "F# XML documentation comment" for the gate's purposes — is a line-based
  scan for `///` sufficient, and what does it get wrong?
- AMB-007: AC-004 asks that no member lose its documentation. What is the comparison, given that the
  F# compiler emits a `<member>` element only for members that carry documentation at all?

## Public Or Tool-Facing Impact

- The generated XML documentation of `FS.GG.Coord.Core` and `FS.GG.Coord.GitHub` gains entries. Both
  assemblies are consumed only by `FS.GG.Coord.Cli` in-repo; neither `src/FS.GG.Coord.Core` nor
  `src/FS.GG.Coord.GitHub` is a `kit:` source in `registry/repos.yml`, so no published kit payload
  changes and no coherent-set version bump is implied.
- A new workflow, `signature-doc-siting`, reports on every pull request. It is not a required context.
- This specification is an SDD lifecycle artifact and command-report contract input.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd clarify --work 2730-doc-comments-to-signatures`.
