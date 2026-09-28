# ADR-0091: Start v2 clean and repair forward

- **Status:** Accepted
- **Date:** 2026-09-28
- **Decision owner:** FS-GG accountable programme owner
- **Scope:** GitHub Substrate v2 activation and repository adoption
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
2. Start a fresh v2 generation in `.github`. The repository moves directly from the historical
   `OperatingV1` state to a new `OpenV2` clean-start generation. This decision does not fabricate a
   `VerifiedV2` state, migration receipt, human-run receipt or proof that old data was transformed.
3. Run one real working journey through the installed v2 path, then run the same ordinary path again
   as a normal rerun smoke test. Repair defects found by either journey before expanding adoption.
4. Add repositories explicitly, one at a time, and repair forward. `.github` is the only continuously
   activated repository initially. Generated and scaffolded defaults remain unchanged until a later
   repository-specific adoption decision.

The owner authorizes the repository administrator to create the clean epoch using a narrow temporary
writer grant for the exact cutover ref and to restore that ref's rules immediately after the write.
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
