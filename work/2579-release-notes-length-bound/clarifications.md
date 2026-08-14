---
schemaVersion: 1
workId: 2579-release-notes-length-bound
title: "bounding PackageReleaseNotes, and gating the length the registry actually enforces"
stage: clarify
changeTier: tier1
status: clarified
sourceSpec: work/2579-release-notes-length-bound/spec.md
publicOrToolFacingImpact: true
---

# bounding PackageReleaseNotes, and gating the length the registry actually enforces Clarifications

## Source Specification
- work/2579-release-notes-length-bound/spec.md

## Clarification Questions
- CQ-001 [AMB:AMB-001] blocking answered: How is the unbounded accumulation bounded?
- CQ-002 [AMB:AMB-002] blocking answered: What makes advisory preservation survive a future trim?
- CQ-003 [AMB:AMB-003] blocking answered: Which packages, and which measurement of "length"?
- CQ-004 [AMB:AMB-004] blocking answered: A second, lower warning threshold?
- CQ-005 [AMB:AMB-005] blocking answered: A length arm in the sibling release workflows?
- CQ-006 [AMB:AMB-006] blocking answered: Does `0.52.0`'s org-feed-only state belong in the advisories?

## Answers

- CQ-001 [AMB:AMB-001] answer: The accumulated history is redundant with the registry; the standing
  advisories are not. Every published version's notes are already served, permanently and immutably,
  on that version's own listing — `0.23.0`'s entry is readable at
  `nuget.org/packages/FS.GG.Coord.Cli/0.23.0` forever — so re-shipping them inside `0.52.0`'s notes
  duplicates content the registry already hosts. What a later listing carries that an earlier one
  CANNOT is a correction ABOUT an earlier one: `FS.GG.Coord.Cli 0.50.1` and `0.50.5` are permanent
  two-of-three sets whose siblings published, whose own publish was refused, and which
  `.github#1772`'s sibling-tag precondition plus immutable tags make impossible to complete — so
  those listings can never be corrected. The newest listing is the only surface on which the org can
  say so, and `5d45ced4` established that it is THE channel: an unpinned `dotnet tool install`
  resolves the newest version, which makes its notes precisely what a consumer reads. That asymmetry
  is the whole basis of the split.

- CQ-002 [AMB:AMB-002] answer: Care is not a mechanism, and this repository has the measurement.
  `4fccc76d` replaced `<PackageReleaseNotes>` wholesale (+31/-418, 618 lines to 231) and deleted
  every poisoned-set warning; its author's own repair commit says *"I mentioned the truncation
  nowhere, because I had not noticed it."* `check-engine-release-notes` was GREEN across that
  deletion, because it checks the first token and says in its own docstring that it deliberately does
  not judge prose. A bound that leaves the advisories inside the same field as the narrative
  recreates that hazard under worse conditions, because the next author will be trimming against a
  hard ceiling. So the two become different MSBuild properties and the gate refuses removal of the
  reference.

- CQ-003 [AMB:AMB-003] answer: The set is `FS.GG.Kit`, `FS.GG.Drivers`, `FS.GG.Coord.Cli`, named once
  in `check-coherent-set-version.py`'s `PROJECTS` tuple — whose own comment says *"Adding a fourth
  member is a deliberate edit to this tuple"* — and folded out of that file rather than restated,
  because a second copy is how a fourth member escapes this gate. The quantity is the EVALUATED
  MSBuild property: the file's raw XML inner text is 37,334 characters while the evaluated property
  is 37,279. Of that 55-character difference, 36 come from `&lt;`/`&gt;` unescaping and 19 from a
  `$(FsggCoherentSetVersion)` reference the notes themselves contain, which MSBuild expands to
  `0.52.0`. The nuspec receives the evaluated value, so that is what nuget.org counted.

- CQ-004 [AMB:AMB-004] answer: No second threshold; report headroom instead. `0.52.0`'s author had
  1,241 characters of headroom and wrote 3,575 — what was missing was not a stricter red but the
  NUMBER. A gate reding at, say, 30,000 would refuse a tree nuget.org would have accepted, and
  `30000` would be a limit this org invented and would then have to maintain against one it does not
  control.

- CQ-005 [AMB:AMB-005] answer: No. `FS.GG.Kit` and `FS.GG.Drivers` declare no `PackageReleaseNotes`
  today, and the only way either gains one is a pull request editing its `.csproj` — both of which
  are now in this gate's `PATHS_SUBJECT` and both triggers' `paths:`, and `main` is protected, so no
  tag is reachable without passing this gate. The residual risk is stated rather than hidden: the
  three release workflows run in parallel on the three sibling tag pushes, so a release-time-only
  refusal in `release-coord-engine.yml` is structurally too late for the siblings — exactly the
  `0.52.0` shape. The answer is that the refusal is not release-time-only; it is PR-time, at the only
  point where notes can enter.

