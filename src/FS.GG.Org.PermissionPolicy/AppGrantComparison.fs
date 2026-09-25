namespace FS.GG.Org.PermissionPolicy

open System.Text.RegularExpressions

/// A provider-extracted App-token step; Absent means the observed step requests no scopes.
type AppTokenRequestFact =
    {
        Repository: string
        AppIdentity: string
        Requested: PermissionBlock
    }

type AppTokenStepVerdict =
    {
        JobId: string
        StepIndex: int
        Verdict: PermissionVerdict
    }

type AppTokenWorkflowScan =
    {
        InspectedSteps: int
        Requests: AppTokenStepVerdict list
    }

/// Pure comparison against an already-bound, pinned App installation inventory.
[<RequireQualifiedAccess>]
module AppGrantComparison =
    let private scopeName = Regex("^[a-z][a-z0-9_]*$", RegexOptions.CultureInvariant)

    let private level = function
        | NoAccess -> "none"
        | Read -> "read"
        | Write -> "write"

    let compare (bound: BoundPermissionCall) (request: AppTokenRequestFact option) =
        match request with
        | None -> Refused "app-request-fact-missing"
        | Some fact when fact.Repository <> bound.AppGrants.Repository ->
            Refused "app-request-repository-mismatch"
        | Some fact when fact.AppIdentity <> bound.AppGrants.InventoryId ->
            Refused "app-identity-mismatch"
        | Some fact ->
            match fact.Requested with
            | Absent -> Satisfied
            | Null -> Refused "app-request-null"
            | UnsupportedShape -> Refused "app-request-shape-unsupported"
            | Shorthand _ -> Refused "app-request-shorthand-unsupported"
            | Scopes scopes when scopes |> List.exists (fun (scope, _) ->
                System.String.IsNullOrWhiteSpace scope || not (scopeName.IsMatch scope)) ->
                Refused "app-request-scope-invalid"
            | Scopes scopes when scopes |> List.exists (fun (_, value) ->
                not (isNull value) && value.Contains "${{") ->
                Refused "app-request-dynamic"
            | Scopes scopes ->
                let inventory =
                    bound.AppGrants.Grants
                    |> List.map (fun (scope, granted) -> scope, level granted)
                    |> Scopes
                Permissions.compare inventory Absent (Scopes scopes)

    /// Scan a supplied authority workflow and compare every observed App-token step.
    /// The returned list is evidence, not an aggregate gate verdict.
    let compareWorkflow (bound: BoundPermissionCall) repository path text =
        if repository <> bound.AppGrants.Repository then
            Error { Code = "app-workflow-repository-mismatch"; Path = path }
        else
            WorkflowPermissionSyntax.appTokenSteps path text
            |> Result.map (fun scan ->
                {
                    InspectedSteps = scan.InspectedSteps
                    Requests =
                        scan.Requests
                        |> List.map (fun step ->
                            let request =
                                {
                                    Repository = repository
                                    AppIdentity =
                                        step.AppIdentitySecret
                                        |> Option.defaultValue bound.AppGrants.InventoryId
                                    Requested = step.Requested
                                }
                            {
                                JobId = step.JobId
                                StepIndex = step.StepIndex
                                Verdict = compare bound (Some request)
                            })
                })
