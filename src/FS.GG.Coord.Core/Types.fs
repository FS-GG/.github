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

    type Blocker = { Ref: Ref; State: BlockerState }

    type PathToken =
        | Matchable of string
        | Unmatchable of string

    type TouchSet =
        | Undeclared
        | DeclaredNone
        | Declared of PathToken list

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

    type Item =
        { Ref: Ref
          Status: BoardStatus
          State: IssueState
          TouchSet: TouchSet
          Blockers: Blocker list
          Claim: (Claim * Liveness) option }

    type Verdict<'a> =
        | Green of 'a
        | Red of reasons: string list
        | NoVerdict of reason: string
