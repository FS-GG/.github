---
schemaVersion: 1
workId: 2730-doc-comments-to-signatures
title: Doc Comments Sited Where The Compiler Keeps Them
stage: clarify
changeTier: tier1
status: clarified
sourceSpec: work/2730-doc-comments-to-signatures/spec.md
---

# Clarifications

## Source Specification
- work/2730-doc-comments-to-signatures/spec.md

## Clarification Questions

- **CQ-001** (AMB-001): The row says "move contract prose to the `.fsi`". Prose that is genuinely about
  the implementation is correctly placed where it is. Where is the line, and what test decides which
  side a `///` block falls on?
- **CQ-002** (AMB-002): What structurally prevents this gate from firing on correct code — the failure
  mode that turns a policy gate into something contributors learn to suppress?
- **CQ-003** (AMB-003): The row names `tests/source-coherence` as the gate's home. That directory is
  already the fixture for `scripts/check-source-coherence.py`. Where does this gate actually live?
- **CQ-004** (AMB-004): `src/FS.GG.Coord.Cli` is out of lane but inside any honest whole-repository
  subject. Does the gate's subject shrink, or does the residue enter a baseline?
- **CQ-005** (AMB-005): A per-file baseline will be staled by `.github#2724` and `.github#2731`. Is
  that a conflict to avoid or the mechanism working?
- **CQ-006** (AMB-006): What exactly counts as an F# XML documentation comment, and what does a
  line-leading `grep '^\s*///'` get wrong?
- **CQ-007** (AMB-007): AC-004 asks that no member lose its documentation. What is the comparison,
  given that the F# compiler emits a `<member>` element only for members that carry documentation?

## Answers

- CQ-001 → The line is drawn by **audience**, not by subject matter, and the deciding test is one
  question asked of each block: *can a caller who never opens the `.fs` act on this sentence?* A
  promise, a refusal, an assumption the caller must not make, the incident that made it a rule — yes,
  and it belongs in the `.fsi`. An ordering constraint inside this function, why this fold and not the
  obvious one, which API quirk this branch works around, why a local is `mutable` — no, and it is
  correctly placed exactly where it is (resolves AMB-001).
- CQ-002 → Because the gate's subject is the **comment marker**, never the content. It never asks what
  a comment says, so it cannot form an opinion about which side of CQ-001's line a sentence falls on.
  A `//` comment is invisible to it. The only thing it refuses is a `///` in a file whose `///` the
  compiler provably discards — which is wrong independently of what the comment says (resolves
  AMB-002).
- CQ-003 → Not `tests/source-coherence`: that directory belongs to an unrelated
  registry-versus-`FS.GG.SDD`-source gate (`.github#741`) and its name collides only by accident. The
  gate is `scripts/check-signature-doc-siting.py` with `tests/signature-doc-siting/` and
  `.github/workflows/signature-doc-siting.yml`, following `.github#2689`'s
  `pipefail-assertions` shape exactly (resolves AMB-003).
- CQ-004 → The subject stays whole-repository — every `.fs` under `src/` with a sibling `.fsi` — and
  the `Cli` residue enters a baseline of exact per-file counts. A subject that shrinks to what has
  already been fixed is the `.github#266` failure mode in its purest form: a gate over a population
  chosen so it cannot fire (resolves AMB-004).
- CQ-005 → It is the mechanism working. The baseline must match the tree **exactly**, not `<=`, so a
  count that is too high is a new offender and one that is too low is a stale baseline; both are red.
  That is what makes it shrink rather than merely exist, and it is why an extraction lane that moves
  prose into a new `.fsi` decrements it in the same commit. Whichever of the three rows lands second
  recomputes the file. No blocker edge follows from this (resolves AMB-005).
