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

// The product qualifier is offline and separate from the historical organization constructor.
// File digests bind reviewed evidence; they do not turn caller-authored evidence into native proof.
let closedKeys (node: JsonNode) (names: string list) =
    keys node names
    require (node.AsObject().Count = names.Length && (names |> List.forall (fun name -> node.AsObject().ContainsKey name))) "missing property"

let productDigest value = (value: string).Length = 64 && digest value
let productRevision value = (value: string).Length = 40 && revision value

let sha256 (bytes: byte array) =
    Security.Cryptography.SHA256.HashData bytes |> Convert.ToHexString |> fun value -> value.ToLowerInvariant()

let readBoundedJson (path: string) =
    let full = Path.GetFullPath path
    let mutable current = FileInfo(full) :> FileSystemInfo
    while not (isNull current) do
        require ((current.Attributes &&& FileAttributes.ReparsePoint) = enum<FileAttributes> 0) "linked input path"
        current <- match current with
                   | :? FileInfo as file -> file.Directory :> FileSystemInfo
                   | :? DirectoryInfo as directory -> directory.Parent :> FileSystemInfo
                   | _ -> null
    require (File.Exists full && (File.GetAttributes full &&& FileAttributes.Directory) = enum<FileAttributes> 0) "regular input required"
    use stream = File.OpenRead full
    require (stream.Length <= 1048576L) "input exceeds 1 MiB"
    let bytes = Array.zeroCreate<byte> (int stream.Length)
    stream.ReadExactly bytes
    require (stream.ReadByte() = -1) "input changed while reading"
    let text = Text.UTF8Encoding(false, true).GetString bytes
    use document = System.Text.Json.JsonDocument.Parse text
    let rec duplicates (element: System.Text.Json.JsonElement) =
        if element.ValueKind = System.Text.Json.JsonValueKind.Object then
            let names = Collections.Generic.HashSet<string>(StringComparer.Ordinal)
            for entry in element.EnumerateObject() do
                require (names.Add entry.Name) ("duplicate property " + entry.Name)
                duplicates entry.Value
        elif element.ValueKind = System.Text.Json.JsonValueKind.Array then
            for value in element.EnumerateArray() do duplicates value
    duplicates document.RootElement
    bytes, JsonNode.Parse text

let referenced (baseDirectory: string) (reference: JsonNode) =
    closedKeys reference ["path"; "sha256"]
    require (productDigest(s reference "sha256")) "evidence digest required"
    let bytes,node = readBoundedJson (Path.Combine(baseDirectory, s reference "path"))
    require (sha256 bytes = s reference "sha256") "evidence digest differs"
    node

let distinctIds subject (values: string list) =
    require (values |> List.forall (String.IsNullOrWhiteSpace >> not)) (subject + " identity required")
    require ((Set.ofList values).Count = values.Length) ("duplicate " + subject + " identity")

let strictTimestamp node name =
    timestamp node name
    require (Regex.IsMatch(s node name, @"\A[0-9]{4}-[0-9]{2}-[0-9]{2}T[0-9]{2}:[0-9]{2}:[0-9]{2}(\.[0-9]{1,7})?(Z|[+-][0-9]{2}:[0-9]{2})\z")) ("timestamp offset required " + name)

