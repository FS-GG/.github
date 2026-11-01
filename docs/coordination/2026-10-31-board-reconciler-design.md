# BOARD-TEND-01: `board-v2 tend`, an F# reconciler that derives, plans and applies safe Project 3 fixes

Design, 2026-10-31. Delivery [#5136](https://github.com/FS-GG/.github/issues/5136) under Outcome
[#4730](https://github.com/FS-GG/.github/issues/4730) (COORD-EFF-02). This is phase 1 of #5136: the
design only. It makes no board write and changes no code, contract or workflow. Root (Programme/root)
owns approval and all acceptance. [Root's decisions](https://github.com/FS-GG/.github/pull/5137#issuecomment-6548516184)
on D1-D12 (2026-10-31), Root's rulings on the independent reviews, and the owner-authorized decision 9 on
Delivery closure ([#4730 comment](https://github.com/FS-GG/.github/issues/4730#issuecomment-6551301543), 2026-11-01) are folded in below and listed in §13. Phase 2 (implementation PRs, §12) starts only after this design passes independent review.

## Why

The user asked for automatic checking, updating and fixing of Coordination Project 3
(`PVT_kwDOEYAWY84Bldpa`), done by deterministic F# machinery in the coordination engine rather than by
manual Root passes and Python glue (#5136).

Today the board has two detectors and four narrow writers. Every other field change is a manual Root
write:

| Piece | What it does | What it cannot do |
|---|---|---|
| [Board-currency rules](../../scripts/board-currency-rules.py) R1-R9, U ([design](2026-10-31-board-currency-gate.md)) | Pure rule set over a snapshot; findings in [#4985](https://github.com/FS-GG/.github/issues/4985) | Write anything |
| [Board-drift observer](../../scripts/board-drift-observer.py) rules a-e, x | Hourly heuristics; single writer of the #4985 body | Write a board field |
| [Merge gate](../../tools/routine-delivery.py) `--board-gate` (#5106) | One post-merge Delivery Status write (In review, or In progress with `fsgg:delivery-continues/v1`) through an item-scoped guard with durable custody; from §12 PR 5, the continues/completes declaration check | Any other field, any card other than the merged Delivery |
| Built-in "Item closed → Done" workflow | Sets Done when an issue closes; Root decision 2 made it the only writer of Done | Cards added after closure, or closed before it was enabled |
| `board-v2 migration-apply` | Absent-value initialization of explicitly declared values ([`V2Migration`](../../src/FS.GG.Coord.GitHub/V2Migration.fsi)) | Infer a value; replace a populated value; scale (#5123) |
| [Done-retention archiver](https://github.com/FS-GG/.github/issues/5119) (#5119 PR 5, held; Python, hosted workflow) | Archive Done beyond the newest 200 | Anything else |

The coordination engine does not derive Status, Track or Work level from native issue facts anywhere:
`board-v2 refresh` writes only Observation, migration initializes only values its caller declares, and
enrollment derives only Roadmap from a plan reference
([`V2Migration.fsi`](../../src/FS.GG.Coord.GitHub/V2Migration.fsi), "no title, leaf, Status or Track
inference"). The reconciler is the first component that infers them.

### Cases from the 2026-10-31 manual passes

These are the acceptance fixtures (§11). They come from #5136 and the cohort-90 apply record (#5133):

1. 11 cards had no Status.
2. 128 cards lacked Work level or Track.
3. Closed cards were not Done.
4. 5 cards were In review with no open PR waiting for review.
5. 34 cards were In progress; about 13 were stale or finished.
6. 25 Backlog cards had a body saying "Blocked on: Nothing".
7. Outcome Status did not follow its children.
8. Enrolling 114 new cards took about 3 hours, 63 migration invocations (31 prepares, 30 applies,
   2 inspects) besides the enrollment stages, and seven Root rulings: a `--main-revision` assertion
   (#5113), a reader-identity hand rebind (#5120), a 207-row inspect accepted as Unknown (#5121), a
   re-split into 29 scopes of at most 4 cards after 19 effects hit the 270-second ceiling (#5123), the
   Roadmap inheritance rule, closed cards targeting Done, and 14 exclusions.

The live board at 20:40Z had 222 items, all issues, none archived: Done 123, Blocked 51, Ready 33,
In progress 15, Backlog 0, In review 0; Work level Delivery 197, Outcome 25. That is one observation,
not a fixture.

## 1. Shape

```text
reads (batched, bounded)  ->  Snapshot  ->  derive (pure)  ->  Plan
                                                               |- Apply    -> guarded writer -> readback
                                                               |- Propose  -> #4985 (once per ID)
                                                               '- Refuse   -> plan report only
```

- **Command.** A new verb in the `board-v2` dispatch arm, `board-v2 tend` (D1). Root chose the name
  because the kernel table already has a V1 `reconcile` (chore remedies,
  [`Options.fs`](../../src/FS.GG.Coord.Cli.Kernel/Options.fs)) and the retired `coord-board-reconcile.yml`
  diagnostic shares that word. "The reconciler" in this document means this command.
- **Modes.** Without `--apply` it only reads and writes the plan file. That is the default. `--apply`
  also needs an enable file (§8) and the writer lock.
- **Modules.** Pure derivation in `FS.GG.Coord.GitHub/V2Tend.fs` beside the pure enrollment
  planner; reads in `V2TendReads.fs` through the existing batched acquisition
  ([`V2InspectionAcquisition`](../../src/FS.GG.Coord.GitHub/V2InspectionAcquisition.fs), 25 aliased
  queries per send); writes through the reconciler's own closed change type (§6); the CLI in
  `BoardV2Application`. The command is registered in the
  [writer census](v1-writer-census.json) as a writer.
- **Not a revival.** ADR-0091 retired the legacy v1 automatic projection writer. This is a new V2
  writer with its own rule set, enable switch and custody, and it never uses `Done.rollUpParents`.

## 2. Inputs and reads

### Facts

Native state only, as in the board-currency rule set:

- per card: item id, `isArchived`, and the Status, Work level, Track and Roadmap values, each with its
  `updatedAt`;
- per issue: node id, repository and `isPrivate`, `state`, `stateReason`, `createdAt`, `closedAt`, `updatedAt`,
  body, `author`, `editor` and `lastEditedAt` (for S9's Root-authorship check), native parent,
  sub-issues (complete list), `blockedBy` (complete list);
- for enrollment candidates only: `Issue.projectItems(includeArchived: true)`, so an archived Project 3
  item is seen (§3, enrollment state);
- open pull requests in the organization: number, repository, `isDraft`, `updatedAt`, body;
- for the few cards that need it (below): the issue's cross-reference timeline (PRs that mention it,
  with state, `mergedAt` and body) and its recent comments;
- the project schema: fields, options and the `workflows` list (so "Item closed → Done" is read, not
  assumed);
- enrollment: the private binding's enrolled rows (a local file in Root custody) and the public
  commitment at `main` ([`board-v2-import-manifest.json`](board-v2-import-manifest.json)), both read
  locally with no network call;
- local custody: the reconciler's own records and the merge gate's records
  (`$XDG_STATE_HOME/fsgg/board-gate`).

A PR is **linked** to a Delivery when the board-currency `linkage_of()` verdict for that PR has no
failures and names exactly that Delivery: the body references exactly one Project 3 Delivery through
`Refs` or a closing keyword (after CRLF normalisation and removal of code spans and fences), the Delivery
has a parent Outcome, and no reference is unreadable. This is the linkage the merge gate requires and
lifecycle dispatch rule 1 asks for. A PR that mentions the Delivery but fails `linkage_of()` (no
Delivery resolved, several Deliveries, no parent Outcome, an Unknown) is **loosely linked**: it never
drives an automatic Status move, only a proposal. A PR is never a Delivery. Because R1 stays in Python
(§9), one shared conformance corpus is run against both implementations so they cannot drift.

### Reads and their cost

| Read | Shape | Sends at 222 items | Sends at 500 items |
|---|---|---:|---:|
| A. Board and issue facts | `items(first:50)` with the four field values and the issue fields above, `subIssues(first:50)`, `blockedBy(first:50)`; the first page also carries the schema and workflows | 5 | 10 |
| B. Open PRs | one organization search, `is:pr is:open`, 100 per page; it only selects candidates (below) | 1 | 1 |
| C. Targeted evidence | timeline cross-references (`last:25`), `comments(last:20)` and, for enrollment candidates, `projectItems(includeArchived: true)`, 25 issues aliased per send, only for cards a rule needs (cards the B search shows with an open PR, In review or In progress without an open linked PR, Blocked without an open native blocker, Backlog candidates for Ready, Outcomes with every child closed, enrollment candidates) | 3-4 | 5-7 (worst case 20) |
| D. Overflow | sub-issue continuation pages for an Outcome with more than 50 children | 0-1 | 0-2 |
| **Total** | | **about 9-10** | **about 16-20 (worst case 33)** |

The C row is derived from the live mix, not assumed. A read-only snapshot taken for the review at
2026-10-31T21:19Z had 222 cards, 98 of them open, and 19 open PRs in the organization. 56 open cards
needed a targeted read: 51 Blocked cards with no open native blocker (their `fsgg:blocked-on/v1` marker
must be read) and 5 In progress or In review cards with no open PR referencing them. That is 3 sends of
25, or 4 with the Outcome and candidate legs. At 500 cards the same proportions give about 126 open
cards needing reads, 6 or 7 sends, and A doubles to 10, hence about 16-20. The worst case is every card
needing a targeted read.

- Pages stay at 50 items. Root's ruling on #5130 was to take no saving that raises points, and pages of
  50 with nested connections cost about 1-2 GraphQL points each, as the currency fetch measures today
  (5 requests, 5 points at 222 items).
- No REST comment read happens in the board pass. A new comment moves the issue's `updatedAt`, so a
  comment's `createdAt` never exceeds it, and R4's activity maximum (issue `updatedAt`, the Status
  value's `updatedAt`, linked PR `updatedAt`, comment `createdAt`) is the same without the comments.
  Comments are read only in C, and only for marker legs (`fsgg:blocked-on/v1`,
  `fsgg:outcome-closure-proposed/v1`, `fsgg:outcome-accepted/v1`).
- Merged PRs are not searched org-wide. A rule that needs merged evidence (S4, P-ACCEPT) reads that
  card's timeline in C, so a PR merged months ago is still seen, unlike a 7-day search window.
- **The timeline decides, the search selects.** Search indexing lags, so the B search only selects S3 and
  S3b candidates. Whether a card has an open, draft, merged or loosely linked PR is decided from its
  timeline read in C and confirmed by the immediate re-read before the send (§6). A PR the search has not
  indexed yet is picked up on a later run; missing a move for one run is safe.
- A board whose `totalCount` exceeds 500 is refused on the first page, as `GraphQl.drain` does since
  #5122. A truncated connection is Unknown for the rules that need it (§4c), never an empty list.

**Budget.** The read ceiling is `8 + ceil(rows/50) + ceil(rows/25)` sends (22 at 222, 38 at 500), and
the deadline is 120 seconds for reads. Writes have their own ceiling (§6). Hourly runs cost about
24 × 20 ≈ 480 read sends a day at 500 items. For comparison, #5119 measured the hourly drift fetch at
26 calls after its cut, and a 500-row `inspect` at 175 sends against its 1,064 ceiling. The reconciler
reads less than one inspection per run. Phase 2 measures the real per-run cost and prints it in the
plan (`requests`, `graphqlPoints`, `restCalls`), which is #5136 Done-when 4.

## 3. Derivation

`V2Tend.derive : Parameters -> Snapshot -> Plan` is pure. Its only clock is `Snapshot.ObservedAt`,
so one snapshot always gives the same plan (board-currency lesson 7). An effect ID is
`<rule>:<item>:<field>:<target>` with no age or count, so an unchanged situation keeps its ID.
Parameters are part of the rule-set version `fsgg:board-tend-rules/v1`.

**Derivation runs to a fixpoint** ([Root's ruling
B](https://github.com/FS-GG/.github/pull/5139#issuecomment-6548815933), refined by [the round-3
rulings](https://github.com/FS-GG/.github/pull/5139#issuecomment-6548872887)). Work level is structural
and needs no other field (below). Some other rules read values filled in the same run: T1i reads the
parent's Track, and O1 reads the children's Statuses after S3, S3b or S4 moved them.

1. **Visibility within a round.** `derive` evaluates cards in topological order of the native
   hierarchy: parents before children for fills, children before parents for the roll-up. An effect
   computed for one card is applied to an in-memory copy of the snapshot at once, so every card
   evaluated after it in the same round sees it. Rounds repeat until one adds nothing.
2. **Only Track and Status have fallbacks, and they wait for settled inputs.** The fallbacks are T1f
   (the `Follow-up` default), S9 (Backlog) and O1's fill of an empty Outcome with Backlog. A fallback
   applies only when every input it could inherit from has **settled**: already populated on the board,
   or produced by a non-fallback rule in an earlier round. An input that is still pending, or that only a
   fallback would fill, defers the fallback, by which time that input is usually on the board. So a child
   never takes `Follow-up` while its parent's Track is about to be filled from a declaration.
3. **Cycles collapse.** If the remaining dependencies still form a cycle, its cards are collapsed into one
   unit (a strongly connected component). Within the unit, declarations decide; a field no declaration
   decides becomes a `track-undetermined` or `status-undetermined` proposal. Nothing inside a cycle waits.
4. **No indefinite deferral.** A `deferred: input-pending` effect becomes a proposal after 3 runs or
   24 hours, whichever comes first. The reconciler counts runs and first-deferral time in its local state
   directory; that record is an input to `derive`, so derivation stays a pure function of its inputs.
   Proposing does not give up the effect: if its inputs settle later and its rule is enabled, it is
   still applied automatically, and its proposal clears. For a deferral caused by a settle window (W1h's
   leaf window), the runs and hours are counted from the moment the window ends, not from the first
   deferral.
5. **Termination.** Fills only move a field from empty to a value, and never again. S3, S3b and S4 exclude
   each other on the PR facts (an open non-draft linked PR, only a draft one, none at all), so no move
   on a Delivery can be undone by another move in the same derivation. O1 reads only its children's
   Statuses, and no rule reads a parent's Status, so Status changes propagate only upward, one hierarchy
   level per round. Each field therefore changes at most once in a derivation, and the iteration stops.
   It is capped at hierarchy depth plus 2 rounds; reaching the cap is a refusal (`fixpoint-not-reached`),
   not a partial plan.
6. **Send order.** Each effect records the effects it depends on, whichever way the dependency runs: a
   child's inherited Track depends on the parent's fill, and an Outcome's roll-up depends on its
   children's moves. The writer sends strictly in dependency order.
7. **Refusals propagate.** Every dependant of a refused, deferred or uncertain effect is deferred to the
   next run, transitively.
8. **One effect per field.** Successive moves on one field collapse into one effect from the original preimage to the
   final target, carrying every rule in the chain (for example S9 then S3); every rule in the chain must
   be enabled.

Two properties follow, both over the **enabled** rule set and both tested (§10):

- **Idempotence.** Let k be the length of the longest fallback chain among the enabled rules (a fallback
  whose input is itself filled by a fallback adds one link). Applying plans repeatedly settles within k
  applications: the k+1st derivation, with no new native change, yields no Apply effect. A fallback held
  back for an unsettled input is listed as `deferred: input-pending`, and only those entries produce
  effects in later applications.
- **Liveness.** With no new native change, after max(k, 3) runs or 24 hours, whichever comes first,
  every field of every card is filled, refused or proposed. No field stays deferred, because of the
  3-run or 24-hour expiry above (which also covers W1h's leaf settle window).

| Parameter | v1 value | Why |
|---|---|---|
| `activityHours` | 48 | The same as board-currency R4 |
| `doneSettleMinutes` | 15 | Gives the built-in Done workflow time to act before the backstop writes (S1) |
| `reviewSettleHours` | 48 | An In review card must have stayed so this long before S4 moves it, so a PR about to open is not raced |
| `doneVisible` | 200 | #5119's retention bound |
| `closeSettleHours` | 48 | C1 closes a Delivery only this long after its most recent merge (review of #5150) |
| `leafSettleHours` | 24 | W1h writes Delivery on a leaf only once the issue is this old (round-4 ruling 2) |
| `maxItemsPerRun` | 100 | Write ceiling per run (§6) |

Body sections are read only in a closed grammar: a first line
`Work level: <v> · Track: <v> · Intended Status: <v>` (the **intended-fields line**, emphasis markup
removed, each value one of the field's option names) and the `## Blocked on`, `## Done when` and
`## Acceptance evidence` sections. Dispatch rule 1 asks for these values but gives no exact grammar, so
the contract-amendment PR (§12) codifies this grammar in the lifecycle contract before any rule that
reads it is enabled. Any other form is "not declared", never a guess.

### Work level

Work level is **structural** ([round-3 ruling 1](https://github.com/FS-GG/.github/pull/5139#issuecomment-6548872887)). It is decided in one pass over the native
sub-issue hierarchy and reads no other board field, so it takes part in no cycle; only its leaf branch
can wait, and only on time and the parent's membership. Never overwrite a populated value. For an empty value:

1. **Structure (W1h).** A card with at least one native sub-issue is an Outcome. The leaf branch is
   narrower ([round-4 ruling 2](https://github.com/FS-GG/.github/pull/5139#issuecomment-6548916283)): a card with no sub-issues is written as a Delivery only when
   (a) its native parent is a Project 3 card, (b) the issue is at least 24 hours old (`leafSettleHours`,
   so a just-filed issue that is about to receive sub-issues is not called a Delivery), and (c) the body
   declares no different Work level. While W1d is not enabled, the declaration is still read for (c),
   but only to refuse. If any of (a)-(c) fails for a reason that can change, the effect is deferred with
   the usual expiry (§3, fixpoint) and then becomes a proposal.
2. **Declaration (W1d).** The body's `Work level:` value on the intended-fields line either confirms the
   structural value or conflicts with it. A conflict is refused (`work-level-conflict`), never hidden.
3. A leaf with no parent has no structural value. Its declaration decides (W1d); with no declaration it
   is a proposal, `work-level-undetermined`.

### Track

Never overwrite. For an empty value:

1. **A declared Track wins (T1d)** ([round-6 ruling](https://github.com/FS-GG/.github/pull/5139#issuecomment-6548973997), mirroring W1h(c)). If the intended-fields
   line declares a Track, T1i and T1f never write that card's Track. With T1d enabled, T1d writes the
   declared value. While T1d is off, the declaration is a refusal-only read: the effect is deferred, and
   on expiry it becomes a `track-declared-pending` proposal.
2. **Inheritance (T1i), a heuristic.** An undeclared card that is structurally a leaf under a Project 3
   parent whose Work level is (or structurally will be) Outcome inherits that parent's Track, whether or
   not its own Work level has been written yet ([round-5 ruling](https://github.com/FS-GG/.github/pull/5139#issuecomment-6548947046)). A young leaf whose W1h write
   is still in its 24-hour window therefore inherits at once. In live data, though, inheritance
   disagrees with about 36% of the existing Delivery-Outcome pairs whose Tracks people set, so T1i is not
   in the recommended unattended starting set (§8). While it is not enabled, its would-be value is shown
   in the plan for the comparison, and the field expires into a `track-undetermined` proposal.
3. **Default (T1f).** `Follow-up`, only for an undeclared card with no Project 3 parent. It is a
   fallback: it applies only once settled, and it waits while the card's own Work level is pending (for
   example W1h still deferring).

`Follow-up` is the default because it claims less than `Active delivery` (D5, accepted).

### Roadmap

Never overwrite. For an enrolled card the desired value is its enrolled row's `roadmap`, because the
binding is the authority. Roadmap is written only to enrolled cards (D6): for an unenrolled Delivery the
reconciler computes the value for the enrollment request, and the field is filled from the enrolled row
after `bind-runtime`. That value is the parent's enrolled Roadmap, with a
`#anchor` dropped when the value is a protected document, which cannot carry one (the cohort-90 rule
Root accepted). The value must pass `V2ProjectionSource.programmePlanBlobGrammar`, and its document must
exist on `main` (rule RM1). A parent with no enrolled Roadmap (for example #5097), or a plan that does
not pass (SDK-PATCH-01, #5114), is a proposal naming the plan owner. An Outcome with no Roadmap is always
a proposal: choosing an owning plan is Root's.

### Status of a Delivery

Evaluated in order; the first rule that matches decides. `OPR` is the set of open non-draft linked PRs,
`DPR` the open draft linked PRs and `MPR` the merged linked PRs, all in the strict `linkage_of()` sense
(§2). `LPR` is the set of loosely linked PRs of any state.

| Rule | Condition | Desired | Class |
|---|---|---|---|
| S1 | Closed (any `stateReason`), Status not Done, `closedAt` older than `doneSettleMinutes` | Done | Apply |
| S2 | Open and Status Done | — | Propose `done-open` |
| S3 | `OPR` not empty, Status Backlog, Ready or In progress | In review | Apply |
| S3b | `OPR` empty, `DPR` not empty, Status Backlog or Ready | In progress | Apply |
| P-LINK | `OPR` and `DPR` empty, an open PR in `LPR` | — | Propose (the PR's `linkage_of()` failure is named) |
| S4 | Status In review, `OPR`, `DPR`, `MPR` and `LPR` empty (closed unmerged PRs are ignored), In review for longer than `reviewSettleHours`, timeline complete | In progress | Apply |
| C1 | Open issue with a **populated** Work level of Delivery and Status **In review**; `OPR` and `DPR` empty, no open PR in `LPR`, `MPR` not empty, timeline complete; the most recent merge in `MPR` is at least `closeSettleHours` old and carries `fsgg:delivery-completes/v1` (and not `fsgg:delivery-continues/v1`); and the issue was never reopened after a `fsgg:tend-closed/v1` comment or a C1 custody record | Issue closed (COMPLETED), with the dispatch-rule-3 comment | Apply (decision 9) |
| P-REOPENED | An issue C1 would close, but it was reopened after a `fsgg:tend-closed/v1` comment or a C1 custody record | — | Propose `reopened-after-close`; never closed again automatically |
| P-ACCEPT | Status In review or In progress, `OPR` and `DPR` empty, `MPR` not empty, `## Acceptance evidence` present and not "None yet.", and C1 does not apply (disabled, or the last merge continues) | — | Propose `acceptance-ready` |
| S5 | Status In progress, `OPR` and `DPR` empty, no activity within `activityHours` | — | Propose `stale-active` (to Ready, Blocked, or acceptance) |
| P-READY | Status Backlog, `## Blocked on` is "Nothing" or "Nothing external", no open native blocker | — | Propose Backlog → Ready; for an unenrolled card the proposal says selection also needs enrollment or a `root-admit-unenrolled` marker |
| P-UNBLOCK | Status Blocked, every native blocker and `fsgg:blocked-on/v1` blocker closed | — | Propose Blocked → Ready |
| P-NOBLOCKER | Status Blocked, no blocker named | — | Propose (R3 `no-blocker`) |
| S9 | Open, Status empty, and the intended-fields line says Backlog or says nothing | Backlog | Apply |
| S9r | Open, Status empty, the intended-fields line says Ready, and the line is Root-authored (below) | Ready | Apply |
| P-INTENT | Open, Status empty, the intended-fields line says anything else (In progress, In review, Blocked, Done), or says Ready without verified Root authorship | — | Propose the declared value; S9 still fills Backlog |

Notes:

- **C1 closes a Delivery when its linked work is merged** ([decision 9](https://github.com/FS-GG/.github/issues/4730#issuecomment-6551301543),
  narrowed by [the review of #5150](https://github.com/FS-GG/.github/pull/5150)). The owner amended "tend never closes issues" for Delivery
  issues only. C1 uses the strict `linkage_of()` linkage (exactly one Delivery), needs at least one merged
  PR and no open or draft one, strict or loose, and needs a complete timeline. Because a Delivery may need
  several PRs, it also needs all of:
  - Status **In review**, which the merge gate writes after a merge. A card moved back to In progress says
    the work continues, and C1 leaves it.
  - A settle window of `closeSettleHours` (48) after the most recent merge, so a follow-up PR can open.
  - A **populated** Work level of Delivery. A structural-only Delivery (a leaf with no Work level yet) is
    never closed.
  - **`fsgg:delivery-completes/v1` on the most recent merge** ([ruling](https://github.com/FS-GG/.github/pull/5150#issuecomment-6552391470)). The PR that completes the
    Delivery says so; a last merge that declares `fsgg:delivery-continues/v1`, both markers or neither is
    never closed by C1. This is how C1 still honours decision 9's "close when all PRs are merged": the
    completing PR states that it completes the Delivery.
  - **No reopen after a close.** An issue that was reopened after a `fsgg:tend-closed/v1` comment or a C1
    custody record is never closed again automatically; it becomes a `reopened-after-close` proposal.

  It never closes an Outcome, and never closes a Blocked card: Blocked records remaining acceptance that is
  not a PR. It closes with reason COMPLETED after a comment that follows dispatch rule 3: the merge commits
  of the merged PRs and their recorded independent verdicts, or "Unknown: no recorded independent verdict"
  where none is recorded, plus `<!-- fsgg:tend-closed/v1 -->`. The built-in "Item closed → Done" workflow,
  or S1 as its backstop, then writes Done.
- **C1 is observed before it is enabled.** For 7 days after the timer starts (§12 PR 8), C1 runs plan-only:
  the plan's would-close list is reviewed against Root's manual sweep, which applies the same conditions
  meanwhile (decision 9). Only then may the enable file list C1.
- **Every routine PR declares its relation to its Delivery** ([ruling](https://github.com/FS-GG/.github/pull/5150#issuecomment-6552391470)). Its body carries exactly
  one of `<!-- fsgg:delivery-continues/v1 -->` (the Delivery stays In progress after the merge) and
  `<!-- fsgg:delivery-completes/v1 -->` (this merge completes the Delivery's PR work). In review is
  machine-written once S3 is on, and the opt-out marker alone was rarely used, so neither guard was
  reliable without an explicit completion statement. The merge gate checks the declaration (§12 PR 5):
  advisory for its first 7 days, then required. Root's manual sweep applies the same rule; for PRs merged
  before the rule existed, Root judges case by case, as in the 2026-11-01 sweep, and C1 never closes them.
- **The wrong-Delivery risk.** C1 trusts the PR body's link. A PR that names the wrong Delivery, strictly
  linked, would close that Delivery. The settle window, the In review requirement and the reopen guard
  bound the damage (a reopened issue is never closed again, and the closing comment names the PRs), but
  they do not detect the wrong link itself; the merge gate's R1 check is where the link is made right.
- **Blocked is never written or overwritten automatically, with one exemption: S1.** Blocked records
  Root's judgement that a named prerequisite is missing, which is also why the merge gate refuses a
  Blocked preimage. A closed card is different: closing is Root's acceptance act and supersedes that
  judgement, and the built-in workflow already moves a closed Blocked card to Done. S1 therefore moves a
  closed Blocked card to Done; no other rule moves a card off Blocked.
- **S3 and S3b use only strict linkage.** An automatic move happens only for a PR whose `linkage_of()`
  verdict has no failures and names exactly this Delivery, as the merge gate and dispatch rule 1 require.
  Anything looser is P-LINK.
- **S3 and the merge gate agree.** After a merge the gate writes In review, or In progress with
  `fsgg:delivery-continues/v1`. With no open PR left, S4 does not fire while `MPR` is not empty, and
  S3 does not fire without an open PR, so the reconciler never undoes the gate's write.
- **A merge can race S3, S3b and S4.** The decision rests on the linked PR's state, so the adjacent
  re-read before the send (§6) includes each deciding PR's `state`, `isDraft` and `mergedAt`. If any of
  them differs from the snapshot, the effect is refused for this run and derived again next run.
- **S4 heals on the next run.** A PR opened after the timeline read and before the send is not among
  the deciding facts, so S4 can move a card to In progress just as review starts. The next run sees the
  open linked PR and S3 moves the card back to In review.
- **S4 is narrower than #5136's wording.** "In review with no open PR" includes Deliveries whose
  implementation merged and which wait for acceptance; the status lifecycle defines In review as exactly
  that. Those go to P-ACCEPT, not back to In progress. S4 fires only when no open, draft or merged PR is
  linked, strictly or loosely, which is exactly the table's condition. A closed unmerged PR does not count:
  an abandoned PR leaves nothing in review. Root accepted this (D4).
- **S1 makes the reconciler a second Done writer.** It writes Done only where the built-in workflow did
  not: a card added after its issue closed, or closed before the workflow was on (cohort-90 found
  5 such cards). Both writers set the same value, so a race ends in the same state. This amends Root
  decision 2 ("the only writer of Done"); Root approved the amendment (D2). Phase 2 updates the
  board-currency design's §2 and dispatch rule 3 to name the backstop.
- **S9 writes only Backlog, or Ready under S9r.** Backlog claims the least ("no concrete next action is
  admitted"). Ready asserts an admitted next action, so S9r needs the intended-fields line to be
  Root-authored, verified the way board-currency verifies Root markers: the issue's `author.login` is in
  `rootLogins` and, if the body was ever edited, so is `editor.login`. Every FS-GG agent authenticates as
  that account, so S9r means "authored by an FS-GG agent account", not by Root's hands; Root accepted
  that meaning. Every other declared value is a proposal (P-INTENT). If an open
  linked PR exists, the fixpoint then applies S3 or S3b to the filled Backlog, and the two collapse into
  one effect.

### Status of an Outcome: the roll-up

The roll-up needs the complete native sub-issue list, and each open sub-issue must be a Project 3 card
with a Status. Otherwise the Outcome is Unknown (`children-incomplete`), as the lifecycle contract
requires ("incomplete parent child coverage withholds parent rollup"). An open child whose Status is
Done contradicts its native state: it is S2 `done-open` on the child, and the Outcome's roll-up is
withheld (`child-done-open`) until it is resolved. With `C` the Statuses of the remaining open children,
evaluated top to bottom:

| `C` | Desired | Also |
|---|---|---|
| every child In review | In progress | Propose `all-children-in-review`: whether the whole Outcome is in review is Root's call |
| contains In progress or In review | In progress | |
| else contains Ready | Ready | |
| else contains Backlog | Backlog | |
| else all Blocked | Blocked (never applied) | Propose Blocked |
| empty, at least one closed child (R5) | none | Propose `children-complete` (close, or file the remaining Deliveries) |
| no children at all (R6) | none | Propose `no-open-delivery` |

Rule O1 applies the roll-up only between Backlog, Ready and In progress: the preimage and the target
must both be one of those three. An **empty** Outcome Status counts as a writable preimage: O1 fills the
desired value when it is Backlog, Ready or In progress, and otherwise fills Backlog, the least-claiming
value, alongside the proposal from the table. That Backlog fill is a fallback (§3, fixpoint) and waits for
the children to settle. An Unknown roll-up (`children-incomplete` or `child-done-open`) wins over it:
nothing is filled. A Blocked preimage is left alone and the derived value is
proposed. In review and Done are never written to an Outcome automatically. The status lifecycle
already says that "active child work can make an Outcome In progress" and that "one child PR cannot make
the whole Outcome In review". Root approved amending the lifecycle contract's "never automatically
climb parents" to allow this bounded roll-up (D3); the amendment lands in phase 2 before O1 is enabled.
A closed Outcome follows S1.

### Enrollment state

An unenrolled card is an enrollment candidate when it is an issue (not a PR or draft) in one of the
binding's repositories, is open, has a derivable Work level, and has an RM1 Roadmap. Native open
sub-issues of enrolled Outcomes that have no visible Project 3 card are candidates too, because auto-add
is off. `ProjectV2.items` omits archived items, so before E1 a candidate is confirmed through
`Issue.projectItems(includeArchived: true)`. If that shows an archived Project 3 item, the candidate is
refused (`archived-member`): whether `addProjectV2ItemById` returns, restores or duplicates an archived
item is not established, so it is Unknown, and the reconciler never sends the add. Closed cards are not
candidates: binding rows are append-only and the cap is 500, so a
row is spent only on work that can still be scheduled (D7, accepted). Other cases:

- a repository outside the binding (FsQuint#50, SC2.Client#49): a refusal, `outside-binding`, reported
  once and kept as a known exception;
- a plan that does not pass, or a parent with no Roadmap: a proposal naming the plan owner;
- a PR or draft card: a refusal (R7 `draft`).

§7 describes the path.

### Archived state

The desired archived state follows #5119: when more than `doneVisible` cards are Done, every Done and
closed card older than the newest 200 by `closedAt` should be archived. The four existing archive guards
are kept: terminal only, never a live row's blocker, never an unreadable row, and the explicit bound.
An archived enrolled row stays enrolled and reads as `Archived` (#5132). Until the Python archiver is
retired (§9) it remains the only archive writer, and the reconciler only plans archives for parity.

## 4. The typed plan

```fsharp
type Field = Status | WorkLevel | Track | Roadmap
type Apply =
    | SetField of item: ItemId * field: Field * preimage: string option * target: string * rules: RuleId list
    | AddMembership of issueNodeId: string * rule: RuleId
    | Archive of item: ItemId * rule: RuleId
    | CloseDelivery of issueNodeId: string * mergedPrs: string list * rule: RuleId   // C1 only (decision 9)
    | PrepareEnrollment of rows: EnrollmentRow list
type Effect = { Id: string; Apply: Apply; DependsOn: string list; Deciding: DecidingFacts }
type Proposal = { Id: string; Rule: RuleId; Subject: Subject; Suggested: string option; Evidence: string list }
type Refusal = { Id: string; Rule: RuleId; Subject: Subject; Reason: RefusalReason }  // includes every Unknown
type Plan =
    { Ruleset: string; ObservedAt: DateTimeOffset; InputsDigest: string
      Apply: Apply list; Propose: Proposal list; Refuse: Refusal list; Cost: ReadCost }
```

Every card lands in exactly one class for each field, and a test pins that. The plan file is
`fsgg.board-tend-plan/1`, written with the existing exclusive 0600 custody.

### (a) Apply: mechanically safe and reversible

| Rule | Effect | Why it is mechanical | Reversal |
|---|---|---|---|
| W1h, W1d, T1d, T1i, T1f, RM1 | Fill an empty Work level or Track (any card), or Roadmap (enrolled cards only, D6) | Fills only an absent value from native hierarchy, an explicit body declaration or the enrolled row; a populated value is never touched (the migration route's existing absent-only rule) | `clearProjectV2ItemFieldValue` restores the empty preimage, which is recorded |
| S9, S9r | Fill an empty Status with Backlog, or Ready from a Root-authored intended-fields line | As above; Backlog is the least-claiming option, and Ready needs verified Root authorship | Clear the value |
| S1 | Closed → Done | Native closure is Root's acceptance act; the built-in workflow already makes the same write | Set the recorded preimage option |
| S3, S3b | → In review or In progress when a strictly linked open PR exists | Decided from native PR state and a `linkage_of()` verdict with no failures naming exactly this Delivery, which is what the merge gate requires; the PR facts are re-read before the send; never from Blocked or Done | Set the recorded preimage |
| S4 | In review → In progress when nothing is linked, strictly or loosely | Decided from a complete native timeline after a settle window; the PR facts are re-read before the send | Set the recorded preimage |
| O1 | Outcome roll-up among Backlog, Ready and In progress | A function of children's Statuses over a complete child list | Set the recorded preimage |
| E1 | Add membership for an enrollment candidate, with Status, Work level and Track filled in the same run (D12) | Every input is native, including the archived-item check; the card is never left bare (§7) | `deleteProjectV2Item`, only for an item that the reconciler's own E1 custody record proves it created, and only while its values are still the reconciler's |
| E2 | Prepare the enrollment successor and open its carrier PR | Offline; the carrier's title, body and commit messages must pass `check-closing-keywords` before the push; the merge is Root's | Close the PR |
| A1 | Archive Done beyond 200 (after the Python archiver retires) | #5119's guards | `unarchiveProjectV2Item` |
| C1 | Close an In review Delivery (populated Work level) whose strictly linked PRs are all merged, none open, the last merge at least 48 h old and carrying `fsgg:delivery-completes/v1`, and never reopened after a tend close (decision 9) | Decided from native PR state and strict linkage; never an Outcome or a Blocked card; the complete open-PR set is re-read before the send | `reopenIssue` on an issue the reconciler's own custody proves it closed; the comment stays as the record, and the reopen guard then keeps C1 off that issue |

Each Apply effect also has a preimage condition: if the adjacent re-read differs from the snapshot, in
the item's fields or in its deciding PR facts, the effect is refused for this run and derived again next
run.

### (b) Propose to Root

S2 `done-open`, P-ACCEPT, S5 `stale-active`, P-LINK, P-INTENT, P-READY, P-UNBLOCK, P-NOBLOCKER, Outcome
Blocked or all-children-in-review roll-ups, `child-done-open`, `children-complete`, `no-open-delivery`,
`work-level-undetermined`, Roadmap and plan gaps, board-currency R2 `outcome-unaccepted`, R8
`pr-open-on-closed` and R9 hierarchy findings. These ask for a judgement: whether the next action is
admitted, whether acceptance is met, or which plan owns the work.

### (c) Refuse or Unknown

A truncated or unreadable input; null content; a draft or PR card; a field option missing from the
schema; conflicting declarations; a repository outside the binding; an archived member (`archived-member`);
an uncertain custody record (ours or the merge gate's) for the item; a fixpoint that does not settle; a
held writer lock; an exhausted budget or a dependency on a deferred effect (both reported as `deferred`,
not Unknown). A refusal is never a pass ([#266](https://github.com/FS-GG/.github/issues/266)) and never
silently dropped: it is in the plan file, and a refusal that persists for 24 hours becomes a proposal.

## 5. The reconciler closes only Delivery issues, under C1

Closing an Outcome, accepting an Outcome, and every other issue effect stay Root's. The one exception is
C1: the owner-authorized decision 9 ([#4730 comment](https://github.com/FS-GG/.github/issues/4730#issuecomment-6551301543)) makes the reconciler close a Delivery
whose strictly linked PRs are all merged and none open (§3). P-ACCEPT and `children-complete` stay
proposals. This is enforced by construction, not by care:

- **A closed change type.** The reconciler has its own discriminated union, `TendChange`, with exactly
  the cases `SetFieldValue`, `ClearFieldValue` (rollback only), `AddItem`, `RemoveCreatedItem` (E1
  rollback only), `ArchiveItem` and `CloseDelivery`. It is not a subtype of `V2Migration.Change` that a
  later case could widen. Each case maps to fixed GraphQL documents whose only root field is
  `updateProjectV2ItemFieldValue`, `clearProjectV2ItemFieldValue`, `addProjectV2ItemById`,
  `deleteProjectV2Item` or `archiveProjectV2Item`, or, for `CloseDelivery` alone, `addComment` followed by
  `closeIssue` with `stateReason: COMPLETED` on the same issue.
- **`CloseDelivery` is constructible only for a Delivery.** Its constructor takes the C1 effect, which
  carries the card's populated Work level (Delivery), its Status (In review) and its issue node id; there
  is no way to build it for an Outcome, a Blocked card, a structural-only Delivery or an issue that is not
  on the board. Immediately before the comment and the close, the writer re-reads the issue, its Work
  level, its Status and its **complete** open-PR set: the full cross-reference timeline, paged until its
  node count equals its `totalCount`, plus the issue's own comments for a `fsgg:tend-closed/v1` marker. A
  truncated or changed read, any open or draft linked PR, or a reopen marker refuses the effect.
  Custody records the comment and the close as one effect,
  keyed by the issue node id; an uncertain send is read back (issue state and the `fsgg:tend-closed/v1`
  comment), never resent.
- **A transport-level allowlist.** The reconciler's transport parses every outgoing mutation document and
  refuses, before dispatch, any document with a root field outside the five project mutations, plus
  `addComment` and `closeIssue` only when the document's subject is the issue node id of a `CloseDelivery`
  effect in the current plan whose card is a Delivery. Any other issue mutation (`updateIssue`,
  `reopenIssue` outside a recorded C1 rollback, `addSubIssue`, `closeIssue` on any other issue) cannot leave
  the process even if a builder were wrong.
- **Tests that can fail.** (1) The tend transport is handed hand-written documents for `closeIssue` on an
  Outcome, on a Blocked Delivery and on an issue with no C1 effect, and for `updateIssue` and `addSubIssue`,
  alone and aliased beside an allowed field; each must be refused with zero recorded sends. (2) Every
  `TendChange` case is rendered and the set of root fields it produces must equal the allowlist exactly.
  (3) Over the fixtures and generated boards, every closing effect is C1 on a Delivery that is not Blocked,
  and no plan closes an Outcome. Mutation legs (§10) that admit `closeIssue` for an Outcome, or drop the
  Blocked exclusion, must turn these tests red.
- **Carrier PRs.** E2 runs `check-closing-keywords` on the carrier's title, body and commit messages
  before the push and refuses on any closing reference, because this repository squash-merges with the
  commit messages.
- **Three allowlisted routes, and only three.** (1) The GraphQL transport with the five project
  mutations above and, for `CloseDelivery` alone, `addComment` and `closeIssue` on that Delivery.
  (2) The proposals comment on #4985 (§6), through a REST route whose allowlist is exactly
  `POST /repos/FS-GG/.github/issues/4985/comments`. (3) E2's carrier: a push of one fresh
  `routine/board-enroll-<cohort>` branch of FS-GG/.github whose only change is the manifest commitment,
  then `POST /repos/FS-GG/.github/pulls` for that branch, after the keyword check above. No other REST or
  git write is admitted.

## 6. The write path

### Reuse and extension

`migration-apply` already holds the engine's write discipline: an exclusive invocation marker, numbered
`-precondition`, `-intent`, `-intent-metadata`, `-response` and `-receipt` files written exclusive,
0600, write-through and fsynced, one `Dispatch` per effect, a `clientMutationId` the response must echo,
visibility-backoff readback, and no resend (`RetryMutation` is refused). What it lacks is an item-scoped
precondition: it re-reads the whole scope before every effect, which is why cohort-90 managed only about
19 effects per 270 seconds (#5123).

The reconciler extends it rather than adding a second writer:

1. The reconciler's closed `TendChange` type and allowlisted transport (§5) sit on the shared custody
   and dispatch machinery; `migration-apply` keeps its own `Change` type.
2. An item-scoped precondition, generalising the guard Root accepted for the merge gate (#5106): the
   compared state is the item's complete reported field-value set, its item and project identities, the
   field's schema and options, the issue's determining facts (state, parent), and for S3, S3b and S4 the
   deciding PRs' `state`, `isDraft` and `mergedAt`. The readback must equal the preimage with only the
   selected fields moved.
3. **An immediate re-read before every send.** Each item's field effects go in one mutation document
   (aliased `updateProjectV2ItemFieldValue` fields, run in order). The request sent immediately before
   that mutation is a re-read of that item (and its deciding PRs), and every field in the mutation is
   compared against it. No other request is interleaved and there is no group window; the custody record
   is written between the two, which is a local write, not a request. The readback follows the send.
4. **The remaining window.** GitHub's project field mutations take no expected value, so there is no
   compare-and-swap. Between the re-read's response and the mutation taking effect, about one round
   trip, a writer that does not take the lock (a person in the UI, the built-in Done workflow, a hosted
   job) can change the field, and the reconciler's send then overwrites it. If the other write lands
   after the reconciler's and before the readback, the readback shows it and the result is `uncertain`;
   if it lands before the reconciler's, the readback cannot tell. The window is accepted because the
   lock removes every cooperative writer, the Done workflow writes the same value S1 writes, and the
   preimage is recorded so any overwrite can be reversed. A test pins the ordering: in the recorded call
   log, the request immediately before each mutation is the re-read of the same item.
5. The same item-scoped precondition is offered to `migration-apply` for #5123, so the enrollment
   initialization and the reconciler share one write discipline (D8, accepted; Root coordinates with the
   #5119 owner); it lands in §12 PR 4.

### Per effect

1. The preimage is the snapshot value; an equal value is a no-op.
2. Immediate re-read of the item and its deciding PRs; any difference refuses the effect this run.
3. Durable possible-send custody: one record per send in `$XDG_STATE_HOME/fsgg/board-tend/<run>/`,
   carrying the fields, preimages and targets. A field send is keyed by item id. An E1 add is keyed by
   the issue's node id, because the item id does not exist until GitHub returns it; the returned item id
   is then written into the record, which is the only evidence `RemoveCreatedItem` accepts. If the record
   cannot be written, nothing is sent (`refused-custody`).
4. One send.
5. Independent readback. `written` only when the readback equals the preimage with only the selected
   fields moved; the record is rewritten to `verified`.
6. A failed, malformed or uncertain send or readback is `uncertain`. Never resend. The record stays,
   and later runs refuse that item (`uncertain-custody`) and only read back: a readback equal to the
   target rewrites the record to `verified`; anything else leaves it for Root, who reads the item and
   retires the record. This is the merge gate's crash-recovery rule.

### Write budget

At most `maxItemsPerRun` items (default 100) and `3 × items` sends (re-read, mutation, readback), with
mutations at most one per second. GitHub documents a secondary limit of about 80 content-generating
requests a minute and 500 an hour; whether field updates count is not established, so the pacing assumes
they do. Steady state is a handful of items per run. The cohort-90 initialization (114 cards, 423
effects) becomes 114 mutation sends and 228 reads across two runs, a few minutes in all, instead of 63
migration invocations over about 3 hours.

### Enrolled and unenrolled cards

- **Enrolled** (rows in the private binding behind the public commitment): every Apply rule. A Roadmap
  must equal the enrolled row; a differing populated Roadmap is a refusal (`ConflictingHumanValue`, as
  in the planner).
- **Unenrolled cards** (candidates and non-candidates alike): absent-value fills of Status, Work level
  and Track (S9, W1, T1) through the same item-scoped guard, so the board is complete (D6, overriding the
  proposed "S1 and A1 only"; it matches Root's manual fills recorded on #4941). Root
  [widened D6](https://github.com/FS-GG/.github/pull/5139#issuecomment-6548590116) to the mechanically
  safe Status moves as well: S3 (open linked PR → In review),
  S3b (only a draft PR linked → In progress), S4 (In review with nothing linked → In progress) and the O1
  roll-up within Backlog, Ready and In progress, all through the same guarded writer. S1 and A1 also
  apply, and P-READY proposes Ready for an unenrolled Backlog card whose `## Blocked on` says "Nothing".
  Roadmap and the enrollment rows stay enrolled-only. For a candidate, E1 adds the
  card and makes the fills in the same run, so a bare card never stays on the board; Roadmap follows
  enrollment (§7).

### Interaction with the merge gate and the Done workflow

- The reconciler reads the merge gate's custody directory and refuses any item with a `possible-send`
  record (`gate-custody-uncertain`). An item with a `deferred` record is touched only by replay (`GR`);
  while `GR` is not enabled, the reconciler's other rules refuse it (`gate-deferred-pending`) and leave
  it to the gate's own replay.
- Both take the same writer lock (§8), so a post-merge write and a reconciler write never interleave.
  The merge gate waits for the lock for at most 60 seconds. If it still cannot take it, it does not send:
  it writes a **deferred-transition** custody record (repository, PR, the PR's `mergedAt`, Delivery,
  item, preimage and target) and reports `boardTransition: deferred` ([Root's ruling A](https://github.com/FS-GG/.github/pull/5139#issuecomment-6548815933)):
  - **Who replays.** From PR 1 the gate itself applies pending deferred records, under the lock, at the
    start of its next invocation, through its existing guarded path. Once enabled, the reconciler replays
    them too, under its own rule id `GR` in the enable file.
  - **Order** ([round-3 ruling 2](https://github.com/FS-GG/.github/pull/5139#issuecomment-6548872887)). Records replay in merge order (the PR's `mergedAt`). For one
    Delivery, the newest record supersedes the older ones: they are rewritten to `superseded` and never
    sent.
  - **Closed issues.** A record whose Delivery is now closed is not applicable: it is rewritten to
    `not-applicable` and sends nothing, as the gate already does for a closed Delivery; Done stays with
    the built-in workflow and S1.
  - **Exact preimage.** Replay sends only when the item's current Status exactly equals the recorded
    preimage, then follows the usual guards (immediate re-read, one send, readback). Anything else
    durably rewrites the record to `refused` and raises a proposal; nothing is sent.
  - **Supersession, crash-safe** ([round-4 ruling 1](https://github.com/FS-GG/.github/pull/5139#issuecomment-6548916283)). Every record stores its PR's `mergedAt`.
    Supersession is decided per Delivery over **all** of that Delivery's records, whatever their state,
    ordered by `mergedAt`; a record written before PR 1, which has no `mergedAt`, is ordered oldest. A
    `deferred` record older than some other record for that Delivery never sends. Before the newest
    record moves to `possible-send`, every older `deferred` record is durably rewritten to `superseded`
    (written, fsynced, directory fsynced); when a newer record is already in a non-`deferred` state, the
    older `deferred` records are rewritten to `superseded` the same way. A crash at any point therefore
    leaves at most the newest record able to send.
  - **State machine.** `deferred → possible-send → verified`, with `deferred → superseded`,
    `deferred → refused` and `deferred → not-applicable` as terminal exits. `deferred`, `superseded`,
    `refused` and `not-applicable` are added to the gate's custody validator, and the gate never sends for
    a PR whose record is `deferred` except through replay. Replay durably rewrites the record to
    `possible-send` before its one send and to `verified` after a readback proves the target.
  - **Readback sweep.** Each gate invocation starts with a read-only sweep of every `possible-send`
    record, before replay and before its own work: each is read back and rewritten to `verified` when the
    target is proven, and otherwise left as it is. The sweep never sends.
  - **Under the lock.** Both writers create and rewrite these record files only while holding the
    writer lock, so the gate and the reconciler never replay the same record twice.
- S1 waits `doneSettleMinutes` after `closedAt`, so in the normal case the built-in workflow has already
  written Done and S1 is a no-op. If the workflow is read as disabled, S1 still applies: lifecycle
  rule 3 then makes the closer write Done through the guarded helper, and the reconciler is that helper.
- The gate writes no Done and no Outcome Status; the reconciler writes no PR-time verdict. Neither undoes
  the other (§3, S3 and S4).

### Proposals to #4985

The drift observer stays the only writer of the #4985 body. The reconciler adds append-only comments:

- **Stable IDs.** `P:<rule>:<subject>:<reason>`, with the subject a public reference or the observer's
  `private:<12 hex HMAC>` placeholder, using `BOARD_DRIFT_HMAC_KEY` held on fdev, mode 0600 in Root
  custody (D9). The fdev copy must be the same bytes as the repository secret the observer uses, so one
  card has one placeholder in both writers; the two copies are only ever rotated together.
- **Privacy.** For a private or unknown-visibility subject, the `Suggested` and `Evidence` fields pass
  through the same gate as the subject: they name only Status values, rule IDs and other placeholders,
  never a title, body text, PR reference or repository name.
- **Once per ID.** A ledger in `$XDG_STATE_HOME/fsgg/board-tend/proposals.json` maps each ID to the
  comment that first carried it and its state (live or cleared). A run posts one comment with only the
  IDs never posted before, carrying `<!-- fsgg:board-tend-proposals/v1 run=<id> -->`. An ID that clears
  is marked cleared with no comment. If it fires again later, the ledger marks it live again and the plan
  file and run summary show it, but it is not reposted: a flapping condition must not notify on every
  flap (board-currency lesson 6).
- **Custody.** The run id is recorded before the post. After the post, the comment is read back by the
  id GitHub returned. An uncertain post is never repeated: the next run finds the comment by its run
  marker among comments created after the custody time, and verifies it by id. It never selects a
  comment by recency, and it never edits any comment.
- **Notifications** follow the observer's rule: only new IDs cause a comment.

## 7. The auto-enroll path

The goal is that enrolling new cards needs no tool invocations by hand and one Root action per batch.
This is the lifecycle contract's dispatch rule 4 (each clearing pass enrolls new Deliveries in batches
through the #4941 planner), made automatic.

| Stage | Today (cohort-90) | Reconciler |
|---|---|---|
| Find candidates | Root lists them | From the snapshot (§3), no extra read |
| Make the card | Auto-add (now off) | E1: `AddItem` (after the archived-member check) with Status, Work level and Track filled in the same run |
| Roadmap | Root ruling | RM1, codified; computed for the request, written after `bind-runtime` |
| Status targets | Root decided per card | The derivation in §3; closed cards are not enrolled |
| `resolve-issues`, `route-preflight`, `plan`, `prepare`, `render-carrier` | Run by hand | Run in-process by the reconciler with the same inputs, once per batch |
| `ConstructorIdentity` | `Unsupported` whenever `main` moves; Root asserted `--main-revision` | **Prerequisite #5113** (content-based identity). Until then E2 is refused with `constructor-identity`; asserting the revision automatically would defeat the check |
| `ProjectAutoAdd` | `Unknown` on exactly the cohort's own bare cards | Prerequisite (small): the preflight accepts the request's rows as expected memberships, and E1 leaves no bare card |
| Carrier PR | Opened by a worker | E2 opens one PR (only the manifest commitment changes); at most one open at a time, and new candidates wait for the next |
| Merge | Root | Root, through the routine merge (D10). Automatic merge of carrier-only PRs is revisited after 3 clean unattended runs |
| `bind-runtime` | By hand | After the merge, by the next run |
| Reader identity after an upgrade | Hand rebind | **Prerequisite #5120** (a designed rebind stage checked against the executing assembly) |
| Initialization | 30 applies at 4 cards per scope | §6 writer: 3 sends per item and no whole-scope re-read, which delivers #5123 |
| Population read | 207-row inspect accepted as Unknown (#5121) | Fixed in source only by #5127 (175 fixture sends at 500 rows); installed from the PR 7 release. Membership of every enrolled node is checked with the #5132 `projectItems` cross-check, so an archived enrolled row is explicit rather than a gap |

**Capacity is a prerequisite too.** Binding rows are append-only (the successor is a raw-byte splice that
preserves the predecessor), archived enrolled rows are retained (#5132), and the cap is 500. With 207 rows
and new Deliveries filed every day, the cap will be reached. Row retirement, for example a successor that
drops closed and archived rows from the manifest under a new commitment, gets its own design, and that
design is a prerequisite for unattended auto-enroll (D7). It is a step in the phase 2 sequence (§12). Until
it is accepted and delivered, E2 refuses above 450 rows and files a proposal.

## 8. Scheduling, locking and enablement

- **Where it runs: fdev.** The private binding and Root's credential (the `gh` PAT the merge gate already
  writes with) are there, and GitHub drops scheduled workflow runs, which is why the drift observer is
  already dispatched from fdev. A hosted workflow would need a new board writer identity; the
  board-currency design keeps the App read-only. A hosted plan-only job can be added later for
  visibility.
- **Timer.** A user unit `fsgg-board-tend.timer`, hourly at minute 07, away from the observer's :23
  cron, its :53 fdev dispatch and the 04:43 archive. `Persistent=true`, with a 120-second randomised delay.
- **After merge.** After a successful merge and the gate's transition, `routine-delivery.py` runs
  `systemctl --user start --no-block fsgg-board-tend.service`. The call is gated exactly like
  `--board-gate enforce`: only a copy running in a checkout whose `origin` is FS-GG/.github makes it, so
  the receiver copy distributed by the drivers package never does. It is best effort: a failure to start
  never affects the merge.
- **Post-merge runs are never swallowed.** The trigger first writes a `pending-post-merge` marker in the
  reconciler's state directory, then starts the unit. The 5-minute minimum interval applies only to timer
  runs; a run that finds the marker always runs. If the unit is already active, systemd does not queue a
  second start, so a running reconciler checks the marker when it finishes and, if the marker is newer
  than its own start, runs once more. The marker is cleared only by a run that started after it was
  written.
- **Lock.** Today `migration-apply` locks `Path.GetTempPath()/fsgg-v2-migration-PVT_kwDOEYAWY84Bldpa.lock`,
  and `Path.GetTempPath()` follows `TMPDIR`, so two writers with different `TMPDIR` values do not exclude
  each other. The refresh path's `fsgg-v2-observation-<project>.lock` has the same defect, and engine
  tests that reach either lock contend with a live apply (#5126). Phase 2 PR 1 moves every Project 3
  writer to one fixed path, `$XDG_RUNTIME_DIR/fsgg/board-writer-PVT_kwDOEYAWY84Bldpa.lock`, and refuses
  to write if it is unset. The writers are `migration-apply`, `refresh`, the merge gate, Root's manual
  guarded route (the status-lifecycle procedure), the Python Done-retention archiver and the reconciler.
  The lock directory is injectable, tests inject a private one, and a test proves a test run never opens
  the production path, which delivers #5126. During the transition writers also take the legacy lock at
  the literal `/tmp` path.
- **Lock semantics.** Every side uses `flock(2)` with `LOCK_EX` on the same path: the F# writers call it
  explicitly rather than relying on .NET's `FileShare.None` emulation, which an environment variable can
  disable, and the Python writers use `fcntl.flock`. A cross-process test proves an F# holder excludes a
  Python taker and the reverse. The reconciler uses `LOCK_NB`: a held lock gives a plan-only run with
  `lock-held`, never a queued writer. The merge gate waits up to 60 seconds and then defers (§6).
- **A writer outside fdev cannot share the lock.** The Done-retention archiver currently runs as a hosted
  workflow, where no fdev lock exists. Until it runs from fdev under the lock, or A1 replaces it, the
  reconciler writes nothing to archive-eligible cards (Done and closed, beyond the newest 200), so the two
  are disjoint by construction. Root accepted this mitigation.
- **Dry run by default.** `--apply` is ignored unless
  `$XDG_CONFIG_HOME/fsgg/board-tend.json` exists, is owned by the user with mode 0600, and lists the
  enabled rules. The recommended unattended starting set is
  `{"apply": ["W1h","T1f"], "propose": true, "maxItemsPerRun": 100}`, which needs no contract amendment
  ([round-6 ruling](https://github.com/FS-GG/.github/pull/5139#issuecomment-6548973997)). Rules are enabled one at a time. Deleting the file, or
  `"apply": []`, is the kill switch. A run without the file still writes its plan, so the rollout can
  compare plans before any write.
- **T1i joins only after a comparison.** T1i is a heuristic. It enters the unattended set only after a
  7-day plan-only comparison shows its would-be Track disagrees with at most 5% of the Tracks people have
  set on existing Delivery-Outcome pairs. The plan reports that disagreement rate on every run, so the
  comparison is a count from the reconciler's own output, not a separate study.
- **Contract-gated rules.** S1, S3, S3b, S4, S9, S9r, O1, E1, C1 and the declaration branches W1d and T1d
  change what the lifecycle contract says (D2, D3, the intended-fields grammar, and automatic Status moves
  and membership). The hierarchy and default branches W1h, T1i and T1f, and gate replay `GR`, are not
  gated ([Root's ruling C](https://github.com/FS-GG/.github/pull/5139#issuecomment-6548815933)). The enable file may
  list them only once the contract-amendment PR (§12) is on `main`: the reconciler reads the amendment's
  marker `fsgg:board-tend-contract/v1` from `lifecycle-contract.md` at the installed revision and refuses
  to enable a gated rule without it.

## 9. Migration from the Python observers

This follows #5094's retirement plan: observe at least 7 days, Root rules on each pair separately, then one
PR per approved pair removes the old rule, its ack route and its tests. The reconciler adds a third column
to each pair. During observation it runs plan-only, and phase 2 publishes a per-run comparison of IDs.

| Python rule(s) | Reconciler rule(s) | Order | Notes |
|---|---|---|---|
| drift d, currency R7 | W1, T1, RM1, S9, S9r (apply); `work-level-undetermined`, P-INTENT (propose) | First | Drift d and R7 are identical; the reconciler fixes rather than reports |
| drift x, currency U | Refuse class | First | The reconciler's Unknown is at least as wide as U |
| drift b, currency R2 | S1 (apply), S2 (propose); R2 `outcome-unaccepted` (propose) | Second | b on PR cards: the board holds no PR cards today |
| drift a, currency R8 | S3, S3b (apply); P-LINK, `pr-open-on-closed` (propose) | Second | a covers any card; the reconciler covers issue cards, which is every card now |
| drift c, currency R4 | S4 (apply), S5 (propose), O1 (apply) | Third | 48 hours, with the Outcome legs |
| currency R3 | P-UNBLOCK, P-NOBLOCKER | Third | |
| currency R5, R6 | `children-complete`, `no-open-delivery` | Third | |
| currency R9 | Refuse plus proposal | Third | |
| Done-retention archiver (#5119) | A1 | Last | Ported with its four guards after parity; one archive writer at all times |
| currency R1, `board-linkage`, merge gate | — | Not migrated | PR-time attribution, not board state. Its one implementation stays shared by the three Python points; the reconciler is held to it by the conformance corpus (§2) |
| drift e, drift s | — | Not migrated | Not duplicates (#5094) |

`board-currency-fetch.py` retires when its last consumer rule does. `board-drift-fetch.py` stays while
e and s stay.

## 10. Test strategy

- **Pure fixtures.** Each case in §11 is a snapshot JSON under `tests/coord-board-v2-projection/`
  (the `InternalsVisibleTo` target), with `observedAt` as the only clock. No test reads the wall clock;
  the writer takes a `TimeProvider`, as `runMigrationWithCustody` already does with its `ManualTime`
  tests.
- **Properties.**
  - Determinism: permuting items, PRs and children gives the same plan.
  - Idempotence: over the enabled rules, repeated application settles within k applications, k being the
    longest fallback chain; the k+1st derivation yields no Apply effect, and only `deferred:
    input-pending` entries produce effects after the first (§3). Generated hierarchies with every enabled
    rule subset check the bound.
  - Liveness: with no new native change, after max(k, 3) runs or 24 hours of injected clock time,
    whichever is first, every field of every card is filled, refused or proposed; a `deferred:
    input-pending` entry becomes a proposal on its third run or after 24 hours; an expired entry whose
    inputs settle later is still applied by an enabled rule and its proposal clears.
  - Dependencies: effects are sent strictly in dependency order in both directions (parent fill before
    child inheritance, child move before roll-up), and every dependant of a refused effect is deferred,
    transitively.
  - Visibility and fallbacks: an effect is visible to later cards in the same round; a fallback (T1f, S9,
    O1's empty fill) never applies while an input it could inherit from is pending; a cycle collapses
    into one unit decided by declarations or proposed.
  - Declared Track: T1i and T1f never write a card whose intended-fields line declares a Track; while
    T1d is off the effect is deferred and expires into `track-declared-pending`.
  - T1i comparison: the plan's disagreement rate between T1i's would-be values and human-set Tracks on
    existing pairs is computed and reported.
  - Track on young leaves: T1i inherits from a Project 3 Outcome parent before the leaf's Work level is
    written; T1f never applies while the card's own Work level or its parent's Track is pending; a
    settle-window deferral's expiry counts from the end of the window.
  - Structure: W1h decides Work level in one pass without reading any other field, and a declaration that
    disagrees with it is refused. The leaf branch writes Delivery only with a Project 3 parent, an issue at
    least 24 hours old and no differing declaration; a differing declaration refuses it even while W1d is
    off; otherwise it defers and expires.
  - Safety: Apply never overwrites a populated Work level, Track or Roadmap, never writes Blocked, never
    moves off Blocked or Done, never sets Done on an open issue, and never writes In review or Done to an
    Outcome.
  - Partition: each card and field is in exactly one class.
- **Writer.** Through `Fake.Recorder`: the re-read differs in a field or in a deciding PR's state,
  draft flag or merge, custody cannot be written, the send errors, the send times out after dispatch, the
  readback is malformed or shows another field moved, and a crash between send and readback. Every case
  asserts the send count (0 or 1, never 2) and the custody state. One test asserts that the request
  immediately before each mutation in the call log is the re-read of the same item.
- **Never closing.** The transport refusal and root-field tests of §5.
- **Lock and replay.** The cross-process `flock` test (F# against Python, both directions); the merge
  gate's 60-second wait ending in a `deferred` record; the gate's next invocation replaying it; replay
  refusing (and proposing) when the Status differs from the recorded preimage; the validator accepting
  `deferred`, `superseded` and `not-applicable`; the gate never sending for a `deferred` PR outside
  replay; replay in merge order, with older records for the same Delivery superseded and never sent; a
  preimage mismatch ending in `refused` with a proposal; supersession decided over all of a Delivery's
  records by the stored `mergedAt`, with pre-PR-1 records ordered oldest; older `deferred` records
  durably `superseded` before the newest becomes `possible-send`, and also when a newer record is already
  non-`deferred`; with a crash injected between the two writes; a closed Delivery's record
  becoming `not-applicable`; `possible-send` written before the replay send; the
  read-only `possible-send` sweep at the start of each gate invocation sending nothing; and a reconciler
  replay under `GR`.
- **E1.** An archived member refuses the add; `RemoveCreatedItem` refuses an item without a matching
  E1 custody record keyed by the issue node id.
- **Proposals.** A private subject's `Suggested` and `Evidence` carry no title, body text, PR reference or
  repository; a cleared ID that fires again is not reposted; the placeholder for a fixture card equals
  the observer's for the same key.
- **Post-merge trigger.** A copy outside the FS-GG/.github checkout never calls `systemctl`; a
  post-merge marker written during a run, or within 5 minutes of the last run, still produces a run.
- **Budget.** Synthetic boards of 222 and 500 items assert read sends against the ceiling in §2, and the
  write send count for 25, 100 and 114 items.
- **Conformance.** The linkage corpus runs against `linkage_of()` and the F# implementation. During the
  migration, the overlapping currency rules are compared ID by ID on the same snapshots.
- **Mutation.** A spec file `tests/coord-board-tend-mutation/specs.yml` for the existing
  `scripts/gate-mutate.py` harness, with one leg per guard: each Apply condition, strict linkage, the
  PR-fact re-read, the Blocked and Done protections, the settle windows, absent-only filling, S9's
  Backlog and Root-authorship limits, the fixpoint, structural Work level, settled-input fallbacks,
  cycle collapse, the input-pending expiry, within-round visibility, send order and transitive deferral,
  the W1 conflict refusal, the gate replay's exact preimage, merge order and supersession, the roll-up's
  completeness check, the archived-member refusal, the custody write before send, no resend, the lock,
  the enable file and its contract gate, the transport allowlist, and C1's Delivery-only, not-Blocked,
  strict-linkage and continues-marker conditions. Every leg must be killed, and its
  anchor check runs on each PR that touches the reconciler, as `tests/coord-engine-mutation/run.sh`
  does.
- **Temporary paths.** Tests set a private `TMPDIR` and an injected lock directory. They never touch the
  production lock.

## 11. Acceptance fixtures

| # | Case (from #5136 and #5133) | Fixture asserts |
|---|---|---|
| 1 | No Status (11 cards) | S9 Backlog; S9r Ready only for a Root-authored line; any other declared value is P-INTENT; with a strictly linked open PR, one collapsed S9 + S3 effect |
| 2 | Missing Work level or Track (128) | W1 from the parent and from the declaration; T1; a W1 conflict is refused |
| 3 | Closed, not Done | S1 after the settle window; nothing inside it |
| 4 | In review, no open PR (5) | Nothing linked: S4. Merged PR with evidence: P-ACCEPT, Status unchanged |
| 5 | In progress, stale or finished (13 of 34) | Open PR: S3. Idle: S5. Merged with evidence: P-ACCEPT |
| 6 | Backlog with "Blocked on: Nothing" (25) | P-READY when there is no open native blocker, enrolled or not (the unenrolled proposal names enrollment); never applied |
| 7 | Outcome Status ignoring its children | O1 within Backlog, Ready, In progress; all children In review gives In progress plus a proposal; Blocked proposed; an empty Outcome Status filled; an open Done child withholds the roll-up; an incomplete child list is Unknown |
| 8a | Roadmap inheritance with an anchor (Coordination#1026's 8 children) | RM1 drops the anchor; protected-document grammar passes |
| 8b | Repository outside the binding (FsQuint#50) | `outside-binding`, reported once over several runs |
| 8c | No admissible plan (SDK-PATCH-01), parent with no Roadmap (#4811 under #5097) | A proposal naming the plan owner; no write |
| 8d | Closed card in the cohort (Unity.Client#28) | Done by S1; not an enrollment candidate |
| 8e | `ConstructorIdentity` after `main` moves (#5113) | E2 refused until the prerequisite lands |
| 8f | 114-card initialization (#5123) | At most 100 items in one run, 3 sends per item, no whole-scope re-read |
| 9 | Merge-gate interplay | After the gate's In review with a merged PR, the plan is empty for that item; a gate `possible-send` record refuses the item |
| 10 | Closes only Deliveries under C1 | The only closing effects in any plan are C1 on a Delivery that is not Blocked; no plan closes an Outcome; the transport refuses hand-written `closeIssue` on an Outcome, a Blocked card or an issue without a C1 effect |
| 11 | Loose linkage | A PR referencing two Deliveries, or a Delivery with no parent Outcome: P-LINK, no move |
| 12 | Merge race | The deciding PR merges between snapshot and re-read: S3 refused this run |
| 13 | Same-run dependency | A new Outcome and its child: the child's T1 follows the parent's; the plan is idempotent |
| 14 | Archived member | A candidate with an archived Project 3 item: `archived-member`, no add |
| 15 | Gate under contention | The gate's deferred-transition record is applied by the next run under the guards |
| 16 | Closed Blocked card | S1 moves it to Done; no other rule moves it |
| 17 | Abandoned PR | In review with only a closed unmerged PR: S4 applies |
| 18 | Search lag | An open PR missing from the search but present in the timeline: no move this run, a move next run once indexed; a PR in the search but closed in the timeline: no move |
| 19 | Deferred record replay | The gate's next invocation replays its `deferred` record when Status equals the preimage, and proposes when it does not |
| 20 | Fallback wait | A new Outcome whose Track comes from a declaration and a child with none, the child under 24 hours old (its W1h still deferring): with T1i enabled the child inherits the parent's Track at once; with T1i off it shows the would-be value and expires into `track-undetermined`; in neither case does it take `Follow-up` |
| 21 | W1 conflict | A declaration of Outcome on a leaf whose parent has sub-issues: structurally a Delivery, so refused as `work-level-conflict`, not written |
| 22 | Replay order | Two deferred records for one Delivery from two merges: only the newer is replayed; the older becomes `superseded` and is never sent; records for different Deliveries replay in merge order |
| 23 | Expired deferral | A fallback whose input stays pending becomes a proposal on its third run, or after 24 hours, whichever comes first; when the input later settles, the effect applies and the proposal clears |
| 24 | Young or unparented leaf | A leaf issue 2 hours old under a Project 3 Outcome: W1h defers, then writes Delivery after 24 hours; a leaf whose parent is off the board: deferred, then proposed; a leaf whose body declares Outcome while W1d is off: refused |
| 25 | Crash during replay | Two `deferred` records for one Delivery and a crash after the older is rewritten to `superseded`: the next invocation sends at most once, for the newer record only; a newer `verified` record keeps an older `deferred` one from ever sending |
| 26 | Declared Follow-up under an Active delivery parent | T1i does not write `Active delivery`; with T1d off the effect defers and then becomes `track-declared-pending`; with T1d on, T1d writes `Follow-up` |
| 27 | Declared Active delivery on a parentless card | T1f does not write `Follow-up`; with T1d off it defers and then becomes `track-declared-pending`; with T1d on, T1d writes `Active delivery` |
| 28 | All linked work merged (decision 9) | An In review Delivery (populated Work level) with two strictly linked merged PRs, the last 3 days old and carrying `fsgg:delivery-completes/v1`, and none open: C1 closes it with the dispatch-rule-3 comment; no close for a last merge under 48 h old, a last merge carrying `fsgg:delivery-continues/v1`, both markers or neither, Status In progress, a structural-only Delivery, a Blocked Delivery, an Outcome, an open or draft PR, an open loosely linked PR, or an incomplete timeline; a Delivery reopened after a `fsgg:tend-closed/v1` comment: `reopened-after-close` proposal, no close |

## 12. Phase 2 PR sequence

One open PR per chain, each `Refs #5136`, each reviewed independently:

1. The fixed writer lock path with `flock` semantics for every Project 3 writer, including the merge
   gate's bounded wait, its `deferred` records, the state machine in its validator, the read-only
   `possible-send` sweep and its own replay of deferred records (in merge order, newest per Delivery) at
   the start of its next invocation; an
   injectable lock directory and the test that no test opens the production path (§8). This PR owns the
   gate's lock and delivers #5126.
2. Pure derivation to a fixpoint, plan types, fixtures (§11), properties and the mutation spec. No IO.
3. Batched reads, `board-v2 tend` plan-only, the cost counters and a live read-only measurement at the
   current board size. The C1 derivation (decision 9) lands here, so plan-only already shows which
   Deliveries would close; its would-close list is what the 7-day C1 observation reviews.
4. The writer: `TendChange` (including `CloseDelivery`), the transport allowlist, the immediate re-read,
   custody (E1 and C1 keyed by issue node id) and the reconciler's replay of the gate's deferred records
   (`GR`). This also delivers #5123 (D8).
5. Proposals to #4985 with the ledger, and the merge-gate declaration check ([ruling](https://github.com/FS-GG/.github/pull/5150#issuecomment-6552391470)): every routine
   PR carries exactly one of `fsgg:delivery-continues/v1` and `fsgg:delivery-completes/v1`. The check is
   advisory for its first 7 days and then required. A PR declaring `fsgg:delivery-completes/v1` whose
   Delivery's `## Done when` names a release, install or readback step also gets an advisory warning,
   because C1 would close that Delivery after the merge.
6. The contract amendment, which gates S1, S3, S3b, S4, S9, S9r, O1, E1, C1, W1d and T1d: Delivery closure by
   C1 (decision 9) in dispatch rule 3, with the lifecycle contract's "a merged source PR cannot establish
   installed acceptance" line amended to say how C1 relates to it, the Done backstop (D2) in
   dispatch rule 3 and the board-currency design, the bounded Outcome roll-up (D3), the automatic Status
   moves S3, S3b and S4, the intended-fields grammar and S9's limits, and E1's attended membership add,
   in the lifecycle contract and status lifecycle, carrying the marker `fsgg:board-tend-contract/v1`.
   It also amends the routine-PR contract (the PR-body markers of the board-currency gate design and the
   routine delivery skill): every routine PR declares exactly one of `fsgg:delivery-continues/v1` and
   `fsgg:delivery-completes/v1`, and C1 closes only after a last merge that declares completion.
7. Release, install and qualify the coordination engine (0.114.1 or later, containing PRs 1-6) on fdev,
   through the normal coherent release route, with installed readback.
8. The fdev units, the post-merge trigger and the enable file; 7 days plan-only, then Root enables rules
   one at a time, gated rules only after PR 6. C1 is enabled only after its own 7-day would-close
   observation and after the merge gate's declaration check (PR 5) has become required.
9. The row-retirement design (D7), a docs PR reviewed like this one.
10. Auto-enroll (§7), after #5113, #5120 and the delivered row retirement.
11. Retirement PRs, one per pair approved under §9.

#5136 Done-when 2 is met after PR 4, Done-when 3 after PR 8 and the first retirement, and Done-when 4 is
measured in PR 3 and again on the first enabled week.

## 13. Root's recorded decisions

From [Root's decisions](https://github.com/FS-GG/.github/pull/5137#issuecomment-6548516184), 2026-10-31:

| ID | Decision | Root's ruling |
|---|---|---|
| D1 | Command name | `board-v2 tend`, avoiding the V1 `reconcile` collision |
| D2 | The reconciler is a backstop Done writer for a closed card 15 minutes after closure, if the workflow has not written Done | Yes; decision 2 is amended |
| D3 | Bounded Outcome roll-up O1 | Yes, limited to Backlog, Ready and In progress; never Blocked, In review or Done automatically |
| D4 | In review with a merged PR stays In review and becomes P-ACCEPT | Yes |
| D5 | Track defaults to `Follow-up` when nothing declares one | Yes |
| D6 | Field fills on unenrolled cards | **Overridden:** Status, Work level and Track absent-value fills are allowed on unenrolled cards through the same guarded item-scoped writer; Roadmap and manifest fields stay enrolled-only. **Widened** by Root later the same day ([comment](https://github.com/FS-GG/.github/pull/5139#issuecomment-6548590116)): the Status moves S3, S3b, S4 and O1 (within Backlog, Ready and In progress) are also allowed on unenrolled cards through the same writer |
| D7 | Do not enroll closed cards; refuse above 450 rows; row retirement | Yes; row retirement gets its own design, is a prerequisite for unattended auto-enroll, and is added to phase 2 (§12, PR 9) |
| D8 | Share the item-scoped writer with `migration-apply` and take #5123 into the writer PR (§12, PR 4) | Yes; Root coordinates with the #5119 owner |
| D9 | Hold `BOARD_DRIFT_HMAC_KEY` on fdev for private placeholders in proposals | Yes |
| D10 | Root merges enrollment carrier PRs | Yes, for now; automatic merge of carriers is revisited after 3 clean unattended runs |
| D11 | The reconciler writes with Root's `gh` PAT on fdev, as the merge gate does; the App stays read-only | Yes |
| D12 | E1 adds Project 3 membership for enrollment candidates and fills their fields in the same run; built-in auto-add stays off (dispatch rule 5) | Yes |
| — | The `TMPDIR` lock hazard | One fixed lock path for every Project 3 writer in phase 2 PR 1, which also delivers #5126 |

[Root's rulings](https://github.com/FS-GG/.github/pull/5139#issuecomment-6548756028) on the first
independent review of #5139 (REQUEST_CHANGES at `77320a0f`), 2026-10-31, including the reviewer's
nonmaterial list:

| Finding | Ruling | Where |
|---|---|---|
| S3 and S3b linkage | Automatic only for a `linkage_of()` verdict with no failures naming exactly one Delivery; anything else is a proposal | §2, §3 (P-LINK) |
| Merge race | The re-read includes the deciding PRs' state, draft flag and merge; a change refuses for this run | §3, §6 |
| Writer and lock | Gate waits up to 60 s, then writes a deferred-transition record the reconciler applies; `flock` on both sides; writer list adds Root's manual route and the archiver; immediate per-item re-read with the remaining window documented and a test; E1 custody keyed by issue node id | §6, §8 |
| S9 | Codify the intended-fields grammar in the lifecycle contract; apply only Backlog, and Ready when Root-authored; every other value is a proposal | §3, §12 |
| Same-run dependencies | Derive to a fixpoint and defer dependants; idempotence holds and is tested | §3, §10 |
| Archived cards | Confirm candidates through `Issue.projectItems(includeArchived: true)`; roll back only what custody proves was created; an archived member is Unknown and refused | §3, §4, §6 |
| Never closing | Carrier keyword check before push; a closed `TendChange` type and a transport allowlist; tests that can fail | §5 |
| Rollout | A contract-amendment PR gating S1, S3, S3b, S4, S9, O1 and E1; an engine release and installed qualification before the timer PR; PR 1 owns the gate's lock | §8, §12 |
| Read cost | About 9-10 requests at 222 items and 16-20 at 500, from the live mix | §2 |
| Nonmaterial | Roll-up all-In-review row, open Done child and empty Outcome Status; S1's Blocked exemption; S4 table and note reconciled, timeline decides over search; one HMAC key on both sides; privacy for `Suggested` and `Evidence`; no repost of a re-fired ID; post-merge trigger gated to the `.github` checkout and never swallowed; #5121 fixed in source only; D7 and D8 PR numbers; archiver mitigation accepted | §2, §3, §6, §7, §8, §13 |

[Root's rulings](https://github.com/FS-GG/.github/pull/5139#issuecomment-6548815933) on the delta re-review of #5139 (second REQUEST_CHANGES at `d015f72f`), 2026-10-31:

| Finding | Ruling | Where |
|---|---|---|
| A. Deferred-transition records | The gate replays its own deferred records at the start of its next invocation from PR 1; the reconciler replays too under `GR`; replay needs an exact preimage match, else a proposal; states `deferred → possible-send → verified`, `deferred` in the gate's validator; both writers touch the records only under the lock | §6, §10, §12 |
| B. Fixpoint and fallbacks | Fallbacks (T1f, S9, O1's empty fill; W1h from children until round 3 made Work level structural) apply only once their inputs have settled; effects are visible within a round in topological order; sends go in dependency order; every dependant of a refused effect is deferred; W1 conflicts are refused, never hidden | §3, §10, §11 |
| C. Gating W1 and T1 | Only the declaration branches W1d and T1d are contract-gated; the example enable file uses ungated rules | §3, §8 |
| Residue | S4 heals on the next run; E2's PR creation is the third allowlisted route; enrolled membership uses the #5132 cross-check; the PR 7 release includes PR 6; Unknown withholds the roll-up over the empty-Outcome fill; S9r means authored by an FS-GG agent account | §3, §5, §7, §12 |

[Root's rulings](https://github.com/FS-GG/.github/pull/5139#issuecomment-6548872887) on the round-3 re-review of #5139 (third REQUEST_CHANGES at `a2593781`),
2026-10-31:

| Finding | Ruling | Where |
|---|---|---|
| 1. Settled-input deadlock | Work level is structural (W1h in one pass; W1d confirms or is refused); only Track and Status have fallbacks; remaining cycles collapse into one unit decided by declarations or proposed; `deferred: input-pending` becomes a proposal after 3 runs or 24 hours; idempotence restated as settling within k applications over the enabled rules, plus a liveness property, both tested | §3, §10, §11 |
| 2. Replay order | Deferred records replay in merge order; for one Delivery the newest supersedes older ones, which are never sent; a fixture | §6, §10, §11 |
| Nonmaterial | `GR` treats a closed Delivery as not-applicable; a read-only `possible-send` sweep starts each gate invocation; the monotonicity reasoning rests on S3, S3b and S4 excluding each other and on no rule reading a parent's Status | §3, §6 |

[Root's rulings](https://github.com/FS-GG/.github/pull/5139#issuecomment-6548916283) on the round-4 re-review of #5139 (at `0fc687de`), 2026-10-31:

| Finding | Ruling | Where |
|---|---|---|
| 1. Crash-safe replay | A terminal `deferred → refused` for a preimage mismatch, raising a proposal; supersession decided per Delivery over all its records by the stored `mergedAt`; an older `deferred` record never sends; older records durably `superseded` before the newest moves to `possible-send` | §6, §10, §11 |
| 2. W1h leaf branch | Delivery only with a Project 3 parent, an issue at least 24 hours old and no differing declaration (a refusal-only read while W1d is off); otherwise deferred with the usual expiry | §3, §10, §11 |
| Nonmaterial | Liveness bound max(k, 3 runs) or 24 hours; an expired, proposed deferral is still applied when it settles, and its proposal clears | §3, §10 |

[Root's rulings](https://github.com/FS-GG/.github/pull/5139#issuecomment-6548947046) on the round-5 re-review of #5139 (at `98faef72`), 2026-10-31:

| Finding | Ruling | Where |
|---|---|---|
| T1f on young leaves | T1i inherits through the structural classification, before the leaf's Work level is written; T1f also waits for the card's own pending Work level; fixture 20 holds for a child under 24 hours old | §3, §10, §11 |
| Nonmaterial | A settle-window deferral's expiry starts when the window ends; "older than some other record"; older `deferred` records are also superseded when a newer record is already non-`deferred`; pre-PR-1 records without `mergedAt` are ordered oldest | §3, §6, §10 |

[Root's rulings](https://github.com/FS-GG/.github/pull/5139#issuecomment-6548973997) on the round-6 re-review of #5139 (at `15e48d7d`), 2026-10-31:

| Finding | Ruling | Where |
|---|---|---|
| Declared Track overwritten | A declared Track wins: T1i and T1f never write it; while T1d is off it is a refusal-only read, deferred and expiring into `track-declared-pending`; two fixtures | §3, §10, §11 |
| T1i is a heuristic | Inheritance disagrees with about 36% of live pairs, so T1i stays out of the unattended starting set until a 7-day plan-only comparison shows at most 5% disagreement; the starting set is `["W1h","T1f"]`, and T1f applies only to undeclared, parentless, settled cards | §3, §8 |

Owner-authorized decision 9, 2026-11-01 ([#4730 comment](https://github.com/FS-GG/.github/issues/4730#issuecomment-6551301543)), recorded by Root under the owner's
authorization to make all decisions:

| Decision | Ruling | Where |
|---|---|---|
| 9. Delivery closure | Tend closes a Delivery automatically when all its strictly linked PRs are merged (at least one) and none are open, unless the last merge carries `fsgg:delivery-continues/v1`; never an Outcome, never a Blocked card; closed with a comment listing the merged PRs. This amends "tend never closes issues" for Delivery issues only. `closeIssue` (with `addComment`) becomes a typed `CloseDelivery` change and a transport allowlist entry for Deliveries only. C1 is gated on the contract amendment (PR 6) and on the enable file. Until it is enabled, Root applies the rule by sweep | §3, §4, §5, §8, §11, §12 |

[Root's rulings](https://github.com/FS-GG/.github/pull/5150) on the review of the decision-9 amendment (REQUEST_CHANGES on #5150), within
decision 9:

| Finding | Ruling | Where |
|---|---|---|
| C1 closes Deliveries with work left | A reopen guard (never close an issue reopened after a `fsgg:tend-closed/v1` comment or C1 custody record; propose instead); the complete open-PR set re-read before the send; C1 also needs Status In review, a 48 h settle window after the last merge, and a populated Work level of Delivery; 7 days of plan-only would-close observation before enabling; a merge-gate advisory warning for a routine PR without the continues marker whose Delivery's done-when names a release, install or readback step | §3, §4, §5, §11, §12 |
| Nonmaterial | The close comment follows dispatch rule 3 (merge commits and recorded independent verdicts, or "Unknown: no recorded independent verdict"); PR 6 also amends the "merged source PR cannot establish installed acceptance" line; the wrong-Delivery link risk is noted | §3, §12 |

[Root's ruling on the reviewer's residual](https://github.com/FS-GG/.github/pull/5150#issuecomment-6552391470) (2026-11-01, owner-authorized under decision 9), which
resolves it with an explicit completion marker:

| Finding | Ruling | Where |
|---|---|---|
| In review and the continues opt-out are not reliable completion evidence | Every routine PR declares exactly one of `fsgg:delivery-continues/v1` and `fsgg:delivery-completes/v1`; the merge gate checks it, advisory for 7 days and then required; C1 closes only when the last merged linked PR carries `fsgg:delivery-completes/v1`, with every other C1 condition; Root's manual sweep applies the same rule and judges pre-rule PRs case by case; PR 6 amends the routine-PR contract | Why, §3, §4, §11, §12 |
