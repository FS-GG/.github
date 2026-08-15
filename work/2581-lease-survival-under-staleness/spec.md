---
schemaVersion: 1
workId: 2581-lease-survival-under-staleness
title: Lease Survival Under Staleness
stage: specify
changeTier: tier1
status: specified
publicOrToolFacingImpact: true
---

# Lease Survival Under Staleness Specification

Prose status: specified

## User Value

A worker holding a live claim, whose shared checkout has fallen behind `origin/main` on the engine's
own source trees, can renew its lease **without a re-claim and without waiting on the host** — because
the refusal now names a recovery route the worker can actually take, instead of naming the one checkout
it was instructed not to touch.

Today it cannot, and the cost was measured twice in one session on 2026-08-14. `.github#2549`'s lease
was created at `22:10:05Z` and expired before `done --flip`; `heartbeat` was refused with *"EXPIRED and
cannot be renewed in place"*, forcing a re-claim, which changed the claim generation, which left
`claim-generation` red until `delivery` was re-issued — **`delivery` was called twice**.
`.github#2563` hit the identical shape (claimed `22:55:59Z`, dead at `00:55:59Z`,
`claim-generation` at `01:07:47Z` reporting the item *"not currently held by anyone"*). Its worker
reported the trap from inside it: *"`heartbeat` is itself refused by the same staleness, so I cannot
hold the lease open."*

The trap is not that `heartbeat` is refused. `scripts/fsgg-coord-guards.sh:182-183` puts it in
`BOARD_WRITES` deliberately, and the file documents the cost at `:134-138`:

> a worker whose item outlives an engine merge WILL be refused a `heartbeat` or a `done` and told to
> update and rebuild. **That is a stall of about a minute, on a remedy that is local, cheap and theirs**

The defect is that this justification describes a regime this repository does not always run in. Because
several lanes land concurrently, the shared-checkout rebuild is **serialised by the host**, and workers
are instructed to hold the claim, run no repair, build nothing, and report. In that regime the remedy is
not theirs, the "about a minute" becomes an unbounded wait on another actor, and the one verb that would
preserve the claim is refused by the same guard.

## Scope

- SB-001: **The `:134-138` cost justification is corrected** to say which regime it describes. "Local,
  cheap and theirs" is true only where the worker owns the remedy; under host-serialised repair it is
  false, the stall is unbounded, and the comment must say so rather than leave a reader auditing this
  guard believing the cost is bounded.
- SB-002: **The refusal distinguishes "this is yours to fix" from "this belongs to the host — hold and
  report".** Those demand opposite actions and the current text prints only the first, naming `$top`
  (the SHARED checkout at tier 2b) as the thing to `merge --ff-only` and rebuild.
- SB-003: **The refusal names a recovery route that preserves the claim without touching the shared
  checkout and without moving a head that is under review** — the resolver's own tier 1
  (`FSGG_COORD_ENGINE_BIN`), which `scripts/fsgg-coord:156-159` honours *before* `TOP` is even computed
  and which therefore consults no guard, pointed at a **current** engine the worker built from a
  checkout it owns. Tier 2a (build in your own worktree) is named as the alternative for the case where
  the worker's head is still free to move, with its precondition stated.
- SB-004: **A lease-specific line for the renewal verb**, naming the consequence the generic write
  refusal hides: this refusal can outlive the lease it is standing on, and an expired lease cannot be
  renewed in place (`src/FS.GG.Coord.Core/Protocol.fs:704`), so the reader must act rather than wait.
- SB-005: **A new hermetic gate, `tests/coord-guards/run.sh`,** whose fixture is the **tier-2b
  upstream-drift** shape that `.github#2549` and `.github#2563` actually hit — a caller standing in a
  linked worktree whose shared checkout is BEHIND its `origin` default branch under the engine's source
  trees. No suite exercises that shape today: `tests/coord-engine-parity/shim.sh`'s staleness fixture
  (`shim.sh:204-216`) is a single `git init` directory with no remote, so it drives only `stale_guard`'s
  mtime half and `upstream_drift` returns silently on it.
