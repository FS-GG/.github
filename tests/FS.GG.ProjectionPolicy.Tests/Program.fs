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
let yaml = YamlStream()
let reader = new StreamReader(Path.Combine(root, "registry/skills.yml"))
yaml.Load(reader)
reader.Dispose()
let mapping = yaml.Documents[0].RootNode :?> YamlMappingNode
let catalog = mapping.Children[YamlScalarNode("skills")] :?> YamlSequenceNode
let field (row: YamlMappingNode) key =
    match row.Children.TryGetValue(YamlScalarNode(key)) with
    | true, (:? YamlScalarNode as value) when not (isNull value.Value) -> value.Value
    | _ -> failwithf "registry skill row lacks %s" key
let rows =
    catalog.Children
    |> Seq.map (fun node ->
        let row = node :?> YamlMappingNode
        { Id = field row "id"; Scope = field row "scope"; Owner = field row "owner" })
    |> Seq.toList

let document = File.ReadAllText(Path.Combine(root, "docs/registry/compatibility.md"))
let marker = "<!-- BEGIN GENERATED: fsgg-skill-registry-counts -->"
let start = document.IndexOf(marker, StringComparison.Ordinal)
if start < 0 || document.IndexOf(marker, start + marker.Length, StringComparison.Ordinal) >= 0 then
    failwith "missing or duplicate count-region marker"
let finish = document.IndexOf("<!-- END GENERATED: fsgg-skill-registry-counts -->", start, StringComparison.Ordinal)
if finish < 0 then failwith "missing count-region end marker"
let region = document.Substring(start, finish - start)
let summaryStart = region.IndexOf("**", StringComparison.Ordinal)
if summaryStart < 0 then failwith "count-region dynamic body missing"
let observed = region.Substring(summaryStart).TrimEnd('\r', '\n')
match SkillCounts.render rows with
| Error message -> failwithf "checked-in registry refused: %s" message
| Ok rendered -> expect "current source bytes project to checked-in count body" observed rendered

printfn "skill count F# projection controls: %d passed (%d checked-in rows)" controls rows.Length
