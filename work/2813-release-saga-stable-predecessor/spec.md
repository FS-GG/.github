---
schemaVersion: 1
workId: 2813-release-saga-stable-predecessor
title: Live stable predecessor authority and forward recovery
stage: specify
changeTier: tier1
status: specified
publicOrToolFacingImpact: true
---

# Live stable predecessor authority and forward recovery Specification

Prose status: specified

## User Value
Release operators can prepare a coherent set only against the live promoted stable-channel predecessor and recover a poisoned published version without changing its identity.

## Scope
- SB-001: Bind preparation and retry validation to the exact stable-channel receipt; add fail-closed controls, docs, tests, mutation evidence, and prepare source for a new unused forward-recovery version while preserving 0.70.0.
- SB-002: Advance the coherent source scalar to unused stable version `0.71.0`, with release notes that identify
  `0.70.0` as poisoned and `0.69.0` as the only promoted predecessor. Publication remains a post-merge obligation.

## Non-Goals
- SB-003: Do not mutate, replace, delete, or promote any `coherent-set/v0.70.0` package, tag, manifest,
  journal, draft asset, or release metadata.
- SB-004: Do not publish or promote `0.71.0` before this source change is independently reviewed and merged.

## User Stories
- US-001 (P1): As a release operator, I can prepare a coherent set only against the live promoted
  stable-channel predecessor, independent of a lagging registry projection.
- US-002 (P1): As a release operator resuming a failed preparation, I can reuse a draft only when its
  predecessor version and content identity still match the live stable channel.
- US-003 (P1): As a package consumer, I receive recovery under a new version while the poisoned `0.70.0`
  identity remains immutable and unpromoted.

## Acceptance Scenarios
- AC-001 [US-001] [FR-001]: Given registry `package-version: 0.68.0` and the latest published
  coherent-set receipt `0.69.0`, when preparation resolves its predecessor, then it selects and binds
  the receipt's version `0.69.0` and content ID before any build or pack step.
- AC-002 [US-001] [FR-001]: Given a missing, unreadable, prerelease, malformed, tag/receipt-contradictory,
  or source/receipt-contradictory live channel, when preparation starts, then it refuses before packing,
  drafting, tagging, or publishing.
- AC-003 [US-002] [FR-002]: Given an existing draft, when preparation retries, then equivalent package
  payloads are reusable only if stored and candidate descriptors bind the same predecessor version and content ID.
- AC-004 [US-003] [FR-003]: Given published packages and immutable tags for `0.70.0`, when recovery is
  prepared, then every `0.70.0` remote asset and identity remains unchanged and unpromoted, while source
  advances to unused `0.71.0` against promoted `0.69.0`.
- AC-005 [US-001] [US-002] [US-003] [FR-003]: Given the hermetic saga fixture, when its predecessor
  authority or retry-identity guard is inverted, then the focused suite fails on the reproduced stale-registry escape.

## Functional Requirements
- FR-001: Before any pack, draft, tag, or publication write, preparation MUST read the latest published coherent-set release's `stable-channel.json`; validate stable SemVer, content ID, exact source SHA, coherent tag/version agreement, and tag/source agreement; bind both predecessor version and content ID into the candidate manifest; and never use registry metadata to select or validate this identity. (covers AC-001, AC-002)
- FR-002: Retry MUST accept an existing draft only when stored and candidate descriptors bind the same predecessor version and content ID, in addition to the existing release, source, policy, channel, package, and payload identity checks. (covers AC-003)
- FR-003: Tests MUST reproduce registry `0.68.0` versus stable receipt `0.69.0`, stale/malformed/missing receipts, poisoned `0.70.0` preservation, retry, and successful forward promotion under `0.71.0`; an inverted authority gate MUST fail. (covers AC-004, AC-005)
- FR-004: Documentation MUST declare live stable receipt > prepared manifest > registry projection authority and specify that poisoned versions remain unpromoted while recovery advances to a new unused coherent version. (covers AC-004)

## Ambiguities
No material ambiguities recorded.

## Public Or Tool-Facing Impact
- The workflow boundary, manifest descriptor identity, coherent package version, release notes, and operator
  recovery procedure are public/tool-facing release contracts. The descriptor change is additive within
  `fsgg.release-saga/1`; manifests without the new field remain readable but cannot be newly prepared or reused.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd clarify --work 2813-release-saga-stable-predecessor`.
