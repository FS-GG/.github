namespace FS.GG.Coord.Cli

module ContextViewApplication =
    open System
    open System.IO
    open System.Text
    open System.Text.Json
    open System.Security.Cryptography
    open FS.GG.Coord
    open FS.GG.Coord.ContextView
    open FS.GG.Coord.GitHub
    open FS.GG.Coord.GitHub.Transport

    type Arguments =
        { Issue: IssueRef
          PullRequest: int option
          Action: Action
          CapBytes: int
          Custody: string option
          Workspace: string option
          ReportFile: string }

    let parseArguments (arguments: string list) =
        let known = set [ "--issue"; "--pr"; "--action"; "--cap-bytes"; "--custody"; "--workspace"; "--report-file" ]
        let rec pairs remaining (acc: Map<string, string>) =
            match remaining with
            | [] -> Ok acc
            | flag :: value :: rest when known.Contains flag ->
                if acc.ContainsKey flag then Error $"{flag} given more than once" else pairs rest (Map.add flag value acc)
            | flag :: _ when known.Contains flag -> Error $"{flag} needs a value"
            | other :: _ -> Error $"unknown argument {other}"
        let number flag (text: string) =
            match Int32.TryParse(text, Globalization.NumberStyles.None, Globalization.CultureInfo.InvariantCulture) with
            | true, n when n > 0 -> Ok n
            | _ -> Error $"{flag} must be a positive integer"
        let optional flag (values: Map<string, string>) parse =
            match Map.tryFind flag values with
            | Some text -> parse text |> Result.map Some
            | None -> Ok None
        pairs arguments Map.empty
        |> Result.bind (fun values ->
            match Map.tryFind "--issue" values, Map.tryFind "--report-file" values with
            | None, _ -> Error "--issue is required"
            | _, None -> Error "--report-file is required"
            | Some issueText, Some report ->
                parseIssueRef issueText
                |> Result.bind (fun issue ->
                    optional "--pr" values (number "--pr")
                    |> Result.bind (fun pr ->
                        optional "--action" values parseAction
                        |> Result.bind (fun action ->
                            optional "--cap-bytes" values (number "--cap-bytes")
                            |> Result.map (fun cap ->
                                { Issue = issue; PullRequest = pr; Action = defaultArg action Implement
                                  CapBytes = defaultArg cap DefaultCapBytes
                                  Custody = Map.tryFind "--custody" values; Workspace = Map.tryFind "--workspace" values
                                  ReportFile = report })))))

    // ------------------------------------------------------------------ local, read-only sources

    let private readBounded (path: string) (limit: int) =
        use stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read)
        let bytes = Array.zeroCreate<byte> (limit + 1)
        let mutable size = 0
        let mutable ended = false
        while not ended && size < bytes.Length do
            let read = stream.Read(bytes, size, bytes.Length - size)
            if read = 0 then ended <- true else size <- size + read
        if size > limit then raise (InvalidDataException "file exceeds its bound")
        bytes[0 .. size - 1]

    let readCustody (path: string) : Read<CustodyFact> =
        try
            let text = UTF8Encoding(false, true).GetString(readBounded path 262144)
            match OperationObservationApplication.decodeCursor text with
            | Error reason -> Unavailable reason
            | Ok cursor ->
                let identity = cursor.Identity
                Supplied
                    { Issue = identity.Issue; Slice = identity.Slice; Attempt = identity.Attempt; Owner = identity.Owner
                      Repository = identity.Repository; PullRequest = identity.PullRequest; Head = identity.Head
                      Unknowns = identity.Unknowns
                      Terminal = cursor.Terminal |> Option.map OperationObservation.attentionName
                      Gap = cursor.Gap }
        with
        | :? IOException | :? UnauthorizedAccessException | :? DecoderFallbackException | :? InvalidDataException ->
            Unavailable "custody file is missing, unreadable, oversize or not UTF-8"

    let private fileFact (root: string) (relative: string) : FileFact =
        try
            if Path.IsPathRooted relative || relative.Split('/') |> Array.exists (fun part -> part = "..") then
                Unreadable "path is not repository-relative"
            else
                let full = Path.GetFullPath(Path.Combine(root, relative))
                if not (full.StartsWith(root + string Path.DirectorySeparatorChar, StringComparison.Ordinal)) then Unreadable "path escapes the workspace"
                elif Directory.Exists full then Directory
                elif File.Exists full then
                    if not (isNull (FileInfo(full).LinkTarget)) then Unreadable "linked path"
                    else File(SHA256.HashData(readBounded full 8388608) |> Convert.ToHexString |> _.ToLowerInvariant())
                else Absent
        with
        | :? IOException | :? UnauthorizedAccessException | :? InvalidDataException -> Unreadable "path could not be read within its bound"

    let private readAtlas (root: string) : Read<AtlasEntry list> =
        let directory = Path.Combine(root, "docs", "atlas", "records")
        if not (Directory.Exists directory) then Unavailable "docs/atlas/records is not present in the workspace"
        else
            try
                let files = Directory.GetFiles(directory, "*.json") |> Array.sort
                if files.Length > 5000 then Unavailable "Atlas archive exceeds the 5000 record bound"
                else
                    let parsed =
                        files
                        |> Array.map (fun file ->
                            try
                                use document = JsonDocument.Parse(readBounded file 1048576)
                                let text name =
                                    match document.RootElement.TryGetProperty(name: string) with
                                    | true, v when v.ValueKind = JsonValueKind.String -> v.GetString()
                                    | _ -> raise (InvalidDataException name)
                                Some { Id = text "id"; Kind = text "kind"; Status = text "status"; Title = text "title"
                                       Record = Path.GetFileNameWithoutExtension file }
                            with
                            | :? JsonException | :? InvalidDataException | :? IOException | :? InvalidOperationException -> None)
                    let unreadable = parsed |> Array.filter Option.isNone |> Array.length
                    if unreadable > 0 then Unavailable $"{unreadable} Atlas record(s) could not be parsed; unresolved IDs would be unsound"
                    else Supplied(parsed |> Array.choose id |> List.ofArray)
            with :? IOException | :? UnauthorizedAccessException -> Unavailable "Atlas archive could not be listed"

    let readNavigation (root: string) (paths: string list) : Read<NavigationFacts> =
        let full = Path.GetFullPath root
        if not (Directory.Exists full) then Unavailable "workspace is not a directory"
        else
            let files = paths |> List.map (fun p -> p, fileFact full p) |> Map.ofList
            Supplied { Files = files; Atlas = readAtlas full }

    // ------------------------------------------------------------------ observe, execute

    let observe (transport: IDeadlineSinglePageGitHubTransport) (arguments: Arguments) : Observations =
        let issue = ContextViewReads.readIssue transport arguments.Issue
        let pullRequest =
            match arguments.PullRequest with
            | Some number -> ContextViewReads.readPullRequest transport arguments.Issue.Repository number
            | None -> NotRequested
        let custody =
            match arguments.Custody with
            | Some path -> readCustody path
            | None -> NotRequested
        let navigation =
            match arguments.Workspace with
            | None -> NotRequested
            | Some root ->
                let asked =
                    match issue with
                    | Supplied fact -> requestedPaths arguments.Action fact
                    | _ -> instructionPaths arguments.Action
                readNavigation root asked
        { Issue = arguments.Issue; Action = arguments.Action; RequestedPullRequest = arguments.PullRequest
          CapBytes = arguments.CapBytes; IssueRead = issue; PullRequestRead = pullRequest
          Custody = custody; Navigation = navigation }

    let private resultSchema = "fsgg.coord.context-view-result/1"

    let private refusal (reason: string) =
        JsonSerializer.Serialize
            {| schema = resultSchema; status = "refusal"; reason = reason; effects = "none" |}

    let private privateOptions () =
        let options = FileStreamOptions(Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None)
        if not (OperatingSystem.IsWindows()) then options.UnixCreateMode <- UnixFileMode.UserRead ||| UnixFileMode.UserWrite
        options

    let execute (transport: IDeadlineSinglePageGitHubTransport) (arguments: Arguments) =
        let exitRefused = 3
        let report = Path.GetFullPath arguments.ReportFile
        let parent = Path.GetDirectoryName report
        if File.Exists report || Directory.Exists report then refusal "report file already exists; a new path is required", exitRefused
        elif isNull parent || not (Directory.Exists parent) then refusal "report file directory does not exist", exitRefused
        else
            match build (observe transport arguments) with
            | Error(CapOutOfBounds(cap, minimum)) ->
                refusal $"cap {cap} is outside {MinimumCapBytes}..{MaximumCapBytes} or too small for the header, gaps and omission records (minimum useful {minimum})", exitRefused
            | Ok view ->
                try
                    use stream = new FileStream(report, privateOptions ())
                    stream.Write(Encoding.UTF8.GetBytes view.Json)
                    stream.Flush true
                    let line =
                        JsonSerializer.Serialize
                            {| schema = resultSchema
                               status = (match view.Status with Complete -> "complete" | Incomplete -> "incomplete")
                               report = report; bytes = view.Bytes; sha256 = view.Sha256; capBytes = arguments.CapBytes
                               gaps = view.Gaps.Length
                               obligationGaps = view.Gaps |> List.filter (fun g -> g.Severity = Obligation) |> List.length
                               omissions = view.Omissions.Length
                               effects = "read-only; the only write is the new report file" |}
                    line, 0
                with :? IOException | :? UnauthorizedAccessException ->
                    refusal "report file could not be created exclusively", exitRefused

    let usage =
        String.concat "\n"
            [ "fsgg-coord-engine context-view --issue OWNER/REPO#N --report-file NEW_PATH"
              "    [--pr N] [--action implement|repair|review|merge|accept|resume]"
              "    [--cap-bytes N] [--custody CURSOR_FILE] [--workspace DIR]"
              ""
              "Optional, read-only scoped context view (schema fsgg.coord.context-view/1) for one owning issue."
              "Reads the six-field issue and, with --pr, the pull request and its check runs (GET only), plus the"
              "optional observe-operation cursor (--custody) and a local checkout (--workspace). The one write is the"
              $"NEW file --report-file (never overwritten). Default cap {DefaultCapBytes} bytes ({MinimumCapBytes}..{MaximumCapBytes}); overflow omits whole"
              "sections, lists each with a retrieval pointer and reports status incomplete. Missing inputs are typed gaps."
              "Needs $GITHUB_TOKEN or $GH_TOKEN. Prints one compact result line. Exit 0 view written, 3 refusal." ]

    let tryRun (arguments: string list) =
        match arguments with
        | [ "context-view"; ("--help" | "-h") ] ->
            printfn "%s" usage
            Some 0
        | "context-view" :: rest ->
            Some(
                match parseArguments rest with
                | Error reason ->
                    printfn "%s" (refusal reason)
                    3
                | Ok parsed ->
                    let token =
                        let primary = Environment.GetEnvironmentVariable "GITHUB_TOKEN"
                        if String.IsNullOrWhiteSpace primary then Environment.GetEnvironmentVariable "GH_TOKEN" else primary
                    if String.IsNullOrWhiteSpace token then
                        printfn "%s" (refusal "native read credential unavailable ($GITHUB_TOKEN or $GH_TOKEN)")
                        3
                    else
                        use transport = new HttpTransport(apiBaseFromEnv (), token)
                        let line, code = execute (transport :> IDeadlineSinglePageGitHubTransport) parsed
                        printfn "%s" line
                        code)
        | _ -> None
