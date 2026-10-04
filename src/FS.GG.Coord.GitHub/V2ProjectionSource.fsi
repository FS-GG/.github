namespace FS.GG.Coord.GitHub

/// Fixed canonical composition; no caller-supplied currentness, verifier, URL or executable recipe.
module V2ProjectionSource =
    open Errors
    open Transport
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
    type InspectionItem =
        { Evidence: ItemReport; ExpectedNodeId: string; SourceCurrentness: string
          Planning: PlanningObservation option; PlanningGap: IoError option; Discrepancies: string list }
    type InspectionReport =
        { Binding: Binding; Evidence: BatchReport; Items: InspectionItem list }
    /// Facts supplied by the existing integrator; None means unknown and grants no authority.
    type IntegratorFacts =
        { OpenPullRequests: string list option; TouchSets: Map<string, string list> option; AvailableSlots: int option }
    type PlanningCandidate =
        { Issue: IssueRef; HumanStatus: string option; Track: string option; Roadmap: string option
          SourceCurrentness: string; Observation: string option; UnmetOrUnknown: string list; Integrator: IntegratorFacts }
    /// Fixed queries/selected GET reads only. No writer, mutation or durable retry composition.
    val createInspectionTransport: binding: Binding -> token: string -> IoResult<IGitHubTransport * System.IDisposable>
    /// Reuse admitted source collection and exact planning readers. Never calls runOneShot or any writer.
    val inspectFixed: transport: IGitHubTransport -> binding: Binding -> previous: BatchReport option -> InspectionReport
    /// Deterministic display of selected human intent and known/unknown evidence. No scheduling decision.
    val planningCandidates: report: InspectionReport -> facts: IntegratorFacts -> PlanningCandidate list
