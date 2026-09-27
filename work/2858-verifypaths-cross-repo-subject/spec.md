---
schemaVersion: 1
workId: 2858-verifypaths-cross-repo-subject
title: Explicit verify-paths subject and Coordination cross-repo paths
stage: specify
changeTier: tier1
status: specified
publicOrToolFacingImpact: true
---

# Explicit verify-paths subject and Coordination cross-repo paths Specification

Prose status: specified

## User Value
An operator can verify a PR against the issue they explicitly named, including a Coordination issue declaring paths in the PR repository.

## Scope
- SB-001: verify-paths issue subject selection, explicit Coordination cross-repository path namespace, and parity evidence.

## Non-Goals
- SB-002: Do not change claim, review, delivery, or issue routing decisions.
- SB-003: Do not treat a cross-repository closing reference as authorization to use a Coordination declaration.

## User Stories
- US-001 (P1): As an operator, I can verify a PR against the issue I name and see which declaration the verdict used.
- US-002 (P1): As a merge-gate maintainer, I can refuse an unlicensed or ambiguous cross-repository comparison before it emits a verdict.

## Acceptance Scenarios
- AC-001 [US-001] [FR-001]: Given PR files admitted by its branch-derived issue but outside the named issue, `verify-paths <ref> --pr N --repo R` reports DRIFT against `<ref>`; conflicting positional and `--issue` refs refuse.
- AC-002 [US-001] [FR-002]: Given an explicit `--repo FS.GG.SDD --issue FS-GG/.github#N`, where `#N` declares SDD paths, the changed SDD files receive OK or DRIFT against that declaration.
- AC-003 [US-001] [FR-003]: Every OK or DRIFT output identifies the PR repository and the issue-body `Paths:` declaration by full cross-repository ref.
- AC-004 [US-002] [FR-004]: A missing explicit PR repository, undeclared `Paths:`, or unmatchable token yields no OK or DRIFT across repositories, including under `--warn`.
- AC-005 [US-002] [FR-002]: A closing reference to another repository without an explicit subject remains SKIP and cannot authorize the Coordination comparison.
- AC-006 [US-001] [FR-005]: A correctly scoped PR on an `item/<n>-…` branch still reports OK.

## Functional Requirements
- FR-001: A positional issue ref or `--issue` selects the issue-body `Paths:` declaration; both together are refused. (Stories: US-001; Acceptance: AC-001)
- FR-002: An explicit PR `--repo` and named `FS-GG/.github` issue permit a cross-repository `Paths:` comparison; implicit cross-repository closing references never authorize it. (Stories: US-001, US-002; Acceptance: AC-002, AC-005)
- FR-003: Every OK or DRIFT verdict names the PR repository, issue repository, and issue-body `Paths:` source. (Stories: US-001; Acceptance: AC-003)
- FR-004: An undeclared or ambiguous cross-repository path namespace refuses before OK or DRIFT, including under `--warn`. (Stories: US-002; Acceptance: AC-004)
- FR-005: A conventionally named branch with a correctly scoped issue continues to pass. (Stories: US-001; Acceptance: AC-006)

## Ambiguities
- AMB-001: A repo-relative path token cannot identify its repository by syntax alone. The explicit PR `--repo` is the cross-repository namespace selector; the `.github` issue supplies the declaration.

## Public Or Tool-Facing Impact
- This specification is an SDD lifecycle artifact and command-report contract input.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd clarify --work 2858-verifypaths-cross-repo-subject`.
