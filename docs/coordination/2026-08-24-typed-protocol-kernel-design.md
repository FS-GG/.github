---
title: "Design: Agent-authored F# specification kernel and canonical mutation algebra"
category: Design
categoryindex: 4
index: 23
description: "One agent-authored F# specification AST, piloted in S.I.R., with typed process and mutation extensions for FS-GG coordination."
---

# Design: Agent-authored F# specification kernel and canonical mutation algebra

| Field | Value |
|---|---|
| Status | Accepted direction; staged implementation required |
| Authored | 2026-08-24T09:43:48+02:00 |
| Updated | 2026-08-24T10:19:38+02:00 — name the `typed-sdd` lifecycle and prepare its default transition |
| Evidence snapshot | 2026-08-24T07:26:29Z |
| Scope | A shared specification kernel, the S.I.R. pilot, and the FS-GG coordination process/protocol extension |
| Extends | [ADR-0034](../adr/0034-typed-coordination-engine.md), [ADR-0040](../adr/0040-port-the-io-layer.md), [ADR-0058](../adr/0058-adopt-one-governing-principle-derive-dont-restate.md) |
| Cross-repo decision | [ADR-0076](../adr/0076-agent-authored-fsharp-specification-kernel.md) |
| Predecessor design | [Reducing coordination change amplification](2026-08-22-coordination-change-risk-mitigation-design.md) |
| Delivery plan | [Specification and protocol kernel roadmap](../reports/2026-08-24-094348-typed-protocol-kernel-roadmap.md) |

## Executive decision

The S.I.R. executable-rules EDSL and the proposed coordination EDSL belong under one architectural
umbrella: an **agent-authored F# specification kernel**. They are not one universal language. A small
shared AST owns specification identity, vocabulary, requirements, constraints, decisions, evidence
obligations, provenance, composition, versions, and normalized projections. Typed extension models add
product rules, process state machines, or external mutation semantics without forcing those domains into
one closed discriminated union.

S.I.R. is the first pilot. Its existing executable rules corpus already supplies the demanding evidence:
typed facts, predicates, formula ASTs, transition contracts, registered opaque algorithms, canonical
encoding, content identity, generated documentation, replay bindings, and agent-facing author/check
skills. The pilot will generalize only the substrate proven reusable by that corpus. After the pilot,
FS.GG.SDD becomes the platform owner of the extracted specification contracts and authoring lifecycle;
S.I.R. remains owner of gameplay vocabulary and interpreters. Publication precedes adoption by other
FS-GG repositories.

The coordination engine then consumes the same specification substrate through a **typed protocol
extension**. That extension is the sole authority for protocol vocabulary, observed facts, process state,
commands, decisions, events, mutation semantics, receipts, and projections. External systems continue to
own their raw state; one source-specific adapter turns each successful or failed read into a
revision-bound typed observation. Only kernel decisions may produce mutation plans, and only
source-specific interpreters may execute those plans.

The kernel will include a small F# embedded DSL, but the DSL's output—not its syntax—is canonical. It
must build an inspectable `SpecificationModel` with typed extension nodes. The canonical compiler checks
and normalizes that AST, then derives validators, schemas, documentation, transition diagrams, command
metadata, and model tests. Arbitrary F# closures hidden inside the model are not sufficient because a
generator or checker cannot inspect them; deliberately opaque algorithms must be registered, fingerprinted,
and carry explicit contracts and evidence obligations.

Agents are the only writers of canonical specification source. Humans interact through bounded skills in
an iterative loop: express intent, inspect the current model, resolve one material choice, propose a typed
change and human projection, validate it, review the semantic diff, and repeat. This is an authoring policy
and tool capability—not trust in an account identity. CI accepts changes only when they pass through the
canonical compiler, normalizer, provenance record, and generated-view freshness gate. Human-readable
projections remain first-class review surfaces, but never a second authority.

## Lifecycle name and default direction

The consumer-facing process is **Typed SDD**. Its stable machine identifier is `typed-sdd`. “Typed” names
the durable property—the canonical specification is a compiled, inspectable AST—without coupling the
process name to computation-expression syntax, a particular builder, or the current agent runtime.

After the already-decided `spec-kit` retirement, FS-GG workspace and product scaffolding will expose three
durable lifecycle lanes. During the transition, `spec-kit` remains a fourth legacy value:

| Machine value | Human name | Canonical authority | Intended posture |
|---|---|---|---|
| `none` | Freeform | Repository-local choices; no FS-GG specification lifecycle | Permanently available explicit opt-out |
| `sdd` | Standard SDD | Existing SDD lifecycle artifacts and structured contracts | Current default; remains supported after Typed SDD arrives |
| `typed-sdd` | Typed SDD | Agent-authored F# EDSL compiled to the canonical Specification AST | Additive opt-in first; intended future workspace default |

`freeform` is vocabulary for humans, not a new wire value: the existing compatible machine token remains
`none`. The legacy `spec-kit` lane remains on its already-decided retirement path and gains no new role in
this design.

Typed SDD reuses the Standard SDD lifecycle stages, readiness semantics, and evidence discipline. It changes
the authoritative representation and authoring path, not the meaning of “specify, clarify, plan, tasks,
implement, verify, ship.” Skills dispatch to the selected representation backend; they must not fork the
stage model or maintain a second instruction corpus.

