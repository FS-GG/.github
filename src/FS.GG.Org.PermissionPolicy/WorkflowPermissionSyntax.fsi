namespace FS.GG.Org.PermissionPolicy

type PermissionSyntaxDiagnostic = { Code: string; Path: string }

type ReusableWorkflowCall =
    { Callee: string
      Ref: string
      WorkflowPermissions: PermissionBlock
      JobPermissions: PermissionBlock }

type AppTokenStep =
    { JobId: string
      StepIndex: int
      AppIdentitySecret: string option
      Requested: PermissionBlock }

type AppTokenScan =
    { InspectedSteps: int
      Requests: AppTokenStep list }

type WorkflowJobShape =
    { JobId: string
      IsReusableCall: bool
      StepCount: int
      AppStepIndices: int list }

type AppTokenDetailedScan =
    { InspectedSteps: int
      Jobs: WorkflowJobShape list
      Requests: AppTokenStep list }

/// A pure YAML adapter for the permission reducer. Pinned-ref and roster reads remain external.
/// Every entry point rejects ambiguous YAML shapes and returns the supplied path in its diagnostic.
/// Successful parsing proves local syntax only; it does not authenticate bytes or source revisions.
[<RequireQualifiedAccess>]
module WorkflowPermissionSyntax =
    /// Extract every repository identity from supplied registry bytes. The caller must authenticate
    /// the bytes and source ref; this parser only enforces the local YAML and identity shape.
    val registryRepositories: path: string -> text: string -> Result<string list, PermissionSyntaxDiagnostic>

    /// Inspect one caller job by exact ID; the caller/callee uses relationship is resolved elsewhere.
    val caller: path: string -> jobId: string -> text: string -> Result<PermissionBlock * PermissionBlock, PermissionSyntaxDiagnostic>

    /// Require the selected job to call the organization's reusable workflow at a stated ref.
    /// Reading that ref and binding the returned callee bytes remain separate provider work.
    val callerCall: path: string -> jobId: string -> text: string -> Result<ReusableWorkflowCall, PermissionSyntaxDiagnostic>

    /// Enumerate organization reusable-workflow calls from every job in one supplied caller file.
    /// The caller workflow roster and bytes must be authenticated by the provider separately.
    val callerCallJobs: path: string -> text: string -> Result<(string * ReusableWorkflowCall) list, PermissionSyntaxDiagnostic>

    /// Inspect only the callee's top-level grant; this permissive entry point has no call evidence.
    val callee: path: string -> text: string -> Result<PermissionBlock, PermissionSyntaxDiagnostic>

    /// Require the callee's `workflow_call` declaration before comparing its grant.
    /// This is syntax only: the caller's ref must still resolve to these exact bytes.
    val callableCallee: path: string -> text: string -> Result<PermissionBlock, PermissionSyntaxDiagnostic>

    /// Inspect every step in every ordinary job, then extract static App-token requests.
    /// A valid reusable-call job has no steps; malformed job and step shapes refuse.
    val appTokenStepsDetailed: path: string -> text: string -> Result<AppTokenDetailedScan, PermissionSyntaxDiagnostic>

    /// Inspect every ordinary job step and return App token requests.
    val appTokenSteps: path: string -> text: string -> Result<AppTokenScan, PermissionSyntaxDiagnostic>
