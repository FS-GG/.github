---
title: "Proposal: Revise the SVG game engine roadmap for bounded delivery and v2 compatibility"
category: Design
categoryindex: 4
description: "An analysis of development-flow alignment, engine delivery dependencies, Quint authority, generated-workspace carryover and release qualification, with a concrete revision proposal."
---

# Proposal: Revise the SVG game engine roadmap for bounded delivery and v2 compatibility

Authored: **2026-09-07 12:12:07 UTC**. Status: **proposed revisions for review**.

This records the analysis behind the accompanying prose revision of the
[SVG game engine and Fable template roadmap](2026-09-07-064259-svg-game-engine-template-design-roadmap.md).
Neither document changes an accepted ADR, product behavior, executable policy, provider configuration,
release contract or required gate. The recommendations distinguish proposed design changes from the
subsequent owner decisions and implementation needed to make them real.

## 1. Recommendation and evidence scope

Keep the engine architecture and complete-workspace ambition. Revise the delivery design so that reusable
engine capabilities, optional installed studio tools, workspace lifecycle adoption and coordination cutover
have explicit, separate dependencies. Ordinary development should consume the approved common workflow,
including its eventual simplifications, without acquiring an engine-specific acceptance bureaucracy.

The [bureaucracy-reduction proposal](2026-09-07-074251-radical-development-bureaucracy-reduction-design-and-roadmap.md)
and [merged proposal-alignment analysis](coordination/2026-09-07-112837-development-flow-proposal-alignment-analysis.md)
justify revisiting delivery assumptions. They do not demonstrate that SVG safety, runtime conformance or
engine formal models are expensive or unnecessary. The 68.4% historical migration-driver overhead figure
is not a measured property of ordinary game development, v2 execution or this engine programme.

The evidence here is the local roadmap, its local governing/design references, and the aligned
[orchestration](coordination/2026-08-31-operations-research-first-agent-orchestration-design.md) and
[performance-flow](coordination/2026-09-06-performance-bounded-development-flow-design-and-roadmap.md)
proposals. This is a design and dependency analysis, not a new source-code audit of S.I.R. or the producers.
Their recorded package/source identities remain historical observations. No game, browser benchmark,
published artifact or ordinary v2 journey was executed for this analysis. Runtime findings in the original
roadmap are not re-certified here.

## 2. Findings and priority

| Priority | Finding in the current roadmap | Consequence | Recommended disposition |
|---|---|---|---|
| Necessary | §8A combines engine qualification, workspace-default adoption and fleet-epoch conditions in M11.6 | Readers cannot reliably tell which engine deliverables can proceed independently | Separate publication, product readiness, lifecycle activation and coordination-authority claims |
| Necessary | §§10/13 verify generated artifacts and skills, but not the subsequent ordinary development experience | Generated guidance can silently restore the removed process | Add clean/upgrade workflow journeys using the shared approved routine profile |
| Necessary | §8A says every new behavior module has canonical Quint semantics but does not explain routine implementation changes | Either excessive model/receipt churn or an unjustified model-free shortcut is possible | Distinguish model semantics, implementation correspondence, content parameters and working prose |
| Necessary | §11 requires separately reviewable work packages without defining their tracking/acceptance shape | Work decomposition can become one issue, SDD family and receipt cycle per sub-number | Make a bounded PR a valid implementation unit where the adopted policy permits it |
| Necessary | §§6A/8A append work and gates without fully integrating §11 | The main roadmap omits 0.7, 1.5, 7.5, 10.6 and 11.6; estimates disagree by location | Fold additions into one table and one additive estimate |
| Recommended | M8 depends on all M7, and M7 depends on the editor/workspace milestone M4 | Basic networked gameplay waits on unrelated authoring/analysis features | Express dependencies by required capability; preserve later integrated closure |
| Recommended | M10 requires installed-artifact proof while M11 contains template publication | Candidate qualification and final activation are ambiguous | Qualify exact published candidate artifacts before their separately authorized promotion/activation |
| Recommended | C16 describes an optional module while §4/§13 require complete capability coverage | Optional installation can be confused with optional programme delivery | Distinguish available capability from inclusion in the default player |
| Recommended | §9 has explicit render budgets, while bootstrap and development-flow budgets are only partial or absent | Faster rendering can hide a heavy player bundle or expensive development path | Maintain three separate scorecards with measured thresholds established at the relevant stage |
| Preserve | Four state kinds, owner packages, real consumer tests and bounded replay claims | These prevent donor coupling, disclosure leaks and misleading proof | Keep and use them to structure qualification |

