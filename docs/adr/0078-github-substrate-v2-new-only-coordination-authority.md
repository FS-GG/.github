# ADR-0078: GitHub Substrate v2 is a new-only coordination authority

- **Status:** Accepted
- **Date:** 2026-08-26
- **Decision owners:** FS-GG organization maintainers and receiver maintainers
- **Affects:** FS-GG/.github, FS-GG/FS.GG.SDD, FS-GG/FS.GG.Rendering, FS-GG/FS.GG.Governance, FS-GG/FS.GG.Templates, FS-GG/FS.GG.Game, FS-GG/FS.GG.Audio, FS-GG/FS.GG.Net, and the new FS-GG/FS.GG.Coordination component
- **Related design:** [GitHub Substrate v2 fleet cutover](../coordination/2026-08-25-github-substrate-v2-fleet-cutover-design.md)
- **Execution roadmap:** [GitHub Substrate v2 fleet cutover roadmap](../github-substrate-v2-roadmap.md)
- **Q0 threat model:** [protected epoch, administration, GitHub state, supply chain, and receivers](../../work/2953-gh-modernization-m0-invariants/threat-model.md)

## Context

FS-GG's current coordination engine preserves guarantees GitHub does not supply directly: comment-order
claim compare-and-set, leases, touch-set exclusion, exact-head independent review, protected post-merge
verification, resumable release operations, and durable evidence. It also carries historical projections
and general mutation verbs that overlap newer native issue types, fields, hierarchy, dependencies,
Projects, rulesets, releases, attestations, and repository properties.

Replacing those representations incrementally inside the v1 producer would qualify the replacement with
the system being replaced and would require v1 and v2 precedence rules. A fleet cutover must instead bind
one exact candidate, stop every normal writer, migrate and verify while closed, open once, and delete the
old authority.

ADR-0077 selects a canonical literate Quint source plus a small generated FS-GG compiled contract. The v2
coordination model depends on the exact source and extracted modules that pass producer Q1, the resulting
post-Q1 ADR-0077 amendment, and the published Quint-profile/compiled-contract artifact. Protocol
implementation must wait for those three authorities and consume the package rather than copy or reference
FS.GG.SDD source.

## Decision

FS-GG will use the dedicated, currently inert `FS.GG.Coordination` component. It owns the canonical literate Quint coordination
specification, pure process model, GitHub adapters and mutation interpreters, CLI host, qualification
contracts, audit reconciliation, and published v2 artifacts. `.github` keeps organization policy,
registries, desired-state instances, the protected cutover ledger, the v1 bridge/retirement work, and thin
reusable workflow entry points.

GitHub is authoritative only for facts it can expose with sufficient identity, revision, completeness,
relation, and audit semantics. Native issues/types/fields, hierarchy, dependencies, repository settings,
rulesets, runs/checks, releases, packages, and attestations are used under that condition. FS-GG retains
only the missing process, concurrency, evidence, and transaction semantics: claim/lease/touch-set streams,
operation locks/elections, exact-head review and post-merge verification, resumable mutation plans, epoch
transitions, semantic contract compatibility, and two-feed release recovery.

V2 is a new-only writer. V1 remains the sole normal production authority until the protected ledger
reaches `OpenV2`; preparation may add inert schema and read-only projections but may not enable a v2 normal
writer. Every released v1 write path receives the universal epoch precondition or is disabled before
freeze. V1 and v2 normal writers are never active together.

The fleet epoch is an expected-parent Git ledger on a dedicated protected ref with immutable phase tags
and a protected environment approval boundary. The issue projection is informative, not authoritative.
The legal spine is `OperatingV1 -> Preparing(manifest) -> FreezeRequested(manifest) -> Frozen(snapshot) ->
SwitchedV2(candidate) -> VerifiedV2(evidence) -> OpenV2(acceptance) -> RetiringV1(deletion) ->
OperatingV2(report)`. Before `OpenV2`, any state from `Preparing` through `VerifiedV2` may enter
`RollingBack(reason) -> OperatingV1(recovery)`. `OpenV2` is the point of no return: afterward recovery is
roll-forward and v1 never resumes.

Qualification is independent of v1 completion. Generated structural/model cases and independently
authored black-box controls bind exact source, model, dependency, package, workflow, receiver, settings,
and evidence fingerprints. A merged PR, v1 review/delivery record, Project status, or roadmap checkbox is
not a v2 qualification receipt.

