# BOARD-RECONCILE-01: an F# reconciler that derives, plans and applies safe Project 3 fixes

Design, 2026-10-31. Delivery [#5136](https://github.com/FS-GG/.github/issues/5136) under Outcome
[#4730](https://github.com/FS-GG/.github/issues/4730) (COORD-EFF-02). This is phase 1 of #5136: the
design only. It makes no board write and changes no code, contract or workflow. Root (Programme/root)
owns approval and all acceptance. [Root's decisions](https://github.com/FS-GG/.github/pull/5137#issuecomment-6548516184) on D1-D12 (2026-10-31) are folded in below and listed in §13.
Phase 2 (implementation PRs, §12) starts only after this design passes independent review.

## Why

The user asked for automatic checking, updating and fixing of Coordination Project 3
(`PVT_kwDOEYAWY84Bldpa`), done by deterministic F# machinery in the coordination engine rather than by
manual Root passes and Python glue (#5136).

Today the board has three detectors and two narrow writers. Every other field change is a manual Root
write:

| Piece | What it does | What it cannot do |
|---|---|---|
| [Board-currency rules](../../scripts/board-currency-rules.py) R1-R9, U ([design](2026-10-31-board-currency-gate.md)) | Pure rule set over a snapshot; findings in [#4985](https://github.com/FS-GG/.github/issues/4985) | Write anything |
| [Board-drift observer](../../scripts/board-drift-observer.py) rules a-e, x | Hourly heuristics; single writer of the #4985 body | Write a board field |
| [Merge gate](../../tools/routine-delivery.py) `--board-gate` (#5106) | One post-merge Delivery Status write (In review, or In progress with `fsgg:delivery-continues/v1`) through an item-scoped guard with durable custody | Any other field, any card other than the merged Delivery |
| Built-in "Item closed → Done" workflow | Sets Done when an issue closes; Root decision 2 made it the only writer of Done | Cards added after closure, or closed before it was enabled |
| `board-v2 migration-apply` | Absent-value initialization of explicitly declared values ([`V2Migration`](../../src/FS.GG.Coord.GitHub/V2Migration.fsi)) | Infer a value; replace a populated value; scale (#5123) |
| [Done-retention archiver](https://github.com/FS-GG/.github/issues/5119) (#5119 PR 5, held, Python) | Archive Done beyond the newest 200 | Anything else |

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
   2 inspects) besides the enrollment stages, and seven Root rulings: a `--main-revision` assertion (#5113), a reader-identity hand
   rebind (#5120), a 207-row inspect accepted as Unknown (#5121), a re-split into 29 scopes of at most
   4 cards after 19 effects hit the 270-second ceiling (#5123), the Roadmap inheritance rule, closed
   cards targeting Done, and 14 exclusions.

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
  queries per send); writes through new `V2Migration.Change` cases (§6); the CLI in
  `BoardV2Application`. The command is registered in the
  [writer census](v1-writer-census.json) as a writer.
- **Not a revival.** ADR-0091 retired the legacy v1 automatic projection writer. This is a new V2
  writer with its own rule set, enable switch and custody, and it never uses `Done.rollUpParents`.

## 2. Inputs and reads

### Facts

Native state only, as in the board-currency rule set:

- per card: item id, `isArchived`, and the Status, Work level, Track and Roadmap values, each with its
  `updatedAt`;
- per issue: node id, repository and `isPrivate`, `state`, `stateReason`, `closedAt`, `updatedAt`,
  body, native parent, sub-issues (complete list), `blockedBy` (complete list);
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

A "linked PR" is a PR whose body references the issue through a `Refs` line or a closing keyword, using
the same normalisation as the board-currency `linkage_of()` (CRLF to LF, code spans and fences
removed). A PR is never a Delivery. Because R1 stays in Python (§9), one shared conformance corpus is
run against both implementations so they cannot drift.

### Reads and their cost

| Read | Shape | Sends at 222 items | Sends at 500 items |
|---|---|---:|---:|
| A. Board and issue facts | `items(first:50)` with the four field values and the issue fields above, `subIssues(first:50)`, `blockedBy(first:50)`; the first page also carries the schema and workflows | 5 | 10 |
| B. Open PRs | one organization search, `is:pr is:open`, 100 per page | 1 | 1 |
| C. Targeted evidence | timeline cross-references (`last:25`) and `comments(last:20)`, 25 issues aliased per send, only for cards a rule needs (In review without an open PR, In progress without an open PR, Blocked without an open native blocker, Backlog candidates for Ready, Outcomes with every child closed) | 1-2 | 2-4 (worst case 20) |
| D. Overflow | sub-issue continuation pages for an Outcome with more than 50 children | 0-1 | 0-2 |
| **Total** | | **7-9** | **13-17 (worst case 33)** |

- Pages stay at 50 items. Root's ruling on #5130 was to take no saving that raises points, and pages of
  50 with nested connections cost about 1-2 GraphQL points each, as the currency fetch measures today
  (5 requests, 5 points at 222 items).
- No REST comment read happens in the board pass. A new comment moves the issue's `updatedAt`, so a
  comment's `createdAt` never exceeds it, and R4's activity maximum (issue `updatedAt`, the Status
  value's `updatedAt`, linked PR `updatedAt`, comment `createdAt`) is the same without the comments. Comments are read only in C, and only for marker legs (`fsgg:blocked-on/v1`,
  `fsgg:outcome-closure-proposed/v1`, `fsgg:outcome-accepted/v1`).
- Merged PRs are not searched org-wide. A rule that needs merged evidence (S4, P-ACCEPT) reads that
  card's timeline in C, so a PR merged months ago is still seen, unlike a 7-day search window.
- A board whose `totalCount` exceeds 500 is refused on the first page, as `GraphQl.drain` does since
  #5122. A truncated connection is Unknown for the rules that need it (§4c), never an empty list.

**Budget.** The read ceiling is `8 + ceil(rows/50) + ceil(rows/25)` sends (22 at 222, 38 at 500), and
the deadline is 120 seconds for reads. Writes have their own ceiling (§6). Hourly runs cost about
24 × 17 ≈ 400 read sends a day at 500 items. For comparison, #5119 measured the hourly drift fetch at
26 calls after its cut, and a 500-row `inspect` at 175 sends against its 1,064 ceiling. The reconciler
reads less than one inspection per run. Phase 2 measures the real per-run cost and prints it in the
plan (`requests`, `graphqlPoints`, `restCalls`), which is #5136 Done-when 4.

## 3. Derivation

`V2Tend.derive : Parameters -> Snapshot -> Plan` is pure. Its only clock is `Snapshot.ObservedAt`,
so one snapshot always gives the same plan (board-currency lesson 7). An effect ID is
`<rule>:<item>:<field>:<target>` with no age or count, so an unchanged situation keeps its ID.
Parameters are part of the rule-set version `fsgg:board-tend-rules/v1`.

| Parameter | v1 value | Why |
|---|---|---|
| `activityHours` | 48 | The same as board-currency R4 |
| `doneSettleMinutes` | 15 | Gives the built-in Done workflow time to act before the backstop writes (S1) |
| `reviewSettleHours` | 48 | An In review card must have stayed so this long before S4 moves it, so a PR about to open is not raced |
| `doneVisible` | 200 | #5119's retention bound |
| `maxItemsPerRun` | 100 | Write ceiling per run (§6) |

Body sections are read only in the closed grammar the dispatch rule writes: a first line
`Work level: <v> · Track: <v> · Intended Status: <v>` (emphasis markup removed) and the `## Blocked on`,
`## Done when` and `## Acceptance evidence` sections. Any other form is "not declared", never a guess.

### Work level

Never overwrite a populated value. For an empty value:

1. **Hierarchy.** A native parent that is a Project 3 Outcome gives Delivery. One or more sub-issues that
   are Project 3 Deliveries, with no Delivery parent, gives Outcome.
2. **Declaration.** The body's `Work level:` value.
3. Both present and equal, or only one present: that value (rule W1, auto-apply).
4. Both present and different: refuse, `work-level-conflict`.
5. Neither present: propose, `work-level-undetermined`.

### Track

Never overwrite. For an empty value: the body's `Track:` declaration; otherwise, for a Delivery, its
parent Outcome's Track; otherwise `Follow-up` (rule T1, auto-apply). `Follow-up` is the default because it
claims less than `Active delivery` (D5, accepted).

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
`DPR` the open drafts and `MPR` the merged ones.

| Rule | Condition | Desired | Class |
|---|---|---|---|
| S1 | Closed (any `stateReason`), Status not Done, `closedAt` older than `doneSettleMinutes` | Done | Apply |
| S2 | Open and Status Done | — | Propose `done-open` |
| S3 | `OPR` not empty, Status Backlog, Ready or In progress | In review | Apply |
| S3b | `OPR` empty, `DPR` not empty, Status Backlog or Ready | In progress | Apply |
| S4 | Status In review, `OPR` and `DPR` empty, `MPR` empty, In review for longer than `reviewSettleHours`, timeline complete | In progress | Apply |
| P-ACCEPT | Status In review or In progress, `OPR` and `DPR` empty, `MPR` not empty, `## Acceptance evidence` present and not "None yet." | — | Propose `acceptance-ready` |
| S5 | Status In progress, `OPR` and `DPR` empty, no activity within `activityHours` | — | Propose `stale-active` (to Ready, Blocked, or acceptance) |
| P-READY | Status Backlog, enrolled, `## Blocked on` is "Nothing" or "Nothing external", no open native blocker | — | Propose Backlog → Ready |
| P-UNBLOCK | Status Blocked, every native blocker and `fsgg:blocked-on/v1` blocker closed | — | Propose Blocked → Ready |
| P-NOBLOCKER | Status Blocked, no blocker named | — | Propose (R3 `no-blocker`) |
| S9 | Open, Status empty | The body's `Intended Status`, else S3/S3b's target when it applies, else Backlog | Apply |

Notes:

- **Blocked is never written or overwritten automatically.** It records Root's judgement that a named
  prerequisite is missing, which is also why the merge gate refuses a Blocked preimage.
- **S3 and the merge gate agree.** After a merge the gate writes In review, or In progress with
  `fsgg:delivery-continues/v1`. With no open PR left, S4 does not fire while `MPR` is not empty, and
  S3 does not fire without an open PR, so the reconciler never undoes the gate's write.
- **S4 is narrower than #5136's wording.** "In review with no open PR" includes Deliveries whose
  implementation merged and which wait for acceptance; the status lifecycle defines In review as exactly
  that. Those go to P-ACCEPT, not back to In progress. S4 fires only when no PR of any state is linked.
  Root accepted this (D4).
- **S1 makes the reconciler a second Done writer.** It writes Done only where the built-in workflow did
  not: a card added after its issue closed, or closed before the workflow was on (cohort-90 found
  5 such cards). Both writers set the same value, so a race ends in the same state. This amends Root
  decision 2 ("the only writer of Done"); Root approved the amendment (D2). Phase 2 updates the
  board-currency design's §2 and dispatch rule 3 to name the backstop.
- **S9's Backlog fallback** claims the least ("no concrete next action is admitted").

### Status of an Outcome: the roll-up

The roll-up needs the complete native sub-issue list, and each open sub-issue must be a Project 3 card
with a Status. Otherwise the Outcome is Unknown (`children-incomplete`), as the lifecycle contract
requires ("incomplete parent child coverage withholds parent rollup"). With `C` the Statuses of its open
children:

| `C` | Desired |
|---|---|
| contains In progress or In review | In progress |
| else contains Ready | Ready |
| else contains Backlog | Backlog |
| else all Blocked | Blocked |
| empty, at least one closed child (R5) | none: propose `children-complete` (close, or file the remaining Deliveries) |
| no children at all (R6) | none: propose `no-open-delivery` |

Rule O1 applies the roll-up only between Backlog, Ready and In progress: the preimage and the target
must both be one of those three. A Blocked target or preimage, and the Outcome All-In-review case, become
proposals. In review and Done are never written to an Outcome automatically. The status lifecycle
already says that "active child work can make an Outcome In progress" and that "one child PR cannot make
the whole Outcome In review". Root approved amending the lifecycle contract's "never automatically
climb parents" to allow this bounded roll-up (D3); the amendment lands in phase 2 before O1 is enabled.
A closed Outcome follows S1.

### Enrollment state

An unenrolled card is an enrollment candidate when it is an issue (not a PR or draft) in one of the
binding's repositories, is open, has a derivable Work level, and has an RM1 Roadmap. Native open
sub-issues of enrolled Outcomes that have no Project 3 card at all are candidates too, because
auto-add is off. Closed cards are not candidates: binding rows are append-only and the cap is 500, so a
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
    | SetField of item: ItemId * field: Field * preimage: string option * target: string * rule: RuleId
    | AddMembership of issue: IssueRef * rule: RuleId
    | Archive of item: ItemId * rule: RuleId
    | PrepareEnrollment of rows: EnrollmentRow list
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
| W1, T1, RM1 | Fill an empty Work level or Track (any card), or Roadmap (enrolled cards only, D6) | Fills only an absent value from native hierarchy, an explicit body declaration or the enrolled row; a populated value is never touched (the migration route's existing absent-only rule) | `clearProjectV2ItemFieldValue` restores the empty preimage, which is recorded |
| S9 | Fill an empty Status | As above; the fallback is the least-claiming option | Clear the value |
| S1 | Closed → Done | Native closure is Root's acceptance act; the built-in workflow already makes the same write | Set the recorded preimage option |
| S3, S3b | → In review or In progress when an open linked PR exists | Decided from native PR state and body linkage, the same linkage the merge gate trusts; never from Blocked or Done | Set the recorded preimage |
| S4 | In review → In progress when nothing is linked | Decided from a complete native timeline, after a settle window | Set the recorded preimage |
| O1 | Outcome roll-up among Backlog, Ready and In progress | A function of children's Statuses over a complete child list | Set the recorded preimage |
| E1 | Add membership for an enrollment candidate, with Status, Work level and Track filled in the same run (D12) | Every input is native; the card is never left bare (§7) | `deleteProjectV2Item` on a card the reconciler created and nobody has edited since |
| E2 | Prepare the enrollment successor and open its carrier PR | Offline; the merge is Root's | Close the PR |
| A1 | Archive Done beyond 200 (after the Python archiver retires) | #5119's guards | `unarchiveProjectV2Item` |

Each Apply effect also has a preimage condition: if the adjacent re-read differs from the snapshot, the
effect is refused for this run and derived again next run.

### (b) Propose to Root

S2 `done-open`, P-ACCEPT, S5 `stale-active`, P-READY, P-UNBLOCK, P-NOBLOCKER, Outcome Blocked or
All-In-review roll-ups, `children-complete`, `no-open-delivery`, `work-level-undetermined`, Roadmap and plan gaps,
board-currency R2 `outcome-unaccepted`, R8 `pr-open-on-closed` and R9 hierarchy findings. These ask for a
judgement: whether the next action is admitted, whether acceptance is met, or which plan owns the work.

### (c) Refuse or Unknown

A truncated or unreadable input; null content; a draft or PR card; a field option missing from the
schema; conflicting declarations; a repository outside the binding; an uncertain custody record (ours or
the merge gate's) for the item; a held writer lock; an exhausted budget (reported as `deferred`, not
Unknown). A refusal is never a pass ([#266](https://github.com/FS-GG/.github/issues/266)) and never
silently dropped: it is in the plan file, and a refusal that persists for 24 hours becomes a proposal.

## 5. The reconciler never closes an issue

Acceptance and closure stay Root's. P-ACCEPT and `children-complete` are proposals only. This is enforced
by construction, not by care:

- The writer's request builder accepts only the reconciler's `V2Migration.Change` cases: one
  `updateProjectV2ItemFieldValue`, `clearProjectV2ItemFieldValue` (rollback only), `addProjectV2ItemById`,
  `deleteProjectV2Item` (rollback of E1 only) and `archiveProjectV2Item`. It cannot construct an issue
  mutation (`closeIssue`, `updateIssue`, `addSubIssue`, comment edits).
- A test sends every other mutation name through the builder and expects a refusal, and a mutation leg
  (§10) that admits `closeIssue` must turn that test red.
- The only issue-side write is the proposals comment on #4985 (§6), made through a separate REST route
  that can only create a comment on that one issue.

## 6. The write path

### Reuse and extension

`migration-apply` already holds the engine's write discipline: an exclusive invocation marker, numbered
`-precondition`, `-intent`, `-intent-metadata`, `-response` and `-receipt` files written exclusive,
0600, write-through and fsynced, one `Dispatch` per effect, a `clientMutationId` the response must echo,
visibility-backoff readback, and no resend (`RetryMutation` is refused). What it lacks is an item-scoped
precondition: it re-reads the whole scope before every effect, which is why cohort-90 managed only about
19 effects per 270 seconds (#5123).

The reconciler extends it rather than adding a second writer:

1. New `Change` cases `TransitionPlanningValue(item, field, from, to)`, `ClearPlanningValue` (rollback),
   `ArchiveItem` and `RemoveCreatedItem` (rollback). Existing `AddMembership` and
   `InitializePlanningValue` are reused.
2. An item-scoped precondition, generalising the guard Root accepted for the merge gate (#5106): the
   compared state is the item's complete reported field-value set, its item and project identities, the
   field's schema and options, and the issue's determining facts (state, parent). An adjacent read must
   equal the snapshot before the send. The readback must equal the preimage with only the selected field
   moved.
3. **Grouped reads, one send per item.** Up to 25 items are re-read in one aliased query. Each item's
   field effects go in one mutation document: aliased `updateProjectV2ItemFieldValue` fields, run in
   order. The group is read back in one aliased query. A group's window is at most 30 seconds, otherwise
   it is re-read. A transition that replaces a populated value (S3, S3b, S4, O1, S1) uses a group of
   one, keeping the merge gate's adjacency exactly; only absent-value fills use bigger groups.
4. The same change is offered to `migration-apply` for #5123, so the enrollment initialization and the
   reconciler share one writer (D8, accepted; Root coordinates with the #5119 owner).

### Per effect

1. The preimage is the snapshot value; an equal value is a no-op.
2. Adjacent re-read (grouped as above); any difference refuses the effect this run.
3. Durable possible-send custody: one record per item send in
   `$XDG_STATE_HOME/fsgg/board-tend/<run>/`, keyed by item id and carrying the field, preimage and
   target. If it cannot be written, nothing is sent (`refused-custody`).
4. One send.
5. Independent readback. `written` only when the readback equals the preimage with only the selected
   fields moved; the record is rewritten to `verified`.
6. A failed, malformed or uncertain send or readback is `uncertain`. Never resend. The record stays,
   and later runs refuse that item (`uncertain-custody`) and only read back: a readback equal to the
   target rewrites the record to `verified`; anything else leaves it for Root, who reads the item and
   retires the record. This is the merge gate's crash-recovery rule.

### Write budget

At most `maxItemsPerRun` items (default 100) and `2 × ceil(items/25) + items` sends, with mutations at
most one per second. GitHub documents a secondary limit of about 80 content-generating requests a minute
and 500 an hour; whether field updates count is not established, so the pacing assumes they do. Steady
state is a handful of items per run. The cohort-90 initialization (114 cards, 423 effects) becomes 114
mutation sends plus about 10 group reads across two runs, instead of 30 applies.

### Enrolled and unenrolled cards

- **Enrolled** (rows in the private binding behind the public commitment): every Apply rule. A Roadmap
  must equal the enrolled row; a differing populated Roadmap is a refusal (`ConflictingHumanValue`, as
  in the planner).
- **Unenrolled cards** (candidates and non-candidates alike): absent-value fills of Status, Work level
  and Track (S9, W1, T1) through the same item-scoped guard, so the board is complete (D6, overriding the
  proposed "S1 and A1 only"; it matches Root's manual fills recorded on #4941). Root widened D6 to the
  mechanically safe Status moves as well: S3 (open linked PR → In review), S4 (In review with nothing
  linked → In progress) and the O1 roll-up within Backlog, Ready and In progress, all through the same
  guarded writer. S1 and A1 also apply. S3b (draft only → In progress) is not in the widened set and
  stays enrolled-only. Roadmap and the enrollment rows stay enrolled-only. For a candidate, E1 adds the
  card and makes the fills in the same run, so a bare card never stays on the board; Roadmap follows
  enrollment (§7).

### Interaction with the merge gate and the Done workflow

- The reconciler reads the merge gate's custody directory and refuses any item with a `possible-send`
  record (`gate-custody-uncertain`).
- Both take the same writer lock (§8), so a post-merge write and a reconciler write never interleave.
  The merge gate is Python; phase 2 adds the lock to it, with a cross-process test that the two exclude
  each other.
- S1 waits `doneSettleMinutes` after `closedAt`, so in the normal case the built-in workflow has already
  written Done and S1 is a no-op. If the workflow is read as disabled, S1 still applies: lifecycle
  rule 3 then makes the closer write Done through the guarded helper, and the reconciler is that helper.
- The gate writes no Done and no Outcome Status; the reconciler writes no PR-time verdict. Neither undoes
  the other (§3, S3 and S4).

### Proposals to #4985

The drift observer stays the only writer of the #4985 body. The reconciler adds append-only comments:

- **Stable IDs.** `P:<rule>:<subject>:<reason>`, with the subject a public reference or the observer's
  `private:<12 hex HMAC>` placeholder, using `BOARD_DRIFT_HMAC_KEY` held on fdev, mode 0600 in Root
  custody (D9).
- **Once per ID.** A ledger in `$XDG_STATE_HOME/fsgg/board-tend/proposals.json` maps each ID to the
  comment that first carried it and its state (live or cleared). A run posts one comment with only the
  IDs not live in the ledger, carrying `<!-- fsgg:board-tend-proposals/v1 run=<id> -->`. An ID that
  clears is marked cleared with no comment. If it fires again later, it is a new occurrence and is posted
  again.
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
| Make the card | Auto-add (now off) | E1: `AddMembership` with Status, Work level and Track filled in the same run |
| Roadmap | Root ruling | RM1, codified; computed for the request, written after `bind-runtime` |
| Status targets | Root decided per card | The derivation in §3; closed cards are not enrolled |
| `resolve-issues`, `route-preflight`, `plan`, `prepare`, `render-carrier` | Run by hand | Run in-process by the reconciler with the same inputs, once per batch |
| `ConstructorIdentity` | `Unsupported` whenever `main` moves; Root asserted `--main-revision` | **Prerequisite #5113** (content-based identity). Until then E2 is refused with `constructor-identity`; asserting the revision automatically would defeat the check |
| `ProjectAutoAdd` | `Unknown` on exactly the cohort's own bare cards | Prerequisite (small): the preflight accepts the request's rows as expected memberships, and E1 leaves no bare card |
| Carrier PR | Opened by a worker | E2 opens one PR (only the manifest commitment changes); at most one open at a time, and new candidates wait for the next |
| Merge | Root | Root, through the routine merge (D10). Automatic merge of carrier-only PRs is revisited after 3 clean unattended runs |
| `bind-runtime` | By hand | After the merge, by the next run |
| Reader identity after an upgrade | Hand rebind | **Prerequisite #5120** (a designed rebind stage checked against the executing assembly) |
| Initialization | 30 applies at 4 cards per scope | §6 writer: about 1 send per item (fixes #5123 by construction) |
| Population read | 207-row inspect accepted as Unknown | Fixed by #5127 (175 sends at 500); the reconciler's own read checks every enrolled node is a member |

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
  `systemctl --user start --no-block fsgg-board-tend.service` on fdev. This is best effort: a
  failure to start never affects the merge. systemd does not queue a second start of an active oneshot
  unit, and the reconciler skips a run within 5 minutes of the last one unless `--force` is given.
- **Lock.** Today `migration-apply` locks `Path.GetTempPath()/fsgg-v2-migration-PVT_kwDOEYAWY84Bldpa.lock`,
  and `Path.GetTempPath()` follows `TMPDIR`, so two writers with different `TMPDIR` values do not exclude
  each other. The refresh path's `fsgg-v2-observation-<project>.lock` has the same defect, and engine
  tests that reach either lock contend with a live apply (#5126). Phase 2 PR 1 moves every Project 3
  writer (`migration-apply`, `refresh`, the merge gate and the reconciler) to one fixed path,
  `$XDG_RUNTIME_DIR/fsgg/board-writer-PVT_kwDOEYAWY84Bldpa.lock`, and refuses to write if it is unset.
  The lock directory is injectable, tests inject a private one, and a test proves a test run never opens
  the production path, which fixes #5126. During the transition writers also take the legacy lock at the
  literal `/tmp` path. The reconciler takes locks without waiting: a held lock gives a plan-only run with
  `lock-held`, never a queued writer.
- **Dry run by default.** `--apply` is ignored unless
  `$XDG_CONFIG_HOME/fsgg/board-tend.json` exists, is owned by the user with mode 0600, and lists the
  enabled rules: `{"apply": ["S1","W1","T1"], "propose": true, "maxItemsPerRun": 100}`. Rules are enabled
  one at a time. Deleting the file, or `"apply": []`, is the kill switch. A run without the file still
  writes its plan, so the rollout can compare plans before any write.

## 9. Migration from the Python observers

This follows #5094's retirement plan: observe at least 7 days, Root rules on each pair separately, then one
PR per approved pair removes the old rule, its ack route and its tests. The reconciler adds a third column
to each pair. During observation it runs plan-only, and phase 2 publishes a per-run comparison of IDs.

| Python rule(s) | Reconciler rule(s) | Order | Notes |
|---|---|---|---|
| drift d, currency R7 | W1, T1, RM1, S9 (apply); `work-level-undetermined` (propose) | First | Drift d and R7 are identical; the reconciler fixes rather than reports |
| drift x, currency U | Refuse class | First | The reconciler's Unknown is at least as wide as U |
| drift b, currency R2 | S1 (apply), S2 (propose); R2 `outcome-unaccepted` (propose) | Second | b on PR cards: the board holds no PR cards today |
| drift a, currency R8 | S3, S3b (apply); `pr-open-on-closed` (propose) | Second | a covers any card; the reconciler covers issue cards, which is every card now |
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
  - Idempotence: applying the Apply class to the snapshot and deriving again gives an empty Apply class.
  - Safety: Apply never overwrites a populated Work level, Track or Roadmap, never writes Blocked, never
    moves off Blocked or Done, never sets Done on an open issue, and never writes In review or Done to an
    Outcome.
  - Partition: each card and field is in exactly one class.
- **Writer.** Through `Fake.Recorder`: an adjacent read differs, custody cannot be written, the send
  errors, the send times out after dispatch, the readback is malformed or shows another field moved, and
  a crash between send and readback. Every case asserts the send count (0 or 1, never 2) and the custody
  state.
- **Budget.** Synthetic boards of 222 and 500 items assert read sends against the ceiling in §2, and the
  write send count for 25, 100 and 114 items.
- **Conformance.** The linkage corpus runs against `linkage_of()` and the F# implementation. During the
  migration, the overlapping currency rules are compared ID by ID on the same snapshots.
- **Mutation.** A spec file `tests/coord-board-tend-mutation/specs.yml` for the existing
  `scripts/gate-mutate.py` harness, with one leg per guard: each Apply condition, the Blocked and Done
  protections, the settle windows, absent-only filling, the roll-up's completeness check, the custody
  write before send, no resend, the lock, the enable file and the issue-mutation refusal. Every leg must
  be killed, and its anchor check runs on each PR that touches the reconciler, as
  `tests/coord-engine-mutation/run.sh` does.
- **Temporary paths.** Tests set a private `TMPDIR` and an injected lock directory. They never touch the
  production lock.

## 11. Acceptance fixtures

| # | Case (from #5136 and #5133) | Fixture asserts |
|---|---|---|
| 1 | No Status (11 cards) | S9: the declared `Intended Status`; otherwise S3 or S3b; otherwise Backlog |
| 2 | Missing Work level or Track (128) | W1 from the parent and from the declaration; T1; a W1 conflict is refused |
| 3 | Closed, not Done | S1 after the settle window; nothing inside it |
| 4 | In review, no open PR (5) | Nothing linked: S4. Merged PR with evidence: P-ACCEPT, Status unchanged |
| 5 | In progress, stale or finished (13 of 34) | Open PR: S3. Idle: S5. Merged with evidence: P-ACCEPT |
| 6 | Backlog with "Blocked on: Nothing" (25) | P-READY only when enrolled with no open native blocker; never applied |
| 7 | Outcome Status ignoring its children | O1 within Backlog, Ready, In progress; Blocked and All-In-review proposed; an incomplete child list is Unknown |
| 8a | Roadmap inheritance with an anchor (Coordination#1026's 8 children) | RM1 drops the anchor; protected-document grammar passes |
| 8b | Repository outside the binding (FsQuint#50) | `outside-binding`, reported once over several runs |
| 8c | No admissible plan (SDK-PATCH-01), parent with no Roadmap (#4811 under #5097) | A proposal naming the plan owner; no write |
| 8d | Closed card in the cohort (Unity.Client#28) | Done by S1; not an enrollment candidate |
| 8e | `ConstructorIdentity` after `main` moves (#5113) | E2 refused until the prerequisite lands |
| 8f | 114-card initialization (#5123) | At most 100 items in one run, about 1.1 sends per item, no whole-scope re-read |
| 9 | Merge-gate interplay | After the gate's In review with a merged PR, the plan is empty for that item; a gate `possible-send` record refuses the item |
| 10 | Never close | No plan, for any fixture, contains an issue mutation |

## 12. Phase 2 PR sequence

One open PR per chain, each `Refs #5136`, each reviewed independently:

1. The fixed writer lock path for every Project 3 writer, with an injectable lock directory and the
   test that no test opens the production path (§8). This also fixes #5126.
2. Pure derivation, plan types, fixtures (§11), properties and the mutation spec. No IO.
3. Batched reads, `board-v2 tend` plan-only, the cost counters and a live read-only measurement at the
   current board size.
4. The writer extension (§6), custody and the merge gate's use of the shared lock. This also delivers
   #5123 (D8).
5. Proposals to #4985 with the ledger.
6. The contract amendments Root approved: Done backstop (D2) in dispatch rule 3 and the board-currency
   design, and the bounded Outcome roll-up (D3) in the lifecycle contract and status lifecycle.
7. The fdev units, the post-merge trigger and the enable file; 7 days plan-only, then Root enables rules
   one at a time (S1 and O1 only after PR 6).
8. The row-retirement design (D7), a docs PR reviewed like this one.
9. Auto-enroll (§7), after #5113, #5120 and the delivered row retirement.
10. Retirement PRs, one per pair approved under §9.

#5136 Done-when 2 is met after PR 4, Done-when 3 after PR 7 and the first retirement, and Done-when 4 is
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
| D6 | Field fills on unenrolled cards | **Overridden:** Status, Work level and Track absent-value fills are allowed on unenrolled cards through the same guarded item-scoped writer; Roadmap and manifest fields stay enrolled-only. **Widened** by Root later the same day (relayed by the coordinator; not yet recorded on GitHub): the Status moves S3, S4 and O1 (within Backlog, Ready and In progress) are also allowed on unenrolled cards through the same writer |
| D7 | Do not enroll closed cards; refuse above 450 rows; row retirement | Yes; row retirement gets its own design, is a prerequisite for unattended auto-enroll, and is added to phase 2 (§12, PR 8) |
| D8 | Share the item-scoped writer with `migration-apply` and take #5123 into the writer PR | Yes; Root coordinates with the #5119 owner |
| D9 | Hold `BOARD_DRIFT_HMAC_KEY` on fdev for private placeholders in proposals | Yes |
| D10 | Root merges enrollment carrier PRs | Yes, for now; automatic merge of carriers is revisited after 3 clean unattended runs |
| D11 | The reconciler writes with Root's `gh` PAT on fdev, as the merge gate does; the App stays read-only | Yes |
| D12 | E1 adds Project 3 membership for enrollment candidates and fills their fields in the same run; built-in auto-add stays off (dispatch rule 5) | Yes |
| — | The `TMPDIR` lock hazard | One fixed lock path for every Project 3 writer in phase 2 PR 1, which also fixes #5126 |
