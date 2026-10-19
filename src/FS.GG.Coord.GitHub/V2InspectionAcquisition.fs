namespace FS.GG.Coord.GitHub

/// Invocation-local acquisition for the fixed read-only inspection. Evaluation discovers
/// requests only; no mutation or authority result escapes until every discovered read resolves.
module internal V2InspectionAcquisition =
    open System
    open System.Collections.Generic
    open System.Text
    open System.Text.Json.Nodes
    open System.Text.RegularExpressions
    open Errors
    open Transport

    // Only fixed, internally authored queries reach this compositor. Preserve nested aliases,
    // fragments and quoted strings; prefix variables and *root response names* independently.
    let private compose index (request: Request) =
        match request.Body with
        | Query(document, variables) when request.Method = "POST" && request.Path = "graphql" && request.Budget = GraphQl
                                               && not (document.Contains("\"\"\"", StringComparison.Ordinal)) ->
            let prefix = $"b{index}_"
            let renamed = Regex.Replace(document, @"""(?:\\.|[^""\\])*""|\$([A-Za-z_][A-Za-z0-9_]*)", MatchEvaluator(fun matched ->
                if matched.Value.StartsWith("\"", StringComparison.Ordinal) then matched.Value
                else "$" + prefix + matched.Groups[1].Value))
            let opening = renamed.IndexOf '{'
            if opening < 0 || not (renamed.StartsWith("query", StringComparison.Ordinal)) then None
            else
                let header = renamed.Substring(0, opening)
                let start = header.IndexOf '('
                let declarations = if start < 0 then "" else header.Substring(start + 1, header.LastIndexOf(')') - start - 1)
                let selection = renamed.Substring(opening + 1, renamed.LastIndexOf('}') - opening - 1)
                let output = StringBuilder()
                let aliases = ResizeArray<string * string>()
                let mutable supported = not (header.Contains('=') || header.Contains('"') || header.Contains('#'))
                let mutable depth, arguments, position = 0, 0, 0
                let mutable quoted, escaped = false, false
                while position < selection.Length do
                    let character = selection.[position]
                    if quoted then
                        output.Append character |> ignore
                        if escaped then escaped <- false
                        elif character = '\\' then escaped <- true
                        elif character = '"' then quoted <- false
                        position <- position + 1
                    elif character = '#' || character = '@' || (depth = 0 && character = '.') then
                        supported <- false; output.Append character |> ignore; position <- position + 1
                    elif character = '"' then
                        quoted <- true; output.Append character |> ignore; position <- position + 1
                    elif depth = 0 && arguments = 0 && (Char.IsLetter character || character = '_') then
                        let finish = Regex.Match(selection.Substring(position), "^[A-Za-z_][A-Za-z0-9_]*").Length + position
                        let original = selection.Substring(position, finish - position)
                        let mutable next = finish
                        while next < selection.Length && Char.IsWhiteSpace selection.[next] do next <- next + 1
                        let alias = prefix + original
                        aliases.Add(alias, original)
                        output.Append(alias).Append(':') |> ignore
                        if next < selection.Length && selection.[next] = ':' then
                            position <- next + 1
                            while position < selection.Length && Char.IsWhiteSpace selection.[position] do position <- position + 1
                            let length = Regex.Match(selection.Substring(position), "^[A-Za-z_][A-Za-z0-9_]*").Length
                            output.Append(selection.Substring(position, length)) |> ignore
                            position <- position + length
                        else
                            output.Append(original) |> ignore; position <- finish
                    else
                        if character = '{' then depth <- depth + 1
                        elif character = '}' then depth <- depth - 1
                        elif character = '(' then arguments <- arguments + 1
                        elif character = ')' then arguments <- arguments - 1
                        output.Append character |> ignore; position <- position + 1
                if supported && not quoted && depth = 0 && arguments = 0 then
                    Some(declarations, output.ToString(), variables |> List.map (fun (name, value) -> prefix + name, value), List.ofSeq aliases)
                else None
        | _ -> None

    let private acquire (transport: IGitHubTransport) (requests: Request list) =
        if requests.Length = 1 then [requests.Head, transport.Send (requests.Head)]
        else
            let parts = requests |> List.mapi compose
            if parts |> List.exists Option.isNone then requests |> List.map (fun request -> request, transport.Send request)
            else
                let parts = parts |> List.choose id
                let declarations = parts |> List.map (fun (value, _, _, _) -> value) |> List.filter (String.IsNullOrWhiteSpace >> not) |> String.concat ","
                let selection = parts |> List.map (fun (_, value, _, _) -> value) |> String.concat " "
                let variables = parts |> List.collect (fun (_, _, value, _) -> value)
                let document = "query" + (if declarations = "" then "" else "(" + declarations + ")") + "{" + selection + "}"
                let request = { requests.Head with Body = Query(document, variables); Subject = "bounded complete-cohort inspection batch" }
                let response = transport.Send request
                let batchAliases = parts |> List.collect (fun (_, _, _, aliases) -> aliases |> List.map fst) |> Set.ofList
                List.map2 (fun (original: Request) (_, _, _, (aliases: (string * string) list)) ->
                    let result = response |> Result.bind (fun value ->
                        try
                            let root = JsonNode.Parse value.Body
                            let data = JsonObject()
                            let errors = JsonArray()
                            for alias, name in aliases do
                                if not (isNull root.["data"]) && not (isNull root.["data"].[alias]) then data.[name] <- (root.["data"].[alias]).DeepClone()
                            if not (isNull root.["errors"]) then
                                for error in root.["errors"].AsArray() do
                                    let path = error.["path"]
                                    // Only a well-formed path rooted in a known alias can attribute a
                                    // failure. Missing/malformed/unknown paths affect the whole batch.
                                    let validSegment (segment: JsonNode) =
                                        match segment with
                                        | :? JsonValue as value ->
                                            match value.TryGetValue<string>() with
                                            | true, name -> not (String.IsNullOrWhiteSpace name)
                                            | _ -> match value.TryGetValue<int>() with true, index -> index >= 0 | _ -> false
                                        | _ -> false
                                    let knownRoot =
                                        match path with
                                        | :? JsonArray as segments when segments.Count > 0 && (segments |> Seq.forall validSegment) ->
                                            match segments.[0] with
                                            | :? JsonValue as first ->
                                                match first.TryGetValue<string>() with
                                                | true, alias when batchAliases.Contains alias -> Some alias
                                                | _ -> None
                                            | _ -> None
                                        | _ -> None
                                    if knownRoot.IsNone || (aliases |> List.exists (fun (alias, _) -> knownRoot = Some alias)) then
                                        errors.Add(error.DeepClone())
                            let decoded = JsonObject()
                            decoded.["data"] <- data
                            if errors.Count > 0 then decoded.["errors"] <- errors
                            Ok { value with Body = decoded.ToJsonString(); ETag = None }
                        with _ -> Error(Malformed(original.Subject, "native inspection batch response is unreadable")))
                    original, result) requests parts

    /// One phase owns one cache; callers create a new phase after integrator reads. The
    /// evaluator must be read-only and recreate mutable gates/counters on every discovery round.
    let run (transport: IGitHubTransport) (evaluate: IGitHubTransport -> 'value) =
        let responses = Dictionary<Request, IoResult<Response>>()
        let pending = ResizeArray<Request>()
        let pendingKeys = HashSet<Request>()
        let deferred = Malformed("inspection acquisition", "read awaits bounded phase acquisition")
        let replay =
            { new IGitHubTransport with
                member _.Send request =
                    match responses.TryGetValue request with
                    | true, response -> response
                    | _ -> if pendingKeys.Add request then pending.Add request
                           Error deferred
                member _.SendMutation _ = Error(Malformed("inspection acquisition", "mutation is unavailable"))
                member _.RetryMutation _ = Error(Malformed("inspection acquisition", "replay is unavailable")) }
        let rec collect round =
            pending.Clear()
            pendingKeys.Clear()
            let value = evaluate replay
            if pending.Count = 0 then value
            elif round >= 64 then
                // Bound discovery independently of the provider/invocation budgets. Every
                // unresolved read becomes a real failure and receives one final evaluation.
                for request in pending do responses.[request] <- Error(Malformed(request.Subject, "inspection acquisition round bound reached"))
                evaluate replay
            else
                let discovered = pending |> Seq.toList
                let graphql, rest = discovered |> List.partition (fun request -> compose 0 request |> Option.isSome)
                for request in rest do responses.[request] <- transport.Send request
                for batch in graphql |> List.chunkBySize 10 do
                    for request, result in acquire transport batch do responses.[request] <- result
                collect (round + 1)
        collect 0
