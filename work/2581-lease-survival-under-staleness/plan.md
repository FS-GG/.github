---
schemaVersion: 1
workId: 2581-lease-survival-under-staleness
title: Lease Survival Under Staleness
stage: plan
changeTier: tier1
status: planned
sourceSpec: work/2581-lease-survival-under-staleness/spec.md
sourceClarifications: work/2581-lease-survival-under-staleness/clarifications.md
sourceChecklist: work/2581-lease-survival-under-staleness/checklist.md
publicOrToolFacingImpact: true
---

# Lease Survival Under Staleness Plan

Prose status: planned

## Source Snapshot
- spec: work/2581-lease-survival-under-staleness/spec.md sha256:a7ce10cb9690fb90e6b9fe1e67efb9ed433ed5fd7fb21549639423cf302d5d49 schemaVersion:1
- clarifications: work/2581-lease-survival-under-staleness/clarifications.md sha256:a1ed79528710a2cbae11d78e15d06aac7ea58e3559f33e6ca0edb46795bf41e3 schemaVersion:1
- checklist: work/2581-lease-survival-under-staleness/checklist.md sha256:9e033c619ef2fedcfecae83b49fa4b77ab6ac503c099b443708850b08a01eda6 schemaVersion:1

## Plan Scope
- Work item 2581-lease-survival-under-staleness is planned from the current specification, clarification, and checklist facts.
- Requirement count: 1.
- Clarification decision count: 0.
- Checklist result count: 1.

## Plan Decisions
- PD-001 [AC-001] [FR-001] complete: KEEP `heartbeat` IN `BOARD_WRITES` AND KEEP IT REFUSED. The three literal verb sets in `scripts/fsgg-coord-guards.sh` are byte-unchanged, so `tests/coord-engine-parity/shim.sh` §3b's "exactly 3 literal sets" extraction and its bijection against the engine's `command-contract --json` `writes` key are unaffected, and §3c's per-verb refusal loop keeps covering `heartbeat`. The refusal's message changes; its exit code (69), its timing (before the `exec`) and its membership do not. DEC-001 carries the measured argument against the alternative.
- PD-002 [AC-002] [FR-002] complete: SAY WHICH CHECKOUT `$top` IS, IN BOTH DIRECTIONS. `stale_guard` already answers this in one direction — the `.github#2402`/`.github#2471` block at `fsgg-coord-guards.sh:480-495` fires when `$shared != $top` and tells the reader they are looking at their OWN tier-2a build. The mirrored case, `$shared == $top`, currently says nothing at all, which is exactly the case a tier-2b worker hits and exactly the case where the printed remedy may not be theirs to run. It is composed in the same already-slow branch (`shared_toplevel` is resolved there once, for the reason that comment gives), so the silent common path pays nothing.
- PD-003 [AC-003] [FR-003] complete: PRINT THE TIER-1 ROUTE AS THE CLAIM-PRESERVING ONE, AND TIER 2a AS THE CONDITIONAL ALTERNATIVE. `scripts/fsgg-coord:156-159` execs an explicit `FSGG_COORD_ENGINE_BIN` before `TOP` is computed, so it reaches no guard; the route is therefore "build a CURRENT engine in a checkout you own, then name it", spelled `git worktree add --detach` at the ref `upstream_drift` resolved (`$b`) so that no head under review moves. Tier 2a is named second and with its precondition, because clearing the refusal there requires the caller's own HEAD to contain the drifted commits — which mid-review means rebasing a head a critic may have confirmed, the move the `:490-494` text already forbids.
- PD-004 [AC-004] [FR-004] complete: ONE EXTRA LINE FOR THE LEASE-RENEWAL VERB, KEYED ON `$verb`, NOT A NEW SET. `stale_guard` already receives the verb as `$3` and already branches on it for `delivery-route`'s read arms (`:500-505`), so the lease line is the same shape and costs nothing structurally. It names what the generic write refusal hides: this refusal can outlive the lease it is standing on, and an expired lease cannot be renewed in place (`src/FS.GG.Coord.Core/Protocol.fs:704`), so waiting is the one response that cannot work.
- PD-005 [AC-005] [FR-005] complete: QUALIFY `:134-138` RATHER THAN DELETE IT, ON THIS FILE'S OWN PRECEDENT. Every retired claim in this module is kept and marked as what was once believed (`:37-41`, `:105-116`, `:159-168`), because the record of a wrong inference is what stops it being re-derived. The sentence stays and gains the regime it describes, plus the statement that under host-serialised repair the remedy is not the worker's and the stall is unbounded — with `.github#2549` and `.github#2563` cited as the measurement.
- PD-006 [AC-006] [FR-006] complete: ASSERT THE NON-WEAKENING AS BEHAVIOUR, NOT AS A PROMISE. The new suite drives every verb in `BOARD_WRITES` and `BOARD_WRITES_CONDITIONAL` — read out of the module itself rather than restated, so a future edit to the sets cannot leave this leg measuring a shorter list — against the same fixture and requires refusal with the engine unreached for each.
- PD-007 [AC-007] [FR-007] complete: PIN THE HAPPY PATH IN THE SAME SUITE. The new text is composed only inside the `[ -n "$detail" ]` branch, i.e. only when a refusal already exists; a leg with a build NEWER than its source and a checkout not behind requires total silence and `ENGINE RAN`, so a stray unconditional `echo` would red here rather than teach the fleet to skim the guard.
- PD-008 [AC-008] [FR-008] complete: A DEDICATED WORKFLOW FOR THE NEW SUITE, PLUS THE MISSING PATH ON `coord-engine.yml`. The new suite is hermetic (a `git init` fixture, a shell script standing in for the engine, no dotnet, no network), so it gets its own fast workflow rather than a step on the 20-minute engine job. Separately, `scripts/fsgg-coord-guards.sh` is added to both `paths:` lists of `.github/workflows/coord-engine.yml`: since `.github#1586` moved the partition and both guards into that file it has been named by no workflow filter anywhere, so §3b and §3c have been silent on precisely the PRs whose subject they are — the `.github#2551` class, and load-bearing for this PR, which edits that file.

