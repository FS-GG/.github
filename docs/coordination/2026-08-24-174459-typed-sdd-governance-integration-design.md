---
title: "Design: Typed SDD and complete Governance integration"
category: Design
categoryindex: 4
index: 24
description: "A deferred successor design for incorporating the complete FS.GG.Governance pipeline into Typed SDD, including a typed constitution, operational policy boundaries, evidence, enforcement, release, and audit."
---

# Design: Typed SDD and complete Governance integration

> **Authoring dependency update:** [ADR-0077](../adr/0077-quint-first-typed-specification-authority.md)
> replaces canonical F# authoring for future Typed SDD work. This Governance design's normative-versus-
> operational authority split, one-way handoff, ratification, and no-clobber rules remain. Its future
> constitutional source must be Quint and its stable subjects must come from the generated compiled
> contract described by the [migration design](2026-08-25-quint-first-typed-sdd-migration-design.md).

| Field | Value |
|---|---|
| Status | Proposed successor; implementation deferred behind the Quint-first backend and migration |
| Authored | 2026-08-24T17:44:59+02:00 |
| Scope | Typed SDD constitutional authority plus the complete Governance configuration, sensing, routing, checking, evidence, enforcement, ship, release, and audit pipeline |
| Depends on | [ADR-0077](../adr/0077-quint-first-typed-specification-authority.md), the [Quint-first migration](2026-08-25-quint-first-typed-sdd-migration-design.md), and the historical [Typed SDD P0–P4 implementation](../reports/2026-08-24-094348-typed-protocol-kernel-roadmap.md) |
| Extends | [Typed protocol kernel design](2026-08-24-typed-protocol-kernel-design.md) |
| Preserves until activation | [ADR-0004](../adr/0004-constitution-ownership-for-lifecycle-sdd-products.md) and the current `.fsgg/constitution.md` contract |
| Activation gate | Quint backend publication, migration acceptance, and consumer soak; P4 alone no longer qualifies the authoring substrate |

## Executive decision

Typed SDD should eventually incorporate the whole FS.GG.Governance system through a versioned governance
integration extension and handoff, not merely connect a typed constitution to four constitutional checks.
The constitution is the normative root of that integration: its principles, applicability, obligations,
exceptions, amendment rules, evidence requirements, stable identity, version, and ratification status
belong in the inspectable `SpecificationModel`. The current `.fsgg/constitution.md` then becomes a generated
human projection for Typed SDD, not a higher semantic authority outside the model.

The constitution is not the whole governance model. The design also incorporates Governance's existing
configuration validation, fact sensing, adapters, path/capability routing, gate registry and selection,
reified check algebra, deterministic/agent/human competency tiers, evidence capture and reuse, freshness,
cache and cost controls, inherited organizational floors, mode/profile enforcement, ship rollup, release
preconditions, attestations, audit records, generated views, and CLI/host execution boundary. Those concerns
remain implemented and owned by FS.GG.Governance. Typed SDD supplies stable normative subjects, declared
evidence obligations, lifecycle facts, and a versioned binding manifest that lets Governance apply its full
pipeline without importing SDD code or redefining SDD meaning.

This is deliberately **not part of the current Typed SDD implementation**. No constitution AST, migration,
compiler surface, provider value, or Governance contract described here may be added to P0–P4. Work may
begin only after P4 has published and proven the base kernel, extension contract, normalization,
provenance, authoring receipts, migration framework, and supported `typed-sdd` lifecycle. This document
must not force the pilot to design a constitutional extension before the generic substrate is stable.

P5—the separate decision to make Typed SDD the omitted-value default—does not silently absorb this work.
After P4, maintainers must explicitly choose one of two sequences:

1. implement and soak the constitution plus complete Governance integration before the P5 default flip; or
2. complete P5 with the then-current Typed SDD constitution posture and introduce this feature afterward.

Either sequence requires its own feature lifecycle, cross-repository issues, compatibility decisions,
published artifacts, and acceptance evidence. This design expresses the intended architecture, not
authorization to widen the active roadmap.

The typed specification owns **what a product requires, why, and what evidence is owed**.
FS.GG.Governance remains an optional adjudication system that owns **how facts are sensed, which checks are
selected, who or what is competent to decide, whether evidence is reusable, and at which boundary a result
blocks**. Governance rule implementations and operational policies are referenced through stable, versioned
identities; their predicates and execution policy are not copied into FS.GG.SDD. Standard SDD retains its
authored Markdown constitution. Freeform (`none`) retains no FS-GG lifecycle constitution.

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

The answer is not to put all governance behavior inside FS.GG.SDD. Nor is it enough to add a narrow
constitution-to-check bridge. The answer is to give normative meaning one typed home and define an explicit
integration contract through which the complete Governance pipeline can sense, route, adjudicate, capture,
enforce, ship, release, and explain that meaning.

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

