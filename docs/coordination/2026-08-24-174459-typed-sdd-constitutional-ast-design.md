---
title: "Design: Typed SDD constitutional model and compiled governance bridge"
category: Design
categoryindex: 4
index: 24
description: "A deferred successor design that moves Typed SDD constitutional semantics into a typed AST while preserving human ratification, Governance optionality, and existing lifecycle lanes."
---

# Design: Typed SDD constitutional model and compiled governance bridge

| Field | Value |
|---|---|
| Status | Proposed successor; implementation deferred behind Typed SDD P4 |
| Authored | 2026-08-24T17:44:59+02:00 |
| Scope | Typed SDD constitutional authority, ratification, projections, migration, and Governance integration |
| Depends on | [ADR-0076](../adr/0076-agent-authored-fsharp-specification-kernel.md) and the [Typed SDD P0–P4 implementation](../reports/2026-08-24-094348-typed-protocol-kernel-roadmap.md) |
| Extends | [Typed protocol kernel design](2026-08-24-typed-protocol-kernel-design.md) |
| Preserves until activation | [ADR-0004](../adr/0004-constitution-ownership-for-lifecycle-sdd-products.md) and the current `.fsgg/constitution.md` contract |
| Activation gate | P4 exit receipt: Typed SDD is a supported published opt-in across every provider/profile |

## Executive decision

Typed SDD should eventually represent a product constitution as a first-class typed extension of the
shared `SpecificationModel`. Its principles, applicability, obligations, exceptions, amendment rules,
evidence requirements, stable identity, version, and ratification status belong in the inspectable AST.
The current `.fsgg/constitution.md` then becomes a generated human projection for Typed SDD, not a higher
semantic authority outside the model.

This is deliberately **not part of the current Typed SDD implementation**. No constitution AST, migration,
compiler surface, provider value, or Governance contract described here may be added to P0–P4. Work may
begin only after P4 has published and proven the base kernel, extension contract, normalization,
provenance, authoring receipts, migration framework, and supported `typed-sdd` lifecycle. This document
must not force the pilot to design a constitutional extension before the generic substrate is stable.

P5—the separate decision to make Typed SDD the omitted-value default—does not silently absorb this work.
After P4, maintainers must explicitly choose one of two sequences:

1. implement and soak the constitutional extension before the P5 default flip; or
2. complete P5 with the then-current Typed SDD constitution posture and introduce this feature afterward.

Either sequence requires its own feature lifecycle, cross-repository issues, compatibility decisions,
published artifacts, and acceptance evidence. This design expresses the intended architecture, not
authorization to widen the active roadmap.

The constitutional AST owns **what a product requires and why**. FS.GG.Governance remains an optional
consumer and enforcement engine that owns **how a requirement is sensed, who is competent to decide it,
and at which boundary a failure blocks**. Governance rule implementations are referenced through stable,
versioned identities; their predicates are not copied into FS.GG.SDD. Standard SDD retains its authored
Markdown constitution. Freeform (`none`) retains no FS-GG lifecycle constitution.

## Why this feature is needed

The current architecture has three individually reasonable constitutional representations whose
relationship is only conventional:

1. FS.GG.SDD owns and seeds `.fsgg/constitution.md` as the lifecycle constitution.
2. FS.GG.SDD also carries an authoritative Markdown content contract and an embedded F# string literal
   whose bytes are tested against that contract.
3. FS.GG.Governance already implements a composed F# `fsharp-constitution` profile containing four
   executable rule packs and generates the corresponding `capabilities.yml` region.

That arrangement predates Typed SDD. In Standard SDD it is defensible: Markdown is the authored lifecycle
authority, tests protect the seed, and Governance is an optional executor of a useful subset. In Typed SDD
it would create an exception at the top of the authority hierarchy. Requirements and protocol semantics
would be canonical AST nodes, while the rules that govern how those nodes may be authored and implemented
would remain canonical prose.

The result would be a constitutional version of the same defect the specification kernel is intended to
remove:

- a principle can be amended in Markdown without changing its executable Governance mapping;
- a Governance pack can change applicability or identity without a constitutional semantic diff;
- generated agent guidance can cite a constitution version without naming the normalized model that
  supplied it;
- a customized Markdown constitution has no stable node identity, so migration must compare sentences;
- “ratified” is prose rather than a typed state backed by a receipt; and
- compiler or normalizer changes can alter constitutional interpretation without a distinct compatibility
  classification.

The answer is not to put all governance behavior inside FS.GG.SDD. The answer is to give constitutional
meaning one typed home and make enforcement bind to it through an explicit contract.

## Existing authority and machinery

### SDD constitution ownership

