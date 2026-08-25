---
schemaVersion: 1
workId: 2968-new-sdd-workspace-0-10-1-release
title: Release FS.GG.NewSddWorkspace 0.10.1
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

# Release FS.GG.NewSddWorkspace 0.10.1 Charter

## Identity
- Work id: `2968-new-sdd-workspace-0-10-1-release`
- Lifecycle stage: charter
- Status: chartered

## Principles
- Release only the already-merged package source at immutable commit `264725f374e3f05da46d7c3089462076a1f9bf7a`.
- Treat tag identity, one prepared `.nupkg`, GitHub Packages, nuget.org, nuspec repository metadata, and a clean tool installation as one evidence chain.
- Separate preparation from authority: this branch may prove readiness, but only the host may merge, tag, dispatch, publish, or reconcile the registry.
- Never repack between feeds or infer publication from a successful workflow alone.

## Scope Boundaries
- In: specify and verify the independent `FS.GG.NewSddWorkspace` 0.10.1 release from the authoritative merged source; exercise the existing self-test/build/pack and workflow preflight; define exact post-merge publication and verification obligations.
- Out: the concurrent `.github` coherent-set release, changes to shared version scalars, Coord.Cli notes, distributed pins, registry/docs reconciliation in this preparation PR, any new scaffolder behavior, and any release action before fresh review and host acceptance.

## Policy Pointers
- SDD policy comes from `.fsgg/sdd.yml` and `.fsgg/agents.yml`.
- Governance files are optional compatibility pointers and are not evaluated by this command.
- ADR-0012, ADR-0013, ADR-0016, `publishing-and-deployment`, and `.github/workflows/release-new-sdd-workspace.yml` own the distribution rules.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd specify --work 2968-new-sdd-workspace-0-10-1-release`.
- Tier 1: a public stable dotnet-tool version and its immutable release coordinates are receiver-facing contracts.
- Post-publication registry/changelog/compatibility reconciliation is a separately sequenced continuation after `.github#2941`, not part of this branch's touch-set.
