---
schemaVersion: 1
workId: 2525-partial-read-fail-open
title: Partial Read Fail Open
stage: specify
changeTier: tier1
status: specified
publicOrToolFacingImpact: true
---

# Partial Read Fail Open Specification

Prose status: specified

## User Value
A board driver can trust that "no active items" and "nothing schedulable" are measured facts, so it never declares the board finished while claims are held and work is outstanding.

## Scope
- SB-001: Board-scan pagination and row-parse completeness in Scan.fs, plus the DriverEvents active-inventory and batch schedulable-set projections that render its output as an answer.

## Non-Goals
- SB-002: Do not implement later lifecycle commands or Governance enforcement in this specification.

## User Stories
- US-001 (P1): As a user, I can A board driver can trust that "no active items" and "nothing schedulable" are measured facts, so it never declares the board finished while claims are held and work is outstanding.

## Acceptance Scenarios
- AC-001 [US-001] [FR-001]: Given Partial Read Fail Open is available, when the user exercises it, then they can A board driver can trust that "no active items" and "nothing schedulable" are measured facts, so it never declares the board finished while claims are held and work is outstanding.

## Functional Requirements
- FR-001: A board scan that cannot prove it observed every page returns Error rather than Ok with a short row list. (Stories: US-001; Acceptance: AC-001)
- FR-002: A board node selected as an Issue or PullRequest whose identity cannot be parsed returns Error rather than being silently dropped. (Stories: US-001; Acceptance: AC-001)
- FR-003: driver --events never renders the bare "no active items" line when the projection carries any Unreadable classification. (Stories: US-001; Acceptance: AC-001)
- FR-004: A complete read of a genuinely empty active set still renders "no active items" unchanged. (Stories: US-001; Acceptance: AC-001)
- FR-005: batch reports the measured candidate count alongside "nothing schedulable right now". (Stories: US-001; Acceptance: AC-001)
- FR-006: An item absent from the current facts batch never has its cursor-held last-known holder presented as its current holder. (Stories: US-001; Acceptance: AC-001)
- FR-007: Every guard added here reds its fixture when the guard is deleted. (Stories: US-001; Acceptance: AC-001)

## Root Cause Diagnosis (.github#2525 AC6)

> **AMENDED AFTER INDEPENDENT REVIEW ROUND 1.** Two real causes were in play and this section originally
> carried only one of them. The critic settled which is which **by execution rather than argument**, and
> the ordering below is its finding, not a compromise between two opinions.
>
> **The proximate cause of the reported incident is a poisoned scan cache**, not a partial paginated read.
> `Scan.board` returns `Ok rows` from a parseable cache entry *before* `scanFresh` is ever called
> (`Scan.fs:663-670`), and `dotnet test` had been writing four-row test-fixture boards into the
> developer's own `~/.cache/fsgg-coord` — so live `batch`/`take`/`driver` reads served a fabricated board
> for the cache's TTL. **No completeness guard can detect that by construction**: the poisoned board is
> complete, well-formed and internally consistent. It is simply not the board. That cause is fixed at the
> write, and is written up under "The proximate cause" below.
>
> The pagination and row-parse defects described after it are **also real and independently confirmed** —
> they are reachable on the fresh-scan route and are fixed here — but they are not what produced the two
> observations in the issue. Their write-up is retained unchanged, demoted to its true standing.
>
> One consequence is recorded honestly rather than smoothed over: the original **PM-001** reasoned that
> the cache route needed nothing because "it expires on its own". That is what routed the implementation
> away from the proximate cause. See `plan.md` PM-001 for the correction.

## The proximate cause — a poisoned scan cache (amended, round 1)

`Cache.root()` falls back to `$XDG_CACHE_HOME/fsgg-coord`, then to `~/.cache/fsgg-coord`
(`Cache.fs:17-28`). Four test legs performed **real** scans with no cache isolation, so their fixture
boards were written to the developer's own cache by `Cache.putScan`:

