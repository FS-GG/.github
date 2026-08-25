---
schemaVersion: 1
workId: release-coord-engine-0-75-5
title: Coherent coordination release 0.75.5
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

# Coherent coordination release 0.75.5 Charter

## Identity
- Work id: `release-coord-engine-0-75-5`
- Lifecycle stage: charter
- Status: chartered

## Principles
- Publish one coherent stable version from one reviewed merge commit; no member may move alone.
- Pack once and publish the exact same package payloads to GitHub Packages first and nuget.org second.
- Treat immutable tags, served feeds, clean installation, registry authority, consumer pins, and
  generated projections as one release fact that must agree before completion.
- Keep the preparation diff declarative. Publication and feed-derived reconciliation remain explicit
  post-merge obligations rather than claims inferred from local archives.

## Scope Boundaries
- Advance `FS.GG.Coord.Cli`, `FS.GG.Kit`, and `FS.GG.Drivers` together from published `0.75.4` to
  stable `0.75.5`, carrying the post-merge verification classifier and full completion merge-OID fixes.
- Use the existing coherent-release saga, sibling tag namespaces, dual-feed order, and Trusted
  Publishing bindings without redesigning them.
- Reconcile source version/release notes before publication, then reconcile feed-facing registry,
  changelog, compatibility, architecture disposition, distributed tool pins, and skill projections
  from observed feed reality after publication.
- Verify byte-equivalent served payloads modulo feed-added signatures, isolated public installation,
  sibling-tag source equality, an immutable promoted release, and `releaseOwed=false`.
- Typed SDD P5 remains deferred until OperatingV2. No provider/receiver contract flip, new engine
  behavior, package identity, workflow topology, or other product-family release is in scope.

## Policy Pointers
- SDD policy comes from `.fsgg/sdd.yml` and `.fsgg/agents.yml`.
- Governance files are optional compatibility pointers and are not evaluated by this command.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd specify --work release-coord-engine-0-75-5`.
