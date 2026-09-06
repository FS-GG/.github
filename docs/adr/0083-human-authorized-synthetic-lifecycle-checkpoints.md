# ADR-0083: Human-authorized synthetic lifecycle checkpoints

- **Status:** Accepted
- **Date:** 2026-09-06
- **Decision owners:** accountable user authorization; FS-GG/.github#3263
- **Amends:** ADR-0082 only for extraordinary histories that its ordinary retention/recovery mechanisms cannot repair
- **Affects:** lifecycle telemetry proof API, CLI, validation, worker and roadmap protocols

## Context

Append-only evidence preserves what actually happened, including mistakes and artifacts that later tool
versions cannot interpret. FS-GG/FS.GG.Coordination#304 contains both an unavailable historical private
receipt and a later immutable reconciliation terminal whose supersession evidence was written as prose.
Strict 0.84.0 validation correctly refuses it, but editing the history would destroy its audit value and a
shape-specific validator exception would require another release for every future extraordinary case.

## Decision

The toolkit exposes one generic, explicit extraordinary boundary:
`fsgg.telemetry.synthetic-checkpoint/v1`. Its canonical digest binds the repository, issue, run, unit,
exact frontier revision and digest, a closed reason, an immutable human issue-comment authorization,
literal declarations that missing provenance is not required and missing data will not be reconstructed,
and a non-empty set of passed functional checks with immutable evidence.

One adjacent `synthetic-evidence-checkpoint` phase begins immediately after that frontier. Its started
event solely consumes the proof digest; its completed event is the new trusted anchor. Canonical event
shape, identity, ordering, transition, timestamp, and digest-chain validation still cover the complete
history. Evidence and reconciliation findings at or before the authorized frontier are not reinterpreted;
they are openly replaced at this one boundary. Every event after the anchor returns to the unchanged
strict evidence and reconciliation contract.

The checkpoint is never inferred from worker authority. Missing authorization, scope/frontier mismatch,
proof reuse, multiple proofs or checkpoint phases, digest tampering, or absent/failing functional
verification is a refusal.

## Consequences

- Immutable malformed history remains visible and byte-for-byte auditable.
- Missing receipts, malformed reconciliation, tool-version incompatibility, and other extraordinary cases
  share one bounded mechanism instead of accumulating validator exceptions.
- The synthetic proof does not establish historical token counts or missing provenance; its explicit flags
  say precisely that.
- Normal lifecycle work resumes from a named trusted anchor and stays strict.

## Compatibility and rollout

This is an additive minor release. Histories without `--synthetic-checkpoint` behave exactly as before.
Older consumers reject the new input fail-closed. Publish the producer coherent set first, reconcile the
registry, then let the blocked consumer append its scope-bound checkpoint.