The Q0 runtime decision, recorded on 2026-08-26 under delegated maintainer authority, makes runtime
reconciliation for this cutover scheduled-audit authoritative. No continuously hosted
App/webhook service has an accepted owner, availability target, secret/ingress design, observability,
upgrade/incident process, retention policy, cost envelope, and disaster-recovery proof. Webhook handling
may be built and qualified as an optional accelerator, but it cannot authorize a transition and is not on
the critical path. A future hosted boundary requires a separate accepted operational decision.

The later Typed SDD workspace-default flip is deferred until `OperatingV2`. Successful producer Q1 over
the exact literate source/extracted module set, its post-Q1 ADR-0077 amendment, and the published
Quint-profile/compiled-contract artifact are real ordered prerequisites for the v2 protocol model; later consumer
adoption, provider/default, and retirement rows are not hidden prerequisites for the cutover.

Every compatibility reader, migration adapter, workflow, command, field, parser, schedule, exception, and
package introduced or retained for cutover has a named deletion unit and an observable absence test.
The Q0 threat model defines the protected assets, actors, five trust boundaries, abuse cases, controls,
residual risks, security acceptance, and pre/post-`OpenV2` incident posture. Its exact bytes and the
authority/mutation/corpus/deletion subject are bound into one independently recomputed review fingerprint.
Every Q0 role review uses two unedited repair-PR comments: an earlier distinct narrative from the
authorized reviewer, followed by an exact current-head attestation whose sole final `Evidence` line cites
that narrative. Live discovery requires the same GitHub `User`, strict temporal ordering, and authorization
by either an allowed association or an exact login in the fingerprint-bound `reviewAuthorAllowlist`.
It rejects Bots, missing users, non-allowlisted contributors, self, later, edited, wrong-author, wrong-PR,
or trailing records.

