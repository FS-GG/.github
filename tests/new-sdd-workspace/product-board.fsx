// Native offline controls. Run against the exact compiled creator with its dependency closure.
// Preparation uses synthetic transport fixtures; these controls establish no publication/adoption.
open System
open System.IO
open System.Text.Json.Nodes
open System.Security.Cryptography
open NewSddWorkspace
open FS.GG.Coord.GitHub.V2Projection

let digest (value: byte array) = SHA256.HashData value |> Convert.ToHexString |> _.ToLowerInvariant()
let sha (value: string) = digest (Text.Encoding.UTF8.GetBytes value)
let adapter = digest (File.ReadAllBytes typeof<Binding>.Assembly.Location)
let binding = """{
"bindingVersion":3,"importRecipeRevision":"eeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeee",
"importArtifactSha256":"ffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff",
"selectedIssues":{"I_product":"acme/app#7"},"schemaVersion":1,
"recipeRevision":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa","populationRevision":"cccccccccccccccccccccccccccccccccccccccc",
"organizationId":"O_acme","artifactSha256":"ADAPTER","ownerKind":"organization","owner":"acme",
"projectNumber":77,"projectTitle":"Product","projectId":"PVT_product",
"status":{"id":"S_status","options":{"Backlog":"s1","Ready":"s2","In progress":"s3","Blocked":"s4","Done":"s5"}},
"roadmapFieldId":"F_roadmap","track":{"id":"S_track","options":{"Active delivery":"t1","Follow-up":"t2"}},
"observation":{"id":"S_observation","options":{"Verified":"o1","Stale":"o2","Unknown":"o3"}},
"repositories":["acme/app"]} """.Replace("ADAPTER", adapter)
let pass = function Ok value -> value | Error error -> failwith error
let refuse description = function Error _ -> () | Ok _ -> failwith ("accepted " + description)
// Expected identity comes from committed source, independently of the loaded assembly.
let coherentSourceVersion (text: string) =
    try
        let rows =
            System.Xml.Linq.XElement.Parse(text).Descendants()
            |> Seq.filter (fun element -> element.Name.LocalName = "FsggCoherentSetVersion")
            |> Seq.toArray
        if rows.Length <> 1 || rows.[0].HasAttributes || rows.[0].HasElements then
            Error "expected one unconditional coherent version declaration"
        else
            let value = rows.[0].Value
            if Text.RegularExpressions.Regex.IsMatch(value, @"^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)$") then Ok value
            else Error "malformed coherent source version"
    with _ -> Error "malformed coherent source properties"
let coherentVersionMatches expected actual =
    if expected = actual then Ok () else Error "compiled Creator loaded a different coherent CLI"
for invalid in [ "<Project/>"; "<Project><FsggCoherentSetVersion>0.99.1</FsggCoherentSetVersion><FsggCoherentSetVersion>0.99.1</FsggCoherentSetVersion></Project>";
                 "<Project><FsggCoherentSetVersion>invalid</FsggCoherentSetVersion></Project>";
                 "<Project><FsggCoherentSetVersion Condition='false'>0.99.1</FsggCoherentSetVersion></Project>"; "<Project>" ] do
    coherentSourceVersion invalid |> refuse "missing, duplicate or malformed coherent source version"
coherentSourceVersion "<Project><FsggCoherentSetVersion>0.99.1</FsggCoherentSetVersion></Project>" |> pass |> ignore
coherentVersionMatches "0.99.1" "0.99.0" |> refuse "loaded version differs from committed source"
// Frozen compilation uses the authenticated published producer, while the Creator
// keeps its own current source provenance. Do not compare those independent versions.
let selectedCoherentVersion currentProperties frozenProperties frozenPropertiesDigest =
    match frozenProperties, frozenPropertiesDigest with
    | None, None -> coherentSourceVersion currentProperties
    | Some properties, Some expectedDigest when digest properties = expectedDigest ->
        coherentSourceVersion (Text.Encoding.UTF8.GetString properties)
    | Some _, Some _ -> Error "selected producer properties differ from the fixed source pin"
    | _ -> Error "selected producer properties and source pin must be paired"
let testProperties = "<Project><FsggCoherentSetVersion>0.99.1</FsggCoherentSetVersion></Project>"
let testBytes = Text.Encoding.UTF8.GetBytes testProperties
let testDigest = digest (Text.Encoding.UTF8.GetBytes testProperties)
selectedCoherentVersion testProperties None None |> pass |> ignore
selectedCoherentVersion "<Project/>" (Some testBytes) (Some testDigest) |> pass |> ignore
selectedCoherentVersion testProperties (Some (Text.Encoding.UTF8.GetBytes(testProperties.Replace("0.99.1", "0.99.0")))) (Some testDigest)
    |> refuse "changed selected producer version"
