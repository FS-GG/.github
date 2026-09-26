namespace FS.GG.Org.PermissionPolicy

/// A provider-extracted App-token step; Absent means the observed step requests no scopes.
type AppTokenRequestFact =
    { Repository: string
      AppIdentity: string
      Requested: PermissionBlock }

type AppTokenStepVerdict =
    { JobId: string
      StepIndex: int
      Verdict: PermissionVerdict }

type AppTokenWorkflowScan =
    { InspectedSteps: int
      Requests: AppTokenStepVerdict list }

/// Pure comparison against an already-bound, pinned App installation inventory.
[<RequireQualifiedAccess>]
module AppGrantComparison =
    val compare: bound: BoundPermissionCall -> request: AppTokenRequestFact option -> PermissionVerdict

    /// Scan supplied authority workflow bytes and compare every observed App-token step.
    val compareWorkflow:
        bound: BoundPermissionCall ->
        repository: string ->
        path: string ->
        text: string ->
        Result<AppTokenWorkflowScan, PermissionSyntaxDiagnostic>
