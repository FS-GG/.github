# ADR-0041: A chore takes the item CAS, unchanged, on a closed per-repo lock issue — the refactor it seemed to need was the reason it never shipped

- **Status:** Accepted (2026-07-17) — **configuration clause amended by [ADR-0042](0042-the-chore-lock-ref-is-embedded-beside-the-roster.md) (2026-07-17).** The substrate decision below stands unchanged; only *where the lock's number is recorded* moved, from `registry/repos.yml` to `Options.choreLockRef`. See the dated note at "The lock issue is CLOSED".
- **Date:** 2026-07-17
- **Affects:** `.github` (the chore queue: `Chore.fs`, `Writes.claim`, `Options.fs`), and every repo where chores are offered — sdd, rendering, governance, templates, game, audio
- **Decides:** [#873](https://github.com/FS-GG/.github/issues/873) — Phase 4.3 condition 1. Unblocks [#733](https://github.com/FS-GG/.github/issues/733) (the wiring), which this record does **not** do.

## Context

[`docs/design/coordination-engine.md` §4.6](../design/coordination-engine.md) proposes the chore queue: the
tool has no thread, so it conscripts the next caller. Adopting the *helping* name means adopting its four
correctness conditions, and the design doc is explicit that a subset is not a smaller version of the feature
but a worse one — *"without those four, it is a machine for manufacturing duplicate work and false green."*

Condition 1 is **a chore must be CLAIMED, not broadcast.** If N workers each call `next` and each is handed
the same chore, N of them do it — [#464](https://github.com/FS-GG/.github/issues/464) (*N workers file one
finding N times*) and [#463](https://github.com/FS-GG/.github/issues/463) (*two workers hand-synced the same
kit twice in one day*), rediscovered inside the mechanism meant to help.

Conditions 2, 3 and 4 are discharged as types in `src/FS.GG.Coord.Core/Chore.fs`, which is landed and
**deliberately unwired**: `offer` is reachable from no command, and `Chore.fsi` says why — the lock is IO, it
cannot live in a pure core, and which substrate it takes was an open decision. That state was correct. This
record ends it.

### The framing that stalled it

[#873](https://github.com/FS-GG/.github/issues/873) put three options, and the cost it assigned each is what
kept the queue dead for as long as it was:

| option | #873's stated cost |
|---|---|
| **A.** Generalise `Writes.claim` — extract the CAS skeleton, parameterise marker prefix + lease | touches the org's most safety-critical function; ADR-0040 C4 says the port changes *the language, not the substrate* |
| **B.** A second CAS for `fsgg:chore` markers | **this is [#485](https://github.com/FS-GG/.github/issues/485)** — one rule computed in two places, agreeing at first and drifting later |
| **C.** A marker on a dedicated chore issue, `Writes.claim` unchanged | serialises chores to one worker per repo; invents a well-known issue |

The shared premise of A and B was that `Writes.claim` is *"145 lines of claim-specific policy that a chore
lock wants none of"* — stale collection, twin detection (#419), `prev=` (#481), renew-in-place (#550) — so
that reusing it meant **factoring** it and not reusing it meant a **second** CAS.

**That premise is wrong on its load-bearing point, and everything else follows from that.**

## The finding

Measured on `main` @ `9c67b5e` (2026-07-17), reading `claim`'s body:

- It touches **only comments** — `Reads.markers`, `postComment`, `patchComment`, `deleteComment`. There is
  no `set-field`, no project read, no GraphQL anywhere in it.
- Its **lease is already a parameter** (`leaseMinutes: int`).
- Its **one** board coupling is a caller-supplied callback: `readPreviousStatus: unit -> BoardStatus option`.

So `Writes.claim` is **already a general comment-order CAS over an arbitrary issue ref.** It is not
item-specific; it is *item-configured*, by its caller, through a callback.

The evidence was already in the repo: `tests/FS.GG.Coord.GitHub.Tests/WriteTests.fs` has driven it as
`claim transport 120 me None aRef (fun () -> None)` — an arbitrary ref with the board callback stubbed —
for the whole of its life. **That call IS the chore-lock configuration.** The lock condition 1 wanted was
built, tested, and sitting there while the queue stayed dead waiting for someone to decide how to build it.

## Decision

**C, sharpened: a chore takes `Writes.claim` — unchanged — on a dedicated per-repo chore-lock issue, with a
short lease.**

```fsharp
Writes.claim transport choreLeaseMinutes worker session choreLockRef (fun () -> None)
```

No new function. No new marker prefix. No new parameter. No edit to `Writes.claim`.

**The lock issue is CLOSED, and must not be LOCKED.** Closed, so it never appears in an `--state open` read
(`/pnext-item` §4's dedupe), never lands on the board, and cannot be mistaken for work by a worker asking for
work — the [#442](https://github.com/FS-GG/.github/issues/442) failure, avoided by construction. Not *locked*,
because a locked conversation refuses comments and **the marker is a comment** — locking the lock issue would
disable the lock.

**Its number is recorded per-repo in `registry/repos.yml`.** Absent ⇒ `offer` refuses. A chore queue that
cannot find its lock must offer nothing, never broadcast: condition 1 fails **closed**, like every other
"could not look" in this engine (#266, #421).

> **AMENDED 2026-07-17 by [ADR-0042](0042-the-chore-lock-ref-is-embedded-beside-the-roster.md)
> ([#1026](https://github.com/FS-GG/.github/issues/1026)) — the number is NOT in `registry/repos.yml`, and
> could never have been.** The engine has no YAML reader, deliberately: the shim ships as a `kind: client`
> kit item *without* the roster (case 13 §6c / #381), so a `repos.yml` reader would be absent exactly where
> receivers run and `offer` would refuse there forever — inverting the mechanism #733 is for. The ref is
> **embedded beside the roster**, keyed on owner and repo:
>
> ```fsharp
> Options.choreLockRef: owner: string -> repo: string -> Types.Ref option
> ```
>
> **The rest of this paragraph is unchanged and load-bearing:** absent ⇒ `offer` refuses, fail closed. Only
> the *source* moved. `.github`'s lock is [#1033](https://github.com/FS-GG/.github/issues/1033); the six
> receivers have none yet and are `None` until #733 creates theirs.

### Why the marker prefix does not need parameterising

`fsgg:claim` disambiguates markers **on the same issue**. A dedicated issue disambiguates **by subject**.
Those are two namespaces for one job, and A proposed adding the second while the first already sufficed.
Only chore markers live on the chore-lock issue, so nothing there can be confused with an item claim.

### Why not A

A refactors the org's most safety-critical function to obtain a generality **it already has** — and the
refactor would make the result *worse*, not merely redundant. On a dedicated lock issue, the four pieces of
"policy a chore lock wants none of" are three **beneficial** and one **already a parameter**:

| #873 called it | on a chore-lock issue it is |
|---|---|
| stale-marker collection — *"a chore lock has no debris to collect"* | **false, and beneficially so.** It has exactly the debris every lock has: a worker that dies mid-chore leaves a marker. Collection is what makes a short lease **self-healing** — the next claimant collects the dead one. |
| twin detection (#419) | **required.** Two workers sharing an id must not both hold the chore lock; that is the same failure the item CAS exists to refuse, and #419 is live on this board. |
| `prev=` / `PreviousStatus` (#481) | **harmless** — `fun () -> None`. A lock issue has no column to restore, and the callback is precisely where that coupling already lives. |
| renew-in-place / heartbeat (#550) | **harmless**, and the lease is a **parameter** — pass a short one. |

So A's deliverable is: refactor the safety-critical function in order to **parameterise off protections that
are beneficial**. It buys nothing and spends the one thing ADR-0040 C4 says this port must not spend.

### Why not B

B is [#485](https://github.com/FS-GG/.github/issues/485) by construction — the same CAS rule computed in two
places, agreeing at first and drifting later — re-committed inside the core built to retire it. #873 is right
about B and this record adds nothing to it.

## Consequences

- **Chores serialise to one in flight per repo, and that IS a new bound.** #873 argued it is not — that
  *"`offer` already returns at most one chore"* — but `offer` returns at most one chore **per caller**, so
  without this lock N workers could run N chores concurrently. The bound is real. It is accepted because a
  chore is a **single board write**, offered only at a safe point, and because the bound buys two things: no
  two chores can race on the board, and the REST spend below is capped.
- **Chores spend REST, which is the budget that dies.** Each acquire is a marker read + a post + a re-read,
  plus a delete on release — and the claim lock lives on REST (ADR-0034 §3), which hit 0/5,000 twice on
  2026-07-16 ([#894](https://github.com/FS-GG/.github/issues/894),
  [#907](https://github.com/FS-GG/.github/issues/907)) while GraphQL stayed healthy. A chore queue that drains
  the budget the lock lives on would be a helping mechanism that stops the fleet. One-in-flight-per-repo plus
  a short lease is what makes that spend bounded — so the serialisation above is not merely tolerable, it is
  part of why this is safe.
- **`who` and `reap` do not see the chore lock.** Both scan **board** items, and the lock issue is off-board
  by design. So a held chore lock is invisible to the roster, and a lapsed one is not collected by `reap`.
  It is still self-healing: `claim`'s own stale collection takes the dead marker on the next acquire, and the
  lease is minutes. Naming it because "the lock nothing reports" is the kind of gap this org finds late — if
  chores ever grow beyond seconds, revisit this line first.
- **New infrastructure, and it is small:** one closed issue per repo, one registry field. That is #733's to
  land, with the `perform` path re-verifying via `Chore.isRetired` against a **fresh** read — never the 90s
  scan cache, because a reconciler's question may not be answered from a snapshot.
- **`Chore.fsi`'s condition-1 comment stops being true the moment this lands**, and is corrected in the same
  change. It currently states the substrate is an open decision; leaving that would re-emit the premise that
  stalled the queue to every reader of the core (the [#968](https://github.com/FS-GG/.github/issues/968)
  shape: retire the conclusion, leave the premise, watch it regenerate).
- **This record does not wire anything.** `offer` is still reachable from nothing when it lands, and #733 is
  still the item that changes that. A decision is not an implementation, and #733 can be stamped only once
  `offer` is actually reachable.

## Verification

The premise this record rests on is pinned, not asserted. `WriteTests.fs` now drives `claim` in the chore
configuration — off-board ref, short lease, `fun () -> None` — and the harness's `scripted` transport
`failwith`s on any call beyond the three it scripts, so the tests assert `claim`'s **call shape**: a board
read added to `claim` is a fourth call and reds them. Verified by mutation on this tree: one extra transport
read inside `claim` fails 26 of the suite's 57 tests, including all three added here.

That matters because this ADR's decision is only as true as its premise. An ADR asserting *"claim needs no
change"* that nothing re-checks is exactly the shape
[#944](https://github.com/FS-GG/.github/issues/944) closed — a claim of coverage with nothing behind it —
and it would rot the same way.
