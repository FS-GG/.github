---
schemaVersion: 1
workId: 2527-post-acceptance-head-move
title: "review protocol: a PR whose head moves after its chain was accepted"
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

# review protocol: a PR whose head moves after its chain was accepted Charter

## Identity
- Work id: `2527-post-acceptance-head-move`
- Lifecycle stage: charter
- Status: chartered

## Principles
- **Durable review evidence is append-only.** A recovery mechanism may retire a chain from the
  protocol's reading of a PR; it may never edit, quote-inert, or delete another critic's marker. What a
  reader could verify before the recovery must still be verifiable after it.
- **The one-initial-marker rule is load-bearing, not incidental.** It is what stops a stranger silently
  continuing another critic's chain. Any mechanism that admits a second chain onto a PR must be shown
  not to become a laundering route for exactly that, and the controlled counterpart — two competing
  initial markers with no intervening accepted-then-moved head — must still fail closed.
- **Prefer evidence the engine can already observe over an out-of-band grant.** `RepairPhaseReceipt` and
  `CriticSuccessionReceipt` are grants because the facts they carry (a host's decision, a critic's
  unavailability) are genuinely unobservable from the PR. "This chain was accepted at a head that is no
  longer current" is not: it is written in the acceptance marker's own required fields. A grant here
  would move an observable fact into an assertable one, which is the laundering risk.
- **No second marker parser** (`.github#2175` acceptance 11). Whatever this change reads, it reads
  through `Driver`'s existing marker classification and field grammar.
- **Fail closed on anything unrecognised.** A shape this mechanism does not positively recognise must
  land on the pre-existing refusal, not on a permissive default.

## Scope Boundaries
- In scope: the protocol contract text (`independent-review.md`), the pure decision layer
  (`Review.fs`), the marker-classification support it needs (`Driver.fs`), the CLI's rendering of the
  new fact, and an executable fixture.
- Out of scope: changing `Protocol.reviewPolicy`'s marker vocabulary (no new marker kind is introduced,
  so no generated projection region changes); the repair phase, critic succession, and the round
  ceilings, all of which keep their existing meanings; any change to how a head is *moved*.
- Keep SDD lifecycle ownership separate from optional Governance enforcement.

## Policy Pointers
- SDD policy comes from `.fsgg/sdd.yml` and `.fsgg/agents.yml`.
- Governance files are optional compatibility pointers and are not evaluated by this command.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd specify --work 2527-post-acceptance-head-move`.
