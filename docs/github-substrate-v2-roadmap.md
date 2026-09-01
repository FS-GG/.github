---
title: "Roadmap: GitHub Substrate v2 fleet cutover"
category: Design
categoryindex: 4
index: 26
description: "The complete execution sequence for independently building and qualifying FS.GG.Coordination, cutting the fleet to GitHub Substrate v2, and retiring v1."
---

# Roadmap: GitHub Substrate v2 fleet cutover

This is the living execution roadmap for replacing the current FS-GG coordination system. V2 is built in
a new `FS.GG.Coordination` repository, consumes the published FS.GG.SDD specification kernel, and is
qualified independently of the v1 lifecycle it replaces. The fleet is prepared additively, frozen once,
switched and verified while normal writes remain closed, opened at one explicit point of no return, and
then observed for a fixed 30 days before destructive v1 contraction. V1 authoring is fenced immediately at
open; retained assets are inert forensic/recovery evidence and cannot restart v1. This document owns
cross-repository sequence and exit gates; the
[governing design](coordination/2026-08-25-github-substrate-v2-fleet-cutover-design.md) owns the architecture
and rationale.

> **Quint-first candidate dependency:** [ADR-0077](adr/0077-quint-first-typed-specification-authority.md)
> and the [migration design](coordination/2026-08-25-quint-first-typed-sdd-migration-design.md) require the
> behavioral protocol in GS2-02 to be a literate Quint source consumed through the published FS.GG
> compiled-contract boundary. Census/bootstrap work may proceed, but protocol implementation must wait for
> successful Q1 qualification, the post-qualification ADR-0077 amendment, and that producer artifact. The
> current F# P4 package remains production authority meanwhile.

> **Model-based-testing siting:** FS.GG.SDD supplies the published Quint/ITF and generic replay contract;
> `FS.GG.Coordination` owns its canonical protocol model, adapter, observable-state projection, and replay
> tests. `.github` owns the frozen v1 corpus, isolated GitHub qualification environment, registry, and
> routing policy. It must not grow a centralized v2 replay implementation or a product-specific shadow
> transition model.

> **Remaining-migration architecture amendment:** the
> [2026-08-30 architecture review](coordination/2026-08-30-github-substrate-v2-remaining-migration-architecture-review.md)
> found that comment-order CAS and immediate post-open contraction do not meet the required concurrency and
> recovery contracts. GS2-03.10 accepted the sharded expected-parent Git-journal redesign and requalified
> the affected GS2-02 contracts; GS2-03.7 subsequently accepted the repaired candidate supply chain. The
> architecture amendment is therefore enforced rather than an outstanding blocker.