“Necessary” means needed for a coherent revision aligned with the newer proposals; it is not a declaration
that the current executable system is defective. The document contains no requirement for a hosted OR
service or PB controller today. An explicit nondependency sentence is enough; importing their roadmaps or
rewriting the game architecture around them would be unnecessary scope.

## 3. Separate the completion claims

The revised document should name the following claims and their dependencies in its release table:

| Claim | Evidence needed | What it does not imply |
|---|---|---|
| Engine capability preview A/B/C | Named implemented capabilities, compatible package closure, applicable product/model tests and real installed consumer | Complete studio, future workspace defaults or v2 operational acceptance |
| Complete game/studio capability set | All required C01–C20 functionality, selected-bundle journeys, S.I.R. parity, performance/accessibility and data compatibility | Authority to flip workspace lifecycle or coordination epoch |
| Generated workspace integration | Published producer capabilities, explicit backend/profile, installed skills and build/evidence adapters, tested upgrade | All historical workspace modes have migrated or all fleet writers may switch |
| Lightweight development carryover | Actual supported routine-change journey, effective published guidance, proper operating authority and separately sufficient cost evidence | A new semantic authority, an exemption from technical checks or proof of rare-defect equivalence |
| Lifecycle/default activation | The owning SDD/Templates decisions, required receiver qualification and applicable operating epoch | Permission inferred from successful engine compilation or game qualification |

Preserve release D as the complete target workspace when all its promised product and workspace conditions
are met. If game capabilities finish while the lifecycle/default programme is pending, report the precise
partial result and ship only through an already-supported release route. Do not relabel that result as D.
This separates useful delivery from the full claim without quietly reducing the requested end state.

M11.6 should therefore distinguish compatible template publication from lifecycle-changing activation. A
content/default-example change and a lifecycle-default flip are different operations, even if one release
could carry both. For each, identify the actual contract and owner rather than use “applicable fleet epoch”
as an unexplained blanket dependency. Preserve the documented OperatingV2 condition for the future
workspace-default flip and the refusal of fenced legacy writers after OpenV2.

## 4. Preserve one Quint authority with proportional change handling

The [single Typed SDD lifecycle](design-goals/single-typed-sdd-lifecycle.md) and
[Quint-backed workspace](design-goals/quint-backed-workspaces.md) goals deliberately put framework and
consumer workspaces on the same semantic base. Reducing authoring ceremony cannot mean creating a weaker
engine-only workspace architecture. [ADR-0077](adr/0077-quint-first-typed-specification-authority.md) and its
[Q1 amendment](coordination/2026-08-26-adr-0077-q1-qualification-amendment.md) remain the relevant authority
references, not a locally invented “freeform game” backend.

Add a change-handling explanation to §8A, expressed as proposed behavior of the published tooling:

| Change | Model treatment | Implementation/technical evidence |
|---|---|---|
| Existing behavior implemented differently, such as fixing stale worker rejection | Preserve accepted semantics when they already specify the intended result | Re-run affected real conformance and regression checks on the changed implementation; reuse formal evidence only under its accepted subject rules |
| New rule, command precedence, session transition or data-compatibility behavior | Amend the owning bounded model and relevant bindings/assumptions | Readable semantic diff, applicable formal qualification and real runtime correspondence |
| Art, layout or content values within an accepted schema and parameter envelope | Validate against that declared envelope; do not invent a new protocol model for every instance | Content validation, applicable visual/accessibility checks and asset provenance; gameplay-bearing content retains the required identity |
| Content changes beyond the accepted envelope or with new behavior | Treat as a semantic change, not decorative data | Update the appropriate specification/contract and qualify its consumer behavior |
| Prose or presentation metadata with no behavioral change | Retain source provenance and establish semantic equivalence through supported extraction/impact rules | Relevant documentation/projection checks; never re-sign historical evidence as if it were a new execution |
| Unknown impact or changes to model tooling, evidence rules or authority | Use the strongest applicable accepted route until impact is resolved | No path-only assumption of semantic equivalence and no self-edited eligibility rule |

This is not an instruction to skip a model update based on a file extension. Changed F# can violate an
unchanged model; it still needs new implementation evidence. An unchanged behavioral subject can permit
formal-result reuse while the terminal candidate and conformance observations change. These are separate
identities and should be described separately in §8A's evidence paragraph.

The future ChangeProposal remains a producer-owned contract. A routine PR could carry its model binding
and readable semantic delta through existing generated mechanisms, if the published producer supports
that shape. A separate issue, second PR or hand-authored no-change receipt is not inherently required by
having one semantic authority. However, the engine plan cannot assume that such a lighter shape is already
supported: identify any gap under the existing SDD programme, rather than inventing a template-local schema.

