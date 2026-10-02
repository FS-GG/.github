module FS.GG.Coord.BoardV2Import

open System
open System.IO
open System.Text.Json.Nodes
open System.Text.RegularExpressions

// Offline administrative authority: no transport, effects, durable state or lifecycle reducer.
let require condition message = if not condition then invalidOp message
let prop (node: JsonNode) (name: string) = node.[name]
let str (node: JsonNode) = if isNull node then "" else node.GetValue<string>()
let s node name = prop node name |> str
let b node name = prop node name |> fun n -> n.GetValue<bool>()
let arr (node: JsonNode) = node.AsArray() |> Seq.toList
let integer node name = prop node name |> fun n -> n.GetValue<int>()
let nonempty node name = require (not (String.IsNullOrWhiteSpace(s node name))) (name + " is required")
let keys (node: JsonNode) (allowed: string list) =
    for pair in node.AsObject() do require (List.contains pair.Key allowed) ("unexpected property " + pair.Key)
let timestamp node name =
    let mutable value = DateTimeOffset.MinValue
    require (DateTimeOffset.TryParse(s node name, &value)) ("invalid timestamp " + name)
let exact node name expected = require (s node name = expected) (name + " differs")
let issuePattern = "^FS-GG/[^/#]+#[1-9][0-9]*$"
let validRef (value: string) = Regex.IsMatch(value, issuePattern)
let fields = [
    "Status", "single-select", ["Backlog"; "Ready"; "In progress"; "Blocked"; "Done"], "human-scheduling", false
    "Roadmap", "text", [], "owning-plan", false
    "Track", "single-select", ["Active delivery"; "Follow-up"], "human-scheduling", false
    "Observation", "single-select", ["Verified"; "Stale"; "Unknown"], "restricted-refresh", true ]
let add item = List.contains (s item "decision") ["import"; "follow-up"]
let digest (value: string) = Regex.IsMatch(value, "^[a-f0-9]{64}$")
let revision (value: string) = Regex.IsMatch(value, "^[a-f0-9]{40}$")

let validate (manifest: JsonNode) =
    keys manifest ["schema"; "observedAt"; "source"; "target"; "fields"; "items"; "preparationState"; "binding"; "inventory"]
    timestamp manifest "observedAt"
    exact manifest "schema" "fsgg.coordination-board-v2-import/v1"
    let source = manifest.["source"]
    keys source ["owner"; "title"; "number"; "id"; "treatment"]
    exact source "owner" "FS-GG"
    exact source "title" "Coordination"
    exact source "id" "PVT_kwDOEYAWY84Bb08W"
    exact source "treatment" "retain-as-legacy"
    require (integer source "number" = 1) "legacy project number differs"
    let target = manifest.["target"]
    keys target ["ownerKind"; "owner"; "title"; "schemaVersion"; "creationState"; "number"; "id"]
    exact target "ownerKind" "organization"
    exact target "owner" "FS-GG"
    exact target "title" "Coordination V2"
    require (integer target "schemaVersion" = 1) "schemaVersion differs"
    let created = s target "creationState" = "created-and-read-back"
    require (created || s target "creationState" = "pending-authorized-operation") "invalid creationState"
    if created then
        require (integer target "number" > 1) "Project 1 cannot be a V2 target"
        require ((s target "id").StartsWith("PVT_") && s target "id" <> s source "id") "invalid V2 target id"
    else require (isNull target.["number"] && isNull target.["id"]) "pending target must not invent identities"
    let actualFields = arr manifest.["fields"]
    require (actualFields.Length = 4) "exactly four fields required"
    require ((actualFields |> List.map (fun f -> s f "name") |> Set.ofList) = (fields |> List.map (fun (n,_,_,_,_) -> n) |> Set.ofList)) "field names differ"
    for name,kind,options,owner,refresh in fields do
        let field = actualFields |> List.find (fun f -> s f "name" = name)
        keys field ["name"; "kind"; "options"; "owner"; "importerMaySeed"; "refreshMayWrite"; "id"; "optionIds"]
        exact field "kind" kind
        exact field "owner" owner
        require ((arr field.["options"] |> List.map str) = options) (name + " options differ")
        require (b field "importerMaySeed" && b field "refreshMayWrite" = refresh) (name + " ownership differs")
        if created then nonempty field "id"
        else require (isNull field.["id"]) ("pending " + name + " must not invent identity")
    if created then require ((actualFields |> List.map (fun f -> s f "id") |> Set.ofList |> Set.count) = 4) "duplicate field id"
    let items = arr manifest.["items"]
    require (not items.IsEmpty) "items required"
    let refs = Collections.Generic.HashSet<string>()
    let nodes = Collections.Generic.HashSet<string>()
    for item in items do
        keys item ["issue"; "nodeId"; "title"; "observedUpdatedAt"; "observedState"; "decision"; "reason"; "remainingOutcome"; "roadmap"; "status"; "track"; "observation"; "dependencies"; "pilot"; "owner"; "nextAction"; "acceptanceEvidence"; "adjudication"]
        timestamp item "observedUpdatedAt"
        let reference = s item "issue"
        require (validRef reference) "invalid issue identity"
        require (refs.Add reference) "duplicate issue identity"
        require ((s item "nodeId").StartsWith("I_") && nodes.Add(s item "nodeId")) "invalid or duplicate issue node identity"
        require (List.contains (s item "decision") ["import"; "follow-up"; "adjudicate"; "omit-delivered"; "omit-superseded"]) "invalid decision"
        require (not (b item "pilot") || add item) "pilot requires approved remaining outcome"
        for key in ["title"; "reason"; "observedUpdatedAt"] do nonempty item key
        require (List.contains (s item "observedState") ["open"; "closed"]) "invalid issue state"
        require (List.contains (s item "status") ["Backlog"; "Ready"; "In progress"; "Blocked"; "Done"]) "invalid Status seed"
        require (List.contains (s item "track") ["Active delivery"; "Follow-up"]) "invalid Track seed"
        require (List.contains (s item "observation") ["Verified"; "Stale"; "Unknown"]) "invalid Observation seed"
        if add item then
            require (s item "observedState" = "open") "approved addition must be a remaining open native issue"
            for key in ["remainingOutcome"; "roadmap"; "owner"; "nextAction"; "acceptanceEvidence"] do nonempty item key
            exact item "adjudication" "verified-remaining"
            require (s item "acceptanceEvidence" <> "unknown") "verified admission needs actual acceptance evidence"
            require (s item "track" = (if s item "decision" = "follow-up" then "Follow-up" else "Active delivery")) "decision/track mismatch"
        let dependencies = arr item.["dependencies"]
        require (dependencies.Length <= 50) "maximum fifty dependencies per item"
        require ((dependencies |> List.map (fun d -> s d "issue") |> Set.ofList |> Set.count) = dependencies.Length) "duplicate dependency"
        for dep in dependencies do
            keys dep ["issue"; "observedState"; "evidence"]
            require (validRef(s dep "issue")) "invalid dependency identity"
            require (List.contains (s dep "observedState") ["open"; "closed"; "unknown"]) "invalid dependency state"
            require (List.contains (s dep "evidence") ["native"; "issue-body"; "owning-plan"; "unknown"]) "invalid dependency evidence"
    let pilots = items |> List.filter (fun i -> b i "pilot")
    require (pilots.Length <= 5) "maximum five pilot items"
    exact manifest "preparationState" "adjudication-required"
    items, pilots, created

