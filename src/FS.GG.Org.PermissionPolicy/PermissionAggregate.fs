namespace FS.GG.Org.PermissionPolicy

open System
open System.Text.RegularExpressions

type AuthorityWorkflowExpectation =
    {
        Path: string
        Jobs: WorkflowJobShape list
    }

type AuthorityWorkflowRoster =
    {
        Repository: string
        SourceRef: string
        Workflows: AuthorityWorkflowExpectation list
    }

type AuthorityWorkflowSnapshot =
    {
        Repository: string
        Path: string
        SourceRef: string
        Text: string
    }

type AppInventorySnapshot =
    {
        SourceRef: string
        Inventory: AppGrantFact
    }

type AggregatePermissionEvidence =
    {
        Roster: AuthorityWorkflowRoster option
        Workflows: AuthorityWorkflowSnapshot list option
        Inventories: AppInventorySnapshot list option
    }

type AggregatePermissionFinding =
    | UnderGrantFinding of Subject: string * UnderGrants: UnderGrant list
    | UnprovenDefaultFinding of Subject: string

type AggregatePermissionVerdict =
    | GateSatisfied
    | GateFindings of AggregatePermissionFinding list

/// A complete, supplied-source reducer for one bound caller/callee pair and all selected
/// authority workflows. Provider authentication and fleet-wide enumeration remain external.
[<RequireQualifiedAccess>]
module PermissionAggregate =
    let private authority = "FS-GG/.github"
    let private workflowPath =
        Regex("^\\.github/workflows/[^/]+\\.ya?ml$", RegexOptions.CultureInvariant)

    let private unique (values: 'a list) =
        values.Length = (values |> Set.ofList |> Set.count)

    let private validJob (job: WorkflowJobShape) =
        not (String.IsNullOrWhiteSpace job.JobId)
        && (if job.IsReusableCall then job.StepCount = 0 && List.isEmpty job.AppStepIndices
            else job.StepCount > 0
                 && unique job.AppStepIndices
                 && job.AppStepIndices = List.sort job.AppStepIndices
                 && (job.AppStepIndices |> List.forall (fun index -> index > 0 && index <= job.StepCount)))

    let private validExpectation (workflow: AuthorityWorkflowExpectation) =
        not (String.IsNullOrWhiteSpace workflow.Path)
        && workflowPath.IsMatch workflow.Path
        && not (List.isEmpty workflow.Jobs)
        && unique (workflow.Jobs |> List.map _.JobId)
        && List.forall validJob workflow.Jobs

    let private orderedJobs (jobs: WorkflowJobShape list) = jobs |> List.sortBy _.JobId

    let private gather results =
        results
        |> List.fold (fun state next ->
            state |> Result.bind (fun accumulated -> next |> Result.map (fun item -> item :: accumulated))) (Ok [])
        |> Result.map List.rev

    let private subject (bound: BoundPermissionCall) =
        $"{bound.CallerRepository} -> {bound.Call.Callee}@{bound.Call.Ref}"

    let evaluate (expectedSourceRef: string) (bound: BoundPermissionCall)
                 (evidence: AggregatePermissionEvidence) : Result<AggregatePermissionVerdict, string> =
        if String.IsNullOrWhiteSpace expectedSourceRef then Error "source-ref-missing"
        else
            match evidence.Roster with
            | None -> Error "workflow-roster-missing"
            | Some roster when roster.Repository <> authority -> Error "workflow-roster-repository-mismatch"
            | Some roster when roster.SourceRef <> expectedSourceRef -> Error "stale-source-ref"
            | Some roster when List.isEmpty roster.Workflows
                               || not (unique (roster.Workflows |> List.map _.Path))
                               || not (List.forall validExpectation roster.Workflows) ->
                Error "workflow-roster-invalid"
            | Some roster ->
                match evidence.Workflows with
                | None -> Error "workflow-facts-missing"
                | Some snapshots when not (unique (snapshots |> List.map _.Path)) ->
                    Error "workflow-facts-duplicate"
                | Some snapshots when snapshots |> List.exists (fun snapshot ->
                    snapshot.Repository <> authority
                    || String.IsNullOrWhiteSpace snapshot.Path
                    || not (workflowPath.IsMatch snapshot.Path)) ->
                    Error "workflow-source-invalid"
                | Some snapshots when snapshots |> List.exists (fun snapshot ->
                    snapshot.SourceRef <> expectedSourceRef) ->
                    Error "stale-source-ref"
                | Some snapshots when snapshots |> List.exists (fun snapshot ->
                    String.IsNullOrWhiteSpace snapshot.Text) ->
                    Error "workflow-text-missing"
                | Some snapshots ->
                    let expectedPaths = roster.Workflows |> List.map _.Path |> Set.ofList
                    let observedPaths = snapshots |> List.map _.Path |> Set.ofList
                    if not (Set.isSubset expectedPaths observedPaths) then Error "workflow-missing"
                    elif expectedPaths <> observedPaths then Error "workflow-unselected"
                    else
                        let expectations = roster.Workflows |> List.map (fun item -> item.Path, item.Jobs) |> Map.ofList
                        let scanned =
                            snapshots
                            |> List.sortBy _.Path
                            |> List.map (fun snapshot ->
                                match WorkflowPermissionSyntax.appTokenStepsDetailed snapshot.Path snapshot.Text with
                                | Error diagnostic -> Error("workflow-syntax:" + diagnostic.Code)
                                | Ok scan when orderedJobs scan.Jobs <> orderedJobs expectations[snapshot.Path] ->
                                    Error "workflow-shape-mismatch"
                                | Ok scan -> Ok(snapshot.Path, scan))
                            |> gather

                        scanned
                        |> Result.bind (fun scans ->
                            match evidence.Inventories with
                            | None -> Error "inventories-missing"
                            | Some inventories when not (unique (inventories |> List.map (fun item -> item.Inventory.InventoryId))) ->
                                Error "inventories-duplicate"
                            | Some inventories when inventories |> List.exists (fun item ->
                                item.SourceRef <> expectedSourceRef) ->
                                Error "stale-source-ref"
                            | Some inventories ->
                                let validated =
                                    inventories
                                    |> List.map (fun item ->
                                        PermissionEvidenceBinding.validateAppGrant
                                            item.Inventory.InventoryId item.Inventory
                                        |> Result.mapError (fun code -> "inventory-invalid:" + code))
                                    |> gather

                                validated
                                |> Result.bind (fun facts ->
                                    let byIdentity = facts |> List.map (fun fact -> fact.InventoryId, fact) |> Map.ofList
                                    match Map.tryFind bound.AppGrants.InventoryId byIdentity with
                                    | None -> Error "inventory-missing"
                                    | Some defaultFact when defaultFact <> bound.AppGrants ->
                                        Error "inventory-binding-mismatch"
                                    | Some _ ->
                                        let requests =
                                            scans
                                            |> List.collect (fun (path, scan) ->
                                                scan.Requests |> List.map (fun step -> path, step))
                                        let identities =
                                            requests
                                            |> List.map (fun (_, step) ->
                                                step.AppIdentitySecret
                                                |> Option.defaultValue bound.AppGrants.InventoryId)
                                        if identities |> List.exists (fun identity ->
                                            not (Map.containsKey identity byIdentity)) then
                                            Error "inventory-missing"
                                        else
                                            WorkflowPermissionSyntax.callableCallee bound.Call.Callee bound.CalleeText
                                            |> Result.mapError (fun diagnostic -> "callee-syntax:" + diagnostic.Code)
                                            |> Result.bind (fun floor ->
                                                let caller =
                                                    Permissions.compare bound.Call.WorkflowPermissions
                                                        bound.Call.JobPermissions floor
                                                match caller with
                                                | Refused code -> Error("caller-refused:" + code)
                                                | _ ->
                                                    let appResults =
                                                        requests
                                                        |> List.map (fun (path, step) ->
                                                            let identity =
                                                                step.AppIdentitySecret
                                                                |> Option.defaultValue bound.AppGrants.InventoryId
                                                            let inventory = byIdentity[identity]
                                                            let selected = { bound with AppGrants = inventory }
                                                            let request =
                                                                {
                                                                    Repository = authority
                                                                    AppIdentity = identity
                                                                    Requested = step.Requested
                                                                }
                                                            let verdict = AppGrantComparison.compare selected (Some request)
                                                            let where = $"{path} [{step.JobId}] App-token step {step.StepIndex}"
                                                            match verdict with
                                                            | Refused code -> Error("app-request-refused:" + code)
                                                            | UnprovenDefault -> Error "app-request-default-unproven"
                                                            | Satisfied -> Ok None
                                                            | UnderGranted short ->
                                                                Ok(Some(UnderGrantFinding(where, short))))
                                                        |> gather
                                                    appResults
                                                    |> Result.map (fun appFindings ->
                                                        let callerFindings =
                                                            match caller with
                                                            | UnderGranted short ->
                                                                [ UnderGrantFinding(subject bound, short) ]
                                                            | UnprovenDefault ->
                                                                [ UnprovenDefaultFinding(subject bound) ]
                                                            | _ -> []
                                                        let findings = callerFindings @ List.choose id appFindings
                                                        if List.isEmpty findings then GateSatisfied
                                                        else GateFindings findings))))
