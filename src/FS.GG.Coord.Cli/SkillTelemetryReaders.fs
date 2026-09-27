namespace FS.GG.Coord.Cli

open System
open System.Buffers.Binary
open System.Collections.Generic
open System.Diagnostics
open System.Globalization
open System.IO
open System.Security.Cryptography
open System.Text
open System.Text.Json
open System.Text.RegularExpressions
open System.Threading.Tasks
open System.Runtime.InteropServices
open Microsoft.Win32.SafeHandles

module internal SkillPrivateDurability =
    [<DllImport("libc", EntryPoint = "open", SetLastError = true)>]
    extern int private Open(string path, int flags)

    [<DllImport("libc", EntryPoint = "fsync", SetLastError = true)>]
    extern int private Fsync(int descriptor)

    [<DllImport("libc", EntryPoint = "close", SetLastError = true)>]
    extern int private Close(int descriptor)

    let syncDirectory path =
        if not (OperatingSystem.IsWindows()) then
            let descriptor = Open(path, 0)
            if descriptor < 0 then raise (IOException("private directory sync could not open the directory"))
            try
                if Fsync descriptor <> 0 then raise (IOException("private directory sync failed"))
            finally
                Close descriptor |> ignore

module SkillTelemetryReaders =
    type Coverage =
        | Unknown
        | Unsupported

    type ReaderError = { Message: string; Coverage: Coverage }

    type RepositoryIdentity = private RepositoryIdentity of string
    type CredentialReference = private CredentialReference of string

    type HostConfig =
        {
            Path: string
            StoreRoot: string
            Engine: string
            Repository: RepositoryIdentity option
            Workspace: bool
            Producer: string option
            BindingDigest: string option
            CredentialReference: CredentialReference option
        }

    type Assignment =
        {
            Schema: string
            FeatureId: string
            ItemId: string
            AttemptId: string
            ParentAttemptId: string option
            ProducerStream: string
        }

    type NativeCounters =
        {
            Input: int64
            CachedInput: int64
            Output: int64
            Reasoning: int64
            Total: int64
        }

    type NativeTurn =
        {
            TurnId: Guid
            Sequence: int
            Provider: string
            Model: string
            Effort: string
            Usage: NativeCounters
        }

    type NativeInventoryRow =
        {
            TurnId: Guid
            Sequence: int
            Status: string
            Terminal: bool
            UsageAvailable: bool
        }

    type NativeInventory =
        {
            ThreadId: Guid
            AllTurnIds: Guid list
            TurnInventory: NativeInventoryRow list
            Turns: NativeTurn list
            Complete: bool
            Coverage: Coverage
            Provider: string option
            Model: string option
            Effort: string option
            SourceDigest: string
            RosterDigest: string
            InventoryPaging: JsonElement
            InventoryCapturedAt: string
            SourceBinding: JsonElement
            AppServerResponses: JsonElement
            RolloutRecords: JsonElement
            ProviderProvenance: string option
        }

    let private failure message =
        Error
            {
                Message = message
                Coverage = Unknown
            }

    let private identityPattern =
        Regex("^[A-Za-z0-9][A-Za-z0-9._:/@+-]{0,199}\\z", RegexOptions.CultureInvariant)

    let private boundedIdentity (name: string) (optionalValue: string option) =
        match optionalValue with
        | None -> Ok None
        | Some value when identityPattern.IsMatch value -> Ok(Some value)
        | _ -> failure $"{name} must be a bounded stable identity"

    module private Process =
        type Output =
            {
                ExitCode: int
                Stdout: string
                Stderr: string
            }

        let run
            (timeoutMilliseconds: int)
            (executable: string)
            (arguments: string list)
            (workingDirectory: string option)
            =
            try
                let start = ProcessStartInfo(executable)
                start.UseShellExecute <- false
                start.RedirectStandardOutput <- true
                start.RedirectStandardError <- true
                start.CreateNoWindow <- true

                match workingDirectory with
                | Some value -> start.WorkingDirectory <- value
                | None -> ()

                arguments |> List.iter start.ArgumentList.Add
                use childProcess = new Diagnostics.Process(StartInfo = start)

                if not (childProcess.Start()) then
                    failure $"{executable} is unavailable"
                else
                    let stdout = childProcess.StandardOutput.ReadToEndAsync()
                    let stderr = childProcess.StandardError.ReadToEndAsync()

                    if not (childProcess.WaitForExit(timeoutMilliseconds)) then
                        try
                            childProcess.Kill(true)
                        with _ ->
                            ()

                        failure $"{executable} timed out"
                    else
                        Task.WaitAll([| stdout :> Task; stderr :> Task |])

                        Ok
                            {
                                ExitCode = childProcess.ExitCode
                                Stdout = stdout.Result
                                Stderr = stderr.Result
                            }
            with error ->
                failure $"{executable} is unavailable: {error.Message}"

    module private Json =
        let options =
            JsonDocumentOptions(CommentHandling = JsonCommentHandling.Disallow, AllowTrailingCommas = false)

        let rec private rejectDuplicates (value: JsonElement) =
            match value.ValueKind with
            | JsonValueKind.Object ->
                let names = HashSet<string>(StringComparer.Ordinal)

                value.EnumerateObject()
                |> Seq.iter (fun property ->
                    if not (names.Add property.Name) then
                        raise (JsonException($"duplicate property: {property.Name}"))

                    rejectDuplicates property.Value)
            | JsonValueKind.Array -> value.EnumerateArray() |> Seq.iter rejectDuplicates
            | _ -> ()

        let parse bytes =
            try
                use document = JsonDocument.Parse(ReadOnlyMemory<byte>(bytes), options)
                rejectDuplicates document.RootElement
                Ok(document.RootElement.Clone())
            with error ->
                failure $"JSON is malformed: {error.Message}"

        let exactProperties (expected: Set<string>) (value: JsonElement) =
            if value.ValueKind <> JsonValueKind.Object then
                false
            else
                let actual = value.EnumerateObject() |> Seq.map _.Name |> Set.ofSeq
                actual = expected

        let stringProperty (name: string) (value: JsonElement) =
            let mutable property = Unchecked.defaultof<JsonElement>

            if
                value.TryGetProperty(name, &property)
                && property.ValueKind = JsonValueKind.String
            then
                Some(property.GetString())
            else
                None

        let optionalStringProperty (name: string) (value: JsonElement) =
            let mutable property = Unchecked.defaultof<JsonElement>

            if
                not (value.TryGetProperty(name, &property))
                || property.ValueKind = JsonValueKind.Null
            then
                Some None
            elif property.ValueKind = JsonValueKind.String then
                Some(Some(property.GetString()))
            else
                None

    module Configuration =
        let private hostSchema = "fsgg.telemetry.host-config/1"
        let private workspaceSchema = "fsgg.telemetry.workspace-config/1"
        let private runtimeAssignmentSchema = "fsgg.telemetry.codex-assignment/1"
        let private ciAssignmentSchema = "fsgg.telemetry.ci-assignment/1"

        let private ownerPattern =
            Regex("^[A-Za-z0-9](?:[A-Za-z0-9-]{0,38}[A-Za-z0-9])?\\z", RegexOptions.CultureInvariant)

        let private repositoryPattern =
            Regex("^(?!\\.{1,2}\\z)[A-Za-z0-9_.-]{1,100}\\z", RegexOptions.CultureInvariant)

        let private scpPattern =
            Regex("^git@github\\.com:([^/]+)/([^/]+)\\z", RegexOptions.IgnoreCase ||| RegexOptions.CultureInvariant)

        let repositoryValue (RepositoryIdentity value) = value
        let private credentialValue (CredentialReference value) = value

        let canonicalRepository value =
            let invalid () =
                failure "git origin is not a canonical GitHub repository URL"

            if
                String.IsNullOrEmpty value
                || value <> value.Trim()
                || value |> Seq.exists Char.IsWhiteSpace
            then
                invalid ()
            else
                let scp = scpPattern.Match value

                let parts =
                    if scp.Success then
                        Some(scp.Groups[1].Value, scp.Groups[2].Value)
                    else
                        let mutable uri = Unchecked.defaultof<Uri>

                        if not (Uri.TryCreate(value, UriKind.Absolute, &uri)) then
                            None
                        elif
                            not (String.Equals(uri.Host, "github.com", StringComparison.OrdinalIgnoreCase))
                            || not (String.IsNullOrEmpty uri.Query)
                            || not (String.IsNullOrEmpty uri.Fragment)
                            || not uri.IsDefaultPort
                        then
                            None
                        elif uri.Scheme = "https" && not (String.IsNullOrEmpty uri.UserInfo) then
                            None
                        elif uri.Scheme = "ssh" && uri.UserInfo <> "git" then
                            None
                        elif uri.Scheme <> "https" && uri.Scheme <> "ssh" then
                            None
                        else
                            match uri.AbsolutePath.TrimStart('/').Split('/', StringSplitOptions.None) with
                            | [| owner; repository |] -> Some(owner, repository)
                            | _ -> None

                match parts with
                | None -> invalid ()
                | Some(owner, rawRepository) ->
                    let repository =
                        if rawRepository.EndsWith(".git", StringComparison.Ordinal) then
                            rawRepository[.. rawRepository.Length - 5]
                        else
                            rawRepository

                    if ownerPattern.IsMatch owner && repositoryPattern.IsMatch repository then
                        Ok(RepositoryIdentity $"{owner}/{repository}")
                    else
                        invalid ()

        let private environmentRepository () =
            [ "FSGG_TELEMETRY_REPOSITORY"; "GITHUB_REPOSITORY" ]
            |> List.tryPick (fun name ->
                match Environment.GetEnvironmentVariable name with
                | null -> None
                | value -> Some(name, value))

        let discoverRepository cwd =
            match environmentRepository () with
            | Some(name, value) ->
                match canonicalRepository $"https://github.com/{value}" with
                | Ok repository when repositoryValue repository = value -> Ok repository
                | _ -> failure $"{name} is not a bounded GitHub repository identity"
            | None ->
                match Process.run 5000 "git" [ "config"; "--local"; "--get-all"; "remote.origin.url" ] (Some cwd) with
                | Error error -> Error error
                | Ok output ->
                    let bytes = Encoding.UTF8.GetByteCount output.Stdout

                    let origins =
                        output.Stdout.Split([| '\r'; '\n' |], StringSplitOptions.RemoveEmptyEntries)

                    if output.ExitCode <> 0 || bytes > 4096 || origins.Length <> 1 then
                        failure "telemetry repository discovery requires one local origin"
                    else
                        canonicalRepository origins[0]

        let private secureMode (path: string) required =
            if OperatingSystem.IsWindows() then
                true
            else
                try
                    (File.GetUnixFileMode path &&& enum<UnixFileMode> 0o777) = enum<UnixFileMode> required
                with _ ->
                    false

        let private configPath explicitPath =
            match explicitPath with
            | Some path -> path, true
            | None ->
                match Environment.GetEnvironmentVariable "FSGG_TELEMETRY_CONFIG" with
                | null ->
                    let baseDirectory =
                        match Environment.GetEnvironmentVariable "XDG_CONFIG_HOME" with
                        | null ->
                            Path.Combine(Environment.GetFolderPath Environment.SpecialFolder.UserProfile, ".config")
                        | value -> value

                    Path.Combine(baseDirectory, "fs-gg", "telemetry.json"), false
                | value -> value, true

        let private parseConfig path =
            try
                let info = FileInfo path

                if not info.Exists || not (String.IsNullOrEmpty info.LinkTarget) then
                    failure "telemetry config must be a regular non-symlink file"
                elif not (info.FullName.Equals(Path.GetFullPath path, StringComparison.Ordinal)) then
                    failure "telemetry config path must be absolute"
                elif not (Path.IsPathFullyQualified path) then
                    failure "telemetry config path must be absolute"
                elif not (secureMode path 0o600) then
                    failure "telemetry config permissions must be 0600"
                elif info.Length > 65536L then
                    failure "telemetry config exceeds 64 KiB"
                else
                    File.ReadAllBytes path |> Json.parse
            with error ->
                failure $"telemetry config is unreadable: {error.Message}"

        let private workspaceRepository () =
            discoverRepository Environment.CurrentDirectory

        let private workspaceCredentialReference (config: JsonElement) producer repository =
            let mutable associations = Unchecked.defaultof<JsonElement>

            if
                not (config.TryGetProperty("associations", &associations))
                || associations.ValueKind <> JsonValueKind.Array
            then
                failure "telemetry workspace credential association is missing or ambiguous"
            else
                let matches = ResizeArray<JsonElement>()

                for association in associations.EnumerateArray() do
                    let mutable repositories = Unchecked.defaultof<JsonElement>

                    if
                        Json.stringProperty "producerId" association = Some producer
                        && association.TryGetProperty("repositories", &repositories)
                        && repositories.ValueKind = JsonValueKind.Array
                        && (repositories.EnumerateArray()
                            |> Seq.exists (fun value ->
                                value.ValueKind = JsonValueKind.String && value.GetString() = repository))
                    then
                        matches.Add(association.Clone())

                if matches.Count = 1 then
                    let mutable destination = Unchecked.defaultof<JsonElement>
                    if matches[0].TryGetProperty("destination", &destination) && destination.ValueKind = JsonValueKind.Object then
                        match Json.stringProperty "credentialReference" destination with
                        | Some value when Regex.IsMatch(value, "^[A-Za-z0-9][A-Za-z0-9_-]{0,63}\\z") ->
                            Ok(CredentialReference value)
                        | _ -> failure "telemetry workspace credential association is missing or ambiguous"
                    else
                        failure "telemetry workspace credential association is missing or ambiguous"
                else
                    failure "telemetry workspace credential association is missing or ambiguous"

        let discover explicitPath =
            let path, required = configPath explicitPath

            if not (File.Exists path) && not required then
                Ok None
            else
                match parseConfig path with
                | Error error -> Error error
                | Ok value ->
                    match Json.stringProperty "schema" value with
                    | Some schema when schema = hostSchema ->
                        if not (Json.exactProperties (Set.ofList [ "schema"; "storeRoot"; "engine" ]) value) then
                            failure "telemetry config has an invalid closed shape"
                        else
                            match Json.stringProperty "storeRoot" value, Json.stringProperty "engine" value with
                            | Some storeRoot, Some engine when
                                Path.IsPathFullyQualified storeRoot
                                && not (String.IsNullOrEmpty engine)
                                && not (engine.Contains '/')
                                && not (engine.Contains '\\')
                                ->
                                Ok(
                                    Some
                                        {
                                            Path = Path.GetFullPath path
                                            StoreRoot = storeRoot
                                            Engine = engine
                                            Repository = None
                                            Workspace = false
                                            Producer = None
                                            BindingDigest = None
                                            CredentialReference = None
                                        }
                                )
                            | Some storeRoot, _ when not (Path.IsPathFullyQualified storeRoot) ->
                                failure "telemetry store root must be absolute"
                            | _ -> failure "telemetry engine must be an executable name"
                    | Some schema when schema = workspaceSchema ->
                        if
                            not (
                                Json.exactProperties
                                    (Set.ofList [ "schema"; "engine"; "associations"; "retiredAssociations" ])
                                    value
                            )
                        then
                            failure "telemetry workspace config has an invalid closed shape"
                        else
                            match Json.stringProperty "engine" value, workspaceRepository () with
                            | Some engine, Ok repository when
                                not (String.IsNullOrEmpty engine)
                                && not (engine.Contains '/')
                                && not (engine.Contains '\\')
                                ->
                                let repositoryText = repositoryValue repository

                                match
                                    Process.run
                                        20000
                                        engine
                                        [
                                            "telemetry"
                                            "workspace"
                                            "binding"
                                            "--config"
                                            Path.GetFullPath path
                                            "--repository"
                                            repositoryText
                                        ]
                                        None
                                with
                                | Error error -> Error error
                                | Ok output when output.ExitCode <> 0 || Encoding.UTF8.GetByteCount output.Stdout > 4096 ->
                                    failure (
                                        if String.IsNullOrWhiteSpace output.Stderr then
                                            "telemetry workspace binding failed"
                                        else
                                            output.Stderr.Trim()
                                    )
                                | Ok output ->
                                    match Encoding.UTF8.GetBytes output.Stdout |> Json.parse with
                                    | Error _ -> failure "telemetry workspace binding result is invalid"
                                    | Ok binding ->
                                        let fields =
                                            Set.ofList
                                                [
                                                    "schema"
                                                    "configPath"
                                                    "repository"
                                                    "producerId"
                                                    "bindingDigest"
                                                    "destination"
                                                    "privateStateRoot"
                                                ]

                                        match
                                            Json.stringProperty "schema" binding,
                                            Json.stringProperty "repository" binding,
                                            Json.stringProperty "producerId" binding,
                                            Json.stringProperty "bindingDigest" binding,
                                            Json.stringProperty "privateStateRoot" binding
                                        with
                                        | Some "fsgg.telemetry.workspace-binding/1",
                                          Some returnedRepository,
                                          Some producer,
                                          Some digest,
                                          Some stateRoot when
                                            Json.exactProperties fields binding
                                            && returnedRepository = repositoryText
                                            && Path.IsPathFullyQualified stateRoot
                                            ->
                                            match workspaceCredentialReference value producer repositoryText with
                                            | Error error -> Error error
                                            | Ok credential ->
                                                Ok(
                                                    Some
                                                        {
                                                            Path = Path.GetFullPath path
                                                            StoreRoot = stateRoot
                                                            Engine = engine
                                                            Repository = Some repository
                                                            Workspace = true
                                                            Producer = Some producer
                                                            BindingDigest = Some digest
                                                            CredentialReference = Some credential
                                                        }
                                                )
                                        | _ -> failure "telemetry workspace binding result is invalid"
                            | Some _, Error error -> Error error
                            | _ -> failure "telemetry engine must be an executable name"
                    | _ -> failure "telemetry config schema is unsupported"

        let validateWorkspace config =
            if not config.Workspace then
                Ok()
            else
                match config.Repository with
                | None -> failure "telemetry workspace validation unavailable"
                | Some repository ->
                    match
                        Process.run
                            20000
                            config.Engine
                            [
                                "telemetry"
                                "workspace"
                                "status"
                                "--config"
                                config.Path
                                "--repository"
                                repositoryValue repository
                            ]
                            None
                    with
                    | Ok output when output.ExitCode = 0 -> Ok()
                    | Ok output ->
                        failure (
                            if String.IsNullOrWhiteSpace output.Stderr then
                                "telemetry workspace validation failed"
                            else
                                output.Stderr.Trim()
                        )
                    | Error error -> Error error

        let private credentialEnvironmentName (reference: string) =
            "FSGG_TELEMETRY_CREDENTIAL_" + reference.Replace('-', '_').ToUpperInvariant()

        [<DllImport("libc", SetLastError = true, EntryPoint = "access")>]
        extern int private access(string path, int mode)

        let private findOnPath name =
            let path = Environment.GetEnvironmentVariable "PATH"

            if String.IsNullOrEmpty path then
                None
            else
                path.Split Path.PathSeparator
                |> Seq.map (fun directory -> Path.Combine(directory, name))
                |> Seq.tryFind (fun candidate ->
                    File.Exists candidate && (OperatingSystem.IsWindows() || access(candidate, 1) = 0))

        let private ownerControlled path =
            try
                let info = FileInfo path

                if not info.Exists || not (String.IsNullOrEmpty info.LinkTarget) then
                    false
                elif OperatingSystem.IsWindows() then
                    true
                else
                    let mode = File.GetUnixFileMode path
                    let writable = UnixFileMode.GroupWrite ||| UnixFileMode.OtherWrite

                    if (mode &&& writable) <> enum<UnixFileMode> 0 then
                        false
                    else
                        match
                            Process.run 5000 "stat" [ "-c"; "%u"; path ] None, Process.run 5000 "id" [ "-u" ] None
                        with
                        | Ok owner, Ok current when owner.ExitCode = 0 && current.ExitCode = 0 ->
                            owner.Stdout.Trim() = current.Stdout.Trim()
                        | _ -> false
            with _ ->
                false

        let mutationCommand config command =
            if not config.Workspace then
                Ok command
            else
                match config.CredentialReference with
                | None -> failure "telemetry workspace credential association is unavailable"
                | Some reference ->
                    let name = credentialValue reference

                    if
                        not (String.IsNullOrEmpty(Environment.GetEnvironmentVariable(credentialEnvironmentName name)))
                    then
                        Ok command
                    else
                        match findOnPath "fdev-telemetry" with
                        | Some path when ownerControlled path -> Ok(Path.GetFullPath path :: "exec" :: command)
                        | Some _ -> failure "telemetry credential client is not an owner-controlled executable"
                        | None -> failure "telemetry credential client is unavailable"

        let assignment schema feature item attempt parentAttempt producer =
            if schema <> runtimeAssignmentSchema && schema <> ciAssignmentSchema then
                failure "assignment schema is unsupported"
            else
                match
                    boundedIdentity "feature" (Some feature),
                    boundedIdentity "item" (Some item),
                    boundedIdentity "attempt" (Some attempt),
                    boundedIdentity "parent attempt" parentAttempt,
                    boundedIdentity "producer" (Some producer)
                with
                | Ok(Some featureValue),
                  Ok(Some itemValue),
                  Ok(Some attemptValue),
                  Ok parentValue,
                  Ok(Some producerValue) ->
                    Ok
                        {
                            Schema = schema
                            FeatureId = featureValue
                            ItemId = itemValue
                            AttemptId = attemptValue
                            ParentAttemptId = parentValue
                            ProducerStream = producerValue
                        }
                | Error error, _, _, _, _
                | _, Error error, _, _, _
                | _, _, Error error, _, _
                | _, _, _, Error error, _
                | _, _, _, _, Error error -> Error error
                | _ -> failure "assignment identity is unavailable"

        let private ensurePrivateDirectory directory =
            if Directory.Exists directory then
                let info = DirectoryInfo directory
                if not (String.IsNullOrEmpty info.LinkTarget) then
                    raise (IOException("private telemetry directory must not be a symlink"))
                if not (OperatingSystem.IsWindows()) &&
                   (File.GetUnixFileMode directory &&& enum<UnixFileMode> 0o077) <> enum<UnixFileMode> 0 then
                    raise (IOException("private telemetry directory permissions must exclude group and other"))
            else
                Directory.CreateDirectory directory |> ignore
                if not (OperatingSystem.IsWindows()) then
                    File.SetUnixFileMode(directory, enum<UnixFileMode> 0o700)

        let createCiAssignment config feature item attempt parentAttempt producer =
            match
                validateWorkspace config, assignment ciAssignmentSchema feature item attempt parentAttempt producer
            with
            | Error error, _ -> Error error
            | _, Error error -> Error error
            | Ok(), Ok value ->
                try
                    let directory = Path.Combine(config.StoreRoot, "assignments")
                    ensurePrivateDirectory directory
                    let unsafeName = $"{producer}-{item}-{attempt}"
                    let safeName = Regex.Replace(unsafeName, "[^A-Za-z0-9._-]", "_")
                    let boundedName = safeName[.. min 179 (safeName.Length - 1)]
                    let target = Path.Combine(directory, boundedName + ".json")
                    let temporary = Path.Combine(directory, $"{boundedName}.{Guid.NewGuid():N}.tmp")

                    let payload =
                        {|
                            schema = value.Schema
                            featureId = value.FeatureId
                            itemId = value.ItemId
                            attemptId = value.AttemptId
                            parentAttemptId = value.ParentAttemptId |> Option.toObj
                            producerStream = value.ProducerStream
                        |}

                    try
                        do
                            use stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None)
                            if not (OperatingSystem.IsWindows()) then
                                File.SetUnixFileMode(temporary, enum<UnixFileMode> 0o600)
                            let bytes = UTF8Encoding(false).GetBytes(JsonSerializer.Serialize(payload) + "\n")
                            stream.Write bytes
                            stream.Flush true
                        File.Move(temporary, target, true)
                        SkillPrivateDurability.syncDirectory directory
                    finally
                        if File.Exists temporary then File.Delete temporary
                    Ok target
                with error ->
                    failure $"private assignment is unavailable: {error.Message}"

    module NativeUsage =
        let private terminalStatuses = Set.ofList [ "completed"; "failed"; "interrupted" ]
        let private maximumEvidenceBytes = 512 * 1024
        let private maximumLineBytes = 1024 * 1024

        /// Keeps bounded wire lines exactly; rollout callers may discard oversized rows.
        type private ExactLines(source: Stream) =
            let buffer = Array.zeroCreate<byte> 8192
            let mutable offset = 0
            let mutable count = 0
            let mutable eof = false

            member _.ReadLine(timeoutMilliseconds: int option, discardOversized: bool) =
                use retained = new MemoryStream()
                let deadline = Stopwatch.StartNew()
                let mutable complete = false
                let mutable discarding = false

                while not complete && not eof do
                    if offset = count then
                        let remaining =
                            timeoutMilliseconds
                            |> Option.map (fun timeout -> timeout - int deadline.ElapsedMilliseconds)

                        if remaining |> Option.exists (fun milliseconds -> milliseconds <= 0) then
                            raise (TimeoutException("native byte line read timed out"))

                        use cancellation = new Threading.CancellationTokenSource()
                        remaining |> Option.iter cancellation.CancelAfter
                        let received =
                            source.ReadAsync(buffer, 0, buffer.Length, cancellation.Token)
                                .GetAwaiter().GetResult()

                        if received = 0 then eof <- true
                        else
                            offset <- 0
                            count <- received

                    if offset < count then
                        let newline = Array.IndexOf(buffer, byte '\n', offset, count - offset)
                        let endOffset = if newline >= 0 then newline + 1 else count
                        let length = endOffset - offset

                        if not discarding then
                            if retained.Length + int64 length > int64 maximumLineBytes then
                                if discardOversized then
                                    discarding <- true
                                    retained.SetLength(0L)
                                else
                                    raise (IOException("native byte line exceeds 1 MiB"))
                            else
                                retained.Write(buffer, offset, length)
                        offset <- endOffset
                        complete <- newline >= 0

                if discarding then Some(Array.empty)
                elif retained.Length = 0L then None
                else Some(retained.ToArray())

        let private decodedLine (raw: byte array) =
            let mutable length = raw.Length
            if length > 0 && raw[length - 1] = byte '\n' then length <- length - 1
            if length > 0 && raw[length - 1] = byte '\r' then length <- length - 1
            UTF8Encoding(false, true).GetString(raw, 0, length)

        let private isNativeUsageRecordPrefix (raw: byte array) =
            try
                let mutable reader = Utf8JsonReader(ReadOnlySpan<byte>(raw, 0, min 256 raw.Length), false, JsonReaderState())
                let mutable awaitingType = false
                let mutable decided = false
                let mutable matched = false
                while not decided && reader.Read() do
                    if awaitingType then
                        matched <- reader.TokenType = JsonTokenType.String && reader.GetString() = "token_usage_record"
                        decided <- true
                    elif reader.TokenType = JsonTokenType.PropertyName && reader.CurrentDepth = 1 && reader.GetString() = "type" then
                        awaitingType <- true
                matched
            with :? JsonException -> false

        let coverageForParent (parent: Guid option) =
            match parent with
            | Some _ -> Unknown
            | None -> Unsupported

        let private sha256 (bytes: byte array) =
            SHA256.HashData bytes |> Convert.ToHexString |> _.ToLowerInvariant()

        let private combineDigest (chunks: byte array list) =
            use hash = IncrementalHash.CreateHash HashAlgorithmName.SHA256

            for chunk in chunks do
                let length = Array.zeroCreate<byte> 8
                BinaryPrimitives.WriteInt64BigEndian(length, int64 chunk.Length)
                hash.AppendData length
                hash.AppendData chunk

            hash.GetHashAndReset() |> Convert.ToHexString |> _.ToLowerInvariant()

        let private stringField name (value: JsonElement) = Json.stringProperty name value

        let private guidField name value =
            match stringField name value with
            | Some text ->
                match Guid.TryParseExact(text, "D") with
                | true, parsed -> Some parsed
                | _ -> None
            | None -> None

        let private int64Field (name: string) (value: JsonElement) =
            let mutable property = Unchecked.defaultof<JsonElement>
            let mutable number = 0L

            if
                value.TryGetProperty(name, &property)
                && property.TryGetInt64(&number)
                && number >= 0L
            then
                Some number
            else
                None

        let private counters value =
            match
                int64Field "input_tokens" value,
                int64Field "cached_input_tokens" value,
                int64Field "output_tokens" value,
                int64Field "reasoning_output_tokens" value,
                int64Field "total_tokens" value
            with
            | Some input, Some cached, Some output, Some reasoning, Some total when
                cached <= input && reasoning <= output && input + output = total
                ->
                Ok
                    {
                        Input = input
                        CachedInput = cached
                        Output = output
                        Reasoning = reasoning
                        Total = total
                    }
            | _ -> failure "native usage counters are malformed"

        type private AppServer(command: string) =
            let start = ProcessStartInfo(command)
            let evidence = ResizeArray<byte array>()
            let mutable disposed = false
            let mutable stderrDrain: Task<string> option = None

            do
                start.ArgumentList.Add "app-server"
                start.UseShellExecute <- false
                start.RedirectStandardInput <- true
                start.RedirectStandardOutput <- true
                start.RedirectStandardError <- true
                start.CreateNoWindow <- true

            let hostProcess = new Diagnostics.Process(StartInfo = start)
            let outputLines = lazy (ExactLines(hostProcess.StandardOutput.BaseStream))

            let requestBytes id methodName parameters =
                let request = Dictionary<string, obj>()
                request["id"] <- id
                request["method"] <- methodName
                request["params"] <- parameters

                JsonSerializer.SerializeToUtf8Bytes(request, JsonSerializerOptions(PropertyNamingPolicy = null))
                |> fun value -> Array.append value [| byte '\n' |]

            let readResponse id =
                let deadline = Stopwatch.StartNew()
                let mutable found = None
                let mutable errorMessage = None

                while found.IsNone && errorMessage.IsNone && deadline.ElapsedMilliseconds < 8000L do
                    let remaining = max 1 (8000 - int deadline.ElapsedMilliseconds)
                    let received =
                        try outputLines.Value.ReadLine(Some remaining, false)
                        with
                        | :? TimeoutException
                        | :? OperationCanceledException ->
                            errorMessage <- Some "Codex App Server read timed out"
                            None
                        | :? IOException as error ->
                            errorMessage <- Some error.Message
                            None

                    match received with
                    | None when errorMessage.IsNone -> errorMessage <- Some "Codex App Server read timed out"
                    | None -> ()
                    | Some bytes ->
                        try
                            let line = decodedLine bytes
                            match Json.parse (Encoding.UTF8.GetBytes line) with
                            | Ok value ->
                                let mutable responseId = 0
                                let mutable idProperty = Unchecked.defaultof<JsonElement>

                                if
                                    value.TryGetProperty("id", &idProperty)
                                    && idProperty.TryGetInt32(&responseId)
                                    && responseId = id
                                then
                                    let mutable result = Unchecked.defaultof<JsonElement>
                                    let mutable responseError = Unchecked.defaultof<JsonElement>

                                    if
                                        value.TryGetProperty("result", &result)
                                        && result.ValueKind = JsonValueKind.Object
                                        && (not (value.TryGetProperty("error", &responseError))
                                            || responseError.ValueKind = JsonValueKind.Null)
                                    then
                                        found <- Some(result.Clone(), bytes)
                                    else
                                        errorMessage <- Some "Codex App Server refused a read-only usage request"
                            | Error _ -> ()
                        with :? DecoderFallbackException -> errorMessage <- Some "Codex App Server returned invalid UTF-8"

                match found, errorMessage with
                | Some value, _ -> Ok value
                | _, Some message -> failure message
                | _ -> failure "Codex App Server read timed out"

            member this.Start() =
                try
                    if not (hostProcess.Start()) then
                        failure "Codex App Server is unavailable"
                    else
                        stderrDrain <- Some(hostProcess.StandardError.ReadToEndAsync())

                        match
                            this.Request(
                                1,
                                "initialize",
                                {|
                                    clientInfo =
                                        {|
                                            name = "fsgg_telemetry"
                                            title = "FS-GG Telemetry"
                                            version = "1"
                                        |}
                                    capabilities = {| experimentalApi = true |}
                                |}
                            )
                        with
                        | Error error -> Error error
                        | Ok _ ->
                            hostProcess.StandardInput.WriteLine("{\"method\":\"initialized\",\"params\":{}}")
                            hostProcess.StandardInput.Flush()
                            Ok()
                with error ->
                    failure $"Codex App Server is unavailable: {error.Message}"

            member _.Request(id, methodName, parameters: obj) =
                try
                    let request = requestBytes id methodName parameters
                    hostProcess.StandardInput.BaseStream.Write(request, 0, request.Length)
                    hostProcess.StandardInput.BaseStream.Flush()

                    match readResponse id with
                    | Error error -> Error error
                    | Ok(result, response) ->
                        evidence.Add request
                        evidence.Add response

                        if evidence |> Seq.sumBy _.Length > maximumEvidenceBytes then
                            failure "native App Server evidence exceeds the bound"
                        else
                            Ok result
                with error ->
                    failure $"Codex App Server stream is unavailable: {error.Message}"

            member _.Evidence = evidence |> Seq.toList

            interface IDisposable with
                member _.Dispose() =
                    if not disposed then
                        disposed <- true

                        try
                            if not hostProcess.HasExited then
                                hostProcess.Kill(true)

                            hostProcess.WaitForExit(2000) |> ignore
                        with _ ->
                            ()

                        stderrDrain <- None
                        hostProcess.Dispose()

        let private page (server: AppServer) methodName requestId (parameters: Map<string, obj>) =
            let rows = ResizeArray<JsonElement>()
            let seen = HashSet<string>(StringComparer.Ordinal)
            let mutable cursor: string option = None
            let mutable finished = false
            let mutable pageNumber = 0
            let mutable error = None

            while not finished && error.IsNone && pageNumber < 10 do
                let request = Dictionary<string, obj>()

                for KeyValue(key, value) in parameters do
                    request[key] <- value

                request["cursor"] <- cursor |> Option.toObj
                request["limit"] <- 100

                match server.Request(requestId + pageNumber, methodName, request) with
                | Error value -> error <- Some value
                | Ok result ->
                    let mutable data = Unchecked.defaultof<JsonElement>

                    if
                        not (result.TryGetProperty("data", &data))
                        || data.ValueKind <> JsonValueKind.Array
                    then
                        error <-
                            Some
                                {
                                    Message = "Codex App Server returned malformed pagination"
                                    Coverage = Unknown
                                }
                    else
                        data.EnumerateArray() |> Seq.iter (fun row -> rows.Add(row.Clone()))

                        if rows.Count > 1000 then
                            error <-
                                Some
                                    {
                                        Message = "native child inventory exceeds the bound"
                                        Coverage = Unknown
                                    }
                        else
                            match Json.optionalStringProperty "nextCursor" result with
                            | None ->
                                error <-
                                    Some
                                        {
                                            Message = "Codex App Server pagination is invalid"
                                            Coverage = Unknown
                                        }
                            | Some None -> finished <- true
                            | Some(Some next) when
                                String.IsNullOrEmpty next || cursor = Some next || not (seen.Add next)
                                ->
                                error <-
                                    Some
                                        {
                                            Message = "Codex App Server pagination is invalid"
                                            Coverage = Unknown
                                        }
                            | Some(Some next) -> cursor <- Some next

                pageNumber <- pageNumber + 1

            match error with
            | Some value -> Error value
            | None when not finished -> failure "Codex App Server pagination exceeds the bound"
            | None -> Ok(rows |> Seq.toList, pageNumber)

        module private NativeFile =
            [<Literal>]
            let ReadOnly = 0

            [<Literal>]
            let CloseOnExec = 0x80000

            [<Literal>]
            let NoFollow = 0x20000

            [<Literal>]
            let Directory = 0x10000

            [<DllImport("libc", EntryPoint = "open", SetLastError = true)>]
            extern int Open(string path, int flags)

            [<DllImport("libc", EntryPoint = "openat", SetLastError = true)>]
            extern int OpenAt(int directory, string path, int flags)

            [<DllImport("libc", EntryPoint = "close", SetLastError = true)>]
            extern int Close(int descriptor)

        let private openRollout codexHome path =
            try
                let sessions = Path.GetFullPath(Path.Combine(codexHome, "sessions"))
                let source = Path.GetFullPath path
                let relative = Path.GetRelativePath(sessions, source)

                if
                    Path.IsPathRooted relative
                    || relative = ".."
                    || relative.StartsWith(".." + string Path.DirectorySeparatorChar, StringComparison.Ordinal)
                then
                    failure "native usage rollout is unavailable or outside the private host"
                elif OperatingSystem.IsLinux() then
                    let descriptors = ResizeArray<int>()

                    try
                        let directoryFlags =
                            NativeFile.ReadOnly
                            ||| NativeFile.CloseOnExec
                            ||| NativeFile.NoFollow
                            ||| NativeFile.Directory

                        let root = NativeFile.Open("/", directoryFlags)

                        if root < 0 then
                            raise (IOException("root directory is unavailable"))

                        descriptors.Add root
                        let mutable current = root

                        let sessionComponents =
                            Path
                                .GetRelativePath("/", sessions)
                                .Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries)

                        for segment in sessionComponents do
                            let next = NativeFile.OpenAt(current, segment, directoryFlags)

                            if next < 0 then
                                raise (IOException("session directory is unavailable"))

                            descriptors.Add next
                            current <- next

                        let components =
                            relative.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries)

                        if components.Length = 0 then
                            raise (IOException("rollout filename is unavailable"))

                        for segment in components[.. components.Length - 2] do
                            let next = NativeFile.OpenAt(current, segment, directoryFlags)

                            if next < 0 then
                                raise (IOException("rollout directory is unavailable"))

                            descriptors.Add next
                            current <- next

                        let fileDescriptor =
                            NativeFile.OpenAt(
                                current,
                                components[components.Length - 1],
                                NativeFile.ReadOnly ||| NativeFile.CloseOnExec ||| NativeFile.NoFollow
                            )

                        if fileDescriptor < 0 then
                            raise (IOException("rollout file is unavailable"))

                        let handle = new SafeFileHandle(nativeint fileDescriptor, true)
                        let stream = new FileStream(handle, FileAccess.Read)

                        if not stream.CanSeek || stream.Length > 128L * 1024L * 1024L then
                            stream.Dispose()
                            raise (IOException("rollout file is not a bounded regular file"))

                        Ok(stream :> Stream)
                    finally
                        for descriptor in descriptors |> Seq.rev do
                            NativeFile.Close descriptor |> ignore
                else
                    let mutable current = sessions

                    let components =
                        relative.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries)

                    let mutable safe =
                        Directory.Exists sessions
                        && String.IsNullOrEmpty(DirectoryInfo(sessions).LinkTarget)

                    for segment in components[.. components.Length - 2] do
                        current <- Path.Combine(current, segment)
                        let info = DirectoryInfo current
                        safe <- safe && info.Exists && String.IsNullOrEmpty info.LinkTarget

                    let info = FileInfo source

                    safe <-
                        safe
                        && info.Exists
                        && String.IsNullOrEmpty info.LinkTarget
                        && info.Length <= 128L * 1024L * 1024L

                    if safe then
                        let stream = File.OpenRead source

                        if stream.CanSeek && stream.Length <= 128L * 1024L * 1024L then
                            Ok(stream :> Stream)
                        else
                            stream.Dispose()
                            failure "native usage rollout is unavailable or outside the private host"
                    else
                        failure "native usage rollout is unavailable or outside the private host"
            with _ ->
                failure "native usage rollout is unavailable or outside the private host"

        type private UsageRecord =
            {
                Response: string
                Usage: NativeCounters
                TurnTotal: NativeCounters
            }

        let private rolloutUsage codexHome path threadId (turnIds: Set<Guid>) =
            match openRollout codexHome path with
            | Error error -> Error error
            | Ok source ->
                try
                    let records = Dictionary<Guid, ResizeArray<UsageRecord>>()
                    let evidence = ResizeArray<byte array>()
                    use sourceStream = source
                    let lines = ExactLines(sourceStream)
                    let mutable failed = None
                    let mutable next = lines.ReadLine(None, true)

                    while next.IsSome && failed.IsNone do
                        let bytes = next.Value
                        if bytes.Length > 0 && isNativeUsageRecordPrefix bytes then
                            let line = decodedLine bytes
                            match Json.parse (Encoding.UTF8.GetBytes line) with
                            | Error _ ->
                                failed <-
                                    Some
                                        {
                                            Message = "native usage record is malformed"
                                            Coverage = Unknown
                                        }
                            | Ok row ->
                                let mutable payload = Unchecked.defaultof<JsonElement>

                                if
                                    stringField "type" row <> Some "token_usage_record"
                                    || not (row.TryGetProperty("payload", &payload))
                                    || payload.ValueKind <> JsonValueKind.Object
                                then
                                    failed <-
                                        Some
                                            {
                                                Message = "native usage record is malformed"
                                                Coverage = Unknown
                                            }
                                else
                                    match
                                        guidField "thread_id" payload,
                                        guidField "turn_id" payload,
                                        stringField "response_id" payload
                                    with
                                    | Some recordThread, Some turnId, Some response when
                                        recordThread = threadId
                                        && turnIds.Contains turnId
                                        && not (String.IsNullOrEmpty response)
                                        ->
                                        let mutable usage = Unchecked.defaultof<JsonElement>
                                        let mutable turnTotal = Unchecked.defaultof<JsonElement>

                                        if
                                            payload.TryGetProperty("usage", &usage)
                                            && payload.TryGetProperty("turn_token_usage", &turnTotal)
                                        then
                                            match counters usage, counters turnTotal with
                                            | Ok usageValue, Ok totalValue ->
                                                let collection =
                                                    match records.TryGetValue turnId with
                                                    | true, value -> value
                                                    | _ ->
                                                        let value = ResizeArray<UsageRecord>()
                                                        records[turnId] <- value
                                                        value

                                                collection.Add
                                                    {
                                                        Response = response
                                                        Usage = usageValue
                                                        TurnTotal = totalValue
                                                    }

                                                evidence.Add bytes

                                                if evidence |> Seq.sumBy _.Length > maximumEvidenceBytes then
                                                    failed <-
                                                        Some
                                                            {
                                                                Message = "native rollout evidence exceeds the bound"
                                                                Coverage = Unknown
                                                            }
                                            | Error error, _
                                            | _, Error error -> failed <- Some error
                                        else
                                            failed <-
                                                Some
                                                    {
                                                        Message = "native usage record is malformed"
                                                        Coverage = Unknown
                                                    }
                                    | _ ->
                                        failed <-
                                            Some
                                                {
                                                    Message = "native usage record belongs to another thread or turn"
                                                    Coverage = Unknown
                                                }

                        if failed.IsNone then next <- lines.ReadLine(None, true)

                    match failed with
                    | Some error -> Error error
                    | None -> Ok(records, evidence |> Seq.toList)
                with error ->
                    failure $"native usage rollout is unavailable or outside the private host: {error.Message}"

        let private aggregate (records: ResizeArray<UsageRecord>) =
            let responses = Dictionary<string, NativeCounters>(StringComparer.Ordinal)

            for record in records do
                responses[record.Response] <- record.Usage

            let total =
                responses.Values
                |> Seq.fold
                    (fun state value ->
                        {
                            Input = state.Input + value.Input
                            CachedInput = state.CachedInput + value.CachedInput
                            Output = state.Output + value.Output
                            Reasoning = state.Reasoning + value.Reasoning
                            Total = state.Total + value.Total
                        })
                    {
                        Input = 0L
                        CachedInput = 0L
                        Output = 0L
                        Reasoning = 0L
                        Total = 0L
                    }

            total, records[records.Count - 1].TurnTotal

        let private rosterDigest (threadId: Guid) (rows: NativeInventoryRow list) =
            let projection =
                rows
                |> List.map (fun row ->
                    {|
                        status = row.Status
                        terminal = row.Terminal
                        turnId = row.TurnId.ToString("D")
                        turnSequence = row.Sequence
                    |})

            JsonSerializer.SerializeToUtf8Bytes(
                {|
                    threadId = threadId.ToString("D")
                    turnInventory = projection
                |},
                JsonSerializerOptions(PropertyNamingPolicy = null)
            )
            |> sha256

        let private sourceBinding
            (capturedAt: string)
            (rootInvocation: string)
            (invocation: string)
            (parentThread: Guid)
            (threadId: Guid)
            (turnIds: Guid list)
            (revision: int)
            =
            JsonSerializer.SerializeToUtf8Bytes(
                {|
                    capturedAt = capturedAt
                    hostSource = "codex-app-server:thread/turns/list"
                    invocationId = invocation
                    orderedTurnIds = turnIds |> List.map (fun (value: Guid) -> value.ToString("D"))
                    parentThreadId = parentThread.ToString("D")
                    producerIdentity = "fsgg-work-roadmap-native-collector/1"
                    revision = revision
                    rootInvocationId = rootInvocation
                    schema = "fsgg.telemetry.native-inventory-source-binding/1"
                    threadId = threadId.ToString("D")
                |},
                JsonSerializerOptions(PropertyNamingPolicy = null)
            )

        let collectWith
            (command: string)
            (codexHome: string)
            (parentThreadId: Guid)
            (nativeAgentId: string)
            (rootInvocationId: string)
            (invocationId: string)
            (revision: int)
            =
            if
                String.IsNullOrEmpty rootInvocationId
                || rootInvocationId.Length > 256
                || String.IsNullOrEmpty invocationId
                || invocationId.Length > 256
                || revision < 0
                || not (Regex.IsMatch(nativeAgentId, "^[A-Za-z0-9_-]{1,128}\\z"))
            then
                failure "native parent or child identity is unavailable"
            else
                try
                    use server = new AppServer(command)

                    match server.Start() with
                    | Error error -> Error error
                    | Ok() ->
                        match
                            page
                                server
                                "thread/list"
                                100
                                (Map.ofList
                                    [
                                        "parentThreadId", box (parentThreadId.ToString("D"))
                                        "sourceKinds", box [| "subAgent"; "subAgentThreadSpawn" |]
                                    ])
                        with
                        | Error error -> Error error
                        | Ok(children, _) ->
                            let matches = ResizeArray<JsonElement>()
                            let mutable readError = None

                            for child in children do
                                match guidField "id" child with
                                | None -> ()
                                | Some childId ->
                                    match
                                        server.Request(
                                            200,
                                            "thread/read",
                                            box
                                                {|
                                                    threadId = childId.ToString("D")
                                                    includeTurns = false
                                                |}
                                        )
                                    with
                                    | Error error -> readError <- Some error
                                    | Ok result ->
                                        let mutable thread = Unchecked.defaultof<JsonElement>
                                        let mutable source = Unchecked.defaultof<JsonElement>
                                        let mutable subAgent = Unchecked.defaultof<JsonElement>
                                        let mutable spawn = Unchecked.defaultof<JsonElement>

                                        if
                                            result.TryGetProperty("thread", &thread)
                                            && thread.ValueKind = JsonValueKind.Object
                                        then
                                            let agentPath =
                                                if
                                                    thread.TryGetProperty("source", &source)
                                                    && source.ValueKind = JsonValueKind.Object
                                                    && source.TryGetProperty("subAgent", &subAgent)
                                                    && subAgent.ValueKind = JsonValueKind.Object
                                                    && subAgent.TryGetProperty("thread_spawn", &spawn)
                                                    && spawn.ValueKind = JsonValueKind.Object
                                                then
                                                    stringField "agent_path" spawn, guidField "parent_thread_id" spawn
                                                else
                                                    None, None

                                            match guidField "parentThreadId" thread, agentPath with
                                            | Some parent, (Some path, Some spawnParent) when
                                                parent = parentThreadId
                                                && spawnParent = parentThreadId
                                                && path.EndsWith("/" + nativeAgentId, StringComparison.Ordinal)
                                                ->
                                                matches.Add(thread.Clone())
                                            | _ -> ()

                            match readError with
                            | Some error -> Error error
                            | None when matches.Count <> 1 -> failure "native child identity is missing or ambiguous"
                            | None ->
                                let thread = matches[0]

                                match guidField "id" thread, stringField "path" thread with
                                | Some threadId, Some rolloutPath ->
                                    match
                                        page
                                            server
                                            "thread/turns/list"
                                            300
                                            (Map.ofList
                                                [
                                                    "threadId", box (threadId.ToString("D"))
                                                    "sortDirection", box "asc"
                                                    "itemsView", box "notLoaded"
                                                ])
                                    with
                                    | Error error -> Error error
                                    | Ok(turnRows, pages) ->
                                        let ids = turnRows |> List.map (guidField "id")

                                        if ids |> List.exists Option.isNone then
                                            failure "native turn identities are malformed"
                                        else
                                            let turnIds = ids |> List.choose id

                                            if (turnIds |> Set.ofList |> Set.count) <> turnIds.Length then
                                                failure "native turn identities are malformed"
                                            else
                                                match
                                                    rolloutUsage codexHome rolloutPath threadId (Set.ofList turnIds)
                                                with
                                                | Error error -> Error error
                                                | Ok(records, rolloutEvidence) ->
                                                    let provider = stringField "modelProvider" thread
                                                    let model = stringField "model" thread
                                                    let effort = stringField "reasoningEffort" thread

                                                    let profileAvailable =
                                                        [ provider; model; effort ]
                                                        |> List.forall (function
                                                            | Some value ->
                                                                not (String.IsNullOrWhiteSpace value)
                                                                && value.Length <= 128
                                                            | None -> false)

                                                    let inventory = ResizeArray<NativeInventoryRow>()
                                                    let observations = ResizeArray<NativeTurn>()

                                                    let mutable complete =
                                                        profileAvailable && pages = 1 && not turnRows.IsEmpty

                                                    let mutable inventoryError = None

                                                    for sequence, turn in turnRows |> List.indexed do
                                                        match guidField "id" turn, stringField "status" turn with
                                                        | Some turnId, Some status when
                                                            not (String.IsNullOrEmpty status) && status.Length <= 64
                                                            ->
                                                            let terminal = terminalStatuses.Contains status
                                                            let mutable available = false

                                                            if terminal then
                                                                match records.TryGetValue turnId with
                                                                | true, values when values.Count > 0 ->
                                                                    let total, turnTotal = aggregate values

                                                                    if total = turnTotal then
                                                                        available <- true

                                                                        observations.Add
                                                                            {
                                                                                TurnId = turnId
                                                                                Sequence = sequence + 1
                                                                                Provider = defaultArg provider ""
                                                                                Model = defaultArg model ""
                                                                                Effort = defaultArg effort ""
                                                                                Usage = total
                                                                            }
                                                                    else
                                                                        complete <- false
                                                                | _ -> complete <- false
                                                            else
                                                                complete <- false

                                                            inventory.Add
                                                                {
                                                                    TurnId = turnId
                                                                    Sequence = sequence + 1
                                                                    Status = status
                                                                    Terminal = terminal
                                                                    UsageAvailable = available
                                                                }
                                                        | _ ->
                                                            inventoryError <-
                                                                Some
                                                                    {
                                                                        Message = "native turn status is malformed"
                                                                        Coverage = Unknown
                                                                    }

                                                    match inventoryError with
                                                    | Some error -> Error error
                                                    | None ->
                                                        let capturedAt =
                                                            DateTimeOffset.UtcNow
                                                                .ToString("O", CultureInfo.InvariantCulture)
                                                                .Replace("+00:00", "Z")

                                                        let binding =
                                                            sourceBinding
                                                                capturedAt
                                                                rootInvocationId
                                                                invocationId
                                                                parentThreadId
                                                                threadId
                                                                turnIds
                                                                revision

                                                        let inventoryValues = inventory |> Seq.toList

                                                        let evidenceRecords =
                                                            server.Evidence
                                                            |> List.chunkBySize 2
                                                            |> List.choose (function
                                                                | [ requestBytes; responseBytes ] ->
                                                                    match Json.parse requestBytes with
                                                                    | Ok request ->
                                                                        let methodName = stringField "method" request |> Option.defaultValue ""
                                                                        if methodName = "initialize" then None
                                                                        else
                                                                            let parameters = request.GetProperty("params")
                                                                            let cursor =
                                                                                match Json.optionalStringProperty "cursor" parameters with
                                                                                | Some value -> value
                                                                                | None -> None
                                                                            Some
                                                                                {|
                                                                                    method = methodName
                                                                                    threadId = stringField "threadId" parameters |> Option.toObj
                                                                                    requestCursor = cursor |> Option.toObj
                                                                                    requestSha256 = sha256 requestBytes
                                                                                    requestBytesBase64 = Convert.ToBase64String requestBytes
                                                                                    responseSha256 = sha256 responseBytes
                                                                                    responseBytesBase64 = Convert.ToBase64String responseBytes
                                                                                |}
                                                                    | Error _ -> None
                                                                | _ -> None)

                                                        let mutable previousCursor: string option = None
                                                        let paging =
                                                            evidenceRecords
                                                            |> List.filter (fun record -> record.method = "thread/turns/list")
                                                            |> List.mapi (fun index record ->
                                                                let response = Convert.FromBase64String record.responseBytesBase64
                                                                let parsed = JsonDocument.Parse response
                                                                let result = parsed.RootElement.GetProperty("result")
                                                                let next =
                                                                    match Json.optionalStringProperty "nextCursor" result with
                                                                    | Some value -> value
                                                                    | None -> None
                                                                let rowCount = result.GetProperty("data").GetArrayLength()
                                                                let row =
                                                                    {|
                                                                        page = index + 1
                                                                        requestCursor = previousCursor |> Option.toObj
                                                                        nextCursor = next |> Option.toObj
                                                                        rowCount = rowCount
                                                                    |}
                                                                previousCursor <- next
                                                                row)

                                                        let bindingRecord =
                                                            {|
                                                                schema = "fsgg.telemetry.native-inventory-source-binding/1"
                                                                producerIdentity = "fsgg-work-roadmap-native-collector/1"
                                                                sha256 = sha256 binding
                                                                bytesBase64 = Convert.ToBase64String binding
                                                            |}

                                                        let rolloutRecords =
                                                            rolloutEvidence
                                                            |> List.map (fun bytes ->
                                                                {|
                                                                    sha256 = sha256 bytes
                                                                    bytesBase64 = Convert.ToBase64String bytes
                                                                |})

                                                        let sourceEvidence =
                                                            evidenceRecords
                                                            |> List.collect (fun record ->
                                                                [ Convert.FromBase64String record.requestBytesBase64
                                                                  Convert.FromBase64String record.responseBytesBase64 ])
                                                        let sourceDigest = combineDigest (binding :: (sourceEvidence @ rolloutEvidence))

                                                        Ok
                                                            {
                                                                ThreadId = threadId
                                                                AllTurnIds = turnIds
                                                                TurnInventory = inventoryValues
                                                                Turns = observations |> Seq.toList
                                                                Complete = complete
                                                                Coverage = Unknown
                                                                Provider = provider
                                                                Model = model
                                                                Effort = effort
                                                                SourceDigest = sourceDigest
                                                                RosterDigest = rosterDigest threadId inventoryValues
                                                                InventoryPaging = JsonSerializer.SerializeToElement paging
                                                                InventoryCapturedAt = capturedAt
                                                                SourceBinding = JsonSerializer.SerializeToElement bindingRecord
                                                                AppServerResponses = JsonSerializer.SerializeToElement evidenceRecords
                                                                RolloutRecords = JsonSerializer.SerializeToElement rolloutRecords
                                                                ProviderProvenance = provider |> Option.map (fun _ -> "codex-app-server-thread.modelProvider")
                                                            }
                                | _ -> failure "native child thread metadata is unavailable"
                with error ->
                    failure $"native usage is unavailable: {error.Message}"

        let collect parentThreadId nativeAgentId rootInvocationId invocationId revision =
            let codexHome =
                match Environment.GetEnvironmentVariable "CODEX_HOME" with
                | null -> Path.Combine(Environment.GetFolderPath Environment.SpecialFolder.UserProfile, ".codex")
                | value -> value

            collectWith "codex" codexHome parentThreadId nativeAgentId rootInvocationId invocationId revision
