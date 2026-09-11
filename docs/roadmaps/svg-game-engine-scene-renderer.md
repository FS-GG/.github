# SVG scene and renderer contract

Feature identity: **SVG-SCENE-02**.
Status: **Execution active; SVG-SCENE-02.1–.3 complete**.
Programme: [SVG engine and Fable workspace](../2026-09-07-064259-svg-game-engine-template-design-roadmap.md).
Unified part: **SVG game engine and Fable workspace completion**, in the
[feature index](../2026-09-07-154210-fs-gg-unified-development-roadmap.md#98-feature-parts-and-subroadmap-index).
Depends on: completed [SVG-FOUND-01](svg-game-engine-foundation.md) and
[SVG-QUAL-01](svg-game-engine-installed-model-qualification.md).

Planning baseline: `.github` `5c8020f68a112f338b184b54a12e83bd0821d866`;
Rendering `815783987fbf1d6f2e8165e2ae31ddf0bf61db2d`;
Templates `b081808cc8c802d264d8fdeaf417911ca821c211`.
Execution starts from those merged revisions or their verified successors.

Planning owner: `.github`. Rendering owns scene, vector, input mechanism and browser implementation;
Game owns its session/policy envelopes; Templates owns generated and external compatibility consumers.
SDD supplies its already published 1.7.0 model toolchain. This feature requires no SDD change unless a
new, reproducible producer defect is found. S.I.R. must not be accessed or changed; use only the
already recorded donor characterization and Templates-owned fixtures.

## Outcome and completion boundary

Deliver the portable scene/vector/browser contract required by Preview A: stable scene and node
identities, general affine composition, correct SVG paint and definitions, camera/picking, accessible
interaction, reproducible document serialization/export and qualified grid/continuous consumers.

Finish the remaining M0 inventory/decisions and M1 contract foundations, and qualify M2's selected
renderer surface. C01/C02 can reach source and local-consumer qualification. C03 receives its complete
declared rendering surface plus document round-trip/export evidence; arbitrary SVG import and authoring
remain M3. C18 and C19 receive the specific browser/accessibility/performance evidence below, not blanket
complete-workspace qualification.

This feature ends with an exact local candidate and a complete Preview A handoff. **SVG-PREVIEW-A**
owns producer/template publication and public installed receiver qualification. No local package,
successful browser run or source merge is Release A.

## Reuse and demonstrated gaps

Rendering #1279/#1280 supplied `RetainedScene`, `SvgRetained`, curated Fable Scene source and
`FS.GG.UI.Scene.SvgBrowser`. Rendering #1281 qualified the canonical literate model with public SDD 1.7.0,
192 matching real .NET/Fable transitions, negative controls and the Chromium effect boundary.
Templates #462 proved clean and retained typed-profile receivers. Reuse those sources and fixtures.

The inspected implementation still has concrete limits:

- Camera state supports pan and positive uniform zoom only.
- Scene has useful paths, paints, gradients, clipping and text values, but no SVG symbol/mask document
  or stable identities for the children inside an object's Scene subtree.
- Every successful browser transition clears each layer's object children, including selection and
  camera changes. Stable root/layer elements do not prove retained object/node reconciliation.
- Browser `setPaint` gives a stroked shape a fill, whereas `Paint.stroke` documents stroke-only style.
  `Shader.SolidColor` is accepted by validation but needs a correct paint-source mapping.
- Geometric hit-test fallback covers basic shapes, not the declared paths/text/definition surface.
- The browser assigns `role=button` with `aria-selected`; the accessible state mapping needs repair.
- Existing measurements cover one tiny Chromium fixture. They do not establish the programme's
  ordinary/dense workloads, cross-browser behavior, physical mobile performance or assistive technology.

The foundation's local `0.4.0-preview.1` package numbers are rehearsal identities. Rendering's local-pack
axis explicitly is not its publication axis. Review actual package history before assigning any release
identity; do not carry a rehearsal number into a public feed by assumption.

## Decisions

### Packages and compatibility

Keep portable data, validation, affine arithmetic and pure reducers in `FS.GG.UI.Scene`; keep DOM,
browser font loading and browser hit testing in `FS.GG.UI.Scene.SvgBrowser`. Extend their existing curated
Fable views and compatibility metadata. Do not introduce a new package per logical module.

Keep existing public records and `SceneNode` cases compatible. Add a Rendering-owned SVG document
envelope that embeds existing Scene geometry instead of replacing it. Its identified elements contain:

- stable element identity, explicit ordered children and an affine transform;
- either an existing Scene leaf, an identified group or a local symbol instance;
- explicit optional clip/mask references and presentation attributes;
- semantic-object identity separately from render-node identity.

Definitions contain typed local symbols, clips, masks, gradients and font references. Geometry continues
to use Scene `Point`, `Rect`, `PathSpec`, `Color`, `Paint` and `FontSpec`. New definition/placement metadata
does not become a competing general geometry vocabulary.

Provide a checked adapter from the foundation `RetainedScene` and retain its public mount/dispatch
entry points. Both old and new entry points use the corrected shared implementation. Record behavioral
repairs explicitly and retain the old reducer's accepted trace corpus; do not preserve a demonstrated
paint or accessibility defect merely because an earlier fixture missed it.

Rendering owns versioned asset/document and build-time extension descriptors. Game owns generic
session/input/snapshot identity envelopes; no Rendering dependency enters Game.Core. M1 session
contracts describe initialization, input admission, advancement, projection, snapshot and restoration
without claiming that the M5 worker/server implementations already exist. Each new public contract
has a compiling consumer example and an explicit runtime-support classification.

### Coordinates, ordering and identity

Use affine six-tuples with the SVG convention:

`x' = a*x + c*y + e`, `y' = b*x + d*y + f`.

Composition `compose parent local` means apply local first, then parent. World-to-viewport camera and
element-local transforms are distinct. Support translation, rotation, positive/nonuniform scale,
reflection and skew through this matrix representation. Preserve foundation pan/zoom through an adapter.

Reject non-finite matrices before scene acceptance. Singular element transforms produce no interactive
inverse and an explicit diagnostic; they must not cause false picking. Camera transforms must be
invertible. Define numerical tolerances in the qualified fixture domain and keep those numerical claims
outside bounded Quint safety proofs. Test matrix arithmetic against independently calculated examples,
not only inverse/forward functions sharing one implementation.

CSS client pixels, SVG viewport coordinates, world coordinates and device pixels remain distinct.
Resize/viewBox and device-scale changes update the projection without mutating gameplay coordinates.
Anchored zoom/rotation retains the same world point under the anchor.

Layer and child lists define paint order; later siblings paint above earlier siblings. IDs identify
objects and break ties only where an API explicitly accepts unordered input. Duplicate identities are
invalid, never an implicit last-write-wins rule. Reordering moves existing DOM elements. This follows
the [SVG rendering model](https://www.w3.org/TR/SVG2/render.html).

### Preview-A SVG surface

| Surface | Selected mapping and validation |
|---|---|
| Geometry | Existing empty/group/rectangle/circle/ellipse/line/path/translation, plus existing Arc/ArcTo and TextRun. Paths preserve nonzero/evenodd fill rules; split complete elliptical revolutions into representable SVG arc segments. Reject malformed/non-finite geometry with node paths. |
| Paint | Solid fill or stroke following existing Paint style; explicit independent fill/stroke in the new SVG presentation envelope. Preserve cap/join/miter, opacity and dash arrays. Resolve `Shader.SolidColor`; map existing linear/radial color lists to evenly spaced stops. Add typed explicit stops for SVG documents. |
| Gradients | Linear/radial, `userSpaceOnUse` and `objectBoundingBox`, explicit gradient transform, pad/repeat/reflect. Stops are ordered, offsets are bounded, and missing/cyclic references are errors. Use sRGB interpolation for the selected profile. |
| Symbols | Local typed definitions and instances with explicit viewport/viewBox placement. No external `<use>` references. Instance identity stays separate from shared definition identity. |
| Clipping | Rect/path clips, explicit units and nested intersections. A clipped-out point cannot hit the clipped object. |
| Masks | Typed alpha and luminance masks with explicit region and units; supported vector content only. Mask transparency does not automatically change pointer targeting. Authors use a clip or explicit interaction geometry when they need that behavior. |
| Text | Plain text, SizedText and TextRun with explicit family/size/weight, anchor and direction. Browser shaping is presentation evidence, not deterministic gameplay geometry. |
| Export | A complete accepted document exports SVG with all required local definitions, stable references, declared fonts and preserved ordering; export never serializes only the currently visible/cached DOM. |

Gradient, masking and paint mappings follow the
[SVG paint-server contract](https://www.w3.org/TR/SVG2/pservers.html),
[painting contract](https://www.w3.org/TR/SVG2/painting.html), and
[CSS masking contract](https://www.w3.org/TR/css-masking-1/).
The pointer distinction follows [SVG interaction semantics](https://www.w3.org/TR/SVG2/interact.html).

Reject unsupported features explicitly: raster/external resources, uploaded script/CSS/event handlers,
foreignObject, filters, sweep gradients, perspective, non-sRGB color spaces, native glyph proof payloads,
arbitrary pictures/charts/regions and executable extensions. Do not silently flatten them. A transparent
existing cache wrapper may be supported by recursively validating its content; native cache semantics
are not exported as SVG behavior.

Use bounded `fsgg.svg-document/1` serialization for the typed document with .NET/Fable round trips and
unknown-version refusal. Arbitrary SVG XML import, editing tools and full font embedding/outline conversion
remain M3. This distinction leaves C03's complete import/export criterion open until that later work.

IDs in emitted DOM/definitions are derived from a caller-supplied unique mount namespace plus typed
document identity using collision-free escaping. Reject duplicate mount namespaces in one document host.
Never emit an unvalidated identifier as raw markup or resolve references outside the accepted document.

### Fonts and geometry spike

Use self-hosted Noto Sans Latin regular for deterministic gallery comparisons, with an exact locked
Fontsource package, font-file digest and OFL notice recorded before copying. Select only the required
WOFF2 asset; use no CDN at runtime. The production API accepts declared font assets and exposes
loading, ready, fallback and failure state. A missing font gives an explicit fallback and diagnostic;
it cannot silently become a passed font-fidelity result.
[Fontsource Noto Sans](https://fontsource.org/fonts/noto-sans) is the selected distribution family.

Browser/native shaping differences and unsupported scripts remain recorded. Font metrics cannot become
authoritative collision bounds. Text interaction uses authored semantic hit bounds where portable
geometry is required; browser text hit testing is a presentation capability.

For the M0 Boolean-geometry spike, select `polygon-clipping` 0.15.7 behind a browser authoring-only
adapter, with bounded adaptive curve flattening and explicit approximation tolerance. Prove holes,
coincident edges, degeneracy and cancellation/size limits before committing its M3 integration.
It is MIT and exposes polygon union/intersection/difference/xor; it does not promise exact Bézier
Boolean operations. [Producer package](https://github.com/mfogel/polygon-clipping/blob/main/package.json).
Do not add this dependency to Preview A's player or claim gameplay exactness.

### Input, accessibility and retained work

Keep selection/focus semantic and independent of DOM identity. The browser host must route keyboard,
pointer and the HTML alternative through the same admitted commands. Preserve current input mechanism
ownership; do not implement a second template-local keymap resolver.

For the preview, provide ordinary HTML object-selection buttons with `aria-pressed`, clear accessible
names, a selection status, and keyboard camera controls. The SVG supplies title/description and visual
focus/selection. If a scene is presented as an interactive application, that mode is explicit rather than
automatically swallowing browser/assistive-technology navigation. Avoid duplicate tab sequences between
the SVG and HTML alternative. The [WAI button pattern](https://www.w3.org/WAI/ARIA/apg/patterns/button/)
defines the button state and keyboard behavior.

Do not consume keyboard events in native editable controls, composition, or unsupported gestures.
Handle pointercancel, lostpointercapture, focus loss and disposal without stuck capture. Touch dragging
is enabled only on the scene's declared gesture surface; page scrolling remains available outside it.

Reconcile by stable node identity. Selection/focus changes update state attributes only; camera changes
update the camera transform only; unchanged layers/objects/definitions are retained. Coalesce presentation
work on requestAnimationFrame while reducer state remains synchronously ordered. Rejected updates do not
schedule a render. After stabilization, unchanged scenes schedule no frames and rebuild no nodes.

Minimal input and authoring contract reducers cover the M1 foundation: guarded selection/camera commands,
atomic document replacement/edit rejection, undo/redo and immutable play-snapshot handoff. Their public
behavior and actual reducer correspondence are implemented here. Full modal sequences, tools, docking,
editing UI and session runtime remain M3–M5.

## Browser and workload contract

Use the existing locked Playwright 1.62.1 browser family and Vite 8.1.5 unless a demonstrated incompatibility
requires an explicit pin update. Record actual browser revisions. Preserve the exact installed SDD 1.7.0
profile/tool identities from SVG-QUAL-01; do not substitute the ambient authoring compiler.

Functional acceptance runs Chromium, Firefox and WebKit at 1280×720/DPR1, 800×600/DPR2, and a
390×844 touch-emulated viewport. Reflow is additionally checked at 320 CSS pixels and at 400% zoom.
Touch emulation is not physical mobile performance qualification. A real assistive-technology session
must verify the HTML alternative, selection announcement and focus recovery on one supported desktop
browser; automated role inspection alone is insufficient for that claim.

Milestone .1 records the exact dedicated Linux x86_64 performance host CPU, RAM, OS, display/headless mode,
browser, power/load conditions and serving/cache settings. Freeze that reference before measuring the
candidate. Shared-CI elapsed time is diagnostic unless the run satisfies those conditions.

| Workload | Fixed fixture | Preview-A acceptance |
|---|---|---|
| Ordinary | 100 visible semantic objects, four layers; each object includes a shape, a 12-segment path and a short text label; shared gradients/symbols | p95 input-to-presentation ≤100 ms |
| Dense | 200 such objects plus 20 clip/mask groups and a selection overlay | p95 input-to-presentation ≤150 ms |
| Definition/curve gallery | 200 paths ×256 segments, 64 gradients, 32 masks, 100 text runs and repeated symbols | Functional fidelity, bounded validation and separately recorded stage timings; no claim that it satisfies ordinary-game latency |
| Idle | Stabilized ordinary scene for 10 seconds | Zero scheduled frames and zero node/layer rebuilds |
| Interaction | 100 selection changes, 100 camera changes and one-object revisions | Unchanged object/definition elements retain identity; selection/camera updates do not rebuild scene children |
| Lifecycle | 100 mount/update/dispose cycles | No surviving owned listeners, frames, observers, font handles or document roots; available retained-heap observations included |

Use five warm-up iterations, at least 200 interaction samples per ordinary/dense run, three independent
runs, raw observations and a declared p95 calculation. Distinguish callback-to-next-frame timing from
actual presentation; if a required presentation metric is unavailable, report unavailable rather than
substituting rAF as a pass. A failed accepted target requires optimization or an explicit programme
amendment, not automatic threshold inflation.

Initial safety limits are 4 MiB serialized document, 10,000 document nodes, 100,000 path segments,
512 definitions, 32 nesting/reference levels and 50,000 expanded symbol nodes. Validate before DOM
mutation; cyclic references fail independently of depth. These are profile limits, not benchmark targets.

Record startup raw/gzip bytes separately for runtime, fixture content and fonts. The minimal grid/continuous
player entry must remain ≤150 KiB gzip of executable JS/CSS, excluding separately reported font/content
bytes, and reach first usable interaction within 2 seconds on the frozen local reference with an empty
browser cache. These are chosen Preview-A budgets, not claims inferred from the foundation measurements.
Authoring Boolean geometry and future studio modules must be absent from this entry's dependency graph.

Full world-extent culling, 60 Hz gameplay/effects, physical mobile performance and integrated M6–M8 workloads
remain SVG-SCALE-01/M9. The existing 20% extent-cost target is preserved for that qualification.

## Executable milestones

- [x] **SVG-SCENE-02.1 — Close the remaining inventory and freeze qualification subjects — route: routine**

  Depends on: SVG-QUAL-01.3.

  Owners/touch set: `.github` programme ledger and one bounded M0 evidence report;
  Rendering documentation/fixtures; Game and Templates contract inspection. Do not access S.I.R.

  Acceptance: every C01–C20 row identifies implemented, locally demonstrated, published, installed or missing
  evidence and its owning future feature. Record M0.1–.7 dispositions, actual package axes, supported/rejected
  Scene cases, third-party assets, geometry/font spike results, browser/workload manifest and migration
  inventory. Bind the existing input/editor/session contract sources and the precise additions required by
  .2/.4. No missing implementation is marked complete by inventory alone. Check native published package
  identities before selecting future candidate versions.

  Evidence: the [M0 inventory and qualification-subject report](../reports/2026-09-11-svg-scene-02-m0-inventory.md)
  binds exact `.github`, Rendering, Game and Templates revisions; separates source/local/public/installed
  evidence for C01–C20; records every M0.1–M0.7 disposition; and freezes package axes, the selected/rejected
  Scene surface, third-party archives, geometry/font spike, browser/workload manifest and migration subjects.
  Public Scene is already 0.28.0 while SvgBrowser is absent, so the foundation rehearsal number is not reused
  as a release claim. Missing capabilities remain assigned to their accepted future features. No S.I.R. access
  occurred, and no package, provider or default changed.

- [x] **SVG-SCENE-02.2 — Deliver portable document, affine and contract envelopes — route: routine**

  Depends on: .1.

  Owners/touch set: Rendering `src/Scene/`, curated Fable metadata, API baselines,
  `models/svg-foundation/` and portable tests; Game's existing Core/Fable contract files and focused
  contract consumers only where session/input identity declarations are actually missing.
  Rendering must not depend on Game.

  Acceptance: existing consumers compile unchanged. New identified SVG documents embed existing Scene
  leaves, validate references/limits and compose affine transforms correctly. Independently calculated
  translate/rotate/skew/reflection examples and degenerate cases pass on .NET/Fable. Asset/extension
  envelopes and Game session envelopes have compiling examples and explicit compatibility/support
  classification; no placeholder declaration is claimed to implement M5 sessions. Begin canonical model
  amendments alongside new state semantics, preserving the existing 192-transition corpus.

  Evidence: Game [PR #621](https://github.com/FS-GG/FS.GG.Game/pull/621), merged as
  [`494bd455`](https://github.com/FS-GG/FS.GG.Game/commit/494bd45591851c03496460e250f6584025fb5e52),
  delivered Rendering-free session/input/projection/snapshot envelopes with exact compatibility checks,
  compiling packed .NET/Fable consumers and an explicit `ContractEnvelopeOnly` classification. Rendering
  [PR #1282](https://github.com/FS-GG/FS.GG.Rendering/pull/1282), merged as
  [`acfca1e8`](https://github.com/FS-GG/FS.GG.Rendering/commit/acfca1e87866f7c1b4cab3b064e225d8f585a977),
  delivered the identified document/Scene-leaf envelope, reference and profile-limit validation, affine
  composition and asset/extension contracts. Its 102 Scene tests and isolated packed .NET/Fable consumers
  passed translate/rotate/skew/reflection plus singular/non-finite cases and replayed all 192 existing model
  transitions with unchanged projection SHA256
  `cd1b2c74c95a5f25df06ca0921af7d7af9c578d1f589ef6012fea56fadd5c0d2`. The
  [portable-contract report](https://github.com/FS-GG/FS.GG.Rendering/blob/acfca1e87866f7c1b4cab3b064e225d8f585a977/docs/reports/2026-09-11-svg-scene-02-portable-contracts.md)
  records support boundaries and the SVG-SCENE-02.4 model-amendment seam: .2 adds no reducer state and makes
  no M5 runtime claim. All candidate packages remain local; no publication/default activation occurred, and
  S.I.R. remained read-only.

- [x] **SVG-SCENE-02.3 — Complete the selected SVG paint and definition mapping — route: routine**

  Depends on: .2.

  Owners/touch set: Rendering Scene validation/serialization, `src/Scene.SvgBrowser/`,
  browser conformance gallery, package metadata and focused tests.

  Acceptance: every selected table row renders, serializes and exports through the public API; unsupported
  cases fail with stable object/node diagnostics before mounting or replacement. Fixtures expose stroke-only
  interiors, solid shader precedence, evenodd holes, complete arcs, duplicate IDs, gradient stops/transforms,
  nested clips, alpha/luminance masks, symbol instances and explicit font failure.
  Loading exported SVG in an isolated browser preserves the selected visual result and local references.
  .NET/Fable document round trips agree without claiming arbitrary SVG import.
  Use analytic DOM/geometry checks and per-browser visual references, with interior-color assertions
  and meaningful tolerances rather than one permissive screenshot threshold.

  Evidence: Rendering [PR #1283](https://github.com/FS-GG/FS.GG.Rendering/pull/1283), merged as
  [`b3a8a2c4`](https://github.com/FS-GG/FS.GG.Rendering/commit/b3a8a2c4caf6945bb0a2d9570a11d6b27fd8fb96),
  delivered bounded `fsgg.svg-document/1` serialization and standalone export, selected paint/geometry,
  typed gradients, symbols, nested clips, alpha/luminance masks, declared fonts and pre-mount stable
  diagnostics through the public Scene/SvgBrowser surface. Its 105 Scene tests, isolated packed .NET/Fable
  consumers and Chromium gallery passed at the exact merged head. Typed round trips and exports agreed at
  SHA256 `92769fe60988b62b044b10c7fcd6f50b921bfeae6a82e20c6073818d2c49f31a`; the existing 192-transition
  corpus remained unchanged at SHA256
  `cd1b2c74c95a5f25df06ca0921af7d7af9c578d1f589ef6012fea56fadd5c0d2`. Analytic DOM/definition checks,
  interior-color assertions and a per-channel visual tolerance of 28 covered the selected Chromium result;
  isolated reload of the exported SVG preserved local references and matched sampled interiors within two
  channel values. The
  [paint/definition report](https://github.com/FS-GG/FS.GG.Rendering/blob/b3a8a2c4caf6945bb0a2d9570a11d6b27fd8fb96/docs/reports/2026-09-11-svg-scene-02-paint-definitions.md)
  records the exact supported/unsupported boundary: typed document round trip is not arbitrary SVG import,
  broader browser/accessibility/performance qualification remains .5/.6, and .3 adds no reducer semantics.
  Rendering remains independent of Game; packages stayed local, no publication/default activation occurred,
  and S.I.R. remained read-only.

- [ ] **SVG-SCENE-02.4 — Retain nodes and deliver accessible interaction contracts — route: routine**

  Depends on: .2; integrate .3's full surface before closure.

  Owners/touch set: Rendering retained/document reducers and canonical models, KeyboardInput's narrowly
  required portable adapter, SvgBrowser, and focused unit/browser tests.

  Acceptance: reorder/update only affected nodes; camera/selection changes retain all scene children.
  Paint-order, transformed path/text/symbol hits and clipping agree with the declared browser semantics;
  masked transparency follows the explicit policy. Mount namespaces cannot collide. Keyboard, pointer and
  HTML controls select the same semantic identity. Native editing/composition is preserved; loss/cancel/
  dispose clears capture and scheduled work. Atomic edit rejection, undo/redo and immutable play-snapshot
  witnesses run through real minimal contract reducers. This proves contract foundations, not complete
  editor/input tooling. Existing supported entry points share the fixes.

- [ ] **SVG-SCENE-02.5 — Qualify models and the complete browser surface — route: routine**

  Depends on: .2–.4.

  Owners/touch set: Rendering canonical literate models/bindings, existing installed profile qualification,
  .NET/Fable replay consumers, browser gallery and path-selected CI.

  Acceptance: public SDD 1.7.0 authors/inspects offline using exact provisioned tools. Real .NET/Fable
  reducers replay the expanded corpus and produce matching projected results. Wrong order, stale revision,
  invalid reference acceptance, lost capture and non-atomic edit mutants fail at the first divergence.
  Finite models cover state/control behavior; geometry, font and browser timing retain their native proofs.
  All three browser families pass declared functional/visual/resize/DPR/touch-emulation/reflow cases.
  The accessible desktop journey is observed with actual assistive technology.
  Failed/unavailable dimensions are not folded into passing aggregate counts.

- [ ] **SVG-SCENE-02.6 — Meet Preview-A workload and resource budgets — route: routine**

  Depends on: .3–.5.

  Owners/touch set: Rendering performance fixtures, browser observation schema, focused renderer fixes
  and path-selected qualification workflow.

  Acceptance: the frozen workloads meet the above latency, idle, retained-update, resource and startup
  predicates, with exact environment and raw observations retained. Deliberate unnecessary rebuild,
  listener leak and excessive-document controls demonstrate that the respective gates fail.
  Report font/content bytes, browser stages and unavailable heap/presentation evidence separately.
  Do not claim complete C19/M9 or physical mobile qualification.

- [ ] **SVG-SCENE-02.7 — Qualify generated consumers and hand off Preview A — route: routine**

  Depends on: .1–.6.

  Owners/touch set: Templates `templates/fs-gg-fable-game/SvgFoundation/`,
  `tests/composition/fable-game/`, staged upgrade script and relevant composition workflows;
  `.github` programme/Unified evidence links. Rendering changes only for discovered producer defects.

  Acceptance: grid and fractional-coordinate consumers exercise the same published API shape from isolated
  local candidate packages, including defs, transforms, clipping, masks, text, selection and export.
  The tactical compatibility fixture uses only its already captured disclosed data; S.I.R. is not accessed.
  Clean `--svgFoundation true` creation preserves both the `none` source-only journey and explicit
  typed-SDD/profile-2 journey. A retained foundation receiver adopts staged changes without destructive
  scaffold rerun, preserves authored/lifecycle/skill content, refuses collisions and demonstrates rollback.

  Reconcile every M0/M1/M2 work-package outcome against its actual evidence. Contract-only session
  declarations do not close M5; minimal edit/input contract reducers do not close M3/M4.
  Record any unfulfilled M1/M2 obligation as a specific remaining prerequisite before Preview A, not as an
  unexplained whole-program dependency or a silently waived condition.

  Retain exact candidate archives/source hashes, API comparison, package-axis choices, required-feed plan,
  template pins and clean/upgrade evidence. The public producer/template publication and installed public
  receiver journey remain pending for SVG-PREVIEW-A.

## Generated-workspace impact

Affected family: opt-in `fable-game` / `fs-gg-fable-game` SVG preview with existing lifecycle choices.
The ordinary arena, omitted provider/lifecycle behavior and other provider families do not change.

Before: the fixture supports the foundation's small pan/zoom/selection scene.
After .7's candidate composition: the selected fixture demonstrates the full Preview-A document and
renderer surface, accessible controls, export and qualified contract behavior. This is the first milestone
that changes freshly generated candidate contents.

Public installed behavior changes only after SVG-PREVIEW-A publishes compatible Rendering producer
artifacts, Templates adopts those immutable pins and publishes its package, and the selected
direct/provider/wizard path actually consumes them. Required new skill bytes additionally pass their
owner publication and SDD materialization boundary. SDD 1.7.0 is already public; Rendering/Templates
foundation candidates remain local. Public versions are assigned from actual producer axes, not the
foundation's rehearsal numbers.

Existing workspaces use the supported staged upgrade path. New publication does not overwrite existing
files or refresh skills. Versioned SVG document migration and package/config rollback are explicit;
later saves/replays remain under their future identity contracts.

## Observation, exit and next feature

Use native source/check/package/browser results and existing UTEL observation. Preserve prior feature
evidence and whole-request planning, implementation, review, failures and repair lineage.
Automatic parent/child and CI-population coverage remains incomplete; platform-native child usage remains
unsupported. Missing usage cannot support an efficiency claim and does not block valid source delivery.

No V0–V6 completion is an entry gate. Candidate-bound shared inputs and protected operations still follow
their actual owner contracts. Stop only the affected claim on a real correctness, artifact, tool or
authority failure; continue independent authorized work without weakening evidence.

Exit when .1–.7 pass and the Preview-A packet names every actual remaining operation prerequisite.
The next feature is **SVG-PREVIEW-A**, which publishes the coherent producer/template set and proves
public installed clean/upgrade consumers. Release A remains open until that feature closes its actual
release predicates. C03 import/authoring, complete C18/C19 and Releases B–D remain in their accepted
later features.
