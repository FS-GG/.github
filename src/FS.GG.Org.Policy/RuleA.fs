namespace FS.GG.Org.Policy

open System
open System.Collections.Generic
open System.IO
open System.Text.RegularExpressions
open YamlDotNet.RepresentationModel

// A single-workflow result. The caller must refuse a whole-tree audit with zero audited pairs.
type RuleAResult = { AuditedPair: bool; Verdict: RuleAVerdict }

// Aggregate audit result; zero pairs cannot masquerade as a clean fleet scan.
type RuleAAudit = { AuditedPairs: int; Verdict: RuleAVerdict }

// Pure, source-only PR/push path agreement. This is not an installed gate or a parity receipt.
module RuleA =
    let private noVerdict path code message =
        { AuditedPair = false; Verdict = NoVerdict { Path = path; Code = code; Message = message } }

    let private result audited verdict = { AuditedPair = audited; Verdict = verdict }
    let private finding path message = Finding (path + ": " + message)

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
                | :? YamlScalarNode as scalar when scalar.Style = YamlDotNet.Core.ScalarStyle.Literal || scalar.Style = YamlDotNet.Core.ScalarStyle.Folded ->
                    // Mark lines are one-based; End is the first line after a block scalar.
                    for line in int scalar.Start.Line - 1 .. int scalar.End.Line - 2 do covered.Add(line) |> ignore
                | :? YamlScalarNode as scalar when scalar.Start.Line < scalar.End.Line ->
                    // A quoted (or multiline plain) scalar can contain a line that starts
                    // with '#'. It is still data, not a standalone YAML comment. Here End
                    // is on the closing scalar line, so include that line as well.
                    for line in int scalar.Start.Line - 1 .. int scalar.End.Line - 1 do covered.Add(line) |> ignore
                | :? YamlMappingNode as mapping ->
                    for pair in mapping.Children do
                        pending.Push(pair.Key)
                        pending.Push(pair.Value)
                | :? YamlSequenceNode as sequence ->
                    for child in sequence.Children do pending.Push(child)
                | _ -> ()
        covered

    let private marker (text: string) =
        let covered = opaqueLines text
        let regex = Regex("^[ \\t]*#[ \\t]*paths-coherence:[ \\t]*allow-divergence(?=$|[ \\t—:-])[ \\t]*[—:-]?[ \\t]*(?<reason>.*)$", RegexOptions.CultureInvariant)
        text.Split('\n')
        |> Array.mapi (fun index line -> index, regex.Match(line.TrimEnd('\r')))
        |> Array.choose (fun (index, found) ->
            if covered.Contains(index) || not found.Success then None
            else Some(found.Groups.["reason"].Value.Trim()))
        |> function
            | [||] -> None
            | reasons -> Some (reasons |> Array.tryFind (String.IsNullOrWhiteSpace >> not) |> Option.defaultValue "")

    let private validate path name paths =
        match paths with
        | Missing -> Ok None
        | Invalid why -> Error { Path = path; Code = "paths-shape"; Message = name + ".paths: " + why }
        | Sequence [] -> Error { Path = path; Code = "paths-empty"; Message = name + ".paths is empty" }
        | Sequence values when values |> List.exists (fun value -> value.StartsWith("!", StringComparison.Ordinal)) ->
            Error { Path = path; Code = "paths-negated"; Message = name + ".paths contains a negated pattern" }
        | Sequence values -> Ok (Some (Set.ofList values))

    // Evaluate rule (a) for one workflow. No filesystem, workflow mutation, or receiver effect.
    let inspect path text =
        match WorkflowSyntax.inspect path text with
        | Error diagnostic -> result false (NoVerdict diagnostic)
        | Ok syntax ->
            let pr, push = syntax.PullRequest, syntax.Push
            let prFiltered = pr.Paths <> Missing
            let pushFiltered = push.Paths <> Missing
            if (prFiltered && push.HasPathsIgnore) || (pushFiltered && pr.HasPathsIgnore) then
                noVerdict path "paths-ignore" "an allow-list faces an inverted ignore-list"
            elif not pr.Declared || not push.Declared || (not prFiltered && not pushFiltered) then
                result false Agreement
            else
                match validate path "pull_request" pr.Paths, validate path "push" push.Paths with
                | Error diagnostic, _ | _, Error diagnostic -> result false (NoVerdict diagnostic)
                | Ok prPaths, Ok pushPaths ->
                    let reason = marker text
                    match prPaths, pushPaths, reason with
                    | None, Some _, Some signed | Some _, None, Some signed when signed <> "" -> result true Agreement
                    | None, Some _, _ | Some _, None, _ -> result true (finding path "filtered/unfiltered triggers require a signed allow-divergence comment")
                    | Some _, Some _, Some "" -> result true (finding path "allow-divergence comment has no reason")
                    | Some left, Some right, Some _ when left = right -> result true (finding path "stale allow-divergence comment on identical filters")
                    | Some left, Some right, None when left <> right -> result true (finding path "pull_request and push paths differ")
                    | Some _, Some _, _ -> result true Agreement
                    | _ -> result false Agreement

    // Audit an already enumerated workflow set. Filesystem enumeration belongs to a later adapter.
    let audit (workflows: seq<string * string>) =
        let inspected = workflows |> Seq.map (fun (path, text) -> inspect path text) |> Seq.toList
        let count = inspected |> List.sumBy (fun item -> if item.AuditedPair then 1 else 0)
        let noVerdicts = inspected |> List.choose (fun item -> match item.Verdict with NoVerdict value -> Some value | _ -> None)
        let findings = inspected |> List.choose (fun item -> match item.Verdict with Finding value -> Some value | _ -> None)
        let verdict =
            match noVerdicts, findings with
            | first :: _, _ -> NoVerdict first
            | [], _ when count = 0 -> NoVerdict { Path = "<workflow-set>"; Code = "zero-pairs"; Message = "no comparable or signed-divergent trigger pair was audited" }
            | [], first :: _ -> Finding first
            | _ -> Agreement
        { AuditedPairs = count; Verdict = verdict }
