---
schemaVersion: 1
workId: 2953-gh-modernization-m0-invariants
title: Ratify GitHub Substrate v2 authority and rollback boundaries
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

# Ratify GitHub Substrate v2 authority and rollback boundaries Charter

## Identity
- Work id: `2953-gh-modernization-m0-invariants`
- Lifecycle stage: charter
- Status: chartered

## Principles
- GitHub owns facts only where its identity, revision, completeness, and relation semantics satisfy the cutover invariants.
- FS-GG retains claim leases, touch-set exclusion, source-bound evidence, resumable mutation plans, semantic contract compatibility, and dual-feed recovery.
- V2 is a new-only authority in `FS.GG.Coordination`; v1 remains authoritative until the protected `OpenV2` transition and never resumes afterward.
- Qualification evidence must bind exact source and artifact fingerprints and include independently authored negative controls.

## Scope Boundaries
- In: finish the Typed SDD handoff; ratify authority, mutation, compatibility, and deletion boundaries; create the organization ADR and evidence-backed Q0 package; decide the runtime boundary; correct the Epic and dependency projection.
- In: classify P4/P5 and active adjacent work as `v2-unit`, `v2-blocker`, `parallel-product`, `candidate-input-change`, `superseded-inventory`, or `cutover-deferred`.
- Out: repository provisioning, v2 implementation, live schema/settings mutation, installing an App, freezing or switching the fleet, and any production writer change.
- Out: treating the Coordination Project, v1 completion records, or roadmap checkboxes as qualification authority.

## Policy Pointers
- SDD policy comes from `.fsgg/sdd.yml` and `.fsgg/agents.yml`.
- Governance files are optional compatibility pointers and are not evaluated by this command.
- The governing design is `docs/coordination/2026-08-25-github-substrate-v2-fleet-cutover-design.md`.
- The execution contract is `docs/github-substrate-v2-roadmap.md`, unit `GS2-00`.
- ADR-0077 and the Quint-first migration design require the published Quint-profile/compiled-contract producer before `GS2-02`; the later workspace-default flip is not a hidden prerequisite.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd specify --work 2953-gh-modernization-m0-invariants`.
- Tier 1: this unit fixes organization-wide authority, permission, rollback, and point-of-no-return semantics used by every receiver.
