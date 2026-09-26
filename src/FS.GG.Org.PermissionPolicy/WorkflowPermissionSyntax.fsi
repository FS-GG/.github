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
    /// Extract repository identities from supplied registry bytes after local shape validation.
    val registryRepositories: path: string -> text: string -> Result<string list, PermissionSyntaxDiagnostic>

    /// Inspect one caller job by exact ID.
    val caller: path: string -> jobId: string -> text: string -> Result<PermissionBlock * PermissionBlock, PermissionSyntaxDiagnostic>

    /// Require one caller job to name an organization reusable workflow and ref.
    val callerCall: path: string -> jobId: string -> text: string -> Result<ReusableWorkflowCall, PermissionSyntaxDiagnostic>

    /// Enumerate organization reusable workflow calls from every job in supplied caller bytes.
    val callerCallJobs: path: string -> text: string -> Result<(string * ReusableWorkflowCall) list, PermissionSyntaxDiagnostic>

    /// Inspect only a callee's top level permission grant.
    val callee: path: string -> text: string -> Result<PermissionBlock, PermissionSyntaxDiagnostic>

    /// Require a callee workflow_call declaration before returning its permission grant.
    val callableCallee: path: string -> text: string -> Result<PermissionBlock, PermissionSyntaxDiagnostic>

    /// Inspect every ordinary job step and retain job shape evidence with App token requests.
    val appTokenStepsDetailed: path: string -> text: string -> Result<AppTokenDetailedScan, PermissionSyntaxDiagnostic>

    /// Inspect every ordinary job step and return App token requests.
    val appTokenSteps: path: string -> text: string -> Result<AppTokenScan, PermissionSyntaxDiagnostic>
