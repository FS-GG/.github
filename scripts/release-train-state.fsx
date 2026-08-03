#load "release-train-lib.fsx"

/// Durable, fail-closed coordination over the producer-owned release evidence.
/// It intentionally does not pack, publish, tag, or edit a registry: those effects stay with
/// producer workflows.  It records the facts and tells an operator the one receipt still needed.
open System
open System.Collections.Generic
open System.IO
open System.Security.Cryptography
open System.Text.Json
open System.Text.Json.Nodes
open System.Text.RegularExpressions
open ReleaseTrain

let usage () =
    eprintfn "Usage: dotnet fsi scripts/release-train-state.fsx -- inspect --run FILE --audit FILE --workflows FILE [--registry FILE]"
    eprintfn "       dotnet fsi scripts/release-train-state.fsx -- plan --run FILE"
    eprintfn "       dotnet fsi scripts/release-train-state.fsx -- advance --run FILE --release-id ID --decision KIND --subject-commit SHA --evidence URL [--workflow-receipt FILE]"
    eprintfn "       dotnet fsi scripts/release-train-state.fsx -- verify --run FILE --verification FILE [--verification FILE ...]"
    eprintfn "       dotnet fsi scripts/release-train-state.fsx -- import --run FILE --receipt FILE"

let args = normalizedArgs ()

let option name = parseSimpleOption name args
let options name = optionValues name args
let require name = option name |> Option.defaultWith (fun () -> failwith $"{name} is required")
let requireFile name =
    let path = require name |> Path.GetFullPath
    if not (File.Exists path) then failwith $"{name} does not exist: {path}"
    path

let write path (node: JsonNode) =
    let settings = JsonSerializerOptions(WriteIndented = true)
    File.WriteAllText(path, node.ToJsonString(settings) + Environment.NewLine)

let read path =
    let text = File.ReadAllText path
    JsonNode.Parse text |> Option.ofObj |> Option.defaultWith (fun () -> failwith $"run state is empty: {path}")

let obj (node: JsonNode) =
    match node with
    | :? JsonObject as value -> value
    | _ -> failwith "expected JSON object"

let array (node: JsonNode) =
    match node with
    | :? JsonArray as value -> value
    | _ -> failwith "expected JSON array"

let tryProperty (name: string) (source: JsonObject) =
    match source[name] with
    | null -> None
    | value -> Some value

let property (name: string) (source: JsonObject) =
    tryProperty name source |> Option.defaultWith (fun () -> failwith $"state requires `{name}`")

let stringProperty (name: string) (source: JsonObject) =
    let value = property name source
    match value.GetValueKind() with
    | JsonValueKind.String -> value.GetValue<string>()
    | _ -> failwith $"state property `{name}` must be a string"

let intProperty (name: string) (source: JsonObject) =
    let value = property name source
    match value.GetValueKind() with
    | JsonValueKind.Number -> value.GetValue<int>()
    | _ -> failwith $"state property `{name}` must be a number"

let boolProperty fallback (name: string) (source: JsonObject) =
    match tryProperty name source with
    | Some value when value.GetValueKind() = JsonValueKind.True -> true
    | Some value when value.GetValueKind() = JsonValueKind.False -> false
    | _ -> fallback

let stringOr fallback (name: string) (source: JsonObject) =
    match tryProperty name source with
    | Some value when value.GetValueKind() = JsonValueKind.String -> value.GetValue<string>()
    | _ -> fallback

let sha256 (path: string) =
    File.ReadAllBytes path |> SHA256.HashData |> Convert.ToHexString |> fun value -> value.ToLowerInvariant()

let evidence (path: string) =
    JsonObject(
        [ KeyValuePair("path", JsonValue.Create(path) :> JsonNode)
          KeyValuePair("sha256", JsonValue.Create(sha256 path) :> JsonNode) ])

let receiptCurrent (receipt: JsonObject) =
    let path = stringProperty "path" receipt
    File.Exists path && String.Equals(sha256 path, stringProperty "sha256" receipt, StringComparison.Ordinal)

let tryElement (name: string) (root: JsonElement) =
    let mutable value = Unchecked.defaultof<JsonElement>
    if root.TryGetProperty(name, &value) then Some value else None

let validateReport path requiredArray errorArray =
    use document = JsonDocument.Parse(File.ReadAllText path)
    let root = document.RootElement
    let required = tryElement requiredArray root |> Option.defaultWith (fun () -> failwith $"evidence receipt {path} is missing `{requiredArray}`")
    let hasErrors =
        required.EnumerateArray()
        |> Seq.exists (fun item ->
            match tryElement errorArray item with
            | Some errors -> errors.GetArrayLength() > 0
            | None -> failwith $"evidence receipt {path} is missing `{errorArray}`")
    if hasErrors then failwith $"evidence receipt {path} reports {errorArray}"

let requireStringElement path name (root: JsonElement) =
    match tryElement name root with
    | Some value when value.ValueKind = JsonValueKind.String && not (String.IsNullOrWhiteSpace(value.GetString())) -> value.GetString()
    | _ -> failwith $"evidence receipt {path} is missing non-empty `{name}`"

let validateWorkflowReceipt path =
    use document = JsonDocument.Parse(File.ReadAllText path)
    let root = document.RootElement
    let releaseId = requireStringElement path "releaseId" root
    let subjectCommit = requireStringElement path "subjectCommit" root
    let workflowRun = requireStringElement path "workflowRun" root
    if requireStringElement path "conclusion" root <> "success" then
        failwith $"workflow receipt {path} is not successful"
    releaseId, subjectCommit, workflowRun

let requireBooleanElement path name (root: JsonElement) =
    match tryElement name root with
    | Some value when value.ValueKind = JsonValueKind.True -> true
    | Some value when value.ValueKind = JsonValueKind.False -> false
    | _ -> failwith $"evidence receipt {path} is missing boolean `{name}`"

