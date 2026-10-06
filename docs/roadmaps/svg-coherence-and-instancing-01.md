# SVG coherence, shared implementation and repeated-model performance

Date: 2026-10-04. Status: active; milestones .1 and .2 delivered, .3 in progress.
Shared implementation, deployment and host-browser acceptance remain open. Host GPU tests require
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
acceptance is claimed. The coherent 0.32.1 package candidate, genuine installed
receiver qualification and publication remain pending. This closes only that source
window; .3/.4, consumer adoption and the late host GPU batch remain open.

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
