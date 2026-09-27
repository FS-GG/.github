open System
open System.Text.Json
open FS.GG.Coord.Cli

module Readers = SkillTelemetryReaders

let emit value =
    Console.Out.WriteLine(JsonSerializer.Serialize(value))

let emitError (error: Readers.ReaderError) =
    emit {| ok = false; error = error.Message |}

let configProjection (config: Readers.HostConfig option) =
    match config with
    | None ->
        {|
            ok = true
            configured = false
            path = null
            storeRoot = null
            engine = null
            workspace = false
            repository = null
        |}
    | Some value ->
        {|
            ok = true
            configured = true
            path = value.Path
            storeRoot = value.StoreRoot
            engine = value.Engine
            workspace = value.Workspace
            repository =
                value.Repository
                |> Option.map Readers.Configuration.repositoryValue
                |> Option.toObj
        |}

let nativeProjection (value: Readers.NativeInventory) =
    {|
        ok = true
        threadId = value.ThreadId
        allTurnIds = value.AllTurnIds
        inventory =
            value.TurnInventory
            |> List.map (fun row ->
                {|
                    turnId = row.TurnId
                    sequence = row.Sequence
                    status = row.Status
                    terminal = row.Terminal
                    usageAvailable = row.UsageAvailable
                |})
        turns =
            value.Turns
            |> List.map (fun turn ->
                {|
                    turnId = turn.TurnId
                    sequence = turn.Sequence
                    provider = turn.Provider
                    model = turn.Model
                    effort = turn.Effort
                    input = turn.Usage.Input
                    cachedInput = turn.Usage.CachedInput
                    output = turn.Usage.Output
                    reasoning = turn.Usage.Reasoning
                    total = turn.Usage.Total
                |})
        complete = value.Complete
        provider = value.Provider |> Option.toObj
        model = value.Model |> Option.toObj
        effort = value.Effort |> Option.toObj
        sourceDigest = value.SourceDigest
        rosterDigest = value.RosterDigest
    |}

[<EntryPoint>]
let main args =
    match args |> Array.toList with
    | [ "canonical"; value ] ->
        match Readers.Configuration.canonicalRepository value with
        | Ok repository ->
            emit
                {|
                    ok = true
                    value = Readers.Configuration.repositoryValue repository
                |}
        | Error error -> emitError error

        0
    | [ "discover-repository"; cwd ] ->
        match Readers.Configuration.discoverRepository cwd with
        | Ok repository ->
            emit
                {|
                    ok = true
                    value = Readers.Configuration.repositoryValue repository
                |}
        | Error error -> emitError error

        0
    | [ "discover"; path ] ->
        match Readers.Configuration.discover (if path = "-" then None else Some path) with
        | Ok config -> emit (configProjection config)
        | Error error -> emitError error

        0
    | [ "mutation"; path ] ->
        match Readers.Configuration.discover (Some path) with
        | Ok(Some config) ->
            match Readers.Configuration.mutationCommand config [ "fixture-engine"; "submit" ] with
            | Ok command -> emit {| ok = true; command = command |}
            | Error error -> emitError error
        | Ok None ->
            emit
                {|
                    ok = false
                    error = "not configured"
                |}
        | Error error -> emitError error

        0
    | [ "ci"; path; feature; item; attempt ] ->
        match Readers.Configuration.discover (Some path) with
        | Ok(Some config) ->
            match Readers.Configuration.createCiAssignment config feature item attempt None "fixture-producer" with
            | Ok assignment -> emit {| ok = true; assignment = assignment |}
            | Error error -> emitError error
        | Ok None ->
            emit
                {|
                    ok = false
                    error = "not configured"
                |}
        | Error error -> emitError error

        0
    | [ "native"; command; codexHome; parent; nativeId; rootInvocation; invocation; revision ] ->
        match
            Readers.NativeUsage.collectWith
                command
                codexHome
                (Guid.Parse parent)
                nativeId
                rootInvocation
                invocation
                (Int32.Parse revision)
        with
        | Ok value -> emit (nativeProjection value)
        | Error error -> emitError error

        0
    | [ "coverage"; parent ] ->
        let value =
            Readers.NativeUsage.coverageForParent (if parent = "none" then None else Some(Guid.Parse parent))

        emit {| ok = true; coverage = string value |}
        0
    | _ ->
        Console.Error.WriteLine(
            "usage: ReadersProbe <canonical|discover-repository|discover|mutation|ci|native|coverage> ..."
        )

        2
