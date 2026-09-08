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

## Workspace, authority, and observation boundaries

GS2-08.1 changes no generated workspace and enables no runtime behavior. The first possible new-workspace
effect is receiver adoption in GS2-08.8 after the GS2-08.7 producer publication; each family must pin the exact
artifact and prove a clean creation obeys the same fence as an upgrade. Adoption remains explicit rather than a
default flip. Existing workspaces require their separately owned upgrade path; publication does not rewrite them.

The protected Git ledger is semantic authority, while the control issue is only a projection. Readers use a
fresh authoritative read at every effect boundary; a cache can help discovery but cannot authorize an effect.
Provider or telemetry loss remains unknown and cannot manufacture success. UTEL-01 v2 owns programme telemetry;
missing usage or coverage is reported as unknown and does not weaken this feature's native qualification.
