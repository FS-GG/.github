namespace FS.GG.Org.PermissionPolicy

open System
open System.Text.RegularExpressions

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

/// Reduce all calls in an exact supplied caller fleet. This does not authenticate provider reads.
[<RequireQualifiedAccess>]
module PermissionFleet =
    let private authority = "FS-GG/.github"
    let private rosterPath = "registry/repos.yml"
    // The live default sweep selects every repos[].full row, including non-participants and
    // non-FS-GG owners. Identity membership is checked against the parsed registry separately.
    let private repositoryName =
        Regex("^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$", RegexOptions.CultureInvariant)
    let private workflowPath = Regex("^\\.github/workflows/[^/]+\\.ya?ml$", RegexOptions.CultureInvariant)

    let private unique values = List.length values = (values |> Set.ofList |> Set.count)
    let private key repository path = repository, path
    let private callKey repository path jobId = repository, path, jobId
    let private gather results =
        results
        |> List.fold (fun state next ->
            state |> Result.bind (fun accumulated -> next |> Result.map (fun item -> item :: accumulated))) (Ok [])
        |> Result.map List.rev

    let evaluate (expectedSourceRef: string) (evidence: CallerFleetEvidence)
                 : Result<AggregatePermissionVerdict, string> =
        if String.IsNullOrWhiteSpace expectedSourceRef then Error "source-ref-missing"
        else
            match evidence.Roster with
            | None -> Error "fleet-roster-missing"
            | Some roster when roster.Repository <> authority || roster.Path <> rosterPath ->
                Error "fleet-roster-source-mismatch"
            | Some roster when roster.SourceRef <> expectedSourceRef -> Error "stale-source-ref"
            | Some roster when List.isEmpty roster.Repositories
                               || not (unique (roster.Repositories |> List.map _.Repository))
                               || (roster.Repositories |> List.exists (fun item ->
                                   String.IsNullOrWhiteSpace item.Repository
                                   || not (repositoryName.IsMatch item.Repository)
                                   || String.IsNullOrWhiteSpace item.SourceRef
                                   || not (unique item.WorkflowPaths)
                                   || (item.WorkflowPaths |> List.exists (fun path ->
                                       String.IsNullOrWhiteSpace path || not (workflowPath.IsMatch path))))) ->
                Error "fleet-roster-invalid"
            | Some roster ->
                let expectedRefs =
                    roster.Repositories
                    |> List.map (fun item -> item.Repository, item.SourceRef)
                    |> Map.ofList
                let expectedKeys =
                    roster.Repositories
                    |> List.collect (fun item -> item.WorkflowPaths |> List.map (key item.Repository))
                    |> Set.ofList
                match evidence.Workflows with
                | None -> Error "caller-workflows-missing"
                | Some snapshots when not (unique (snapshots |> List.map (fun item ->
                    key item.Repository item.WorkflowPath))) ->
                    Error "caller-workflow-facts-duplicate"
                | Some snapshots when snapshots |> List.exists (fun item ->
                    String.IsNullOrWhiteSpace item.Repository
                    || String.IsNullOrWhiteSpace item.WorkflowPath
                    || not (workflowPath.IsMatch item.WorkflowPath)
                    || String.IsNullOrWhiteSpace item.Text) ->
                    Error "caller-workflow-source-invalid"
                | Some snapshots when snapshots |> List.exists (fun item ->
                    match Map.tryFind item.Repository expectedRefs with
                    | Some sourceRef -> item.SourceRef <> sourceRef
                    | None -> false) ->
                    Error "stale-source-ref"
                | Some snapshots when (snapshots |> List.map (fun item ->
                    key item.Repository item.WorkflowPath) |> Set.ofList) <> expectedKeys ->
                    Error "caller-workflow-facts-mismatch"
                | Some snapshots ->
                    snapshots
                    |> List.sortBy (fun item -> key item.Repository item.WorkflowPath)
                    |> List.map (fun item ->
                        WorkflowPermissionSyntax.callerCallJobs item.WorkflowPath item.Text
                        |> Result.mapError (fun diagnostic -> "caller-syntax:" + diagnostic.Code)
                        |> Result.map (List.map (fun (jobId, call) ->
                            callKey item.Repository item.WorkflowPath jobId, call)))
                    |> gather
                    |> Result.map List.concat
                    |> Result.bind (fun selectedCalls ->
                        if List.isEmpty selectedCalls then Error "caller-pairs-empty"
                        else
                            match evidence.Calls with
                            | None -> Error "caller-call-facts-missing"
                            | Some facts when not (unique (facts |> List.map (fun item ->
                                callKey item.Repository item.WorkflowPath item.JobId))) ->
                                Error "caller-call-facts-duplicate"
                            | Some facts when (facts |> List.map (fun item ->
                                callKey item.Repository item.WorkflowPath item.JobId) |> Set.ofList)
                                <> (selectedCalls |> List.map fst |> Set.ofList) ->
                                Error "caller-call-facts-mismatch"
                            | Some facts ->
                                let rosterFact: RosterFact =
                                    { Repository = roster.Repository
                                      Path = roster.Path
                                      Repositories = roster.Repositories |> List.map _.Repository }
                                let byCall =
                                    facts
                                    |> List.map (fun item ->
                                        callKey item.Repository item.WorkflowPath item.JobId, item)
                                    |> Map.ofList
                                selectedCalls
                                |> List.map (fun ((repository, _, _) as selectedKey, call) ->
                                    let supplied = byCall[selectedKey]
                                    if supplied.BindingFacts.Roster <> Some rosterFact then
                                        Error "caller-roster-binding-mismatch"
                                    else
                                        PermissionEvidenceBinding.bind repository supplied.InventoryId
                                            call supplied.BindingFacts
                                        |> Result.mapError (fun code -> "caller-binding:" + code)
                                        |> Result.bind (fun bound ->
                                            PermissionAggregate.evaluate expectedSourceRef bound
                                                evidence.Authority))
                                |> gather
                                |> Result.map (fun verdicts ->
                                    let findings =
                                        verdicts |> List.collect (function
                                            | GateSatisfied -> []
                                            | GateFindings values -> values)
                                    if List.isEmpty findings then GateSatisfied
                                    else GateFindings findings))