### Existing complete Governance pipeline

The constitution profile sits inside a substantially larger implemented system. The current Governance
architecture is a typed, mostly pure pipeline with explicit host edges:

```text
strict .fsgg configuration + inherited reference profiles
                         |
                         v
                 typed project facts
                         +---------- external adapters and sensed facts
                         |
                         v
        changed-path routing and finding classification
                         |
                         v
            gate registry and route selection
                         |
                         v
    freshness, reuse, cache eligibility, and cost budget
                         |
                         v
       gate execution + deterministic/agent/human review
                         |
                         v
          evidence capture, provenance, attestations
                         |
                         v
             mode/profile enforcement decision
                         |
                         v
              ship and release rollups
                         |
                         v
       route/gates/evidence/audit/release projections
```

The configuration boundary is currently the strict, versioned four-file `.fsgg` contract:
`governance.yml` supplies project facts, `policy.yml` supplies profiles and branch/review policy,
`capabilities.yml` supplies paths, surfaces, domains, and check declarations, and `tooling.yml` supplies the
allowlisted command catalog and external-tool requirements. Invalid configuration produces diagnostics and
no partial typed model. The configuration is operational policy, not an informal shadow of the
constitution.

Governance then performs several different jobs that must not be collapsed into “run constitutional
checks”:

- sensing turns repository, project, SDD handoff, release, freshness, tool, and adapter observations into
  typed facts at explicit I/O boundaries;
- routing maps normalized changed paths to capability domains, reports ambiguity and unclassified governed
  paths, builds a stable gate registry, and selects a deduplicated route with reasons and declared cost;
- adjudication evaluates reified `Check<'fact>` values (`Atom`, `All`, `Any`, `Not`, `Implies`, or explicitly
  opaque) using three-valued `Pass`, `Fail`, and `Uncertain` semantics and records what every check reads;
- competency separates deterministic checks from agent-reviewed and human-only decisions instead of
  pretending all policy is machine-decidable;
- evidence machinery captures outcomes and provenance, distinguishes real, synthetic, failed, skipped, and
  pending evidence, computes freshness and reuse identities, and prevents stale or tainted evidence from
  masquerading as a fresh result;
- operational controls apply review and cost budgets, cache eligibility, command allowlists, timeouts,
  environment classes, maturity, severity, execution modes, profiles, and non-lowerable inherited floors;
- enforcement keeps base severity visible while deriving an effective advisory or blocking posture for the
  selected mode/profile;
- ship and release assemble whole-change and publication decisions from already-typed results, including
  package evidence, release preconditions, attestations, and exit-code basis; and
- versioned route, gate, evidence, generated-view, audit, and release projections provide stable contracts
  for CI, branch protection, agents, generated readiness views, and humans.

Typed SDD integration is incomplete unless it gives every one of these stages an explicit source,
contract, or “Governance-only” boundary. The existing implementation remains the reference behavior; this
design does not replace it with a speculative second governance kernel in FS.GG.SDD.

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
| Governance binding from normative/evidence nodes to operational subjects | Versioned SDD-produced integration manifest, validated by Governance |
| Governance configuration schema and typed operational facts | FS.GG.Governance configuration contracts |
| Rule/check algebra, rule implementation, and adapter catalog | FS.GG.Governance kernel and rule packs |
| Repository, project, release, freshness, and external sensing | FS.GG.Governance sensing/adapters at the host edge |
| Changed-path classification, gate registry, route, and route rationale | FS.GG.Governance routing/gates pipeline |
| Check competency, maturity, base severity, mode, profile, and boundary | Governance policy/profile and inherited reference floor |
| Cost/review budget, command allowlist, environment, and timeout | Governance policy/tooling configuration |
| Evidence capture, provenance, taint, freshness, reuse, and cache eligibility | FS.GG.Governance evidence pipeline |
| Agent and human review records | Governance review contracts and approved reviewers |
| Effective enforcement decision | FS.GG.Governance enforcement fold |
| Whole-change ship decision and publication decision | FS.GG.Governance ship/release folds |
| Attestation, audit, route, gate, evidence, and release wire formats | FS.GG.Governance versioned projections |
| External facts | Their source authority, represented as typed observations |
| Lifecycle readiness | FS.GG.SDD readiness model |
| Cross-repository rollout and versions | ADRs, dependency registry, and Coordination board |
| Human-readable constitution | Generated Markdown projection for Typed SDD |

No row may be silently answered by the authority in another row. In particular, a blocking Governance
verdict does not amend a constitutional principle; a constitutional compiler success does not prove that
an external check passed; an SDD evidence obligation does not decide freshness or cache eligibility; and a
Governance profile cannot weaken an inherited constitutional obligation.

