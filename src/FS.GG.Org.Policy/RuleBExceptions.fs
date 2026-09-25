namespace FS.GG.Org.Policy

open System
open System.Collections.Generic
open System.IO
open System.Text.RegularExpressions
open YamlDotNet.RepresentationModel

/// Pure source-only classification of rule (b)'s path-bound `allow-uncovered` comments.
module RuleBExceptions =
    type Disposition =
        | Uncovered
        | Unsigned
        | Signed of reason: string

    let private marker =
        Regex("^[ \\t]*#[ \\t]*paths-coherence:[ \\t]*allow-uncovered[ \\t]+(?<path>[^\\s—:]+)[ \\t]*[—:-]?[ \\t]*(?<reason>.*)$", RegexOptions.CultureInvariant)

    let private directory (path: string) =
        let slash = path.LastIndexOf('/')
        if slash < 0 then "" else path.Substring(0, slash)

    let private opaqueLines (text: string) =
        let stream = YamlStream()
        use reader = new StringReader(text)
        stream.Load(reader)
        let covered = HashSet<int>()
        let visited = HashSet<YamlNode>(HashIdentity.Reference)
        let pending = Stack<YamlNode>()
        pending.Push(stream.Documents.[0].RootNode)
        while pending.Count > 0 do
            let node = pending.Pop()
            if visited.Add(node) then
                match node with
                | :? YamlScalarNode as scalar when
                    scalar.Style = YamlDotNet.Core.ScalarStyle.Literal
                    || scalar.Style = YamlDotNet.Core.ScalarStyle.Folded ->
                    // A trailing newline leaves End at column 1 of the following line. Without
                    // one, End remains on the last content line, which is still opaque YAML data.
                    let last = int scalar.End.Line - (if int scalar.End.Column = 1 then 2 else 1)
                    for line in int scalar.Start.Line - 1 .. last do
                        covered.Add(line) |> ignore
                | :? YamlScalarNode as scalar when scalar.Start.Line < scalar.End.Line ->
                    for line in int scalar.Start.Line - 1 .. int scalar.End.Line - 1 do
                        covered.Add(line) |> ignore
                | :? YamlMappingNode as mapping ->
                    for pair in mapping.Children do
                        pending.Push(pair.Key)
                        pending.Push(pair.Value)
                | :? YamlSequenceNode as sequence ->
                    for child in sequence.Children do pending.Push(child)
                | _ -> ()
        covered

    /// Resolve only directories actually omitted by the supplied coverage observation. A signed
    /// marker for another directory can never excuse one of these entries.
    let inspect (path: string) (text: string) (coverage: RuleBCoverage) : Result<Map<string, Disposition>, SyntaxDiagnostic> =
        WorkflowSyntax.inspect path text
        |> Result.map (fun _ ->
            let opaque = opaqueLines text
            let markers =
                text.Split('\n')
                |> Array.mapi (fun index line -> index, line)
                |> Array.choose (fun (index, line) ->
                    let found = marker.Match(line.TrimEnd('\r'))
                    if found.Success && not (opaque.Contains index) then
                        Some(found.Groups.["path"].Value.TrimEnd('/'), found.Groups.["reason"].Value.Trim())
                    else None)
                |> Map.ofArray

            coverage.Uncovered
            |> List.map (snd >> directory)
            |> Set.ofList
            |> Seq.map (fun missing ->
                let disposition =
                    match markers |> Map.tryFind missing with
                    | None -> Uncovered
                    | Some "" -> Unsigned
                    | Some reason -> Signed reason
                missing, disposition)
            |> Map.ofSeq)