let validateVerificationReceipt path =
    use document = JsonDocument.Parse(File.ReadAllText path)
    let root = document.RootElement
    let name = requireStringElement path "name" root
    let subjectCommit = requireStringElement path "subjectCommit" root
    if root.GetProperty("schemaVersion").GetInt32() < 2 then failwith $"verification receipt {path} has unsupported schemaVersion"
    if requireStringElement path "expectedCommit" root <> subjectCommit then failwith $"verification receipt {path} binds different expected and subject commits"
    if requireStringElement path "conclusion" root <> "success" then
        failwith $"verification receipt {path} is not successful"
    [ "generatedAt"; "tag"; "expectedPackages"; "observedPackages"; "tagCommit"; "tagMatchesExpectedCommit"; "packages" ]
    |> List.iter (fun name -> if tryElement name root |> Option.isNone then failwith $"evidence receipt {path} is missing `{name}`")
    let packages = tryElement "packages" root |> Option.get
    if packages.ValueKind <> JsonValueKind.Array then failwith $"evidence receipt {path} has non-array `packages`"
    packages.EnumerateArray()
    |> Seq.iter (fun package ->
        [ "packageId"; "version"; "gitHubUrl"; "nuGetUrl"; "gitHubArchiveSha256"; "nuGetArchiveSha256"; "payloadFiles"; "payloadIdentical"; "differences"; "gitHubAvailable"; "nuGetAvailable" ]
        |> List.iter (fun field -> if tryElement field package |> Option.isNone then failwith $"verification receipt {path} package is missing `{field}`")
        let differences = package.GetProperty("differences")
        if differences.ValueKind <> JsonValueKind.Array then failwith $"verification receipt {path} package has non-array `differences`"
        differences.EnumerateArray()
        |> Seq.iter (fun difference ->
            if difference.ValueKind <> JsonValueKind.String || String.IsNullOrWhiteSpace(difference.GetString()) then
                failwith $"verification receipt {path} package has a non-string or empty difference"))
    name, subjectCommit, requireBooleanElement path "gitHubAvailable" root, requireBooleanElement path "nuGetAvailable" root

let validateCompletionReceipt path =
    use document = JsonDocument.Parse(File.ReadAllText path)
    let root = document.RootElement
    let kind = requireStringElement path "kind" root
    if requireStringElement path "conclusion" root <> "success" then failwith $"completion receipt {path} is not successful"
    match kind with
    | "consumer-embedding" -> kind, Some(requireStringElement path "releaseId" root), Some(requireStringElement path "subjectCommit" root), Some(requireStringElement path "producerId" root), None, None, None
    | "propagation" -> kind, Some(requireStringElement path "releaseId" root), Some(requireStringElement path "subjectCommit" root), None, None, None, None
    | "canonical-registry" ->
        if not (requireBooleanElement path "canonicalMerged" root) || not (requireBooleanElement path "projectionCurrent" root) then
            failwith $"canonical registry receipt {path} does not attest merged canonical registry and current projection"
        kind, None, None, None, Some(requireStringElement path "registryPath" root |> Path.GetFullPath), Some(requireStringElement path "registrySha256" root), Some(requireStringElement path "registryTopologySha256" root)
    | _ -> failwith $"completion receipt {path} has unsupported kind `{kind}`"

let registryEdges path =
    let mutable currentId = ""
    let mutable owner = ""
    let rows = ResizeArray<string * string * string list>()
    let mutable consumers = []
    let emitCurrent () =
        if currentId <> "" && owner <> "" then rows.Add(currentId, owner, consumers)
    for line in File.ReadLines path do
        let contract = Regex.Match(line, @"^\s*-\s+id:\s*([^\s#]+)")
        let ownerMatch = Regex.Match(line, @"^\s+owner:\s*([^\s#]+)")
        let consumersMatch = Regex.Match(line, @"^\s+consumers:\s*\[([^\]]*)\]")
        if contract.Success then
            emitCurrent (); currentId <- contract.Groups[1].Value; owner <- ""; consumers <- []
        elif ownerMatch.Success && currentId <> "" then owner <- ownerMatch.Groups[1].Value
        elif consumersMatch.Success && currentId <> "" then
            consumers <- consumersMatch.Groups[1].Value.Split(',', StringSplitOptions.RemoveEmptyEntries) |> Array.map (fun value -> value.Trim()) |> Array.toList
    emitCurrent ()
    rows |> Seq.toList

let topologySha256 path =
    registryEdges path
    |> List.sort
    |> List.map (fun (contract, owner, consumers) ->
        let consumerIds = String.concat "," consumers
        $"{contract}|{owner}|{consumerIds}")
    |> String.concat "\n"
    |> System.Text.Encoding.UTF8.GetBytes
    |> SHA256.HashData
    |> Convert.ToHexString
    |> fun value -> value.ToLowerInvariant()

let normalizeRepoId id = if id = ".github" then "github" else id

