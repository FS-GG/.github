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
    let tool = sprintf "{\"tools\":{\"fs.gg.coord.cli\":{\"version\":\"%s\",\"commands\":[\"fsgg-coord-engine\"]}}}" pin
    let fetch = function
        | "registry/coordination-kit-skill-manifest.json" -> Ok(manifest kit)
        | "registry/driver-skill-manifest.json" -> Ok(manifest drivers)
        | "dist/dotnet/.config/dotnet-tools.json" -> Ok tool
        | "scripts/fsgg-coord" -> Ok "#!/bin/sh\nexit 0\n"
        | path when path.StartsWith ".claude/skills/" -> Ok content
        | path -> Error("missing " + path)
    let prepare fetch = ProductBoard.prepare root "acme/app" (String.replicate 40 "a") binding fetch
    let plan = prepare fetch |> pass
    if Directory.EnumerateFileSystemEntries(root) |> Seq.isEmpty |> not then failwith "preview wrote workspace files"
    prepare (fun _ -> Error "producer unavailable") |> refuse "missing publication"
    prepare (fun path -> fetch path |> Result.map (fun value -> if path.StartsWith ".claude/skills/" then value + "drift" else value)) |> refuse "consumer digest drift"
    ProductBoard.prepare root "acme/app" "main" binding fetch |> refuse "moving producer reference"
    ProductBoard.apply root plan |> pass
    if not ((prepare fetch |> pass).Changes.IsEmpty) then failwith "repeat must be a no-op"
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
