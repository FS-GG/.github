namespace FS.GG.Org.EffectiveSteps

open System
open System.IO
open System.Text.RegularExpressions
open YamlDotNet.RepresentationModel

/// Source-only candidate over workflow and fixture text. No filesystem or receiver effects.
module EffectiveSteps =
    let private scalar (node: YamlNode) =
        match node with
        | :? YamlScalarNode as value -> Option.ofObj value.Value
        | _ -> None

    let private memberValue (name: string) (node: YamlNode) =
        match node with
        | :? YamlMappingNode as mapping ->
            mapping.Children
            |> Seq.tryPick (fun pair ->
                if scalar pair.Key = Some name then Some pair.Value else None)
        | _ -> None

    let private inactive (node: YamlNode) =
        match memberValue "if" node |> Option.bind scalar with
        | None -> false
        | Some value ->
            let condition = value.Trim().ToLowerInvariant().Replace("${{", "").Replace("}}", "").Trim()
            condition = "false" || condition = "0" || condition.StartsWith("false &&", StringComparison.Ordinal)

    let private nonGating (node: YamlNode) =
        match memberValue "continue-on-error" node with
        | None -> false
        | Some value ->
            match scalar value with
            | Some text when
                let condition = text.Trim().ToLowerInvariant()
                condition = "false" || condition = "0" || condition = "${{ false }}" || condition = "${{ 0 }}" -> false
            | _ -> true

    // A deliberately bounded proof: accept only direct command lines. Unknown shell
    // forms remain unproven until independently characterized and added here.
    let private invokes (script: string) (path: string) =
        let pattern = @"^\s*(?:python3?|bash|sh)\s+['""']?" + Regex.Escape(path) + @"['""']?(?:\s|$)"
        let mutable dead = 0
        let mutable heredoc: string option = None
        script.Split('\n')
        |> Array.exists (fun line ->
            let line = line.TrimEnd('\r')
            let trimmed = line.Trim()
            match heredoc with
            | Some ending ->
                if trimmed = ending then heredoc <- None
                false
            | None when Regex.IsMatch(trimmed, @"^if\s+(?:false|0)\s*;\s*then") ->
                dead <- dead + 1
                false
            | None when dead > 0 ->
                if Regex.IsMatch(trimmed, @"^if\b") then dead <- dead + 1
                elif Regex.IsMatch(trimmed, @"^fi(?:\s|;|$)") then dead <- dead - 1
                false
            | None ->
                let code = line.Split('#', 2)[0]
                let declaration = Regex.Match(code, @"<<-?\s*['""']?([A-Za-z_][A-Za-z_0-9]*)")
                if declaration.Success then heredoc <- Some declaration.Groups[1].Value
                let trimmedCode = code.Trim()
                not (trimmedCode.StartsWith("false &&", StringComparison.Ordinal)
                     || trimmedCode.EndsWith("\\", StringComparison.Ordinal)
                     || trimmedCode.Contains("||", StringComparison.Ordinal))
                && Regex.IsMatch(code, pattern))

    /// Prove that a gating workflow step executes a checker and its test fixture,
    /// either directly or through an executable fixture body.
    let evaluate (workflowYaml: string) (fixtureBody: string) (checker: string) (fixture: string) : Result<unit, string> =
        try
            let stream = YamlStream()
            stream.Load(new StringReader(workflowYaml))
            if stream.Documents.Count <> 1 then Error "workflow document count"
            else
                let root = stream.Documents[0].RootNode
                match memberValue "jobs" root with
                | Some (:? YamlMappingNode as jobs) ->
                    let mutable fixtureSeen = false
                    let mutable checkerSeen = false
                    let mutable malformed = false
                    for job in jobs.Children.Values do
                        if not (inactive job || nonGating job) then
                            match memberValue "steps" job with
                            | Some (:? YamlSequenceNode as steps) ->
                                if memberValue "uses" job |> Option.isSome then malformed <- true
                                for step in steps.Children do
                                    if not (inactive step || nonGating step) then
                                        match memberValue "run" step with
                                        | Some run ->
                                            if memberValue "uses" step |> Option.isSome then malformed <- true
                                            match scalar run with
                                            | Some body ->
                                                fixtureSeen <- fixtureSeen || invokes body fixture
                                                checkerSeen <- checkerSeen || invokes body checker
                                            | None -> malformed <- true
                                        | None -> ()
                            | _ -> malformed <- true
                    if malformed then Error "malformed workflow step shape"
                    elif not fixtureSeen then Error "fixture not executed"
                    elif checkerSeen || invokes fixtureBody checker then Ok ()
                    else Error "checker not executed"
                | _ -> Error "missing jobs mapping"
        with
        | :? YamlDotNet.Core.YamlException -> Error "malformed YAML"
        | :? ArgumentException -> Error "malformed YAML"