Every supported workspace provider and product profile must eventually be able to select `typed-sdd`. No
provider may silently coerce it to `sdd`, treat an unrecognized value as `none`, or claim support while
omitting the compiler, authoring skills, provenance, and readiness checks. The lifecycle value must survive
unchanged through provider descriptors, scaffold provenance, refresh/upgrade, doctor/readiness, generated
guidance, and compatibility receipts.

### Default-transition rule

This document establishes `typed-sdd` as the intended successor default, not the current default. `sdd`
continues to govern omitted lifecycle selection until a separate default-flip decision proves all readiness
conditions and amends ADR-0056. The flip is one versioned cross-repository contract change and must move the
raw template, every provider descriptor, `fsgg-sdd scaffold`, workspace wizard, registry, documentation, and
default-path tests coherently.

Default readiness requires all of the following:

1. S.I.R. has completed the pilot and re-adopted the published kernel without a local shared-substrate copy.
2. FS.GG.SDD publishes the Typed SDD compiler, lifecycle backend, migration tooling, authoring/inspection
   skills, and stable contract fixtures to both feeds.
3. Every supported workspace provider and product profile passes explicit `none`, `sdd`, and `typed-sdd`
   composition tests; an omitted lifecycle still proves `sdd` until the flip commit.
4. Fresh Typed SDD workspaces can complete the full lifecycle using only installed artifacts and generated
   skills, with no S.I.R. checkout or source-project reference.
5. Standard SDD → Typed SDD migration either preserves each supported semantic fact or returns an explicit,
   stable ambiguity; it never silently converts prose guesses into canonical AST nodes.
6. Refresh and upgrade preserve the selected lane, content identity, consumer extensions, and human-readable
   projections. They do not rewrite canonical specifications behind the author's back.
7. Agent unavailability, compiler failure, unsupported extension versions, stale projections, and direct
   canonical edits fail visibly with recovery instructions. `--lifecycle sdd` and `--lifecycle none` remain
   explicit creation-time choices.
8. Representative non-S.I.R. consumer workspaces complete an opt-in soak with no second semantic authority,
   no untyped escape hatch, and bounded authoring friction.
9. The default-flip release includes migration documentation, rollback boundaries, wrong-default controls,
   and exact package/template/provider identities.

The flip does not retire Standard SDD. Retirement, if ever justified by measured adoption and migration
evidence, requires another decision. Freeform (`none`) remains a permanent deliberate escape from the
platform lifecycle rather than an error or degraded Typed SDD mode.

XML will **not** become the canonical representation of outside data. SCXML and BPMN demonstrate that
XML can interchange state-machine or business-process models, but serialization does not establish
authority, freshness, idempotency, or transition legality. FS-GG will retain versioned JSON as its
machine contract, preserve source-native evidence at the boundary, and may generate SCXML or BPMN as
non-authoritative visualization/interchange projections later.

The enforcement rule is deliberately narrower than “everything must be modelled”:

> A discovery may always be recorded. A change may not introduce or alter protocol semantics outside
> the kernel. The repair must extend an existing model, add a new modelled surface, or carry a narrow,
> expiring boundary exemption.

This gates capabilities and executable semantics, not the ability to report something previously
unknown.

## Why these two efforts are one program

S.I.R. and coordination have different semantics but the same representation problem:

| Concern | S.I.R. rules corpus | Coordination protocol | Shared kernel responsibility |
|---|---|---|---|
| Stable meaning | Rule IDs and supersession | Subject, event, field, and operation IDs | Stable IDs, revisions, references, vocabulary |
| Inspectable semantics | Facts, formulas, predicates, transitions | Guards, state transitions, mutation plans | Typed extension AST and declared opaque boundary |
| Human collaboration | `sir-author-rule` iterative skill | Coordination/specification authoring skills | Intent → typed proposal → semantic diff → acceptance loop |
| Derived views | Docs, manifests, explorer, explanations | Docs, skills, schemas, command metadata | Deterministic normalized model and projections |
| Evidence | Examples, properties, coherence, replay | Receipts, inversions, model/replay tests | Evidence obligations, provenance, fingerprints |
| Evolution | Status, supersession, package identity | Event/schema/model compatibility | Versioning, migrations, content identity |

The shared unit is therefore not “workflow” and not “game rule.” It is a versioned, inspectable
specification node with stable identity, declared meaning, references, provenance, and evidence. Domain
extensions own execution. This avoids both duplicated infrastructure and a god DSL.

The current S.I.R. architecture explicitly calls its rules library a product subsystem rather than a
general requirements framework. This decision preserves that boundary during the pilot. Generalization
is an extraction of demonstrated common concepts, not a retroactive transfer of S.I.R. semantics.

## Why another coordination step is necessary

ADR-0034 moved scheduling into a pure F# core and made protocol prose a generated projection. That
removed a large class of bash, nullable-value, and copied-document failures. The remaining churn sits
one level above and below that success:

- above the core, one process fact can still be represented independently as a string, URL, issue-body
  line, Projects field, JSON property, receipt property, and diagnostic;
- below the core, generic writes do not consistently state whether the intent is replacement, set
  membership change, append, transition, creation, or compensation;
- across time, multi-system work can partially land, but the retry is not always driven by a durable
  operation receipt;
- across releases, process code and stored histories evolve without one compatibility classification;
  and
- across callers, a pure predicate can be shared while each caller adds a different precondition around
  it, recreating two authorities.

