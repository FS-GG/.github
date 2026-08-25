# GitHub Substrate v2 Q0 threat model

This model protects the one-way migration from the current coordination substrate to the new-only
GitHub Substrate v2 authority. It covers the ratified design and Q0 census; later units must refine it
when they introduce concrete schemas, interpreters, credentials, packages, or receiver routes.

## Assets and security objectives

- The protected epoch and its monotonic `OperatingV1 -> Bridging -> OperatingV2 -> OpenV2` history must
  remain authentic, complete, ordered, and impossible to roll back after `OpenV2`.
- Claims, blockers, review decisions, delivery receipts, releases, and administrative plans must retain
  their exact subject, revision, completeness, principal, and evidence bindings.
- Administrative and release credentials must be least-privilege, short-lived where GitHub supports it,
  unavailable to ordinary read/plan routes, and absent from logs and durable evidence.
- Package, workflow, schema, corpus, and receiver bytes must remain attributable to reviewed source and
  immutable digests. Mutable projections must never become completion authority.
- Cross-repository receivers must neither accept an unqualified producer transition nor silently remain
  on v1 after their v2 receipt says otherwise.

## Actors, trust boundaries, and assumptions

Trusted actors are protected-branch reviewers, the separately protected administrative principal,
GitHub's authenticated API/control plane, the qualified release identity, and registered receiver
identities. Ordinary contributors, workflow jobs without the necessary environment grant, stale or
compromised worker processes, dependency publishers, and externally owned repositories are untrusted.
GitHub itself is a dependency rather than an infallible oracle: reads may be stale or incomplete, writes
may have indeterminate outcomes, pagination may truncate, and mutable issue/project state may race.

The five explicit boundaries are:

1. `protectedEpoch`: reviewed git history and protected tags versus all mutable operational projections.
2. `administrativePrincipal`: separately approved plan/apply jobs versus ordinary workflows and workers.
3. `githubMutableState`: typed interpreters versus issues, comments, Projects, settings, checks, and APIs.
4. `packageSupplyChain`: reviewed source/one-pack release identity versus feeds, caches, and dependencies.
5. `crossRepositoryReceiver`: the producer's qualified epoch and receipts versus independently mutable
   receiver repositories, installations, workflows, credentials, and external ownership.

## Abuse cases, controls, and residual risk

| Threat / abuse case | Boundary and impact | Required prevention, detection, and recovery | Residual / disposition |
|---|---|---|---|
| A writer omits or invents an authority, mutation route, corpus case, or deletion unit. | `githubMutableState`; an unmodelled path bypasses the v2 gate. | Exact category sets, independently recomputed tree/command fingerprints, canonical review fingerprint, subject-mutation controls, CI invocation, and GS2-09 exhaustive interpreter registry. | Repository-admin routes not exposed by GitHub APIs remain explicit unsupported/authorized outcomes; absence cannot be inferred. |
| A stale worker, duplicate delivery, reordered event, or lost response advances state twice. | `githubMutableState`; split brain or false completion. | Protected epoch, idempotency keys, expected pre-state, exact-head/generation bindings, append-only receipts, complete reread, replay/model corpus, and fail-closed indeterminate outcomes. | GitHub offers no general CAS for every surface; saga compensation and operator repair remain necessary. |
| A maintainer edits mutable comments, issue bodies, project fields, or check names to fabricate authority. | `githubMutableState`; false scheduling, review, or completion. | Mutable values are projections only; validated digest chains and protected epoch are authority; independent review and scheduled audits compare projections. | GitHub administrators can still mutate/delete hosted objects; audits detect divergence but cannot prevent every platform-admin action. |
| A compromised ordinary token invokes rulesets, Apps, environments, issue types, or destructive settings changes. | `administrativePrincipal`; organization-wide takeover or irreversible loss. | Separate protected environment/principal, least privilege, typed plan/apply, expected pre-state, dry run, approval, receipt, and post-write verification; no admin secret in routine jobs. | Organization owners remain a root of trust. Unavailable API surfaces require controlled UI action with recorded evidence. |
| Epoch history is force-pushed, tag-replaced, or an old client attempts downgrade after `OpenV2`. | `protectedEpoch`; v1 regains production authority. | Protected branch/tag/ruleset, ancestry and signature/tamper checks, old-client epoch refusal, credential/route deletion, and one-way `OpenV2`. | A GitHub organization owner can override protection; independent exported digests and audit receipts provide detection and reconstruction evidence, not absolute prevention. |
| A package/feed/cache serves bytes unrelated to reviewed source, or a dependency is substituted. | `packageSupplyChain`; arbitrary code runs with workflow permissions. | Immutable pins, locked restore, one pack, byte-identical dual-feed verification, package/source/tag digests, OIDC/protected release, clean install, SBOM/provenance where supported, and receiver digest checks. | nuget.org/GitHub/runner compromise is outside direct control; publication and receiver activation stop on mismatch. |
| A receiver accepts v2 before prerequisites, accepts the wrong producer epoch, or reports success while v1 writers remain. | `crossRepositoryReceiver`; fleet split-brain. | Registered roster/owner, exact producer and receiver digests, staged prepare/commit/verify receipts, independent receiver checks, writer-fence inversion tests, and Q9/Q10 complete-fleet census. | External repositories cannot be compelled; they remain explicitly external/unreadable and cannot count toward FS-GG completion. |
| A webhook or App event is forged, replayed, dropped, or becomes a hidden mutation authority. | `githubMutableState`; derived state advances without a complete audit. | Cutover runtime rejects hosted webhook authority. Scheduled audits are authoritative; events may only schedule rereads after `OperatingV2`, with signature, delivery-id, replay, completeness, and availability qualification. | Scheduled audits increase detection latency; Q10 measures cadence, API budget, and recovery time. |
| CI is absent, path-filtered away, cancelled, or a gate returns green after reading nothing. | All; review accepts unverified evidence. | Dedicated Q0 workflow covers validator/evidence/threat/workflow changes, runs a negative-control job before the positive gate, and treats unknown/no-verdict exits as failure. Required review records cite the exact canonical fingerprint. | Repository settings must retain the check through landing; Q12 audits required-context policy. |
| Evidence leaks tokens, private values, or sensitive incident material. | `administrativePrincipal` / `packageSupplyChain`; credential compromise. | Record identities, permissions, digests, and outcomes—not secret values. Mask logs, minimize artifacts, use short-lived tokens, and rotate/revoke on suspected exposure. | GitHub log redaction is defense in depth, not authorization to print a secret. |

## Security acceptance and incident posture

Security review must independently reproduce the canonical Q0 fingerprint, exercise invalid inventory,
unknown-writer, corpus, deletion-unit, threat-source, and reviewer-fingerprint mutations, and cite its
immutable evidence record. A material mismatch blocks ADR acceptance.

On suspected compromise: disable mutation workflows and administrative grants; preserve protected epoch,
logs, artifacts, and API observations; rotate affected credentials; run a complete scheduled audit from
an immutable candidate; classify every partial or indeterminate write; repair only through typed plans and
new receipts. Before `OpenV2`, rollback restores the last proven bridge configuration. After `OpenV2`,
rollback means a reviewed forward v2 release—never restoration of v1 authority.
