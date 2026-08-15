---
schemaVersion: 1
workId: 2662-critic-succession-ledger
title: Critic Succession Ledger
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

# Critic Succession Ledger Charter

## Identity
- Work id: `2662-critic-succession-ledger`
- Coordination item: `FS-GG/.github#2662`
- Lifecycle stage: charter
- Status: chartered
- Delivery route: `sdd-required` (route-decision revision 2, digest `b863255efdb2ddc1e0261e3501373612e6e7cfd8e8e9b7d8186778ecc6baa24c`)

## Principles
- A recorded verdict must name the instance that actually reviewed. The one ledger shape the
  validator accepts today — `kind: confirmation` bearing the despawned critic's identity — is a false
  statement about authorship, so restoring a recordable verdict may never be reached by adopting it
  (constitution II: structured artifacts are the machine contract).
- Legibility is a property of the record, not of the reader's reconstruction. The host's
  acceptance-time and `landable`-time readers see only the comment ledger, so a succession that is
  not written into the record does not exist for them (constitution II, VIII).
- Continuity is the property being preserved, not the obstacle. An identity change with no valid,
  accountable grant must stay refused with the message it is refused with today; the repair widens
  exactly one admission and nothing else (constitution VIII: safe failure).
- Already-written evidence is immutable. Every digest already posted to a pull request must remain
  byte-identical under the new code, and an engine that predates the field must fail closed on a
  record that uses it rather than silently accept a weaker one (constitution III).
- A gate ships with evidence it can fail. The succession allowance is a gate that ADMITS, so its
  inversion evidence is a mutation that removes the allowance and a leg that reds.

## Scope Boundaries
- In scope: the `fsgg.coord.review-decision/v2` record shape, its digest, its wire codec, the ledger
  validator's critic-continuity rule, the derived "current generation critic" fact those consumers
  read, the fixtures that drive the ledger, and the two agent-skill mirrors plus the coordination doc
  that state the record shape.
- Out of scope: `Review.criticSuccessionValid` and the decision layer `.github#2417` already built —
  it is correct, head-bound, and this work must not weaken it; the grant's own durable on-PR marker
  (there is none today, and inventing one is a separate contract); and any change to the
  `fsgg-coord` claim, delivery, or landable surfaces.
- Keep SDD lifecycle ownership separate from optional Governance enforcement.

## Policy Pointers
- SDD policy comes from `.fsgg/sdd.yml` and `.fsgg/agents.yml`.
- Governance files are optional compatibility pointers and are not evaluated by this command.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd specify --work 2662-critic-succession-ledger`.
