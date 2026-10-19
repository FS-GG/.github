// Run by product-board.fsx against the same compiled Creator. Only public immutable producer
// content and a synthetic product binding are used; no real receiver or native system is touched.

open System
open System.IO
open System.Diagnostics
open System.Text.Json.Nodes
open System.Security.Cryptography
open NewSddWorkspace
open FS.GG.Coord.GitHub.V2Projection

let pass = function Ok value -> value | Error error -> failwith error
let refuse label = function Error _ -> () | Ok _ -> failwith ("accepted " + label)
let digest (value: byte array) = SHA256.HashData value |> Convert.ToHexString |> _.ToLowerInvariant()
let producer = "cc1d3c12c1ed7e7128012063d7ab478c7198b523"
let source = Path.GetFullPath(Path.Combine(__SOURCE_DIRECTORY__, "../.."))
let adapter = digest (File.ReadAllBytes typeof<Binding>.Assembly.Location)
let pin =
    (Reflection.Assembly.Load("fsgg-coord-engine").GetCustomAttributes(typeof<Reflection.AssemblyInformationalVersionAttribute>, false)
     |> Array.exactlyOne :?> Reflection.AssemblyInformationalVersionAttribute).InformationalVersion.Split('+').[0]
let readOld path =
    let start = ProcessStartInfo("git", WorkingDirectory = source, RedirectStandardOutput = true, RedirectStandardError = true)
    for arg in [ "show"; producer + ":" + path ] do start.ArgumentList.Add arg
    use child = Process.Start start
    let result = child.StandardOutput.ReadToEnd()
    let error = child.StandardError.ReadToEnd()
    child.WaitForExit()
    if child.ExitCode <> 0 then failwith ("missing immutable retained fixture input: " + error)
    result
let cache = Collections.Generic.Dictionary<string, string>()
let old path =
    match cache.TryGetValue path with
    | true, value -> value
    | _ -> let value = readOld path in cache.Add(path, value); value
let selectedPaths (read: string -> string) =
    [ for manifest in [ "registry/coordination-kit-skill-manifest.json"; "registry/driver-skill-manifest.json" ] do
          for row in JsonNode.Parse(read manifest).["skills"].AsArray() do
              let id = row.["id"].GetValue<string>()
              let selected =
                  if manifest.Contains("driver-") then row.["scope"].GetValue<string>() = "driver"
                  else Set.contains id (Set.ofList [ "check-board"; "cross-repo-coordination"; "initialize-sdd-workspace"; "intra-repo-parallel-work"; "pnext-item" ])
              if selected then
                  for leaf in row.["files"].AsArray() do
                      for root in [ ".agents/skills"; ".claude/skills" ] do
                          yield root + "/" + id + "/" + leaf.["path"].GetValue<string>() ]
let oldPaths = selectedPaths old
let actual path = File.ReadAllText(Path.Combine(source, path))
let fetch revision path =
    try Ok(if revision = producer then old path else actual path)
    with error -> Error error.Message
