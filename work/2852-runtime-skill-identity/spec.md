---
schemaVersion: 1
workId: 2852-runtime-skill-identity
title: "Bind producer, package, materialized, and runtime-loaded skill identity"
stage: specify
changeTier: tier1
status: specified
publicOrToolFacingImpact: true
---

# Bind producer, package, materialized, and runtime-loaded skill identity Specification

Prose status: specified

## User Value
A consumer and an agent can identify the exact skill bytes loaded at runtime and verify them against the authoritative producer source and the package that materialized them.

## Scope
- SB-001: Define one content identity for a skill from its authoritative producer source and carry that identity through the central registry and FS.GG.Kit manifest.
- SB-002: Make coordination materialization verify and report the package-declared identity against receiver bytes.
- SB-003: Make the runtime-view tool report which declared root supplies the loaded skill and verify its bytes against the same identity.
- SB-004: Exercise the complete chain for `cross-repo-coordination` and keep the implementation generic for every registered coordination-kit skill.

## Non-Goals
- SB-101: Do not change `materializes-when`, template parameters, or the choice of which skills a product receives.
- SB-102: Do not infer authority from whichever runtime root happens to be readable first.
- SB-103: Do not require a runtime vendor to expose internal loader APIs; declared runtime roots and their actual bytes are the observable boundary.
- SB-104: Do not reconcile receiver drift silently in check mode.

## User Stories
- US-001 (P1): As an agent verifying a skill claim, I can ask the tool which producer source and digest govern the runtime-loaded copy, so citations are not resolved against an arbitrary mirror.
- US-002 (P1): As a receiver maintainer, I can prove that the package manifest, materialized bytes, and every declared runtime-visible copy agree with the producer identity.
- US-003 (P1): As a gate maintainer, I get a non-zero, reason-specific result when a one-line divergence is introduced at any link in the chain.

## Acceptance Scenarios
- AC-001 [US-001] [FR-001] [FR-002]: Given a registered coordination-kit skill and a coherent receiver, an identity query names the skill id, authoritative producer source, expected digest, package/materialized destination, declared runtime roots, and the runtime-visible file(s), and all digest values agree.
- AC-002 [US-002] [FR-003]: Given a pinned FS.GG.Kit package, materialization/check output binds each managed skill destination to the digest from that package's manifest rather than to a moving checkout.
- AC-003 [US-001] [FR-004]: Given multiple declared runtime roots, the report identifies every visible copy and the declared live source root; it never selects an authority by directory order.
- AC-004 [US-003] [FR-005]: Given a one-line mutation in a materialized or runtime-visible `cross-repo-coordination/SKILL.md`, the corresponding gate exits non-zero and names the divergent artifact and expected/actual digest.
- AC-005 [US-003] [FR-006]: Given an absent, unreadable, duplicate, or malformed identity input, the tools return an inconclusive/error result rather than an empty or coherent result.

## Functional Requirements
- FR-001: The registry contract MUST expose the authoritative skill id, owner/source path, and canonical sha256 used as the producer identity. (Stories: US-001, US-002; Acceptance: AC-001)
- FR-002: Runtime identity output MUST name the authoritative producer source and every package, materialized, and runtime-visible artifact it compares, including each digest and verdict. (Stories: US-001; Acceptance: AC-001)
- FR-003: `coordination-sync --against-pin` MUST bind materialized skill bytes to the restored package's own manifest digest and emit machine-readable identity facts for those comparisons. (Stories: US-002; Acceptance: AC-002)
- FR-004: `skill-view` MUST bind runtime-visible copies to the receiver's declared live source/root set and report all copies without inferring authority from traversal order. (Stories: US-001, US-002; Acceptance: AC-003)
- FR-005: Focused gates MUST prove that a one-line divergence in `cross-repo-coordination` makes the materialization or runtime identity verdict red, then restore and prove green. (Stories: US-003; Acceptance: AC-004)
- FR-006: Missing, unreadable, malformed, duplicate, and digest-mismatched identity facts MUST fail closed with a reason-specific diagnostic. (Stories: US-003; Acceptance: AC-005)

## Ambiguities
- AMB-001: Whether the package manifest alone is the runtime identity authority or must be cross-checked against the central producer registry when both are locally available.
- AMB-002: Whether identity output belongs in existing check output or a dedicated `identity` command suitable for agents and gates.
- AMB-003: How a receiver with copied runtime roots distinguishes the declared live source from generated views without making path order authoritative.

## Public Or Tool-Facing Impact
- Extends tool output for registry/materialization/runtime skill checks with a stable identity projection; existing check/apply behavior and parameters remain compatible.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd clarify --work 2852-runtime-skill-identity`.
