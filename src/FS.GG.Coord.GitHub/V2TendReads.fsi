namespace FS.GG.Coord.GitHub

/// Batched read-only reads for `board-v2 tend` (FS-GG/.github#5136 phase 2 PR 3; design §2).
///
/// A run reads (A) the board items with their field values and issue facts, 50 per page; (B) the open pull
/// requests of the organization, 100 per page; and (C) for every open issue card, its cross-reference
/// timeline (last 25) and recent comments (last 50), 25 issues aliased per request. Every request is a
/// GraphQL query; the transport is wrapped so that no mutation can be sent. A board over 500 items is
/// refused on the first page, and a truncated connection is reported as incomplete, never as empty.
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

    /// The read ceiling of design §2: 8 + ceil(rows/50) + ceil(rows/25) requests.
    val readCeiling: rows: int -> int

    /// A transport that refuses every mutation and every request that is not a GraphQL query.
    val readOnly: transport: IGitHubTransport -> IGitHubTransport

    /// Body references a PR makes through `Refs` and closing keywords, after CRLF normalisation and removal
    /// of fenced and inline code, as `owner/repo#n` in lower case (the board-currency `pr_refs`).
    val bodyRefs: repository: string -> body: string -> string list

    /// The PR body carries `<!-- fsgg:delivery-continues/v1 -->` outside code.
    val continues: body: string -> bool

    /// The board-currency `linkage_of()` verdict for one PR: `Ok delivery` (no failures, exactly one
    /// Delivery) or `Error reason`. `cards` maps lower-case references to (Work level, open, Status, parent).
    val linkage:
        cards: Map<string, V2Tend.WorkLevel option * bool * V2Tend.Status option * string option> ->
        prRepository: string -> prNumber: int -> prOpen: bool -> body: string -> Result<string, string>

    /// The intended-fields line and the `## Blocked on` / `## Acceptance evidence` sections of an issue body.
    val bodySections: body: string -> string option * string option * string option

    /// Read the board and derive the snapshot `V2Tend.derive` takes, with the run's cost.
    val readSnapshot: transport: IGitHubTransport -> inputs: Inputs -> Result<V2Tend.Snapshot * Cost, string>