selectedCoherentVersion testProperties (Some testBytes) None |> refuse "unpaired producer source pin"
let creatorRoot = Path.GetFullPath(Path.Combine(__SOURCE_DIRECTORY__, "../.."))
let frozenSetting name = Environment.GetEnvironmentVariable name |> Option.ofObj |> Option.filter (String.IsNullOrWhiteSpace >> not)
let expectedCoherentVersion =
    let currentProperties = File.ReadAllText(Path.Combine(creatorRoot, "Directory.Build.props"))
    match frozenSetting "FsggFrozenCoordSource", frozenSetting "FsggFrozenCoordDependencies" with
    | None, None -> selectedCoherentVersion currentProperties None None |> pass
    | Some source, Some _ ->
        if not (Path.IsPathFullyQualified source) || Path.GetFullPath source = creatorRoot then
            failwith "frozen producer source must be a distinct absolute root"
        let pin = JsonNode.Parse(File.ReadAllText(Path.Combine(creatorRoot, "scripts/creator-frozen-coord-dependencies.json")))
        let leaf = pin.["sourceLeaves"].AsArray() |> Seq.filter (fun row -> row.["path"].GetValue<string>() = "Directory.Build.props") |> Seq.exactlyOne
        let properties = File.ReadAllBytes(Path.Combine(source, "Directory.Build.props"))
        selectedCoherentVersion currentProperties (Some properties) (Some (leaf.["sha256"].GetValue<string>())) |> pass
    | _ -> failwith "frozen producer source and binary roots must be paired"

ProductBoard.validate "acme/app" adapter binding |> pass |> ignore
ProductBoard.validate "acme/other" adapter binding |> refuse "foreign repository"
ProductBoard.validate "acme/app" (String.replicate 64 "0") binding |> refuse "wrong loaded artifact"
ProductBoard.validate "acme/app" adapter (binding.Replace("\"bindingVersion\":3", "\"bindingVersion\":2")) |> refuse "organization binding"
ProductBoard.validate "acme/app" adapter (binding.Replace("\"ownerKind\":\"organization\"", "\"ownerKind\":\"user\"")) |> refuse "unsupported owner"
ProductBoard.validate "acme/app" adapter (binding.Replace("\"schemaVersion\":1", "\"schemaVersion\":2")) |> refuse "wrong schema"
ProductBoard.validate "acme/app" adapter (binding.Replace("\"projectId\":\"PVT_product\"", "\"projectId\":\"PVT_kwDOEYAWY84Bldpa\"")) |> refuse "organization target"
ProductBoard.validate "acme/app" adapter (binding.Replace("\"projectId\":\"PVT_product\"", "\"projectId\":\"PVT_kwDOEYAWY84Bb08W\"")) |> refuse "legacy project"
ProductBoard.validate "acme/app" adapter (binding.Replace("\"schemaVersion\":1", "\"schemaVersion\":1,\"surprise\":true")) |> refuse "unknown property"

ProductBoard.validateRemote "acme/app" "git@github.com:acme/app.git" |> pass
ProductBoard.validateRemote "acme/app" "https://github.com/acme/app.git" |> pass
ProductBoard.validateRemote "acme/app" "https://github.com/acme/other.git" |> refuse "foreign remote"
ProductBoard.validateRemote "acme/app" "https://user:secret@github.com/acme/app.git" |> refuse "credential-bearing remote"