let validateEvidence requireRegistry (state: JsonObject) =
    let evidenceRoot = obj (property "evidence" state)
    let registry = obj (property "registry" state)
    let audit = obj (property "audit" evidenceRoot)
    let workflows = obj (property "workflows" evidenceRoot)
    if not (receiptCurrent audit) then failwith "audit evidence is stale or unavailable"
    if not (receiptCurrent workflows) then failwith "workflow evidence is stale or unavailable"
    validateReport (stringProperty "path" audit) "repositories" "findings"
    validateReport (stringProperty "path" workflows) "results" "errors"
    if requireRegistry && not (receiptCurrent registry) then failwith "registry topology evidence is stale or unavailable"
    let workflowReceipts =
        array (property "workflowReceipts" evidenceRoot)
        |> Seq.map (fun item ->
        let receipt = obj item
        if not (receiptCurrent receipt) then failwith "workflow receipt is stale or unavailable"
        validateWorkflowReceipt (stringProperty "path" receipt))
        |> Seq.toList
    array (property "releases" state)
    |> Seq.map obj
    |> Seq.iter (fun release ->
        let workflowRun = stringOr "" "workflowRun" release
        if not (String.IsNullOrWhiteSpace workflowRun) then
            let releaseId = stringProperty "id" release
            let mainCommit = stringProperty "mainCommit" release
            if not (workflowReceipts |> List.exists (fun (id, commit, run) -> id = releaseId && commit = mainCommit && run = workflowRun)) then
                failwith $"release `{releaseId}` has no current successful workflow receipt bound to its merged-main commit")
    array (property "verifications" evidenceRoot)
    |> Seq.iter (fun item ->
        let receipt = obj item
        if not (receiptCurrent receipt) then failwith "verification evidence is stale or unavailable"
        validateVerificationReceipt (stringProperty "path" receipt) |> ignore)
    let completions =
        array (property "completionReceipts" evidenceRoot)
        |> Seq.map (fun item ->
            let receipt = obj item
            if not (receiptCurrent receipt) then failwith "completion receipt is stale or unavailable"
            validateCompletionReceipt (stringProperty "path" receipt))
        |> Seq.toList
    let hasCompletion kind releaseId subjectCommit =
        completions |> List.exists (fun (receiptKind, receiptRelease, receiptCommit, _, _, _, _) -> receiptKind = kind && receiptRelease = Some releaseId && receiptCommit = Some subjectCommit)
    array (property "releases" state)
    |> Seq.map obj
    |> Seq.iter (fun release ->
        let id = stringProperty "id" release
        let commit = stringProperty "mainCommit" release
        if stringOr "producer" "kind" release = "consumer" && boolProperty false "consumerEmbeddingVerified" release && not (hasCompletion "consumer-embedding" id commit) then
            failwith $"consumer `{id}` has no current embedding receipt"
        if boolProperty false "downstreamVerified" release && not (hasCompletion "propagation" id commit) then
            failwith $"release `{id}` has no current propagation receipt")
    if boolProperty false "canonicalMerged" registry then
        let path = stringProperty "path" registry
        let hash = stringProperty "sha256" registry
        let canonicalPath = stringProperty "canonicalPath" registry
        let topology = stringProperty "canonicalTopologySha256" registry
        if path <> canonicalPath || topologySha256 path <> topology then failwith "canonical registry no longer matches the run's inspected identity and topology"
        if not (completions |> List.exists (fun (kind, _, _, _, receiptPath, receiptHash, receiptTopology) -> kind = "canonical-registry" && receiptPath = Some path && receiptHash = Some hash && receiptTopology = Some topology)) then
            failwith "registry is marked canonical without a current canonical-registry receipt"

let command = args |> List.tryHead |> Option.defaultWith (fun () -> usage (); failwith "a command is required")

let orderedReleases (items: JsonArray) =
    let byId =
        items
        |> Seq.map (fun item -> let release = obj item in stringProperty "id" release, release)
        |> Map.ofSeq
    let rec visit visited visiting id =
        if Set.contains id visiting then failwith $"release dependency cycle includes `{id}`"
        elif Set.contains id visited then visited, []
        else
            let release = byId |> Map.tryFind id |> Option.defaultWith (fun () -> failwith $"unknown release dependency `{id}`")
            let dependencies =
                match tryProperty "dependsOn" release with
                | Some values -> array values |> Seq.map (fun value -> value.GetValue<string>()) |> Seq.toList
                | _ -> []
            let visited, ordered =
                dependencies
                |> List.fold (fun (seen, result) dependency ->
                    let nextSeen, next = visit seen (Set.add id visiting) dependency
                    nextSeen, result @ next) (visited, [])
            Set.add id visited, ordered @ [ release ]
    items
    |> Seq.fold (fun (seen, result) item ->
        let seen, next = visit seen Set.empty (stringProperty "id" (obj item))
        seen, result @ next) (Set.empty, [])
    |> snd
    |> List.distinctBy (stringProperty "id")

let action (kind: string) (releaseId: string option) (missing: string) (terminal: bool) =
    JsonObject(
        [ KeyValuePair("kind", JsonValue.Create(kind) :> JsonNode)
          KeyValuePair("releaseId", match releaseId with Some value -> JsonValue.Create(value) :> JsonNode | None -> null)
          KeyValuePair("missingReceipt", JsonValue.Create(missing) :> JsonNode)
          KeyValuePair("terminal", JsonValue.Create(terminal) :> JsonNode) ])

let feedState (release: JsonObject) = stringOr "none" "feedState" release

