---
schemaVersion: 1
workId: 2819-pass-red-exhaustion-writer
title: Round-three pass/red repair-phase agreement
stage: specify
changeTier: tier1
status: specified
publicOrToolFacingImpact: true
---

# Round-three pass/red repair-phase agreement Specification

Prose status: specified

## User Value
Restore a bounded, typed recovery route when an immutable round-three pass is later invalidated by a settled red required check.

## Scope
- SB-001: Align snapshot ordinary-exhaustion projection and live escalation-writer admission across claim turnover for the exact terminal chain where confirmation round three passed and its required checks subsequently settled red.
- SB-002: Preserve the immutable round-three pass and completed wait as the terminal evidence sealing the one permitted repair-phase escalation.
- SB-003: Add pure Core, Lifecycle, live-writer, cross-claim, and shell-wire regressions for the accepted route and its refusal controls.

## Non-Goals
- SB-101: Do not edit or delete the immutable pass, append a duplicate round-three record, or permit ordinary confirmation round four.
- SB-102: Do not route pending checks into exhaustion or change green checks' eligibility for host acceptance.
- SB-103: Do not weaken exact-head, backlink, claim-generation, critic-succession, or repair-phase provenance checks.
- SB-104: Do not create a second repair phase or change unrelated delivery states.

## User Stories
- US-001 (P1): As an implementer recovering a terminal review chain, I can enter the one permitted repair phase when a settled red required check invalidates an immutable round-three pass.
- US-002 (P1): As a host or critic, I can trust that the projection and escalation writer accept exactly the same terminal evidence and still reject stale or unbounded mutations.

## Acceptance Scenarios
- AC-001 [US-001] [FR-001] [FR-002]: Given initial review and confirmations one/two require changes, confirmation three passes at the current head, its matching wait completes, a required check settles red, and the claim turns over, inspection returns ordinary exhaustion and the escalation writer accepts one repair-phase entry sealed to the immutable confirmation-three digest.
- AC-002 [US-002] [FR-003]: Given the same chain while checks are pending, inspection still awaits checks; given all required checks are green, inspection remains eligible for host acceptance.
- AC-003 [US-002] [FR-004]: Given a wrong head, backlink, predecessor digest, claim generation, or otherwise stale escalation draft, the writer refuses it without writing a repair-phase marker.
- AC-004 [US-002] [FR-005]: Given an attempted ordinary confirmation round four or second repair phase, the protocol refuses it.
- AC-005 [US-001] [FR-006]: Given pure snapshot, lifecycle, live-writer, cross-claim, and hosted-wire fixtures for the chain, every layer projects and authorizes the same bounded route.

## Functional Requirements
- FR-001: A completed ordinary round-three wait whose immutable verdict is pass MUST become ordinary exhaustion only when the exact-head required checks have settled red and claim turnover satisfies the existing recovery preconditions. (Stories: US-001; Acceptance: AC-001)
- FR-002: The repair-phase escalation writer MUST admit that same pass-then-red terminal chain and MUST seal the escalation to the immutable round-three record digest without editing or duplicating that record. (Stories: US-001; Acceptance: AC-001)
- FR-003: Pending checks MUST continue to produce await-checks, and green checks MUST continue to produce the existing host-acceptance-eligible route. (Stories: US-002; Acceptance: AC-002)
- FR-004: Exact-head, backlink, predecessor digest, claim-generation, and existing repair-provenance checks MUST remain fail-closed. (Stories: US-002; Acceptance: AC-003)
- FR-005: Ordinary confirmation round four and a second repair phase MUST remain impossible. (Stories: US-002; Acceptance: AC-004)
- FR-006: Regression coverage MUST exercise pure Core classification, Lifecycle application, live writer admission, cross-claim turnover, and the hosted review-critic succession wire, including inversion evidence that the accepted route fails when the production predicate is reverted. (Stories: US-001; Acceptance: AC-005)

## Ambiguities
- AMB-001: Whether the shared terminal-set predicate belongs in Core review facts or Lifecycle application code while remaining reusable by the live writer.
- AMB-002: Which existing check-state aggregate is the single authority for distinguishing pending, green, and settled red at escalation time.

## Public Or Tool-Facing Impact
- Changes the typed `review` projection and the authorization boundary for `review record` repair-phase escalation; no new marker schema or ordinary round is introduced.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd clarify --work 2819-pass-red-exhaustion-writer`.
