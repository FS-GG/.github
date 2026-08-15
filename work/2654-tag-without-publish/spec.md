---
schemaVersion: 1
workId: 2654-tag-without-publish
title: kit-auto-publish tags without prepared bytes
stage: specify
changeTier: tier1
status: specified
publicOrToolFacingImpact: true
---

# kit-auto-publish tags without prepared bytes Specification

Prose status: specified

## User Value

A merge that the coherent-set auto-publish rail decides to tag either publishes all three
packages to both feeds or creates no tags at all, so no release namespace is ever left
immutably tagged with nothing behind it.

The defect this replaces is measured, not hypothetical. `.github#2571` merged as `a415652f`,
moving `<FsggCoherentSetVersion>` from `0.58.0` to `0.58.1`. `kit-auto-publish.yml` did
exactly what it is built to do: it decided `tag` and pushed all three sibling tags at that
commit. All three release workflows then failed within seconds on
`gh release download "coherent-set/v$VERSION" ... release not found`, because
`release-saga-prepare.yml` — the only thing that creates that release — is
`workflow_dispatch`-only and no operator had run it. `0.58.1` is therefore immutably tagged
in three namespaces with zero packages published, and its authoring PR's declared
`kind=release-verification` obligation carries no receipt, because there was no release to
verify.

The two rails are each internally correct. Nothing joins them. That join is this work.

## Scope

- SB-001: The join between `kit-auto-publish.yml`'s tag decision and the
  `coherent-set/v<version>` bytes `release-saga-prepare.yml` prepares — specifically, making
  it structurally impossible for the tag write to execute in a run where preparation did not
  succeed.
- SB-002: `release-saga-prepare.yml`'s trigger surface and its stated assumption about who
  starts publication.
- SB-003: The patch-line row of `.claude/skills/pnext-item/references/merge-and-release.md`
  and its byte-identical `.agents` mirror, so the obligation token a worker is told to
  declare names the act that actually occurs.
- SB-004: A regression leg in `tests/kit-auto-publish/` that scores the workflow topology
  rather than the decision program, plus re-runnable gate-inversion evidence for it.
- SB-005: An explicit, recorded disposition for the three already-pushed `0.58.1` tags.

## Non-Goals

- SB-006: `.github#2442`'s frontier rail — the deliberate restriction of the auto rail to a
  same-line patch bump — is a recorded maintainer decision and is not reopened here.
- SB-007: `decide()`'s action vocabulary, refusal reasons and fail-closed observation
  contract are unchanged. The gap is downstream of every answer `decide()` gives, which is
  why a leg that only scores `decide()` stays green through this defect.
- SB-008: Performing any publication to a public feed. Publication stays behind the same
  irreversible edge; this work makes the automation able to reach it correctly, and records
  what a human must still do for `0.58.1`.
- SB-009: `check-kit-published-coherence.py`'s obligation-arm verdict semantics. The arm
  already derives its answer from `decide()` rather than restating a version line
  (`.github#2571`); this work does not change what it computes, only the prose whose claim
  about the world it is grading against.
- SB-010: Do not implement later lifecycle commands or Governance enforcement in this
  specification.

## User Stories

- US-001 (P1): As the release train, when an eligible patch merges, I either complete the
  whole publication or leave no immutable trace, so a namespace is never burned on bytes
  that do not exist.
- US-002 (P1): As an operator, I can still prepare a coherent set by hand at an exact
  commit, and the automation uses the same path I do rather than a second one that can drift.
- US-003 (P1): As a worker declaring a post-merge obligation, the token the reference tells
  me to use names the act that actually happens on my version line.
- US-004 (P2): As a reviewer, I can run one hermetic leg that fails if the tag write is ever
  re-decoupled from preparation, without needing a live release to observe it.
- US-005 (P2): As whoever picks up `0.58.1`, I can read a recorded disposition for its three
  tags instead of inferring one.

## Acceptance Scenarios

