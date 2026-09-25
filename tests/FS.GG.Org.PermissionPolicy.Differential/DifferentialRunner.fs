open System
open System.IO
open System.Text.Json
open FS.GG.Org.PermissionPolicy

let property (name: string) (value: JsonElement) = value.GetProperty name
let field (name: string) (value: JsonElement) = (property name value).GetString()
let items (name: string) (value: JsonElement) = (property name value).EnumerateArray() |> Seq.toList
let number (name: string) (value: JsonElement) = (property name value).GetInt32()
let boolean (name: string) (value: JsonElement) = (property name value).GetBoolean()

let level = function
    | "none" -> NoAccess
    | "read" -> Read
    | "write" -> Write
    | other -> failwithf "invalid corpus inventory level %s" other

let jobShape (value: JsonElement) : WorkflowJobShape =
    {
        JobId = field "job_id" value
        IsReusableCall = boolean "reusable" value
        StepCount = number "steps" value
        AppStepIndices = items "app_steps" value |> List.map (fun item -> item.GetInt32())
    }

let inventory (value: JsonElement) : AppGrantFact =
    {
        Repository = "FS-GG/.github"
        InventoryId = field "id" value
        Grants =
            property "grants" value
            |> fun grants -> grants.EnumerateObject() |> Seq.toList
            |> List.map (fun (grant: JsonProperty) -> grant.Name, level (grant.Value.GetString()))
    }

let report result =
    match result with
    | Error code -> printfn "DIFF|NO_VERDICT|%s" code
    | Ok GateSatisfied -> printfn "DIFF|OK|satisfied"
    | Ok(GateFindings findings) -> printfn "DIFF|FINDING|%d" findings.Length

let run path =
    use document = JsonDocument.Parse(File.ReadAllText path)
    let scenario = document.RootElement
    let sourceRef = field "source_ref" scenario
    let callerRepository = field "caller_repository" scenario
    let callerText = field "caller_yaml" scenario
    let calleeText = field "callee_yaml" scenario
    let inventories = items "inventories" scenario
    let defaultInventory = inventories |> List.tryFind (fun value -> field "id" value = "default")
    let authorityRoster: AuthorityWorkflowRoster =
        {
            Repository = "FS-GG/.github"
            SourceRef = sourceRef
            Workflows =
                items "expected_workflows" scenario
                |> List.map (fun value ->
                    { Path = field "path" value
                      Jobs = items "jobs" value |> List.map jobShape })
        }
    let workflows: AuthorityWorkflowSnapshot list =
        items "authority_workflows" scenario
        |> List.map (fun value ->
            { Repository = "FS-GG/.github"
              Path = field "path" value
              SourceRef = sourceRef
              Text = field "text" value })
    let inventoryFacts: AppInventorySnapshot list =
        inventories |> List.map (fun value -> { SourceRef = sourceRef; Inventory = inventory value })
    let evidence: AggregatePermissionEvidence =
        { Roster = Some authorityRoster
          Workflows = Some workflows
          Inventories = Some inventoryFacts }

    match WorkflowPermissionSyntax.callerCall "caller.yml" "sync" callerText with
    | Error diagnostic -> report (Error("caller-syntax:" + diagnostic.Code))
    | Ok call ->
        let selectedInventory = defaultInventory |> Option.map inventory
        let facts: PermissionBindingFacts =
            {
                CallerRepository = callerRepository
                Roster =
                    Some
                        { Repository = "FS-GG/.github"
                          Path = "registry/repos.yml"
                          Repositories = items "roster_repositories" scenario |> List.map (fun item -> item.GetString()) }
                Callee =
                    Some
                        { Repository = "FS-GG/.github"
                          WorkflowPath = ".github/workflows/" + call.Callee
                          Ref = call.Ref
                          Origin = if call.Ref = "main" then WorkingTree else ExactRefRead
                          Text = calleeText }
                AppGrants = selectedInventory
            }
        match PermissionEvidenceBinding.bind callerRepository "default" call facts with
        | Error code -> report (Error("binding:" + code))
        | Ok bound -> PermissionAggregate.evaluate sourceRef bound evidence |> report

let args = Environment.GetCommandLineArgs()
if args.Length <> 2 then failwith "usage: DifferentialRunner <scenario.json>"
run args[1]