let existingProduct (requestPath: string) =
    let _,request = readBoundedJson requestPath
    closedKeys request ["schema"; "selection"; "qualifier"; "adapter"; "populationRevision"; "metadata"; "currentness"; "adjudication"; "priorPopulation"]
    exact request "schema" "fsgg.board-v2-existing-product-request/1"
    let directory = Path.GetDirectoryName(Path.GetFullPath requestPath)
    let selection = request.["selection"]
    closedKeys selection ["authorization"; "organizationId"; "ownerKind"; "owner"; "repository"; "projectId"; "projectNumber"; "projectTitle"; "principal"; "selectedIssues"]
    exact selection "authorization" "root-selected"
    exact selection "organizationId" "O_kgDOEYAWYw"
    exact selection "ownerKind" "organization"
    exact selection "owner" "FS-GG"
    let repository = s selection "repository"
    require (Regex.IsMatch(repository, @"\AFS-GG/[A-Za-z0-9_.-]+\z")) "one owner-local repository required"
    let projectId = s selection "projectId"
    let projectNumber = integer selection "projectNumber"
    require (projectNumber > 1 && projectNumber <> 3 && projectId.StartsWith("PVT_") && not (List.contains projectId ["PVT_kwDOEYAWY84Bb08W"; "PVT_kwDOEYAWY84Bldpa"])) "legacy or organization target refused"
    nonempty selection "projectTitle"
    let selected = selection.["selectedIssues"]
    let cohort = selected.AsObject() |> Seq.map (fun entry -> entry.Key, str entry.Value) |> Seq.toList
    require (cohort.Length >= 1 && cohort.Length <= 5) "one to five selected issues required"
    distinctIds "issue reference" (cohort |> List.map snd)
    for node,reference in cohort do
        require (node.StartsWith("I_") && validRef reference && reference.StartsWith(repository + "#", StringComparison.Ordinal)) "foreign selected issue"
        let suffix = reference.Substring(repository.Length + 1)
        let mutable number = 0
        require (Int32.TryParse(suffix, &number) && number > 0 && suffix = string number) "noncanonical issue reference"
    let qualifier = request.["qualifier"]
    let adapter = request.["adapter"]
    for axis in [qualifier; adapter] do
        closedKeys axis ["recipeRevision"; "artifactSha256"]
        require ((s axis "recipeRevision").Length = 40 && (s axis "artifactSha256").Length = 64 && productRevision(s axis "recipeRevision") && productDigest(s axis "artifactSha256")) "recipe/artifact identity required"
    let assembly = Reflection.Assembly.GetExecutingAssembly()
    let actualArtifact = File.ReadAllBytes assembly.Location |> sha256
    require (s qualifier "artifactSha256" = actualArtifact) "qualifier assembly differs"
    let informational = assembly.GetCustomAttributes(typeof<Reflection.AssemblyInformationalVersionAttribute>, false)
                        |> Seq.cast<Reflection.AssemblyInformationalVersionAttribute> |> Seq.exactlyOne
    require (informational.InformationalVersion.EndsWith("+" + s qualifier "recipeRevision", StringComparison.Ordinal)) "qualifier source revision differs"
    require (s adapter "artifactSha256" <> actualArtifact && s adapter "recipeRevision" <> s qualifier "recipeRevision") "adapter and qualifier axes differ"
    let populationRevision = s request "populationRevision"
    require (isNull request.["populationRevision"] || (populationRevision.Length = 40 && productRevision populationRevision)) "population revision must be null or immutable"
    require (populationRevision <> s qualifier "recipeRevision" && populationRevision <> s adapter "recipeRevision") "population identity axis differs"
    let metadata = referenced directory request.["metadata"]
    closedKeys metadata ["additionalFindings"; "atomicSnapshot"; "calls"; "credentialPrincipal"; "firstCause"; "projects"; "scope"]
    require (isNull metadata.["firstCause"] && (arr metadata.["additionalFindings"]).IsEmpty) "metadata findings unresolved"
    require (not (b metadata "atomicSnapshot") && integer metadata "calls" >= 1 && integer metadata "calls" <= 18) "metadata bounds differ"
    closedKeys selection.["principal"] ["id"; "login"]
    closedKeys metadata.["credentialPrincipal"] ["id"; "login"]
    for name in ["id"; "login"] do
        nonempty selection.["principal"] name
        exact metadata.["credentialPrincipal"] name (s selection.["principal"] name)
    let projects = arr metadata.["projects"]
    distinctIds "project" (projects |> List.map (fun row -> s row.["target"] "projectId"))
    let observed = projects |> List.filter (fun row -> s row.["target"] "projectId" = projectId)
    require (observed.Length = 1) "exact observed project required"
    let observed = observed.Head
    closedKeys observed ["findings"; "identityAfter"; "membership"; "notRun"; "schema"; "target"; "values"]
    require ((arr observed.["findings"]).IsEmpty && (arr observed.["notRun"]).IsEmpty) "project evidence incomplete"
    let target = observed.["target"]
    closedKeys target ["expectedTitle"; "issueId"; "issueNumber"; "number"; "projectId"; "repository"]
    exact target "projectId" projectId
    exact target "repository" repository
    require (integer target "number" = projectNumber) "project number differs"
    exact target "expectedTitle" (s selection "projectTitle")
    let identity = observed.["identityAfter"]
    closedKeys identity ["closed"; "id"; "number"; "public"; "title"; "url"]
    require (not (b identity "closed") && integer identity "number" = projectNumber) "observed project closed or differs"
    exact identity "id" projectId
    exact identity "title" (s selection "projectTitle")
    exact identity "url" ($"https://github.com/orgs/FS-GG/projects/{projectNumber}")
    b identity "public" |> ignore
    let schema = observed.["schema"]
    closedKeys schema (fields |> List.map (fun (name,_,_,_,_) -> name))
    let fieldIds = fields |> List.map (fun (name,_,_,_,_) -> s schema.[name] "id")
    distinctIds "field" (projectId :: fieldIds)
    for name,kind,options,_,_ in fields do
        let field = schema.[name]
        closedKeys field (if kind = "text" then ["__typename"; "dataType"; "id"; "name"] else ["__typename"; "dataType"; "id"; "name"; "options"])
        exact field "name" name
        exact field "__typename" (if kind = "text" then "ProjectV2Field" else "ProjectV2SingleSelectField")
        exact field "dataType" (if kind = "text" then "TEXT" else "SINGLE_SELECT")
        if kind <> "text" then
            let actual = arr field.["options"]
            for option in actual do closedKeys option ["id"; "name"]
            require (actual.Length = options.Length && (actual |> List.map (fun option -> s option "name") |> Set.ofList) = Set.ofList options) (name + " option names differ")
            distinctIds (name + " option") (actual |> List.map (fun option -> s option "id"))
    let membership = observed.["membership"]
    closedKeys membership ["complete"; "issueIdentityCoverage"; "items"; "opaqueItemIds"; "pages"; "selectedAbsence"; "selectedMemberships"; "totalCount"]
    require (b membership "complete" && b membership "issueIdentityCoverage" && (arr membership.["opaqueItemIds"]).IsEmpty) "membership incomplete or opaque"
    let members = arr membership.["items"]
    require (integer membership "pages" >= 1 && integer membership "pages" <= 5 && integer membership "totalCount" = members.Length) "membership pages/count differ"
    distinctIds "membership" (members |> List.map (fun memberRow -> s memberRow "id"))
    for memberRow in members do closedKeys memberRow ["content"; "id"; "isArchived"; "type"]
    let chosen = cohort |> List.map (fun (node,reference) ->
        let matches = members |> List.filter (fun memberRow -> s memberRow.["content"] "id" = node)
        require (matches.Length = 1) "selected member absent or duplicated"
        let memberRow = matches.Head
        require (not (b memberRow "isArchived")) "selected member archived"
        exact memberRow "type" "ISSUE"
        let content = memberRow.["content"]
        closedKeys content ["__typename"; "id"; "number"; "repository"]
        exact content "__typename" "Issue"
        closedKeys content.["repository"] ["id"; "nameWithOwner"]
        nonempty content.["repository"] "id"
        exact content.["repository"] "nameWithOwner" repository
        require (reference = repository + "#" + string (integer content "number")) "member issue differs"
        memberRow)
    // The metadata route currently emits selected planning values for exactly one member.
    // Do not silently synthesize values for a wider cohort.
    require (cohort.Length = 1) "selected planning coverage supports one observed member"
    require (fst cohort.Head = s target "issueId" && snd cohort.Head = repository + "#" + string (integer target "issueNumber")) "metadata selected cohort differs"
    exact membership "selectedAbsence" "not-absent"
    require (JsonNode.DeepEquals(membership.["selectedMemberships"], JsonArray(chosen |> List.map (fun row -> row.DeepClone()) |> List.toArray))) "selected membership summary differs"
    let values = observed.["values"]
    closedKeys values ["allPayloadPreservation"; "otherValueTypes"; "selected"]
    require (not (b values "allPayloadPreservation")) "all-payload preservation is not qualified"
    let planning = values.["selected"]
    closedKeys planning ["Status"; "Roadmap"; "Track"; "Observation"]
    for name,kind,_,_,_ in fields do
        let value = planning.[name]
        closedKeys value (if kind = "text" then ["__typename"; "field"; "id"; "text"] else ["__typename"; "field"; "id"; "name"; "optionId"])
        nonempty value "id"
        exact value "__typename" (if kind = "text" then "ProjectV2ItemFieldTextValue" else "ProjectV2ItemFieldSingleSelectValue")
        require (JsonNode.DeepEquals(value.["field"], schema.[name])) "planning field join differs"
        if kind <> "text" then
            require (arr schema.[name].["options"] |> List.exists (fun option -> s option "id" = s value "optionId" && s option "name" = s value "name")) "planning option join differs"
    let prior = referenced directory request.["priorPopulation"]
    exact prior "schema" "fsgg.coordination-board-v2-import/v1"
    for name in ["ownerKind"; "owner"; "title"; "id"] do
        exact prior.["target"] name (if name = "title" then s selection "projectTitle" elif name = "id" then projectId else s selection name)
    require (integer prior.["target"] "number" = projectNumber) "prior target differs"
    // Source-currentness and root adjudication are joined below; metadata alone is insufficient.
    let currentness = referenced directory request.["currentness"]
    let adjudication = referenced directory request.["adjudication"]
    closedKeys currentness ["schema"; "targetKey"; "principal"; "organization"; "repository"; "issue"; "body"; "owningPlan"; "dependencies"; "revisionStable"; "requests"; "atomicSnapshot"; "mutations"; "historicalConstructorAuthenticated"]
    exact currentness "schema" "private.board-dot6.source-currentness/1"
    let targetKey = if repository = "FS-GG/FS.GG.Rendering" then "rendering" elif repository = "FS-GG/FS.GG.Game" then "game" else ""
    require (targetKey <> "") "product currentness scope unsupported"
    exact currentness "targetKey" targetKey
    require (JsonNode.DeepEquals(currentness.["principal"], selection.["principal"])) "source principal differs"
    closedKeys currentness.["organization"] ["id"; "login"]
    exact currentness.["organization"] "id" (s selection "organizationId")
    exact currentness.["organization"] "login" "FS-GG"
    closedKeys currentness.["repository"] ["id"; "nameWithOwner"]
    exact currentness.["repository"] "nameWithOwner" repository
    exact currentness.["repository"] "id" (s chosen.Head.["content"].["repository"] "id")
    require (b currentness "revisionStable" && not (b currentness "atomicSnapshot") && integer currentness "mutations" = 0 && not (b currentness "historicalConstructorAuthenticated")) "source revision or effect boundary differs"
    let issue = currentness.["issue"]
    closedKeys issue ["id"; "number"; "url"; "state"; "updatedAt"]
    exact issue "id" (fst cohort.Head)
    require (repository + "#" + string (integer issue "number") = snd cohort.Head) "source issue differs"
    let issueNumber = integer issue "number"
    let issueUrl = $"https://github.com/{repository}/issues/{issueNumber}"
    exact issue "url" issueUrl
    exact issue "state" "OPEN"
    strictTimestamp issue "updatedAt"
    let body = currentness.["body"]
    closedKeys body ["sha256"; "utf8Bytes"]
    require (productDigest(s body "sha256") && integer body "utf8Bytes" >= 1 && integer body "utf8Bytes" <= 1048576) "body identity/size invalid"
    let plan = currentness.["owningPlan"]
    closedKeys plan ["kind"; "url"; "issueId"; "updatedAt"; "bodySha256"; "nonempty"]
    exact plan "kind" "same-native-issue"
    exact plan "url" issueUrl
    exact plan "issueId" (s issue "id")
    exact plan "updatedAt" (s issue "updatedAt")
    exact plan "bodySha256" (s body "sha256")
    require (b plan "nonempty") "owning plan empty"
    let dependencies = currentness.["dependencies"]
    closedKeys dependencies ["complete"; "items"; "observedAt"]
    require (b dependencies "complete" && (arr dependencies.["items"]).IsEmpty) "complete empty dependencies required for selected pilot"
    strictTimestamp dependencies "observedAt"
    let requests = arr currentness.["requests"]
    require (requests.Length = 3) "three original source requests required"
    let expectedIndices = if targetKey = "rendering" then [1;3;5] else [2;4;6]
    let phases = ["issue-before"; "blocked-by"; "issue-after"]
    let mutable previousEnd = DateTimeOffset.MinValue
    for index,phase,requestRow in List.zip3 expectedIndices phases requests do
        closedKeys requestRow ["index"; "phase"; "method"; "url"; "startedAt"; "finishedAt"; "status"; "raw"; "metadata"]
        require (integer requestRow "index" = index && integer requestRow "status" = 200) "source request slot/status differs"
        exact requestRow "phase" phase
        exact requestRow "method" (if phase = "blocked-by" then "GET" else "POST")
        exact requestRow "url" (if phase = "blocked-by" then $"https://api.github.com/repos/{repository}/issues/{issueNumber}/dependencies/blocked_by?per_page=50" else "https://api.github.com/graphql")
        strictTimestamp requestRow "startedAt"
        strictTimestamp requestRow "finishedAt"
        let start = DateTimeOffset.Parse(s requestRow "startedAt")
        let finish = DateTimeOffset.Parse(s requestRow "finishedAt")
        require (start >= previousEnd && finish >= start) "source request ordering differs"
        previousEnd <- finish
        let load reference =
            closedKeys reference ["path"; "bytes"; "sha256"]
            require (Path.IsPathFullyQualified(s reference "path")) "source raw path must be absolute"
            require (integer reference "bytes" >= 0 && integer reference "bytes" <= 1048576 && productDigest(s reference "sha256")) "source raw reference bounds differ"
            let bytes,node = readBoundedJson (s reference "path")
            require (bytes.Length = integer reference "bytes" && sha256 bytes = s reference "sha256") "source raw reference differs"
            node
        let raw = load requestRow.["raw"]
        let status = load requestRow.["metadata"]
        closedKeys status ["index"; "targetKey"; "phase"; "method"; "url"; "startedAt"; "finishedAt"; "status"; "retainedBytes"; "sha256"; "bodyRetained"; "bodyComplete"; "truncated"; "transportFailure"; "additionalFailures"; "headers"]
        for name in ["phase"; "method"; "url"; "startedAt"; "finishedAt"] do exact status name (s requestRow name)
        exact status "targetKey" targetKey
        require (integer status "index" = index && b status "bodyRetained" && b status "bodyComplete" && not (b status "truncated") && isNull status.["transportFailure"] && (arr status.["additionalFailures"]).IsEmpty) "source status coverage incomplete"
        closedKeys status.["headers"] ["link"; "contentLength"; "contentType"; "requestId"; "rateLimitRemaining"]
        for name in ["link"; "contentLength"; "contentType"; "requestId"; "rateLimitRemaining"] do
            if not (isNull status.["headers"].[name]) then require (Text.Encoding.UTF8.GetByteCount(s status.["headers"] name) <= 4096) "header exceeds bound"
        // Normalization authenticity still belongs to the root-accepted original operation.
        require (integer status "status" = 200 && s status "sha256" = s requestRow.["raw"] "sha256" && integer status "retainedBytes" = integer requestRow.["raw"] "bytes") "source status/raw join differs"
        if phase = "blocked-by" then
            require ((arr raw).IsEmpty && isNull status.["headers"].["link"]) "raw dependencies not empty or paginated"
            exact dependencies "observedAt" (s requestRow "finishedAt")
        else
            require (isNull raw.["errors"] || (arr raw.["errors"]).IsEmpty) "source GraphQL errors"
            let data = raw.["data"]
            require (JsonNode.DeepEquals(data.["viewer"], currentness.["principal"]) && JsonNode.DeepEquals(data.["organization"], currentness.["organization"])) "raw source principal/organization differs"
            let repo = data.["repository"]
            exact repo "id" (s currentness.["repository"] "id")
            exact repo "nameWithOwner" repository
            let rawIssue = repo.["issue"]
            exact rawIssue "__typename" "Issue"
            for name in ["id"; "url"; "state"; "updatedAt"] do exact rawIssue name (s issue name)
            require (integer rawIssue "number" = integer issue "number" && JsonNode.DeepEquals(rawIssue.["repository"], currentness.["repository"])) "raw source issue repository differs"
            let text = s rawIssue "body"
            let bytes = Text.Encoding.UTF8.GetBytes text
            require (not (String.IsNullOrWhiteSpace text) && bytes.Length = integer body "utf8Bytes" && sha256 bytes = s body "sha256") "raw body/owning-plan identity differs"
    closedKeys adjudication ["schema"; "targetKey"; "issueId"; "currentnessSha256"; "authorization"; "adjudication"; "decision"; "roadmap"; "remainingOutcome"; "textualBlockerDisposition"]
    exact adjudication "schema" "private.board-dot6.source-adjudication/1"
    exact adjudication "targetKey" targetKey
    exact adjudication "issueId" (s issue "id")
    exact adjudication "currentnessSha256" (s request.["currentness"] "sha256")
    exact adjudication "authorization" "root-selected"
    exact adjudication "adjudication" "verified-remaining"
    exact adjudication "decision" (if targetKey = "rendering" then "import" else "follow-up")
    exact adjudication "roadmap" issueUrl
    nonempty adjudication "remainingOutcome"
    let textual = adjudication.["textualBlockerDisposition"]
    closedKeys textual ["status"; "evidenceBodySha256"; "statement"]
    require (List.contains (s textual "status") ["preserved"; "none-observed"]) "textual blocker disposition required"
    if targetKey = "rendering" then exact textual "status" "preserved"
    exact textual "evidenceBodySha256" (s body "sha256")
    nonempty textual "statement"
    let boundary = JsonObject()
    boundary.["projectCreationPerformed"] <- JsonValue.Create(false)
    boundary.["membershipMutationPerformed"] <- JsonValue.Create(false)
    boundary.["historicalConstructorAuthenticated"] <- JsonValue.Create(false)
    boundary.["qualifier"] <- qualifier.DeepClone()
    boundary.["adapter"] <- adapter.DeepClone()
    boundary.["metadata"] <- request.["metadata"].DeepClone()
    boundary.["currentness"] <- request.["currentness"].DeepClone()
    boundary.["adjudication"] <- request.["adjudication"].DeepClone()
    boundary.["priorPopulation"] <- request.["priorPopulation"].DeepClone()
    boundary.["textualBlockerDisposition"] <- textual.DeepClone()
    let population = JsonObject()
    population.["schema"] <- JsonValue.Create("fsgg.coordination-board-v2-import/v1")
    population.["observedAt"] <- JsonValue.Create(s requests.[2] "finishedAt")
    population.["target"] <- prior.["target"].DeepClone()
    population.["target"].["creationState"] <- JsonValue.Create("created-and-read-back")
    let admission = JsonObject()
    admission.["organizationId"] <- JsonValue.Create(s selection "organizationId")
    admission.["visibility"] <- JsonValue.Create("complete")
    admission.["authorization"] <- JsonValue.Create("root-selected")
    admission.["repositories"] <- JsonArray(JsonValue.Create(repository) :> JsonNode)
    admission.["recipeRevision"] <- qualifier.["recipeRevision"].DeepClone()
    admission.["artifactSha256"] <- qualifier.["artifactSha256"].DeepClone()
    population.["binding"] <- admission
    let item = JsonObject()
    for name,value in ["issue",snd cohort.Head; "nodeId",s issue "id"; "observedUpdatedAt",s issue "updatedAt"; "observedState","open"; "decision",s adjudication "decision"; "adjudication","verified-remaining"; "remainingOutcome",s adjudication "remainingOutcome"; "roadmap",issueUrl; "status",s planning.["Status"] "name"; "track",s planning.["Track"] "name"; "observation",s planning.["Observation"] "name"] do
        item.[name] <- JsonValue.Create(value)
    // These are retained observations, never field seed/update intents.
    item.["observedRoadmapValue"] <- planning.["Roadmap"].["text"].DeepClone()
    item.["pilot"] <- JsonValue.Create(true)
    item.["dependencies"] <- JsonArray()
    population.["items"] <- JsonArray(item :> JsonNode)
    let inventory = JsonObject()
    let publicEvidence = JsonObject()
    for name in ["projectCreationPerformed"; "membershipMutationPerformed"; "historicalConstructorAuthenticated"; "qualifier"; "adapter"; "textualBlockerDisposition"] do
        publicEvidence.[name] <- boundary.[name].DeepClone()
    for name in ["metadata"; "currentness"; "adjudication"; "priorPopulation"] do
        publicEvidence.[name + "Sha256"] <- request.[name].["sha256"].DeepClone()
    let priorImport = JsonObject()
    for name in ["recipeRevision"; "artifactSha256"] do
        nonempty prior.["binding"] name
        priorImport.[name] <- JsonValue.Create(s prior.["binding"] name)
    priorImport.["historicalConstructorAuthenticated"] <- JsonValue.Create(false)
    publicEvidence.["priorImportProvenance"] <- priorImport
    inventory.["prospectiveQualification"] <- publicEvidence
    population.["inventory"] <- inventory
    let binding = JsonObject()
    for name,value in ["recipeRevision",s adapter "recipeRevision"; "artifactSha256",s adapter "artifactSha256"; "importRecipeRevision",s qualifier "recipeRevision"; "importArtifactSha256",actualArtifact; "organizationId",s selection "organizationId"; "ownerKind","organization"; "owner","FS-GG"; "projectTitle",s selection "projectTitle"; "projectId",projectId; "roadmapFieldId",s schema.["Roadmap"] "id"] do
        binding.[name] <- JsonValue.Create(value)
    binding.["bindingVersion"] <- JsonValue.Create(3)
    binding.["schemaVersion"] <- JsonValue.Create(1)
    binding.["projectNumber"] <- JsonValue.Create(projectNumber)
    binding.["populationRevision"] <- request.["populationRevision"] |> fun value -> if isNull value then null else value.DeepClone()
    binding.["selectedIssues"] <- selected.DeepClone()
    binding.["repositories"] <- admission.["repositories"].DeepClone()
    for name in ["Status"; "Track"; "Observation"] do
        let field = JsonObject()
        field.["id"] <- schema.[name].["id"].DeepClone()
        let options = JsonObject()
        for option in arr schema.[name].["options"] do options.[s option "name"] <- option.["id"].DeepClone()
        field.["options"] <- options
        binding.[name.ToLowerInvariant()] <- field
    let output = JsonObject()
    output.["schema"] <- JsonValue.Create("fsgg.board-v2-existing-product-qualification/1")
    output.["provenance"] <- boundary
    output.["nativeMutationIntents"] <- JsonArray()
    output.["executable"] <- JsonValue.Create(not (isNull request.["populationRevision"]))
    output.["state"] <- JsonValue.Create(if isNull request.["populationRevision"] then "non-executable-draft-pending-protected-population" else "assembled-binding-pending-published-decoder-and-native-acceptance")
    output.["population"] <- population
    output.["binding"] <- binding
    output

let organizationMain (args: string array) =
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

[<EntryPoint>]
let main args =
    try
        if args.Length = 2 && args.[1] = "--existing-product" then
            let result = existingProduct args.[0]
            printfn "%s" (result.ToJsonString())
            0
        else
            organizationMain args
    with error -> eprintfn "board-v2 import refused: %s" error.Message; 2
