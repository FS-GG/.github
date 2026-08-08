namespace FS.GG.Coord.Cli

open System
open System.IO
open System.Text.Json
open FS.GG.Coord
open FS.GG.Coord.Cli.Options

/// The local half of #2134's public command contract.  `apply` is deliberately a typed refusal until
/// the Client owns the live duplicate/ownership/board transaction; a green validation is never a create.
module IntakeApplication =
    let private error message =
        printfn "{\"schema\":\"fsgg.coord.intake-result/v1\",\"kind\":\"refusal\",\"reason\":%s}" (JsonSerializer.Serialize message)
        ExitCode.toInt ExitCode.Error

    let private requiredString (root: JsonElement) (name: string) =
        match root.TryGetProperty name with
        | true, value when value.ValueKind = JsonValueKind.String -> Ok(value.GetString())
        | true, _ -> Error $"{name} must be a string"
        | false, _ -> Error $"{name} is required"

    let private decode path =
        try
            use document = JsonDocument.Parse(File.ReadAllText path)
            let root = document.RootElement
            if root.ValueKind <> JsonValueKind.Object then Error "draft must be a JSON object" else
            let known = Set.ofList [ "schema"; "id"; "owner"; "repository"; "title"; "observed"; "rootCause"; "acceptance"; "verification"; "paths"; "class"; "status"; "disposition" ]
            let unknown = root.EnumerateObject() |> Seq.tryFind (fun property -> not (known.Contains property.Name))
            match unknown with
            | Some property -> Error $"unknown draft field '{property.Name}'"
            | None ->
                let strings = [ "schema"; "id"; "owner"; "repository"; "title"; "observed"; "rootCause"; "acceptance"; "verification"; "class"; "status" ] |> List.map (requiredString root)
                let paths =
                    match root.TryGetProperty "paths" with
                    | true, value when value.ValueKind = JsonValueKind.Array && (value.EnumerateArray() |> Seq.forall (fun item -> item.ValueKind = JsonValueKind.String)) -> Ok(value.EnumerateArray() |> Seq.map (fun item -> item.GetString()) |> List.ofSeq)
                    | true, _ -> Error "paths must be an array of strings"
                    | false, _ -> Error "paths is required"
                let disposition =
                    match root.TryGetProperty "disposition" with
                    | true, value when value.ValueKind = JsonValueKind.String && value.GetString() = "create" -> Ok Intake.Create
                    | true, value when value.ValueKind = JsonValueKind.String && value.GetString() = "reuse" -> Ok Intake.Reuse
                    | true, _ -> Error "disposition must be 'create' or 'reuse'"
                    | false, _ -> Error "disposition is required"
                match strings, paths, disposition with
                | [ Ok schema; Ok id; Ok owner; Ok repository; Ok title; Ok observed; Ok rootCause; Ok acceptance; Ok verification; Ok className; Ok status ], Ok paths, Ok disposition ->
                    let draft: Intake.Draft =
                        { Schema = schema; Id = id; Owner = owner; Repository = repository; Title = title; Observed = observed; RootCause = rootCause; Acceptance = acceptance; Verification = verification; Paths = paths; Class = className; Status = status; Disposition = Some disposition }
                    Ok draft
                | _ ->
                    let failures = strings |> List.choose (function Error e -> Some e | _ -> None)
                    let failures = match paths with Error e -> failures @ [ e ] | _ -> failures
                    let failures = match disposition with Error e -> failures @ [ e ] | _ -> failures
                    Error(String.concat "; " failures)
        with
        | :? IOException as ex -> Error $"cannot read draft: {ex.Message}"
        | :? JsonException as ex -> Error $"draft is not valid JSON: {ex.Message}"

    let run (opts: Options) =
        match opts.Args with
        | [ action; path ] ->
            match decode path with
            | Error reason -> error reason
            | Ok draft ->
                match action, Intake.validate draft with
                | "validate", Ok _ ->
                    printfn "{\"schema\":\"fsgg.coord.intake-result/v1\",\"kind\":\"validated\",\"draftId\":%s,\"writes\":0}" (JsonSerializer.Serialize draft.Id)
                    ExitCode.toInt ExitCode.Green
                | "validate", Error findings -> error (findings |> List.map (fun finding -> $"{finding.Field} {finding.Detail}") |> String.concat "; ")
                | "apply", Ok _ -> error "live intake apply is not wired; validation performed zero writes"
                | "apply", Error findings -> error (findings |> List.map (fun finding -> $"{finding.Field} {finding.Detail}") |> String.concat "; ")
                | other, _ -> error $"unknown intake action '{other}' (expected validate or apply)"
        | _ -> error "usage: intake <validate|apply> <draft.json>"
