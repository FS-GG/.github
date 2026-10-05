namespace FS.GG.Coord.Cli

open System
open System.Diagnostics
open System.IO
open System.Security.Cryptography
open System.Text
open System.Text.Encodings.Web
open System.Text.Json
open System.Text.Json.Nodes
open System.Text.RegularExpressions
open SkillTelemetryReaders

module SkillTelemetryAdapter =
    type CommandResult =
        { ExitCode: int
          Stdout: byte array
          Stderr: byte array }

    type TelemetryCommand =
        | Begin of feature: string * item: string * originalItem: string option * attempt: string *
            parentAttempt: string option * parentToken: string option * relation: string * producer: string *
            model: string * effort: string * lateAfterSeconds: int
        | PopulationOnly of feature: string * item: string * originalItem: string * producer: string
        | Started of token: string * nativeId: string
        | Finish of token: string * outcome: string * exitCode: int option
        | UsageReconcile of token: string
        | CiAssignment of feature: string * item: string * attempt: string * parentAttempt: string option * producer: string
        | Review of token: string * scope: string * input: FileInfo
        | Activity of token: string * input: FileInfo
        | UsageAttribution of token: string * input: FileInfo
        | Complication of token: string * input: FileInfo
        | Status

    let private stateSchema = "fsgg.telemetry.roadmap-dispatch-state/1"
    let private originalStateSchema = "fsgg.telemetry.original-binding-state/1"
    let private batchSchema = "fsgg.telemetry.ingest/1"
    let private runtime = "collaboration-spawn-agent"
    let private utf8 = UTF8Encoding(false)
    let private compact = JsonSerializerOptions(WriteIndented = false, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping)
    let private identityPattern = Regex("^[A-Za-z0-9][A-Za-z0-9._:/@+-]{0,199}\\z", RegexOptions.CultureInvariant)
    let private tokenPattern = Regex("^[0-9a-f]{32}\\z", RegexOptions.CultureInvariant)
    let private assignmentDigestPattern = Regex("^[0-9a-f]{40}:[0-9a-f]{64}\\z", RegexOptions.CultureInvariant)

    exception AdapterError of string

    let acceptsStateSchema schema = schema = stateSchema

    let private fail message = raise (AdapterError message)
    let private node<'T> (value: 'T) : JsonNode = JsonValue.Create<'T>(value) :> JsonNode
    let private nullNode : JsonNode = null

    let private property (name: string) (value: JsonNode) (target: JsonObject) =
        target[name] <- value
        target

    let private jsonObject (fields: (string * JsonNode) list) =
        let value = JsonObject()
        fields |> List.iter (fun (name, item) -> value[name] <- item)
        value

    let private jsonArray (values: JsonNode seq) =
        let result = JsonArray()
        values |> Seq.iter (fun value -> result.Add value)
        result

    let private stringValue (value: JsonNode) =
        if isNull value then None else
        try Some(value.GetValue<string>()) with _ -> None

    let private requiredString (name: string) (value: JsonObject) =
        match value[name] |> stringValue with
        | Some text -> text
        | None -> fail $"dispatch state is malformed ({name})"

    let private optionalString (name: string) (value: JsonObject) = value[name] |> stringValue

    let private requiredInt (name: string) (value: JsonObject) =
        try value[name].GetValue<int>() with _ -> fail $"dispatch state is malformed ({name})"

    let private boolValue (name: string) (value: JsonObject) =
        try value[name].GetValue<bool>() with _ -> fail $"dispatch state is malformed ({name})"

    let private sha256 (value: byte array) = Convert.ToHexString(SHA256.HashData value).ToLowerInvariant()

    let private digest prefix (values: string list) =
        prefix + (values |> String.concat "\u001f" |> utf8.GetBytes |> sha256).Substring(0, 32)

    // Fact identities are global across kinds; keep the logical item ID in itemId.
    let private itemFactIdentity feature item =
        if feature = item then digest "roadmap-item-" [ item ] else item

    let private validateIdentity (name: string) (optional: bool) (value: string option) =
        match value with
        | None when optional -> None
        | Some text when identityPattern.IsMatch text -> Some text
        | _ -> fail $"{name} must be 1-200 bounded identity characters"

    let private validateToken (token: string) =
        if not (tokenPattern.IsMatch token) then fail "token must be the opaque 32-hex dispatch token"
        token

    let private timestamp () = DateTimeOffset.UtcNow.ToString("O").Replace("+00:00", "Z")

    let private result code stdout stderr =
        { ExitCode = code
          Stdout = if String.IsNullOrEmpty stdout then Array.empty else utf8.GetBytes stdout
          Stderr = if String.IsNullOrEmpty stderr then Array.empty else utf8.GetBytes stderr }

    let private success (value: JsonNode) = result 0 (value.ToJsonString(compact) + "\n") ""

    let private event kind identity item fields : JsonNode =
        jsonObject (
            [ "kind", node kind
              "identity", node identity
              "itemId", (item |> Option.map node |> Option.defaultValue nullNode)
              "revision", node 0 ] @ fields)

    type private ProcessResult = { Code: int; Stdout: string; Stderr: string }

    let private execute timeoutSeconds maxOutput (arguments: string list) =
        match arguments with
        | [] -> fail "telemetry command is unavailable"
        | executable :: args ->
            try
                let start = ProcessStartInfo(executable)
                start.UseShellExecute <- false
                start.RedirectStandardOutput <- true
                start.RedirectStandardError <- true
                args |> List.iter start.ArgumentList.Add
                use childProcess = new Process(StartInfo = start)
                if not (childProcess.Start()) then fail "telemetry command is unavailable"
                let stdout = childProcess.StandardOutput.ReadToEndAsync()
                let stderr = childProcess.StandardError.ReadToEndAsync()
                if not (childProcess.WaitForExit(timeoutSeconds * 1000)) then
                    try childProcess.Kill(true) with _ -> ()
                    fail "telemetry command timed out"
                let output = stdout.GetAwaiter().GetResult()
                let errors = stderr.GetAwaiter().GetResult()
                if utf8.GetByteCount output > maxOutput || utf8.GetByteCount errors > maxOutput then
                    fail "telemetry command output exceeds its bound"
                { Code = childProcess.ExitCode; Stdout = output; Stderr = errors }
            with
            | :? ComponentModel.Win32Exception -> fail "telemetry command is unavailable"
            | :? InvalidOperationException -> fail "telemetry command is unavailable"

    let private isRegular path maximum =
        let info = FileInfo path
        if not info.Exists || info.LinkTarget <> null || info.Length > maximum then false
        elif OperatingSystem.IsWindows() then true
        else (File.GetUnixFileMode path &&& enum<UnixFileMode> 0o777) = enum<UnixFileMode> 0o600

    let private writePrivateBytes directory name (bytes: byte array) =
        if Directory.Exists directory then
            let info = DirectoryInfo directory
            if info.LinkTarget <> null then fail "private telemetry directory must not be a symlink"
            if not (OperatingSystem.IsWindows()) && (File.GetUnixFileMode directory &&& enum<UnixFileMode> 0o077) <> enum<UnixFileMode> 0 then
                fail "private telemetry directory permissions must exclude group and other"
        else
            Directory.CreateDirectory directory |> ignore
            if not (OperatingSystem.IsWindows()) then File.SetUnixFileMode(directory, enum<UnixFileMode> 0o700)
        let path = Path.Combine(directory, name + ".json")
        let temporary = Path.Combine(directory, $".{name}.{Guid.NewGuid():N}.tmp")
        try
            do
                use stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None)
                if not (OperatingSystem.IsWindows()) then
                    File.SetUnixFileMode(temporary, enum<UnixFileMode> 0o600)
                stream.Write bytes
                if bytes.Length = 0 || bytes[bytes.Length - 1] <> byte '\n' then stream.WriteByte(byte '\n')
                stream.Flush true
            File.Move(temporary, path, true)
            if not (OperatingSystem.IsWindows()) then File.SetUnixFileMode(path, enum<UnixFileMode> 0o600)
            SkillPrivateDurability.syncDirectory directory
            path
        finally
            if File.Exists temporary then File.Delete temporary

    let private stateDirectory (config: HostConfig) schema =
        Path.Combine(config.StoreRoot, if schema = originalStateSchema then "orchestrator-original-bindings" else "orchestrator-dispatches")

    let private statePath (config: HostConfig) token =
        Path.Combine(stateDirectory config stateSchema, validateToken token + ".json")

    let private saveState (config: HostConfig) (state: JsonObject) =
        let schema = requiredString "schema" state
        let token = requiredString "token" state
        writePrivateBytes (stateDirectory config schema) token (utf8.GetBytes(state.ToJsonString compact)) |> ignore

    let private readObject path unavailableMessage =
        try
            if not (isRegular path 262144L) then fail unavailableMessage
            match JsonNode.Parse(File.ReadAllBytes path) with
            | :? JsonObject as value -> value
            | _ -> fail unavailableMessage
        with
        | :? JsonException -> fail unavailableMessage
        | :? IOException -> fail unavailableMessage
        | :? UnauthorizedAccessException -> fail unavailableMessage

    let private readState (config: HostConfig) token =
        let value = readObject (statePath config token) "dispatch state is unavailable"
        if requiredString "schema" value <> stateSchema || requiredString "token" value <> token then
            fail "dispatch state is malformed"
        if config.Workspace &&
           (optionalString "associationProducer" value <> config.Producer || optionalString "associationDigest" value <> config.BindingDigest) then
            fail "dispatch state belongs to a retired workspace association"
        value

    let private commandForPublication (config: HostConfig) producer digestValue input =
        let command =
            if config.Workspace then
                let repository =
                    config.Repository
                    |> Option.map Configuration.repositoryValue
                    |> Option.defaultWith (fun () -> fail "telemetry workspace repository is unavailable")
                [ config.Engine; "telemetry"; "workspace"; "submit"; "--config"; config.Path
                  "--repository"; repository; "--producer"; producer; "--binding-digest"; digestValue; "--input"; input ]
            else
                [ config.Engine; "telemetry"; "store"; "publish"; "--store-root"; config.StoreRoot; "--input"; input ]
        match Configuration.mutationCommand config command with
        | Ok value -> value
        | Error error -> fail error.Message

    let private preparePublication (config: HostConfig) (state: JsonObject) operation nextPhase (events: JsonArray) =
        match state["pendingPublication"] with
        | :? JsonObject as pending ->
            let batch = pending["batch"]
            if requiredString "operation" pending <> operation || requiredString "nextPhase" pending <> nextPhase ||
               isNull batch || batch["events"].ToJsonString(compact) <> events.ToJsonString(compact) then
                fail "a different telemetry publication is already pending"
        | null ->
            let sequence = requiredInt "sequence" state + 1
            let invocation = requiredString "invocationId" state
            let batch = jsonObject [
                "schema", node batchSchema
                "ingestId", node (invocation + "-" + sequence.ToString("000000"))
                "sourceIdentity", node (requiredString "producerStream" state)
                "generation", node invocation
                "cursor", node (string sequence)
                "eventCount", node events.Count
                "events", events.DeepClone() ]
            state["sequence"] <- node sequence
            state["pendingPublication"] <- jsonObject [ "operation", node operation; "nextPhase", node nextPhase; "batch", batch ]
            saveState config state
        | _ -> fail "telemetry publication intent is malformed"

    let private publishPending (config: HostConfig) (state: JsonObject) =
        match Configuration.validateWorkspace config with
        | Error error -> fail error.Message
        | Ok () -> ()
        let pending =
            match state["pendingPublication"] with
            | :? JsonObject as value when value.Count = 3 -> value
            | _ -> fail "telemetry publication intent is unavailable"
        let batch =
            match pending["batch"] with
            | :? JsonObject as value -> value
            | _ -> fail "telemetry publication intent is malformed"
        let sequence = requiredInt "sequence" state
        let invocation = requiredString "invocationId" state
        if requiredString "schema" batch <> batchSchema || requiredString "generation" batch <> invocation ||
           requiredString "cursor" batch <> string sequence then fail "telemetry publication intent is malformed"
        let directory = Path.Combine(config.StoreRoot, "orchestrator-publish")
        let temporary = writePrivateBytes directory $"batch-{invocation}-{sequence}" (utf8.GetBytes(batch.ToJsonString compact))
        let producer = optionalString "associationProducer" state |> Option.defaultValue ""
        let bindingDigest = optionalString "associationDigest" state |> Option.defaultValue ""
        let completed =
            try execute 20 131072 (commandForPublication config producer bindingDigest temporary)
            finally File.Delete temporary
        if completed.Code <> 0 then
            let message = if String.IsNullOrWhiteSpace completed.Stderr then "telemetry batch publication failed" else completed.Stderr.Trim()
            if message.Contains("invalid-request", StringComparison.Ordinal) then
                state["sequence"] <- node (sequence - 1)
                state.Remove "pendingPublication" |> ignore
                let operation = requiredString "operation" pending
                if operation.StartsWith("native-usage:", StringComparison.Ordinal) then state.Remove "usageIntent" |> ignore
                if operation.StartsWith("native-roster:", StringComparison.Ordinal) then state.Remove "rosterIntent" |> ignore
                saveState config state
            fail message
        if requiredString "operation" pending = "population-only" then
            if not config.Workspace then fail "original binding requires a receipt-scoped workspace"
            let response = completed.Stdout.Trim()
            let status =
                if response.StartsWith("{", StringComparison.Ordinal) then
                    try
                        use document = JsonDocument.Parse response
                        let root = document.RootElement
                        if root.GetProperty("schema").GetString() <> "fsgg.telemetry.receipt/1" ||
                           root.GetProperty("batchId").GetString() <> requiredString "ingestId" batch then
                            fail "original binding receipt is malformed"
                        root.GetProperty("status").GetString()
                    with :? JsonException -> fail "original binding receipt is malformed"
                else response
            if status = "durably-received" then fail "original binding receipt is not applied; retry the exact binding"
            if status <> "applied" then fail "original binding receipt did not apply"
        state["phase"] <- node (requiredString "nextPhase" pending)
        state.Remove "pendingPublication" |> ignore
        saveState config state

    let private publish config state operation nextPhase events =
        preparePublication config state operation nextPhase (jsonArray events)
        publishPending config state

    let private drainCommand (config: HostConfig) =
        if config.Workspace then
            let repository = config.Repository |> Option.map Configuration.repositoryValue |> Option.defaultValue ""
            let command = [ config.Engine; "telemetry"; "workspace"; "drain"; "--config"; config.Path; "--repository"; repository; "--binding-digest"; config.BindingDigest |> Option.defaultValue "" ]
            match Configuration.mutationCommand config command with
            | Ok value -> value
            | Error error -> fail error.Message
        else [ config.Engine; "telemetry"; "store"; "drain"; "--store-root"; config.StoreRoot ]

    let private coverage (state: JsonObject) =
        if optionalString "hostParentThreadId" state |> Option.isSome then "native-collaboration-usage-unknown"
        else "native-collaboration-usage-unsupported"

    let private outputDispatch status token coverageValue =
        jsonObject [ "schema", node "fsgg.telemetry.roadmap-dispatch/1"; "status", node status; "token", node token; "coverage", node coverageValue ]

    let private authorizedOriginal feature item original =
        let read endpoint =
            let completed = execute 10 131072 [ "gh"; "api"; endpoint ]
            if completed.Code <> 0 then fail "protected original-item assignment is unavailable"
            try JsonNode.Parse(completed.Stdout) with :? JsonException -> fail "protected original-item assignment is malformed"
        let reference = read "repos/FS-GG/.github/git/ref/heads/main"
        let revision =
            try
                if reference["ref"].GetValue<string>() <> "refs/heads/main" || (reference["object"]["type"]).GetValue<string>() <> "commit" then
                    fail "protected original-item revision is malformed"
                let value = (reference["object"]["sha"]).GetValue<string>()
                if not (Regex.IsMatch(value, "^[0-9a-f]{40}\\z")) then fail "protected original-item revision is malformed"
                value
            with _ -> fail "protected original-item revision is malformed"
        let content = read $"repos/FS-GG/.github/contents/docs/coordination/telemetry-original-item-assignments.json?ref={revision}"
        try
            if content["type"].GetValue<string>() <> "file" || content["encoding"].GetValue<string>() <> "base64" then
                fail "protected original-item assignment is malformed"
            let bytes = Convert.FromBase64String(content["content"].GetValue<string>().Replace("\n", ""))
            if bytes.Length > 65536 then fail "protected original-item assignment is malformed"
            use document = JsonDocument.Parse bytes
            let root = document.RootElement
            if root.ValueKind <> JsonValueKind.Object || root.EnumerateObject() |> Seq.map _.Name |> Set.ofSeq <> Set [ "schema"; "assignments" ] ||
               root.GetProperty("schema").GetString() <> "fsgg.telemetry.original-item-assignments/1" then
                fail "protected original-item assignment is malformed"
            let matches =
                root.GetProperty("assignments").EnumerateArray()
                |> Seq.map (fun row ->
                    let names = row.EnumerateObject() |> Seq.map _.Name |> Set.ofSeq
                    if names <> Set [ "featureId"; "itemId"; "originalItemId" ] then fail "protected original-item assignment is malformed"
                    row.GetProperty("featureId").GetString(), row.GetProperty("itemId").GetString(), row.GetProperty("originalItemId").GetString())
                |> Seq.filter (fun (f, i, _) -> f = feature && i = item)
                |> Seq.map (fun (_, _, o) -> o)
                |> Seq.toList
            if matches <> [ original ] then fail "original item is not authorized by the protected assignment"
            revision + ":" + sha256 bytes
        with
        | :? JsonException -> fail "protected original-item assignment is malformed"
        | :? FormatException -> fail "protected original-item assignment is malformed"

    let private matchingDispatch config (expected: JsonObject) =
        let directory = stateDirectory config stateSchema
        if not (Directory.Exists directory) then None else
        let paths = Directory.GetFiles(directory, "*.json")
        if paths.Length > 4096 then fail "dispatch state inventory exceeds the recovery bound"
        let matching =
            paths
            |> Array.filter (fun path -> Regex.IsMatch(Path.GetFileName path, "^[0-9a-f]{32}\\.json\\z"))
            |> Array.map (fun path -> readState config (Path.GetFileNameWithoutExtension path))
            |> Array.filter (fun state -> [ "featureId"; "itemId"; "attemptId" ] |> List.forall (fun name -> optionalString name state = optionalString name expected))
        if matching.Length > 1 then fail "dispatch identity is ambiguous in private state"
        match Array.tryHead matching with
        | None -> None
        | Some state ->
            let same =
                expected
                |> Seq.forall (fun pair ->
                    let actual = if pair.Key = "originalItemId" && isNull state[pair.Key] then state["itemId"] else state[pair.Key]
                    let wanted = pair.Value
                    (isNull actual && isNull wanted) || (not (isNull actual) && not (isNull wanted) && actual.ToJsonString(compact) = wanted.ToJsonString(compact)))
            if not same then fail "dispatch attempt retry differs from its durable identity"
            Some state

    let private beginDispatch config feature item originalItem attempt parentAttempt parentToken relation producer model effort lateAfter =
        let feature = validateIdentity "feature" false (Some feature) |> Option.get
        let item = validateIdentity "item" false (Some item) |> Option.get
        let mutable original = validateIdentity "original item" false (Some(originalItem |> Option.defaultValue item)) |> Option.get
        let attempt = validateIdentity "attempt" false (Some attempt) |> Option.get
        let parentAttempt = validateIdentity "parent attempt" true parentAttempt
        let producer = validateIdentity "producer" false (Some producer) |> Option.get
        let model = validateIdentity "model" false (Some model) |> Option.get
        let effort = validateIdentity "effort" false (Some effort) |> Option.get
        if lateAfter < 0 then fail "late-after-seconds must be non-negative"
        if not (Set [ "root"; "child"; "follow-up" ] |> Set.contains relation) then fail "relation must be root, child or follow-up"
        let mutable parent: JsonObject option = None
        let mutable parentDispatch: string option = None
        let mutable parentInvocation: string option = None
        let mutable assignmentDigest: string option = None
        let mutable actualRelation = "root"
        match parentToken with
        | Some parentToken ->
            let value = readState config parentToken
            parent <- Some value
            if originalItem.IsNone then original <- optionalString "originalItemId" value |> Option.defaultValue item
            let phase = requiredString "phase" value
            if phase <> "started" && phase <> "terminal" then fail "parent dispatch must be started before a child is expected"
            if requiredString "itemId" value <> item then fail "parent and child dispatches must share the item identity"
            if optionalString "originalItemId" value |> Option.defaultValue item <> original then fail "parent and child dispatches must share the original item identity"
            parentDispatch <- Some(requiredString "dispatchId" value)
            parentInvocation <- Some(requiredString "invocationId" value)
            assignmentDigest <- optionalString "originalAssignmentDigest" value
            actualRelation <- relation
            if relation = "follow-up" then
                match value["usageLedger"] with
                | null | :? JsonObject -> ()
                | _ -> fail "follow-up usage baseline is malformed"
        | None when relation <> "root" -> fail "child and follow-up dispatches require --parent-token"
        | None when original <> item -> assignmentDigest <- Some(authorizedOriginal feature item original)
        | None -> ()
        let expected = jsonObject [
            "featureId", node feature; "itemId", node item; "originalItemId", node original
            "originalAssignmentDigest", (assignmentDigest |> Option.map node |> Option.defaultValue nullNode)
            "attemptId", node attempt; "parentAttemptId", (parentAttempt |> Option.map node |> Option.defaultValue nullNode)
            "producerStream", node producer; "model", node model; "effort", node effort; "relation", node actualRelation
            "parentDispatchId", (parentDispatch |> Option.map node |> Option.defaultValue nullNode)
            "parentInvocationId", (parentInvocation |> Option.map node |> Option.defaultValue nullNode)
            "lateAfterSeconds", node lateAfter ]
        match matchingDispatch config expected with
        | Some existing ->
            let phase = requiredString "phase" existing
            if phase = "begin-pending" then publishPending config existing
            elif phase <> "expected" then fail "dispatch attempt already progressed beyond expectation"
            outputDispatch "expected" (requiredString "token" existing) (coverage existing)
        | None ->
            if parent |> Option.exists (fun value -> requiredString "phase" value = "terminal" && actualRelation <> "follow-up") then
                fail "parent dispatch must be started before a child is expected"
            let token, activation, dispatch, invocation = Guid.NewGuid().ToString("N"), Guid.NewGuid().ToString("N"), Guid.NewGuid().ToString("N"), Guid.NewGuid().ToString("N")
            let activation, rootInvocation =
                match parent with
                | Some value -> requiredString "activationId" value, requiredString "rootInvocationId" value
                | None -> activation, invocation
            let hostParent =
                if actualRelation = "root" then None else
                match Environment.GetEnvironmentVariable "CODEX_THREAD_ID" with
                | value when not (String.IsNullOrEmpty value) && Guid.TryParse value |> fst -> Some value
                | _ -> None
            let baseline =
                match parent, hostParent with
                | Some previous, Some thread when actualRelation = "follow-up" && optionalString "hostParentThreadId" previous = Some thread ->
                    match NativeUsage.collect (Guid.Parse thread) (requiredString "nativeId" previous)
                              (requiredString "rootInvocationId" previous) (requiredString "invocationId" previous) 0 with
                    | Ok inventory -> Some inventory
                    | Error _ -> None
                | _ -> None
            let baselineIds = baseline |> Option.map (fun inventory -> inventory.AllTurnIds |> List.map string) |> Option.defaultValue []
            let state = jsonObject [
                "schema", node stateSchema; "token", node token; "phase", node "begin-pending"; "sequence", node 0
                "featureId", node feature; "itemId", node item; "originalItemId", node original
                "originalAssignmentDigest", (assignmentDigest |> Option.map node |> Option.defaultValue nullNode)
                "attemptId", node attempt; "parentAttemptId", (parentAttempt |> Option.map node |> Option.defaultValue nullNode)
                "producerStream", node producer; "model", node model; "effort", node effort
                "activationId", node activation; "dispatchId", node dispatch; "invocationId", node invocation; "rootInvocationId", node rootInvocation
                "parentDispatchId", (parentDispatch |> Option.map node |> Option.defaultValue nullNode)
                "parentInvocationId", (parentInvocation |> Option.map node |> Option.defaultValue nullNode)
                "relation", node actualRelation; "lateAfterSeconds", node lateAfter; "nativeId", nullNode
                "hostParentThreadId", (hostParent |> Option.map node |> Option.defaultValue nullNode)
                "baselineTurnIds", jsonArray (baselineIds |> Seq.map node)
                "baselineThreadId", (baseline |> Option.map (fun inventory -> node (string inventory.ThreadId)) |> Option.defaultValue nullNode)
                "baselineProvenance", (baseline |> Option.map (fun _ -> node "codex-app-server-thread-turns-list") |> Option.defaultValue nullNode)
                "usageBaselineKnown", node (actualRelation <> "follow-up" || baseline.IsSome)
                "associationProducer", (config.Producer |> Option.map node |> Option.defaultValue nullNode)
                "associationDigest", (config.BindingDigest |> Option.map node |> Option.defaultValue nullNode) ]
            baseline |> Option.iter (fun inventory ->
                state["baselineHostSource"] <- node "codex-app-server:thread/turns/list"
                state["baselinePaging"] <- JsonNode.Parse(inventory.InventoryPaging.GetRawText())
                state["baselineCapturedAt"] <- node inventory.InventoryCapturedAt
                state["baselineRosterDigest"] <- node inventory.RosterDigest
                state["baselineSourceDigest"] <- node inventory.SourceDigest
                state["baselineSourceBinding"] <- JsonNode.Parse(inventory.SourceBinding.GetRawText())
                state["baselineProducerStream"] <- parent |> Option.map (requiredString "producerStream" >> node) |> Option.defaultValue nullNode
                state["baselineCollectorProducer"] <- node "fsgg-work-roadmap-native-collector/1"
                state["baselineAppServerResponses"] <- JsonNode.Parse(inventory.AppServerResponses.GetRawText())
                state["baselineRolloutRecords"] <- JsonNode.Parse(inventory.RolloutRecords.GetRawText()))
            let at = timestamp ()
            let events = ResizeArray<JsonNode>()
            if actualRelation = "root" then
                events.Add(event "feature" feature None [ ("name", node feature) ])
                events.Add(event "item" (itemFactIdentity feature item) (Some item) [ ("featureId", node feature) ])
                let key = digest "" [ item; original ]
                events.Add(event "budget-population" ("budget-population-" + key) (Some item) [ "originalItemId", node original; "state", node "open"; "sourceKind", node "native-item"; "sourceRef", node ("roadmap-dispatch:" + key) ])
                events.Add(event "operational-activation" ("operational-activation-" + activation) (Some item) [ "activationId", node activation; "scope", node "explicit-future-dispatches"; "runtime", node runtime; "activatedAt", node at; "clockProvenance", node "host-wall"; "lateAfterSeconds", node lateAfter ])
            parentAttempt |> Option.iter (fun value -> events.Add(event "parent-child" (digest "parent-child-" [ value; attempt ]) (Some item) [ "parentId", node value; "childId", node attempt ]))
            events.Add(event "expected-dispatch" ("expected-dispatch-" + dispatch) (Some item) [ "dispatchId", node dispatch; "activationId", node activation; "relation", node actualRelation; "parentDispatchId", (parentDispatch |> Option.map node |> Option.defaultValue nullNode); "runtime", node runtime; "expectedAt", node at; "clockProvenance", node "host-wall" ])
            publish config state "begin" "expected" events
            outputDispatch "expected" token (coverage state)

    let private started config token nativeId =
        let state = readState config token
        let nativeId = validateIdentity "native id" false (Some nativeId) |> Option.get
        match requiredString "phase" state with
        | "started" ->
            if requiredString "nativeId" state <> nativeId then fail "started dispatch belongs to a different native identity"
        | "start-pending" ->
            if requiredString "nativeId" state <> nativeId then fail "pending start belongs to a different native identity"
            publishPending config state
        | "expected" ->
            let item, invocation = requiredString "itemId" state, requiredString "invocationId" state
            let at = timestamp ()
            let events = ResizeArray<JsonNode>()
            events.Add(event "invocation-lineage" ("invocation-lineage-" + invocation) (Some item) [ "dispatchId", state["dispatchId"].DeepClone(); "invocationId", node invocation; "relation", state["relation"].DeepClone(); "parentInvocationId", (if isNull state["parentInvocationId"] then nullNode else state["parentInvocationId"].DeepClone()); "rootInvocationId", state["rootInvocationId"].DeepClone(); "runtime", node runtime ])
            events.Add(event "runtime-admission" ("runtime-admission-" + invocation) (Some item) [ "invocationId", node invocation; "featureId", state["featureId"].DeepClone(); "attemptId", state["attemptId"].DeepClone(); "parentAttemptId", (if isNull state["parentAttemptId"] then nullNode else state["parentAttemptId"].DeepClone()); "producerStream", state["producerStream"].DeepClone(); "requestedModel", state["model"].DeepClone(); "requestedEffort", state["effort"].DeepClone(); "backend", node "codex-collaboration" ])
            events.Add(event "runtime-start" ("runtime-process-" + invocation) (Some item) [ "invocationId", node invocation; "threadId", node nativeId; "turnId", nullNode; "turnSequence", nullNode; "processId", node 0; "phase", node "process" ])
            for name in [ "admission"; "start" ] do events.Add(event "event-time" ($"event-time-{invocation}-{name}") (Some item) [ "invocationId", node invocation; "event", node name; "occurredAt", node at; "occurredClockProvenance", node "host-wall"; "observedAt", node at; "observedClockProvenance", node "host-wall" ])
            events.Add(event "runtime-gap" ($"runtime-gap-{invocation}-native-process") (Some item) [ "invocationId", node invocation; "code", node "native-process-id-unavailable" ])
            if optionalString "hostParentThreadId" state |> Option.isNone then events.Add(event "runtime-gap" ($"runtime-gap-{invocation}-native-usage") (Some item) [ "invocationId", node invocation; "code", node "native-collaboration-usage-unsupported" ])
            state["phase"] <- node "start-pending"; state["nativeId"] <- node nativeId
            publish config state "started" "started" events
        | _ -> fail "dispatch must be expected before start"
        outputDispatch "started" token (coverage state)

    let private rosterFingerprint threadId turnId sequence provenance =
        let fields = jsonArray [ node threadId; node turnId; node sequence; node provenance ]
        fields.ToJsonString(compact) |> utf8.GetBytes |> sha256

    let private rosterOperationHash (entries: JsonObject list) =
        let rows =
            entries
            |> List.map (fun entry ->
                let hash = requiredString "hash" entry
                let turnId = requiredString "turnId" entry
                let sequence = requiredInt "turnSequence" entry
                $"{{\"hash\": \"{hash}\", \"turnId\": \"{turnId}\", \"turnSequence\": {sequence}}}")
        "[" + String.concat ", " rows + "]" |> utf8.GetBytes |> sha256

    let private usageFingerprint (state: JsonObject) (inventory: NativeInventory) (turn: NativeTurn) =
        let quoteOption value = value |> Option.map JsonSerializer.Serialize |> Option.defaultValue "null"
        let usage = turn.Usage
        let fields =
            $"[{turn.Sequence}, {quoteOption inventory.Provider}, {quoteOption inventory.ProviderProvenance}, {quoteOption inventory.Model}, {quoteOption inventory.Effort}, \"codex-native-token-usage-record\", {{\"cached_input_tokens\": {usage.CachedInput}, \"input_tokens\": {usage.Input}, \"output_tokens\": {usage.Output}, \"reasoning_output_tokens\": {usage.Reasoning}, \"total_tokens\": {usage.Total}}}]"
        fields |> utf8.GetBytes |> sha256

    let private finishRosterIntent config (state: JsonObject) =
        match state["rosterIntent"] with
        | null -> ()
        | :? JsonObject as intent ->
            let published = if state.ContainsKey "turnRosterPublishedCount" then requiredInt "turnRosterPublishedCount" state else 0
            let roster =
                match state["expectedTurnRoster"] with
                | :? JsonArray as values -> values |> Seq.choose stringValue |> Seq.toList
                | _ -> fail "native turn roster intent is malformed"
            let baseline =
                match state["baselineTurnIds"] with
                | :? JsonArray as values -> values |> Seq.choose stringValue |> Seq.toList
                | null -> []
                | _ -> fail "native turn roster intent is malformed"
            let entries =
                match intent["entries"] with
                | :? JsonArray as values -> values |> Seq.toList
                | _ -> fail "native turn roster intent is malformed"
            if published < 0 || requiredInt "offset" intent <> published ||
               optionalString "expectedTurnRosterProvenance" state <> Some "codex-app-server-thread-turns-list" then
                fail "native turn roster intent is malformed"
            let threadId = requiredString "nativeThreadId" state
            for index, entry in entries |> List.indexed do
                match entry with
                | :? JsonObject as value ->
                    let position = published + index
                    let sequence = baseline.Length + position + 1
                    let turnId = requiredString "turnId" value
                    if value.Count <> 3 || position >= roster.Length || roster[position] <> turnId ||
                       requiredInt "turnSequence" value <> sequence ||
                       requiredString "hash" value <> rosterFingerprint threadId turnId sequence "codex-app-server-thread-turns-list" then
                        fail "native turn roster intent differs from expected population"
                | _ -> fail "native turn roster intent is malformed"
            state["turnRosterPublishedCount"] <- node (published + entries.Length)
            state.Remove "rosterIntent" |> ignore
            saveState config state
        | _ -> fail "native turn roster intent is malformed"

    let private resumeNativePublication config (state: JsonObject) =
        match state["pendingPublication"] with
        | :? JsonObject as pending ->
            let operation = requiredString "operation" pending
            try publishPending config state
            with error ->
                if not (state.ContainsKey "pendingPublication") && operation.StartsWith("native-roster:", StringComparison.Ordinal) then
                    state.Remove "rosterIntent" |> ignore
                    saveState config state
                raise error
            if operation = "native-thread" then state["nativeThreadPublished"] <- node true
            elif operation = "native-inventory-authority" then
                state["nativeInventoryPublished"] <- node true
                match state["nativeInventoryIntegration"] with
                | :? JsonObject as value -> value["status"] <- node "published"
                | _ -> ()
            elif operation.StartsWith("native-roster:", StringComparison.Ordinal) then finishRosterIntent config state
            saveState config state
        | null -> ()
        | _ -> fail "telemetry publication intent is malformed"

    let private reconcileUsage config (state: JsonObject) =
        if requiredString "phase" state <> "terminal" || optionalString "hostParentThreadId" state |> Option.isNone then
            "native-collaboration-usage-unsupported"
        elif (if state.ContainsKey "usageBaselineKnown" then not (boolValue "usageBaselineKnown" state)
              else requiredString "relation" state = "follow-up") then
            "native-collaboration-usage-unknown"
        else
            resumeNativePublication config state
            if state.ContainsKey "rosterIntent" then finishRosterIntent config state
            match state["usageIntent"] with
            | :? JsonObject as intent ->
                if state.ContainsKey "pendingPublication" then publishPending config state
                let ledger =
                    match state["usageLedger"] with
                    | :? JsonObject as value -> value
                    | null -> let value = JsonObject() in state["usageLedger"] <- value; value
                    | _ -> fail "native usage ledger is malformed"
                ledger[requiredString "turnId" intent] <- jsonObject [
                    "revision", node (requiredInt "revision" intent)
                    "hash", node (requiredString "hash" intent) ]
                state.Remove "usageIntent" |> ignore
                saveState config state
            | null -> ()
            | _ -> fail "native usage intent is malformed"
            let parentThread = Guid.Parse(requiredString "hostParentThreadId" state)
            match NativeUsage.collect parentThread (requiredString "nativeId" state) (requiredString "rootInvocationId" state) (requiredString "invocationId" state) 0 with
            | Error _ -> "native-collaboration-usage-unknown"
            | Ok inventory ->
                let allIds = inventory.AllTurnIds |> List.map string
                let baseline =
                    match state["baselineTurnIds"] with
                    | :? JsonArray as values -> values |> Seq.choose stringValue |> Seq.toList
                    | null -> []
                    | _ -> fail "native usage baseline is malformed"
                if baseline.Length <> (Set.ofList baseline).Count ||
                   (allIds |> List.truncate baseline.Length) <> baseline ||
                   (optionalString "baselineThreadId" state |> Option.exists ((<>) (string inventory.ThreadId))) then
                    "native-collaboration-usage-unknown"
                else
                    let eligible = allIds |> List.skip baseline.Length
                    let oldRoster =
                        match state["expectedTurnRoster"] with
                        | :? JsonArray as values -> values |> Seq.choose stringValue |> Seq.toList
                        | null -> []
                        | _ -> fail "native expected turn roster is malformed"
                    if (eligible |> List.truncate oldRoster.Length) <> oldRoster then
                        fail "native expected turn roster changed or reordered"
                    match optionalString "nativeThreadId" state with
                    | Some existing when existing <> string inventory.ThreadId -> fail "native child thread changed for a dispatch"
                    | _ -> ()
                    if not (state.ContainsKey "nativeThreadPublished") || not (boolValue "nativeThreadPublished" state) then
                        state["nativeThreadId"] <- node (string inventory.ThreadId)
                        saveState config state
                        let threadEvent =
                            event "runtime-start" ("runtime-thread-" + requiredString "invocationId" state)
                                (Some(requiredString "itemId" state)) [
                                "invocationId", state["invocationId"].DeepClone(); "threadId", node (string inventory.ThreadId)
                                "turnId", nullNode; "turnSequence", nullNode; "processId", node 0; "phase", node "thread" ]
                        publish config state "native-thread" "terminal" [ threadEvent ]
                        state["nativeThreadPublished"] <- node true
                        saveState config state
                    let publishedAuthority = state.ContainsKey "nativeInventoryPublished" && boolValue "nativeInventoryPublished" state
                    if publishedAuthority then
                        if eligible <> oldRoster then fail "published native inventory roster cannot change"
                    else
                        let binding = JsonNode.Parse(inventory.SourceBinding.GetRawText()) :?> JsonObject
                        let bindingBytes = Convert.FromBase64String(requiredString "bytesBase64" binding)
                        let body = JsonNode.Parse(bindingBytes) :?> JsonObject
                        let boundIds =
                            match body["orderedTurnIds"] with
                            | :? JsonArray as values -> values |> Seq.choose stringValue |> Seq.toList
                            | _ -> fail "native source binding is malformed"
                        if requiredString "rootInvocationId" body <> requiredString "rootInvocationId" state ||
                           requiredString "invocationId" body <> requiredString "invocationId" state ||
                           requiredString "parentThreadId" body <> requiredString "hostParentThreadId" state ||
                           requiredString "threadId" body <> string inventory.ThreadId || boundIds <> allIds ||
                           requiredInt "revision" body <> 0 || requiredString "sha256" binding <> sha256 bindingBytes then
                            fail "native source binding differs from durable dispatch identity"
                        state["nativeThreadId"] <- node (string inventory.ThreadId)
                        state["expectedTurnRoster"] <- jsonArray (eligible |> Seq.map node)
                        state["expectedTurnRosterProvenance"] <- node "codex-app-server-thread-turns-list"
                        state["nativeInventoryHostSource"] <- node "codex-app-server:thread/turns/list"
                        state["nativeInventoryPaging"] <- JsonNode.Parse(inventory.InventoryPaging.GetRawText())
                        state["nativeInventoryCapturedAt"] <- node inventory.InventoryCapturedAt
                        state["nativeInventoryRosterDigest"] <- node inventory.RosterDigest
                        state["nativeInventorySourceDigest"] <- node inventory.SourceDigest
                        state["nativeInventoryProducerStream"] <- state["producerStream"].DeepClone()
                        state["nativeInventoryBindingDigest"] <- node (requiredString "sha256" binding)
                        state["nativeInventorySourceBinding"] <- binding.DeepClone()
                        state["nativeInventoryCollectorProducer"] <- node "fsgg-work-roadmap-native-collector/1"
                        state["nativeInventoryAppServerResponses"] <- JsonNode.Parse(inventory.AppServerResponses.GetRawText())
                        state["nativeInventoryRolloutRecords"] <- JsonNode.Parse(inventory.RolloutRecords.GetRawText())
                        let provider = inventory.Provider
                        let provenance = inventory.ProviderProvenance
                        if optionalString "nativeProvider" state |> Option.exists (fun old -> Some old <> provider || optionalString "nativeProviderProvenance" state <> provenance) then
                            fail "native provider observation changed for a thread"
                        state["nativeProvider"] <- provider |> Option.map node |> Option.defaultValue nullNode
                        state["nativeProviderProvenance"] <- provenance |> Option.map node |> Option.defaultValue nullNode
                        let pages = inventory.InventoryPaging.GetArrayLength()
                        let factReady = provider.IsSome && inventory.Model = Some(requiredString "model" state) &&
                                        inventory.Effort = Some(requiredString "effort" state) && pages = 1
                        let inventoryId = digest "native-inventory-" [ requiredString "invocationId" state ]
                        let fact =
                            event "runtime-native-inventory/1" (digest "runtime-native-inventory-" [ requiredString "invocationId" state ])
                                (Some(requiredString "itemId" state)) [
                                "inventoryId", node inventoryId; "originalItemId", state["originalItemId"].DeepClone()
                                "invocationId", state["invocationId"].DeepClone(); "page", node 1; "pages", node 1
                                "expectedTurnIds", jsonArray (eligible |> Seq.map node)
                                "expectedProvider", (provider |> Option.map node |> Option.defaultValue nullNode)
                                "requestedModel", state["model"].DeepClone(); "requestedEffort", state["effort"].DeepClone()
                                "support", node "provider-native-final-turn-counters"; "followupBaseline", node baseline.Length
                                "capturedAt", node inventory.InventoryCapturedAt
                                "sourceKind", node "provider-capability-and-dispatch-roster"; "sourceDigest", node inventory.SourceDigest ]
                        let sourceFact =
                            event "runtime-native-inventory-source/1" (digest "runtime-native-inventory-source-" [ requiredString "invocationId" state ])
                                (Some(requiredString "itemId" state)) [
                                "inventoryId", node inventoryId; "originalItemId", state["originalItemId"].DeepClone()
                                "invocationId", state["invocationId"].DeepClone(); "sourceDigest", node inventory.SourceDigest
                                "sourceBinding", binding.DeepClone() ]
                        state["nativeInventoryIntegration"] <- jsonObject [
                            "status", node (if factReady then "ready" else "incomplete-source-provenance")
                            "reason", (if factReady then nullNode else node "provider/profile provenance or a stable single-page inventory is unavailable")
                            "collectorProducer", node "fsgg-work-roadmap-native-collector/1"
                            "sourceDigest", node inventory.SourceDigest; "sourceBinding", binding.DeepClone()
                            "fact", (if factReady then fact.DeepClone() else nullNode)
                            "sourceFact", (if factReady then sourceFact.DeepClone() else nullNode) ]
                        saveState config state
                        if factReady then
                            publish config state "native-inventory-authority" "terminal" [ fact; sourceFact ]
                            state["nativeInventoryPublished"] <- node true
                            (state["nativeInventoryIntegration"] :?> JsonObject)["status"] <- node "published"
                            saveState config state
                    let mutable published =
                        if state.ContainsKey "turnRosterPublishedCount" then requiredInt "turnRosterPublishedCount" state else 0
                    if published < 0 || published > eligible.Length then fail "native turn roster publication cursor is malformed"
                    if not (state.ContainsKey "turnRosterPublishedCount") then
                        state["turnRosterPublishedCount"] <- node 0
                        saveState config state
                    while published < eligible.Length do
                        let chunk = eligible |> List.skip published |> List.truncate 64
                        let entries =
                            chunk |> List.mapi (fun index turnId ->
                                let sequence = baseline.Length + published + index + 1
                                jsonObject [ "turnId", node turnId; "turnSequence", node sequence
                                             "hash", node (rosterFingerprint (string inventory.ThreadId) turnId sequence "codex-app-server-thread-turns-list") ])
                        let events =
                            chunk |> List.mapi (fun index turnId ->
                                let sequence = baseline.Length + published + index + 1
                                event "runtime-start" (digest "runtime-turn-" [ requiredString "invocationId" state; turnId ])
                                    (Some(requiredString "itemId" state)) [
                                    "invocationId", state["invocationId"].DeepClone(); "threadId", node (string inventory.ThreadId)
                                    "turnId", node turnId; "turnSequence", node sequence; "processId", node 0; "phase", node "turn" ])
                        state["rosterIntent"] <- jsonObject [ "offset", node published; "entries", jsonArray (entries |> Seq.map (fun value -> value :> JsonNode)) ]
                        saveState config state
                        publish config state ("native-roster:" + rosterOperationHash entries) "terminal" events
                        finishRosterIntent config state
                        published <- requiredInt "turnRosterPublishedCount" state
                    let ledger =
                        match state["usageLedger"] with
                        | :? JsonObject as value -> value
                        | null -> let value = JsonObject() in state["usageLedger"] <- value; saveState config state; value
                        | _ -> fail "native usage ledger is malformed"
                    let eligibleSet = Set.ofList eligible
                    for turn in inventory.Turns do
                        let turnId = string turn.TurnId
                        if eligibleSet.Contains turnId then
                            let fingerprint = usageFingerprint state inventory turn
                            let previousRevision, previousHash =
                                match ledger[turnId] with
                                | :? JsonObject as previous -> requiredInt "revision" previous, requiredString "hash" previous
                                | _ -> -1, ""
                            if previousHash <> fingerprint then
                                let revision = previousRevision + 1
                                let observation = event "runtime-turn-usage" (digest "runtime-turn-usage-" [ requiredString "invocationId" state; turnId ]) (Some(requiredString "itemId" state)) [
                                    "invocationId", state["invocationId"].DeepClone(); "threadId", node (string inventory.ThreadId); "turnId", node turnId; "turnSequence", node turn.Sequence
                                    "provider", (inventory.Provider |> Option.map node |> Option.defaultValue nullNode)
                                    "requestedModel", state["model"].DeepClone(); "observedModel", (inventory.Model |> Option.map node |> Option.defaultValue nullNode)
                                    "requestedEffort", state["effort"].DeepClone(); "observedEffort", (inventory.Effort |> Option.map node |> Option.defaultValue nullNode)
                                    "backend", node "codex-collaboration"; "scope", node "turn"; "provenance", node "codex-native-token-usage-record"
                                    "input", node turn.Usage.Input; "cachedInput", node turn.Usage.CachedInput; "output", node turn.Usage.Output; "reasoning", node turn.Usage.Reasoning; "total", node turn.Usage.Total ]
                                observation["revision"] <- node revision
                                state["usageIntent"] <- jsonObject [ "turnId", node turnId; "revision", node revision; "hash", node fingerprint ]
                                saveState config state
                                publish config state $"native-usage:{turnId}:{revision}" "terminal" [ observation ]
                                ledger[turnId] <- jsonObject [ "revision", node revision; "hash", node fingerprint ]
                                state.Remove "usageIntent" |> ignore
                                saveState config state
                    match state["nativeInventoryIntegration"] with
                    | :? JsonObject as integration ->
                        integration["locallyReconciled"] <- node (inventory.Complete && not eligible.IsEmpty &&
                            (inventory.Turns |> List.map (fun turn -> string turn.TurnId) |> List.filter eligibleSet.Contains) = eligible &&
                            (ledger |> Seq.map (fun entry -> entry.Key) |> Set.ofSeq) = eligibleSet &&
                            requiredInt "turnRosterPublishedCount" state = eligible.Length)
                        saveState config state
                    | _ -> ()
                    "native-collaboration-usage-unknown"

    let private dashboard config =
        let failed reason = jsonObject [ "status", node "advisory-failure"; "reason", node reason ]
        try
            let completed = execute 60 8192 [ config.Engine; "telemetry"; "dashboard"; "publisher-event"; "--config"; config.Path ]
            if completed.Code <> 0 then failed "publisher-event-subprocess-failed"
            else
                let health = JsonNode.Parse completed.Stdout
                let expected = Set [ "schema"; "status"; "reason"; "observedAt"; "publicRevision"; "commit" ]
                match health with
                | :? JsonObject as value when (value |> Seq.map (fun field -> field.Key) |> Set.ofSeq) = expected &&
                                              optionalString "schema" value = Some "fsgg.telemetry.dashboard-event-health/1" ->
                    jsonObject [ "status", node "observed"; "health", health ]
                | _ -> failed "publisher-event-result-invalid"
        with
        | AdapterError _ -> failed "publisher-event-subprocess-failed"
        | :? IOException -> failed "publisher-event-subprocess-failed"
        | :? UnauthorizedAccessException -> failed "publisher-event-subprocess-failed"
        | :? JsonException -> failed "publisher-event-result-invalid"
        | _ -> failed "publisher-event-subprocess-failed"

    let private finish config token outcome explicitExitCode =
        if not (Set [ "completed"; "failed"; "cancelled"; "blocked" ] |> Set.contains outcome) then fail "outcome is invalid"
        let state = readState config token
        let exitCode = explicitExitCode |> Option.defaultValue (if outcome = "completed" then 0 else 1)
        if exitCode < 0 then fail "exit-code must be non-negative"
        match requiredString "phase" state with
        | "terminal" | "terminal-pending" as phase ->
            if phase = "terminal" && not (state.ContainsKey "exitCode") then
                if explicitExitCode.IsSome then fail "legacy terminal state cannot verify an explicit exit-code retry"
                state["exitCode"] <- node exitCode; saveState config state
            if requiredString "outcome" state <> outcome || requiredInt "exitCode" state <> exitCode then fail "terminal retry differs from the durable terminal intent"
            if phase = "terminal-pending" then publishPending config state
        | "started" ->
            let item, invocation, at = requiredString "itemId" state, requiredString "invocationId" state, timestamp ()
            let events = [
                event "runtime-terminal" ("runtime-terminal-" + invocation) (Some item) [ "invocationId", node invocation; "threadId", state["nativeId"].DeepClone(); "outcome", node outcome; "exitCode", node exitCode ]
                event "event-time" ($"event-time-{invocation}-terminal") (Some item) [ "invocationId", node invocation; "event", node "terminal"; "occurredAt", node at; "occurredClockProvenance", node "host-wall"; "observedAt", node at; "observedClockProvenance", node "host-wall" ] ]
            state["phase"] <- node "terminal-pending"; state["outcome"] <- node outcome; state["exitCode"] <- node exitCode
            publish config state "finish" "terminal" events
        | _ -> fail "dispatch must be started before terminal"
        let usageCoverage = try reconcileUsage config state with AdapterError _ -> "native-collaboration-usage-unknown"
        let drain = execute 30 131072 (drainCommand config)
        let value = jsonObject [ "schema", node "fsgg.telemetry.roadmap-dispatch/1"; "status", node "terminal"; "token", node token; "outcome", node outcome; "coverage", node usageCoverage; "drain", node (if drain.Code = 0 then "complete" else "pending") ]
        if drain.Code = 0 && outcome = "completed" && requiredString "relation" state = "root" then value["dashboardPublication"] <- dashboard config
        value

    let private readContract (input: FileInfo) (schema: string) (fields: Set<string>) =
        if input.LinkTarget <> null || not input.Exists || input.Length > 32768L then fail "private observation input must be a regular file of at most 32768 bytes"
        try
            use document = JsonDocument.Parse(File.ReadAllBytes input.FullName)
            if document.RootElement.ValueKind <> JsonValueKind.Object then fail $"private observation input must have the exact {schema} shape"
            let properties = document.RootElement.EnumerateObject() |> Seq.toList
            let names = properties |> List.map _.Name
            if Set.ofList names <> Set.add "schema" fields || names.Length <> Set.count fields + 1 || document.RootElement.GetProperty("schema").GetString() <> schema then
                fail $"private observation input must have the exact {schema} shape"
            JsonNode.Parse(document.RootElement.GetRawText()) :?> JsonObject
        with :? JsonException -> fail "private observation input is unreadable"

    let private recordEvent config state (value: JsonObject) =
        let kind = requiredString "kind" value
        let identity = requiredString "identity" value
        publish config state $"observation:{kind}:{identity}" (requiredString "phase" state) [ value ]
        let drain = execute 30 131072 (drainCommand config)
        if drain.Code <> 0 then fail (if String.IsNullOrWhiteSpace drain.Stderr then "telemetry observation drain failed" else drain.Stderr.Trim())
        let result = jsonObject [ "schema", node "fsgg.telemetry.roadmap-observation/1"; "status", node "recorded"; "kind", value["kind"].DeepClone() ]
        if requiredString "phase" state = "terminal" && requiredString "relation" state = "root" then result["dashboardPublication"] <- dashboard config
        result

    let private observation config token input kind =
        let state = readState config token
        let phase = requiredString "phase" state
        let schema, fields, allowed =
            match kind with
            | "review:attempt" | "review:item" -> "fsgg.telemetry.process-review-input/1", Set [ "revision"; "outcomeSynopsis"; "wentWell"; "problems"; "avoidableDelayOrRework"; "processObservations"; "remainingRisks"; "concreteImprovements"; "evidence"; "evidenceCoverage"; "populationCoverage"; "confidence"; "reviewerModel"; "reviewerEffort"; "reviewedAt"; "durationSeconds" ], phase = "terminal"
            | "activity" -> "fsgg.telemetry.activity-span-input/1", Set [ "revision"; "activityId"; "category"; "startedAt"; "endedAt"; "clockProvenance"; "evidence"; "summary" ], Set [ "started"; "terminal" ] |> Set.contains phase
            | "usage" -> "fsgg.telemetry.activity-usage-attribution-input/1", Set [ "revision"; "usageIdentity"; "activityId"; "classification"; "input"; "cachedInput"; "output"; "reasoning"; "total" ], phase = "terminal"
            | _ -> "fsgg.telemetry.complication-input/1", Set [ "revision"; "complicationId"; "activityId"; "trigger"; "cause"; "occurredAt"; "synopsis"; "evidence" ], Set [ "started"; "terminal" ] |> Set.contains phase
        if not allowed then fail (if kind.StartsWith("review") then "process review requires a terminal attempt" elif kind = "usage" then "usage attribution requires a terminal attempt" elif kind = "activity" then "activity span requires a started attempt" else "complication requires a started attempt")
        if kind = "review:item" && requiredString "relation" state <> "root" then fail "item process review requires the root dispatch token"
        let value = readContract input schema fields
        let get (name: string) = value[name].DeepClone()
        let item = requiredString "itemId" state
        let identity, outputKind, extras =
            if kind.StartsWith("review") then
                let scope = kind.Substring 7
                let subject = if scope = "attempt" then requiredString "attemptId" state else item
                digest "process-review-" [ scope; item; subject ], "process-review", [ "scope", node scope; "attemptId", (if scope = "attempt" then state["attemptId"].DeepClone() else nullNode) ]
            elif kind = "activity" then
                let id = validateIdentity "activity" false (Some(value["activityId"].GetValue<string>())) |> Option.get
                digest "activity-span-" [ item; id ], "activity-span", [ "invocationId", state["invocationId"].DeepClone(); "attemptId", state["attemptId"].DeepClone() ]
            elif kind = "usage" then
                let id = validateIdentity "usage identity" false (Some(value["usageIdentity"].GetValue<string>())) |> Option.get
                digest "activity-usage-" [ item; id ], "activity-usage-attribution", []
            else
                let id = validateIdentity "complication" false (Some(value["complicationId"].GetValue<string>())) |> Option.get
                digest "complication-" [ item; id ], "complication", [ ("attemptId", state["attemptId"].DeepClone()) ]
        let observation = jsonObject ([ "kind", node outputKind; "identity", node identity; "itemId", node item ] @ extras)
        fields |> Set.iter (fun name -> if not (kind = "complication" && name = "complicationId") then observation[name] <- get name)
        recordEvent config state observation

    let private populationOnly config feature item original producer =
        let feature = validateIdentity "feature" false (Some feature) |> Option.get
        let item = validateIdentity "item" false (Some item) |> Option.get
        let original = validateIdentity "original item" false (Some original) |> Option.get
        let producer = validateIdentity "producer" false (Some producer) |> Option.get
        if not config.Workspace then fail "population-only requires a receipt-scoped workspace"
        if item = original then fail "population-only requires a distinct protected original item"
        let token = digest "" [ "population-only"; feature; item ]
        let path = Path.Combine(stateDirectory config originalStateSchema, token + ".json")
        let expected = jsonObject [ "schema", node originalStateSchema; "token", node token; "featureId", node feature; "itemId", node item; "originalItemId", node original; "producerStream", node producer; "associationProducer", (config.Producer |> Option.map node |> Option.defaultValue nullNode); "associationDigest", (config.BindingDigest |> Option.map node |> Option.defaultValue nullNode) ]
        let existing = FileInfo path
        if existing.Exists || existing.LinkTarget <> null then
            let state = readObject path "original binding state is unavailable"
            expected |> Seq.iter (fun pair -> if isNull state[pair.Key] || state[pair.Key].ToJsonString(compact) <> pair.Value.ToJsonString(compact) then fail "original binding retry differs from its protected identity")
            if requiredString "invocationId" state <> "original-binding-" + token || not (assignmentDigestPattern.IsMatch(requiredString "originalAssignmentDigest" state)) || requiredInt "sequence" state <> 1 then fail "original binding state is malformed"
            match requiredString "phase" state with
            | "applied" when not (state.ContainsKey "pendingPublication") -> authorizedOriginal feature item original |> ignore
            | "pending" -> publishPending config state
            | _ -> fail "original binding state is malformed"
        else
            let assignmentDigest = authorizedOriginal feature item original
            let state = expected.DeepClone() :?> JsonObject
            state["originalAssignmentDigest"] <- node assignmentDigest; state["phase"] <- node "pending"; state["sequence"] <- node 0; state["invocationId"] <- node ("original-binding-" + token)
            let key = digest "" [ item; original ]
            let events = [ event "feature" feature None [ ("name", node feature) ]; event "item" (itemFactIdentity feature item) (Some item) [ ("featureId", node feature) ]; event "budget-population" ("budget-population-" + key) (Some item) [ "originalItemId", node original; "state", node "open"; "sourceKind", node "native-item"; "sourceRef", node ("roadmap-dispatch:" + key) ] ]
            publish config state "population-only" "applied" events
        jsonObject [ "schema", node "fsgg.telemetry.original-binding-result/1"; "status", node "applied" ]

    let private configured (config: HostConfig) command =
        match command with
        | Begin(feature, item, original, attempt, parentAttempt, parentToken, relation, producer, model, effort, lateAfter) -> beginDispatch config feature item original attempt parentAttempt parentToken relation producer model effort lateAfter
        | PopulationOnly(feature, item, original, producer) -> populationOnly config feature item original producer
        | Started(token, nativeId) -> started config token nativeId
        | Finish(token, outcome, exitCode) -> finish config token outcome exitCode
        | UsageReconcile token ->
            let state = readState config token
            let usageCoverage = reconcileUsage config state
            let drain = execute 30 131072 (drainCommand config)
            jsonObject [ "schema", node "fsgg.telemetry.roadmap-usage/1"; "status", node "reconciled"; "token", node token; "coverage", node usageCoverage; "drain", node (if drain.Code = 0 then "complete" else "pending") ]
        | CiAssignment(feature, item, attempt, parent, producer) ->
            match Configuration.createCiAssignment config feature item attempt parent producer with
            | Ok path -> jsonObject [ "schema", node "fsgg.telemetry.assignment-result/1"; "status", node "ready"; "assignment", node path ]
            | Error error -> fail error.Message
        | Review(token, scope, input) when scope = "attempt" || scope = "item" -> observation config token input ("review:" + scope)
        | Review _ -> fail "review scope must be attempt or item"
        | Activity(token, input) -> observation config token input "activity"
        | UsageAttribution(token, input) -> observation config token input "usage"
        | Complication(token, input) -> observation config token input "complication"
        | Status ->
            let command =
                if config.Workspace then [ config.Engine; "telemetry"; "workspace"; "status"; "--config"; config.Path; "--repository"; config.Repository |> Option.map Configuration.repositoryValue |> Option.defaultValue "" ]
                else [ config.Engine; "telemetry"; "store"; "status"; "--store-root"; config.StoreRoot ]
            let completed = execute 20 131072 command
            jsonObject [ "schema", node "fsgg.telemetry.host-status/1"; "status", node (if completed.Code = 0 then "ready" else "unavailable") ]

    let run config command =
        match config with
        | None -> result 2 "{\"schema\":\"fsgg.telemetry.host-status/1\",\"status\":\"not-configured\"}\n" ""
        | Some config ->
            try
                let value = configured config command
                let exitCode =
                    match command with
                    | Status when value["status"].GetValue<string>() = "unavailable" -> 1
                    | _ -> 0
                let completed = success value
                { completed with ExitCode = exitCode }
            with
            | AdapterError message -> result 1 "" $"fsgg roadmap telemetry: {message}\n"
            | :? IOException as error -> result 1 "" $"fsgg roadmap telemetry: {error.Message}\n"
            | :? UnauthorizedAccessException as error -> result 1 "" $"fsgg roadmap telemetry: {error.Message}\n"
            | :? JsonException as error -> result 1 "" $"fsgg roadmap telemetry: {error.Message}\n"
            | error -> result 1 "" $"fsgg roadmap telemetry: {error.Message}\n"
