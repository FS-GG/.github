namespace FS.GG.Coord.GitHub

/// Proposals to #4985 for board-v2 tend (FS-GG/.github#5136 phase 2 PR 5; design §6 "Proposals to #4985").
///
/// The drift observer stays the only writer of the #4985 body; tend adds append-only comments. Each proposal
/// has a stable ID `P:<rule>:<subject>:<reason>`, whose subject is the public reference or the observer's
/// `private:<12 hex HMAC>` placeholder for a private or unknown-visibility repository. A ledger maps each ID to
/// the comment that first carried it and its state (live or cleared). A run posts one comment with only the
/// IDs never posted before; a cleared ID that fires again is marked live but never reposted. The run and its
/// IDs are recorded before the post; an uncertain post is never repeated, and the next run finds it by its run
/// marker among Root's comments created after the custody time. No comment is ever selected by recency or
/// edited.
module V2TendPropose =
    open System
    open Transport

    val ledgerSchema: string

    /// The observer's placeholder: `private:` and the first 12 hex digits of HMAC-SHA256(key, raw reference).
    val placeholder: key: byte[] -> raw: string -> string

    type Entry =
        { Id: string
          Rule: string
          Subject: string
          Reason: string
          /// Published only when it is a Status name.
          Suggested: string option }

    /// The plan's proposals through the visibility gate; `isPublic repo` is true only for an explicit public repo.
    val entries: key: byte[] -> isPublic: (string -> bool) -> proposals: V2Tend.Proposal list -> Entry list

    /// Repository visibility in one GraphQL request: true only for an explicit `isPrivate: false`.
    val readVisibility: transport: IGitHubTransport -> repos: string list -> Result<Map<string, bool>, string>

    type LedgerEntry = { State: string; Comment: string option; FirstPosted: string option; LastSeen: string }

    type Pending = { Run: string; RecordedAt: DateTimeOffset; Ids: string list }

    type Ledger = { Ids: Map<string, LedgerEntry>; Pending: Pending option }

    val emptyLedger: Ledger
    val decodeLedger: json: string -> Result<Ledger, string>
    val encodeLedger: ledger: Ledger -> string

    /// The ledger after observing `current`, and the entries never posted (nor pending) before.
    val reconcile: ledger: Ledger -> current: Entry list -> now: DateTimeOffset -> Ledger * Entry list

    /// The comment: the run marker, a header line and one table row per new ID.
    val commentBody: run: string -> fresh: Entry list -> string

    /// One proposals run under the writer lock (see the module summary). Without a key of 16 bytes or more
    /// nothing is posted and the ledger is untouched.
    val run:
        context: V2TendWrite.Context -> syncDirectory: (string -> unit) -> ledgerPath: string -> key: byte[] option ->
        proposals: V2Tend.Proposal list -> V2TendWrite.Outcome list
