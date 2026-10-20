namespace FS.GG.Coord

/// A read-only attention reducer for one already admitted original CI operation.
module OperationObservation =
    open System

    type Identity =
        { Issue: string; Slice: string; Attempt: string; Owner: string
          SourceRevision: string; IssueUpdatedAt: string; IssueBodySha256: string; Repository: string; PullRequest: int; Head: string
          RunId: int64; RunAttempt: int; PopulationRevision: string; Unknowns: string list }

    type Budget =
        { ClockDomain: string; Started: int64; WorkDeadline: int64; CleanupDeadline: int64
          LastClock: int64; RequestsRemaining: int; OutputBytesRemaining: int }

    type Check =
        { Id: int64; Name: string; Status: string; Conclusion: string option
          StartedAt: string option; CompletedAt: string option }

    type Sample =
        { Head: string; RunId: int64; RunAttempt: int; RunStatus: string
          RunConclusion: string option; PullRequestState: string; Merged: bool; Draft: bool
          Checks: Check list; Complete: bool; Gaps: string list }

    type Attention =
        | Completed | Failed | PullRequestClosed | HeadChanged | RunChanged
        | PopulationChanged | MaterialChange | Unknown | Deadline | BudgetExhausted
        | Interrupted | ObservationFailure

    type Cursor =
        { Identity: Identity; Budget: Budget; Population: string option; Semantic: string option; Control: string option
          Revision: int; Terminal: Attention option; Evidence: string option; Gap: string option }

    type Decision =
        | ObserveOriginal of Cursor
        | AwaitWithinDeadline of Cursor * milliseconds: int
        | ReturnAttention of Cursor * Attention * terminalIdentity: string

    val validateIdentity: Identity -> Result<Identity, string>
    val start: Identity -> domain: string -> now: int64 -> seconds: int -> Result<Cursor, string>
    val resume: expected: Identity -> domain: string -> now: int64 -> Cursor -> Result<Cursor, string>
    val attentionName: Attention -> string
    val finish: Attention -> Cursor -> Decision
    val decide: now: int64 -> Sample option -> Cursor -> Decision
    /// Charge before issuing a request; a crash can lose allowance, never replenish it.
    val chargeRequest: now: int64 -> Cursor -> Result<Cursor, string>
    val chargeOutput: bytes: int -> Cursor -> Result<Cursor, string>
