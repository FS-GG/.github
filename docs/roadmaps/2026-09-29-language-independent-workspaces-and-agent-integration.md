# Language-independent V2 workspaces and agent integration

Product workspaces choose their own languages and toolchains, including multiple languages in one
repository. Keep the existing coordination core and expose portable integration contracts. Evaluate
AG-UI at the presentation boundary and Microsoft Agent Framework inside bounded agent attempts.

**Status:** amendment selected on 2026-09-29; .3 read-only AG-UI source qualification and the .4
bounded Agent Framework evaluation closed on 2026-09-30. The .4 trial rejects production adoption of
Microsoft Agent Framework 1.22.0 for this path. The .2 qualification-image source is merged, its P2
executor candidate passed the actual six-case native gate at its prior exact head, remains unmerged after
a conflict-free rebase whose current-head qualification is pending, and Rust/Go image
profiles are protected with a passing strict native run. P2 source delivery, P3 publication, P4 receiver
adoption, P5 matrix closure and full .5 language-route adoption remain open.
**Part:** **V2-LANG-01**, indexed in the
[Unified roadmap](../2026-09-07-154210-fs-gg-unified-development-roadmap.md#98-feature-parts-and-subroadmap-index)
and the [V2 execution roadmap](../github-substrate-v2-roadmap.md#language-independent-product-workspaces--2026-09-29).
**Owners:** Coordination owns execution and protocol adapters; `.github` owns shared planning and
qualification coordination; SDD/Templates own published workspace profiles and materialization;
product owners retain their implementation, verification and delivery authority.

This is a prospective extension to supported product integration. It preserves the accepted selected
V2 profile and the frozen R5 population, cutoff and historical outcomes. New language fixtures use
separate qualification identities; they cannot supplement or rewrite that historical cohort.
The [host execution boundary](../github-substrate-v2-roadmap.md#required-host-execution-boundary--2026-09-29)
still applies. Framework integration creates no authority to run general-purpose agents on Home/Main.

## Runtime and ownership boundaries

Separate the coordination runtime, agent implementation and product languages. Reuse the selected
Akka.NET foundation where its existing scope applies, together with the supported CLI/workflow path.
Product repositories need neither an Akka dependency nor a Microsoft Agent Framework SDK. Any .NET
runtime needed by shared tooling belongs to that tooling's declared distribution prerequisites;
product code and product build commands remain independent of it.

The [actor design](../coordination/2026-08-31-operations-research-first-agent-orchestration-design.md#52-actor-hierarchy)
keeps work-item lifecycle, capacity, generation authority and effect settlement in durable owners.
An agent or framework returns candidates and observations. Its session state, checkpoint or completion
event cannot accept delivery, release an unsettled reservation or renew the original attempt budget.

[Microsoft Agent Framework](https://github.com/microsoft/agent-framework) supplies agent abstractions,
typed graph workflows, checkpoints, middleware and telemetry across its supported implementations.
SDK availability does not constrain the languages of product source. Record qualified capabilities
per SDK and version rather than assuming implementation parity.

Its documented [workflow execution model](https://learn.microsoft.com/en-us/agent-framework/concepts/workflows/builder-and-execution)
uses superstep barriers: all triggered executors finish before subsequent stages advance. Therefore
keep independent roadmap lanes under the existing scheduler. Trial a framework graph only within a
bounded collaboration whose synchronization is intentional; do not put the entire roadmap behind
one graph's barrier. Include a slow-branch case proving unrelated work items can still advance.

## Portable workspace contract

Publish a versioned process or service contract, with standalone commands and optional SDKs. Internal
CLR types, actor references, F# union encodings and framework checkpoint objects are not public wire
contracts. Define schema versions and portable representations for identifiers, counters, timestamps,
missing values and errors; preserve unknown outcomes and reject unsupported required capabilities.
Cross-language fixtures must agree on serialization, validation and schema-upgrade behavior.
Command envelopes retain stable command/idempotency IDs, expected revisions, fencing generations,
causation and deadlines. Specify exact counter encoding, UTC timestamp precision and absent-versus-null
semantics; verify canonical bytes and receipt digests across implementations. An adapter must refuse
foreign workspace scope or stale authority before an effect, regardless of its implementation language.

Extend the [provider-neutral execution boundary](2026-09-09-190726-standalone-telemetry-host-and-orchestration.md#23-execution-providers-and-subscription-sessions)
with workspace/toolchain declarations. Resolve these from reviewed, versioned configuration, subject
to the existing execution authorization. A product profile identifies:

- components, working directories, toolchain/runtime versions and qualified execution images;
- build, test, lint and artifact entry points for each component and the composed product;
- bounded execution parameters and structured results, including exit status, artifact references,
  source revision, verification identity and explicit incomplete/unknown evidence;
- cancellation-request and termination-observed events, deadlines and recovery behavior.

The host accepts neither arbitrary uploaded shell recipes nor toolchain selection that bypasses its
fixed-operation boundary. Provision selected toolchains in isolated development or qualification
environments. Pin dependencies and separate incompatible caches. A mixed frontend/backend workspace
must verify component results and its actual composed product journey; a generic build success alone
cannot certify product functionality. Keep clean creation, retained upgrades and local-only operation
separate and qualify each supported route against actual published tooling.

## AG-UI presentation trial

Use the [framework-independent .NET SDK](https://devblogs.microsoft.com/dotnet/ag-ui-dotnet-sdk/) for a
read-only projection of one existing work item. Start with `AGUI.Abstractions` and `AGUI.Formatting`
over SSE, which covers the protocol's full event set. The optional protobuf codec covers a subset.
Reference `AGUI.Server` when its `IChatClient` adapter fits; a domain-event projection can emit the
protocol primitives without converting the coordination core into a chat client.

Map [run, message, tool, activity and state events](https://docs.ag-ui.com/concepts/events) to canonical
work-item, attempt, session and generation identities. Preserve event order and provenance through
the existing durable replay path; reconnect must expose gaps or recover a verified snapshot. UI state
is a projection and never bootstraps authoritative workflow state. An
[interrupted run](https://docs.ag-ui.com/concepts/interrupts) may end with `RUN_FINISHED` while waiting
for input; even an ordinary finished run is not verified work-item delivery.

Qualify a client in a different language from the Host, duplicate events, lost connection, replay,
stale generations, observer loss and truthful partial state. A later write or approval interface
requires the existing authenticated, revision-bound command contract and a separate bounded window.
The initial projection creates no new mandatory workspace service or source-merge dependency.

## Bounded Agent Framework trial

Compare one existing agent route with an optional `AIAgent` adapter and a bounded workflow, using the
same inputs and finite allocation. Preserve CLI/subscription authentication and provider-specific
usage provenance; an `IChatClient` interface alone does not qualify cancellation or cost accounting.
Keep framework-specific checkpoints opaque, pinned to compatible implementation/topology identities,
and referenced from the parent attempt's records. External effects still require intent, reconciliation
and verified receipts through the existing core.

Borrow typed message declarations and graph validation from
[workflow executors](https://learn.microsoft.com/en-us/agent-framework/concepts/workflows/executors).
F# implementations can evaluate the generic executor base classes or a narrow C# shim instead of
requiring C# partial-class generators in product workspaces. Use
[middleware](https://learn.microsoft.com/en-us/agent-framework/concepts/agents/middleware/) and
telemetry hooks where the adapter supports them, with canonical correlation identities. Tool-call
middleware does not govern arbitrary CLI effects or replace core capability enforcement.

Measure implementation effort, dependency footprint, debugging and recovery cost, and remaining
custom code. Use the existing [bounded investment rule](../2026-09-07-154210-fs-gg-unified-development-roadmap.md#81-investment-trigger-and-smallest-experiment).
Record adopt, defer or reject from that comparison; language independence does not depend on adoption.
Pin adopted packages and retain MIT notices when copying substantial framework or SDK source.

The bounded trial closed through [Coordination PR #899](https://github.com/FS-GG/FS.GG.Coordination/pull/899)
at protected `9516006663393709e8f96ecd1f21b9ce16d729bd`. Its
[coherent run 36709064662](https://github.com/FS-GG/FS.GG.Coordination/actions/runs/36709064662)
succeeded on attempt 2 after one bounded Apalache timeout rerun. The
[owning trial record](https://github.com/FS-GG/FS.GG.Coordination/blob/9516006663393709e8f96ecd1f21b9ce16d729bd/docs/roadmaps/v2-lang-agent-framework-trial.md)
retains the actual `AIAgent` and fixed-workflow tests, fail-closed checkpoint handling, durable
coordinator recovery, cancellation and unknown-effect cases, package footprint and reproducible runtime
measurements. The decision is **reject production adoption of Microsoft Agent Framework 1.22.0 for this
path**: the existing coordinator already owns launch and durable reconciliation, while the framework adds
dependencies and runtime and maintenance cost without a demonstrated unmet product need. The optional
source trial remains evidence; it is not published, installed, activated or added to generated workspaces.

## Language qualification population

Use the enrolled app journeys as reusable functional cases, with separately identified language
fixtures. The following assignments are proposed qualification variants, not instructions to rewrite
already delivered apps or change FourD's selected stack:

| Functional case | Proposed qualification variant | Required product evidence |
|---|---|---|
| Hello world | Python | Invoke the actual entry point and verify the greeting |
| Todo | TypeScript | Add/edit/complete/filter/delete and retained state |
| Tic-tac-toe | Rust | Legal moves, wins/draws, terminal refusal and restart |
| Snake | Go | Input, growth, score, collision, pause and restart |
| FourD | Existing selected stack | Preserve its accepted design-v2 technical comparison and save/runtime boundaries |
| Composed product | TypeScript frontend with Python backend | Verify both component commands and the frontend-to-backend journey |

Select exact fixture repositories, revisions and toolchains before execution. Equivalent existing
products can supply cases when they exercise the same contracts. Qualify every selected language
route through enrollment, isolated execution, native functional verification, artifact/result readback,
cancellation, duplicate delivery and recovery. Include an unsupported-toolchain refusal and a
non-.NET product environment that uses the distributed integration tooling without a .NET SDK or
product-library dependency. Record any tooling runtime requirement explicitly.

### Current source and native evidence

Source delivery and native qualification are separate gates:

- [Coordination PR #900](https://github.com/FS-GG/FS.GG.Coordination/pull/900) merged the pinned
  qualification-image source at protected
  `aca2093cf7186eee2f57f4bba8ff55c8563ab861`. It does not publish or install a producer.
- [Coordination PR #901](https://github.com/FS-GG/FS.GG.Coordination/pull/901) independently merged
  fixed native-capability diagnostic source at protected
  `6210dc1612e38acc7f16a6a6ce62bfad9f280c97`. Its authenticated development result advertised
  `gpt-5.6-sol` / `medium`, started zero model sessions and completed owned cleanup. This is
  capability-source evidence, not a collector installation, native capture or language-route adoption.
- [Templates PR #650](https://github.com/FS-GG/FS.GG.Templates/pull/650) merged fixed Rust and Go image
  profiles at protected `66ce4faacc122ef4a2d2331a0c10fe388e7c3b69`. The strict hosted
  [native run 36744671457](https://github.com/FS-GG/FS.GG.Templates/actions/runs/36744671457)
  passed both actual built entry points. This qualifies those source/image fixtures without adopting
  installed routes.
- Coordination [PR #902](https://github.com/FS-GG/FS.GG.Coordination/pull/902) remains open at current
  rebased head `241a2a25b8eacf867fc3bdd37f740cea4457ebf8`, whose hosted qualification is pending. Its prior
  [native run 36747384736](https://github.com/FS-GG/FS.GG.Coordination/actions/runs/36747384736)
  belongs to exact source `9ec9535d6e4b5da5c0840caeb17dd5abb0331c27` and completed six actual
  isolated operations with 6 passed, 0 failed and 0 unknown, plus complete cleanup.
  Independently downloaded artifact `11113157066` had SHA-256
  `cafe68ba23e26489ebe0d741887c15e94ac2400314d3f564ecf14f19bb33f18c`.
  The successful native gate does not close P2 before protected source delivery.

## Delivery windows and closure

- [x] **V2-LANG-01.1 — Select the amendment.** Record language independence, durable ownership,
  independent lane progression, bounded framework trials and the prospective qualification population.
- [ ] **V2-LANG-01.2 — Publish portable workspace integration.** Coordination and SDD/Templates
  publish the schemas, adapters and toolchain profiles. Qualify cross-language serialization, fresh
  creation, retained adoption, mixed components and local-only operation against exact artifacts.
  P1 utility and installed-package preparation are present on the #902 candidate. #900 supplies the
  merged qualification-image source, and #902's prior exact head passed its separate six-case native
  executor run. The rebased current head awaits hosted qualification and remains unmerged, so P2 source
  delivery is pending; P3 publication, P4 adoption and P5
  closure remain future gates.
- [x] **V2-LANG-01.3 — Qualify the AG-UI projection.**
  [Coordination PR #894](https://github.com/FS-GG/FS.GG.Coordination/pull/894) merged the optional
  read-only projection at protected `5d86daf3898be683bd6720bdd36da479a29a0260` after exact-head
  hosted checks. The [owning source plan](https://github.com/FS-GG/FS.GG.Coordination/blob/5d86daf3898be683bd6720bdd36da479a29a0260/docs/roadmaps/v2-lang-agui-projection.md)
  records a Python SSE client, durable replay, duplicate/reconnect, gap, stale-generation and
  observer-loss qualification and the pinned SDK/dependency cost. Adoption is limited to the source
  adapter; no endpoint, package publication, receiver activation or write/approval interface is selected.
- [x] **V2-LANG-01.4 — Evaluate bounded Agent Framework execution.**
  [Coordination PR #899](https://github.com/FS-GG/FS.GG.Coordination/pull/899) merged the bounded
  `AIAgent` facade, real fixed workflow, adverse-lifecycle qualification and retained measurement
  evidence at protected `9516006663393709e8f96ecd1f21b9ce16d729bd`. Exact inputs permit one existing
  coordinator launch; wrong or duplicate calls create no extra launch, and workflow completion never
  claims delivery. Recovery remains with the durable coordinator. Framework session and workflow
  checkpoint replay fail closed, cancellation and deadlines propagate, and unknown provider effects
  remain explicit. The measured decision rejects production adoption of version 1.22.0 for this path;
  no package publication, installation, activation or generated-workspace change follows.
- [ ] **V2-LANG-01.5 — Qualify and adopt language routes.** Run the selected matrix through .2's
  actual published contract, verify native product journeys and scoped results, and record supported
  and unresolved routes. Include .3's UI and .4's framework cases only for routes selected for adoption.
  [Templates PR #649](https://github.com/FS-GG/FS.GG.Templates/pull/649) source-delivered the native
  TypeScript, Rust and Go fixtures at protected `b517903b0c03a99d33150b9f7f959a08683e6bcb` from exact
  head `ae0a370e0a9d08dd010ffb492a6abf852804ce5b`. The
  [native fixture run 36713489760](https://github.com/FS-GG/FS.GG.Templates/actions/runs/36713489760)
  passed all three jobs with Rust 1.98.1, Go 1.27.1, Node 24.8.0 and TypeScript 5.9.2; the
  [composition run 36713489579](https://github.com/FS-GG/FS.GG.Templates/actions/runs/36713489579)
  also passed.
  [Templates PR #650](https://github.com/FS-GG/FS.GG.Templates/pull/650) subsequently merged fixed
  Rust and Go image profiles at protected `66ce4faacc122ef4a2d2331a0c10fe388e7c3b69`. Its strict
  hosted [native run 36744671457](https://github.com/FS-GG/FS.GG.Templates/actions/runs/36744671457)
  passed both actual built-entrypoint routes. This advances source/image and native gates only.
  The [owning language-routes plan](https://github.com/FS-GG/FS.GG.Templates/blob/66ce4faacc122ef4a2d2331a0c10fe388e7c3b69/docs/roadmaps/v2-lang-language-routes.md)
  keeps protected P2 delivery, P3 publication, P4 receiver adoption, P5 matrix closure and full
  installed route adoption open.

The .2 integration and .3 projection proceeded independently after .1, and .4 closed as a bounded
source evaluation without selecting framework adoption. Each .5 product route depends on its own
qualified .2 binding and does not wait for unrelated products. Declare
the matrix before closing the parent: partial route delivery cannot prove complete language coverage.
All remaining windows have executable technical cases; no elapsed-time observation or human study is
required. Source delivery, package publication and verified receiver adoption remain separate outcomes.
