---
title: "FS.GG development design inventory"
category: Design
categoryindex: 4
description: "A complete documentation census with a curated register of development programmes, older proposals, supporting evidence and authority references."
---

# FS.GG development design inventory

Snapshot: **2026-09-07**. Companion to the [development master](development-master.md). This is a
discovery and planning inventory, not an execution queue, policy registry or completion authority.

**Current priorities live in the [master’s current plan](development-master.md#1-current-plan).**
This broader inventory preserves additional proposals and history; it does not give every row equal
priority or add it to the current plan.

## Coverage and reading rules

The census covers **237 pre-existing Markdown documents** under this repository’s `docs/`, plus the
two master/inventory pages added by this change: **239 documents, each listed once below**. It includes
proposals under `reports/`, design-goal pages, ADRs, acceptance specifications and the two local drafts.
Filename alone is not used to decide whether a document contains future development work.

The tracked source snapshot is fetched `origin/main` at `8363d57f3b5b15dd86b6efe8df53a89e93633648`.
The shared checkout was at `6315cf8f980dd49c31b0bed64309676d28ee51b0`; source content was read from the fetched
revision where it differed. The governance proposal was a local draft and accompanies this documentation change; the telemetry
report remains a local-only draft and is identified without a publication link. Relative
links open the checkout’s current files, which can lag the recorded source snapshot until it is updated.

Scope excludes lifecycle `work/`/`readiness/` artifacts, source-code documentation outside `docs/`, and a
complete census of other repositories. The [master’s current plan](development-master.md#1-current-plan) and the programme rows below
identify the planning sources; the next portfolio expansion would inventory those owners’ designs
in place, preserving canonical links rather than copying their documents here. No claim of organization-wide
completeness or live board/issue status is made.

Dispositions distinguish a proposal, accepted direction, implementation evidence, qualified completion,
a deliberate hold and historical lineage. “Needs reconciliation” means current implementation was not
established by this review; it does not mean the work is unimplemented. Earlier banners and checkboxes
are dated evidence, not current production authority. Accepted ADRs and exact roadmap contracts retain
their own status and supersession semantics.

## Snapshot implementation evidence

- [PR #3320](https://github.com/FS-GG/.github/pull/3320), commit `b09987d3`, adopted the routine pilot boundary.
- [PR #3321](https://github.com/FS-GG/.github/pull/3321), commit `5da8bff9`, added native delivery/readback.
- [PR #3322](https://github.com/FS-GG/.github/pull/3322), commit `8363d57f`, added a non-blocking observer.
  The radical-design ledger still leaves R2 unchecked. This inventory records implementation evidence
  without certifying R2 acceptance, full economics coverage or R5 carryover.
- SVG revision rationale was incorporated by PR #3315; v2 prospective integration landed in PR #3319.
  Those prose merges do not implement the proposed product/profile changes.

## Census totals

| Group | Documents |
|---|---:|
| Programme register | 20 |
| Older programmes and carryover | 16 |
| Historical split planning | 10 |
| Orientation and reference | 49 |
| Research, audits and delivery reports | 44 |
| Game acceptance corpus | 16 |
| Architecture decision records | 84 |
| **Total** | **239** |

## Programme register

This register includes the current plan and further-development candidates. Membership is not scheduling;
the master deliberately keeps v2/simplification/OR/PB dominant. See each stream and disposition before use.

| Document | Stream | Snapshot disposition | Remaining decision / use |
|---|---|---|---|
| [S.I.R. Combat in Quint handbook design and roadmap](2026-08-27-sir-combat-quint-learning-handbook-design-and-roadmap.md) | Learning / S.I.R. | Proposed handbook; S.I.R. is intended publication and semantic owner | Confirm first chapter/model slice and publication plan; do not move domain authority into this cross-project index. |
| [SVG game engine and Fable template: design and delivery roadmap](2026-09-07-064259-svg-game-engine-template-design-roadmap.md), with [SVG-FOUND-01 foundation subroadmap](roadmaps/svg-game-engine-foundation.md) | Game / SVG | Proposed complete programme; bounded foundation window ready for independent current-route work, implementation not started | Begin third-party provenance, Scene/API and Fable/model-tool evidence, then retained SVG and neutral/S.I.R. consumers. No V0–V6 completion prerequisite; published preview qualification and lifecycle/default activation remain separate. |
| [Design and roadmap: Reduce development bureaucracy to 10–20%](2026-09-07-074251-radical-development-bureaucracy-reduction-design-and-roadmap.md) | Routine development | Partial implementation: source ledger marks R0/R1; observer landed but R2 is unchecked | Reconcile R2 closure with PR #3322; R3–R5 remain unchecked, including independent v2 carryover evidence. |
| [Proposal: Revise the SVG game engine roadmap for bounded delivery and v2 compatibility](2026-09-07-121207-svg-game-engine-roadmap-revision-proposal.md) | Game / SVG | Supporting revision rationale; incorporated into the original roadmap by PR #3315 | Read the original roadmap for the integrated proposal; retain this analysis as rationale, not a duplicate programme. |
| [Design: Typed SDD and complete Governance integration](coordination/2026-08-24-174459-typed-sdd-governance-integration-design.md) | Governance / SDD | Proposed successor explicitly deferred behind backend/migration | Keep existing Governance functionality distinct from the new constitutional handoff; explicitly choose activation order. |
| [Design: GitHub Substrate v2 and coordinated fleet cutover](coordination/2026-08-25-github-substrate-v2-fleet-cutover-design.md) | Coordination v2 | Governing design lineage; opening banner predates later accepted roadmap work | Read with ADR-0078 and the architecture amendment; do not infer live epoch from a design banner. |
| [Design: Quint-first Typed SDD migration and feature preparation](coordination/2026-08-25-quint-first-typed-sdd-migration-design.md) | Quint / SDD | Accepted direction and recorded Q1 qualification; Q2 banner is a dated source statement | Refresh actual producer/migration evidence before dispatch or default claims; no external issue audit performed. |
| [ADR-0077 amendment: accepted Q1 Quint qualification](coordination/2026-08-26-adr-0077-q1-qualification-amendment.md) | Quint / SDD | Accepted Q1 amendment; bounded qualification evidence | Preserve qualified scope; do not extrapolate Q1 into shipped backend or consumer/default adoption. |
| [Architecture review: GitHub Substrate v2 remaining migration](coordination/2026-08-30-github-substrate-v2-remaining-migration-architecture-review.md) | Coordination v2 | Architecture amendment; roadmap records later adoption beyond the original pause | Retain journal/fencing/full-snapshot rationale; the roadmap owns current progress. |
| [Design: operations-research-first agent orchestration](coordination/2026-08-31-operations-research-first-agent-orchestration-design.md) | Optional orchestration | Proposed H0–H8; federation F0–F5 and visualization remain conditional | Name unmet scheduling/durability need; keep Git authority and required governance outside optimizer discretion. |
| [Design and roadmap: Performance-bounded, Quint-governed development flow](coordination/2026-09-06-performance-bounded-development-flow-design-and-roadmap.md) | Optional controller | Proposed PB0–PB8 controller; optional for routine simplification | Assign retained budget/recovery functions independently; justify and qualify any controller slice. |
| [Analysis: Align development-flow proposals without weakening v2 migration](coordination/2026-09-07-112837-development-flow-proposal-alignment-analysis.md) | Routine / OR / PB | Merged analysis of proposed scope; some adoption statements predate R0 | Preserve distinctions between ceremony and runtime safeguards; read adoption claims against current policy. |
| [Analysis: Integrate radical CI simplification into the v2 migration roadmap](coordination/2026-09-07-131445-v2-roadmap-ci-simplification-analysis.md) | Coordination v2 / routine | Prospective integration analysis, merged with roadmap §12 | Use its unit mapping for a later accepted contract change; preserve exact roadmap pins. |
| [Design proposal: Preserve governance while simplifying CI](coordination/2026-09-07-150716-governance-preserving-ci-simplification-design-proposal.md) | Governance / routine | Proposal included in this documentation change; local-only at census, not adopted | Decide independent delivery/synchronization classification and enforcing owners before implementing the refinement. |
| [Roadmap: GitHub Substrate v2 fleet cutover](github-substrate-v2-roadmap.md) | Coordination v2 | Executable roadmap; recorded acceptance through GS2-07.5; later unchecked frontier remains source-owned | Use the source for dispatch and exact gates; §12 is prospective integration, not adopted unit contracts. |
| [StarCraft II unit symbology library — architecture & design](reports/2026-07-05-starcraft2-unit-symbology-library-design.md) | Showcase / rendering | Pre-ADR proposal; current implementation status unverified | Decide retain/defer and check current provider/Governance compatibility; preserve optional showcase identity. |
| [Polyglot Web Product Architecture and Implementation Design](reports/2026-07-27-163509-polyglot-web-product-architecture-and-implementation-design.md) | Web / templates | Proposed programme amended by ADR-0071; partial adoption referenced | Reconcile remaining milestones with owner evidence; preserve neutral TypeScript and separately selected Fable/game providers. |
| [Fable bindings, current Glutinum, complex TypeScript libraries, and Quint analysis](reports/2026-09-03-101829-fable-bindings-glutinum-quint-analysis.md) | Bindings | Completed analysis containing proposed P0–P4 development | Analysis completion is not converter implementation; evaluate adapter, closure and runtime-proof work in the owner repository. |
| [F# roadmap telemetry and projection automation](reports/2026-09-04-fsharp-roadmap-telemetry-and-projection-automation-design.md) | Telemetry / delivery | Accepted for filing in source; whole implementation status unverified | Reconcile retained automation with routine observer and removed receipt obligations; do not create a second economics programme. |
| `reports/2026-09-07-v2-migration-telemetry-and-process-overhead-analysis.md` (local draft; unpublished) | Telemetry / evidence | Local draft report; uncommitted at census | Keep discoverable without assuming acceptance or independently verified private telemetry; use as evidence, not a dispatch source. |

## Older programmes and carryover

| Document | Stream | Snapshot disposition | Remaining decision / use |
|---|---|---|---|
| [Phase D — the corpus through the shim, and the deletion of bash](2026-07-15-phase-d-corpus-through-shim-plan.md) | Engine history | Earlier migration plan; paired July 16 dispositions exist | Read payoff and differential dispositions before treating an old phase as outstanding. |
| [D.4 — the five `51-fs-flip.sh` differential assertions, disposed on the record](2026-07-16-d4-differential-disposition.md) | Engine history | Historical substitution/retirement disposition | Do not recreate removed escape-hatch assertions whose subject was retired. |
| [The Phase-D payoff — the 14 `engine-retires:phase-d` issues, disposed on the record](2026-07-16-phase-d-payoff-disposition.md) | Engine history | Historical explicit closure/residual disposition | Preserve surviving defects separately from retired port work; external residual state not re-audited. |
| [Design: Reducing coordination change amplification](coordination/2026-08-22-coordination-change-risk-mitigation-design.md) | Coordination reliability | Older proposed staged design; adoption needs reconciliation | Map command catalogue, lifecycle predicates, bootstrap, completeness and release residuals into current owners before reopening stages. |
| [Design: Agent-authored F# specification kernel and canonical mutation algebra](coordination/2026-08-24-typed-protocol-kernel-design.md) | Typed kernel history | Source states P0–P4 implemented; future authoring and coordination superseded | Use Quint migration and v2 for future work; preserve old contracts/history for supported consumers. |
| [Design: the coordination engine](design/coordination-engine.md) | Engine history | Original typed-engine proposal; later implementation and v2 lineage exist | Separate inherited useful principles from obsolete bash/shim migration work. |
| [Skill vendoring & mirroring — robustness roadmap](reports/2026-07-01-skill-vendoring-robustness-roadmap.md) | Skills | Older implementation roadmap; later skill ownership ADRs apply | Reconcile with ADR-0067/0070 and current publishing/materialization before dispatch. |
| [Extract FS.GG.Game as an SDD-driven component — implementation plan](reports/2026-07-06-extract-fs-gg-game-component-sdd-driven.md) | Game ownership | Earlier extraction report; ADR-0022 records component ownership | Use owner repository for new work and preserve extraction rationale. |
| [Consumer documentation roadmap — READMEs and the org front door](reports/2026-07-21-consumer-documentation-roadmap.md) | Documentation | Older design/roadmap; consumer guide now exists | Compare original acceptance with current guides; preserve uncovered user journeys instead of restarting the whole plan. |
| [workBoard — a single-repo, board-driven driver skill for product workspaces](reports/2026-07-21-workboard-single-repo-board-driver-design.md) | Product drivers | Older design with ADR-0064 and later skill lineage | Check current driver behavior and routine alignment before filing residual work. |
| [Skill context-budget and progressive-disclosure roadmap](reports/2026-07-25-223627-skill-context-budget-and-progressive-disclosure-roadmap.md) | Skills | Implementation roadmap with separate July 26 rollout evidence | Compare rollout and current skill contract; retain genuine context-budget residuals, not obsolete root assumptions. |
| [Skill context-budget rollout evidence](reports/2026-07-26-skill-context-budget-rollout-evidence.md) | Skills | Dated rollout evidence for context-budget programme | Use as implementation evidence of stated scope; no claim that every later host still behaves identically. |
| [Native collaboration-runtime supervision: design and roadmap](reports/2026-07-30-150617-native-collaboration-runtime-supervision-design-and-roadmap.md) | Orchestration history | Explicitly on hold since July 31; retained as design history | Do not resume its old milestones implicitly; OR design is the newer optional architecture to compare. |
| [GitHub-native operation keying and effect fencing — the design gate for `.github#1858`](reports/2026-08-04-github-native-executor-fencing-design.md) | Authority history | Earlier review design; current extent of adoption not re-audited | Retain failure rationale and compare accepted v2 journal/effect boundaries before reusing old mechanisms. |
| [Coordination churn: redesign proposal and stabilization roadmap](reports/2026-08-14-090508-coordination-churn-redesign-roadmap.md) | Coordination reliability | Proposal with later implementation/retirement evidence in its history | Separate delivered stages from remaining release/recovery work; compare v2 and routine successors. |
| [Roadmap: Agent-authored F# specification and protocol kernel](reports/2026-08-24-094348-typed-protocol-kernel-roadmap.md) | Typed kernel history | P-series historical delivery; future work redirected to successors | Do not restart P-series from old checkboxes; retain default/retirement boundaries via successor sources. |

## Historical split planning

The existing docs index identifies this as the June historical planning corpus. These documents retain intent and boundaries; their future tense is not an uncompleted programme.

| Document | Inventory role / planning treatment |
|---|---|
| [Design and controls](design-and-controls.md) | Historical split intent; use current architecture and owner evidence for implementation. |
| [Governance implementation plan](governance-implementation-plan.md) | Historical split intent; use current architecture and owner evidence for implementation. |
| [Governance project](governance-project.md) | Historical split intent; use current architecture and owner evidence for implementation. |
| [FS.GG implementation plans](implementation-plan.md) | Historical split intent; use current architecture and owner evidence for implementation. |
| [Project split decision](project-split-decision.md) | Historical split intent; use current architecture and owner evidence for implementation. |
| [Rendering implementation plan](rendering-implementation-plan.md) | Historical split intent; use current architecture and owner evidence for implementation. |
| [Rendering project](rendering-project.md) | Historical split intent; use current architecture and owner evidence for implementation. |
| [Research notes](research-notes.md) | Historical split intent; use current architecture and owner evidence for implementation. |
| [SDD project](sdd-project.md) | Historical split intent; use current architecture and owner evidence for implementation. |
| [Transition and boundaries](transition-and-boundaries.md) | Historical split intent; use current architecture and owner evidence for implementation. |

## Orientation and reference

Reference/discovery material informs the programmes above. “Reference” is a document role, not certification that every sentence or version claim is current. Design-goal pages describe targets; consult their dated status and governing sources.

| Document | Inventory role / planning treatment |
|---|---|
| [FS-GG architecture](architecture.md) | Supporting reference or guide; no independent development status asserted. |
| [Org-shared .NET build configuration](build/README.md) | Supporting reference or guide; no independent development status asserted. |
| [FS.GG components](components.md) | Supporting reference or guide; no independent development status asserted. |
| [Agent setup instructions for FS-GG](consumer/agent-setup.md) | Supporting reference or guide; no independent development status asserted. |
| [Output, automation & CI](consumer/automation.md) | Supporting reference or guide; no independent development status asserted. |
| [FAQ & troubleshooting](consumer/faq.md) | Supporting reference or guide; no independent development status asserted. |
| [Getting started](consumer/getting-started.md) | Supporting reference or guide; no independent development status asserted. |
| [Adopting governance](consumer/governance.md) | Supporting reference or guide; no independent development status asserted. |
| [FS-GG consumer guide](consumer/index.md) | Supporting reference or guide; no independent development status asserted. |
| [The development lifecycle](consumer/lifecycle.md) | Supporting reference or guide; no independent development status asserted. |
| [The consumer README standard](consumer/readme-standard.md) | Supporting reference or guide; no independent development status asserted. |
| [Versions, feeds & updates](consumer/versioning-and-updates.md) | Supporting reference or guide; no independent development status asserted. |
| [Which components do I need?](consumer/which-products.md) | Supporting reference or guide; no independent development status asserted. |
| [Who drives the lifecycle — humans, agents & the CLI](consumer/who-drives-the-lifecycle.md) | Supporting reference or guide; no independent development status asserted. |
| [Cross-repo coordination protocol](coordination/README.md) | Supporting reference or guide; no independent development status asserted. |
| [Agent-harness session identifiers, and why one cannot be the worker id](coordination/agent-session-identifiers.md) | Supporting reference or guide; no independent development status asserted. |
| [Cross-repo auto-update fabric](coordination/auto-update-fabric.md) | Supporting reference or guide; no independent development status asserted. |
| [Coordination board schema](coordination/board-schema.md) | Supporting reference or guide; no independent development status asserted. |
| [The contract-coherence gate](coordination/contract-coherence-gate.md) | Supporting reference or guide; no independent development status asserted. |
| [Compact evidence and CI artifact retention](coordination/evidence-retention.md) | Supporting reference or guide; no independent development status asserted. |
| [GraphQL budget & the `fsgg-coord` client](coordination/graphql-budget.md) | Supporting reference or guide; no independent development status asserted. |
| [Intra-repo parallel-work protocol](coordination/parallel-work.md) | Supporting reference or guide; no independent development status asserted. |
| [Coordination Project access attestation](coordination/project-access-attestation.md) | Supporting reference or guide; no independent development status asserted. |
| [Receiver-projection migration shape](coordination/receiver-proj-migration-shape.md) | Supporting reference or guide; no independent development status asserted. |
| [Coherent-set release saga](coordination/release-saga.md) | Supporting reference or guide; no independent development status asserted. |
| [The reusable-workflow contract](coordination/reusable-workflow-contract.md) | Supporting reference or guide; no independent development status asserted. |
| [Candidate-engine self-host bootstrap](coordination/self-host-bootstrap.md) | Supporting reference or guide; no independent development status asserted. |
| [Semantic-diff audit](coordination/semantic-diff-audit.md) | Supporting reference or guide; no independent development status asserted. |
| [The shipped-surface-mutation event](coordination/shipped-surface-mutation.md) | Supporting reference or guide; no independent development status asserted. |
| [The skill-apparatus retirement order (ADR-0067 §9, phase 4)](coordination/skill-apparatus-retirement-order.md) | Supporting reference or guide; no independent development status asserted. |
| [The skill registry (design)](coordination/skill-registry.md) | Supporting reference or guide; no independent development status asserted. |
| [The skill-union assertion](coordination/skill-union-assertion.md) | Supporting reference or guide; no independent development status asserted. |
| [Structured route and review decisions](coordination/structured-decisions.md) | Supporting reference or guide; no independent development status asserted. |
| [Public-content trust boundary](coordination/untrusted-content-boundary.md) | Supporting reference or guide; no independent development status asserted. |
| [FS.GG documentation](design-goals/README.md) | Target-architecture orientation; implementation-status page is dated, not live telemetry. |
| [What consumers receive](design-goals/consumer-delivery.md) | Target-architecture orientation; implementation-status page is dated, not live telemetry. |
| [Why GitHub Substrate v2](design-goals/github-substrate-v2-benefits.md) | Target-architecture orientation; implementation-status page is dated, not live telemetry. |
| [Implementation status](design-goals/implementation-status.md) | Target-architecture orientation; implementation-status page is dated, not live telemetry. |
| [Incoherent and contradictory proposals](design-goals/incoherent-proposals.md) | Target-architecture orientation; implementation-status page is dated, not live telemetry. |
| [Quint-backed workspaces](design-goals/quint-backed-workspaces.md) | Target-architecture orientation; implementation-status page is dated, not live telemetry. |
| [One Typed SDD lifecycle](design-goals/single-typed-sdd-lifecycle.md) | Target-architecture orientation; implementation-status page is dated, not live telemetry. |
| [Typed source and pure model](design-goals/typed-source-and-pure-model.md) | Target-architecture orientation; implementation-status page is dated, not live telemetry. |
| [FS.GG development design inventory](development-design-inventory.md) | New navigation document; no execution or policy authority. |
| [FS.GG development master](development-master.md) | New navigation document; no execution or policy authority. |
| [F* specification experiments](fstar/README.md) | Supporting reference or guide; no independent development status asserted. |
| [FS.GG project split](index.md) | Supporting reference or guide; no independent development status asserted. |
| [Quint specification experiments](quint/README.md) | Supporting reference or guide; no independent development status asserted. |
| [Contract & compatibility registry](registry/compatibility.md) | Supporting reference or guide; no independent development status asserted. |
| [FS.GG tools](tools.md) | Supporting reference or guide; no independent development status asserted. |

## Research, audits and delivery reports

Dated evidence and analyses are preserved even where they are not separate programmes. Findings may contain residual ideas; promotion to planned work needs an owner and comparison with later designs. Report completion does not establish implementation completion.

| Document | Inventory role / planning treatment |
|---|---|
| [Project-management topologies for an ADR + registry + Projects v2 system](2026-06-30-project-management-topologies-adr-registry-projects-v2-analysis.md) | Dated analysis/evidence; residual implementation status not re-audited. |
| [FS.GG.Coordination administrator settings report](2026-08-26-fs-gg-coordination-admin-settings-report.md) | Dated analysis/evidence; residual implementation status not re-audited. |
| [FS.GG.Coordination GS2-01.2 reconciliation report](2026-08-26-fs-gg-coordination-gs2-01-2-reconciliation-report.md) | Dated analysis/evidence; residual implementation status not re-audited. |
| [FS.GG.Coordination protected receipt approval report](2026-08-27-fs-gg-coordination-protected-approval-report.md) | Dated analysis/evidence; residual implementation status not re-audited. |
| [F*-first specification and F# extraction assessment](fstar/reports/assessment.md) | Dated analysis/evidence; residual implementation status not re-audited. |
| [Combat consequence specification](fstar/reports/combat.md) | Dated analysis/evidence; residual implementation status not re-audited. |
| [Communication-network specification](fstar/reports/communication.md) | Dated analysis/evidence; residual implementation status not re-audited. |
| [Quint scalability risks and controls](quint/reports/2026-08-29-154335-UTC-scalability-risks-and-controls.md) | Dated analysis/evidence; residual implementation status not re-audited. |
| [Quint-first specification-authoring assessment](quint/reports/assessment.md) | Dated analysis/evidence; residual implementation status not re-audited. |
| [Combat consequence specification](quint/reports/combat.md) | Dated analysis/evidence; residual implementation status not re-audited. |
| [Communication-network specification](quint/reports/communication.md) | Dated analysis/evidence; residual implementation status not re-audited. |
| [Code quality & architecture review — FS-GG/.github](reports/2026-07-02-code-quality-architecture-review.md) | Dated analysis/evidence; residual implementation status not re-audited. |
| [P0 — `Scene.Geometry` cut-line usage audit (gates ADR-0022)](reports/2026-07-06-p0-scene-geometry-cut-line-audit.md) | Dated analysis/evidence; residual implementation status not re-audited. |
| [Issue throughput and recurring error loops — an org-wide audit of 2026-07-12](reports/2026-07-12-issue-throughput-and-recurring-error-loops.md) | Dated analysis/evidence; residual implementation status not re-audited. |
| [Up-front design & architecture — how other people fill the gap ADRs don't](reports/2026-07-12-up-front-design-practices-and-the-proposal-gap.md) | Dated analysis/evidence; residual implementation status not re-audited. |
| [Cross-repo coordination overhead — a root-cause analysis of 2026-07-20](reports/2026-07-20-cross-repo-coordination-overhead-root-cause.md) | Dated analysis/evidence; residual implementation status not re-audited. |
| [drive-board run — 2026-07-21](reports/2026-07-21-drive-board.md) | Dated analysis/evidence; residual implementation status not re-audited. |
| [drive-board run — 2026-07-22 (second run)](reports/2026-07-22-drive-board-2.md) | Dated analysis/evidence; residual implementation status not re-audited. |
| [drive-board run — 2026-07-22 (third run)](reports/2026-07-22-drive-board-3.md) | Dated analysis/evidence; residual implementation status not re-audited. |
| [drive-board run — 2026-07-22 (fourth run)](reports/2026-07-22-drive-board-4.md) | Dated analysis/evidence; residual implementation status not re-audited. |
| [drive-board completion report — 2026-07-22 to 2026-07-23](reports/2026-07-22-drive-board.md) | Dated analysis/evidence; residual implementation status not re-audited. |
| [Comprehensive code and architecture review](reports/2026-07-25-194615-comprehensive-code-architecture-review.md) | Dated analysis/evidence; residual implementation status not re-audited. |
| [drive-board run — 2026-07-25](reports/2026-07-25-drive-board.md) | Dated analysis/evidence; residual implementation status not re-audited. |
| [drive-board run — 2026-07-26](reports/2026-07-26-drive-board.md) | Dated analysis/evidence; residual implementation status not re-audited. |
| [drive-board — 2026-07-27](reports/2026-07-27-drive-board.md) | Dated analysis/evidence; residual implementation status not re-audited. |
| [`#1794`'s mutation matrix, re-measured under repaired anchors — 2026-07-28](reports/2026-07-28-coord-engine-mutation-matrix.md) | Dated analysis/evidence; residual implementation status not re-audited. |
| [drive-board — 2026-07-28 handoff](reports/2026-07-28-drive-board-handoff.md) | Dated analysis/evidence; residual implementation status not re-audited. |
| [Which gates have ever produced a finding? — 2026-07-28](reports/2026-07-28-gate-finding-history.md) | Dated analysis/evidence; residual implementation status not re-audited. |
| [Adjudicating the never-red gates OUTSIDE `.github` by mutation — 2026-07-28](reports/2026-07-28-gate-mutation-adjudication-fleet.md) | Dated analysis/evidence; residual implementation status not re-audited. |
| [Adjudicating the never-red gates by mutation — 2026-07-28](reports/2026-07-28-gate-mutation-adjudication.md) | Dated analysis/evidence; residual implementation status not re-audited. |
| [The eight gates `#1810` could not measure — 2026-07-28](reports/2026-07-28-gate-mutation-wave3.md) | Dated analysis/evidence; residual implementation status not re-audited. |
| [Cross-repository release-tag measurement — 2026-07-30](reports/2026-07-30-cross-repo-release-tag-measurement.md) | Dated analysis/evidence; residual implementation status not re-audited. |
| [drive-board run — 2026-07-30](reports/2026-07-30-drive-board.md) | Dated analysis/evidence; residual implementation status not re-audited. |
| [Automation liveness and pin/feed drift — healthcheck legs 9–10](reports/2026-08-02-automation-liveness-and-pin-feed-drift.md) | Dated analysis/evidence; residual implementation status not re-audited. |
| [CLI surface three-way reconciliation — healthcheck leg 13](reports/2026-08-02-cli-surface-reconciliation.md) | Dated analysis/evidence; residual implementation status not re-audited. |
| [Required-context reconciliation — healthcheck leg 3](reports/2026-08-02-required-context-reconciliation.md) | Dated analysis/evidence; residual implementation status not re-audited. |
| [Roster and registry reconciliation — healthcheck legs 11–12](reports/2026-08-02-roster-and-registry-reconciliation.md) | Dated analysis/evidence; residual implementation status not re-audited. |
| [Sparse-checkout closure — fleet-wide healthcheck leg 4](reports/2026-08-02-sparse-checkout-closure-fleet.md) | Dated analysis/evidence; residual implementation status not re-audited. |
| [Board hygiene — healthcheck leg 14](reports/2026-08-03-board-hygiene.md) | Dated analysis/evidence; residual implementation status not re-audited. |
| [Healthcheck S1–S8 detective and suspect generator](reports/2026-08-03-healthcheck-s1-s8-detective.md) | Dated analysis/evidence; residual implementation status not re-audited. |
| [`org-healthcheck` operator skill](reports/2026-08-03-org-healthcheck-operator-skill.md) | Dated analysis/evidence; residual implementation status not re-audited. |
| [Per-repository architecture review verdicts](reports/2026-08-03-per-repo-architecture-verdicts.md) | Dated analysis/evidence; residual implementation status not re-audited. |
| [M6 compatibility-retirement readiness (superseded historical gate)](reports/2026-08-14-m6-retirement-readiness.md) | Dated analysis/evidence; residual implementation status not re-audited. |
| [Receiver yield, 2026-07-20 through 2026-08-20](reports/2026-08-20-receiver-yield.md) | Dated analysis/evidence; residual implementation status not re-audited. |

## Game acceptance corpus

Examples and acceptance material remain discoverable alongside the game/workspace designs. Inclusion does not transfer their current ownership or turn every example into a new delivery milestone.

| Document | Inventory role / planning treatment |
|---|---|
| [Your first build: a TestSpec, end to end](TestSpecTutorial.md) | Acceptance/example reference; not a scheduled feature by inclusion. |
| [Asteroids](TestSpecs/Games/asteroids.md) | Acceptance/example reference; not a scheduled feature by inclusion. |
| [Breakout](TestSpecs/Games/breakout.md) | Acceptance/example reference; not a scheduled feature by inclusion. |
| [Doodle Jump](TestSpecs/Games/doodle-jump.md) | Acceptance/example reference; not a scheduled feature by inclusion. |
| [Flappy Bird](TestSpecs/Games/flappy-bird.md) | Acceptance/example reference; not a scheduled feature by inclusion. |
| [Frogger](TestSpecs/Games/frogger.md) | Acceptance/example reference; not a scheduled feature by inclusion. |
| [Hollowveil](TestSpecs/Games/metroidvania.md) | Acceptance/example reference; not a scheduled feature by inclusion. |
| [Missile Command](TestSpecs/Games/missile-command.md) | Acceptance/example reference; not a scheduled feature by inclusion. |
| [Pong](TestSpecs/Games/pong.md) | Acceptance/example reference; not a scheduled feature by inclusion. |
| [Hollow Depths](TestSpecs/Games/roguelike-dungeon-crawler.md) | Acceptance/example reference; not a scheduled feature by inclusion. |
| [Hollowreach](TestSpecs/Games/sandbox-survival.md) | Acceptance/example reference; not a scheduled feature by inclusion. |
| [Snake](TestSpecs/Games/snake.md) | Acceptance/example reference; not a scheduled feature by inclusion. |
| [Space Invaders](TestSpecs/Games/space-invaders.md) | Acceptance/example reference; not a scheduled feature by inclusion. |
| [Tetris](TestSpecs/Games/tetris.md) | Acceptance/example reference; not a scheduled feature by inclusion. |
| [Bulwark: Tower Defense](TestSpecs/Games/tower-defense.md) | Acceptance/example reference; not a scheduled feature by inclusion. |
| [Breachpoint Tactics](TestSpecs/Games/turn-based-tactics.md) | Acceptance/example reference; not a scheduled feature by inclusion. |

## Architecture decision records

Authority and supersession stay in each ADR and the ADR index. Listing a decision does not mean it is currently accepted, universally applicable or still unsuperseded.

| Document | Inventory role / planning treatment |
|---|---|
| [ADR-0001: Cross-repo coordination via GitHub issues + a registry](adr/0001-cross-repo-coordination-via-issues.md) | Decision reference; read its own status and supersession. |
| [ADR-0002: Products are composed by scaffold, `lifecycle` is a template parameter, governance is populated-by-default](adr/0002-composition-by-scaffold-lifecycle-parameter-governance-populated.md) | Decision reference; read its own status and supersession. |
| [ADR-0003: Rename the `fs-skia-ui` version-coherence machinery to `fs-gg-ui`](adr/0003-rename-fs-skia-ui-version-machinery-to-fs-gg-ui.md) | Decision reference; read its own status and supersession. |
| [ADR-0004: SDD owns the lifecycle constitution for `lifecycle=sdd` products, shipped at `.fsgg/constitution.md`](adr/0004-constitution-ownership-for-lifecycle-sdd-products.md) | Decision reference; read its own status and supersession. |
| [ADR-0005: `.fsgg/` slot ownership — SDD owns `project.yml`, Governance owns `governance.yml`](adr/0005-fsgg-slot-ownership-sdd-project-governance-governance.md) | Decision reference; read its own status and supersession. |
| [ADR-0006: `.github` owns the org-shared .NET build config; `RestoreLockedMode` gates on `GITHUB_ACTIONS`](adr/0006-org-shared-dotnet-build-config-and-unified-restore-locked-mode-gate.md) | Decision reference; read its own status and supersession. |
| [ADR-0007: `FS.GG.Governance.ReferenceGateSet` version-derivation rule](adr/0007-reference-gate-set-package-version-derivation.md) | Decision reference; read its own status and supersession. |
| [ADR-0008: The `fsgg-sdd` CLI is a first-class member of the coherent set (orchestrator axis)](adr/0008-fsgg-sdd-cli-first-class-member-of-coherent-set.md) | Decision reference; read its own status and supersession. |
| [ADR-0009: The `fsgg-sdd` CLI is the single orchestrator — detect-and-remediate, not silent auto-update](adr/0009-cli-single-orchestrator-detect-and-remediate.md) | Decision reference; read its own status and supersession. |
| [ADR-0011: Every agent-skill root carries the full skill union; `fsgg-sdd` owns the mirror](adr/0011-agent-skill-roots-full-union-orchestrator-owned-mirror.md) | Decision reference; read its own status and supersession. |
| [ADR-0012: Dual-publish FS-GG packages to nuget.org (public) alongside the org GitHub Packages feed](adr/0012-dual-publish-to-nuget-org.md) | Decision reference; read its own status and supersession. |
| [ADR-0013: Publish to nuget.org via Trusted Publishing (OIDC), not a long-lived API key](adr/0013-trusted-publishing-oidc-for-nuget-org.md) | Decision reference; read its own status and supersession. |
| [ADR-0014: Skill vendoring & mirroring — one manifest, one materialize-and-verify, content-addressed](adr/0014-skill-vendoring-one-manifest-one-materialize-verify.md) | Decision reference; read its own status and supersession. |
| [ADR-0015: Register the registry schema as a governed contract](adr/0015-register-the-registry-schema-as-a-governed-contract.md) | Decision reference; read its own status and supersession. |
| [ADR-0016: Retire the Templates-local `new-fullstack.sh`; `new-sdd-fullstack.sh` is the sole full-stack scaffolder](adr/0016-retire-templates-local-new-fullstack-single-scaffolder.md) | Decision reference; read its own status and supersession. |
| [ADR-0017: Org skill registry — condition-aware materialization for the skill union](adr/0017-skill-registry-condition-aware-materialization.md) | Decision reference; read its own status and supersession. |
| [ADR-0018: Transient vs durable SDD artifact taxonomy — regenerable process output is gitignored by role, not committed per-feature](adr/0018-transient-durable-sdd-artifact-taxonomy.md) | Decision reference; read its own status and supersession. |
| [ADR-0019: Org repo roster registry + coordination-kit distribution](adr/0019-org-repo-roster-registry-and-coordination-kit.md) | Decision reference; read its own status and supersession. |
| [ADR-0020: Name the two products precisely — **platform**, **component**, **workspace**](adr/0020-platform-workspace-component-vocabulary.md) | Decision reference; read its own status and supersession. |
| [ADR-0021: Parallel intra-repo work via claim + worktree + declared touch-set](adr/0021-parallel-intra-repo-work-claim-worktree-touchset.md) | Decision reference; read its own status and supersession. |
| [ADR-0022: Extract FS.GG.Game as an SDD-driven component](adr/0022-extract-fs-gg-game-as-an-sdd-driven-component.md) | Decision reference; read its own status and supersession. |
| [ADR-0023: Onboard FS.GG.Audio as an SDD-driven component](adr/0023-onboard-fs-gg-audio-as-an-sdd-driven-component.md) | Decision reference; read its own status and supersession. |
| [ADR-0024: Wire FS.GG.Audio into the game/sample-pack scaffold profile](adr/0024-wire-fs-gg-audio-into-the-game-scaffold-profile.md) | Decision reference; read its own status and supersession. |
| [ADR-0025: First-class shipped-surface mutation — the governed event + reconcile protocol](adr/0025-first-class-shipped-surface-mutation-event.md) | Decision reference; read its own status and supersession. |
| [ADR-0026: Committed compact ship verdict — the merge-boundary answer survives in git history (extends 0018)](adr/0026-committed-compact-ship-verdict.md) | Decision reference; read its own status and supersession. |
| [ADR-0027: The parallel-work lock is keyed on the worker, not the account — plus identity, visibility, scheduling, and a channel](adr/0027-worker-keyed-claim-lock-and-worker-channel.md) | Decision reference; read its own status and supersession. |
| [ADR-0028: Keyboard input-config boundary — mechanism (Rendering) vs policy (Game)](adr/0028-keyboard-input-config-mechanism-policy-boundary.md) | Decision reference; read its own status and supersession. |
| [ADR-0029: The game TestSpec corpus is FS.GG.Game-owned; .github keeps pointer stubs](adr/0029-game-owns-the-testspec-corpus.md) | Decision reference; read its own status and supersession. |
| [ADR-0030: Creation-time scaffolding self-updates the CLI by default — a bounded carve-out to ADR-0009](adr/0030-creation-time-scaffolding-self-updates-by-default.md) | Decision reference; read its own status and supersession. |
| [ADR-0031 (WITHDRAWN): A silently re-published package is a NAMED failure — lock-file restores are cold, and the catalog `packageHash` names the cause](adr/0031-republished-package-is-a-named-failure.md) | Decision reference; read its own status and supersession. |
| [ADR-0032: `FSharp.Core` was never re-published — the lock file's `contentHash` must not depend on the machine](adr/0032-the-lock-hash-must-not-depend-on-the-machine.md) | Decision reference; read its own status and supersession. |
| [ADR-0033: The fixed-step double buffer is a simulation primitive, owned by `FS.GG.Game.Core`](adr/0033-fixed-step-double-buffer-is-a-simulation-primitive.md) | Decision reference; read its own status and supersession. |
| [ADR-0034: The coordination engine is a typed core; the tool is the model, and the docs are its projection](adr/0034-typed-coordination-engine.md) | Decision reference; read its own status and supersession. |
| [ADR-0035: Observed run receipts — a test obligation is satisfied by a run SDD *read*, not by a `pass` an agent *typed*](adr/0035-observed-run-receipts.md) | Decision reference; read its own status and supersession. |
| [ADR-0036 — The shared-build-config drift check compares against a pin, not against `main`](adr/0036-the-build-config-drift-check-pins-its-source.md) | Decision reference; read its own status and supersession. |
| [ADR-0037: Schema growth is publish-before-flip — two ordered PRs, and the validator gates on the declared version](adr/0037-schema-growth-is-publish-before-flip.md) | Decision reference; read its own status and supersession. |
| [ADR-0038: The defect corpus is the cut-over gate — the shadow clock could never tick, and the corpus found what the clock was built to classify as noise](adr/0038-the-corpus-is-the-cut-over-gate.md) | Decision reference; read its own status and supersession. |
| [ADR-0039: nuget.org is the read path; the org feed is the publish path](adr/0039-nuget-org-is-the-read-path.md) | Decision reference; read its own status and supersession. |
| [ADR-0040: The IO layer is ported too — "delete the bash implementation" was never executable, and the half that was left behind is where the bugs still are](adr/0040-port-the-io-layer.md) | Decision reference; read its own status and supersession. |
| [ADR-0041: A chore takes the item CAS, unchanged, on a closed per-repo lock issue — the refactor it seemed to need was the reason it never shipped](adr/0041-the-chore-lock-is-the-item-cas-on-another-subject.md) | Decision reference; read its own status and supersession. |
| [ADR-0042: The chore-lock ref is embedded beside the roster — `repos.yml` is unreadable exactly where the queue has to work](adr/0042-the-chore-lock-ref-is-embedded-beside-the-roster.md) | Decision reference; read its own status and supersession. |
| [ADR-0043: A superseded run is the one its group replaced — the conclusion is not part of the test](adr/0043-a-superseded-run-is-the-one-its-group-replaced.md) | Decision reference; read its own status and supersession. |
| [ADR-0044: Generated artifacts are derived from their generators, not declared](adr/0044-generated-artifacts-are-derived-from-their-generators.md) | Decision reference; read its own status and supersession. |
| [ADR-0045: A body-line sentinel says what an empty field cannot — human-blocked, and file-less chore](adr/0045-machine-readable-sentinels-for-human-block-and-chore.md) | Decision reference; read its own status and supersession. |
| [ADR-0046: One exit-code union, and the two GitHub-layer codes move off the verdict codes they collided with](adr/0046-one-exit-code-union-renumber-the-github-layer-collisions.md) | Decision reference; read its own status and supersession. |
| [ADR-0047: The Client.fs decomposition seams — extract the kit-digest advisory first](adr/0047-client-fs-decomposition-seams-kit-digest-first.md) | Decision reference; read its own status and supersession. |
| [ADR-0048: FR-level classification keys a per-requirement, non-synthetic evidence obligation](adr/0048-fr-level-classification-keys-a-per-requirement-evidence-obligation.md) | Decision reference; read its own status and supersession. |
| [ADR-0049: A Governance gate can bind to a scaffold template-profile, and every product of that profile inherits it](adr/0049-template-profile-gate-inheritance.md) | Decision reference; read its own status and supersession. |
| [ADR-0050: Re-verify a registry predicate against its owning manifest at the transition — filing-time and flip-time](adr/0050-predicate-at-transition-re-verify.md) | Decision reference; read its own status and supersession. |
| [ADR-0051: A coordination room is an item-referencing rendezvous whose roster and lifecycle derive from the items it references](adr/0051-coordination-rooms-derive-from-referencing-items.md) | Decision reference; read its own status and supersession. |
| [ADR-0052: Onboard FS.GG.Net as a transport component, and model wire contracts in the registry](adr/0052-onboard-fs-gg-net-transport-component.md) | Decision reference; read its own status and supersession. |
| [ADR-0053: A roadmap is driven one milestone at a time, each in a fresh disposable subagent running the full SDD lifecycle](adr/0053-roadmap-driven-milestone-loop-disposable-sdd-subagents.md) | Decision reference; read its own status and supersession. |
| [ADR-0054: A `.github`-authored, product-materialized *driver* skill is a third `skill-registry` class — `workRoadmap` rides it](adr/0054-workroadmap-delivery-fabric-a-github-authored-product-materialized-driver.md) | Decision reference; read its own status and supersession. |
| [ADR-0055: `FS.GG.Governance.ReferenceGateSet` versioning — plain SemVer with an in-package schema manifest](adr/0055-reference-gate-set-versioning-plain-semver-with-schema-manifest.md) | Decision reference; read its own status and supersession. |
| [ADR-0056: `sdd` is the default lifecycle for every vendored product; `spec-kit` is legacy, frozen, and scheduled for removal](adr/0056-sdd-is-the-default-lifecycle-spec-kit-is-legacy-and-scheduled-for-removal.md) | Decision reference; read its own status and supersession. |
| [ADR-0057: A `.github`-authored, never-materialized *operator* skill is a fourth `skill-registry` class — `drive-board` rides it](adr/0057-operator-scope-a-github-authored-never-materialized-skill-class.md) | Decision reference; read its own status and supersession. |
| [ADR-0058: Adopt one governing principle — *derive, don't restate; gate capabilities, not declarations*](adr/0058-adopt-one-governing-principle-derive-dont-restate.md) | Decision reference; read its own status and supersession. |
| [ADR-0059: Freeze the coordination taxonomy — two real instances before a new class](adr/0059-freeze-the-coordination-taxonomy-two-instances-before-a-class.md) | Decision reference; read its own status and supersession. |
| [ADR-0060: Generate the derived registry fields instead of hand-flipping them](adr/0060-generate-derived-registry-fields-instead-of-hand-flipping-them.md) | Decision reference; read its own status and supersession. |
| [ADR-0061: Collapse the publish-before-flip schema/validator split](adr/0061-collapse-the-publish-before-flip-schema-validator-split.md) | Decision reference; read its own status and supersession. |
| [ADR-0062: A versioned kit package replaces the byte-copy sync fan-out](adr/0062-versioned-kit-package-replaces-byte-copy-sync.md) | Decision reference; read its own status and supersession. |
| [ADR-0063: The scaffold materializer sources a skill from its owner repo, not a frozen provider template](adr/0063-scaffold-materializer-sources-skills-from-the-owner-repo.md) | Decision reference; read its own status and supersession. |
| [ADR-0064: `workBoard` — a single-repo, board-driven driver skill fills the fourth loop quadrant](adr/0064-workboard-single-repo-board-driven-driver-skill.md) | Decision reference; read its own status and supersession. |
| [ADR-0065: One agent-skill root contract for framework repos and product workspaces](adr/0065-one-agent-skill-root-contract.md) | Decision reference; read its own status and supersession. |
| [ADR-0066: `Class` is a body line and the board field is its projection](adr/0066-class-is-a-body-line-and-the-board-field-is-its-projection.md) | Decision reference; read its own status and supersession. |
| [ADR-0067: Resolve, don't copy — one skill source of truth, two runtime roots, and a generated view](adr/0067-resolve-dont-copy-one-skill-source-two-runtime-roots-a-generated-view.md) | Decision reference; read its own status and supersession. |
| [ADR-0068: the engine tool manifest leaves kit ownership, and #1077's invariant is asserted instead of arranged for](adr/0068-the-engine-tool-manifest-leaves-kit-ownership.md) | Decision reference; read its own status and supersession. |
| [ADR-0069: Fable lockstep is a profiled Game.Core package contract](adr/0069-fable-lockstep-is-a-profiled-game-core-package-contract.md) | Decision reference; read its own status and supersession. |
| [ADR-0070: Restore is a precondition of the repo being usable — a receiver commits no skill content, and both runtime roots are generated](adr/0070-restore-is-a-precondition-no-committed-skill-content-in-a-receiver.md) | Decision reference; read its own status and supersession. |
| [ADR-0071: Two web workspace providers, one template package](adr/0071-two-web-workspace-providers-one-template-package.md) | Decision reference; read its own status and supersession. |
| [ADR-0072: Console and Fable bindings are separate workspace providers](adr/0072-console-and-fable-bindings-are-separate-workspace-providers.md) | Decision reference; read its own status and supersession. |
| [ADR-0073: Plain HTTP with explicit versioned DTOs replaces Fable.Remoting for `fable-game`'s typed request/response](adr/0073-plain-http-with-explicit-dtos-replaces-fable-remoting.md) | Decision reference; read its own status and supersession. |
| [ADR-0074: Live prose citations are local-file contracts](adr/0074-live-prose-citations-are-local-file-contracts.md) | Decision reference; read its own status and supersession. |
| [ADR-0075: Executor identity is a capability the process holds, not an attribute of it — a per-receiver operation lock takes the item CAS on a third subject, and one exported rule orders both](adr/0075-per-receiver-operation-lock-and-lease-free-election.md) | Decision reference; read its own status and supersession. |
| [ADR-0076: Agent-authored F# specification kernel, piloted in S.I.R.](adr/0076-agent-authored-fsharp-specification-kernel.md) | Decision reference; read its own status and supersession. |
| [ADR-0077: Quint-first Typed SDD authority with a generated FS-GG contract](adr/0077-quint-first-typed-specification-authority.md) | Decision reference; read its own status and supersession. |
| [ADR-0078: GitHub Substrate v2 is a new-only coordination authority](adr/0078-github-substrate-v2-new-only-coordination-authority.md) | Decision reference; read its own status and supersession. |
| [ADR-0079: One accountable owner authorizes delivery](adr/0079-single-accountable-delivery-authority.md) | Decision reference; read its own status and supersession. |
| [ADR-0080: Scope child-unit qualification and qualify milestone closure comprehensively](adr/0080-scoped-child-qualification-comprehensive-milestone-closure.md) | Decision reference; read its own status and supersession. |
| [ADR-0081: Adapt qualification cadence from observed cost and defect yield](adr/0081-adaptive-qualification-cadence-from-observed-cost-and-defect-yield.md) | Decision reference; read its own status and supersession. |
| [ADR-0082: Durable private content-addressed telemetry receipts](adr/0082-durable-private-content-addressed-telemetry-receipts.md) | Decision reference; read its own status and supersession. |
| [ADR-0083: Human-authorized synthetic lifecycle checkpoints](adr/0083-human-authorized-synthetic-lifecycle-checkpoints.md) | Decision reference; read its own status and supersession. |
| [Architecture Decision Records (cross-repo)](adr/README.md) | ADR catalogue/template; no separate programme. |
| [ADR-NNNN: <title>](adr/template.md) | ADR catalogue/template; no separate programme. |

## Lightweight upkeep

When a design is added, moved, folded into another design or deliberately deferred, update its row and
the master only where their navigation or synthesis changes. Preserve a successor link and the reason
for historical disposition. Recheck corpus coverage during a portfolio review; do not add a required CI
gate, board item, per-change report or new machine-consumed registry for this inventory.

Before publishing links to local drafts, carry those drafts with the intended documentation change or
replace the links with an explicit unavailable-draft note. Never silently drop their subjects or claim
they were accepted. The inventory’s dated counts describe this census, not a permanently enforced invariant.