- CQ-006 → Measured, not assumed. Three slashes are an XML documentation comment; **four or more are
  not** — F# lexes `////…` as an ordinary comment. Verified by building
  `FS.GG.Coord.Core` at `-c Release` with both spellings on one declaration in `IntakeReceipt.fsi`:
  the `///` sentinel appears in `FS.GG.Coord.Core.xml` under
  `M:FS.GG.Coord.IntakeReceipt.validate(…)`; the `////` sentinel does not appear at all. A doc comment
  also need not be line-leading — `src/FS.GG.Coord.Core/Protocol.fs:59` and six other sites carry one
  after a `{` on the same physical line, and a `^\s*///` grep misses all of them (8 in the two swept
  projects, 2 more in `Cli`). It must also not be inside a `(* … *)` block comment or a string
  literal. `src/` contains no such case today, which is exactly why the fixture must construct one
  (resolves AMB-006).
- CQ-007 → The comparison is over the generated XML built at `-c Release` before and after, and it is
  one-directional by design: every `<member>` name present before must be present after, and every
  documentation text present before must be present after. Additions are permitted and expected —
  moved contract prose is the point. The asymmetry is deliberate: an undocumented member has no
  `<member>` element at all (verified: `IntakeReceipt` appears nowhere in the baseline XML), so a
  two-directional equality check would forbid the improvement this work exists to make (resolves
  AMB-007).

## Decisions

- **DEC-001** [CQ-001] [AMB:AMB-001] [FR-002] [AC-002] [AC-003]: A `///` block moves to the `.fsi` if
  and only if a caller who never opens the `.fs` could act on it. Otherwise it stays in place, wording
  unchanged, as `//`. A third disposition — dropped as a duplicate of prose the `.fsi` already carries
  — is permitted and must be enumerated in the pull request, so "no contract prose was lost" is a
  claim a reviewer can check rather than accept.
- **DEC-002** [CQ-002] [AMB:AMB-002] [FR-003] [FR-007] [AC-009] [AC-010]: The gate decides on the
  marker, never the content, and its subject is restricted to `.fs` files that have a sibling `.fsi`.
  It therefore reports nothing about a file whose `///` the compiler keeps, and nothing about any `//`
  comment at all.
- **DEC-003** [CQ-003] [AMB:AMB-003]: The gate ships as `scripts/check-signature-doc-siting.py`,
  `tests/signature-doc-siting/{run.sh,baseline.txt}` and
  `.github/workflows/signature-doc-siting.yml`, modelled on `.github#2689`. The item's declared
  `tests/source-coherence` path is left untouched.
- **DEC-004** [CQ-004] [AMB:AMB-004] [FR-005] [AC-008]: The gate's subject is every `.fs` under `src/`
  with a sibling `.fsi`. The 943 `Cli` doc-comment lines across 12 files enter
  `tests/signature-doc-siting/baseline.txt` as exact per-file counts.
- **DEC-005** [CQ-005] [AMB:AMB-005]: Exact-count baseline semantics — higher is a new offender, lower
  is a stale baseline, both red. The interaction with `.github#2724`/`.github#2731` is recorded in the
  baseline header and the pull request as a sequencing fact, and **no `Blocked by` edge is added**.
- **DEC-006** [CQ-006] [AMB:AMB-006] [FR-007] [AC-010]: The gate lexes rather than greps: it tracks
  `(* … *)` nesting and `"` / `@"` / `"""` string literals, requires exactly three slashes (a fourth
  disqualifies), and does not require the comment to be line-leading.
- **DEC-007** [CQ-007] [AMB:AMB-007] [FR-006] [AC-004]: XML documentation is compared one-directionally
  — no `<member>` and no documentation text present before may be absent after; additions are expected.

## Accepted Deferrals

- **DEC-008** [FR-005]: Sweeping `src/FS.GG.Coord.Cli` is deferred to the extraction programme
  (`.github#2724`, `.github#2731` onward), which gives each extracted module a `.fsi` with its prose
  moved as part of that work. Recorded as 12 baseline lines totalling 943, not dropped.
- **DEC-009**: Making `signature-doc-siting` a required status context is deferred to the repository
  owner. A required context is a branch-protection decision no implementer holds, and
  `pipefail-assertions` (`.github#2689`) set the precedent of shipping such a gate unrequired.

## Remaining Ambiguity
- None. AMB-001 through AMB-007 are resolved by the decisions above.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd checklist --work 2730-doc-comments-to-signatures`.