let rec nextAction (state: JsonObject) =
    let registry = obj (property "registry" state)
    let allReleases = array (property "releases" state) |> orderedReleases
    let decisions =
        match tryProperty "decisions" state with
        | Some values -> array values |> Seq.map obj |> Seq.toList
        | None -> []
    let decision kind releaseId =
        decisions
        |> List.exists (fun item ->
            stringProperty "kind" item = kind
            && stringOr "" "releaseId" item = releaseId)
    let blocked =
        decisions
        |> List.tryFind (fun item -> stringProperty "kind" item = "human-blocker")
    match blocked with
    | Some item ->
        action "human-escalation" (Some(stringOr "" "releaseId" item))
            "explicit human-blocker decision and its evidence must be resolved" true
    | None when boolProperty false "requiresClassification" state ->
        match allReleases |> List.tryFind (fun release ->
            let id = stringProperty "id" release
            not (decision "release-owed" id) && not (decision "no-release" id)) with
        | Some release ->
            action "classify-release" (Some(stringProperty "id" release))
                "explicit release-owed or no-release classification bound to the current merged-main commit" false
        | None ->
            let releases = allReleases |> List.filter (fun release -> decision "release-owed" (stringProperty "id" release))
            let scoped = JsonArray(releases |> List.map (fun release -> release.DeepClone()) |> List.toArray)
            let state = JsonObject([ KeyValuePair("registry", registry.DeepClone()); KeyValuePair("releases", scoped :> JsonNode) ])
            nextAction state
    | None ->
        let releases = allReleases
        let artifactEligible release =
            stringOr "producer" "kind" release <> "consumer"
            || boolProperty false "consumerEmbeddingVerified" release
        let artifactReleases = releases |> List.filter artifactEligible
        let terminalFeed =
            releases
            |> List.tryFind (fun release -> [ "org-only"; "public-only"; "disagree" ] |> List.contains (feedState release))
        match terminalFeed with
        | Some release ->
            action "human-escalation" (Some(stringProperty "id" release))
                $"dual-feed receipt is `{feedState release}`; inspect immutable artifacts and record a human decision" true
        | None ->
            match artifactReleases |> List.tryFind (fun release -> intProperty "expectedPackages" release <> intProperty "observedPackages" release) with
            | Some release -> action "verify-packages" (Some(stringProperty "id" release)) "expected-versus-observed package count receipt" false
            | None ->
                match artifactReleases |> List.tryFind (fun release -> stringOr "" "mainCommit" release <> stringOr "" "tagCommit" release) with
                | Some release -> action "verify-tag" (Some(stringProperty "id" release)) "exact tag-to-merged-main commit receipt" false
                | None ->
                    match artifactReleases |> List.tryFind (fun release -> String.IsNullOrWhiteSpace(stringOr "" "workflowRun" release)) with
                    | Some release -> action "await-workflow" (Some(stringProperty "id" release)) "successful producer workflow run bound to the release commit" false
                    | None ->
                        let ready id =
                            let producer = releases |> List.find (fun release -> stringProperty "id" release = id)
                            feedState producer = "both-equivalent"
                            && boolProperty false "artifactVerified" producer
                        let consumerBlocked =
                            releases
                            |> List.tryFind (fun release ->
                                stringOr "producer" "kind" release = "consumer"
                                && (match tryProperty "dependsOn" release with
                                    | Some value -> array value |> Seq.exists (fun dependency -> not (ready (dependency.GetValue<string>())))
                                    | _ -> false))
                        match consumerBlocked with
                        | Some release -> action "await-producer" (Some(stringProperty "id" release)) "verified producer artifact and consumer pin receipt" false
                        | None ->
                            match releases |> List.tryFind (fun release -> stringOr "producer" "kind" release = "consumer" && not (boolProperty false "consumerEmbeddingVerified" release)) with
                            | Some release -> action "verify-consumer" (Some(stringProperty "id" release)) "fresh consumer or retrofit materialization receipt" false
                            | None ->
                                match releases |> List.tryFind (fun release -> feedState release = "none" || not (boolProperty false "artifactVerified" release)) with
                                | Some release -> action "publish" (Some(stringProperty "id" release)) "dual-feed, payload-equivalence, tag, workflow, and expected-package receipt" false
                                | None ->
                                    match releases |> List.tryFind (fun release -> not (boolProperty false "downstreamVerified" release)) with
                                    | Some release -> action "verify-propagation" (Some(stringProperty "id" release)) "dispatch or Renovate propagation receipt" false
                                    | None when not (boolProperty false "canonicalMerged" registry) ->
                                        action "flip-registry" None "re-read merged canonical registry/projection after all packages are live" false
                                    | None -> action "complete" None "all release, consumer, propagation, and canonical-registry receipts are present" true

