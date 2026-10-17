namespace FS.GG.Coord.GitHub

/// Fixed canonical composition; no caller-supplied currentness, verifier, URL or executable recipe.
module V2ProjectionSource =
    open Errors
    open Transport
    open System
    open V2Projection

    type NativeObservation =
        { Issue: IssueRef; NodeId: string; UpdatedAt: string; State: string; Url: string; BodySha256: string }

    /// Native closure, delivery, publication and operation acceptance remain distinct observations.
    /// Failed reads retain the project's prior value and report Unknown/Stale outside the board.
    type ItemReport =
        { Issue: IssueRef
          Native: NativeObservation option
          DependencyObservations: NativeObservation list
          DependencyReadComplete: bool
          PlanObservation: NativeObservation option
          Delivery: string
          Publication: string
          NativeAcceptance: string
          Health: string
          Reads: int
          MutationAttempts: int
          MembershipPages: int option
          Projection: Report option
          Gap: IoError option
          LastVerified: Report option }

    type BatchReport =
        { ProjectId: string
          RecipeRevision: string
          PopulationRevision: string
          ImportRecipeRevision: string
          ImportArtifactSha256: string
          ArtifactSha256: string
          NativeDispatch: V2ObservationTransport.DispatchReceipt list
          ProtectedInputs: (string * string * string) list
          Selected: int option
          Attempted: int
          Verified: int
          Items: ItemReport list
          PopulationGap: IoError option
          Cleanup: string }

    /// Verify the loaded adapter assembly hash before IO. Read the fixed canonical manifest at the
    /// admitted population commit, validate the complete three-to-five organization pilot or one-to-five
    /// receiver repository cohort and exact binding, then
    /// freshly read each native issue, complete blocked_by snapshot (including empty) and finite owning plan.
    /// This selected pilot admits only reviewed empty dependency snapshots; any new edge is Stale.
    /// An issue revision differing from the reviewed manifest is Stale. Unknown reads license no write.
    /// Verified means complete/current native observation, never acceptance of the owning outcome.
    /// Version 2 retains the organization manifest. Version 3 reads the same import/v1 manifest path
    /// from its sole selected receiver repository, checks its pinned/current main blob and reads only
    /// that repository's canonical docs/native plan. Product metadata never comes from the organization
    /// manifest. Previous reports are historical display evidence only and cannot make a write current.
    val runFixed:
        transport: IGitHubTransport ->
        binding: Binding ->
        previous: BatchReport option ->
            BatchReport

    /// Read evidence and human intent remain distinct. No new verification timestamp or write is minted.
    /// Native head-bound observations; none of these facts establishes deployment acceptance.
    type PullRequestFacts =
        { Id: string; Repository: string; Number: int; Url: string; Head: string; UpdatedAt: string
          Author: string option; Issues: string list; Files: string list; ReviewDecision: string option
          Reviews: string list; Checks: string list; Gaps: string list }

    /// ObservedAt is the conservative collection-start time, including failed collection attempts.
    /// MinValue means collection was never attempted; it is an absent-observation sentinel and is
    /// never fresh, even when a caller supplies MinValue as its evaluation time. The v4 wire emits null.
    type NativeIntegratorFacts =
        { PullRequests: PullRequestFacts list; RepositoryGaps: Map<string, string>; ObservedAt: DateTimeOffset }

    /// Trusted caller inputs for one current slice, not a parent outcome reservation or authority.
    type Action = Source | Activation
    type Prerequisite = { Action: Action; Reference: string; Satisfied: bool option; SupplyingOwner: string }
    type CurrentSlice =
        { Issue: IssueRef; Owner: string; Repository: string; Paths: string list; Action: Action
          Prerequisites: Prerequisite list; PrerequisitesComplete: bool; IntakeAuthorized: bool option }
    type Reservation = { Owner: string; Repository: string; Paths: string list }
    type SelectionContext =
        { Slices: CurrentSlice list; ImplementationSlots: int option; ReviewSlots: int option; HostedCiSlots: int option
          Reservations: Reservation list option; ObservedAt: DateTimeOffset; ValidUntil: DateTimeOffset; Evidence: string }
    type Selection =
        { Issue: IssueRef; Action: Action; Decision: string; Owner: string option; PullRequest: string option
          Head: string option; Reasons: string list; Evidence: string list }

    type InspectionItem =
        { Evidence: ItemReport; ExpectedNodeId: string; SourceCurrentness: string
          Planning: PlanningObservation option; PlanningGap: IoError option; Discrepancies: string list; Owner: string option; SourceRevision: string option }

    type InspectionReport =
        { Binding: Binding; Evidence: BatchReport; Items: InspectionItem list
          Memberships: Membership list; MembershipGap: IoError option; NativeIntegrator: NativeIntegratorFacts }

    /// Facts supplied by the existing integrator; None means unknown and grants no authority.
    type IntegratorFacts =
        { OpenPullRequests: string list option; TouchSets: Map<string, string list> option; AvailableSlots: int option }
    type PlanningCandidate =
        { Issue: IssueRef; HumanStatus: string option; Track: string option; Roadmap: string option
          SourceCurrentness: string; Observation: string option; UnmetOrUnknown: string list; Integrator: IntegratorFacts }
    /// Programme v4 enrollment/v2 separates exact native/plan identities from current issue, closure,
    /// dependencies and protected-main plan revisions. Native PR observations include non-enrolled
    /// in-flight work, bounded full connection reads, head-bound reviews/checks and final head barriers.
    /// Fixed queries/selected GET reads only. No writer, mutation or durable retry composition.
    val createInspectionTransport: binding: Binding -> token: string -> IoResult<IGitHubTransport * System.IDisposable>
    /// Reuse admitted source collection and exact planning readers. Never calls runOneShot or any writer.
    val inspectFixed: transport: IGitHubTransport -> binding: Binding -> previous: BatchReport option -> InspectionReport
    /// Deterministic display of selected human intent and known/unknown evidence. No scheduling decision.
    val planningCandidates: report: InspectionReport -> facts: IntegratorFacts -> PlanningCandidate list

    /// Action-specific, read-only selection with a trusted current-slice/resource seam. Never dispatches.
    val selectActions: report: InspectionReport -> context: SelectionContext option -> now: System.DateTimeOffset -> Selection list