Keep the seven bounded model areas in §8A. They address real stateful behavior, including undo, visibility,
cancellation, ordering and replay compatibility. Do not replace them with one huge engine model, require a
new model for every patch, or force numerical geometry and browser paint into a control-state abstraction.
The existing native tests for those claims remain necessary.

## 5. Make generated development behavior a deliverable

Source-level guidance inspection is insufficient. Add a small installed-workspace journey to M10 and its
upgrade counterpart to M11, reusing the shared R5 acceptance examples and observation machinery.

The proposed journey is:

1. Install the relevant published template/bundle through a supported direct or SDD entry point.
2. Make a bounded routine change, such as correcting a visible label or an implementation bug under an
   existing behavioral model. Exercise a semantic rule change separately to prove the stronger path.
3. Observe the effective policy and owner-sourced guidance actually resolved by that workspace. Complete
   the routine change with the approved owner/PR/check pattern; no hidden receipt-only PR, phase ledger or
   extra authorizer may be introduced by the template.
4. Change the source candidate and perform repair on the same PR. Exercise unrelated base advancement
   under the chosen supported merge policy; technical evidence remains correctly bound to its subject.
5. Withhold usage telemetry and delay a derived board/roadmap view in an isolated test. Valid native code
   delivery remains delivered; the missing sample cannot certify an overhead result.
6. Repeat on an upgraded workspace, preserving authored rules, content, saves, keymaps and lifecycle identity.
7. Verify that a protected authority/security/migration change selects its proper route, and that a v2
   receiver cannot fall back to a fenced v1 writer.

A networked sample's required server deployment is a separate deliverable from its source merge. A
PR-less operation example should cover an actually supported operation, not force the game template to
implement a general release controller merely to satisfy the test.

Use representative coverage of distinct entry points, materializers, policy configurations and migrations.
Share generic SDD/Coordination qualification where its declared subject actually covers the receiver;
add engine-specific observations for its emitted artifacts and behavior. Neither one happy-path installation
nor a gratuitous full Cartesian product of browsers, providers, agents and lifecycle modes is a sound default.

One functional journey establishes integration behavior, not the 10–20% overhead claim. That claim uses
the shared R4/R5 cohort, usage coverage, conservative unknown bound and delayed-repair follow-up. The
engine should join that evidence where applicable, not create its own telemetry receipt family.

## 6. Revise the dependency sequence while preserving capability scope

The current table makes M8 wait for M7, whose scope includes replay, timeline, planner and rule explorer.
M7 in turn waits for M4's workspace/editor. Some of those are integration dependencies, not prerequisites
for starting server input ordering or reconnect work. The reported “critical path” also lacks measured
stage durations and resource constraints; call it a proposed dependency spine until M0 can calculate one.

Retain existing milestone and work-package identities. Refine their dependencies instead of replacing them
with a new hierarchy:

| Work | Proposed dependency refinement | Later integration requirement retained |
|---|---|---|
| M0 audit | Audit enough rights, ownership, API overlap and package closure to authorize the first extraction; deepen later capability audits before their decisions | Every C-row classified before claiming complete inventory/product scope |
| M1/M2 first vertical slice | Publish/consume one portable scene and a small interactive example in a neutral consumer and S.I.R. | Full scene API, SVG subset and accessibility qualification remain M1/M2 outcomes |
| M5 session core | Follow portable scene/session/input contracts; use a minimal content snapshot without waiting for the full vector editor | Complete input, collision and local/server semantics at the applicable feature boundary |
| M7.1 replay core | Follow session identity, admitted inputs and snapshot contracts; add M6 persistence dependencies when durable save/replay integration is involved | Real cross-runtime correspondence and divergence diagnosis |
| M7.2–7.4 timeline, planner and rule explorer | Integrate with the relevant M4 workspace facilities and the runtime/replay core | Tactical and neutral authoring/analysis journeys remain part of the complete scope |
| M8.1–8.3 network host, ordering and reconnect | Begin after the required M5 session/transport contract; do not wait for the entire analysis UI | M8.4 saved replay waits for M7.1 and applicable persistence; integrated two-browser proof remains mandatory |
| M9 measurement | Start the trace harness with the first M2 browser slice; apply resource and frame observations throughout | Full effects/network/replay workload matrix after the relevant M6–M8 features |
| M10 composition | Begin minimal generated-consumer plumbing with the first candidate packages | Final complete-bundle and upgrade qualification waits for all included features |
| M11 release/adoption | Publish compatible producer candidates in bounded batches for real consumers; qualify exact candidate template artifacts before activation | Final coherent-set, required-feed and consumer verification remains intact |