let inspect () =
    let run = require "--run" |> Path.GetFullPath
    let auditPath = requireFile "--audit"
    let workflowPath = requireFile "--workflows"
    let registryPath = option "--registry" |> Option.defaultValue "registry/dependencies.yml" |> Path.GetFullPath
    if not (File.Exists registryPath) then failwith $"registry does not exist: {registryPath}"
    validateReport auditPath "repositories" "findings"
    validateReport workflowPath "results" "errors"
    let topology = registryEdges registryPath
    use audit = JsonDocument.Parse(File.ReadAllText auditPath)
    let auditSets =
        audit.RootElement.GetProperty("repositories").EnumerateArray()
        |> Seq.collect (fun repository ->
            let repoId = repository.GetProperty("id").GetString()
            let commit = repository.GetProperty("originMain").GetString()
            match tryElement "releaseSets" repository with
            | Some sets when sets.ValueKind = JsonValueKind.Array && sets.GetArrayLength() > 0 ->
                sets.EnumerateArray()
                |> Seq.map (fun releaseSet ->
                    let releaseSetId = releaseSet.GetProperty("id").GetString()
                    let expectedTags = releaseSet.GetProperty("expectedTags").EnumerateArray() |> Seq.map _.GetString() |> Seq.toList
                    if expectedTags.IsEmpty then failwith $"release set `{releaseSetId}` has no expected tag"
                    let patterns = releaseSet.GetProperty("tagPatterns").EnumerateArray() |> Seq.map _.GetString() |> Seq.toList
                    let packages = releaseSet.GetProperty("packages").EnumerateArray() |> Seq.toList
                    releaseSetId, repoId, commit,
                    releaseSet.GetProperty("baselineTag").GetString(), releaseSet.GetProperty("workflow").GetString(),
                    expectedTags, patterns, packages)
            | _ ->
                let packages = repository.GetProperty("packages").EnumerateArray() |> Seq.toList
                if packages.IsEmpty then Seq.empty
                else
                    let tag = repository.GetProperty("baselineTag").GetString()
                    Seq.singleton(repoId, repoId, commit, tag, "", [ tag ], [], packages))
        |> Seq.toList
    let contractRelease =
        topology
        |> List.choose (fun (contract, owner, consumers) ->
            let ownerId = normalizeRepoId owner
            let candidates = auditSets |> List.filter (fun (_, repoId, _, _, _, _, _, _) -> normalizeRepoId repoId = ownerId)
            match candidates with
            | [ (releaseId, _, _, _, _, _, _, _) ] -> Some(contract, releaseId, consumers)
            | _ ->
                candidates
                |> List.tryFind (fun (releaseId, _, _, _, _, expectedTags, _, _) ->
                    releaseId.EndsWith($":{contract}", StringComparison.Ordinal)
                    || expectedTags |> List.exists (fun tag -> tag.StartsWith($"{contract}/", StringComparison.Ordinal)))
                |> Option.map (fun (releaseId, _, _, _, _, _, _, _) -> contract, releaseId, consumers))
    let packageRelease =
        auditSets
        |> List.collect (fun (releaseId, _, _, _, _, _, _, packages) ->
            packages
            |> List.choose (fun package ->
                match tryElement "packageId" package with
                | Some value when value.ValueKind = JsonValueKind.String -> Some(value.GetString(), releaseId)
                | _ -> None))
        |> Map.ofList
    let releases = JsonArray()
    for releaseId, repoId, commit, baselineTag, workflow, expectedTags, tagPatterns, packages in auditSets do
        let normalizedRepo = normalizeRepoId repoId
        let packageDependencies =
            packages
            |> List.collect (fun package ->
                match tryElement "packageReferences" package with
                | Some refs when refs.ValueKind = JsonValueKind.Array -> refs.EnumerateArray() |> Seq.map _.GetString() |> Seq.toList
                | _ -> [])
            |> List.choose (fun packageId -> Map.tryFind packageId packageRelease)
        let registryDependencies =
            contractRelease
            |> List.choose (fun (_, producerRelease, consumers) ->
                if consumers |> List.map normalizeRepoId |> List.contains normalizedRepo then Some producerRelease else None)
        let dependencies =
            packageDependencies @ registryDependencies
            |> List.filter ((<>) releaseId)
            |> List.distinct
            |> List.sort
        let coherentSets =
            contractRelease
            |> List.choose (fun (contract, ownerRelease, _) -> if ownerRelease = releaseId then Some contract else None)
        let packageIds =
            packages
            |> List.choose (fun package ->
                match tryElement "packageId" package with
                | Some value when value.ValueKind = JsonValueKind.String -> Some(value.GetString())
                | _ -> None)
        let expectedArtifacts =
            packages
            |> List.choose (fun package ->
                match tryElement "packageId" package, tryElement "version" package with
                | Some packageId, Some version when packageId.ValueKind = JsonValueKind.String && version.ValueKind = JsonValueKind.String ->
                    Some(JsonObject(
                        [ KeyValuePair("packageId", JsonValue.Create(packageId.GetString()) :> JsonNode)
                          KeyValuePair("version", JsonValue.Create(version.GetString()) :> JsonNode) ]) :> JsonNode)
                | _ -> None)
        releases.Add(JsonObject(
            [ KeyValuePair("id", JsonValue.Create(releaseId) :> JsonNode)
              KeyValuePair("repository", JsonValue.Create(repoId) :> JsonNode)
              KeyValuePair("kind", JsonValue.Create(if List.isEmpty dependencies then "producer" else "consumer") :> JsonNode)
              KeyValuePair("dependsOn", JsonArray(dependencies |> List.map (fun value -> JsonValue.Create(value) :> JsonNode) |> List.toArray) :> JsonNode)
              KeyValuePair("coherentSets", JsonArray(coherentSets |> List.map (fun value -> JsonValue.Create(value) :> JsonNode) |> List.toArray) :> JsonNode)
              KeyValuePair("packages", JsonArray(packageIds |> List.map (fun value -> JsonValue.Create(value) :> JsonNode) |> List.toArray) :> JsonNode)
              KeyValuePair("expectedArtifacts", JsonArray(expectedArtifacts |> List.toArray) :> JsonNode)
              KeyValuePair("expectedPackages", JsonValue.Create(packages.Length) :> JsonNode)
              KeyValuePair("observedPackages", JsonValue.Create(0) :> JsonNode)
              KeyValuePair("mainCommit", JsonValue.Create(commit) :> JsonNode)
              KeyValuePair("baselineTag", JsonValue.Create(baselineTag) :> JsonNode)
              KeyValuePair("tag", JsonValue.Create(expectedTags.Head) :> JsonNode)
              KeyValuePair("expectedTags", JsonArray(expectedTags |> List.map (fun value -> JsonValue.Create(value) :> JsonNode) |> List.toArray) :> JsonNode)
              KeyValuePair("tagPatterns", JsonArray(tagPatterns |> List.map (fun value -> JsonValue.Create(value) :> JsonNode) |> List.toArray) :> JsonNode)
              KeyValuePair("releaseWorkflow", JsonValue.Create(workflow) :> JsonNode)
              KeyValuePair("tagCommit", JsonValue.Create("") :> JsonNode)
              KeyValuePair("workflowRun", JsonValue.Create("") :> JsonNode)
              KeyValuePair("feedState", JsonValue.Create("none") :> JsonNode)
              KeyValuePair("artifactVerified", JsonValue.Create(false) :> JsonNode)
              KeyValuePair("consumerEmbeddingVerified", JsonValue.Create(List.isEmpty dependencies) :> JsonNode)
              KeyValuePair("downstreamVerified", JsonValue.Create(false) :> JsonNode) ]))
    let runId = option "--id" |> Option.defaultValue (Path.GetFileNameWithoutExtension run)
    let state = JsonObject(
        [ KeyValuePair("schemaVersion", JsonValue.Create(2) :> JsonNode)
          KeyValuePair("runId", JsonValue.Create(runId) :> JsonNode)
          KeyValuePair("registry", JsonObject([ KeyValuePair("path", JsonValue.Create(registryPath) :> JsonNode); KeyValuePair("sha256", JsonValue.Create(sha256 registryPath) :> JsonNode); KeyValuePair("canonicalPath", JsonValue.Create(registryPath) :> JsonNode); KeyValuePair("canonicalTopologySha256", JsonValue.Create(topologySha256 registryPath) :> JsonNode); KeyValuePair("canonicalMerged", JsonValue.Create(false) :> JsonNode) ]) :> JsonNode)
          KeyValuePair("evidence", JsonObject([ KeyValuePair("audit", evidence auditPath :> JsonNode); KeyValuePair("workflows", evidence workflowPath :> JsonNode); KeyValuePair("workflowReceipts", JsonArray() :> JsonNode); KeyValuePair("verifications", JsonArray() :> JsonNode); KeyValuePair("completionReceipts", JsonArray() :> JsonNode) ]) :> JsonNode)
          KeyValuePair("decisions", JsonArray() :> JsonNode)
          KeyValuePair("requiresClassification", JsonValue.Create(true) :> JsonNode)
          KeyValuePair("releases", releases :> JsonNode) ])
    state["nextAction"] <- nextAction state
    write run state
    printfn "%s" (state.ToJsonString(JsonSerializerOptions(WriteIndented = true)))

