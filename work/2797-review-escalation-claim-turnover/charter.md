---
schemaVersion: 1
workId: 2797-review-escalation-claim-turnover
title: Preserve escalation authority across exhausted-claim turnover
stage: charter
changeTier: tier1
status: chartered
policyPointers:
  - .fsgg/sdd.yml
  - .fsgg/agents.yml
  - .fsgg/policy.yml
  - .fsgg/capabilities.yml
  - .fsgg/tooling.yml
---

# Preserve escalation authority across exhausted-claim turnover Charter

## Identity
- Work id: `2797-review-escalation-claim-turnover`
- Lifecycle stage: charter
- Status: chartered

## Principles
- Preserve the ordinary three-round review ceiling across claim turnover; repair phase is a distinct bounded authority, never round four.
- Derive escalation authority from the exhausted review ledger, completed durable wait, legacy exhaustion evidence, and one fresh current claimant.
- Refuse every malformed, stale, ambiguous, or replayed authority shape before any GitHub write.
- Keep the review projection and production writer on one typed contract, with fail-before and pass-after mutation witnesses.

## Scope Boundaries
- In: structured review escalation after a completed confirmation-round-3 wait when the ordinary claim has been released and a fresh repair-phase claim is current.
- In: exact item, PR, head, round, digest, wait-chain, exhaustion-evidence, claimant-freshness, and duplicate-escalation validation.
- In: pure projection and production-writer coverage for the changed-claim route, plus the coherent engine release obligation.
- Out: granting the replacement claim authority to append confirmation, pass, or acceptance on the exhausted PR; changing either review ceiling; rewriting consumer history.

## Policy Pointers
- SDD policy comes from `.fsgg/sdd.yml` and `.fsgg/agents.yml`.
- Governance files are optional compatibility pointers and are not evaluated by this command.

## Lifecycle Notes
- Governing board item: `FS-GG/.github#2797`.
- Canonical specification: `work/2797-review-escalation-claim-turnover/spec.md`.
- The live S.I.R. chain is reproduction evidence; producer behavior must reconcile it mechanically after release.
- Next lifecycle action: `fsgg-sdd specify --work 2797-review-escalation-claim-turnover`.