let root = Path.Combine(Path.GetTempPath(), "product-board-controls-" + Guid.NewGuid().ToString "N")
Directory.CreateDirectory root |> ignore
try
    // Use the shipped origin helper: authority is captured before fake provider Git creation.
    let freshAuthority = ProductBoard.captureOriginAuthority root
    Directory.CreateDirectory(Path.Combine(root, ".git")) |> ignore
    let git origin head rootResult configResult args =
        match args with
        | [ "remote"; "get-url"; "origin" ] -> origin
        | [ "rev-parse"; "--show-toplevel" ] -> rootResult
        | [ "config"; "--local"; "--get"; "remote.origin.url" ] -> configResult
        | [ "symbolic-ref"; "--quiet"; "HEAD" ] -> 0, "refs/heads/main"
        | [ "rev-parse"; "--verify"; "HEAD" ] -> head
        | _ -> failwith "unexpected production Git probe"
    let unborn = git (2, "") (128, "") (0, root) (1, "")
    let originCheck authority succeeded probe = ProductBoard.validateOrigin authority succeeded root "acme/app" probe
    originCheck freshAuthority false unborn |> refuse "originless Git before successful scaffold"
    originCheck freshAuthority true unborn |> pass
    originCheck freshAuthority true (git (0, "git@github.com:acme/app.git") (128, "") (0, root) (1, "")) |> pass
    let retainedAuthority = ProductBoard.captureOriginAuthority root
    originCheck retainedAuthority true unborn |> refuse "retained originless Git"
    originCheck retainedAuthority false unborn |> refuse "retrofit originless Git"
    originCheck retainedAuthority false (git (0, "https://github.com/acme/other.git") (0, "commit") (0, root) (0, "origin")) |> refuse "retained foreign origin"
    originCheck retainedAuthority false (git (0, "https://github.com/acme/app.git") (0, "commit") (0, root) (0, "origin")) |> pass
    originCheck freshAuthority true (git (0, "https://github.com/acme/other.git") (128, "") (0, root) (1, "")) |> refuse "fresh foreign origin"
    originCheck retainedAuthority false (git (0, "https://user:secret@github.com/acme/app.git") (0, "commit") (0, root) (0, "origin")) |> refuse "retained credential-bearing origin"
    originCheck freshAuthority true (git (2, "") (0, "commit") (0, root) (1, "")) |> refuse "fresh committed originless Git"
    originCheck freshAuthority true (git (2, "") (128, "") (128, "") (1, "")) |> refuse "unreadable fresh Git"
    originCheck freshAuthority true (git (2, "") (128, "") (0, root + "-foreign") (1, "")) |> refuse "foreign Git root"
    originCheck freshAuthority true (git (2, "") (128, "") (0, root) (128, "")) |> refuse "unreadable origin configuration"
    Directory.Delete(Path.Combine(root, ".git"), true)
    originCheck retainedAuthority true unborn |> refuse "retained Git disappearance"
    let kit = [ "check-board"; "cross-repo-coordination"; "initialize-sdd-workspace"; "intra-repo-parallel-work"; "pnext-item" ]
    let drivers = [ "atlas"; "work-roadmap"; "work-board"; "work-board-normal"; "work-board-best"; "padd-item"; "continue-from-handoff"; "stop-with-handoff"; "routine-decisions"; "agent-handoff"; "work-programme" ]
    let content = "fixture consumer\n"
    let generatedLeaves =
        [ "padd-item/SKILL.md";
          "padd-item/agents/openai.yaml";
          "padd-item/references/legacy-product-intake.md";
          "work-board-best/SKILL.md";
          "work-board-best/agents/openai.yaml";
          "work-board-normal/SKILL.md";
          "work-board-normal/agents/openai.yaml";
          "work-board/SKILL.md";
          "work-board/agents/openai.yaml";
          "work-board/references/backlog-triage.md";
          "work-board/references/deep-detail.md";
          "work-board/references/feedback-contract.md";
          "work-board/references/host-loop.md";
          "work-board/references/intake.md";
          "work-board/references/lifecycle-contract.md";
          "work-board/references/workspace-scope.md";
          "work-roadmap/SKILL.md";
          "work-roadmap/agents/openai.yaml";
          "work-roadmap/references/critique-contract.md";
          "work-roadmap/references/deep-detail.md";
          "work-roadmap/references/feedback-contract.md";
          "work-roadmap/references/host-loop.md";
          "work-roadmap/references/lifecycle-log.md";
          "work-roadmap/references/roadmap-ledger.md" ]
    let manifest ids =
        let rows = ids |> List.map (fun id ->
            let declared = generatedLeaves |> List.choose (fun path ->
                let prefix = id + "/"
                if path.StartsWith(prefix, StringComparison.Ordinal) then Some(path.Substring(prefix.Length)) else None)
            let leaves = if declared.IsEmpty then [ "SKILL.md" ] else declared
            let files = leaves |> List.map (fun path -> sprintf "{\"path\":\"%s\",\"sha256\":\"%s\",\"executable\":false}" path (digest (Text.Encoding.UTF8.GetBytes content)))
            sprintf "{\"id\":\"%s\",\"scope\":\"driver\",\"files\":[%s]}" id (String.concat "," files))
        "{\"schemaVersion\":2,\"skills\":[" + String.concat "," rows + "]}"
    let version =
        (Reflection.Assembly.Load("fsgg-coord-engine").GetCustomAttributes(typeof<Reflection.AssemblyInformationalVersionAttribute>, false)
         |> Array.exactlyOne) :?> Reflection.AssemblyInformationalVersionAttribute
    let pin = version.InformationalVersion.Split('+').[0]
    coherentVersionMatches expectedCoherentVersion pin |> pass
    let tool = sprintf "{\"tools\":{\"fs.gg.coord.cli\":{\"version\":\"%s\",\"commands\":[\"fsgg-coord-engine\"]}}}" pin
    let driverManifest = JsonNode.Parse(manifest drivers).AsObject()
    let optionalOperator = JsonNode.Parse(manifest [ "operator-only" ]).["skills"].[0].DeepClone()
    optionalOperator.["scope"] <- JsonValue.Create "operator"
    driverManifest.["skills"].AsArray().Add optionalOperator
    let fetch = function
        | "registry/coordination-kit-skill-manifest.json" -> Ok(manifest kit)
        | "registry/driver-skill-manifest.json" -> Ok(driverManifest.ToJsonString())
        | "dist/dotnet/.config/dotnet-tools.json" -> Ok tool
        | "scripts/fsgg-coord" -> Ok "#!/bin/sh\nexit 0\n"
        | path when path.StartsWith ".claude/skills/" -> Ok content
        | path -> Error("missing " + path)
    let prepare fetch = ProductBoard.prepare root "acme/app" (String.replicate 40 "a") binding fetch
    prepare (fun path -> fetch path |> Result.map (fun value ->
        if path = "dist/dotnet/.config/dotnet-tools.json" then value.Replace(pin, "0.97.1") else value))
        |> refuse "immutable kit tool pin differs from loaded coherent CLI"
    let freshRoot = Path.Combine(root, "fresh-generated")
    let freshAuthority = ProductBoard.captureFreshScaffoldTarget freshRoot |> Option.get
    let ownerPath = Path.Combine(root, "existing-owner-file")
    File.WriteAllText(ownerPath, "owner")
    if ProductBoard.captureFreshScaffoldTarget ownerPath |> Option.isSome then failwith "existing owner file gained fresh authority"
    File.Delete ownerPath
    Directory.CreateDirectory(Path.Combine(freshRoot, ".config")) |> ignore
    Directory.CreateDirectory(Path.Combine(freshRoot, ".fsgg")) |> ignore
    let freshTools = Path.Combine(freshRoot, ".config/dotnet-tools.json")
    let freshProvenance = Path.Combine(freshRoot, ".fsgg/scaffold-provenance.json")
    let generated = """{"version":1,"isRoot":true,"tools":{"fs.gg.coord.cli":{"version":"0.94.0","commands":["fsgg-coord-engine"]},"fs.gg.sdd.cli":{"version":"2.1.0","commands":["fsgg-sdd"]},"owner-tool":{"version":"1.2.3","commands":["keep"]}}}"""
    // Synthetic old/new bodies exercise the real compiled snapshot and replacement boundaries.
    let driverPaths =
        [ for mirror in [ ".agents/skills"; ".claude/skills" ] do
              for leaf in generatedLeaves do
                  yield mirror + "/" + leaf ]
    let oldDriver = "synthetic SDD-generated predecessor driver\n"
    for relative in driverPaths do
        let path = Path.Combine(freshRoot, relative)
        Directory.CreateDirectory(Path.GetDirectoryName path) |> ignore
        File.WriteAllText(path, oldDriver)
    let driverRows = driverPaths |> List.map (fun path ->
        sprintf "{\"path\":\"%s\",\"owner\":\"driver\",\"sha256\":\"%s\"}" path (digest (Text.Encoding.UTF8.GetBytes oldDriver)))
    let generatedProvenance =
        """{"schemaVersion":1,"generator":{"id":"FS.GG.SDD.Artifacts"},"sddOwnedPaths":[{"path":".config/dotnet-tools.json","owner":"sdd"}],"driverPaths":["""
        + String.concat "," driverRows + "]}"
    File.WriteAllText(freshTools, generated)
    File.WriteAllText(freshProvenance, generatedProvenance)
    // Older successful scaffolds retain only the six required predecessor leaves.
    let roadmapPaths = driverPaths |> List.filter (fun path ->
        not ([ "work-board/SKILL.md"; "work-board/references/workspace-scope.md"; "padd-item/SKILL.md" ]
             |> List.exists (fun leaf -> path = ".agents/skills/" + leaf || path = ".claude/skills/" + leaf)))
    for relative in roadmapPaths do File.Delete(Path.Combine(freshRoot, relative))
    let legacyProvenance = JsonNode.Parse(generatedProvenance)
    let legacyRows = legacyProvenance.["driverPaths"].AsArray()
    for index in [ legacyRows.Count - 1 .. -1 .. 0 ] do
        if roadmapPaths |> List.contains (legacyRows.[index].["path"].GetValue<string>()) then legacyRows.RemoveAt index
    File.WriteAllText(freshProvenance, legacyProvenance.ToJsonString())
    ProductBoard.captureGeneratedToolManifest freshAuthority freshRoot true |> pass |> ignore
    // A present roadmap file cannot gain authority without exact driver provenance.
    File.WriteAllText(Path.Combine(freshRoot, roadmapPaths.Head), oldDriver)
    ProductBoard.captureGeneratedToolManifest freshAuthority freshRoot true |> refuse "unowned generated roadmap"
    for relative in roadmapPaths do File.WriteAllText(Path.Combine(freshRoot, relative), oldDriver)
    File.WriteAllText(freshProvenance, generatedProvenance)
    if ProductBoard.captureFreshScaffoldTarget freshRoot |> Option.isSome then failwith "existing owner directory gained fresh authority"
    ProductBoard.prepare freshRoot "acme/app" (String.replicate 40 "a") binding fetch |> refuse "preexisting/retained generated-looking owner pin"
    ProductBoard.captureGeneratedToolManifest freshAuthority freshRoot false |> refuse "failed scaffold snapshot"
    File.WriteAllText(freshProvenance, generatedProvenance.Replace("\"owner\":\"sdd\"", "\"owner\":\"authored\""))
    ProductBoard.captureGeneratedToolManifest freshAuthority freshRoot true |> refuse "foreign manifest ownership"
    File.Delete freshProvenance
    ProductBoard.captureGeneratedToolManifest freshAuthority freshRoot true |> refuse "missing scaffold provenance"
    File.WriteAllText(freshProvenance, generatedProvenance)
    File.WriteAllText(freshTools, generated.Replace("\"isRoot\":true", "\"isRoot\":false"))
    ProductBoard.captureGeneratedToolManifest freshAuthority freshRoot true |> refuse "nonroot generated manifest"
    File.WriteAllText(freshTools, generated)
    let mutable driverRefusals = 0
    let refuseDriver description result =
        refuse description result
        driverRefusals <- driverRefusals + 1
        printfn "Fresh generated driver refusal passed: %s" description
    // Metadata cannot manufacture fresh authority for missing, duplicated or edited driver bytes.
    let saveProvenance (mutate: JsonObject -> unit) =
        let value = JsonNode.Parse(generatedProvenance).AsObject()
        mutate value
        File.WriteAllText(freshProvenance, value.ToJsonString())
    for index in 0 .. driverPaths.Length - 1 do
        saveProvenance (fun value -> value.["driverPaths"].AsArray().RemoveAt index)
        ProductBoard.captureGeneratedToolManifest freshAuthority freshRoot true |> refuseDriver "missing driver ownership"
        saveProvenance (fun value ->
            let rows = value.["driverPaths"].AsArray()
            rows.Add(rows.[index].DeepClone()))
        ProductBoard.captureGeneratedToolManifest freshAuthority freshRoot true |> refuseDriver "duplicate driver ownership"
        saveProvenance (fun value -> value.["driverPaths"].[index].["owner"] <- JsonValue.Create "authored")
        ProductBoard.captureGeneratedToolManifest freshAuthority freshRoot true |> refuseDriver "foreign driver ownership"
        saveProvenance (fun value -> value.["driverPaths"].[index].["sha256"] <- JsonValue.Create(String.replicate 64 "0"))
        ProductBoard.captureGeneratedToolManifest freshAuthority freshRoot true |> refuseDriver "wrong driver digest"
        File.WriteAllText(freshProvenance, generatedProvenance)
        let path = Path.Combine(freshRoot, driverPaths.[index])
        File.AppendAllText(path, "authored edit")
        ProductBoard.captureGeneratedToolManifest freshAuthority freshRoot true |> refuseDriver "driver differs from genuine captured provenance"
        File.WriteAllText(path, oldDriver)
        File.Delete path
        ProductBoard.captureGeneratedToolManifest freshAuthority freshRoot true |> refuseDriver "missing generated driver file"
        File.WriteAllText(path, oldDriver)
    saveProvenance (fun value -> value.Remove "driverPaths" |> ignore)
    ProductBoard.captureGeneratedToolManifest freshAuthority freshRoot true |> refuseDriver "missing driver provenance collection"
    saveProvenance (fun value -> value.["driverPaths"].[0].["path"] <- JsonValue.Create ".agents/skills/work-board/../SKILL.md")
    ProductBoard.captureGeneratedToolManifest freshAuthority freshRoot true |> refuseDriver "alias ownership path cannot grant exact driver authority"
    File.WriteAllText(freshProvenance, generatedProvenance)
    ProductBoard.captureGeneratedToolManifest freshAuthority (freshRoot + "-foreign") true |> refuseDriver "foreign fresh target"
    let snapshot = ProductBoard.captureGeneratedToolManifest freshAuthority freshRoot true |> pass
    let prepareFresh fetch = ProductBoard.prepareGenerated snapshot freshRoot "acme/app" (String.replicate 40 "a") binding fetch
    let mutable adoptionRefusals = 0
    let refuseAdoption description result =
        refuse description result
        adoptionRefusals <- adoptionRefusals + 1
        printfn "Fresh generated provenance refusal passed: %s" description
    let provenanceMutants index : (string * (JsonObject -> unit)) list =
        [ "missing", (fun value -> value.["driverPaths"].AsArray().RemoveAt index)
          "duplicate", (fun value -> let rows = value.["driverPaths"].AsArray() in rows.Add(rows.[index].DeepClone()))
          "foreign owner", (fun value -> value.["driverPaths"].[index].["owner"] <- JsonValue.Create "authored")
          "stale digest", (fun value -> value.["driverPaths"].[index].["sha256"] <- JsonValue.Create(String.replicate 64 "0")) ]
    for index in 0 .. driverPaths.Length - 1 do
        for label, mutate in provenanceMutants index do
            saveProvenance mutate
            let mutable fetched = false
            prepareFresh (fun name -> fetched <- true; fetch name) |> refuseAdoption (label + " ownership after capture")
            if fetched then failwith "changed scaffold provenance reached producer fetch"
            File.WriteAllText(freshProvenance, generatedProvenance)
            let mutable changed = false
            prepareFresh (fun name ->
                if not changed then
                    changed <- true
                    saveProvenance mutate
                fetch name) |> refuseAdoption (label + " ownership changed during producer fetch")
            File.WriteAllText(freshProvenance, generatedProvenance)
    saveProvenance (fun value -> value.["generator"].["id"] <- JsonValue.Create "foreign")
    prepareFresh fetch |> refuseAdoption "generator provenance changed after capture"
    File.WriteAllText(freshProvenance, generatedProvenance)
    saveProvenance (fun value -> value.["ownerNote"] <- JsonValue.Create "concurrent owner")
    prepareFresh fetch |> refuseAdoption "unrelated provenance changed after capture"
    File.WriteAllText(freshProvenance, generatedProvenance)
    saveProvenance (fun value -> value.["productBoard"] <- JsonObject())
    ProductBoard.captureGeneratedToolManifest freshAuthority freshRoot true |> refuseAdoption "already adopted provenance cannot create a fresh snapshot"
    File.WriteAllText(freshProvenance, generatedProvenance)
    if not (OperatingSystem.IsWindows()) then
        let mode = File.GetUnixFileMode freshProvenance
        File.SetUnixFileMode(freshProvenance, mode ^^^ UnixFileMode.UserExecute)
        prepareFresh fetch |> refuseAdoption "scaffold provenance mode changed after capture"
        File.SetUnixFileMode(freshProvenance, mode)
    for relative in driverPaths do
        let path = Path.Combine(freshRoot, relative)
        File.AppendAllText(path, "authored edit")
        prepareFresh fetch |> refuseDriver "generated driver edited after capture"
        File.WriteAllText(path, oldDriver)
        if not (OperatingSystem.IsWindows()) then
            let mode = File.GetUnixFileMode path
            File.SetUnixFileMode(path, mode ^^^ UnixFileMode.UserExecute)
            prepareFresh fetch |> refuseDriver "generated driver mode edited after capture"
            File.SetUnixFileMode(path, mode)
        let mutable mutated = false
        prepareFresh (fun name ->
            if not mutated then
                mutated <- true
                File.AppendAllText(path, "during fetch")
            fetch name) |> refuseDriver "generated driver edited during producer fetch"
        File.WriteAllText(path, oldDriver)
    // A non-selected driver-like file remains authored even during a fresh invocation.
    let otherDriver = Path.Combine(freshRoot, ".agents/skills/atlas/SKILL.md")
    Directory.CreateDirectory(Path.GetDirectoryName otherDriver) |> ignore
    File.WriteAllText(otherDriver, "owner authored")
    prepareFresh fetch |> refuseDriver "fresh authority does not cover arbitrary driver files"
    File.Delete otherDriver
    prepareFresh (fun name ->
        fetch name |> Result.map (fun value ->
            if name = "registry/driver-skill-manifest.json" then
                let manifest = JsonNode.Parse(value).AsObject()
                let workBoard = manifest.["skills"].AsArray() |> Seq.find (fun row -> row.["id"].GetValue<string>() = "work-board")
                workBoard.["files"].AsArray().RemoveAt 1
                manifest.ToJsonString()
            else value)) |> refuseDriver "selected driver payload omits captured workspace scope"
    File.AppendAllText(freshTools, " ")
    prepareFresh fetch |> refuse "fresh generated manifest edited after capture"
    File.WriteAllText(freshTools, generated)
    if not (OperatingSystem.IsWindows()) then
        let mode = File.GetUnixFileMode freshTools
        File.SetUnixFileMode(freshTools, mode ^^^ UnixFileMode.UserExecute)
        prepareFresh fetch |> refuse "fresh generated manifest mode edited after capture"
        File.SetUnixFileMode(freshTools, mode)
    let mutable mutatedDuringFetch = false
    prepareFresh (fun path ->
        if not mutatedDuringFetch then
            mutatedDuringFetch <- true
            File.AppendAllText(freshTools, " ")
        fetch path) |> refuse "fresh generated manifest changed during producer fetch"
    File.WriteAllText(freshTools, generated)
    let freshPlan = prepareFresh fetch |> pass
    if driverPaths |> List.exists (fun relative -> freshPlan.Changes |> List.exists (fun change -> change.Path = relative && change.Before = Some(Text.Encoding.UTF8.GetBytes oldDriver) && change.After = Text.Encoding.UTF8.GetBytes content) |> not) then
        failwith "fresh driver plan omitted a selected predecessor replacement"
    let captureMode = if OperatingSystem.IsWindows() then None else Some(File.GetUnixFileMode freshTools)
    let provenanceMode = if OperatingSystem.IsWindows() then None else Some(File.GetUnixFileMode freshProvenance)
    let blocked = Path.Combine(freshRoot, "blocked")
    File.WriteAllText(blocked, "owner bytes")
    let impossible: ProductBoard.FileChange = { Path = "blocked/child"; Before = None; After = [| 1uy |]; Executable = false; BeforeMode = None }
    ProductBoard.apply freshRoot { freshPlan with Changes = freshPlan.Changes @ [ impossible ] } |> refuse "late failure must roll back generated pin and staged files"
    if File.ReadAllText(freshTools) <> generated || File.ReadAllText(blocked) <> "owner bytes" then failwith "rollback changed generated or owner preimages"
    if File.ReadAllText(freshProvenance) <> generatedProvenance then failwith "rollback changed original scaffold provenance"
    if captureMode |> Option.exists (fun mode -> File.GetUnixFileMode freshTools <> mode) then failwith "rollback changed generated manifest mode"
    if provenanceMode |> Option.exists (fun mode -> File.GetUnixFileMode freshProvenance <> mode) then failwith "rollback changed original scaffold provenance mode"
    if driverPaths |> List.exists (fun relative -> File.ReadAllText(Path.Combine(freshRoot, relative)) <> oldDriver) then
        failwith "rollback changed generated driver preimages"
    if File.Exists(Path.Combine(freshRoot, "scripts/fsgg-coord")) then failwith "rollback retained staged producer bytes"
    File.Delete blocked
    let staleProvenance = prepareFresh fetch |> pass
    File.AppendAllText(freshProvenance, " ")
    ProductBoard.apply freshRoot staleProvenance |> refuseAdoption "scaffold provenance bytes changed after preview"
    File.WriteAllText(freshProvenance, generatedProvenance)
    if not (OperatingSystem.IsWindows()) then
        let modePlan = prepareFresh fetch |> pass
        let mode = File.GetUnixFileMode freshProvenance
        File.SetUnixFileMode(freshProvenance, mode ^^^ UnixFileMode.UserExecute)
        ProductBoard.apply freshRoot modePlan |> refuseAdoption "scaffold provenance mode changed after preview"
        File.SetUnixFileMode(freshProvenance, mode)
        let aliasTarget = Path.Combine(root, "aliased-provenance")
        File.WriteAllText(aliasTarget, generatedProvenance)
        File.Delete freshProvenance
        File.CreateSymbolicLink(freshProvenance, aliasTarget) |> ignore
        ProductBoard.captureGeneratedToolManifest freshAuthority freshRoot true |> refuseAdoption "scaffold provenance symlink capture"
        File.Delete freshProvenance
        File.WriteAllText(freshProvenance, generatedProvenance)
        let aliasPlan = prepareFresh fetch |> pass
        File.Delete freshProvenance
        File.CreateSymbolicLink(freshProvenance, aliasTarget) |> ignore
        ProductBoard.apply freshRoot aliasPlan |> refuseAdoption "same-byte provenance symlink after preview"
        if File.ReadAllText(aliasTarget) <> generatedProvenance then failwith "provenance alias target was written"
        File.Delete freshProvenance
        File.WriteAllText(freshProvenance, generatedProvenance)
        File.Delete aliasTarget
    let expectedAdoptionRefusals = if OperatingSystem.IsWindows() then 340 else 344
    if adoptionRefusals <> expectedAdoptionRefusals then
        failwithf "fresh provenance refusal inventory incomplete: %d / %d" adoptionRefusals expectedAdoptionRefusals
    printfn "Fresh generated provenance controls passed: %d actual refusals; 42 active hashes/original provenance and rollback checked." adoptionRefusals
    let staleFresh = prepareFresh fetch |> pass
    File.AppendAllText(freshTools, " ")
    ProductBoard.apply freshRoot staleFresh |> refuse "fresh generated preimage changed after plan"
    File.WriteAllText(freshTools, generated)
    for relative in driverPaths do
        let staleDriver = prepareFresh fetch |> pass
        let path = Path.Combine(freshRoot, relative)
        File.AppendAllText(path, "after preview")
        ProductBoard.apply freshRoot staleDriver |> refuseDriver "generated driver changed after preview"
        File.WriteAllText(path, oldDriver)
        if not (OperatingSystem.IsWindows()) then
            let modePlan = prepareFresh fetch |> pass
            let mode = File.GetUnixFileMode path
            File.SetUnixFileMode(path, mode ^^^ UnixFileMode.UserExecute)
            ProductBoard.apply freshRoot modePlan |> refuseDriver "generated driver mode changed after preview"
            File.SetUnixFileMode(path, mode)
    if not (OperatingSystem.IsWindows()) then
        let relative = driverPaths.Head
        let path = Path.Combine(freshRoot, relative)
        let aliasTarget = Path.Combine(root, "aliased-driver")
        File.WriteAllText(aliasTarget, oldDriver)
        File.Delete path
        File.CreateSymbolicLink(path, aliasTarget) |> ignore
        ProductBoard.captureGeneratedToolManifest freshAuthority freshRoot true |> refuseDriver "driver symlink capture"
        prepareFresh fetch |> refuseDriver "driver symlink after capture"
        File.Delete path
        File.WriteAllText(path, oldDriver)
        let aliasPlan = prepareFresh fetch |> pass
        File.Delete path
        File.CreateSymbolicLink(path, aliasTarget) |> ignore
        ProductBoard.apply freshRoot aliasPlan |> refuseDriver "same-byte driver symlink after preview"
        if File.ReadAllText(aliasTarget) <> oldDriver then failwith "driver alias target was written"
        File.Delete path
        File.WriteAllText(path, oldDriver)
        File.Delete aliasTarget
        // Alias ancestors also refuse even when every captured leaf has identical bytes/mode.
        let driverDirectory = Path.Combine(freshRoot, ".agents/skills/work-board")
        let heldDirectory = Path.Combine(root, "aliased-driver-directory")
        Directory.Move(driverDirectory, heldDirectory)
        Directory.CreateSymbolicLink(driverDirectory, heldDirectory) |> ignore
        ProductBoard.captureGeneratedToolManifest freshAuthority freshRoot true |> refuseDriver "driver ancestor symlink capture"
        prepareFresh fetch |> refuseDriver "driver ancestor symlink after capture"
        Directory.Delete driverDirectory
        Directory.Move(heldDirectory, driverDirectory)
        let ancestorPlan = prepareFresh fetch |> pass
        Directory.Move(driverDirectory, heldDirectory)
        Directory.CreateSymbolicLink(driverDirectory, heldDirectory) |> ignore
        ProductBoard.apply freshRoot ancestorPlan |> refuseDriver "driver ancestor symlink after preview"
        Directory.Delete driverDirectory
        Directory.Move(heldDirectory, driverDirectory)
    let expectedDriverRefusals = if OperatingSystem.IsWindows() then 383 else 473
    if driverRefusals <> expectedDriverRefusals then
        failwithf "fresh driver refusal inventory incomplete: %d / %d" driverRefusals expectedDriverRefusals
    printfn "Fresh generated driver controls passed: %d actual refusals, 42 planned/replaced mirrors, rollback and exact repeat checked below." driverRefusals
    ProductBoard.apply freshRoot (prepareFresh fetch |> pass) |> pass
    if driverPaths |> List.exists (fun relative -> File.ReadAllText(Path.Combine(freshRoot, relative)) <> content) then
        failwith "fresh driver installation omitted a selected mirror"
    let installedProvenance = JsonNode.Parse(File.ReadAllText freshProvenance)
    let adoption = installedProvenance.["productBoard"].["generatedDriverAdoption"]
    if adoption.["kitRevision"].GetValue<string>() <> String.replicate 40 "a"
       || adoption.["originalScaffoldProvenanceSha256"].GetValue<string>() <> sha generatedProvenance
       || not (JsonNode.DeepEquals(adoption.["originalScaffoldProvenance"], JsonNode.Parse(generatedProvenance))) then
        failwith "fresh adoption lost exact original scaffold provenance or immutable Kit identity"
    for relative in driverPaths do
        let active = installedProvenance.["driverPaths"].AsArray() |> Seq.filter (fun row -> row.["path"].GetValue<string>() = relative) |> Seq.toList
        let records = adoption.["drivers"].AsArray() |> Seq.filter (fun row -> row.["path"].GetValue<string>() = relative) |> Seq.toList
        if active.Length <> 1 || active.Head.["owner"].GetValue<string>() <> "driver"
           || active.Head.["sha256"].GetValue<string>() <> sha (File.ReadAllText(Path.Combine(freshRoot, relative)))
           || records.Length <> 1 || records.Head.["beforeSha256"].GetValue<string>() <> sha oldDriver
           || records.Head.["afterSha256"].GetValue<string>() <> sha content then
            failwith "fresh driver active provenance or adoption history differs from installed bytes"
    if adoption.["drivers"].AsArray().Count <> 42 then failwith "adoption history broadened beyond selected42 leaves"
    ProductBoard.captureGeneratedToolManifest freshAuthority freshRoot true |> refuse "applied adoption cannot be recaptured as original scaffold"
    let installedTools = JsonNode.Parse(File.ReadAllText freshTools)
    if installedTools.["tools"].["fs.gg.coord.cli"].["version"].GetValue<string>() <> pin
       || installedTools.["tools"].["fs.gg.sdd.cli"].["version"].GetValue<string>() <> "2.1.0"
       || installedTools.["tools"].["owner-tool"].["commands"].[0].GetValue<string>() <> "keep" then
        failwith "fresh generated replacement changed nonCoord tools"
    if not ((ProductBoard.prepare freshRoot "acme/app" (String.replicate 40 "a") binding fetch |> pass).Changes.IsEmpty) then
        failwith "ordinary exact selected pin repeat must be noop"
    Directory.Delete(freshRoot, true)
    let plan = prepare fetch |> pass
    if Directory.EnumerateFileSystemEntries(root) |> Seq.isEmpty |> not then failwith "preview wrote workspace files"
    prepare (fun _ -> Error "producer unavailable") |> refuse "missing publication"
    prepare (fun path -> fetch path |> Result.map (fun value -> if path.StartsWith ".claude/skills/" then value + "drift" else value)) |> refuse "consumer digest drift"
    ProductBoard.prepare root "acme/app" "main" binding fetch |> refuse "moving producer reference"
    ProductBoard.apply root plan |> pass
    for mirror in [ ".agents/skills"; ".claude/skills" ] do
        for driver in drivers do
            let path = Path.Combine(root, mirror, driver, "SKILL.md")
            if not (File.Exists path) || File.ReadAllText path <> content then
                failwith ("complete delivered driver cohort omitted or changed " + mirror + "/" + driver)
        if File.ReadAllText(Path.Combine(root, mirror, "work-board/references/workspace-scope.md")) <> content then
            failwith "complete driver reference payload was omitted"
        if Directory.Exists(Path.Combine(root, mirror, "operator-only")) then
            failwith "optional operator was silently included in the driver cohort"
    printfn "Complete product driver materialization passed: %d delivered skills in both mirrors, reference payload and optional-operator exclusion." drivers.Length
    if not ((prepare fetch |> pass).Changes.IsEmpty) then failwith "repeat must be a no-op"
    let retainedTools = Path.Combine(root, ".config/dotnet-tools.json")
    let retainedToolBytes = File.ReadAllBytes retainedTools
    File.WriteAllText(retainedTools, File.ReadAllText(retainedTools).Replace(pin, "0.97.1"))
    prepare fetch |> refuse "retained predecessor tool pin cannot be overwritten"
    File.WriteAllBytes(retainedTools, retainedToolBytes)
    let settingsPath = Path.Combine(root, ".claude/settings.json")
    let settings = JsonNode.Parse(File.ReadAllText settingsPath).AsObject()
    settings.["ownerSetting"] <- JsonValue.Create "keep"
    File.WriteAllText(settingsPath, settings.ToJsonString())
    if not ((prepare fetch |> pass).Changes.IsEmpty) then failwith "unrelated JSON formatting/keys must be preserved"
    let before = File.ReadAllBytes settingsPath
    settings.["env"].["FSGG_COORD_REPOSITORY"] <- JsonValue.Create "acme/other"
    File.WriteAllText(settingsPath, settings.ToJsonString())
    prepare fetch |> refuse "owner config conflict"
    File.WriteAllBytes(settingsPath, before)
    let skill = Path.Combine(root, ".agents/skills/work-board/SKILL.md")
    File.WriteAllText(skill, "owner authored")
    prepare fetch |> refuse "owner skill conflict"
    File.WriteAllText(skill, content)
    let newFile = Path.Combine(root, ".fsgg/board-v2-binding.json")
    File.Delete newFile
    let stale = prepare fetch |> pass
    File.WriteAllText(newFile, "concurrent edit")
    ProductBoard.apply root stale |> refuse "changed preimage"
finally
    Directory.Delete(root, true)
printfn "Product V2 offline controls passed; native/public adoption remains unproven."
