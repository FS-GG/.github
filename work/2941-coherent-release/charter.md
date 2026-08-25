---
schemaVersion: 1
workId: 2941-coherent-release
title: Coherent coordination release 0.75.4
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

# Coherent coordination release 0.75.4 Charter

## Identity
- Work id: `2941-coherent-release`
- Lifecycle stage: charter
- Status: chartered

## Principles
- Publish one coherent stable version from one reviewed merge commit; no member may move alone.
- The repository release workflows own packaging and publication. Never publish a locally repacked or
  unreviewed artifact, and never repack between GitHub Packages and nuget.org.
- Treat immutable tags, both served feeds, install/restore behavior, the registry, and generated human
  projections as one release fact that must agree before the release is complete.
- Keep the release diff narrowly declarative: version sources, canonical pin, registry/changelog,
  compatibility projection, and architecture reconciliation only.

## Scope Boundaries
- Advance `FS.GG.Coord.Cli`, `FS.GG.Kit`, and `FS.GG.Drivers` together from published `0.75.3` to the
  next unused stable version selected by live tag/feed audit. This is a forward-only repair: immutable
  `0.75.3` shipped the intended engine bytes with materially incomplete release notes and must not be
  repacked or retagged.
- Use the existing repository release workflow and its sibling-tag/dual-feed gates; changing release
  workflow implementation is outside this item's declared touch-set.
- Publish only after the reviewed release-preparation PR is accepted and merged; publication is a
  declared post-merge obligation, not evidence inferred from a local pack.
- Reconcile `registry/dependencies.yml`, `registry/CHANGELOG.md`,
  `docs/registry/compatibility.md`, `docs/architecture.md`, and the canonical distributed tool pin.
- Verify both feeds serve the same package contents modulo NuGet signatures, all sibling tags resolve
  to one commit, public install/restore succeeds, and engine freshness reports `releaseOwed=false`.
- No other product-family release, feature implementation, or general Coordination-board work is in
  scope.

## Policy Pointers
- SDD policy comes from `.fsgg/sdd.yml` and `.fsgg/agents.yml`.
- Governance files are optional compatibility pointers and are not evaluated by this command.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd specify --work 2941-coherent-release`.
