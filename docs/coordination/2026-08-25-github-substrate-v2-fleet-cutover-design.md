---
title: "Design: GitHub Substrate v2 and coordinated fleet cutover"
category: Design
categoryindex: 4
index: 25
description: "A typed, new-only GitHub authority model and fail-closed cutover for the FS-GG coordination fleet."
---

# Design: GitHub Substrate v2 and coordinated fleet cutover

This design makes GitHub Substrate v2 the first complete coordination-protocol extension over the
published FS.GG specification kernel. GitHub owns the facts it can represent natively; the typed
coordination model owns concurrency, process state, mutation plans, and evidence that GitHub does not
provide. A bridge release first teaches every old writer to honor a durable fleet epoch. The fleet then
freezes, installs and verifies v2 without normal writes, and crosses one explicit `OpenV2` point of no
return. Production becomes new-only after that receipt; historical v1 state is sealed and verifiable but
is not a permanent compatibility burden on the v2 runtime.

| Field | Value |
|---|---|
| Status | Proposed governing design for [`.github#2953`](https://github.com/FS-GG/.github/issues/2953) |
| Authored | 2026-08-25 |
| Program | [GitHub modernization Epic `.github#2952`](https://github.com/FS-GG/.github/issues/2952) |
| Execution spine | [Bootstrap/qualify `.github#2963`](https://github.com/FS-GG/.github/issues/2963), [bridge/ledger `.github#2964`](https://github.com/FS-GG/.github/issues/2964), [cutover/retire `.github#2965`](https://github.com/FS-GG/.github/issues/2965) |
| Execution roadmap | [GitHub Substrate v2 fleet-cutover roadmap](../github-substrate-v2-roadmap.md) |
| Maintainer direction | Coordinated fleet cutover; breaking changes are allowed when they remove the wrong authority or avoid permanent compatibility machinery |
| Builds on | [ADR-0076](../adr/0076-agent-authored-fsharp-specification-kernel.md), [typed protocol kernel design](2026-08-24-typed-protocol-kernel-design.md), [change-risk design](2026-08-22-coordination-change-risk-mitigation-design.md) |
| Preserves | Claim CAS and leases, touch-set exclusion, exact-SHA review/delivery evidence, semantic contract registry, and two-feed release recovery |

## 1. Evidence and implementation baseline

The generic specification substrate is no longer hypothetical. FS.GG.SDD `1.4.0-preview.1` publishes:

- `SpecificationModel<'extension>` and `ExtensionContract<'extension>`;
- deterministic validation, normalization, and fingerprints;
- JSON codecs and Markdown/JSON projections;
- semantic diff;
- typed evidence obligations and receipts;
- explicit `Migrated | Ambiguous | Unsupported` migration outcomes; and
- the additive `typed-sdd` lifecycle and authoring, inspection, migration, and rollback commands.

S.I.R. consumes the published generic model for a real rules corpus. The unimplemented portion is the
coordination-specific protocol extension: external authority observations, process state and events,
mutation algebra and interpreters, durable operation plans, compatibility/cutover epochs, and the
protocol-surface compiler gate. This is the correct seam for the GitHub design.

The current `.github` baseline also establishes the scale and the reason not to add another layer:

- protocol schema `fsgg.coord.protocol/12`;
- 54 CLI commands: 20 always-writing, six conditionally writing, and 28 read-only;
- 116 workflows and 18,545 lines of workflow YAML in `.github`;
- a 9,263-line `Client.fs`, 3,098-line `Reads.fs`, and 1,875-line `Writes.fs`;
- 75 numbered ADR files, a 529-line bespoke ADR parser, a handwritten index, and a handwritten
  supersession map;
- project-local and issue-body representations for facts GitHub now supports natively; and
- 84 open pull requests across the eight FS-GG repositories at the evidence reading, including many
  dependency-update PRs and several coordination-tool adoption PRs.

These counts are not themselves defects. They show that a design which adds adapters, compatibility
readers, workflows, or document projections without deleting their predecessors is a failed design.

### 1.1 Two filing-time contradictions are part of the corpus

Filing this Epic demonstrated two live representation defects:

1. the typed protocol explicitly admits `Paths: none` for an Epic, while `intake apply` rejects it because
   every intake path must exist in the repository; and
2. the documented intake vocabulary admits `capability`, while the live Project `Class` field contains
   only `decision`, `defect`, and `hardening`.

Neither was bypassed. They are evidence for the v2 rule: one compiled vocabulary, one authority binding,
and no production mutation whose value has not been validated against the observed native schema.

## 2. Goals and non-goals

### Goals

1. Give every coordination fact exactly one semantic authority.
2. Use native GitHub identity, type, hierarchy, dependency, issue-field, Project, ruleset, release, and
   security features where GitHub can own the fact completely.
3. Keep FS-GG-specific state only where GitHub lacks the required concurrency, evidence, semantic, or
   transaction contract.
4. Make all remote writes typed mutation plans with idempotency, preconditions, durable step receipts, and
   an explicit partial/indeterminate outcome.
5. Derive CLI commands, schemas, docs, skills, settings manifests, and workflow metadata from the compiled
   protocol model.
6. Cut the whole fleet to a new-only steady state without an interval in which v1 and v2 may both write.
7. Seal historical v1 evidence without forcing production v2 to support every old representation forever.
8. Reduce command, workflow, required-context, parser, projection, and exception surface measurably.

### Non-goals

- Replacing the GitHub issue/project substrate with a custom database or hosted workflow service.
- Replacing comment-order claim CAS, worker identities, leases, operation locks, or merge elections with
  assignees or Project columns.
- Moving semantic dependency compatibility out of `registry/dependencies.yml`.
- Pretending GitHub can make NuGet.org and GitHub Packages publication atomic.
- Event-sourcing every GitHub object or repository fact.
- Making Projects preview webhooks the only reconciliation mechanism.
- Forcing arbitrary narrative into closed enums merely so that it appears typed.
- Blocking finding intake because the protocol model does not yet contain the newly discovered concept.

## 3. Governing principles

### 3.1 Authority, observation, process, and projection are different kinds

Every surface is classified as exactly one of:

- **native authority** — GitHub owns the value and its identity;
- **registry authority** — a reviewed repository document owns semantic cross-repository intent;
- **protocol authority** — an append-only typed event/receipt stream owns a state GitHub cannot represent
  safely;
- **observation** — revision-bound evidence read from one of those authorities; or
- **projection** — replaceable human or machine output derived from an authority.

A Project field, issue-body line, comment marker, JSON field, and CLI string may not all independently
represent the same fact. A projection carries the source fingerprint that produced it and never licenses a
write when it is stale or unreadable.

### 3.2 Native does not mean automatically authoritative

GitHub is authoritative only when its native semantics meet the contract. Native dependency edges do.
Assignees do not provide a multi-agent claim CAS. A text field does not provide atomic touch-set mutation.
A run conclusion does not prove a multi-feed release. The test is semantic, not whether an API exists.

### 3.3 User intent and derived lifecycle state are separate

Humans and policy supply scheduling intent. Claims, blockers, PRs, reviews, merges, and completion receipts
produce lifecycle facts. A pure reducer derives Project Status. No operator and no reconciler hand-writes a
derived status as a durable decision.

### 3.4 New-only means one writer, not no preparation

The new schema may be created and backfilled while v1 remains authoritative. The v2 engine may compare
read-only decisions. Neither is a second production writer. At the cutover, all normal writers are frozen;
v2 is installed and verified; `OpenV2` enables only v2. The old writer never resumes afterward.

### 3.5 Irreversibility is a state transition

The cutover has a named point of no return. Before `OpenV2`, rollback restores the bridge runtime and old
authority snapshot. After `OpenV2`, new-only events may exist and recovery is roll-forward. No runbook may
promise rollback across that boundary unless an executable, tested down-migration exists.

## 4. Required additions to the coordination protocol extension

The published generic specification envelope should remain small. The following concepts belong in the
unimplemented coordination extension, not in the generic kernel.

The code below is a semantic sketch, not a frozen public API:

```fsharp
type AuthorityKind =
    | GitHubNative
    | RepositoryRegistry
    | ProtocolStream
    | ExternalService

type AuthorityBinding =
    { Id: SpecificationId
      Kind: AuthorityKind
      SubjectKind: SpecificationId
      CodecId: SpecificationId
      RevisionModelId: SpecificationId
      CompletenessModelId: SpecificationId }

type Observation<'fact> =
    | Observed of value: 'fact * revision: Revision * evidence: EvidenceRef
    | Absent of revision: Revision * completeness: CompletenessProof
    | Contradictory of evidence: EvidenceRef list
    | Unreadable of ObservationFailure

type ProjectionBinding =
    { Id: SpecificationId
      SourceAuthority: SpecificationId
      Target: ExternalSubject
      SourceFingerprint: string
      WritePolicy: ProjectionWritePolicy }
```

The important additions are described below.

### 4.1 Authority binding

Every registered fact declares its owner, subject identity, codec, revision model, and completeness model.
This prevents a failed read, a truncated connection, an omitted field, and a genuine absence from becoming
the same value. It generalizes the strongest existing `IoResult`, page-completeness, ETag, and field-revision
work instead of replacing it.

### 4.2 Completeness proof

`Absent` is legal only when the adapter proves it read the complete relevant domain. A paginated read names
the terminal cursor or total-count agreement. A webhook-derived view names its cursor plus the last full
audit that bounds missing delivery. No `list |> isEmpty` result may become absence without this proof.

### 4.3 Native relation algebra

Hierarchy and dependencies are sets of typed native edges:

```fsharp
type NativeRelationKind = ParentChild | Blocks
type NativeRelation =
    { Kind: NativeRelationKind
      From: IssueRef
      To: IssueRef }

type RelationMutation = AddEdge of NativeRelation | RemoveEdge of NativeRelation
```

Scalar replacement is not a relation mutation. Add/remove operations re-read the current edge set and
verify the intended post-state. The GraphQL/REST implementation detail is isolated in the GitHub
interpreter.

### 4.4 Intent and lifecycle

`LifecycleProjection.SchedulingIntent` is the starting point. V2 makes the input channel explicit and
native while keeping Project Status derived:

- native issue field `FS-GG Scheduling`: `Backlog | Ready | Parked | Deferred`;
- native issue field `FS-GG Hold reason`: `Human decision | Human action | External`, required only when
  scheduling is `Parked`;
- native dependencies supply implementation blockers;
- protocol observations supply claims, PR/review state, delivery receipts, and issue state; and
- the reducer alone projects Project Status.

The existing `Blocked on:` body sentinel retires. It is migrated into scheduling plus hold reason before
v2 opens.

### 4.5 Protocol event streams

Typed event streams remain for state that requires server ordering, CAS-like election, or durable
evidence:

- item claim, lease, release, and adoption;
- active touch-set changes bound to claim generation;
- operation-lock grants and merge elections;
- review/repair decisions and exact-head acceptance;
- delivery and done receipts;
- durable multi-step operation receipts.

GitHub issue comments remain the operational transport initially because GitHub assigns their identity and
order, they are independently readable, and the existing CAS/recovery behavior is already proven. They are
not an immutable archive: comments may be edited or deleted. Every payload is therefore versioned JSON in a
marker envelope with sequence, predecessor digest, subject, generation, and model fingerprint. Prose is a
generated explanation, never the parser input.

The extension classifies each stream's retention contract. Ephemeral coordination streams, such as released
claims, may compact under an explicit terminal receipt. Durable decisions, delivery evidence, and operation
receipts are hash-chained and checkpointed into a content-addressed repository or immutable release artifact.
A missing or rewritten comment is then detectable and cannot silently rewrite audit history. The global
cutover epoch uses the stronger repository ledger in section 4.9 and is not comment-authoritative.

### 4.6 Mutation algebra and durable plans

The current distinction between `Set` and `Clear`, partial GraphQL mutations, comment verification, and
release sagas becomes one registered mutation vocabulary:

- `Create`, `Append`, `AddEdge`, `RemoveEdge`, `Set`, `Clear`, `Transition`, and `Compensate`;
- one registered interpreter per authority;
- expected revision or idempotency identity on every write;
- `Applied`, `AlreadyApplied`, `RefusedBeforeWrite`, `Stale`, `Partial`, and `Indeterminate` outcomes;
- a receipt persisted and re-read before the next step; and
- explicit roll-forward once an irreversible step executes.

Generic public `set-field`, raw GraphQL mutation, and direct body-rewrite commands retire from the normal CLI
surface. A narrowly scoped repair command may invoke the same typed interpreter with an explicit repair
plan, authority, reason, and receipt.

### 4.7 Cost-aware observation plans

Cost is part of the adapter contract, not scattered advice. Each read plan declares:

- REST, GraphQL, search, or webhook-cursor budget class;
- pagination/completeness strategy;
- cache eligibility and freshness;
- maximum fan-out and concurrency;
- a cheaper narrow-subject route where one exists; and
- a refusal policy when the plan cannot complete inside budget.

The compiler can then reject a hot-path command which calls a full-board plan or an event handler which
silently widens into a fleet scan. Measurements update plan metadata; they do not change domain truth.

### 4.8 Desired GitHub state

The protocol extension also models GitHub configuration desired state:

- organization issue types and issue fields;
- repository custom-property schema and values;
- Project fields, views, workflows, visibility, and membership policy;
- repository rulesets, merge methods, auto-merge/queue posture, Actions policy, and branch deletion;
- reusable-workflow/action pin policy;
- release environments, immutable releases, tag protection, and trusted-publisher bindings; and
- vulnerability, secret, dependency, SBOM, and attestation posture.

The model derives an `inspect -> plan -> apply -> verify` reconciler. Unsupported plan or permission states
are typed outcomes. Repository settings are no longer an undocumented manual precondition or a hand-kept
shell table.

### 4.9 Cutover epoch

The global epoch is a protocol state machine stored in a content-addressed Git ledger on a dedicated,
ruleset-protected cutover ref and bound to the exact cutover manifest. Each transition is an expected-parent
commit; a protected, non-deletable phase tag anchors the accepted commit. The GitHub App is the sole normal
writer and its bypass is limited to the protected `fleet-cutover` environment. A dedicated cutover-control
issue projects the current state, evidence, and operator guidance for humans, but it is not authority:

```text
OperatingV1
    -> Preparing(manifest)
    -> FreezeRequested(manifest)
    -> Frozen(snapshot)
    -> SwitchedV2(candidate)
    -> VerifiedV2(evidence)
    -> OpenV2(acceptance)       # point of no return
    -> RetiringV1(deletion)
    -> OperatingV2(report)

Before OpenV2 only:
    Preparing | FreezeRequested | Frozen | SwitchedV2 | VerifiedV2
        -> RollingBack(reason)
        -> OperatingV1(recovery)
```

Every v1 bridge writer and every v2 writer reads the ledger ref fresh before a mutation, verifies its commit
ancestry, manifest fingerprint, phase tag, and transition legality, and fails closed on an unreadable or
contradictory result. During any frozen or switching state, normal writes refuse. Cutover mutations require
the manifest identity, current epoch commit, protected environment approval, and the dedicated cutover
operation grant. The issue projection is regenerated only after the authoritative transition re-reads
successfully.

### 4.10 Sealed legacy history

V2 production does not carry permanent upcasters for all v1 history. The cutover classifies v1 state as:

- **live and migrated** — required to continue an open operation in v2;
- **live and drained** — operation completed or deliberately released before freeze;
- **historical and sealed** — preserved with source, schema, bytes, digests, and the exact verifier artifact;
  or
- **invalid and explicitly disposed** — never silently omitted, with issue and rationale.

The sealed archive is independently verifiable and readable for audit. It is not consulted to authorize a
new production mutation. Only the bounded live migration codec ships in the cutover tool, and that tool is
retired after the migration report is accepted.

## 5. Target GitHub authority model

### 5.1 Issues and organization fields

Use native issue types as the primary taxonomy:

| Native issue type | Replaces |
|---|---|
| `Epic` | `[epic]` convention and `Kind: anchor` where it is hierarchical |
| `Feature` | `Class: capability` |
| `Task` | `Class: hardening` |
| `Bug` | `Class: defect` |
| `Decision` | `Class: decision` and ordinary human-decision records |
| `Register` | standing register rows exempt from work lifecycle |
| `Directive` | standing directive rows exempt from work lifecycle |

Do not retain `Class` and `Kind` as parallel authoritative vocabularies. The migration maps every current
combination and refuses ambiguous or contradictory rows.

Organization issue fields become native authorities for human planning facts:

| Field | Type | Ownership |
|---|---|---|
| `FS-GG Scheduling` | single select | human/policy input |
| `FS-GG Hold reason` | single select | human/policy input when parked |
| `Priority` | single select | human/policy input |
| `Effort` | single select S/M/L/XL | planning input |
| `Start date` / `Target date` | date | planning input |
| `Severity` | single select | finding/triage input |
| `Phase` | single select | program projection validated against parent program |
| `Workstream` | text or single select after cardinality measurement | planning input |
| `Contract` | text containing validated registry IDs | registry-reference input |
| `FS-GG Touch set` | text | projection of the typed touch-set stream, not mutation authority |

Repository scope derives from the issue repository. The Project-local `Repo Scope` field retires.

### 5.2 Native relationships

- Parent/sub-issue is the sole hierarchy authority.
- Native issue dependencies are the sole implementation-dependency authority.
- Project `Blocked by` text and body `Blocked by:` lines retire.
- Room membership remains a typed protocol relation until GitHub offers an appropriate arbitrary relation;
  it must not misuse hierarchy or dependency merely for visibility.

The engine computes blocked status from the complete native edge set plus human scheduling holds. A native
edge webhook accelerates reconciliation but a fresh API read remains the transition proof.

### 5.3 Issue body

The v2 body is descriptive and reviewable:

- observed/requested outcome;
- root cause or explicitly unknown cause;
- acceptance criteria;
- verification evidence; and
- links to design, specification, and durable operation records.

`Paths:`, `Class:`, `Kind:`, `Blocked on:`, `Rooms:`, and dependency lines stop being semantic input. If a
compact human projection remains useful, it is generated with a source fingerprint and ignored by the
decision parser.

### 5.4 Project

The Coordination Project is a view and lifecycle projection over issues, not a second issue database:

- built-in `Status` is engine-derived;
- native issue fields appear consistently across projects;
- native parent progress and dependency badges replace copied progress/blocker fields;
- roadmap, triage, active work, review, decisions, release, and fleet-health views derive from the same
  fields;
- a Project template owns fields, views, and built-in workflows; and
- project membership is idempotently reconciled from issue type, repository properties, and program rules.

No Project draft issue may enter scheduling. Drafts are migration findings until converted to real issues.

### 5.5 Touch sets and claims

The claim protocol remains custom because GitHub has no CAS and all agents may share one account.

At intake, a typed touch-set event is created and projected to `FS-GG Touch set`. Claim binds the current
touch-set revision into the GitHub-assigned claim generation. Widen/narrow appends a generation-bound event;
the reducer deterministically selects valid successors and rejects cross-generation or concurrent replace
conflicts. Overlap may use the projected field as a cheap candidate prefilter, but a claim re-reads and
validates authoritative events before granting the lock. A stale projection can delay work; it cannot
authorize overlapping work.

### 5.6 Repository profiles and settings

`registry/repos.yml` remains reviewed authority for membership, external repositories, rich capability
contracts, and release topology. Selected FS-GG-owned attributes project into repository custom properties.
Those properties target rulesets and searches, but a drifted property never rewrites the registry.

One typed repository profile derives each repository's expected:

- required aggregate checks and merge-group support;
- merge method, auto-merge, merge queue, branch deletion, and conversation/review rules;
- Actions allowlist and immutable pin policy;
- release environment and immutable-release posture;
- security and dependency posture; and
- receiver workflow/package pins.

Per-repository exceptions require an ID, reason, owner issue, expiry, and model evidence. There is no
permanent anonymous exception list.

## 6. Runtime and command surface

### 6.1 One protocol compiler, thin hosts

The v2 product is built in a dedicated `FS.GG.Coordination` repository. Keeping the replacement inside the
special `.github` repository would couple its build, release, and qualification to the workflow and
coordination machinery it replaces. `.github` retains organization policy, registries, decision/design
records, the cutover ledger, desired-state instances, and thin reusable workflow entry points. The current
v1 source in `.github` receives only the bridge fence and necessary retirement fixes.

The compiled coordination extension supplies command metadata, schemas, projections, event codecs,
mutation capability census, and model tests. The new repository's hosts are deliberately thin:

- `FS.GG.Coord.Core` — compiled process models and pure decisions;
- `FS.GG.Coord.GitHub` — authority adapters and registered mutation interpreters;
- `FS.GG.Coord.Cli` — argument/JSON host over domain operations;
- GitHub App/webhook host — event normalization and targeted reconcile dispatch; and
- Actions workflows — protected execution environments, not alternative policy implementations.

The coordination project consumes the published FS.GG.SDD specification-kernel package. It does not copy
the generic types and does not use a source-project reference across repositories. `.github` and receivers
consume only exact published v2 artifacts during qualification and cutover.

### 6.2 Domain operations instead of generic mutation verbs

Normal user-facing operations converge on:

- `intake validate|plan|apply|inspect`;
- `roadmap validate|plan|apply|inspect`;
- `work take|claim|heartbeat|release|inspect`;
- `review inspect|record|advance`;
- `delivery inspect|advance`;
- `fleet inspect|plan|apply|verify`;
- `cutover inspect|prepare|freeze|switch|verify|open|retire|rollback`; and
- read-only diagnostics such as budget, schema, protocol, and history inspection.

Raw `graphql`, generic `set-field`, separate `add`, and other internal mutation primitives become
interpreter capabilities, not public workflow recipes. `plan` is pure and serializable; `apply` consumes
the exact plan identity; `inspect` re-reads the result.

### 6.3 GitHub App and permissions

The existing GitHub App becomes the normal remote mutation principal. The model derives a permission
census from registered interpreters. Each workflow/job declares only the permissions its selected plan
needs. Administrative settings operations run through protected environments and installation tokens;
ordinary coordination operations cannot acquire administration or release permissions.

Unsupported API/plan/permission states fail as `Unsupported` or `Unauthorized`, never as absence. Manual
operator repair uses the same plan and receipt contract.

### 6.4 Event-driven reconciliation with audit repair

Webhooks normalize into immutable event envelopes keyed by source and delivery/event ID. They schedule a
narrow subject reconciliation; they do not directly mutate derived state. Duplicate and reordered events
are idempotent. The durable cursor records what has been processed.

A complete scheduled audit remains the repair authority for missing deliveries, preview Project events,
external repositories, and schema drift. Event and audit routes must converge under replay. Schedules are
reduced only after measured event coverage; they are not deleted on optimism.

## 7. CI and workflow architecture

### 7.1 Stable aggregate gates

The fleet should expose a small stable contract of required checks:

- `product` — repository-native build/test/package obligations;
- `governance` — selected policy and evidence obligations;
- `coordination` — claim, touch-set, pin, contract, and receiver obligations;
- `review` — independent exact-head review acceptance; and
- `release` where a merge changes a releasable surface.

Internal jobs may evolve without becoming branch-protection API. Each aggregate result carries typed
reasons and exact source/run provenance. It cannot report green when a selected child is missing,
unreadable, still pending, or red.

### 7.2 Generated workflow inventory

One typed subject inventory should generate or drive repetitive policy jobs. Composite actions own repeated
checkout/setup/restore/evidence steps; reusable workflows own cross-repository job contracts. The current
policy-runner is the migration seed.

The target is not an arbitrary workflow count. Acceptance requires:

- every remaining workflow has one distinct trigger/permission/concurrency reason;
- no independent checker is duplicated merely to get another context name;
- receiver job IDs and aggregate context names are versioned public contracts; and
- removed workflows appear in a deletion ledger with their replacement or retired obligation.

### 7.3 Immutable execution dependencies

Cross-repository reusable workflows and third-party Actions use full commit SHAs. Renovate is the sole
automated update path. A modelled pin policy replaces the current accepted `@main` exception after the v2
receiver wave proves immutable updates and required-context continuity.

## 8. Release and supply-chain architecture

Trusted Publishing through OIDC remains. V2 adds:

- protected release environments bound to publisher policy;
- immutable releases and protected release tags;
- one pack operation producing content-addressed package bytes;
- SPDX SBOMs and artifact attestations for packages and release assets;
- dependency submission and dependency review;
- exact public-download verification; and
- existing dual-feed saga receipts and roll-forward recovery.

GitHub release immutability complements rather than replaces the release saga. A feed read remains
authority for whether a package is externally served.

## 9. Data migration

### 9.1 Manifest

The cutover manifest is generated from a fresh complete reading and binds:

- protocol/model/package fingerprints;
- exact old and new engine artifacts;
- Project and organization schema IDs plus semantic names/types/options;
- every migrated issue and Project item by global ID and repository/number;
- old field values, body declarations, native relations, type, parent, and projected v2 result;
- every live claim, operation, review, delivery, release, and queued write;
- repository profile and expected settings for every rostered repository;
- exact receiver commits and prepared pin/config PR heads;
- v1 archive payloads, verifier identity, counts, and digests;
- allowed dispositions for ambiguous or invalid rows; and
- all preflight, switch, verification, rollback, and deletion steps.

The manifest is content-addressed. No later phase accepts a regenerated manifest under the same operation.

### 9.2 Transformation rules

At minimum:

- `[epic]`/Kind/Class combinations map to one native issue type;
- Project/body effort, dates, severity, phase, workstream, and contract values map to org issue fields;
- repo scope is checked against the actual repository then omitted;
- textual blocker refs become native dependency edges;
- existing sub-issues are verified as the native hierarchy authority;
- human-block sentinels become scheduling and hold-reason fields;
- touch-set declarations become v2 touch-set events plus projection;
- lifecycle watermarks, claims, reviews, delivery, and release operations are drained or migrated;
- body metadata projections are removed after the v2 facts verify; and
- Project-local fields are deleted only after `OpenV2` makes v1 rollback unavailable.

Every transformation is `Migrated`, `Ambiguous`, or `Unsupported`. The cutover refuses on the latter two
until an explicit disposition is recorded. No heuristic default silently turns prose into authority.

### 9.3 Historical state

Archived Project items and closed issues remain audit subjects. They need not all receive active planning
fields, but their old authority bytes and relationships must be sealed. Open items and anything referenced
by an open operation receive full v2 migration.

## 10. Fleet cutover protocol

### 10.0 Bootstrap qualification lane

V2 is not implemented or certified through the existing coordination validation/verification lifecycle.
That would make the system under replacement both a dependency and its own acceptance oracle. Before v2
implementation begins, the maintainers approve a bounded bootstrap qualification contract in the new
`FS.GG.Coordination` repository. The live Coordination Project may project its milestones, but board state,
v1 done stamps, v1 review transitions, and v1 delivery receipts are not qualification evidence.

The custom lane keeps ordinary source control and review discipline, but replaces the v1 process gates with
evidence designed for this cutover:

1. a frozen, content-addressed corpus of known v1 incidents, protocol fixtures, live-shape snapshots, and
   adversarial cases;
2. compiler/unit/property/model tests for the typed extension and pure reducers;
3. adapter contract tests using recorded API fixtures plus destructive integration tests only in isolated
   GitHub test repositories;
4. black-box journeys authored independently of the generated model, preventing compiler and generated
   tests from agreeing on the same defect;
5. shadow-read comparison against the live fleet with no v2 mutation permission;
6. fault injection after every mutation step, replay, retry, partial/indeterminate recovery, and rollback
   rehearsal;
7. permission-boundary, rate/cost, pagination/completeness, webhook-loss, and tamper controls;
8. reproducible package/tool builds, API compatibility classification, immutable artifact identity, SBOM,
   attestation, and install-from-feed tests; and
9. an independent architecture/security/cutover review over exact candidate artifacts and evidence.

The typed model may generate exhaustive structural and transition cases, but at least one black-box oracle
for every safety-critical invariant must be maintained outside that generator. Otherwise a defect in the
specification or compiler would also manufacture a passing test. Qualification results are versioned,
content-addressed receipts in the cutover manifest; a green legacy workflow is neither required nor
sufficient.

The only v1 change intentionally delivered through the existing repository is the bridge fence in F2. Its
acceptance is two-sided: current v1 regression tests prove unchanged behavior in `OperatingV1`, while the
new independent harness proves that every registered old writer refuses at and after `FreezeRequested`.

### F0 — ratify the model and corpus

- Accept an org ADR for this authority split, kernel additions, new-only strategy, and point of no return.
- Create `FS.GG.Coordination` with its bootstrap qualification contract, minimal permissions, ruleset, and
  independent release path.
- Freeze representative incident, parity, pagination, partial-write, claim, review, delivery, and release
  fixtures.
- Add the two filing-time contradictions and native dependency/hierarchy cases to the corpus.
- Derive the complete current authority and mutation census.

Exit: every existing surface is classified, and every proposed deletion has a replacement or retirement
rationale.

### F1 — extend the typed protocol kernel

- Reference the published FS.GG.SDD specification kernel.
- Implement authority bindings, completeness proofs, relations, projections, observation plans, cutover
  epochs, mutation plans, and sealed-history contracts.
- Model one vertical slice end to end: native dependency observation and add/remove, with no production
  write enabled.

Exit: model, interpreter fake, schema, docs, semantic diff, replay, and mutation controls agree.

### F2 — ship and adopt the v1 bridge fence

- Add the global epoch read to every existing v1 mutation entry point.
- Publish one bridge release without changing normal v1 semantics in `OperatingV1`.
- Adopt the bridge package/tool and coordination-kit pin in all seven receivers and `.github`.
- Prove an unreadable, contradictory, or frozen epoch prevents every normal mutation.

Exit: no registered v1 writer can ignore `FreezeRequested` or later frozen states.

### F3 — prepare native schema and desired-state profiles

- Create issue types, issue fields, repository custom-property schema, Project template/views, protected
  environments, and candidate repository profiles additively.
- Backfill v2 projections without changing the v1 scheduling decision.
- Record unsupported organization-plan/API capabilities explicitly.

Exit: schema inspection agrees exactly with the compiled model and is safe to leave inert on rollback.

### F4 — shadow reads and build the cutover manifest

- Run v1 and v2 decisions read-only over the complete defect corpus and live snapshots.
- Classify every divergence.
- Drain or identify every live operation; seal historical state.
- Prepare receiver PRs, workflow pins, merge-group triggers, rulesets, and release/security settings.
- Reconcile the open dependency-update PR backlog so the cutover does not inherit known obsolete pins.

Exit: zero unexplained divergence, zero unclassified live operation, and every receiver change has an exact
green head ready for the cutover window.

### F5 — request and establish freeze

- Acquire the dedicated cutover operation grant through the protected `fleet-cutover` environment.
- Commit and anchor `FreezeRequested(manifest)` in the cutover ledger.
- Stop new claims, intake applies, board mutations, review advances, merges performed by the coordination
  client, dispatches, and releases.
- Drain active claims and settle or explicitly park human work.
- Apply temporary per-repository update restrictions with only the cutover App bypass where supported.
- Take the complete source snapshot, then re-read it and prove no mutation occurred between reads.
- Commit and anchor `Frozen(snapshot)` in the cutover ledger.

Exit: all normal v1 and v2 mutation controls are red, active claims are zero, release sagas are settled,
and repository heads/settings match the manifest.

### F6 — switch while closed

- Publish/promote the exact v2 engine and kit artifacts if publication was not completed before freeze.
- Activate v2 manifest resolution in `.github`.
- Merge/apply the prepared receiver pins and configuration in dependency order.
- Materialize native relationships and authoritative fields from the frozen manifest.
- Apply normal v2 rulesets, aggregate checks, Actions policies, environments, and security settings.
- Disable v1 schedules, dispatch routes, and writers without deleting rollback assets.
- Commit and anchor `SwitchedV2(candidate)` in the cutover ledger.

Exit: every repository resolves the exact v2 artifact and settings profile, but normal mutations remain
frozen.

### F7 — verify the closed v2 fleet

- Run complete schema, repository-profile, required-context, merge-group, package/install, event replay,
  board, relationship, projection, and archive verification.
- Exercise canary issues through intake, hierarchy, dependency, claim, touch-set, review, delivery, and
  done flows inside an isolated cutover test program.
- Inject missing, stale, contradictory, partial, rate-limited, permission, and dropped-webhook controls.
- Verify the rollback plan from current state without executing destructive deletion.
- Commit and anchor `VerifiedV2(evidence)` in the cutover ledger.

Exit: every positive journey passes and every named wrong-path control refuses or reds.

### F8 — open v2

- Present the exact manifest, evidence roll-up, remaining risks, and rollback boundary for protected
  environment approval.
- Commit and anchor `OpenV2(acceptance)`, then lift the v2 normal-write fence.
- Prove one real bounded work item completes from intake through done under v2.

`OpenV2` is the point of no return. Any later failure uses roll-forward repair. V1 never resumes.

### F9 — retire v1 and normalize operations

- Delete v1 readers/writers, text blocker field, Class/Kind/body sentinels, old Project fields, legacy
  schedules, moving-ref exception, and temporary migration adapters.
- Archive the v1 verifier and evidence manifest outside production dependency closure.
- Remove temporary update restrictions and enable the accepted normal merge-queue/ruleset profiles.
- Publish the deletion ledger and 0/7/14/30-day operational readings.
- Commit and anchor `OperatingV2(report)` after deletion and immediate verification; later readings amend the report,
  not the epoch.

Exit: no v1 production authority or mutation path remains, and the v2 protocol-surface census is complete.

## 11. Rollback and recovery

| State | Allowed recovery |
|---|---|
| `Preparing` | abandon candidate; v1 continues |
| `FreezeRequested` | resolve/drain blockers or return to `OperatingV1` with a recovery receipt |
| `Frozen` | restore old settings/pins from snapshot; re-enable bridge v1; open only after verification |
| `SwitchedV2` | keep writes frozen; restore old settings/pins and v1 projections; inert v2 schema may remain |
| `VerifiedV2` | same as `SwitchedV2`; approval may choose rollback instead of open |
| `OpenV2` and later | roll-forward only; old writers remain fenced permanently |

Rollback is itself a durable mutation plan. A failed rollback step is partial/indeterminate and resumes from
receipts; it is never a shell checklist presumed complete.

## 12. Proof obligations

### 12.1 Model and compiler

- stable IDs and fingerprints are deterministic;
- every fact has one authority and codec;
- every projection has one source and freshness proof;
- every mutation has one interpreter, precondition, and idempotency identity;
- unknown or contradictory vocabulary cannot produce a successful plan;
- semantic diff identifies authority, wire, guard, and desired-settings changes; and
- the protocol-surface gate rejects unmodelled production behavior.

### 12.2 GitHub adapter

- every connection proves completeness;
- issue type/field/schema IDs are resolved and checked against semantic names and types;
- relation add/remove is idempotent and concurrent-safe under re-read;
- webhook duplicates/reordering and audit repair converge;
- a Project or API preview failure has a scheduled-read fallback;
- permission and organization-plan gaps are distinguishable from absence; and
- cost bounds hold under board-size and worker-concurrency tests.

### 12.3 Concurrency and lifecycle

- claim, lease, touch-set, op-lock, and election invariants retain their current negative controls;
- scheduling intent cannot be overwritten by observed status;
- stale projection or webhook state cannot authorize a mutation;
- merge queue re-evaluates every temporal required check against the merge group;
- exact-head review and post-merge protected-run verification remain distinct; and
- model-based plus bounded formal tests cover claim/election, relation set mutation, and saga retry.

### 12.4 Fleet and release

- every receiver uses the exact published artifact, not a source shortcut;
- every required context is unconditionally produced on PR and merge-group events;
- Action and workflow references satisfy immutable pin policy;
- package, SBOM, attestation, public download, and dual-feed evidence agree;
- no active release crosses freeze; and
- repository settings agree with their derived profile after every phase.

### 12.5 Cutover corpus

Inject failure after every mutation step in F5 through F9. Retry from receipts must converge or produce a
typed terminal refusal. At least these controls are mandatory:

- old client attempts a write after freeze;
- manifest digest changes between phases;
- one receiver remains on the bridge version;
- native edge count differs from the text migration source;
- issue field exists with wrong type or option set;
- Project item is archived, external, duplicated, or unreadable;
- temporary ruleset only partially applies;
- merge-group check never reports;
- webhook is dropped and later repaired by audit;
- package is uploaded but not publicly served;
- immutable release rejects a recovery rewrite; and
- v1 deletion begins before `OpenV2`.

## 13. Repository ownership and release order

| Repository | Cutover responsibility |
|---|---|
| FS.GG.SDD | Published generic specification kernel; only additive extension support actually required by coordination |
| FS.GG.Coordination (new) | V2 coordination extension, GitHub adapters/interpreters, CLI/App hosts, custom qualification harness, and published v2 artifacts |
| `.github` | V1 bridge fence and retirement; org desired-state instances, cutover ledger/orchestration, org ADR/design, registry, Project migration, reusable workflow entry points, and evidence index |
| Governance | Consume stable v2 subjects/evidence only where its existing pipeline needs them; do not absorb coordination meaning |
| SDD, Rendering, Governance, Templates, Game, Audio, Net | Adopt bridge fence, then exact v2 engine/kit/workflow pins and repository profile |
| External roster rows | Observed and reported; changed only by their owner under an explicit external disposition |

Producer publication precedes receiver adoption. The switch order is:

1. published specification-kernel contract already available;
2. bootstrap and qualify the new coordination repository independently;
3. bridge coordination tool and kit from the v1 repository;
4. all bridge receiver adoptions;
5. v2 tool/kit and immutable workflow revisions;
6. frozen native/schema/settings migration;
7. receiver v2 pins and profile application;
8. closed-fleet verification;
9. `OpenV2`; and
10. v1 deletion and final v2 release evidence.

## 14. Typed ADR/decision corpus successor

After the coordination protocol compiler is stable, add a `.github`-owned `DecisionExtension` over the
same published specification kernel. It is related but is not a prerequisite for `OpenV2`.

The typed extension owns:

- decision ID, title, status, date, and decision owners;
- affected repository and contract IDs;
- amendment, supersession, withdrawal, and execution edges;
- context, decision, consequences, and considered alternatives as structured rich-text sections;
- ratification and semantic-diff receipts; and
- generated index/navigation metadata.

Markdown remains the primary human review projection. `docs/adr/NNNN-*.md`, `docs/adr/README.md`, and the
supersession map are generated from the same model. Narrative is retained as rich text; semantic edges and
status are typed. The compiler replaces the bespoke Markdown parser and catches one-sided relationships by
construction.

Migration of the 75 existing records is one explicit corpus cutover:

1. import each file with its git revision and digest;
2. type every status and relationship;
3. retain narrative sections without heuristic reinterpretation;
4. compare generated projections and semantic graph with the current corpus;
5. disposition every ambiguity explicitly;
6. switch ADR authoring to the agent-mediated typed workflow; and
7. retire `check-adr-coherence.py`, handwritten README rows, and handwritten supersession entries.

The GitHub Substrate v2 ADR is authored under the current mechanism and later participates in this
migration. There is no bootstrap dependency on an unfinished decision extension.

## 15. Other machinery improvements enabled by the design

Typed SDD should become the common specification substrate for several other FS-GG mechanisms, but it
must not become the mandatory serialization or authoring syntax for every structured file. There are three
different adoption modes:

1. **Canonical EDSL** — agent-authored F# is authority when the artifact declares semantic rules,
   relationships, transitions, or generation behavior and benefits from semantic diff.
2. **Kernel-backed document model** — YAML, JSON, or Markdown remains the appropriate human/external input,
   while a typed extension supplies validation, normalization, fingerprint, migration, and projections.
3. **Typed observation only** — generated evidence and runtime state use shared identities and evidence
   envelopes but are not authored specifications.

The admission test is concrete: use an extension when an artifact needs stable semantic identity, typed
cross-references, deterministic normalization, semantic diff, derived views, evidence obligations, and
explicit migration. Prefer an ordinary domain type or strict parser when it does not need most of those
properties.

### 15.1 Fleet application map

| Surface | Adoption | Why and boundary | Sequence |
|---|---|---|---|
| Coordination protocol and GitHub desired state | Canonical EDSL | Commands, authorities, relations, settings, mutations, permissions, and cutover transitions are specifications. GitHub observations and receipts remain external facts. | This cutover; first priority |
| Cross-repository contract and release topology | Canonical EDSL | `registry/dependencies.yml` already carries stable contract IDs, owners, consumers, compatibility, versions, and coherent-set policy. A `ContractTopologyExtension` should derive the YAML/compatibility view and release-impact plan; feed/package observations remain evidence. | Start after the coordination extension's first vertical slice; do not gate `OpenV2` |
| Repository catalog and coordination-kit profile | Canonical EDSL | Roster identity, capabilities, external ownership, receiver obligations, and release topology are declarative policy. Derived custom properties and desired repository profiles can share the same subjects. | Develop with desired-state profiles |
| Skill catalog and delivery predicates | Federated canonical EDSL | Producer manifests remain owner-authored inputs. A `SkillDeliveryExtension` types scope, ownership, file digests, delivery channels, and the current string predicate grammar as an inspectable predicate AST; the central registry is a compiled union, not copied authorship. Skill prose remains Markdown. | Follow repository catalog; migrate one producer family first |
| Governance rule/check catalog | Canonical EDSL around existing typed rules | Governance already has reified checks, facts, evidence, routing, and pure loops. Wrap the rule catalog in a kernel extension for identity, semantic diff, projections, and migrations; do not move Governance evaluation policy into SDD or rewrite `.fsgg` parsing. | Independent pilot after package-boundary design |
| Provider/profile/template composition | Canonical EDSL for the composition matrix | Provider IDs, lifecycle/profile options, parameters, conditional payloads, pins, and generated test cases form a specification currently exercised by several large scripts. Template content remains files; the extension derives descriptors, manifests, and matrix fixtures. | Pilot on one provider family after Typed SDD default readiness |
| ADR and decision corpus | Canonical EDSL with rich-text nodes | Status and relationship semantics become typed; narrative stays rich text; Markdown/index/supersession views are generated as section 14 defines. | After the coordination compiler stabilizes; not a cutover prerequisite |
| Executable TestSpec/acceptance scenario catalogs | Selective canonical EDSL | Use typed scenarios where stable inputs, expected transitions, invariants, and evidence can execute or generate tests. Long-form game-design tutorials and examples remain Markdown. | Product-owned pilots only when an executable consumer exists |
| `.fsgg` Governance and SDD configuration | Kernel-backed document model | These are maintainer/user-authored configuration with good strict typed parsers. Reuse fingerprint, migration, diagnostics, and projection contracts where valuable; do not require consumers to author F#. | Opportunistic convergence, no source-format cutover |
| Release declarations, work artifacts, audit/CI reports, SBOMs, attestations, API surfaces | Typed observation or existing contract | These primarily describe a requested operation or observed result. They should bind to specification IDs/fingerprints and evidence obligations, not become EDSL source merely because they are structured. | Adopt shared envelopes as producers change |
| Product runtime state, ordinary code, design-token data, prose documentation | No platform EDSL by default | Product domain types and native formats are the right authority unless a concrete specification lifecycle and semantic-diff need is demonstrated. | Explicit proposal required |

This map implies a small family of repository-owned extensions over one published kernel, not a universal
FS-GG AST. Cross-extension references use stable IDs and versioned contracts; one extension may consume a
projection or published contract from another, but it may not inspect another domain's private union cases.

### 15.2 Broader architectural improvements

These are architectural improvements rather than uses of a new GitHub feature:

1. **Collapse the public command surface.** Domain plan/apply/inspect operations replace dozens of generic
   and overlapping verbs.
2. **Decompose the runtime by protocol surface.** Generated command registration and capability boundaries
   replace the 9,263-line client as the integration point.
3. **Move incident narrative out of executable source.** Stable diagnostic/control IDs link compact code to
   the defect corpus and design; source comments explain invariants without becoming a second report.
4. **Generate policy projections.** Workflow subjects, required aggregate checks, docs, skills, schemas,
   settings profiles, and permission censuses derive from the compiled model.
5. **Make compatibility debt expire.** Every bridge, exemption, old codec, and shadow reader has an owner,
   issue, expiry/cutover state, and deletion test.
6. **Separate detection from repair.** Audits emit typed findings; reconcilers apply only an accepted exact
   plan. No check silently repairs its own subject.
7. **Standardize evidence envelopes.** One event/receipt envelope carries source, subject, revision,
   causation, correlation, model fingerprint, and evidence reference across coordination operations.
8. **Add operational SLOs.** Measure claim latency, time blocked after dependency resolution, webhook repair
   lag, API cost per decision, queue time, CI duration, false-positive/unknown rate, partial-operation
   recovery, and successor-row churn.
9. **Control automation backlog.** Renovate remains sole dependency bot; stale superseded PRs are closed or
   refreshed, update grouping follows coherent sets, and the fleet reports dependency-update age rather
   than accumulating silent queues.
10. **Practice recovery.** Periodic fixture-backed cutover and release-recovery rehearsals verify that sealed
    archives, settings snapshots, and mutation receipts are usable before an incident.

## 16. Alternatives considered

### Incrementally replace fields while v1 and v2 both write

Rejected. It makes divergence a supported production state and requires permanent precedence rules between
text, body, native relations, issue fields, and events.

### Rewrite the coordination engine independently of the specification kernel

Rejected. The generic compiler, fingerprint, semantic diff, migration, projection, and evidence contracts
are already published. Reimplementing them would create the duplicate framework ADR-0076 exists to prevent.

### Make GitHub native state authoritative for everything

Rejected. Text/Project fields, assignees, and status checks do not provide claim CAS, touch-set concurrency,
exact-head review evidence, semantic contract compatibility, or two-feed transaction recovery.

### Keep Markdown/body metadata because REST reads are cheaper

Rejected as authority. A generated compact projection may remain as a cost optimization, but a stale or
edited projection cannot authorize work. Cost is handled by observation plans and candidate prefilters.

### Host a custom coordination database or adopt a durable workflow service

Rejected for this version. It adds operational availability and recovery dependencies, makes checkout-only
repair impossible, and is unnecessary for the measured scale. The event/plan model leaves a future storage
adapter possible.

### Roll back v2 after new-only writes begin

Rejected unless a separately implemented down-migration proves it. Promising such rollback would hide
irreversibility. `OpenV2` makes roll-forward the explicit policy.

### Block GitHub v2 on typed ADR migration

Rejected. Decision typing is valuable but not required to make coordination writes safe. Coupling them would
create a bootstrap cycle and widen the cutover unnecessarily.

## 17. Acceptance conditions

The architecture is accepted for implementation only when:

1. a cross-repository ADR records the authority table, protocol additions, fleet-cutover strategy, and
   `OpenV2` boundary;
2. every current protocol fact and remote mutation appears in the derived census;
3. every native GitHub capability in scope has live API/plan/permission evidence and an unsupported path;
4. the v1 bridge fence can stop every existing registered writer;
5. the complete live migration can be represented without heuristic defaults or silent omissions;
6. issue type, field, relationship, Project, repository profile, Actions, release, and security desired
   states compile from one model;
7. one vertical relation slice proves the generic extension/interpreter boundary before wider work;
8. model, replay, mutation, and bounded concurrency controls cover every irreversible or concurrent path;
9. the cutover manifest and archive formats are versioned, content-addressed, and independently verified;
10. rollback is executable through `VerifiedV2`, and roll-forward is explicit after `OpenV2`;
11. each migration step has an exact deletion criterion and no indefinite compatibility mode;
12. receiver changes are published/adopted in contract order with no source-project shortcuts;
13. the required-check/ruleset model is complete before merge queue can replace custom check election;
14. sealed v1 evidence remains auditable after all v1 production code is removed; and
15. a final review can point to each retained custom mechanism and state the missing native semantic that
   still justifies it.

## 18. Stop conditions

Return to design instead of widening implementation if:

- the protocol extension needs an untyped fact, mutation, or authority escape hatch;
- a GitHub native surface cannot expose sufficient revision/completeness evidence for the assigned role;
- v1 bridge adoption cannot fence every writer;
- a cutover step requires normal v1 and v2 writers to be enabled together;
- live operations cannot be drained or migrated without rewriting immutable evidence;
- Project/native relationship limits cannot represent the live graph and no explicit alternate authority is
  accepted;
- merge queue would bypass or fail to re-run a temporal required check;
- desired-state application cannot distinguish unsupported, unauthorized, absent, stale, and partial;
- rollback requires deleting or rewriting evidence created before `OpenV2`;
- the extension begins copying generic specification-kernel concepts into `.github`; or
- implementation adds more compatibility readers, public commands, or workflows than its accepted deletion
  ledger retires.

## 19. Required review questions

The independent architecture review must answer, from evidence rather than preference:

1. Is any fact assigned to GitHub that lacks the required concurrency, revision, completeness, or audit
   semantics?
2. Is any retained custom authority now redundant with a native feature?
3. Can every v1 writer actually be fenced by the bridge epoch?
4. Does the cutover ever enable two writers or cross the point of no return before closed-fleet proof?
5. Are historical audit and production compatibility cleanly separated?
6. Does desired-state reconciliation centralize power without equally strong permission, partial-write, and
   recovery controls?
7. Are issue taxonomies and fields minimal, or do they recreate Class/Kind/Repo Scope under new names?
8. Does the protocol extension build on the published specification kernel without widening that generic
   package for GitHub-specific concerns?
9. Are the migration and deletion ledgers complete for board fields, body metadata, workflows, commands,
   parsers, schedules, settings, and receiver pins?
10. Would a maintainer with only the sealed archive, cutover manifest, published artifacts, and GitHub state
    be able to explain and recover every partial step?
