# V2 roadmap reconciliation against repository code

Date: 2026-09-24. Scope: the [Unified Roadmap](../2026-09-07-154210-fs-gg-unified-development-roadmap.md),
its current v2 implementation boundary, producer/receiver dependencies, and reusable planning guidance.
This is source/evidence inspection, not a new live migration, release, admission or installed-host qualification.

## Evidence boundary

Each repository's `main` was fetched before inspection. A local checkout was not assumed to match it.

| Repository | Inspected protected default revision |
|---|---|
| `.github` | `d6ee7c79d67c3bdc2e3af07dcbe0e606967f76b5` |
| Coordination | `e96f4821a40c595ebe960e6cf126ace748852f30` |
| Governance | `df47d597fbddbbf41b8facc034185fc9755ef6f6` |
| SDD | `cb89bcafb95efa1d11ecd29c21ca4108cd30a2cf` |
| Templates | `273c9218960215ee856de9e59a769a0e7b63da8f` |

The existing local Coordination branch `routine/gs2-09-7-rehearsal`, commit
`82b4772d3807dfec08901f898d9129ab76e88aac`, contains a rehearsal plan, `MigrationStepExecution` and
`MigrationGitHubRead` work absent from inspected protected main. The exact remote branch lookup returned
no ref. This is a useful implementation input, not merged or publicly retrievable evidence. Preserve and
reconcile it before writing another implementation. Its reported live diagnostic results were not rerun.

No Authority ref, signer, credential, admission operation, receiver default or package was changed by this
audit. The digest-bound GS2 roadmap and accepted receipts were deliberately left byte-identical.

## Findings and corrections

### 1. The unified frontier lagged its native owners

The unified status and feature index still scheduled callable `.4`/`.5` and described migration as unstarted.
The owner [callable plan][call-plan], [readiness packet][readiness], [native acceptance][native] and
[discovery handoff][handoff] establish V2-CALL-01.1–.5 at their bounded scope. The
[accepted receipt directory][accepted] contains GS2-09.1–.6, with .6 accepted on September 23.

Correction: report those delivered boundaries and place .7/.8 on the current path. Preserve earlier
progress in an immutable snapshot instead of mixing superseded pending statements into the current report.
Historical `pendingGates` in the sealed callable handoff remain unchanged: they describe its observation
time, and later native receipts establish the newer state.

### 2. Callable delivery is real, but it is not a migration executor

[Program.fs][program] dispatches `delivery` even though its no-argument banner says no production commands
are enabled. [DeliveryCommand.fs][delivery-command] composes observation, planning and advancement with
the GitHub provider; [OrdinaryDelivery.fs][ordinary] refuses pre-OpenV2 effects and preserves unknown
dispatch/journal outcomes as pending. Reading only the banner would incorrectly rediscover missing wiring.

The readiness packet pins package **FS.GG.Coordination.Cli 0.1.1**, release source
`f4837f054a4222a2195c7edcb4f59058459807f4`, candidate/release-asset SHA-256
`3072f67fa7ad19cc93240eff7b1b3003c12d07882ea9aa85710167273852bf7d`, and a separately recorded
nuget.org signed archive. The [.github receiver manifest][tools] pins that package alongside the distinct
legacy **FS.GG.Coord.Cli 0.90.0** bridge. These are different package/command identities, not version drift.

The packet's effect is `ordinary-source-delivery` in one admitted public synthetic disposable repository.
It explicitly excludes migration/cutover, production targets, fleet epoch mutation and release/publication.
Correction: reuse its adapters and recovery evidence without inheriting broader effect authority or
mistaking a synthetic target's OpenV2 observation for fleet production open.

### 3. Accepted migration contracts do not prove provider execution

[GitHubCompleteDiscoveryQualification.fs][discovery] requires exactly nine authority families, terminal
pagination, subject identities/revisions/digests and two matching complete passes. Its
[architecture contract][discovery-doc] explicitly separates provider capture. Accepted immutable-manifest,
transform, disposition, history and rollback code is in Qualification.Contracts; inspected main has no
composed migration CLI/provider executor.