ADR-0004 assigns the lifecycle constitution to FS.GG.SDD and places it at
`.fsgg/constitution.md`. Providers remain app-only and Freeform emits no constitution. This ownership
decision remains correct: a Typed SDD constitution is still a lifecycle artifact, so its extension
contract and generic base profile belong to FS.GG.SDD rather than Rendering, Templates, Governance, or a
product provider.

The current SDD skeleton deliberately preserves an existing authored constitution instead of overwriting
it on refresh. That no-clobber property must survive migration. A compiler may diagnose an old or ambiguous
constitution, but refresh and upgrade may not reinterpret or rewrite it without an accepted semantic diff.

### The current Markdown constitution

The seeded constitution contains:

- engineering principles;
- applicability words such as “non-trivial,” “public,” and “multi-step”;
- required artifacts and evidence;
- Tier 1 and Tier 2 classification;
- lifecycle expectations;
- amendment and semantic-versioning policy; and
- an unratified status statement.

These are structured concepts even though they are presently expressed as prose. Some require human
judgement, but human judgement does not make their identity, applicability, expected evidence, or decision
status unmodellable.

The current sentence that calls the Markdown file the “highest-precedence engineering authority” is
compatible with Standard SDD. Under Typed SDD, the generated Markdown must instead say that it is a
projection of a named constitutional model and that the referenced normalized digest is authoritative.

### Existing FS.GG.Governance constitution profile

Governance already has a real typed constitutional asset, not merely YAML configuration. Its
`SurfaceChecks.Profile` composes four packs:

- public surface;
- idiomatic simplicity;
- effect boundary; and
- evidence boundary.

The profile owns rule identities, pack ownership, maturity, rationale, collision detection, normalized
findings, and the generated reference-gate-set projection. Enforcement inherits that profile independently
of whether a consumer edits or deletes its resolved YAML. This work must be reused rather than recreated.

It is not yet a complete product constitution. It covers four executable engineering surfaces but does not
represent the full Markdown constitution, its amendment semantics, product-local principles, ratification,
or lifecycle classification. Conversely, the Markdown constitution does not carry the Governance profile's
precise rule identities, sensing boundaries, maturity, or finding provenance.

### Existing SDD/Governance handoff

SDD emits an optional versioned `governance-handoff.json`; Governance consumes it without importing SDD
implementation code or mutating SDD artifacts. This one-way contract is the correct integration shape. The
constitutional extension should enrich or version that handoff after producer publication, not introduce a
source-project reference or make Governance a Typed SDD build dependency.

### Existing meta-governance

ADRs remain the durable record of why a cross-repository choice was made. The Coordination board remains
the sequencing authority. The dependency registry remains the cross-repository contract inventory. Moving
constitutional content into an AST does not turn the AST into the owner of its own rollout, package
publication, or cross-repository legitimacy.

## Governing principles for the design

1. **One constitutional meaning.** A principle, obligation, exception, and applicability rule has one
   stable node identity and one normalized meaning.
2. **No circular self-authorization.** A compiler may validate and project a constitution; it cannot by
   itself ratify a constitution or approve a change to its own interpretation.
3. **Human ratification, agent-mediated canonical writes.** Agents author the typed source through the
   repository workflow. Humans accept or refuse the semantic constitutional diff.
4. **Governance stays optional.** Typed SDD must compile, migrate, author, verify, and ship its lifecycle
   artifacts without installing FS.GG.Governance.
5. **Enforcement does not become semantics.** Severity, maturity, execution environment, route cost, and
   protected-boundary posture do not redefine the constitutional obligation they enforce.
6. **No silent prose inference.** A migration recognizes a known model or reports clauses requiring
   resolution. It does not guess structured meaning from unrestricted Markdown.
7. **No-clobber remains true.** Refresh and upgrade may create proposed migrations and projections, never
   silently replace an authored constitution.
8. **Draft and ratified are different values.** An unratified baseline cannot be reported as ratified
   authority merely because it parses or compiles.
9. **Derive, do not restate.** Markdown, agent guidance, lifecycle checklists, Governance handoffs, and
   schemas carry model IDs and fingerprints and are derived where their contents overlap.
10. **Later feature, proven substrate.** The constitution extension specializes the published Typed SDD
    extension contract; it does not amend that contract speculatively during P0–P4.

## Authority model

The target authority split is:

| Concern | Authority |
|---|---|
| Lifecycle selection (`none`, `sdd`, `typed-sdd`) | FS.GG.SDD lifecycle/provenance contract |
| Generic Typed SDD constitutional vocabulary | FS.GG.SDD constitution extension package |
| Product constitutional principles and obligations | Product-authored constitutional extension source |
| Normalized constitutional meaning and identity | Canonical specification compiler output |
| Human acceptance of a constitutional revision | Ratification/amendment receipt |
| Governance rule implementation and sensing | FS.GG.Governance rule pack |
| Check competency, maturity, severity, route, and boundary | Governance policy/profile |
| External facts | Their source authority, represented as typed observations |
| Lifecycle readiness | FS.GG.SDD readiness model |
| Cross-repository rollout and versions | ADRs, dependency registry, and Coordination board |
| Human-readable constitution | Generated Markdown projection for Typed SDD |

