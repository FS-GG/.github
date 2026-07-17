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

        member Short: string

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
        /// A real declaration. May still contain unmatchable tokens — that is a third death, and the
        /// scheduler and the linter must agree about it.
        | Declared of PathToken list
        /// WE DID NOT READ THE BODY. The touch-set is UNKNOWN, which is not the same fact as absent —
        /// and the difference is this whole codebase. Coercing an unread body to `Undeclared` would
        /// report a confident OMISSION about an item nobody looked at, and then schedule every other
        /// item against a surface we cannot see. It yields `Undetermined`: not startable, and said so.
        | Unreadable of reason: string

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

    /// An item, as the scheduler must see it.
    type Item =
        { Ref: Ref
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
          ItemPr: int option }

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
