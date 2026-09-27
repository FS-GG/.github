namespace FS.GG.Coord.Cli

open System
open System.IO
open System.Text
open System.Text.Json
open SkillTelemetryAdapter

module SkillCommandApplication =
    let private utf8 = UTF8Encoding(false)

    let private emit (output: Stream) (bytes: byte array) =
        output.Write(bytes, 0, bytes.Length)
        output.Flush()

    let private write code stdout stderr =
        emit (Console.OpenStandardOutput()) stdout
        emit (Console.OpenStandardError()) stderr
        code

    let private text (value: string) = utf8.GetBytes value
    let private telemetryError message = write 1 [||] (text $"fsgg roadmap telemetry: {message}\n")
    let private syntax command message =
        write 2 [||] (text $"usage: {command}\n{command}: error: {message}\n")

    let private options command required optional (arguments: string list) =
        let permitted = Set.ofList (required @ optional)
        let rec loop (remaining: string list) (values: Map<string, string>) =
            match remaining with
            | [] ->
                match required |> List.tryFind (fun name -> not (Map.containsKey name values)) with
                | Some name -> Error $"the following arguments are required: {name}"
                | None -> Ok values
            | name :: value :: rest when permitted.Contains name && not (value.StartsWith("--", StringComparison.Ordinal)) ->
                // argparse retains the last value for repeated scalar options.
                loop rest (Map.add name value values)
            | name :: _ when permitted.Contains name -> Error $"argument {name}: expected one argument"
            | name :: _ -> Error $"unrecognized arguments: {name}"
        loop arguments Map.empty

    let private required name (values: Map<string, string>) = values[name]
    let private optional name (values: Map<string, string>) = Map.tryFind name values
    let private defaultValue name fallback values = optional name values |> Option.defaultValue fallback

    let private choice name allowed value =
        if List.contains value allowed then Ok value
        else
            let choices = allowed |> List.map (fun x -> "'" + x + "'") |> String.concat ", "
            Error $"argument {name}: invalid choice: '{value}' (choose from {choices})"

    let private integer name (value: string) =
        match Int32.TryParse value with
        | true, parsed -> Ok parsed
        | _ -> Error $"argument {name}: invalid int value: '{value}'"

    let private parseTelemetry command args =
        let parse requiredNames optionalNames build =
            options command requiredNames optionalNames args |> Result.bind build
        match command with
        | "begin" ->
            parse [ "--feature"; "--item"; "--attempt"; "--model"; "--effort" ]
                [ "--original-item"; "--parent-attempt"; "--parent-token"; "--relation"; "--producer"; "--late-after-seconds" ]
                (fun values ->
                    let relation = defaultValue "--relation" "root" values
                    match choice "--relation" [ "root"; "child"; "follow-up" ] relation,
                          integer "--late-after-seconds" (defaultValue "--late-after-seconds" "60" values) with
                    | Ok relation, Ok seconds ->
                        Ok(Begin(required "--feature" values, required "--item" values,
                                 optional "--original-item" values, required "--attempt" values,
                                 optional "--parent-attempt" values, optional "--parent-token" values,
                                 relation, defaultValue "--producer" "roadmap-orchestrator" values,
                                 required "--model" values, required "--effort" values, seconds))
                    | Error error, _ | _, Error error -> Error error)
        | "population-only" ->
            parse [ "--feature"; "--item"; "--original-item" ] [ "--producer" ]
                (fun values -> Ok(PopulationOnly(required "--feature" values, required "--item" values,
                    required "--original-item" values, defaultValue "--producer" "roadmap-orchestrator" values)))
        | "started" ->
            parse [ "--token"; "--native-id" ] []
                (fun values -> Ok(Started(required "--token" values, required "--native-id" values)))
        | "finish" ->
            parse [ "--token"; "--outcome" ] [ "--exit-code" ]
                (fun values ->
                    match choice "--outcome" [ "completed"; "failed"; "cancelled"; "blocked" ] (required "--outcome" values),
                          (optional "--exit-code" values |> Option.map (integer "--exit-code") |> Option.defaultValue (Ok 0)) with
                    | Ok outcome, Ok exitCode -> Ok(Finish(required "--token" values, outcome,
                        if Map.containsKey "--exit-code" values then Some exitCode else None))
                    | Error error, _ | _, Error error -> Error error)
        | "usage-reconcile" -> parse [ "--token" ] [] (fun values -> Ok(UsageReconcile(required "--token" values)))
        | "ci-assignment" ->
            parse [ "--feature"; "--item"; "--attempt" ] [ "--parent-attempt"; "--producer" ]
                (fun values -> Ok(CiAssignment(required "--feature" values, required "--item" values,
                    required "--attempt" values, optional "--parent-attempt" values,
                    defaultValue "--producer" "routine-delivery" values)))
        | "review" ->
            parse [ "--token"; "--scope"; "--input" ] []
                (fun values ->
                    choice "--scope" [ "attempt"; "item" ] (required "--scope" values)
                    |> Result.map (fun scope -> Review(required "--token" values, scope, FileInfo(required "--input" values))))
        | "activity" -> parse [ "--token"; "--input" ] []
                            (fun values -> Ok(Activity(required "--token" values, FileInfo(required "--input" values))))
        | "usage-attribution" -> parse [ "--token"; "--input" ] []
                                     (fun values -> Ok(UsageAttribution(required "--token" values, FileInfo(required "--input" values))))
        | "complication" -> parse [ "--token"; "--input" ] []
                               (fun values -> Ok(Complication(required "--token" values, FileInfo(required "--input" values))))
        | "status" -> parse [] [] (fun _ -> Ok Status)
        | _ -> Error $"invalid choice: '{command}'"

    let private roadmap args =
        match args with
        | "--config" :: [] -> syntax "roadmap-telemetry" "argument --config: expected one argument"
        | "--config" :: path :: _ when path.StartsWith("--", StringComparison.Ordinal) ->
            syntax "roadmap-telemetry" "argument --config: expected one argument"
        | _ ->
            let explicitConfig, commandAndArgs =
                match args with
                | "--config" :: path :: rest -> Some path, rest
                | _ -> None, args
            match commandAndArgs with
            | command :: arguments ->
                match parseTelemetry command arguments with
                | Error error -> syntax "roadmap-telemetry" error
                | Ok parsed ->
                    match SkillTelemetryReaders.Configuration.discover explicitConfig with
                    | Error error -> telemetryError error.Message
                    | Ok config ->
                        let result = SkillTelemetryAdapter.run config parsed
                        write result.ExitCode result.Stdout result.Stderr
            | [] -> syntax "roadmap-telemetry" "the following arguments are required: command"

    let private preflightError message =
        let quoted = JsonSerializer.Serialize message
        write 2 [||] (text $"{{\"decision\": \"error\", \"message\": {quoted}}}\n")

    let private preflight command args =
        match command, args with
        | "assess", [ path ]
        | "graph", path :: _ ->
            let requirements =
                if command = "assess" then Ok []
                else
                    let rec collect values = function
                        | [] when values <> [] -> Ok(List.rev values)
                        | "--requires" :: value :: rest when not (value.StartsWith("--", StringComparison.Ordinal)) -> collect (value :: values) rest
                        | _ -> Error "the following arguments are required: --requires"
                    collect [] (List.tail args)
            match requirements with
            | Error error -> syntax "preflight" error
            | Ok requirements ->
                try
                    let info = FileInfo path
                    if info.Length > 2L * 1024L * 1024L then preflightError "Input exceeds 2 MiB"
                    else
                        let bytes = File.ReadAllBytes path
                        let result =
                            if command = "assess" then
                                SkillPreflight.assess (ReadOnlyMemory<byte> bytes)
                                |> Result.map (fun (_, output) -> 0, output)
                            else
                                SkillPreflight.graph (ReadOnlyMemory<byte> bytes) requirements
                                |> Result.map (fun (decision, output) ->
                                    (match decision with SkillPreflight.Blocked _ -> 1 | _ -> 0), output)
                        match result with
                        | Ok(code, output) -> write code output [||]
                        | Error error -> preflightError error.Message
                with error -> preflightError error.Message
        | "assess", _ -> syntax "preflight" "the following arguments are required: path"
        | "graph", _ -> syntax "preflight" "the following arguments are required: path, --requires"
        | _ -> syntax "preflight" $"invalid choice: '{command}'"

    let tryRun argv =
        let args = match argv with "skill" :: rest -> rest | _ -> argv
        match args with
        | "roadmap-telemetry" :: rest -> Some(roadmap rest)
        | "preflight" :: command :: rest -> Some(preflight command rest)
        | [ "preflight" ] -> Some(syntax "preflight" "the following arguments are required: command")
        | _ -> None
