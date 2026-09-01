---
schemaVersion: 1
workId: gs2-04-9-protected-sandbox-authority
title: Gs2 04 9 Protected Sandbox Authority
stage: specify
changeTier: tier1
status: specified
publicOrToolFacingImpact: true
---

# GS2-04.9 Protected Sandbox Authority Specification

Prose status: specified

## User Value
Operators can execute GS2-04.9 live qualification through a protected non-production App identity and retain cleanup proof.

## Scope
- SB-001: Add one manual protected workflow in the organization credential-owning repository.
- SB-002: Mint the existing `fs-gg-cross-repo-dispatch` installation token for only the fixed disposable repository and granted permissions required by Q4.
- SB-003: Check out the exact product candidate, invoke its live harness, always collect cleanup evidence, and bind the protected run identity.

## Non-Goals
- SB-004: No production target, human-token qualification effect, general-purpose App token artifact, deployment, package publication, or successor roadmap work.

## User Stories
- US-001 (P1): As an operator, I can dispatch an exact product candidate for live comprehensive qualification without receiving the App private key.
- US-002 (P1): As a security reviewer, I can prove the App token was repository-scoped, target-fixed, short-lived, and never substituted with a human or production credential.
- US-003 (P1): As a roadmap auditor, I can inspect immutable run artifacts proving authoritative rereads, reverse compensation, and final cleanup.

## Acceptance Scenarios
- AC-001 [US-001] [FR-001]: Given a 40-hex candidate SHA, when manually dispatched, then the workflow checks out exactly that commit from FS.GG.Coordination and refuses branch names, missing commits, or checkout drift.
- AC-002 [US-002] [FR-002]: Given protected App secrets, when authority is minted, then the token is limited to `FS.GG.GitHub.Substrate.Sandbox`, the declared subset of installation permissions, and the observed App actor.
- AC-003 [US-002] [FR-003]: Given any target or identity mismatch, when preflight runs, then it exits before the product harness can issue a write and retains a red diagnostic artifact.
- AC-004 [US-003] [FR-004]: Given a live execution success or failure, when the job terminates, then cleanup runs unconditionally and exact-candidate evidence is uploaded with a unique run/attempt identity.

## Functional Requirements
- FR-001: The workflow MUST accept only a full 40-hex FS.GG.Coordination candidate, check out that exact object with pinned actions, and prove `HEAD` equality before execution. (covers AC-001)
- FR-002: The workflow MUST mint `fs-gg-cross-repo-dispatch` from protected secrets with explicit owner, repository allowlist, and minimum granted permission requests; the token and private key MUST never be uploaded or printed. (covers AC-002)
- FR-003: Preflight MUST authoritatively verify App actor id 297630107, repository node `R_kgDOUKXpqQ`, private/non-production description, Project node `PVT_kwDOEYAWY84BiESo`, purpose marker, and run expiry before exporting the token to the product harness. (covers AC-003)
- FR-004: The live harness MUST run with fixed sandbox coordinates and bounded quotas; cleanup and evidence upload MUST use `always()`, artifacts MUST include run and attempt identity, and missing or residual cleanup MUST remain non-green. (covers AC-004)

## Ambiguities
- AMB-001: The exact App permissions that remain sufficient for every Q4 surface must be fixed without requesting its whole installation grant.
- AMB-002: The workflow must preserve cleanup execution when the live harness exits non-zero without converting that failure into green.

## Public Or Tool-Facing Impact
- Add one manually dispatched protected workflow; no reusable secret-bearing interface and no change to product package APIs.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd clarify --work gs2-04-9-protected-sandbox-authority`.