let plan () =
    let path = requireFile "--run"
    let state = obj (read path)
    if not (List.contains (intProperty "schemaVersion" state) [ 1; 2 ]) then failwith "unsupported release-run schemaVersion"
    validateEvidence true state
    state["nextAction"] <- nextAction state
    write path state
    printfn "%s" (state["nextAction"].ToJsonString(JsonSerializerOptions(WriteIndented = true)))

let advance () =
    let path = requireFile "--run"
    let state = obj (read path)
    validateEvidence true state
    let decision = require "--decision"
    if not ([ "release-owed"; "no-release"; "semver-effect"; "human-blocker" ] |> List.contains decision) then
        failwith "--decision must be release-owed, no-release, semver-effect, or human-blocker"
    let decisions = array (property "decisions" state)
    let subjectCommit = require "--subject-commit"
    let proof = require "--evidence"
    let releaseId = require "--release-id"
    if String.IsNullOrWhiteSpace proof then failwith "--evidence must not be empty"
    let release =
        array (property "releases" state)
        |> Seq.map obj
        |> Seq.tryFind (fun item -> stringProperty "id" item = releaseId)
        |> Option.defaultWith (fun () -> failwith $"unknown release `{releaseId}`")
    if subjectCommit <> stringProperty "mainCommit" release then
        failwith "decision subject commit must equal the release's merged-main commit"
    match option "--workflow-receipt" with
    | Some receiptPath when decision = "release-owed" || decision = "semver-effect" ->
        let full = Path.GetFullPath receiptPath
        if not (File.Exists full) then failwith $"--workflow-receipt does not exist: {full}"
        let receiptRelease, receiptCommit, workflowRun = validateWorkflowReceipt full
        if receiptRelease <> releaseId || receiptCommit <> subjectCommit then
            failwith "workflow receipt must name this release and its merged-main commit"
        release["workflowRun"] <- JsonValue.Create(workflowRun)
        let receipts = array (property "workflowReceipts" (obj (property "evidence" state)))
        let snapshot = evidence full
        if not (receipts |> Seq.map obj |> Seq.exists (fun current -> stringProperty "sha256" current = stringProperty "sha256" snapshot)) then receipts.Add(snapshot)
    | Some _ -> failwith "--workflow-receipt is only valid with release-owed or semver-effect"
    | None when decision = "release-owed" || decision = "semver-effect" ->
        failwith "release-owed and semver-effect decisions require a successful --workflow-receipt"
    | None -> ()
    let duplicate =
        decisions |> Seq.exists (fun item -> let existing = obj item in stringProperty "kind" existing = decision && stringProperty "subjectCommit" existing = subjectCommit && stringOr "" "releaseId" existing = releaseId)
    if not duplicate then
        decisions.Add(JsonObject([ KeyValuePair("kind", JsonValue.Create(decision) :> JsonNode); KeyValuePair("releaseId", JsonValue.Create(releaseId) :> JsonNode); KeyValuePair("subjectCommit", JsonValue.Create(subjectCommit) :> JsonNode); KeyValuePair("evidence", JsonValue.Create(proof) :> JsonNode) ]))
    state["nextAction"] <- nextAction state
    write path state
    printfn "%s" (state["nextAction"].ToJsonString(JsonSerializerOptions(WriteIndented = true)))