## Architecture

```text
agent-authored Typed SDD source
  requirements + constitution + evidence declarations
                         |
                         v
              compiler and normalizer
                         |
        +----------------+------------------+
        |                |                  |
        v                v                  v
 normalized model   human projections   ratification/readiness
 and digest         and semantic diff    receipts
        |
        v
 versioned Governance integration manifest
 normative subjects + applicability + evidence subjects + SDD facts
        |
        v
 optional FS.GG.Governance adapter
        |
        +--> compose strict .fsgg config and inherited profiles
        +--> sense repository/project/release/external facts
        +--> route changed paths and select gates
        +--> evaluate reified checks at declared competency tier
        +--> resolve freshness/reuse/cache/cost and capture evidence
        +--> derive effective enforcement for mode/profile
        +--> roll up ship and release decisions
        +--> emit provenance, attestation, audit, and explanations
```

The constitutional model and governance integration declaration are extensions of the shared
`SpecificationModel`, not a separate compiler or root document format. Product requirements may reference
constitutional nodes. Constitutional nodes may refer to registered evidence contracts, but must not depend
on a product requirement whose validity they are supposed to govern; the compiler rejects that cycle.
Bindings to Governance are compiled after normative normalization so operational configuration never
changes the specification digest.

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

## Governance integration extension model

The post-P4 spike should model a small typed integration declaration alongside the constitution. It is a
binding layer, not a copy of Governance configuration or the Governance rule EDSL. The conceptual normalized
shape is:

```fsharp
type GovernanceContractVersion = private GovernanceContractVersion of string
type GovernanceSubjectId = private GovernanceSubjectId of NodeId
type GovernanceRuleRef =
    { Package: PackageIdentity
      Profile: ProfileId option
      Rule: RuleId
      CompatibleVersions: VersionRange }

type EvidenceSubjectBinding =
    { Obligation: ObligationId
      Subject: GovernanceSubjectId
      RequiredEvidence: EvidenceContractRef list }

type GovernanceRuleBinding =
    { Subject: GovernanceSubjectId
      ConstitutionalObligation: ObligationId option
      Requirement: RequirementId option
      Rule: GovernanceRuleRef
      AppliesWhen: Applicability
      Coverage: Required | Supplemental }

type SddFactExport =
    { Fact: FactContractRef
      SourceNode: NodeId
      Freshness: FreshnessDeclaration
      Sensitivity: DataClassification }

type GovernanceIntegrationModel =
    { ContractVersion: GovernanceContractVersion
      Constitution: ConstitutionRef
      Subjects: GovernanceSubjectId list
      EvidenceSubjects: EvidenceSubjectBinding list
      RuleBindings: GovernanceRuleBinding list
      ExportedFacts: SddFactExport list
      ExpectedProfiles: ProfileRef list
      RequiredCapabilities: CapabilityContractRef list }
```

These names are illustrative until they can be reconciled with the published P4 extension vocabulary and
the installed Governance API. The semantic boundary is firm:

- the integration model may name a normative node, evidence subject, lifecycle fact, published profile, or
  external rule contract;
- it may declare whether a binding is required for coverage or supplemental enforcement;
- it may declare the minimum data contract and freshness expectation for an SDD-produced fact;
- it may not encode a `Check<'fact>` predicate, path glob, command line, timeout, cost, maturity, severity,
  mode, cache result, reviewer verdict, ship decision, or release decision; and
- it may not claim that a referenced rule exists or passed. Governance validates resolution and produces
  that evidence.

The normalized SDD model has two related digests. The **normative digest** covers requirements,
constitution, evidence obligations, and their meaning. The **integration digest** additionally covers the
binding manifest and external contract ranges. Changing a Governance binding invalidates integration
evidence but is not automatically a constitutional amendment. Changing the obligation to which it binds
changes the normative digest and follows constitutional amendment rules.

## End-to-end Governance incorporation

The following matrix is the required architecture contract. A later implementation plan must not reduce
it to the constitution-profile row.

