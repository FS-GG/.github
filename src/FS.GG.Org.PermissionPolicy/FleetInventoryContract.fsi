namespace FS.GG.Org.PermissionPolicy

type FleetRegistrySnapshot =
    { Repository: string
      Path: string
      SourceRef: string
      Text: string }

type FleetRepositoryHead =
    { Repository: string
      HeadRef: string }

type WorkflowEnumerationState =
    | Terminal
    | Incomplete

type FleetWorkflowEnumeration =
    { Repository: string
      HeadRef: string
      State: WorkflowEnumerationState
      WorkflowPaths: string list }

/// Supplied facts only. The provider adapter must authenticate registry bytes, repository heads,
/// and terminal listings before this evidence may support an installed gate decision.
type FleetInventoryEvidence =
    { Registry: FleetRegistrySnapshot option
      Heads: FleetRepositoryHead list option
      Enumerations: FleetWorkflowEnumeration list option
      Fleet: CallerFleetEvidence }

type ProvisionalFleetVerdict =
    | ProvisionalSatisfied
    | ProvisionalFindings of AggregatePermissionFinding list

/// Check supplied fleet fact shape and cross-source closure; never an authority verdict.
[<RequireQualifiedAccess>]
module FleetInventoryContract =
    val evaluateSupplied:
        expectedSourceRef: string ->
        evidence: FleetInventoryEvidence ->
        Result<ProvisionalFleetVerdict, string>
