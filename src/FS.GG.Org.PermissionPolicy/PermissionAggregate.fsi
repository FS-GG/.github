namespace FS.GG.Org.PermissionPolicy

type AuthorityWorkflowExpectation =
    { Path: string
      Jobs: WorkflowJobShape list }

type AuthorityWorkflowRoster =
    { Repository: string
      SourceRef: string
      Workflows: AuthorityWorkflowExpectation list }

type AuthorityWorkflowSnapshot =
    { Repository: string
      Path: string
      SourceRef: string
      Text: string }

type AppInventorySnapshot =
    { SourceRef: string
      Inventory: AppGrantFact }

type AggregatePermissionEvidence =
    { Roster: AuthorityWorkflowRoster option
      Workflows: AuthorityWorkflowSnapshot list option
      Inventories: AppInventorySnapshot list option }

type AggregatePermissionFinding =
    | UnderGrantFinding of Subject: string * UnderGrants: UnderGrant list
    | UnprovenDefaultFinding of Subject: string

type AggregatePermissionVerdict =
    | GateSatisfied
    | GateFindings of AggregatePermissionFinding list

/// Reduce one bound caller/callee pair and all supplied authority workflow facts.
[<RequireQualifiedAccess>]
module PermissionAggregate =
    val evaluate:
        expectedSourceRef: string ->
        bound: BoundPermissionCall ->
        evidence: AggregatePermissionEvidence ->
        Result<AggregatePermissionVerdict, string>