## Contract Impact
- PC-001 [PD-001] [PD-002] [PD-003] [PD-004] operator-facing diagnostic: the text `stale_guard` prints on a refusal is an operator contract and it changes here; its exit code, the invocations it refuses, and the verb partition behind them do not. `tests/coord-engine-parity/shim.sh` §3c matches the refusal case-insensitively on the word `refused` and on the engine NOT having run, so the added text is compatible with it by construction.
- PC-002 [PD-001] kit distribution: `scripts/fsgg-coord-guards.sh` has no `kit:` row in `registry/repos.yml` and `src/FS.GG.Kit/stage-kit.sh` stages only `kit:` rows, so this change ships to no receiver, republishes no kit, and leaves `registry/repos.lock` untouched. `scripts/fsgg-coord` — which IS a kit row — is deliberately not edited, so no `scripts/repos.sh relock` is owed.
- PC-003 [PD-008] CI selection: `.github/workflows/coord-engine.yml` gains one `paths:` entry in each of its two lists, and a new `.github/workflows/coord-guards.yml` is added. Neither changes an existing required status context; `coord-guards` is a new, additional check.

## Verification Obligations
- VO-001 [PD-001] [PD-006] [PC-001] semanticTest: `bash tests/coord-guards/run.sh` drives the real `scripts/fsgg-coord` against a hermetic tier-2b fixture and requires `heartbeat` REFUSED at exit 69 with the engine unreached, and the same for every other verb in the module's own two write sets.
- VO-002 [PD-002] [PD-003] [PD-004] [PD-005] semanticTest: the same suite asserts the refusal names the shared checkout as shared and host-owned-possible, prints the tier-1 recovery route and the tier-2a alternative, carries the lease consequence for the renewal verb, and that the module no longer holds the unqualified "local, cheap and theirs" claim.
- VO-003 [PD-003] semanticTest: the recovery route is EXECUTED — a current engine at a path the caller owns, named through `FSGG_COORD_ENGINE_BIN` — and `heartbeat` is required to reach it at exit 0 with no staleness output. Naming a route the suite never runs would be the same defect this item is repairing.
- VO-004 [PD-001] [PD-002] [PD-003] [PD-004] [PD-005] [PD-006] [PD-007] gateInversion: every assertion the suite adds is demonstrated RED against the unrepaired module (`git stash` of the guard edit, or the pristine file from `origin/main` supplied to the suite through its documented override), and the exact mutation and observed red are recorded in `evidence.yml`. A gate whose inversion survives is a material finding by definition.
- VO-005 [PD-007] semanticTest: a fresh-engine leg requires the guard to stay wholly silent and the engine to run, so the new text cannot manufacture a refusal or a warning on the happy path.
- VO-006 [PD-008] staticCheck: `scripts/lint-shell.sh` (the `shell-lint` workflow, which has no `paths:` filter) shellchecks the edited module and the new suite; and the two workflow files are asserted by a suite leg to name `scripts/fsgg-coord-guards.sh`, so the reachability fix cannot silently regress.

## Performance Intent
- The new text is composed only inside `stale_guard`'s existing refusal branch, which is by construction the slow path — the guard is silent on the common case, and `upstream_drift` is measured at ~5 ms for the whole probe (`fsgg-coord-guards.sh:346-348`). No new `git` call, subprocess or file read is added to the silent path. `shared_toplevel` is already resolved in the refusal branch and is reused rather than re-run.

## Migration Posture
- PM-001 [PC-001] [PC-002] diagnoseOnly: nothing persisted changes shape — no marker grammar, no board field, no receipt, no lockfile. A worker running an older `scripts/fsgg-coord-guards.sh` and a worker running this one refuse and permit exactly the same invocations; only the words differ, so there is no version skew to migrate and no receiver to update.

## Generated View Impact
- GV-001 [PD-001] workModel: `readiness/2581-lease-survival-under-staleness/work-model.json` is regenerated by the lifecycle commands from the plan sources above; it is SDD-owned and is refreshed rather than hand-edited.
- GV-002 [PD-008] noGeneratedRegistryImpact: no generated registry artifact is affected. `registry/repos.lock` is a digest over `kit:` rows only and `scripts/fsgg-coord-guards.sh` has none, so the relock this item's `take` receipt warned about is owed only if `scripts/fsgg-coord` is edited — which PD-001 through PD-008 deliberately do not do.

## Accepted Deferrals
No accepted plan deferrals recorded.

## Planning Findings
No blocking planning findings recorded.

## Advisory Notes
- Optional Governance pointers remain compatibility facts only.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd tasks --work 2581-lease-survival-under-staleness`.