Q0 was accepted on 2026-08-26 against repair PR #3002 head
`d07cc9daeef46f6f034e2e4cf23dcf3deeea6da0` and canonical fingerprint
`febaa98f354fcad88f50c4c17e7592f3d46d9e6c1d0c381831b6a705e4d68668`. The exact unedited role
attestations are [architecture](https://github.com/FS-GG/.github/pull/3002#issuecomment-5419411396),
[security](https://github.com/FS-GG/.github/pull/3002#issuecomment-5419418539),
[operations](https://github.com/FS-GG/.github/pull/3002#issuecomment-5419407467), and
[cross-repository](https://github.com/FS-GG/.github/pull/3002#issuecomment-5419406435). The final bounded
repair-phase confirmation is [revision 3](https://github.com/FS-GG/.github/pull/3002#issuecomment-5419425110).
The subsequent ratification projection is guarded by a structural SDD check: Q0 accepts only exact JSON
analysis reporting `outcome: noChange`, `coherent: true`, `implementationReady`, zero stale/generated-view
findings, and no diagnostics, followed by a clean tracked-tree assertion. The immutable
[revision-4 changes-required record](https://github.com/FS-GG/.github/pull/3002#issuecomment-5419558809)
diagnosed the stale, substring-only verifier at head `b0b63507f115f0de9e488c9f68dcde22b6992c67` and
authorized repair; it is not repair closure. The actual implementation is
[0ced1901](https://github.com/FS-GG/.github/commit/0ced1901) plus the sealed evidence commit
[c9e82ef](https://github.com/FS-GG/.github/commit/c9e82ef), and the independent
[c9 repaired-head architecture narrative](https://github.com/FS-GG/.github/pull/3002#issuecomment-5419650173)
explicitly verifies that the stale-SDD defect is fixed while requiring the provenance wording correction.
The later [revision-5 changes-required record](https://github.com/FS-GG/.github/pull/3002#issuecomment-5419655595)
diagnoses and authorizes correction of that provenance contradiction; the independent
[cross-repository narrative](https://github.com/FS-GG/.github/pull/3002#issuecomment-5419636584)
corroborates it. Neither record is a future acceptance claim, and fresh live role attestations remain
mandatory for every changed head and fingerprint.
The [revision-6 changes-required record](https://github.com/FS-GG/.github/pull/3002#issuecomment-5419728195)
and [operations narrative](https://github.com/FS-GG/.github/pull/3002#issuecomment-5419704123) diagnose a
later stale `analysis.json` digest in evidence, verification, and ship projections and authorize repair;
they are not acceptance. Q0 therefore also independently hashes every declared evidence snapshot and
every top-level verify, ship, and governance-handoff source against current bytes, with a stale-analysis
inversion, before the tracked-tree assertion.
The [revision-7 changes-required record](https://github.com/FS-GG/.github/pull/3002#issuecomment-5419837439)
and [architecture narrative](https://github.com/FS-GG/.github/pull/3002#issuecomment-5419807264) diagnose
that digest checks alone could accept omitted sources; they authorize repair and are not acceptance. The
downstream gate therefore requires exact, duplicate-free label/path multisets for evidence, verify, ship,
and governance handoff and rejects malformed, missing, duplicate, unexpected, and stale rows. Independent
omission, duplication, and extra-row mutations exercise that completeness contract.
The later [revision-8 changes-required record](https://github.com/FS-GG/.github/pull/3002#issuecomment-5419940163)
and [operations narrative](https://github.com/FS-GG/.github/pull/3002#issuecomment-5419915373) diagnose
that path-and-digest checks could still accept a misclassified or malformed source row; they authorize
repair and are not acceptance. Q0 therefore binds the exact per-path kind, required and allowed source-row
keys, integer schema version, current schema status where the projection defines it, and the projection's
single canonical SHA-256 representation. Evidence snapshots require an exact label/path row with a bare
lowercase digest; verify and ship require an exact two-key `sha256` digest object; governance handoff
requires its exact prefixed lowercase digest string. Independent mutations cover missing and wrong kinds,
missing and wrong schemas/statuses, unexpected row and digest keys, alternate digest forms, malformed
types, and non-lowercase or non-64-hex values across every projection, in addition to the earlier
missing/duplicate/extra/path/stale controls.
Hosted [run 32925218156](https://github.com/FS-GG/.github/actions/runs/32925218156) / job
[98048225279](https://github.com/FS-GG/.github/actions/runs/32925218156/job/98048225279) subsequently proved
the association-only review check was not workflow-portable: the maintainer view reported the immutable
review author's private membership as `MEMBER`, while the workflow token reported `CONTRIBUTOR` and found
zero roles. The fingerprint-bound exact User login allowlist is the repair authority for that parity defect;
changing it invalidates every existing attestation and requires fresh exact-head review.
The [revision-10 changes-required record](https://github.com/FS-GG/.github/pull/3002#issuecomment-5420215570),
[security narrative](https://github.com/FS-GG/.github/pull/3002#issuecomment-5420187392), and
[cross-repository narrative](https://github.com/FS-GG/.github/pull/3002#issuecomment-5420206908) diagnose
the remaining divergent-login-parser risk and authorize repair; they are not acceptance. One canonical
predicate now precedes both association and allowlist authorization and validates the allowlist itself:
1–39 ASCII characters, alphanumeric endpoints, and only alphanumerics or isolated internal hyphens.

## Consequences

The already-created README-only repository and later active bootstrap can be qualified without circular
trust in the v1 lifecycle. Protocol implementation still waits for the ordered producer Q1, ADR amendment,
and published-artifact boundary. The cutover carries a larger up-front census, corpus, migration manifest, failure matrix, and
closed-fleet rehearsal, but it has one authority at every phase and an explicit irreversible boundary.

Scheduled audits impose bounded reconciliation latency and API cost. Those limits must be measured before
freeze, and an unreadable or incomplete audit fails closed. The choice avoids operating an unowned
always-on service during the safety-critical transition; events can reduce latency later without changing
authority.

The current v1 engine receives only the common epoch bridge, necessary security corrections, and deletion
work. Product and Typed SDD work may continue before candidate freeze, but every candidate-input change is
re-observed; after freeze it either waits or creates a new candidate and a full Q0-Q7 rerun.

After `OpenV2`, compatibility and rollback assets that could restore v1 become liabilities and are deleted.
Historical evidence remains sealed and independently verifiable outside the v2 production dependency
closure.

## Alternatives considered

1. **Incrementally replace v1 fields and writers in place.** Rejected because it requires dual authority,
   precedence rules, and self-qualification.
2. **Make GitHub native state authoritative for every fact.** Rejected because GitHub does not provide the
   required claim CAS, touch-set exclusion, exact-head evidence, resumable transaction, or dual-feed
   semantics.
3. **Keep v2 inside `.github`.** Rejected because its build, release, and qualification would remain
   coupled to the special repository and the machinery it replaces.
4. **Host a mandatory webhook service for cutover.** Rejected for this cutover because no complete
   operational boundary is accepted and complete scheduled reads already supply repair authority.
5. **Permit rollback after `OpenV2`.** Rejected because new-only v2 writes have no qualified down-migration;
   promising rollback would hide irreversible state.
6. **Block v2 on the Typed SDD workspace-default flip.** Rejected because the published backend is the
   dependency while the default is a receiver policy change that would destabilize candidate inputs.
