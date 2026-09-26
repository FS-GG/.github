namespace FS.GG.Org.Policy

open System
open System.Collections.Generic
open System.Text
open System.Text.RegularExpressions

// Coverage facts over a supplied, normalized project-reference graph. Empty subjects are unscoped,
// not an authoritative fleet-wide agreement.
type RuleBCoverage = { Subjects: string list; Uncovered: (string * string) list }

// Pure source-only rule (b) core. Filesystem discovery, project XML and workflow YAML are separate inputs.
module RuleB =
    let private diagnostic message =
        { Code = "coverage-input"; Path = "<coverage>"; Message = message }

    let private normalized (path: string) =
        not (String.IsNullOrWhiteSpace path)
        && not (path.StartsWith("/", StringComparison.Ordinal))
        && not (Regex.IsMatch(path, "^[A-Za-z]:", RegexOptions.CultureInvariant))
        && not (path.Contains('\\'))
        && not (path.Contains("//", StringComparison.Ordinal))
        && (path.Split('/') |> Array.forall (fun part -> part <> "" && part <> "." && part <> ".."))

    let private literalPrefix (pattern: string) =
        let wildcard =
            pattern
            |> Seq.tryFindIndex (fun character -> character = '*' || character = '?')
            |> Option.defaultValue pattern.Length
        pattern.Substring(0, wildcard).TrimEnd('/')

    let private patternRegex (pattern: string) =
        let expression = StringBuilder("^")
        let mutable index = 0
        while index < pattern.Length do
            if index + 2 < pattern.Length && pattern.Substring(index, 3) = "**/" then
                expression.Append("(?:.*/)?") |> ignore
                index <- index + 3
            elif index + 1 < pattern.Length && pattern.Substring(index, 2) = "**" then
                expression.Append(".*") |> ignore
                index <- index + 2
            elif pattern.[index] = '*' then
                expression.Append("[^/]*") |> ignore
                index <- index + 1
            elif pattern.[index] = '?' then
                expression.Append("[^/]") |> ignore
                index <- index + 1
            else
                expression.Append(Regex.Escape(string pattern.[index])) |> ignore
                index <- index + 1
        // `$` also matches before a final newline. A supplied dependency with that suffix must
        // not be pronounced covered by a pattern that does not include it.
        expression.Append("\\z") |> ignore
        Regex(expression.ToString(), RegexOptions.CultureInvariant)

    let private directory (path: string) =
        let separator = path.LastIndexOf('/')
        if separator < 0 then "" else path.Substring(0, separator)

    let private closure (graph: Map<string, string list>) project =
        let found = HashSet<string>(StringComparer.Ordinal)
        let pending = Stack<string>(graph.[project])
        while pending.Count > 0 do
            let dependency = pending.Pop()
            if found.Add dependency then
                for transitive in graph |> Map.tryFind dependency |> Option.defaultValue [] do
                    pending.Push transitive
        found |> Seq.sort |> Seq.toList

    // Inspect only facts supplied by a later authoritative enumerator. Every referenced project
    // must have a supplied graph node; this does not authenticate the enumerator or its roster.
    // A broad `src/**` or a single source-file pattern does not name a project; only a literal
    // prefix equal to its directory does. Refuse GitHub filter operators this matcher does not
    // implement instead of treating them as literal characters or a different wildcard.
    let inspect (patterns: string list) (graph: Map<string, string list>) : Result<RuleBCoverage, SyntaxDiagnostic> =
        if List.isEmpty patterns
           || patterns |> List.exists (fun value ->
               not (normalized value)
               || value.StartsWith("!", StringComparison.Ordinal)
               || value.IndexOfAny([| '?'; '+'; '['; ']' |]) >= 0) then
            Error(diagnostic "paths patterns must be nonempty, normalized, positive repo-relative strings using supported glob operators")
        elif graph |> Map.exists (fun project references -> not (normalized project) || references |> List.exists (normalized >> not)) then
            Error(diagnostic "project-reference paths must be normalized repo-relative strings")
        else
            let missingReference =
                graph
                |> Map.toSeq
                |> Seq.tryPick (fun (project, references) ->
                    references
                    |> List.tryFind (fun dependency -> not (graph.ContainsKey dependency))
                    |> Option.map (fun dependency -> project, dependency))
            match missingReference with
            | Some(project, dependency) ->
                Error(diagnostic (sprintf "%s references %s, absent from supplied project graph" project dependency))
            | None ->
                let prefixes = patterns |> List.map literalPrefix |> Set.ofList
                let matchers = patterns |> List.map patternRegex
                let selects (path: string) = matchers |> List.exists (fun pattern -> pattern.IsMatch path)
                let subjects =
                    graph
                    |> Map.toList
                    |> List.map fst
                    |> List.filter (fun project -> prefixes.Contains(directory project))
                let uncovered =
                    subjects
                    |> List.collect (fun project ->
                        closure graph project
                        |> List.filter (selects >> not)
                        |> List.map (fun dependency -> project, dependency))
                Ok { Subjects = subjects; Uncovered = uncovered }