- SB-006: **Gate-inversion evidence, with the real condition observed red first** (`.github#2551`). The
  suite pins the pre-change behaviour as a leg — `heartbeat` refused, exit 69 — and every new assertion
  is demonstrated failing against the unrepaired module.
- SB-007: **`stale_guard` is not weakened for any state-transition write.** `done`, `claim`, `take`,
  `widen`, `set-paths` and `release` are asserted still refused under the identical fixture, and the
  verb partition is unchanged: `heartbeat` stays in `BOARD_WRITES`.
- SB-008: **CI reachability for the file this work edits.** `scripts/fsgg-coord-guards.sh` is named in no
  workflow's `paths:` filter anywhere in the repository (`grep -rn "fsgg-coord-guards" .github/workflows/`
  returns nothing). Since `.github#1586` moved the verb partition and both guards out of
  `scripts/fsgg-coord` into that file, a PR editing only the guard module starts `coord-engine.yml` on no
  path — so `tests/coord-engine-parity/shim.sh` §3b and §3c, whose entire subject is that file, are
  silent on exactly the PRs that change it. Both `paths:` lists gain the file, and the new suite gets its
  own workflow.

## Non-Goals

- SB-101: **Does not exempt `heartbeat`, or any verb, from `stale_guard`.** See DEC-001 — refuted on
  measured evidence, not weighed and set aside.
- SB-102: **Does not change the verb partition.** `BOARD_WRITES`, `BOARD_WRITES_CONDITIONAL` and
  `BOARD_READS` keep exactly today's membership and remain exactly three literal assignments, which is
  what `tests/coord-engine-parity/shim.sh:327-334` requires and what §3b's bijection against the engine's
  `command-contract --json` `writes` key grades.
- SB-103: **Does not touch the engine.** No file under `src/` changes; the lease clock, the claim CAS and
  the marker grammar are untouched.
- SB-104: **Does not add a lease grace period, and does not make the lease clock staleness-aware.** See
  DEC-002.
- SB-105: **Does not add a host-side lease reset.** See DEC-003.
- SB-106: **Does not repair `.github#2645`** (a bare `claim` reverting board `Status` from `In review`),
  which is a different cause on a different file and is already filed.
- SB-107: **Does not make the recovery route automatic.** The shim cannot build an engine, and a resolver
  that tried would stop being the transparent pipe its header promises.

## User Stories

- US-001 (P1): As a worker holding a live claim on a shared checkout the host is repairing, I am told at
  the point of refusal that the checkout named in the remedy may not be mine to fix, and I am given a
  route that renews my lease without touching it — so the wait does not consume the claim I need in
  order to act once it ends.
- US-002 (P1): As a reader auditing `stale_guard`, the comment that states this guard's cost tells me
  which operating regime it is describing, so I do not conclude the cost is bounded when under
  host-serialised repair it is not.
- US-003 (P1): As the next person to change `scripts/fsgg-coord-guards.sh`, CI actually runs the suites
  whose subject is that file.

## Acceptance Scenarios

- AC-001 [US-001] [FR-001]: Given a linked-worktree caller whose shared checkout is behind its `origin`
  default branch under `src/FS.GG.Coord.{Cli,Core,GitHub}`, when `scripts/fsgg-coord heartbeat <ref>` is
  run, then it is REFUSED with exit 69 and the engine is never reached — the failing condition, pinned
  before and after the change.
- AC-002 [US-001] [FR-002]: Given that same fixture, when the refusal is printed, then it names the
  shared checkout as shared, states that it may be host-owned and must not be repaired by a worker who
  was told to hold, and prints the tier-1 recovery route.