| Governance machinery | Typed SDD supplies | Governance retains | Integrated output/behavior |
|---|---|---|---|
| Strict configuration | Lifecycle identity, integration contract version, expected capabilities/profiles | Four-file schema, validation, normalized operational facts, unknown-field and dangling-reference diagnostics | One validation report that distinguishes malformed SDD handoff, malformed Governance config, and incompatible versions |
| Inheritance/reference profiles | Optional compatible profile references and normative obligations that must not be weakened | Embedded organization floors, profile composition, collision checks, and “local may raise, never lower” policy | Effective policy explains inherited and local sources independently of the constitution digest |
| Adapters and sensing | Versioned SDD facts with source node, digest, freshness declaration, and sensitivity | Adapter SPI, built-in adapters, filesystem/git/project/tool/release sensing, SDD-handoff adapter, and I/O isolation | Typed fact assertions with source authority and provenance; no Governance parsing of SDD Markdown/F# |
| Path/capability routing | Requirement/surface identities when available as useful annotations | Governed root, path maps/globs, domains, ambiguity rules, unmatched-path findings, and deterministic precedence | Route traces may cite SDD subjects but remain valid for non-SDD paths and checks |
| Gate registry and selection | Stable rule references and coverage requirement | Check/tooling catalog resolution, stable gate identity, prerequisites, timeout, product checks, route selection, and cost rollup | Each selected gate explains both the repository route and any bound normative subject |
| Rule EDSL/kernel | Normative/evidence subjects and typed input contracts | `Check<'fact>` algebra, fixed-point inference, three-valued verdict, `eval`/`render`/`hash`/`explain`/`reads`, provenance, and opaque-rule restrictions | A verdict cites the exact rule implementation, facts read, and SDD subjects; no predicate duplication in SDD |
| Competency and review | Human-decision obligations where the specification requires judgement | Deterministic, agent-reviewed, and human-only tiers; prompt isolation; review keys, review budgets, model/reviewer records | `Uncertain` remains explicit until the declared competent authority supplies a valid record |
| Calibration and advisory promotion | No model mutation; optional evidence subjects for calibration outcomes | Calibration samples, measured confidence, advisory-promotion criteria, and policy-owned maturity transitions | A promotion changes Governance policy/version and evidence, not the normative digest |
| Gate execution | Nothing executable beyond stable declared external contract references | Command allowlist, environment class, timeout, process host, execution disposition and outcome | Execution records identify selected gate, command contract, input identity, outcome, and provenance |
| Evidence capture and taint | Evidence obligation ID, subject ID, expected kind, and SDD-owned structural receipts | Capture loop, real/synthetic/pending/failed/skipped states, provenance graph, mutation/producer/render receipts, and taint propagation | Evidence is attached to normative subjects without converting “artifact exists” into “obligation satisfied” |
| Freshness and reuse | Source model/integration digests and declared fact freshness expectations | Freshness-key construction/resolution/sensing, content identities, evidence reuse store, cache eligibility, and invalidation policy | Reuse proves that exact relevant inputs and contracts match; stale SDD projections or bindings force recomputation |
| Cost and capacity | At most a declared lifecycle urgency or required boundary, never numeric execution policy | Check cost classes, route rollup, cost budget, review budget, cache controls, and no-hide findings | Planning can display expected Governance cost while Governance alone accepts/refuses a budgeted route |
| Maturity/severity/mode/profile | Normative force and any constitutionally required boundary as an invariant | Advisory/blocking severity, maturity, six run modes, four enforcement profiles, branch policy, and effective-severity derivation | Enforcement explains base and effective severity; relaxation cannot hide the underlying result or weaken a constitutional invariant |
| Ship | Exact normative and integration digests, ratification/readiness receipts, and SDD-owned evidence status | Whole-change rollup, blockers/warnings/passing partition, exit-code basis, cache/execution/generated-view additions | Ship output binds the decision to the exact Typed SDD model but is still computable for Standard SDD and non-SDD consumers |
| Release | Package/release declarations and SDD readiness evidence where applicable | Release sensing, semantic-version rules, package evidence, publication preconditions, attestation summary, release decision/report | Release distinguishes advisory verify preview from blocking release and cites the model used |
| Audit and explanation | Node titles and stable references suitable for human projection | Route/gates/evidence/attestation/audit/release schemas, deterministic JSON, human text, provenance and generated views | Humans and machines can traverse model node → binding → route → check → evidence → enforcement → ship/release decision |
| Snapshots and generated-view currency | Canonical source/projection identities and expected producer contract | Snapshot comparison, generated-view declarations, currency sensing/enforcement, refresh commands, and no-clobber behavior | Stale derived views are visible findings and never silently become authority |
| Host and CLI | No replacement CLI requirement; optional links from SDD commands to Governance artifacts | `fsgg` command parsing, sense/plan/act loops, filesystem/process/network edges, exit codes, and output paths | Tools compose through versioned artifacts; neither repository imports the other's host implementation |

### Configuration and projection posture

The initial feature must keep the four Governance YAML files canonical for operational policy. Generating
all of them from Typed SDD would improperly make SDD the owner of routing, commands, cost, severity, and
branch enforcement. Conversely, Governance must not infer normative obligations by reading generated
`.fsgg/constitution.md`.