No row may be silently answered by the authority in another row. In particular, a blocking Governance
verdict does not amend a constitutional principle, and a constitutional compiler success does not prove
that an external check passed.

## Architecture

```text
human constitutional intent
          |
          v
repository-owned constitution authoring skill
          |
          v
agent-authored constitution extension source
          |
          v
Typed SDD compiler + normalizer
          |
          +--> normalized ConstitutionModel + digest
          +--> semantic constitutional diff
          +--> generated .fsgg/constitution.md
          +--> lifecycle obligations/evidence plan
          +--> constitutional binding manifest
          +--> agent guidance projection
          |
          v
human ratification/amendment decision
          |
          v
revision-bound ratification receipt
          |
          +-----------------------------+
          |                             |
          v                             v
FS.GG.SDD readiness             optional Governance adapter
                                      |
                                      v
                             Governance rule packs/profile
                                      |
                                      v
                         advisory/blocking enforcement verdict
```

The constitutional model is one extension of the shared `SpecificationModel`, not a separate compiler or
root document format. Product requirements may reference constitutional nodes. Constitutional nodes may
refer to registered evidence/check contracts, but must not depend on a product requirement whose validity
they are supposed to govern; the compiler rejects that cycle.

## Constitutional extension model

The exact builder syntax is deferred until the post-P4 implementation spike. The normalized concepts are
not deferred:

```fsharp
type ConstitutionId = private ConstitutionId of string
type PrincipleId = private PrincipleId of NodeId
type ObligationId = private ObligationId of NodeId
type AmendmentId = private AmendmentId of NodeId

type ConstitutionStatus =
    | Draft
    | Ratified of RatificationRef
    | Superseded of successor: ConstitutionId * receipt: AmendmentRef

type NormativeForce =
    | Required
    | Forbidden
    | Recommended
    | Permitted

type Applicability =
    | Always
    | ChangeClass of ChangeClassId
    | Surface of SurfaceId
    | LifecycleStage of LifecycleStageId
    | NamedPredicate of PredicateContractRef
    | All of Applicability list
    | Any of Applicability list

type VerificationRequirement =
    | StructuredArtifact of ArtifactContractRef
    | Evidence of EvidenceKind * EvidenceSubject list
    | RegisteredCheck of CheckContractRef
    | HumanDecision of DecisionKind
    | ExplicitException of ExceptionPolicy

type ConstitutionalObligation =
    { Id: ObligationId
      Force: NormativeForce
      Statement: StructuredStatement
      AppliesWhen: Applicability
      Requires: VerificationRequirement list
      Rationale: StructuredStatement
      Supersedes: ObligationId list }

type ConstitutionalPrinciple =
    { Id: PrincipleId
      Title: string
      Statement: StructuredStatement
      Obligations: ConstitutionalObligation list
      Evidence: EvidenceObligation list
      References: NodeId list }

type ConstitutionModel =
    { Id: ConstitutionId
      Version: ConstitutionVersion
      Status: ConstitutionStatus
      BasedOn: ConstitutionRef list
      Principles: ConstitutionalPrinciple list
      ChangeClasses: ChangeClassDefinition list
      AmendmentPolicy: AmendmentPolicy
      Provenance: AuthoringProvenance }
```

`StructuredStatement` must be inspectable enough to render deterministic prose and a semantic diff. It is
not an unrestricted string that hides every normative distinction. Natural-language rationale remains
textual, but force, applicability, references, exceptions, required evidence, and supersession are typed.

`NamedPredicate` and `RegisteredCheck` use versioned contract references. They cannot contain arbitrary F#
closures. A predicate implementation may remain opaque only under the specification kernel's registered
algorithm rules: declared inputs, outputs, reads, evidence, implementation fingerprint, and compatibility
policy.

## Bootstrap and physical artifacts

The design must avoid replacing one exceptional authority with another. A small bootstrap manifest is
therefore allowed outside the constitutional extension, but it contains no substantive principle:

```json
{
  "schema": "fsgg.typed-sdd.constitution-bootstrap/v1",
  "lifecycle": "typed-sdd",
  "specification": "product-specification",
  "extension": "fsgg.constitution/v1",
  "constitution": "product-constitution",
  "source": "<canonical source reference selected by the published P4 layout>",
  "normalizedDigest": "sha256:<digest>",
  "compiler": "<package identity>",
  "status": "draft|ratified|superseded",
  "ratificationReceipt": "<optional artifact reference>"
}
```