- AC-003 [US-001] [FR-003]: Given that same fixture and a **current** engine built at a path the caller
  owns, when `FSGG_COORD_ENGINE_BIN` names it and `heartbeat` is re-run, then the engine is reached, the
  exit code is 0, and no staleness verdict is consulted — the lease is renewable without a re-claim, a
  shared-checkout repair, or the host.
- AC-004 [US-001] [FR-004]: Given that same fixture, when the renewal verb is refused, then the message
  names the lease consequence — that this refusal can outlive the lease and an expired lease cannot be
  renewed in place.
- AC-005 [US-002] [FR-005]: Given `scripts/fsgg-coord-guards.sh`, when its cost justification is read,
  then it does not carry the unqualified claim that the remedy is "local, cheap and theirs", and it names
  the host-serialised regime in which that claim is false.
- AC-006 [US-001] [FR-006]: Given that same fixture, when `done`, `claim`, `take`, `widen`, `set-paths`
  and `release` are each run, then every one is REFUSED and the engine is never reached.
- AC-007 [US-001] [FR-007]: Given a fixture whose engine is NEWER than its source and whose checkout is
  not behind, when any verb is run, then the guard is silent and the engine runs — the new text
  manufactures no refusal on the happy path.
- AC-008 [US-003] [FR-008]: Given `.github/workflows/coord-engine.yml`, when its `pull_request` and
  `push` `paths:` filters are read, then both name `scripts/fsgg-coord-guards.sh`; and a workflow exists
  that runs `tests/coord-guards/run.sh` and is selected by a change to the guard module.

## Functional Requirements

- FR-001: `heartbeat` remains refused on a stale engine, verb-partition membership unchanged, and the refusal is reproduced by a hermetic fixture in the tier-2b upstream-drift shape. (Stories: US-001; Acceptance: AC-001)
- FR-002: The refusal composed by `stale_guard` distinguishes a checkout the reader owns from the shared checkout, and states explicitly that the shared one may be host-owned and must not be repaired by a worker instructed to hold. (Stories: US-001; Acceptance: AC-002)
- FR-003: The refusal names a recovery route that is executable by the reader alone, and that route is executed in the gate and observed to renew through a current engine. (Stories: US-001; Acceptance: AC-003)
- FR-004: The refusal for the lease-renewal verb names the lease consequence, which the generic board-write refusal does not. (Stories: US-001; Acceptance: AC-004)
- FR-005: The `:134-138` justification is regime-qualified and no longer asserts the unqualified claim. (Stories: US-002; Acceptance: AC-005)
- FR-006: No state-transition write is weakened; every verb in `BOARD_WRITES` and `BOARD_WRITES_CONDITIONAL` remains refused under the identical stale condition. (Stories: US-001; Acceptance: AC-006)
- FR-007: The happy path stays silent — a current engine produces no warning and no refusal. (Stories: US-001; Acceptance: AC-007)
- FR-008: Every suite whose subject is `scripts/fsgg-coord-guards.sh` is selected by a change to it. (Stories: US-003; Acceptance: AC-008)

## Ambiguities

All ambiguities raised by this specification are answered in
`work/2581-lease-survival-under-staleness/clarifications.md` as DEC-001 through DEC-005. None remains
blocking.

## Public Or Tool-Facing Impact

- `scripts/fsgg-coord-guards.sh` is **not** kit content (`.github#1586`): it has no row in
  `registry/repos.yml`, `src/FS.GG.Kit/stage-kit.sh` stages only `kit:` rows, and it is dead code in
  every receiver because both guards are reachable only from tiers 2/2b, which require a source build
  under the caller's own toplevel. So this change republishes no kit, stales no receiver, and leaves
  `registry/repos.lock` untouched — `scripts/fsgg-coord` itself is not edited.
- The refusal's **text** is operator-facing and changes. Its exit code (69), its verb partition, and the
  set of invocations it refuses do not.

## Lifecycle Notes

- Next lifecycle action: `fsgg-sdd clarify --work 2581-lease-survival-under-staleness`.
