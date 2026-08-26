---
schemaVersion: 1
workId: 2953-gh-modernization-m0-invariants
title: GitHub Substrate v2 Q0 ratification
stage: clarify
changeTier: tier1
status: clarified
sourceSpec: work/2953-gh-modernization-m0-invariants/spec.md
publicOrToolFacingImpact: true
---

# GitHub Substrate v2 Q0 ratification Clarifications

## Source Specification
- work/2953-gh-modernization-m0-invariants/spec.md

## Clarification Questions
No clarification questions recorded.

## Answers
No clarification answers recorded.

## Decisions
- **DEC-001** [FR-005]: The ordered producer boundary tracked through `FS-GG/FS.GG.SDD#924` is the only new Typed SDD prerequisite for `GS2-02`: Q1 qualifies the exact canonical literate Quint source and extracted module set, a post-Q1 ADR-0077 amendment records the accepted authority/fingerprint/compatibility contract, and GS2-01.4 consumes the resulting published artifact. Later Q4-Q7 adoption and the workspace-default flip are not prerequisites.
- **DEC-002** [FR-005]: Defer the Typed SDD workspace-default flip until `OperatingV2`. This avoids changing lifecycle defaults, providers, scaffolder output, registry projections, receiver heads, and reusable workflows inside the v2 candidate-freeze window.
- **DEC-003** [FR-006]: Q0 records, under delegated maintainer authority on 2026-08-26, that scheduled complete fleet audits are authoritative for this cutover. The reviewed operational evidence has no continuously hosted App/webhook boundary with accepted ownership, availability, secret, ingress, observability, upgrade/incident, recovery, retention, or cost evidence. Event delivery remains an optional accelerator after `OperatingV2`; `.github#2961` leaves the critical path.
- **DEC-004** [FR-007]: Use a protected Git ref plus immutable phase tags and environment approval for the fleet epoch. The control issue is a projection and cannot authorize a transition.
- **DEC-005** [FR-007]: `OpenV2` is irreversible: rollback is permitted only through `VerifiedV2`; after `OpenV2`, recovery is roll-forward and no v1 writer resumes.
- **DEC-006** [FR-008]: Q0 requires independently authored architecture, security, operations, and cross-repository review against exact fingerprints. Each authorized reviewer first posts a distinct, unedited narrative comment on the repair PR, then posts an exact, unedited attestation whose canonical final `Evidence` line names that earlier comment; both comments must have the same GitHub `User`. Before either association or allowlist authorization, the login must be canonical: 1–39 ASCII characters, alphanumeric endpoints, and only alphanumerics or single internal hyphens. Authorization is then an allowed live association or an exact login in the fingerprint-bound, unique, nonempty `reviewAuthorAllowlist`. This avoids viewer-dependent private-membership results while keeping Bots, malformed/missing users, non-allowlisted `CONTRIBUTOR`/`NONE`, missing, self, later, edited, wrong-author, and wrong-PR evidence fail-closed. A generated roll-up or v1 review/delivery record cannot satisfy those roles.
- **DEC-007** [FR-009]: Program anchors remain issues and Project rows for visibility. Stable GS2 unit receipts in `FS.GG.Coordination`, merged PRs, and protected administrative receipts own completion.

## Accepted Deferrals
- **DEF-001** [DEC-002]: Resume the Typed SDD workspace-default decision after `OperatingV2`, using fresh v2 claims/review/evidence.
- **DEF-002** [DEC-003]: Re-evaluate a hosted webhook/App runtime after `OperatingV2` only when a named operator and full operational qualification exist.
- **DEF-003** [FR-005]: The advisory-only Agentic Workflows pilot, broader typed extensions, convenience UI, and non-authoritative reports remain outside the cutover critical path.

## Remaining Ambiguity
No blocking ambiguity remains.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd checklist --work 2953-gh-modernization-m0-invariants`.
