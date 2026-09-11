# SVG game engine foundation

Feature identity: **SVG-FOUND-01**.

Status: **Portable scene contract and package closure proven; retained SVG browser adapter is next**.

Planning baseline: `.github` main `4d84fd07a71fcff3409cea2915f22b6d664b5431`, 2026-09-11.

Unified part: **SVG game engine foundation and Fable preview**, in the
[section 9.8 feature index](../2026-09-07-154210-fs-gg-unified-development-roadmap.md#98-feature-parts-and-subroadmap-index).
Stage: **section 15 independent producer/product track**, alongside V0–V1.
Accountable planning owner: `.github`; implementation owners are Rendering,
Templates and S.I.R., with Game consulted on its existing contracts.

This is the executable foundation window of the
[SVG engine programme](../2026-09-07-064259-svg-game-engine-template-design-roadmap.md), following its
[revision rationale](../2026-09-07-121207-svg-game-engine-roadmap-revision-proposal.md). It covers selected
M0–M2 work and early M9/M10/M11 preparation. It does not replace C01–C20, close whole original milestones,
or make the revision analysis another completion ledger. These checkboxes own only the bounded foundation
outcomes; the original programme retains comprehensive capability and release claims.

## Outcome and scope

Establish a small, portable retained SVG scene that a neutral Fable consumer and a S.I.R. adapter can both
use. A developer can select a semantic object by pointer or keyboard, pan and zoom, update a scene revision,
and preserve root/layer identity. A continuous-coordinate fixture and a grid fixture share the public scene
surface.

The ready window ends with source behavior, isolated candidate-package consumers and a reviewable preview
release/adoption packet. Protected producer or template publication and activation are outside this window.
Neither local package installation nor source merge is reported as a published preview.

Include stable identities, basic groups/shapes/styles, explicit transforms, bounded selection and revision
handling, accessible DOM alternatives, Fable package closure, donor characterization and early
performance/startup observations. Choose the small SVG subset in SVG-FOUND-01.1 and give unsupported elements
an explicit result.

Full vector editing, workspace docking and key sequences, physics, audio, saves, replay, planners, rule
exploration, multiplayer expansion and complete SVG fidelity remain later programme outcomes. Their required
availability is unchanged.

## Evidence to reuse and gaps

The following are inspected source and recorded evidence, not fresh passing runs. Refresh affected revisions
when implementation starts; do not infer installed behavior from a source branch.

| Source | Evidence and reuse | Gap relevant to this window |
|---|---|---|
| Rendering `86102999e7f60a494bed74e825e36b284fef6d62`: [`Scene`](https://github.com/FS-GG/FS.GG.Rendering/tree/86102999e7f60a494bed74e825e36b284fef6d62/src/Scene), [`KeyboardInput`](https://github.com/FS-GG/FS.GG.Rendering/tree/86102999e7f60a494bed74e825e36b284fef6d62/src/KeyboardInput) | Implemented Scene vocabulary, inspection/codec surfaces and keymap mechanism. Scene has no direct native project dependency; KeyboardInput references Scene. | Whole-package Fable compatibility and retained SVG rendering are unproven. Rendering's Elmish project references SkiaViewer, so it is not automatically the browser adapter. Prefer reuse or a documented adapter over a second Scene vocabulary. |
| Game `24f79084fdd289f34387f91b1d4398c78fde16eb`: [`Game.Core` package project](https://github.com/FS-GG/FS.GG.Game/blob/24f79084fdd289f34387f91b1d4398c78fde16eb/src/Game.Core/FS.GG.Game.Core.fsproj) | Existing pure simulation substrate and curated Fable profile. | Packaged Fable source selects Primitives, Pathfinding, Edges and Los. .NET fixed-step, collision and input APIs are not automatically qualified browser capabilities. This foundation does not require widening that profile. |
| Templates `61091078337689c6ab1aac139bc03f6a07ca8f99`: [`fable-game`](https://github.com/FS-GG/FS.GG.Templates/tree/61091078337689c6ab1aac139bc03f6a07ca8f99/templates/fs-gg-fable-game) | Implemented Fable/Elmish arena, explicit HTTP DTOs, SignalR, locked dependencies, cross-runtime codecs and browser tests; Game.Core pin remains 0.13.0. | Reusable SVG producer consumption and foundation-specific fresh/upgrade journeys are missing. Preserve transport ownership and existing regression coverage. |
| [Registry publication record](../../registry/dependencies.yml) | Records Workspace.Template 0.10.0 publication and installation evidence, and published wizard 0.10.1 selection. | The template provider descriptor's “not registry-active” comment is stale. Reconcile it against release/registry evidence; do not recreate an already delivered activation. |
| S.I.R. `80e1ac9328865ec8d1ee3eeea130560ef22b1b01`: [`App.fs`](https://github.com/EHotwagner/S.I.R./blob/80e1ac9328865ec8d1ee3eeea130560ef22b1b01/src/SIR.Client.Web/App.fs), [`projection signatures`](https://github.com/EHotwagner/S.I.R./blob/80e1ac9328865ec8d1ee3eeea130560ef22b1b01/src/SIR.Client/TacticalSceneProjection.fsi), [`integration report`](https://github.com/EHotwagner/S.I.R./blob/80e1ac9328865ec8d1ee3eeea130560ef22b1b01/docs/issue-138-fable-game-integration-report.md) | Implemented retained tactical SVG and real template adoption, with product-owned gameplay/disclosure. The S.I.R. copyright owner authorized reuse of its code for this programme without AGPL conditions on 2026-09-11. Reuse its acceptance and performance fixtures for characterization. | Generic extraction is not demonstrated. Inventory third-party contributions, dependencies and assets before copying or redistribution; the owner authorization does not determine their separate terms. |
| SDD source `8d648c8deaf1edc16b942d0cfccee722c3a0a24c`, [original section 8A](../2026-09-07-064259-svg-game-engine-template-design-roadmap.md#8a-quint-first-specifications-and-v2-upgrade-compatibility) | Existing consumer-profile, extraction and correspondence direction; local SDD head records a 1.6.0 release change. | Observe exact installed backend/profile/tool support for the selected engine behavior. A release commit or historical 1.5.0 document is insufficient installed qualification. |

Ownership follows [ADR-0022](../adr/0022-extract-fs-gg-game-as-an-sdd-driven-component.md),
[ADR-0028](../adr/0028-keyboard-input-config-mechanism-policy-boundary.md),
[ADR-0069](../adr/0069-fable-lockstep-is-a-profiled-game-core-package-contract.md),
[ADR-0071](../adr/0071-two-web-workspace-providers-one-template-package.md) and
[ADR-0073](../adr/0073-plain-http-with-explicit-dtos-replaces-fable-remoting.md).

Rendering owns generic scene/SVG/input mechanisms. Game retains simulation and game policy; its core gains no
Rendering dependency. Templates composes producer artifacts. S.I.R. retains combat, disclosure and tactical
adaptation. Net owns its existing transport contracts, while Audio is outside the foundation closure. SDD owns
model tooling, lifecycle and materialization; `.github` owns cross-repository decisions and indexing.

## Independence and actual prerequisites

| Boundary | Required now or before dependent work | Later activation condition |
|---|---|---|
| Repository source delivery | Current authorized routine route, affected owner contracts and focused native checks | No V0–V6 completion prerequisite |
| S.I.R. donor code | Owner authorization is recorded; inventory third-party contributions, dependencies and assets before affected copying or redistribution | Publication preserves any third-party license and notice obligations found by the inventory |
| Portable/public contracts | Scene comparison, browser closure proof and an accepted decision for any changed responsibility | Published producer artifacts before external consumer adoption |
| New modeled behavior | Owning bounded semantic source, supported installed profile/toolchain and real correspondence for the changed behavior | Future common-lifecycle default adoption is separate |
| Fable template preview | Current supported provider/template path; explicit opt-in candidate consumption | Exact producer publication, template publication and affected receiver adoption |
| Coordination/default change | None is introduced by this foundation | Actual GS2 candidate/freeze boundaries, OpenV2/OperatingV2 and SDD/Templates decisions apply only to operations that require them |

Unfinished R5 cohort evidence, v2 bridge rollout, OR/PB, hosted execution, scheduling, cooperation and fleet
migration do not block this source lane. A change touching bound cutover inputs must still be reconciled with
that candidate's owner; independence does not exempt frozen inputs.

Routine is the process route for every milestone. Only an explicit human instruction selects heavyweight
process for named scope. Modeled semantics, public API checks, native checks and protected release authority
remain substantive requirements within that route.

## Ready foundation window

- [x] **SVG-FOUND-01.1 — Establish an executable extraction boundary — route: routine**

  Depends on: none.

  Scope: `.github` coordinates a bounded Rendering/Game/Templates/S.I.R. audit. Record exact donor paths,
  owner-authorized S.I.R. reuse and any third-party terms, existing Scene/input overlap, a minimal SVG subset,
  package/browser dependency closure, two consumer fixtures and the installed SDD/profile tool result needed
  by the selected stateful behavior. Record an ownership decision only where the proposed cut changes accepted
  responsibility.

  Acceptance: a small grid projection and free-coordinate projection have a documented route into the shared
  surface; unsupported Scene cases are explicit. Existing template creation/build commands and baseline
  identities come from their actual owner. A toolchain gap has an exact failing or unavailable observation and
  owning seam rather than a request to finish v2. Third-party material with unresolved terms is excluded from
  copying until resolved. This slice need not inventory all C01–C20 capabilities.

  Evidence: the [extraction-boundary audit](../reports/2026-09-11-svg-game-engine-extraction-boundary.md)
  binds the five inspected revisions, minimal supported and rejected SVG cases, Scene/input ownership cut,
  browser package closure, grid and free-coordinate fixtures, current template identities and commands,
  installed profile-2 refusal, and third-party exclusion. The S.I.R. M0 static fixture passed; the performance
  contract test was unavailable because the locked Playwright dependency is not installed. Delivered by the
  [routine SVG-FOUND-01.1 PR #3422](https://github.com/FS-GG/.github/pull/3422).

- [x] **SVG-FOUND-01.2 — Prove the portable scene contract and package closure — route: routine**

  Depends on: SVG-FOUND-01.1's ownership, third-party provenance and supported-tool decisions.

  Scope: Rendering implements the smallest reusable contract/adapter and Fable package path. Reuse existing
  Scene types where possible. Game changes only if an actual existing contract gap is demonstrated.

  Acceptance: isolated .NET and Fable consumers compile the selected public surface from locally packed
  candidate artifacts without sibling source links or native browser dependencies. Both fixtures preserve
  stable semantic IDs and transform meaning. Unsupported and non-finite inputs have tested outcomes. Newly
  introduced revision/selection semantics have bounded model witnesses and real reducer correspondence; a
  stale-revision mutation is detected. This does not claim full Scene Fable support or published consumption.

  Evidence: [Rendering PR #1279](https://github.com/FS-GG/FS.GG.Rendering/pull/1279), merged as
  [commit `646817c8`](https://github.com/FS-GG/FS.GG.Rendering/commit/646817c847e034e31ff2e6a2da91e7b847a8eb8b),
  delivered the [portable Scene evidence report](https://github.com/FS-GG/FS.GG.Rendering/blob/646817c847e034e31ff2e6a2da91e7b847a8eb8b/docs/reports/2026-09-11-svg-found-01-2-portable-scene.md),
  curated Fable package surface and isolated .NET/Fable consumers. Scene tests passed 94 cases; bounded Quint
  exploration ran 10,000 traces with current- and stale-selection witnesses, and the real reducer correspondence
  suite detected the stale-revision mutant. No package was published.

- [ ] **SVG-FOUND-01.3 — Render and interact through a retained SVG root — route: routine**

  Depends on: SVG-FOUND-01.2.

  Scope: Rendering browser adapter and focused browser fixtures; begin original M9 measurement here.

  Acceptance: pointer and keyboard selection identify the same object, with an accessible DOM route. Anchored
  zoom/pan and inverse picking work at representative transforms. Updating one object retains the SVG root and
  unaffected layer identities; stale revisions cannot replace current content. Repeated mount/dispose leaves
  no owned event listeners or frame work. Record browser/device, scene cost, startup bytes and input-to-paint
  observations with unavailable metrics explicit. These measurements are a foundation baseline, not complete
  M9 qualification.

- [ ] **SVG-FOUND-01.4 — Exercise neutral and S.I.R. consumers — route: routine**

  Depends on: SVG-FOUND-01.3.

  Scope: Templates adds an explicitly selected foundation sample/fixture through existing composition seams;
  S.I.R. adds the bounded product projection adapter. Use candidate packages for integration; repository-local
  references may assist development but do not satisfy acceptance.

  Acceptance: the neutral fixture renders grid and continuous coordinates without S.I.R. types. The S.I.R.
  fixture preserves disclosed identities, retained layers, camera and relevant selection/focus behavior against
  its characterized baseline. Hidden facts remain absent from the scene and accessible projection. Existing
  arena transport and browser regressions remain passing. Superseded donor production code is removed only
  after supported package adoption; an interim adapter selects one implementation explicitly.

- [ ] **SVG-FOUND-01.5 — Make the preview release and receiver change reviewable — route: routine**

  Depends on: SVG-FOUND-01.4.

  Scope: Rendering and Templates prepare compatible candidate artifacts, release ordering, exact source/payload
  identities and a bounded consumer/adoption packet; S.I.R. owns its upgrade fixture. `.github` records only
  necessary contract/pin effects.

  Acceptance: an isolated directory with no sibling checkout installs the locally packed candidate template,
  restores locks, builds and serves the selected foundation scene. A separately retained older workspace adopts
  the documented package/config delta without destructive scaffold rerun, preserves authored files and reports
  conflicts. Effective owner guidance and lifecycle selection are observed; existing skills are not presumed
  refreshed by backfill. Local rehearsal, producer publication pending, template publication pending and
  installed public qualification pending remain distinct results.

  The packet names candidate versions only after compatibility review, required feeds, the supported preview
  selection mechanism, release owner, consumer pins, rollback limits and remaining authority. Preparing it does
  not publish, activate the registry, deploy or change a lifecycle/default.

## Generated-workspace impact

Apply [unified section 9.9](../2026-09-07-154210-fs-gg-unified-development-roadmap.md#99-when-new-workspaces-change).

Affected family: explicitly selected `fable-game` / `fs-gg-fable-game` foundation preview, initially through
its supported existing lifecycle configuration. The neutral `web`, rendering provider and omitted
lifecycle/default selection do not change.

Before: the supported template supplies the current server-authoritative grid arena. After explicit preview
adoption: the selected sample also uses a reusable retained SVG scene with pointer/keyboard selection and camera
interaction. It is not the complete studio or cooperative-arena capability claim.

SVG-FOUND-01.4 first changes selected source composition; SVG-FOUND-01.5 first rehearses fresh creation from a
candidate archive. Neither changes installed public scaffold contents. The installed effect begins only when the
exact producer set is published, Templates adopts and publishes those immutable identities, and the selected
direct/provider/wizard receiver consumes that package.

Baseline recorded identities include Workspace.Template 0.10.0 and wizard 0.10.1; the existing game template
pins Game.Core 0.13.0. Future engine and template release identities remain unset until SVG-FOUND-01.5. New
guidance additionally needs its owning skill publication and actual SDD materializer adoption where applicable.

Existing-workspace adoption is separate: preserve authored domain/content, configuration and lifecycle identity;
merge explicit package/config changes, detect conflicts and retain rollback inputs. Publishing a scaffold does
not update existing files. Save/replay migrations enter only when a later slice changes those formats.

## Later outline and stop condition

After SVG-FOUND-01.5, extend this feature with an authorized producer/template publication and installed-consumer
qualification window. Required evidence is the exact candidate set, release route and authority, feed payload
equivalence, template pins, selected receiver identities and clean/upgrade results. Report preview readiness only
for the demonstrated subset.

Subsequent planning expands only the next useful capability window: the remaining M1/M2 surface, M3/M4 authoring
and input, M5 sessions, then their actual M6–M8 dependencies. Full C01–C20 availability, S.I.R. parity, M9
qualification and Release D remain the original programme target.

A default playable SVG starter, studio bundle vocabulary, common Quint lifecycle migration and eventual default
activation need their respective provider/SDD decisions. Any activation requiring OperatingV2 remains dependent
on that authority; compatible current-route preview publication does not inherit that dependency.

Use native owner PR/check/build/browser results and the existing UTEL observation system. Automatic parent/child
and CI population coverage remains incomplete, and platform-native `collaboration.spawn_agent` usage capture is
unsupported. Missing observations remain unknown and do not block valid source delivery or support an efficiency
claim.

Stop this ready window at SVG-FOUND-01.5's reviewable publication boundary. Pause only the affected slice if API
ownership, third-party provenance, installed profile support or required dependency closure cannot be established.
Do not solve such a gap by inventing a lifecycle contract, weakening technical evidence or waiting for unrelated
V0–V6 completion.
