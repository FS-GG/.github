# ADR-0087: Single-owner approval for v1 admission genesis

- **Status:** Accepted
- **Date:** 2026-09-23
- **Decision owner:** FS-GG accountable programme owner
- **Affects:** `.github` protected authorization workflow and environment; `FS.GG.Coordination` v1 admission verifier
- **Applies:** [ADR-0079](0079-single-accountable-delivery-authority.md) to the one-time production v1 admission genesis

## Context

The prepared genesis workflow used the shared `fleet-cutover` environment. That environment prevents a run initiator from approving the run and lists two reviewers. The programme has one available human owner; the second-person condition is therefore unsatisfiable. The one-time genesis cannot borrow repository admin rights, a bot identity, or a chat instruction as its approval. Its Authority journal ref is still absent, and ordinary writes correctly remain fail-closed.

## Decision

Use a dedicated `fleet-v1-admission-owner` environment for this one-time genesis workflow. It names only the accountable owner (GitHub user ID `1645484`) as required reviewer, permits that owner to approve a run they initiated, requires a five-minute wait, and admits only the exact `main` branch. The workflow remains manual, first-attempt-only, read-only, SHA-256 pinned and short-lived. Native evidence must prove exactly one approval by that same owner, the exact environment ID and effective rules, the exact workflow and artifact bytes, and the bound source, intent and run. The trust-anchored signature, dedicated App writer scope, expected-absent ref creation and independent readback remain separate technical predicates.

This intentionally removes two-person separation for **this genesis only**. The shared `fleet-cutover` environment is not modified; later cutover transitions require their own explicit policy decision if a second reviewer remains unavailable. An absent approval, a changed environment rule, another actor, an expired receipt, or a missing credential still refuses.

## Consequences

The sole owner can complete a protected manual approval without inventing another person. The same owner can also initiate and approve the run, so the approval is an explicit human confirmation and audit event, not independent judgement. The five-minute wait, immutable workflow/source pins, cryptographic intent, scoped App and exact Git readback limit accidental drift but do not restore dual control. No ordinary production v1 write becomes available from this decision alone.

## Alternatives considered

- Keep `fleet-cutover` and wait for a second reviewer: impossible under the stated single-person staffing constraint.
- Disable self-review on the shared environment or use a bot dispatch to make the same owner appear independent: either hides the staffing reality or weakens unrelated cutover operations.
- Omit native approval entirely: loses the explicit, run-bound human confirmation and is rejected.