The 72-hour board window ending at the evidence snapshot contained 54 opened issues and 32 closures,
for net growth of 22. Of the 34 newly opened rows still open, 31 were technical protocol work after
excluding two coordination-room records and one profile change. A conservative reading classified 18
as representation/vocabulary/identity/authority defects and another nine as state-transition,
mutation, retry, or ordering defects. The rate is a symptom; repeated semantic authorship is the
mechanism.

The clearest current class row is [`.github#2903`](https://github.com/FS-GG/.github/issues/2903): a
24-hour product-board pass reduced 23 process errors and two chain resets to one shape—an engine-held
fact accompanied by a hand-authored second representation. Its children expose four separate missing
kernel concepts:

- [`.github#2905`](https://github.com/FS-GG/.github/issues/2905): merge and post-merge verification are
  distinct process states;
- [`.github#2906`](https://github.com/FS-GG/.github/issues/2906): an aggregate verdict needs typed reasons
  and evidence provenance;
- [`.github#2907`](https://github.com/FS-GG/.github/issues/2907): a set-valued dependency field needs
  compare-and-set `Add`/`Remove`, not scalar replacement; and
- [`.github#2908`](https://github.com/FS-GG/.github/issues/2908): selected engine identity and repository
  execution context are separate facts.

This document generalizes those repairs without replacing their bounded implementation rows.

## Relationship to existing designs

This is an extension, not a reset.

| Existing decision or design | What remains authoritative | What this design adds |
|---|---|---|
| ADR-0034 typed coordination engine | Pure F# core, fail-closed unions, sole GraphQL principal, generated protocol prose | One uniform model for observations, commands, events, mutations, and receipts across every process family |
| ADR-0040 typed IO layer | Transport seam and mutation preconditions in types | A closed mutation vocabulary and interpreters that are the only remote-effect capability |
| ADR-0058 derive, don't restate | Every projection names what it derives from | A checkable model census and architectural gate enforcing that rule on protocol-bearing code |
| 2026-08-22 change-risk design | Command catalogue, complete transition predicates, receipt-gated completion, model testing | The common algebra and evolution rules beneath those seven decisions |
| Existing `Protocol.fs` | Protocol documentation is typed data and generated outward | `ProtocolModel` also describes executable structure, schemas, mutations, and compatibility |
| S.I.R. ADR-0001 executable rules corpus | Layered F# rule semantics, canonical identity, generated views, replay, and agent author/check workflows remain S.I.R.-owned | Extract only reusable specification identity, provenance, composition, normalization, and evidence contracts |
| FS.GG.SDD specification lifecycle | SDD owns lifecycle artifacts, stable requirement IDs, validation, and agent/human workflow | Replace Markdown-as-semantic-authority incrementally with compiled F# specification ASTs and derived human projections |

The earlier documents remain useful because they contain incident-specific acceptance criteria. This
design supplies the shared substrate on which those criteria stop being isolated conventions.

## Goals and non-goals

### Goals

1. Give every specification concept and protocol fact exactly one semantic owner.
2. Make failed, partial, stale, absent, and contradictory reads different values.
3. Make invalid lifecycle transitions and ambiguous mutation kinds unrepresentable at the command
   boundary.
4. Make every retry answerable from durable receipts rather than caller memory.
5. Derive every secondary representation from one model or label it explicitly as external evidence.
6. Make protocol evolution replayable and compatibility-classified.
7. Detect new unmodelled executable semantics before review or remote mutation.
8. Preserve the current CLI paths, JSON contracts, rate-limit discipline, and defect corpus during
   migration.
9. Prove agent-authored F# specification ergonomics in S.I.R. before extracting a platform contract.
10. Let product, lifecycle, process, and protocol models share infrastructure without sharing domain
    vocabulary or interpreters.
11. Make Typed SDD selectable for every supported consumer workspace/profile and prepare it to become the
    coherent workspace default after an explicit, evidence-gated flip.

### Non-goals

- Replacing GitHub, Actions, NuGet, or git as external authorities.
- Event-sourcing every piece of repository data.
- Building a general-purpose workflow engine, universal rules language, or one closed AST containing
  every FS-GG domain case.
- Moving S.I.R. gameplay semantics or coordination policy into FS.GG.SDD.
- Inferring typed semantics from unrestricted natural language or arbitrary F# source.
- Preventing humans from reading, reviewing, or discussing specifications; only canonical writes are
  agent-mediated.
- Removing Standard SDD or Freeform merely because Typed SDD becomes the default.
- Preventing discoveries whose vocabulary does not exist yet.
- Treating generated internal consistency as proof that an external property is true. ADR-0034's
  amendment preserving independent property gates continues to apply.

## Canonical terminology

The following words have one meaning in the target architecture.

| Term | Meaning |
|---|---|
| Authority | The external or internal component entitled to answer one fact |
| Subject | The stable identity about which a fact, event, or mutation speaks |
| Observation | A typed value or typed inability to obtain a value, bound to evidence and revision |
| Snapshot | A set of observations used together for one decision |
| Intent | What an actor asks the protocol to do; never a raw storage operation |
| Decision | Pure acceptance or refusal of an intent against a snapshot and current process state |
| Event | An immutable domain statement authorized by a decision |
| Process state | The fold of accepted events, not a board field or issue state |
| Mutation | One typed external-effect primitive compiled from an event |
| Mutation plan | A revision-bound, resumable ordering of mutation steps |
| Receipt | Durable evidence of one attempted effect and its observed outcome |
| Projection | A rebuildable view derived from events, observations, or the protocol model |
| Codec | The sole translation between one source-native representation and one domain type |
| Interpreter | The sole executor of one mutation family against one external authority |
| Typed SDD | The `typed-sdd` lifecycle: agent-authored F# EDSL compiled to the canonical Specification AST |
| Standard SDD | The existing `sdd` lifecycle and artifact authority |
| Freeform | Human name for lifecycle machine value `none`; no FS-GG specification process |

These names belong in the F# types first. CLI strings, JSON keys, documentation tables, and diagrams are
projections of them.

## Architecture

```text
human intent <--> repository-owned skills <--> coding agent
                                           |
                                           v
                             F# specification EDSL
                                           |
                                           v
                     canonical compiler + normalized AST
                       /                 |                \
                      v                  v                 v
          S.I.R. rule extension   SDD lifecycle model   protocol extension
          formulas/transitions    requirements/evidence states/events/mutations
                      \                  |                 /
                       +-------- generated projections ---+

GitHub / Actions / git / files / feeds / package manifests
                           |
                    authority adapters
              raw bytes + source-specific revision
                           |
                           v
              typed Observation<'value> values
                           |
                           v
             +-------------------------------+
             |       typed protocol kernel   |
             |                               |
 intent ---> | decide -> events -> evolve    | ---> process state
             |             |                 |
             |             +-> compile       | ---> mutation plan
             |                               |
             | protocol AST -> projections   | ---> schema/docs/tests
             +-------------------------------+
                                  |
                           mutation interpreters
                                  |
                                  v
                       external writes + receipts
                                  |
                       reconcile/replay projections
```

### Shared kernel and typed extensions

The reusable package is deliberately smaller than either consumer:

```fsharp
type SpecificationId = private SpecificationId of string
type NodeId = private NodeId of string
type ExtensionId = private ExtensionId of string

type EvidenceObligation =
    { Id: NodeId
      Kind: EvidenceKind
      Subjects: NodeId list
      RequiredBy: NodeId list }

type SpecificationNode =
    { Id: NodeId
      Kind: ExtensionId
      Title: string
      Statement: StructuredStatement
      References: NodeId list
      Supersedes: NodeId list
      Evidence: EvidenceObligation list
      Payload: ExtensionPayload }

type SpecificationModel =
    { Id: SpecificationId
      SchemaVersion: SchemaVersion
      Vocabulary: TermDefinition list
      Nodes: SpecificationNode list
      Provenance: AuthoringProvenance }
```

The `ExtensionPayload` name above is conceptual. In authored F#, each consumer has a concrete typed AST and
passes it to a typed extension compiler. The normalized cross-extension envelope contains only the
extension contract ID, version, canonical bytes, and digest; it does not deserialize into `obj`, discover
types through reflection, or fall through to an unrestricted `Other of string`. A consumer supplies a
typed extension module with compiler rules, canonical codec, semantic-diff renderer, projections, and
evidence validators. The compiler composes normalized extensions by stable IDs and contracts; it does not
merge every consumer's unions into one assembly.

The first extension families are:

- **requirements** — scope boundaries, user value, requirements, acceptance scenarios, decisions,
  invariants, and evidence obligations;
- **S.I.R. rules** — facts, predicates, formulas, transitions, registered algorithms, rationale,
  applications, and replay identity;
- **process** — states, intents, commands, events, guards, and projections; and
- **protocol** — authorities, observations, mutation plans, interpreters, receipts, and external
  compatibility.

The source `.fs`/`.fsx` is the reviewable authoring form. A compiler-emitted normalized AST and digest are
the comparison and interchange form. Generated Markdown, JSON Schema, manifests, and diagrams are
projections. Consumers may execute ordinary F# only behind a registered opaque node whose declared
inputs, outputs, reads, writes, evidence, and implementation fingerprint remain inspectable.

### Agent-mediated authoring protocol

The platform authoring skill and domain-specific skills share one protocol:

1. load the compiled model and relevant dependency cone;
2. capture the human's intent and identify unresolved material choices;
3. ask at most one repository-unanswerable material question at a time;
4. present both the proposed typed form and its human projection;
5. apply the F# edit through the repository workflow;
6. compile, normalize, validate references/evidence, and render a semantic diff;
7. execute the domain extension's focused evidence and coherence checks; and
8. iterate until the human accepts or a typed ambiguity remains explicit.

Canonical files carry generation/provenance metadata and are protected by a capability gate that recognizes
the compiler's normalized result. The gate does not attempt to distinguish a human from an agent by GitHub
account, commit author, or prose style. Direct edits simply fail normalization/freshness or lack the required
authoring receipt. Emergency edits use a narrow, expiring exemption and must be normalized in the same
change.

The dependency direction is one way. Adapters may depend on Core domain types. Core cannot depend on
GitHub JSON, environment variables, filesystem paths, process working directories, or the wall clock.
Interpreters may execute a `MutationPlan`; they may not construct domain events or silently reinterpret
intent.

## 1. Observation and evidence algebra

The current `Unreadable`, `Unknown`, `NoVerdict`, and explicit sentinel cases are successful local
examples. Generalize them into a common envelope:

```fsharp
type AuthorityId = private AuthorityId of string
type SubjectId = private SubjectId of string

type Revision =
    | GitHubEntityRevision of updatedAt: DateTimeOffset * etag: string option
    | CommitRevision of sha: string
    | RunRevision of runId: int64 * headSha: string
    | StreamRevision of tail: string option
    | ContentRevision of sha256: string

type Evidence =
    { Authority: AuthorityId
      Subject: SubjectId
      Source: Uri
      Revision: Revision
      ObservedAt: DateTimeOffset
      RawDigest: string }

type Observation<'value> =
    | Present of value: 'value * evidence: Evidence
    | ConfirmedAbsent of evidence: Evidence
    | Unreadable of authority: AuthorityId * subject: SubjectId * reason: ReadFailure
    | Contradictory of observations: Evidence list
```

`ConfirmedAbsent` is constructible only after a complete successful read. Pagination truncation, a rate
limit, missing permissions, a malformed response, and a decoder failure cannot produce it. An adapter
preserves the source document or its content digest so the observation can be audited without making raw
syntax part of the domain model.

A `Snapshot` is not a loose record of defaults. Its constructor validates that every required authority
was observed at a compatible subject and revision. Decision functions do not read external systems or
invent timestamps.

## 2. Commands, decisions, events, and state

Each process family owns four explicit types:

```fsharp
module Delivery =
    type State =
        | Working
        | ReviewAccepted of AcceptedReview
        | MergeAuthorized of MergeAuthorization
        | Merged of MergeReceipt
        | AwaitingPostMergeVerification of MergeReceipt
        | Verified of ProtectedRunReceipt
        | Completed of CompletionReceipt

    type Intent =
        | AuthorizeMerge
        | RecordMerge
        | RecordProtectedRun
        | Complete

    type Event =
        | MergeWasAuthorized of MergeAuthorization
        | PullRequestWasMerged of MergeReceipt
        | MergeWasVerified of ProtectedRunReceipt
        | DeliveryWasCompleted of CompletionReceipt

    val decide: State -> Intent -> Snapshot -> Result<Event list, Refusal list>
    val evolve: State -> Event -> State
```

The event vocabulary describes domain meaning, not storage effects. `StatusWasSetToDone` is a projection
event and therefore the wrong domain event; `DeliveryWasCompleted` is the fact from which board status,
issue closure, claim release, and cleanup are projected.

All consumers use the complete `Decision`. A writer may add only execution facts such as a fresh
revision comparison. It may not repeat a subset of the domain predicate.

## 3. Canonical mutation algebra

The mutation algebra is the closed vocabulary of external effects:

```fsharp
type Mutation =
    | CreateOnce of
        subject: SubjectId *
        key: IdempotencyKey *
        payload: CreatePayload

    | SetScalar of
        subject: SubjectId *
        field: ScalarField *
        expected: Revision *
        value: ScalarValue

    | ClearScalar of
        subject: SubjectId *
        field: ScalarField *
        expected: Revision

    | AddMember of
        subject: SubjectId *
        field: SetField *
        expected: Revision *
        member': SetMember

    | RemoveMember of
        subject: SubjectId *
        field: SetField *
        expected: Revision *
        member': SetMember

    | AppendEvent of
        stream: StreamId *
        expectedTail: EventId option *
        key: IdempotencyKey *
        event: ExternalEvent

    | TransitionExternal of
        subject: SubjectId *
        expectedState: ExternalState *
        expected: Revision *
        target: ExternalState
```

No ordinary caller receives a public “execute arbitrary `Mutation`” capability. Domain events compile
to mutations through a process-owned compiler. This prevents the algebra from becoming a more elegant
version of today's generic `set-field` escape hatch.

Every mutation has:

- a stable subject;
- one authority and interpreter;
- a precondition revision or idempotency key;
- a typed outcome distinguishing applied, already applied, refused before write, stale, partially
  observed, and indeterminate;
- a receipt codec; and
- an inversion proving that the wrong mutation kind cannot produce success.

Set membership is not scalar replacement. Clearing is not setting an empty value. Append is not update.
Creation is not an upsert unless its domain command explicitly says so.

## 4. Mutation plans and durable sagas

GitHub issue creation, labelling, board placement, field projection, comment append, and claim release
cannot participate in one ACID transaction. The protocol must not describe them as atomic. It will use a
durable, centrally orchestrated saga:

```fsharp
type PlannedStep =
    { StepId: StepId
      Mutation: Mutation
      DependsOn: StepId list
      Compensation: Mutation option }

type MutationPlan =
    { OperationId: OperationId
      Intent: IntentEnvelope
      ModelVersion: ProtocolVersion
      Steps: NonEmptyList<PlannedStep>
      Completion: CompletionPredicate }

type StepOutcome =
    | Applied of MutationReceipt
    | AlreadyApplied of MutationReceipt
    | RefusedBeforeWrite of Refusal
    | Stale of expected: Revision * actual: Revision
    | Indeterminate of reason: string
```

The interpreter persists a step receipt before proceeding. Retry reloads the plan and receipts,
re-observes indeterminate effects, and resumes at the first unproved step. Compensation is a new
explicit effect, never deletion or rewriting of history. Some operations are irreversible; the plan
must name that fact and switch to roll-forward recovery before executing them.

This directly governs intake partial writes, review ledger appends, delivery projections, and coherent
release recovery.

## 5. The protocol extension EDSL and canonical model

The protocol extension's ergonomic surface may use a computation expression:

```fsharp
let deliveryModel =
    protocol "delivery" {
        authority githubIssueAuthority
        authority githubPullRequestAuthority
        authority protectedRunAuthority

        state Delivery.State.Working
        stateCase<Delivery.State>

        command Delivery.Intent.AuthorizeMerge
        eventCase<Delivery.Event>

        transition "authorize-merge" fromState Working on AuthorizeMerge
        transition "record-merge" fromState MergeAuthorized on RecordMerge
        transition "verify-merge" fromState Merged on RecordProtectedRun
        transition "complete" fromState Verified on Complete

        projection boardStatusProjection
        projection issueStateProjection
    }
```

The example is illustrative; exact builder syntax is deferred to the roadmap spike. The following
constraints are not deferred:

1. The builder produces a plain, serializable, inspectable protocol extension inside a
   `SpecificationModel` AST.
2. Stable IDs are explicit and survive F# case renames.
3. The compiler rejects duplicate IDs, orphan states/events, unreachable declared states, missing
   authority bindings, unowned mutations, projection cycles, and absent schema versions.
4. Domain decisions remain ordinary total F# functions over typed inputs.
5. Critical guards needed for model checking are represented as named predicates with enumerated input
   dimensions, not arbitrary opaque closures.
6. Computation-expression syntax remains optional; direct construction of the same AST is possible.

F# computation expressions are appropriate syntax for sequencing and custom operations, but syntax
alone is not a model. The normalized AST and its compiler are the authority. S.I.R.'s existing explicit
records and provisional compact builders must be tested alongside computation expressions; the pilot may
conclude that a hybrid is clearer.

## 6. External data and configuration

### Decision: native source, typed meaning, versioned JSON contract

Each external system keeps its native format:

| Source | Native evidence | Canonical domain result |
|---|---|---|
| GitHub REST/GraphQL | JSON response plus ETag/updated time/page completeness | `Observation<GitHubFact>` |
| GitHub Actions | Run/check JSON plus run id and head SHA | `Observation<RunVerdict>` |
| git | Object IDs and command result | `Observation<RepositoryFact>` |
| NuGet/feed | Index/registration response plus package digest | `Observation<PackageFact>` |
| Repo configuration | Versioned JSON or YAML document plus content digest | Typed config value |
| Human-authored prose | Raw text plus parser evidence where legacy requires it | Typed fact or explicit unparseable observation |

Every source/domain pair has one codec. Strict decoders reject unknown fields unless the schema version
defines them as forward-compatible. Encoders are used only for contracts owned by FS-GG. Source-native
raw evidence remains available by digest for audit and decoder regression tests.

For FS-GG-owned configuration, generate JSON Schema 2020-12 from the model where the type mapping is
lossless and maintain explicit schema fragments where it is not. The F# decoder remains semantic
authority; JSON Schema is an early structural validator and editor/tooling projection. Every schema
has round-trip, unknown-field, old-version, and inversion fixtures.

### Why not canonical XML

[SCXML](https://www.w3.org/TR/scxml/) is a standard executable state-machine notation, and
[BPMN 2.0](https://www.omg.org/spec/BPMN/2.0/) includes normative XML schemas. They are valuable prior
art, but unsuitable as this kernel's authority:

- both introduce a second language whose semantics must be mapped to F#;
- neither represents GitHub read completeness, ETags, claim generations, package digests, or the
  protocol's authority rules without extensions;
- XSD validates document structure, not whether an observation was fresh or a mutation used the right
  concurrency precondition;
- broad workflow standards contain substantially more control-flow surface than this protocol needs;
  and
- an XML model plus F# interpreters recreates the “two representations of one fact” problem unless the
  F# is generated entirely from XML, which would discard the compiler and domain types as the primary
  design medium.

SCXML export remains a possible generated visualization/interchange format after the kernel model is
stable. It must never be an input to production decisions.

## 7. Event envelopes and evolution

Receipts and durable events use an envelope inspired by CloudEvents without adopting a broker protocol:

```fsharp
type EventEnvelope<'event> =
    { Schema: SchemaUri
      Id: EventId
      Source: AuthorityId
      Subject: SubjectId
      Time: DateTimeOffset
      CausationId: EventId option
      CorrelationId: OperationId
      ModelVersion: ProtocolVersion
      Revision: Revision
      Data: 'event }
```

`Source + Id` is the deduplication identity. `Subject` is independently addressable. `Schema` changes on
incompatible payload evolution. Stored events are immutable.

Every protocol change is classified before merge:

| Change | Required treatment |
|---|---|
| Add optional projection metadata | Backward-compatible schema change and old-reader fixture |
| Add command/event/state | Model version bump, exhaustiveness update, generated artifact update, history replay |
| Change a transition guard | Behavioral compatibility review plus replay of every retained history |
| Rename wire case or remove field | New schema URI and explicit upcaster/migration |
| Change interpreter semantics | Mutation-contract version bump and old receipt/idempotency replay |
| Change authority for a fact | New authority binding and dual-read migration with divergence evidence |

The engine reports both package version and protocol fingerprint. Generated docs, skills, schemas, and
receipts carry that fingerprint, closing the runtime-artifact identity gap described by `.github#2852`
and `.github#2908`.

## 8. Enforcement: the protocol-surface gate

The gate derives its census from the normalized specification model's protocol extension, project
references, and registered interpreters. It does not maintain a second handwritten list.

It rejects:

- direct remote mutation outside an interpreter project;
- raw comparison of a registered state/status wire string outside its codec;
- a second parser for an authority-owned fact;
- a new protocol-facing `bool` where absence, unreadable, or contradictory are possible;
- hand-authored generation tokens, digests, or IDs that the authority can derive;
- a mutation without expected revision or idempotency identity;
- a ledger append without expected tail;
- a process event with no state evolution;
- a projection with no model source and fingerprint;
- a wire change with no compatibility classification;
- a new model surface with no model-based sequence or inversion coverage; and
- an interpreter path absent from the mutation capability census.

The gate has three explicit outcomes:

```text
MODELLED     change is owned by a protocol surface
EXEMPT       narrow external boundary, reason + expiry + owning issue required
UNMODELLED   protocol behavior exists outside the model; merge refused
```

An exemption is for a genuinely open external boundary, not a convenient string escape hatch. Expiry
is mechanically enforced. There is no permanent `Other of string` case in a closed protocol vocabulary.

The gate runs early, before expensive CI and independent review. Independent behavioral gates remain:
model/projection consistency cannot prove that the model describes the real GitHub or feed behavior.

## 9. Verification strategy

### Compiler and algebra properties

- every specification model and protocol extension compile deterministically;
- stable IDs and fingerprints do not depend on source order;
- encode/decode round trips preserve domain values;
- missing/unknown/contradictory observations never authorize an effect;
- a mutation plan cannot contain an unowned interpreter;
- duplicate event application is idempotent;
- stale revisions cannot overwrite a newer value; and
- projections rebuild from the same accepted event history.

### Model-based sequence testing

Use FsCheck's state-machine approach to generate command sequences against both the pure model and a
fixture-backed interpreter. At minimum, cover:

- concurrent add/remove on set-valued fields;
- lease expiry during review and reacquisition;
- head movement after accepted review;
- duplicate wait completion and append retry;
- merge followed by absent, red, unreadable, then green protected runs;
- issue creation followed by label or board-field rejection;
- stale local refs and conflicting engine manifests; and
- process-version upgrade while an operation is partially complete.

The model and system under test must agree on state, emitted events, remote calls, receipts, and refusal
kind. Shrunk failing sequences join the permanent defect corpus.

### Replay and mutation testing

Retain representative production-safe histories and replay them on every protocol change, following the
same determinism principle used by durable workflow systems. Mutation controls remove one guard,
revision, subject, authority, idempotency key, or projection source and must make a named test red.

### Optional formal verification

The F# model remains executable authority. For high-concurrency protocols—the claim CAS, operation
election, set mutation, and saga retry state—generate or maintain a small TLA+ model to check safety,
deadlock freedom, and selected liveness properties. Formal models are verification artifacts, not a
second executable implementation.

## Prior work and lessons adopted

| Prior work | Adopted lesson | Deliberate limit here |
|---|---|---|
| S.I.R. executable rules corpus and author/coherence skills | Layer inspectable facts/formulas/transitions from registered opaque algorithms; make one typed corpus drive execution, explanations, projections, evidence, and replay | S.I.R. remains the gameplay owner and the pilot must prove a concept before it enters the shared kernel |
| FS.GG.SDD typed artifact readers and `SpecificationIntent` authoring | Stable requirement/story/acceptance IDs and lifecycle validation are already valuable typed contracts | Stop parsing Markdown into the final semantic authority after the versioned migration |
| Harel statecharts | Explicit event/state structure; hierarchy and orthogonal regions where concurrency genuinely exists | Do not introduce hierarchy merely to compress a small state space |
| Workflow Patterns Initiative | Use established control-flow patterns as a coverage checklist | Do not adopt a general workflow language |
| W3C SCXML | Event-driven state-machine interchange and conformance tests are feasible | XML is at most a generated projection |
| OMG BPMN 2.0 | Process notation and interchange can serve human review | BPMN execution semantics are too broad for kernel authority |
| Temporal deterministic workflows | Pure replayable workflow decisions must isolate nondeterministic external activities; histories must be replayed across code changes | Do not add a workflow service to the coordination critical path |
| Event sourcing | Intent-named events, append-only audit, optimistic stream concurrency, rebuildable projections | Event-source only protocol lifecycles that benefit; do not event-source the entire repository |
| Saga orchestration | Multi-authority operations require idempotent steps, compensation/roll-forward, semantic locking, and observability | Keep the orchestrator inside the existing tool rather than a distributed service |
| CloudEvents | Stable source, id, subject, and schema identities improve deduplication and routing | Use the envelope concepts without requiring CloudEvents transports |
| F# computation expressions | A readable embedded syntax can build a custom computation | The produced AST, not builder magic, is canonical |
| FsCheck model-based testing | Generate stateful operations against both a reference model and the implementation | Preserve deterministic defect fixtures for exact regressions too |
| JSON Schema | Standard structural validation and tooling for JSON documents | The F# decoder and domain model own semantics |
| CUE and Dhall | Typed/constraint configuration and normal forms reduce config drift | Adding a second configuration language is not justified while F# already owns the engine |
| TLA+ | State-machine specifications can check safety/liveness over concurrent behavior | Apply selectively to the hardest protocols, not as a parallel implementation of everything |

## Alternatives considered

### Keep adding narrow types and gates opportunistically

Rejected as the end state. It produces valuable repairs but lacks a census proving that every process
surface participates. The last three days show that a local type often moves the divergence to the next
boundary.

### Canonical mutation algebra without a process model

Rejected as incomplete. It would distinguish `Add` from `Set` but would not establish whether the event
authorizing the mutation is legal or whether `Merged` and `Verified` are separate states.

### Canonical process EDSL without a mutation algebra

Rejected as incomplete. A perfect state graph can still execute a destructive replacement, duplicate an
append, or strand a partial multi-system write.

### SCXML or BPMN XML as the source of truth

Rejected for production authority. These are mature interchange standards, but FS-GG would still need a
semantic bridge for its typed observations, revisions, authorities, receipts, and GitHub-specific
effects. That bridge would become the real protocol while XML appeared canonical.

### Full event sourcing

Rejected. It is costly and constrains future storage design. Apply append-only histories only to process
state and durable operations where audit, replay, and recovery justify the complexity.

### Adopt Temporal or another durable workflow runtime

Rejected for now. The ideas fit; the operational dependency does not. Coordination must remain
repairable from a checkout when package feeds or hosted systems are degraded. The internal model can be
designed so a future runtime adapter is possible.

### Gate every finding until it has a model case

Rejected. That would suppress the observations needed to discover missing concepts and encourage vague
escape cases. Gate executable changes, not reporting.

## Consequences

### Benefits

- Most protocol changes become one domain edit followed by generated projections and compiler errors.
- Mutation intent, concurrency, and retry behavior become inspectable and testable.
- Stored histories can prove whether a new engine version preserves in-flight work.
- Runtime output can name the exact authority, subject, revision, and protocol fingerprint behind a
  verdict.
- Future findings attach to a stable model surface or establish a genuinely new one.
- S.I.R., SDD, and coordination share specification infrastructure without transferring domain ownership.
- Humans get consistent agent-mediated iteration and semantic diffs while canonical edits remain typed.

### Costs and risks

- The kernel introduces more explicit concepts and initially increases code volume.
- A universal algebra or EDSL can become a “god model”; bounded process modules and capability-specific
  extensions/interpreters are required to prevent that.
- Agent-only authoring can become ceremonial or unauditable unless it is enforced by compiler capability,
  receipts, and semantic diffs rather than account identity.
- Event and schema evolution require long-term compatibility discipline.
- Generated outputs can create false confidence; external property gates and fixture adapters must stay
  independent.
- Migration temporarily runs old and new paths together, increasing surface until each compatibility
  cutover is complete.
- Overzealous enforcement can punish discovery. The modelled/exempt/unmodelled distinction must remain
  visible and reviewable.

## Acceptance conditions for the architecture

The design is considered implemented only when:

1. the S.I.R. pilot completes real iterative agent/human authoring sessions over representative facts,
   formulas, transitions, registered algorithms, projections, evidence, and replay;
2. the shared kernel is extracted into and published by FS.GG.SDD, then consumed by S.I.R. without a local
   shadow copy;
3. every protocol-bearing external read appears in the authority/codec census;
4. every remote write is reachable only through a registered mutation interpreter;
5. claim, review, delivery, dependency, intake, and release processes expose typed state/intent/event
   modules or an explicit documented exemption;
6. every durable operation resumes solely from receipts after injected failure at any step;
7. every generated projection carries a specification/protocol fingerprint and regenerates cleanly;
8. a specification/protocol-surface gate rejects deliberate unmodelled read, decision, mutation, and projection
   controls;
9. retained histories replay on the current engine and on every compatibility migration;
10. model-based tests compare the pure model with fixture-backed interpreters;
11. at least the claim/election and set-mutation protocols have bounded concurrency verification; and
12. a 30-day post-cutover reading shows no successor chain caused by hand-authored second
    representations;
13. every supported workspace provider/profile can explicitly scaffold and verify `typed-sdd` without
    changing the current omitted-value default;
14. Standard SDD migration, refresh, upgrade, authoring, and failure-mode controls satisfy the
    default-readiness conditions; and
15. a separate contract-change decision flips every default-bearing surface together only after the
    opt-in soak is accepted.

## Sources

- David Harel, [Statecharts: A Visual Formalism for Complex Systems](https://doi.org/10.1016/0167-6423(87)90035-9), 1987.
- Workflow Patterns Initiative, [Control-Flow Patterns](https://www.workflowpatterns.com/patterns/control/).
- W3C, [State Chart XML (SCXML) 1.0](https://www.w3.org/TR/scxml/), 2015.
- Object Management Group, [Business Process Model and Notation 2.0](https://www.omg.org/spec/BPMN/2.0/).
- Temporal, [Workflow definition and deterministic constraints](https://github.com/temporalio/documentation/blob/main/docs/encyclopedia/workflow/workflow-definition.mdx).
- Microsoft, [Event Sourcing pattern](https://learn.microsoft.com/en-us/azure/architecture/patterns/event-sourcing).
- AWS, [Saga orchestration pattern](https://docs.aws.amazon.com/prescriptive-guidance/latest/cloud-design-patterns/saga-orchestration.html).
- Cloud Native Computing Foundation, [CloudEvents specification](https://github.com/cloudevents/spec/blob/main/cloudevents/spec.md).
- Microsoft, [F# computation expressions](https://learn.microsoft.com/en-us/dotnet/fsharp/language-reference/computation-expressions).
- FsCheck, [Model-based testing](https://fscheck.github.io/FsCheck/StatefulTestingNew.html).
- JSON Schema, [Specification 2020-12](https://json-schema.org/specification).
- CUE, [Language specification](https://cuelang.org/docs/reference/spec/).
- Dhall, [Language and formal-semantics repository](https://github.com/dhall-lang/dhall-lang).
- Leslie Lamport, [The specification language TLA+](https://www.microsoft.com/en-us/research/wp-content/uploads/2016/12/The-Specification-Language-TLA.pdf).
