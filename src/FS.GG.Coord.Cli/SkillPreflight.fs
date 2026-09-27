namespace FS.GG.Coord.Cli

open System
open System.Collections.Generic
open System.Globalization
open System.IO
open System.Text
open System.Text.Json
open System.Text.RegularExpressions
open YamlDotNet.Core
open YamlDotNet.RepresentationModel

module SkillPreflight =
    exception private PreflightRefusal of string

    type CommandError = { Message: string; ExitCode: int }

    type EstimateDecision =
        | InsufficientData
        | NotCostJustified
        | Uncertain
        | PilotCandidate

    type GraphDecision =
        | Passed
        | Blocked of missingAncestors: string list

    [<Literal>]
    let private MaximumInputBytes = 2 * 1024 * 1024

    let private numberNames =
        [| "horizon_runs"
           "setup_cost"
           "maintenance_cost"
           "defect_probability_low"
           "defect_probability_high"
           "detection_probability"
           "avoidable_runner_minutes"
           "runner_cost_per_minute"
           "preflight_runner_minutes"
           "false_block_probability"
           "triage_cost" |]

    let private probabilityNames =
        numberNames |> Set.ofArray |> Set.filter (fun name -> name.Contains("probability", StringComparison.Ordinal))

    let private error message = Error { Message = message; ExitCode = 2 }
    let private refuse message = raise (PreflightRefusal message)

    // Match Python json.dumps' default ensure_ascii spelling because command success bytes are frozen.
    let private quote (value: string) =
        let output = StringBuilder(value.Length + 2)
        output.Append('"') |> ignore
        for character in value do
            match character with
            | '"' -> output.Append("\\\"") |> ignore
            | '\\' -> output.Append("\\\\") |> ignore
            | '\b' -> output.Append("\\b") |> ignore
            | '\f' -> output.Append("\\f") |> ignore
            | '\n' -> output.Append("\\n") |> ignore
            | '\r' -> output.Append("\\r") |> ignore
            | '\t' -> output.Append("\\t") |> ignore
            | value when int value < 0x20 || int value > 0x7e ->
                output.Append("\\u").Append((int value).ToString("x4", CultureInfo.InvariantCulture)) |> ignore
            | value -> output.Append(value) |> ignore
        output.Append('"').ToString()

    let private renderLines (lines: string list) =
        Encoding.UTF8.GetBytes(String.concat "\n" lines + "\n")

    let private renderFloat (value: float) =
        let rendered = value.ToString("R", CultureInfo.InvariantCulture)
        if value = Math.Truncate(value) && not (rendered.Contains('E')) then rendered + ".0" else rendered.ToLowerInvariant()

    let private ensureBound (input: ReadOnlyMemory<byte>) =
        if input.Length > MaximumInputBytes then error "Input exceeds 2 MiB" else Ok input

    let private rejectDuplicateJsonProperties (input: ReadOnlyMemory<byte>) =
        let mutable reader = Utf8JsonReader(input.Span, JsonReaderOptions(CommentHandling = JsonCommentHandling.Disallow))
        let objects = Stack<HashSet<string>>()
        let mutable duplicate: string option = None

        while duplicate.IsNone && reader.Read() do
            match reader.TokenType with
            | JsonTokenType.StartObject -> objects.Push(HashSet<string>(StringComparer.Ordinal))
            | JsonTokenType.EndObject -> objects.Pop() |> ignore
            | JsonTokenType.PropertyName ->
                let name = reader.GetString()
                if not (objects.Peek().Add(name)) then duplicate <- Some name
            | _ -> ()

        match duplicate with
        | Some name -> refuse $"Duplicate JSON key: {name}"
        | None -> ()

    let private getProperty (root: JsonElement) (name: string) =
        let mutable value = Unchecked.defaultof<JsonElement>
        if root.TryGetProperty(name, &value) then Some value else None

    let private missingOrNull root name =
        match getProperty root name with
        | None -> true
        | Some value -> value.ValueKind = JsonValueKind.Null

    let private readNumber root name =
        match getProperty root name with
        | Some value when value.ValueKind = JsonValueKind.Number ->
            let number = value.GetDouble()
            if not (Double.IsFinite(number)) || number < 0.0 then
                refuse $"{name} must be a finite nonnegative number"
            if Set.contains name probabilityNames && number > 1.0 then
                refuse $"{name} must be between 0 and 1"
            number
        | _ -> refuse $"{name} must be a finite nonnegative number"

    let private decisionText = function
        | InsufficientData -> "insufficient-data"
        | NotCostJustified -> "not-cost-justified"
        | Uncertain -> "uncertain"
        | PilotCandidate -> "pilot-candidate"

    let assess (input: ReadOnlyMemory<byte>) =
        try
            match ensureBound input with
            | Error refusal -> Error refusal
            | Ok input ->
                rejectDuplicateJsonProperties input
                use document = JsonDocument.Parse(input)
                let root = document.RootElement
                if root.ValueKind <> JsonValueKind.Object then refuse "Estimate must be an object"

                let allowed = Set.ofArray (Array.append numberNames [| "assumptions" |])
                let extras =
                    root.EnumerateObject()
                    |> Seq.map _.Name
                    |> Seq.filter (fun name -> not (Set.contains name allowed))
                    |> Seq.sort
                    |> Seq.toList
                if not extras.IsEmpty then
                    let names = String.concat ", " extras
                    refuse $"Unknown fields: {names}"

                let missing =
                    Array.append numberNames [| "assumptions" |]
                    |> Array.filter (missingOrNull root)
                    |> Array.toList

                if not missing.IsEmpty then
                    let missingLines =
                        missing
                        |> List.mapi (fun index name ->
                            let suffix = if index + 1 = missing.Length then "" else ","
                            $"    {quote name}{suffix}")
                    let output =
                        [ "{"
                          "  \"decision\": \"insufficient-data\","
                          "  \"missing\": [" ]
                        @ missingLines
                        @ [ "  ],"
                            "  \"basis\": \"estimates, not observed savings\""
                            "}" ]
                        |> renderLines
                    Ok(InsufficientData, output)
                else
                    let assumptionsElement = getProperty root "assumptions" |> Option.get
                    if assumptionsElement.ValueKind <> JsonValueKind.String || String.IsNullOrWhiteSpace(assumptionsElement.GetString()) then
                        refuse "Provide the horizon, rates and provenance in assumptions"

                    let values = numberNames |> Array.map (fun name -> name, readNumber root name) |> Map.ofArray
                    if values["horizon_runs"] <> Math.Truncate(values["horizon_runs"]) then
                        refuse "horizon_runs must be an integer"
                    if values["defect_probability_low"] > values["defect_probability_high"] then
                        refuse "Probability bounds are reversed"

                    let fixedCost = values["setup_cost"] + values["maintenance_cost"]
                    let overhead =
                        values["preflight_runner_minutes"] * values["runner_cost_per_minute"]
                        + values["false_block_probability"] * values["triage_cost"]

                    let outcome probability =
                        let benefit =
                            probability * values["detection_probability"] * values["avoidable_runner_minutes"]
                            * values["runner_cost_per_minute"]
                        let margin = benefit - overhead
                        let net = values["horizon_runs"] * margin - fixedCost
                        if [ fixedCost; overhead; benefit; margin; net ] |> List.exists (Double.IsFinite >> not) then
                            refuse "Costs exceed supported numeric range"
                        let breakEven =
                            if margin > 0.0 then
                                Some(Math.Ceiling(fixedCost / margin).ToString("F0", CultureInfo.InvariantCulture))
                            else None
                        net, margin, breakEven

                    let lowNet, lowMargin, lowBreakEven = outcome values["defect_probability_low"]
                    let highNet, highMargin, highBreakEven = outcome values["defect_probability_high"]
                    let decision =
                        if highNet <= 0.0 then NotCostJustified
                        elif lowNet > 0.0 then PilotCandidate
                        else Uncertain
                    let breakEvenText = function Some value -> value | None -> "null"
                    let output =
                        [ "{"
                          $"  \"decision\": {quote (decisionText decision)},"
                          "  \"basis\": \"estimates, not observed savings\","
                          $"  \"assumptions\": {quote (assumptionsElement.GetString())},"
                          "  \"outcomes\": {"
                          "    \"low\": {"
                          $"      \"net_benefit\": {renderFloat lowNet},"
                          $"      \"net_per_run\": {renderFloat lowMargin},"
                          $"      \"break_even_runs\": {breakEvenText lowBreakEven}"
                          "    },"
                          "    \"high\": {"
                          $"      \"net_benefit\": {renderFloat highNet},"
                          $"      \"net_per_run\": {renderFloat highMargin},"
                          $"      \"break_even_runs\": {breakEvenText highBreakEven}"
                          "    }"
                          "  },"
                          "  \"authority\": \"advisory; never waives required checks\""
                          "}" ]
                        |> renderLines
                    Ok(decision, output)
        with
        | PreflightRefusal message -> error message
        | :? JsonException as ex -> error ex.Message
        | :? ArgumentException as ex -> error ex.Message
        | :? OverflowException -> error "Costs exceed supported numeric range"

    let private scalarValue context (node: YamlNode) =
        match node with
        | :? YamlScalarNode as scalar when not (isNull scalar.Value) -> scalar.Value
        | _ -> refuse context

    let private mapping context (node: YamlNode) =
        match node with
        | :? YamlMappingNode as value -> value
        | _ -> refuse context

    let private rejectDuplicateYamlKeys (root: YamlNode) =
        // Aliases may revisit an already inspected node, including recursively. The source-size bound
        // applies to parser input, so inspect every represented node once rather than expanding aliases.
        let visited = HashSet<YamlNode>(HashIdentity.Reference)
        let rec visit (node: YamlNode) =
            if visited.Add(node) then
                match node with
                | :? YamlMappingNode as map ->
                    let keys = HashSet<string>(StringComparer.Ordinal)
                    for pair in map.Children do
                        let key = scalarValue "Expected literal YAML mapping keys" pair.Key
                        if not (keys.Add(key)) then refuse $"Duplicate YAML key: {key}"
                        visit pair.Value
                | :? YamlSequenceNode as sequence -> sequence.Children |> Seq.iter visit
                | :? YamlScalarNode -> ()
                | _ -> refuse "Unsupported YAML node shape"
        visit root

    let private tryChild (map: YamlMappingNode) name =
        map.Children
        |> Seq.tryPick (fun pair ->
            match pair.Key with
            | :? YamlScalarNode as key when key.Value = name -> Some pair.Value
            | _ -> None)

    let private validJobId = Regex("^[A-Za-z_][A-Za-z0-9_-]*$", RegexOptions.CultureInvariant)

    let graph (yaml: ReadOnlyMemory<byte>) (requirements: string list) =
        try
            match ensureBound yaml with
            | Error refusal -> Error refusal
            | Ok input ->
                let utf8 = UTF8Encoding(false, true)
                let text = utf8.GetString(input.Span)
                let stream = YamlStream()
                use reader = new StringReader(text)
                stream.Load(reader)
                if stream.Documents.Count <> 1 then refuse "Workflow must contain one YAML document"
                let rootNode = stream.Documents[0].RootNode
                rejectDuplicateYamlKeys rootNode
                let root = mapping "Workflow must contain a nonempty jobs mapping" rootNode
                let jobs =
                    match tryChild root "jobs" with
                    | Some node -> mapping "Workflow must contain a nonempty jobs mapping" node
                    | None -> refuse "Workflow must contain a nonempty jobs mapping"
                if jobs.Children.Count = 0 then refuse "Workflow must contain a nonempty jobs mapping"
                if jobs.Children.Count > 256 then refuse "More than 256 jobs: select a bounded explicit scope"

                let dependencies = Dictionary<string, string list>(StringComparer.Ordinal)
                for pair in jobs.Children do
                    let name = scalarValue "Expected literal job identifiers and job mappings" pair.Key
                    if not (validJobId.IsMatch(name)) then refuse "Expected literal job identifiers and job mappings"
                    let job = mapping "Expected literal job identifiers and job mappings" pair.Value
                    let needs =
                        match tryChild job "needs" with
                        | None -> []
                        | Some (:? YamlScalarNode as scalar) when not (isNull scalar.Value) -> [ scalar.Value ]
                        | Some (:? YamlSequenceNode as sequence) ->
                            sequence.Children
                            |> Seq.map (scalarValue $"{name}: needs must be literal job identifiers; expressions are unsupported")
                            |> Seq.toList
                        | Some _ -> refuse $"{name}: needs must be literal job identifiers; expressions are unsupported"
                    if needs |> List.exists (validJobId.IsMatch >> not) then
                        refuse $"{name}: needs must be literal job identifiers; expressions are unsupported"
                    if List.length needs <> (Set.ofList needs).Count then refuse $"{name}: duplicate dependency"
                    dependencies.Add(name, needs)

                for KeyValue(name, needs) in dependencies do
                    let unknown = needs |> List.filter (dependencies.ContainsKey >> not) |> List.sort
                    if not unknown.IsEmpty then
                        let names = unknown |> List.map quote |> String.concat ", "
                        refuse $"{name}: unknown dependencies [{names}]"

                let visiting = HashSet<string>(StringComparer.Ordinal)
                let ancestors = Dictionary<string, Set<string>>(StringComparer.Ordinal)
                let rec visit name =
                    if visiting.Contains(name) then refuse $"Dependency cycle at {name}"
                    match ancestors.TryGetValue(name) with
                    | true, known -> known
                    | _ ->
                        visiting.Add(name) |> ignore
                        let result =
                            dependencies[name]
                            |> List.fold (fun found dependency -> Set.add dependency (Set.union found (visit dependency))) Set.empty
                        visiting.Remove(name) |> ignore
                        ancestors.Add(name, result)
                        result
                dependencies.Keys |> Seq.iter (visit >> ignore)

                let mutable blocked: (string * string list) option = None
                for requirement in requirements do
                    if blocked.IsNone then
                        let separator = requirement.IndexOf(':')
                        let target = if separator < 0 then requirement else requirement.Substring(0, separator)
                        let sources = if separator < 0 then [||] else requirement.Substring(separator + 1).Split(',')
                        if separator < 0 || not (dependencies.ContainsKey(target)) || sources |> Array.exists (dependencies.ContainsKey >> not) then
                            refuse $"Unknown or malformed requirement {quote requirement}; use target:prerequisite[,prerequisite]"
                        let missing = sources |> Array.filter (fun source -> not (Set.contains source ancestors[target])) |> Array.sort |> Array.toList
                        if not missing.IsEmpty then blocked <- Some(target, missing)

                match blocked with
                | Some(target, missing) ->
                    let missingLines =
                        missing
                        |> List.mapi (fun index name ->
                            let suffix = if index + 1 = missing.Length then "" else ","
                            $"    {quote name}{suffix}")
                    let output =
                        [ "{"
                          "  \"decision\": \"blocked\","
                          "  \"scope\": \"dependency-order-only\","
                          $"  \"target\": {quote target},"
                          "  \"missing_ancestors\": [" ]
                        @ missingLines @ [ "  ]"; "}" ]
                        |> renderLines
                    Ok(Blocked missing, output)
                | None ->
                    let requirementLines =
                        requirements
                        |> List.mapi (fun index value ->
                            let suffix = if index + 1 = requirements.Length then "" else ","
                            $"    {quote value}{suffix}")
                    let output =
                        [ "{"
                          "  \"decision\": \"passed\","
                          "  \"scope\": \"dependency-order-only\","
                          $"  \"jobs\": {dependencies.Count},"
                          "  \"requirements\": [" ]
                        @ requirementLines @ [ "  ]"; "}" ]
                        |> renderLines
                    Ok(Passed, output)
        with
        | PreflightRefusal message -> error message
        | :? DecoderFallbackException as ex -> error ex.Message
        | :? YamlException as ex -> error ex.Message
        | :? ArgumentException as ex -> error ex.Message