let verify () =
    let path = requireFile "--run"
    let state = obj (read path)
    validateEvidence true state
    let releases = array (property "releases" state)
    let evidenceRoot = obj (property "evidence" state)
    let reports = array (property "verifications" evidenceRoot)
    for reportPath in options "--verification" do
        let full = Path.GetFullPath reportPath
        if not (File.Exists full) then failwith $"--verification does not exist: {full}"
        let receiptName, receiptCommit, githubAvailable, nugetAvailable = validateVerificationReceipt full
        use report = JsonDocument.Parse(File.ReadAllText full)
        let root = report.RootElement
        let name = root.GetProperty("name").GetString()
        if name <> receiptName then failwith $"verification receipt {full} is inconsistent"
        let release = releases |> Seq.map obj |> Seq.tryFind (fun candidate -> stringProperty "id" candidate = name) |> Option.defaultWith (fun () -> failwith $"verification names unknown release `{name}`")
        if receiptCommit <> stringProperty "mainCommit" release then failwith "verification receipt subject commit must equal the release's merged-main commit"
        let receiptTag = root.GetProperty("tag").GetString()
        let expectedTags =
            match tryProperty "expectedTags" release with
            | Some tags -> array tags |> Seq.map (fun tag -> tag.GetValue<string>()) |> Seq.toList
            | None -> [ stringProperty "tag" release ]
        if not (List.contains receiptTag expectedTags) then
            failwith $"verification receipt tag `{receiptTag}` does not match release `{name}` expected tags"
        let expectedArtifacts =
            match tryProperty "expectedArtifacts" release with
            | Some artifacts ->
                array artifacts
                |> Seq.map (fun artifact ->
                    let item = obj artifact
                    stringProperty "packageId" item, stringProperty "version" item)
                |> Seq.sort
                |> Seq.toList
            | None -> []
        let observedArtifacts =
            root.GetProperty("packages").EnumerateArray()
            |> Seq.map (fun package -> package.GetProperty("packageId").GetString(), package.GetProperty("version").GetString())
            |> Seq.sort
            |> Seq.toList
        let packageFeedFacts =
            root.GetProperty("packages").EnumerateArray()
            |> Seq.map (fun package ->
                package.GetProperty("gitHubAvailable").GetBoolean(),
                package.GetProperty("nuGetAvailable").GetBoolean(),
                package.GetProperty("payloadIdentical").GetBoolean(),
                (package.GetProperty("differences").EnumerateArray() |> Seq.length))
            |> Seq.toList
        if packageFeedFacts |> List.exists (fun (github, nuget, identical, differenceCount) -> identical <> (github && nuget && differenceCount = 0)) then
            failwith $"verification receipt package equivalence for release `{name}` contradicts feed availability or payload differences"
        let derivedGithubAvailable = not packageFeedFacts.IsEmpty && (packageFeedFacts |> List.forall (fun (github, _, _, _) -> github))
        let derivedNugetAvailable = not packageFeedFacts.IsEmpty && (packageFeedFacts |> List.forall (fun (_, nuget, _, _) -> nuget))
        if githubAvailable <> derivedGithubAvailable || nugetAvailable <> derivedNugetAvailable then
            failwith $"verification receipt aggregate feed availability does not match release `{name}` package rows"
        let auditedExpectedCount = intProperty "expectedPackages" release
        if not expectedArtifacts.IsEmpty && expectedArtifacts.Length <> auditedExpectedCount then
            failwith $"release `{name}` expected package count does not match its audited artifact set"
        if not expectedArtifacts.IsEmpty && observedArtifacts <> expectedArtifacts then
            failwith $"verification receipt package ID/version multiset does not match release `{name}` expected artifacts"
        let receiptExpectedCount = root.GetProperty("expectedPackages").GetInt32()
        if receiptExpectedCount <> auditedExpectedCount then
            failwith $"verification receipt expected package count does not match release `{name}` audited package count"
        let receiptObservedCount = root.GetProperty("observedPackages").GetInt32()
        if receiptObservedCount <> observedArtifacts.Length then
            failwith $"verification receipt observed package count does not match release `{name}` package rows"
        release["observedPackages"] <- JsonValue.Create(observedArtifacts.Length)
        release["tagCommit"] <- JsonValue.Create(root.GetProperty("tagCommit").GetString())
        let matchingTag = root.GetProperty("tagMatchesExpectedCommit").GetBoolean()
        let equivalent = not packageFeedFacts.IsEmpty && (packageFeedFacts |> List.forall (fun (github, nuget, _, differenceCount) -> github && nuget && differenceCount = 0))
        let complete = matchingTag && equivalent && intProperty "expectedPackages" release = intProperty "observedPackages" release
        let feed =
            match derivedGithubAvailable, derivedNugetAvailable with
            | true, true when complete -> "both-equivalent"
            | true, true -> "disagree"
            | true, false -> "org-only"
            | false, true -> "public-only"
            | false, false -> "none"
        release["feedState"] <- JsonValue.Create(feed)
        release["artifactVerified"] <- JsonValue.Create((feed = "both-equivalent"))
        reports.Add(evidence full)
    state["nextAction"] <- nextAction state
    write path state
    printfn "%s" (state["nextAction"].ToJsonString(JsonSerializerOptions(WriteIndented = true)))

let importReceipt () =
    let path = requireFile "--run"
    let state = obj (read path)
    let full = requireFile "--receipt"
    let kind, receiptRelease, receiptCommit, producerId, registryPath, registryHash, registryTopology = validateCompletionReceipt full
    if kind = "canonical-registry" then validateEvidence false state else validateEvidence true state
    let registry = obj (property "registry" state)
    let releases = array (property "releases" state) |> Seq.map obj |> Seq.toList
    let receipts = array (property "completionReceipts" (obj (property "evidence" state)))
    let snapshot = evidence full
    let release id =
        releases |> List.tryFind (fun item -> stringProperty "id" item = id)
        |> Option.defaultWith (fun () -> failwith $"completion receipt names unknown release `{id}`")
    match kind, receiptRelease, receiptCommit, producerId, registryPath, registryHash, registryTopology with
    | "consumer-embedding", Some id, Some commit, Some producer, _, _, _ ->
        let consumer = release id
        if stringOr "producer" "kind" consumer <> "consumer" || commit <> stringProperty "mainCommit" consumer then
            failwith "consumer embedding receipt must bind the consumer's merged-main commit"
        let dependencies = array (property "dependsOn" consumer) |> Seq.map (fun item -> item.GetValue<string>()) |> Set.ofSeq
        if not (Set.contains producer dependencies) then failwith "consumer embedding receipt names a non-dependency producer"
        let upstream = release producer
        if feedState upstream <> "both-equivalent" || not (boolProperty false "artifactVerified" upstream) then
            failwith "consumer embedding receipt requires the producer's verified dual-feed artifact"
        let currentAction = obj (nextAction state)
        let currentActionKind = stringProperty "kind" currentAction
        let currentActionRelease = stringOr "" "releaseId" currentAction
        let alreadyRecorded =
            receipts
            |> Seq.map obj
            |> Seq.exists (fun current -> stringProperty "sha256" current = stringProperty "sha256" snapshot)
        if boolProperty false "consumerEmbeddingVerified" consumer then
            if not alreadyRecorded then failwith "completed consumer embedding import must exactly match its recorded receipt"
        elif currentActionKind <> "verify-consumer" || currentActionRelease <> id then
            failwith $"consumer embedding receipt for `{id}` is only legal for its current verify-consumer action"
        consumer["consumerEmbeddingVerified"] <- JsonValue.Create(true)
    | "propagation", Some id, Some commit, _, _, _, _ ->
        let target = release id
        if commit <> stringProperty "mainCommit" target then failwith "propagation receipt must bind the release's merged-main commit"
        let currentAction = obj (nextAction state)
        let currentActionKind = stringProperty "kind" currentAction
        let currentActionRelease = stringOr "" "releaseId" currentAction
        let alreadyRecorded =
            receipts
            |> Seq.map obj
            |> Seq.exists (fun current -> stringProperty "sha256" current = stringProperty "sha256" snapshot)
        if boolProperty false "downstreamVerified" target then
            if not alreadyRecorded then failwith "completed propagation import must exactly match its recorded receipt"
        elif currentActionKind <> "verify-propagation" || currentActionRelease <> id then
            failwith $"propagation receipt for `{id}` is only legal for its current verify-propagation action"
        target["downstreamVerified"] <- JsonValue.Create(true)
    | "canonical-registry", None, None, _, Some receiptPath, Some receiptHash, Some receiptTopology ->
        let canonicalPath = stringProperty "canonicalPath" registry
        let expectedTopology = stringProperty "canonicalTopologySha256" registry
        let currentActionKind = stringProperty "kind" (obj (nextAction state))
        let alreadyRecorded =
            receipts
            |> Seq.map obj
            |> Seq.exists (fun current -> stringProperty "sha256" current = stringProperty "sha256" snapshot)
        if boolProperty false "canonicalMerged" registry then
            if currentActionKind <> "complete" || not alreadyRecorded then
                failwith "completed canonical registry import must exactly match its recorded receipt"
        elif currentActionKind <> "flip-registry" then
            failwith "canonical registry receipt is only legal for the current flip-registry action"
        if receiptPath <> canonicalPath then failwith "canonical registry receipt path must equal the inspected canonical registry target"
        if not (File.Exists canonicalPath) || sha256 canonicalPath <> receiptHash then failwith "canonical registry receipt is stale or does not match the inspected canonical target"
        if receiptTopology <> expectedTopology || topologySha256 canonicalPath <> expectedTopology then
            failwith "canonical registry receipt does not preserve the inspected registry topology"
        registry["sha256"] <- JsonValue.Create(receiptHash)
        registry["canonicalMerged"] <- JsonValue.Create(true)
    | _ -> failwith "completion receipt fields are inconsistent"
    if not (receipts |> Seq.map obj |> Seq.exists (fun current -> stringProperty "sha256" current = stringProperty "sha256" snapshot)) then receipts.Add(snapshot)
    validateEvidence true state
    state["nextAction"] <- nextAction state
    write path state
    printfn "%s" (state["nextAction"].ToJsonString(JsonSerializerOptions(WriteIndented = true)))