| Field | Value |
|---|---|
| Status | GS2-00 and GS2-01 accepted; GS2-02.1–GS2-02.11, all GS2-03 units, and GS2-04.1–GS2-04.7 accepted; GS2-01.9 not applicable; GS2-04.8 is next |
| Program | [GitHub modernization Epic `.github#2952`](https://github.com/FS-GG/.github/issues/2952) |
| Ratification | [`.github#2953`](https://github.com/FS-GG/.github/issues/2953) |
| Build and qualification | [`.github#2963`](https://github.com/FS-GG/.github/issues/2963) |
| Bridge and cutover ledger | [`.github#2964`](https://github.com/FS-GG/.github/issues/2964) |
| Fleet cutover and retirement | [`.github#2965`](https://github.com/FS-GG/.github/issues/2965) |
| Point of no return | Authoritative `OpenV2` transition in the protected cutover ledger |
| Current production authority | v1 until `OpenV2`; preparation and shadow reads do not change that |

## 1. How work is executed

The three program issues are too large to hand directly to a general worker. They are durable anchors for
ownership, roll-up, and cross-repository sequencing. Actual work is performed as one bounded roadmap unit
at a time.

### 1.0 Qualification strength at child and parent boundaries

[ADR-0080](adr/0080-scoped-child-qualification-comprehensive-milestone-closure.md) makes scoped
content-addressed qualification the default for every ordinary child unit. A gate executes when its declared
semantic subject changes and may reuse independently validated immutable evidence when that subject is identical.
Formal-input drift always executes the canonical formal gate. The exact-head terminal manifest binds every current
or reused gate subject and artifact.

The final child of each parent milestone is also its explicit closure candidate. That candidate binds the complete
accepted child set and forces all declared gates cold; scoped reuse is forbidden. Protected merge and exact-merge
verification produce the append-only parent closure receipt. Freeze, release, cutover, rollback-authority, and
`OpenV2` boundaries are comprehensive regardless of their position in the hierarchy. GS2-04.9 is the first closure
candidate governed by this default.

[ADR-0081](adr/0081-adaptive-qualification-cadence-from-observed-cost-and-defect-yield.md) adds the
operating principle inside that envelope: approximately daily, use observed gate cost, unique actionable-defect
yield, detection delay, closure equivalence, and blast radius to recommend retaining, increasing, or reducing each
gate's cadence. Sparse evidence stays explicitly inconclusive, cadence changes are reviewed versioned policy, and
closure plus production-authority boundaries cannot be weakened. A defect first found at closure feeds the next
cadence review instead of becoming an unpriced surprise.

### 1.1 Before active `FS.GG.Coordination` bootstrap

The README-only repository exists at the explicitly authorized inert bootstrap commit
`ce22e4d10f2efae7aa09018521487b598c082350`; it has no v2 code or production authority. Only `GS2-00`
and preparation for the active-bootstrap portion of `GS2-01` are assignable. The instruction to an agent
must name one unit and its permitted effects, for example:

> Execute `GS2-00.2` from the GitHub Substrate v2 roadmap. Produce the proposed ADR and authority census;
> preserve the inert repository, do not modify live GitHub configuration, and do not start v2 implementation.

Any active bootstrap beyond that inert creation, installing the GitHub App, creating environments, or
changing organization settings requires explicit organization-administrator authority. A worker may
prepare and verify an exact plan, but must not infer that authority from the roadmap.

### 1.2 After the bootstrap repository exists

`GS2-01` creates a repository-owned `github-substrate-v2-work` skill and a small roadmap command. That
command reads this document's stable unit IDs and the new repository's typed specification/evidence index,
then reports the next unit whose prerequisites are satisfied. It never schedules directly from mutable
Project status.

The normal instruction becomes:

> In `FS.GG.Coordination`, use `github-substrate-v2-work` to execute the next ready unit. Work only that
> unit, publish its required qualification evidence, and stop at its named exit gate.

Every unit must have:

- one owning repository;
- an explicit touch-set or administrative target set;
- prerequisite unit IDs and issue references;
- an exact candidate/source revision;
- positive acceptance cases and independent negative controls;
- a permission ceiling;
- a rollback or no-write boundary;
- generated and independently authored evidence; and
- a merged PR or protected administrative receipt before it is marked complete.

### 1.3 Evidence and status

The Coordination Project is a visibility projection. V1 claim, review, delivery, verification, and done
records do not qualify v2. Completion authority is the custom qualification receipt stored against the
exact source and artifact fingerprints in `FS.GG.Coordination`.

Each unit has these states:

```text
Planned -> Ready -> Implementing -> Candidate -> Qualified -> Accepted
                    |                 |
                    +-> Refused       +-> Rejected
```

- `Ready` means every prerequisite receipt is accepted.
- `Candidate` means code or a settings plan exists but has no authority to enter production.
- `Qualified` means all generated and independent controls pass against exact artifacts.
- `Accepted` means the owning PR is merged or the protected administrative operation is verified.
- `Refused` and `Rejected` retain evidence and return to design or implementation; they are not failures
  hidden by a retry.

Roadmap checkboxes are updated only from accepted receipts. They are a human projection, not proof.

### 1.4 Typed SDD handoff and parallel-work rule

Typed SDD P0–P4 are complete and provide the published generic kernel. The former coordination M0–M9
sequence is not another lane to burn down beside this roadmap. It is an inventory whose still-valid
requirements are mapped into GS2 units. In particular, `.github#2932` must not be claimed as written:

- its authority/mutation/compatibility census and baselines move to `GS2-00.3`–`GS2-00.7`;
- its omission and byte-compatibility controls enter the frozen v1 corpus;
- its coordination types and compiler surface move to `GS2-02` in `FS.GG.Coordination`; and
- its Standard SDD completion artifacts remain historical evidence, not v2 qualification.

Ordinary product work continues through v1 and does not need a v2 rebase. Typed SDD P5 readiness and soak
work may run in parallel through `GS2-09`. The P5 default flip has only two admitted schedules: complete
and stabilize it before `GS2-10`, or defer it until `OperatingV2`. At `GS2-10`, the kernel version,
lifecycle defaults, providers, scaffolder, registry, receiver heads, workflows, and settings become frozen
candidate inputs. Any later change invalidates approval and requires a new candidate plus the complete
Q0–Q7 rerun. No such change crosses `GS2-11`–`GS2-12`.

Before dispatching a unit, classify every adjacent row as one of:

| Classification | Meaning | Scheduling effect |
|---|---|---|
| `v2-unit` | Directly implements one named GS2 unit | Schedulable only when that unit's prerequisites hold |
| `v2-blocker` | Proven external work without which a unit cannot be authored or qualified | File/retain in its owning repo and record a real dependency edge |
| `parallel-product` | Ordinary product or Typed SDD work whose output can be re-observed before candidate freeze | May proceed; it does not become part of the v2 worker's touch-set |
| `candidate-input-change` | Kernel, lifecycle default, provider, registry, workflow, receiver, or settings change | Refresh before `GS2-10`; mint a new candidate or defer afterward |
| `superseded-inventory` | Former M-series work already transferred into GS2 acceptance | Never claim from Project status; amend/close the obsolete row |
| `cutover-deferred` | Safe work intentionally held across the freeze | Resume only after `OperatingV2` through the new system |

The Coordination board remains the visibility and dependency projection. It is not allowed to recreate the
superseded execution plan by presenting a historical M-series row as `Ready`.

## 2. Non-negotiable program invariants

1. The published FS.GG.SDD specification kernel is consumed as a package; no source-project shortcut or
   copied generic kernel is permitted.
2. GitHub owns facts it can represent with the required identity, revision, completeness, and relation
   semantics. FS-GG owns only the missing process, concurrency, evidence, and transaction semantics.
3. V2 is implemented in `FS.GG.Coordination`; `.github` retains organization policy, registries, designs,
   the cutover ledger, desired-state instances, and thin reusable workflow entry points.
4. The v1 engine receives only the universal epoch bridge, necessary security fixes, and retirement work.
5. Preparation may create inert schema and read-only projections. V1 and v2 normal production writers are
   never enabled together.
6. Every write is a typed, revision-bound, idempotent mutation plan with a durable outcome that includes
   partial and indeterminate states.
7. Generated model tests are necessary but insufficient. Every safety-critical invariant has at least one
   independently authored black-box oracle.
8. Failure, absence, incomplete reads, unsupported capabilities, insufficient permission, stale data,
   contradiction, partial success, and indeterminate success remain distinct.
9. Before `OpenV2`, rollback restores the bridge v1 authority while writes remain controlled. At and after
   `OpenV2`, recovery is roll-forward and v1 never resumes.
10. No compatibility reader, migration adapter, workflow, command, field, or exception survives without a
    deletion unit and observable deletion test.
11. A lease, comment order, or pre/post read is not a concurrency authority. Every claim, review epoch,
    operation grant, and cutover transition uses protected expected-parent Git-ref CAS and presents its
    monotonically increasing generation as a fencing token at the external effect boundary.
12. Webhooks and commands schedule reconciliation; only the shared fresh-observe/reduce/plan/apply/verify
    reconciler performs normal production writes, and complete scheduled audits repair lost hints.

## 3. Program dependency map

```text
GS2-00 Ratify design and freeze authority census
   |
   +-------------------------+
   |                         |
   v                         v
GS2-01 Bootstrap repo     GS2-08 Specify v1 bridge/ledger contract
   |                         |
   | waits for Quint Q1      |
   | + ADR-0077 amendment    |
   v                         |
GS2-02 Protocol core         |
   |                         |
   +----------+--------------+
   |          |              |
   v          v              v
GS2-03      GS2-04         GS2-08 Implement/publish/adopt bridge
Qualification GitHub IO       |
   |          |               |
   +----+-----+               |
        |                     |
        v                     |
   GS2-05 Work model          |
        |                     |
        +---------+-----------+
        |         |
        v         v
   GS2-06       GS2-07
   Settings/CI  Events/queue
        |         |
        +----+----+
             |
             v
        GS2-09 Migration/archive tooling
             |
             v
        GS2-10 Qualify exact candidate and prepare fleet
             |
             v
        GS2-11 Freeze and snapshot
             |
             v
        GS2-12 Switch and verify while closed
             |
             v
        GS2-13 Open v2 and retire v1
             |
             v
        GS2-14 Observe, normalize, and close
```

`GS2-03` and `GS2-04` may proceed in parallel after the protocol envelope is frozen. `GS2-06` and
`GS2-07` may proceed in parallel after the work model and adapter boundaries are qualified. The bridge may
be developed in parallel with v2, but it cannot publish until the epoch wire contract is frozen. No fleet
preparation begins from an unqualified candidate.

## 4. Qualification gates

These gates replace the existing coordination validation/verification process for v2.

| Gate | Proves | Required evidence |
|---|---|---|
| Q0 Architecture | Authorities and boundaries are coherent | Accepted ADR, authority/mutation/deletion census, threat model, independent review |
| Q1 Compiler | Literate Quint source extracts, typechecks, and compiles deterministically and completely | Literate-source and extracted-module fingerprints, pinned extractor/Quint/profile identities, Quint type/effect results, compiled-contract canonical bytes, semantic diff, source mapping, schema/projection freshness, and wrong-source/extraction/model controls |
| Q2 Pure model | The canonical Quint model's state and decisions obey invariants | Quint examples, simulation, property and bounded model checking, safety/liveness witnesses and counterexamples, plus independently authored transition tests |
| Q3 Adapter | External observations and writes preserve meaning | Consumer-owned ITF replay adapter, observable-state projection, recorded fixtures, pagination/completeness, revision, idempotency, partial/indeterminate and mapping-mutation controls |
| Q4 Sandbox | Real GitHub behavior matches the adapter contract | Isolated repositories/project, destructive test identities, API/permission/rate evidence |
| Q5 Shadow | V2 reads the live fleet without changing it | Complete snapshots, v1/v2 decision comparison, explained divergence ledger, zero v2 write permission |
| Q6 Recovery | Every multi-step path resumes safely | Failure after every step, receipt replay, compensation/roll-forward, rollback rehearsal |
| Q7 Supply chain | The exact candidate can be trusted and installed | Reproducible build, package hashes, SBOM, attestations, both-feed/public-read verification |
| Q8 Closed fleet | The switched fleet works before normal writes open | Schema/settings/receiver proof, isolated canary journeys, injected wrong paths, rollback readiness |
| Q9 Retirement | V1 can no longer author production state | Static/runtime writer census, deletion ledger, old-client refusal, sealed-history verification |
| Q10 Operations | V2 is stable after opening | 0/7/14/30-day SLOs, incidents, repair lag, API cost, queue/CI/release measurements |

No single test generator may satisfy both sides of a safety claim. For example, the protocol compiler may
generate all legal epoch transitions, but an independent black-box suite must still attempt an old-client
write after freeze and reject a forged or rewound ledger.

Q2 model checking and Q3/Q4 correspondence answer different questions. Q2 proves properties of the Quint
model. Q3 replays fingerprinted ITF traces through the real pure implementation and compares only its
declared model-observable state. Q4 exercises the same adapter boundary against isolated GitHub behavior.
Passing one gate cannot substitute for either of the others, and `.github` may not host a fake v2
implementation merely to make replay pass.

Q1 and Q2 also cannot be split across different authored models. Q1 qualifies the exact literate Quint
source and extracted module set whose behavior Q2 explores; an F#-only reducer suite, generated shadow
model, or separately authored formal model cannot substitute for native Quint type/effect checking and
model checking. F# tests remain required implementation evidence and enter correspondence through Q3.

## 5. Detailed milestones

### GS2-00 — Ratify architecture and freeze the v1 corpus

**Parent:** `.github#2953`
**Owner:** `.github`
**Exit gate:** Q0

- [x] **GS2-00.0 — Accept the program handoff.** Finish or explicitly disposition Typed SDD P4
  release/tag/feed/registry residue (including `.github#2968` while it remains open); amend or resolve
  `.github#2932` as superseded and map each retained clause to this roadmap; record whether P5 is not
  started, readiness-only, finishing before `GS2-10`, or deferred until `OperatingV2`; classify every
  active Typed SDD claim, review, delivery, release, and receiver PR as `finish`, `park`, or `defer`.
  Re-adjudicate `.github#2932`'s declared dependencies (`.github#2903`, `.github#2905`, `.github#2841`,
  and `.github#2850`) independently as current-v1 defects, corpus inputs, real v2 blockers, or deferred
  work; the superseded edge itself is not a v2 prerequisite.
  Repair the modernization Epic's task-line acceptance and place dependencies for `.github#2964` and
  `.github#2965` in the authoritative Project `Blocked by` field rather than inert issue-body lines.
- [x] **GS2-00.1 — Resolve review questions.** Review every authority assignment, retained custom
  mechanism, GitHub plan/API limitation, permission boundary, and rollback claim in the governing design.
- [x] **GS2-00.2 — Author the org ADR.** Record the dedicated repository, published-kernel dependency,
  new-only writer policy, independent qualification lane, protected Git epoch ledger, native/custom
  authority table, and `OpenV2` boundary. Keep status Proposed until independent review is complete.
- [x] **GS2-00.3 — Complete the v1 authority census.** Enumerate every issue/body/Project/registry/comment,
  workflow, command, JSON contract, environment variable, file, package, schedule, and setting that can
  affect a coordination decision.
- [x] **GS2-00.4 — Complete the mutation census.** Name every always-writing and conditionally writing
  command, workflow, App route, repair script, release route, and administrative path; bind each to its
  current precondition and eventual v2 disposition.
- [x] **GS2-00.5 — Freeze the defect and behavior corpus.** Content-address representative claim,
  touch-set, dependency, hierarchy, intake, review, delivery, merge, release, pagination, rate-limit,
  partial-write, stale-read, and self-hosting incidents. Import `.github#2932`'s reproducible 72-hour
  churn, mutation-entry, protocol-string, replay, omission, and misclassification baselines without
  importing its superseded v1 implementation plan.
- [x] **GS2-00.6 — Freeze public compatibility surfaces.** Inventory CLI verbs/flags/exit codes, JSON and
  marker schemas, package IDs/versions, reusable workflow inputs/outputs/job IDs, required contexts, and
  receiver pins. Classify each `Preserve`, `Migrate`, `Seal`, or `Retire`.
- [x] **GS2-00.7 — Freeze the deletion ledger.** Every v1 source tree, parser, projection, field, schedule,
  workflow, exception, and package must have a later deletion unit and a test proving absence.
- [x] **GS2-00.8 — Accept Q0.** Architecture, security, operations, and cross-repository reviewers sign the
  exact design/census fingerprints; unresolved material questions block `GS2-01`.
- [x] **GS2-00.9 — Decide runtime operations.** Select or reject a deployment boundary for the App/webhook
  host, including ownership, availability target, secrets, ingress verification, logs/metrics, upgrades,
  incident response, data retention, cost, and disaster recovery. If no acceptable host is approved,
  scheduled audits remain authoritative and `.github#2961` is removed from the cutover critical path by an
  explicit amendment rather than by shipping an unowned service.
  **Q0 decision recorded 2026-08-26 under delegated maintainer authority:** reject a hosted boundary for
  this cutover. The complete operational evidence has no accepted owner/service boundary, availability
  target, secret/ingress design, observability, upgrade and incident process, retention policy, cost
  envelope, or disaster-recovery proof. Scheduled complete audits therefore remain authoritative; events
  are an optional post-`OperatingV2` accelerator and `.github#2961` is not on the critical path.

  **Q0 acceptance evidence:** repair PR #3002 head
  `d07cc9daeef46f6f034e2e4cf23dcf3deeea6da0`, fingerprint
  `febaa98f354fcad88f50c4c17e7592f3d46d9e6c1d0c381831b6a705e4d68668`, exact role attestations
  ([architecture](https://github.com/FS-GG/.github/pull/3002#issuecomment-5419411396),
  [security](https://github.com/FS-GG/.github/pull/3002#issuecomment-5419418539),
  [operations](https://github.com/FS-GG/.github/pull/3002#issuecomment-5419407467),
  [cross-repository](https://github.com/FS-GG/.github/pull/3002#issuecomment-5419406435)), and the final
  [repair-phase confirmation](https://github.com/FS-GG/.github/pull/3002#issuecomment-5419425110).
  Ratification evidence additionally requires structural SDD analysis with exact `noChange`,
  `coherent: true`, `implementationReady`, zero stale/generated-view findings, no diagnostics, and a clean
  tracked tree after all gates. The immutable
  [revision-4 changes-required record](https://github.com/FS-GG/.github/pull/3002#issuecomment-5419558809)
  diagnoses the stale substring-only verifier at old head `b0b63507f115f0de9e488c9f68dcde22b6992c67`
  and authorizes repair; it does not close the finding. Commits
  [0ced1901](https://github.com/FS-GG/.github/commit/0ced1901) and
  [c9e82ef](https://github.com/FS-GG/.github/commit/c9e82ef) implement and seal the fix, while the independent
  [c9 architecture narrative](https://github.com/FS-GG/.github/pull/3002#issuecomment-5419650173)
  verifies that the stale-SDD defect is fixed and separately requires this provenance correction. The
  [revision-5 changes-required record](https://github.com/FS-GG/.github/pull/3002#issuecomment-5419655595)
  and [cross-repository narrative](https://github.com/FS-GG/.github/pull/3002#issuecomment-5419636584)
  diagnose and authorize correction of the provenance contradiction; they make no future acceptance claim.
  Every changed head/fingerprint still requires fresh live role attestations.
  The later [revision-6 changes-required record](https://github.com/FS-GG/.github/pull/3002#issuecomment-5419728195)
  and [operations narrative](https://github.com/FS-GG/.github/pull/3002#issuecomment-5419704123) diagnose
  stale downstream `analysis.json` digests and authorize repair; they are not acceptance. Q0 additionally
  hashes every evidence source snapshot and every top-level verify, ship, and governance-handoff source
  against current bytes and carries a stale-analysis inversion before asserting a clean tracked tree.
  The [revision-7 changes-required record](https://github.com/FS-GG/.github/pull/3002#issuecomment-5419837439)
  and [architecture narrative](https://github.com/FS-GG/.github/pull/3002#issuecomment-5419807264)
  diagnose and authorize correction of source-set omission; they are not acceptance. Q0 requires exact,
  duplicate-free per-artifact label/path multisets and rejects malformed, missing, duplicate, unexpected,
  or stale rows, with independent omission, duplicate, and extra-row mutations.
  The [revision-8 changes-required record](https://github.com/FS-GG/.github/pull/3002#issuecomment-5419940163)
  and [operations narrative](https://github.com/FS-GG/.github/pull/3002#issuecomment-5419915373)
  diagnose and authorize repair of the remaining source-row schema gap; they are not acceptance. Q0 now
  binds exact per-path kinds, required/allowed row and nested digest keys, integer schema version, current
  schema status where applicable, and one canonical lowercase SHA-256 representation per projection.
  Table-driven controls reject missing/wrong kind, missing/wrong schema or status, unexpected keys,
  alternate digest forms, malformed types, and invalid digest casing/length across evidence, verify, ship,
  and governance handoff while retaining all source-set and stale-byte inversions.
  Hosted [run 32925218156](https://github.com/FS-GG/.github/actions/runs/32925218156) / job
  [98048225279](https://github.com/FS-GG/.github/actions/runs/32925218156/job/98048225279) then proved that
  `author_association` is viewer-dependent for private organization membership: the maintainer view
  reported `MEMBER`, while the workflow token reported the same immutable comments as `CONTRIBUTOR` and
  rejected all four roles. Q0 therefore binds an exact unique GitHub User login allowlist into the signed
  fingerprint and accepts a User when either its live association is allowed or its login is allowlisted;
  Bots, missing users, and non-allowlisted `CONTRIBUTOR`/`NONE` records remain fail-closed.
  The later [revision-10 diagnosis](https://github.com/FS-GG/.github/pull/3002#issuecomment-5420215570),
  [security evidence](https://github.com/FS-GG/.github/pull/3002#issuecomment-5420187392), and
  [cross-repository evidence](https://github.com/FS-GG/.github/pull/3002#issuecomment-5420206908) require
  one login grammar before both authorization routes: 1–39 ASCII characters, alphanumeric endpoints,
  and only alphanumerics or single internal hyphens. Empty, whitespace, leading/trailing/double hyphen,
  overlength, non-string, underscore, dot, and Unicode identities fail even with an allowed association.

### GS2-01 — Bootstrap `FS.GG.Coordination`

**Parent:** `.github#2963`
**Owner:** organization administrator, then `FS.GG.Coordination`
**Depends on:** GS2-00
**Exit:** independently buildable empty product and executable qualification skeleton

**Producer route:** ADR-0077's successor backend is implemented through
[Q1–Q3 of the Quint-first migration sequence](coordination/2026-08-25-quint-first-typed-sdd-migration-design.md#7-prepared-feature-sequence).
`GS2-01.4` consumes that route's published artifact; the bootstrap worker must not recreate its extractor,
profile, compiled contract, or generic ITF machinery inside `FS.GG.Coordination`.

- [x] **GS2-01.1 — Complete repository provisioning.** Preserve the explicitly authorized README-only
  creation receipt, then apply least-privilege teams, branch ruleset, signed/immutable tag policy, secret
  scanning, dependency graph, Dependabot alerts, Actions policy, auto-merge policy, and default branch
  settings. Record the exact settings receipt; the early inert creation alone does not complete this unit.
- [x] **GS2-01.2 — Register the component.** Add the repository and ownership/release topology to the
  reviewed registry, architecture map, custom-property projection, GitHub App installation scope, and
  Coordination Project membership rules.
- [x] **GS2-01.3 — Establish the solution boundary.** Create packages/projects for protocol specification,
  pure core, GitHub adapters, CLI host, App/webhook host, qualification contracts, and tests. Enforce
  one-way dependencies and keep GitHub SDK/HTTP concerns out of the pure core.
- [x] **GS2-01.4 — Pin the published Quint-capable kernel.** Restore the exact published FS.GG.SDD artifact
  carrying the accepted Quint profile and compiled-contract boundary from the supported read feed, verify
  its identity and bundle digest, and prohibit source-project or checkout-relative references. An earlier
  bootstrap may temporarily pin P4 for non-semantic scaffolding, but that pin cannot qualify GS2-02. This
  unit is not ready until Q1 has succeeded and ADR-0077 has been amended with the accepted literate source,
  extraction, authority, fingerprint, and compatibility contract.
- [x] **GS2-01.5 — Establish custom CI.** Add only bootstrap qualification jobs: deterministic build,
  compiler/unit tests, dependency/security checks, package/install smoke, and evidence-manifest validation.
  Do not import v1 coordination completion gates.
- [x] **GS2-01.6 — Create the work skill.** Add `github-substrate-v2-work` with commands to inspect this
  roadmap, check unit prerequisites, create a unit evidence manifest, run the relevant Q gates, and stop at
  the unit boundary.
- [x] **GS2-01.7 — Create evidence storage.** Version schemas and directories for corpus inputs, external
  observations, independent oracles, generated cases, test results, artifact manifests, reviews, and
  accepted qualification receipts. Generated bulky output remains in immutable CI artifacts/releases;
  compact indexes and digests remain in git.
- [x] **GS2-01.8 — Prove bootstrap recovery.** A clean machine clones, restores, builds, tests, packs,
  installs, and validates an empty candidate using published dependencies only.
- **GS2-01.9 — Provision non-production runtime (not applicable to this cutover).** Accepted GS2-00.9
  rejected a hosted App/webhook boundary, so this historical conditional branch is not a pending checkpoint.
  If a future ratification accepts that boundary, it must provision development and qualification
  environments, deployment identity, secret rotation, observability, backup/recovery, and a kill switch
  before any production event subscription exists.

### GS2-02 — Implement the typed coordination specification

**Parent:** `.github#2963`
**Owner:** `FS.GG.Coordination`
**Depends on:** GS2-01, including the final Quint-capable GS2-01.4 pin
**Exit gates:** Q1 and the pure portion of Q2

**Implementation references, in authority order:**

1. [ADR-0077](adr/0077-quint-first-typed-specification-authority.md) owns the authoring and authority
   decision, including the mandatory post-Q1 amendment.
2. The [Quint-first migration design](coordination/2026-08-25-quint-first-typed-sdd-migration-design.md)
   owns literate extraction, the FS-GG Quint profile, compiled-contract boundary, ITF replay ownership, and
   producer-before-consumer sequence; its Q5 is the direct GS2 handoff.
3. The [Quint experiment](quint/README.md) and
   [assessment](quint/reports/assessment.md) are the executable baseline and recorded limitations, not
   production authority.
4. The [governing cutover design](coordination/2026-08-25-github-substrate-v2-fleet-cutover-design.md)
   owns the coordination domain, proof obligations, authority boundaries, and cutover semantics that the
   Quint protocol must express.
5. Quint's [literate-specification documentation](https://quint.sh/docs/literate) defines the upstream
   workflow being qualified; only the pinned profile/toolchain accepted by ADR-0077 may enter CI.

- [x] **GS2-02.1 — Author the canonical literate Quint protocol.** Keep reviewer-oriented Markdown prose
  beside deterministically extracted, named Quint blocks that specify subjects, authorities, codecs, commands,
  events, mutations, projections, observation plans, settings profiles, evidence obligations, and version
  IDs under the published FS-GG Quint profile. Generate stable integration identities through the compiled
  contract; prose cannot add hidden semantics, extracted `.qnt` files cannot be edited independently, and
  no parallel F# protocol AST is permitted.
- [x] **GS2-02.2 — Implement authority bindings.** Model native GitHub, repository registry, protocol
  stream, git ledger, Actions, package feed, and other external authorities with explicit revision and
  completeness contracts.
- [x] **GS2-02.3 — Implement observations.** Distinguish observed, proven absent, contradictory,
  unreadable, unsupported, unauthorized, incomplete, stale, and rate-limited outcomes.
- [x] **GS2-02.4 — Implement lifecycle intent.** Separate human scheduling intent from claims, blockers,
  PR/review/delivery observations and derived lifecycle status.
- [x] **GS2-02.5 — Implement native relation algebra.** Represent parent/child and blocking relations as
  typed edge sets with idempotent add/remove intent rather than scalar replacement.
- [x] **GS2-02.6 — Implement protocol streams.** Type claim/lease/touch-set, operation-lock/election,
  review, delivery, and operation-receipt envelopes; classify ephemeral retention versus durable
  checkpointing.
- [x] **GS2-02.7 — Implement mutation algebra.** Cover create, append, add/remove edge, set, clear,
  transition, and compensate with expected revision, idempotency, and all terminal/uncertain outcomes.
- [x] **GS2-02.8 — Implement durable plans.** Compile decisions into ordered resumable steps with
  causation/correlation, receipt re-read, compensation boundary, and roll-forward classification.
- [x] **GS2-02.9 — Implement desired-state specifications.** Type issue schema, Projects, repository
  profiles, rulesets, workflow pins, permissions, releases, security, and supply-chain settings.
- [x] **GS2-02.10 — Implement compiled-contract outputs.** Derive schemas, command metadata, permission census,
  mutation census, settings plans, Markdown/JSON views, semantic diff, diagrams, and model-test inventory.
- [x] **GS2-02.11 — Prove deterministic identity.** Equivalent literate Quint authoring forms extract and
  normalize identically; semantic changes produce stable, reviewable diffs; prose-only changes cannot alter
  behavioral identity; unsupported source, extractor, Quint, profile, or schema versions fail before execution.

### GS2-03 — Build the independent qualification system

**Parent:** `.github#2963`
**Owner:** `FS.GG.Coordination`
**Depends on:** GS2-02.1–GS2-02.8
**Exit gates:** Q1, Q2, Q6, and Q7 harness capability

- [x] **GS2-03.1 — Define the qualification manifest.** Bind source, model, compiler, dependencies,
  generated cases, independent cases, external fixtures, package bytes, environment, results, and reviewers.
- [x] **GS2-03.2 — Import the frozen corpus.** Preserve original bytes, provenance, expected behavior,
  ambiguity, and current-v1 result; never normalize away the defect being tested.
- [x] **GS2-03.3 — Add generated structural tests.** From the qualified Quint source and compiled contract,
  derive vocabulary completeness, transition coverage, command/mutation registration, permission coverage,
  schema round-trip, and projection freshness cases without creating a second behavioral model.
- [x] **GS2-03.4 — Add independent black-box oracles.** Hand-author tests for claim exclusion, stale
  projections, dependency set concurrency, partial operations, old-client fencing, ledger rewind/tamper,
  exact-head review, post-merge verification, and dual-feed release recovery. Include independently authored
  scale and abstraction oracles that force existing and future Quint roots to preserve required outcomes and
  anti-vacuity witnesses across bounded concrete-versus-abstract comparisons; record root closure, dependency
  depth, state/sample counts, elapsed time, peak memory, and artifact volume against runner-calibrated budgets.
  **Accepted 2026-08-29:** protected-main merge
  [`17a6f0e`](https://github.com/FS-GG/FS.GG.Coordination/commit/17a6f0e48f79356cbfd673c8e0e8f5bb5f3efd30)
  and exact-merge [qualification run 33273274618](https://github.com/FS-GG/FS.GG.Coordination/actions/runs/33273274618)
  completed the single fresh repair phase. The typed
  [repair-phase record](https://github.com/FS-GG/FS.GG.Coordination/pull/93#issuecomment-5464629983), fresh
  [successor pass](https://github.com/FS-GG/FS.GG.Coordination/pull/93#issuecomment-5464669577), and
  [host acceptance](https://github.com/FS-GG/FS.GG.Coordination/pull/93#issuecomment-5464677128) bind exact
  head `0e3de0aaac9fc2f95a1625ad3f43e0f2ee90a455`; source-derived executable closure and reproduced
  digest-pinned Quint typechecking close both terminal escalation findings with no accepted exception.
- [x] **GS2-03.5 — Add native Quint model/property/formal tests.** Run examples, simulation, reachability
  witnesses, safety properties, temporal liveness checks, and bounded model checking over claim/election,
  relation mutation, lifecycle, operation saga, epoch, and rollback state spaces, retaining reproducible
  Quint/ITF counterexamples.
  **Accepted 2026-08-30:** protected-main merge
  [`893997af`](https://github.com/FS-GG/FS.GG.Coordination/commit/893997af87cb723c06cb9e1865c56165f28e22ed)
  and exact-merge [qualification run 33282988853](https://github.com/FS-GG/FS.GG.Coordination/actions/runs/33282988853)
  accepted exact candidate `aa2f8d165aeeb107c210ed231a9702f69c0e76a6`. The fresh
  [successor pass](https://github.com/FS-GG/FS.GG.Coordination/pull/97#issuecomment-5465677710) and
  [host acceptance](https://github.com/FS-GG/FS.GG.Coordination/pull/97#issuecomment-5465697691) bind a
  clean-checkout SDD projection with all 24 tracked observations verified, six reproducible temporal
  transition-removal counterexamples with retained Quint/ITF/trace/manifest/receipt digests, and
  runner-calibrated hosted elapsed budgets. All findings closed without exception.
- [x] **GS2-03.6 — Add fault injection.** Fail before and after every external step; lose responses;
  duplicate/reorder events; return partial pages; exhaust rate budgets; revoke permission; mutate concurrent
  revisions; and require convergence or typed refusal.
  **Accepted 2026-08-30:** protected-main merge
  [`df8e7d29`](https://github.com/FS-GG/FS.GG.Coordination/commit/df8e7d290c9990087ada6c2cd62e859db976f0ee),
  exact-merge [Bootstrap qualification run 33287672800](https://github.com/FS-GG/FS.GG.Coordination/actions/runs/33287672800),
  and [push CodeQL run 33287672761](https://github.com/FS-GG/FS.GG.Coordination/actions/runs/33287672761)
  accepted exact candidate `5b5b1fd82a47e1bc14dcc9c94b72307c61c76b6d`. The fresh
  [successor pass](https://github.com/FS-GG/FS.GG.Coordination/pull/101#issuecomment-5466125131) and
  [host acceptance](https://github.com/FS-GG/FS.GG.Coordination/pull/101#issuecomment-5466191671) bind
  15 deterministic executions across the source-derived Inspect, Plan, Apply, and Verify boundary: 11
  converge and four return accepted typed refusals. Seven subject-defect and nine artifact inversions fire;
  independent final-state comparison rejects duplicate and reorder corruption even when their trace labels
  are restored to the healthy labels. All findings closed without exception.
- [x] **GS2-03.10 — Amend and requalify the remaining-migration architecture.** Implement the
  [2026-08-30 review](coordination/2026-08-30-github-substrate-v2-remaining-migration-architecture-review.md):
  replace comment-order concurrency authority with sharded expected-parent Git journals and fencing
  generations; make one reconciler the normal write path; bind full snapshot epochs; add audit repair;
  change cutover to open, observe, then contract; and requalify every affected GS2-02 contract with
  independent counterexamples and black-box negative controls. This unit blocks GS2-03.7 and all later
  implementation; historical receipts do not qualify the amended candidate. Track the bounded work and
  fresh review on [`.github#3075`](https://github.com/FS-GG/.github/issues/3075).
  **Accepted 2026-08-30:** [Coordination PR 109](https://github.com/FS-GG/FS.GG.Coordination/pull/109)
  merged as protected-main commit
  [`624ae14f`](https://github.com/FS-GG/FS.GG.Coordination/commit/624ae14f9a1d9d4baf92045a2357fc549d274f04)
  after [exact-head hosted qualification run 33331721108](https://github.com/FS-GG/FS.GG.Coordination/actions/runs/33331721108)
  accepted candidate `a45837654be041ee8366bb857266005e5eb13c10`, exact source
  `7d6755e0e723796eb30486451cb3610e6a74874f26055a3c382986ce525d3218`,
  and compiled contract `947262bc9f70c371d79a917804d2ed4adcabbb1cc2ff683eedc637e36e6b163e`.
  The canonical non-refresh replay is green in 593,599 ms with result
  `d68b2c8a25bff5174cab55cb5292d382a1f577cdf807739ba759705c42fbd33b` across seven bounded roots, eleven formal
  scenarios, 8 positive invariants, and 126 catalogue-derived negative controls. The journal race/fencing
  model explores distinct retry and fencing properties over 20 states/48 transitions; reconciliation
  explores 51/163 with total webhook loss, duplicate/reordered hints, mid-read authority change, and audit
  restart; review epochs explore 10/22 including a valid current effect and rejected stale effect; and
  evidence-rich observation-gated contraction explores 136/532 across clock, success, freshness, and
  snapshot binding. Under [ADR-0079](adr/0079-single-accountable-delivery-authority.md), these checks and
  critique records are evidence for one accountable acceptance decision; no external or multiple
  authorization is required. Every affected
  safety mutant fails and every removed-step temporal counterexample reproduces with retained deterministic
  ITF, Quint trace, and manifest digests. Protected branches in a dedicated authority repository are now
  the durable CAS journal, using exact old-OID Git receive-pack leases;
  winning generations fence effects, comments/webhooks are projections or hints, and complete audits repair
  loss. Qualification preflights all projection witnesses before expensive TLC work, derives rejection
  coverage as `71 + 5 × formal scenarios`, and reconciles the labeled 186 external / 161 Quint / 47 verify
  launch inventory through an atomic concurrent observer. After the second related late-stage verifier
  defect, the Accountable Delivery Owner paused retries, traced the shared default Apalache endpoint as the
  ordering hazard, isolated every compile/verify invocation on a unique endpoint, retained full stdout/stderr
  failure evidence, and reran the complete corpus. The hosted gate passed in 1,222 seconds; no exception or
  retry-only acceptance was used. The owner bound the exact commit and evidence identities; another person,
  account, or agent was not an authorization gate.
  **Provisioning update 2026-08-30:** the public
  [`FS-GG.Coordination.Authority`](https://github.com/FS-GG/FS.GG.Coordination.Authority) repository now
  exists as repository `1351660651`. Active ruleset `v2-journal-writer` (`21872113`) restricts create/update
  and bypasses only App `4166418`; active `v2-journal-integrity` (`21872115`) independently rejects
  deletion/non-fast-forward with no bypass actors. Both use the corrected GitHub fnmatch
  `refs/heads/fsgg/v2/journal/**/*`. Live rule suite `3875208315` proves a human administrator cannot
  create a matching ref, and effective-rule readback returns all four rules. The first `/**` target was
  found by the negative control to match nothing, removed, and replaced before any authority opened.
  The policy-owner qualification workflow landed through [`.github` PR 3076](https://github.com/FS-GG/.github/pull/3076)
  and [run 33330220225](https://github.com/FS-GG/.github/actions/runs/33330220225) proved App
  creation/fast-forward CAS plus stale, rewrite, and deletion rejection without exposing the App private key
  to the authority repository. The single-accountable acceptance contract then landed through
  [`.github` PR 3078](https://github.com/FS-GG/.github/pull/3078) and shipped in coherent set
  [`0.77.0`](https://github.com/FS-GG/.github/releases/tag/coherent-set%2Fv0.77.0), content
  `sha256:ec52502830d351b841dc64fe1833a9640991a413cfd6230e0d2ecce9f6115711`.
- [x] **GS2-03.7 — Add reproducibility and supply-chain checks.** Build twice in independent clean
  environments and compare package bytes, designate one candidate byte set, verify provenance and SBOM
  predicates separately, publish those identical candidate bytes to every allowed pre-production feed,
  then download and install them from isolated feeds in clean consumers with repository-local empty caches.
  Retain package, symbol, SBOM, attestation, served-download, and installed-assembly digests.
  **Accepted 2026-08-30:** protected candidate
  [`ea9781c`](https://github.com/FS-GG/FS.GG.Coordination/commit/ea9781c89d169eec2e4f6aad004acc3a764d59d7)
  passed [hosted run 33336821654](https://github.com/FS-GG/FS.GG.Coordination/actions/runs/33336821654),
  which packed once, published only version `0.0.0-gs2-03-7.ea9781c89d16` to the candidate channel,
  compared served bytes, and executed two clean consumers. The retained Actions artifact is bound by
  repository/run/artifact IDs and zip `sha256:592cfd8ece9ce30a2430e81425ae08ee241e43366c91e0b484dc3bf5d0541ade`;
  package, canonical symbol package, portable PDB, installed assembly, SPDX SBOM, provenance, and served
  attestation digests are recorded in the
  [accepted receipt](https://github.com/FS-GG/FS.GG.Coordination/blob/9341a5a299bfd2cb90c5ea9b1e2355d34297ab4e/evidence/github-substrate-v2/accepted/GS2-03.7.json),
  merged through [Coordination PR 112](https://github.com/FS-GG/FS.GG.Coordination/pull/112).
- [x] **GS2-03.8 — Add critique evidence gates.** Architecture, security, adapter, migration, and cutover
  perspectives produce findings against exact candidate fingerprints. The Accountable Delivery Owner may
  perform every perspective under distinct phase identities and makes the sole acceptance decision; a green
  roll-up must still be derived from the bound evidence rather than asserted in prose.
  **Accepted 2026-08-31:** protected implementation
  [`2427478b`](https://github.com/FS-GG/FS.GG.Coordination/commit/2427478b6fffba470e86ff46cf2ca22106a11a6d)
  introduced the typed `critique-evidence/1` generator/validator, executable reviews/v2 schema, closed
  five-perspective inventory, distinct phase identities, exact candidate/evidence/content bindings, and
  derived `all-required-bound-green/1` roll-up. [PR 116](https://github.com/FS-GG/FS.GG.Coordination/pull/116)
  passed [full hosted qualification 33342488041](https://github.com/FS-GG/FS.GG.Coordination/actions/runs/33342488041)
  and CodeQL; protected main then reused the identical complete tracked tree and revalidated the merge.
  The Accountable Delivery Owner retained five separately hashed perspective findings and an executable
  acceptance harness that checks the PR-execute/protected-main-reuse relationship, Q7 gate inventory,
  exact tree/unit identities, and every hosted conclusion before regenerating the canonical
  [critique bundle](https://github.com/FS-GG/FS.GG.Coordination/blob/4c7878623f2fc73c4a5d8eb55697d159e6e08e86/evidence/github-substrate-v2/reviews/GS2-03.8.json).
  Acceptance [PR 117](https://github.com/FS-GG/FS.GG.Coordination/pull/117) passed
  [hosted run 33343928942](https://github.com/FS-GG/FS.GG.Coordination/actions/runs/33343928942), including
  reproduction of the actual bundle; protected-main [run 33344891169](https://github.com/FS-GG/FS.GG.Coordination/actions/runs/33344891169)
  revalidated acceptance merge `4c787862` while skipping all six expensive lanes. The append-only
  [accepted receipt](https://github.com/FS-GG/FS.GG.Coordination/blob/4c7878623f2fc73c4a5d8eb55697d159e6e08e86/evidence/github-substrate-v2/accepted/GS2-03.8.json)
  has self-digest `67e73228b0c6ef658b1294314e75e2bc62021f070f1c446b0ffdbda19834e116`.
  One Accountable Delivery Owner made the sole decision; no reviewer count, external account, or additional
  authorization was required. The observed evidence-only acceptance route still paid one full formal run;
  that selector-granularity cost is retained for a later CI architecture improvement rather than hidden.
- [x] **GS2-03.9 — Prove the harness can fail.** Mutation-test or invert every gate class so a vacuous,
  absent, stale, truncated, forged, or generated-only evidence set is red.
  **Accepted 2026-08-31:** protected implementation
  [`53f0338d`](https://github.com/FS-GG/FS.GG.Coordination/commit/53f0338dea988fd79b95092286709df7c0fb4745)
  introduced the typed `harness-mutation-proof/1` boundary, closed ten-gate and six-mutation inventories,
  ten healthy controls, and the derived sixty-cell negative Cartesian matrix. Every cell is created by the
  generator, executes the production qualification-manifest validator, records its actual stable diagnostic,
  and is regenerated during validation; callers cannot assert outcomes, coverage counts, or a hand-picked
  subset. Valid-hash forgery is resealed through every dependent manifest digest and rejected only against
  the independently frozen healthy baseline. Generated-case producers are also red when they are the sole
  provenance for any non-generated evidence class. [Implementation PR 120](https://github.com/FS-GG/FS.GG.Coordination/pull/120)
  passed [full qualification run 33348201595](https://github.com/FS-GG/FS.GG.Coordination/actions/runs/33348201595)
  and protected main reused the identical subject through
  [run 33349197336](https://github.com/FS-GG/FS.GG.Coordination/actions/runs/33349197336).

  The first hosted candidate exposed an opaque TLC transition-removal result after a prior related formal
  failure. Under the standard second-defect rule, the owner stopped retries, inventoried the complete formal
  result boundary, and reproduced the exact relation model independently. The model still produced its
  two-state liveness counterexample, while the runner had conflated semantic green, tool exit, output-protocol
  drift, and nondeterminism into one message. The class-level repair now retains exit code/stdout/stderr for
  every simulation-measurement, transition-removal, and counterexample-marker failure and hashes both
  projection and temporal-diagnostic reproductions when they differ. It changes no success predicate, lane,
  or ordering edge. The repaired local canonical run passed Q1/Q2 with 8 positive invariants, 126 rejected
  controls, 11 reproducible formal counterexamples, and the exact 186 external / 161 Quint / 47 verify
  process census before the complete hosted run passed.

  Independent [acceptance PR 121](https://github.com/FS-GG/FS.GG.Coordination/pull/121) retained exact Q7,
  hosted formal, PR-execute, protected-main-reuse, and four-run observations; five separately hashed critique
  perspectives; a deterministic combined digest over both validator source files; and the canonical
  [mutation proof](https://github.com/FS-GG/FS.GG.Coordination/blob/d7029dd485419e19a3b6b6491932b39b7e29ecba/evidence/github-substrate-v2/mutation-proofs/GS2-03.9.json)
  (`sha256:4585fb2f68700dd8d8f0a470a55591fc0d5b6e8a31d2936ff2388fe655204060`).
  Acceptance passed [full run 33349742847](https://github.com/FS-GG/FS.GG.Coordination/actions/runs/33349742847)
  and merged as protected commit
  [`d7029dd4`](https://github.com/FS-GG/FS.GG.Coordination/commit/d7029dd485419e19a3b6b6491932b39b7e29ecba);
  protected-main [run 33350667104](https://github.com/FS-GG/FS.GG.Coordination/actions/runs/33350667104)
  then revalidated the identical acceptance subject. The append-only
  [accepted receipt](https://github.com/FS-GG/FS.GG.Coordination/blob/d7029dd485419e19a3b6b6491932b39b7e29ecba/evidence/github-substrate-v2/accepted/GS2-03.9.json)
  has self-digest `c5b0bf313583e26dc6a2f471b58e22d6315f4ff425d05cf6f74070c45c5ecde2`.
  One Accountable Delivery Owner made the decision; no external, multiple, or approval-count authorization
  was required, and every declared technical predicate remained fail-closed.

### GS2-04 — Implement GitHub authority adapters and interpreters

**Parent:** `.github#2963`
**Owner:** `FS.GG.Coordination`
**Depends on:** GS2-02; GS2-03 manifest contract
**Exit gates:** Q3 and Q4

- [x] **GS2-04.1 — Transport foundation.** Implement typed REST/GraphQL requests, response envelopes,
  retries that respect idempotency, ETags/revisions, rate budgets, pagination, node/connection completeness,
  API-version headers, redaction, and deterministic fixture capture.

  Accepted 2026-08-31. [Implementation PR 124](https://github.com/FS-GG/FS.GG.Coordination/pull/124)
  bound typed REST and GraphQL transport, idempotency-aware retry decisions, revisions and rate facts,
  pagination completeness, redaction, and deterministic fixtures to exact candidate
  `15737cfd698216fb5a232178eb5e2f36233efb4b`. The candidate passed
  [full qualification run 33358807559](https://github.com/FS-GG/FS.GG.Coordination/actions/runs/33358807559)
  and merged as protected commit
  [`3182fccc`](https://github.com/FS-GG/FS.GG.Coordination/commit/3182fcccf81ad3e519624ad12cbd7f7ce5c3b66a),
  whose [protected-main run 33360184506](https://github.com/FS-GG/FS.GG.Coordination/actions/runs/33360184506)
  revalidated the identical subject. Qualification comprised 34 focused unit tests, 258 architecture tests,
  and fourteen Q3 positive/inversion controls. The hosted candidate also exposed an implicit Apalache server
  lifecycle assumption; the repair made server start, readiness, verification, and teardown explicit so the
  canonical formal lane passed without weakening a property.

  [Acceptance PR 126](https://github.com/FS-GG/FS.GG.Coordination/pull/126) added the append-only
  [GS2-04.1 receipt](https://github.com/FS-GG/FS.GG.Coordination/blob/dbaecade8d47d872336601b3b3bb9785082dbb81/evidence/github-substrate-v2/accepted/GS2-04.1.json)
  with self-digest `867e1d9842f0821be0af1bacfc175ef23da1351f22bb353de6ffb3671229a58e`
  after [full run 33360908095](https://github.com/FS-GG/FS.GG.Coordination/actions/runs/33360908095),
  including canonical Quint, passed. It merged at
  [`dbaecade`](https://github.com/FS-GG/FS.GG.Coordination/commit/dbaecade8d47d872336601b3b3bb9785082dbb81)
  and [protected-main run 33362412616](https://github.com/FS-GG/FS.GG.Coordination/actions/runs/33362412616)
  passed. The implementation completion digest is
  `1e1f1b894b9449ab8fa1bacfc175ef23da1350f33bed09915ff55b09fcec88f`; the acceptance
  [completion receipt](https://github.com/FS-GG/FS.GG.Coordination/issues/125#issuecomment-5474420007)
  is `a7eb421bff718dc81cef9500f17ebf4d46bfefa7612e3215e4bc225550950b3c`.

  The landing exposed a second class boundary: the released delivery client sent an exact head but relied on
  GitHub's implicit merge-commit default, while the repository was squash-only. The safe exact-head squash
  fallback landed this accepted candidate; the standard second-defect response then stopped retries and
  redesigned delivery to observe typed merge capabilities, choose deterministically `squash > rebase > merge`,
  serialize the method explicitly, and refuse before any write when policy is unreadable or permits none.
  The class repair is tracked by [`.github#3091`](https://github.com/FS-GG/.github/issues/3091) and
  [PR 3092](https://github.com/FS-GG/.github/pull/3092). One Accountable Delivery Owner made every decision;
  no external, multiple, or approval-count authorization was required.

- [x] **GS2-04.2 — Issue/type/field adapter.** Resolve semantic identities to live IDs, verify type and
  option sets, read complete values, and plan guarded create/update/clear operations.
- [x] **GS2-04.3 — Native relation adapter.** Read complete hierarchy/dependency sets and perform
  add/remove with stale re-read and post-state verification.

  Accepted 2026-08-31. [Implementation PR 138](https://github.com/FS-GG/FS.GG.Coordination/pull/138)
  passed [exact-head qualification run 33399640438](https://github.com/FS-GG/FS.GG.Coordination/actions/runs/33399640438)
  and merged as protected commit
  [`ee0353ac`](https://github.com/FS-GG/FS.GG.Coordination/commit/ee0353acc753fb33d02ff3addeeb03bb1b2b2c4c),
  whose [protected-main run 33400527635](https://github.com/FS-GG/FS.GG.Coordination/actions/runs/33400527635)
  passed. [Acceptance PR 140](https://github.com/FS-GG/FS.GG.Coordination/pull/140) added the append-only
  [GS2-04.3 receipt](https://github.com/FS-GG/FS.GG.Coordination/blob/c524f8410323e7a79a6bab4ad691a99e75f530e1/evidence/github-substrate-v2/accepted/GS2-04.3.json)
  with self-digest `210ae250d081c39ec422d59c9bd4a72c653650380497aef586164b6fc3a52507`,
  merged at [`c524f841`](https://github.com/FS-GG/FS.GG.Coordination/commit/c524f8410323e7a79a6bab4ad691a99e75f530e1),
  and passed [protected-main run 33402803310](https://github.com/FS-GG/FS.GG.Coordination/actions/runs/33402803310).

- [x] **GS2-04.4 — Project adapter.** Treat membership and Status as projections; handle archived,
  duplicated, external, draft, missing, and unreadable items without inventing absence.

  Accepted 2026-08-31. [Implementation PR 144](https://github.com/FS-GG/FS.GG.Coordination/pull/144)
  passed [exact-head qualification run 33409563736](https://github.com/FS-GG/FS.GG.Coordination/actions/runs/33409563736)
  with canonical Quint reused, and merged as protected commit
  [`36abdce0`](https://github.com/FS-GG/FS.GG.Coordination/commit/36abdce0a907b0acd3d8ace1f4ac6d6491f4e080),
  whose [protected-main run 33410269364](https://github.com/FS-GG/FS.GG.Coordination/actions/runs/33410269364)
  passed. [Acceptance PR 146](https://github.com/FS-GG/FS.GG.Coordination/pull/146) added the append-only
  [GS2-04.4 receipt](https://github.com/FS-GG/FS.GG.Coordination/blob/62396de0b38a13fd8f2cb5fec7ba9e8e42770823/evidence/github-substrate-v2/accepted/GS2-04.4.json)
  with self-digest `a2a71df6fe5f871c8f40b353c161b58fbb2206643b02dcb3ef5987802a82c252`,
  merged at [`62396de0`](https://github.com/FS-GG/FS.GG.Coordination/commit/62396de0b38a13fd8f2cb5fec7ba9e8e42770823),
  and passed [protected-main run 33412110063](https://github.com/FS-GG/FS.GG.Coordination/actions/runs/33412110063).

- [x] **GS2-04.5 — Comment/projection adapter.** Preserve server-issued identity/order, validate marker
  JSON and referenced journal digests, distinguish edit/delete/tamper, and regenerate human projections
  from durable authority. A comment never authorizes a concurrency-sensitive transition.

  Accepted 2026-08-31. [Implementation PR 150](https://github.com/FS-GG/FS.GG.Coordination/pull/150)
  passed [exact-head qualification run 33433701416](https://github.com/FS-GG/FS.GG.Coordination/actions/runs/33433701416)
  and merged as protected commit
  [`76782e3f`](https://github.com/FS-GG/FS.GG.Coordination/commit/76782e3f86646f2303d0aad90b28d27198a4f7ef),
  whose [protected-main run 33434459359](https://github.com/FS-GG/FS.GG.Coordination/actions/runs/33434459359)
  passed. [Acceptance PR 152](https://github.com/FS-GG/FS.GG.Coordination/pull/152) added the append-only
  [GS2-04.5 receipt](https://github.com/FS-GG/FS.GG.Coordination/blob/a8e6c061918cc351de76d7a42be4f1c2a1792686/evidence/github-substrate-v2/accepted/GS2-04.5.json)
  with self-digest `27b27b76cf52fca137059aa466c7922dc096cae33c0a4e1045735bd937497091`,
  merged at [`a8e6c061`](https://github.com/FS-GG/FS.GG.Coordination/commit/a8e6c061918cc351de76d7a42be4f1c2a1792686),
  and passed [protected-main run 33436270111](https://github.com/FS-GG/FS.GG.Coordination/actions/runs/33436270111).

- [x] **GS2-04.6 — Sharded Git journal adapter.** Perform protected expected-parent commits for claim,
  review, operation, and global cutover aggregates in the dedicated `FS.GG.Coordination.Authority`
  repository. Use `refs/heads/fsgg/v2/journal/<kind>/<shard>`, explicit old-OID
  `--force-with-lease` receive-pack CAS, the split `v2-journal-writer` and `v2-journal-integrity`
  rulesets targeting `refs/heads/fsgg/v2/journal/**/*`, and a repository-limited journal App token;
  read back both rulesets and effective branch rules.
  Issue monotonically increasing fencing generations; verify ancestry; create immutable phase anchors;
  detect rewind/deletion/divergence; compact only after a terminal checkpoint; and project state to issues
  without making one fleet-wide normal-operation ref. Custom refs and API-path-scoped bypass claims fail.

  Accepted 2026-09-01. [Implementation PR 156](https://github.com/FS-GG/FS.GG.Coordination/pull/156)
  passed [exact-head qualification run 33443440969](https://github.com/FS-GG/FS.GG.Coordination/actions/runs/33443440969)
  and merged as protected commit
  [`b6c25c0b`](https://github.com/FS-GG/FS.GG.Coordination/commit/b6c25c0b3f26211d4cfcfcdc8f08f9b87e43586c),
  whose [protected-main run 33444027453](https://github.com/FS-GG/FS.GG.Coordination/actions/runs/33444027453)
  passed. [Acceptance PR 158](https://github.com/FS-GG/FS.GG.Coordination/pull/158) added the append-only
  [GS2-04.6 receipt](https://github.com/FS-GG/FS.GG.Coordination/blob/53fe450f8d60fdfe4cc68aaaeaa181666796e31a/evidence/github-substrate-v2/accepted/GS2-04.6.json)
  with self-digest `4011bbe0d7e2db27aff2b6e0a36d9bc342dc89e8310b56d4e358b6d73bc96511`,
  merged at [`53fe450f`](https://github.com/FS-GG/FS.GG.Coordination/commit/53fe450f8d60fdfe4cc68aaaeaa181666796e31a),
  and passed [protected-main run 33445770572](https://github.com/FS-GG/FS.GG.Coordination/actions/runs/33445770572).
- [x] **GS2-04.7 — Repository/settings adapter.** Inspect and plan custom properties, rulesets, merge
  policies, Actions policy, environments, releases, tags, security, and dependency features with supported,
  unauthorized, and unavailable outcomes.

  Accepted 2026-09-01. [Implementation PR 162](https://github.com/FS-GG/FS.GG.Coordination/pull/162)
  merged as protected commit
  [`99dd3ca2`](https://github.com/FS-GG/FS.GG.Coordination/commit/99dd3ca27df05dc65ffea2b1c513c0423460f51a),
  whose [protected-main run 33450426877](https://github.com/FS-GG/FS.GG.Coordination/actions/runs/33450426877)
  passed. [Acceptance PR 164](https://github.com/FS-GG/FS.GG.Coordination/pull/164) added the append-only
  [GS2-04.7 receipt](https://github.com/FS-GG/FS.GG.Coordination/blob/89d0dda060efbe28d2dd25ad5a475fe865392b6f/evidence/github-substrate-v2/accepted/GS2-04.7.json)
  with self-digest `5a54e22d217044a4b5cc2424c8d7e8e24f86dbbe8c6d6373c0c487819a930acf`,
  merged at [`89d0dda0`](https://github.com/FS-GG/FS.GG.Coordination/commit/89d0dda060efbe28d2dd25ad5a475fe865392b6f),
  and passed [protected-main run 33451465408](https://github.com/FS-GG/FS.GG.Coordination/actions/runs/33451465408).
- [ ] **GS2-04.8 — Actions/release/feed adapter.** Observe runs/checks/merge groups, immutable releases,
  attestations, packages, and public downloads without treating upload responses as served artifacts.
- [ ] **GS2-04.9 — Sandbox qualification and comprehensive GS2-04 closure.** Exercise destructive create/update/delete/rollback behavior in
  isolated test repositories and a test Project using non-production identities and quotas.
  Bind every accepted GS2-04 child receipt, execute Q3/Q4 and all repository qualification gates cold under
  ADR-0080 comprehensive mode, and retain the protected GS2-04 closure receipt before GS2-05 may consume the
  adapter milestone.

### GS2-05 — Implement the native work and roadmap model

**Parents:** `.github#2954`, `.github#2960`, `.github#2963`
**Owner:** `FS.GG.Coordination`, with `.github` schema instances
**Depends on:** GS2-02–GS2-04
**Exit:** complete work lifecycle passes Q2–Q5 without production writes

- [ ] **GS2-05.1 — Finalize taxonomy.** Ratify native issue types and eliminate parallel Class/Kind
  authority. Define exact migration for every current combination and refuse ambiguity.
- [ ] **GS2-05.2 — Finalize organization issue fields.** Specify scheduling, hold reason, priority,
  effort, dates, severity, phase, workstream, contract, and touch-set projection with minimal vocabularies.
- [ ] **GS2-05.3 — Implement intake.** Provide pure validate/plan, exact-plan apply, and live inspect for
  issues, fields, Project membership, hierarchy, dependencies, and protocol initialization.
- [ ] **GS2-05.4 — Implement roadmap intake.** Compile a roadmap into an Epic, bounded work issues,
  parent edges, dependency edges, dates/fields, and drift inspection without making Project fields the
  execution ledger.
- [ ] **GS2-05.5 — Implement claims/touch sets.** Use protected journal expected-parent CAS plus a fencing
  generation for each claim and conflict domain; treat lease expiry only as successor eligibility; use the
  native field/comment projections only as candidate prefilters; acquire multi-touch grants in a
  deterministic compensatable plan; and re-prove exclusion and generation at every external effect.
- [ ] **GS2-05.6 — Implement review/delivery.** Bind accountable critique evidence to an immutable full-snapshot
  `ReviewEpochKey` separate from its stable chain identity; allow succession only inside one epoch; require
  a fresh phase seat after any snapshot change without requiring another authority; distinguish merged from
  protected post-merge verification; and generate journal-bound delivery/done receipts.
- [ ] **GS2-05.7 — Implement lifecycle projection.** Derive Status from scheduling intent, holds,
  dependencies, claim, PR/review, delivery, and issue state; no operator writes derived state as intent.
- [ ] **GS2-05.8 — Shadow the complete live fleet.** Compare v1 and v2 read-only decisions, record every
  divergence, and reach zero unexplained divergence without granting v2 mutation permission.

### GS2-06 — Implement desired state, CI, release, and supply-chain policy

**Parents:** `.github#2955`, `.github#2956`, `.github#2957`, `.github#2958`, `.github#2963`
**Owner:** `FS.GG.Coordination` model; `.github` instances and reusable entry points
**Depends on:** GS2-02–GS2-05
**Exit:** every rostered repository has a verified plan and rollback, not yet necessarily applied

- [ ] **GS2-06.1 — Repository profiles.** Derive expected settings from the reviewed roster and project
  selected attributes into native custom properties; retain external rows and rich registry authority.
- [ ] **GS2-06.2 — Required-check census.** Union classic protection and rulesets, classify every check,
  prove unconditional PR/merge-group production, and reduce the external contract to stable aggregates.
- [ ] **GS2-06.3 — Ruleset plans.** Define branch/tag protection, reviews, conversations, merge methods,
  auto-merge/queue, branch deletion, bypass principals, and expiring exceptions per profile.
- [ ] **GS2-06.4 — Immutable execution pins.** Move reusable workflows and third-party Actions to full
  commit SHAs, define immutable workflow publication, and retain Renovate as the sole automated update path.
- [ ] **GS2-06.5 — Permission compilation.** Derive App and workflow permissions from registered
  interpreters; separate normal coordination, admin/cutover, and release principals/environments.
- [ ] **GS2-06.6 — Release hardening.** Preserve OIDC and dual-feed saga semantics while adding protected
  environments, immutable releases/tags, one pack, SBOMs, attestations, dependency submission/review, and
  public-download verification.
- [ ] **GS2-06.7 — Workflow consolidation and change-impact selection.** Replace duplicated policy jobs
  with typed inventory, composite steps, reusable job contracts, and stable aggregate outputs. Compile a
  versioned dependency graph from changed subjects and non-file inputs to the smallest sound transitive
  closure of build, test, policy, coordination, packaging, and release obligations; policy may still mark
  an obligation unconditional. Required aggregates always resolve, but an unselected child reports a typed
  `NotApplicable` reason without provisioning its expensive job. Unknown, ambiguous, stale, or incomplete
  impact fails closed rather than silently skipping work, and merge-group selection is recomputed against
  the queued head and current base/settings. Independently test representative source, test, workflow,
  dependency, generated-output, documentation, policy, and release changes plus mixed and unknown changes.
  Before cutover, record per-repository baselines and accepted targets for workflow/job fan-out, billed
  minutes, queue time, and p50/p95 completion time, and prove the selector meets them without a missed
  obligation. Keep a small unconditional core suite, run scheduled full-suite sentinels that compare the
  selected closure with actual failures, and disable selection fleet-wide after any missed obligation.
  Record every removed workflow and obligation.
- [ ] **GS2-06.8 — Fleet dry plans.** Inspect, plan, serialize, review, and re-inspect all repository
  settings without applying them. Unsupported plan/permission cases receive explicit dispositions.

### GS2-07 — Implement event reconciliation and merge readiness

**Parents:** `.github#2961`, `.github#2962`, `.github#2963`
**Owner:** `FS.GG.Coordination`
**Depends on:** GS2-04–GS2-06
**Exit:** events and audits converge; merge-group policy is qualified before any production queue

- [ ] **GS2-07.1 — Event envelope and cursor.** Normalize source, delivery/event identity, subject,
  revision, causation, correlation, and receipt; duplicate and reordered delivery is idempotent.
- [ ] **GS2-07.2 — Narrow reconciliation.** Route supported issue, relation, Project, repository, ruleset,
  run/check, release, and installation events to a deduplicating subject queue. Commands and events only
  schedule work; the shared fresh-observe/reduce/sealed-plan/apply/verify reconciler is the exclusive normal
  writer path.
- [ ] **GS2-07.3 — Audit repair.** Retain a complete scheduled audit as authority for dropped deliveries,
  preview gaps, external repositories, and schema drift; prove event/audit convergence under replay.
- [ ] **GS2-07.4 — Event security.** Verify signatures, installation/repository scope, replay bounds,
  payload/API disagreement, and least privilege; events schedule reconciliation but never directly mutate
  derived state.
- [ ] **GS2-07.5 — Merge-group support.** Ensure every aggregate required check runs on merge groups and
  re-evaluates temporal claim, review, head, dependency, and release obligations.
- [ ] **GS2-07.6 — Queue sandbox/pilot.** Exercise queue admission, base movement, check growth, expiry,
  failure recovery, and rollback in a low-volume isolated or representative repository before fleet enablement.
- [ ] **GS2-07.7 — Measure event benefit.** Record latency, dropped-event repair, API cost, schedule count,
  and false/unknown outcomes; reduce polling only from evidence.
- [ ] **GS2-07.8 — Qualify runtime operations.** Exercise deploy, rollback, secret rotation, outage,
  replay/backlog recovery, regional/provider failure where applicable, log redaction, alert routing, and
  emergency disable. A webhook host that cannot be operated safely does not enter the production plan.

### GS2-08 — Ship the universal v1 bridge and protected epoch ledger

**Parent:** `.github#2964`
**Owner:** `.github` for v1; `FS.GG.Coordination` for contract and independent tests
**Depends on:** GS2-00; GS2-02 epoch/manifest vocabulary before publication
**Exit:** every released v1 writer is fenced fleet-wide

- [ ] **GS2-08.1 — Freeze the epoch wire contract.** Define states including `ObservingV2` and
  `ContractingV1`, legal transitions, manifest binding, ledger/ref/tag layout, ancestry proof, failure
  semantics, caching ceiling, operation-generation fencing, and issue projection.
- [ ] **GS2-08.2 — Complete ledger protections.** Preserve and continuously audit the authority
  repository's split branch rulesets; add immutable tag rules, the protected `fleet-cutover` environment,
  a contents-only selected-repository journal App (or explicit security acceptance of the shared App),
  control issue, effective-rule readback, and tamper/rewind monitoring.
- [ ] **GS2-08.3 — Map every v1 writer.** Turn the GS2-00 mutation census into an executable coverage list;
  unknown or dynamically discovered write entry points fail the bridge build.
- [ ] **GS2-08.4 — Add one common precondition.** Every normal v1 mutation entry reads and verifies the
  fresh ledger epoch before its first effect. Unreadable, contradictory, frozen, switched, or v2-open state
  refuses before write.
- [ ] **GS2-08.5 — Preserve OperatingV1 behavior.** Current regression/corpus behavior remains unchanged
  when the verified epoch is `OperatingV1`; the bridge adds no second semantic authority.
- [ ] **GS2-08.6 — Independently attack the fence.** From outside the v1 test generator, attempt every
  write class under all epochs, stale cache, lost response, ledger rewind, missing tag, wrong manifest,
  permission loss, and older client versions.
- [ ] **GS2-08.7 — Publish the bridge.** Build once, sign/attest, publish to required feeds, verify public
  installation, and record exact tool/kit/workflow identities.
- [ ] **GS2-08.8 — Adopt all receivers.** Update `.github`, SDD, Rendering, Governance, Templates, Game,
  Audio, and Net; resolve superseded dependency-update PRs; prove each live route uses the exact bridge.
- [ ] **GS2-08.9 — Seal unfenceable clients.** If an old writer cannot read the epoch, disable/revoke its
  dispatch, credential, schedule, or installation before freeze and record that as its fence proof.

### GS2-09 — Build migration, archive, and rollback tooling

**Parents:** `.github#2954`, `.github#2963`, `.github#2965`
**Owner:** `FS.GG.Coordination`
**Depends on:** GS2-05–GS2-08
**Exit gates:** Q5 and Q6 over full snapshots

- [ ] **GS2-09.1 — Implement complete discovery.** Read every open and relevant closed issue, Project
  item, field, hierarchy/dependency edge, claim/event stream, review/delivery/release record, repository
  setting, workflow pin, and receiver identity. Retain terminal pagination proofs and per-authority
  high-water marks, then require two complete quiescent reads with identical normalized digests.
- [ ] **GS2-09.2 — Implement the immutable manifest.** Bind old/new model and artifact fingerprints,
  global IDs, old bytes/values, v2 results, live operations, receiver heads, settings plans, archive digests,
  dispositions, phase plans, reviewers, and rollback inputs.
- [ ] **GS2-09.3 — Implement typed transforms.** Map taxonomy, planning fields, repo scope, body metadata,
  blockers, hierarchy, scheduling holds, touch sets, lifecycle receipts, and desired settings as
  `Migrated`, `Ambiguous`, or `Unsupported`.
- [ ] **GS2-09.4 — Implement live-operation handling.** For each claim, queued write, review, delivery,
  release, and cutover-adjacent operation, choose drain, migrate, park, or explicit invalid disposition.
- [ ] **GS2-09.5 — Implement sealed history.** Preserve source schema/bytes/digests, verifier artifact,
  expected outcomes, and lookup index without putting permanent v1 upcasters in the v2 production closure.
- [ ] **GS2-09.6 — Implement rollback plans.** Restore settings, receiver pins, v1 projections, schedules,
  and authority snapshot through `VerifiedV2`; make each step resumable from receipts.
- [ ] **GS2-09.7 — Rehearse on representative copies.** Run migrate, interrupt every step, retry,
  rollback, re-run, and archive verification in isolated repositories/Project snapshots.
- [ ] **GS2-09.8 — Prove idempotency and no omission.** Re-running an exact manifest changes nothing;
  adding one unknown live subject or losing one page prevents qualification.

### GS2-10 — Qualify the exact candidate and prepare the fleet

**Parent:** `.github#2965`
**Owner:** `FS.GG.Coordination`, `.github`, and every receiver
**Depends on:** GS2-03–GS2-09
**Exit gates:** Q0–Q7 accepted against one immutable candidate

- [ ] **GS2-10.1 — Freeze candidate identities.** Record source commits, dependency locks, model/compiler
  fingerprints, packages, container/tool assets if any, workflows, App build, verifier artifacts, Typed
  SDD lifecycle-default decision, provider/scaffolder identities, and every receiver head/settings profile.
- [ ] **GS2-10.2 — Run the full qualification matrix.** No selective rerun may replace a failed full
  result; repairs create a new candidate identity.
- [ ] **GS2-10.3 — Complete live shadow comparison.** Read the complete fleet repeatedly over a bounded
  operational window, explain all v1/v2 differences, and prove v2 has no production write permission.
- [ ] **GS2-10.4 — Generate the cutover manifest.** Capture the complete live source, transformations,
  archive, prepared receiver heads, settings plans, exact phase operations, and rollback plans.
- [ ] **GS2-10.5 — Prepare receiver changes.** Create exact-head, green PRs or protected plans for pins,
  workflows, rulesets, settings, environments, and permissions; do not merge/apply switch changes yet.
- [ ] **GS2-10.6 — Drain backlog hazards.** Resolve or disposition obsolete Renovate PRs, coordination-tool
  adoption PRs, release candidates, conflicting settings changes, and work expected to cross the window.
- [ ] **GS2-10.7 — Rehearse the whole cutover.** Execute freeze through rollback and freeze through
  simulated `OpenV2` in isolated fleet replicas; record durations, API budgets, operator decisions, and all
  manual steps.
- [ ] **GS2-10.8 — Approve readiness.** Independent architecture, security, operations, migration, and
  receiver reviewers accept the exact candidate/manifest. Any source or plan change invalidates approval.
- [ ] **GS2-10.9 — Close the concurrent-change gate.** Prove there is no active kernel publication,
  lifecycle-default flip, provider/registry flip, coordination receiver change, reusable-workflow change,
  repository-settings mutation, or release saga expected to cross the cutover window. Defer each remaining
  row until `OperatingV2` or mint a new candidate and repeat the complete Q0–Q7 matrix.

### GS2-11 — Freeze the production fleet

**Parent:** `.github#2965`
**Owner:** protected cutover operators
**Depends on:** GS2-10; approved cutover window
**Exit:** authoritative `Frozen(snapshot)`; all normal writes demonstrably closed

- [ ] **GS2-11.1 — Announce and verify the window.** Confirm operators, approvers, communication channel,
  status page, abort criteria, rate budget, credentials, backups, and candidate/manifest fingerprints.
- [ ] **GS2-11.2 — Acquire the cutover grant.** Enter the protected environment and commit/anchor
  `FreezeRequested(manifest)` with exact expected parent.
- [ ] **GS2-11.3 — Stop ingress.** Refuse new claims, intake/roadmap applies, board mutations, review and
  delivery advances, coordination merges/dispatches, settings reconciles, releases, lifecycle-default
  changes, provider/registry flips, and receiver updates.
- [ ] **GS2-11.4 — Drain active work.** Complete, release, or explicitly park every claim, queued write,
  review, delivery, merge election, operation lock, and release saga. Active count must reach zero.
- [ ] **GS2-11.5 — Restrict repositories temporarily.** Apply the approved update restrictions with only
  the cutover App/operator bypass; verify every repository and exception.
- [ ] **GS2-11.6 — Prove the fence.** Attempt representative operations through every client/workflow/App
  generation; all normal writes must refuse for the epoch reason before external effect.
- [ ] **GS2-11.7 — Take the frozen snapshot.** Perform two complete reads, prove no intervening mutation,
  bind the result to the manifest, and commit/anchor `Frozen(snapshot)`.
- [ ] **GS2-11.8 — Decide continue or rollback.** Any active writer, unreadable authority, manifest drift,
  unplanned head/settings change, or unsettled operation executes the rollback plan before switch.

### GS2-12 — Switch and verify while the fleet remains closed

**Parent:** `.github#2965`
**Owner:** protected cutover operators
**Depends on:** authoritative Frozen state
**Exit gate:** Q8 and authoritative `VerifiedV2(evidence)`

- [ ] **GS2-12.1 — Activate exact v2 artifacts.** Promote/install only the accepted candidate; verify
  package bytes, model identity, App build, workflow SHAs, and receiver resolution.
- [ ] **GS2-12.2 — Apply receiver switch changes.** Merge/apply prepared heads in the recorded dependency
  order and refuse any changed head, run set, or settings precondition.
- [ ] **GS2-12.3 — Migrate authority.** Apply issue types/fields, native hierarchy/dependencies, scheduling
  holds, touch-set streams/projections, Project membership/status, and other authoritative transformations.
- [ ] **GS2-12.4 — Apply desired settings.** Install custom properties, rulesets, aggregate checks,
  immutable pins, App permissions, environments, release/security features, and event routes.
- [ ] **GS2-12.5 — Disable v1 execution.** Turn off old schedules, dispatch routes, workflows, credentials,
  and writers without deleting rollback assets; commit/anchor `SwitchedV2(candidate)`.
- [ ] **GS2-12.6 — Verify schema and migration.** Re-read every migrated subject, relation, field,
  projection, archive digest, repository setting, receiver, and phase receipt against the manifest.
- [ ] **GS2-12.7 — Run closed-fleet canaries.** In the isolated cutover program, exercise intake,
  roadmap, hierarchy, dependency, claim/touch set, review, delivery/done, settings, event repair, merge group,
  and release observation without opening ordinary production writes.
- [ ] **GS2-12.8 — Inject wrong paths.** Test stale/missing/contradictory/partial/rate-limited/unauthorized
  observations, lost webhooks, altered fields, wrong receiver, missing check, package-not-served, and ledger
  tamper; each must refuse, repair, or remain explicitly indeterminate.
- [ ] **GS2-12.9 — Rehearse rollback from the switched state.** Verify every rollback precondition and
  operation without deleting v2 evidence. If any rollback step is not executable, do not open.
- [ ] **GS2-12.10 — Commit verification.** Independent reviewers accept Q8 and the operator commits/anchors
  `VerifiedV2(evidence)`. Failure chooses repair-and-reverify or rollback while writes remain closed.

### GS2-13 — Open v2, fence v1, and enter observation

**Parent:** `.github#2965`
**Owner:** protected cutover operators followed by repository maintainers
**Depends on:** authoritative VerifiedV2 state and final human approval
**Exit gate:** Q9 authoring-fence proof and authoritative `ObservingV2`

- [ ] **GS2-13.1 — Present the irreversible decision.** Show exact manifest/candidate, Q0–Q8 roll-up,
  open risks, rollback status, operational ownership, and the consequence that v1 cannot resume afterward.
- [ ] **GS2-13.2 — Commit `OpenV2`.** Obtain protected human approval, commit/anchor
  `OpenV2(acceptance)`, verify it independently, then enable only v2 normal writers.
- [ ] **GS2-13.3 — Complete one bounded real journey.** Take a low-risk work item from intake through
  native hierarchy/dependency, claim/touch set, review, delivery, merge/post-merge verification, and done.
- [ ] **GS2-13.4 — Establish operational watch.** Monitor errors, indeterminate operations, repair lag,
  event backlog, API budget, queue/CI latency, release state, and old-client attempts; recovery is roll-forward.
- [ ] **GS2-13.5 — Disable v1 authoring.** Revoke old write credentials, schedules, dispatch routes,
  installations, moving-ref exceptions, and mutation entry points. Retained source/binaries are inert and
  no longer resolvable from a normal production route.
- [ ] **GS2-13.6 — Prove the permanent fence.** Attempt every v1 write class and representative old-client
  generation after `OpenV2`; each refuses before external effect for independently observed epoch,
  credential, route, or installation reasons.
- [ ] **GS2-13.7 — Seal observation assets.** Publish the content-addressed v1 archive, verifier, manifest,
  lookup guide, retained inert recovery inputs, and 30-day retention/contraction plan outside the v2
  production dependency closure.
- [ ] **GS2-13.8 — Normalize safe repository policies.** Remove only temporary freeze restrictions needed
  to operate v2, enable approved merge queues/settings, retain contraction safeguards, and re-inspect every
  repository profile.
- [ ] **GS2-13.9 — Commit `ObservingV2`.** Bind the open receipt, permanent v1 authoring-fence proof,
  first real journey, operational dashboard, sealed assets, and fixed 0/7/14/30-day reading definitions.
- [ ] **GS2-13.10 — Hand off the observation.** Assign owners and SLOs for incidents, indeterminate
  operations, action items, old-client attempts, and the later contraction; no destructive v1 deletion is
  allowed before the 30-day gate.

### GS2-14 — Observe, improve, and close the renovation

**Parent:** `.github#2965` and Epic `.github#2952`
**Owner:** `FS.GG.Coordination` and `.github`
**Depends on:** ObservingV2
**Exit gate:** Q10, authoritative `OperatingV2`, and closed Epic

- [ ] **GS2-14.1 — Record the immediate reading.** At 0 days capture journey success, incident count,
  partial/indeterminate operations, old-client attempts, API cost, event repair, queue/CI/release latency,
  and remaining deletion debt.
- [ ] **GS2-14.2 — Record 7-, 14-, and 30-day readings.** Use identical definitions and source-bound
  evidence; do not hide failures by changing denominators or suppressing findings.
- [ ] **GS2-14.3 — Complete remaining roll-forward repairs.** Each incident receives a typed cause,
  bounded fix, regression oracle, and evidence; recurring missing concepts return to the specification.
- [ ] **GS2-14.4 — Approve contraction.** After the unchanged 30-day definition passes, independently
  review incidents, open action items, old-client attempts, sealed assets, and exact deletion plans; commit
  `ContractingV1(plan)` or extend observation without changing the metric denominator.
- [ ] **GS2-14.5 — Delete v1 runtime code.** Remove v1 readers/writers, public generic mutation routes,
  compatibility adapters, old event/schema decoders, and source packages/workflows after exact static and
  runtime inventory checks.
- [ ] **GS2-14.6 — Delete v1 data authorities.** Remove Class/Kind/Repo Scope/Blocked-by Project fields,
  body sentinels/metadata parsers, old status writers, control comments used as authority, and temporary
  backfill projections after exact deletion checks.
- [ ] **GS2-14.7 — Delete v1 operational infrastructure.** Remove remaining obsolete App permissions,
  environments, branch/ruleset exemptions, caches, and retained recovery assets that are unsafe after the
  observation gate; preserve only the sealed audit package and verifier.
- [ ] **GS2-14.8 — Verify deletion and sealed-history access.** A clean checkout/install contains no v1
  production path, every old client remains fenced, and a maintainer can still explain and verify every
  archived operation.
- [ ] **GS2-14.9 — Reconcile public architecture and operations docs.** Update the component map,
  coordination guide, recovery guide, security model, release guide, skills, and website status from v2
  authority; remove renovation warnings only when the contraction gate passes.
- [ ] **GS2-14.10 — Release deferred programs.** Re-observe the normalized receiver fleet and authorize
  P5 or other `cutover-deferred` work to resume through v2. Do not reuse a pre-cutover claim, review,
  candidate, or release receipt.
- [ ] **GS2-14.11 — Commit `OperatingV2`.** Bind the completed deletion ledger, clean-install/runtime
  absence proof, sealed-history verification, normalized settings, and final Q9/Q10 evidence.
- [ ] **GS2-14.12 — Close children and Epic.** Verify every child issue and native subissue relationship,
  accept Q10, publish the final report, and close `.github#2952` only when no legacy production authority or
  unowned follow-up remains.

## 6. Work intentionally outside the cutover critical path

The following work may use the same kernel or GitHub capabilities, but it is not necessary to make v2 live
or v1 retired. It must not delay `GS2-11` once all actual cutover prerequisites are qualified:

- [`.github#2959`](https://github.com/FS-GG/.github/issues/2959), the advisory-only GitHub Agentic
  Workflows pilot;
- migration of the ADR corpus to a future typed `DecisionExtension`;
- broader Typed SDD extensions for contract topology, skill delivery, Governance rules, provider/template
  composition, and executable TestSpecs;
- a later decision to make `typed-sdd` the default lifecycle, subject to the `GS2-10` candidate-freeze
  rule above; and
- convenience UI, reports, or projections that do not authorize a coordination decision.

These may proceed independently with their own evidence. If one becomes a real prerequisite, the governing
design and this dependency map must be amended before it can block the fleet cutover.

## 7. Fleet-by-fleet adoption checklist

For `.github`, SDD, Rendering, Governance, Templates, Game, Audio, and Net, the cutover manifest must carry
one row proving all applicable obligations:

- [ ] bridge artifact and epoch behavior;
- [ ] exact v2 tool/kit/workflow pins;
- [ ] repository custom properties and desired profile;
- [ ] required aggregate checks on pull requests and merge groups;
- [ ] immutable Actions/reusable-workflow references;
- [ ] App installation and least permissions;
- [ ] branch/tag rulesets, merge policy, bypass, and temporary freeze restriction;
- [ ] release environment, OIDC, immutable release/tag, SBOM, attestation, and feed behavior where publishing;
- [ ] webhook/event coverage and scheduled audit repair;
- [ ] open claims, reviews, deliveries, releases, queues, and dependency-update PR disposition;
- [ ] migration and rollback receipts; and
- [ ] post-retirement absence of v1 writers and configuration.

External roster rows are observed and reported. They change only through their owner and an explicit
disposition; the FS-GG cutover must not silently assume administrative authority over them.

## 8. Mandatory failure matrix

The qualification corpus must include at least these independent controls:

- an old client attempts every write class after `FreezeRequested`;
- the epoch ref, ancestry, phase tag, manifest digest, or issue projection disagrees;
- a page truncates while total count or terminal cursor claims more data;
- GitHub returns success but authoritative re-read is absent or contradictory;
- a relation changes concurrently between plan and apply;
- a Project item is missing, duplicated, archived, draft, external, or unreadable;
- an issue field exists under the wrong data type or option vocabulary;
- a claim/touch-set projection is stale, edited, deleted, or from another generation;
- two workers or executors race claim, operation-lock, relation, review, and delivery decisions;
- a webhook is duplicated, reordered, dropped, forged, or outside installation scope;
- a required check is absent, path-filtered, renamed, stale-green, or missing on merge group;
- a receiver resolves the wrong package, workflow SHA, model fingerprint, or settings profile;
- a ruleset/settings plan partially applies or loses permission mid-operation;
- a package upload succeeds but either feed or public download does not serve the exact bytes;
- an immutable release/tag rejects an attempted rewrite needed by a mistaken recovery path;
- rollback is interrupted after every step through `VerifiedV2`;
- `OpenV2` is attempted without exact protected approval or complete Q8 evidence;
- v1 deletion starts before `OpenV2`, or a v1 production path survives Q9; and
- a generated test set is self-consistent while an independent black-box oracle disagrees.

## 9. Stop and return to design when

- a unit requires an untyped authority, mutation, or permission escape hatch;
- GitHub cannot provide the revision/completeness semantics assigned to a native authority;
- a v1 writer cannot be fenced, disabled, or credential-revoked before freeze;
- a normal v1 and v2 writer must be active simultaneously;
- the candidate changes after qualification without receiving a new identity and full rerun;
- migration needs a heuristic default for an ambiguous semantic fact;
- live work cannot be drained or migrated without rewriting accepted evidence;
- rollback through `VerifiedV2` depends on deleting new evidence;
- merge queue can bypass or omit a temporal required check;
- a cutover operation exceeds its measured API/permission/time envelope with no reviewed alternative;
- the new repository starts copying generic kernel or another component's private domain union; or
- added compatibility, workflow, command, and parser surface exceeds the accepted deletion ledger.

## 10. Definition of live and retired

The new system is **live** only when:

1. the protected ledger is at `OperatingV2` descending from the accepted manifest;
2. only exact qualified v2 artifacts can perform normal coordination mutations;
3. native issue types/fields/relations and desired repository settings match the compiled model;
4. claims, touch sets, reviews, delivery, releases, and repair paths retain their required custom guarantees;
5. event and full-audit reconciliation converge;
6. every receiver resolves the expected immutable artifacts and required checks; and
7. the first bounded real journey and immediate operational reading are accepted.

V1 is **retired** only when:

1. no executable, workflow, schedule, App route, credential, public command, or admin recipe can author v1
   production state;
2. old Project fields and issue-body semantic parsers no longer influence decisions;
3. compatibility and migration code is absent from the production dependency closure;
4. temporary cutover bypasses and restrictions are removed;
5. historical state is sealed with independently runnable verification;
6. old clients fail closed against the v2 epoch; and
7. Q9 and the 30-day Q10 reading report no unowned legacy authority.

Closing an issue, merging the last implementation PR, or setting a Project card to Done is not by itself
evidence that either definition holds.

## 11. Updating this roadmap

- Architecture changes require the governing design and ADR to change first.
- Sequence, prerequisites, unit boundaries, and exit-gate changes update this file.
- Typed SDD P5, generic-kernel, provider/scaffolder, registry, workflow, receiver, or settings changes
  update the concurrent-change ledger until `GS2-10`; after that point they invalidate the candidate or are
  recorded as `cutover-deferred`.
- A former M-series issue may not return to a schedulable state without an explicit mapping showing which
  GS2 requirement is missing and why the existing transfer does not already own it.
- Implementation semantics live in the typed `FS.GG.Coordination` specification and are projected here by
  stable unit/subject links once that compiler exists; do not copy union cases into this roadmap.
- Accepted unit receipts append their source/artifact/evidence links to the unit; they do not replace its
  acceptance text.
- A newly discovered requirement is recorded even if the vocabulary is missing. It blocks the affected
  transition until the specification is extended; discovery itself is never suppressed.
- The “Ongoing renovations” website notice remains until `GS2-14.5` and the 30-day Q10 gate are accepted.