- CQ-006 [AMB:AMB-006] answer: No, not in this item. `FS.GG.Coord.Cli 0.52.0` is on the org feed and
  absent from nuget.org, but that is unlike `0.50.1`/`0.50.5`: all three `v0.52.0` tags dereference
  to one commit (`2bf6e726`), so no two-of-three TAG set exists, and the org-feed `0.52.0` is a
  complete, usable package. It is a feed-parity defect (`.github#2580`) and the subject of the decided
  additive `0.52.1` re-cut, which lands after this item. Writing an advisory here would state a
  conclusion that item has not reached yet; `$(FsggStandingAdvisories)` is where it would go if it
  concludes one is owed.

## Decisions

- DEC-001 [CQ-001] [AMB:AMB-001]: **BOUND `PackageReleaseNotes` TO THREE PARTS: `$(FsggStandingAdvisories)` + the current release's entry + a pointer to the served per-version history.** The accumulated narrative is removed because the registry already serves it per version, immutably, at stable URLs. REJECTED — **truncate to the last N versions**: it measures the wrong unit (entries here range 289 to 4,932 characters, a 17-fold spread, so no N bounds the field in *characters*, which is what nuget.org enforces — reintroducing this item's own defect class inside its fix), and it ages the `DO NOT ADOPT 0.50.5` advisory out silently, because that advisory lives inside `0.50.6`'s entry and a count-based window would delete it as a side effect of an unrelated cut. REJECTED — **replace the notes with a pointer to `registry/CHANGELOG.md`**: that file is a log of `dependencies.yml` changes, not of engine release notes, so it does not contain the content being removed and the pointer would not be a relocation; and a link is the wrong channel for a safety warning, because the consumer deciding whether to `dotnet tool install` reads the listing text, often inside an IDE package pane, so a two-hop link degrades exactly the message that had to be preserved. Retained in reduced form: a pointer IS kept, for the narrative history, which is not safety-critical. REJECTED — **per-version notes only (current entry, nothing else)**: it is semantically the correct meaning of the field and it bounds hardest, but it deletes the correction channel outright; older listings are immutable, so the newest listing is the only place the org can say `DO NOT ADOPT 0.50.5`. Per-version-only is right for everything a listing says about *itself* and wrong for everything it must say about its *predecessors*.
- DEC-002 [CQ-002] [AMB:AMB-002]: **THE ADVISORIES BECOME A SEPARATE MSBuild PROPERTY, `<FsggStandingAdvisories>`, REFERENCED FROM `<PackageReleaseNotes>`, AND THE GATE REFUSES BOTH ITS REMOVAL AND ITS EMPTYING.** Trimming the narrative and deleting a `DO NOT ADOPT` warning become edits to different properties. The structural arm checks the AUTHORED element text for the literal `$(FsggStandingAdvisories)`; the semantic arm checks the EVALUATED property is non-empty and present in the evaluated notes. This mirrors `check-coherent-set-version.py`'s stated reason for carrying both arms: a structural check alone passes text that does not resolve, and a semantic check alone passes an inlined copy that the next trim deletes.
- DEC-003 [CQ-003] [AMB:AMB-003]: **SUBJECT = `check-coherent-set-version.py`'s `PROJECTS`, READ BY AST AND NEVER RESTATED; QUANTITY = THE EVALUATED MSBuild PROPERTY.** Read by AST rather than imported, on the same discipline `tests/engine-release-notes/run.sh` already applies to `PATHS_SUBJECT` — the gate must not execute another gate to learn its own subject. `scripts/check-coherent-set-version.py` therefore becomes an operand of this gate and joins `PATHS_SUBJECT` and both trigger filters.
- DEC-004 [CQ-004] [AMB:AMB-004]: **NO SECOND THRESHOLD. REPORT HEADROOM ON EVERY RUN INSTEAD** — used, remaining, and percent of budget, per package, green or red. The single constant is nuget.org's 35,000, named as the external constraint it is and cited to the observed `400` text.
- DEC-005 [CQ-005] [AMB:AMB-005]: **NO LENGTH ARM IN `release-kit.yml` OR `release-drivers.yml`.** Neither package declares `PackageReleaseNotes`, and the only way either gains one is a pull request editing its `.csproj`, which this gate's widened trigger now selects. Adding release-time arms would place the refusal after the point where the sibling packages already publish in parallel, which is the failure shape rather than a fix for it.
- DEC-006 [CQ-006] [AMB:AMB-006]: **NO `0.52.0` ADVISORY IN THIS ITEM.** `0.52.0` is a feed-parity gap, not a two-of-three set — all three tags name one commit and the org-feed package is complete and usable. Its disposition belongs to `.github#2580` and the decided additive `0.52.1` re-cut, which land after this item. `<FsggStandingAdvisories>` is the place such a warning would go if that item concludes one is owed.

## Accepted Deferrals
No accepted deferrals recorded.

## Remaining Ambiguity
None. All six blocking ambiguities are resolved by DEC-001 through DEC-006 above.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd checklist --work 2579-release-notes-length-bound`.
