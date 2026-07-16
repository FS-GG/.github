# The Phase-D payoff — the 14 `engine-retires:phase-d` issues, disposed on the record

**Date:** 2026-07-16
**Owner:** `.github` (the coordination engine)
**Governs:** the "take the payoff" leg of epic [#729](https://github.com/FS-GG/.github/issues/729) —
the disposition of every open issue labelled [`engine-retires:phase-d`](https://github.com/FS-GG/.github/labels/engine-retires%3Aphase-d)
now that ADR-0040 Phase D is COMPLETE.
**Companion:** [the Phase D plan](2026-07-15-phase-d-corpus-through-shim-plan.md) (the port itself);
[the D.4 differential disposition](2026-07-16-d4-differential-disposition.md) (the bash-deletion record).

## Why this file exists

The `engine-retires:phase-d` label reads, verbatim:

> *IO-layer defect (ADR-0040 Phase B); closeable when Phase D wires the shim. Do NOT re-fix in bash.*

Phase D wired the shim (D.2 swap `5fabbe1`) and deleted bash (D.4 `b0bb55a`). So the label's own promise —
*closeable when Phase D wires the shim* — has come due for all 14 issues that still carry it. Epic #729 names
this leg "take the payoff." **Taking the payoff is not "close them all."** It is checking each one against the
*completed engine*, closing the ones the typed IO port actually retired — with the engine code and the parity
case that prove it — and, for the ones the port did **not** retire, saying so out loud and re-filing them as
what they now are: **open findings against the engine.**

This is the same discipline the D.4 manifest applied to the deleted differential assertions: *a silently
shrinking gate is the failure; a documented retirement is not.* A silently mislabelled backlog is the same
failure. This file is the documented retirement — and the documented **non**-retirement.

The honest count: **the port retired 9 of the 14. Five survived it** — three carried bugs and two mixed
(one carried, one enhancement-residual on a resolved deadlock). "Retires ~14" (the plan's 2026-07-15
correction) was the ceiling of the write-path/IO *family*; the realised figure, verified issue-by-issue
below, is **9**.

## Retired by the port — **CLOSED**

Each row was verified against the compiled engine source and (where one exists) its `tests/coord-engine-parity/`
case. These are closed with an evidence comment pointing here.

| # | defect (bash) | retired because | proof |
|---|---|---|---|
| **#507** | one CJK/emoji issue body (legal under GitHub's 65,536-*char* cap, ~196 KB) marshalled through `argv` breaches `MAX_ARG_STRLEN`; the claim scan `E2BIG`-fails closed for every worker | **structural** — no body ever touches a command line: every write body is HTTP `StringContent` (`Transport.fs:191`) and every read body is pulled off the `HttpClient` response (`Reads.fs` `issueBody`, `markerFrom`). `execve`/argv marshalling of a body of any size does not exist. Same fix that retired #497; #507 is its single-body residual. | `argv_server.py` / case 43 leg B: `who` reads a candidate set `> 131072 bytes` and reports its holder rather than dying (`run.sh` case 43). |
| **#534** | `take` captured the budget-spending scan's stderr and replayed it only on the `[ -z "$pick" ]` branch — which a fatal `die_rate` can never reach — so an exhausted budget exited 75 naming nothing but "Try another item." | the typed `Error` propagates through `Result` to one `fail` sink (`Client.fs:80-82`) that **always** prints `Errors.explain e`; `RateLimited` → "GraphQL budget EXHAUSTED… resets in ~Nm…" + exit 75 (`Errors.fs:59-78`, `:28-30`). No capture-then-conditional-replay branch exists to swallow it. | `ratelimit_server.py` / case 40 (#418): `take` on an exhausted budget exits 75 **and** its output matches `grep -qi 'budget'`. |
| **#585** | `take` exited 0 on five outcomes, four of which claimed nothing (empty, blocked, unreadable board, lost-race storm) — so a `take && work_it` wrapper edited with no claim. | one distinct code per outcome; 0 **only** on an actual claim: nothing-startable → `ExitNone` (5), unreadable board → `fail e` (never `ExitNone`, so "could not look" ≠ "empty", #266), lost race → `ExitContended` (6), budget → 75, claim → `ExitGreen` (0). `Client.fs:1373-1410`. | `starved_server.py` / case 52: a nothing-startable queue exits 5, not 0 (`run.sh` case 52). |
| **#584** | bash's `active_claims` guarded `$cand` but not `$claims`; a `claims_of` `die` inside `$( )` exited only the subshell, dropping a LIVE claim so `who` exited 0 (a #344 recurrence). | a marker read is a typed `Result`; every claim read fails closed — candidate loop (`Scan.fs:568`), off-board arm (`Scan.fs:720`), `who`'s own loop (`Client.fs:540/622`) → `fail e` returns non-zero, never an empty table at 0. The subshell-die class cannot exist. | `pw_server.py` `FSGG_PARITY_MALFORMED_COMMENTS=42` / case 42: a faulted read on the in-flight holder makes `batch` refuse and NOT double-book the overlapping item (`run.sh` case 42, legs at 134–146). |
| **#550** | `release`/`heartbeat` picked a marker by the WORKER STRING alone across the claim set, so a twin (same id, different session) could delete/renew a live twin's lock on another item. | `release`/`heartbeat` take one explicit ref → `verifyHeld` reads THAT item's markers only and returns a `Held` carrying that item's winning **comment id**; the writes act by comment id (`Writes.fs:452-481`, "addressed by its comment id, never by the worker string. #550 is what happens otherwise"). Cross-item corruption is unreachable; same-item twins are refused by the #419 claim backstop (`Writes.fs:227-238`). | case 32 (#533): `release` cannot touch a marker that is not ours; case 44 (#419): the twin-claim backstop leaves a twin's marker intact. *(Residual: `verifyHeld` matches on worker-id, not a session predicate — a deliberately mis-targeted ref could still match a twin. Non-automatic, unlike the bash bug; noted, not blocking the close.)* |
| **#706** | `widen` never checked the caller HELD the claim — any worker could rewrite a live holder's touch-set. | **structural** — `Writes.widen` takes a `Held` as its first argument, and `Held` is `[<Sealed>]` with no public constructor; the only door in the widen path is `verifyHeld`, which returns `Some Held` only when the live CAS winner **is** the caller (`Writes.fs:322-338`, `:439`). `Client.fs:1852` refuses a non-holder before any write. A non-holder widen is *unexpressible*. | case 34 note (`run.sh`): the widen fixtures land only because the caller holds the item; `verifyHeld`'s fail-closed refusal is proven separately. |
| **#611** | `set-field` blamed "the value or the field" when the thing it could not parse was the ISSUE REF. | `setField` parses the ref FIRST (`Client.fs:1569-1572`) and fails with the distinct `unrecognised issue ref '…' (use a URL, owner/repo#n, or repo#n)` (`:126`) before the value/field path is reached. | *(no dedicated parity case — see the coverage note below.)* Fix #2 of the issue (accept a bare `<n>`) was **not** adopted; that is a `pnext-item` §4 docs correction, out of engine scope. |
| **#600** | the done-stamp had no green path for work resolved WITHOUT a PR (obsolete / duplicate / resolved-elsewhere stamped red on correct work), and `done --flip` wrote `Status: Done` on the way to a red refusal. | `Done.verify` takes `resolvedWithoutPr`; non-blank evidence → `Green(ResolvedWithoutPr)`, blank → red "Say what finished it" (`Done.fs:124-153`); exposed as `done <issue> --evidence "<reason>"` requiring the issue Closed. The board write now lives inside the `Green` arm only, so a red verdict writes no Status. | *(no dedicated parity case — see the coverage note below.)* The port names the flag `--evidence` and checks `state==CLOSED` (not `state_reason`); a `not_planned` close also passes — noted, not blocking. |
| **#613** | `epic_rollup` stamped a parent epic's board `Done` but never CLOSED the parent issue, so the upward climb died after one hop. | `Done.rollUp`'s terminal action stamps the board Done **and** `closeIssue` in the same step, then climbs to the grandparent (`Done.fs:654-671`; the comment cites #613 by name). | `doneflip` case B: `grep -q 'FS.GG.SDD#302 stamped Done and closed'` (`run.sh`, doneflip B). |

**Coverage note (#600, #611):** the engine code retires both defects, but neither has a
`tests/coord-engine-parity/` assertion pinning it. Closing on verified code is correct — the defect is gone
from the engine — but the regression is unguarded. The follow-up parity cases are tracked in
[#839](https://github.com/FS-GG/.github/issues/839), not a blocker (the corpus is a regression net, not the
proof the fix exists).

**Residuals note (#611, #550, #600):** three of the closed issues are closed over a smaller, genuinely
separate tail — #611's bare-`<n>` acceptance (a `pnext-item` §4 docs correction, not an engine change),
#550's belt-and-suspenders session predicate, and the coverage gaps above. Each is closed because the
**engine** defect it named is retired; the tails are tracked in [#839](https://github.com/FS-GG/.github/issues/839)
so they are not lost in this prose. (Contrast #646, kept OPEN because its residual is an explicit acceptance
bullet the issue calls "the one that matters most," not a separable tail.)

## NOT retired by the port — **KEPT OPEN, re-filed against the engine**

The port carried these across (or resolved only part). Per epic #729's rule — *a finding in the coordination
domain is filed against the ENGINE, not against bash* — the `engine-retires:phase-d` label (which promised
Phase D would retire them) is **removed**; they stay open as engine work, `bug`-labelled where they are bugs.

| # | why it survived the port | the real fix |
|---|---|---|
| **#523** *(CRITICAL)* | `widen` still PATCHes the body **before** the collision re-check. `Client.fs` widen: `Writes.widen` (the PATCH) at `:1868`, then `activeCollisions` (the #353 re-check that notifies colliding workers) at `:1884`. On an exhausted GraphQL budget the scan returns `RateLimited` → exit 75 **after** the body already landed — colliding workers never told. The engine's own `Writes.fsi` claimed to retire #523, but that argument (`rewrite`→`Rewritten`→`patchBody`) only orders the **grammar** validation before the PATCH; it never touched the collision re-check, which *is* #523. **That false claim is corrected in this PR** (`Writes.fsi`). | compute `activeCollisions` against the proposed touch-set **before** `Writes.widen`; refuse (body untouched) if the scan is unreadable; PATCH only once a verdict exists, then notify. (Backstop: roll the body back on an unreadable re-check.) |
| **#651** | the open-PR proof-of-life (#581) is a property of the stale MARKER, not of the ITEM. `Scan.fs` probes `Reads.prAlive` only inside `Some m -> if Reads.isStale …`; on the no-marker path (`holder = None`) nothing probes the branch, and `Schedulability`'s `None` case falls through to `Startable` (`Schedulability.fs:113-114`). A markerless Ready/Backlog item with a live `item/<n>-*` PR is still offered. Faithfully reproduced. | probe `Reads.prAlive` on the `holder = None` path too, and surface an `ItemPrOpen` verdict so `take`/`batch`/`who` skip it. Add a parity case (markerless item + open PR ⇒ not offered). |
| **#641** | `fsgg-coord issues` still lists PULL REQUESTS. `Reads.issues` returns `response.Body` **raw** (`Reads.fs:1252` — "emit the RAW bytes bash's `issues` prints"). The sibling `openIssues` **does** filter `pull_request` and even names #641 (`Reads.fs:1166`) — but that guards the *claim scan*, not the `issues` command the §4 duplicate-check reads. Faithfully reproduced. | filter `pull_request` at emit time in `Reads.issues` (preserving array shape), or add an `--include-prs` opt-in. |
| **#614** | `done --flip`'s roll-up still infers "all children Done ⇒ parent Done", so one PARTIAL child closes an open parent. The core grew a `Discharge` (`Partial`/`Completes`) type and `Done.rollUp` honours `Partial` (`Done.fs:36-39`, `:543-550`) — but the CLI **hard-codes `Done.Completes`** (`Client.fs:2218`) with no `--partial` flag and no auto-detection, so the `Partial` path is unreachable dead code and the partition assumption survives. | wire a discharge flag (`done --flip --partial "<why>"`, or invert the default) through `Options.fs`/`Client.fs:2218`, plus the `pnext-item` §4 warning. The plumbing already exists. |
| **#646** *(mixed)* | the titled **deadlock is structurally resolved**: `claim` no longer validates the body's `Paths:` (`Writes.fs:178-222`), so a malformed item is lockable by ref; with #706 making `widen` holder-gated, the repair happens under the lock, no unlocked race. But acceptance bullet 1 — `lint` red on an item whose `Paths:` *contains* an unmatchable token — is only half-met: `touchSetFindings` (`Client.fs:2769`) emits `BAD-TOUCH-SET` only when **every** token is unmatchable (`List.forall`, `Client.fs:2781`); a **partial** declaration falls through silently. | (enhancement, not a carried bug) switch `forall`→`exists` at `Client.fs:2781` and name the offending subset, so a partial-unmatchable declaration is caught at filing time. `claim --paths` from the issue is **not** needed. |

## The `Writes.fsi` correction shipped here

`Writes.fsi`'s module docstring claimed *"The same move retires #523."* It does not — see #523 above. The
docstring's own rule is *"a module that overstates its scope is a module that gets trusted for things it does
not do."* The claim is corrected to say what the move actually does (enforces #523's grammar ordering by
construction) and what it does not (the collision re-check that is #523 proper still runs post-PATCH; #523
stays open). No behaviour changes; the shim is untouched, so no `repos.lock` relock.

## The rule this leg reaffirms

> **Taking the payoff means verifying it, not assuming it.** The port was *for* the write-path/IO family, and
> it retired most of it — but "labelled `engine-retires`" was never proof of retirement. Nine were retired and
> are closed with their proof; five were not and are now honest, open, engine-owned findings. The gap between
> "designed to retire ~14" and "verifiably retired 9" is the whole reason this manifest is a decision in the
> diff and not a silent close of the label.

---

*This manifest executes the "take the payoff" leg of epic #729. It closes no issue it did not verify against
the compiled engine, and it keeps open every issue the engine did not actually retire.*
