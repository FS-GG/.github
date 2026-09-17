# GS2-08 universal v1 bridge and epoch ledger

This subroadmap implements the **Universal bridge and receiver fencing** part in the
[Unified Development Roadmap section 9.8](../2026-09-07-154210-fs-gg-unified-development-roadmap.md#98-feature-parts-and-subroadmap-index).
The native [GitHub Substrate v2 roadmap](../github-substrate-v2-roadmap.md#gs2-08--ship-the-universal-v1-bridge-and-protected-epoch-ledger)
and its content-addressed Coordination unit records remain completion authority. This plan is navigation and
bounded delivery detail; it does not create another status ledger.

## Baseline and fixed decisions

GS2-00 and the affected GS2-02/GS2-03.10 contracts are accepted. GS2-07.8 accepted the no-host runtime:
scheduled complete audits remain authoritative, and this feature does not introduce an orchestration service.
The existing protected Authority journal supplies append-only expected-parent CAS, generations, snapshots,
and reread settlement. GS2-08 reuses it as one canonical fleet aggregate instead of accepting a caller-selected
authority.

The bridge wire contract is versioned and content-addressed. It fixes canonical fleet identity; exact
journal/ref/tag and genesis/trust-anchor layout; manifest identity; complete epoch states and legal transitions;
operation and claim generation fencing at the effect boundary; fresh-read/cache rules; issue projection; and
distinct success, refusal, partial, and indeterminate outcomes. The amended post-open sequence is
`OpenV2 -> ObservingV2 -> ContractingV1 -> OperatingV2`; obsolete `RetiringV1` is not a state. There is no
post-`OpenV2` restoration of v1. `RollingBack` never independently enables writing: only a verified transition
back to `OperatingV1` can do so.

Before freeze, incumbent behavior is explicit rather than inferred. `OperatingV1` admits eligible incumbent
ordinary effects. `Preparing` admits only incumbents whose operation began under the current manifest and whose
claim/operation generations still match a fresh authority read; it refuses new ordinary admission.
`FreezeRequested`, `Frozen`, `SwitchedV2`, `VerifiedV2`, `OpenV2`, `ObservingV2`, `ContractingV1`, and
`OperatingV2` refuse ordinary v1 effects. Every effect refuses stale generations or cache, missing parent/tag,
rewind, wrong manifest, unknown or duplicate fields, and unreadable or contradictory authority. A lost response
settles only by rereading the exact authoritative operation identity; a known effect is never repeated.

## Accepted window: GS2-08.1

GS2-08.1 is accepted by Coordination's native content-addressed receipt
`49c70359ebfbc00331ba90c7c5b100a292efa4cc95a5dfa8007867ceceec5c31` ([Coordination PR #334](https://github.com/FS-GG/FS.GG.Coordination/pull/334)).
That authority covers the frozen wire contract and its qualified source; this navigation plan does not duplicate
its checklist or reinterpret source acceptance as publication, installation, or live authority.

## Qualified source window: GS2-08.2 deterministic protection plan

- [ ] Prepare and qualify only the source needed to install and audit the protected
  ledger/tag/environment/App/control-issue boundary in a later separately authorized operation.

The source contract derives the exact `refs/heads/fsgg/v2/journal/cutover/d5` fleet ref through the existing
Authority journal addressing machinery. It retains full-namespace deletion and non-fast-forward integrity,
plans an exact fleet-ref exclusion from the shared writer, and requires a dedicated contents-only App identity
before that ref can be written. Until a real dedicated App id is separately established, the missing identity
is typed and application remains blocked; continued shared-App use is also a production blocker unless a later
security acceptance explicitly authorizes it. The dry plan additionally separates phase-tag creation from
immutable tag integrity and describes the protected environment and non-authoritative control issue.

Qualification uses sanitized, deterministic observations and requires complete pagination, fresh revisions,
content-addressed page evidence, prior-observation continuity, full effective-ruleset composition, and distinct
unknown versus proven-absent outcomes. Registration and source qualification do not assert provider readback,
authorize application, accept GS2-08.2, or satisfy the later installation and continuous-audit obligation.

This window remains source-only and pending. It does not create or move a live ref or tag, modify the Authority
repository, App, environment, control issue, credential, receiver, or production state, publish a package, or
perform a cutover transition.

## Ready window: GS2-08.2 read-only observation and operation planning

- [ ] Qualify deterministic normalization of complete, content-addressed GitHub provider observations and bind
  them to an exact dry operation plan without authorizing or performing any administrative effect.

This bounded continuation accepts only explicit provider response envelopes: repository identity and revision,
endpoint identity, status, pagination position, response digest, observation time, and typed ruleset, phase-tag,
environment, App, and control-issue payloads. It proves page ordering and completeness, freshness, aggregate
digest identity, and continuity from the previously sealed observation. Missing permissions, incomplete pages,
unknown fields, contradictory duplicates, stale reads, or a changed repository/revision remain explicit unknown
or refusal outcomes rather than evidence of absence.

The normalized observation compiles through the already-qualified protection-plan adapter. A second deterministic
seal binds the exact provider observation, effective ruleset composition, ordered administrative intents, typed
preconditions, and unresolved blockers. The output remains a dry operation plan with `ApplyAuthorized=false`.
Generated cases and independently authored expectations must agree on normalization, pagination, freshness,
composition, continuity, tamper refusal, exact fleet-ref targeting, dedicated contents-only App requirements, and
the no-apply boundary.

This window may add repository-local pure parsing, normalization, planning, fixtures, validators, tests, and native
qualification registration. It does not call GitHub, use credentials, create an acceptance receipt, or change any
live ref, tag, ruleset, repository setting, App, environment, issue, workflow, schedule, receiver, package, or
cutover state. Provider readback and administrative application remain later separately authorized operations.

## Later outcome outline

1. GS2-08.2 installs and audits the protected ledger/tag/environment/App/control-issue boundary.
2. GS2-08.3 inventories every incumbent writer; GS2-08.4 applies the common fresh epoch precondition.
3. GS2-08.5 proves eligible `OperatingV1` and `Preparing` incumbent behavior is preserved; GS2-08.6 attacks
   every fence and old-client generation independently.
4. GS2-08.7 publishes one immutable bridge artifact. GS2-08.8 adopts that identity across every receiver.
   GS2-08.9 disables or revokes any client that cannot honor the fence.

Each later protected operation needs its own native authority. Source qualification in 08.1 is neither
publication nor installation, and neither event authorizes a fleet transition.

## Producer source window: GS2-08.3 writer census

- [ ] Keep one executable, fail-closed producer census of every v1 write entry point and require it before
  later fence implementation or release.

The checked census derives all 54 coordination command roots from a freshly built
`Options.commandCatalogue` through `Options.renderCommandContract`; it does not preserve a second ambient count.
The current candidate reports 20 always-writing, 6 conditionally-writing, and 28 never-remotely-writing roots.
It additionally inventories exact tracked source identities across Coord, scripts, tools, workflows/local actions,
release and build declarations, executable skill/build wrappers, tests, and registries. Direct routine-delivery
REST `PUT` merge, repair/dispatch/registry automations, REST/GraphQL sinks, protected administration, package
publication, and explicitly local-only telemetry are distinct dispositions. An unknown source, a changed identity,
an unresolved dynamic sink, a missing command, or a changed write classification refuses the build.

The cheap structural census runs for every change through `change-completeness`. Typed parity runs only after the
candidate engine exists, and the release workflow repeats it before its first package/publish path. Verification
never regenerates the checked census. Mutation fixtures prove omitted, new, misclassified, dynamic, malformed, and
unwired variants red. This window changes no provider, live authority, package, fence, or receiver; GS2-08.4 owns
the common epoch precondition, Coordination owns independent qualification, and native acceptance remains pending.

The producer census also closes its source boundary across every coordination-kit receiver derived from
`registry/repos.yml`: SDD, Rendering, Governance, Templates, Game, Audio, and Net. A bounded read-only collector
binds each reviewed revision to its Git tree, complete tracked-source manifest, installed coordination-tool pin,
writer/callsite conditions, credential boundary, callable workflow or local-action dependency, and the exact
legacy 0.58.0/0.75.4 or current 0.87.0 tool source that can substantiate that receiver. Identical bytes are stored
once while each receiver invocation remains distinct. Mutable `main` workflow references retain both their
mutable spelling and an exact observed callee revision; the Rendering dispatch route retains its historical
`5fed2838f9ed085ffca09f4cc18b4f7bc59c1294` callee. The offline validator never performs network access or
regenerates expectations, and refuses incomplete trees, missing or reordered routes, unresolved callees, legacy
metadata substitution, or a writer laundered as read-only. This is still a source snapshot: it neither proves
installed behavior nor creates the GS2-08.4 fence.

## Candidate source windows: GS2-08.4 and GS2-08.5 producer fence

The producer candidate imports the six protected admission sources from Coordination merge
`48fa43e67de52d4e728a9abff30686fc029d1d8d` (tree
`484e6c53f9f474bfedfd10f22ac301e3227478cd`) and verifies their recorded hashes on every change. REST and
GraphQL write entry points now use one typed mutation boundary. That boundary rereads authority, generations,
journal state, and permits; records durable settlement evidence before reporting success; retries only a
`ProvenAbsent` original request under a new claim; and refuses the legacy mutation-capable `Send` path while
preserving reads.

The live client remains fail closed until installation supplies protected authority and journal ports, provider
reconciliation, credentials, and admitted records. Coordination merge
`9588dc819898e8c18f18a0a5eedf95427208ba6d` (tree
`75ee13d0aab7bf09ec6532a1faa1528478083f64`, reviewed source
`b3db81ee17a122dfacf9565f036081a0dcca0d67`) now natively accepts the GS2-08.5 source behavior. That acceptance
does not install the producer in any receiver and does not authorize live provider effects.

## Producer attack source window: GS2-08.6

- [ ] Independently attack the merged producer boundary across the accepted closed writer population and retain
  offline, content-addressed evidence for native Coordination qualification.

Protected `.github` merge `068d5dc3fa24d7e1fca99401c755e2f1f5fafe1d` (tree
`a3302c16a0ed5f44e6490a87d3a28e5563ea25b9`) delivered bounded partial GS2-08.6 evidence. Coordination merge
`9588dc819898e8c18f18a0a5eedf95427208ba6d` registers that evidence as partial; it does not accept GS2-08.6.

The completion oracle independently enumerates all 16 concrete production mutation callsites and maps equivalent
callsites to six public production boundaries. Compiled positive and refusal controls execute those boundaries
through `FencedTransport` and `DurableMutationFence`, including REST and GraphQL across all eleven epochs. The
attacks cover stale and stable-rewound authority, an actually absent tag, wrong manifest, permission loss, lost
response, typed claim and durable-operation replacement, `Preparing` nonmembers, admission close and restart,
phase change between successive effects, request-byte identity conflicts, mutation-shaped legacy reads, an actual
conditional old-SHA mismatch, and retry only after `ProvenAbsent` under a fresh fence.

The durable leg serializes and reopens the journal after process-state loss at intent, before send, after send,
before settlement, and after settlement. It asserts exact `Applied`, `ProvenAbsent`, `Partial`, and `Indeterminate`
states and provider-effect counts, and runs a deterministic same-parent two-owner CAS race. The live composition
stays fail closed, while its test escape requires both an explicit flag and an absolute loopback URI.

The structural leg treats the accepted census as a closed population without deriving expected behavior from
producer code. The retained 0.75.4 client was executed against an isolated intercepting provider and performed a
write without the fence, so it is an observed GS2-08.9 failure. The exact 0.58.0 artifact is unavailable and remains
unresolved. The 22 non-CLI workflow, script, publication, and direct-delivery writer sources are recorded with
content hashes and exact execution blockers; none is represented as an attempted refusal when its control-plane
envelope or redirectable artifact is unavailable. They remain GS2-08.9 residuals.

This completion is candidate source and offline evidence until its own protected merge and later native
Coordination acceptance. It does not install behavior in any receiver. Q4 is unclaimed because this lane has no
isolated GitHub repository, token, ruleset, or authority to perform a safe live-provider mutation.

## Accepted GS2-08.7 publication and ready GS2-08.8 receiver horizon

This horizon supersedes the earlier readiness and pending-status wording in this navigation plan. Coordination's
protected merge `c8907be5dabc0a1d59dbe3c239ffd53a5541c863` is the single completion authority for GS2-08.1–08.6;
the GS2-08.6 receipt digest is `4b19806d1c4f9d147368e29e04ab7a480dcb6b798aed32e37159e8ac92a1e0cf`.
GS2-08.7 is accepted by Coordination merge `6d3be73fd80a53c3929818960b4e9dc48293621d`
([PR #416](https://github.com/FS-GG/FS.GG.Coordination/pull/416)) and receipt digest
`0cac376a7f981f56143f14ebbbc6a465529251420fce93b5d115d44282e08e55`. The public coherent set is
`0.90.0`, from source `3adada5a9738464291088830c47a30a3a8fc9561` and tree
`0f075e251d90a2d33efe556df1dac38394b0a388`, with promoted content id
`sha256:52b2774de277855c16a3c0852bc5113deea13d9076b866e6a7de0acc84f4b9c4` and release-manifest SHA-256
`1bbb77f3de10ba3116f9de2ea1df3f5edee38be0de07fa0eaf7c173da3a8456a`. Earlier sections remain
historical design and source evidence rather than a second status ledger.

- [x] **P1 — Offline immutable-candidate probe.** Bind one explicitly supplied local `FS.GG.Coord.Cli` archive
  to its package id/version, archive and payload digests, source commit/tree, installed assembly digests, accepted
  receipt identities, and exact release-workflow/Kit/Drivers identities. Install it from a local-only source with
  private caches, execute the installed command, and load only packed fence assemblies for distinct eligible-epoch,
  refused-epoch, and settlement controls. Negative controls refuse changed or missing bridge assemblies, wrong
  source or receipt bindings, incomplete evidence, and a substituted same-version archive. The loopback provider
  records zero effects. Evidence: `tests/bridge-package/` and the `bridge-package` workflow.
- [x] **P2 — Independent publication qualification registration.** Coordination protected merge
  `09e6eac1b073efdd2da576ef9789e66a96c1415b` registers the artifact closure, feed, signing,
  attestation, public-install, workflow, refusal, and operation-ceiling contract without accepting GS2-08.7 or
  authorizing publication.
- [x] **P3 — Release-path qualification, provenance binding, and version preparation.** Bind P1 and P2 into
  preparation, component publishers, promotion, stored-draft resume, completed-release replay, and the automatic
  patch entry point. Require signed exact-producer-archive attestations while comparing normalized payloads across
  feeds. Fresh read-only feed observations select unused coherent version `0.90.0`; no package or tag is published.
- [x] **P4 — Protected coherent publication.** Use separately authorized release-saga operation authority to
  publish the prepared coherent set and retain prepared and per-feed served archive hashes, normalized payload
  identity, signatures, attestations, journals, and immutable promotion evidence.
- [x] **P5 — Public-only readback and native GS2-08.7 acceptance.** Install from the anonymous public read path
  with cleared sources and private empty caches, re-run bridge behavior and identity checks, retain exact public
  archive and installed-assembly evidence, and obtain native GS2-08.7 acceptance. Receiver adoption is GS2-08.8.

P1 preserves the current production limitation: `Client.fs` composes `UnavailableProductionMutationFence` until
protected authority, journal, operation scope, and provider reconciliation are installed. Publication and
public-only native acceptance are complete. The probe demonstrates refusal and does not wire live admission. The observed
0.75.4 bypass, unavailable 0.58.0 artifact, 22 external routes, and Q4 remain GS2-08.9 inputs.

### GS2-08.8 receiver adoption — window A

- [x] Adopt the exact public `0.90.0` CLI pin in the `.github` distribution manifest and provide one shared,
  revision/tree-bound receiver proof at `tests/bridge-receivers/`. The proof consumes an explicit receiver report,
  public release manifest and public packages; verifies the CLI, Kit and Drivers version and payload identities;
  executes the CLI only from its isolated installed location; and proves fake credentials against loopback remain
  refused by `UnavailableProductionMutationFence` with zero provider mutations. Independent mutations reject a
  missing receiver, unaccounted route, old CLI, substituted package and a revision-bound dependency falsely called
  immutable. The per-receiver evidence is `docs/reports/gs2-08-8-bridge-adoption.json`.
- [ ] Adopt the same evidence contract at the remaining receiver families from their explicit protected revisions
  and trees, with clean-creation and separately owned upgrade proof where applicable.

Window A changes the `.github` receiver pin and shared proof only. It does not enable production admission or
complete fleet adoption. Direct workflow, script, publication and delivery writers remain assigned to GS2-08.9;
this receiver proof does not recategorize them as fenced or immutable.

## Workspace, authority, and observation boundaries

GS2-08.1–08.3 change no generated workspace and enable no runtime behavior. The first possible new-workspace
effect is receiver adoption in GS2-08.8 after the GS2-08.7 producer publication; each family must pin the exact
artifact and prove a clean creation obeys the same fence as an upgrade. Adoption remains explicit rather than a
default flip. Existing workspaces require their separately owned upgrade path; publication does not rewrite them.

The protected Git ledger is semantic authority, while the control issue is only a projection. Readers use a
fresh authoritative read at every effect boundary; a cache can help discovery but cannot authorize an effect.
Provider or telemetry loss remains unknown and cannot manufacture success. UTEL-01 v2 owns programme telemetry;
missing usage or coverage is reported as unknown and does not weaken this feature's native qualification.
