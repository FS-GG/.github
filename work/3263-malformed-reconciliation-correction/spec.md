---
schemaVersion: 1
workId: 3263-malformed-reconciliation-correction
title: Human-authorized synthetic lifecycle checkpoint
stage: specify
changeTier: tier1
status: specified
publicOrToolFacingImpact: true
---

# Human-authorized synthetic lifecycle checkpoint Specification

Prose status: specified

## User Value
Unblock extraordinary append-only lifecycle histories with one explicit, auditable checkpoint and resume normal strict work from a new trusted anchor.

## Scope
- SB-001: Lifecycle proof schema, validation, CLI, projected skills, tests, ADR, coherent release, and registry reconciliation.

## Non-Goals
- SB-002: Do not edit, delete, reorder, or reconstruct historical lifecycle events, private receipts, counts, or provenance.
- SB-003: Do not make synthetic checkpoints an automatic or ordinary recovery path.

## User Stories
- US-001 (P1): As the accountable human, I can explicitly authorize a scope-bound synthetic evidence checkpoint when an extraordinary immutable history cannot satisfy the current tool contract.
- US-002 (P1): As a later worker, I can trust the checkpoint as a new anchor and use the unchanged strict lifecycle contract after it.

## Acceptance Scenarios
- AC-001 [US-001] [FR-001] [FR-002] [FR-003]: Given an exact blocked frontier and human authorization, when a single matching checkpoint is appended immediately after it, then validation accepts the checkpoint without requiring provenance for the missing evidence or reconstructing it.
- AC-002 [US-001] [FR-004] [FR-005]: Given missing, tampered, ambiguous, reused, wrong-scope, wrong-frontier, or functionally unverified authorization, validation fails closed.
- AC-003 [US-002] [FR-006]: Given a valid checkpoint, when later ordinary events are appended, then every later event validates under the unchanged strict contract.
- AC-004 [US-002] [FR-007]: Given the producer release, when a fresh consumer installs it, then the current #304 history can append a checkpoint and resume without another shape-specific release.

## Functional Requirements
- FR-001: A `fsgg.telemetry.synthetic-checkpoint/v1` proof binds canonical item, run id, unit id, exact frontier revision and digest, reason category, immutable human authorization URL, `missing_provenance_required:false`, and `reconstruct_missing_data:false`. (Stories: US-001; Acceptance: AC-001)
- FR-002: The proof carries at least one functional verification result and every result is `passed` with immutable GitHub or content-addressed evidence. (Stories: US-001; Acceptance: AC-001)
- FR-003: Exactly one immediately following completed `synthetic-evidence-checkpoint` event consumes exactly one proof digest and becomes the new trusted lifecycle anchor. (Stories: US-001; Acceptance: AC-001)
- FR-004: Validation rejects absent authorization, wrong item/run/unit/frontier, reuse, multiple proofs or checkpoints, digest tampering, and absent or failing functional verification. (Stories: US-001; Acceptance: AC-002)
- FR-005: Digest-chain integrity and canonical event structure are never bypassed; only pre-checkpoint evidence and reconciliation findings covered by the exact authorization are replaced. (Stories: US-001; Acceptance: AC-002)
- FR-006: Strict ordinary evidence and reconciliation validation applies unchanged after the checkpoint and to all histories without one. (Stories: US-002; Acceptance: AC-003)
- FR-007: The generic reason vocabulary covers missing private evidence, malformed reconciliation, tool-version incompatibility, and extraordinary other circumstances; publish and reconcile a coherent-set version before adoption. (Stories: US-002; Acceptance: AC-004)

## Ambiguities
No material ambiguities recorded; the accountable user selected the explicit synthetic checkpoint design and authorized missing provenance only at that checkpoint.

## Public Or Tool-Facing Impact
- Adds a versioned proof document, a CLI `--synthetic-checkpoint` input, a lifecycle validation output field, and generated worker guidance.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd clarify --work 3263-malformed-reconciliation-correction`.
