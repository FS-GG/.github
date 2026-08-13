---
schemaVersion: 1
workId: 2496
title: "pnext-item names the live delivery <ref> --pr N call point"
stage: clarify
changeTier: tier1
status: clarified
sourceSpec: work/2496/spec.md
publicOrToolFacingImpact: true
---

# pnext-item names the live delivery <ref> --pr N call point Clarifications

## Source Specification
- work/2496/spec.md

## Clarification Questions
- CQ-001 [AMB:AMB-001]: Where in the worker's ordinary flow should the live, non-`--snapshot` `delivery <ref> --pr N` call go?
- CQ-002 [AMB:AMB-002]: What does the documented step say a worker does when the live call errors, and does it gate the merge?
- CQ-003 [AMB:AMB-003]: May a worker who does not currently hold the item's live claim marker make this call?

## Answers
- CQ-001 → Right before the merge (§6), immediately after `landable` reports green for the exact head SHA and immediately before the `gh api -X PUT …/pulls/<pr>/merge` call — not right after opening the PR, and not on every push. Read from `src/FS.GG.Coord.Cli/Client.fs`'s live `delivery` command (`Client.fs:1890-2040`): `ensureAuthorization` (`Client.fs:1855`) PATCHes the marker onto whatever head the PR carries **at the moment of the call**, keyed by `Reads.prHeadSha` read fresh from GitHub. A call made right after PR open would bind the marker to that early head; every later push (a repair round, a rebase) changes the head and re-stales the marker (`check-claim-generation.py`'s MISMATCHED diagnosis: "head= no longer equals this PR's actual current head SHA"), and nothing in the flow calls `delivery` again until this new step exists. Calling right before merge binds the marker to the one head that matters — the head that is actually about to be merged, which is also the last head CI evaluates `claim-generation` against, since no further push follows.
- CQ-002 → Report and continue. `ensureAuthorization`'s write is the ONLY thing the live `delivery` command's authorization side-effect does (`Client.fs:1836-1844`'s doc comment): it never touches `GuardedLand`/`Complete` (still exclusively `--apply`-gated) and is not itself a merge-affecting action. `check-claim-generation.py`'s own docstring, and `Landable.fs`'s `advisoryCheckNames` carve-out (`.github#2373`), establish that `claim-generation`'s `[missing]`/`[stale]` conclusion is excluded from `landable`'s rollup by name until the check is armed into branch protection — so a failed live call cannot legitimately block a merge that is otherwise green, and the documented step must not treat it as a merge gate. It must not be silently swallowed either: a worker who cannot make this call routinely (not once) is exactly the five-for-five pattern `.github#2488` measured recurring, so the step directs the worker to report the failure (to whoever dispatched it, or in the item's own history) rather than proceed as if the call succeeded.
- CQ-003 → No. `Client.fs`'s `delivery` command resolves `liveClaim` before it will do anything (`Client.fs:1920-1930`): it requires `Some marker when marker.Worker.Value = w.Id` (the CALLING worker's own id matches the live claim holder) or, only when the board already shows the item `Done`/`Closed`, `None`. Any other case — no live claim, or a live claim held by a DIFFERENT worker id — is a hard `Error(Errors.Malformed(..., "no live claim marker can authorize delivery"))`, and the whole `delivery` invocation fails, not just the authorization side-effect. So a fresh independent critic (a different `FSGG_WORKER` identity) cannot make this call on the implementing worker's behalf, and a worker cannot make it after `done --flip` released its own claim. The documented step must run from the SAME worker identity that has held the claim since `take`, and before that worker releases it.

## Decisions
- **DEC-001** [CQ-001] [AMB:AMB-001] [FR-001] [AC-001]: The live `delivery <ref> --pr N` call is made exactly once per item, in `pnext-item` §6 (Merge and obligations), immediately after `landable` reports green for the exact head SHA and immediately before the merge REST call — not after PR open, not on every push.
- **DEC-002** [CQ-001] [AMB:AMB-001] [FR-002] [AC-002]: Because `rebindAuthorization` makes a call against an already-current marker a zero-cost no-op PATCH-skip (`Client.fs:1804-1805`, `AuthorizationCurrent`), the ONE call before merge is sufficient — it needs no companion call earlier in the flow, and does not turn into a per-push write, because it is invoked exactly once regardless of how many repair rounds preceded it.
- **DEC-003** [CQ-002] [AMB:AMB-002] [FR-003] [AC-003]: A failed live `delivery` call is reported by the worker (to the dispatching host, or recorded in the item's own history) and does not block the merge — `claim-generation` stays advisory-only by construction (`Landable.fs` `advisoryCheckNames`, `.github#2373`) until armed into branch protection, which this item does not do.
- **DEC-004** [CQ-003] [AMB:AMB-003] [FR-003]: Only the worker holding the item's live claim marker (the same `FSGG_WORKER` identity active since `take`) may make this call, and only before that worker releases the claim (`done --flip`). A fresh critic or a worker after claim release must not attempt it — the live form refuses it outright.

## Accepted Deferrals
No accepted deferrals recorded.

## Remaining Ambiguity
- None. AMB-001, AMB-002, and AMB-003 resolved above.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd checklist --work 2496`.
