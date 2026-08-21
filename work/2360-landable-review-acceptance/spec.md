---
schemaVersion: 1
workId: 2360-landable-review-acceptance
title: Require review acceptance in landable by default
stage: specify
changeTier: tier1
status: specified
publicOrToolFacingImpact: true
---

# Require review acceptance in landable by default Specification

Prose status: specified

## User Value
A worker following the ordinary landing path cannot receive a green verdict without a valid host
review-acceptance chain for the pull request's current head. The existing unattended
`registry-coherence` caller remains usable, but its deliberate review exemption becomes visible
rather than looking identical to an asserted review gate.

## Scope
- SB-001: Make review acceptance the default in `Client.landable`, preserving the existing explicit
  `fsgg:review-decision/v2` spelling.
- SB-002: Preserve the known `registry-coherence` unattended caller through a narrowly named exemption
  when it does not also request the review token, and report whether the review assertion was evaluated.
- SB-003: Add end-to-end command fixtures in `LandableNotOpenTests.fs` for default, explicit,
  stale-head, and exempt paths.
- SB-006: Bind host acceptance and landing authorization to the coordination item, live claim
  generation, PR head, and the current tip of the PR-declared base branch, and revalidate those
  revisions at the write boundary. Never treat the PR object's cached `base.sha` as that authority.

## Non-Goals
- SB-004: Do not change option grammar, branch protection, workflow files, or the semantics of any
  ordinary check-run name supplied through `--require`.
- SB-005: Do not expand review acceptance to non-green, merged, or closed verdicts; the assertion is a
  final downgrade of an otherwise-green candidate, as it is today.

## User Stories
- US-001 (P1): As a worker, a plain `landable` command refuses an otherwise-green PR until its current
  head has a valid host acceptance record.
- US-002 (P1): As an operator, command diagnostics reveal whether review acceptance was evaluated or
  deliberately exempted, so two green results no longer conceal different authorization strength.
- US-003 (P1): As the registry autofix workflow, I can continue using the explicitly named
  `registry-coherence` gate without acquiring a critic protocol that workflow does not run.

## Acceptance Scenarios
- AC-001 [US-001] [FR-001]: Given an otherwise-green open PR with no review comments, when plain
  `landable` runs, then it prints `pending`, exits 7, and names `fsgg:review-decision/v2` on stderr.
- AC-002 [US-001] [FR-002]: Given an otherwise-green open PR with a valid accepted chain bound to its
  current head, when plain `landable` runs, then it prints `green` and exits 0.
- AC-003 [US-001] [FR-004]: Given a valid chain bound only to another head, when plain or explicitly
  guarded `landable` runs, then it prints `pending` and exits 7.
- AC-004 [US-003] [FR-003]: Given an otherwise-green open PR with the explicitly required
  `registry-coherence` check and no review chain, when `landable` runs without the review token, then
  it stays green and stderr names the deliberate review exemption.
- AC-005 [US-002] [FR-003]: Given the same `registry-coherence` call also names
  `fsgg:review-decision/v2`, when no valid review chain exists, then the review request wins and the
  result is pending rather than exempt.
- AC-006 [US-002] [FR-001] [FR-002]: Given an otherwise-green verdict reaches the review decision,
  when the assertion is evaluated, then stderr states that evaluation; when the narrow exemption is
  used, stderr states the exemption instead.
- AC-007 [US-001] [FR-005]: Given host acceptance, when the owned `review record` producer appends it,
  then the sealed receipt binds the canonical issue/PR subject, live claim generation, exact head,
  and exact current tip resolved from the PR's declared base ref.
- AC-008 [US-001] [FR-006]: Given a valid accepted receipt and unchanged head but a moved effective
  base, when `landable` recomputes authorization, then it returns pending and names expected and actual
  base revisions.
- AC-009 [US-001] [FR-007]: Given guarded delivery, when claim, head, or base differs at the final
  write boundary, then no merge request is sent; on agreement, an emitted receipt names head and base.
- AC-010 [US-001] [FR-008]: Given two open PRs and formerly green authorization for the first, when the
  second lands a structurally relevant change into main while the first PR object's `base.sha` remains
  stale, then the advanced `refs/heads/main` tip invalidates authorization until merged-tree revalidation.

## Functional Requirements
- FR-001: A plain `landable` call on an otherwise-green PR MUST evaluate review acceptance and MUST return pending exit 7, naming the marker token, when no valid current-head chain exists. (covers AC-001, AC-006)
- FR-002: A plain `landable` call on an otherwise-green PR with valid current-head acceptance MUST return green exit 0. (covers AC-002, AC-006)
- FR-003: The existing `registry-coherence` unattended caller MUST remain exempt only when it does not also name the review token, and the command MUST emit a diagnostic distinguishing exemption from evaluation. (covers AC-004, AC-005, AC-006)
- FR-004: Explicit `--require fsgg:review-decision/v2` MUST remain compatible, and acceptance bound to a different head MUST remain pending rather than satisfy the gate. (covers AC-003, AC-005)
- FR-005: The acceptance receipt MUST bind its canonical issue/PR subject, live claim generation, exact head SHA, and exact current tip SHA of the PR-declared base branch, and these fields MUST be digest-protected. The PR object's cached `base.sha` and a merge-base MUST NOT substitute for that live ref. (covers AC-007)
- FR-006: `landable` MUST recompute head, claim generation, and the live PR-declared base-branch tip immediately before a green verdict; a mismatch MUST be pending and a base mismatch MUST name expected and actual SHAs. (covers AC-008, AC-010)
- FR-007: Guarded landing MUST re-read and condition on the current claim, head, and base immediately before its head-conditional GitHub merge write and MUST emit a receipt naming both revisions used. (covers AC-009)
- FR-008: An executable two-PR-equivalent fixture MUST keep the first PR's head and cached `base.sha` unchanged while advancing only its declared base ref, proving the live tip movement invalidates formerly green authorization. (covers AC-010)

## Ambiguities
- AMB-001 open: how to preserve a critic-free unattended caller without retaining a broad opt-in
  escape for ordinary workers.
- AMB-002 open: what observable output distinguishes an evaluated review assertion from an exemption
  without changing stdout's one-word machine contract.

## Public Or Tool-Facing Impact
- Plain `landable` becomes stricter for otherwise-green PRs: it now checks the existing structured
  review chain by default. Stdout and exit-code vocabulary do not change; stderr gains provenance for
  the review assertion or its narrow exemption.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd clarify --work 2360-landable-review-acceptance`.
