---
schemaVersion: 1
workId: 2953-gh-modernization-m0-invariants
title: GitHub Substrate v2 Q0 ratification
stage: specify
changeTier: tier1
status: specified
publicOrToolFacingImpact: true
---

# GitHub Substrate v2 Q0 ratification Specification

Prose status: specified

## User Value
FS-GG operators receive one reviewable authority, rollback, deletion, and runtime decision package that safely gates the independent v2 build.

## Scope
- SB-001: Ratify GS2-00 only: handoff dispositions, organization ADR, v1 authority/mutation/compatibility/deletion censuses, frozen corpus index, runtime boundary, Epic/dependency repairs, and exact-fingerprint Q0 review evidence.
- SB-002: Treat successful producer Q1 over the exact literate Quint source/extracted module set, the post-Q1 ADR-0077 amendment, and the published Quint-profile/compiled-contract artifact tracked by `FS-GG/FS.GG.SDD#924` as ordered `GS2-01.4`/`GS2-02` prerequisites; treat the later lifecycle-default decision as `cutover-deferred` until `OperatingV2`.
- SB-003: Preserve `.github#2932` and the completed Standard Typed SDD P0-P4 artifacts as historical/corpus inputs, never as a parallel v2 implementation lane or qualification authority.

## Non-Goals
- SB-004: Preserve the explicit user-authorized README-only `FS.GG.Coordination` repository created early at `ce22e4d10f2efae7aa09018521487b598c082350`, but do not begin its active bootstrap or qualification, install an App, create production environments, change production schema/settings, or enable/disable a writer.
- SB-005: Do not implement GS2-01 or later units, close program anchors early, or accept roadmap checkboxes/project status as proof.
- SB-006: Do not make the advisory Agentic Workflows pilot or the later Typed SDD default flip a hidden cutover prerequisite.

## User Stories
- US-001 (P1): As a cutover operator, I can identify one authority for each coordination fact and one fence or deletion disposition for every v1 writer before active repository bootstrap and qualification begin.
- US-002 (P1): As an independent reviewer, I can reproduce every Q0 census and verify that omissions, stale Project projections, and incompatible authority assignments fail closed.
- US-003 (P1): As a receiver maintainer, I can distinguish work that blocks v2 from parallel product work, frozen-candidate inputs, superseded inventory, and work deferred until `OperatingV2`.

## Acceptance Scenarios
- AC-001 [US-001] [FR-001] [FR-002]: Given the live v1 repository and fleet registry, when the censuses run, then every authority and mutation entry has a revision/completeness contract, current precondition, v2 disposition, and deletion or preservation unit, and removing a known entry makes validation red.
- AC-002 [US-002] [FR-003] [FR-004]: Given the frozen corpus and public compatibility inventory, when Q0 is assembled, then original bytes/provenance and exact artifact digests are retained, every compatibility surface is classified, and an omitted representative surface is detected.
- AC-003 [US-003] [FR-005]: Given every active adjacent Typed SDD and modernization row, when handoff classification is applied, then each row has exactly one roadmap classification and a real dependency edge only where authorship depends on landed work.
- AC-004 [US-001] [FR-006]: Given the runtime alternatives, when the organization decision is ratified, then scheduled audits remain authoritative unless an owned hosted boundary proves availability, secrets, ingress, telemetry, upgrades, incident response, retention, cost, and recovery.
- AC-005 [US-002] [FR-007] [FR-008]: Given the ADR, design, censuses, and operational decision, when independent architecture/security/operations/cross-repository review runs against exact fingerprints, then all material questions are resolved before `GS2-01` becomes Ready.
- AC-006 [US-003] [FR-009]: Given the live Epic and program anchors, when the projection is reconciled, then the Epic checklist names GS2 ownership accurately and `.github#2964/#2965` carry their actual dependencies in the Project `Blocked by` field rather than issue-body prose.

## Functional Requirements
- FR-001: The authority census must enumerate issue bodies/comments/types/fields, Project items/fields, registries, workflows, commands, JSON markers/contracts, environment variables, files, packages, schedules, repository/org settings, and external authorities that affect a coordination decision. (Stories: US-001, US-002; Acceptance: AC-001)
- FR-002: The mutation census must enumerate every always/conditional writer, bind its current precondition and permission, and assign `Preserve`, `Bridge`, `Migrate`, `Seal`, or `Retire` plus an exact later unit. Unknown writers fail validation. (Stories: US-001; Acceptance: AC-001)
- FR-003: The frozen corpus must content-address representative success and failure cases, including `.github#2932`'s churn, mutation-entry, protocol-string, replay, omission, misclassification, and byte-compatibility evidence without importing its superseded implementation plan. (Stories: US-002; Acceptance: AC-002)
- FR-004: The compatibility/deletion inventory must cover CLI verbs/flags/exit codes, JSON/marker schemas, package IDs/versions, reusable workflow contracts, required contexts, receiver pins, parsers, projections, exceptions, schedules, packages, and source trees, with observable absence tests for retirement. (Stories: US-001, US-002; Acceptance: AC-002)
- FR-005: P4 residue is complete; `.github#2932` is `superseded-inventory`; `.github#2841`, `.github#2850`, and `.github#2903` are current-v1 defects or corpus inputs, not v2 prerequisites; `FS-GG.SDD#924` is a `v2-blocker` until exact-source Q1, the post-Q1 ADR-0077 amendment, and publication complete; subsequent adoption/default rows are candidate-input changes or cutover-deferred. (Stories: US-003; Acceptance: AC-003)
- FR-006: The Q0 decision selects scheduled complete audits as the authoritative runtime posture and rejects a hosted App/webhook boundary for this cutover because the required ownership, availability, secrets, ingress, telemetry, upgrades, incident response, retention, cost, and recovery evidence is absent; `.github#2961` is outside the critical path under that recorded choice. (Stories: US-001; Acceptance: AC-004)
- FR-007: The organization ADR must record the dedicated repository, published-kernel dependency, new-only writer policy, independent qualification lane, protected Git epoch ledger, native/custom authority table, and irreversible `OpenV2` boundary, remaining Proposed until independent acceptance. (Stories: US-001, US-002; Acceptance: AC-005)
- FR-008: Q0 acceptance must bind exact design, ADR, census, corpus-index, and operational-decision fingerprints and include independent architecture, security, operations, and cross-repository findings; generated validation alone is insufficient. (Stories: US-002; Acceptance: AC-005)
- FR-009: The roadmap, Epic acceptance, issue hierarchy, Project status, and `Blocked by` fields must agree with the accepted GS2 sequence, while explicitly remaining projections rather than completion authority. (Stories: US-003; Acceptance: AC-006)

## Ambiguities
No material ambiguities recorded.

## Public Or Tool-Facing Impact
- The ADR changes organization-wide coordination authority and rollback policy. The roadmap and Project fields change scheduling projections, and an exact Q0 validation workflow gates this evidence; no production writer, public CLI, production schema, package, or live setting changes in this unit.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd clarify --work 2953-gh-modernization-m0-invariants`.
