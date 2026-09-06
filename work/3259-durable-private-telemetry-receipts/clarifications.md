---
schemaVersion: 1
workId: 3259-durable-private-telemetry-receipts
title: Durable Private Telemetry Receipts
stage: clarify
changeTier: tier1
status: clarified
sourceSpec: work/3259-durable-private-telemetry-receipts/spec.md
publicOrToolFacingImpact: true
---

# Durable Private Telemetry Receipts Clarifications

## Source Specification
- work/3259-durable-private-telemetry-receipts/spec.md

## Clarification Questions
- Q-001: Where is the canonical durable private receipt store and how is its root selected?
- Q-002: What makes writes collision-safe and later reads tamper-evident?
- Q-003: How do existing lifecycle commands locate receipts without weakening explicit `--usage` compatibility?
- Q-004: What can advance an already-missing historical receipt without trusting its public counts?
- Q-005: What does final roll-up do with an adjudicated legacy gap?

## Answers
- A-001 [Q-001]: Use `$FSGG_USAGE_RECEIPT_STORE` when explicitly configured, otherwise `$XDG_STATE_HOME/fsgg/telemetry/usage`, falling back to the platform local-application-data directory. Refuse roots resolving within a system temporary directory or repository worktree. The store is per-user, untracked, and survives ordinary worker/worktree/session cleanup.
- A-002 [Q-002]: Address immutable CSVs as `sha256/<first-two-hex>/<64-hex>.csv`. Create directories and files owner-only, write a unique sibling temporary file, fsync/close, then atomically create/move the digest target without overwrite. An existing byte-identical target is idempotent; different or unreadable bytes are a refusal. Every resolve recomputes SHA-256.
- A-003 [Q-003]: Collection archives the completed report automatically and reports its canonical digest source. `seal-successor`, `validate`, and summarize/roll-up first use explicit `--usage` inputs, then resolve every still-needed `runtime-usage-csv:sha256:<digest>` from the canonical store. Explicit inputs remain supported and must be byte/digest coherent.
- A-004 [Q-004]: A closed `fsgg.telemetry.legacy-receipt-proof/v1` document binds the original lifecycle event digest, missing receipt digest, canonical issue/comment authority, documented exhaustive lookup, proof author, distinct reviewer, review evidence, and decision `irrecoverable-exclude-usage`. The validator accepts it only for a receipt absent from both explicit inputs and the canonical store and only when a later lifecycle recovery event cites the proof digest. It never materializes usage CSV bytes or validates the historical counts.
- A-005 [Q-005]: Validation can classify the chain structurally complete with an adjudicated legacy gap, while usage summarization excludes the original event entirely and reports the excluded digest/count of gaps. This is a migration state, not measured usage and not a reusable unit waiver.

## Decisions
- D-001 [Q-001]: Canonical private storage is durable host state, never `/tmp`, a repository path, or a public artifact.
- D-002 [Q-002]: Content equality and digest verification are required on both idempotent writes and every read; no overwrite operation exists.
- D-003 [Q-003]: Automatic canonical resolution is additive compatibility. Existing explicit `--usage` files keep precedence but cannot override a digest mismatch.
- D-004 [Q-004]: Legacy proof semantics downgrade unverifiable historical usage to explicitly excluded, independently reviewed provenance; they never upgrade copied public counts to measured data.
- D-005 [Q-005]: One proof digest may adjudicate one missing receipt digest once, and the lifecycle must visibly cite it before terminal reconciliation succeeds.

## Accepted Deferrals
No accepted deferrals recorded.

## Remaining Ambiguity
No blocking ambiguity remains.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd checklist --work 3259-durable-private-telemetry-receipts`.
