# SVG coherence, shared implementation and repeated-model performance

Date: 2026-10-04. Status: active; milestones .1 and .2 delivered, .3 in progress.
Shared implementation, receiver adoption and host-browser acceptance remain open;
Rendering 0.32.1 package publication is accepted. Host GPU tests require
active human intervention and are batched at the latest practical qualification
boundary, after autonomous preparation and container validation. Proposed identity:
**SVG-COHERENCE-01**. Rendering owns the reusable implementation and performance
research; Game owns simulation; Templates owns the reference composition; product
owners own migration. The programme integrator owns cross-repository joins.

Make the SVG stack discoverable and consistently reused, then qualify hundreds of
independently changing instances of realistic game models. Preserve the existing
SVG implementation and its valid performance evidence. Close demonstrated gaps
without creating a second renderer or moving simulation into the presentation layer.

This plan extends the [accepted SVG programme](../2026-09-07-064259-svg-game-engine-template-design-roadmap.md)
and [scale qualification](svg-scale-01.md). Coordinate migration with
[FABLE-ADOPT-01](2026-10-01-staged-fable-game-adoption.md); do not create a competing
product-adoption sequence or mark its remaining outcomes complete. The new instance
workload does not retroactively invalidate historical acceptance for other workloads.

## Current progress

**2026-10-07: Public Templates-only receiver accepted.**
[Run 37549683984/a1](https://github.com/FS-GG/FS.GG.Templates/actions/runs/37549683984)
succeeded on repaired harness `c56541c6dbadbc753b500ac5ffc941e8af083854`.
Artifact `11452437787` retains a passed `templates` qualification: public Template
0.18.1 at original source/tag `d9fe65ea8a456f59d663f20c647a38a195e13c2c`, archive
`0d9395b028f14b2c06afe1f1de019610217774f0cb8b6a9618f2aed7d51790fe`, descriptor
`1e57e732230bb8e0b17d5a63efe218dcbc4ee49ad4983aa6db5ab43f4dc1e835` and SDK 2.1.0.
Direct and provider Chromium FourD journeys each passed. No new operation was
launched during this readback. Full-mode Wizard qualification remains pending;
its frozen coherent dependencies and successor publication require separate gates.
Earlier receiver failures remain retained. Full .8 acceptance is still open.

**2026-10-06: Published-template receiver harness repaired and delivered.**
[Templates #677](https://github.com/FS-GG/FS.GG.Templates/pull/677) merged exact head
`196a843cf4af0c97300e8ea66a9ad0e7badec03f` as
`c56541c6dbadbc753b500ac5ffc941e8af083854` at 22:58:52 UTC, with eleven checks passed
and four skipped. [Composition 37541790067/a1](https://github.com/FS-GG/FS.GG.Templates/actions/runs/37541790067)
passed all 119 checks and all 15 owner-skill causal controls. The focused provider suite
passed 186 assertions. The receiver now checks the exact descriptor template ID in
scaffold provenance; package source, archive, descriptor and lifecycle checks remain separate.
Owner-skill provenance binds the exact installed Artifacts assembly version rather than a
hardcoded SDK version; ordinary composition used SDK 2.2.0.

The original [published receiver 37537943991/a1](https://github.com/FS-GG/FS.GG.Templates/actions/runs/37537943991)
failed at the provenance guard before builds or browser checks; the first source attempt's
118-pass/one-failure composition result remains retained separately. This repair changes
only the harness and its controls. Published 0.18.1 bytes and tag still bind `d9fe65ea`;
a successor receiver must pin the fixed harness separately while retaining the original
package, descriptor and SDK 2.1.0 inputs. Fresh published-package installed acceptance,
effective defaults and full Wizard adoption remain open. No successor dispatch is selected
by this source closure.

**2026-10-06: Workspace Template 0.18.1 published on both feeds.**
Root accepted [publisher 37521553159/a1](https://github.com/FS-GG/FS.GG.Templates/actions/runs/37521553159)
at 20:26:36 UTC from protected `d9fe65ea8a456f59d663f20c647a38a195e13c2c`; the existing
composition gate passed 118 checks with zero failures against the original archive.
Tag `fs-gg-templates/v0.18.1` binds that source. Original artifact `11440537746`
contains the 887,368-byte package, SHA256
`d57ae0f90f4b44cb0eee6fbcdf7c356384fcb6f8562025c52bd3caa8877d9bac`.
Both authenticated feed readbacks match all 458 payload entries; GitHub retains
the original archive, while nuget.org repository signing produces raw SHA256
`0d9395b028f14b2c06afe1f1de019610217774f0cb8b6a9618f2aed7d51790fe`.
Receipt artifact `11441584620` has SHA256
`b306fcc9e7d7568d01879e8c3e85bbf61ec02fe5a0cf7f0a7982ab02d37c33bb`;
the [release](https://github.com/FS-GG/FS.GG.Templates/releases/tag/fs-gg-templates/v0.18.1)
was created at 20:24:13 UTC after both readbacks. The package has eight template
configurations and seven selectors, including the existing project-knowledge
and Python templates; this corrects the older inventory description without
adding a template identity. Fresh installed Templates-only receiver acceptance,
effective defaults and full Wizard adoption remain open. Full adoption waits for
the coherent Wizard successor after genuine 0.99 publication; the frozen 0.97.1
predecessor remains historical. Host GPU qualification and native usage remain unknown.

**2026-10-06: Templates 0.18.1 publication source delivered.**
[Templates #676](https://github.com/FS-GG/FS.GG.Templates/pull/676) merged exact
`2e90e9312db1410f543bda099bc9db322118d858` as
`d9fe65ea8a456f59d663f20c647a38a195e13c2c` at 19:06:10 UTC, with 15 checks
passed and four skipped. The one Workspace Template package and five descriptor
self-pins now select 0.18.1; Rendering 0.32.1 pins remain unchanged. The existing
public receiver admits a closed Templates-only mode and a separate full mode
requiring genuinely published Wizard 0.16.0 inputs. Ten focused preflight controls
and the hosted pack, composition, source and installed-receiver checks passed.
This closes the successor source step within SVG-COHERENCE-01.8. Workspace Template
At that source checkpoint, publication remained 0.18.0: fresh 0.18.1 feed occupancy,
tag/publication, both-feed readback and installed successor adoption were open. Templates-only public
qualification can proceed after genuine publication; full Wizard adoption waits
for the coherent successor dependency after 0.99 is genuinely published. The
original frozen 0.97.1 Wizard predecessor remains historical; no additional
predecessor qualification or new workflow route is selected. Broader adoption,
effective defaults and host GPU qualification remain open; native usage is unknown.

**2026-10-06: Published Rendering inputs qualified in the source-built receiver.**
Root accepted [full run 37506865594/a1](https://github.com/FS-GG/FS.GG.Rendering/actions/runs/37506865594)
at 18:05:43 UTC: Rendering caller `998f4e41ec0f2822e927d311ff534238d12785b1`
used protected Templates `208e5bffe99153375f7d3e2e1c84653104883cd3` and
published Rendering 0.32.1 inputs. Authenticated artifact `11432751407` is 45,058
bytes, SHA256 `531101019f8cbee9ef70b3bf963edddc20611e5c8965d2af804da5952cc2f961`.
It binds original producer/custody, three matched public archive readbacks,
180 ProviderComposition passes, five locked project restores, CLR/Fable codec,
Studio/Tactical/player checks, and 12 browser passes (four per Chromium, Firefox
and WebKit, zero skips, flaky or unexpected results) with verified report hashes.
The authenticated cache-check control passed; cache metadata was not archived.
The receiver job completed in 337 seconds within its existing 35-minute bound.
This closes the selected public Rendering input qualification within
SVG-COHERENCE-01.8. At that qualification checkpoint, Templates remained source-built
and Workspace Template publication was still 0.18.0; successor publication and
fresh installed/default/Wizard acceptance were open. Physical screenreader use was not observed;
broader adoption and host GPU qualification remain open. Earlier failed requests
and their partial evidence remain historical; native usage remains unknown.

**2026-10-06: Repaired public receiver caller source delivered.**
[Rendering #1397](https://github.com/FS-GG/FS.GG.Rendering/pull/1397) merged exact
`bc9161f015cc91a0dc805d8e006362d9728543d8` as
`998f4e41ec0f2822e927d311ff534238d12785b1` at 17:45:31 UTC, with 22 checks
passed and one skipped. Both the reusable workflow and source checkout now
select protected Templates `208e5bffe99153375f7d3e2e1c84653104883cd3`, which
contains the admitted codec reference repair. The explicit public Rendering
0.32.1 mode, read-only permissions, original producer custody joins, forced PR
preflight and existing 35-minute receiver bound remain unchanged; 12 negative
wrapper controls passed. This closes the repaired caller source step within
SVG-COHERENCE-01.8. At that source checkpoint, a new full request could name this protected caller
through an exact branch/readback, but had not been dispatched or accepted. The first
failed full run and its partial proofs remain retained below. Full public-input
qualification, Templates successor publication and final installed/default/wizard
acceptance remain open, alongside broader adoption and host GPU qualification.

**2026-10-06: Public receiver codec reference repair delivered.**
The first full public-input [run 37498421148/a1](https://github.com/FS-GG/FS.GG.Rendering/actions/runs/37498421148)
failed before locked restore because the validator rejected the codec probes’
shipped local Protocol/Domain project references. Its authenticated artifact
retains 180 ProviderComposition PASS lines and three matched public Rendering
0.32.1 archive/source/custody readbacks; restore, codec and browser qualification
were not completed. The preceding raw-SHA dispatch request was refused with HTTP
422 before run creation; neither failed request receives full qualification credit.
[Templates #675](https://github.com/FS-GG/FS.GG.Templates/pull/675) merged exact
`ee6899b23ad921506fbd74bc75b064e923ab12fd` as
`208e5bffe99153375f7d3e2e1c84653104883cd3` at 17:20:13 UTC, with 11 checks
passed and four skipped. Its validator admits only the probes’ exact existing
local edges and rejects conditional ancestors or alternative project references;
25 focused pure controls and ordinary source CI passed. This closes the repair
source step within SVG-COHERENCE-01.8. At that repair checkpoint, the Rendering caller still needed to select this
protected receiver before a new full request. Full public-input qualification,
Templates successor publication and final installed/default/wizard acceptance
remain open, alongside broader adoption and host GPU qualification.

**2026-10-06: Rendering public-input receiver caller source delivered.**
[Rendering #1396](https://github.com/FS-GG/FS.GG.Rendering/pull/1396) merged head
`c7dbc9bdb90bbce4ba46d63063dc1d6d6c92c991` as
`77c270f8c50fdc35b90f18bcad3f3ae25f6b27f2` at 16:34:19 UTC, with 22 checks
passed and one skipped. The existing caller selects protected Templates
`b2fb539b2707d52fc323fddf0570fafa1449e13d` for both workflow and source, and
explicitly selects the public Rendering 0.32.1 input mode. Its source checks
preserve read-only permissions, exact custody joins and forced PR preflight;
the receiver keeps its existing 35-minute qualification bound.
At that source checkpoint, the full public-mode workflow request was prepared against that caller
revision but had not been dispatched or accepted. This closes the caller source
step within SVG-COHERENCE-01.8; native public-input qualification, publication of
the Templates successor and its final installed/default/wizard acceptance remain
open, alongside broader adoption and host GPU qualification.

**2026-10-06: Templates Fable Rendering 0.32.1 source adoption delivered.**
[Templates #674](https://github.com/FS-GG/FS.GG.Templates/pull/674) merged head
`8748d3304297036a82532841e7b76ead14efb848` as
`b2fb539b2707d52fc323fddf0570fafa1449e13d` at 15:56:35 UTC, with 15 checks
passed and four skipped. Five Fable project/lock pairs and the player/Studio build
scripts now select published Rendering 0.32.1. Source CI passed the ordinary
composition, Release C source and installed Typed SDD checks; composition ran
for 19 minutes 15 seconds within its existing 30-minute job bound. The initial
stale pin expectation, build-driver override and receipt-fixture failures remain
historical evidence; their source repairs preserve locked restores and the
separate historical 0.31.0 API mirror.

The existing receiver now declares an explicit public mode with bounded archive
acquisition, exact custody/source joins and separate signed archive and NuGet
lock hashes. This prepares the selected public-mode caller qualification; that
full run remains pending. Workspace Template publication remains 0.18.0, so
publication and fresh installed/default/wizard acceptance of a Templates package
containing these new pins remain open. Original notification failed before either
POST and is not credited as adoption. This closes the Fable source/pin step within
SVG-COHERENCE-01.8; broader adoption, .3/.4 and host GPU acceptance remain open.

**2026-10-06: Rendering 0.32.1 publication accepted on both feeds.**
Root accepted [publisher 37461402933/a1](https://github.com/FS-GG/FS.GG.Rendering/actions/runs/37461402933)
executed at `6ea40861d15b581735655fbe7253673bf1cee907`, retaining the 19 original
packages from producer `6c9f766fdd91483c2de6f061e75589e94852a265`. Each feed
acknowledged all 19 packages; GitHub exact archive and nuget.org custody-payload
readbacks matched. Retained archive `11414392696` has SHA256
`85e134d2a510acb25d312f67284731cebcc1e5e5f9bb2678c791dbf8c55d2ca5`.
Tags `fs-gg-ui/v0.32.1`, `fs-gg-ui-template/v0.32.1` and `v0.32.1` bind the
original producer. The immutable source plan's `publicationReady=false` remains
historical source evidence; readiness and acceptance of this selected publication
attempt are separate observations. Provider and Fable source adoption are now
delivered as recorded above; selected public-mode qualification and final
published Templates installed use remain open. Notification failed before POST.
This closes publication only within .8; .3/.4, broader .8 adoption
and the late host GPU batch remain open.

The [consumer inventory](https://github.com/FS-GG/FS.GG.Rendering/pull/1380)
merged at `da3f64bbbff205529c9845f779de335ead7af6c0`; the
[self-running browser harness](https://github.com/FS-GG/FS.GG.Rendering/pull/1381)
merged at `3f67e4b778c9880e91faf4be58f23c02423ad337`. These close .1 and .2.
The harness passed its software-browser checks, including result download,
resume, integrity and three fault controls. No host GPU qualification is claimed.

Milestone .3 now includes stage attribution, composition parity, fixed-operation
motion/diversity/churn comparisons and lifetime diagnostics. The original 72-record
screen remains unchanged: 43 records meet its sample threshold, 23 are inconclusive
and six composition cases are unsupported under that original protocol. Later
composition parity is a separate four-case functional result, not a retroactive
success for those six cases.

The first bounded .4 change, [unchanged-attribute reconciliation](https://github.com/FS-GG/FS.GG.Rendering/pull/1382),
merged at `98861065e911581b8e27034b63351c637127b373`. It skips DOM attribute writes
when the accepted value already matches, preserving the existing public API.
All 17 browser controls and native CI passed. Its compiled candidate completed
24 comparison records; median update CPU was lower in all 12 paired cells
(shared geometry 1.18–8.89%, expanded geometry 4.13–8.18%). Tail results were mixed
and animation-frame cadence was unchanged. These are software-browser diagnostics,
not a universal performance or GPU claim; the rest of .4 remains open.

The next bounded .4 source window, [export-prefix factoring](https://github.com/FS-GG/FS.GG.Rendering/pull/1389),
merged at `f133cb9f979cb4d5053e67c72ac7a6041524fc31` with exact-head coherent
validation passed. It computes the mount/document prefix once per export and retains
curated Fable package content without changing the public API. The
[software qualification report](https://github.com/FS-GG/FS.GG.Rendering/blob/f133cb9f979cb4d5053e67c72ac7a6041524fc31/docs/reports/svg-export-prefix-20261005.md)
records three pairs and a median CPU reduction of 25.70%, with individual maximum
regressions and original failures preserved. GPU-disabled Chromium used the SVG
8GiB development allowance and peaked at 2,583,908,352 bytes; no 2GiB fit or GPU
acceptance is claimed. The coherent 0.32.1 publication is now accepted as recorded
above; selected public-mode qualification and final published Templates installed
use remain pending.
This closes only that source window; .3/.4, consumer adoption and the late host GPU batch remain open.

The 0.32.1 release-route source window delivered through
[Rendering #1390](https://github.com/FS-GG/FS.GG.Rendering/pull/1390) at
`6c9f766fdd91483c2de6f061e75589e94852a265` after required hosted and coherent validation passed
for exact head `4c146e1de3b4e1916fd59b03c88ce56cc3357f00`. The ten inputs qualified from
`b4326e3a6dca9c372beb4d9f7a6be807538a4212` retain their exact bytes. The original
10.469-second F# window passed five selected outcomes, including an assertion
negative control and both pre-transport wrapper refusals. A separate 21.647-second
guard reproduction passed five selected outcomes after the hosted stale-ledger and
report failures; its genuine fixed render equals the delivered report. Both windows
retained clean custody, core limits `[0,0]` and no resource failure. Failed attempts
remain retained and receive no successful credit.

Stage C candidate production passed in [run 37419955679](https://github.com/FS-GG/FS.GG.Rendering/actions/runs/37419955679)
from `6c9f766fdd91483c2de6f061e75589e94852a265`; both publication jobs were skipped.
Root verified artifact `11392773621` (14,912,688 bytes, SHA256
`486182db3efd8442c007f66322a7bfb27caf0cb8e3c87bc2521e66dd90a7adee`),
its retained source-bound custody manifest and all 19 package hashes.
[Templates #672](https://github.com/FS-GG/FS.GG.Templates/pull/672) merged the exact
receiver input bindings at `4eff52b27933c69a9ab4e276b3a29032d279b7e0` after required
composition, static controls and coherent checks passed.

Candidate receiver qualification was pending at the Stage C checkpoint above.
Root subsequently accepted the source-bound native
[receiver run 37428584026/a1](https://github.com/FS-GG/FS.GG.Rendering/actions/runs/37428584026)
through Rendering caller `67091200bc7bbcbb8189b2cf84244b2af919ecd1` at protected
Templates `4eff52b27933c69a9ab4e276b3a29032d279b7e0`: 12 browser cases,
180 provider checks and the package/Fable qualification passed. The publisher admitted that candidate receiver proof. The later selected
publication attempt has its own accepted readiness, feed, tag and authority joins,
recorded above; the original source plan remains immutable. Published-feed consumer
adoption and final installed use remain pending. Candidate proof and publication do
not close .3/.4, consumer adoption or the late GPU batch.

A separate 30-record diversity/churn window completed all 3,000 measured updates.
Twelve lifetime records completed 6,000 updates and 60 exact ownership-fault
controls. Four subsequent teardown cases completed 2,000 updates: residual DOM
counts fell after the page run returned, and blank navigation left four nodes,
one document and no listeners in each case. This narrows the fixture's earlier
residual to continuation/page lifetime; it does not prove producer leak freedom.
Original raw evidence, failed attempts and private asset boundaries remain retained.

Visible-count scaling, dynamic composition, allocation-stage evidence, integrated
consumers and the late host GPU batch remain open. A Templates reference planning
lane can prepare existing-API composition alongside research. No new instance API,
published package or installed product adoption is inferred from the guard merge.

## 1. Evidence and the missing qualification

Read-only investigation inspected Rendering `16da43f37de9f0eea0f4a65a67e6dee44c36ef1c`,
Game `8de4c2747d40e9993cc9a08cd50e1e2d599f69fb`, Templates `96b9d01` and S.I.R.
`80e1ac9`. These are investigation snapshots, not assertions about future main or
installed versions. Resolve full consumer revisions and current package identities
in milestone .1 before implementation.

| Established implementation/evidence | Limit relevant to this plan |
|---|---|
| Rendering Scene owns portable documents, symbols, retained state and spatial indexing; Scene.SvgBrowser owns browser rendering and lifecycle | Product consumers must actually use these capabilities; package availability alone is insufficient |
| `Symbol`/`SymbolInstance` export to SVG `symbol`/`use` | Shared authored geometry is not a guarantee of constant per-instance render cost or GPU instanced drawing |
| Ordinary/dense benchmarks contain 100/200 objects with shared symbols | The shared symbol is a single circle; timed interactions select objects rather than independently move every complex model |
| Gallery contains 100 symbol uses and complex paths/masks/text | Gallery timing is diagnostic, not an independently animated soldier-instance budget |
| Thirty-second continuous test records animation-frame cadence | The declared workload is camera motion, not hundreds of independent articulated or pose-changing models |
| Spatial test increases world extent while visible content stays constant | This does not establish visible-density scaling or arbitrary entry-order costs |
| Templates has a 2,000-entry/200-visible scale fixture | Its direct SpatialWorkingSet use is in ScalePlayer; other screens do not automatically inherit culling |
| SC2 Live/Tactical directly clear and recreate SVG unit elements | Shared retained-node and instancing behavior is not adopted there |
| S.I.R. supplied earlier product-specific behavior, measurements and design | It remains read-only; proposed donor architecture is not proof of generic implementation or product adoption |

Primary implementation and evidence locations:

- [Rendering Scene](https://github.com/FS-GG/FS.GG.Rendering/tree/16da43f37de9f0eea0f4a65a67e6dee44c36ef1c/src/Scene),
  especially `SvgDocument.fs`, `RetainedSvg.fs` and `SpatialWorkingSet.fs`.
- [Rendering browser adapter](https://github.com/FS-GG/FS.GG.Rendering/tree/16da43f37de9f0eea0f4a65a67e6dee44c36ef1c/src/Scene.SvgBrowser),
  especially `SvgBrowser.fs`, `SvgSessionHost.fs` and `SvgExternalSessionHost.fs`.
- [Actual browser workloads](https://github.com/FS-GG/FS.GG.Rendering/tree/16da43f37de9f0eea0f4a65a67e6dee44c36ef1c/tests/Scene.SvgBrowser.Tests),
  `Program.fs`, `performance-test.mjs` and `run-performance.sh`.
- [Recorded scale measurements](https://github.com/FS-GG/FS.GG.Rendering/blob/16da43f37de9f0eea0f4a65a67e6dee44c36ef1c/docs/reports/2026-09-13-svg-scale-performance.md).
- [Existing adoption inventory and ownership decisions](evidence/fable-adopt-01.1-inventory-disposition-20261001.md).

The current harness explicitly launches Chromium with `--disable-gpu`. **This
container has no GPU acceleration**, as established for this task. Its software
browser measurements remain a distinct profile; they cannot qualify host GPU
presentation. Host-main hardware, browser control and network reachability have
not yet been verified by this plan.

## 2. Coherent ownership and representation

| Boundary | Owner and intended responsibility |
|---|---|
| Simulation/entity state | Game.Core or the product's external engine; ticks, identity, commands, physics and authoritative facts |
| Portable visual assets | Rendering Scene; immutable glyph/model definitions, references, transforms, bounds, paint and supported document interchange |
| Visual instances | Rendering Scene contracts and browser adapter; stable instance identity referencing a definition, typed presentation overrides and revisioned updates |
| Browser effects | Scene.SvgBrowser; retained nodes, scheduling, hit testing, culling integration, generation-scoped caches, listeners and disposal |
| Game-to-Scene conversion | Optional Game.Render adapters where reusable conversion is demonstrated; no DOM, duplicate SVG exporter or new authoritative state |
| Product projections | Product code; soldier/faction rules, disclosure, overlays, semantic commands, native gateways and identifiers |
| Reference application | Templates; coherent package consumption, realistic examples, accessibility, clean/retained installed journeys |
| Documentation and evidence | Rendering owns algorithms and benchmark contracts; Templates owns integrated journeys; this programme indexes them |

Retain both document authoring/export and runtime presentation. Establish one
shared source of definition and instance identity, validation, transforms, paint
and resource ownership across these paths. Do not force each simulation tick
through full document serialization. Do not replace ordinary accessible HTML
forms/panels with SVG merely to reduce the number of UI technologies.

The proposed instance contract distinguishes asset identity/revision, instance
identity, authoritative epoch/revision and presentation revision. An instance
references immutable art; its pose, visibility and allowed paint parameters vary
independently. Health text, selection and transient effects may be separate layers
when measurement and semantic identity justify that choice. Product IDs stay
opaque to Rendering. Visual bounds and collision geometry remain separate.

Choose exact public types only after .1/.3 prototypes. Reuse existing
`SvgDocument`, `SymbolInstance`, affine transforms and semantic IDs wherever they
fit. Do not introduce a parallel scene graph simply to add an instance API.

## 3. Inventory and extraction method

Create one Rendering-owned capability/consumer map linked by the owning plans.
Inventory actual production entry points, not only package references. Include:

- Scene and identified-document browser paths, Studio, input/animation/session hosts;
- Game.Render and Game's simulation spatial index, keeping simulation and visual queries distinct;
- Templates Player, tactical/arcade examples, Studio, ScalePlayer and external-authority reference;
- SC2 Live/Tactical, BAR JavaScript/Fable presentation and FourD's DOM/grid projection;
- read-only S.I.R. characterization and the documented migration exclusions.

For each capability record owner, exact source, installed package, active caller,
tests, local duplication, planned disposition and compatibility obligations.
Classify it as shared and adopted, shared but unadopted, product-specific, genuine
producer gap, generated projection, historical artifact or intentionally unsupported.

Extract only demonstrated common behavior. Start with a failing producer-level
reproducer from a consumer, generalize parameters, implement once in Rendering,
then qualify two meaningfully different consumers. Preserve product gateways,
ordering, disclosure and native authority. Local Game sessions use SvgSessionHost;
externally clocked engines use SvgExternalSessionHost and must not acquire a
second browser-owned simulation clock.

Track every compatibility shim with its owner, supported versions, retirement
condition and test. Retire replaced renderer paths after accepted migration;
avoid an indefinite hidden fallback and dual state ownership. Keep generated
signature mirrors explicitly generated/checked rather than independently edited.

## 4. Repeated-model research

### Questions and competing implementations

Measure where work grows: asset validation, model-to-scene projection, allocation,
serialization, candidate parsing, reconciliation, style/layout, paint/raster,
compositing, input/hit testing and cleanup. Separate first mount from steady state.

Compare equivalent scenes and commands through:

1. The current document replacement path as the baseline.
2. Shared `symbol`/`use` with retained per-instance transforms and overrides.
3. Retained expanded geometry with the same semantics, as a control for whether
   symbol reuse actually helps each browser/workload.
4. A bounded instance-update path using shared existing types, with coalesced
   transform/paint changes and explicit create/remove/definition invalidation.

Keep camera-only changes, definition changes and instance changes distinct.
Test one definition used many times and many different definitions. Evaluate
static-art sharing separately from independent articulated animation or pose
variants. Raster caches or other rendering backends remain evidence-driven
follow-up decisions, not assumed replacements in this SVG plan.

SVG `use` creates a rendered instance shadow tree; source DOM node count does not
describe all rendering work. See the [SVG instance specification](https://www.w3.org/TR/SVG/struct.html#UseElement).
Report source nodes, symbol complexity, estimated expanded geometry and actual
browser observations separately. Do not label estimates as measured GPU work.

### Workload matrix

| Axis | Proposed coverage |
|---|---|
| Visible instance count | 1, 100, 250, 500, 1,000; bounded higher-count saturation experiments separately |
| World size | Same visible set in progressively larger worlds; visible entries at beginning, middle and end of index input; shuffled order |
| Art complexity | Single-circle control, representative licensed soldier glyph, detailed glyph; record exact paths/segments/nesting/definitions |
| Asset diversity | One definition; multiple unit classes; unique-instance control; shared-definition replacement |
| Motion | Static; camera only; 10%, 50%, 100% independently translating/rotating; independent pose changes |
| Simulation/presentation | Selected fixed tick rates, interpolation on/off, actual host refresh rates; no per-frame network round trip |
| Appearance | Faction colours, health labels/bars, selection/hover/focus, outlines, opacity, clips, masks and effects separately and in representative combinations |
| Churn | Spawn/despawn bursts; steady churn; viewport entry/exit; zoom; reconnect/epoch replacement; background recovery |
| Interaction | Dense overlap/topmost target, keyboard focus, pointer capture, touch alternatives, offscreen selected entities and accessible text |
| Lifetime | Warm steady play plus extended churn/mount/dispose runs to expose retained resources and memory growth |

Use a staged design: small screening matrix, controlled experiments isolating
dominant factors, then representative combined workloads and worst-case controls.
Do not run the entire Cartesian product or quietly drop expensive failing cells.
Record the reason for every excluded combination and the claims it leaves open.

Assets, command streams, seeds, camera routes and update distributions are versioned
and identical across variants. The representative soldier must be agreed as a real
target asset rather than a convenient trivial substitute. Include correctness
checks for geometry, styling, hit policy, layering and export alongside timing.

### Source hotspots to investigate

- `SpatialWorkingSet` stores entries in an F# list and indexes it per candidate;
  `entries.Length` also traverses it. Profile random-access storage, cached counts,
  construction without repeated list append, and any needed incremental updates.
- `SvgDocumentBrowserHost.Replace` exports/parses a complete candidate before
  reconciliation. Measure and separate definition validation from instance deltas.
- The document reconciler searches sibling arrays for each child. Evaluate keyed
  lookup and attribute-change checks without changing order or element identity.
- Retained interaction synchronization repeatedly scans selectable objects.
  Evaluate revision-scoped identity lookup and accessibility updates.
- Inspect actual cache ownership, dependency invalidation and bounds recomputation;
  do not infer complete layer caching from stable DOM identity alone.

These are hypotheses from source inspection. Accept optimizations only with
semantic equivalence, useful measurements and no lifecycle/resource regression.

## 5. Container-served fixtures, host-main browser execution

### Proposed execution topology

The container builds immutable production fixtures and serves only their static
artifact directory through a selected reachable port. The existing browser on host-main opens that URL, runs the self-contained
test queue locally, and downloads one combined result. No host-side installation,
command-line runner or remote-control service is required for this default route. Rendering occurs on host-main, regardless of
where the HTTP server runs. Serving on the container does not require a container GPU.

Default user journey: **open the supplied URL → click Run all → download results**.
The page owns warm-up, case sequencing, progress, bounded durations, checkpoint/resume
and result export. The container owns all building and serving. Its open/reachable
port is sufficient to deliver the page to host-main; no additional host setup is
introduced as a prerequisite. Loading the page establishes reachability naturally.
Milestone .2 prepares and container-tests the page; .7 is the late batched host run.

The page collects browser-exposed observations and marks unavailable host/GPU facts
explicitly. A browser on host-main can use its GPU, but an ordinary page cannot
reliably certify SVG hardware acceleration, driver identity or GPU/compositor timing.
These limits do not prevent the page suite from running. When a specific acceptance
claim requires those facts, collect browser diagnostics or a DevTools trace once
in the same late session, as a clearly explained supplementary step.

Optional enhanced route: an already available host-local Playwright runner loads
that same URL for trusted input automation, browser-level diagnostics and detailed
traces. It consumes the same workload/results contract. Installing or configuring
such a runner is not required for the default page suite. Supported browser families
can load the same page; any required manual browser changes are grouped into the
one session rather than requested case by case.

If host automation must attach to a browser, use an owned dedicated profile and
an explicitly selected local/tunnelled endpoint. Do not expose a browser debugging
port on the public fixture listener. Prefer host-local launch over CDP attachment
where possible; [Playwright documents CDP attachment as lower fidelity](https://playwright.dev/docs/api/class-browsertype#browser-type-connect-over-cdp).
[Chrome's debugging changes](https://developer.chrome.com/blog/remote-debugging-port)
also require attention to dedicated profile selection. This is a proposed route,
not authorization or evidence of a running host browser.

### Fixture and page-runner separation

Refactor the existing harness into build/package, static serving, browser driving,
measurement and reporting components within Rendering's existing test area.
Reuse its workloads, negative controls and evidence formats. Add a base-URL mode
that does not launch a container browser or assume container host facts describe
the browser machine. Keep the current local software route available unchanged
as an explicitly named historical/regression profile.

Expose an artifact manifest and run identity in the fixture. Verify source/package,
bundle, asset and workload hashes before and after each run. Serve fixed production
bytes, with no hot reload or development compilation during measurement. Select an
unused task-specific port and an owned server process; report the reachable URL
and stop only that server when the window ends. Do not serve the checkout or
credentials. A result-download path avoids needing a write-enabled web service.

Host workload actions and timestamps originate inside the host browser. Steady-state
measurement begins after resources load; report cold-load/network time separately.
Never subtract container wall-clock timestamps from browser performance timestamps.
Use a stable origin for storage tests; use HTTPS or a suitable localhost forwarding
route when tested APIs require a secure context. Record cache/service-worker state.

### One late, consolidated human-assisted session

Prepare the in-page runner, fixtures and result collection early, but do not schedule
human-assisted host setup, reachability probes, baseline runs or individual GPU
experiments as early milestone prerequisites. Complete inventory, container
research, shared implementation, reference composition, selected product candidate
integration and package preparation first. Container evidence supports CPU-side
engineering decisions; GPU-specific choices remain provisional until the late batch.

The session admission checklist is a concrete readiness bundle:

- all affected pure, portable, software-browser, semantic, lifecycle and package
  checks pass; no known defect still requires a host visit to diagnose;
- immutable baseline and candidate builds, hashes, realistic assets, supported
  browser/workload manifest and negative controls are staged together;
- one self-contained page has container-tested capability checks, a sequential queue,
  progress display, bounded run durations, checkpoint/resume and consolidated
  result export; no manual navigation is required between ordinary cases;
- the page states the URL/open/run/download steps, estimated total run duration
  and result destination; any optional diagnostic step has a specific purpose;
- proposed budgets and comparison rules are fixed before the host batch, including
  honest unsupported/inconclusive outcomes.

At that session's start, load the URL and collect available environment facts once.
Record verified acceleration when supplementary browser diagnostics are available;
otherwise label that fact unknown while preserving the useful host-browser results.
Run baseline, alternatives, final candidate, integrated consumer journeys and
negative controls in the same scheduled window, using controlled
ordering and independent repetitions. Include all selected GPU-dependent browser
profiles in that session. Missing privileged diagnostics do not block unrelated
page tests or trigger tooling installation. A failed capability check produces one actionable report,
not repeated individual prompts or benchmark requests.

If the batch reveals defects, return to autonomous/container repair, collect all
related fixes, and propose one focused follow-up batch covering changed subjects
and necessary regression controls. Reuse unchanged exact-artifact evidence within
its original scope; do not repeat the whole matrix by default. Earlier host work
requires a concrete dependency that cannot be resolved through source inspection
or container testing and an explicit scheduling decision; it is an exception,
not the default research loop. A human-unavailable host leaves GPU acceptance
pending while independent preparation continues.

### Host qualification

Collect page-visible facts automatically. For optional enhanced measurements,
record OS, CPU, memory, GPU/vendor/device, driver, browser/version/channel, launch
flags, hardware acceleration status, raster/compositor backend, display refresh,
viewport, device pixel ratio, power mode and foreground/visibility state. Verify
actual acceleration using browser GPU diagnostics, not merely the absence of
`--disable-gpu`; headed mode alone is not proof. A remote desktop or virtual display
may change the measured route and must be recorded. Unsupported/unobservable
dimensions remain unknown; require them only for the acceptance claims that depend
on them. Scripted page interactions do not establish trusted physical-input
latency or privileged trace measurements.

Prefer a visible host display for interactive acceptance, and prevent concurrent
benchmark runs from competing for that GPU/display. Headless host runs may be a
separate qualified profile. Emulated mobile dimensions are not physical mobile
GPU evidence. [Playwright browser selection](https://playwright.dev/docs/browsers)
and [Chrome performance tooling](https://developer.chrome.com/docs/devtools/performance/reference)
are implementation references, not evidence that this host is configured.

## 6. Measurement and acceptance contract

Preserve all earlier receipts with their software-rendering identity. Define new
host profiles and versioned workload budgets before accepting optimized candidates.
Prepare baseline artifacts and provisional targets during container research;
measure baseline and candidates together in the late host batch. Proposed
interactive targets start with the selected display/frame cadence (for example,
16.67 ms at 60 Hz); freeze the supported soldier count, complexity, input-latency
budget and missed-frame allowance after container baseline characterization and
an explicit product decision, before the human-assisted host batch. Report baseline failures without relaxing thresholds to fit them.

Record warm-up, run duration, sample count, independent repetitions, percentile
method and raw samples. Use at least three independent measured repetitions for
acceptance; extend runs for meaningful tail estimates. Alternate comparison order
to reduce warm-cache/thermal bias. Report p50/p95/p99 where supported by sufficient
samples, variability and worst stalls, not only averages or one FPS number.

Separate browser update CPU, rAF cadence, style/layout, paint/raster and observed
compositor events. Screenshot completion is a functional surface witness, not a
physical display presentation timestamp. Do not take screenshots each frame in
the principal timing run. Run detailed traces separately where instrumentation
changes cost, and disclose unavailable stages. Measure input during animation,
long tasks, allocations/GC, retained ownership and memory after repeated churn;
browser/process memory is not automatically GPU memory.

Add controls that demonstrably detect full reconstruction, an instance-count or
geometry multiplier, uncancelled frames/listeners, stale epoch completion, leaked
definitions and incorrect culling/focus. Invalid or excessive input must preserve
the last accepted scene. Export must retain full accepted content independently
of viewport culling. Refused/unsupported operations must not fabricate success.

An instance update should not rebuild unchanged definitions or unrelated instances.
Camera changes should not manufacture simulation revisions. Shared-definition
replacement must invalidate exactly its dependents. Caches must have bounded
ownership and release on scene/epoch disposal. Correctness gates remain mandatory
even when an alternative is faster.

## 7. Ordered implementation and evidence milestones

| Milestone | Deliverable and owner | Acceptance and dependencies |
|---|---|---|
| .1 Inventory and contract decision | Rendering-led capability/consumer map; producer contract proposal; product classification | Exact source/caller evidence, no duplicate adoption ownership; links from existing owner plans; no runtime claim |
| .2 Host route preparation | Rendering harness split and self-running test page with combined result download | Container dry runs, exact artifact identities, queued execution/checkpoint/export behavior and original software profile retained; no human-assisted host operation; may proceed alongside .1 |
| .3 Container instance research | Rendering screened benchmarks and provisional design report using realistic glyphs | Raw software-profile results, CPU/allocation/update-path analysis, equivalent outputs and explicit GPU unknowns; depends on prepared harness, not host availability; contract refinements join .1 |
| .4 Shared implementation | Rendering instance updates/index/reconciliation/cache changes selected by .3 | Focused unit/portable/software-browser tests, before/after container evidence, versioned API and compatibility checks; GPU conclusions remain provisional |
| .5 Integrated reference | Templates soldier workload in the actual player, shared APIs and accessible alternatives | Container-qualified local/external authority, stress/churn/focus/export; actual application composition joins the staged host suite |
| .6 Consumer and package candidate preparation | FourD/SC2/BAR owners within FABLE-ADOPT-01's existing sequence, plus release owners | Selected product source integration, native semantic obligations, exact package/clean-consumer/retaining-upgrade candidates and autonomous checks complete; freeze baseline/candidate artifacts and one consolidated host packet; no claim of GPU-qualified adoption |
| .7 Late host-main GPU qualification batch | Rendering measurement owner and host operator | One open-URL/run-all/download session runs baseline/alternatives/candidates and selected consumer journeys; optional diagnostics verify GPU-specific claims without blocking page tests; depends on .1–.6 readiness; follow-up only for demonstrated affected gaps |
| .8 Publication, installed adoption and closure | Existing release/product owners and programme integrator | Required .7 evidence accepted, coherent publication and final installed/upgrade readback, superseded copies retired, consumer map and owning/unified plans reconciled; an artifact change affecting GPU behavior rejoins a focused batch |

Parallel preparation: consumer inventories, realistic assets, pure reference command
streams and harness separation can proceed independently. Keep one owner for
Scene/instance public contracts and one integrator for shared package/API joins.
Serialize GPU benchmarks inside the late host window; do not turn each worker's
milestone into a human-assisted test request. Prepare all selected consumers before
that window, without waiting for unrelated portfolio work. Package candidates can
support isolated consumer preparation; publication and qualified installed adoption
remain distinct. Refresh active work/branches before assigning implementation so
this proposal does not disrupt already running roadmap lanes.

## 8. Migration, release and completion

Use adapters at existing seams and preserve accepted document formats and stable
semantic identities. Test retained saves/assets, unknown fields/versions, malformed
references, interrupted upgrades and rollback before retiring compatibility routes.
Do not silently rewrite user assets or claim S.I.R. adoption; that repository is
excluded from writes under the existing programme. Keep desktop/Skia optimization
claims distinct from browser SVG claims.

Delivery requires more than a new API: the reference and selected products must
call it, consume the published version where promised, and pass their actual
journeys. Record deliberately retained custom renderers with reason and scope.
Update existing FABLE-ADOPT-01 and SVG owner plans rather than maintaining duplicate
completion ledgers.

The final report must answer: which code owns SVG representation; which consumers
reuse it; which copies were removed or retained; how model definitions and instances
are represented; what a change costs; which soldier counts/complexities and browsers
were measured; which host used real GPU acceleration; and which limits remain open.

Immediate work: extend visible-count and sparse-motion qualification under .3,
prepare the Templates reference, and select further producer changes only from
demonstrated gaps. Preserve the
immutable baseline and software-browser profile. Actual host capability discovery
and all GPU browser cases remain deferred to the late .7 batch.


## Candidate receiver acceptance — 2026-10-06

[Rendering run 37428584026/1](https://github.com/FS-GG/FS.GG.Rendering/actions/runs/37428584026)
uses caller `67091200bc7bbcbb8189b2cf84244b2af919ecd1` and protected Templates
`4eff52b27933c69a9ab4e276b3a29032d279b7e0`. Its original evidence archive is
30,941 bytes, SHA256 `90d6693543e21fe98649a3efca3f64a0bfcca64450967a095a75bbd9bbd0edb3`.
Root verified the native terminal jobs, original Stage C custody, the three consumed package
hashes and five delivered Fable entries. Chromium, Firefox and WebKit each passed four cases
with zero unexpected, skipped or flaky results; the successful native provider command recorded
180 passing checks. The generated candidate receiver is accepted in this scope.

This retains the original 0.32.1 candidate archive from run 37419955679/1. No package was
regenerated. DOM automation does not establish actual screen-reader use. Stage D publication,
both-feed readback and adoption of published packages remain separate outstanding obligations.

## Stage D boundary source delivery — 2026-10-06

[Rendering #1392](https://github.com/FS-GG/FS.GG.Rendering/pull/1392) merged at
`01bc86518f3ab0c9c842a1ddc660c4d5326bc6f8` with required and coherent validation passed
for exact head `ceee5447779f62936b486421e5ed4be99c4e7023`. Retained native qualification
accepted five outcomes and fifteen boundary cases; the reporter's exit 2 remains separate
from those accepted cases. The route retains `attemptReady=false`: Stage D publication and
readiness, both-feed readback and adoption remain pending. No candidate archive is regenerated
and no publication effect is authorized by this source closure.

## SVG-COHERENCE-01.8 — Wizard coherent 0.99 dependency window

Planning observation: 2026-10-07. This is a bounded continuation of .8 under the
[Unified §9.8 index](../2026-09-07-154210-fs-gg-unified-development-roadmap.md#98-feature-parts-and-subroadmap-index),
owned by the `.github` Creator implementation owner with the programme root as
publication and cross-repository integrator. The existing .8 row remains the
completion ledger. The change in dependency source and payload, from frozen
0.97.1 to prepared schema-14 0.99, warrants this replan. Session restart and prior
failures do not create a new feature or renew an operation.

### Reuse and exact gaps

Protected source `ca1b668a31e878945026bcec8934065f6769d00e` and inspected local
`1f63cd3dfd7cf85f099dbfbd0f2b7be5609cb1f1` have identical tree
`5a5b25fdd3e10892d441c607cc4f4740ae4318c3`. Static traversal from
`scripts/NewSddWorkspace/NewSddWorkspace.fsproj` still yields the same eleven
projects as `scripts/creator-frozen-coord-dependencies.json`. Twenty-one of its
267 pinned source leaves differ, and sixteen current compiled leaves are absent
from the old pin: Core's EfficiencyEvidence, EfficiencyInput, NativeExecJsonl,
NativeResponses and ProcessEfficiency pairs, plus Client's CustodyProcessLease,
DirectResponses and ObservedCodexProcess pairs. Linked Store compilation inputs
still resolve into the CLI source directory. Retaining eleven project names or
changing the version scalar alone cannot qualify this closure.

The actual Creator caller is `Program.prepareProductBoard` →
`ProductBoard.validate/prepareGenerated/prepare` →
`BoardV2Application.parseBinding` and `V2Projection.Binding`. The two producer
modules, signatures and implementations are byte-identical to frozen
`99ea75286f5c3cea2a261fef4e5b45cd70378185`. Creator's separately delivered
fresh-scaffold manifest capture must remain intact: only a same-invocation,
successful, unchanged SDD-owned manifest permits replacement; retained workspaces
and retrofit retain strict conflict refusal. `ProductBoard` also compares the
loaded adapter digest and CLI informational version against the immutable kit
revision's tool manifest and verifies selected kit/driver leaf digests.

This supports an authenticated closure refresh, subject to compiled compatibility
proof. There is no demonstrated need for a Binding, publisher, journal or evidence
schema change. The read-only candidate's existing three-file archive format and
independent Wizard `0.16.0` release binding remain the selected source contracts;
this statement is not proof that the coordinate is free. The artifact reader binds
the genuine successful first-attempt run and original archive, while candidate
package checks emit the dependency evidence. Preserve both boundaries.

Fresh native [Creator #4232](https://github.com/FS-GG/.github/pull/4232) readback
shows head `2968bbf9de67b49feb933ce19a0a14e32c84127b` merged as
`09d779330c639b90303574f884e415ea1f5cf4b4` on 2026-10-06. This supersedes the
October 5 handoff's open-PR statement, while its original exact-source 0.97.1
local qualification stays scoped to that implementation. Original candidate
[37268962676/a1](https://github.com/FS-GG/.github/actions/runs/37268962676)
succeeded at `b1181b1afd6d0aa7da57e179c44bddcea50d8054`. Original artifact
`11327780826` contains Wizard **0.15.0 with coherent 0.97.0**, not 0.16.0 or
0.97.1. It is historical and cannot qualify this 0.99 window. The seven observed
candidate runs contained no authenticated fresh 0.16 candidate; this is bounded
observation, not global absence. [H4 37288088411/a1](https://github.com/FS-GG/.github/actions/runs/37288088411)
succeeded at `49a5e93668d4647999cd7171a26d4ad4d151b66b`; recovery source identifies
its `tsdd-knowledge-wizard-013` lineage. The retained final package readback 403
and failed/unaccepted H4 acceptance remain distinct from Actions success. The
observed 0.13 journal generation 17 has all eight effects verified; its consumed
effects cannot be replayed. The selected Authority ref
`board-v2-product-creator-016` returned 404 to this reader; its absence versus
visibility is unresolved. The 0.16 journal/tag/release and NuGet GETs returned
404, while the GitHub feed returned 403: visibility and original lineage remain
unknown. No coordinate-free conclusion or fresh operation grant follows.

The accepted Templates-only run 37549683984/a1 remains the .8 receipt above.
Templates' actual full receiver additionally installs the public Wizard and runs
fable-game complete creation with default and `none` lifecycle, both with
`--no-coordination`. Therefore that full receiver does not replace the compiled
Product V2 seam tests or establish native board acceptance.

### Next implementation window — route: routine

Assign one newest available Sol worker at medium effort after root supplies the
accepted inputs below, using the exact installed work-roadmap skill. Preserve
campaign `unified-roadmap-20261003`, item/original item `SVG-COHERENCE-01.8`, and
planning attempt `wizard099-plan-20261007`; original-attempt provenance is missing
and must remain an explicit gap. Root owns shared-main integration and admission;
keep one local branch until its existing queue permits the coherent source PR.

**Prerequisite at the point of consumption.** Root completes genuine coherent
0.99 publication through UTEL-REL-16 and supplies its immutable source, original
CLI archive, raw archive hash, release/asset identities, promoted manifest and
both-feed readbacks. Root also retains the full original seven-file candidate
identity; a source scalar, prepared manifest or successful test is insufficient.
The implementation owner may inspect and draft controls before this boundary,
but must not install guessed archive pins or substitute rebuilt dependency DLLs.
Root must reconcile the exact Wizard candidate, release/feed coordinates and
Authority journal lineage before selecting any fresh 0.16 operation. An occupied
coordinate, initialized journal or unknown effect requires the existing exact-byte
recovery decision, or a separately planned distinct successor; never reset or
silently replace it.

**Exact planned touch-set in `.github`:**

- `scripts/creator-frozen-coord-dependencies.json` — authenticate the 0.99 source
  and original public archive; enumerate the complete actual project, compiled,
  linked, shared-property/package/SDK/lock input closure and archive runtime members.
  Preserve historical 0.97.1 identity and receipts in existing evidence/history.
- `scripts/creator-frozen-coord-dependencies.py` — bind emitted version to the
  selected authenticated pin rather than literal 0.97.1; check declared source
  coverage against the actual project inputs, including the new nested Client
  pair and linked Store inputs. Keep archive, layout, destination, copy and
  pre-acquisition source guards closed and fail before staging on a mismatch.
- `.github/workflows/release-new-sdd-workspace-successor-candidate.yml` — select
  the accepted 0.99 URL/path consistently with the pin while preserving the
  source check before SDK setup/acquisition, read-only permissions, non-cancelling
  concurrency, 45-minute ceiling and exact-main checks.
- `tests/new-sdd-workspace-successor/run.py` and
  `tests/new-sdd-workspace-successor/frozen_dependencies.py` — update the selected
  coherent identity and closure expectations together; retain the old-version,
  missing/rebuilt member, source drift and copy-route refusal controls. Derive
  expected roster from the reviewed pin and separately check exact observed
  eleven-project membership, rather than silently accepting arbitrary growth.
- `tests/new-sdd-workspace/product-board.fsx` — add only missing causal controls
  needed to prove the existing fresh/retained behavior against the loaded 0.99
  adapter; retain all existing actual compiled caller checks.
- This owning plan — add the exact source result and remaining operation boundary
  in the implementation PR. Root can append this subsection without overwriting
  newer progress entries.

No producer Core/Client/Store/Responses implementation, Creator runtime defaults,
`ProductBoard.fs/.fsi`, publisher/admission/journal protocol, registry publication
state or Templates source change is selected. If the final published graph,
package metadata or compiled seam contradicts these observations, stop that
portion and return the concrete incompatibility to root before expanding scope.
The existing Creator fsproj frozen-copy contract remains the implementation seam;
no project edge removal or reference substitution is required by present evidence.

**Acceptance and stopping point.** First run the pure successor controls and the
source guard against the final selected checkout. Require missing newly compiled
or linked leaves, wrong source/version, changed archive/member, absent/extra
member and source graph mutants to refuse before dependency staging. Retain the
old 0.97.1-current-source refusal as history. After root admits local qualification
capacity, stage only authenticated 0.99 package bytes and run the existing Creator
source suite, actual compiled ProductBoard probe, and one pack/package-closure
check through the existing candidate sequence. Verify all staged dependency hashes
remain equal before and after build/pack, each expected runtime body occurs, the
Wizard deps metadata selects CLI 0.99, and the freshly built Creator alone differs.
Do not assume the new archive still has 81 members. The loaded Binding digest,
immutable kit tool pin, generated-manifest ownership/preimage/mode, retained-pin
conflicts and rollback must be tested together. Wrong 0.97.1 metadata must refuse.
No test may invoke a real Responses provider, telemetry migration or board write.

Return focused evidence, exact source and the original archive joins for root's
ordinary source review, required checks and guarded merge. Preserve failed
attempts and independently discovered defects; failed prerequisites fence dependent
checks. This window closes source preparation only. It ends before hosted candidate
dispatch, package publication, installed receiver acceptance or .8 completion.

### Later outcomes and generated-workspace impact

After source landing and root's fresh lineage/coordinate decision, outline the
existing Wizard candidate → authenticated original archive → independent preflight
→ protected publication → both-feed readback sequence. Any partial effect retains
its original archive and journal. Only then can Templates select full-mode public
inputs and qualify default/none Fable-game creation, provenance, locked build and
Chromium FourD journey. Keep the consumed Templates-only receipt and original
Template 0.18.1 source/archive/descriptor/SDK 2.1.0 pins; pin the repaired harness
separately. Product V2 fresh/retained acceptance stays with its owning board work.

Under Unified §9.9, this source refresh alone changes no installed tool or lifecycle
default. The first observable fresh-workspace change follows separately published
Wizard adoption: an explicitly selected Fable-game complete workspace uses the
accepted public Templates/Rendering composition, and an explicitly selected
Product V2 path can carry the published coherent adapter while preserving the
fresh-manifest authority restriction. Templates' `--no-coordination` proof cannot
claim that Product V2 behavior. Existing workspaces need their separate preserving
upgrade/conflict/interruption/rollback acceptance; no automatic retrofit, telemetry
activation, Responses execution, Host installation or GPU acceptance follows.

Keep the existing §9.8 SVG row/link; root may append this subsection anchor during
implementation integration. No planning-only PR, second board ledger or new native
operation is created. Observation here consists of local file/Git comparison and
bounded native GET readbacks. Telemetry is not configured; token usage and the
original attempt remain unknown. Compilation, new package behavior and installed
0.99 adoption were not tested during planning.

### 2026-10-07 source preparation against published 0.99

The dependency prerequisite above is now established by coherent publisher
[37566865635/a1](https://github.com/FS-GG/.github/actions/runs/37566865635)
and the promoted [0.99 release](https://github.com/FS-GG/.github/releases/tag/coherent-set/v0.99.0),
release `405346080`, published at 03:54:17 UTC. Root accepted both-feed readbacks
and delivered Unified closure through [#4300](https://github.com/FS-GG/.github/pull/4300),
merge `3bd43688bc0ff0b7fe17e1bc7eff2475bf5e0b80`. Original producer
[37565794335/a1](https://github.com/FS-GG/.github/actions/runs/37565794335)
at `64e95ebec1a8294e16edaafdb27e6aa96f32c6f7` retained artifact `11458892057`: seven
original members, ZIP SHA256
`9283d4e59a759c58fa6aad2460ef4e665fdb48f9d7ede15690505d73200b163f`.
The selected CLI is its original 32,224,239-byte archive, SHA256
`d50b6de718c509e581984759dfc2547a6afa287488a829930bec391823ae9f5e`,
public asset `617480948`. The prepared candidate manifest remains historical
source evidence; accepted publication is a separate root readback.

The local source refresh pins 281 actual dependency inputs and the observed 81
runtime members, preserving the eleven-project roster and Creator frozen-copy
contract. Actual Compile declarations include nested Client and linked Store
files; shared imports, central packages, SDK and lock inputs are included. Missing,
duplicate or extra source declarations refuse before archive access. Emitted
coherent version and Creator dependency metadata now select the reviewed 0.99 pin.
The pre-acquisition guard, read-only workflow permissions, exact-main checks,
non-cancelling concurrency and 45-minute ceiling remain intact.

The local source guard and all 146 pure successor controls passed in 2.313 seconds.
Controls include omitted nested/linked files, source graph/leaf drift, missing or
rebuilt runtime bodies, old 0.97.1 metadata and historical-pin refusal. The compiled
ProductBoard probe adds loaded-0.99, immutable-kit mismatch and retained predecessor
pin checks alongside its existing fresh ownership/preimage/mode/rollback controls.
Those compiled checks, actual Creator source qualification and pack/closure proof
remain pending root's resource admission. Static source/workflow controls are the
selected preflight for this bounded linear dependency refresh; no custom pipeline
model or measured CI-savings claim is made.

This is local source preparation, not Wizard publication or runtime acceptance.
Historical 0.97.1 receipts, original failed attempts and consumed Templates-only
`37549683984/a1` remain scoped and unreplayed. The corrected 0.15 candidate pointer
above does not change original native receipts. Root must still resolve 0.16
visibility/lineage before any hosted candidate or publisher selection; no workflow
dispatch, grant, tag, provider operation, installed adoption, effective-default
activation or preserving upgrade is admitted by this source work. The original
worker dispatch preceded restored observation configuration; this follow-up retains
that attribution gap and unknown usage without substituting the root token.


### Incremental telemetry repair sequencing — 2026-10-07

This extends the same SVG-COHERENCE-01.8 dependency window. Root independently
accepted the compiled and package qualification for Wizard source
`28caa52be69eb2b69462109eda3d5c5172f93c52`; [#4301](https://github.com/FS-GG/.github/pull/4301)
merged as `6419282331b9789666527621191b91310a5209bc` at 04:54:37 UTC. Its earlier
local preparation paragraph above remains a historical checkpoint. Preserve its
exact 0.99 source/archive qualification and the accepted published release
`405346080` from `64e95ebec1a8294e16edaafdb27e6aa96f32c6f7` without relabeling them.

The tested telemetry fix at local
`3e8be3f1c2821a24b4e704bbe82e08ca9df60765` aligns dispatch-state reads and writes
at a bounded 1 MiB, including the persisted newline. It changes the compiled
`src/FS.GG.Coord.Cli/SkillTelemetryAdapter.fs` leaf frozen by the Wizard pin.
The positive source suite and separate original-source red baseline are distinct
accepted evidence. The first FS0240 control scaffold failure and deadline-refused
continuation remain an incomplete original window. Existing private states,
reconciliation records and unknown native usage must remain intact.

**Late main failure remains a dependency fence.** Exact
[engine-pin run 37573721971/a1](https://github.com/FS-GG/.github/actions/runs/37573721971)
on merge 641928 failed its live `pin` job `112637946362` after the fixture passed.
The original log states that the canonical manifest pins 0.97.0 while the newest
resolvable CLI is 0.99.0. Its raw log SHA256 is
`eca804356ad7b0fd85a9a68eb0ebd47c8ceaead3a79ecbc2658cb1396166271b`.
PR success does not waive this main failure or permit dependent activation.
The existing workflow and `registry/repos.yml:582–609` identify this manifest as
`.github`'s canonical pin, excluded from Kit delivery under ADR-0068. Repair only
its `fs.gg.coord.cli.version` to genuinely public **0.99.0**, preserving other rows;
root has selected the existing SVG Sol owner for this isolated current-pin
repair, currently pending. Root may join it with the pending publication projection
in one CI batch. The patch worker preserves this owner and consumes its merged
result instead of racing the same file. Requalify the ordinary main live check through its authenticated
reader. Never set it to unpublished 0.99.1: the same gate refuses ahead-of-feed pins.
This source correction is distinct from installed tool selection or receiver defaults.

**Selected minimal route:** deliver the compatible repair and prepare a fresh
whole coherent patch; keep the Wizard candidate held until that patch is genuinely
published and its closure is refreshed. Proposed source version **0.99.1** is a
patch because the selected change repairs internal bounded IO without changing
public signatures or a wire schema. It must move CLI, Kit and Drivers together.
There is no way to retain a passing current-source frozen guard after changing
this compiled leaf while continuing to name the original 0.99 archive. The guard
must correctly refuse during the interval. Do not remove that leaf, substitute
current hashes under the old source SHA, rebuild the old package, use a historical
main candidate, or weaken the source-before-acquisition check.

**Source CI remains executable during that interval.** At source 28c, ordinary
`new-sdd-workspace-selftest.yml:94` invokes `tests/new-sdd-workspace/run.sh`, whose
normal source build does not set `FsggFrozenCoordDependencies`. It then runs the
pure successor suite. `frozen_dependencies.py:110–147` positively checks the
published historical source and explicitly expects current-source refusal when
pinned leaves drift. Only the manual
`release-new-sdd-workspace-successor-candidate.yml:23–31` exports the frozen
profile and requires its source check before SDK setup and acquisition. Thus a
source-fix PR can pass its ordinary technical gates without future archive pins;
that pass cannot qualify a frozen Wizard release candidate.

The compiled ProductBoard probe at `tests/new-sdd-workspace/product-board.fsx:82`
currently asserts literal 0.99.0. Replace only that expectation with the independently
read, uniquely declared `FsggCoherentSetVersion` in committed `Directory.Build.props`.
Reject absent/duplicate/malformed declarations and a loaded-version mismatch; never
set expected version from the loaded assembly itself. Its existing kit mismatch,
fresh-manifest ownership/preimage/mode, retained-conflict and rollback checks stay.
The frozen candidate already validates the same props through its source guard,
so the probe remains meaningful for source-built and later frozen patch profiles.

#### Next bounded Sol-medium source window

Root remains the sole integrator. Reuse telemetry repair owner/source and the
existing Wizard owner for the eventual closure refresh, with one writer for their
shared plan. Start from freshly observed protected main containing #4301. Integrate
the exact three-file repair and prepare the compatible patch in one coherent routine
source PR when root admits its lane. The implementation worker receives the installed
work-roadmap skill and original evidence; no new feature, board ledger or planning PR.

Exact required source-preparation touch-set:

- Repair: `src/FS.GG.Coord.Cli/SkillTelemetryAdapter.fs`,
  `tests/skill-fsharp/adapter/Program.fs`, and `docs/roadmaps/utel-local-telemetry-store.md`.
- Current dependency owned by the existing SVG owner, not the patch worker:
  `dist/dotnet/.config/dotnet-tools.json` selects published 0.99.0 after the
  pending repair; preserve that value independently of the source scalar below.
- Coherent source identity: `Directory.Build.props` and
  `src/FS.GG.Coord.Cli/FS.GG.Coord.Cli.fsproj` release notes; all three existing
  projects continue to reference the single scalar.
- Existing release rail: `.github/workflows/release-successor-candidate.yml`,
  `.github/workflows/release-successor-publish.yml`,
  `scripts/release-successor-publish.py`, `scripts/release_successor_journal.py`,
  and `tests/release-successor-candidate/run.py`. Select the proposed patch,
  genuine promoted 0.99 predecessor/source/channel, and a distinct prospective
  journal identity together. `utel-rel-17` is a proposed identity, not a grant or
  an observed unused journal. Preserve existing admission, sixteen effects,
  first-attempt/source guards and immutable archive recovery semantics.
- Test compatibility: `tests/new-sdd-workspace/product-board.fsx` as above.
- Existing records: `docs/roadmaps/utel-release-successor.md`, this SVG plan,
  `registry/dependencies.yml` and `registry/CHANGELOG.md`. Preserve accepted
  published 0.99 facts separately from prepared 0.99.1. The inspected registry
  still reports package frontier 0.98; reconcile that lag only from the genuine
  accepted 0.99 receipt, never label 0.99.1 published before readback.
- Derived outputs, only as required by the canonical generators: `docs/architecture.md`,
  `docs/registry/compatibility.md`, both `.agents`/`.claude` publishing-and-deployment
  `SKILL.md` mirrors, `registry/driver-skill-manifest.json` and the affected
  `registry/skills.yml` digest row. Preserve unrelated generated regions and use
  the repository generators. These are source/publication metadata, not activation.

Leave `creator-frozen-coord-dependencies.json`, its verifier and the Wizard
candidate download at genuine 0.99 during source preparation. Leave SDD defaults, installed selectors, Host
version/installation, telemetry stores and all operation credentials unchanged.
No generic release framework, protocol refactor or new workflow is needed.

**Proportionate qualification:** existing static release-binding tests first,
then the normal admitted source build/adapter suite and compiled ProductBoard
probe against the exact prepared source. Require the current Wizard source guard
to refuse before acquisition and the historical 0.99 positive fixture to pass.
Keep the original negative baseline receipt; repeat it only if new changes affect
its meaning. Run existing coherent-version evaluation, release-note and projection/
registry controls, the existing engine-pin fixture and authenticated live main
readback after the canonical-pin repair, routine eligibility/operation-boundary
fixtures, and exact-head
required/coherent CI. The source PR must be green on these ordinary gates; never
manufacture a current frozen-closure pass. Preserve all original failures and
separate source qualification from release qualification. Static sequencing plus
existing refusal/recovery controls is sufficient; no custom model or new costly
pipeline is selected. Build/qualification needs separate resource admission.

**Coordinate and effect gates:** fresh public NuGet indices show 0.99.0 present
and 0.99.1 absent for all three packages; the proposed release and journal ref
returned 404, while the available GitHub Packages CLI reader returned 403. These
partial observations do not establish a usable coordinate. Before native work,
the existing rail must authenticate its own scoped reader, verify all three
coordinates on both feeds, predecessor channel/source/content, all relevant
component/coherent tags and release, and the exact proposed protected journal.
Missing visibility, an occupied coordinate, a prior journal or an uncertain effect
stops its dependent step. Root decides a distinct successor or exact-byte recovery;
neither this plan nor source constants activate the publisher or confer a grant.

#### Later dependency boundary and actual receiver acceptance

After root accepts source delivery, hold final main through a separately admitted
first-attempt patch candidate, original seven-file archive authentication,
`publish=false` preflight, any selected publication, all sixteen journal effects
and both-feed readback. Preserve original bytes and source throughout. Only genuine
published patch identities permit the Wizard owner to update its dependency JSON,
existing candidate URL/path and selected-version assertions in
`tests/new-sdd-workspace-successor/frozen_dependencies.py`. Reuse the generic
closure verifier and version-derived package checks already delivered by #4301;
re-enumerate actual source/runtime populations rather than guessing hashes or counts.
Then requalify frozen source, Creator compile/probe, pack and original-byte closure
on that exact source. Source merge alone never clears the separate Wizard 0.16
feed-visibility/lineage hold or selects a Wizard candidate/publisher.

The same manifest mismatch is an actual-consumer dependency inside .8:
`ProductBoard.prepare` fetches `dist/dotnet/.config/dotnet-tools.json` from the
chosen immutable kit revision and rejects 0.97.0 with loaded 0.99/0.99.1. The
compiled probe injects a matching synthetic manifest; it proves neither the real
fetch nor native receiver acceptance. The demonstrated canonical repair to 0.99
supplies compatible source for a 0.99 receiver only once that exact immutable
revision and actual caller have been qualified. A source-built 0.99.1 must continue
to reject a real 0.99 manifest while the patch remains unpublished.

After genuine patch publication, root can join the canonical pin update to 0.99.1
with the mandatory publication projection and Wizard closure refresh. The canonical
manifest is excluded from the frozen source-leaf roster and Kit distribution, so
that isolated pin update does not itself require repacking the coherent patch.
Select its immutable producer revision and verify real kit/driver leaf digests,
loaded adapter digest, matching public tool version and actual preparation for
fresh and retained/conflicting workspaces. This is the existing receiver boundary,
not an automatic fleet default or installation. Templates full mode uses
`--no-coordination`, so its public Wizard journey cannot close this Product V2 gap.

Under Unified §9.9, the patch source changes no fresh workspace, installed tool,
lifecycle default or enabled telemetry. Published package consumption and later
explicit receiver selection are the first behavior changes; existing workspace
upgrades retain independent preservation/conflict/rollback checks. No installed
state repair, retrospective usage reconstruction, new Responses/Host operation
or GPU claim is selected. Keep .8 and its existing §9.8 link open. This window stops
at the local source-sequencing plan and typed return; root chooses the next window.

### Compatible telemetry patch source preparation — 2026-10-07

Root admitted local source preparation after canonical0.99 pin/main live checks and
publication projection landed, through merge `65c8d8ced152cea2634e44521cf25a572c26f79c`.
The same telemetry owner integrates qualified3e8 and prepares coherent0.99.1,
prospectiveutel-rel-17, and an independently source-derived ProductBoard probe version.
Published0.99 archives and frozen Wizard0.99 closure remain immutable and strict;
current frozen source drift refusal is expected. Source/compiled qualification,
canonical generated metadata, exact-head CI, publication, Wizard closure refresh
and actual receiver acceptance remain separate pending boundaries. No operation,
installation, default change or .8 completion follows. Native follow-up begin remains
unavailable with genuine old lineage; no token, baseline or usage was substituted.