One narrow projection is allowed: a Governance-owned resolver may materialize or refresh generated regions
that are already derivable from a published Governance profile and the integration manifest. The generated
region must name its producer, input digests, schema version, and regeneration command. Hand-authored
routing, local checks, policy, and tooling remain untouched. Deleting the projection must not delete an
embedded inherited floor.

After this integration has soaked, Governance may independently adopt the shared Typed SDD extension
substrate for a typed authoring EDSL for its own configuration. If it does, Governance still owns that AST,
compiler extension, compatibility policy, and YAML projections. It is a separate feature and must not be a
hidden prerequisite of this design.

### Verdict and satisfaction semantics

Constitutional applicability, evidence state, Governance verdict, and enforcement outcome are distinct:

```text
obligation applicability:  not-applicable | applicable | unresolved
evidence state:            pending | real | synthetic | failed | skipped | auto-synthetic
check verdict:             pass | fail | uncertain
effective enforcement:     advisory | blocking
whole-change decision:     pass | fail with explicit exit-code basis
```

No projection may flatten these into one Boolean “compliant” field. A passing check with stale evidence is
not satisfied. A synthetic receipt remains visibly synthetic. An `Uncertain` agent/human check cannot be
treated as `Pass`. An advisory failure remains a failure even when it does not block the current mode. An
unbound required obligation is a coverage failure, not a successful empty route.

### Optionality and non-SDD operation

FS.GG.Governance must continue to govern Standard SDD, Freeform, and repositories with no FS.GG.SDD
installation. Its configuration, adapters, route, checks, evidence, enforcement, ship, release, and audit
contracts therefore cannot require a `GovernanceIntegrationModel`. When no typed handoff exists, the SDD
adapter reports “not supplied” and the rest of Governance operates from its other facts and policy.

Typed SDD likewise remains usable without Governance. It compiles normative meaning, projects the
constitution, validates its own structural obligations, and emits readiness. For a declared external
Governance binding it reports `not configured`, `unavailable`, or `unbound` according to the binding's
coverage semantics; it never fabricates a Governance pass.

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

For future Quint-backed Typed SDD:

- canonical Quint source is agent-authored through the normal authoring capability;
- the generated compiled contract and digest are the comparison/interchange form, while behavioral
  meaning remains in Quint;
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

The typed specification and Governance answer different questions:

| Typed specification/constitution | Governance |
|---|---|
| What is required, forbidden, permitted, or recommended? | How is it sensed or reviewed? |
| To which changes or surfaces does the obligation apply? | Which repository paths and environments invoke the check? |
| What evidence is owed and which subject owes it? | Which adapter, command, review, or reused receipt obtains and validates that evidence? |
| Which stable constitutional node explains the requirement? | Which rule/finding identity reports the verdict? |
| What amendment changed the meaning? | At which mode/profile does failure block, and what ship/release decision follows? |

The boundary is the `GovernanceIntegrationModel` and its versioned handoff projection defined above. It
cannot restate the constitutional statement or precompute a verdict. Governance retains its own kernel,
configuration, `CheckTier`, maturity, severity, cost, environment, routing, execution, inheritance,
evidence, freshness, review, enforcement, release, and explanation behavior.

### Reusing the existing F# constitution profile

The existing `fsharp-constitution` profile becomes the first integration candidate after activation:

- each existing Governance rule identity maps to one constitutional obligation or to a declared
  input-state diagnostic;
- the constitutional extension references the published Governance profile/contract version;
- Governance's `SurfaceChecks.Profile` remains the authority for pack implementation, maturity, sensing,
  and finding normalization;
- the generated `capabilities.yml` region remains a Governance distribution projection; and
- a cross-repository compatibility fixture proves that every bound obligation resolves to the expected
  Governance identity without copying the rule inventory into SDD.

The integration must expose honest partial coverage. The existing four packs do not satisfy the whole
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

Installing Governance adds the complete operational pipeline described above. It does not change the
constitution's normalized meaning.

## Lifecycle integration

The typed model and its Governance bindings participate in the existing lifecycle without adding a stage:

| Stage | Typed SDD behavior | Governance relationship |
|---|---|---|
| Charter | Select effective constitution and record model/digest/status | Resolve expected Governance contract/profile compatibility without requiring installation |
| Specify | Classify requirements against constitutional applicability | Declare governance subjects, fact exports, and evidence obligations by stable ID |
| Clarify | Surface unresolved constitutional ambiguity one material choice at a time | Expose bindings whose applicability or competent reviewer cannot be derived |
| Checklist | Derive applicable obligations and required evidence | Report required, supplemental, human, and currently unbound coverage separately |
| Plan | Explain compliance, exceptions, migrations, and public-surface consequences by node ID | Show routes/costs only from a Governance-produced plan; SDD does not estimate them |
| Tasks | Ensure required obligations have owned implementation/evidence tasks | Create execution/review tasks for selected gates without copying commands into the spec |
| Analyze | Refuse missing, contradictory, stale, or unbound mandatory obligations | Validate manifest/config/profile resolution and route completeness |
| Evidence | Attach SDD-owned receipts to obligation IDs | Capture external results, provenance, review records, freshness, reuse, and taint against the same subjects |
| Verify | Recompile, check projection freshness, and replay bindings | Sense, route, run/reuse gates, enforce in `Verify`, and emit advisory release preview where configured |
| Ship | Bind exact normative/integration digests into readiness and handoff | Enforce in `Gate`/`Release`; emit ship, audit, attestation, and release artifacts |

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
- Governance configuration invalid or incompatible with the manifest;
- required profile, adapter, rule, gate, command, or external tool unavailable;
- route ambiguity, unmatched governed path, or required subject selecting no gate;
- Governance read/check indeterminate;
- external observation stale or incomplete;
- required agent/human review missing, over budget, or bound to different inputs;
- evidence synthetic, tainted, stale, or ineligible for reuse;
- cost or review budget exceeded;
- inherited policy floor conflict;
- gate execution failed, timed out, or was not executed;
- stale generated Governance view;
- ship decision blocked; and
- release precondition, package evidence, or attestation incomplete.

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

### Governance integration properties

- every declared rule binding resolves both a constitutional obligation and a published Governance rule;
- an empty binding census is a refusal, not complete coverage;
- unknown Governance rule identities remain malformed/unbound rather than fabricated violations;
- Governance maturity or severity changes do not change the constitutional model digest;
- constitutional meaning changes do change its digest and invalidate stale bindings when required;
- deleting Governance configuration cannot lower an inherited Governance floor; and
- Typed SDD without Governance still completes its supported lifecycle path.

### Governance pipeline properties

- strict configuration and handoff validation produces no partial integrated model;
- adapter and sensing tests prove authority, normalization, provenance, and I/O isolation;
- path routing and gate selection remain deterministic under irrelevant declaration/input ordering;
- every selected gate explains its path/domain reason and any bound SDD subject;
- reified checks preserve `Pass`/`Fail`/`Uncertain`, reads, hash, render, and explanation equivalence;
- deterministic, agent-reviewed, and human-only checks cannot substitute for one another;
- execution occurs only through the allowlisted command/environment/timeout contract;
- evidence state, taint, provenance, freshness, reuse, and cache eligibility survive projection round trips;
- stale normative or integration digests invalidate reuse without changing the recorded prior outcome;
- cost and review budgets refuse or diagnose work without hiding required checks;
- inherited policy floors cannot be lowered by local config, generated-region deletion, mode, or profile;
- effective severity preserves base severity and reason in every projection;
- ship and release reports carry upstream decisions verbatim rather than recomputing them from rendered rows;
- verify remains advisory for release readiness where the Governance contract declares it advisory; and
- Governance without Typed SDD remains fully supported by the same regression suite.

### Projection and guidance properties

- Markdown, agent guidance, readiness, and handoff artifacts carry constitution identity and digest;
- a hand edit to a projection is stale and regenerates from the AST;
- Claude and Codex guidance derive equivalent constitutional obligations; and
- generated content never claims a draft is ratified or an unavailable check passed.

## Cross-repository ownership and rollout

| Repository | Future responsibility |
|---|---|
| S.I.R. | No new responsibility; supplies evidence about the generic extension substrate only |
| FS.GG.SDD | Constitution and integration extension types/compiler, generic base model, authoring/migration skills, projections, readiness and handoff producer |
| FS.GG.Governance | Handoff adapter; config/sensing/routing/gates/kernel/execution/evidence/enforcement/ship/release/audit owner; existing profile mapping; compatibility fixtures |
| FS.GG.Templates | Compose published lifecycle/provider support only after producer publication |
| Product repositories | Own local constitutional extensions, human ratification, product-specific evidence and adoption decision |
| `.github` | Cross-repository ADR, registry contracts, sequencing, compatibility and rollout evidence |

The handoff is a versioned data contract. FS.GG.SDD is its producer and publishes the old/new semantics,
schema version, normalized fixtures, and supported compatibility range before FS.GG.Governance adopts it.
Governance must accept the prior handoff during the declared compatibility window or fail with a precise
unsupported-version diagnostic. Additive fields require tolerant older-consumer behavior only where the
published schema promises it; renamed identities, changed normalization, or altered verdict meaning are
breaking changes. Governance-owned audit/release schemas follow Governance's own version policy and do not
wait on an SDD package release unless their shape actually consumes the new contract.

