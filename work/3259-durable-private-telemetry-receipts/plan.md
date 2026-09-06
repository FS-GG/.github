---
schemaVersion: 1
workId: 3259-durable-private-telemetry-receipts
title: Durable Private Telemetry Receipts
stage: plan
changeTier: tier1
status: planned
sourceSpec: work/3259-durable-private-telemetry-receipts/spec.md
sourceClarifications: work/3259-durable-private-telemetry-receipts/clarifications.md
sourceChecklist: work/3259-durable-private-telemetry-receipts/checklist.md
publicOrToolFacingImpact: true
---

# Durable Private Telemetry Receipts Plan

Prose status: planned

## Source Snapshot
- spec: work/3259-durable-private-telemetry-receipts/spec.md sha256:7aeded40e242b959581357183bb8240ee0788ce2e861277afa67869ad9d01fa8 schemaVersion:1
- clarifications: work/3259-durable-private-telemetry-receipts/clarifications.md sha256:f6dc4ec7b183378016575194e7e94b948496d4d1252582443ce9250b2fbc06e9 schemaVersion:1
- checklist: work/3259-durable-private-telemetry-receipts/checklist.md sha256:570c7f1a013a63bfd780886a92d35659cb93627f66013ac0a4aa074dd55b1aaf schemaVersion:1

## Plan Scope
- Work item 3259-durable-private-telemetry-receipts is planned from the current specification, clarification, and checklist facts.
- Requirement count: 4.
- Clarification decision count: 0.
- Checklist result count: 4.

## Plan Decisions
- PD-001 [AC-001] [FR-001] complete: Add a Core receipt-store abstraction that validates CSV bytes, derives the SHA-256 address, enforces a non-temporary/non-repository host-state root, writes owner-only files atomically without overwrite, and verifies every resolved copy.
- PD-002 [AC-002] [FR-002] complete: Make collection archive its emitted report and add typed `telemetry usage archive|resolve` diagnostics; lifecycle commands resolve missing explicit receipts from the store and refuse unsafe roots, mismatched digests, corruption, collision, and non-CSV reconstruction.
- PD-003 [AC-003] [FR-003] complete: Add a closed legacy-proof codec and validator. A proof requires distinct author/reviewer identities, immutable review evidence, exact original event and missing receipt digests, canonical issue/comment binding, exhaustive lookup evidence, and the sole decision `irrecoverable-exclude-usage`; reconciliation also requires a later lifecycle event citing the proof digest.
- PD-004 [AC-004] [FR-004] complete: Update both generated skill roots, CLI and compatibility documentation, registry versions/change logs, focused unit and black-box tests, semantic skill fixtures, and coherent publication evidence.

## Contract Impact
- PC-001 [PD-001] [PD-002] command: `telemetry usage collect` gains durable archive semantics; new `archive` and `resolve` verbs expose deterministic receipt-store behavior. Existing `--usage` inputs remain accepted.
- PC-002 [PD-002] command: `telemetry lifecycle validate|seal-successor` and `telemetry summarize` gain `--receipt-store` resolution and repeatable `--legacy-proof`; default resolution is additive but failures remain fail-closed.
- PC-003 [PD-003] schema: `fsgg.telemetry.legacy-receipt-proof/v1` is a new closed migration proof. It cannot carry token counts, cannot create a usage receipt, and produces an excluded-gap classification only.
- PC-004 [PD-004] package: the coordination CLI/Core/Kit and driver skill projections form one coherent minor release before downstream use.

## Verification Obligations
- VO-001 [PD-001] [PC-001] semanticTest: Unit-test deterministic addressing, default/configured roots, private modes, idempotent archive, collision/corruption refusal, unsafe roots, and cross-process atomic behavior.
- VO-002 [PD-002] [PC-001] [PC-002] blackBox: Run CLI tests and telemetry parity proving collection archives and seal/validate resolve without caller paths; invert canonical bytes and root safety to observe red.
- VO-003 [PD-003] [PC-003] semanticTest: Replay the #304 missing digest with absent storage; reject unreviewed/same-actor/count-bearing/stale-authority proofs and accept only a recovery-event-bound excluded gap. Prove summarization excludes its counts.
- VO-004 [PD-004] [PC-004] integration: Run skill-quality, full policy runner, package/API compatibility, exact-head independent critique, guarded merge, coherent publication, public download/install, registry reconciliation, and downstream resolution replay.

## Performance Intent
No performance intent is declared for this work item.

## Migration Posture
- PM-001 [PC-001] backwardCompatible: Explicit `--usage` callers continue to work, while newly collected receipts are durably archived and digest references resolve automatically.
- PM-002 [PC-003] reviewedLegacyBoundary: Historical missing receipts are never reconstructed. A separately reviewed proof changes only their aggregation disposition to excluded and remains visibly bound in the append-only lifecycle.

## Generated View Impact
- GV-001 [PD-001] workModel: `fsgg-sdd tasks`, `analyze`, `agents`, and `refresh` regenerate the work model, readiness views, and configured guidance from authored SDD sources; generated output is never edited to assert readiness.

## Accepted Deferrals
No accepted plan deferrals recorded.

## Planning Findings
No blocking planning findings recorded.

## Advisory Notes
- Optional Governance pointers remain compatibility facts only.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd tasks --work 3259-durable-private-telemetry-receipts`.
