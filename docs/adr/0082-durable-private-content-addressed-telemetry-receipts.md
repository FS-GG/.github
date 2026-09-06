# ADR-0082: Durable private content-addressed telemetry receipts

Status: Accepted  
Date: 2026-09-06  
Decision owners: FS-GG/.github#3259  
Supersedes no prior ADR; completes the retention boundary left open by FS-GG/.github#3199.

## Context

Lifecycle events publish aggregate token counts and a SHA-256 reference to a private runtime-usage CSV.
The supervisor contract introduced by #3199 made post-response collection accountable, but it left the
receipt bytes in caller-selected, often temporary storage. FS-GG/FS.GG.Coordination#304 retained the public
digest after both the CSV and source Codex session disappeared. Strict validation correctly refused the
unverifiable measured event, but there was no provenance-preserving recovery state.

## Decision

Frozen usage CSVs are immutable host evidence. Collection must archive them in a per-user state directory,
addressed by their SHA-256 digest as `sha256/<first-two>/<digest>.csv`. The configured/default root may not
be a system temporary directory or repository worktree. Directories and files are owner-only; publication
uses a unique write-through sibling and an atomic no-overwrite move. Existing targets are accepted only
when byte-identical, and every read recomputes the digest. Explicit receipt paths remain compatible, then
lifecycle sealing and validation resolve any remaining cited digest from canonical storage.

Already-missing historical receipts are not reconstructed. The only migration is a closed
`fsgg.telemetry.legacy-receipt-proof/v1` document binding the original lifecycle digest, missing receipt
digest, canonical issue/comment, exhaustive lookup evidence, distinct author and reviewer identities,
review evidence, and `irrecoverable-exclude-usage`. A later append-only lifecycle event must cite the proof
digest. The historical public counts remain visible as history but are excluded from all usage roll-ups and
are never reclassified as measured evidence.

## Consequences

- Ordinary worker/worktree and runtime-session cleanup no longer removes the evidence a later handoff needs.
- Digest collision, store corruption, unsafe placement, and caller reconstruction remain fail-closed.
- Recovery is explicit, independently reviewed, and generic; there is no per-unit waiver.
- Private receipt bytes remain outside git and public GitHub artifacts. Operators must include the private
  state directory in host backup/retention policy when audit survival across host loss is required.

## Compatibility and rollout

This is an additive minor release. Existing `--usage` arguments continue to work. Producers publish the
0.84.0 coherent set before skills or downstream operators rely on automatic resolution or legacy proofs.
Both configured skill roots carry the same storage/recovery contract.
