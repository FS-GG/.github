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

    let callerRoster: CallerFleetRoster =
        { Repository = "FS-GG/.github"
          Path = "registry/repos.yml"
          SourceRef = sourceRef
          Repositories =
            items "expected_caller_workflows" scenario
            |> List.map (fun value ->
                { Repository = field "repository" value
                  SourceRef = field "source_ref" value
                  WorkflowPaths = items "paths" value |> List.map _.GetString() }) }
    let callerSourceRef repository =
        callerRoster.Repositories
        |> List.find (fun item -> item.Repository = repository)
        |> _.SourceRef
    let rosterFact: RosterFact =
        { Repository = "FS-GG/.github"
          Path = "registry/repos.yml"
          Repositories = items "roster_repositories" scenario |> List.map _.GetString() }
    let callerSnapshots: CallerWorkflowSnapshot list =
        let first =
            { Repository = callerRepository
              WorkflowPath = ".github/workflows/caller.yml"
              SourceRef = callerSourceRef callerRepository
              Text = callerText }
        let others =
            (property "additional_callers" scenario).EnumerateObject()
            |> Seq.collect (fun entry ->
                entry.Value.EnumerateArray()
                |> Seq.mapi (fun index value ->
                    { Repository = entry.Name
                      WorkflowPath = $".github/workflows/caller-{index + 1}.yml"
                      SourceRef = callerSourceRef entry.Name
                      Text = value.GetString() }))
            |> Seq.toList
        first :: others
    let callFacts: CallerCallFact list =
        items "caller_call_facts" scenario
        |> List.map (fun value ->
            let repository = field "repository" value
            let callee = field "callee" value
            let calleeRef = field "ref" value
            let selectedInventory =
                inventories
                |> List.tryFind (fun item -> field "id" item = field "inventory_id" value)
                |> Option.map inventory
            let selectedCallee =
                if calleeRef = "main" then calleeText
                else field calleeRef (property "pinned_callees" scenario)
            { Repository = repository
              WorkflowPath = field "path" value
              JobId = field "job_id" value
              InventoryId = field "inventory_id" value
              BindingFacts =
                { CallerRepository = repository
                  Roster = Some rosterFact
                  Callee = Some
                    { Repository = "FS-GG/.github"
                      WorkflowPath = ".github/workflows/" + callee
                      Ref = calleeRef
                      Origin = if calleeRef = "main" then WorkingTree else ExactRefRead
                      Text = selectedCallee }
                  AppGrants = selectedInventory } })
    let fleetEvidence: CallerFleetEvidence =
        { Roster = Some callerRoster
          Workflows = Some callerSnapshots
          Calls = Some callFacts
          Authority = evidence }
    PermissionFleet.evaluate sourceRef fleetEvidence |> report

let args = Environment.GetCommandLineArgs()
if args.Length <> 2 then failwith "usage: DifferentialRunner <scenario.json>"
run args[1]
