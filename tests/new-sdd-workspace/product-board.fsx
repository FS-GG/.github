// Native offline controls. Run against the exact compiled creator with its dependency closure.
// Preparation uses synthetic transport fixtures; these controls establish no publication/adoption.
open System
open System.IO
open System.Text.Json.Nodes
open System.Security.Cryptography
open NewSddWorkspace
open FS.GG.Coord.GitHub.V2Projection

let digest (value: byte array) = SHA256.HashData value |> Convert.ToHexString |> _.ToLowerInvariant()
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
    let drivers = [ "work-board"; "work-board-normal"; "work-board-best"; "padd-item" ]
    let content = "fixture consumer\n"
    let manifest ids =
        let rows = ids |> List.map (fun id -> sprintf "{\"id\":\"%s\",\"files\":[{\"path\":\"SKILL.md\",\"sha256\":\"%s\",\"executable\":false}]}" id (digest (Text.Encoding.UTF8.GetBytes content)))
        "{\"schemaVersion\":2,\"skills\":[" + String.concat "," rows + "]}"
    let version =
        (Reflection.Assembly.Load("fsgg-coord-engine").GetCustomAttributes(typeof<Reflection.AssemblyInformationalVersionAttribute>, false)
         |> Array.exactlyOne) :?> Reflection.AssemblyInformationalVersionAttribute
    let pin = version.InformationalVersion.Split('+').[0]
    coherentVersionMatches expectedCoherentVersion pin |> pass
    let tool = sprintf "{\"tools\":{\"fs.gg.coord.cli\":{\"version\":\"%s\",\"commands\":[\"fsgg-coord-engine\"]}}}" pin
    let fetch = function
        | "registry/coordination-kit-skill-manifest.json" -> Ok(manifest kit)
        | "registry/driver-skill-manifest.json" -> Ok(manifest drivers)
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
    let generatedProvenance = """{"schemaVersion":1,"generator":{"id":"FS.GG.SDD.Artifacts"},"sddOwnedPaths":[{"path":".config/dotnet-tools.json","owner":"sdd"}]}"""
    File.WriteAllText(freshTools, generated)
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
    let snapshot = ProductBoard.captureGeneratedToolManifest freshAuthority freshRoot true |> pass
    let prepareFresh fetch = ProductBoard.prepareGenerated snapshot freshRoot "acme/app" (String.replicate 40 "a") binding fetch
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
    let captureMode = if OperatingSystem.IsWindows() then None else Some(File.GetUnixFileMode freshTools)
    let blocked = Path.Combine(freshRoot, "blocked")
    File.WriteAllText(blocked, "owner bytes")
    let impossible: ProductBoard.FileChange = { Path = "blocked/child"; Before = None; After = [| 1uy |]; Executable = false; BeforeMode = None }
    ProductBoard.apply freshRoot { freshPlan with Changes = freshPlan.Changes @ [ impossible ] } |> refuse "late failure must roll back generated pin and staged files"
    if File.ReadAllText(freshTools) <> generated || File.ReadAllText(blocked) <> "owner bytes" then failwith "rollback changed generated or owner preimages"
    if captureMode |> Option.exists (fun mode -> File.GetUnixFileMode freshTools <> mode) then failwith "rollback changed generated manifest mode"
    if File.Exists(Path.Combine(freshRoot, "scripts/fsgg-coord")) then failwith "rollback retained staged producer bytes"
    File.Delete blocked
    let staleFresh = prepareFresh fetch |> pass
    File.AppendAllText(freshTools, " ")
    ProductBoard.apply freshRoot staleFresh |> refuse "fresh generated preimage changed after plan"
    File.WriteAllText(freshTools, generated)
    ProductBoard.apply freshRoot (prepareFresh fetch |> pass) |> pass
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
