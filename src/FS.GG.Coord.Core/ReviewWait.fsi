namespace FS.GG.Coord

/// Durable, bounded state for protocol-created review queues (.github#2756).
module ReviewWait =
    open System

    [<Literal>]
    val Marker: string = "<!-- fsgg:review-wait/v1 -->"

    type Kind = InitialReview | RepairConfirmation

    type WaitReceipt =
        { Item: string
          ClaimGeneration: string
          ReviewGeneration: string
          Kind: Kind
          EnteredAt: DateTimeOffset
          ExpiresAt: DateTimeOffset
          EvidenceRef: string }

    type Transition =
        | Enter of WaitReceipt
        | Complete of reviewGeneration: string * at: DateTimeOffset * evidenceRef: string
        | Cancel of reviewGeneration: string * at: DateTimeOffset * evidenceRef: string
        | Timeout of reviewGeneration: string * at: DateTimeOffset * evidenceRef: string

    type State =
        | NoReceipt
        | Waiting of WaitReceipt
        | Completed of WaitReceipt * evidenceRef: string
        | Cancelled of WaitReceipt * evidenceRef: string
        | Recoverable of WaitReceipt * reason: string
        | Invalid of errors: string list

    val validate: Transition -> Result<Transition, string list>
    /// Canonical structured-review generation token bound to the exact head, queue kind and round.
    val generationToken: head: string -> kind: Kind -> round: int -> string
    val encode: Transition -> string
    val tryDecode: string -> Result<Transition option, string>
    val project:
        item: string ->
        currentClaimGeneration: string option ->
        prOpen: bool ->
        now: DateTimeOffset ->
        events: Transition list -> State
