# Shared WASM execution foundation

**WASM-SHARED-01 — extraction plan, 2026-10-02. Stages .1–.4 are closed, including publication and fresh installed qualification; adoption stages .5–.7 remain open.**

Extract the reusable browser WASM host and guest authoring primitives from BAR and SC2 into
versioned dependencies. Qualify a fresh importing consumer, migrate the two existing hosts, and
provide a FourD reaction-policy example. Future products should consume the same foundation.
This product infrastructure track adds no gate to V2 platform acceptance.

The [unified V2 roadmap](../2026-09-07-154210-fs-gg-unified-development-roadmap.md#98-feature-parts-and-subroadmap-index)
indexes this part as WASM-SHARED-01. Its
[workspace boundary](../2026-09-07-154210-fs-gg-unified-development-roadmap.md#993-shared-wasm-execution-foundation)
separates source extraction, publication and installed adoption. The `.github` roadmap owner
coordinates the programme; FS.GG.Game is the selected producer.

## Inspected source and existing consumers

| Consumer | Inspected protected source | Current implementation |
|---|---|---|
| BAR | `9f0d8712e2c9a21cbf49f515a65b520516239b5a` | [Worker](https://github.com/FS-GG/FSBarV2/blob/9f0d8712e2c9a21cbf49f515a65b520516239b5a/src/Broker.Browser.Wasm/guest-worker.js), [module admission](https://github.com/FS-GG/FSBarV2/blob/9f0d8712e2c9a21cbf49f515a65b520516239b5a/src/Broker.Browser.Wasm/wasm-profile.js), supervisor and product wire validation; local Rust guest SDK |
| SC2 | `57eadf00bb9e776664e00fe200bb4f2952f72829` | [Worker](https://github.com/FS-GG/FS.GG.SC2.Client/blob/57eadf00bb9e776664e00fe200bb4f2952f72829/fixtures/guest-worker.js), supervisor, product contracts and Rust/C author tooling |
| FourD | `60f2a41eeaf22325b6af7a264791c79644c6fb6d` | [Uploaded reaction policies](https://github.com/FS-GG/FS.GG.FourD/blob/60f2a41eeaf22325b6af7a264791c79644c6fb6d/docs/FOURD-01.design-v2.md) are a proposal; no third implemented host is established |

Both existing workers implement allocation, a two-u32 output descriptor, bounded byte spans,
guest invocation, output copying and cleanup. Their admission, response validation and lifecycle
contracts differ. A shared implementation must preserve each qualified product contract through
explicit compatibility profiles; common behavior cannot be inferred solely from similar source.

The [Fable foundation adoption plan](2026-10-01-staged-fable-game-adoption.md)
owns presentation, input and session composition. Its external-authority/WASM reference remains
open. Coordinate that reference with this dependency, while allowing WASM extraction and BAR/SC2
adoption to proceed without waiting for the FourD rendering pilot. Native gateways retain their
authority and clocks.

## Dependency boundary

The package names below are selected. `FS.GG.Wasm.Contracts` and `FS.GG.Wasm.Browser` now exist as
published version **0.2.0** from FS.GG.Game on GitHub Packages and nuget.org. The first
immutable 0.1.1 baseline remains historical. FS.GG.Game owns shared execution policy and releases;
the guest SDK ships as a versioned source archive.

| Deliverable | Shared responsibility | Product responsibility |
|---|---|---|
| `FS.GG.Wasm.Contracts` | Typed F# descriptors for ABI profiles, export signatures, module identity, resource limits, invocation identity, generations and outcomes | Observation/command schemas, authentication, game rules and native receipts |
| `FS.GG.Wasm.Browser` | Admission, isolated worker execution, allocation/span checks, copied output, deadlines, fault settlement, generation fencing, replacement and disposal | Validating decoded product responses and applying authorized effects |
| Guest SDK | Portable descriptor/allocation helpers, ABI documentation and independently built Rust/C examples; distribution format selected explicitly | Product wire codecs, command builders and specialized author APIs |
| Conformance assets | Shared adversarial modules and production-path browser checks | Native-game journeys and product-specific regression evidence |

F# should own the supervisory state transitions and validated configuration. Keep the JavaScript
needed for WebAssembly and Worker APIs within the shared package. A Quint model should exercise
the lifecycle and request settlement; replay relevant traces through the production F# reducer.
The model does not establish memory safety or browser resource enforcement by itself.

Generic invocation transports opaque bytes. Controller and advisor roles describe bounded capacity
and confer no native command grant; an advisor result cannot enter a command route by changing a browser envelope. Session authority,
receipt interpretation and handling of unknown native effects stay in the product adapters.

## Compatibility and resource contracts

- Preserve BARC and SC2C export names and ABI versions through profiles. BAR currently checks
  version `1`; the inspected SC2 worker checks `0x00010000`. Do not silently replace either guest ABI.
- Preserve existing product input/output codecs and validation. BAR response identity validation
  remains an adapter capability. SC2 offline/live/production framing remains product-owned.
- Inventory export signatures, alignment, empty-output rules, import policy, memory/table maxima,
  initialization, shutdown and cleanup semantics before declaring profile compatibility.
- BAR admission currently validates finite memory/table maxima, signatures and export sets and
  rejects start functions. Preserve these checks. Additional SC2 restrictions require explicit
  compatibility evidence; extraction must not weaken an existing product boundary.
- Validate descriptor/input/output ranges and non-overlap before using guest memory. Refresh memory
  views after guest calls that can grow memory. Never free a malformed, overlapping or unowned span.
  Define how cleanup faults settle the invocation and dispose the worker.
- Bound compile, instantiate, allocation, invocation, free and shutdown through a supervisor outside
  the executing worker. Guest start functions and cleanup calls are executable guest code too.
- A browser deadline terminates execution; it does not supply deterministic instruction fuel or
  a complete operating-system memory quota. Document the supported browser contract accurately.
- FourD authoritative or replayable policies need a separately qualified deterministic execution
  contract. A native fuel-metered backend may be added later; browser elapsed-time limits alone
  cannot establish replay determinism. The initial FourD example must state its execution scope.

## Stages and completion evidence

### Connected producer prerequisite to .5 — source, publication and installed qualification CLOSED

The published 0.1.1 boundary exposed missing connection between the real Worker and its
F# lifecycle. Empty BAR output, unaligned output and lifecycle expiry counterexamples required
repair before product adapters could rely on that boundary. [Game #679](https://github.com/FS-GG/FS.GG.Game/pull/679)
delivers the connected producer at protected `4afacb501b9371b4cc81494b7bf91b46c880a663`,
tree `191d7c0ca82af8a4d610e88b35ce97df802d3463`.

The actual Worker now joins typed load/initialize/invoke/shutdown identities, phase and
queue policy, deadline settlement and physical termination. Unsafe guest output retires the
worker and its authority. Formal exploration covers both cold and initialized states;
five production F#/.NET/Fable state-and-effect traces and four causal mutants passed.
The fresh exact-candidate consumer passed .NET/Fable, independently built Rust/C modules
and all 25 Chromium cases using empty locked package caches. This is source qualification.

Native ApiCompat against the genuine published 0.1.1 archives identified four removed Browser
constructors: WorkerCommand, RequestProjection, EffectProjection and HostProjection.
Contracts remains compatible. The selected successor is coherent **0.2.0**, with that exact
pre-1.0 migration recorded rather than suppressed. The immutable first release remains 0.1.1.
[Publisher 37037339434](https://github.com/FS-GG/FS.GG.Game/actions/runs/37037339434)
qualified that immutable producer, both genuine feeds and SDK assets. SDK archive SHA-256 is
`ddd7b0ea76a1811ec3cdd45c373ac0ee556cf90eb9ae4cecad48dec1095475a3` and manifest SHA-256 is
`dee8b02ad0e43e7d279492ed6378eb873f76edeb5a72c29e9c336409d5907853`.
The original installed run 37039137022 passed org 25/25 and public 24/25, then failed one expiry
assertion. It remains retained evidence. [Game #680](https://github.com/FS-GG/FS.GG.Game/pull/680)
repaired only qualification's missing typed deadline reason and allowance for two valid canonical
expiry schedules at protected `7397d4b408b8e7b44393fee9f837278723934867`, tree
`4881135b718beaeb87eab1763d9f13e31410389f`; producer bytes and tag stayed unchanged.
Fresh [installed run 37047631080](https://github.com/FS-GG/FS.GG.Game/actions/runs/37047631080)
passed **25/25 org and 25/25 public** cases, .NET/Fable and independently built Rust/C modules.
Both consumers use lock SHA-256
`d6a4e7dde5754ac50c73f9887c4aedf2e1f845b967ecbd6b26f9eb150667439e`.
Root authenticated both original served archives against native digests and independently joined
producer, qualifier, SDK and manifest in the full native log. Root readback receipt SHA-256 is
`bc5b5e7d292c37fa6025462fba1c4821131fc144eafeaf5fbf5a74aa59dfa788`.
This closes .5-P's connected 0.2.0 publication/installed prerequisite. Product adapters select
that boundary; BAR/SC2 frozen paths remain independently qualified. Consumer adapters, native
journeys and .6–.7 remain open.


Stages .1–.3 source and .4 publication/installed qualification are closed. Stages .5–.7 remain open. Record source delivery, publication and
installed qualification separately, with exact versions, revisions and artifact digests.

| Stage | Deliverable | Dependencies | Completion evidence |
|---|---|---|---|
| **.1 — Contract and compatibility inventory — CLOSED (source)** | FS.GG.Game owns the unpublished `FS.GG.Wasm.Contracts` source; typed F# profiles preserve BAR ABI 1 and SC2 ABI `0x00010000`, with strict imported and inventory-only legacy SC2 admission paths, destructive BAR and transactional SC2 replacement, pinned source corpus and a source-archive SDK distribution choice | Protected BAR `9f0d8712e2c9a21cbf49f515a65b520516239b5a` and SC2 `57eadf00bb9e776664e00fe200bb4f2952f72829` inventories | [Game #672](https://github.com/FS-GG/FS.GG.Game/pull/672), protected source `3182cb1b678d9fa9da00edba263702a545e38bc0`, tree `8938409692792f7a90b4ba31e8d5bc96be1472af`; local .NET/Fable package-consumer proof and exact source readback. No host runtime, publication or adoption inferred |
| **.2 — Shared runtime and lifecycle — CLOSED (source)** | Unpublished `FS.GG.Wasm.Browser` production F# lifecycle/host, strict admission and invocation, thin JS mechanics, canonical Quint model and full-state/effect correspondence | Closed .1 contract | [Game #673](https://github.com/FS-GG/FS.GG.Game/pull/673), protected source `c951df2b02515cc44eefe6f1078923abd52b71be`, tree `e4453f9bf3565cf4f1321365efecb13fc2e56570`; hosted source/model/package/browser run `36991045744` and existing Game gates passed. No publication, installed host, product adoption or native authority inferred |
| **.3 — Guest SDK and fresh consumer — CLOSED (source)** | Versioned `0.1.0-source.3` Rust/C guest SDK source archive, package-owned Worker assets, independently compiled BAR/SC2 modules and a clean Fable consumer | Closed .1 contract and .2 host | [Game #674](https://github.com/FS-GG/FS.GG.Game/pull/674), protected source `e91db7efaf15dfb56b1925307ad0ceb46a3da31d`, tree `6a8c7e5c9dbcb309c71b612be78093df23c877a8`; hosted archive/package/fresh-consumer/browser run `36996692451` and existing Game gates passed. No publication, feed readback, installed qualification, product adoption or native authority inferred |
| **.4 — Publication and clean import — CLOSED** | Contracts/Browser `0.1.1` on GitHub Packages and nuget.org; SDK source archive under immutable `wasm/v0.1.1` | Closed .2 and .3; protected publisher source `ea015cbf884b01754bc6615476241450907b6b24` and qualifier source `3062be7e37c97959b9d522492ea1505f89a4c74e` | [Publisher `37012281052`](https://github.com/FS-GG/FS.GG.Game/actions/runs/37012281052) passed both feed and release-asset readbacks; genuine public fresh consumer passed locally and [org installed run `37017511208`](https://github.com/FS-GG/FS.GG.Game/actions/runs/37017511208) passed. Both locked consumers have SHA-256 `040afc28749bdf7545ce8d424e8cae602583790b06c8439f92a7f86b69fa4007`; .NET/Fable, extracted Rust/C examples and six Chromium Worker cases passed |
| **.5 — BAR and SC2 adoption** | Separate product adapter migrations | Closed .5-P connected 0.2.0 published/installed boundary; each product's baseline | Actual production paths consume the dependency; existing ABI, replacement, authority and unknown-effect behavior pass. Native acceptance remains separately reported |
| **.6 — FourD extension example** | Optional reaction-policy composition using the published host | .4; declared execution scope | Clean example imports the dependency, invokes a policy and handles trap/deadline/disposal through the shared API; no claim of completed FourD gameplay or deterministic replay without corresponding evidence |
| **.7 — Remove duplicate runtime policy** | Delete replaced product host mechanics and reconcile SDK/runtime guidance | Each consumer's accepted adoption | Active paths have one owner for shared execution policy; product codecs and authority adapters remain local |

After .1, lifecycle/model work, module admission/invocation work and SDK authoring can proceed in
separate touch-sets against the same descriptor. Product owners can prepare adapter inventories
and regression baselines concurrently. After .4, BAR, SC2 and the FourD example are independent
consumer lanes. A product incident blocks only consumers that require the missing capability.

### .1 protected source closure

[FS.GG.Game #672](https://github.com/FS-GG/FS.GG.Game/pull/672) merged the exact candidate
`1991d2972060587d8e51abfccbd300e3db53a06f` at protected
`3182cb1b678d9fa9da00edba263702a545e38bc0`; both resolve to tree
`8938409692792f7a90b4ba31e8d5bc96be1472af`. The delivered
[compatibility contract](https://github.com/FS-GG/FS.GG.Game/blob/3182cb1b678d9fa9da00edba263702a545e38bc0/docs/wasm/compatibility.md)
fixes the six i32 export signatures and the product differences: BAR ABI 1 versus SC2 ABI
`0x00010000`, strict BAR/imported-SC2 admission versus the weaker inventory-only legacy SC2 path,
BAR alignment and nonempty output versus SC2's existing worker-layer rules, and destructive versus
transactional replacement. Generic roles grant no product authority. Unsafe cleanup gaps are future
.2 repairs rather than compatibility requirements.

The local source gate passed 11 F# contract/negative controls, packed
`FS.GG.Wasm.Contracts.0.1.0-source.1`, inspected its `.fsi`, Fable sources and compatibility
metadata, and restored matching .NET/Fable consumers from that nupkg without sibling references.
The tested local nupkg had SHA-256
`41cc25ed8ef1ba0a51934654531ffc26fb9405e5f473d3be529c458a4a9ffa9a`; it is not published.
The pinned BAR/SC2 corpus contains attributed source hashes and expected decisions. Neither protected
source checkout contained a built `.wasm`, so no guest artifact or toolchain digest is invented.
Native Game run [`36979942169`](https://github.com/FS-GG/FS.GG.Game/actions/runs/36979942169),
routine eligibility run [`36979941395`](https://github.com/FS-GG/FS.GG.Game/actions/runs/36979941395)
and all protected branch contexts passed. The new project deliberately remains outside the Game
solution, and no hosted workflow ran its package verifier; this is exact source and local package
proof, not a hosted package gate, browser host, feed readback or installed consumer qualification.

### .2 protected source closure

[FS.GG.Game #673](https://github.com/FS-GG/FS.GG.Game/pull/673) merged qualified candidate
`4022ab59800492407258a2fbeb1e985248de9017` at protected
`c951df2b02515cc44eefe6f1078923abd52b71be`; both resolve to tree
`e4453f9bf3565cf4f1321365efecb13fc2e56570`. The production F# reducer owns active, candidate and
retiring generations, bounded ordinary/ordered/snapshot queues, deadlines, freeze, terminal delivery
and disposal. The host interprets data-only effects. Strict binary admission and validated span/
ownership mechanics preserve BAR ABI 1 and imported SC2 ABI `0x00010000`, including BAR destructive
load and SC2 transactional candidate validation. Legacy SC2 URL loading remains an inventory-only
compatibility path, and generic controller/advisor roles grant no product authority.

Quint 0.32.0 typechecked and ran exactly eight named lifecycle tests. Bounded simulation used seed
`20261002`, 2,000 samples and 40 steps; this is sampled evidence rather than exhaustive proof.
Model-generated ITF traces replayed full projected state and ordered effects through the production
reducer on .NET and Fable/Node, while the historical-to-current mutation was rejected. Lifecycle
controls passed 9/9. Strict UTF-8 and admission controls reject malformed continuation, overlong,
surrogate, out-of-range and incomplete encodings. A fresh package consumer compiled all 15 Fable
sources without sibling project references, and six Chromium Worker cases exercised real attributed
Rust BAR/SC2 modules plus memory growth, malformed/aliased ownership, traps, cleanup faults and a
terminable infinite guest.

The independent hosted WASM run
[`36991045744`](https://github.com/FS-GG/FS.GG.Game/actions/runs/36991045744) rebuilt the pinned modules
with Rust 1.90.0, ran the contracts, Quint/correspondence, package-consumer and Playwright 1.63.0
boundaries from cold package state, and passed. All existing Game gate checks also passed. Retained
local candidates are `FS.GG.Wasm.Contracts.0.1.0-source.2` with SHA-256
`dd8ffe037e1688bfb6fd049f1295f321756ded38221112ece093b4f20178b3c7` and
`FS.GG.Wasm.Browser.0.1.0-source.2` with SHA-256
`23658f8f234ce95df94f494550c42b307f666df3d3cdd1a190581b0da00be254`. They are unpublished,
outside the Game solution and existing release set, and do not establish a released or installed
browser host. Product source, native authority, stock Recoil and FourD scope remain unchanged.

### .3 protected source closure

[FS.GG.Game #674](https://github.com/FS-GG/FS.GG.Game/pull/674) merged qualified candidate
`eec59377d004b68d47bdebc1f2449065c090607a` at protected
`e91db7efaf15dfb56b1925307ad0ceb46a3da31d`; both resolve to tree
`6a8c7e5c9dbcb309c71b612be78093df23c877a8`. The deterministic guest SDK source archive has SHA-256
`3b67d3468ddef583a6348ec7eafe103ca886c23ae90cc5e0610333bd1671e5b9` and contains `no_std` Rust and
freestanding C helpers, provenance, checksums and build instructions. Two Rust and four C BAR/SC2
modules were built independently from the extracted archive with Rust 1.90.0 and pinned WASI SDK 34.0.

Exact-head local packages have SHA-256
`0991b76c5fd8883a9dd704a8b1ca6fb6ea06ef0d23a8e15599bea856be42964b` for Contracts and
`24551d598dadd87bd359edbda0e9cf918bfb28a5500c4d010163a0c6bd523584` for Browser. A second clean root
restored those packages without sibling references, built the .NET consumer with zero warnings and
errors, and compiled all 15 package sources with Fable 5.18.0. Playwright 1.63.0 Chromium passed six
cases through one installed package Worker below `/sub/app`, covering Rust/C BAR and SC2 calls, trap
containment, deadline termination and repeated disposal. Hosted run
[`36996692451`](https://github.com/FS-GG/FS.GG.Game/actions/runs/36996692451) completed successfully at
10:42:48 UTC, and all protected Game checks passed before the merge at 10:52:24 UTC.

The source archive and package files are unpublished local candidates. Stage .3 establishes source,
archive and fresh package-consumer qualification only. It does not establish feed publication or
readback, an installed shared host, product/native adoption, native authority, or BAR, SC2 and FourD
effects.

### .4 publication and installed qualification closure

[FS.GG.Game #677](https://github.com/FS-GG/FS.GG.Game/pull/677) protected publication source
`ea015cbf884b01754bc6615476241450907b6b24`, tree
`9526ed555e877080e79b7234b4ec0dac613991ec`, owns the coherent `0.1.1` set.
[Publisher `37012281052`](https://github.com/FS-GG/FS.GG.Game/actions/runs/37012281052)
completed successfully: retained custody preceded org upload/readback, public OIDC upload/readback,
and release-asset upload/readback. The [immutable release](https://github.com/FS-GG/FS.GG.Game/releases/tag/wasm/v0.1.1)
became public at 13:34:11 UTC on 2026-10-02. Game `0.16.0` and Skills `0.9.0` remain independent.

| Artifact | Retained original SHA-256 | Public served SHA-256 |
|---|---|---|
| Contracts `0.1.1` | `05003a606e3551cdd636c12b90d9fe9c29f7840e8ed8c6781847d1824b48c499` | `a675196b5e4f0caa295e8df0d10e2400aca8b9780b855fcb8dedb81444e017cc` |
| Browser `0.1.1` | `625ca033cfa2641f79c4780c23b0a926a07e907e7d169564d9c19a2ee00e3de0` | `bf550bc627a85adb4537a8f765299f7da0450bc647e459955df2886c7512769e` |
| SDK `fsgg-wasm-sdk-0.1.1.tar.gz` | `923173219374de6c2f5adc62c930042e18abc17125b0e32ba8d66c57266430d2` | Same exact archive |

Both feeds received the same retained package originals. Public repository signing changes the
served ZIP archive hashes; the publisher verified every non-signature member and rejected any
other path or byte change. The release manifest SHA-256 is
`975166b2cf53b17c2e5f55b1e020266f4cba78c9d2257d27a751f25deb58c2b5`.
Browser's compiled and Fable dependencies select Contracts at exact `[0.1.1]`.

[FS.GG.Game #678](https://github.com/FS-GG/FS.GG.Game/pull/678) protected qualification source
`3062be7e37c97959b9d522492ea1505f89a4c74e`, tree
`70177a549cb2081ee18cb907805bae1f6e9410b1`, fixes duplicate public-source mapping and adds
read-only org qualification. This qualifier is distinct from the immutable package source;
consumer fixtures and SDK tooling retain their publication-source provenance. The genuine public
feed-only consumer passed locally. Local org package access returned HTTP403, so the org result
comes from the actual successful [hosted installed run `37017511208`](https://github.com/FS-GG/FS.GG.Game/actions/runs/37017511208),
using its `packages: read` job token. Its retained qualification artifact digest is
`74a253848bc10222dd01f6f91dbbc3b3f498e00a1a25aa2bc7350d33025f32ad`.

Each feed consumer generated a lock in one empty NuGet package root and restored a second empty
root in locked mode. Both locks have SHA-256
`040afc28749bdf7545ce8d424e8cae602583790b06c8439f92a7f86b69fa4007`.
The .NET build passed with zero warnings/errors; Fable 5.18.0 compiled all 15 installed sources.
Rust/Cargo 1.90.0 and pinned WASI SDK 34.0 built two Rust and four C modules from the downloaded,
hash-verified SDK archive. Playwright 1.63.0 Chromium passed all six package Worker cases under
`/sub/app`: Rust/C BAR and SC2 calls, trap containment, deadline termination and repeated disposal.
Neither installed route packed producer sources, resolved sibling projects, used local feed
fallbacks or copied Worker files into the consumer.

The immutable `wasm/v0.1.0` tag remains failed history: publisher `37008933801` refused a dirty
checkout before packing or publication. Its corrected successor is `0.1.1`; no tag or package bytes
were relabeled. Stage .4 establishes published dependencies and fresh installed qualification.
BAR/SC2 production migration, FourD composition, native authority and duplicate removal remain
stages .5–.7.

## Qualification and rollback

Use independent guest modules to exercise malformed descriptors, integer/range overflow, overlap,
memory growth, forbidden imports, wrong signatures/ABI versions, oversized input/output, traps,
infinite loops and cleanup failure. Browser tests should also exercise delayed replies, wrong
request/generation identities, replacement failure, termination and repeated disposal. Verify that
stale results cause no product effect and that every admitted request receives at most one terminal
outcome. Load failure must not erase an active module unless that is the declared replacement policy.

Installed checks must use the package's real worker-loading route, including deployment base URLs,
bundler behavior and documented browser security requirements. Test supported browser/toolchain
versions explicitly. A source checkout or locally copied JavaScript file cannot establish clean
import qualification.

Retain a runnable pinned product baseline until its migration passes. Rollback restores dependency
pins and the previous entry without rewriting native state, saves or accepted evidence. Remove
duplicate mechanics only after the corresponding product accepts the shared route.

## Definition of ready to import

The initial foundation is ready to import: .1–.4 passed with published versioned artifacts, packaged
Worker assets, documented compatibility profiles, independently built guest examples and fresh
installed .NET/Fable consumers from both package feeds. BAR/SC2 adoption and the FourD example remain separately visible until .5–.6 pass.
Complete extraction requires those consumer outcomes and .7 duplicate removal.

The next implementation actions are .5 BAR/SC2 adapter migrations and the independent .6 FourD
example. Preserve each product baseline and qualify its actual production path before .7 removes
duplicate mechanics. Published and installed qualification does not establish product/native
adoption or native authority.

## Consumer-adoption horizon — 2026-10-03

This extends WASM-SHARED-01 stages .5–.6. Stages .1–.4 and connected
prerequisite .5-P retain their recorded closure. The selected dependency is
the coherent Contracts/Browser **0.2.0** release, produced at
`4afacb501b9371b4cc81494b7bf91b46c880a663` and installed-qualified at
`7397d4b408b8e7b44393fee9f837278723934867`. The initial horizon assumed no
additional producer release; the SC2 narrowing amendment below records the
subsequently discovered prerequisite.

The `.github` programme integrator owns this plan and shared-roadmap edits.
Each product has one accountable adapter owner. BAR native-capacity and SC2
native-replay owners retain their existing qualification paths and artifacts.
All windows use the routine route. Preparation and integration micro-steps
may accumulate on the same local branch; the programme integrator admits
one coherent delivery PR per consumer chain.

### Current evidence and decisions

Protected consumer inspection used BAR `4e3ee2d4b77ab3f8875fcfb21cee20a374630707`,
SC2 `ceb7a43d9e88a036982280cff2ab4fe912d8d25d`, and FourD
`60f2a41eeaf22325b6af7a264791c79644c6fb6d`. Re-read the current FS-GG default
before implementation; BAR's local origin can point to an obsolete fork.

BAR's preview and live runtimes instantiate `GuestSupervisor`; its Worker
also validates product response identity through `barc-wire.js`. Shared
execution therefore requires retaining that validation in the product
adapter and retiring the host after rejected output. BAR receiver packing,
adoption and qualification explicitly name the old Worker and dependencies.
A development-browser pass alone cannot establish receiver adoption.

SC2's supervisor exposes offline, URL-loaded, imported, realtime,
replacement, reset and shutdown routes. It also supplies `returnCode`,
`abiInvoked`, `abiResult` and `advisorObservation` consumed by recording and
advisor disposition. Preserve these meanings from actual dispatch and
terminal outcomes. Strict imported admission is reusable; legacy URL
admission is inventory-only in the producer and cannot be enabled there.
Each shipped URL-loaded module must independently pass strict admission
before its entry moves. SC2 currently uses Fable 5.13.0; producer installed
qualification used 5.18.0. Prove the product compiler or make the minimum
necessary toolchain change at the integration join.

FourD has no implemented uploaded-policy host. The first example is an
explicit optional, non-authoritative sandbox. It may reuse the published
strict `sc2c_*` ABI transport with a separately named FourD example wire
format; this supplies neither SC2 authority nor a new shared ABI profile.
Its output recommends Guard, Ambush or Hold for a caller-supplied visible
observation. It does not execute a game action or enter saved/replayed state.
Authoritative uploaded reactions remain a separate deterministic-execution
decision in the existing FourD design.

All consumers restore exact published package versions into clean locked
caches and deploy the complete package-owned Worker asset tree, including
`policy/`. Use `Host.CreateConnected`; product adapters translate product
identities and validate product outputs. Do not reproduce the shared queue,
deadline, memory or replacement reducer in JavaScript.

### Ready and joined windows

- [x] **WASM-SHARED-01.5-B1 — BAR adapter and compatibility qualification**
  — route: routine.
  Depends on: Closed .5-P; protected BAR baseline.
  Scope: new `src/Broker.Browser.SharedWasm/` and
  `tests/Broker.Browser.SharedWasm.Tests/`, containing an isolated Fable
  adapter project, exact package locks, mechanical JS facade and focused
  package-Worker tests. Consume existing BAR wire validation without
  changing native-capacity, gateway, stock-engine or receiver files.
  Acceptance: the adapter supports the existing load, initialize, process,
  shutdown, disarm, dispose, active and generation contract. Independently
  built manual/custom guests pass through the real installed Worker.
  Destructive replacement, busy refusal, 250 ms phase limits, ABI 1,
  nonempty aligned output, malformed/foreign response refusal and no late
  effect pass. Product response rejection retires the shared host. Trap,
  cleanup fault, deadline and repeated disposal settle once. Serve the
  complete installed assets below a non-root application URL.
  Stop: qualified adapter prepared locally; production receiver adoption
  awaits .5-B2. A producer incompatibility is a bounded producer defect,
  not permission to weaken the BAR profile.

- [ ] **WASM-SHARED-01.5-B2 — BAR production and receiver adoption**
  — route: routine.
  Depends on: .5-B1 and a join with the BAR native-capacity owner.
  Scope: integrate the adapter into `src/Broker.Browser.Client/` and the
  `src/Broker.Browser.Wasm/` facade. Update the existing package/adopt/
  qualify scripts and receiver tests to bind the complete installed Worker
  dependency closure. Preserve archive path and integrity safeguards
  through an explicit compatible extension or versioned successor.
  Acceptance: preview and live product entrypoints consume the shared
  host; the packaged receiver runs without a producer checkout or local
  feed. Missing or changed package assets refuse qualification. Existing
  browser, wire, authority and unknown-effect guards pass against the
  receiver. The native owner qualifies the joined distribution at its
  applicable native acceptance boundary and retains exact artifact and
  source identities. Existing useful-play results are not relabeled.
  Stop: source, receiver qualification and native acceptance are reported
  separately. No candidate browser change silently replaces the native
  owner's retained or currently admitted distribution.

- [ ] **WASM-SHARED-01.5-S1 — SC2 adapter and compatibility qualification**
  — route: routine.
  Depends on: Closed .5-P; protected SC2 baseline; .5-P2 below for request-limit
  qualification. Adapter preparation and supported-profile checks remain ready.
  Scope: new `src/SC2.Client.SharedWasm/` and
  `tests/SC2.Client.SharedWasm.Tests/`, with isolated package configuration,
  Fable adapter, mechanical facade and browser tests. Read existing
  contracts and supervisor behavior; leave existing browser entry,
  recording, native qualification and fixture-supervisor files to .5-S2.
  Acceptance: cover the complete current `SC2GuestSupervisor` API through
  installed package Workers. Preserve one controller, two advisors and
  one aggregate candidate, bounded ordinary/realtime queues, snapshot
  coalescing, configured input/output/deadline limits and transactional
  replacement. A refused candidate leaves the active module usable;
  unresolved replacement freezes delivery until the existing recovery
  decision. Old generations and advisor output gain no command authority.
  Returned ABI rejection codes, actual dispatch, queued refusal and
  historical-only output retain distinct recording/disposition meanings.
  Independently inventory and build each URL-loaded module and prove
  strict admission. Test non-root Worker loading, faults and disposal.
  Stop: qualified adapter prepared locally; existing production and
  native-replay paths remain owned by the active qualification lane.

- [ ] **WASM-SHARED-01.5-S2 — SC2 production, recording and native join**
  — route: routine.
  Depends on: .5-S1 and the SC2 native-replay owner's join.
  Scope: `fixtures/guest-supervisor.js`, browser package/project/lock and
  asset-build configuration, browser entry loading, and affected product
  tests. The native-replay owner integrates changes needed in recording,
  replay executors, qualification drivers and acceptance evidence.
  Acceptance: the actual imported-controller/advisor, builtin live,
  realtime and offline routes use the shared runtime. Existing tactical,
  production, realtime, replacement, import and recording tests pass.
  Recordings preserve exact module/configuration identity, invocation
  order, output and actual ABI return status. Offline comparison remains
  authority-free. Native joined qualification demonstrates the affected
  product journey and post-write-loss behavior: one durable Unknown,
  no automatic command replay, revoked authority and explicit fresh rearm.
  Preserve the separate owning SC2C-01.6f completion contract.
  Stop: missing strict compatibility or a lost recording fact blocks only
  the affected migration. Source/browser evidence does not close native
  replay, product publication or installed-platform qualification.

- [x] **WASM-SHARED-01.6 — FourD optional reaction-policy example**
  — route: routine.
  Depends on: Closed .5-P; no BAR/SC2 or rendering-pilot prerequisite.
  Scope: isolated `examples/reaction-policy/` with its project, locks,
  independently built SDK guest, example page, build/qualification script
  and tests. Keep the current gameplay core, product entry, rules identity,
  saves and replay formats outside this window.
  Acceptance: a clean public-feed consumer builds and serves the actual
  installed package Worker under a non-root URL; it supplies only the
  declared visible observation and validates one bounded recommendation.
  Demonstrate normal output, malformed output, trap, deadline termination,
  stale result refusal, replacement and repeated disposal. Invalid output
  produces a displayed refusal/fallback and zero game effects. Pin the
  downloaded SDK archive and actual guest/toolchain identities.
  The example states that elapsed-time termination supplies no
  deterministic fuel or authoritative replay guarantee.
  Stop: example source and clean installed-consumer qualification complete;
  hosted example publication and authoritative gameplay remain separate.

### Concurrency and joins

.5-B1, .5-S1 and .6 are independent immediately executable windows in
separate worktrees. Their new adapter/example directories do not overlap
the active native workers. The programme integrator confirms those exact
touch-sets before dispatch and reserves shared build/package files for
the relevant integration window.

The BAR native owner and adapter owner join at the new receiver asset
closure and native packet. The SC2 native-replay owner and adapter owner
join at the production supervisor, recording fields and served bundle.
An ongoing frozen native attempt can finish on its retained bytes while
adapter preparation continues. Changed product bytes require their own
applicable qualification; prior native evidence is not transferred merely
because the public API looks unchanged.

### Remaining outcome and workspace impact

Stage .7 remains outcome outline work until each consumer has accepted its
replacement. Then remove inactive duplicated worker/admission/supervisor
policy and align local guest SDK guidance with the published SDK. Preserve
product codecs, grants, native receipts and recording adapters. Remove
each product's duplicates after that product's acceptance; the other
consumer need not delay an independently valid cleanup.

These windows change no SDD/provider/lifecycle family or general template
default. .5-B2 and .5-S2 first change enabled product source behavior;
.6 adds only an explicitly selected example. The shared producer is
already published as 0.2.0. Product release/receiver adoption and any later
`fs-gg-fable-game` template composition retain their own publication and
installed qualification boundaries.

Clean qualification creates a fresh locked consumer and deploys the whole
package Worker closure without sibling sources or a local feed. Existing
products upgrade explicitly through their adapter and distribution pins;
retain the prior runnable distribution for rollback without rewriting
native state, saves or accepted evidence. No workspace effect follows
from a source merge alone.

Use native builds, browser results, product qualification and release
readbacks as observation sources. The current roadmap telemetry begin
returned `not-configured`; usage attribution and bureaucracy percentages
remain unknown. Preserve that advisory gap and the existing section 7.4
definitions without inventing measurements.

### SC2 request-limit amendment — WASM-SHARED-01.5-P2

The .5-S1 inspection invalidated the assumption that published 0.2.0 can
preserve every SC2 invocation limit. This is a bounded producer extension
within WASM-SHARED-01, not a new feature or a reopening of historical .5-P
acceptance. BAR .5-B1 and the declared FourD .6 example continue on 0.2.0.
Only unsupported SC2 limit-dependent qualification and adoption are fenced.

At producer `7397d4b`, `Lifecycle.request` derives each deadline from the
loaded worker's immutable `ValidatedConfiguration`; `WorkerEntry` requires
the command configuration to equal that loaded configuration. SC2 learns
SC6G limits after load, before initialization, and selects limits again for
ordinary/realtime processing, reset and shutdown. A host loaded at 250 ms /
65536 bytes cannot currently honor an initialization at 10 ms / 44 bytes:
45-byte output can succeed, and termination remains governed by 250 ms.
A later 5 ms process call has the same gap. Rewriting mechanical Worker
commands leaves the F# timer wrong and violates Worker configuration
equality. Reloading loses guest state or changes generation. Refusing these
otherwise valid calls is containment, not completed compatibility.

#### Selected additive contract

Keep the loaded configuration, guest instance, generation and existing
public APIs unchanged. Add a typed per-request limit record containing
positive integer `MaximumDeadlineMilliseconds` and `MaximumOutputBytes`,
plus a closed request type for load, candidate preparation, initialize,
invoke (including submission class) and shutdown. Expose one additive
`Host.SubmitLimited(now, request, limits)` entry and corresponding pure
`Lifecycle` entry. Proposed names may follow repository conventions; keep
the contract additive rather than extending existing public record
constructors or changing existing DU cases. Return typed validation errors
for requests that cannot be admitted. Existing methods retain 0.2.0 defaults.

- Validate both limits against the exact active/candidate generation's
  loaded ceiling, or the supplied validated configuration for load/prepare:
  `1 <= requested <= loaded maximum`. Reject zero, negative, overflow,
  malformed Fable/JS numbers and increased ceilings before posting guest
  work. Invalid admission returns a typed error without changing guest
  state or settling an unrelated request. Existing identity, freeze,
  generation, duplicate and capacity guards still apply.
- Capture effective limits once at admission in internal pending state.
  SC2's deadline is the checked sum of the reducer's normalized monotonic
  enqueue time and requested milliseconds; queueing and phase changes
  cannot extend it. BAR phase watchdogs reset using that request's selected
  budget. Preserve the canonical before/at/after expiry boundary and clock
  clamping established by the owning qualification work; do not change
  expiry inclusivity as part of this extension.
- Keep the original load configuration as the Worker admission ceiling.
  For initialize/process, derive an effective command configuration by
  narrowing only deadline/output fields. Worker validation permits that
  downward relation while requiring identical artifact/configuration
  digests, profile, policies and all other resource fields, plus exact
  worker/generation identity. It never replaces its loaded ceiling with
  one request's narrowed values. Load/shutdown have no output payload;
  their explicit deadlines still apply without mutating the loaded ceiling.
- Host timer policy and Worker output checks consume the same captured
  limits. A later request may use larger limits than an earlier request
  within the immutable loaded ceiling. No pending, retiring or candidate
  request inherits another request's budget. Reuse existing output-fault,
  cleanup, disposal and single-settlement behavior.
- The SC2 adapter retains SC6G decoding, successful-init session state and
  candidate promotion. Initialization selects the existing minimum of
  explicit and SC6G limits. Processing selects the minimum timeout, while
  its current output selection is explicit override or stored cap; do not
  silently replace that selection with a new global minimum rule. Realtime,
  reset, candidate and shutdown routes preserve their actual existing
  selections. Generic producer code does not parse SC6G or own native grants.

#### Producer window and consumer join

- [ ] **WASM-SHARED-01.5-P2 — Published request limits and SC2 compatibility policy**
  — route: routine.
  Source owner: FS.GG.Game; same original WASM-SHARED-01 part and cost lineage.
  First source touch-set: `src/Wasm.Browser/{RuntimeProtocol,Host,Lifecycle,WorkerEntry}.{fs,fsi}`
  as needed, and new focused request-limit test/fixture files. Keep existing
  public constructors intact. Extend canonical `eng/wasm-shared/lifecycle.qnt`
  and its actual .NET/Fable correspondence with captured deadline/output
  limits and downward-only command validation. Expose additional projection
  through additive APIs where needed, not changed record constructors.
  Root currently owns Game PR #681's qualifier failure and overlapping model,
  correspondence, verifier and runtime-documentation files. Prepare disjoint
  runtime/tests locally; join those shared files only through that owner
  after its source is stable. Do not duplicate the qualifier repair or open
  another PR on its dependency chain without root admission.
  Acceptance: stateful guests retain the same instance/generation across
  load, narrowed initialization and subsequent differently bounded calls.
  A 44-byte cap rejects 45 bytes; a 10 ms initialization and 5 ms invocation
  arm those exact F# deadlines. Queued ordinary/ordered/snapshot work retains
  its own admission budget; phases, replacement and late callbacks cannot
  extend or transplant it. Cover candidate isolation, rejected increases,
  invalid numeric inputs, stale identities, exact deadline boundaries and
  repeated disposal. Model-generated traces replay full relevant state and
  ordered effects on .NET and Fable. Mutants that keep the loaded deadline,
  ignore output narrowing or overwrite the next request's ceiling fail.
  Browser tests use actual package Workers and demonstrate termination and
  output rejection; wall-clock tests do not claim exact scheduler latency.
  Old 0.2.0 API consumer compilation and native ApiCompat must pass without
  suppressions, alongside existing profile and connected-host checks.

  Publish the coherent Contracts/Browser/SDK successor through existing
  release tooling, then repeat fresh locked org/public installed acceptance
  with the new per-request cases and existing cases. Reserve **0.2.1** only
  if unused and the addition passes actual compatibility checks; any required
  breaking constructor change instead requires explicit **0.3.0** migration
  and plan reconciliation. Source version is not a publication claim. Root
  integrates `eng/wasm-shared/version.props`, publication/qualification pins,
  release docs and shared-registry adoption after the model/qualifier join.
  Record source delivery, publication and installed qualification separately.

After .5-P2's published installed boundary, the existing .5-S1 owner pins
the successor and exercises its full adapter matrix, including each SC6G,
per-call and replacement limit selection. .5-S2 then joins the native-replay
owner as already planned. Local candidate-package tests can prepare this
join but cannot close installed compatibility. Existing .5-S1 requirements,
native acceptance, publication and .7 removal boundaries remain intact.
No scaffold or lifecycle default changes. Telemetry remains not-configured;
usage and bureaucracy attribution are unknown.

#### Final .5-P2 semantic inventory and producer packet — 2026-10-03

This completes the bounded producer packet above before implementation;
the request-limit contract and its acceptance remain required. Keep one
.5-P2 item, source owner, coherent successor and publication/installed join.
The evidence is the SC2 adapter audit at local candidate
`6f85d19dabc5f3a2155148a10e341a0f1d2ed299`,
`docs/roadmaps/shared-wasm-adapter.md`: all **17** exported supervisor methods,
six independently rebuilt URL modules admitted by installed 0.2.0, and seven
real installed-Worker tests under `/sc2/qualification/`. Its explicit
counterexample assertions demonstrate gaps, not product acceptance. The
existing product compiler Fable 5.13.0 passed installed compilation.

An active call 6 followed by ordered 8 and ordinary 9 actually dispatches
`[6,9,8]` with the default package policy; SC2's mixed FIFO requires
`[6,8,9]`. A frozen host's synchronous Resume/Commit/Freeze sequence actually
posts retained old-generation work and returns its dispatched historical
outcome. Neither JavaScript queue emulation nor that resume workaround is
an admissible replacement for the required shared reducer behavior.

Select one additive, immutable typed compatibility option, provisionally
`Sc2SupervisorV1`, through a new `HostSettings` factory. Validate it only
with `Sc2ImportedStrict`; do not enable legacy module admission. Existing
`HostSettings.create`, APIs, projections and default lifecycle behavior
retain their 0.2.0 semantics. A new read-only projection may expose the
selected policy and additional state without changing public constructors.
The option owns the following connected behavior in shared F#:

| Boundary | Required SC2-compatible behavior and disposition |
| --- | --- |
| Mixed queue | One admission-ordered FIFO across ordinary, ordered and admitted snapshot requests. Class labels select admission limits, not dispatch priority. |
| Ordinary admission | At most four queued requests across all classes, excluding the currently executing request. Reject when the existing mixed queue already has four entries. Enforce the selected request's input ceiling. |
| Ordered admission | At most 256 requests including the executing request and all classes in the mixed queue; at most 4 MiB combined input bytes for that same population including the new input; individual input at most 256 KiB or a smaller validated ceiling. Equality at the byte ceiling is allowed. Ordinary entries cannot evade ordered admission accounting. |
| Snapshot lane | At most one snapshot already admitted to the mixed queue/executing, plus one latest held snapshot outside that queue. A new held snapshot replaces and settles the previous held one undispatched. The held snapshot joins the FIFO tail only after its predecessor settles and the existing FIFO pump advances; it then undergoes ordinary admission and starts its enqueue deadline. Its selected relative budget/input are retained while held, but it is not silently charged as an already-enqueued ordered request. Its separately bounded storage is included in the model. Oversized snapshot input refuses without destroying the session. |
| Queue overflow | Shared F# decides admission. The product F# result adapter maps the actual destructive queue-overflow refusal to `Dispose` before exposing completion, retiring active/candidate workers and settling queued work once. It distinguishes non-destructive oversized-snapshot refusal. No JS counters or scheduler duplicate producer policy. |
| Queued expiry | When an entry reaches the FIFO head for dispatch, an exhausted budget (`now >= due`) settles that entry timed-out and undispatched, retires the whole session including any candidate, and settles remaining queued/held work as undispatched destruction. Do not silently replace this with individual proactive queue expiry or dispatch the next entry. Existing default-profile expiry boundaries stay unchanged; the selected head check has explicit before/at/after vectors. |
| Freeze | Atomically retain active and initialized candidate state, record the recovery token, and invalidate every queued or held request with zero guest dispatch. An already executing call can finish only as historical evidence while frozen. Admissions remain fenced. |
| Commit under freeze | Add a token-bound atomic commit entry and pure reducer transition. Require the current recovery token, transaction, expected active generation, candidate generation, and initialized/validated candidate. Promote once while preserving the same freeze token; never emit a resume, old queued post or new current-authority delivery. Wrong/stale identities leave state unchanged. |
| Recovery completion | Product F# validates the existing transaction/hash/generation receipt, then resumes the same token. Aborting a candidate retains an unresolved recovery fence. Existing default `CommitCandidate` remains unable to commit while frozen. |
| Remaining exports | Product F# retains handles, actual role/aggregate capacity, configuration and boundary decoding, successful-init caps, closing/reset/shutdown state, result projection, recording facts and native grants. Mechanical JS performs fetch/Worker/Promise work only. Strict admission, memory ownership, actual dispatch, return status, trap/deadline containment and disposal stay producer-owned. |

The complete-export acceptance must also preserve composite budgets:
`run` currently has one end-to-end budget covering fetch/load, initialize,
process and shutdown; URL loads include their fetch. A chain of freshly
reset per-phase 250 ms calls is not equivalent. The new typed limit contract
may carry an optional absolute enclosing deadline, or an equivalent typed
budget token, captured once by the product F# composition and bounded by
each request's selected limit in the shared reducer. Expired enclosing
budgets admit no further guest work. Fetch cancellation is mechanical and
cannot restart that budget. This adds no second product queue/timer policy.

Extend the earlier request-limit model work in the same canonical model
with selected compatibility policy, FIFO sequence, full cross-class count/
byte accounting, admitted/held snapshot state, selected queue-expiry action,
freeze token and atomic candidate promotion. Preserve old-policy traces.
Replay generated traces against production .NET and Fable decisions and
ordered effects, including package-connected callbacks. New mutations must
fail for ordinary priority over earlier ordered work, ordinary-byte omission,
incorrect snapshot promotion/deadline start, individual-only queued expiry,
retained frozen queues, commit requiring temporary resume, wrong-token
promotion and clearing the recovery token during commit. Keep all previously
specified deadline/output-narrowing mutations and memory/cleanup controls.

Actual installed acceptance adds: FIFO `[6,8,9]`; mixed ordinary admission
at four; ordered admission at 256 and the exact 4 MiB boundary including
ordinary/active inputs; one admitted plus one latest snapshot; head expiry
destroying the complete session with no following dispatch; freeze draining
queued/held work before active historical completion; matching-token commit
retaining the fence and rejecting old generations; and composite-budget
exhaustion. Exercise both active-present and already-completed freeze cases,
candidate abort and explicit recovery resume. Each request settles once;
ABI invocation/return codes and historical bytes remain distinct from
product effect eligibility. Those browser tests use real independently
built modules, not only mock transports.

BAR inspection at protected `4e3ee2d` confirms `maximumTableElements` is an
old constructor option, but both actual product callers use the 4096 default
(the live caller additionally selects its ordinary 250 ms timeout). Thus
no producer table-ceiling API is required for this bounded product-adoption
window. The adapter's explicit refusal of a custom 2048 ceiling is an open
nondefault compatibility limitation, not full constructor parity; preserve
that limitation until an independently selected validated ceiling extension
has admission tests. Do not widen the producer's 4096 maximum or emulate
table admission in JS. Successful shared shutdown physically retires the
Worker, so BAR `active=false` is the accepted B2 migration behavior; the
old non-null handle did not mean a usable guest. B1 and FourD continue on
their declared 0.2.0 profiles.

One Game owner implements this combined .5-P2 after its existing #681
qualifier repair. The initial runtime touch-set remains
`src/Wasm.Browser/{RuntimeProtocol,Host,Lifecycle,WorkerEntry}.{fs,fsi}` and
focused limit/compatibility fixtures. The same owner joins canonical
`eng/wasm-shared/lifecycle.qnt`, its boundary/expiry qualification modules,
`tests/Wasm.Lifecycle.Tests/`, `tests/Wasm.Lifecycle.Correspondence/`,
`tests/Wasm.PackageConsumer/`, and the existing verify/release qualification
scripts after #681 is stable. Product code and shared registry edits stay
with their existing owners. Use root admission for one coherent delivery;
do not launch separate planning or PR chains for each counterexample.

Keep the preceding compatible-version guidance: reserve unused 0.2.1 only
after additive native API/source compatibility is demonstrated; otherwise
reconcile an explicit 0.3.0 migration. Run the existing coherent qualification
plus both default-policy regression and selected-policy acceptance, publish
one coherent set, and prove fresh locked public/org installed consumers
using exact released Worker/model/qualifier identities. A failed #681 check
is repaired by its owner and is never hidden by the new source work. Only
then can S1 qualify the complete adapter and S2 perform its product/native
join. Source completion, published capability, installed acceptance and
native adoption remain separate. No new workspace default or telemetry
claim follows from this amendment.

## Consumer source readback — 2026-10-03

.5-B1 is Closed through [BAR #26](https://github.com/FS-GG/FSBarV2/pull/26),
protected `8da133be0f41c15fdb78291d428c559c56a9afc2`. The merged tree preserves
the earlier official-package repair; exact adapter/test/owning-plan byte equality
with the reviewed candidate and 37 installed-Worker cases establish the bounded adapter window. .5-B2
production/receiver/native adoption remains open.

.6 is Closed through [FourD #33](https://github.com/FS-GG/FS.GG.FourD/pull/33),
protected `af32cea8864bcc4c970fb8457ef2cde88011e829`, tree
`cddcea669b2b78f50a96de35bd22b445096cbe2a`; independent root tree readback and
seven clean installed-browser cases establish the optional example. Its owning
[plan](https://github.com/FS-GG/FS.GG.FourD/blob/main/docs/roadmaps/shared-wasm-reaction-example.md)
retains hosting and authoritative gameplay outside this completed window.

Game's canonical event qualifier repair is source Closed through
[#681](https://github.com/FS-GG/FS.GG.Game/pull/681), protected
`0b8a3217c94e54bcb5667df48a42dee4398e21c9`, tree
`336c8113540dae0d295c247d0dcb2bc0f4a82ba6`, equal the approved candidate.
Exact-head shared qualification `37099061357` passed with unchanged public 0.2.0
inputs. The completed final .5-P2 packet above is the next producer window.
