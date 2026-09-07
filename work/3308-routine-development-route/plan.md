---
schemaVersion: 1
workId: 3308-routine-development-route
title: R0 — Adopt the smaller contract
stage: plan
changeTier: tier1
status: planned
sourceSpec: work/3308-routine-development-route/spec.md
sourceClarifications: work/3308-routine-development-route/clarifications.md
sourceChecklist: work/3308-routine-development-route/checklist.md
publicOrToolFacingImpact: true
---

# R0 — Adopt the smaller contract Plan

Prose status: planned

## Source Snapshot
- spec: work/3308-routine-development-route/spec.md sha256:97e2f8b236bcc2dd427fce6c72485b5843f286f5c772c194d15915403ee039fd schemaVersion:1
- clarifications: work/3308-routine-development-route/clarifications.md sha256:3510d1d8b3904a5f3f605029c97aadc92d0cbdee69de9fcfe1b35dcc6830a784 schemaVersion:1
- checklist: work/3308-routine-development-route/checklist.md sha256:c58f0a0c1f673a12078cc48b3838cfe820dc1d0f7a7cb308302f0bc06a540cbd schemaVersion:1

## Plan Scope
- Work item 3308-routine-development-route is planned from the current specification, clarification, and checklist facts.
- Requirement count: 2.
- Clarification decision count: 0.
- Checklist result count: 2.

## Plan Decisions
- PD-001 [AC-001] [FR-001] complete: Add an explicit `routine` delivery-route contract whose eligibility is validated from machine-readable policy and whose native PR boundary requires selected technical checks plus exact-head freshness, but none of the strict route's process-only evidence.
- PD-002 [AC-001] [FR-002] complete: Keep `strict` as the only route for publication, deployment, credentials, destructive effects, migrations or cutovers, externally enforced contracts, and all work already operating under strict authority; never infer routine eligibility from size.

## Contract Impact
- PC-001 [PD-001] command report: Version the delivery-route policy and validator so callers can select `routine` prospectively while existing `lightweight` and `sdd-required` records retain strict legacy meaning. Emit `routine-eligibility` from a least-privilege `pull_request_target` definition loaded from the default branch; fetch base/head only as Git data, execute the validator and dependency from the exact base SHA, and leave candidate-controlled `claim-generation` as the strict item gate. Update shared skill sources and regenerate Claude/Codex twins from the same source.

## Verification Obligations
- VO-001 [PD-001] [PC-001] semanticTest: Prove routine admission succeeds without issue, claim, SDD, lifecycle, critique, feedback/receipt-cycle, or metadata-Done inputs; invert every protected-operation and changed-head predicate; prove replacing or deleting candidate workflow/validator bytes cannot influence the trusted result; run focused unit/CLI and skill-projection parity suites.

## Performance Intent
No performance intent is declared for this work item.

## Migration Posture
- PM-001 [PC-001] prospective: Existing route records and in-flight strict work keep their original authority. Only new explicitly admitted pilot work may use `routine`; downstream accepted-unit/release consumers remain strict until their own public contracts adopt it.

### Receiving-surface and evidence-status map

This table binds every proposed removal in the roadmap to the existing surface that must consume or
retire it. `Observed` means the current requirement or receiving surface was directly inspected in this
repository; `inferred` means the named later consumer is the proposed owner but has not yet demonstrated
adoption; `unmeasured` means R2/R4 still owes runtime or cohort evidence. R0 creates only the shared
prospective route; the named later increment remains accountable for adoption.

| Proposed removal or weakening | Receiving surface | Evidence status at R0 | Adoption owner |
|---|---|---|---|
| Per-phase telemetry authorization | Shared `work-roadmap`; Coordination economics/usage adapter | Observed in current driver; runtime benefit unmeasured | R2 |
| Mandatory full SDD family | Shared `work-roadmap`; SDD default consumer | Observed in current driver and this strict bootstrap; ordinary receiver adoption inferred | R5 |
| Universal independent critique | Shared `work-roadmap`; migration-driver callers | Observed in current driver | R1/R4 |
| Distinct implementer/critic/host identities | Shared `work-roadmap`; Coordination lifecycle consumer | Observed in current driver; v2 adoption inferred | R1/R5 |
| Ten repair/confirmation rounds | Shared `work-roadmap` review loop | Observed in current driver | R1 |
| Re-review after artifact-only/base movement | Shared driver changed-head/review caller | Observed in current driver | R1/R4 |
| Two-PR implementation/receipt cycle | Shared delivery driver; Coordination accepted-unit consumer | Observed in current driver; native replacement inferred | R1/R5 |
| Parent-wide qualification on every routine closure | Migration driver; v2 qualification consumer | Observed in migration guidance; v2 boundary inferred | R4/R5 |
| Formal soundness proof for every selected test set | Migration driver/test selector | Observed in migration guidance; defect yield unmeasured | R4 |
| Mutation/inversion evidence for ordinary edits | Shared driver technical-check selector | Observed in current driver; keep for protected gates | R1/R4 |
| Double regeneration/fixed-point checks at handoffs | SDD generated-view consumer; shared driver | Observed in current SDD bootstrap; receiver adoption inferred | R5 |
| Distributed claim/touch-set for routine work | Shared delivery driver; Coordination ownership consumer | Observed in current strict route | R1/R5 |
| Immediate publication/registry adoption per fix | `.github` release/registry owner | Observed release surfaces; batching behavior inferred | R3 |
| Two feeds/two independent builds for routine releases | `.github` publication tooling | Observed publication surfaces; allowed release class unmeasured | R3 |
| Globally fresh toolkit before every item | Migration driver and installed receiver pins | Observed in migration guidance; stable-pin adoption inferred | R3/R5 |
| Completion blocked on metadata/cleanup | Shared delivery driver; Coordination status/projection consumer | Observed in current driver; asynchronous projection inferred | R1/R5 |

## Generated View Impact
- GV-001 [PD-001] agentGuidance: Regenerate the two tracked runtime `work-roadmap` skill twins and driver manifest from their shared source contract, then require projection tests to show equivalent routine/strict behavior. Repository-root `AGENTS.md`/`CLAUDE.md` remain generated SDD workspace guidance and do not duplicate route prose. Full `fs-gg-sdd-*` skill materialization is explicitly outside R0 because this repository intentionally carries the partial product projection and its skill-budget gate rejects that expansion; `.github#2366` owns that pre-existing scaffold gap.

## Accepted Deferrals
No accepted plan deferrals recorded.

## Planning Findings
No blocking planning findings recorded.

## Advisory Notes
- Optional Governance pointers remain compatibility facts only.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd tasks --work 3308-routine-development-route`.