[GitHubRollbackPlanQualification.fs][rollback] requires all five restoration domains and chained receipt
prefixes. Its [architecture documentation][rollback-doc] explicitly excludes network calls and rollback
execution. [PR #507][507] adds meaningful added-subject, exact-manifest and all-prefix rollback controls,
but the rollback target in those tests is an in-memory map.

Correction: GS2-09.7 must compose fresh provider capture and actual ordered effects, then prove interruption,
retry, archive verification, pre-open rollback and a second migration on representative copies. .8 must
compare complete provider populations and prove actual no-op replay. Controlled tests remain valuable
preparation and are neither discarded nor mislabeled native acceptance.

### 4. Local work and protected delivery need distinct labels

The local .7 prototype already covers step intent, in-flight promotion, fresh authority checks, uncertainty,
Project fields/values and native-relation pagination. Its own plan lists missing provider surfaces and
representative acceptance. None of those local files appeared in the inspected protected-main tree.

Correction: retain its exact local revision as a draft locator, recover/review it through the owning PR,
and add a durable remote plan link only once it exists. Source reconciliation is the first implementation
slice. Do not introduce a broken `blob/main` link or count draft tests as delivered behavior.

### 5. Source preparation and protected effects have different dependencies

The immutable contracts support controlled implementation without provider writes. The callable handoff's
closed permission ceiling and the GS2 roadmap's representative-copy requirement establish a later effect
join. Existing admission source at the inspected Coordination head is a separate operation-owned boundary;
this audit did not operate or qualify it.

Correction: the roadmap graph separates observation source, executor/recovery source, omission controls,
admission readiness and receiver/policy preparation. Source lanes can overlap with disjoint touch-sets;
real effects wait for the exact artifact, isolated population, fresh authority and recovery owner. A shared
Coordination integrator resolves adapter conflicts. Candidate freeze joins completed or explicitly deferred
input changes, including independent telemetry or product work that changes frozen inputs.

### 6. Governance primitives exist; installed policy integration still needs proof

Governance [Route.select][route] is a pure join over typed route, gate and finding inputs;
[EvidenceReuse.decide][reuse] uses exact FreshnessKey matching;
[Enforcement.deriveEffectiveSeverity][enforcement] supplies the pure enforcement decision.
None of those functions installs a receiver check, supplies missing facts or changes a GitHub ruleset.

Correction: V0/V3 records actual callers, policy/version identities, selected obligations and installed
positive/refusal cases. It does not schedule a second generic router/reuse engine. Routine source delivery
remains the adopted default; modeled semantics and protected operations select technical safeguards, not
an inferred heavyweight process. The roadmap's older ambiguous process wording was aligned accordingly.

### 7. Publication, materialization and defaults are independent

SDD [DriverSkills.fs][driver-skills] loads embedded published driver manifests, verifies content and plans
materialization with existing IDs respected. [HandlersScaffold.fs][scaffold] consumes those planned writes.
Templates' [Rendering provider][provider] selects the exact 0.31.0 package. A source skill change does not
automatically reach either an installed scaffold tool or an already-created workspace.

The [SDD 2.0.0 release contract][sdd2] changes the omitted **Typed SDD backend** to Quint and explicitly
preserves provider lifecycle tokens. The [2.0.2 release qualification][sdd202] records later public consumer
acceptance. [Release D][release-d] already completes .1–.4 with Templates 0.14.0/wizard 0.11.2; .5 still needs
OperatingV2 and the receiver/workspace lifecycle decision.

Correction: keep package publication, receiver adoption, clean creation, retained upgrade, SVG product
default, Typed SDD backend default and workspace lifecycle default separately named. SDD 2.0.2 publication
is not evidence that every receiver changed. The roadmap no longer calls Release-D .1–.4 or the producer
capability pending.

### 8. Completed shared foundations should be reused at their exact scope

The [Coordination FsQuint adoption record][fsquint-coord] documents public stable 0.1.0 consumption;
the [SDD consumer record][sdd202] documents stable-package release qualification. The old extraction design
has moved execution to FsQuint. O0–O3 and Choreo have existing owned evidence but distinct installed scopes.
The [telemetry successor owner][utel] records 0.91.0 publication and later successor work, so “0.91.0
publication blocked” is no longer current.

Correction: reuse package/model/executor ownership, retain specific installed-fix follow-up, and avoid a
second actor runtime or generic replay extraction. Do not assert a current running Host version, complete
native usage, public dashboard freshness or efficiency from repository source alone. Optional federation,
learning and telemetry improvements remain outside the migration critical path unless a concrete frozen
input or operation depends on them.

### 9. Routine policy does not establish an available merge writer

The inspected [.github routine helper][routine-helper] deliberately raises `EffectAdmissionUnavailable`
before any merge request because common v1 effect admission is unavailable. The
[writer census][writer-census] classifies this boundary as read-only. The adopted routine policy still
allows source preparation and qualification; it does not restore the sealed writer.

Correction: qualify the exact source PR independently, then verify an admitted landing path before its
merge effect. GitHub merge capability, an older successful merge, or a synthetic callable acceptance does
not by itself establish that path. Admission readiness remains with its existing operation owner; this
audit introduces no bypass or new authority requirement.

## Recommended acceptance sequence

1. Reconcile/review the existing .7 source; complete missing nine-authority readers and the closed migration
   interpreter. Prepare independent omission/receiver controls concurrently, with one owner per shared surface.
2. Join only for the real isolated operation: exact artifacts, scoped representative repository/Project copies,
   native capabilities, protected effect authority, durable recovery and cleanup ownership.
3. Accept .7 from actual migrate/interruption/retry/archive/rollback/rerun evidence, then .8 from complete
   population and no-op/omission evidence and comprehensive GS2-09 closure.
4. Complete or defer candidate inputs, freeze and comprehensively qualify GS2-10, then execute closed
   GS2-11–12. Any changed frozen input follows the existing new-candidate/requalification rule.
5. Authorize production OpenV2 only at GS2-13. Keep GS2-14 observation/contraction, R5 statistical follow-up
   and Release-D lifecycle activation as distinct subsequent exits.

The [Unified Roadmap §9.1](../2026-09-07-154210-fs-gg-unified-development-roadmap.md#91-dependencies-and-parallelism)
is the readable dependency graph and owner/lane table. The native GS2 roadmap remains the execution contract.

[call-plan]: https://github.com/FS-GG/FS.GG.Coordination/blob/e96f4821a40c595ebe960e6cf126ace748852f30/docs/roadmaps/callable-ordinary-v2-execution.md
[readiness]: https://github.com/FS-GG/FS.GG.Coordination/blob/e96f4821a40c595ebe960e6cf126ace748852f30/evidence/github-substrate-v2/gs2-09-9/callable-readiness.json
[native]: https://github.com/FS-GG/FS.GG.Coordination/blob/e96f4821a40c595ebe960e6cf126ace748852f30/evidence/github-substrate-v2/gs2-09-9/native-acceptance.json
[handoff]: https://github.com/FS-GG/FS.GG.Coordination/blob/e96f4821a40c595ebe960e6cf126ace748852f30/evidence/github-substrate-v2/gs2-09-9/callable-discovery-handoff.json
[accepted]: https://github.com/FS-GG/FS.GG.Coordination/tree/e96f4821a40c595ebe960e6cf126ace748852f30/evidence/github-substrate-v2/accepted
[program]: https://github.com/FS-GG/FS.GG.Coordination/blob/e96f4821a40c595ebe960e6cf126ace748852f30/src/FS.GG.Coordination.Cli/Program.fs
[delivery-command]: https://github.com/FS-GG/FS.GG.Coordination/blob/e96f4821a40c595ebe960e6cf126ace748852f30/src/FS.GG.Coordination.Cli/DeliveryCommand.fs
[ordinary]: https://github.com/FS-GG/FS.GG.Coordination/blob/e96f4821a40c595ebe960e6cf126ace748852f30/src/FS.GG.Coordination.GitHub/OrdinaryDelivery.fs
[tools]: https://github.com/FS-GG/.github/blob/d6ee7c79d67c3bdc2e3af07dcbe0e606967f76b5/dist/dotnet/.config/dotnet-tools.json
[discovery]: https://github.com/FS-GG/FS.GG.Coordination/blob/e96f4821a40c595ebe960e6cf126ace748852f30/src/FS.GG.Coordination.Qualification.Contracts/GitHubCompleteDiscoveryQualification.fs
[discovery-doc]: https://github.com/FS-GG/FS.GG.Coordination/blob/e96f4821a40c595ebe960e6cf126ace748852f30/docs/architecture/github-complete-discovery.md
[rollback]: https://github.com/FS-GG/FS.GG.Coordination/blob/e96f4821a40c595ebe960e6cf126ace748852f30/src/FS.GG.Coordination.Qualification.Contracts/GitHubRollbackPlanQualification.fs
[rollback-doc]: https://github.com/FS-GG/FS.GG.Coordination/blob/e96f4821a40c595ebe960e6cf126ace748852f30/docs/architecture/github-rollback-plans.md
[507]: https://github.com/FS-GG/FS.GG.Coordination/pull/507
[route]: https://github.com/FS-GG/FS.GG.Governance/blob/df47d597fbddbbf41b8facc034185fc9755ef6f6/src/FS.GG.Governance.Route/Route.fs
[reuse]: https://github.com/FS-GG/FS.GG.Governance/blob/df47d597fbddbbf41b8facc034185fc9755ef6f6/src/FS.GG.Governance.EvidenceReuse/EvidenceReuse.fs
[enforcement]: https://github.com/FS-GG/FS.GG.Governance/blob/df47d597fbddbbf41b8facc034185fc9755ef6f6/src/FS.GG.Governance.Enforcement/Enforcement.fs
[driver-skills]: https://github.com/FS-GG/FS.GG.SDD/blob/cb89bcafb95efa1d11ecd29c21ca4108cd30a2cf/src/FS.GG.SDD.Commands/CommandWorkflow/DriverSkills.fs
[scaffold]: https://github.com/FS-GG/FS.GG.SDD/blob/cb89bcafb95efa1d11ecd29c21ca4108cd30a2cf/src/FS.GG.SDD.Commands/CommandWorkflow/HandlersScaffold.fs
[provider]: https://github.com/FS-GG/FS.GG.Templates/blob/273c9218960215ee856de9e59a769a0e7b63da8f/providers/rendering.providers.yml
[sdd2]: https://github.com/FS-GG/FS.GG.SDD/blob/cb89bcafb95efa1d11ecd29c21ca4108cd30a2cf/docs/release/single-quint-lifecycle-2.0.0.md
[sdd202]: https://github.com/FS-GG/FS.GG.SDD/blob/cb89bcafb95efa1d11ecd29c21ca4108cd30a2cf/docs/release/fsquint-migration.md
[release-d]: https://github.com/FS-GG/.github/blob/d6ee7c79d67c3bdc2e3af07dcbe0e606967f76b5/docs/roadmaps/svg-release-d.md
[fsquint-coord]: https://github.com/FS-GG/FS.GG.Coordination/blob/e96f4821a40c595ebe960e6cf126ace748852f30/docs/architecture/fsquint-adoption.md
[utel]: https://github.com/FS-GG/.github/blob/d6ee7c79d67c3bdc2e3af07dcbe0e606967f76b5/docs/roadmaps/utel-release-successor.md
[routine-helper]: https://github.com/FS-GG/.github/blob/d6ee7c79d67c3bdc2e3af07dcbe0e606967f76b5/tools/routine-delivery.py
[writer-census]: https://github.com/FS-GG/.github/blob/d6ee7c79d67c3bdc2e3af07dcbe0e606967f76b5/docs/coordination/v1-writer-census.json