Producer publication precedes consumer adoption, and Governance publication precedes template/product
adoption of enforcement that depends on it. No source-project reference, local package shortcut, or shared
working-tree dependency may satisfy a rollout acceptance condition. The dependency registry records the
handoff version range and consumer minimum. A cross-repository ADR records the durable authority split,
compatibility window, and retirement criteria before activation.

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

### C3 — Governance integration contract

- Publish the normative-subject, evidence-subject, fact-export, rule-binding, and compatibility contract
  from SDD.
- Version the handoff; publish old/new semantics, normalized fixtures, and supported consumer ranges.
- Extend the Governance SDD adapter to validate and lift the handoff into Governance-owned facts without
  importing SDD implementation code.
- Map Governance's existing `fsharp-constitution` rules without copying its inventory into SDD.

Exit: configuration and adapter validation resolve subjects, profiles, rules, and facts by stable identity;
absence of Governance still preserves the Typed SDD lifecycle.

### C4 — Routing, checks, and execution

- Carry SDD subject references through facts, findings, gates, selected routes, and explanations.
- Prove path routing and non-SDD gates remain behaviorally unchanged.
- Bind reified deterministic/agent/human checks and preserve three-valued semantics.
- Wire selected gates through the existing allowlisted execution boundary and review-record contracts.

Exit: a representative change explains model node → binding → path/domain → gate → check/review → outcome,
with no duplicated predicate or command in SDD.

### C5 — Evidence, enforcement, ship, and release

- Bind evidence capture, provenance, taint, freshness, reuse, cache eligibility, and cost/review budgets to
  stable subjects and exact normative/integration digests.
- Preserve inherited floors, base/effective severity, mode/profile behavior, and the local-only escape hatch.
- Add the digests and subject links to Governance-owned ship, attestation, audit, generated-view, and release
  projections with explicit schema/version treatment.
- Prove Standard SDD and non-SDD Governance workflows remain supported.

Exit: the complete Governance pipeline reaches a deterministic whole-change and publication decision whose
evidence can be traced to the exact Typed SDD model, without altering that model's meaning.

### C6 — Consumer soak

- Migrate representative unmodified, customized, ratified, unratified, governed, and ungoverned products.
- Measure authoring questions, ambiguous clauses, binding gaps, projection drift, and rollback use.
- Remove compatibility shadows after each accepted cutover.

Exit: the feature demonstrates bounded authoring friction, no silent migration, no second constitutional
authority, and no Governance dependency for ordinary Typed SDD operation.

### C7 — Adoption decision

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

### Integrate only the constitution profile

This gives shared obligation/check identities but leaves configuration, adapters, route selection,
competency, evidence lifecycle, inherited floors, ship, release, and audit disconnected from Typed SDD.
The result would still require humans and agents to correlate most governance outcomes by prose. Rejected
as incomplete; the profile is the first compatibility slice, not the final architecture.

### Move Governance into FS.GG.SDD

This would make optional enforcement a lifecycle dependency, duplicate or relocate a mature inference
kernel, and contradict the one-way handoff boundary. Rejected.

### Put Governance policy inside the constitutional AST

Severity, mode, route cost, environment, and competency answer operational enforcement questions. Folding
them into constitutional meaning would make a `warn`→`block-on-ship` rollout look like a constitutional
amendment and would prevent repositories from choosing an enforcement posture. Rejected; use versioned
bindings.

### Put the complete Governance AST inside `SpecificationModel`

This would make every path glob, command, timeout, maturity, profile, cache rule, and release condition part
of SDD normalization. It would prevent Governance from serving non-SDD consumers, couple release cadence,
and turn operational tuning into specification changes. Rejected. A future Governance-owned typed extension
may use the same substrate while retaining separate authority and digests.

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
- The complete route-to-release decision chain can cite the same stable normative/evidence subjects.
- Governance remains independently useful for Standard SDD, Freeform, and non-SDD repositories.
- Standard SDD and Freeform preserve their present semantics.
- Compiler self-change and constitutional ratification become visible, replayable operations.
- Customized constitutions migrate honestly instead of being silently overwritten or heuristically parsed.

### Costs and risks

- Constitutional concepts add a powerful extension that can become over-general or ceremonial.
- Structured applicability cannot remove every human judgement; pretending otherwise would create false
  determinism.
- The base/local composition and amendment rules require long-term compatibility discipline.
- Existing unratified and customized Markdown constitutions may require real human resolution.
- The Governance integration adds another published contract, schema compatibility window, and coordinated
  SDD → Governance → template/product release sequence.