// A fixed descriptive operation constructor. It grants no authorization and executes no effects.
let pilotPlan manifest =
    let _,pilots,created = validate manifest
    require (pilots.Length >= 3) "pilot requires three to five genuine existing remaining issue IDs"
    require created "target/schema readback is required"
    let binding = manifest.["binding"]
    keys binding ["organizationId"; "visibility"; "authorization"; "recipeRevision"; "artifactSha256"; "repositories"]
    exact binding "organizationId" "O_kgDOEYAWYw"
    exact binding "visibility" "complete"
    exact binding "authorization" "root-selected"
    require (revision(s binding "recipeRevision") && digest(s binding "artifactSha256")) "trusted recipe/artifact identity required"
    let allowlist = arr binding.["repositories"] |> List.map str
    require (not allowlist.IsEmpty && (Set.ofList allowlist |> Set.count) = allowlist.Length) "repository allowlist required"
    for item in pilots do
        require (List.contains ((s item "issue").Split('#').[0]) allowlist) "foreign repository"
        for dep in arr item.["dependencies"] do
            require (s dep "observedState" <> "unknown" && s dep "evidence" = "native") "complete native dependency snapshot required"
    for field in arr manifest.["fields"] do
        if s field "kind" = "single-select" then
            let ids = field.["optionIds"]
            keys ids (arr field.["options"] |> List.map str)
            for option in arr field.["options"] |> List.map str do nonempty ids option
            let values = arr field.["options"] |> List.map str |> List.map (s ids)
            require ((Set.ofList values |> Set.count) = values.Length) "duplicate option id"
    let plan = JsonObject()
    plan.["schema"] <- JsonValue.Create("fsgg.coordination-board-v2-pilot-plan/v1")
    plan.["target"] <- manifest.["target"].DeepClone()
    plan.["binding"] <- binding.DeepClone()
    plan.["fields"] <- manifest.["fields"].DeepClone()
    plan.["items"] <- JsonArray(pilots |> List.map (fun i -> i.DeepClone()) |> List.toArray)
    plan.["effects"] <- JsonNode.Parse("[\"fresh-source-and-native-dependencies\",\"exact-project-and-fields\",\"complete-membership-by-project-and-issue-node-id\",\"add-only-after-proven-absence\",\"seed-once-preserve-conflicting-owner-edits\",\"independent-readback\"]")
    plan.["limits"] <- JsonNode.Parse("{\"maxItems\":5,\"pageSize\":50,\"maxPages\":10,\"readRetries\":2,\"deadlineSeconds\":300,\"activeWriters\":1}")
    plan

[<EntryPoint>]
let main args =
    try
        require (args.Length = 1 || (args.Length = 2 && args.[1] = "--pilot-plan")) "usage: board-v2-import MANIFEST [--pilot-plan]"
        let manifest = JsonNode.Parse(File.ReadAllText args.[0])
        let items,pilots,created = validate manifest
        let result : JsonNode =
            if args.Length = 2 then pilotPlan manifest :> JsonNode
            else
                let summary = JsonObject()
                summary.["candidateCount"] <- JsonValue.Create(items.Length)
                summary.["approvedImportCount"] <- JsonValue.Create(items |> List.filter add |> List.length)
                summary.["pilotCount"] <- JsonValue.Create(pilots.Length)
                summary.["unresolvedCount"] <- JsonValue.Create(items |> List.filter (fun i -> s i "decision" = "adjudicate") |> List.length)
                summary.["planConstructible"] <- JsonValue.Create(try pilotPlan manifest |> ignore; true with _ -> false)
                summary.["executable"] <- JsonValue.Create(false)
                summary.["targetCreationState"] <- JsonValue.Create(s manifest.["target"] "creationState")
                summary :> JsonNode
        printfn "%s" (result.ToJsonString())
        0
    with error -> eprintfn "board-v2 import refused: %s" error.Message; 2
