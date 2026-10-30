namespace FS.GG.Coord.GitHub

/// Online, read-only resolution of the native facts the batched enrollment planner consumes
/// (`fsgg.board-v2-native-facts/1`) and of the Project item inventory the preflight inspects.
///
/// The only effect is a `Get` function that takes a path and nothing else: there is no method, body or
/// header parameter, so this module cannot express a mutation. The production `Get` is the argument
/// vector returned by `getArguments`, which is itself closed to a GET of an `/orgs/` or `/repos/` path.
/// Failures are the closed code `NativeReadFailed`; no response text, token or identity is ever returned in
/// a refusal. This module grants no authority and never reads a private binding's enrollment payload.
module V2NativeResolve =

    /// A response. `NotFound` is a clean HTTP 404 (an absent resource); every other failure is `Failed`.
    type GetResult =
        | Body of string
        | NotFound
        | Failed

    /// Read-only GET of an API path. When `paginated` is true the production implementation follows the
    /// response's next links and returns the pages as one JSON array of page arrays.
    type Get = string -> bool -> GetResult

    type ItemContent =
        | IssueContent of reference: string * nodeId: string
        | PullRequestContent of reference: string * nodeId: string
        /// A draft issue or any content kind the resolver does not understand; carries only the kind label.
        | OtherContent of kind: string

    /// One Project item with exactly what the preflight needs: presence of the three scheduling fields
    /// and the item's last update time.
    type ProjectItem =
        { ItemId: string
          Content: ItemContent
          HasStatus: bool
          HasTrack: bool
          HasWorkLevel: bool
          /// The raw text of the item's Roadmap value, when it has one.
          RoadmapRaw: string option
          UpdatedAt: System.DateTimeOffset }

    type Resolved =
        { Facts: V2Enrollment.NativeFacts
          /// Every item of the Project, read completely or the whole resolution fails.
          Items: ProjectItem list }

    /// `GET`-only argument vector for the GitHub CLI, or `None` when the path is outside the closed grammar
    /// (`/orgs/...` or `/repos/...`, bounded characters, an optional bounded query). Always contains
    /// `--method GET` and never a field, body or input flag.
    val getArguments: path: string -> paginated: bool -> string list option

    /// Read the Project's node id and complete item inventory.
    val readProject: get: Get -> binding: V2Projection.Binding -> Result<string * ProjectItem list, V2Enrollment.Refusal>

    /// Resolve every request issue and plan blob against native state. An issue that is absent (404) is
    /// reported with partial visibility so the planner refuses it with its own typed code. Any other read
    /// failure returns `NativeReadFailed` and no facts.
    val resolve: get: Get -> binding: V2Projection.Binding -> request: V2Enrollment.Request -> Result<Resolved, V2Enrollment.Refusal>

    /// The `fsgg.board-v2-native-facts/1` document for the planner. Deterministic, compact.
    val encodeNativeFacts: facts: V2Enrollment.NativeFacts -> string