let binding = """{"bindingVersion":3,"importRecipeRevision":"eeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeee","importArtifactSha256":"ffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff","selectedIssues":{"I_product":"acme/app#7"},"schemaVersion":1,"recipeRevision":"f984a3619129c5e9ab68e1291fd080ef77ef9337","populationRevision":"cccccccccccccccccccccccccccccccccccccccc","organizationId":"O_acme","artifactSha256":"ADAPTER","ownerKind":"organization","owner":"acme","projectNumber":77,"projectTitle":"Product","projectId":"PVT_product","status":{"id":"S_status","options":{"Backlog":"s1","Ready":"s2","In progress":"s3","Blocked":"s4","Done":"s5"}},"roadmapFieldId":"F_roadmap","track":{"id":"S_track","options":{"Active delivery":"t1","Follow-up":"t2"}},"observation":{"id":"S_observation","options":{"Verified":"o1","Stale":"o2","Unknown":"o3"}},"repositories":["acme/app"]}""".Replace("ADAPTER", adapter)
let root = Path.Combine(Path.GetTempPath(), "retained-product-controls-" + Guid.NewGuid().ToString "N")
Directory.CreateDirectory root |> ignore
try
    if pin <> "0.108.0" then
        ProductBoard.prepare root "acme/app" producer binding (fetch producer) |> refuse "retained108 inputs with a different compiled adapter"
        printfn "Retained positive upgrade requires the separately qualified published108 dependency mode; incompatible adapter refusal passed."
    else
        ProductBoard.prepare root "acme/app" producer binding (fetch producer) |> pass |> ProductBoard.apply root |> pass
        let provenancePath = Path.Combine(root, ".fsgg/scaffold-provenance.json")
        let provenance = JsonNode.Parse(File.ReadAllText provenancePath).AsObject()
        provenance.["schemaVersion"] <- JsonValue.Create 1
        provenance.["generator"] <- JsonNode.Parse("""{"id":"FS.GG.SDD.Artifacts","version":"2.2.0"}""")
        // This is the genuine old manifest's42-leaf generated subset, not an invented820-path grant.
        let generatedSource = File.ReadAllText(Path.Combine(source, "scripts/NewSddWorkspace/ProductBoard.fs"))
        let declaration = Text.RegularExpressions.Regex.Match(generatedSource, "(?s)let private generatedDriverPaths =(.*?)let captureFreshScaffoldTarget").Groups.[1].Value
        let generatedPaths =
            [ for item in Text.RegularExpressions.Regex.Matches(declaration, "\"([^\"]+)\"") do
                  let leaf = item.Groups.[1].Value
                  if leaf.Contains("/") && leaf <> "/" && not (leaf.StartsWith(".", StringComparison.Ordinal)) then
                      for runtime in [ ".agents/skills"; ".claude/skills" ] do yield runtime + "/" + leaf ] |> Set.ofList
        if generatedPaths.Count <> 48 then failwith "current generated fixture closure changed"
        let adoptionPaths = oldPaths |> List.filter (fun path -> Set.contains path generatedPaths)
        if oldPaths.Length <> 820 || adoptionPaths.Length <> 42 then failwith "immutable retained fixture closure changed"
        let active = JsonArray()
        let adopted = JsonArray()
        for relative in adoptionPaths do
            let sha = digest (File.ReadAllBytes(Path.Combine(root, relative)))
            let row = JsonObject()
            row.["path"] <- JsonValue.Create relative
            row.["owner"] <- JsonValue.Create "driver"
            row.["sha256"] <- JsonValue.Create sha
            active.Add row
            let record = JsonObject()
            record.["path"] <- JsonValue.Create relative
            record.["beforeSha256"] <- JsonValue.Create(String.replicate 64 "0")
            record.["afterSha256"] <- JsonValue.Create sha
            adopted.Add record
        let unrelated = "scripts/owner-workspace-driver"
        File.WriteAllText(Path.Combine(root, unrelated), "unchanged workspace driver\n")
        let unrelatedRow = JsonObject()
        unrelatedRow.["path"] <- JsonValue.Create unrelated
        unrelatedRow.["owner"] <- JsonValue.Create "driver"
        unrelatedRow.["sha256"] <- JsonValue.Create(digest (File.ReadAllBytes(Path.Combine(root, unrelated))))
        active.Add unrelatedRow
        provenance.["driverPaths"] <- active
        let adoption = JsonObject()
        adoption.["schemaVersion"] <- JsonValue.Create 1
        adoption.["kitRevision"] <- JsonValue.Create producer
        adoption.["drivers"] <- adopted
        adoption.["originalScaffoldProvenance"] <- JsonNode.Parse("""{"preserve":"original historical object"}""")
        provenance.["productBoard"].["generatedDriverAdoption"] <- adoption
        File.WriteAllText(provenancePath, provenance.ToJsonString())
        let original = File.ReadAllBytes provenancePath
        let owner = Path.Combine(root, "qualification-owner.txt")
        File.WriteAllText(owner, "synthetic preservation witness\n")
        if not (OperatingSystem.IsWindows()) then File.SetUnixFileMode(owner, enum<UnixFileMode> 416)
        let snapshot relative =
            let path = Path.Combine(root, relative)
            File.ReadAllBytes path, (if OperatingSystem.IsWindows() then None else Some(File.GetUnixFileMode path)), File.GetLastWriteTimeUtc path
        let protectedPaths = [ "qualification-owner.txt"; unrelated; ".config/dotnet-tools.json"; ".claude/settings.json"; ".fsgg/board-v2-binding.json" ]
        let protectedBefore = protectedPaths |> List.map (fun path -> path, snapshot path)
        let revision = String.replicate 40 "a"
        let prepare transport = ProductBoard.prepareRetained root "acme/app" revision binding transport
        ProductBoard.prepare root "acme/app" revision binding (fetch revision) |> refuse "ordinary prepare cannot upgrade an existing owner"
        prepare (fun revision path -> fetch revision path |> Result.map (fun raw -> if revision = producer then raw + " " else raw)) |> refuse "untrusted predecessor manifest"
        let receiverBinding = Path.Combine(root, ".fsgg/board-v2-binding.json")
        let receiverBindingBytes = File.ReadAllBytes receiverBinding
        let receiverBindingTime = File.GetLastWriteTimeUtc receiverBinding
        File.AppendAllText(receiverBinding, " ")
        prepare fetch |> refuse "instruction authority cannot overwrite receiver binding edits"
        File.WriteAllBytes(receiverBinding, receiverBindingBytes)
        File.SetLastWriteTimeUtc(receiverBinding, receiverBindingTime)
        let changedOutside = ".agents/skills/pnext-item/SKILL.md"
        let originalLeaf = File.ReadAllBytes(Path.Combine(root, changedOutside))
        File.AppendAllText(Path.Combine(root, changedOutside), "owner edit")
        prepare fetch |> refuse "owner edit outside fixed generated paths"
        File.WriteAllBytes(Path.Combine(root, changedOutside), originalLeaf)
        let missing = Path.Combine(root, ".agents/skills/atlas/SKILL.md")
        let missingBefore = File.ReadAllBytes missing
        File.Delete missing
        prepare fetch |> refuse "missing accepted old instruction"
        File.WriteAllBytes(missing, missingBefore)
        let introduced = Path.Combine(root, ".agents/skills/work-board/references/lifecycle-contract.md")
        Directory.CreateDirectory(Path.GetDirectoryName introduced) |> ignore
        File.WriteAllText(introduced, "owner-authored prospective destination")
        prepare fetch |> refuse "preexisting new instruction destination"
        File.Delete introduced
        let plan = prepare fetch |> pass
        if not (plan.Changes |> List.exists (fun change -> change.Path = changedOutside)) then failwith "upgrade omitted changed instruction outside fixed48"
        // Even an unchanged old leaf belongs to the transaction's rechecked whole preimage.
        let unchanged = ".agents/skills/initialize-sdd-workspace/SKILL.md"
        let unchangedBytes = File.ReadAllBytes(Path.Combine(root, unchanged))
        File.AppendAllText(Path.Combine(root, unchanged), "race")
        ProductBoard.apply root plan |> refuse "unchanged owned leaf changed after preview"
        if File.ReadAllBytes provenancePath <> original then failwith "race refusal wrote provenance"
        File.WriteAllBytes(Path.Combine(root, unchanged), unchangedBytes)
        let plan = prepare fetch |> pass
        let beforeRollback = plan.Changes |> List.map (fun change -> change.Path, if change.Before.IsSome then Some(snapshot change.Path) else None)
        File.WriteAllText(Path.Combine(root, "blocked"), "owner")
        let impossible: ProductBoard.FileChange = { Path = "blocked/child"; Before = None; After = [| 1uy |]; Executable = false; BeforeMode = None }
        ProductBoard.apply root { plan with Changes = plan.Changes @ [ impossible ] } |> refuse "late write failure"
        for path, before in beforeRollback do
            match before with
            | Some expected when snapshot path <> expected -> failwith ("rollback changed preimage bytes/mode/mtime: " + path)
            | None when File.Exists(Path.Combine(root, path)) -> failwith ("rollback left new leaf: " + path)
            | _ -> ()
        let plan = prepare fetch |> pass
        ProductBoard.apply root plan |> pass
        let final = JsonNode.Parse(File.ReadAllText provenancePath)
        let transition = final.["productBoard"].["instructionUpgrade"]
        if Convert.FromBase64String(transition.["originalProvenanceBase64"].GetValue<string>()) <> original
           || transition.["originalProvenanceSha256"].GetValue<string>() <> digest original
           || not (JsonNode.DeepEquals(final.["productBoard"].["generatedDriverAdoption"], adoption)) then
            failwith "upgrade lost original whole provenance or rewrote historical42-leaf adoption"
        let finalDrivers = final.["driverPaths"].AsArray() |> Seq.filter (fun row -> row.["path"].GetValue<string>() <> unrelated) |> Seq.length
        if finalDrivers <> 48 then failwith "retained generated instruction ownership did not transition42 to48"
        for path, expected in protectedBefore do
            if snapshot path <> expected then failwith ("upgrade changed protected bytes/mode/mtime: " + path)
        let afterUpgrade = File.ReadAllBytes provenancePath
        let repeat = prepare fetch |> pass
        if not repeat.Changes.IsEmpty then failwith "repeat upgrade must be a no-op"
        ProductBoard.apply root repeat |> pass
        if File.ReadAllBytes provenancePath <> afterUpgrade then failwith "repeat appended provenance"
        File.AppendAllText(Path.Combine(root, changedOutside), "owner edit after upgrade")
        prepare fetch |> refuse "repeat cannot overwrite new owner edit"
        printfn "Retained compiled controls passed: immutable820-leaf predecessor, real42-to48 ownership, outside-fixed-leaf replacement, owner/race refusal, rollback including mtimes, exact repeat and original custody."
finally
    Directory.Delete(root, true)
