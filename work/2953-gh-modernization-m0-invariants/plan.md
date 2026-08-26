---
schemaVersion: 1
workId: 2953-gh-modernization-m0-invariants
title: Gh Modernization M0 Invariants
stage: plan
changeTier: tier1
status: planned
sourceSpec: work/2953-gh-modernization-m0-invariants/spec.md
sourceClarifications: work/2953-gh-modernization-m0-invariants/clarifications.md
sourceChecklist: work/2953-gh-modernization-m0-invariants/checklist.md
publicOrToolFacingImpact: true
---

# Gh Modernization M0 Invariants Plan

Prose status: planned

## Source Snapshot
- spec: work/2953-gh-modernization-m0-invariants/spec.md sha256:01ce360adedf1dc17d32cb8b894977342a0e013f9ab8fe589003988403c18c02 schemaVersion:1
- clarifications: work/2953-gh-modernization-m0-invariants/clarifications.md sha256:f2b70c831538ab3d4757b386c75b679f492f71ef87d737504b879dfc5e6fedf8 schemaVersion:1
- checklist: work/2953-gh-modernization-m0-invariants/checklist.md sha256:467a8baf63ec473d4e24093ace0a60564ab83a8be31449db3b2009ba0810eca2 schemaVersion:1

## Plan Scope
- Complete only `GS2-00`, using `.github#2953` as the merged acceptance anchor and this SDD package as authored implementation evidence.
- Generate machine-readable, content-addressed census/corpus/review indexes in the work package; keep the organization decision in `docs/adr`, and reconcile the design, roadmap, architecture map, and live issue/Project projections.
- Read live GitHub state through paginated REST and the budgeted coordination client; never infer absence from a partial page or convert unreadable state into absence.
- Keep production unchanged. The explicit user-authorized README-only repository at `ce22e4d10f2efae7aa09018521487b598c082350` remains inert; active bootstrap, qualification, and further administrative provisioning begin only in `GS2-01` after Q0 acceptance.

## Plan Decisions
- PD-001 [AC-001] [FR-001] complete: Derive the authority census from repository search, registry/roster data, workflow and command inventories, live issue/Project schemas, and explicit external-authority rows. Store each row with subject, authority, revision/completeness, owner, v2 disposition, and source anchor.
- PD-002 [AC-001] [FR-002] complete: Derive the mutation census from commands, workflows, schedules, release paths, settings/admin routes, and dynamically registered interpreters. A validator compares the declared entries with independent search categories and rejects an injected omission.
- PD-003 [AC-002] [FR-003] complete: Freeze only compact indexes and source digests in git. Preserve remote URLs/IDs and original artifact hashes; immutable bulky outputs belong to CI artifacts/releases in later units.
- PD-004 [AC-002] [FR-004] complete: Combine compatibility and deletion inventories so every preserved/migrated/sealed/retired surface has one later unit and every retired surface names a static or runtime absence proof.
- PD-005 [AC-003] [FR-005] complete: Record a typed handoff table covering P4 residue, `.github#2932`, declared dependencies, Quint Q1-Q7 rows, active claims/reviews/deliveries/releases, and receiver changes. Only the `FS-GG.SDD#924` sequence—exact-source Q1, post-Q1 ADR-0077 amendment, then published artifact—blocks `GS2-01.4` and `GS2-02`.
- PD-006 [AC-004] [FR-006] complete: Record the Q0 delegated-maintainer decision selecting scheduled complete audits as the authoritative runtime posture; amend the design and roadmap to remove hosted events from the critical path while retaining an optional, separately qualified future event accelerator.
- PD-007 [AC-005] [FR-007] complete: Add one Proposed organization ADR with authority table, repository boundary, epoch states, protected transition authority, rollback/roll-forward boundary, permission separation, qualification model, and supersession links.
- PD-008 [AC-005] [FR-008] complete: Add a Q0 manifest that hashes the governing design, ADR, all censuses/indexes, plan/spec, and independent review records. Live role discovery binds each exact attestation to an earlier distinct, unedited, same-author narrative comment on the repair PR. Negative controls mutate copies and must produce a non-zero validator result.
- PD-009 [AC-006] [FR-009] complete: Update the roadmap status/receipts and Epic checklist, then use typed Project set mutation for `.github#2964` and `.github#2965`; re-read both issues and board fields before claiming projection coherence.

## Contract Impact
- PC-001 [PD-007] organizationPolicy: The new ADR governs repository ownership, GitHub/custom authority assignment, epoch transitions, rollback, and deletion. It is additive until `OpenV2` and changes no live writer in this unit.
- PC-002 [PD-009] roadmapProjection: Stable GS2 IDs and parent issue ownership remain compatible; incorrect historical M/F range prose is corrected, and Project dependency fields become the authoritative scheduling projection.

## Verification Obligations
- VO-001 [PD-001] [PD-002] censusCompleteness: Run repository/live-source census generation with recorded revision, pagination, known-present positive controls, and independent omission mutations.
- VO-002 [PD-003] [PD-004] corpusAndDeletion: Verify every frozen input and compatibility/deletion row has a digest, provenance, disposition, later unit, and control; reject a missing digest or deletion proof.
- VO-003 [PD-005] handoffClassification: Re-read every named issue/PR/board row from live sources and reject an unclassified or multiply classified active row.
- VO-004 [PD-006] operationsReview: Independently challenge scheduled-audit availability, latency, API budget, failure recovery, and emergency-disable posture; record residual risk explicitly.
- VO-005 [PD-007] [PC-001] adrCoherence: Run ADR index/link/architecture/prose gates and verify the ADR remains Proposed until the exact-fingerprint independent Q0 review is accepted.
- VO-006 [PD-008] q0NegativeControls: Mutate an authority row, writer row, corpus digest, deletion unit, runtime decision, reviewer fingerprint, attestation grammar, and narrative-evidence relationship separately; each mutation must make Q0 validation red.
- VO-007 [PD-009] [PC-002] liveProjection: Verify Epic body task lines, native child relations, Project statuses, and complete `Blocked by` values from fresh sources with a known-present control.

## Performance Intent
No performance intent is declared for this work item.

## Migration Posture
- PM-001 [PC-001] additiveOnly: GS2-00 adds policy/evidence and corrects scheduling projections. It migrates no production fact and changes no writer authority.
- PM-002 [PC-002] preserveIds: Existing program issue numbers and native child relations remain stable; only their acceptance wording and dependency projection are corrected.

## Generated View Impact
- GV-001 [PD-008] workModel: `readiness/2953-gh-modernization-m0-invariants/work-model.json` refreshes from current SDD sources or reports stale generated evidence.
- GV-002 [PD-008] q0Manifest: The compact Q0 manifest is regenerated from exact source/artifact fingerprints and refuses any unlisted evidence file.

## Accepted Deferrals
No accepted plan deferrals recorded.

## Planning Findings
No blocking planning findings recorded.

## Advisory Notes
- GitHub live schema/settings observations can change after Q0; GS2-10 must re-observe and freeze them as candidate inputs.
- The 30-day Q10 gate means the overall program cannot truthfully close before its observation window elapses.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd tasks --work 2953-gh-modernization-m0-invariants`.
