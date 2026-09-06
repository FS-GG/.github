---
schemaVersion: 1
workId: 3259-durable-private-telemetry-receipts
title: Durable Private Telemetry Receipts
stage: specify
changeTier: tier1
status: specified
publicOrToolFacingImpact: true
---

# Durable Private Telemetry Receipts Specification

Prose status: specified

## User Value
lifecycle evidence remains privately auditable after worker and temporary-session cleanup

## Scope
- SB-001: coordination telemetry collector, lifecycle sealer/validator, roadmap roll-up, generated pnext-item and work-roadmap skill projections, compatibility registry, and coherent package release

## Non-Goals
- SB-002: Do not recover, estimate, or trust token counts from public lifecycle JSON, prose, aggregate totals, or caller-authored replacement CSVs.
- SB-003: Do not commit private receipt bytes, upload them to public GitHub artifacts, or add a GS2-07.3-specific waiver.
- SB-004: Do not alter or merge FS-GG/FS.GG.Coordination#307 and do not inspect GS2-07.4.

## User Stories
- US-001 (P1): As a user, I can lifecycle evidence remains privately auditable after worker and temporary-session cleanup.

## Acceptance Scenarios
- AC-001 [US-001] [FR-001]: Given a valid frozen usage CSV, when the collector completes, then a byte-identical copy exists at the canonical digest-derived private path with owner-only permissions and resolves without another caller-supplied path.
- AC-002 [US-001] [FR-002]: Given an unsafe temporary root, a mismatched source digest, an occupied digest path with different bytes, a corrupted canonical copy, or a reconstructed count-only input, when storage or validation runs, then it fails before accepting lifecycle evidence.
- AC-003 [US-001] [FR-003]: Given a historical lifecycle event whose cited private receipt is irrecoverable, when recovery is requested, then only a typed proof independently reviewed by a distinct minted identity can classify the receipt as irrecoverable, bind the event and receipt digests, and exclude the event's counts from every aggregate.
- AC-004 [US-001] [FR-004]: Given the changed contract and package set, when release gates run against the merged source, then source tests, black-box parity, skill projections, registry compatibility, published artifacts, and downstream installation all verify the same coherent version.

## Functional Requirements
- FR-001: Archive every frozen measured receipt atomically under a canonical per-user content-addressed path with private permissions and resolve it by digest. (Stories: US-001; Acceptance: AC-001)
- FR-002: Reject digest collisions, corrupted stored receipts, temporary-only store roots, and caller-reconstructed counts before accepting lifecycle evidence. (Stories: US-001; Acceptance: AC-002)
- FR-003: Permit an already-missing receipt to advance only through a typed separately reviewed irrecoverability proof bound to the original lifecycle event and receipt digest, and exclude its counts from roll-up. (Stories: US-001; Acceptance: AC-003)
- FR-004: Require focused Core and CLI tests, black-box telemetry parity, skill-quality checks, mutation evidence, full policy gates, exact-head review, and public coherent-release verification. (Stories: US-001; Acceptance: AC-004)

## Ambiguities
No material ambiguities recorded.

## Public Or Tool-Facing Impact
- This specification is an SDD lifecycle artifact and command-report contract input.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd clarify --work 3259-durable-private-telemetry-receipts`.
