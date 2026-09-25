namespace FS.GG.Org.PermissionPolicy

open System
open System.Text.RegularExpressions

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

/// Supplied facts only. The provider adapter must authenticate the registry bytes, repo heads,
/// and terminal listing results before this evidence may support an installed gate decision.
type FleetInventoryEvidence =
    { Registry: FleetRegistrySnapshot option
      Heads: FleetRepositoryHead list option
      Enumerations: FleetWorkflowEnumeration list option
      Fleet: CallerFleetEvidence }

type ProvisionalFleetVerdict =
    | ProvisionalSatisfied
    | ProvisionalFindings of AggregatePermissionFinding list

/// Check the shape and cross-source closure of supplied fleet facts; never an authority verdict.
[<RequireQualifiedAccess>]
module FleetInventoryContract =
    let private authority = "FS-GG/.github"
    let private rosterPath = "registry/repos.yml"
    let private workflowPath =
        Regex("^\\.github/workflows/[^/]+\\.ya?ml$", RegexOptions.CultureInvariant)

    let private unique values = List.length values = (values |> Set.ofList |> Set.count)

    let evaluateSupplied expectedSourceRef (evidence: FleetInventoryEvidence)
        : Result<ProvisionalFleetVerdict, string> =
        if String.IsNullOrWhiteSpace expectedSourceRef then Error "source-ref-missing"
        else
            match evidence.Registry with
            | None -> Error "inventory-registry-missing"
            | Some registry when registry.Repository <> authority || registry.Path <> rosterPath ->
                Error "inventory-registry-source-mismatch"
            | Some registry when registry.SourceRef <> expectedSourceRef -> Error "stale-source-ref"
            | Some registry when String.IsNullOrWhiteSpace registry.Text -> Error "inventory-registry-empty"
            | Some registry ->
                WorkflowPermissionSyntax.registryRepositories registry.Path registry.Text
                |> Result.mapError (fun diagnostic -> "inventory-registry-syntax:" + diagnostic.Code)
                |> Result.bind (fun registryRepos ->
                    match evidence.Fleet.Roster with
                    | None -> Error "fleet-roster-missing"
                    | Some roster when roster.Repository <> authority || roster.Path <> rosterPath ->
                        Error "fleet-roster-source-mismatch"
                    | Some roster when roster.SourceRef <> registry.SourceRef -> Error "stale-source-ref"
                    | Some roster when not (unique (roster.Repositories |> List.map _.Repository))
                                       || (roster.Repositories |> List.map _.Repository |> Set.ofList)
                                          <> (registryRepos |> Set.ofList) ->
                        Error "inventory-repository-set-mismatch"
                    | Some roster ->
                        let repoRows = roster.Repositories |> List.map (fun item -> item.Repository, item) |> Map.ofList
                        match evidence.Heads with
                        | None -> Error "inventory-heads-missing"
                        | Some heads when not (unique (heads |> List.map _.Repository))
                                          || (heads |> List.map _.Repository |> Set.ofList)
                                             <> (registryRepos |> Set.ofList) ->
                            Error "inventory-head-set-mismatch"
                        | Some heads when heads |> List.exists (fun item ->
                            String.IsNullOrWhiteSpace item.HeadRef
                            || item.HeadRef <> repoRows[item.Repository].SourceRef) ->
                            Error "inventory-head-ref-mismatch"
                        | Some heads ->
                            let headRefs = heads |> List.map (fun item -> item.Repository, item.HeadRef) |> Map.ofList
                            match evidence.Enumerations with
                            | None -> Error "inventory-enumerations-missing"
                            | Some listings when not (unique (listings |> List.map _.Repository))
                                                 || (listings |> List.map _.Repository |> Set.ofList)
                                                    <> (registryRepos |> Set.ofList) ->
                                Error "inventory-enumeration-set-mismatch"
                            | Some listings when listings |> List.exists (fun item -> item.State <> Terminal) ->
                                Error "inventory-enumeration-incomplete"
                            | Some listings when listings |> List.exists (fun item ->
                                String.IsNullOrWhiteSpace item.HeadRef
                                || item.HeadRef <> headRefs[item.Repository]) ->
                                Error "inventory-enumeration-head-mismatch"
                            | Some listings when listings |> List.exists (fun item ->
                                not (unique item.WorkflowPaths)
                                || (item.WorkflowPaths |> List.exists (fun path ->
                                    String.IsNullOrWhiteSpace path || not (workflowPath.IsMatch path)))
                                || (item.WorkflowPaths |> Set.ofList)
                                   <> (repoRows[item.Repository].WorkflowPaths |> Set.ofList)) ->
                                Error "inventory-workflow-set-mismatch"
                            | Some _ ->
                                PermissionFleet.evaluate expectedSourceRef evidence.Fleet
                                |> Result.map (function
                                    | GateSatisfied -> ProvisionalSatisfied
                                    | GateFindings findings -> ProvisionalFindings findings))
