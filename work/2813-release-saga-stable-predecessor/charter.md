---
schemaVersion: 1
workId: 2813-release-saga-stable-predecessor
title: Release saga stable predecessor authority
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

# Release saga stable predecessor authority Charter

## Identity
- Work id: `2813-release-saga-stable-predecessor`
- Lifecycle stage: charter
- Status: chartered

## Principles
- Resolve predecessor identity from the live promoted stable-channel receipt, never from a lagging registry projection.
- Fail before packing, drafting, tagging, or publishing when that authority is absent, unreadable, unstable, or contradictory.
- Keep release identity immutable across retries: a stored draft is reusable only against the same authoritative predecessor identity.
- Preserve the published `0.70.0` packages, three tags, manifest, journals, and draft exactly as they exist; recovery is forward-only.

## Scope Boundaries
- In: preparation authority, manifest predecessor identity, retry validation, hermetic regression and mutation coverage,
  operator documentation, and source preparation for one new unused coherent version.
- Out: rewriting or promoting `coherent-set/v0.70.0`; deleting/replacing its assets or tags; blind duplicate
  publication; unrelated registry redesign; and any post-review merge, tag, feed publication, or promotion.

## Policy Pointers
- SDD policy comes from `.fsgg/sdd.yml` and `.fsgg/agents.yml`.
- Governance files are optional compatibility pointers and are not evaluated by this command.
- `docs/coordination/release-saga.md`, the `fsgg.release-saga/1` manifest, and the live
  `stable-channel.json` asset are the release authorities for this change.

## Lifecycle Notes
- Tier 1: this changes the irreversible release boundary and recovery contract for three public packages.
- The item-declared implementation paths remain untouched until this package reaches `implementationReady`.
- Next lifecycle action: `fsgg-sdd specify --work 2813-release-saga-stable-predecessor`.