This is a proposed scheduling improvement, not proof that those APIs are already independent. M0 should
validate each dependency from source and contracts; retain a genuine coupling if it exists. Do not claim
that adding more agents automatically makes the programme faster.

The first useful demonstration can be a small interactive SVG scene using published package candidates.
It need not include audio, studio editing and multiplayer merely because the eventual flagship example
does. Keep the cooperative arena, tactical example and arcade example as end-state tests with their full
promised behavior. Early slices provide evidence toward that scope, not replacements for it.

## 7. Clarify optionality, packaging and publication

“Optional” currently mixes three meanings: a developer can omit a module, a player bundle excludes it, or
the programme need not deliver it. Make those independent columns or explicit notes in the capability table.
For C16 and the planner/rule-lab bundle, the recommended interpretation is **required availability in the
complete workspace, optional installation/loading in an individual product**. If the programme intends to
defer delivery itself, that is an explicit scope decision rather than an inference from the word optional.

Retain the player/studio dependency separation. The engine's rule explorer, timeline and SVG tooling are
product features; the decision to defer an orchestration dashboard says nothing about their value. Likewise,
gameplay command scheduling, input ownership and worker generations are not development ceremony.

Clarify release phases using existing producer mechanisms:

- Publish exact candidate producer artifacts through their authorized release route, including applicable
  required feeds, before downstream consumers depend on them.
- Build and qualify the candidate template from those immutable inputs. For the installed-template journey,
  publish the exact candidate template under an approved preview mechanism before testing its installation.
- Record which candidate identities passed which journey; final promotion, publication or activation uses
  the required release semantics. If promotion changes artifact identity or bytes, requalify the affected
  claim rather than assume the preview evidence automatically transfers.
- Batch compatible work where it forms one useful release. Do not publish every internal primitive merely
  because a work-package number changed, and do not let batching conceal incompatible API changes.

These clarify a potentially circular reading of M10/M11; they do not assert that no candidate publishing
mechanism exists today. Existing dual-feed, independent payload verification, package axes, pinned
consumption and registry obligations stay intact. The bureaucracy proposal's single-feed option is not
permission for this cross-repository engine release to weaken its current external contracts.

## 8. Three measurements and one coherent estimate

Keep separate scorecards:

| Scorecard | Measures and responsibility | Qualification meaning |
|---|---|---|
| Game runtime | Input-to-paint, frame behavior, scene work, memory, audio/worker lifecycle and declared devices; Rendering/Game own relevant surfaces | §9's product-performance claims, including unavailable-stage disclosure |
| Installed product/startup | Player and studio dependency/chunk closure, delivered bytes, cold start and first playable interaction; Templates with producers | Small playable default and optional tooling do not hide mandatory heavyweight dependencies |
| Development flow | Whole-unit overhead, cost per delivered unit, delivered fraction, active/wait time and attributed repairs; shared policy/observer owners with receiver observations | The approved routine-profile experience and separately sufficient R4/R5 efficiency evidence |

Add startup measurements to the M0/M2 baseline and set justified thresholds before final qualification.
Do not invent download or startup limits here without a reference device, hosting/cache assumptions and
measurement. The 100/150 ms interaction targets, 60 Hz aspiration, 20% culling-cost comparison and 20%
development-overhead ceiling have different denominators; they cannot substitute for one another.

Routine overhead includes planning, review, coordination, reporting and process repair, including shared
flow-tool maintenance. Preserve meaningful technical testing and product investigation; do not relabel them
or move all hard items outside the cohort to manufacture a passing ratio. The historical migration figure
cannot be multiplied into the engine effort estimate to predict savings.

Consolidate the current planning allowances in §11:

| Component | Existing allowance, engineer-weeks |
|---|---:|
| Original engine scope | 44–74 |
| Expanded keyboard/input scope | +4–7 |
| Formal modeling, correspondence and migration qualification | +4–6 |
| Current additive total | **52–87** |

This arithmetic preserves the existing allowances; it is not a re-estimate or a forecast. Identify overlap
between runtime tests, conformance adapters and the additions before changing the total. Model-authoring
and correspondence time often contribute directly to product correctness and should not all be labeled
bureaucracy. Report shared-flow overhead and externally imposed waiting separately. Calendar duration also
requires owner capacity, dependency overlap and release/adoption windows; dividing by agent count is not a
schedule. Make the original and intermediate totals historical notes rather than competing headline values.

## 9. Concrete document revision plan

Use one coherent prose revision of the original roadmap, retaining stable C/M identifiers and existing
capability promises. The analysis should remain a rationale reference, not a second dispatchable roadmap.