- AC-001 [US-001] [FR-001]: Given a run in which `decide()` answers `tag` or `tagSiblings`, when the preparation job is skipped or fails, then no job that pushes a `refs/tags/` ref executes in that run.
- AC-002 [US-001] [FR-001]: Given a run in which `decide()` answers `tag`, when preparation succeeds, then the `coherent-set/v<version>` release carrying the manifest-bound packages exists before any tag is pushed, so every release workflow the tags start finds the bytes it downloads.
- AC-003 [US-002] [FR-002]: Given `release-saga-prepare.yml`, when another workflow in this repository calls it with an explicit source commit, then it resolves the version, packs, preflights and creates the draft release at that commit rather than at the caller's head — and its existing `workflow_dispatch` operator path is unchanged.
- AC-004 [US-002] [FR-003]: Given a reader of `release-saga-prepare.yml`'s header, when they ask who starts publication, then the header names both the operator and the auto-publish rail, so no reader concludes an operator dispatch is the only entry point.
- AC-005 [US-003] [FR-004]: Given a worker reading the patch-line row of `merge-and-release.md`, when they choose an obligation token, then the row describes preparation-and-tagging as one automated act and states that a green `kit-auto-publish` run is not evidence that any package was published.
- AC-006 [US-003] [FR-004]: Given the `.claude` copy of `merge-and-release.md` and its `.agents` mirror, when either is changed, then both carry identical bytes.
- AC-007 [US-004] [FR-005]: Given `tests/kit-auto-publish/run.sh`, when it runs hermetically with no network, then it reads the real workflow files and fails if any tag-pushing job's transitive `needs` closure omits a job that calls `release-saga-prepare.yml`, or if that job's condition could admit a state its preparation job does not.
- AC-008 [US-004] [FR-006]: Given each structural leg added by this work, when the pre-repair structure is textually reintroduced into a mutated copy of the real file, then the leg is observed to fail, and that mutation is committed as a re-runnable part of the suite rather than reported as a one-time manual observation.
- AC-009 [US-005] [FR-007]: Given the three `0.58.1` tags at `a415652f`, when this work merges, then their disposition is recorded explicitly — a named recovery route at that exact commit or an abandonment — together with the reason the choice is not a worker's to execute unilaterally.
- AC-010 [US-001] [FR-001]: Given the residual state in which tags exist and the feeds are still empty, when the auto rail next runs, then it escalates rather than retries, and the recorded disposition names the repair.

## Functional Requirements

- FR-001: The auto-publish rail must not push any coherent-set tag unless the coherent-set draft release carrying the manifest-bound packages for that exact version and source commit was produced successfully earlier in the same workflow run. (Stories: US-001; Acceptance: AC-001, AC-002, AC-010)
- FR-002: Saga preparation must be invocable by another workflow in this repository at an explicitly named source commit, without an operator dispatch, and must pack and preflight at that commit rather than at the caller head, leaving the operator dispatch path intact. (Stories: US-002; Acceptance: AC-003)
- FR-003: The stated trigger assumption in the preparation workflow must name every actor that starts publication, so that no reader concludes an operator is the only one who pushes the three tags. (Stories: US-002; Acceptance: AC-004)
- FR-004: The patch-line row of the merge-and-release reference and its mirror must describe the act the merge actually performs, and must state that a green auto-publish run is not evidence that any package was published. (Stories: US-003; Acceptance: AC-005, AC-006)
- FR-005: A regression leg must score the workflow topology downstream of the decision program, failing whenever a job that pushes a coherent-set tag can execute in a run where the preparation job was skipped or failed. (Stories: US-004; Acceptance: AC-007)
- FR-006: Every gate this work adds must ship with a re-runnable mutation that reproduces the pre-repair structure and is observed to turn that gate red. (Stories: US-004; Acceptance: AC-008)
- FR-007: The disposition of the three already-pushed 0.58.1 tags must be recorded explicitly as either a completed recovery at the tagged commit or an abandonment, and never left implicit. (Stories: US-005; Acceptance: AC-009)

## Ambiguities

- AMB-001: Acceptance criterion 2 of `.github#2654` names two opposite repairs and does not
  choose between them: either `kit-auto-publish` READS whether a prepared
  `coherent-set/v<version>` release exists and refuses to tag without one, or preparation
  becomes automatic and `release-saga-prepare`'s header assumption is corrected in the same
  change. They have different blast radii on an irreversible publish path, and only one can
  be built.
- AMB-002: If preparation becomes automatic, it is not settled which commit it must pack at
  on the `tagSiblings` repair path, where the existing kit tag may name a commit that is no
  longer `main`'s head.
- AMB-003: It is not settled whether the residual state — tags pushed, preparation done, but
  a release workflow failing — needs new machinery, or is already covered by the existing
  `tag-exists-without-both-feed-publication` escalation.
- AMB-004: It is not settled whether the `0.58.1` recovery is inside this work's authority.

## Public Or Tool-Facing Impact

- `merge-and-release.md` is coordination-kit packed content read by every worker that
  declares a post-merge obligation. Changing it changes the packed kit manifest and
  therefore obliges a coherent-set version bump and republish.
- `release-saga-prepare.yml` gains a caller-facing `workflow_call` surface whose input
  contract other workflows in this repository bind to.
- This specification is an SDD lifecycle artifact and command-report contract input.

## Lifecycle Notes

- Next lifecycle action: `fsgg-sdd clarify --work 2654-tag-without-publish`.
