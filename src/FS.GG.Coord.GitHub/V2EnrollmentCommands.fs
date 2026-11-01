namespace FS.GG.Coord.GitHub

// Batched programme-enrollment commands shared by the BoardV2Import tool and the coordination engine. Every
// subcommand prints a refusal code only: never payload, nonce, native identity, path or input text. A partially
// created output set is retained on failure; nothing is deleted, retried, reseeded or overwritten.
module V2EnrollmentCommands =
    open System
    open System.IO
    open System.Text.Json
    open System.Text.Json.Nodes
    open System.Text.RegularExpressions

    let private require condition message = if not condition then invalidOp message
    let private prop (node: JsonNode) (name: string) = node.[name]
    let private str (node: JsonNode) = if isNull node then "" else node.GetValue<string>()
    let private s node name = prop node name |> str
    let private b node name = prop node name |> fun n -> n.GetValue<bool>()
    let private integer node name = prop node name |> fun n -> n.GetValue<int>()
    let private exact node name expected = require (s node name = expected) (name + " differs")
    let private closedKeys (node: JsonNode) (names: string list) =
        for pair in node.AsObject() do require (List.contains pair.Key names) ("unexpected property " + pair.Key)
        require (node.AsObject().Count = names.Length && (names |> List.forall (fun name -> node.AsObject().ContainsKey name))) "missing property"

    let private programmeInputLimit = V2Preflight.bindingLimitBytes
    let private programmePayloadLimit = V2Preflight.payloadLimitBytes

    let private programmeRead limit (path: string) =
        let options = FileStreamOptions(Mode = FileMode.Open, Access = FileAccess.Read, Share = FileShare.Read,
                                        Options = FileOptions.SequentialScan)
        use stream = new FileStream(path, options)
        // Length and all reads use this same handle. No path-stat/read-all allocation race;
        // growth and truncation refuse, including a byte beyond the selected bound.
        let length = stream.Length
        require (length > 0L && length <= int64 limit) "programme input bound"
        let bytes = Array.zeroCreate<byte> (int length)
        stream.ReadExactly bytes
        require (stream.ReadByte() = -1 && stream.Length = length) "programme input changed"
        bytes

    let private programmePath (path: string) =
        let full = Path.GetFullPath path
        let mutable current = FileInfo(full) :> FileSystemInfo
        while not (isNull current) do
            // Refuse links in the supplied custody path. This is an ordinary local custody
            // check, not a sandbox against another process replacing parent directories.
            if current.Exists then
                require ((current.Attributes &&& FileAttributes.ReparsePoint) = enum<FileAttributes> 0) "programme path refused"
            current <- match current with
                       | :? FileInfo as file -> file.Directory :> FileSystemInfo
                       | :? DirectoryInfo as directory -> directory.Parent :> FileSystemInfo
                       | _ -> null
        full

    // Same narrow Linux-x64 directory-entry barrier used by the maintained migration CLI.
    // File Flush(true) alone does not make a newly created directory entry durable.
    module private ProgrammeDirectoryNative =
        [<System.Runtime.InteropServices.DllImport("libc", EntryPoint = "open", SetLastError = true)>]
        extern int openDirectory(string path, int flags)
        [<System.Runtime.InteropServices.DllImport("libc", SetLastError = true)>]
        extern int fsync(int descriptor)
        [<System.Runtime.InteropServices.DllImport("libc", SetLastError = true)>]
        extern int close(int descriptor)

    let private programmeDirectorySupported () =
        OperatingSystem.IsLinux()
        && System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture = System.Runtime.InteropServices.Architecture.X64

    let private programmeSyncDirectory (path: string) =
        require (programmeDirectorySupported()) "programme directory durability unavailable"
        // Linux O_DIRECTORY | O_CLOEXEC | O_NOFOLLOW. No fallback, retry or pair-atomicity claim.
        let descriptor = ProgrammeDirectoryNative.openDirectory(path, 0x10000 ||| 0x80000 ||| 0x20000)
        require (descriptor >= 0) "programme directory durability unavailable"
        let mutable complete = false
        try
            complete <- ProgrammeDirectoryNative.fsync descriptor = 0
        finally
            if ProgrammeDirectoryNative.close descriptor <> 0 then complete <- false
        require complete "programme directory durability unavailable"


    let private programmeRetained (path: string) (expected: byte array) =
        if not (OperatingSystem.IsWindows()) then
            require (File.GetUnixFileMode(path) = (UnixFileMode.UserRead ||| UnixFileMode.UserWrite)) "programme output custody"
        let actual = programmeRead programmeInputLimit path
        require (actual.AsSpan().SequenceEqual(expected.AsSpan())) "programme output readback"


    exception Refused of V2Enrollment.Refusal

    let refuse (refusal: V2Enrollment.Refusal) : 'a = raise (Refused refusal)
    let strictUtf8 = Text.UTF8Encoding(false, true)
    let manifestPath = "docs/coordination/board-v2-import-manifest.json"
    let sha256Hex (bytes: byte array) = Security.Cryptography.SHA256.HashData bytes |> Convert.ToHexStringLower

    // Order-insensitive, exact, duplicate-free named arguments.
    let named (args: string array) (names: string list) =
        let rest = args |> Array.skip 1
        if rest.Length <> names.Length * 2 then refuse V2Enrollment.MalformedInput
        let pairs = rest |> Array.chunkBySize 2 |> Array.map (fun pair -> pair.[0], pair.[1])
        if (pairs |> Array.map fst |> Set.ofArray) <> Set.ofList names || pairs.Length <> names.Length then refuse V2Enrollment.MalformedInput
        Map.ofArray pairs

    let read limit (path: string) =
        try programmeRead limit (programmePath path) with _ -> refuse V2Enrollment.InputUnreadable

    let readText limit (path: string) =
        let bytes = read limit path
        try strictUtf8.GetString bytes with _ -> refuse V2Enrollment.MalformedInput

    // Inputs that cannot be resolved without crossing a link are InputUnreadable; outputs are CustodyUnavailable.
    let distinctPaths (inputs: string list) (outputs: string list) =
        let comparison = if OperatingSystem.IsWindows() then StringComparer.OrdinalIgnoreCase else StringComparer.Ordinal
        let resolvedInputs = try inputs |> List.map programmePath with _ -> refuse V2Enrollment.InputUnreadable
        let resolvedOutputs = try outputs |> List.map programmePath with _ -> refuse V2Enrollment.CustodyUnavailable
        let full = resolvedInputs @ resolvedOutputs
        if (Collections.Generic.HashSet<string>(full, comparison)).Count <> full.Length then refuse V2Enrollment.MalformedInput

    let outputsAbsent (paths: string list) =
        for path in paths do
            let full = try programmePath path with _ -> refuse V2Enrollment.CustodyUnavailable
            if File.Exists full || Directory.Exists full then refuse V2Enrollment.OutputExists

    // A CreateNew that loses a race (or meets an entry the earlier absence check could not see) is
    // OutputExists; any other failure to create, write, sync or read back is CustodyUnavailable.
    let write (path: string) (bytes: byte array) =
        let full = try programmePath path with _ -> refuse V2Enrollment.CustodyUnavailable
        if File.Exists full || Directory.Exists full then refuse V2Enrollment.OutputExists
        let stream = match V2Custody.createExclusive full with Ok stream -> stream | Error refusal -> refuse refusal
        try
            (use stream = stream
             stream.Write bytes
             stream.Flush(true))
            programmeSyncDirectory (Path.GetDirectoryName full)
            programmeRetained full bytes
        with _ -> refuse V2Enrollment.CustodyUnavailable

    // `allowEmpty` admits a command whose correct answer can be empty (a diff with no changes); everywhere else an
    // empty answer is a failed read.
    let private gitCapture (allowEmpty: bool) (repository: string) (arguments: string list) (limit: int) : string =
        let info = Diagnostics.ProcessStartInfo("git")
        for argument in [ "-C"; (try programmePath repository with _ -> refuse V2Enrollment.InputUnreadable) ] @ arguments do info.ArgumentList.Add argument
        info.RedirectStandardOutput <- true
        info.RedirectStandardError <- true
        info.UseShellExecute <- false
        info.Environment.["GIT_TERMINAL_PROMPT"] <- "0"
        try
            use proc = Diagnostics.Process.Start info
            let errors = proc.StandardError.ReadToEndAsync()
            use buffer = new MemoryStream()
            let chunk = Array.zeroCreate<byte> 512
            let mutable count = proc.StandardOutput.BaseStream.Read(chunk, 0, chunk.Length)
            while count > 0 && buffer.Length <= int64 limit do
                buffer.Write(chunk, 0, count)
                count <- proc.StandardOutput.BaseStream.Read(chunk, 0, chunk.Length)
            if not (proc.WaitForExit 15000) then
                (try proc.Kill() with _ -> ())
                refuse V2Enrollment.GitReadFailed
            errors.Wait()
            if proc.ExitCode <> 0 || (buffer.Length = 0L && not allowEmpty) || buffer.Length > int64 limit then refuse V2Enrollment.GitReadFailed
            strictUtf8.GetString(buffer.ToArray())
        with
        | Refused refusal -> refuse refusal
        | _ -> refuse V2Enrollment.GitReadFailed

    let private gitRun (repository: string) (arguments: string list) (limit: int) : string = gitCapture false repository arguments limit

    // The constructor-closure evidence for one exact pair of revisions (#5113), read from the checkout's own objects:
    // ancestry by `git merge-base`, the closure derived from the tree at `built`, and the change listing between the
    // two. Any failed or over-bound read, an underivable closure or an unparseable listing is an unobserved input,
    // which `ConstructorIdentity` reports as Unknown: nothing here can produce support by failing.
    let observeConstructorContent (repository: string) (built: string) (main: string) : V2Preflight.Observed<V2Preflight.ConstructorContent> =
        let sha = Regex(@"\A[0-9a-f]{40}\z")
        if not (sha.IsMatch built && sha.IsMatch main) then V2Preflight.NotObserved V2Preflight.Unreadable
        else
            try
                let mergeBase = (gitRun repository [ "merge-base"; built; main ] 128).Trim()
                if not (sha.IsMatch mergeBase) then V2Preflight.NotObserved V2Preflight.ReadFailed
                elif mergeBase <> built then
                    V2Preflight.Known
                        { Built = built; Main = main; BuiltIsAncestor = false; ClosureEntries = 0; ChangedOutsideClosure = 0; ChangedInsideClosure = 0 }
                else
                    let listing = gitRun repository [ "ls-tree"; "-r"; "-z"; "--full-tree"; "--name-only"; built ] 16777216
                    if not (listing.EndsWith("\000", StringComparison.Ordinal)) then V2Preflight.NotObserved V2Preflight.Incomplete
                    else
                        let files = listing.Substring(0, listing.Length - 1).Split('\000') |> Set.ofArray
                        let read (path: string) =
                            if files.Contains path then Some(gitRun repository [ "cat-file"; "blob"; built + ":" + path ] 4194304) else None
                        match V2ConstructorClosure.derive files read with
                        | Error _ -> V2Preflight.NotObserved V2Preflight.Incomplete
                        | Ok closure ->
                            let diff =
                                gitCapture true repository
                                    [ "-c"; "diff.renames=false"; "diff"; "--no-renames"; "--no-ext-diff"; "--no-textconv"; "--name-status"; "-z"; built; main; "--" ]
                                    16777216
                            match V2ConstructorClosure.parseNameStatus diff with
                            | Error _ -> V2Preflight.NotObserved V2Preflight.Incomplete
                            | Ok changes ->
                                let inside = V2ConstructorClosure.changesInside closure changes
                                V2Preflight.Known
                                    { Built = built; Main = main; BuiltIsAncestor = true
                                      ClosureEntries = closure.Paths.Count + closure.Prefixes.Count
                                      ChangedOutsideClosure = changes.Length - inside.Length
                                      ChangedInsideClosure = inside.Length }
            with _ -> V2Preflight.NotObserved V2Preflight.ReadFailed

    // The protected public blob is read from the supplied checkout at the exact merge commit.
    // A fixed argument vector, no shell, bounded output, stderr discarded. A failed read is
    // GitReadFailed; only a blob that is read and then fails to authenticate is a commitment mismatch.
    let protectedPublicBlob (repository: string) (mergeSha: string) =
        if not (Regex.IsMatch(mergeSha, @"\A[0-9a-f]{40}\z")) then refuse V2Enrollment.MalformedInput
        gitRun repository [ "cat-file"; "blob"; mergeSha + ":" + manifestPath ] 2048

    let decodeRequestFile (path: string) =
        match V2Enrollment.decodeRequest (readText 1048576 path) with
        | Ok request -> request
        | Error refusal -> refuse refusal

    // The source revision embedded in an assembly's informational version and the sha256 of its file.
    let private assemblyIdentity (assembly: Reflection.Assembly) =
        let artifact = File.ReadAllBytes assembly.Location |> sha256Hex
        let informational = assembly.GetCustomAttributes(typeof<Reflection.AssemblyInformationalVersionAttribute>, false)
                            |> Seq.cast<Reflection.AssemblyInformationalVersionAttribute> |> Seq.tryExactlyOne
        let revision =
            match informational with
            | Some attribute when attribute.InformationalVersion.Contains "+" -> attribute.InformationalVersion.Substring(attribute.InformationalVersion.LastIndexOf '+' + 1)
            | _ -> ""
        if not (Regex.IsMatch(revision, @"\A[0-9a-f]{40}\z")) then refuse V2Enrollment.CustodyUnavailable
        revision, artifact

    let constructorIdentity () = assemblyIdentity (Reflection.Assembly.GetExecutingAssembly())

    let plan (args: string array) =
        let a = named args [ "--request"; "--native-facts"; "--repository"; "--template-output"; "--payload-output" ]
        distinctPaths [ a.["--request"]; a.["--native-facts"] ] [ a.["--template-output"]; a.["--payload-output"] ]
        outputsAbsent [ a.["--template-output"]; a.["--payload-output"] ]
        let request = decodeRequestFile a.["--request"]
        let facts = match V2Enrollment.decodeNativeFacts (readText 1048576 a.["--native-facts"]) with Ok facts -> facts | Error refusal -> refuse refusal
        distinctPaths [ a.["--request"]; request.PredecessorPrivatePath ] [ a.["--template-output"]; a.["--payload-output"] ]
        let privateJson = readText programmeInputLimit request.PredecessorPrivatePath
        let publicJson = protectedPublicBlob a.["--repository"] request.PredecessorMergeSha
        match V2Enrollment.plan privateJson publicJson request facts with
        | Error refusal -> refuse refusal
        | Ok planned ->
            write a.["--template-output"] (strictUtf8.GetBytes planned.TemplateJson)
            write a.["--payload-output"] planned.Payload
            printfn "successor planned; rows=%d appended=%d; offline only" planned.TotalRows planned.Appended.Length

    let prepare (args: string array) =
        if not (programmeDirectorySupported()) then refuse V2Enrollment.CustodyUnavailable
        let a = named args [ "--request"; "--native-facts"; "--repository"; "--template"; "--payload"; "--private-output"; "--public-output"; "--receipt-output" ]
        let request = decodeRequestFile a.["--request"]
        distinctPaths [ a.["--request"]; a.["--native-facts"]; request.PredecessorPrivatePath; a.["--template"]; a.["--payload"] ] [ a.["--private-output"]; a.["--public-output"]; a.["--receipt-output"] ]
        outputsAbsent [ a.["--private-output"]; a.["--public-output"]; a.["--receipt-output"] ]
        let revision, artifact = constructorIdentity ()
        let facts = match V2Enrollment.decodeNativeFacts (readText 1048576 a.["--native-facts"]) with Ok facts -> facts | Error refusal -> refuse refusal
        let predecessorJson = readText programmeInputLimit request.PredecessorPrivatePath
        let publicPredecessor = protectedPublicBlob a.["--repository"] request.PredecessorMergeSha
        let template = readText programmeInputLimit a.["--template"]
        let payload = read programmePayloadLimit a.["--payload"]
        // The supplied template and payload are untrusted bytes: nothing reaches the constructor until they
        // are exactly what the planner re-derives from the request, the predecessor and the native facts.
        match V2Enrollment.verifyPrepared predecessorJson publicPredecessor request facts template payload with
        | Error refusal -> refuse refusal
        | Ok() -> ()
        match FS.GG.Coord.GitHub.V2Projection.prepareProgrammeBinding template payload
                  (fun () -> Security.Cryptography.RandomNumberGenerator.GetBytes(32)) with
        | Error _ -> refuse V2Enrollment.MalformedInput
        | Ok (privateJson, publicJson) ->
            let privateBytes = strictUtf8.GetBytes privateJson
            let publicBytes = strictUtf8.GetBytes publicJson
            if privateBytes.Length > programmeInputLimit || publicBytes.Length > programmeInputLimit then refuse V2Enrollment.MalformedInput
            write a.["--private-output"] privateBytes
            write a.["--public-output"] publicBytes
            let verdict = V2Enrollment.verifyPair predecessorJson privateJson publicJson
            let commitment = match V2Projection.decodeProgrammePublicCommitment publicJson with Ok value -> value | Error _ -> refuse V2Enrollment.MalformedInput
            let receipt = JsonObject()
            receipt.["schema"] <- JsonValue.Create "fsgg.board-v2-successor-receipt/1"
            receipt.["cohort"] <- JsonValue.Create request.Cohort
            receipt.["predecessorMergeSha"] <- JsonValue.Create request.PredecessorMergeSha
            let rows = JsonObject()
            rows.["predecessor"] <- JsonValue.Create verdict.PredecessorRows
            rows.["appended"] <- JsonValue.Create verdict.AppendedRows
            rows.["total"] <- JsonValue.Create verdict.TotalRows
            receipt.["rows"] <- rows
            receipt.["privateSha256"] <- JsonValue.Create(sha256Hex privateBytes)
            receipt.["publicSha256"] <- JsonValue.Create(sha256Hex publicBytes)
            receipt.["commitmentSha256"] <- JsonValue.Create commitment
            let constructor = JsonObject()
            constructor.["revision"] <- JsonValue.Create revision
            constructor.["artifactSha256"] <- JsonValue.Create artifact
            receipt.["constructor"] <- constructor
            receipt.["prefixPreserved"] <- JsonValue.Create verdict.PrefixPreserved
            receipt.["headerPreserved"] <- JsonValue.Create verdict.HeaderPreserved
            receipt.["recomputedCommitment"] <- JsonValue.Create verdict.RecomputedCommitment
            write a.["--receipt-output"] (strictUtf8.GetBytes(receipt.ToJsonString()))
            // Independently reread the complete retained pair after every output exists.
            programmeRetained (programmePath a.["--private-output"]) privateBytes
            programmeRetained (programmePath a.["--public-output"]) publicBytes
            if not (verdict.PrefixPreserved && verdict.HeaderPreserved) then refuse V2Enrollment.PrefixNotPreserved
            if not verdict.RecomputedCommitment then refuse V2Enrollment.PredecessorCommitmentMismatch
            printfn "successor pair prepared; offline only"

    let bindRuntime (args: string array) =
        if not (programmeDirectorySupported()) then refuse V2Enrollment.CustodyUnavailable
        let a = named args [ "--private-binding"; "--repository"; "--merge-sha"; "--runtime-output" ]
        distinctPaths [ a.["--private-binding"] ] [ a.["--runtime-output"] ]
        outputsAbsent [ a.["--runtime-output"] ]
        let mergeSha = a.["--merge-sha"]
        if not (Regex.IsMatch(mergeSha, @"\A[0-9a-f]{40}\z")) then refuse V2Enrollment.MalformedInput
        let privateJson = readText programmeInputLimit a.["--private-binding"]
        let binding = match V2Projection.decodeBinding privateJson with Ok value -> value | Error _ -> refuse V2Enrollment.MalformedInput
        let publicJson = protectedPublicBlob a.["--repository"] mergeSha
        match V2Projection.authenticateProgrammeEnrollment binding publicJson with
        | Error _ -> refuse V2Enrollment.PredecessorCommitmentMismatch
        | Ok _ ->
            // Replace exactly the 40-hex population revision; every other byte is carried over.
            let needle = "\"populationRevision\":\"" + binding.PopulationRevision + "\""
            let first = privateJson.IndexOf(needle, StringComparison.Ordinal)
            if first < 0 || privateJson.IndexOf(needle, first + 1, StringComparison.Ordinal) >= 0 then refuse V2Enrollment.MalformedInput
            let runtimeJson = privateJson.Substring(0, first) + "\"populationRevision\":\"" + mergeSha + "\"" + privateJson.Substring(first + needle.Length)
            let strip (json: string) =
                let node = JsonNode.Parse json
                node.AsObject().Remove "populationRevision" |> ignore
                node
            let runtime = match V2Projection.decodeBinding runtimeJson with Ok value -> value | Error _ -> refuse V2Enrollment.MalformedInput
            if runtime.PopulationRevision <> mergeSha || not (JsonNode.DeepEquals(strip privateJson, strip runtimeJson))
               || (V2Projection.authenticateProgrammeEnrollment runtime publicJson |> Result.isError) then refuse V2Enrollment.PredecessorCommitmentMismatch
            write a.["--runtime-output"] (strictUtf8.GetBytes runtimeJson)
            printfn "runtime binding written; population revision rebound; offline only"

    let renderCarrier (args: string array) =
        let a = named args [ "--receipt"; "--output" ]
        distinctPaths [ a.["--receipt"] ] [ a.["--output"] ]
        outputsAbsent [ a.["--output"] ]
        let receipt = try JsonNode.Parse(readText 65536 a.["--receipt"]) with _ -> refuse V2Enrollment.MalformedInput
        let hex40 (value: string) = Regex.IsMatch(value, @"\A[0-9a-f]{40}\z")
        let hex64 (value: string) = Regex.IsMatch(value, @"\A[0-9a-f]{64}\z")
        let rendered =
            try
                closedKeys receipt [ "schema"; "cohort"; "predecessorMergeSha"; "rows"; "privateSha256"; "publicSha256"; "commitmentSha256"; "constructor"; "prefixPreserved"; "headerPreserved"; "recomputedCommitment" ]
                exact receipt "schema" "fsgg.board-v2-successor-receipt/1"
                let cohort = s receipt "cohort"
                require (Regex.IsMatch(cohort, @"\A[A-Za-z0-9][A-Za-z0-9._-]{0,63}\z") && hex40 (s receipt "predecessorMergeSha")) "receipt identity"
                for name in [ "privateSha256"; "publicSha256"; "commitmentSha256" ] do require (hex64 (s receipt name)) "receipt digest"
                let rows = receipt.["rows"]
                closedKeys rows [ "predecessor"; "appended"; "total" ]
                let predecessor, appended, total = integer rows "predecessor", integer rows "appended", integer rows "total"
                require (predecessor >= 1 && appended >= 1 && total = predecessor + appended && total <= V2Projection.maximumProgrammeRows) "receipt counts"
                let constructor = receipt.["constructor"]
                closedKeys constructor [ "revision"; "artifactSha256" ]
                require (hex40 (s constructor "revision") && hex64 (s constructor "artifactSha256")) "receipt constructor"
                let yes (name: string) = if b receipt name then "yes" else "no"
                let mergeSha, commitment, publicSha, privateSha = s receipt "predecessorMergeSha", s receipt "commitmentSha256", s receipt "publicSha256", s receipt "privateSha256"
                let revision, artifact = s constructor "revision", s constructor "artifactSha256"
                let prefix, header, recomputed = yes "prefixPreserved", yes "headerPreserved", yes "recomputedCommitment"
                String.concat "\n" [
                    $"# Batched enrollment successor: {cohort}"
                    ""
                    $"This carrier delivers the public commitment of one successor enrollment pair prepared offline by the maintained BoardV2Import constructor (`plan-successor`, `prepare-successor`). It appends {appended} row(s) to the {predecessor} rows of the predecessor, which was the protected commitment at `{mergeSha}`, for {total} rows in total. No identity, order or node id of a predecessor row changes."
                    ""
                    "| Fact | Value |"
                    "|---|---|"
                    $"| Predecessor rows | {predecessor} |"
                    $"| Appended rows | {appended} |"
                    $"| Total rows | {total} |"
                    $"| Commitment SHA256 | `{commitment}` |"
                    $"| Public blob SHA256 | `{publicSha}` |"
                    $"| Private binding SHA256 | `{privateSha}` |"
                    $"| Constructor source revision | `{revision}` |"
                    $"| Constructor SHA256 | `{artifact}` |"
                    $"| Predecessor prefix byte-identical | {prefix} |"
                    $"| Payload header and binding fields unchanged | {header} |"
                    $"| Commitment recomputed from the private pair | {recomputed} |"
                    ""
                    "The [public manifest](board-v2-import-manifest.json) carries only the two-field commitment document. The private payload, nonce and binding stay outside source."
                    ""
                    "After protected review and merge, Root must rebind only the successor private runtime binding's population revision to the actual protected delivery (`bind-runtime`), authenticate the delivered public blob against the pair and qualify the exact installed population read. Offline construction and this carrier grant no native write, implementation admission or enrollment authority."
                    "" ]
            with _ -> refuse V2Enrollment.MalformedInput
        write a.["--output"] (strictUtf8.GetBytes rendered)
        printfn "carrier rendered from receipt; offline only"

    // ---- online read-only resolution and the supported-route preflight ----

    // Test seam only: the executable that serves read-only GETs. It never changes what is requested:
    // every invocation is the closed GET argument vector from V2NativeResolve.getArguments.
    let private ghExecutable () =
        match Environment.GetEnvironmentVariable "BOARD_V2_IMPORT_GH" with
        | null | "" -> "gh"
        | value -> value

    let private ghOutputCap = 33554432

    let ghGet : V2NativeResolve.Get =
        fun path paginated ->
            match V2NativeResolve.getArguments path paginated with
            | None -> V2NativeResolve.Failed
            | Some arguments ->
                try
                    let info = Diagnostics.ProcessStartInfo(ghExecutable ())
                    for argument in arguments do info.ArgumentList.Add argument
                    info.RedirectStandardOutput <- true
                    info.RedirectStandardError <- true
                    info.UseShellExecute <- false
                    info.Environment.["GH_PROMPT_DISABLED"] <- "1"
                    info.Environment.["GH_NO_UPDATE_NOTIFIER"] <- "1"
                    use proc = Diagnostics.Process.Start info
                    let errors = proc.StandardError.ReadToEndAsync()
                    // Stream the body up to the cap; a response that reaches it is killed, never buffered whole.
                    use buffer = new MemoryStream()
                    let chunk = Array.zeroCreate<byte> 65536
                    let mutable overflow = false
                    let mutable count = proc.StandardOutput.BaseStream.Read(chunk, 0, chunk.Length)
                    while count > 0 && not overflow do
                        if buffer.Length + int64 count > int64 ghOutputCap then overflow <- true
                        else
                            buffer.Write(chunk, 0, count)
                            count <- proc.StandardOutput.BaseStream.Read(chunk, 0, chunk.Length)
                    if overflow then
                        (try proc.Kill() with _ -> ())
                        V2NativeResolve.Failed
                    elif not (proc.WaitForExit 120000) then
                        (try proc.Kill() with _ -> ())
                        V2NativeResolve.Failed
                    else
                        errors.Wait()
                        if proc.ExitCode = 0 then V2NativeResolve.Body(strictUtf8.GetString(buffer.ToArray()))
                        elif errors.Result.Contains "HTTP 404" then V2NativeResolve.NotFound
                        else V2NativeResolve.Failed
                with _ -> V2NativeResolve.Failed

    let programmeBinding (request: V2Enrollment.Request) =
        match V2Projection.decodeBinding (readText programmeInputLimit request.PredecessorPrivatePath) with
        | Ok binding when V2Projection.isProgrammeBinding binding -> binding
        | _ -> refuse V2Enrollment.MalformedInput

    let resolveIssues (args: string array) =
        let a = named args [ "--request"; "--native-facts-output" ]
        let request = decodeRequestFile a.["--request"]
        distinctPaths [ a.["--request"]; request.PredecessorPrivatePath ] [ a.["--native-facts-output"] ]
        outputsAbsent [ a.["--native-facts-output"] ]
        let binding = programmeBinding request
        match V2NativeResolve.resolve ghGet binding request with
        | Error refusal -> refuse refusal
        | Ok resolved ->
            let json = V2NativeResolve.encodeNativeFacts resolved.Facts
            // The document must be exactly what the planner's own strict reader accepts.
            if (V2Enrollment.decodeNativeFacts json |> Result.isError) then refuse V2Enrollment.MalformedInput
            write a.["--native-facts-output"] (strictUtf8.GetBytes json)
            printfn "native facts resolved; issues=%d; read-only" resolved.Facts.Issues.Length

    // Order-insensitive named arguments with required names, optional names and value-less flags.
    let namedOptional (args: string array) (required: string list) (optional: string list) (flags: string list) =
        let mutable index = 1
        let values = Collections.Generic.Dictionary<string, string>(StringComparer.Ordinal)
        let present = Collections.Generic.HashSet<string>(StringComparer.Ordinal)
        while index < args.Length do
            let name = args.[index]
            if List.contains name flags then
                if not (present.Add name) then refuse V2Enrollment.MalformedInput
                index <- index + 1
            elif List.contains name required || List.contains name optional then
                if index + 1 >= args.Length || values.ContainsKey name then refuse V2Enrollment.MalformedInput
                values.[name] <- args.[index + 1]
                index <- index + 2
            else refuse V2Enrollment.MalformedInput
        if required |> List.exists (fun name -> not (values.ContainsKey name)) then refuse V2Enrollment.MalformedInput
        values, present

    let private observe (read: unit -> 'a) : V2Preflight.Observed<'a> =
        try V2Preflight.Known(read ()) with _ -> V2Preflight.NotObserved V2Preflight.Unreadable

    let private cgroupHeadroom () : V2Preflight.Observed<int64 option> =
        try
            let line =
                File.ReadAllLines "/proc/self/cgroup" |> Array.tryFind (fun entry -> entry.StartsWith "0::")
            match line with
            | None -> V2Preflight.NotObserved V2Preflight.Unreadable
            | Some entry ->
                let relative = entry.Substring 3
                let mutable levels = []
                let mutable current = relative
                let mutable fin = false
                while not fin do
                    let directory = "/sys/fs/cgroup" + (if current = "/" then "" else current)
                    let limitPath, usedPath = Path.Combine(directory, "memory.max"), Path.Combine(directory, "memory.current")
                    if File.Exists limitPath && File.Exists usedPath then levels <- levels @ [ File.ReadAllText limitPath, File.ReadAllText usedPath ]
                    if current = "/" || current = "" then fin <- true
                    else
                        let parent = current.Substring(0, current.LastIndexOf '/')
                        current <- (if parent = "" then "/" else parent)
                V2Preflight.headroomOf levels
        with _ -> V2Preflight.NotObserved V2Preflight.Unreadable

    let private nugetCache (given: string option) =
        match given with
        | Some path -> path
        | None ->
            match Environment.GetEnvironmentVariable "NUGET_PACKAGES" with
            | null | "" -> Path.Combine(Environment.GetFolderPath Environment.SpecialFolder.UserProfile, ".nuget", "packages")
            | value -> value

    // Host MemAvailable, in bytes.
    let private hostMemAvailable () : V2Preflight.Observed<int64> =
        try
            let line = File.ReadAllLines "/proc/meminfo" |> Array.find (fun entry -> entry.StartsWith "MemAvailable:")
            let m = Regex.Match(line, @"\AMemAvailable:\s+([0-9]+)\s+kB\s*\z")
            if m.Success then V2Preflight.Known(Int64.Parse m.Groups.[1].Value * 1024L) else V2Preflight.NotObserved V2Preflight.Unreadable
        with _ -> V2Preflight.NotObserved V2Preflight.Unreadable

    let private protectedMainRepository = "FS-GG/.github"

    let private requiredFor (values: Collections.Generic.Dictionary<string, string>) (names: string list) =
        if names |> List.exists (fun name -> not (values.ContainsKey name)) then refuse V2Enrollment.MalformedInput

    let private preflightEnrollment (route: V2Preflight.Route) (stage: V2Preflight.Stage) (values: Collections.Generic.Dictionary<string, string>) (online: bool) : int =
        requiredFor values [ "--request" ]
        if online = values.ContainsKey "--native-facts" then refuse V2Enrollment.MalformedInput
        let request = decodeRequestFile values.["--request"]
        let optionalNumber (name: string) (fallback: int64) =
            match values.TryGetValue name with
            | true, text ->
                let mutable parsed = 0L
                if Int64.TryParse(text, Globalization.NumberStyles.None, Globalization.CultureInfo.InvariantCulture, &parsed) && parsed >= 0L && parsed <= 1000000000000L then parsed
                else refuse V2Enrollment.MalformedInput
            | _ -> fallback
        let repository = values.["--repository"]
        let privateJson = observe (fun () -> readText programmeInputLimit request.PredecessorPrivatePath)
        let publicJson = observe (fun () -> protectedPublicBlob repository request.PredecessorMergeSha)
        // Online: one read-only resolution gives the facts, the Project item inventory and which plan-document
        // 404s may be invisibility. A failed read leaves those inputs unobserved (ReadFailed), reported as Unknown.
        let facts, items, hidden =
            if online then
                match privateJson with
                | V2Preflight.Known json ->
                    match V2Projection.decodeBinding json with
                    | Ok binding when V2Projection.isProgrammeBinding binding ->
                        match V2NativeResolve.resolve ghGet binding request with
                        | Ok resolved -> V2Preflight.Known(resolved.Facts, V2Preflight.Live), V2Preflight.Known resolved.Items, Set.ofList resolved.HiddenRepositories
                        | Error _ -> V2Preflight.NotObserved V2Preflight.ReadFailed, V2Preflight.NotObserved V2Preflight.ReadFailed, Set.empty
                    | _ -> V2Preflight.NotObserved V2Preflight.Unreadable, V2Preflight.NotObserved V2Preflight.Unreadable, Set.empty
                | V2Preflight.NotObserved kind -> V2Preflight.NotObserved kind, V2Preflight.NotObserved kind, Set.empty
            else
                let supplied =
                    match V2Enrollment.decodeNativeFacts (readText 1048576 values.["--native-facts"]) with
                    | Ok facts -> V2Preflight.Known(facts, V2Preflight.Supplied)
                    | Error refusal -> refuse refusal
                supplied, V2Preflight.NotObserved V2Preflight.NotSupplied, Set.empty
        let revision = observe (fun () -> fst (constructorIdentity ()))
        // The intended main: an asserted revision, else (online) the live head by GET, else the local tracking ref,
        // which may be stale.
        let mainRevision, freshness =
            match values.TryGetValue "--main-revision" with
            | true, sha -> (if Regex.IsMatch(sha, @"\A[0-9a-f]{40}\z") then V2Preflight.Known sha else refuse V2Enrollment.MalformedInput), V2Preflight.AssertedMain
            | _ when online ->
                (match V2NativeResolve.readMainHead ghGet protectedMainRepository with
                 | Ok sha -> V2Preflight.Known sha
                 | Error _ -> V2Preflight.NotObserved V2Preflight.ReadFailed), V2Preflight.LiveMain
            | _ -> observe (fun () -> (gitRun repository [ "rev-parse"; "--verify"; "refs/remotes/origin/main" ] 128).Trim()), V2Preflight.LocalTrackingRef
        let lockPath = match values.TryGetValue "--lockfile" with | true, path -> path | _ -> Path.Combine(programmePath repository, "tools", "BoardV2Import", "packages.lock.json")
        let lockText = observe (fun () -> readText 1048576 lockPath)
        let locked = match lockText with V2Preflight.Known text -> V2Preflight.lockedContentHash "FSharp.Core" text | V2Preflight.NotObserved kind -> V2Preflight.NotObserved kind
        let cached =
            match lockText with
            | V2Preflight.Known text ->
                observe (fun () ->
                    let node = JsonNode.Parse text
                    let version = node.["dependencies"].AsObject() |> Seq.collect (fun framework -> framework.Value.AsObject()) |> Seq.filter (fun package -> package.Key = "FSharp.Core") |> Seq.map (fun package -> package.Value.["resolved"].GetValue<string>()) |> Seq.exactlyOne
                    if not (Regex.IsMatch(version, @"\A[0-9A-Za-z.+-]{1,64}\z")) then invalidOp "version"
                    let cache = nugetCache (match values.TryGetValue "--nuget-cache" with | true, path -> Some path | _ -> None)
                    File.ReadAllText(Path.Combine(cache, "fsharp.core", version.ToLowerInvariant(), ".nupkg.metadata")))
                |> function
                    | V2Preflight.Known metadata -> V2Preflight.metadataContentHash metadata
                    | V2Preflight.NotObserved kind -> V2Preflight.NotObserved kind
            | V2Preflight.NotObserved kind -> V2Preflight.NotObserved kind
        // BindRuntime's own inputs: the successor binding, the merge that delivered it and the public blob at it.
        let successorPrivate, successorMerge, successorPublic =
            match values.TryGetValue "--successor-private", values.TryGetValue "--successor-merge-sha" with
            | (true, path), (true, sha) ->
                if not (Regex.IsMatch(sha, @"\A[0-9a-f]{40}\z")) then refuse V2Enrollment.MalformedInput
                observe (fun () -> readText programmeInputLimit path), V2Preflight.Known sha, observe (fun () -> protectedPublicBlob repository sha)
            | (false, _), (false, _) -> V2Preflight.NotObserved V2Preflight.NotSupplied, V2Preflight.NotObserved V2Preflight.NotSupplied, V2Preflight.NotObserved V2Preflight.NotSupplied
            | _ -> refuse V2Enrollment.MalformedInput
        // A table of artifact shas, one lowercase sha256 per line; absent or unreadable means an empty table.
        let loadHashes (flag: string) : Set<string> =
            match values.TryGetValue flag with
            | true, path ->
                (try
                    (readText 65536 path).Split([| '\n' |], StringSplitOptions.RemoveEmptyEntries)
                    |> Array.map _.Trim()
                    |> Array.filter (fun line -> Regex.IsMatch(line, @"\A[0-9a-f]{64}\z"))
                    |> Set.ofArray
                 with Refused _ -> Set.empty)
            | _ -> Set.empty
        let inputs : V2Preflight.EnrollmentInputs =
            { Request = request
              Facts = facts
              ConstructorRevision = revision
              ProtectedMain = mainRevision
              ProtectedMainFreshness = freshness
              ConstructorContent =
                (match revision, mainRevision, freshness with
                 | V2Preflight.Known built, V2Preflight.Known main, (V2Preflight.LiveMain | V2Preflight.AssertedMain) when built <> main ->
                     observeConstructorContent repository built main
                 | _ -> V2Preflight.NotObserved V2Preflight.NotEvaluated)
              PredecessorPrivate = privateJson
              PredecessorPublic = publicJson
              RuntimeBinding =
                (match values.TryGetValue "--runtime-binding" with
                 | true, path -> observe (fun () -> readText programmeInputLimit path)
                 | _ -> V2Preflight.NotObserved V2Preflight.NotSupplied)
              SuccessorPrivate = successorPrivate
              SuccessorMergeSha = successorMerge
              SuccessorPublic = successorPublic
              InstalledReaderSha256 =
                (match values.TryGetValue "--installed-reader" with
                 | true, path -> observe (fun () -> sha256Hex (File.ReadAllBytes(programmePath path)))
                 | _ -> V2Preflight.NotObserved V2Preflight.NotSupplied)
              KnownInstalledReaders = loadHashes "--known-readers"
              KnownLiftedCapReaders = loadHashes "--known-lifted-cap-readers"
              KnownCap500Readers = loadHashes "--known-cap-500-readers"
              LockedFSharpCoreHash = locked
              CachedFSharpCoreHash = cached
              MemoryHeadroomBytes = cgroupHeadroom ()
              HostMemAvailableBytes = hostMemAvailable ()
              MinimumHeadroomBytes = optionalNumber "--min-headroom-bytes" 1073741824L
              PayloadLimitBytes = V2Preflight.payloadLimitBytes
              BindingLimitBytes = V2Preflight.bindingLimitBytes
              ProjectItems = items
              InvisibleRepositories = hidden
              Now = DateTimeOffset.UtcNow
              ChurnWindow = TimeSpan.FromMinutes(float (optionalNumber "--churn-window-minutes" 10L)) }
        let report = V2Preflight.run route stage inputs
        printfn "%s" (V2Preflight.toJson report)
        V2Preflight.exitCode report.Overall

    let private slugOf (values: Collections.Generic.Dictionary<string, string>) =
        match values.TryGetValue "--slug" with
        | true, slug -> if Regex.IsMatch(slug, @"\A[A-Za-z0-9_.-]{1,100}/[A-Za-z0-9_.-]{1,100}\z") then slug else refuse V2Enrollment.MalformedInput
        | _ -> protectedMainRepository

    // Runs a maintained gate script from the checkout and returns (exit code, stdout, stderr), or None if it could not run.
    let private runGate (repository: string) (arguments: string list) : (int * string * string) option =
        try
            let info = Diagnostics.ProcessStartInfo("python3")
            for argument in [ "-I"; "-B" ] @ arguments do info.ArgumentList.Add argument
            info.WorkingDirectory <- repository
            info.RedirectStandardOutput <- true
            info.RedirectStandardError <- true
            info.UseShellExecute <- false
            use proc = Diagnostics.Process.Start info
            let output = proc.StandardOutput.ReadToEndAsync()
            let errors = proc.StandardError.ReadToEndAsync()
            if not (proc.WaitForExit 120000) then
                (try proc.Kill() with _ -> ())
                None
            else
                output.Wait()
                errors.Wait()
                Some(proc.ExitCode, output.Result, errors.Result)
        with _ -> None

    // The gate files read from the exact base commit (`git show <base>:<path>`), never from the working tree. Returns
    // false, and writes nothing usable, if any is absent at that base.
    let private trustedGateBlobs (repository: string) (baseSha: string) (directory: string) : bool =
        try
            if not (Regex.IsMatch(baseSha, @"\A[0-9a-f]{40}\z")) then false
            else
                IO.Directory.CreateDirectory(IO.Path.Combine(directory, "lib")) |> ignore
                for relative, target in [ "scripts/check-claim-generation.py", "check-claim-generation.py"
                                          "scripts/lib/gate.py", "lib/gate.py"
                                          "scripts/check-closing-keywords.py", "check-closing-keywords.py" ] do
                    let text = gitRun repository [ "show"; baseSha + ":" + relative ] 4194304
                    IO.File.WriteAllText(IO.Path.Combine(directory, target), text)
                true
        with _ -> false

    // The checkout's `refs/remotes/origin/main`, trusted as a base only if it equals the live head of main read by GET. The local ref can
    // be stale, and a stale base carries older, possibly laxer, gates. Offline nothing verifies it, so the answer is never a pass.
    let private verifiedTrackingRef (repository: string) (slug: string) (online: bool) : Result<string, V2Preflight.GapKind> =
        match (try Some((gitRun repository [ "rev-parse"; "--verify"; "refs/remotes/origin/main" ] 128).Trim()) with _ -> None) with
        | None -> Error V2Preflight.Unreadable
        | Some local ->
            if not online then Error V2Preflight.NotLive
            else
                match V2NativeResolve.readMainHead ghGet slug with
                | Error _ -> Error V2Preflight.ReadFailed
                | Ok live -> if live = local then Ok local else Error V2Preflight.NotLive

    let private preflightRoutine (stage: V2Preflight.Stage) (values: Collections.Generic.Dictionary<string, string>) (online: bool) : int =
        let repository = programmePath values.["--repository"]
        let slug = slugOf values
        let defaultBranch = "main"
        let unobserved kind = V2Preflight.NotObserved kind
        // Why the base the gates are read from could not be trusted, when it could not.
        let baseGap = ref V2Preflight.NotEvaluated
        // What the gates read: head ref and sha, base sha, title, body and the commit messages that become the squash message.
        let candidate, baseForGate, title, body, commits =
            match values.TryGetValue "--pr" with
            | true, number ->
                if not online then unobserved V2Preflight.NotSupplied, None, None, None, None
                else
                    let mutable parsed = 0
                    if not (Int32.TryParse(number, Globalization.NumberStyles.None, Globalization.CultureInfo.InvariantCulture, &parsed)) || parsed < 1 then refuse V2Enrollment.MalformedInput
                    match V2NativeResolve.readPullRequest ghGet slug parsed with
                    | Error _ -> unobserved V2Preflight.ReadFailed, None, None, None, None
                    | Ok pr ->
                        let commits = match V2NativeResolve.readPullRequestCommits ghGet slug parsed with Ok text -> Some text | Error _ -> None
                        V2Preflight.Known({ BaseRef = pr.BaseRef; BaseSha = Some pr.BaseSha; HeadRef = pr.HeadRef; HeadSha = pr.HeadSha; State = Some pr.State }: V2Preflight.RoutineCandidate),
                        Some pr.BaseSha, Some pr.Title, Some pr.Body, commits
            | _ ->
                // A candidate not yet opened: the checkout's branch and head, the supplied title and body file, and the
                // commits between the local tracking ref and the head.
                requiredFor values [ "--title"; "--body-file" ]
                let head = observe (fun () -> (gitRun repository [ "rev-parse"; "--abbrev-ref"; "HEAD" ] 256).Trim(), (gitRun repository [ "rev-parse"; "--verify"; "HEAD" ] 128).Trim())
                let body = try Some(readText 1048576 values.["--body-file"]) with Refused _ -> None
                let commits = try Some(gitRun repository [ "log"; "--format=%B"; "refs/remotes/origin/main..HEAD" ] 1048576) with Refused _ -> None
                let baseSha =
                    match verifiedTrackingRef repository slug online with
                    | Ok sha -> Some sha
                    | Error kind ->
                        baseGap.Value <- kind
                        None
                (match head with
                 | V2Preflight.Known(ref, sha) -> V2Preflight.Known({ BaseRef = defaultBranch; BaseSha = None; HeadRef = ref; HeadSha = sha; State = None }: V2Preflight.RoutineCandidate)
                 | V2Preflight.NotObserved kind -> unobserved kind), baseSha, Some values.["--title"], body, commits
        let live, openCount =
            if online then
                let live = match V2NativeResolve.readMainHead ghGet slug with Ok sha -> V2Preflight.Known sha | Error _ -> unobserved V2Preflight.ReadFailed
                let count =
                    match candidate with
                    | V2Preflight.Known value ->
                        (match V2NativeResolve.openPullRequestsFor ghGet slug value.HeadRef with
                         | Ok list -> V2Preflight.Known list.Length
                         | Error _ -> unobserved V2Preflight.ReadFailed)
                    | V2Preflight.NotObserved kind -> V2Preflight.NotObserved kind
                live, count
            else unobserved V2Preflight.NotSupplied, unobserved V2Preflight.NotSupplied
        // The maintained gates are observed, never re-derived, and they run from the exact base's blobs the way
        // routine-eligibility.yml does: a pull request that edits a gate cannot predict its own pass. If the base's
        // bytes cannot be read the gates are Unknown.
        let scratch = IO.Directory.CreateTempSubdirectory("v2preflight-")
        let routineGate, closingGate =
            try
                let trusted = IO.Path.Combine(scratch.FullName, "trusted")
                let trustedReady = match baseForGate with Some baseSha -> trustedGateBlobs repository baseSha trusted | None -> false
                let bodyFile = IO.Path.Combine(scratch.FullName, "body.md")
                let commitsFile = IO.Path.Combine(scratch.FullName, "commits.txt")
                (match body with Some text -> IO.File.WriteAllText(bodyFile, text) | None -> ())
                (match commits with Some text -> IO.File.WriteAllText(commitsFile, text) | None -> ())
                let routine =
                    match candidate, baseForGate, body with
                    | V2Preflight.Known value, Some baseSha, Some _ when Regex.IsMatch(value.HeadRef, @"\Aroutine/[a-z0-9][a-z0-9._-]*\z") && not trustedReady ->
                        unobserved V2Preflight.Unreadable
                    | V2Preflight.Known value, Some baseSha, Some _ when Regex.IsMatch(value.HeadRef, @"\Aroutine/[a-z0-9][a-z0-9._-]*\z") ->
                        (match runGate repository [ IO.Path.Combine(trusted, "check-claim-generation.py"); "--repo"; slug; "--head-ref"; value.HeadRef; "--head-sha"; value.HeadSha; "--base-sha"; baseSha; "--routine-policy-ref"; baseSha; "--body"; bodyFile ] with
                         | Some(0, out, _) when out.Contains "routine delivery admitted" -> V2Preflight.Known V2Preflight.Admitted
                         | Some(_, _, err) ->
                            let m = Regex.Match(err, @"FINDING \[([a-z-]{1,40})\]")
                            if m.Success then V2Preflight.Known(V2Preflight.GateFinding m.Groups.[1].Value) else unobserved V2Preflight.Unreadable
                         | None -> unobserved V2Preflight.Unreadable)
                    | V2Preflight.Known _, _, _ -> unobserved baseGap.Value
                    | V2Preflight.NotObserved kind, _, _ -> unobserved kind
                let closing =
                    match title, body, commits with
                    | Some _, Some _, Some _ when not trustedReady -> unobserved (if baseForGate.IsNone then baseGap.Value else V2Preflight.Unreadable)
                    | Some t, Some _, Some _ ->
                        (match runGate repository [ IO.Path.Combine(trusted, "check-closing-keywords.py"); "--title=" + t; "--body"; bodyFile; "--commits"; commitsFile ] with
                         | Some(0, out, _) ->
                            let declared = Regex.Match(out, @"will close: ([^\r\n]+)")
                            if declared.Success then V2Preflight.Known(V2Preflight.DeclaredClosing(declared.Groups.[1].Value.Split([| ", " |], StringSplitOptions.RemoveEmptyEntries) |> Array.toList))
                            elif out.Contains "closes no issue" then V2Preflight.Known V2Preflight.NoClosing
                            else unobserved V2Preflight.Unreadable
                         | Some(1, _, _) -> V2Preflight.Known V2Preflight.UndeclaredClosing
                         | _ -> unobserved V2Preflight.Unreadable)
                    | _ -> unobserved V2Preflight.Unreadable
                routine, closing
            finally
                (try scratch.Delete true with _ -> ())
        let report = V2Preflight.runRoutine stage { DefaultBranch = defaultBranch; Candidate = candidate; LiveBaseSha = live; OpenPullRequests = openCount; RoutineGate = routineGate; ClosingGate = closingGate }
        printfn "%s" (V2Preflight.toJson report)
        V2Preflight.exitCode report.Overall

    let private preflightPublication (stage: V2Preflight.Stage) (values: Collections.Generic.Dictionary<string, string>) (online: bool) : int =
        requiredFor values [ "--workflow" ]
        let repository = programmePath values.["--repository"]
        let slug = slugOf values
        let workflowPath = values.["--workflow"]
        if not (Regex.IsMatch(workflowPath, @"\A\.github/workflows/[A-Za-z0-9._-]{1,100}\.ya?ml\z")) then refuse V2Enrollment.MalformedInput
        let audit : V2Preflight.Observed<V2Preflight.CoverageAuditResult> =
            if stage <> V2Preflight.RunWriter then V2Preflight.NotObserved V2Preflight.NotSupplied
            else
                // The maintained gate stays authoritative: its result is observed, not re-derived. It audits the checkout's
                // workflows from its own location, so it cannot be run from base blobs; instead it is observed only when the
                // checkout's gate bytes equal the trusted base's, and is otherwise Unknown (GateModified).
                let gateFiles = [ "scripts/check-publication-writer-coverage.py"; "scripts/publication_writer_lock.py" ]
                // The base is the checkout's tracking ref only if it equals the live head of main; a stale ref would compare the gate
                // with an older, possibly laxer, copy and call a modified gate unmodified.
                let matchesBase =
                    match verifiedTrackingRef repository slug online with
                    | Error kind -> Error kind
                    | Ok baseSha ->
                        try
                            Ok(gateFiles |> List.forall (fun relative ->
                                gitRun repository [ "show"; baseSha + ":" + relative ] 4194304 = strictUtf8.GetString(IO.File.ReadAllBytes(IO.Path.Combine(repository, relative)))))
                        with _ -> Error V2Preflight.Unreadable
                match matchesBase with
                | Error kind -> V2Preflight.NotObserved kind
                | Ok false -> V2Preflight.NotObserved V2Preflight.GateModified
                | Ok true ->
                try
                    let info = Diagnostics.ProcessStartInfo("python3")
                    for argument in [ "-I"; "-B"; Path.Combine(repository, "scripts", "check-publication-writer-coverage.py"); "--local" ] do info.ArgumentList.Add argument
                    info.WorkingDirectory <- repository
                    info.RedirectStandardOutput <- true
                    info.RedirectStandardError <- true
                    info.UseShellExecute <- false
                    use proc = Diagnostics.Process.Start info
                    let output = proc.StandardOutput.ReadToEndAsync()
                    let errors = proc.StandardError.ReadToEndAsync()
                    if not (proc.WaitForExit 120000) then
                        (try proc.Kill() with _ -> ())
                        V2Preflight.NotObserved V2Preflight.Unreadable
                    else
                        output.Wait()
                        errors.Wait()
                        let summary = Regex.Match(output.Result, @"publication writer coverage: ([0-9]+) unmet")
                        match proc.ExitCode, summary.Success with
                        | 0, true when summary.Groups.[1].Value = "0" -> V2Preflight.Known V2Preflight.Clean
                        | 1, true -> V2Preflight.Known(V2Preflight.Unmet(int summary.Groups.[1].Value))
                        | _ -> V2Preflight.NotObserved V2Preflight.Unreadable
                with _ -> V2Preflight.NotObserved V2Preflight.Unreadable
        let inputs : V2Preflight.PublicationInputs =
            { Repository = slug
              WorkflowPath = workflowPath
              Workflow = observe (fun () -> readText 1048576 (Path.Combine(repository, workflowPath)))
              Registry = observe (fun () -> readText 1048576 (Path.Combine(repository, "registry", "publication-writers.json")))
              CoverageAudit = audit }
        let report = V2Preflight.runPublication stage inputs
        printfn "%s" (V2Preflight.toJson report)
        V2Preflight.exitCode report.Overall

    let preflight (args: string array) : int =
        let values, flags =
            namedOptional args
                [ "--route"; "--stage"; "--repository" ]
                [ "--request"; "--native-facts"; "--runtime-binding"; "--installed-reader"; "--known-readers"; "--lockfile"; "--nuget-cache"
                  "--main-revision"; "--known-lifted-cap-readers"; "--known-cap-500-readers"; "--min-headroom-bytes"; "--churn-window-minutes"; "--successor-private"; "--successor-merge-sha"
                  "--pr"; "--title"; "--body-file"; "--workflow"; "--slug" ]
                [ "--online" ]
        let online = flags.Contains "--online"
        let route =
            V2Preflight.routes |> List.tryFind (fun route -> V2Preflight.routeCode route = values.["--route"]) |> Option.defaultWith (fun () -> refuse V2Enrollment.MalformedInput)
        let stage =
            V2Preflight.stages |> List.tryFind (fun stage -> V2Preflight.stageCode stage = values.["--stage"]) |> Option.defaultWith (fun () -> refuse V2Enrollment.MalformedInput)
        match route with
        | V2Preflight.ProgrammeEnrollmentSuccessor -> preflightEnrollment route stage values online
        | V2Preflight.RoutineDelivery when stage = V2Preflight.OpenPullRequest || stage = V2Preflight.MergePullRequest -> preflightRoutine stage values online
        | V2Preflight.PublicationWriterRegistration when stage = V2Preflight.RegisterWriter || stage = V2Preflight.RunWriter -> preflightPublication stage values online
        | _ ->
            // A route and stage that do not belong together have no registered checks: an explicit Unknown.
            let report = V2Preflight.unregisteredReport route stage
            printfn "%s" (V2Preflight.toJson report)
            V2Preflight.exitCode report.Overall

    // ---- reader identity rebind after a coherent upgrade (#5120) ----

    let rebindFields (json: string) (fields: (string * string * string) list) : Result<string, V2Enrollment.Refusal> =
        try
            let root = JsonNode.Parse json
            if isNull root || root.GetValueKind() <> JsonValueKind.Object then Error V2Enrollment.MalformedInput
            else
                let topLevel (node: JsonNode) (name: string) =
                    let mutable value: JsonNode = null
                    if node.AsObject().TryGetPropertyValue(name, &value) && not (isNull value) && value.GetValueKind() = JsonValueKind.String then
                        Some(value.GetValue<string>())
                    else None
                // Each old value must be the top-level value, and its `"name": "old"` occurrence must be unique in the text,
                // so the splice cannot touch a nested or embedded copy.
                let spliced =
                    fields
                    |> List.fold (fun (state: Result<string, V2Enrollment.Refusal>) (name, old, value) ->
                        state
                        |> Result.bind (fun text ->
                            if topLevel root name <> Some old || not (Regex.IsMatch(value, @"\A[0-9a-f]{40}([0-9a-f]{24})?\z")) then Error V2Enrollment.MalformedInput
                            else
                                let matches = Regex.Matches(text, "\"" + Regex.Escape name + "\"(\\s*:\\s*\")" + Regex.Escape old + "\"")
                                if matches.Count <> 1 then Error V2Enrollment.MalformedInput
                                else
                                    let m = matches.[0]
                                    Ok(text.Substring(0, m.Index) + "\"" + name + "\"" + m.Groups.[1].Value + value + "\"" + text.Substring(m.Index + m.Length))))
                        (Ok json)
                spliced
                |> Result.bind (fun text ->
                    // The whole guard: with the named fields removed the documents are equal, and each named field now
                    // holds its new value. Nothing else can have changed.
                    let strip (source: string) =
                        let node = JsonNode.Parse source
                        for name, _, _ in fields do node.AsObject().Remove name |> ignore
                        node
                    let rebound = JsonNode.Parse text
                    if JsonNode.DeepEquals(strip json, strip text) && fields |> List.forall (fun (name, _, value) -> topLevel rebound name = Some value) then Ok text
                    else Error V2Enrollment.MalformedInput)
        with _ -> Error V2Enrollment.MalformedInput

    let private scopeKeys =
        [ "sourceRevision"; "artifactSha256"; "cliArtifactSha256"; "principalId"; "principalLogin"; "selectedIssues"; "statusFieldId"
          "roadmapFieldId"; "trackFieldId"; "observationFieldId"; "inReviewColor"; "inReviewDescription"; "views"; "initialPlanning" ]

    // `rebind-reader`: rebind a runtime binding's (`artifactSha256`, `recipeRevision`) and/or a migration scope's
    // (`sourceRevision`, `artifactSha256`, `cliArtifactSha256`) to the executing reader, after verifying that the executing
    // adapter is the named installed artifact and is listed in the verified-reader table. Writes new owner-only files and a
    // record of the change; never edits an input.
    let private rebindReader (host: Reflection.Assembly option) (args: string array) =
        if not (programmeDirectorySupported()) then refuse V2Enrollment.CustodyUnavailable
        let values, _ =
            namedOptional args [ "--installed-reader"; "--known-readers"; "--record-output" ]
                [ "--runtime-binding"; "--runtime-output"; "--repository"; "--scope"; "--scope-output" ] []
        let pairOf (input: string) (output: string) =
            match values.TryGetValue input, values.TryGetValue output with
            | (true, source), (true, target) -> Some(source, target)
            | (false, _), (false, _) -> None
            | _ -> refuse V2Enrollment.MalformedInput
        let runtime = pairOf "--runtime-binding" "--runtime-output"
        let scope = pairOf "--scope" "--scope-output"
        if runtime.IsNone && scope.IsNone then refuse V2Enrollment.MalformedInput
        if runtime.IsSome <> values.ContainsKey "--repository" then refuse V2Enrollment.MalformedInput
        let sources = [ values.["--installed-reader"]; values.["--known-readers"] ] @ ([ runtime; scope ] |> List.choose id |> List.map fst)
        let targets = values.["--record-output"] :: ([ runtime; scope ] |> List.choose id |> List.map snd)
        distinctPaths sources targets
        outputsAbsent targets
        // The executing reader and the installed release it must be.
        let revision, adapter = constructorIdentity ()
        let installed = sha256Hex (read 268435456 values.["--installed-reader"])
        let known =
            (readText 65536 values.["--known-readers"]).Split([| '\n' |], StringSplitOptions.RemoveEmptyEntries)
            |> Array.map _.Trim()
            |> Array.filter (fun line -> Regex.IsMatch(line, @"\A[0-9a-f]{64}\z"))
            |> Set.ofArray
        if installed <> adapter || not (known.Contains adapter) then refuse V2Enrollment.ReaderNotVerified
        let rebound =
            [ match runtime with
              | None -> ()
              | Some(source, target) ->
                  let json = readText programmeInputLimit source
                  let binding =
                      match V2Projection.decodeBinding json with
                      | Ok binding when V2Projection.isProgrammeBinding binding -> binding
                      | _ -> refuse V2Enrollment.MalformedInput
                  // A runtime binding is bound to a protected delivery: the rebound binding must still authenticate against the
                  // public blob at its own population revision.
                  let publicJson = protectedPublicBlob values.["--repository"] binding.PopulationRevision
                  let fields = [ "artifactSha256", binding.ArtifactSha256, adapter; "recipeRevision", binding.RecipeRevision, revision ]
                  let text = match rebindFields json fields with Ok text -> text | Error refusal -> refuse refusal
                  match V2Projection.decodeBinding text with
                  | Ok after when after.ArtifactSha256 = adapter && after.RecipeRevision = revision && after.PopulationRevision = binding.PopulationRevision
                                  && (V2Projection.authenticateProgrammeEnrollment after publicJson |> Result.isOk) -> ()
                  | _ -> refuse V2Enrollment.PredecessorCommitmentMismatch
                  yield "runtimeBinding", source, target, json, text, fields
              match scope with
              | None -> ()
              | Some(source, target) ->
                  // A migration scope also names the engine CLI, so only the engine host can rebind one, and only when it is
                  // the same coherent revision as the adapter.
                  let cliRevision, cli =
                      match host with
                      | Some assembly -> (try assemblyIdentity assembly with _ -> refuse V2Enrollment.ReaderNotVerified)
                      | None -> refuse V2Enrollment.ReaderNotVerified
                  if cliRevision <> revision then refuse V2Enrollment.ReaderNotVerified
                  let json = readText 262144 source
                  let node = try JsonNode.Parse json with _ -> refuse V2Enrollment.MalformedInput
                  let keys = try node.AsObject() |> Seq.map _.Key |> Set.ofSeq with _ -> refuse V2Enrollment.MalformedInput
                  if Set.remove "workLevel" keys <> Set.ofList scopeKeys then refuse V2Enrollment.MalformedInput
                  let field (name: string) = try node.[name].GetValue<string>() with _ -> refuse V2Enrollment.MalformedInput
                  let fields =
                      [ "sourceRevision", field "sourceRevision", revision; "artifactSha256", field "artifactSha256", adapter
                        "cliArtifactSha256", field "cliArtifactSha256", cli ]
                  let text = match rebindFields json fields with Ok text -> text | Error refusal -> refuse refusal
                  yield "scope", source, target, json, text, fields ]
        if rebound |> List.forall (fun (_, _, _, json, text, _) -> json = text) then refuse V2Enrollment.ReaderAlreadyBound
        let record = JsonObject()
        record.["schema"] <- JsonValue.Create "fsgg.board-v2-reader-rebind/1"
        record.["stage"] <- JsonValue.Create "rebind-reader"
        record.["at"] <- JsonValue.Create(DateTimeOffset.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", Globalization.CultureInfo.InvariantCulture))
        let reader = JsonObject()
        reader.["revision"] <- JsonValue.Create revision
        reader.["artifactSha256"] <- JsonValue.Create adapter
        reader.["installedSha256"] <- JsonValue.Create installed
        reader.["inVerifiedTable"] <- JsonValue.Create true
        record.["reader"] <- reader
        for kind, _, target, json, text, fields in rebound do
            let entry = JsonObject()
            entry.["sourceSha256"] <- JsonValue.Create(sha256Hex (strictUtf8.GetBytes json))
            entry.["outputSha256"] <- JsonValue.Create(sha256Hex (strictUtf8.GetBytes text))
            let diff = JsonObject()
            for name, old, value in fields do
                if old <> value then diff.[name] <- JsonArray(JsonValue.Create old, JsonValue.Create value)
            entry.["fieldDiff"] <- diff
            record.[kind] <- entry
            write target (strictUtf8.GetBytes text)
        write values.["--record-output"] (strictUtf8.GetBytes(record.ToJsonString()))
        printfn "reader identity rebound; targets=%d; offline only" rebound.Length

    let private runWith (host: Reflection.Assembly option) (command: string) (args: string array) =
        try
            match command with
            | "plan-successor" -> plan args; 0
            | "prepare-successor" -> prepare args; 0
            | "bind-runtime" -> bindRuntime args; 0
            | "rebind-reader" -> rebindReader host args; 0
            | "resolve-issues" -> resolveIssues args; 0
            | "preflight" -> preflight args
            | _ -> renderCarrier args; 0
        with
        | Refused refusal ->
            eprintfn "board-v2 successor refused: %s" (V2Enrollment.refusalCode refusal)
            2
        | _ ->
            eprintfn "board-v2 successor refused: %s" (V2Enrollment.refusalCode V2Enrollment.MalformedInput)
            2


    let run (command: string) (args: string array) = runWith None command args

    let runHosted (host: Reflection.Assembly) (command: string) (args: string array) = runWith (Some host) command args

    let commands = [ "plan-successor"; "prepare-successor"; "bind-runtime"; "rebind-reader"; "render-carrier"; "resolve-issues"; "preflight" ]
