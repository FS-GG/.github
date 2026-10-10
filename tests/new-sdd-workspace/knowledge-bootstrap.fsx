open NewSddWorkspace.Program
open System
open System.IO
open System.Text.Json.Nodes
let expectOk label = function Ok value -> value | Error note -> failwithf "%s: %s" label note
let expectError label = function Error _ -> () | Ok _ -> failwithf "%s accepted" label
for value in [ "2.0.3"; "1.99.99"; "2.1.0-preview.1"; "2.1"; "2.1.0 extra"; "02.1.0"; "latest" ] do
    knowledgeProducerVersion value |> expectError value
for value in [ "2.1.0"; "2.1.1"; "3.0.0" ] do
    knowledgeProducerVersion value |> expectOk value |> ignore
let root = Path.Combine(Path.GetTempPath(), "wizard-knowledge-manifest-" + Guid.NewGuid().ToString "N")
Directory.CreateDirectory(Path.Combine(root, ".config")) |> ignore
let path = Path.Combine(root, ".config", "dotnet-tools.json")
try
    File.WriteAllText(path, """{"version":1,"isRoot":true,"tools":{"fable":{"version":"5.13.0","commands":["fable"]}}}""")
    pinKnowledgeProducer root "2.1.0" |> expectOk "merge"
    let manifest = JsonNode.Parse(File.ReadAllText path)
    if manifest.["tools"].["fable"].["version"].GetValue<string>() <> "5.13.0" then failwith "other tool clobbered"
    if manifest.["tools"].["fs.gg.sdd.cli"].["version"].GetValue<string>() <> "2.1.0" then failwith "exact producer pin missing"
    let before = File.ReadAllBytes path
    pinKnowledgeProducer root "2.1.1" |> expectError "owner conflict"
    if File.ReadAllBytes path <> before then failwith "refused owner conflict wrote manifest"
    pinKnowledgeProducer root "2.0.3" |> expectError "insufficient version"
    if File.ReadAllBytes path <> before then failwith "insufficient producer wrote manifest"
    File.WriteAllText(path, """{"version":1,"isRoot":false,"tools":{}}""")
    let before = File.ReadAllBytes path
    pinKnowledgeProducer root "2.1.0" |> expectError "nonroot owner manifest"
    if File.ReadAllBytes path <> before then failwith "nonroot refusal wrote manifest"
    File.WriteAllText(path, """{"version":1,"isRoot":true,"tools":{"FS.GG.SDD.Cli":{"version":"2.1.0","commands":["fsgg-sdd"]}}}""")
    let before = File.ReadAllBytes path
    pinKnowledgeProducer root "2.1.0" |> expectError "ambiguous owner key"
    if File.ReadAllBytes path <> before then failwith "ambiguous key refusal wrote manifest"
    let cache = Path.Combine(root, "cache")
    Directory.CreateDirectory cache |> ignore
    knowledgeOverlayRegistration cache |> expectOk "empty cache" |> ignore
    File.WriteAllText(Path.Combine(cache, "packages.json"), "malformed")
    knowledgeOverlayRegistration cache |> expectError "malformed engine metadata"
    File.WriteAllText(Path.Combine(cache, "packages.json"), """{"Packages":[{"Details":{"PackageId":"FS.GG.Workspace.Template","Version":"0.18.2"}},{"Details":{"PackageId":"FS.GG.Workspace.Template","Version":"0.18.2"}}]}""")
    knowledgeOverlayRegistration cache |> expectError "duplicate engine registration"
    let game = assembleWizardTemplateOptions "./Arena" "Arena" "fable-game" None None None None
    for version in [ "0.17.0"; "0.18.0" ] do
        let source = Some("source: FS.GG.Workspace.Template::" + version)
        if selectScaffoldLifecycle game source <> "sdd" then
            failwith "successor descriptor must preserve Standard SDD on omission"
        if selectScaffoldLifecycle { game with LifecycleExplicit = true; Lifecycle = "typed-sdd" } source <> "typed-sdd" then
            failwith "explicit typed selection must remain unchanged"
    if selectScaffoldLifecycle { game with LifecycleExplicit = true; Lifecycle = "sdd" } (Some "source: FS.GG.Workspace.Template::0.18.0") <> "sdd" then
        failwith "explicit Standard SDD changed"
finally Directory.Delete(root, true)
printfn "typed knowledge version and exact local manifest: ok"
