namespace FS.GG.Org.PermissionPolicy

type CallerRepositoryWorkflowRoster =
    { Repository: string
      SourceRef: string
      WorkflowPaths: string list }

/// Supplied caller inventory. Provider authentication and completeness are external obligations.
type CallerFleetRoster =
    { Repository: string
      Path: string
      SourceRef: string
      Repositories: CallerRepositoryWorkflowRoster list }

type CallerWorkflowSnapshot =
    { Repository: string
      WorkflowPath: string
      SourceRef: string
      Text: string }

type CallerCallFact =
    { Repository: string
      WorkflowPath: string
      JobId: string
      InventoryId: string
      BindingFacts: PermissionBindingFacts }

type CallerFleetEvidence =
    { Roster: CallerFleetRoster option
      Workflows: CallerWorkflowSnapshot list option
      Calls: CallerCallFact list option
      Authority: AggregatePermissionEvidence }

/// Reduce all calls in an exact supplied caller fleet. Provider reads remain external.
[<RequireQualifiedAccess>]
module PermissionFleet =
    val evaluate:
        expectedSourceRef: string ->
        evidence: CallerFleetEvidence ->
        Result<AggregatePermissionVerdict, string>
