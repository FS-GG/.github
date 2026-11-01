namespace FS.GG.Coord.GitHub

/// Batched read-only reads for `board-v2 tend` (FS-GG/.github#5136 phase 2 PR 3; design §2).
///
/// A run reads (A) the board items with their field values and issue facts, 50 per page; (B) the open pull
/// requests of the organization, 100 per page; and (C) for every open issue card, its cross-reference
/// timeline (last 25) and recent comments (last 50), 25 issues aliased per request. Every request is a
/// GraphQL query; the transport is wrapped so that no mutation can be sent. A board over 500 items is
/// refused on the first page, and a truncated connection is reported as incomplete, never as empty.
///
/// Placeholders wired before PR 5 and PR 8 of the design: `RootLogins` is the one Root login the CLI
/// passes until the board-currency `rootLogins` parameter is read; `Ledger` is empty until the state
/// directory (PR 8); `Enrolled` is empty until an enrollment binding is supplied, so RM1 is inert and
/// P-READY reports cards as unenrolled.
module V2TendReads =
    open System
    open Transport

    /// Requests sent, GraphQL points reported by `rateLimit.cost`, and REST calls (always zero here).
    type Cost = { Requests: int; GraphQlPoints: int; RestCalls: int }

    type Inputs =
        { Owner: string
          ProjectNumber: int
          /// Logins whose comments and authorship count as Root's (the board-currency `rootLogins`).
          RootLogins: Set<string>
          ObservedAt: DateTimeOffset
          Ledger: V2Tend.Deferral list
          /// Enrolled issue references with their enrolled Roadmap; empty when no binding is supplied.
          Enrolled: Map<string, string option> }

    /// What `linkage` needs of one board card. A pull-request or draft card is absent: never a Delivery.
    type LinkCard =
        { WorkLevel: V2Tend.WorkLevel option
          Open: bool
          Status: V2Tend.Status option
          Parent: string option
          /// The card is on the board but its content could not be read.
          Unreadable: bool }

    /// The board-currency `linkage_of()` verdict for one PR: linked to exactly one Delivery, an R1 failure
    /// reason, or Unknown (a `U` failure: an unreadable reference or parent), which is never a link.
    type Linkage =
        | Linked of delivery: string
        | Unlinked of reason: string
        | Unknown of reason: string

    /// The read ceiling of design §2: 8 + ceil(rows/50) + ceil(rows/25) requests.
    val readCeiling: rows: int -> int

    /// One top-level definition of a GraphQL document: its kind (`query`, `mutation`, `subscription`,
    /// `fragment`; a bare selection set is `query`) and its root fields, by field name after any alias
    /// (`...` for a fragment spread or inline fragment).
    type Operation = { Kind: string; RootFields: string list }

    /// A parsed GraphQL document. `HasStringLiteral` says a string or block string appears anywhere in it.
    type Document = { Operations: Operation list; HasStringLiteral: bool }

    /// Parse a GraphQL document far enough to know every definition's kind and root fields: comments,
    /// commas, strings and block strings are lexed; selection sets, arguments and directives must balance.
    /// None when it does not parse that far.
    val parseDocument: document: string -> Document option

    /// The kinds of a document's top-level definitions (`parseDocument`); None when it does not parse.
    val operationKinds: document: string -> string list option

    /// A transport that refuses every mutation and every request that is not a GraphQL document of
    /// queries (and fragments) only, judged by `operationKinds`.
    val readOnly: transport: IGitHubTransport -> IGitHubTransport

    /// The comparison key of an `owner/repo#N` reference, shared with the board-currency
    /// `canonical_ref()`: owner and repository in lower case (GitHub names are case-insensitive), N as a
    /// number without leading zeros.
    val canonicalRef: reference: string -> string

    /// Body references a PR makes through `Refs` and closing keywords, after CRLF and lone-CR normalisation
    /// and removal of fenced and inline code, as `canonicalRef` keys (the board-currency `pr_refs`).
    val bodyRefs: repository: string -> body: string -> string list

    /// The PR body carries `<!-- fsgg:delivery-continues/v1 -->` outside code.
    val continues: body: string -> bool

    /// The PR body carries `<!-- fsgg:delivery-completes/v1 -->` outside code.
    val completes: body: string -> bool

    /// The board-currency `linkage_of()` verdict for one PR. `cards` maps `canonicalRef` keys to board
    /// cards; `anonymousUnreadable` says the board holds an unreadable card with no identity, so a miss
    /// could be that card and is Unknown, not "off the board".
    val linkage:
        cards: Map<string, LinkCard> ->
        anonymousUnreadable: bool ->
        prRepository: string -> prNumber: int -> prOpen: bool -> body: string -> Linkage

    /// The intended-fields line and the `## Blocked on` / `## Acceptance evidence` sections of an issue body.
    val bodySections: body: string -> string option * string option * string option

    /// Read the board and derive the snapshot `V2Tend.derive` takes, with the run's cost.
    val readSnapshot: transport: IGitHubTransport -> inputs: Inputs -> Result<V2Tend.Snapshot * Cost, string>
