# ADR-0038: The defect corpus is the cut-over gate — the shadow clock could never tick, and the corpus found what the clock was built to classify as noise

- **Status:** Accepted
- **Date:** 2026-07-14
- **Affects:** `.github` (the coordination engine, the client, the fixture). Amends [ADR-0034](0034-typed-coordination-engine.md) §5 (Phase 3b entry condition) and **takes** the ordering decision that §"Decision 2" of the design doc deliberately deferred to the flip.
- **Fixes:** [FS-GG/.github#730](https://github.com/FS-GG/.github/issues/730) (the flip), [#728](https://github.com/FS-GG/.github/issues/728) (the clock that could not run), [#688](https://github.com/FS-GG/.github/issues/688) (the fixture's own heredoc)

## Context

[ADR-0034](0034-typed-coordination-engine.md) moved the coordination domain to a typed F# core and
staged the cut-over. Phases 0–3a landed. Phase 3b — `--engine=fs` becomes authoritative — was gated on
one thing:

> **Entry:** `fsgg-coord divergence --fleet` is GREEN — three consecutive covered days, ≥2 distinct
> workers, zero blocking divergences, on the build being flipped.

**That clock could not tick, and the reason is structural rather than incidental.** Workers run in
per-item git worktrees ([ADR-0027](0027-worker-keyed-claim-lock-and-worker-channel.md)); a worktree
worker resolved no engine ([#728](https://github.com/FS-GG/.github/issues/728)); a worker who banks no
evidence can never be one of the "≥2 distinct workers" the criterion requires. The gate read
*"NO VERDICT — the shadow compared nothing on 2 of the 3 day(s) in the window"* and would have gone on
reading it. Worse, `Divergence.evaluate` partitions evidence by **exact engine build**, so any engine
republish restarts the three-day window — correct by design, and it means the engine could not be
improved while waiting for the clock that was waiting for the engine.

Meanwhile the board recorded the cost. At the time of writing, **49 of 61 open items across the whole
org lived in `.github`** — the coordination repo — and the arrival rate ran level with the closure
rate (94/36/27/29 opened against 68/24/29/25 closed over four days). The fleet's entire output was
repairs to the machinery that dispatches the fleet, and the work that *retires* that machinery was
gated behind a clock the machinery could not start.

## Decision

**1. The cut-over gate is the DEFECT CORPUS, not the shadow clock.**

`tests/fsgg-coord/cases/` — one case per historical defect, named for it — runs against **both**
engines in CI. `--engine=bash` and `--engine=fs` must both be green. The shadow is demoted from gate
to **telemetry**: it still runs, still logs, still partitions divergences into OUTCOME and REASON, and
is still the right instrument for watching a live fleet. It is simply no longer the thing that decides
whether the flip may happen.

The corpus is the stronger gate, and not by a small margin:

| | the shadow clock | the corpus |
|---|---|---|
| **covers** | whatever items happened to float past a live fleet for three days | every path that has **actually broken** |
| **needs** | a fleet that banks evidence — which, per #728, it structurally could not | a checkout |
| **survives an engine rebuild** | no (the window resets) | yes |
| **a REASON divergence** | classified as *expected, not a bug* | a **failing assertion** |

That last row is the one that settled it, and the evidence is below.

**2. Blockers are checked BEFORE the touch-set.** This is the ordering decision the design doc
(§"Decision 2") deferred: *"which order survives is a Phase 3 decision... recorded here rather than
merged silently, because #485 exists precisely because five predicates were merged silently."* Bash
checked blockers first; the typed core checked the touch-set first; both were right about the item and
only one can be the sentence a worker reads. **Bash's order wins**, for two reasons that point the same
way:

- **Semantics.** A blocked item cannot be started whatever its touch-set says. *"No `Paths:` declared"*
  is an OMISSION a worker can fix in ten seconds — and telling them that about an item they still could
  not start afterwards sends them to fix the wrong thing and come back to the same queue.
- **Cost, which is what settles it.** Blockers are **board facts**: the scan already has them, and they
  are free. A touch-set lives in the issue **body** — one REST read per item. Bash therefore never
  fetched the body of a blocked candidate and never needed to. Touch-set-first would oblige the client
  to fetch a body for every blocked item on the board, paying the budget that dies first
  ([#418](https://github.com/FS-GG/.github/issues/418)) to answer a question the board had already
  answered.

**3. An unread body is `Unreadable`, not `Undeclared`.** `TouchSet` grows a case, and the client sends
`bodyUnreadable` rather than withholding the item. See below for why this was load-bearing.

## What the corpus found that the clock was built to ignore

The shadow's own taxonomy — the one this ADR demotes — reads:

> **REASON** — both agree the item is not startable, and they name a different fact. **Not a bug**, and
> expected at Phase 2.

Sweeping the corpus under `--engine=fs` produced **15 failures across 8 cases**, every one of which the
shadow classifies as REASON, i.e. as noise. Three were real defects that the flip would have shipped:

**(a) A prose blocker vanished, and the item became schedulable.** `BlockerUnparseable` is a real state
— *"Blocked by RESOLVED: shipped last week"* blocks — and the `Blocker` record demanded a `Ref` anyway.
So the one state the type was told to expect was the one it could not hold. The client papered over
that in its own way: `shadow_blockers_json`'s `jq capture` **yields empty on no match**, so no object
was constructed, the blockers array collapsed to `[]`, and an item bash had just classified **BLOCKED**
arrived at the engine **UNBLOCKED**. Under `--engine=fs`, a worker is handed blocked work. This is
epic [#266](https://github.com/FS-GG/.github/issues/266)'s exact shape — an error, an empty result and
a legitimate "no" being the same value — arriving through the one door built to prove #266 was over.
`Blocker.Ref` is now an option and the raw text always survives.

**(b) The swept item silently ceased to exist.** `shadow_sweep` withheld any candidate whose body it
could not read: counted, and skipped. Harmless while bash owned the answer — the observer may never
cost a worker their item, so a failed read degraded the *comparison*, not the run. Once the engine owns
the answer it degrades the **answer**: the item is absent from the engine's world, so it can be neither
offered **nor passed over with a reason**. It just disappears. And this was not a rare path — bash
short-circuits blocked candidates before any body fetch, so *their bodies had never been fixtures at
all*, and nobody had noticed for as long as the fixture had existed. The item now travels with
`bodyUnreadable`, its touch-set is `Unreadable` (UNKNOWN — **not absent**), and the verdict is
`Undetermined`: not startable, and **said so**. Because blockers are now checked first (decision 2), a
blocked item still decides correctly with no body at all — so one unreadable issue cannot starve the
board.

**(c) The lease window and the holder were dropped.** Bash has named them since
[#428](https://github.com/FS-GG/.github/issues/428): *"overlaps in-flight work held by `puffin-h11` on
`FS.GG.Rendering#215` (lease frees in ~96m)"*. The engine said *"overlaps in-flight work: a ⇄ b"* — true,
useless, and it collapsed the distinction between a **live claim** (wait out a window, or go and talk to
a worker) and a **batch member** (frees at the end of this run, no lease at all). Same verdict, opposite
instructions. `Schedulability.explain` could not do better because a collision's holder is a fact about
the **batch**, not the item; `Batch.explainDecision` is now the operator-facing renderer, and the lease
travels with the snapshot because it is client-configurable (`FSGG_CLAIM_LEASE_MIN`) and an engine that
hard-coded 120 would tell every worker to wait out a window that had already closed.

**A fourth, in the fixture itself.** Splitting the 5,808-line `run.sh` monolith into per-case files was
what made (a) and (b) reachable at all: the monolith shared one cache and one `gh` stub across 847
assertions **in file order**, so a case could only be reached through the side effects of every case
above it. The empty-`RC_FILE` fail-open ([#344](https://github.com/FS-GG/.github/issues/344), reopened)
is *unreachable* in the monolith — by the time its assertions run, the publish window is always already
spent. A case that owns its world reaches it on the first call. **Isolation was coverage, not
tidiness.** [#688](https://github.com/FS-GG/.github/issues/688) (the stub's own heredoc executing the
backticks in its comments) fell out of the same split.

None of (a), (b) or (c) is an OUTCOME divergence on a healthy board. All three are REASON divergences.
**The gate that was blocking the flip was built to wave all three through** — and, because the clock
could not tick, would have waved them through *after an indefinite wait*.

## Consequences

- `--engine=fs` is open. `--engine=bash` remains the escape hatch and is asserted **byte-identical** to
  the pre-flip tool, so the rollback is exact.
- **Every failure mode in `fs` is fatal.** A missing engine, a stale engine, an engine that cannot name
  its version, a red verdict, no verdict — all die. Falling back to bash after the caller asked for the
  typed core is a **silent engine substitution**: the worker believes they ran the engine, the ledger
  records a skip nobody reads, and the run is indistinguishable from agreement.
- The reasons a worker reads are now **relayed** from the engine, never restated in bash. The engine
  emits `explain` per decision; bash prints it. A rule stated twice is a rule that will disagree with
  itself, which is [#485](https://github.com/FS-GG/.github/issues/485) — and the fix for #485 may not
  reintroduce #485.
- The shadow keeps running. It is how a **live** fleet is watched, and this ADR does not claim the
  corpus replaces that. It claims the corpus is what a **cut-over** is gated on.
- **What this does not do:** it does not touch the build/publish/pin/feed substrate, and it does not
  change the arrival rate ADR-0034 named. It unblocks the work that retires the domain — Phase 4.1
  (generated projections, and the 54 vendored copies) is next, and it is still the largest single win
  in the design.

## Alternatives considered

**Fix #728 and wait for the clock.** Make worktree workers resolve an engine, then wait three covered
days. Rejected: it is strictly slower, it leaves the engine unimprovable inside its own window (any
republish resets it), and — decisively — it would have gone green **without finding (a), (b) or (c)**,
because the clock is built to classify all three as expected noise. A gate that cannot fail on a real
defect is not a gate.

**Rewrite the engine.** Rejected. The typed core is the one asset here that works; ~2,500 lines of it
were already correct, and the three defects above were in its *edges* — the wire, the client, the
prose — not its spine.
