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
published version `0.1.1` from FS.GG.Game on GitHub Packages and nuget.org. FS.GG.Game owns shared execution policy and releases; the guest SDK ships as a versioned source archive.

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

### Connected producer prerequisite to .5 — source closed, release open

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
Before .5 adapters, qualify protected-source publisher custody, both genuine feeds and SDK
assets, then fresh public and organization installed consumers. None is inferred from the
local candidate or source merge. BAR/SC2 existing frozen paths remain independently qualified;
consumer adapters, native journeys and .6–.7 remain open.


Stages .1–.3 source and .4 publication/installed qualification are closed. Stages .5–.7 remain open. Record source delivery, publication and
installed qualification separately, with exact versions, revisions and artifact digests.

| Stage | Deliverable | Dependencies | Completion evidence |
|---|---|---|---|
| **.1 — Contract and compatibility inventory — CLOSED (source)** | FS.GG.Game owns the unpublished `FS.GG.Wasm.Contracts` source; typed F# profiles preserve BAR ABI 1 and SC2 ABI `0x00010000`, with strict imported and inventory-only legacy SC2 admission paths, destructive BAR and transactional SC2 replacement, pinned source corpus and a source-archive SDK distribution choice | Protected BAR `9f0d8712e2c9a21cbf49f515a65b520516239b5a` and SC2 `57eadf00bb9e776664e00fe200bb4f2952f72829` inventories | [Game #672](https://github.com/FS-GG/FS.GG.Game/pull/672), protected source `3182cb1b678d9fa9da00edba263702a545e38bc0`, tree `8938409692792f7a90b4ba31e8d5bc96be1472af`; local .NET/Fable package-consumer proof and exact source readback. No host runtime, publication or adoption inferred |
| **.2 — Shared runtime and lifecycle — CLOSED (source)** | Unpublished `FS.GG.Wasm.Browser` production F# lifecycle/host, strict admission and invocation, thin JS mechanics, canonical Quint model and full-state/effect correspondence | Closed .1 contract | [Game #673](https://github.com/FS-GG/FS.GG.Game/pull/673), protected source `c951df2b02515cc44eefe6f1078923abd52b71be`, tree `e4453f9bf3565cf4f1321365efecb13fc2e56570`; hosted source/model/package/browser run `36991045744` and existing Game gates passed. No publication, installed host, product adoption or native authority inferred |
| **.3 — Guest SDK and fresh consumer — CLOSED (source)** | Versioned `0.1.0-source.3` Rust/C guest SDK source archive, package-owned Worker assets, independently compiled BAR/SC2 modules and a clean Fable consumer | Closed .1 contract and .2 host | [Game #674](https://github.com/FS-GG/FS.GG.Game/pull/674), protected source `e91db7efaf15dfb56b1925307ad0ceb46a3da31d`, tree `6a8c7e5c9dbcb309c71b612be78093df23c877a8`; hosted archive/package/fresh-consumer/browser run `36996692451` and existing Game gates passed. No publication, feed readback, installed qualification, product adoption or native authority inferred |
| **.4 — Publication and clean import — CLOSED** | Contracts/Browser `0.1.1` on GitHub Packages and nuget.org; SDK source archive under immutable `wasm/v0.1.1` | Closed .2 and .3; protected publisher source `ea015cbf884b01754bc6615476241450907b6b24` and qualifier source `3062be7e37c97959b9d522492ea1505f89a4c74e` | [Publisher `37012281052`](https://github.com/FS-GG/FS.GG.Game/actions/runs/37012281052) passed both feed and release-asset readbacks; genuine public fresh consumer passed locally and [org installed run `37017511208`](https://github.com/FS-GG/FS.GG.Game/actions/runs/37017511208) passed. Both locked consumers have SHA-256 `040afc28749bdf7545ce8d424e8cae602583790b06c8439f92a7f86b69fa4007`; .NET/Fable, extracted Rust/C examples and six Chromium Worker cases passed |
| **.5 — BAR and SC2 adoption** | Separate product adapter migrations | .4 published boundary; each product's baseline | Actual production paths consume the dependency; existing ABI, replacement, authority and unknown-effect behavior pass. Native acceptance remains separately reported |
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
