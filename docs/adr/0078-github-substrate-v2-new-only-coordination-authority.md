# ADR-0078: GitHub Substrate v2 is a new-only coordination authority

- **Status:** Proposed
- **Date:** 2026-08-26
- **Decision owners:** FS-GG organization maintainers and receiver maintainers
- **Affects:** FS-GG/.github, FS-GG/FS.GG.SDD, FS-GG/FS.GG.Rendering, FS-GG/FS.GG.Governance, FS-GG/FS.GG.Templates, FS-GG/FS.GG.Game, FS-GG/FS.GG.Audio, FS-GG/FS.GG.Net, and the new FS-GG/FS.GG.Coordination component
- **Related design:** [GitHub Substrate v2 fleet cutover](../coordination/2026-08-25-github-substrate-v2-fleet-cutover-design.md)
- **Execution roadmap:** [GitHub Substrate v2 fleet cutover roadmap](../github-substrate-v2-roadmap.md)

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

ADR-0077 selects canonical Quint specifications plus a small generated FS-GG compiled contract. The v2
coordination model depends on the published Quint-profile/compiled-contract producer; it must consume that
package rather than copy or reference FS.GG.SDD source.

## Decision

FS-GG will create a dedicated `FS.GG.Coordination` component. It owns the canonical Quint coordination
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
The legal spine is `OperatingV1 -> FreezeRequested -> Frozen -> SwitchedV2 -> VerifiedV2 -> OpenV2 ->
OperatingV2`. Rollback may restore the bridge v1 authority only through `VerifiedV2`. `OpenV2` is the point
of no return: afterward recovery is roll-forward and v1 never resumes.

Qualification is independent of v1 completion. Generated structural/model cases and independently
authored black-box controls bind exact source, model, dependency, package, workflow, receiver, settings,
and evidence fingerprints. A merged PR, v1 review/delivery record, Project status, or roadmap checkbox is
not a v2 qualification receipt.

Runtime reconciliation for this cutover is scheduled-audit authoritative. No continuously hosted
App/webhook service has an accepted owner, availability target, secret/ingress design, observability,
upgrade/incident process, retention policy, cost envelope, and disaster-recovery proof. Webhook handling
may be built and qualified as an optional accelerator, but it cannot authorize a transition and is not on
the critical path. A future hosted boundary requires a separate accepted operational decision.

The later Typed SDD workspace-default flip is deferred until `OperatingV2`. The published
Quint-profile/compiled-contract producer is a real prerequisite for the v2 protocol model; later consumer
adoption, provider/default, and retirement rows are not hidden prerequisites for the cutover.

Every compatibility reader, migration adapter, workflow, command, field, parser, schedule, exception, and
package introduced or retained for cutover has a named deletion unit and an observable absence test.

## Consequences

Repository bootstrap and all v2 implementation can be qualified without circular trust in the v1
lifecycle. The cutover carries a larger up-front census, corpus, migration manifest, failure matrix, and
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
