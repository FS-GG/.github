namespace FS.GG.Coord

/// Pure retained intake effect contract. Journal storage, CAS, reads, and native issue creation live outside Core.
module IntakeTransaction =
    [<Literal>]
    val AggregateId: string = "intake-transactions:fs-gg-production"
    [<Literal>]
    val JournalRef: string = "refs/heads/fsgg/v2/journal/operation/13"
    [<Literal>]
    val Schema: string = "fsgg.coord.intake-transaction/v1"
    [<Literal>]
    val MaxEventBytes: int = 65536

    type Identity = { RepositoryId: int64; RepositoryNodeId: string; DraftId: string }
    type Binding =
        { Identity: Identity
          Owner: string
          Repository: string
          DraftDigest: string
          CompatibleDigests: string list
          RequestSha256: string
          RequestBytes: byte[] }
    type NativeIssue = { RepositoryId: int64; NodeId: string; Number: int; Url: string }
    type Event =
        | Intent of Binding
        | InFlight of Binding
        | Unknown of Binding
        | Bound of Binding * NativeIssue * IntakeReceipt.Receipt
    type State =
        { Phase: string
          Binding: Binding
          Issue: NativeIssue option
          Receipt: IntakeReceipt.Receipt option }

    /// Domain-separated, length-framed immutable target and exact case-sensitive draft ID.
    val key: Identity -> Result<string, string>
    /// Bind the exact request bytes and the finite, explicit legacy digest vocabulary before dispatch.
    val prepareIntent: Identity -> Intake.Draft -> byte[] -> Result<Event, string>
    /// Apply one canonical transition. Unknown remains InFlight; Bound is terminal and replay never creates.
    val apply: State option -> Event -> Result<State, string>
    /// Canonical, bounded JSON envelope, including aggregate/ref/schema.
    val encodeEvent: Event -> Result<byte[], string>
    /// Refuse duplicate keys, oversized or noncanonical JSON, and incompatible envelope identities.
    val decodeEvent: byte[] -> Result<Event, string>