The bootstrap answers only which model is in force and how to locate and verify it. It cannot override a
principle, add an exemption, lower an obligation, or declare a Governance verdict. Its exact path must reuse
the artifact-layout convention actually published by P4; this successor design intentionally does not
preempt that pending contract.

For Typed SDD:

- canonical F# source is agent-authored through the normal authoring capability;
- the normalized AST and digest are the comparison/interchange form;
- `.fsgg/constitution.md` is retained as the stable human-facing location but becomes generated;
- a machine-readable constitutional manifest records node identities and external bindings;
- ratification/amendment receipts are immutable readiness evidence; and
- generated guidance cites the constitution ID, version, digest, and projection freshness.

For Standard SDD, `.fsgg/constitution.md` remains authored and authoritative. For Freeform, none of these
artifacts is required. A command must select behavior from the lifecycle provenance; it may not infer the
lane from the presence of a file.

## Composition and inheritance

A useful constitution must support both a generic lifecycle baseline and product-local decisions without
creating copied constitutions. Composition is by stable node identity:

```text
published Typed SDD base constitution
               +
optional published product/profile constitution
               +
repository-local constitutional amendments
               =
normalized effective constitution + composition receipt
```

Rules:

1. A layer references its base by contract version and digest range; it does not copy base nodes.
2. A local node may add a principle or strengthen an obligation without renaming the inherited node.
3. Weakening, removing, narrowing, or replacing an inherited obligation is an amendment, never an ordinary
   override. It requires explicit rationale, compatibility classification, and ratification.
4. Duplicate stable IDs with unrelated meaning are a compiler error.
5. Supersession is explicit and acyclic.
6. Composition order cannot decide meaning. Conflicting amendments are refused rather than resolved by
   last writer.
7. The effective model records every contributing package/model identity and normalized digest.
8. Updating a base never rewrites a local source. The upgrade command presents an effective semantic diff
   and waits for acceptance.

This resembles Governance's non-lowerable inherited floor but does not blindly adopt “strictest wins.” Two
constitutional statements may not share a simple severity ordering. A genuine semantic conflict requires a
decision; it cannot be collapsed to the stronger-looking string.

## Ratification and amendment protocol

Agent-mediated authoring and human constitutional authority are compatible if they are represented as two
different capabilities.

### Authoring

The constitution authoring skill follows the common Typed SDD protocol:

1. load the compiled effective constitution and relevant dependency cone;
2. capture the requested constitutional intent;
3. identify whether the change adds, strengthens, weakens, narrows, removes, or clarifies an obligation;
4. ask one repository-unanswerable material question at a time;
5. present the proposed typed form and generated Markdown together;
6. compile, normalize, validate composition, and render a semantic diff;
7. run focused evidence and compatibility checks; and
8. leave the revision proposed until a human ratifies or refuses it.

The authoring receipt proves use of the sanctioned compiler path. It does not prove ratification.

### Ratification

A ratification receipt binds a human decision to exact normalized meaning:

```fsharp
type RatificationReceipt =
    { Constitution: ConstitutionId
      Version: ConstitutionVersion
      Digest: ContentDigest
      PreviousDigest: ContentDigest option
      Decision: DecisionRef
      AcceptedAt: DateTimeOffset
      CompilerIdentity: PackageIdentity
      SemanticDiffDigest: ContentDigest
      Scope: RatificationScope }
```

The receipt records a reviewable external decision reference; the compiler does not infer human approval
from commit author, branch protection, or the existence of generated files. A repository may define which
human role is authorized through external policy, but that authorization remains an observation from its
own authority.

`Draft`, `Ratified`, and `Superseded` are distinct outcomes. A draft may guide authoring and produce
diagnostics. Whether work may ship under a draft is an explicit lifecycle or Governance policy; no tool may
print “ratified” merely because the model is valid.

### Semantic versioning

Version treatment is derived from the semantic diff:

| Change | Minimum treatment |
|---|---|
| Rationale or projection wording with unchanged normalized obligation | Patch |
| New principle or materially expanded obligation | Minor |
| New evidence binding that does not change the obligation | Minor plus enforcement compatibility review |
| Weakening, removal, incompatible applicability, or changed normative force | Major |
| Stable ID reassignment to different meaning | Refused; create a new ID and supersede |
| Normalizer change that changes effective meaning | Major compiler/model migration |

The compiler proposes the classification and explains it. Ratification accepts both the semantic diff and
the version treatment.

## Compiler self-change and the circularity boundary

The constitution may govern compiler development, but the new compiler cannot be the sole judge of whether
its changed interpretation preserves the constitution. A compiler or normalizer upgrade that can affect
constitutional meaning uses a two-compiler protocol:

1. compile the current source with the currently trusted compiler;
2. compile it with the candidate compiler;
3. compare normalized models using the old and new semantic-diff contracts;
4. classify codec, normalization, validation, and projection changes separately;
5. replay retained constitutional fixtures and ratification receipts;
6. require an explicit migration decision for any semantic difference; and
7. preserve a rollback path to the previous compiler and model digest.

The trusted bootstrap is therefore small: lifecycle selection, compiler/package identity, model reference,
digest, status, and ratification receipt reference. Substantive policy does not leak back into the
bootstrap.

## Relationship to FS.GG.Governance

### Boundary

The constitutional AST and Governance answer different questions:

| Constitutional model | Governance |
|---|---|
| What is required, forbidden, permitted, or recommended? | How is it sensed or reviewed? |
| To which changes or surfaces does the obligation apply? | Which repository paths and environments invoke the check? |
| What evidence kind satisfies the obligation? | Which adapter or command obtains that evidence? |
| Which stable constitutional node explains the requirement? | Which rule/finding identity reports the verdict? |
| What amendment changed the meaning? | At which mode/profile does failure block? |

The boundary is a versioned binding, conceptually:

```fsharp
type ConstitutionalCheckBinding =
    { Obligation: ObligationId
      Check: CheckContractRef
      Satisfies: EvidenceKind list
      ApplicabilityMapping: ApplicabilityMapping
      ProducerFingerprint: ContentDigest }
```

The binding cannot restate the constitutional statement. It names the obligation and declares what
evidence the check can supply. Governance retains its own `CheckTier`, maturity, severity, cost,
environment, route, inheritance, and explanation behavior.

### Reusing the existing F# constitution profile

The existing `fsharp-constitution` profile becomes the first bridge candidate after activation:

- each existing Governance rule identity maps to one constitutional obligation or to a declared
  input-state diagnostic;
- the constitutional extension references the published Governance profile/contract version;
- Governance's `SurfaceChecks.Profile` remains the authority for pack implementation, maturity, sensing,
  and finding normalization;
- the generated `capabilities.yml` region remains a Governance distribution projection; and
- a cross-repository compatibility fixture proves that every bound obligation resolves to the expected
  Governance identity without copying the rule inventory into SDD.

The bridge must expose honest partial coverage. The existing four packs do not satisfy the whole
constitution. Unbound constitutional obligations remain `Unbound`, `HumanDecision`, or satisfied by an
SDD-owned structured-artifact validator as declared. They never become `Pass` merely because no Governance
rule exists.

### Preserving optionality

Typed SDD cannot reference Governance implementation assemblies. The shared boundary is a published data
contract or leaf contract type with no I/O. When Governance is absent:

- the constitution still compiles and renders;
- SDD still evaluates its own structural lifecycle obligations;
- human-review obligations remain explicit;
- external check bindings are reported as unavailable or not configured, according to their declared
  requirement; and
- lifecycle completion follows SDD policy rather than fabricating a Governance verdict.

Installing Governance adds sensing, explanation, routing, and enforcement. It does not change the
constitution's normalized meaning.

## Lifecycle integration

The constitutional model participates in the existing lifecycle without adding a stage:

| Stage | Constitutional behavior |
|---|---|
| Charter | Select effective constitution and record model/digest/status |
| Specify | Classify scope and requirements against constitutional applicability |
| Clarify | Surface unresolved constitutional ambiguity one material choice at a time |
| Checklist | Derive applicable obligations and required evidence |
| Plan | Explain compliance, exceptions, migrations, and public-surface consequences by node ID |
| Tasks | Ensure required obligations have owned implementation/evidence tasks |
| Analyze | Refuse missing, contradictory, stale, or unbound mandatory obligations |
| Evidence | Attach receipts to constitutional obligation IDs |
| Verify | Recompile, check projection freshness, replay bindings, and evaluate evidence |
| Ship | Bind the exact ratified constitution digest into readiness and optional Governance handoff |

Skills dispatch through the selected representation backend. Standard SDD skills continue reading the
authored Markdown constitution. Typed SDD skills read the compiled constitutional model and show its
Markdown projection. There is one lifecycle instruction corpus, not `sdd` and `typed-sdd` stage forks.

## Migration

### Known generic seed

The exact generic constitution seed has a known digest and known clause inventory. Its migration may be
lossless:

1. recognize the exact supported seed version;
2. reference the equivalent published Typed SDD base-constitution model;
3. generate the new Markdown projection;
4. show that normalized obligations match the known mapping;
5. leave the constitutional state `Draft` unless an existing valid ratification can be migrated; and
6. write nothing until the human accepts the semantic diff and rollback boundary.

