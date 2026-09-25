open System
open System.IO
open FS.GG.ProjectionPolicy
open YamlDotNet.RepresentationModel

let mutable controls = 0
let expect label expected actual =
    controls <- controls + 1
    if actual <> expected then failwithf "%s: expected %A, got %A" label expected actual
let refuses label rows =
    controls <- controls + 1
    match SkillCounts.render rows with
    | Error _ -> ()
    | Ok value -> failwithf "%s: accepted %s" label value
let refusesInput label read =
    controls <- controls + 1
    let admitted =
        try read (); true
        with _ -> false
    if admitted then failwithf "%s: unsafe input was admitted" label

let a = { Id = "a"; Scope = "product"; Owner = "owner-a" }
let b = { Id = "b"; Scope = "process"; Owner = "owner-b" }
let c = { Id = "c"; Scope = "driver"; Owner = "owner-c" }
let expected = "**3 rows** = **1 process** + **1 product** + **1 driver** (1 `owner-a`).\n\n| scope | owner | rows |\n|---|---|---|\n| driver | `owner-c` | 1 |\n| process | `owner-b` | 1 |\n| product | `owner-a` | 1 |\n| **total** | | **3** |"
expect "deterministic order independent of source rows" (Ok expected) (SkillCounts.render [a; b; c])
expect "reversed source rows" (Ok expected) (SkillCounts.render [c; b; a])
refuses "empty catalog" []
refuses "duplicate id across scopes" [a; { b with Id = "a" }]
refuses "missing id" [{ a with Id = "" }]
refuses "missing owner" [{ a with Owner = " " }]
refuses "Markdown owner injection" [{ a with Owner = "owner-a` | 99 |\n| forged" }]
refuses "control character in id" [{ a with Id = "a\000b" }]
refuses "unsupported scope" [{ a with Scope = "invented" }]

let root = Path.GetFullPath(Path.Combine(__SOURCE_DIRECTORY__, "../.."))

// Read-only test adapter. The production reducer never observes physical paths.
let readNoLinks path =
    let absolute = Path.GetFullPath path
    let mutable cursor = Path.GetPathRoot absolute
    for part in (Path.GetRelativePath(cursor, absolute).Split(Path.DirectorySeparatorChar)) do
        cursor <- Path.Combine(cursor, part)
        if File.GetAttributes(cursor).HasFlag(FileAttributes.ReparsePoint) then
            raise (IOException(sprintf "source or target crosses a symlink: %s" cursor))
    File.ReadAllText absolute

let field (row: YamlMappingNode) key =
    match row.Children.TryGetValue(YamlScalarNode(key)) with
    | true, (:? YamlScalarNode as value) when not (isNull value.Value) -> value.Value
    | _ -> raise (FormatException(sprintf "registry skill row lacks %s" key))
let parseRows source =
    let yaml = YamlStream()
    use reader = new StringReader(source)
    yaml.Load(reader)
    if yaml.Documents.Count <> 1 then raise (FormatException "registry has no single YAML document")
    let mapping = yaml.Documents[0].RootNode :?> YamlMappingNode
    if field mapping "schemaVersion" <> "3" then raise (FormatException "unsupported registry schema")
    let catalog = mapping.Children[YamlScalarNode("skills")] :?> YamlSequenceNode
    catalog.Children
    |> Seq.map (fun node ->
        let row = node :?> YamlMappingNode
        { Id = field row "id"; Scope = field row "scope"; Owner = field row "owner" })
    |> Seq.toList

let marker = "<!-- BEGIN GENERATED: fsgg-skill-registry-counts -->"
let endMarker = "<!-- END GENERATED: fsgg-skill-registry-counts -->"
let projectedBody (document: string) =
    let lines = document.Split('\n')
    let positions value = lines |> Array.indexed |> Array.choose (fun (index, line) -> if line = value then Some index else None)
    let starts, ends = positions marker, positions endMarker
    if starts.Length <> 1 || ends.Length <> 1 || starts[0] >= ends[0] then
        raise (FormatException "count region needs one ordered pair of exact-line markers")
    let region = lines[starts[0]+1 .. ends[0]-1]
    match region |> Array.tryFindIndex (fun line -> line.StartsWith("**", StringComparison.Ordinal)) with
    | None -> raise (FormatException "count region has no dynamic body")
    | Some first -> String.Join("\n", region[first..]).TrimEnd('\n')

let sampleRegion = marker + "\n**1 rows** = **0 process** + **1 product** ().\n" + endMarker
refusesInput "duplicate complete generated region" (fun () -> projectedBody (sampleRegion + "\n" + sampleRegion) |> ignore)
refusesInput "duplicate END marker" (fun () -> projectedBody (sampleRegion + "\n" + endMarker) |> ignore)
refusesInput "reversed marker order" (fun () -> projectedBody (endMarker + "\n" + sampleRegion) |> ignore)
let sampleRegistry = "schemaVersion: 3\nskills:\n  - {id: a, scope: product, owner: owner-a}\n"
refusesInput "missing registry owner" (fun () -> parseRows "schemaVersion: 3\nskills:\n  - {id: a, scope: product}\n" |> ignore)
refusesInput "malformed registry root" (fun () -> parseRows "- [a, b]\n" |> ignore)
refusesInput "unsupported registry schema" (fun () -> parseRows (sampleRegistry.Replace("schemaVersion: 3", "schemaVersion: 9")) |> ignore)
refusesInput "duplicate YAML root key" (fun () -> parseRows ("schemaVersion: 3\n" + sampleRegistry) |> ignore)
refusesInput "duplicate YAML row key" (fun () -> parseRows (sampleRegistry.Replace("owner: owner-a", "owner: owner-a, owner: owner-b")) |> ignore)
let duplicateSource = sampleRegistry + "  - {id: a, scope: driver, owner: owner-b}\n"
refuses "duplicate IDs survive YAML adapter" (parseRows duplicateSource)
let malformedScope = sampleRegistry.Replace("scope: product", "scope: [product]")
refusesInput "malformed scope is not rendered" (fun () -> parseRows malformedScope |> ignore)

let scratch = Path.Combine(Path.GetTempPath(), "fsgg-projection-policy-" + Guid.NewGuid().ToString("N"))
Directory.CreateDirectory(scratch) |> ignore
try
    let physical = Path.Combine(scratch, "target.md")
    let link = Path.Combine(scratch, "linked.md")
    File.WriteAllText(physical, sampleRegion)
    File.CreateSymbolicLink(link, physical) |> ignore
    refusesInput "symlinked target is not read by adapter" (fun () -> readNoLinks link |> projectedBody |> ignore)
    refusesInput "symlinked registry source is not read by adapter" (fun () -> readNoLinks link |> parseRows |> ignore)
finally
    Directory.Delete(scratch, true)

let rows = parseRows (readNoLinks (Path.Combine(root, "registry/skills.yml")))
let observed = readNoLinks (Path.Combine(root, "docs/registry/compatibility.md")) |> projectedBody
match SkillCounts.render rows with
| Error message -> failwithf "checked-in registry refused: %s" message
| Ok rendered -> expect "current source bytes project to checked-in count body" observed rendered

printfn "skill count F# projection controls: %d passed (%d checked-in rows)" controls rows.Length