- Carrying subject identities across every Governance projection increases fixture and migration surface.
- Separate normative and operational authorities require careful UI/projection design to remain legible.
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
- the integration cannot preserve Governance operation for non-SDD repositories;
- the route/check/evidence/enforcement/release chain cannot retain stable subject identity without invasive
  duplication of Governance's kernel;
- integration requires an operational policy change to alter the normative digest;
- existing Governance verdict, freshness, taint, severity, or release semantics would be flattened into a
  misleading Boolean;
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
   semantics, and the same binding model supports non-constitutional checks;
9. Governance absence does not prevent supported Typed SDD compilation, authoring, verification, or ship;
10. Governance presence composes strict configuration and inheritance, senses typed facts, routes paths,
    selects gates, evaluates deterministic/agent/human checks, and records explanations without changing
    constitutional meaning;
11. generated Markdown, guidance, readiness, and handoff artifacts carry the same constitution identity and
    fingerprint;
12. product-local inheritance is deterministic, conflict-refusing, and no-clobber;
13. evidence capture, provenance, taint, freshness, reuse, cache eligibility, execution, and cost/review
    budgets bind to exact normative/integration digests and remain distinguishable in projections;
14. inherited floors, modes, profiles, base/effective severity, ship rollup, release preconditions,
    attestations, audit, and generated-view currency retain their existing semantics;
15. Standard SDD and non-SDD Governance regression journeys remain supported;
16. installed-package producer/consumer fixtures pass in dependency order with no source-project shortcut;
17. representative governed and ungoverned consumers complete the full lifecycle and rollback controls; and
18. a separate adoption decision names whether the feature lands before or after P5 and the exact release
    identities that carry it.

## Decision still required after P4

This design recommends the constitutional AST plus the complete SDD/Governance integration boundary. It
intentionally does not decide whether that support must precede the Typed SDD default flip. That choice
should be made from P4 evidence:

- If the existing constitution posture creates a second semantic authority in real Typed SDD opt-in work,
  implement and soak C0–C6 before P5.
- If the posture is a bounded projection/bootstrap concern and migration risk dominates, complete P5 and
  deliver the constitution and Governance integration as a separately versioned successor.

In either case, the current Typed SDD implementation finishes first.

## Sources and existing contracts

- [Agent-authored F# specification kernel and canonical mutation algebra](2026-08-24-typed-protocol-kernel-design.md)
- [Specification and protocol kernel roadmap](../reports/2026-08-24-094348-typed-protocol-kernel-roadmap.md)
- [ADR-0004: SDD constitution ownership](../adr/0004-constitution-ownership-for-lifecycle-sdd-products.md)
- [ADR-0058: derive, do not restate](../adr/0058-adopt-one-governing-principle-derive-dont-restate.md)
- [ADR-0076: agent-authored specification kernel](../adr/0076-agent-authored-fsharp-specification-kernel.md)
- [ADR-0077: Quint-first specification authority](../adr/0077-quint-first-typed-specification-authority.md)
- [Quint-first Typed SDD migration design](2026-08-25-quint-first-typed-sdd-migration-design.md)
- [Current product constitution](../../.fsgg/constitution.md)
- [Current FS.GG architecture: SDD and Governance](../architecture.md)
- [FS.GG.Governance system design index](https://github.com/FS-GG/FS.GG.Governance/blob/main/docs/governance-design/index.md)
- [FS.GG.Governance inference kernel](https://github.com/FS-GG/FS.GG.Governance/blob/main/docs/governance-design/kernel.md)
- [FS.GG.Governance rule EDSL](https://github.com/FS-GG/FS.GG.Governance/blob/main/docs/governance-design/rule-edsl.md)
- [FS.GG.Governance routing and modes](https://github.com/FS-GG/FS.GG.Governance/blob/main/docs/governance-design/routing-and-modes.md)
- [FS.GG.Governance adapter and composition model](https://github.com/FS-GG/FS.GG.Governance/blob/main/docs/governance-design/adapters.md)
- [FS.GG.Governance evidence boundaries](https://github.com/FS-GG/FS.GG.Governance/blob/main/docs/governance-design/evidence-boundaries.md)
- [FS.GG.Governance planning boundary](https://github.com/FS-GG/FS.GG.Governance/blob/main/docs/governance-design/planning-and-optimization.md)
- [FS.GG.Governance decision 0012: composed F# constitution profile](https://github.com/FS-GG/FS.GG.Governance/blob/main/docs/decisions/0012-composed-fsharp-constitution-profile.md)
- [FS.GG.Governance reference constitution profile](https://github.com/FS-GG/FS.GG.Governance/blob/main/reference-gates/README.md)
- [FS.GG.SDD generic constitution content contract](https://github.com/FS-GG/FS.GG.SDD/blob/main/specs/033-skeleton-constitution/contracts/constitution-content.md)