The migration should reference the base model rather than transcribe all seed principles into a local F#
file.

### Ratified or locally edited Markdown

Arbitrary prose is not automatically converted into typed semantics. The analyzer divides input into:

- `Recognized` clauses with an exact supported mapping;
- `ChangedKnownClause` entries whose prior identity is known but whose meaning needs review;
- `LocalClause` entries with no existing typed identity;
- `Ambiguous` text with multiple plausible meanings; and
- `Unsupported` structures that require manual redesign.

An agent may propose typed nodes for changed or local clauses, but a human must resolve their meaning and
ratify the resulting model. The legacy file remains untouched until acceptance. A failed or abandoned
migration leaves Standard SDD fully usable.

### Existing Governance adopters

Migration does not regenerate or overwrite the four Governance YAML files as an incidental SDD action.
After the Typed SDD model is accepted, a separate Governance resolution step may update generated profile
regions or bindings, with the existing no-clobber and derivation gates. Existing hand-authored routing,
policy, and tooling configuration remains Governance-owned.

### Rollback

Rollback restores lifecycle selection and the previous compiler/model references. It does not reverse
remote Governance verdicts, delete ratification receipts, or rewrite canonical specifications. Both the
pre-migration Markdown bytes and post-migration model/projection digests are retained in the migration
receipt.

## Failure semantics

The implementation must distinguish at least:

- constitution source missing;
- bootstrap missing or malformed;
- unsupported extension version;
- compiler unavailable;
- normalized digest mismatch;
- stale Markdown projection;
- direct canonical edit without authoring receipt;
- draft constitution where ratification is required;
- ratification receipt for another digest;
- ambiguous Standard SDD migration;
- inherited-base version unavailable;
- conflicting amendments;
- unknown constitutional node referenced by a requirement;
- mandatory evidence obligation with no binding;
- Governance absent;
- Governance binding unsupported;
- Governance read/check indeterminate; and
- external observation stale or incomplete.

These cannot collapse to “constitution invalid” or a Boolean. Diagnostics name the constitution, node,
source version, compiler identity, expected/observed digest, and recovery path where applicable.

## Verification strategy

### Compiler and model properties

- normalization is deterministic and independent of declaration order where order has no meaning;
- two authoring syntaxes with the same meaning produce identical normalized bytes;
- duplicate IDs, cycles, orphan obligations, invalid supersession, and missing versions are refused;
- composition is deterministic and conflict refusal is order-independent;
- semantic version classification agrees with planted strengthening, weakening, removal, and wording-only
  controls; and
- generated Markdown parses only as a projection and cannot be fed back as canonical Typed SDD input.

### Ratification properties

- a receipt for digest A cannot ratify digest B;
- an authoring receipt cannot substitute for ratification;
- a compiler upgrade that changes normalization triggers the two-compiler path;
- `Draft`, `Ratified`, and `Superseded` remain distinct through JSON round trips; and
- a superseded constitution cannot become current without an explicit rollback/amendment receipt.

### Migration properties

- every supported historical generic seed maps to an exact base-model version;
- one-byte changes outside recognized non-semantic regions do not silently map to the seed;
- edited, reordered, missing, or added clauses produce explicit migration outcomes;
- no migration writes before acceptance;
- abort preserves original bytes and lifecycle selection; and
- migration and rollback receipts replay deterministically.

### Governance bridge properties

- every declared bridge binding resolves both a constitutional obligation and a published Governance rule;
- an empty binding census is a refusal, not complete coverage;
- unknown Governance rule identities remain malformed/unbound rather than fabricated violations;
- Governance maturity or severity changes do not change the constitutional model digest;
- constitutional meaning changes do change its digest and invalidate stale bindings when required;
- deleting Governance configuration cannot lower an inherited Governance floor; and
- Typed SDD without Governance still completes its supported lifecycle path.

### Projection and guidance properties

- Markdown, agent guidance, readiness, and handoff artifacts carry constitution identity and digest;
- a hand edit to a projection is stale and regenerates from the AST;
- Claude and Codex guidance derive equivalent constitutional obligations; and
- generated content never claims a draft is ratified or an unavailable check passed.

## Cross-repository ownership and rollout

| Repository | Future responsibility |
|---|---|
| S.I.R. | No new responsibility; supplies evidence about the generic extension substrate only |
| FS.GG.SDD | Constitution extension types/compiler, generic base model, authoring/migration skills, projections, readiness and handoff producer |
| FS.GG.Governance | Check-binding consumer, existing F# profile mapping, sensing/enforcement, compatibility fixtures, generated gate-set projection |
| FS.GG.Templates | Compose published lifecycle/provider support only after producer publication |
| Product repositories | Own local constitutional extensions, human ratification, product-specific evidence and adoption decision |
| `.github` | Cross-repository ADR, registry contracts, sequencing, compatibility and rollout evidence |