| Original section | Proposed edit | Owner decision needed before implementation |
|---|---|---|
| Opening and §1 | Add the distinction between product capability, workspace integration and lifecycle/default activation; state no dependency on OR/PB completion | None for proposal clarification; actual activation remains owner-authorized |
| §3 ownership | State who publishes routine policy, who implements v2 semantics, and who verifies generated receiver behavior; use existing ownership, no new service | `.github`, Coordination, SDD and Templates resolve any unsupported consumer seam |
| §4 C01–C20 | Clarify capability availability versus bundle inclusion; map rows to shared end-to-end journeys | Explicit scope decision only if a promised capability would actually be deferred |
| §8A authority/evidence | Add the change-handling distinctions and cheap automatic evidence reuse; retain semantic/model and candidate/conformance identities | SDD owns any ChangeProposal or evidence-contract amendment |
| §8A activation | Separate engine compatibility from workspace-default and coordination transitions; preserve exact epoch constraints | Relevant accepted SDD/Coordination policy before changing real defaults |
| §9 performance | Add startup/bundle observations; distinguish browser performance from development economics | Producers/Templates establish measured thresholds before qualification |
| §10 skills/template | Consume effective published routine policy; prevent contradictory generated guidance; keep optional tool loading explicit | Materializer/provider changes through their existing producer ownership |
| §11 roadmap | Fold 0.7/1.5/7.5/10.6/11.6 into the main table; refine dependencies and tracking language; consolidate 52–87 estimate | Actual dependency/estimate validation during M0 |
| §12 publication/migration | Describe exact candidate publication, installed qualification and final activation; batch compatible work | Existing release contracts remain authoritative; no single-feed assumption |
| §13 acceptance | Add routine-change/upgrade/protected-route observations and distinguish product readiness from activation | Reuse shared R5 evidence where applicable; do not change GS2 gates here |
| §§14–15 risks/handoff | Add hidden workflow reintroduction, lifecycle coupling and broad milestone dependency risks; stage M0 around a useful first slice | No new issue/board family solely for this prose revision |

Suggested replacement for the work-package premise:

> Work packages identify bounded implementation and integration outcomes. Where the adopted delivery policy
> permits, one owner and one PR provide durable tracking and technical evidence for a useful change. A
> sub-number does not inherently require another intake issue, SDD artifact family, acceptance PR or
> projection task. Cross-repository contract and release obligations remain explicit, and technical or
> migration milestone acceptance retains its applicable qualification strength.

Suggested addition to the generated-workspace premise:

> The template consumes the common published workspace authority and approved routine-development profile.
> Authoring depth and optional studio features do not select weaker semantic authority. Clean and upgraded
> receivers demonstrate the effective policy through a real ordinary change; a source checkout or a green
> game build alone cannot establish that the generated development experience is correct.

These paragraphs describe the proposed target. They should sit beside explicit current-versus-proposed
status, not masquerade as already-supported implementation instructions.

## 10. Decisions, tradeoffs and completion of the revision

Recommended decisions now: retain the full capability target and common Quint base; separate completion
claims; make the routine path an explicit generated-consumer outcome; refine dependencies at existing
work-package granularity; use exact candidate artifacts; consolidate estimates. These improve coherence
without requiring a new runtime or granting a policy exception.

Decisions that remain with their owners: whether a PR-native ChangeProposal shape is supported or needs a
producer change; exact default-activation dependencies; final package boundaries and startup budgets; any
reduction to generic closure/review/release guarantees. [ADR-0080](adr/0080-scoped-child-qualification-comprehensive-milestone-closure.md),
[ADR-0081](adr/0081-adaptive-qualification-cadence-from-observed-cost-and-defect-yield.md) and the accepted
v2 authority/cutover requirements are not amended by an engine roadmap. Keep those decisions out of a
routine documentation exception.

The main tradeoff is earlier useful delivery with more explicit partial readiness. This requires clear
release claims so users cannot mistake a preview for the complete workspace. Dependency refinement may
expose integration gaps sooner; retain comprehensive applicable closure rather than assume independent
children necessarily compose. Shared evidence reduces duplication only when its subject covers the
receiver. Optional tools must remain available and qualified where the full product promises them.

The document revision is complete when the main table contains all additions, every activation prerequisite
has a named purpose/owner, one authority model remains clear, installed routine behavior has concrete
examples, estimates agree, and no future proposal is presented as present capability. This is document
coherence. Product implementation, published-artifact qualification, actual routine adoption and v2
operational acceptance remain separately demonstrable outcomes.
