# ADR-0091: Start v2 clean and repair forward

- **Status:** Accepted
- **Date:** 2026-09-28
- **Decision owner:** FS-GG accountable programme owner
- **Affects:** GitHub Substrate v2 activation and repository adoption
- **Supersedes:** ADR-0090 and the GS2-09–GS2-14 migration sequence for the clean-start route

## Context

The owner has selected speed over backward validity for the v2 start. The remaining v1 admission,
fleet migration, sealed-history, rollback and multi-stage cutover work costs more than its current
value. V1 is no longer a critical production dependency. Existing accepted evidence remains an
accurate historical record, but it does not have to become a prerequisite for the new epoch.

Coordination CLI `0.1.2` and the existing v2 workflow already support the bounded `.github` pilot.
Ordinary source delivery can use GitHub's native protections and a synchronous merge conditioned on
the exact PR head. No new admission service, generic protocol or migration runtime is required.

## Decision

Use this four-step clean-start route:

1. Deliver source conventionally after the exact head and required checks pass. Send one synchronous,
   head-conditioned GitHub merge request, then read the merged PR state independently. Do not use
   `--admin`, force pushes or protection bypasses, and do not repeat an uncertain mutation blindly.
2. Append a fresh shared `OpenV2` generation in the dedicated authority repository, directly from the
   historical `OperatingV1` state. The installed CLI 0.1.2 and workflow consume that epoch to activate
   `.github` policy first. This decision does not fabricate a `VerifiedV2` state, migration receipt,
   human-run receipt or proof that old data was transformed.
3. Run one real working journey through the installed v2 path, then run the same ordinary path again
   as a normal rerun smoke test. Repair defects found by either journey before expanding adoption.
4. Add explicitly selected repositories in bounded waves and repair forward. Several ready repository
   profiles may share one immutable Coordination CLI release. Prepare receivers and dedicated credentials
   concurrently, then activate each repository separately after its published profile, credential custody,
   exact current required checks and Authority binding are verified. Keep at most three activations in
   flight in the first combined Net, Governance and Game wave. Each retains native protections, an
   exact-head source merge and independent settlement readback. Pause outstanding activations affected
   by a shared-runtime defect. `.github` was the only continuously activated repository at the initial
   cutover; generated and scaffolded defaults remain unchanged until a later specific adoption decision.

The owner authorizes the repository administrator to append the clean epoch using a narrow temporary
writer grant for the exact cutover ref in the authority repository and to restore that ref's rules
immediately after the write.
The operation must read back both the new epoch and restored rules. Native source branch protections
remain in force throughout; this authority does not permit an admin merge, forced update or broader
ruleset bypass.

## Consequences

Backward validity, v1 replay and fleet-wide migration completeness are not acceptance criteria for the
clean-start generation. The unfinished mandatory-v1 admission, migration rehearsal, archive, rollback,
candidate freeze and staged GS2-10–GS2-14 work is cancelled or superseded for this route, not completed.
Its source and evidence remain available for history or later reuse.

Activation is deliberately narrow. A successful `.github` pilot does not activate another repository,
change a template default, or prove fleet compatibility. Failures after opening are repaired forward.
Repository-specific protections and required checks continue to decide whether each later source change
may merge.

C3 acceptance is per selected receiver: install the pinned published package on a clean path, observe one
real ordinary settlement, independently read back its Authority entry and run the same ordinary path again
to observe `SettlementAlreadyComplete`. Retained-upgrade and old-client qualification apply only where that
receiver explicitly promises those capabilities. Other existing installations receive repair-forward
treatment. Long-running efficiency readings and claims remain separate from repository adoption; missing
measurements remain unknown.
