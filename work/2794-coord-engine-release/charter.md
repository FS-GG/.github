---
schemaVersion: 1
workId: 2794-coord-engine-release
title: Coordination engine 0.68.0 coherent release
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

# Coordination engine 0.68.0 coherent release Charter

## Identity
- Work id: `2794-coord-engine-release`
- Lifecycle stage: charter
- Status: chartered

## Principles
- Release only merged `origin/main`; unmerged `.github#2772` behavior is explicitly excluded.
- Treat the coherent-set scalar, three package tags, prepared package bytes, two feeds, registry metadata,
  and downstream install as one release transaction whose evidence binds to one immutable source commit.
- Feed reads and isolated installs establish publication; a workflow conclusion alone does not.
- Preserve the existing release-saga recovery contract: never repack or blindly duplicate-push a version.

## Scope Boundaries
- In: re-measure the debt after `coord-engine/v0.67.0`; derive the next SemVer from shipped behavior;
  prepare and merge the coherent source version and release notes; publish the three coherent-set packages;
  verify byte-identical payloads on GitHub Packages and nuget.org; record registry/compatibility evidence;
  and prove a clean downstream install executes the cross-claim structured escalation route from `.github#2797`.
- Out: any source or behavior from unmerged `.github#2772`; redesign of release workflows; unrelated receiver
  upgrades; and changes to command/wire behavior beyond the already-merged engine commits being released.

## Policy Pointers
- SDD policy comes from `.fsgg/sdd.yml` and `.fsgg/agents.yml`.
- Governance files are optional compatibility pointers and are not evaluated by this command.
- `registry/CHANGELOG.md`, `docs/registry/compatibility.md`, ADR-0039, and the `release-saga/1`
  manifest are the release and distribution authorities.

## Lifecycle Notes
- Tier 1: the coherent version and package listing are receiver-facing distribution contracts.
- Post-merge publication remains owned by this claim and must complete before the Done stamp.
- Next lifecycle action: `fsgg-sdd specify --work 2794-coord-engine-release`.
