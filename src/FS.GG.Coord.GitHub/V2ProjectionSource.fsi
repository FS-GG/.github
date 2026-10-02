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
    /// admitted population commit, validate the complete three-to-five pilot and exact binding, then
    /// freshly read each native issue, complete blocked_by snapshot (including empty) and finite owning plan.
    /// This selected pilot admits only reviewed empty dependency snapshots; any new edge is Stale.
    /// An issue revision differing from the reviewed manifest is Stale. Unknown reads license no write.
    /// Verified means complete/current native observation, never acceptance of the owning outcome.
    /// Previous reports are historical display evidence only and cannot make a write current.
    val runFixed:
        transport: IGitHubTransport ->
        binding: Binding ->
        previous: BatchReport option ->
            BatchReport
