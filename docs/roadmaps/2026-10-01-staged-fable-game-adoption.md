# Staged Fable game foundation adoption

**FABLE-ADOPT-01 — Stage .1 and the selected .3 local reference source window accepted on 2026-10-01.** Fold
reusable findings from FourD, SC2 and BAR into their owning FS-GG producers, qualify the `fable-game` template as the reference consumer, then
adopt its shared dependencies and conventions incrementally in the three existing products.
The [Stage .1 disposition](evidence/fable-adopt-01.1-inventory-disposition-20261001.md) records
the accepted source inventory and ownership decisions. Implementation, publication, installed
qualification and adoption remain open.

The [unified roadmap](../2026-09-07-154210-fs-gg-unified-development-roadmap.md#992-staged-fable-game-foundation-adoption)
indexes this independent product track. It adds no gate to the already accepted V2 platform profile
and does not change historical product qualification or R5 evidence.

## Selected reference source delivery

[Templates #661](https://github.com/FS-GG/FS.GG.Templates/pull/661) delivered the FourD-shaped
local reference at `1c90ac81278a8e7a98fe98aa7d01b8023a8f2278`, tree
`e1a619ad37f87e851d57284d81ad62320a60db9a`, equal reviewed
`4eb2cc3985be42840422ddb9cc2d72f214b17489`. Exact-head composition, typed-receiver and
Release C hosted gates passed. Existing Game/Rendering APIs own the local session and browser
input/lifecycle; the reference supplies four-coordinate commands and pure product projections.
Disposed controls and hosts are inert. The source also repairs atomic adoption of the reviewed
old/current SVG wizard root forms, retaining rollback and rejecting unsupported roots.

This accepts the selected .3 source window. It does not establish feed publication, installed
creation, product adoption or native gameplay. Stage .4 prepares the coherent published set and
fresh installed local reference. Stage .2's external-authority capability gap and .3's broader
external-authority/WASM examples remain open before their dependent product adoption; do not
apply the local Game clock to native SC2/BAR. Retained upgrades require their separate preserving
qualification. Later .5–.7 outcomes remain open.

## Shared WASM dependency coordination

[WASM-SHARED-01](2026-10-02-shared-wasm-foundation.md) separately plans extraction of BAR/SC2
execution mechanics into a shared browser host, typed contracts and guest SDK. Coordinate the
Stage .3 WASM extension example against that qualified dependency. Its extraction and subsequent
WASM-only product adoption do not depend on this plan's FourD rendering pilot. This plan's
presentation/input migration retains its existing .5–.6 sequence. Product wire codecs, native
authority and module ABI compatibility remain with their adapters; shared package publication
and clean importing-consumer qualification are separate evidence.

## Inspected starting point

Fable compiles F# to JavaScript. Using Fable alone does not establish adoption of a common renderer.
The inspected browser projects use different presentation paths:

| Consumer | Inspected source | Current presentation boundary |
|---|---|---|
| `fable-game` | [Template dependencies](https://github.com/FS-GG/FS.GG.Templates/blob/cfe37f35a66e3494b211197a1c84de69f08bf87e/templates/fs-gg-fable-game/SvgFoundation/SvgFoundation.fsproj) and [arcade composition](https://github.com/FS-GG/FS.GG.Templates/blob/cfe37f35a66e3494b211197a1c84de69f08bf87e/templates/fs-gg-fable-game/SvgFoundation/ArcadeExample.fs) | Scene descriptions, `SvgBrowser`, `SvgInputHost`, `SvgSessionHost` and Game.Core; product scenes are projected directly |
| FourD | [Browser dependencies](https://github.com/FS-GG/FS.GG.FourD/blob/60f2a41eeaf22325b6af7a264791c79644c6fb6d/src/FS.GG.FourD.Browser/FS.GG.FourD.Browser.fsproj) and [interaction](https://github.com/FS-GG/FS.GG.FourD/blob/60f2a41eeaf22325b6af7a264791c79644c6fb6d/web/interaction.mjs) | Product Fable bridge and custom DOM grid/controls |
| SC2 | [Browser dependencies](https://github.com/FS-GG/FS.GG.SC2.Client/blob/e1f3fe8cdaca33a2c2e29c1aca6091b5f0a2633e/src/SC2.Client.Browser/SC2.Client.Browser.fsproj) and [live browser](https://github.com/FS-GG/FS.GG.SC2.Client/blob/e1f3fe8cdaca33a2c2e29c1aca6091b5f0a2633e/src/SC2.Client.Browser/Live.fs) | Direct SVG/DOM and a product native-session gateway |
| BAR | [Browser dependencies](https://github.com/FS-GG/FSBarV2/blob/393a43cde6950d7ba58ddf5c40072a91779882c4/src/Broker.Browser.Client/Broker.Browser.Client.fsproj) and [runtime](https://github.com/FS-GG/FSBarV2/blob/393a43cde6950d7ba58ddf5c40072a91779882c4/src/Broker.Browser.Client/runtime.js) | Elmish/Fable wrapper around product JavaScript rendering and native/guest adapters |

These revisions establish the inspected architecture, not the latest installed product versions.
Refresh the source and publication inventory before implementation.

[`FS.GG.Game.Render`](https://github.com/FS-GG/FS.GG.Game/blob/6e2558d811037104b0984d17e19a05dfe5a38421/src/Game.Render/FS.GG.Game.Render.fsproj)
is a pure Game.Core-to-Scene adapter with a default keymap. It is not the browser renderer, and the
inspected template does not directly reference it. Adopt that adapter where its primitives fit;
direct product-to-Scene projection remains valid.

Rendering already owns [normalized browser input](https://github.com/FS-GG/FS.GG.Rendering/blob/be57d2927d10b7f9defca8c45f8e0971de0abddc/src/Scene.SvgBrowser/SvgInputHost.fsi)
and [session generation, projection and disposal policy](https://github.com/FS-GG/FS.GG.Rendering/blob/be57d2927d10b7f9defca8c45f8e0971de0abddc/src/Scene.SvgBrowser/SvgSessionHost.fsi).
Reuse these capabilities before proposing additional abstractions. The
[BAR development audit](https://github.com/FS-GG/FSBarV2/blob/5bc309e2cbd5ae701b16b43d5f978742beee607d/docs/roadmaps/evidence/barc-01.5-development-audit-20261001.md)
is one input to finding triage, not proof that its incidents are shared-framework defects.

## Ownership and extraction rules

Give each finding one owning repository, exact source/run evidence, a minimal reproducer,
classification, narrow touch-set, sibling applicability and acceptance criteria. Classify it as
a shared defect, missing shared capability, consumer adoption gap, product fix or operational fix.
Record when an existing producer capability already solves it. An incident shared in appearance
still needs a reproducer before becoming an upstream defect claim.

| Finding or capability | Proposed owner | Extraction boundary |
|---|---|---|
| Physical gestures, semantic commands, focus, editing/IME, held-input release, SVG coordinates and hit testing | Rendering | Normalize browser observations while product adapters retain command meaning, targets and availability |
| Session ordering, replay and deterministic transition semantics | Game | Reuse Game.Core where applicable; native SC2 and BAR authority stays in their gateways |
| Generation/revision fencing, projection coalescing, suspension and resource disposal | Rendering, with Game for session semantics | Ordered commands and required completions retain their order; only replaceable presentation updates may coalesce |
| Fable package contents and shared browser examples/build conventions | Owning producer, then Templates | Producers ship their required source/native JavaScript; the template composes published APIs and product extension seams |
| Bounded browser failure evidence and production-path conformance | Rendering/Game for shared behavior; each product for its adapter | Reuse portable checks; keep native-engine acceptance and product evidence local |
| Process cleanup, diagnostic artifact custody and deployment records | Existing operational tool owner, selected during triage | Keep host/container operations in operational tooling |
| BAR stock-engine quantities, content/AI discovery; SC2 native receipts and module contract; FourD gameplay and saves | Respective product | Preserve domain rules and authority; extract only a separately demonstrated generic primitive |

Templates composes the foundation; it does not become the owner of duplicated runtime policy.
Net, Audio, SDD or Coordination receive work only when triage demonstrates a gap in their contracts.
Administrative and qualification contracts should derive an F#-validated descriptor and model state
changes in Quint. The separately planned `OPS-TYPED-01` track owns that parallel operational work
after fresh Astra review; it is not an existing replacement or a Rendering/Game responsibility.
When implementation is selected, use the existing producer/receiver coordination route with explicit
dependencies. This planning entry creates no issues, dispatch or release effects.

## Stages and exit evidence

Stage .1 and the selected .3 local reference window are **accepted at source**; remaining Stage .2–.7 outcomes are **open**.
A source merge, published package, generated workspace and installed product acceptance are separate
results; record each exact identity when it exists.

| Stage | Deliverable and owner | Dependencies | Completion condition |
|---|---|---|---|
| **.1 — Inventory and disposition — accepted source** | `.github` planning owner with producer/product owners: [findings-to-owner matrix, current API/package inventory and proposed common browser contract](evidence/fable-adopt-01.1-inventory-disposition-20261001.md) | Inspected product findings and existing Game/Rendering capabilities | Accepted matrix assigns every selected finding a reproducer or explicit evidence gap, one owner and an upstream/adoption/product disposition while retaining product acceptance baselines |
| **.2 — Upstream repairs and capability gaps** | Rendering and Game implement only admitted shared changes; operational owners implement their separate fixes | .1 disposition for each change | An admitted capability gap has a reviewed contract and source tests; a defect has a reproducer that passes its repair through the relevant .NET/Fable/browser route; compatibility and sibling applicability are documented |
| **.3 — Reference template composition** | Templates composes the qualified APIs, examples and build/test conventions into `fable-game` | .1 contract and relevant .2 source | A candidate generated workspace renders, maps input, replaces a session and disposes resources through the shared APIs; extensions cover product projection, semantic commands, external authority and WASM transport |
| **.4 — Coherent publication and installed qualification** | Producer release owners publish the compatible set; Templates publishes and qualifies the reference consumer | .2 and .3 candidate qualification | Exact package/template bytes are read back from required feeds; a fresh installed workspace compiles with Fable and runs browser conformance using delivered F# and native JavaScript. Qualify any retained upgrade promise separately |
| **.5 — FourD pilot** | FourD adopts one complete encounter incrementally, then its selected browser surface | .4 published set | The encounter uses shared presentation/input/lifecycle with thin product adapters; existing saves/replay, four-axis projections, accessible controls and gameplay parity pass through the actual product entry |
| **.6 — SC2 and BAR adoption** | SC2 and BAR owners migrate their selected browser surfaces in separate lanes | .5 confirms the common contract; relevant producer repairs are published | Each production browser path uses the qualified shared foundation; native authority, guest/WASM ABI, input meaning and accepted behaviors remain correct. Product-native acceptance stays separately reported |
| **.7 — Remove superseded infrastructure** | Product owners remove replaced browser policy; Templates/producer owners reconcile documentation and dependency conventions | Each product's .5/.6 parity and adoption evidence | No active path retains a second implementation of the replaced shared policies; package pins, build/test conventions, extension documentation and ownership agree |

Stage .1 closes inventory and disposition. The smallest FourD-shaped template slice under Stage .3
is source-qualified. Complete admitted Stage .2 capability work and broader reference extension
qualification where their consumers require it. Keep later product migration touch-sets as outlines
until the relevant published boundary is qualified. Close the programme only after all three selected product surfaces and their
installed adoption evidence pass; keep unresolved product gameplay qualification visible under its own plan.

## Parallel work and sequencing

The Stage .1 inventory covers the three products. After .1, independent Rendering, Game and
operational changes can proceed in disjoint touch-sets, while Templates prepares candidate composition against
the declared contract. Publish producers before consumers adopt their changed dependencies.
Coherent publication may batch compatible changes; it must not hide unrelated product behavior.

Prepare SC2/BAR adapter inventories and existing regression baselines during the FourD pilot.
Begin their implementation in parallel after .5 establishes the common boundary. If either product
reveals a shared gap, route it back to the owning producer, publish the repair, and let its sibling
continue any unaffected work. Track dependencies per capability and consumer rather than blocking
the entire programme on every open incident.

## Adoption shape and player behavior

Adopt shared dependencies and conventions inside the existing repositories. Use a freshly generated
`fable-game` workspace as a reference and comparison baseline. Port compatible infrastructure in
small reviewable steps, preserving product code, repository history and domain tests.

Shared scenes and retained SVG rendering cover the game presentation surface; ordinary DOM remains
appropriate for forms, editors and accessible panels. Preserve focus, editing and screen-reader
semantics when replacing custom controls. Do not force all product state into Game.Core: native
products supply authoritative projections and consume validated semantic commands through adapters.
The inspected `SvgSessionHost` is a Game-session host; qualify an external-authority composition seam
before applying its clock policy to SC2 or BAR.

FourD retains rules, Commitment/Pressure choices, four-axis views and save/replay formats. SC2 and BAR
retain their native engine/session ownership, authenticated gateway, observations, command receipts,
module sandbox and guest ABI. Browser normalization must preserve the distinction between a physical
gesture and its intended command; module code must receive the documented semantic input.

Players should retain their existing decisions, controls and game outcomes. Any deliberate gameplay,
save or module-contract change is a separately reviewed product change. Foundation migration itself
aims for behavior parity, consistent input/focus and reliable session recovery/disposal.

## Qualification and rollback

Exercise the real renderer and input route, including pointer coordinates under SVG transforms,
overlays, keyboard alternatives, editable focus/IME, blur/visibility transitions, reconnect, module
replacement, delayed replies, revision ordering and repeated mount/dispose. Check that stale
generation replies have no product effect, ordered commands survive projection coalescing, and owned
listeners, frames and requests return to their defined disposed state.

Use shared .NET/Fable correspondence checks for portable contracts and actual Chromium/browser checks
for DOM behavior. Compile independent consumer/module input cases from the documented contract so
tests can detect producer/consumer agreement errors. Fake gateways may qualify adapters but cannot
replace the real-game evidence required by the SC2 and BAR plans.

Each product migrates one usable slice before its broader switch. Retain the prior pinned dependency
set and runnable entry until parity passes; rollback restores that set and route without rewriting
saves, native state or evidence. Remove the superseded implementation after the new route is accepted.

## Generated and retained workspace boundary

The selected workspace family is the SVG `fable-game` template and its actual published creation
routes. No language-provider or lifecycle default change is selected. Stage .3 first changes candidate
source; .4 is the first stage that can change newly generated installed workspaces. Before .4, creation
uses the previously published payload; after .4, it receives the qualified common composition and
extension examples at recorded versions.

Existing SC2, BAR and FourD repositories change through their explicit .5/.6 adoption PRs. Publishing
a template does not rewrite them. Retained generated workspaces need a separate preserving update
qualification for any promised migration: protect owner edits, saves, configuration and extension code,
and prove an interrupted update can recover. Do not claim a blanket template upgrade guarantee.

Record source commits, package/template versions and digests, browser/toolchain identities, clean
creation, retained adoption where promised, parity results and remaining native-product gates in
the owning repository. Reflect completed stages in the unified roadmap after protected readback.

## Retain useful knowledge independently of source caches

Capture finding dispositions, architecture maps, decisions, incidents, successful and failed fixes,
qualification results and relevant supporting excerpts as revisioned project knowledge. Reference
exact repositories, revisions, files and digests for implementation details. Full source snapshots
and bulk symbol/search indexes belong in a separate disposable cache and are excluded from durable
knowledge backups. This follows the roadmap's
[knowledge retention constraint](../2026-09-07-154210-fs-gg-unified-development-roadmap.md#storage-and-capture-design).

For BAR, migrate the existing combined store by preserving knowledge/chapter documents, immutable
history, supersession/conflict relationships and required non-reconstructible evidence. Separate
source-derived material from curated knowledge without dropping architecture maps or prior findings.
Prove search, references and history after restore into a fresh environment before retiring the old
store. Review the resulting knowledge export for its public/private boundary; publish no full source
cache. This plan does not perform that live-store migration or claim a GitHub backup already exists.
