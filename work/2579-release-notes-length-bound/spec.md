---
schemaVersion: 1
workId: 2579-release-notes-length-bound
title: "bounding PackageReleaseNotes, and gating the length the registry actually enforces"
stage: specify
changeTier: tier1
status: specified
publicOrToolFacingImpact: true
---

# bounding PackageReleaseNotes, and gating the length the registry actually enforces Specification

Prose status: specified

## User Value

A release author is told, **before any package is pushed**, how many characters of nuget.org's
35,000-character `PackageReleaseNotes` budget each coherent-set package has left — and the field no
longer grows without bound, so the budget stops being something an author races. Today neither holds.
On 2026-08-14 the `0.52.0` cut pushed `FS.GG.Kit` and `FS.GG.Drivers` to both feeds, then took a hard
`BadRequest 400` from nuget.org on `FS.GG.Coord.Cli` — *"A nuget package's ReleaseNotes property may
not be more than 35000 characters long."* Every gate the author ran locally was green, because no gate
anywhere measures that length: `grep -rn "35000" scripts/ .github/workflows/ tests/` returns nothing.

The failure is not that someone wrote too much. It is that `PackageReleaseNotes` **accumulates every
version's entry**, so it grows monotonically and crosses the limit regardless of any single author's
restraint. Headroom went 1,241 characters at `0.51.1` to **negative 2,279** at `0.52.0`. Until the
accumulation is bounded, every coherent-set cut is unpublishable, and so is every kit-skill lane
behind `check-kit-published-coherence`'s strictly-greater rule.

## Scope

- SB-001: **A length arm inside `scripts/check-engine-release-notes.py`**, whose subject is every
  member of the coherent set — `src/FS.GG.Kit/FS.GG.Kit.csproj`,
  `src/FS.GG.Drivers/FS.GG.Drivers.csproj`, `src/FS.GG.Coord.Cli/FS.GG.Coord.Cli.fsproj` — not
  coord-engine alone. The set is read from `scripts/check-coherent-set-version.py`'s existing
  `PROJECTS` tuple, never restated, so a fourth member cannot be added there and silently escape this
  gate.
- SB-002: **The limit expressed exactly once**, as a module constant naming nuget.org as the external
  constraint it is, with a recorded citation of the observed `400` text. No workflow, fixture or
  document restates `35000`.
- SB-003: **Headroom reported for every package on every run**, green or red — characters used,
  characters remaining, and percentage of budget consumed. Pass/fail alone is what let an author with
  1,241 characters of headroom write a 3,575-character entry.
- SB-004: **`PackageReleaseNotes` bounded by construction** into three parts, two of them structurally
  separate MSBuild properties: `$(FsggStandingAdvisories)` (permanent, safety-critical), the current
  release's own entry (one entry), and a pointer to where the full history is served.
- SB-005: **Structural preservation of the advisories.** The authored `<PackageReleaseNotes>` element
  text must contain the literal reference `$(FsggStandingAdvisories)`, and the evaluated advisory
  property must be non-empty and must appear in the evaluated notes. Trimming the narrative and
  deleting a `DO NOT ADOPT` warning become edits to different properties, and the gate refuses the
  second.
- SB-006: **Reachability.** `PATHS_SUBJECT` grows to name every project whose notes this gate now
  scores, and both `pull_request` and `push` `paths:` filters in
  `.github/workflows/engine-release-notes.yml` are widened to select them. The existing fixture leg
  that compares the two already fails closed on drift; it is retained and now covers the wider set.
- SB-007: **Gate-inversion evidence, with the real tree observed red.** Fixture legs for every new
  arm, plus a leg that runs the length arm against `origin/main`'s actual
  `FS.GG.Coord.Cli.fsproj` — 37,279 evaluated characters — and requires exit 1 with the measured
  overage named.
- SB-008: **`.github#1762`'s first-token check preserved verbatim in behaviour**, together with the
  empty-notes, coherent-scalar and unevaluable-project arms. This adds a property; it replaces none.

## Non-Goals

- SB-101: **Does not recover `0.52.0`.** `FS.GG.Coord.Cli 0.52.0` exists on the org feed and not on
  nuget.org. The decided recovery is an additive `0.52.1` re-cut — refusing both a non-byte-identical
  force-push and the deletion of a published artifact — and it is a separate item that lands AFTER
  this one. Nothing here re-cuts, re-pushes or deletes anything.
- SB-102: **Does not cut a release and does not move `FsggCoherentSetVersion`.** The scalar stays
  `0.52.0` and the notes' first token stays `0.52.0`, so `.github#1762`'s check is satisfied by the
  repaired tree exactly as by the current one.
- SB-103: **Does not touch `check-feed-coherence`'s red on `main`.** That red is the `0.52.0` partial
  publish and is `.github#2580`.
- SB-104: **Does not add a second, lower "warning" threshold that reds.** A limit nuget.org does not
  enforce would be a second source of truth and a red the registry would have accepted. Headroom
  reporting is the signal; see DEC-004.
- SB-105: **Does not add the length arm to `release-kit.yml` or `release-drivers.yml`.** See DEC-005.
- SB-106: **Does not add an advisory for `0.52.0`'s org-feed-only state.** See DEC-006.

## User Stories

- US-001 (P1): As a release author, I am refused before any push when a coherent-set package's
  release notes exceed nuget.org's limit, and I am told by how much.
- US-002 (P1): As a release author preparing an entry, I can see on every PR how much of the budget
  each package has left, so I never discover the ceiling by hitting it.
- US-003 (P1): As a consumer of `FS.GG.Coord.Cli`, the newest listing still tells me which published
  versions must not be adopted, even though the narrative history it used to carry is gone.
