namespace FS.GG.Coord.GitHub

/// The board-v2 tend writer (FS-GG/.github#5136 phase 2 PR 4; design §5, §6, §8).
///
/// Every write is a closed `TendChange` rendered to fixed GraphQL documents and sent through `allowlisted`,
/// which parses the document and refuses any root field outside the five project mutations, and admits
/// `addComment` and `closeIssue` only alone, only with every value in a variable, and only for an issue whose
/// C1 close is running. Field effects are item-scoped: the request immediately before each mutation is a
/// re-read of that item (and its deciding PRs), compared against the snapshot; a durable `possible-send`
/// custody record is written before the one send; an independent readback must equal the re-read with only
/// the selected fields moved. A failed or unproven send is `uncertain` and is never resent: later runs refuse
/// the item and only read it back. GR replays the merge gate's `deferred` records with the gate's rules.
module V2TendWrite =
    open System
    open System.Text.Json.Nodes
    open Errors
    open Transport

    type FieldValue =
        | OptionValue of optionId: string
        | TextValue of text: string

    /// A Delivery issue C1 may close. Its representation is hidden: `deliveryToClose` is its only constructor.
    [<Sealed>]
    type DeliveryToClose =
        member Issue: string
        member Item: string
        member Reference: string
        member Body: string

    /// The reconciler's closed change type. It is not a subtype of `V2Migration.Change`.
    type TendChange =
        | SetFieldValue of projectId: string * itemId: string * values: (string * FieldValue) list
        /// Rollback only.
        | ClearFieldValue of projectId: string * itemId: string * fieldId: string
        | AddItem of projectId: string * contentId: string
        /// E1 rollback only.
        | RemoveCreatedItem of projectId: string * itemId: string
        | ArchiveItem of projectId: string * itemId: string
        /// C1 only (decision 9): `addComment` then `closeIssue` with `stateReason: COMPLETED`.
        | CloseDelivery of DeliveryToClose

    val projectRootFields: Set<string>
    val issueRootFields: Set<string>
    val tendClosedMarker: string

    /// The fixed documents of one change; `mutationId` seeds each `clientMutationId` the response must echo.
    val render: mutationId: string -> change: TendChange -> Request list

    /// Why the allowlist refuses a request, or Ok. `closable` says an issue's C1 close is running.
    val admits: projectId: string -> closable: (string -> bool) -> request: Request -> Result<unit, string>

    /// Wrap a one-shot mutation sender: a request `admits` refuses is never dispatched.
    val allowlisted:
        projectId: string -> closable: (string -> bool) -> send: (Request -> IoResult<Response>) -> (Request -> IoResult<Response>)

    type FieldSchema = { Id: string; Name: string; DataType: string; Options: (string * string) list }

    /// One item as the immediate re-read reports it.
    type ItemState =
        { ItemId: string
          ProjectId: string
          Archived: bool
          ContentId: string
          Ref: string
          Open: bool
          Parent: string option
          Fields: FieldSchema list
          /// Every reported field value, as its canonical JSON, keyed by field id.
          Values: Map<string, string>
          /// The reported name (single select) or text of each value, keyed by field name.
          Named: Map<string, string>
          /// Deciding PRs re-read with the item: state, draft and merge time, keyed by `OWNER/REPO#N`.
          Prs: Map<string, string * bool * string option> }

    /// Re-read one item, its project's field schema and the given deciding PRs in one request. Any truncated
    /// connection, a field with two values, or an unreadable PR is an error.
    val readItem: transport: IGitHubTransport -> itemId: string -> prs: string list -> Result<ItemState, string>

    val custodySchema: string

    /// The reconciler's custody: one record per send under `<root>/<run>/`, named by the hash of its key (the
    /// item id for field sends, the issue node id for a close). Written atomically, private, synced.
    type Custody =
        new: root: string * run: string * syncDirectory: (string -> unit) -> Custody
        member Root: string
        member Run: string
        member Path: key: string -> string
        member Write: key: string * record: JsonObject -> unit
        /// Every readable record of every run with its path, and the count of malformed files.
        member Records: unit -> (string * JsonObject) list * int
        member Rewrite: path: string * record: JsonObject -> unit

    val gateSchema: string
    val gateStates: Set<string>

    type GateRecord =
        { Path: string
          Record: JsonObject
          Repo: string
          Pr: int
          Delivery: string
          ItemId: string option
          State: string
          MergedAt: string option }

    /// The merge gate's custody directory, validated as the gate validates it (schema, states, and a path
    /// that is the hash of its repository and PR). A rewrite keeps every other field of the record.
    type GateCustody =
        new: root: string * syncDirectory: (string -> unit) -> GateCustody
        member Root: string
        member Records: unit -> GateRecord list * int
        member Rewrite: record: GateRecord * state: string * now: DateTimeOffset -> unit

    type Outcome =
        { Subject: string
          Ref: string
          /// `fields`, `close`, `sweep` or `gate`.
          Kind: string
          /// `written`, `noop`, `refused`, `deferred`, `uncertain`, `verified`, or a gate pass result.
          Result: string
          Detail: string }

    type Settings =
        { ProjectId: string
          RootLogins: Set<string>
          MaxItemsPerRun: int
          MinimumMutationInterval: TimeSpan
          /// Done and closed cards beyond this many (newest first) are the archiver's: never written.
          ArchiveRetention: int }

    val defaultSettings: projectId: string -> rootLogins: Set<string> -> Settings

    /// The Project 3 field name a plan field writes (`Status`, `Work level`, `Track`, `Roadmap`).
    val boardField: field: V2Tend.Field -> string

    /// Mutations at most one per `interval`.
    type Pacer =
        new: clock: TimeProvider * wait: (TimeSpan -> unit) * interval: TimeSpan -> Pacer
        member Before: unit -> unit
        member After: unit -> unit

    type Context =
        { Reads: IGitHubTransport
          /// The allowlisted one-shot mutation sender.
          Send: Request -> IoResult<Response>
          Custody: Custody
          Gate: GateCustody
          Clock: TimeProvider
          Pacer: Pacer
          /// The issues whose close is running; the allowlisted sender admits issue mutations only for these.
          Closable: Collections.Generic.HashSet<string>
          Settings: Settings }

    /// Item ids the gate's custody holds as `possible-send` and as `deferred`.
    val gateHolds: records: GateRecord list -> Set<string> * Set<string>

    /// Done and closed cards beyond the newest `retention`, which the reconciler never writes (design §8).
    val archiveEligible: snapshot: V2Tend.Snapshot -> retention: int -> Set<string>

    type IssueReread =
        { IssueId: string
          Ref: string
          Open: bool
          /// Every cross-referencing PR: reference, state, draft, body.
          CrossReferences: (string * string * bool * string) list
          TendClosed: bool }

    /// Re-read a Delivery issue for C1: its complete cross-reference timeline and complete comments, paged
    /// until each node count equals its `totalCount`; a Root `fsgg:tend-closed/v1` comment sets `TendClosed`.
    val readIssue: transport: IGitHubTransport -> rootLogins: Set<string> -> issueId: string -> Result<IssueReread, string>

    /// A merged PR's merge commit and its newest recorded `fsgg:review-decision/v2` verdict with the comment URL.
    val readVerdict: transport: IGitHubTransport -> pr: string -> Result<string option * (string * string) option, string>

    /// The C1 closing comment (dispatch rule 3), ending with the `fsgg:tend-closed/v1` marker.
    val closingComment: lines: (string * string option * (string * string) option) list -> string

    /// The only constructor of `CloseDelivery`: the closure's item re-read must show an open Delivery
    /// (populated Work level) In review, and its issue re-read no reopen marker and no open linked PR.
    val deliveryToClose:
        closure: V2Tend.Closure -> item: ItemState -> issue: IssueReread -> comment: string -> Result<DeliveryToClose, string>

    /// Read back every `possible-send` record of the reconciler's own custody; a proven target rewrites it to
    /// `verified`. Never sends. Returns the keys still uncertain, and one outcome per record.
    val sweepOwn: context: Context -> Set<string> * Outcome list

    /// The merge gate's custody pass (GR), under the writer lock: a read-only sweep of `possible-send` records,
    /// then, when `replay`, supersession per Delivery by `mergedAt` and one guarded send for each Delivery's
    /// newest `deferred` record whose item still shows the recorded preimage (a closed Delivery is
    /// `not-applicable`, anything else `refused`).
    val gatePass: context: Context -> replay: bool -> Outcome list

    type RunInput =
        { Plan: V2Tend.Plan
          Snapshot: V2Tend.Snapshot
          OwnUncertain: Set<string>
          GateUncertain: Set<string>
          GateDeferred: Set<string> }

    /// Apply the plan's Apply effects (one guarded mutation per item, in dependency order, within
    /// `MaxItemsPerRun`) and its Close closures. Never resends; refuses items held by any custody.
    val apply: context: Context -> input: RunInput -> Outcome list

    /// `fsgg.board-tend-outcomes/1`: one entry per outcome (local, private custody: it names cards).
    val encodeOutcomes: outcomes: Outcome list -> string

    type Enable = { Apply: Set<string>; Propose: bool; MaxItemsPerRun: int }

    /// Rules whose enablement needs the contract amendment (`fsgg:board-tend-contract/v1`, design §12 PR 6).
    val contractGated: Set<string>

    /// Rules the enable file may name: every Apply rule and GR.
    val enableable: Set<string>

    /// Parse `board-tend.json` (`apply`, `propose`, `maxItemsPerRun`). Unknown keys or rules are refused; a
    /// gated rule is dropped, with a message, unless the contract amendment is present.
    val parseEnable: json: string -> contractAmended: bool -> Result<Enable * string list, string>