- `tests/FS.GG.Coord.Cli.Tests/ChoresTests.fs` — three `#2254` legs called `reconcileIds`/`Client.batch`
  outside the `withCache` helper defined ten lines above them. These wrote
  `scan-fs-gg-coordination.json`, **the real board's own cache key**, which is what makes them poisoning
  rather than merely untidy.
- `tests/FS.GG.Coord.Cli.Tests/SchedulingCostTests.fs` — the `#2313` leg isolated its cache **key** (a
  GUID board title) but not its **root**, depositing one never-read
  `scan-fs-gg-coordination-2313-<guid>.json` per suite run.

Reproduced at this head with `XDG_CACHE_HOME` pointed at a scratch directory, so the real cache was never
touched: reverting the `ChoresTests` isolation re-creates `scan-fs-gg-coordination.json` at **204 bytes**,
the same artefact measured during the incident.

The acceptance check for this is `tests/FS.GG.Coord.Cli.Tests/CacheIsolationTests.fs`. Its round-1 form
**could not fail**, and that is recorded here rather than quietly replaced: it filtered leaked files for a
marker string that exists only in the sandbox *path*, while `Cache.putScan` writes the rendered rows and
nothing else (`Cache.fs:194`), so the filter was empty by construction and the assertion passed
unconditionally. It also early-returned on `Directory.Exists` with no `else`, asserting nothing at all on CI.
The signal was present in the round-1 inversion matrix and went unexamined — the leaking mutation turned
**two** tests red, not three. The predicate is now a before/after comparison of content digests captured
before any test runs, preceded by a real `putScan` round-trip that proves the detector works on a write it
made itself. This is the same defect class as the round-0 finding about a guard named for a line it did not
assert, one layer up.

The fix is at the write, in two layers, because the read cannot help:

1. each leg gets the isolation it should always have had; and
2. the assembly redirects `Cache.root()`'s **fallback** to a sandbox before any test runs, via a custom
   xUnit test framework (`tests/FS.GG.Coord.Cli.Tests/CacheSandbox.fs`) — so a future leg whose author
   forgets step 1 still cannot reach the user's cache. `XDG_CACHE_HOME` is redirected rather than
   `FSGG_COORD_CACHE` precisely so that every existing fixture's own isolation is left untouched.

## The fresh-scan causes (unchanged from round 0)

AC6 requires the diagnosis, not the suppression. Both reported symptoms are one cause with one
amplifier, and neither is exhaustion or backoff — budget was healthy throughout, with the REST window
freshly reset.

### The cause: the board scan returns `Ok` on reads it cannot prove complete

`Scan.scanFresh` is the single paginated board read behind `batch`, `take`, `next`, and
`driver --events`. Its pagination loop ends the walk and reports success on two unprovable conditions
(`src/FS.GG.Coord.GitHub/Scan.fs:552-561`):

- `pageInfo.hasNextPage` missing, null, or not a Boolean is read as `false`, so the walk stops after
  page one and returns `Ok(acc @ rows)`.
- `hasNextPage: true` with a missing or null `endCursor` falls into the `| _ ->` arm and returns
  `Ok(acc @ rows)` — the server said explicitly that another page exists and the scan reports success.

This exact defect was already found and fixed once, for the external-owner board lookup, and the fix
was never ported here. `src/FS.GG.Coord.GitHub/Board.fs:485-504` carries both refusals and states the
rule in its own comment: *"COMPLETENESS IS A REQUIRED BOOLEAN, not an optional hint. Missing/null/string
used to fall through as `false` and turn an incomplete read into the definite absence."* The board scan
is the more load-bearing of the two reads and has the weaker guard.

A second, row-level variant sits in the same function: `Seq.choose parseRow`
(`src/FS.GG.Coord.GitHub/Scan.fs:545-548`) drops every node it cannot parse, with no counter and no
trace. One arm of that drop is correct — a draft card has no issue behind it and must not become a
phantom ref. But a node the query selected `... on Issue` / `... on PullRequest` whose `number` or
`repository.nameWithOwner` came back unreadable takes the *same* silent exit
(`src/FS.GG.Coord.GitHub/Scan.fs:334`), so a real row vanishes indistinguishably from a draft.

