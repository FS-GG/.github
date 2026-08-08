namespace FS.GG.Coord

/// The coordination domain, as types (ADR-0034).
///
/// THE DESIGN RULE, AND THE ONLY ONE THAT MATTERS HERE: a failed read must not be able to masquerade
/// as a legitimate answer. `FS-GG/.github` epic #266 — "coherence gates that fail open: a missing
/// subject reports green" — has 51 children, and almost every one of them is that mistake made in a
/// substrate where an error, an empty result and a legitimate "no" are the same value (bash's empty
/// string). So none of the types below can express it: there is no `bool` anywhere in a verdict, and
/// "I could not look" is a case you must handle, not a value you can accidentally produce.
module Types =

    /// Which worker holds a lock. NOT the GitHub account: N agents in a fan-out authenticate as the
    /// SAME account, so `@me` is one principal for all of them and an assignee-keyed lock is a no-op
    /// under exactly the conditions it exists for (ADR-0027, superseding ADR-0021 §1).
    type WorkerId =
        | WorkerId of string

        member Value: string

    /// The agent harness session a claim was taken from — PROVENANCE, never identity.
    ///
    /// A session id is unique per *session*, not per *worker*, and the mapping between the two is a
    /// property of the harness: on Claude Code every subagent of a session shares one
    /// `CLAUDE_CODE_SESSION_ID`, so keying a lock on it collapses an N-agent fan-out onto a single
    /// worker — the same-account bug, one level down. It is carried so that "which agent transcript
    /// took this lock?" is a lookup rather than mtime forensics.
    type SessionId =
        | SessionId of string

        member Value: string

    /// The board's `Status` column.
    ///
    /// `NoStatus` is a CASE, not a null. An item on the board with no Status was invisible to every
    /// scheduler — it is not `Backlog`, it is not an error, it is its own state and it must be named
    /// (#437, #485 leg c). A three-valued thing modelled as two values is how it stayed invisible.
    type BoardStatus =
        | NoStatus
        | Backlog
        | Ready
        | InProgress
        | Blocked
        | InReview
        | Done

    /// The state of the ISSUE, which is not the state of the board column.
    ///
    /// The board column is a PROJECTION of the work; the issue is the WORK. When they disagree the
    /// issue wins. Keeping them as separate types is the whole of #520: candidate selection read the
    /// column and nothing else, so a CLOSED issue whose column was never flipped stayed schedulable
    /// forever, and one was handed to a worker two hours after it was closed as completed.
    type IssueState =
        | Open
        | Closed

    /// The state of a thing a `Blocked by` edge points AT — which may be a pull request.
    ///
    /// `Merged` is not pedantry, it is the bug. A PR's state is OPEN | CLOSED | MERGED, so a rule that
    /// clears only on CLOSED unblocks when the PR is ABANDONED and blocks forever once it is
    /// FINISHED: the gate opens precisely when the blocking work is thrown away, and shuts precisely
    /// when it is done (#476).
    ///
    /// `Unknown` and `Unparseable` are the fail-open cases, made unforgettable. A blocker we could not
    /// resolve is NOT a blocker we cleared.
    type BlockerState =
        | BlockerOpen
        | BlockerClosed
        | BlockerMerged
        /// The ref parsed, but we could not learn its state. "I could not look" is not "I looked and
        /// it is fine" (#266, #421).
        | BlockerUnknown
        /// The `Blocked by` text is not an issue ref at all. Prose in a dependency field blocks.
        | BlockerUnparseable

    /// A reference to another item, canonicalised. `Blocked by` is free TEXT only because Projects v2
    /// has no typed dependency field — so the type has to be recovered here.
    type Ref =
        { Owner: string
          Repo: string
          Number: int }

        /// The compact repo-qualified spelling. Deliberately omits the owner.
        member Short: string

        /// The unambiguous owner/repo-qualified spelling.
        member Canonical: string

    /// A `Blocked by` entry.
    ///
    /// `Ref` is an OPTION because `BlockerUnparseable` is a real case and prose is not a ref: "Blocked by
    /// RESOLVED: shipped last week" blocks, and it has no owner, no repo and no number. The record used to
    /// demand a `Ref` anyway — so the one state the type was told to expect was the one it could not hold,
    /// and the client silently dropped every such blocker rather than fail to build one. An item bash
    /// called BLOCKED then arrived here unblocked — and the engine's answer is the one a worker acts on,
    /// so that is a worker being handed blocked work.
    ///
    /// `Raw` is what the field actually SAID, and it is always present.
    type Blocker =
        { Ref: Ref option
          Raw: string
          State: BlockerState }

        /// The canonical ref when we have one, else the prose we were given.
        member Display: string

    /// A single `Paths:` token. NOT a glob.
    ///
    /// A token matches by exact equality or subtree containment; the only wildcard is a TRAILING `/**`
    /// or `/*`. A leading `**/`, or a `*` in the middle, matches nothing — and a token that matches
    /// nothing CONFLICTS WITH NOTHING, so it would read as DISJOINT against every other worker. That
    /// is ADR-0021's own failure — a lock that succeeds under exactly the conditions it exists to
    /// prevent — one level down (#273). So an unmatchable token is a distinct case, never a token.
    type PathToken =
        /// A token the matcher can actually reserve.
        | Matchable of string
        /// A token that can never match a file. Reserving nothing, it protects nothing.
        | Unmatchable of string

    /// What an item declares about the files it will touch.
    ///
    /// `DeclaredNone` is the `Paths: none` SENTINEL, and it is not the same fact as `Undeclared`
    /// (#496). Before they were told apart, an epic and a forgotten touch-set rendered identically —
    /// so no gate could be written at all, nine items of real work went invisible to every worker who
    /// asked for work, and the one surface whose job is board health reported `0 error(s)` over a dead
    /// queue. Neither is schedulable. Only one is a bug.
    type TouchSet =
        /// No `Paths:` line at all. An OMISSION — somebody forgot, and the item is unschedulable.
        | Undeclared
        /// `Paths: none` — a decision somebody made. An epic, a decision item, an investigation whose
        /// scope IS the question. Unschedulable BY DESIGN.
        | DeclaredNone
        /// `Paths: any` — a file-less CHORE. Reserves nothing and conflicts with nothing, so it IS
        /// schedulable and runs alongside any concurrent item. The counterpart to `DeclaredNone` that
        /// #1103 leg 8 splits out: both reserve nothing, only this one is schedulable. A deliberate,
        /// parser-verified empty reservation — not #273's fail-open. See ADR-0045.
        | DeclaredChore
        /// A real declaration. May still contain unmatchable tokens — that is a third death, and the
        /// scheduler and the linter must agree about it.
        | Declared of PathToken list
        /// WE DID NOT READ THE BODY. The touch-set is UNKNOWN, which is not the same fact as absent —
        /// and the difference is this whole codebase. Coercing an unread body to `Undeclared` would
        /// report a confident OMISSION about an item nobody looked at, and then schedule every other
        /// item against a surface we cannot see. It yields `Undetermined`: not startable, and said so.
        | Unreadable of reason: string

    /// How BAD an item is (.github#1588) — the axis the board had no vocabulary for.
    ///
    /// What KIND of work an item is: defect, hardening, or a human decision. Severity is a separate,
    /// ordered axis (.github#1901); Class deliberately remains a single-purpose partition.
    ///
    /// THREE CASES, AND THE SPLIT IS THE POINT. `decision` is not authorable work until a human chooses;
    /// `defect` and `hardening` distinguish broken-now work from prevention.
    ///
    /// THERE IS NO `unknown` CASE, DELIBERATELY. An item whose class nobody has established carries
    /// `None`, and the absence is the finding — `lint`'s `CLASS-UNSET` reports it and `drive-board` treats
    /// it as POSSIBLY a defect. An `Unknown` case would be a value a rule could match and dismiss, which
    /// is #266's fail-open wearing this union's clothes: an untriaged class remains loud.
    type ItemClass =
        /// Something is broken NOW: a gate red on `main`, a summary reporting a byte-identity it never
        /// computed, a reusable workflow that dies at load for every caller.
        | Defect
        /// Nothing is broken; the change removes a way it could break. Real work, and never the reason a
        /// burn-down cannot stop — it accumulates as ordinary backlog and is drained deliberately.
        | Hardening
        /// A human must CHOOSE before any work is authorable. Surfaced, never dispatched: driving it
        /// would make the machine answer the question the item exists to escalate.
        | Decision

    /// The board's closed, ordered severity vocabulary (.github#1901).
    type Severity =
        | Critical
        | High
        | Medium
        | Low
        /// Explicitly untriaged. Ranks last and must remain a lint finding.
        | Unset

    /// WHICH PART OF THE PLAN an item belongs to — the board's `Phase` column (.github#1598).
    ///
    /// A CLOSED, ORDERED vocabulary, and the ORDER is why this is a union rather than the string the
    /// column holds. "Earlier in the plan" is a rank input, and a string carries no order: sorted
    /// lexically, a tenth phase would sort between `P1` and `P2` the day it existed.
    ///
    /// The values are the live board options, documented in the `repo-phase-map` table of
    /// `docs/coordination/board-schema.md`. `P0 Decisions` and `P5 Versioning` are board-wide; every other
    /// phase is the home of exactly one rostered product repo.
    ///
    /// THERE IS NO `unphased` CASE, on `ItemClass`'s terms: an item whose column is empty carries `None`,
    /// and `None` ranks LAST. A case meaning "no phase" would be a value a comparison could match, and the
    /// only safe answer for an unknown priority is the lowest one.
    type Phase =
        | P0Decisions
        | P1Rendering
        | P2Sdd
        | P3Governance
        | P4Templates
        | P5Versioning
        | P6Game
        | P7Audio
        | P8Net

    /// A live claim on an item.
    type Claim =
        { Worker: WorkerId
          Session: SessionId option
          /// Seconds since the marker was last heartbeated.
          AgeSeconds: int
          /// The board column this claim OVERWROTE, so that releasing it can put that column back
          /// rather than guessing `Ready` (#481). A value nobody recorded cannot be restored.
          PreviousStatus: BoardStatus option }

    /// Whether the WORK is alive — which is not the same question as whether the LEASE is.
    ///
    /// Lease expiry is EVIDENCE of abandonment, never proof, and it has a systematic false positive:
    /// work that takes longer than the lease. An open PR on the item's own `item/<n>-*` branch is the
    /// worktree protocol's own artifact and is server-side PROOF OF LIFE (#581). This type exists so
    /// that a caller cannot decide "abandoned" from the lease alone — it has to say which it saw.
    type Liveness =
        /// The lease is current.
        | LeaseHeld
        /// The lease lapsed AND no open PR was found. Only now is abandonment a reasonable reading.
        | LeaseExpiredNoPr
        /// The lease lapsed but an `item/<n>-*` PR is open. The worker is demonstrably still working.
        | LeaseExpiredPrOpen of pr: int
        /// The lease lapsed, no PR is open YET, but a pushed `item/<n>-*` BRANCH exists — the worktree
        /// protocol's own artifact, and proof of life BEFORE §5 opens the PR (#1055/#581). This is the
        /// interval a REST outage strands a worker in: §5 opens the PR only after the work, so during §3
        /// the branch is the only artifact, and `heartbeat` (REST) cannot renew the lease through the same
        /// outage that is expiring it. Weaker evidence than an open PR — a branch can be a stale leftover —
        /// so it withholds and REFUSES a reap without asserting the work is as demonstrably alive as a PR.
        /// NOT `LeaseExpiredNoPr`, and NOT reapable.
        | LeaseExpiredBranchPushed
        /// We could not ask. NOT the same as "no PR" — and this is the case that reaped live work.
        | LivenessUnknown

    /// Whether an open PR is FINISHED WORK ready to land — the #697/#720 verdict `who`/`reap`/`adopt` speak.
    ///
    /// `Liveness` answers WHETHER a PR exists; this answers WHAT IT SAYS. The distinction is the whole of
    /// #697: a stale claim whose PR is green and mergeable is not an abandoned branch to reap — it is a
    /// worker's FINISHED work, minutes from merge, that `reap`'s old "close it, then reap" would have
    /// destroyed. The verdict tells `reap` which refusal to speak and `who` which flag to fly.
    ///
    /// A MISSING SUBJECT IS A FINDING, NOT A PASS (#606): a mergeable PR with zero (live) checks is `PrRed`,
    /// never `PrGreen` — "every check passed" and "CI never started" are the same empty set, and a
    /// conflicted PR is permanently in it (GitHub builds no `refs/pull/N/merge` while it conflicts).
    type PrState =
        /// Mergeable AND every live check passed — and there IS at least one. Finished work; land it.
        | PrGreen
        /// Not mergeable — a conflict. Rebasing it is authoring, not landing, and it gets no CI at all.
        | PrConflicted
        /// Mergeable, checks exist, at least one is still running. "Not green YET" is not "not green".
        | PrPending
        /// Mergeable but a live check failed, OR there are zero checks at all (#606). Not finished work.
        | PrRed
        /// We could not tell — an unreadable read, or `mergeable` still null. Fail closed: advise nothing.
        | PrUnknown
        /// ALREADY LANDED — the PR is closed and `merged` is true. There is nothing to gate (#1680).
        ///
        /// The five states above are all answers to "is this OPEN PR finished work?", and a merged PR is
        /// outside that question rather than a hard case within it. Before this case existed the scorer had
        /// to answer it in a vocabulary with no word for it, and what came out was `PrPending` — because
        /// GitHub reports `mergeable: null` for a merged PR (measured on #1675), which `Reads` reads as
        /// `Computing` and #950 maps to "not yet". So the ONE verdict the contract defines as worth retrying
        /// was returned for the MOST terminal state there is, and `--wait` spent its whole 600s budget on it.
        ///
        /// It matters because the caller is usually the RECOVERY path: `pnext-item` and `adopt` exist for a
        /// worker that died between *merge* and *stamp*, and they instruct the successor to re-gate with
        /// `landable` first. The correct instruction to that successor is "this is merged — go stamp it".
        | PrMerged
        /// CLOSED WITHOUT MERGING — abandoned or rejected. Also terminal, and NOT the same as `PrMerged`
        /// (#1680 AC4): both mean "there is nothing left to gate", but the next act is opposite. A merged PR
        /// is finished work awaiting its stamp; this one must NOT be stamped, because nothing landed. It is
        /// the neighbouring case on the same recovery path, so it is decided here rather than left to
        /// whichever of the five wrong words it would otherwise fall into.
        | PrClosed

    /// A `Blocked on: human/...` body-line sentinel (#1103 leg 2): the item is unschedulable because a
    /// HUMAN must act first, whatever its `Paths:` line declares. `Blocked by` is ref-typed and cannot say
    /// "blocked on a person", so the empty-`Blocked by` park collapsed action and decision into one
    /// rendering; this carries the distinction that flattening lost. Decided by @EHotwagner on #1103.
    type HumanBlock =
        /// `Blocked on: human/decision` — unstartable until a human CHOOSES (a decision item).
        | AwaitingHumanDecision
        /// `Blocked on: human/action` — blocked on a human ACTION (e.g. a scope grant); startable once it
        /// lands, not before.
        | AwaitingHumanAction

    /// An item, as the scheduler must see it.
    type Item =
        { Ref: Ref
          /// The repository whose tree the item's `Paths:` tokens name.  `Ref` remains the issue to
          /// claim and close; `PathRepo` is the independent reservation scope (#1732).
          PathRepo: string
          Status: BoardStatus
          /// The ISSUE's state. Separate from Status on purpose — see IssueState.
          State: IssueState
          TouchSet: TouchSet
          Blockers: Blocker list
          /// The live claim, if any — plus what we know about whether its work is alive.
          Claim: (Claim * Liveness) option
          /// The open `item/<n>-*` PR when this item carries NO live-held claim marker — a duplicate
          /// implementation already in flight (#651). `None` when there is no such PR, or when a claim
          /// marker already governs liveness (there the open PR is the claim's `LeaseExpiredPrOpen`).
          ItemPr: int option
          /// `true` when the markerless item-PR probe could not be completed. `ItemPr = None` alone
          /// never carries that fact; the mutation path therefore reads this receipt and fails closed.
          ItemPrUnreadable: bool
          /// A `Blocked on: human/...` sentinel parsed from the body (#1103 leg 2). `None` when the item
          /// declares no such line. When present, it refuses scheduling regardless of `TouchSet`.
          ///
          /// TWO READERS, NOT ONE (.github#1644). `Schedulability` step 3b refuses to HAND the item out, and
          /// `Chore`'s `BLOCKER-CLEARED` refuses to PROMOTE it — a scheduler that withholds a row while a
          /// mechanical chore writes `Ready` onto it is one mechanism arguing with the other, and the write
          /// wins because it is what the board then shows everybody.
          ///
          /// `None` IS AMBIGUOUS AND ITS READERS MUST TREAT IT SO. It means "declares no sentinel" and "the
          /// body was never read" alike, because this option has nowhere to put the second. The
          /// disambiguating fact is `TouchSet.Unreadable` on the same item, off the same body — which is why
          /// the chore gate consults it and `Client.enrichBoardFacts` consults it for `Class`.
          HumanBlock: HumanBlock option
          /// The item's declared machine-checkable registry predicate, ALREADY RESOLVED against the owning
          /// producer's manifest (ADR-0050 call-site B / .github#1203). This is a FACT on the item, the
          /// way `Blocker.State` is — resolved at the impure edge (the Cli reads `registry/skills.yml` and
          /// the owner checkout), never by `Chore.derive`, which stays pure and cannot mistake a failed
          /// read for a verdict (#266).
          ///
          /// `None` is the common case: the item declares NO machine-checkable predicate (its body carries
          /// no `RegistryPredicate` assertion), so the `BLOCKER-CLEARED` flip is not gated on one and fires
          /// on blockers-cleared exactly as today (ADR-0050 boundary decision 5 — a general prose predicate
          /// is not mechanically evaluable, and inventing one is out of scope).
          ///
          /// When `Some`, the resolved verdict gates the flip: `Agrees` lets it proceed; `Contradicts` and
          /// `Unknown` HOLD the item `Blocked` — fail closed, exactly as one `BlockerUnknown` already keeps
          /// `BLOCKER-CLEARED` from firing (#266, #421). "Could not evaluate the predicate" is not "the
          /// predicate holds."
          Predicate: RegistryPredicate.Verdict option

          /// **WHAT THE ITEM'S OWN TEXT SAYS IT IS** — a `Class:` body line, a `[decision]` title prefix,
          /// or ADR-0045's `Blocked on: human/decision` sentinel (.github#1588, ADR-0066).
          ///
          /// `None` is a REAL answer and the common one today: the item's text carries no evidence of its
          /// severity. It is never a default class. A default here would be #266's fail-open one axis
          /// over — a row nobody triaged reading as a row that is fine — and it is the exact failure
          /// #1588's AC3 names when it says "derived or reported, never guessed".
          Class: ItemClass option

          /// **WHAT THE BOARD COLUMN CURRENTLY RENDERS** — the projection, observed, never derived.
          ///
          /// TWO FIELDS, BECAUSE THEY ARE TWO FACTS, and the reconciliation lives in the gap between them.
          /// `Class` is the item's claim about itself; this is what the board says today;
          /// `CLASS-PROJECTION-LAG` is derived exactly where they disagree. Collapsed into one field the
          /// disagreement would be inexpressible, so nothing could reconcile it and the chore could never
          /// retire — `Chore.isRetired` would answer "still owed" forever against a write that landed.
          ///
          /// Resolved at the impure edge (`Scan` reads the column), never by `Chore.derive`, on exactly
          /// `Predicate`'s terms: the pure derivation reads FACTS and cannot mistake a failed read for a
          /// verdict. A context that never resolves it keeps `None` and derives no projection chore,
          /// which is the fail-closed answer — a projection we could not observe is not one we may write.
          BoardClass: ItemClass option

          /// The mandatory, source-bound delivery-route verdict observed for this item. Missing, stale,
          /// or unreadable evidence is a scheduling hold, never an inferred lightweight route.
          DeliveryRoute: DeliveryRoute.Verdict

          /// The observed `Severity` column. Missing/unrecognised values are represented as `Unset`.
          Severity: Severity

          /// **WHAT THE BOARD'S `Phase` COLUMN CURRENTLY RENDERS** (.github#1598) — observed, never derived.
          ///
          /// The board has carried `Phase` since the project existed and NOTHING IN THE SCHEDULER READ IT:
          /// `batch` and `take` sorted by issue number, so the only lever a driver had for priority was
          /// `Status`, and on 2026-07-27 that meant parking nineteen items in `Backlog` to move five to the
          /// front. This field is what makes the column mean something to a scheduler.
          ///
          /// Resolved at the impure edge on `BoardClass`'s exact terms — the scan reads the column, the
          /// pure core never does. `None` means this reader did not look, or the row genuinely has no
          /// phase, and the two collapse deliberately: both mean "no priority evidence here", and
          /// `Rank.ofItem` sorts such an item LAST. That is the direction that cannot hurt — an unread
          /// column deprioritises an item, it never promotes one over work somebody did classify.
          Phase: Phase option

          /// **HOW MANY WHOLE DAYS AGO THE ISSUE WAS CREATED**, measured at the impure edge (.github#1598).
          ///
          /// An `int` rather than a timestamp, for exactly the reason `Claim.AgeSeconds` is one: the pure
          /// core owns no clock, so a rank computed here is reproducible from its inputs and a test can
          /// state an age instead of mocking a clock.
          ///
          /// It is TWO rank inputs at once, and they pull the same way: the stable oldest-first tie-break,
          /// and the starvation escalation that lifts a long-displaced item above the class and phase terms
          /// entirely (`Rank.StarvationDays`).
          ///
          /// `None` means the reader did not learn it — never "created just now". A zero default would make
          /// every unread age the YOUNGEST, which is the one reading that can never trigger escalation: an
          /// item starving because nothing could read its timestamp would starve silently forever.
          AgeDays: int option }

    /// A three-valued verdict. There is no `bool` in this domain.
    ///
    /// `NoVerdict` is the whole reason this type exists, and it must be NON-ZERO at every exit. The
    /// org evolved this rule the hard way — a non-vacuity guard plus `exit 3` — and enforces it by
    /// CONVENTION across a dozen scripts. It is a type. This is it.
    type Verdict<'a> =
        | Green of 'a
        | Red of reasons: string list
        /// "I could not reach an answer." Never green, never silently dropped.
        | NoVerdict of reason: string

    /// **THE WIRE VOCABULARY: a `BoardStatus` as the Projects v2 OPTION NAME, spelled ONCE.**
    ///
    /// This is the string the board itself round-trips — what `Scan` reads a column back as, and what
    /// `Writes` sends when it sets one. `NoStatus` renders as the EMPTY STRING, because on the wire an
    /// unset column really is empty; that is the wire being honest, not a missing case.
    ///
    /// It exists because this rendering was FOUR private `statusName` functions — `Scan`, `Writes`,
    /// `Client`, `Snapshot` — and `Chore.fs` already named all four in a comment explaining why its own
    /// prose label is correctly a fifth, DIFFERENT thing. The author knew the count; the comment
    /// documented the duplication without closing it. All four agreed, and agreement is the finding
    /// rather than the reassurance: #916 measured a `take` exit table whose copies "agreed with each
    /// other the whole time" and were wrong in three ways, and #972 is this identical class filed as a
    /// bug — "three private fence trackers in one engine, agreeing in none". Four agreeing copies are
    /// one careless edit from being #972 (#983, #485).
    ///
    /// **THIS IS NOT THE PROSE VOCABULARY, AND THE TWO MUST NOT BE COLLAPSED.** There are deliberately
    /// two, and they differ on exactly one case — `NoStatus`:
    ///
    /// - **wire** (here): `NoStatus` → `""`. What the board stores.
    /// - **prose** (`Schedulability.statusText`, `Chore.columnLabel`): `NoStatus` → `"no Status"` /
    ///   `"no Status at all"`. A sentence reading *"the board column says "* is its own bug (#437), so
    ///   in prose the case is NAMED.
    ///
    /// Merging them would reintroduce #437 in one direction or put `"no Status at all"` on the wire in
    /// the other. Read `Chore.fs`'s comment before touching either.
    val statusWireName: s: BoardStatus -> string

    /// **THE BLOCKER WIRE VOCABULARY: a `BlockerState` as the scan's JSON spells it, spelled ONCE.**
    ///
    /// This is the string `scan` emits and `Snapshot.parse` reads back — and, because `check-board` §3
    /// selects on `.state` in `jq` rather than parsing, it is also the string a RECONCILER matches
    /// against. Lower case, deliberately: an issue's own `state` is upper case on the wire and a
    /// blocker's is not, and those two conventions are not to be "unified" (`check-board` says so).
    ///
    /// It exists because this vocabulary was TWO private copies pointing in OPPOSITE directions —
    /// `Scan.blockerStateName` rendered it, `Snapshot.blockerState` parsed it — in two different
    /// projects, with nothing asserting they were inverse. That is worse than #983's four copies, which
    /// at least all faced the same way: a render/parse pair that disagrees is not a stale doc, it is an
    /// engine that cannot read what it just wrote. MEASURED: `merged` -> `"MERGED"` in the renderer left
    /// **775 tests green** (#1012), because `ScanRoundTripTests` exercised exactly one case and both
    /// functions were `private`, so nothing could reach them.
    ///
    /// The bite is a SILENT FALSE CLEAN in `check-board` — its worst output by its own account. A
    /// rendered `"MERGED"` matches no `jq` selector, every merged blocker reads as still-holding,
    /// `BLOCKER-CLEARED` never fires, and items rot in `Blocked` behind work that shipped. That is #476
    /// exactly: the gate shuts precisely when the blocking work is FINISHED.
    ///
    /// **THIS IS NOT THE PROSE VOCABULARY** — `Schedulability.blockerText` is, on the same terms
    /// `statusWireName` is not `Schedulability.statusText`. Read that note above before touching either.
    val blockerStateWireName: s: BlockerState -> string

    /// The INVERSE, and it is DERIVED from `blockerStateWireName` rather than restating it — so the
    /// vocabulary is spelled exactly once in this engine and the pair cannot drift.
    ///
    /// `None` means the string is not a blocker state at all. It is deliberately not `BlockerUnknown`:
    /// that case means "the ref parsed and we could not learn its state", which is a fact about the
    /// BLOCKER. A wire string we cannot read is a fact about the DOCUMENT, and collapsing the two would
    /// let a corrupt snapshot read as a legitimately-unreadable blocker — a parse failure wearing a
    /// verdict's clothes (#266). The caller decides how to fail; `Snapshot` reports it as a parse error
    /// against the JSON path, which is what it did before this function existed.
    val blockerStateOfWireName: s: string -> BlockerState option

    /// **THE CLASS WIRE VOCABULARY: an `ItemClass` as the Projects v2 OPTION NAME, spelled ONCE.**
    ///
    /// THIS ONE STRING IS THREE WIRES AT BIRTH, which is why the pair below exists on day one rather than
    /// after it drifts: it is the board's single-select option name, the value a filer writes in a
    /// `Class:` body line, and the word the options table in `docs/coordination/board-schema.md`
    /// documents. #983 counted four agreeing copies of `statusWireName` and called the agreement the
    /// finding; #1012 measured the render/parse pair that did NOT agree and left 775 tests green while
    /// every merged blocker read as still-holding. Three wires from one function is the answer to both.
    ///
    /// A TOTAL match, no wildcard, on `statusWireName`'s terms. There is no empty case: an item with no
    /// class is `None`, and the absence of a class is not a class meaning absence.
    val itemClassWireName: c: ItemClass -> string

    /// The INVERSE, DERIVED from `itemClassWireName` rather than restating it — so the vocabulary is
    /// spelled exactly once and the board field, the body line and the docs cannot disagree.
    ///
    /// This is the function `Class.fromBody` calls, which is what makes the grammar a filer may write
    /// identical to what `reconcile` writes onto the board. A separate parse list would let the docs
    /// teach a word the writer never emits.
    ///
    /// `None` means the string is not a class at all — NOT a default. Resolving an unrecognised word onto
    /// the nearest of three would be a guess carrying a parser's authority, which is exactly what
    /// .github#1588's AC3 forbids; `lint` reports the item as unclassed instead, and a human decides.
    val itemClassOfWireName: s: string -> ItemClass option

    /// Render a severity as the exact Projects v2 option name.
    val severityWireName: severity: Severity -> string

    /// Parse an exact severity option name, case-insensitively. Unknown words are not severity values.
    val severityOfWireName: s: string -> Severity option

    /// The priority order. Lower sorts earlier; `Unset` is last.
    val severityOrder: severity: Severity -> int

    /// **THE PHASE WIRE VOCABULARY: a `Phase` as the Projects v2 OPTION NAME, spelled ONCE.**
    ///
    /// Two wires, not three — unlike `Class` there is no body-line grammar, because `Phase` is a board
    /// column a human sets and no item text declares. The two are the live single-select option name and
    /// the `repo-phase-map` table in `docs/coordination/board-schema.md`.
    ///
    /// A TOTAL match, no wildcard, on `statusWireName`'s terms: a tenth phase must fail the BUILD rather
    /// than render as "" and rank as unphased.
    val phaseWireName: p: Phase -> string

    /// **THE PLAN ORDER, and the only place a `Phase` is a number.**
    ///
    /// Not derived from the `P<n>` prefix of the wire name. That prefix is a rendering convention, not a
    /// contract, and parsing it back would answer 0 — the HIGHEST priority — for any phase ever named
    /// without one. A total match cannot fail that way; it fails the build instead.
    val phaseOrder: p: Phase -> int

    /// The INVERSE, DERIVED from `phaseWireName` (#1012) — case- and space-forgiving on input, exact on
    /// output, exactly as `itemClassOfWireName` is.
    ///
    /// `None` means the string is not a phase this engine speaks, and it is emphatically not `P0Decisions`:
    /// resolving an unknown column value onto the phase that outranks every other would make a typo the
    /// highest-priority work on the board. An unphased item ranks LAST (`Rank.of`), which is the direction
    /// that cannot hurt.
    val phaseOfWireName: s: string -> Phase option
