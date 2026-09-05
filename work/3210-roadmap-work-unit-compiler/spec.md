---
schemaVersion: 1
workId: 3210-roadmap-work-unit-compiler
title: Roadmap work-unit registration and acceptance compiler
stage: specify
changeTier: tier1
status: specified
publicOrToolFacingImpact: true
---

# Roadmap work-unit registration and acceptance compiler Specification

Prose status: specified

## User Value
compile the next eligible Roadmap v2 work unit through deterministic registration and acceptance in typed F#

## Scope
- SB-001: typed pure inspect/render/verify compiler consuming #3208 lifecycle/review receipts and #3209 qualification evidence, staged-intake transaction #3105, bounded patches, acceptance receipt and evidence index, roadmap-close handoff, tests, package, and work-roadmap adoption.

## Non-Goals
- SB-002: Prose assertions cannot confer registration or acceptance.
- SB-003: Do not add a second direct GitHub write path or bypass staged-intake replay/fencing.
- SB-004: Do not automate independent semantic judgement; accept only an already validated review receipt.
- SB-005: Do not redesign Roadmap v2 authoring or retroactively register already accepted units.

## User Stories
- US-001 (P1): As a user, I can compile the next eligible Roadmap v2 work unit through deterministic registration and acceptance in typed F#.

## Acceptance Scenarios
- AC-001 [US-001] [FR-001]: Given a Roadmap v2 source and catalog, when preparation is inspected, then exactly the first unchecked catalog unit whose immediate prerequisite is accepted is selected and every zero/multiple/misordered candidate state is refused.
- AC-002 [US-001] [FR-002]: Given a selected row, when registrations are derived, then the authority pin, unit registration, gate registrations, and evidence obligations are canonical, bounded, and identity-equal to the catalog row.
- AC-003 [US-001] [FR-003]: Given #3208 lifecycle/review receipts and #3209 qualification evidence, when acceptance is inspected, then only digest-valid, subject-matching, role-correct observed evidence can satisfy the unit's obligations.
- AC-004 [US-001] [FR-004]: Given SDD artifacts and command reports, when evidence state is derived, then only observed executions count and authored claims, stale reports, synthetic success, and missing obligations are refused.
- AC-005 [US-001] [FR-005]: Given implementation and acceptance revisions, when identities are checked, then implementation candidate/merge, acceptance candidate/merge, and protected-main are distinct where required and linked by the declared ancestry/equality rules.
- AC-006 [US-001] [FR-006]: Given a qualified unit, when acceptance is rendered, then one canonical acceptance receipt and one evidence index are sealed atomically and their digests cross-bind the unit, source, evidence, and identities.
- AC-007 [US-001] [FR-007]: Given interrupted preparation or acceptance, when the same immutable inputs are replayed, then output bytes and digests are identical and staged intake reuses rather than duplicates every registration.
- AC-008 [US-001] [FR-008]: Given accepted output, when handed to roadmap close, then the existing typed close boundary consumes it directly without a prose translation or alternate authority.
- AC-009 [US-001] [FR-009]: Given malformed catalog identity, wrong prerequisite, stale source, altered receipt, missing gate, wrong actor/role, identity collapse, unobserved SDD evidence, partial seal, or duplicate registration, when its independent inverted fixture runs, then the exact named refusal occurs before mutation.
- AC-010 [US-001] [FR-010]: Given the coherent package is published and receiver-verified, when `work-roadmap` adopts it and a later GS2 unit is piloted, then preparation through roadmap-close succeeds end to end from a clean exact checkout.

## Functional Requirements
- FR-001: Parse a closed Roadmap v2 catalog and select exactly one next unchecked unit with an accepted immediate prerequisite; reject missing, duplicate, unknown, misordered, or already accepted identities. (Stories: US-001; Acceptance: AC-001)
- FR-002: Derive the authority pin, unit registration, ordered gate registrations, and expected evidence obligations as canonical typed values whose unit identity exactly matches the selected catalog row. (Stories: US-001; Acceptance: AC-002)
- FR-003: Reuse #3208 canonical lifecycle/review validation and #3209 qualification result validation, binding every input to the selected unit, exact subject revision, role, actor, digest, and declared obligation. (Stories: US-001; Acceptance: AC-003)
- FR-004: Derive SDD task and evidence state exclusively from observed analyze/verify/ship execution receipts and reject authored, synthetic, stale, incomplete, or mismatched evidence. (Stories: US-001; Acceptance: AC-004)
- FR-005: Validate distinct implementation candidate/merge, acceptance candidate/merge, and protected-main identities plus required ancestry/equality relations without collapsing phase-specific authority. (Stories: US-001; Acceptance: AC-005)
- FR-006: Render and seal the acceptance receipt and evidence index as one canonical transaction whose cross-digests prevent partial or substituted acceptance. (Stories: US-001; Acceptance: AC-006)
- FR-007: Make inspect/render/verify and staged preparation replay byte-identical and idempotent; use #3105's staged-intake transaction for registration mutations and introduce no new GitHub write implementation. (Stories: US-001; Acceptance: AC-007)
- FR-008: Produce an accepted receipt directly consumable by the existing `roadmap close` typed input boundary. (Stories: US-001; Acceptance: AC-008)
- FR-009: Provide positive and independently expressed inverted fixtures for every selection, identity, evidence, replay, transaction, sealing, and handoff rule. (Stories: US-001; Acceptance: AC-009)
- FR-010: Publish and receiver-verify the coherent coordination package before migrating `work-roadmap`, then record a clean-checkout end-to-end pilot on one later GS2 unit and phase time/token comparison with GS2-07.2. (Stories: US-001; Acceptance: AC-010)

## Ambiguities
No material ambiguities recorded.

## Public Or Tool-Facing Impact
- This specification is an SDD lifecycle artifact and command-report contract input.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd clarify --work 3210-roadmap-work-unit-compiler`.
