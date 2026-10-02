# Shared WASM execution foundation

**WASM-SHARED-01 — extraction plan, 2026-10-02. Implementation and publication remain open.**

Extract the reusable browser WASM host and guest authoring primitives from BAR and SC2 into
versioned dependencies. Qualify a fresh importing consumer, migrate the two existing hosts, and
provide a FourD reaction-policy example. Future products should consume the same foundation.
This product infrastructure track adds no gate to V2 platform acceptance.

The [unified V2 roadmap](../2026-09-07-154210-fs-gg-unified-development-roadmap.md#98-feature-parts-and-subroadmap-index)
indexes this part as WASM-SHARED-01. Its
[workspace boundary](../2026-09-07-154210-fs-gg-unified-development-roadmap.md#993-shared-wasm-execution-foundation)
separates source extraction, publication and installed adoption. The `.github` roadmap owner
coordinates .1 and selects the producer owner before implementation.

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

## Proposed dependency boundary

The package names below are proposals, not existing or published artifacts. Select the producer
repository during contract review; one owner should own shared execution policy and its releases.

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

Generic invocation transports opaque bytes. Controller and advisor roles have explicit permissions;
an advisor result cannot enter a command route by changing a browser envelope. Session authority,
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

All stages below are open. Record source delivery, publication and installed qualification
separately, with exact versions, revisions and artifact digests.

| Stage | Deliverable | Dependencies | Completion evidence |
|---|---|---|---|
| **.1 — Contract and compatibility inventory** | Producer ownership, F# descriptors, existing ABI/profile matrix, packaging choice and baseline tests | Refreshed BAR/SC2 protected source | Both adapters can be expressed without lost validation or changed authority; supported resource guarantees and SDK distribution are explicit |
| **.2 — Shared runtime and lifecycle** | Shared browser host, thin JS boundary, Quint lifecycle model and F# trace replay | .1 contract | Production-path tests cover admission, invocation, faults, cleanup, replacement and disposal; model traces agree with the reducer |
| **.3 — Guest SDK and fresh consumer** | Shared author helpers, independently compiled modules and a minimal Fable consumer | .1 contract; .2 candidate host | Consumer builds and runs with packaged worker/native JS assets; modules built independently exercise the documented ABI |
| **.4 — Publication and clean import** | Versioned host/contracts and SDK artifacts in the selected distribution channels | .2 and .3 candidate qualification | Fresh environment imports released dependencies without copied worker files, sibling source checkouts or repository-relative paths; package bytes and digests are read back |
| **.5 — BAR and SC2 adoption** | Separate product adapter migrations | .4 published boundary; each product's baseline | Actual production paths consume the dependency; existing ABI, replacement, authority and unknown-effect behavior pass. Native acceptance remains separately reported |
| **.6 — FourD extension example** | Optional reaction-policy composition using the published host | .4; declared execution scope | Clean example imports the dependency, invokes a policy and handles trap/deadline/disposal through the shared API; no claim of completed FourD gameplay or deterministic replay without corresponding evidence |
| **.7 — Remove duplicate runtime policy** | Delete replaced product host mechanics and reconcile SDK/runtime guidance | Each consumer's accepted adoption | Active paths have one owner for shared execution policy; product codecs and authority adapters remain local |

After .1, lifecycle/model work, module admission/invocation work and SDK authoring can proceed in
separate touch-sets against the same descriptor. Product owners can prepare adapter inventories
and regression baselines concurrently. After .4, BAR, SC2 and the FourD example are independent
consumer lanes. A product incident blocks only consumers that require the missing capability.

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

The initial foundation is ready when .1–.4 pass: published versioned artifacts, packaged worker
assets, documented compatibility profiles, independently built guest examples and a clean Fable
consumer. BAR/SC2 adoption and the FourD example remain separately visible until .5–.6 pass.
Complete extraction requires those consumer outcomes and .7 duplicate removal.

The next implementation action is .1: select the producer owner and inventory the current
supervisors, ABI profiles and package conventions. No shared package, migration, release or
installed qualification is delivered by this planning document.