Producer publication precedes consumer adoption. No source-project reference, local package shortcut, or
shared working-tree dependency may satisfy a rollout acceptance condition.

## Deferred delivery plan

The following milestones are intentionally inactive until the activation gate is met.

### C0 — Post-P4 evidence and boundary confirmation

- Verify the exact published extension, normalization, migration, provenance, and authoring contracts.
- Re-run this design against real P4 artifacts and update any conceptual type or path that the proven
  kernel makes obsolete.
- Inventory current constitutions and Governance profile versions across representative consumers.
- Decide through a separate ADR whether delivery precedes or follows P5.

Exit: the successor feature has its own Standard or Typed SDD work item, cross-repository issues, narrow
touch-sets, and no change to completed P0–P4 behavior.

### C1 — Constitution extension vertical slice

- Implement the smallest constitutional AST and compiler extension in FS.GG.SDD.
- Represent a small subset of the generic seed with stable identities, applicability, obligations,
  evidence, status, and deterministic Markdown.
- Complete one agent/human amendment and ratification session.

Exit: one real constitution revision compiles, diffs, projects, ratifies, and replays without Governance.

### C2 — Generic base constitution and Standard SDD migration

- Model the full generic seed.
- Map every known clause and expose every judgement-bearing phrase.
- Add exact-seed and customized-Markdown migration analysis, acceptance, rollback, and no-clobber tests.

Exit: every supported generic seed is lossless or returns an explicit stable ambiguity.

### C3 — Governance bridge

- Publish the constitutional binding contract from SDD.
- Map Governance's existing `fsharp-constitution` rules without copying its inventory into SDD.
- Version the handoff and add installed-package producer/consumer fixtures.

Exit: Governance explains and enforces bound constitutional obligations by stable identity, while absence
of Governance preserves the Typed SDD lifecycle.

### C4 — Consumer soak

- Migrate representative unmodified, customized, ratified, unratified, governed, and ungoverned products.
- Measure authoring questions, ambiguous clauses, binding gaps, projection drift, and rollback use.
- Remove compatibility shadows after each accepted cutover.

Exit: the feature demonstrates bounded authoring friction, no silent migration, no second constitutional
authority, and no Governance dependency for ordinary Typed SDD operation.

### C5 — Adoption decision

- Decide whether new Typed SDD workspaces receive the typed base constitution automatically.
- Decide the posture for existing Typed SDD workspaces and whether any default flip dependency is warranted.
- Publish migration/operator guidance and exact release identities.

Exit: adoption is an explicit versioned contract change, not an incidental compiler upgrade.

## Alternatives considered

### Keep Markdown authoritative in Typed SDD

This preserves the existing contract but leaves the highest-precedence lifecycle rules outside the
canonical model. Requirements and evidence would be typed while the rules governing them remained prose.
Rejected as the long-term design; retained for Standard SDD and as the migration source.

### Treat the current Governance F# profile as the constitution

The profile is valuable and must be reused, but it represents four executable rule packs rather than a
complete product constitution. It also owns enforcement maturity and sensing concerns that should not
define constitutional meaning. Rejected as the sole model.

### Move Governance into FS.GG.SDD

This would make optional enforcement a lifecycle dependency, duplicate or relocate a mature inference
kernel, and contradict the one-way handoff boundary. Rejected.

### Put Governance policy inside the constitutional AST

Severity, mode, route cost, environment, and competency answer operational enforcement questions. Folding
them into constitutional meaning would make a `warn`→`block-on-ship` rollout look like a constitutional
amendment and would prevent repositories from choosing an enforcement posture. Rejected; use versioned
bindings.

### Generate the constitutional AST from Markdown

A restricted frontmatter language could be parsed, but unrestricted Markdown cannot express stable typed
semantics without either guessing or becoming another DSL. Rejected as canonical authoring. Exact known
Markdown versions remain valid migration inputs.

### Replace `.fsgg/constitution.md` with no human projection

This would make constitutional review depend on reading F# builder mechanics or normalized JSON. Rejected.
The Markdown path remains a first-class, digest-bearing review surface.

### Implement this during Typed SDD P2 or P4

Doing so would expand the requirements extension, migration adapter, provider tests, and authoring protocol
before their base contracts are proven. It would also force current implementers to reconcile Governance's
profile while building the substrate it must consume. Rejected by the activation gate.

## Consequences

### Benefits

- Typed SDD has no exceptional prose authority above its canonical model.
- Constitutional changes receive stable identity, semantic diffs, compatibility classification, and
  ratification receipts.
