namespace FS.GG.Coord.Cli

open System
open System.IO
open System.Diagnostics
open System.Text.Json
open FS.GG.Coord
open FS.GG.Coord.Cli.Options

// The command contract — the strict decode, how a live checkout is resolved by remote identity, why
// `apply` is a typed refusal, and every exit code — is stated in `IntakeApplication.fsi`, which is
// where the compiler keeps it (.github#2730). What follows is implementation reasoning only.
module IntakeApplication =
    let private error message =
        printfn "{\"schema\":\"fsgg.coord.intake-result/v1\",\"kind\":\"refusal\",\"reason\":%s}" (JsonSerializer.Serialize message)
        ExitCode.toInt ExitCode.Error

    let private requiredString (root: JsonElement) (name: string) =
        match root.TryGetProperty name with
        | true, value when value.ValueKind = JsonValueKind.String -> Ok(value.GetString())
        | true, _ -> Error $"{name} must be a string"
        | false, _ -> Error $"{name} is required"

    let private optionalString (root: JsonElement) (name: string) =
        match root.TryGetProperty name with
        | false, _ -> Ok None
        | true, value when value.ValueKind = JsonValueKind.String && not (String.IsNullOrWhiteSpace(value.GetString())) -> Ok(Some(value.GetString()))
        | true, _ -> Error $"{name} must be a non-empty string"

    // Public deliberately — see the signature. `known` must stay in step with the field list built
    // below it: a field added to one and not the other is either rejected as unknown or silently
    // ignored, and neither failure is visible at the call site.
    let readDraft path =
        try
            use document = JsonDocument.Parse(File.ReadAllText path)
            let root = document.RootElement
            if root.ValueKind <> JsonValueKind.Object then Error "draft must be a JSON object" else
            let known = Set.ofList [ "schema"; "id"; "owner"; "repository"; "title"; "observed"; "rootCause"; "acceptance"; "verification"; "paths"; "class"; "status"; "disposition"; "phase"; "severity"; "blockedBy"; "blockedOn"; "backlogReason"; "judgementQuestion" ]
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
                let optional = [ "phase"; "severity"; "blockedBy"; "blockedOn"; "backlogReason"; "judgementQuestion" ] |> List.map (optionalString root)
                match strings, paths, disposition, optional with
                | [ Ok schema; Ok id; Ok owner; Ok repository; Ok title; Ok observed; Ok rootCause; Ok acceptance; Ok verification; Ok className; Ok status ], Ok paths, Ok disposition,
                  [ Ok phase; Ok severity; Ok blockedBy; Ok blockedOn; Ok backlogReason; Ok judgementQuestion ] ->
                    let severity =
                        severity
                        |> Option.map (fun value ->
                            match Types.severityOfWireName value with
                            | Some parsed -> Ok(Types.severityWireName parsed)
                            | None -> Error "must be Critical, High, Medium, Low or Unset")
                        |> function
                            | None -> Ok None
                            | Some(Ok value) -> Ok(Some value)
                            | Some(Error detail) -> Error $"severity {detail}"
                    match severity with
                    | Error detail -> Error detail
                    | Ok severity ->
                        match Types.itemClassOfWireName className with
                        | None -> Error "class must be defect, hardening or decision"
                        | Some parsed ->
                            let draft: Intake.Draft =
                                { Schema = schema; Id = id; Owner = owner; Repository = repository; Title = title; Observed = observed; RootCause = rootCause; Acceptance = acceptance; Verification = verification; Paths = paths; Class = Types.itemClassWireName parsed; Status = status; Disposition = Some disposition
                                  Phase = phase; Severity = severity; BlockedBy = blockedBy; BlockedOn = blockedOn; BacklogReason = backlogReason; JudgementQuestion = judgementQuestion }
                            Ok draft
                | _ ->
                    let failures = strings |> List.choose (function Error e -> Some e | _ -> None)
                    let failures = match paths with Error e -> failures @ [ e ] | _ -> failures
                    let failures = match disposition with Error e -> failures @ [ e ] | _ -> failures
                    let failures = optional |> List.choose (function Error e -> Some e | _ -> None) |> List.append failures
                    Error(String.concat "; " failures)
        with
        | :? IOException as ex -> Error $"cannot read draft: {ex.Message}"
        | :? JsonException as ex -> Error $"draft is not valid JSON: {ex.Message}"

    let private pathSubject (path: string) =
        if path.EndsWith "/**" then path.Substring(0, path.Length - 3)
        elif path.EndsWith "/*" then path.Substring(0, path.Length - 2)
        elif path.EndsWith "/" then path.TrimEnd '/'
        else path

    let validateLivePaths (draft: Intake.Draft) =
        let start = DirectoryInfo(Directory.GetCurrentDirectory())
        let rec gitRoot (cursor: DirectoryInfo) =
            if isNull cursor then None
            elif Directory.Exists(Path.Combine(cursor.FullName, ".git")) || File.Exists(Path.Combine(cursor.FullName, ".git")) then Some cursor.FullName
            else gitRoot cursor.Parent
        let originMatches root =
            try
                let info = ProcessStartInfo("git")
                info.ArgumentList.Add "-C"
                info.ArgumentList.Add root
                info.ArgumentList.Add "remote"
                info.ArgumentList.Add "get-url"
                info.ArgumentList.Add "origin"
                info.RedirectStandardOutput <- true
                info.RedirectStandardError <- true
                info.UseShellExecute <- false
                use child = Process.Start info
                let rawUrl = child.StandardOutput.ReadToEnd().Trim().TrimEnd('/')
                let url = if rawUrl.EndsWith(".git", StringComparison.OrdinalIgnoreCase) then rawUrl.Substring(0, rawUrl.Length - 4) else rawUrl
                child.WaitForExit()
                let expected = $"%s{draft.Owner}/%s{draft.Repository}"
                child.ExitCode = 0 && (url.EndsWith("/" + expected, StringComparison.OrdinalIgnoreCase) || url.EndsWith(":" + expected, StringComparison.OrdinalIgnoreCase))
            with _ -> false
        match gitRoot start with
        | None -> Error "cannot locate the live repository checkout for path validation"
        | Some currentRoot ->
            let envRoot = Environment.GetEnvironmentVariable "FSGG_REPOS_ROOT"
            let rec ancestors (directory: DirectoryInfo) =
                if isNull directory then [] else directory.FullName :: ancestors directory.Parent
            let candidates =
                [ yield currentRoot
                  if not (String.IsNullOrWhiteSpace envRoot) then
                      yield Path.Combine(envRoot, draft.Repository)
                  for ancestor in ancestors (DirectoryInfo currentRoot) do
                      yield Path.Combine(ancestor, draft.Repository) ]
                |> List.distinct
            match candidates |> List.tryFind (fun path -> Directory.Exists path && originMatches path) with
            | None -> Error $"cannot locate a live checkout of target repository %s{draft.Owner}/%s{draft.Repository} for path validation"
            | Some root ->
                let missing =
                    draft.Paths
                    |> List.map pathSubject
                    |> List.filter (fun path -> not (File.Exists(Path.Combine(root, path)) || Directory.Exists(Path.Combine(root, path))))
                let rendered = String.concat ", " missing
                if List.isEmpty missing then Ok() else Error $"paths do not exist in target repository %s{draft.Owner}/%s{draft.Repository}: {rendered}"

    let run (opts: Options) =
        match opts.Args with
        | [ action; path ] ->
            match readDraft path with
            | Error reason -> error reason
            | Ok draft ->
                match action, Intake.validate draft with
                | "validate", Ok _ ->
                    match validateLivePaths draft with
                    | Error reason -> error reason
                    | Ok() ->
                        printfn "{\"schema\":\"fsgg.coord.intake-result/v1\",\"kind\":\"validated\",\"draftId\":%s,\"writes\":0}" (JsonSerializer.Serialize draft.Id)
                        ExitCode.toInt ExitCode.Green
                | "validate", Error findings -> error (findings |> List.map (fun finding -> $"{finding.Field} {finding.Detail}") |> String.concat "; ")
                | "apply", Ok _ -> error "live intake apply is not wired; validation performed zero writes"
                | "apply", Error findings -> error (findings |> List.map (fun finding -> $"{finding.Field} {finding.Detail}") |> String.concat "; ")
                | other, _ -> error $"unknown intake action '{other}' (expected validate or apply)"
        | _ -> error "usage: intake <validate|apply> <draft.json>"
