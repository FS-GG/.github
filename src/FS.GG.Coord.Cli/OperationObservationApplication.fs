namespace FS.GG.Coord.Cli

module OperationObservationApplication =
    open System
    open System.IO
    open System.Collections.Generic
    open System.Text
    open System.Text.Json
    open System.Security.Cryptography
    open FS.GG.Coord.OperationObservation
    open FS.GG.Coord.GitHub
    open FS.GG.Coord.GitHub.Transport

    type Input = { Identity: Identity; Seconds: int }

    let private closed (names: string list) (node: JsonElement) =
        if node.ValueKind <> JsonValueKind.Object then invalidArg "json" "expected object"
        let fields = node.EnumerateObject() |> Seq.map _.Name |> Seq.toList
        if fields.Length <> names.Length || Set.ofList fields <> Set.ofList names then invalidArg "json" "unknown/missing/duplicate fields"
    let private field name (node: JsonElement) = node.GetProperty(name: string)
    let private text name node = (field name node).GetString()
    let private number name node = (field name node).GetInt32()
    let private long name node = (field name node).GetInt64()
    let private nullable name node =
        let value = field name node
        if value.ValueKind = JsonValueKind.Null then None
        elif value.ValueKind = JsonValueKind.String then Some(value.GetString())
        else invalidArg "json" "expected nullable text"
    let private identity (node: JsonElement) : Identity =
        closed [ "issue"; "slice"; "attempt"; "owner"; "sourceRevision"; "issueUpdatedAt"; "issueBodySha256"; "repository"; "pullRequest"; "head"
                 "runId"; "runAttempt"; "populationRevision"; "unknowns" ] node
        let unknowns = field "unknowns" node
        if unknowns.ValueKind <> JsonValueKind.Array || unknowns.GetArrayLength() > 512 then invalidArg "json" "bounded Unknown array required"
        let result =
            { Issue = text "issue" node; Slice = text "slice" node; Attempt = text "attempt" node; Owner = text "owner" node
              SourceRevision = text "sourceRevision" node; IssueUpdatedAt = text "issueUpdatedAt" node; IssueBodySha256 = text "issueBodySha256" node; Repository = text "repository" node; PullRequest = number "pullRequest" node
              Head = text "head" node; RunId = long "runId" node; RunAttempt = number "runAttempt" node
              PopulationRevision = text "populationRevision" node
              Unknowns = unknowns.EnumerateArray() |> Seq.map _.GetString() |> Seq.toList }
        match validateIdentity result with Ok result -> result | Error message -> invalidArg "json" message
    let private identityWire (value: Identity) =
        {| issue = value.Issue; slice = value.Slice; attempt = value.Attempt; owner = value.Owner
           sourceRevision = value.SourceRevision; issueUpdatedAt = value.IssueUpdatedAt; issueBodySha256 = value.IssueBodySha256; repository = value.Repository; pullRequest = value.PullRequest; head = value.Head
           runId = value.RunId; runAttempt = value.RunAttempt; populationRevision = value.PopulationRevision; unknowns = value.Unknowns |}
    let private decode decoder (json: string) =
        try
            if Encoding.UTF8.GetByteCount json > 262144 then invalidArg "json" "input exceeds256KiB"
            use document = JsonDocument.Parse(json, JsonDocumentOptions(MaxDepth = 8))
            Ok(decoder document.RootElement)
        with
        | :? ArgumentException | :? JsonException | :? InvalidOperationException | :? KeyNotFoundException | :? FormatException | :? OverflowException ->
            Error "missing, corrupt or unsupported observation input"

    let parseInput json =
        decode (fun root ->
            closed [ "schema"; "identity"; "seconds" ] root
            if text "schema" root <> "fsgg.operation-observation-input/1" then invalidArg "json" "unsupported input schema"
            let seconds = number "seconds" root
            if seconds < 1 || seconds > 600 then invalidArg "json" "seconds must be1..600"
            { Identity = identity (field "identity" root); Seconds = seconds }) json

    let private encodeCursorBody (cursor: Cursor) =
        let budget = cursor.Budget
        JsonSerializer.Serialize
            {| schema = "fsgg.operation-observation-cursor/1"; identity = identityWire cursor.Identity
               clockDomain = budget.ClockDomain; started = budget.Started; workDeadline = budget.WorkDeadline
               cleanupDeadline = budget.CleanupDeadline; lastClock = budget.LastClock
               requestsRemaining = budget.RequestsRemaining; outputBytesRemaining = budget.OutputBytesRemaining
               population = Option.toObj cursor.Population; semantic = Option.toObj cursor.Semantic; control = Option.toObj cursor.Control; revision = cursor.Revision
               terminal = cursor.Terminal |> Option.map attentionName |> Option.toObj; evidence = Option.toObj cursor.Evidence; gap = Option.toObj cursor.Gap |}

    let private decodeCursorBody json =
        decode (fun root ->
            closed [ "schema"; "identity"; "clockDomain"; "started"; "workDeadline"; "cleanupDeadline"; "lastClock"
                     "requestsRemaining"; "outputBytesRemaining"; "population"; "semantic"; "control"; "revision"; "terminal"; "evidence"; "gap" ] root
            if text "schema" root <> "fsgg.operation-observation-cursor/1" then invalidArg "json" "unsupported cursor schema"
            let terminal = nullable "terminal" root |> Option.map (fun name ->
                [ Completed; Failed; PullRequestClosed; HeadChanged; RunChanged; PopulationChanged; MaterialChange
                  Unknown; Deadline; BudgetExhausted; Interrupted; ObservationFailure ]
                |> List.tryFind (fun attention -> attentionName attention = name)
                |> Option.defaultWith (fun () -> invalidArg "json" "unsupported attention"))
            { Identity = identity (field "identity" root)
              Budget = { ClockDomain = text "clockDomain" root; Started = long "started" root; WorkDeadline = long "workDeadline" root
                         CleanupDeadline = long "cleanupDeadline" root; LastClock = long "lastClock" root
                         RequestsRemaining = number "requestsRemaining" root; OutputBytesRemaining = number "outputBytesRemaining" root }
              Population = nullable "population" root; Semantic = nullable "semantic" root; Control = nullable "control" root
              Revision = number "revision" root; Terminal = terminal; Evidence = nullable "evidence" root; Gap = nullable "gap" root }) json

    let private checksum (value: string) =
        SHA256.HashData(Encoding.UTF8.GetBytes value) |> Convert.ToHexString |> _.ToLowerInvariant()

    let encodeCursor cursor =
        let body = encodeCursorBody cursor
        JsonSerializer.Serialize {| schema = "fsgg.operation-observation-custody/1"; cursorJson = body; sha256 = checksum body |}

    let decodeCursor json =
        decode (fun root ->
            closed [ "schema"; "cursorJson"; "sha256" ] root
            let body = text "cursorJson" root
            if text "schema" root <> "fsgg.operation-observation-custody/1" || checksum body <> text "sha256" root then
                invalidArg "json" "corrupt cursor custody"
            body) json |> Result.bind decodeCursorBody

    let observe transport clock sleep persist evidence initial =
        let mutable current = initial
        let mutable answer = None
        let retain cursor = persist cursor; current <- cursor
        let stop attention =
            if attention = Deadline && current.Terminal.IsSome && current.Terminal <> Some Deadline then
                current <- { current with Gap = Some("original-deadline-before-" + attentionName current.Terminal.Value + "-delivery") }
            match finish attention current with
            | ReturnAttention(cursor, _, _) as decision -> retain cursor; answer <- Some decision
            | _ -> invalidOp "attention reducer invariant"
        try
            while answer.IsNone do
                match decide (clock()) None current with
                | ReturnAttention(cursor, attention, _) as decision ->
                    retain cursor
                    if attention <> Deadline && clock() >= cursor.Budget.WorkDeadline then stop Deadline
                    else answer <- Some decision
                | AwaitWithinDeadline _ -> invalidOp "empty sample cannot wait"
                | ObserveOriginal _ ->
                    let beforeRequest () =
                        match chargeRequest (clock()) current with
                        | Error reason -> Error reason
                        | Ok cursor -> retain cursor; Ok ()
                    match OperationObservationReads.collect transport current.Identity clock current.Budget.WorkDeadline beforeRequest with
                    | Error reason ->
                        current <- { current with Gap = Some reason }
                        if clock() >= current.Budget.WorkDeadline then stop Deadline
                        elif current.Budget.RequestsRemaining <= 0 then stop BudgetExhausted
                        else stop ObservationFailure
                    | Ok sample ->
                        match decide (clock()) (Some sample) current with
                        | ObserveOriginal _ -> invalidOp "native sample cannot request another read directly"
                        | AwaitWithinDeadline(cursor, delay) ->
                            retain cursor
                            sleep delay
                        | ReturnAttention(cursor, attention, id) ->
                            let reference, bytes, write = evidence sample
                            match chargeOutput bytes cursor with
                            | Error _ -> current <- cursor; stop BudgetExhausted
                            | Ok charged ->
                                // A crash before evidence custody must resume native observation,
                                // never replay a terminal result whose sample was not retained.
                                retain { charged with Terminal = None; Evidence = None }
                                if clock() >= charged.Budget.WorkDeadline then stop Deadline
                                else
                                    write()
                                    let retained = { charged with Evidence = Some reference }
                                    retain retained
                                    if clock() >= retained.Budget.WorkDeadline then stop Deadline
                                    else answer <- Some(ReturnAttention(retained, attention, id))
        with :? OperationCanceledException -> stop Interrupted
        answer.Value

    let private readBounded path =
        use stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read)
        let bytes = Array.zeroCreate<byte> 262145
        let mutable size = 0
        let mutable ended = false
        while not ended && size < bytes.Length do
            let read = stream.Read(bytes, size, bytes.Length - size)
            if read = 0 then ended <- true else size <- size + read
        if size > 262144 then invalidArg "path" "file exceeds256KiB"
        UTF8Encoding(false, true).GetString(bytes, 0, size)

    let private privateOptions mode =
        let options = FileStreamOptions(Mode = mode, Access = FileAccess.ReadWrite, Share = FileShare.None)
        if not (OperatingSystem.IsWindows()) then options.UnixCreateMode <- UnixFileMode.UserRead ||| UnixFileMode.UserWrite
        options
    let private save path content =
        let temporary = path + ".tmp-" + Guid.NewGuid().ToString("N")
        try
            use stream = new FileStream(temporary, privateOptions FileMode.CreateNew)
            let bytes = Encoding.UTF8.GetBytes(content: string)
            stream.Write bytes
            stream.Flush true
            stream.Close()
            File.Move(temporary, path, true)
        finally
            if File.Exists temporary then File.Delete temporary

    let private clockDomain () =
        if not (OperatingSystem.IsLinux()) then invalidOp "original clock continuation currently supports Linux only"
        let boot = File.ReadAllText("/proc/sys/kernel/random/boot_id").Trim()
        match Guid.TryParse boot with
        | true, _ -> "linux-boot-tickcount64/1:" + boot
        | _ -> invalidOp "original boot clock identity unavailable"

    let private result cursorPath (cursor: Cursor) attention id =
        JsonSerializer.Serialize
            {| schema = "fsgg.operation-observation-result/1"; attention = attentionName attention; terminalIdentity = id
               repository = cursor.Identity.Repository; pullRequest = cursor.Identity.PullRequest; head = cursor.Identity.Head
               runId = cursor.Identity.RunId; runAttempt = cursor.Identity.RunAttempt; revision = cursor.Revision
               requestsRemaining = cursor.Budget.RequestsRemaining; unknownsRetained = cursor.Identity.Unknowns.Length
               cursor = cursorPath; evidence = Option.toObj cursor.Evidence; gap = Option.toObj cursor.Gap
               readiness = "not-evaluated"; effects = "read-only"; exactlyOnceDelivery = false |}

    let tryRun arguments =
        match arguments with
        | "observe-operation" :: rest ->
            Some(
                try
                    let mode, inputPath, cursorPath =
                        match rest with
                        | [ ("start" | "resume") as mode; "--input"; input; "--cursor"; cursor ] -> mode, input, cursor
                        | _ -> invalidArg "arguments" "observe-operation start|resume --input PRIVATE_INPUT --cursor PRIVATE_CURSOR"
                    if not (Path.IsPathFullyQualified inputPath && Path.IsPathFullyQualified cursorPath)
                       || cursorPath.Length > 1024 || inputPath = cursorPath then invalidArg "path" "distinct absolute private paths required"
                    let input = match parseInput (readBounded inputPath) with Ok input -> input | Error reason -> invalidArg "input" reason
                    // The original cursor is local recovery state, never an admission or authenticated grant.
                    use ownership = new FileStream(cursorPath + ".lock", privateOptions FileMode.OpenOrCreate)
                    let domain = clockDomain()
                    let now = Environment.TickCount64
                    let original =
                        if mode = "start" then
                            if File.Exists cursorPath then invalidOp "original cursor already exists; use resume"
                            match start input.Identity domain now input.Seconds with Ok cursor -> cursor | Error reason -> invalidArg "input" reason
                        else
                            let cursor = match decodeCursor (readBounded cursorPath) with Ok cursor -> cursor | Error reason -> invalidArg "cursor" reason
                            match resume input.Identity domain now cursor with
                            | Error reason -> invalidArg "cursor" reason
                            | Ok cursor -> cursor
                    let persist cursor =
                        let content = encodeCursor cursor
                        if Encoding.UTF8.GetByteCount content > 262144 then invalidOp "cursor custody exceeds256KiB"
                        save cursorPath content
                    persist original
                    let token =
                        let primary = Environment.GetEnvironmentVariable "GITHUB_TOKEN"
                        if String.IsNullOrWhiteSpace primary then Environment.GetEnvironmentVariable "GH_TOKEN" else primary
                    if String.IsNullOrWhiteSpace token then invalidOp "native read credential unavailable"
                    use transport = new HttpTransport("https://api.github.com", token)
                    let evidence sample =
                        let path = cursorPath + ".evidence-" + Guid.NewGuid().ToString("N") + ".json"
                        // Cursor retains all original identity/Unknowns; the evidence is a bounded native sample.
                        let checks = sample.Checks |> List.map (fun check ->
                            {| id = check.Id; name = check.Name; status = check.Status; conclusion = Option.toObj check.Conclusion
                               startedAt = Option.toObj check.StartedAt; completedAt = Option.toObj check.CompletedAt |})
                        let content = JsonSerializer.Serialize
                                          {| head = sample.Head; runId = sample.RunId; runAttempt = sample.RunAttempt
                                             runStatus = sample.RunStatus; runConclusion = Option.toObj sample.RunConclusion
                                             state = sample.PullRequestState; merged = sample.Merged; draft = sample.Draft
                                             complete = sample.Complete; gaps = sample.Gaps; checks = checks |}
                        if Encoding.UTF8.GetByteCount content > 262144 then invalidOp "private evidence exceeds256KiB"
                        path, Encoding.UTF8.GetByteCount content, (fun () -> save path content)
                    let mutable cancelled = false
                    let cancellation = ConsoleCancelEventHandler(fun _ event -> event.Cancel <- true; cancelled <- true)
                    Console.CancelKeyPress.AddHandler cancellation
                    let decision =
                        try
                            observe (transport :> IDeadlineSinglePageGitHubTransport)
                                (fun () -> if cancelled then raise (OperationCanceledException()) else Environment.TickCount64)
                                (fun delay -> System.Threading.Thread.Sleep delay) persist evidence original
                        finally Console.CancelKeyPress.RemoveHandler cancellation
                    let rec emit decision =
                        match decision with
                        | ReturnAttention(cursor, attention, id) ->
                            let document = result cursorPath cursor attention id
                            let size = Encoding.UTF8.GetByteCount document + 1
                            if size > 4096 then invalidOp "compact attention exceeds4096bytes"
                            match chargeOutput size cursor with
                            | Error reason -> invalidOp reason
                            | Ok cursor ->
                                persist cursor
                                if attention <> Deadline && Environment.TickCount64 >= cursor.Budget.WorkDeadline then
                                    emit (finish Deadline { cursor with Gap = Some("original-deadline-before-" + attentionName attention + "-delivery") })
                                else
                                    printfn "%s" document
                                    if attention = Completed then 0 else 3
                        | _ -> invalidOp "finite observer must return attention"
                    emit decision
                with
                | :? IOException | :? UnauthorizedAccessException | :? ArgumentException | :? InvalidOperationException | :? DecoderFallbackException ->
                    printfn "{\"schema\":\"fsgg.operation-observation-result/1\",\"attention\":\"refusal\",\"reason\":\"missing-corrupt-drifted-expired-or-unavailable-original\",\"effects\":\"read-only\",\"readiness\":\"not-evaluated\"}"
                    3)
        | _ -> None