### The amplifier: a truncated batch is cached and re-served for 90 seconds

`Cache.putScan`'s invariant is *never EMPTY*, not *never PARTIAL*
(`src/FS.GG.Coord.GitHub/Cache.fs:180-186`, guard `isNonEmptyScan`). A 100-of-N row batch is
non-empty, so it is written to the scan cache and re-served to every `Scheduling` caller for the
90-second TTL (`src/FS.GG.Coord.GitHub/Cache.fs:31`). This is why `batch` returned an empty ranking
**twice** and then, unchanged, returned the full ranking on the third invocation: the first read
truncated, the second was served the truncated cache, and the third fell past the TTL and re-read.
It is also why `ready --json` disagreed in the same minute — `ready` reads with `Cache.Reconciling`,
for which the cache is unreachable by construction (`src/FS.GG.Coord.GitHub/Cache.fs:113-126`), so it
saw the rows `batch` could not.

Note that no *separate* cache fix is required: once the scan refuses to return a batch it cannot prove
complete, a partial batch never reaches `putScan` at all.

### Why the shortfall renders as a substantive answer

Both projections treat "fewer rows" as "fewer facts" rather than "a worse read".

- `driver --events`: a previously-active ref absent from the batch is synthesized as `Unreadable`
  (`src/FS.GG.Coord.Core/DriverEvents.fs:164-178`) — correct as far as it goes — but
  `isActive Unreadable = false` (`src/FS.GG.Coord.Core/DriverEvents.fs:126`), so those refs are filtered
  straight out of `Active` (`src/FS.GG.Coord.Core/DriverEvents.fs:263`) and `renderText` emits the bare
  positive assertion `no active items` (`src/FS.GG.Coord.Core/DriverEvents.fs:321-323`). The earlier
  repair for this class (.github#2375/#2385) put the signal on the *transitions* line only, and the
  active line — the one the termination rule reads — was left able to assert an empty inventory over an
  unread board.
- `batch`: fewer candidates produce `Green { Chosen = []; Decisions = [] }`
  (`src/FS.GG.Coord.Core/Batch.fs:629-632`). Every explanatory surface downstream is keyed on
  `Decisions` and silent on the empty list — `starvedBanner`, `sayPassedOver`, and even
  `explainRanking` — so `batch --text` prints one line on stdout, nothing on stderr, and exits 0,
  byte-identical to a genuinely empty board.

### The stale-holder defect (.github#2525 AC5)

`missingActiveRefs` splices the **cursor's** last-known state into a present-tense sentence via
`%A{state}` (`src/FS.GG.Coord.Core/DriverEvents.fs:174`), and the sticky-cursor fold re-pins that
original value for any ref that stays absent (`src/FS.GG.Coord.Core/DriverEvents.fs:243-260`). Because
`Unreadable` bypasses idempotency suppression (`alwaysReports`), the stale name is re-emitted on every
subsequent read and can never be superseded — the only thing that could supersede it is a fresh
classification of a ref that by definition is not in the batch. That is how `#2512` was reported
against `curlew-307b`, who had released cleanly, while `rook-94e0` held it.

### Distinct causes deliberately left out of scope

Adjacent fail-open reads found while establishing the above are *different* causes and are filed
separately rather than folded in here: GraphQL responses read without inspecting `errors`
(`src/FS.GG.Coord.GitHub/Reads.fs:715`, `:1102`, `:2543`), and the unbounded `first: N` connections in
`Board.fs`. The `RateLimited`-swallowing arms at `src/FS.GG.Coord.GitHub/Scan.fs:1152` and `:1323`
already carry an issue (.github#1924) and are not re-filed.

## Ambiguities
No material ambiguities recorded.

## Public Or Tool-Facing Impact
- This specification is an SDD lifecycle artifact and command-report contract input.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd clarify --work 2525-partial-read-fail-open`.
