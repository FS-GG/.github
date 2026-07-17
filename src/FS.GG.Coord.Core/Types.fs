namespace FS.GG.Coord

module Types =

    type WorkerId =
        | WorkerId of string

        member this.Value =
            let (WorkerId v) = this
            v

    type SessionId =
        | SessionId of string

        member this.Value =
            let (SessionId v) = this
            v

    type BoardStatus =
        | NoStatus
        | Backlog
        | Ready
        | InProgress
        | Blocked
        | InReview
        | Done

    type IssueState =
        | Open
        | Closed

    type BlockerState =
        | BlockerOpen
        | BlockerClosed
        | BlockerMerged
        | BlockerUnknown
        | BlockerUnparseable

    type Ref =
        { Owner: string
          Repo: string
          Number: int }

        member this.Short = $"%s{this.Repo}#%d{this.Number}"

    /// A `Blocked by` entry.
    ///
    /// `Ref` is an OPTION because `BlockerUnparseable` is a real case and prose is not a ref: "Blocked by
    /// RESOLVED: shipped last week" blocks, and it has no owner, no repo and no number. The record used to
    /// demand a `Ref` anyway — so the one state the type system was told to expect was the one it could not
    /// hold, and the client quietly dropped every such blocker on the floor rather than fail to build one.
    /// An item bash called BLOCKED then reached the engine as unblocked — and the engine's answer is the
    /// one a worker acts on, so that is a worker being handed blocked work.
    ///
    /// `Raw` is what the field actually SAID, and it is always present — it is the only thing there is to
    /// show a human when the ref did not parse.
    type Blocker =
        { Ref: Ref option
          Raw: string
          State: BlockerState }

        /// What to call it in a sentence: the canonical ref when we have one, else the prose we were given.
        member this.Display =
            match this.Ref with
            | Some r -> r.Short
            | None -> this.Raw

    type PathToken =
        | Matchable of string
        | Unmatchable of string

    type TouchSet =
        | Undeclared
        | DeclaredNone
        | Declared of PathToken list
        /// The body was never read, so the touch-set is UNKNOWN — not absent. See Types.fsi.
        | Unreadable of reason: string

    type Claim =
        { Worker: WorkerId
          Session: SessionId option
          AgeSeconds: int
          PreviousStatus: BoardStatus option }

    type Liveness =
        | LeaseHeld
        | LeaseExpiredNoPr
        | LeaseExpiredPrOpen of pr: int
        | LivenessUnknown

    type PrState =
        | PrGreen
        | PrConflicted
        | PrPending
        | PrRed
        | PrUnknown

    type Item =
        { Ref: Ref
          Status: BoardStatus
          State: IssueState
          TouchSet: TouchSet
          Blockers: Blocker list
          Claim: (Claim * Liveness) option
          /// The open `item/<n>-*` PR when this item carries NO live-held claim marker — a duplicate
          /// implementation already in flight (#651). `None` when there is no such PR, or when a claim
          /// marker already governs liveness (there the open PR is the claim's `LeaseExpiredPrOpen`).
          ItemPr: int option }

    type Verdict<'a> =
        | Green of 'a
        | Red of reasons: string list
        | NoVerdict of reason: string