- US-004 (P1): As a future author trimming these notes under pressure, I cannot delete a
  `DO NOT ADOPT` advisory as a side effect of trimming the narrative.
- US-005 (P1): As a reviewer, I can see each arm of this gate observed red, including the length arm
  against the real `origin/main` tree.

## Acceptance Scenarios

- AC-001 [US-001] [FR-001]: Given a coherent-set project whose evaluated `PackageReleaseNotes`
  exceeds the limit, when `check-engine-release-notes.py` runs, then it exits 1 and names that
  project, its evaluated length and its overage — for any member of the set, not coord-engine alone.
- AC-002 [US-002] [FR-002]: Given any run of the checker, green or red, when it completes, then it
  reports used, remaining and percent-of-budget for every coherent-set package, and `35000` appears
  as a single named constant in the implementation and nowhere else in `scripts/`, `.github/workflows/`
  or `tests/`.
- AC-003 [US-003] [FR-003]: Given the repaired `FS.GG.Coord.Cli.fsproj`, when its evaluated
  `PackageReleaseNotes` is read, then it contains both standing advisories — `DO NOT ADOPT 0.50.1`
  and `DO NOT ADOPT 0.50.5`, each with the reason it can never be completed — the `0.52.0` entry, and
  a pointer to where the full per-version history is served.
- AC-004 [US-004] [FR-004]: Given an edit that removes the `$(FsggStandingAdvisories)` reference from
  the authored `<PackageReleaseNotes>` element, or that empties the advisory property, when the
  checker runs, then it exits 1 naming the missing reference or the empty property — even though the
  length arm and the first-token arm would both be satisfied.
- AC-005 [US-001] [FR-005]: Given a project whose notes do not begin with the evaluated `Version`,
  when the checker runs, then it still exits 1 with `.github#1762`'s message, and given a `Version`
  that does not resolve from `FsggCoherentSetVersion`, then it still exits 1 with `.github#2512`'s.
- AC-006 [US-002] [FR-006]: Given any file named in `PATHS_SUBJECT`, when a pull request changes only
  that file, then `engine-release-notes.yml` runs; and the fixture fails closed if a `PATHS_SUBJECT`
  entry is absent from either trigger's `paths:` list.
- AC-007 [US-005] [FR-007]: Given `origin/main`'s `FS.GG.Coord.Cli.fsproj` at 37,279 evaluated
  characters, when the length arm runs against it, then it exits 1; and given the repaired project,
  then it exits 0 — both observed, both recorded, and the first executed as a fixture leg rather than
  asserted.

## Functional Requirements

- FR-001: The checker evaluates `PackageReleaseNotes` through MSBuild for every member of the coherent set, reading that set from `check-coherent-set-version.py`'s `PROJECTS` rather than restating it, and exits 1 when any member's evaluated value exceeds the limit. (Stories: US-001; Acceptance: AC-001)
- FR-002: The limit is one named module constant citing nuget.org's own error text, and every run reports used/remaining/percent for every member. (Stories: US-002; Acceptance: AC-002)
- FR-003: `PackageReleaseNotes` is bounded to `$(FsggStandingAdvisories)` plus the current release entry plus a history pointer, and the accumulated per-version narrative is removed. (Stories: US-003; Acceptance: AC-003)
- FR-004: The checker refuses an authored `<PackageReleaseNotes>` that does not reference `$(FsggStandingAdvisories)`, and refuses an empty or absent advisory property. (Stories: US-004; Acceptance: AC-004)
- FR-005: `.github#1762`'s first-token check and `.github#2512`'s coherent-scalar check are preserved unchanged in behaviour. (Stories: US-001; Acceptance: AC-005)
- FR-006: `PATHS_SUBJECT` names every project this gate scores, and both triggers in `engine-release-notes.yml` select every `PATHS_SUBJECT` entry. (Stories: US-002; Acceptance: AC-006)
- FR-007: Every arm added here ships with a recorded inversion and an observed red, and the length arm's red is observed against the real `origin/main` tree. (Stories: US-005; Acceptance: AC-007)

## Ambiguities

- AMB-001: **How is the unbounded accumulation bounded?** Truncation to the last N versions, a
  pointer to `registry/CHANGELOG.md`, per-version notes, or something else — each with different
  consequences for what a consumer reads off a published nuspec, and one of them (wholesale deletion)
  already tried and reverted at `5d45ced4`.
- AMB-002: **What makes the preservation of the poisoned-set advisories survive a future trim?**
  Author care demonstrably did not: `4fccc76d`'s author deleted them and did not notice.
- AMB-003: **Which packages, and measured how?** The set's membership, and whether the quantity
  scored is the file's raw XML inner text or the evaluated MSBuild property — these differ by 55
  characters on the current tree.
- AMB-004: **Should the gate carry a second, lower threshold that reds before the real limit?**
- AMB-005: **Do `release-kit.yml` and `release-drivers.yml` need their own length arm?**
- AMB-006: **Does `FS.GG.Coord.Cli 0.52.0`'s org-feed-only state belong in the standing advisories?**

## Public Or Tool-Facing Impact

- `PackageReleaseNotes` is rendered on every published package listing on both feeds. Its content is
  the org's only correction channel for immutable, wrong listings, so a change to what it carries is
  consumer-facing and is specified here rather than left to the implementation.
- `scripts/check-engine-release-notes.py` is a required PR gate; its exit codes are a contract
  (`0` coherent, `1` incoherent, `2` could not evaluate) and are preserved.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd clarify --work 2579-release-notes-length-bound`.
