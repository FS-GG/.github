namespace FS.GG.Coord.Cli.BoardOps

open FS.GG.Coord.Cli
open FS.GG.Coord.Cli.Kernel

module Handlers =

    /// Immutable identities used to join every lifecycle event to its applied unit and source, while
    /// requiring only the terminal event to carry the currently winning claim generation.
    type LifecycleAuthorityExpectation =
        { Repository: string
          Number: int
          Url: string
          Subject: string
          CurrentClaimGeneration: string
          ImplementationRepository: string
          ImplementationCandidate: string
          ImplementationMerge: string
          AcceptanceCandidate: string
          AcceptanceMerge: string
          ProtectedMain: string }

    /// Validate lifecycle identity/source bindings without rewriting historical claim generations.
    val validateLifecycleAuthority:
        expectation: LifecycleAuthorityExpectation -> lifecycleLog: string -> string list

    /// Refuse an SDD work model until it names the expected work item and every declared task is done.
    val validateCompleteSddWorkModel: expectedWorkId: string -> workModelJson: string -> string list

    /// Decide Ready eligibility from ADR-0045's sole dependency-edge authority.
    val readyDependencyVerdict: blockedByColumn: string option -> string option

    /// True when the Projects-v2 item revision changed between decision and mutation.
    val readyDependencyStale:
        before: FS.GG.Coord.GitHub.Board.BlockedByObservation option ->
        after: FS.GG.Coord.GitHub.Board.BlockedByObservation option ->
            bool

    [<Sealed>]
    type CommentCapability =
        member Path: string
        member Body: string
        member Cleanup: unit -> unit

    val allocateCommentCapability:
        worker: string ->
        item: FS.GG.Coord.Types.Ref ->
        source: string ->
            Result<CommentCapability, string>

    val addCmd: Context -> Options.Options -> int
    val flushCmd: Context -> Options.Options -> int
    val setField: Context -> Options.Options -> int
    val child: Context -> Options.Options -> int
    val say: Context -> Options.Options -> int
    val inbox: Context -> Options.Options -> int
    val roomOpen: Context -> Options.Options -> int
    val commentCmd: Context -> Options.Options -> int
    val bootstrapCmd: Context -> Options.Options -> int
    val boardCmd: Context -> int
    val fieldId: Context -> Options.Options -> int
    val optionId: Context -> Options.Options -> int
    val itemIdCmd: Context -> Options.Options -> int
    val bodyEditsCmd: Context -> Options.Options -> int
    val issues: Context -> Options.Options -> int
    val intakeCmd: Context -> Options.Options -> int
    /// Compile authoritative roadmap/catalog bytes, then apply every generated draft through the
    /// existing receipt-first intake transaction. A retry resumes from persisted per-draft receipts.
    val roadmapUnitPrepareApply: Context -> Options.Options -> int
    /// Re-observe every acceptance authority from GitHub and Git before an opaque accepted receipt can
    /// be sealed. Pure candidate inspection is intentionally insufficient at this boundary.
    val roadmapUnitAccept:
        runQualification: (string -> string -> string -> Result<FS.GG.Coord.Qualification.Accepted, string list>) ->
        Context ->
        Options.Options ->
            int
    val internal validateImmutablePreparation:
        input: FS.GG.Coord.RoadmapWorkUnit.AcceptanceInput ->
        roadmap: string ->
        catalog: string ->
            Result<unit, string list>
    /// Exercise the same parsing, qualification-binding, observation, and sealing route as production
    /// with authority observers supplied by a deterministic test host.
    val internal roadmapUnitAcceptWithObservers:
        runQualification: (string -> string -> string -> Result<FS.GG.Coord.Qualification.Accepted, string list>) ->
        observeSdd: (FS.GG.Coord.RoadmapWorkUnit.AcceptanceInput -> Result<unit, string list>) ->
        observeAuthorities: (FS.GG.Coord.RoadmapWorkUnit.AcceptanceInput -> Result<unit, string list>) ->
        Context ->
        Options.Options ->
            int
    /// Keep the production GitHub/Git authority observer active while replacing only the expensive
    /// independent SDD runner in deterministic integration tests.
    val internal roadmapUnitAcceptWithSddObserver:
        runQualification: (string -> string -> string -> Result<FS.GG.Coord.Qualification.Accepted, string list>) ->
        observeSdd: (FS.GG.Coord.RoadmapWorkUnit.AcceptanceInput -> Result<unit, string list>) ->
        Context ->
        Options.Options ->
            int
    val handlers: (Options.Command * HandlerRegistration.Handler) list
    val programHandlers: runWithContext: (Options.Options -> int) -> (Options.Command * (Options.Options -> int)) list