- Requirements, plans, evidence, guidance, and Governance findings can cite the same obligation IDs.
- Existing Governance rule packs and enforcement machinery are reused rather than copied.
- Standard SDD and Freeform preserve their present semantics.
- Compiler self-change and constitutional ratification become visible, replayable operations.
- Customized constitutions migrate honestly instead of being silently overwritten or heuristically parsed.

### Costs and risks

- Constitutional concepts add a powerful extension that can become over-general or ceremonial.
- Structured applicability cannot remove every human judgement; pretending otherwise would create false
  determinism.
- The base/local composition and amendment rules require long-term compatibility discipline.
- Existing unratified and customized Markdown constitutions may require real human resolution.
- The Governance bridge adds another published contract and coordinated release sequence.
- A generated constitution can obscure changes unless semantic diffs remain readable and ratification is
  bound to exact normalized meaning.
- Deferring the feature means early Typed SDD versions may continue using the current constitution posture;
  that temporary boundary must be documented rather than mistaken for the final architecture.

## Stop conditions

Return to design rather than widening implementation if post-P4 evidence shows that:

- the published extension contract cannot express constitutional nodes without a kernel-breaking change;
- a constitutional semantic diff cannot distinguish strengthening from weakening reliably;
- product-local composition requires order-dependent conflict resolution;
- human ratification cannot be bound to exact normalized meaning;
- Governance integration requires SDD to depend on Governance implementation code;
- migration would need unrestricted prose inference or silent rewriting;
- the bootstrap must contain substantive policy to make the system work; or
- constitutional validation prevents discovery, drafting, or explicit unresolved judgement.

## Acceptance conditions

The feature is complete only when:

1. the Typed SDD P4 activation gate is evidenced before the first implementation change;
2. the full generic constitution is represented by stable typed nodes with deterministic normalization;
3. `.fsgg/constitution.md` is a fresh, digest-bearing projection in Typed SDD and remains authored authority
   in Standard SDD;
4. exact known seeds migrate losslessly and customized constitutions produce explicit outcomes without
   writes before acceptance;
5. draft, ratified, and superseded states are typed and backed by valid receipts;
6. constitutional amendments produce semantic and version classifications, including weakening controls;
7. compiler/normalizer changes execute the two-compiler compatibility protocol;
8. the existing Governance F# profile binds by stable obligation/check identities without copied rule
   semantics;
9. Governance absence does not prevent supported Typed SDD compilation, authoring, verification, or ship;
10. Governance presence adds honest sensing and enforcement without changing constitutional meaning;
11. generated Markdown, guidance, readiness, and handoff artifacts carry the same constitution identity and
    fingerprint;
12. product-local inheritance is deterministic, conflict-refusing, and no-clobber;
13. installed-package producer/consumer fixtures pass in dependency order with no source-project shortcut;
14. representative governed and ungoverned consumers complete the full lifecycle and rollback controls; and
15. a separate adoption decision names whether the feature lands before or after P5 and the exact release
    identities that carry it.

## Decision still required after P4

This design recommends the constitutional AST and the SDD/Governance boundary. It intentionally does not
decide whether constitutional support must precede the Typed SDD default flip. That choice should be made
from P4 evidence:

- If the existing constitution posture creates a second semantic authority in real Typed SDD opt-in work,
  implement C0–C4 before P5.
- If the posture is a bounded projection/bootstrap concern and migration risk dominates, complete P5 and
  deliver the constitutional extension as a separately versioned successor.

In either case, the current Typed SDD implementation finishes first.

## Sources and existing contracts

- [Agent-authored F# specification kernel and canonical mutation algebra](2026-08-24-typed-protocol-kernel-design.md)
- [Specification and protocol kernel roadmap](../reports/2026-08-24-094348-typed-protocol-kernel-roadmap.md)
- [ADR-0004: SDD constitution ownership](../adr/0004-constitution-ownership-for-lifecycle-sdd-products.md)
- [ADR-0058: derive, do not restate](../adr/0058-adopt-one-governing-principle-derive-dont-restate.md)
- [ADR-0076: agent-authored specification kernel](../adr/0076-agent-authored-fsharp-specification-kernel.md)
- [Current product constitution](../../.fsgg/constitution.md)
- [Current FS.GG architecture: SDD and Governance](../architecture.md)
- [FS.GG.Governance decision 0012: composed F# constitution profile](https://github.com/FS-GG/FS.GG.Governance/blob/main/docs/decisions/0012-composed-fsharp-constitution-profile.md)
- [FS.GG.Governance reference constitution profile](https://github.com/FS-GG/FS.GG.Governance/blob/main/reference-gates/README.md)
- [FS.GG.SDD generic constitution content contract](https://github.com/FS-GG/FS.GG.SDD/blob/main/specs/033-skeleton-constitution/contracts/constitution-content.md)