let selftest () =
    let release (id: string) (kind: string) (dependencies: string list) (feed: string) (expected: int) (observed: int) (tag: string) (tagCommit: string) (workflow: string) (artifact: bool) (embedding: bool) (downstream: bool) =
        JsonObject([ KeyValuePair("id", JsonValue.Create(id) :> JsonNode); KeyValuePair("kind", JsonValue.Create(kind) :> JsonNode); KeyValuePair("dependsOn", JsonArray(dependencies |> List.map (fun value -> JsonValue.Create(value) :> JsonNode) |> List.toArray) :> JsonNode); KeyValuePair("expectedPackages", JsonValue.Create(expected) :> JsonNode); KeyValuePair("observedPackages", JsonValue.Create(observed) :> JsonNode); KeyValuePair("mainCommit", JsonValue.Create(tag) :> JsonNode); KeyValuePair("tagCommit", JsonValue.Create(tagCommit) :> JsonNode); KeyValuePair("workflowRun", JsonValue.Create(workflow) :> JsonNode); KeyValuePair("feedState", JsonValue.Create(feed) :> JsonNode); KeyValuePair("artifactVerified", JsonValue.Create(artifact) :> JsonNode); KeyValuePair("consumerEmbeddingVerified", JsonValue.Create(embedding) :> JsonNode); KeyValuePair("downstreamVerified", JsonValue.Create(downstream) :> JsonNode) ])
    let state (releases: JsonObject list) (canonical: bool) = JsonObject([ KeyValuePair("schemaVersion", JsonValue.Create(1) :> JsonNode); KeyValuePair("runId", JsonValue.Create("fixture") :> JsonNode); KeyValuePair("registry", JsonObject([ KeyValuePair("canonicalMerged", JsonValue.Create(canonical) :> JsonNode) ]) :> JsonNode); KeyValuePair("releases", JsonArray(releases |> List.map (fun value -> value.DeepClone()) |> List.toArray) :> JsonNode) ])
    let kind value = obj (nextAction value) |> stringProperty "kind"
    if kind (state [] true) <> "complete" then failwith "current/no-release fixture failed"
    let producer = release "producer" "producer" [] "both-equivalent" 1 1 "a" "a" "run" true true true
    let consumer = release "consumer" "consumer" [ "producer" ] "none" 1 0 "b" "" "" false false false
    if kind (state [ consumer; producer ] false) <> "verify-consumer" then failwith "ordered consumer embedding fixture failed"
    if kind (state [ release "missing" "producer" [] "both-equivalent" 2 1 "a" "a" "run" true true true ] false) <> "verify-packages" then failwith "missing package fixture failed"
    if kind (state [ release "partial" "producer" [] "org-only" 1 1 "a" "a" "run" true true true ] false) <> "human-escalation" then failwith "partial publication fixture failed"
    if kind (state [ release "tag" "producer" [] "both-equivalent" 1 1 "a" "b" "run" true true true ] false) <> "verify-tag" then failwith "tag mismatch fixture failed"
    if kind (state [ producer ] false) <> "flip-registry" then failwith "stale registry fixture failed"
    if kind (state [ release "embed" "consumer" [] "both-equivalent" 1 1 "a" "a" "run" true false true ] true) <> "verify-consumer" then failwith "missing consumer embedding fixture failed"
    if kind (state [ producer ] true) <> "complete" then failwith "complete train fixture failed"
    printfn "release-train-state: self-test passed"

try
    match command with
    | "inspect" -> inspect ()
    | "plan" -> plan ()
    | "advance" -> advance ()
    | "verify" -> verify ()
    | "import" -> importReceipt ()
    | "selftest" -> selftest ()
    | "--help" | "-h" -> usage ()
    | _ -> usage (); failwith $"unknown command: {command}"
    exit exitOk
with ex ->
    eprintfn "release-train-state: %s" ex.Message
    exit exitFinding
