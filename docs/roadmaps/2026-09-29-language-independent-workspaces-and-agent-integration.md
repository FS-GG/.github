# Language-independent V2 workspaces and agent integration

Product workspaces choose their own languages and toolchains, including multiple languages in one
repository. Keep the existing coordination core and expose portable integration contracts. Evaluate
AG-UI at the presentation boundary and Microsoft Agent Framework inside bounded agent attempts.

**Status:** selected roadmap amendment, 2026-09-29; implementation and qualification remain pending.
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

## Delivery windows and closure

- [x] **V2-LANG-01.1 — Select the amendment.** Record language independence, durable ownership,
  independent lane progression, bounded framework trials and the prospective qualification population.
- [ ] **V2-LANG-01.2 — Publish portable workspace integration.** Coordination and SDD/Templates
  publish the schemas, adapters and toolchain profiles. Qualify cross-language serialization, fresh
  creation, retained adoption, mixed components and local-only operation against exact artifacts.
- [ ] **V2-LANG-01.3 — Qualify the AG-UI projection.** Project one work item through the existing
  read-only evidence boundary and exercise a different-language client, replay, duplicates, stale
  generations and observation gaps. Compare integration and maintenance cost before adopting it.
- [ ] **V2-LANG-01.4 — Evaluate bounded Agent Framework execution.** Trial one compatible adapter
  and collaboration, including checkpoint recovery, unknown external outcomes, cancellation and a
  slow branch beside an independently progressing work item. Record adopt, defer or reject with
  evidence; do not require framework adoption to close language qualification.
- [ ] **V2-LANG-01.5 — Qualify and adopt language routes.** Run the selected matrix through .2's
  actual published contract, verify native product journeys and scoped results, and record supported
  and unresolved routes. Include .3's UI and .4's framework cases only for routes selected for adoption.

The .2 integration and .3 projection windows can proceed independently after .1; .4 preparation can
also proceed independently, while an integration claim requires .2's qualified contract. Each .5
product route depends on its own qualified binding and does not wait for unrelated products. Declare
the matrix before closing the parent: partial route delivery cannot prove complete language coverage.
All remaining windows have executable technical cases; no elapsed-time observation or human study is
required. Source delivery, package publication and verified receiver adoption remain separate outcomes.
