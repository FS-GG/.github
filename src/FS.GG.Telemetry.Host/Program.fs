namespace FS.GG.Telemetry.Host

open System
open System.Buffers.Binary
open System.Collections.Generic
open System.IO
open System.Runtime.InteropServices
open System.Security.Cryptography
open System.Text
open System.Text.Json
open System.Text.Json.Nodes
open System.Threading.Tasks
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Http
open Microsoft.Extensions.Hosting
open Microsoft.AspNetCore.Hosting
open Microsoft.Extensions.Configuration
open Microsoft.Extensions.Logging
open FS.GG.Coord
open FS.GG.Coord.Cli
open FS.GG.Coord.GitHub
open FS.GG.Telemetry.Dashboard

module private BrowserComposition =
    let options config =
        {
            PublicOrigin = Uri config.ListenUrl
            IdleLifetime = TimeSpan.FromSeconds(float config.BrowserSession.IdleSeconds)
            AbsoluteLifetime = TimeSpan.FromSeconds(float config.BrowserSession.AbsoluteSeconds)
            MaximumSessions = config.BrowserSession.MaximumSessions
            LoginAttemptsPerMinute = config.BrowserSession.LoginAttemptsPerMinute
            LoginAdmission = config.BrowserSession.LoginAdmission
            QueryAdmission = config.BrowserSession.QueryAdmission
            QueryTimeout = TimeSpan.FromSeconds(float config.BrowserSession.QueryTimeoutSeconds)
        }

    let snapshot config workspace itemId =
        match config.Stores |> Array.tryFind (fun store -> store.WorkspaceId = workspace) with
        | None -> Error [ "projection-unavailable" ]
        | Some store ->
            match
                TelemetryStoreApplication.scopedDashboardSnapshot
                    store.Root
                    (TelemetryStoreApplication.assessProductionRoot store.Root)
                    workspace
                    itemId
            with
            | Error _ -> Error [ "projection-unavailable" ]
            | Ok envelope ->
                match DashboardProjection.project workspace (Encoding.UTF8.GetBytes envelope) with
                | Ok bytes -> Ok bytes
                | Error _ -> Error [ "projection-unavailable" ]

module Hosting =
    let createBuilder (listenUrl: string) certificate =
        let builder = WebApplication.CreateBuilder(WebApplicationOptions(Args = [||]))
        builder.Configuration.Sources.Clear()
        builder.Configuration.AddInMemoryCollection() |> ignore
        builder.Logging.ClearProviders() |> ignore
        builder.WebHost.UseUrls(listenUrl) |> ignore

        builder.WebHost.ConfigureKestrel(fun options ->
            options.ConfigureHttpsDefaults(fun https -> https.ServerCertificate <- certificate))
        |> ignore

        builder

module Endpoints =
    let private unauthorized (context: HttpContext) =
        task {
            context.Response.StatusCode <- 401
            do! context.Response.Body.WriteAsync(FS.GG.Telemetry.RemoteContract.writeError "unauthorized-scope")
        }

    let configure (app: WebApplication) (state: Runtime.HostState) credentials =
        let auth (context: HttpContext) =
            let authorization = string context.Request.Headers.Authorization

            if authorization.StartsWith("Bearer ", StringComparison.Ordinal) then
                Runtime.authenticate credentials (authorization.Substring 7)
            else
                None

        app.MapGet(
            "/private/health",
            Func<HttpContext, Task>(fun context ->
                task {
                    match auth context with
                    | Some _ ->
                        context.Response.StatusCode <- if state.Ready then 200 else 503

                        do!
                            context.Response.WriteAsJsonAsync(
                                {|
                                    status = if state.Ready then "ready" else "recovering"
                                |}
                            )
                    | None -> do! unauthorized context
                })
        )
        |> ignore

        app.MapPost(
            "/v1/batches",
            Func<HttpContext, Task>(fun context ->
                task {
                    match auth context with
                    | None -> do! unauthorized context
                    | Some principal ->
                        let scope = principal.Scope
                        if not (state.TryAcquireSlot()) then
                            context.Response.StatusCode <- 429
                            do! context.Response.Body.WriteAsync(FS.GG.Telemetry.RemoteContract.writeError "overload")
                        else
                            let mutable transferred = false

                            try
                                try
                                    if
                                        context.Request.ContentLength.HasValue
                                        && context.Request.ContentLength.Value >
                                            int64 FS.GG.Coord.TelemetryReceipt.MaxEnvelopeBytes
                                    then
                                        context.Response.StatusCode <- 413

                                        do!
                                            context.Response.Body.WriteAsync(
                                                FS.GG.Telemetry.RemoteContract.writeError "oversized-batch"
                                            )
                                    else
                                        use deadline =
                                            Threading.CancellationTokenSource.CreateLinkedTokenSource(
                                                context.RequestAborted
                                            )

                                        deadline.CancelAfter(TimeSpan.FromSeconds 10.)
                                        use stream = new MemoryStream()
                                        let buffer = Array.zeroCreate<byte> 8192
                                        let mutable total = 0
                                        let mutable more = true

                                        while more && total <= FS.GG.Coord.TelemetryReceipt.MaxEnvelopeBytes do
                                            let! count = context.Request.Body.ReadAsync(buffer, deadline.Token) in

                                            if count = 0 then
                                                more <- false
                                            else
                                                stream.Write(buffer, 0, count)
                                                total <- total + count

                                        if total > FS.GG.Coord.TelemetryReceipt.MaxEnvelopeBytes then
                                            context.Response.StatusCode <- 413

                                            do!
                                                context.Response.Body.WriteAsync(
                                                    FS.GG.Telemetry.RemoteContract.writeError "oversized-batch"
                                                )
                                        else
                                            transferred <- true

                                            let! reply =
                                                Runtime.submitPrincipalAcquired state principal (stream.ToArray()) deadline.Token

                                            context.Response.StatusCode <- reply.Status
                                            context.Response.ContentType <- "application/json"
                                            do! context.Response.Body.WriteAsync(reply.Body)
                                with :? OperationCanceledException when
                                    not transferred && not context.Response.HasStarted ->
                                    context.Response.StatusCode <- 408
                            finally
                                if not transferred then
                                    state.ReleaseSlot()
                })
        )
        |> ignore

        app.MapGet(
            "/v1/receipts/{batch}",
            Func<HttpContext, string, Task>(fun context batch ->
                task {
                    match auth context with
                    | None -> do! unauthorized context
                    | Some principal ->
                        if not (state.TryAcquireSlot()) then
                            context.Response.StatusCode <- 429
                            do! context.Response.Body.WriteAsync(FS.GG.Telemetry.RemoteContract.writeError "overload")
                        else
                            // From this point the actor completion owns the admission slot, even if the client disconnects.
                            let! reply = Runtime.lookupAcquired state principal.Scope batch context.RequestAborted
                            context.Response.StatusCode <- reply.Status
                            context.Response.ContentType <- "application/json"
                            do! context.Response.Body.WriteAsync(reply.Body)
                })
        )
        |> ignore

module Operations =
    module private Native =
        [<DllImport("libc", EntryPoint = "open", SetLastError = true)>]
        extern int openDirectory(string path, int flags)

        [<DllImport("libc", SetLastError = true)>]
        extern int fsync(int descriptor)

        [<DllImport("libc", SetLastError = true)>]
        extern int close(int descriptor)

    let private syncDirectory path =
        let descriptor = Native.openDirectory (path, 0x10000 ||| 0x80000)

        if descriptor < 0 then
            raise (IOException "directory sync unavailable")

        try
            if Native.fsync descriptor <> 0 then
                raise (IOException "directory sync unavailable")
        finally
            Native.close descriptor |> ignore

    let private digestBytes (bytes: byte array) =
        Convert.ToHexString(SHA256.HashData bytes).ToLowerInvariant()

    let private digestFile path =
        use stream = File.OpenRead path in Convert.ToHexString(SHA256.HashData stream).ToLowerInvariant()

    let private flushFile path =
        use stream =
            new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.Read, 4096, FileOptions.WriteThrough) in

        stream.Flush true

    let private isLowerSha256 (value: string) =
        not (isNull value)
        && value.Length = 64
        && value
           |> Seq.forall (fun c -> Char.IsAsciiHexDigit c && not (Char.IsLetter c && Char.IsUpper c))

    let private hasSymlinkAncestor path =
        let mutable current = Path.GetFullPath path
        let mutable found = false

        while not found && not (String.IsNullOrEmpty current) do
            if Directory.Exists current then
                found <- not (isNull (DirectoryInfo(current).LinkTarget))
            elif File.Exists current then
                found <- not (isNull (FileInfo(current).LinkTarget))

            let parent = Path.GetDirectoryName current
            current <- if parent = current then null else parent

        found

    let private configMetadataDigest (config: HostConfig) =
        // Bind logical enrollment and authorization without binding machine-specific store paths.
        // A restore is always published below a new state root and activated through a reviewed config.
        JsonSerializer.SerializeToUtf8Bytes
            {|
                schema = config.Schema
                listenUrl = config.ListenUrl
                workspaces = config.Stores |> Array.map _.WorkspaceId
                credentials =
                    config.Credentials
                    |> Array.map (fun x ->
                        {|
                            reference = x.Reference
                            workspaceId = x.WorkspaceId
                            producerId = x.ProducerId
                            streamId = x.StreamId
                            revoked = x.Revoked
                        |})
                browserPrincipals =
                    config.BrowserPrincipals
                    |> Array.map (fun x ->
                        {|
                            principalId = x.PrincipalId
                            workspaceIds = x.WorkspaceIds
                            revoked = x.Revoked
                        |})
                browserSession = config.BrowserSession
            |}
        |> digestBytes

    let private freeSpaceFor path =
        try
            let mutable candidate = Path.GetFullPath path

            while not (Directory.Exists candidate) && not (String.IsNullOrEmpty candidate) do
                candidate <- Path.GetDirectoryName candidate

            DriveInfo.GetDrives()
            |> Array.filter (fun drive ->
                candidate.StartsWith(Path.GetFullPath drive.RootDirectory.FullName, StringComparison.Ordinal))
            |> Array.sortByDescending (fun drive -> drive.RootDirectory.FullName.Length)
            |> Array.tryHead
            |> Option.filter _.IsReady
            |> Option.map _.AvailableFreeSpace
        with _ ->
            None

    let private writeError code errors =
        let body =
            JsonSerializer.Serialize
                {|
                    schema = "fsgg.telemetry.host-error/1"
                    code = code
                    errors = errors |> List.map (fun _ -> code) |> List.toArray
                |}

        Console.Error.WriteLine body

    let private resultExit fallback (result: Result<string, string list>) =
        match result with
        | Ok(json: string) ->
            Console.Out.Write json
            0
        | Error errors ->
            writeError fallback errors

            if fallback = "backup-integrity-failed" then 5
            elif fallback = "invalid-configuration" then 2
            else 3

    let private load path = Configuration.load path

    let private withLock config action =
        match Runtime.ServiceLock.Acquire config.ServiceLockPath with
        | Error _ ->
            writeError "service-lock-unavailable" [ "locked" ]
            4
        | Ok serviceLock -> use serviceLock = serviceLock in action ()

    let private storeFor (config: HostConfig) workspace =
        config.Stores |> Array.tryFind (fun store -> store.WorkspaceId = workspace)

    let private readPrivateEvidence target =
        let info = FileInfo target
        if not info.Exists || not (isNull info.LinkTarget) || info.Length > 2097152L
           || File.GetUnixFileMode target <> (UnixFileMode.UserRead ||| UnixFileMode.UserWrite) then
            invalidOp "native collector replay artifact is invalid"
        File.ReadAllBytes target

    let private writePrivateAtomic root name (bytes: byte array) =
        let target = Path.Combine(root, name)
        if File.Exists target then
            readPrivateEvidence target
        else
            if bytes.Length > 2097152 then invalidOp "native collector evidence exceeds the bound"
            let temporary = Path.Combine(root, "." + Guid.NewGuid().ToString("N") + ".tmp")

            try
                use stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None)
                File.SetUnixFileMode(temporary, UnixFileMode.UserRead ||| UnixFileMode.UserWrite)
                stream.Write bytes
                stream.Flush true
                stream.Close()
                File.Move(temporary, target, false)
                syncDirectory root
                bytes
            finally
                if File.Exists temporary then
                    File.Delete temporary

    let private framedDigest (chunks: byte array seq) =
        use hash = IncrementalHash.CreateHash HashAlgorithmName.SHA256

        for chunk in chunks do
            let length = Array.zeroCreate<byte> 8
            BinaryPrimitives.WriteInt64BigEndian(length, int64 chunk.Length)
            hash.AppendData length
            hash.AppendData chunk

        hash.GetHashAndReset() |> Convert.ToHexString |> _.ToLowerInvariant()

    let private exactNames (value: JsonNode) expected =
        let objectValue = value.AsObject()
        let names = objectValue |> Seq.map _.Key |> Seq.toArray
        names.Length = Set.count expected
        && Array.distinct names |> Array.length = names.Length
        && Set.ofArray names = expected

    let private retainedBytes (value: JsonNode) (digestName: string) (bytesName: string) =
        let bytes = Convert.FromBase64String(value[bytesName].GetValue<string>())
        if digestBytes bytes <> value[digestName].GetValue<string>() then
            invalidOp "native collector retained source digest differs"
        bytes

    let private validateRetainedNativeSource (capture: JsonObject) =
        let binding = capture["sourceBinding"]
        if not (exactNames binding (set [ "schema"; "producerIdentity"; "sha256"; "bytesBase64" ]))
           || binding["schema"].GetValue<string>() <> "fsgg.telemetry.native-inventory-source-binding/1"
           || binding["producerIdentity"].GetValue<string>() <> "fsgg-work-roadmap-native-collector/1" then
            invalidOp "native collector retained source binding is invalid"

        let chunks = ResizeArray<byte array>()
        chunks.Add(retainedBytes binding "sha256" "bytesBase64")

        let exchanges = capture["appServerResponses"].AsArray()
        if exchanges.Count = 0 || exchanges.Count > 256 then
            invalidOp "native collector retained App Server evidence is invalid"

        for exchange in exchanges do
            if not (exactNames exchange (set [ "method"; "threadId"; "requestCursor"; "requestSha256";
                                                 "requestBytesBase64"; "responseSha256"; "responseBytesBase64" ])) then
                invalidOp "native collector retained App Server evidence is invalid"
            chunks.Add(retainedBytes exchange "requestSha256" "requestBytesBase64")
            chunks.Add(retainedBytes exchange "responseSha256" "responseBytesBase64")

        let finalTurnTotals = Dictionary<struct (string * string), struct (int64 * int64 * int64 * int64 * int64)>()
        let rolloutRecords = capture["rolloutRecords"].AsArray()
        if rolloutRecords.Count = 0 || rolloutRecords.Count > 1000 then
            invalidOp "native collector retained rollout evidence is invalid"

        for record in rolloutRecords do
            if not (exactNames record (set [ "sha256"; "bytesBase64" ])) then
                invalidOp "native collector retained rollout evidence is invalid"
            let bytes = retainedBytes record "sha256" "bytesBase64"
            chunks.Add bytes
            use document = JsonDocument.Parse bytes
            let root = document.RootElement
            if root.GetProperty("type").GetString() <> "token_usage_record" then
                invalidOp "native collector retained rollout record is invalid"
            let payload = root.GetProperty("payload")
            let threadId = payload.GetProperty("thread_id").GetString()
            let turnId = payload.GetProperty("turn_id").GetString()
            let totals = payload.GetProperty("turn_token_usage")
            let counters =
                struct (
                    totals.GetProperty("input_tokens").GetInt64(),
                    totals.GetProperty("cached_input_tokens").GetInt64(),
                    totals.GetProperty("output_tokens").GetInt64(),
                    totals.GetProperty("reasoning_output_tokens").GetInt64(),
                    totals.GetProperty("total_tokens").GetInt64()
                )
            let struct (input, cached, output, reasoning, total) = counters
            if String.IsNullOrWhiteSpace threadId || String.IsNullOrWhiteSpace turnId
               || input < 0L || cached < 0L || output < 0L || reasoning < 0L
               || cached > input || reasoning > output || total <> input + output then
                invalidOp "native collector retained rollout counters are invalid"
            finalTurnTotals[struct (threadId, turnId)] <- counters

        let turns = capture["turns"].AsArray()
        if turns.Count = 0 || turns.Count <> finalTurnTotals.Count then
            invalidOp "native collector retained turn projection is incomplete"
        let seen = HashSet<struct (string * string)>()
        for turn in turns do
            let key = struct (turn["threadId"].GetValue<string>(), turn["turnId"].GetValue<string>())
            let expected =
                match finalTurnTotals.TryGetValue key with
                | true, value -> value
                | _ -> invalidOp "native collector retained turn projection differs from rollout evidence"
            let actual =
                struct (turn["input"].GetValue<int64>(), turn["cachedInput"].GetValue<int64>(),
                        turn["output"].GetValue<int64>(), turn["reasoning"].GetValue<int64>(),
                        turn["total"].GetValue<int64>())
            if not (seen.Add key) || actual <> expected then
                invalidOp "native collector retained turn projection differs from rollout evidence"

        let events = capture["envelope"].["payload"].["events"].AsArray()
        let sources =
            events
            |> Seq.filter (fun event -> event["kind"].GetValue<string>() = "runtime-native-inventory-source/1")
            |> Seq.toArray
        if sources.Length <> 1
           || CanonicalJson.canonicalize(Encoding.UTF8.GetBytes(sources[0].["sourceBinding"].ToJsonString()))
                <> CanonicalJson.canonicalize(Encoding.UTF8.GetBytes(binding.ToJsonString()))
           || sources[0].["sourceDigest"].GetValue<string>() <> framedDigest chunks then
            invalidOp "native collector retained source evidence differs from admitted source"

    let private nativeCollectorEnvelope
        (installation: NativeCollectorInstallationConfig)
        (principal: TelemetryReceipt.Principal)
        (dispatchId: string)
        (parentThread: string)
        (nativeAgent: string)
        (resolved: TelemetryStoreApplication.NativeCollectorDispatch)
        (inventory: SkillTelemetryReaders.NativeInventory)
        =
        let sha values =
            String.concat "\n" values |> Encoding.UTF8.GetBytes |> digestBytes

        let invocation = resolved.InvocationId
        let inventoryId = sha [ "native-inventory"; invocation ]
        let inventoryIdentity = sha [ "runtime-native-inventory"; invocation ]
        let sourceIdentity = sha [ "runtime-native-inventory-source"; invocation ]
        let turnIds = inventory.AllTurnIds |> List.map string
        let binding = JsonNode.Parse(inventory.SourceBinding.GetRawText())
        let event (kind: string) (identity: string) (fields: (string * JsonNode) list) =
            let value = JsonObject()
            value["kind"] <- JsonValue.Create kind
            value["identity"] <- JsonValue.Create identity
            value["itemId"] <- JsonValue.Create resolved.ItemId
            value["revision"] <- JsonValue.Create 0

            for name, field in fields do
                value[name] <- field

            value

        let inventoryFact =
            event
                "runtime-native-inventory/1"
                inventoryIdentity
                [ "inventoryId", JsonValue.Create inventoryId :> JsonNode
                  "originalItemId", JsonValue.Create resolved.OriginalItemId :> JsonNode
                  "invocationId", JsonValue.Create invocation :> JsonNode
                  "page", JsonValue.Create 1 :> JsonNode
                  "pages", JsonValue.Create 1 :> JsonNode
                  "expectedTurnIds", JsonSerializer.SerializeToNode turnIds
                  "expectedProvider", JsonValue.Create installation.Provider :> JsonNode
                  "requestedModel", JsonValue.Create resolved.RequestedModel :> JsonNode
                  "requestedEffort", JsonValue.Create resolved.RequestedEffort :> JsonNode
                  "support", JsonValue.Create "provider-native-final-turn-counters" :> JsonNode
                  "followupBaseline", JsonValue.Create 0 :> JsonNode
                  "capturedAt", JsonValue.Create inventory.InventoryCapturedAt :> JsonNode
                  "sourceKind", JsonValue.Create "provider-capability-and-dispatch-roster" :> JsonNode
                  "sourceDigest", JsonValue.Create inventory.SourceDigest :> JsonNode ]
        let sourceFact =
            event
                "runtime-native-inventory-source/1"
                sourceIdentity
                [ "inventoryId", JsonValue.Create inventoryId :> JsonNode
                  "originalItemId", JsonValue.Create resolved.OriginalItemId :> JsonNode
                  "invocationId", JsonValue.Create invocation :> JsonNode
                  "sourceDigest", JsonValue.Create inventory.SourceDigest :> JsonNode
                  "sourceBinding", binding ]
        let batchId = "native-collector-" + (sha [ dispatchId; parentThread; nativeAgent ])[..31]
        let payload = JsonObject()
        payload["schema"] <- JsonValue.Create TelemetryStore.BatchSchema
        payload["ingestId"] <- JsonValue.Create("receipt-" + TelemetryReceipt.key principal.Scope.Producer batchId)
        payload["sourceIdentity"] <- JsonValue.Create "protected-native-collector"
        payload["generation"] <- JsonValue.Create(string principal.GrantGeneration.Value)
        payload["cursor"] <- JsonValue.Create dispatchId
        payload["eventCount"] <- JsonValue.Create 2
        payload["events"] <- JsonArray(inventoryFact, sourceFact)
        let envelope = JsonObject()
        envelope["schema"] <- JsonValue.Create TelemetryReceipt.Schema
        envelope["workspaceId"] <- JsonValue.Create principal.Scope.Workspace
        envelope["producerId"] <- JsonValue.Create principal.Scope.Producer
        envelope["streamId"] <- JsonValue.Create principal.Scope.Stream
        envelope["batchId"] <- JsonValue.Create batchId
        envelope["payload"] <- payload

        CanonicalJson.canonicalize (Encoding.UTF8.GetBytes(envelope.ToJsonString()))
        |> Result.map Encoding.UTF8.GetBytes
        |> Result.defaultWith (fun _ -> invalidOp "native collector envelope is invalid")

    let private readCapture path (principal: TelemetryReceipt.Principal) (bytes: byte array) =
        let capture = JsonNode.Parse(bytes).AsObject()
        let names = capture |> Seq.map _.Key |> Set.ofSeq
        if names <> set [ "schema"; "installationDigest"; "grantId"; "grantGeneration"; "envelope"; "turns";
                          "sourceBinding"; "appServerResponses"; "rolloutRecords" ]
           || capture["schema"].GetValue<string>() <> "fsgg.telemetry.protected-native-capture/2"
           || capture["installationDigest"].GetValue<string>() <> digestBytes(File.ReadAllBytes(path + ".native-collector.json"))
           || capture["grantId"].GetValue<string>() <> principal.GrantId.Value
           || capture["grantGeneration"].GetValue<int64>() <> principal.GrantGeneration.Value
           || capture["turns"].AsArray().Count > 1000 then
            invalidOp "native collector capture binding differs"
        validateRetainedNativeSource capture
        let envelopeBytes =
            CanonicalJson.canonicalize (Encoding.UTF8.GetBytes(capture["envelope"].ToJsonString()))
            |> Result.map Encoding.UTF8.GetBytes
            |> Result.defaultWith (fun _ -> invalidOp "native collector capture envelope is invalid")
        match TelemetryReceipt.parse envelopeBytes with
        | Ok envelope when envelope.Scope = principal.Scope -> envelopeBytes, capture
        | _ -> invalidOp "native collector capture principal differs"

    let private nativeDeliveryEnvelope (principal: TelemetryReceipt.Principal)
                                      (candidate: TelemetryStoreApplication.NativeDeliveryCandidate)
                                      (observation: NativeDeliverySource.Observation) =
        let optionNode (value: string option) = value |> Option.map (fun text -> JsonValue.Create(text) :> JsonNode) |> Option.defaultValue null
        let identity = "native-delivery-" + candidate.BindingDigest[..31]
        let fact = JsonObject()
        fact["kind"] <- JsonValue.Create "learn-native-delivery-source/1"
        fact["identity"] <- JsonValue.Create identity
        fact["itemId"] <- JsonValue.Create candidate.ItemId
        fact["revision"] <- JsonValue.Create 0
        fact["candidateIdentity"] <- JsonValue.Create candidate.Identity
        fact["candidateSourceRef"] <- JsonValue.Create candidate.SourceRef
        fact["candidateDigest"] <- JsonValue.Create candidate.BindingDigest
        fact["repository"] <- JsonValue.Create observation.Repository
        fact["pullRequest"] <- JsonValue.Create observation.PullRequest
        fact["expectedHead"] <- JsonValue.Create observation.ExpectedHead
        fact["observedHead"] <- JsonValue.Create observation.ObservedHead
        fact["baseRef"] <- JsonValue.Create observation.BaseRef
        fact["baseSha"] <- JsonValue.Create observation.BaseSha
        fact["state"] <- JsonValue.Create observation.State
        fact["mergeCommit"] <- optionNode observation.MergeCommit
        fact["mergedAt"] <- optionNode observation.MergedAt
        fact["sourceKind"] <- JsonValue.Create "github-pull-request-readback"
        fact["sourceDigest"] <- JsonValue.Create observation.SourceDigest
        fact["originalWindowBinding"] <- JsonValue.Create "unverified"
        let payload = JsonObject()
        payload["schema"] <- JsonValue.Create TelemetryStore.BatchSchema
        payload["ingestId"] <- JsonValue.Create("receipt-" + identity)
        payload["sourceIdentity"] <- JsonValue.Create "protected-native-delivery-source"
        payload["generation"] <- JsonValue.Create(string principal.GrantGeneration.Value)
        payload["cursor"] <- JsonValue.Create candidate.SourceRef
        payload["eventCount"] <- JsonValue.Create 1
        payload["events"] <- JsonArray(fact)
        let envelope = JsonObject()
        envelope["schema"] <- JsonValue.Create TelemetryReceipt.Schema
        envelope["workspaceId"] <- JsonValue.Create principal.Scope.Workspace
        envelope["producerId"] <- JsonValue.Create principal.Scope.Producer
        envelope["streamId"] <- JsonValue.Create principal.Scope.Stream
        envelope["batchId"] <- JsonValue.Create("native-delivery-" + candidate.BindingDigest[..31])
        envelope["payload"] <- payload
        CanonicalJson.canonicalize(Encoding.UTF8.GetBytes(envelope.ToJsonString()))
        |> Result.map Encoding.UTF8.GetBytes
        |> Result.defaultWith (fun _ -> invalidOp "native delivery envelope is invalid")

    let private readNativeDeliveryCapture
        (principal: TelemetryReceipt.Principal)
        (installationDigest: string)
        (candidate: TelemetryStoreApplication.NativeDeliveryCandidate)
        (bytes: byte array) =
        let capture = JsonNode.Parse(bytes).AsObject()
        let names = capture |> Seq.map _.Key |> Set.ofSeq
        if names <> set [ "schema"; "installationDigest"; "grantId"; "grantGeneration";
                          "candidateBinding"; "candidateDigest"; "responseBody"; "sourceDigest"; "envelope" ]
           || capture["schema"].GetValue<string>() <> "fsgg.telemetry.protected-native-delivery-capture/1"
           || capture["installationDigest"].GetValue<string>() <> installationDigest
           || capture["grantId"].GetValue<string>() <> principal.GrantId.Value
           || capture["grantGeneration"].GetValue<int64>() <> principal.GrantGeneration.Value
           || capture["candidateDigest"].GetValue<string>() <> candidate.BindingDigest
           || CanonicalJson.canonicalize(Encoding.UTF8.GetBytes(capture["candidateBinding"].ToJsonString())) <> Ok candidate.Binding
           || digestBytes(Encoding.UTF8.GetBytes(capture["responseBody"].GetValue<string>())) <> capture["sourceDigest"].GetValue<string>() then
            invalidOp "native delivery capture binding differs"
        let envelopeBytes =
            CanonicalJson.canonicalize(Encoding.UTF8.GetBytes(capture["envelope"].ToJsonString()))
            |> Result.map Encoding.UTF8.GetBytes |> Result.defaultWith invalidOp
        let observation =
            NativeDeliverySource.decodeResponse candidate (capture["responseBody"].GetValue<string>())
            |> Result.defaultWith (String.concat "; " >> invalidOp)
        let expectedEnvelope = nativeDeliveryEnvelope principal candidate observation
        if not (CryptographicOperations.FixedTimeEquals(envelopeBytes, expectedEnvelope)) then
            invalidOp "native delivery capture response and envelope differ"
        let envelope =
            TelemetryReceipt.parse envelopeBytes
            |> Result.defaultWith (String.concat "; " >> invalidOp)
        match envelope.Batch.Facts with
        | [ { Payload = TelemetryStore.LearnNativeDeliverySource(_, sourceRef, candidateDigest, _, _, _, _, _, _, _, _, _, sourceDigest) } ]
            when envelope.Scope = principal.Scope && sourceRef = candidate.SourceRef
                 && candidateDigest = candidate.BindingDigest
                 && sourceDigest = capture["sourceDigest"].GetValue<string>() ->
            envelopeBytes, envelope, capture
        | _ -> invalidOp "native delivery capture envelope binding differs"

    type private InstalledOriginMaterial =
        {
            Query: TelemetryStoreApplication.InstalledOriginQuery
            ObservedAt: string
            ExpiresAt: string
            InstallationSha256: string
        }

    let private installedOriginMaterial path (config: HostConfig)
                                              (installation: NativeCollectorInstallationConfig)
                                              (principal: TelemetryReceipt.Principal)
                                              now =
        let readBounded (maximum: int) (file: string) =
            let info = FileInfo file
            let rec realAncestors (directory: DirectoryInfo) =
                isNull directory || (directory.Exists && isNull directory.LinkTarget && realAncestors directory.Parent)
            if not info.Exists || isNull info.Directory || not (isNull info.LinkTarget)
               || not (realAncestors info.Directory)
               || info.Length <= 0L || info.Length > int64 maximum
               || File.GetUnixFileMode file <> (UnixFileMode.UserRead ||| UnixFileMode.UserWrite) then
                invalidOp "installed origin evidence is unavailable"
            File.ReadAllBytes file
        let parse (bytes: byte array) = JsonNode.Parse(bytes).AsObject()
        let text (value: JsonObject) (name: string) = value[name].GetValue<string>()
        let integer (value: JsonObject) (name: string) = value[name].GetValue<int64>()
        let sidecarBytes = readBounded 16384 (path + ".native-collector.json")
        let configBytes = readBounded 1048576 path
        let managerBytes = readBounded 65536 (path + ".native-collector.receipt.json")
        let manager = parse managerBytes
        let expectedManager =
            set [ "schema"; "status"; "ownerUid"; "hostConfigSha256"; "sidecarSha256";
                  "executableSha256"; "credentialReference"; "workspaceId"; "producerId"; "streamId";
                  "grantId"; "grantGeneration"; "sourceVerification"; "snapshotOrigin";
                  "sharedCostCompleteness"; "activationAuthorized"; "sourceReferenceSha256";
                  "verifierRuntimeManifestSha256" ]
        let verifier = installation.NativeVerifier |> Option.defaultWith (fun () -> invalidOp "installed verifier is unavailable")
        let sourceReferenceBytes =
            readBounded 65536 (Path.Combine(Path.GetDirectoryName(path), "source-reference.json"))
        let runtimeManifestBytes = readBounded (1024 * 1024) verifier.RuntimeManifestPath
        if not (exactNames manager expectedManager)
           || text manager "schema" <> "fsgg.telemetry.native-collector-installation-receipt/3"
           || text manager "status" <> "installed"
           || manager["activationAuthorized"].GetValue<bool>()
           || text manager "hostConfigSha256" <> digestBytes configBytes
           || text manager "sidecarSha256" <> digestBytes sidecarBytes
           || text manager "executableSha256" <> digestBytes(File.ReadAllBytes installation.ExecutablePath)
           || text manager "credentialReference" <> installation.CredentialReference
           || text manager "workspaceId" <> principal.Scope.Workspace
           || text manager "producerId" <> principal.Scope.Producer
           || text manager "streamId" <> principal.Scope.Stream
           || text manager "grantId" <> principal.GrantId.Value
           || integer manager "grantGeneration" <> principal.GrantGeneration.Value
           || text manager "sourceReferenceSha256" <> digestBytes sourceReferenceBytes
           || text manager "verifierRuntimeManifestSha256" <> verifier.RuntimeManifestSha256
           || digestBytes runtimeManifestBytes <> verifier.RuntimeManifestSha256 then
            invalidOp "installed origin manager receipt differs"

        let evidence (name: string) maximum = readBounded maximum (Path.Combine(installation.EvidenceRoot, name))
        let profileBytes = evidence "fixed-native-capability-profile.json" 65536
        let resultBytes = evidence "fixed-native-capability-result.json" 65536
        let captureBytes = evidence "native-source-capture.json" (64 * 1024 * 1024)
        let snapshotBytes = evidence "native-source-snapshot.json" (64 * 1024 * 1024)
        let verificationBytes = evidence "native-source-verification.json" 1048576
        let profile, result, capture, verification, sourceReference =
            parse profileBytes, parse resultBytes, parse captureBytes, parse verificationBytes, parse sourceReferenceBytes
        let profileSha = digestBytes profileBytes
        let captureDigest = text capture "captureDigest"
        let observed = text result "evidenceObservedAt"
        let expires = text result "evidenceExpiresAt"
        let observedAt, expiresAt = DateTimeOffset.Parse observed, DateTimeOffset.Parse expires
        let startedAt, completedAt = DateTimeOffset.Parse(text result "startedAt"), DateTimeOffset.Parse(text result "completedAt")
        let projection = (capture["projection"]).AsObject()
        let threads = (projection["threads"]).AsArray()
        let routeSupported =
            threads
            |> Seq.exists (fun thread ->
                thread["provider"].GetValue<string>() = installation.Provider
                && thread["model"].GetValue<string>() = installation.Model
                && thread["effort"].GetValue<string>() = installation.Effort)
        let profileFields =
            set [ "schema"; "operation"; "revision"; "hostExecutableSha256"; "providerExecutable";
                  "providerExecutableSha256"; "expectedAdapterVersion"; "expectedCodexVersion";
                  "environmentAllowList"; "credentialScope"; "maximumRuntimeSeconds"; "maximumStreamBytes";
                  "expiresAt"; "disposableWorkspace"; "cleanup" ]
        let resultFields =
            set [ "schema"; "operation"; "profileRevision"; "profileSha256"; "hostExecutableSha256";
                  "providerExecutableSha256"; "adapterVersion"; "credentialScope"; "environmentAllowList";
                  "maximumRuntimeSeconds"; "maximumStreamBytes"; "requestedModel"; "requestedEffort";
                  "startedAt"; "completedAt"; "disposition"; "detail"; "authenticationState";
                  "authenticationProvenance"; "evidenceSchema"; "evidenceProvenance"; "evidenceObservedAt";
                  "evidenceExpiresAt"; "modelSessionStarts"; "cleanup" ]
        let captureFields =
            set [ "schema"; "outcome"; "rootThreadId"; "limits"; "initialExchanges";
                  "confirmationExchanges"; "rollouts"; "projection"; "captureDigest" ]
        let verificationFields =
            set [ "schema"; "status"; "outcome"; "captureDigest"; "missingDescendants";
                  "foreignDescendants"; "mismatchedThreads"; "missingTurns"; "foreignTurns";
                  "missingUsage"; "foreignUsage"; "mismatchedUsage" ]
        let sourceReferenceFields =
            set [ "schema"; "profileSha256"; "nativeSourceVolume"; "developmentTarget";
                  "collectorReadOnlyTarget"; "readerProfileSha256"; "captureQualified";
                  "verifierRuntimeManifestSha256" ]
        let cleanup = result["cleanup"]
        let cleanupFields =
            set [ "processTreeTerminationRequired"; "processTreeTerminated";
                  "workspaceRemovalAttempted"; "workspaceRemoved" ]
        let verificationGaps =
            [ "missingDescendants"; "foreignDescendants"; "mismatchedThreads"; "missingTurns";
              "foreignTurns"; "missingUsage"; "foreignUsage"; "mismatchedUsage" ]
        if not (exactNames profile profileFields) || not (exactNames result resultFields)
           || not (exactNames capture captureFields) || not (exactNames verification verificationFields)
           || not (exactNames sourceReference sourceReferenceFields)
           || text profile "schema" <> "fsgg.orchestration.host-fixed-native-capability/1"
           || text profile "operation" <> "codex-native-capability/1"
           || text profile "providerExecutable" <> installation.ExecutablePath
           || text profile "providerExecutableSha256" <> digestBytes(File.ReadAllBytes installation.ExecutablePath)
           || text profile "expiresAt" <> expires
           || text result "schema" <> "fsgg.orchestration.host-fixed-native-capability-result/1"
           || text result "operation" <> "codex-native-capability/1"
           || text result "profileRevision" <> text profile "revision"
           || text result "profileSha256" <> profileSha
           || text result "hostExecutableSha256" <> text profile "hostExecutableSha256"
           || text result "providerExecutableSha256" <> digestBytes(File.ReadAllBytes installation.ExecutablePath)
           || text result "adapterVersion" <> text profile "expectedAdapterVersion"
           || text result "credentialScope" <> text profile "credentialScope"
           || integer result "maximumRuntimeSeconds" <> integer profile "maximumRuntimeSeconds"
           || integer result "maximumStreamBytes" <> integer profile "maximumStreamBytes"
           || not (JsonNode.DeepEquals(result["environmentAllowList"], profile["environmentAllowList"]))
           || text result "requestedModel" <> installation.Model
           || text result "requestedEffort" <> installation.Effort
           || text result "disposition" <> "advertised-supported"
           || text result "authenticationState" <> "authenticated"
           || integer result "modelSessionStarts" <> 0L
           || not (exactNames cleanup cleanupFields)
           || (cleanupFields |> Seq.exists (fun name -> not (cleanup[name].GetValue<bool>())))
           || text capture "schema" <> "fsgg.learn.native-source-capture/1"
           || text capture "outcome" <> "native-census-and-usage-reconciled-at-capture"
           || text verification "schema" <> "fsgg.learn.native-source-verification/1"
           || text verification "status" <> "verified"
           || text verification "captureDigest" <> captureDigest
           || not (System.Text.RegularExpressions.Regex.IsMatch(captureDigest, "^[0-9a-f]{64}$"))
           || (verificationGaps |> List.exists (fun name -> verification[name].AsArray().Count <> 0))
           || text sourceReference "schema" <> "fsgg.telemetry.persistent-source-references/3"
           || text sourceReference "readerProfileSha256" <> profileSha
           || text sourceReference "collectorReadOnlyTarget" <> installation.CodexHome
           || sourceReference["captureQualified"].GetValue<bool>()
           || text sourceReference "verifierRuntimeManifestSha256" <> verifier.RuntimeManifestSha256
           || not routeSupported || startedAt > completedAt || completedAt > observedAt
           || observedAt > now || expiresAt <= observedAt || expiresAt <= now then
            invalidOp "installed origin retained evidence differs"
        NativeSourceVerification.verifyRetained verifier installation.EvidenceRoot captureBytes snapshotBytes verificationBytes
        |> Result.defaultWith (String.concat "; " >> invalidOp)
        let unchanged (expected: byte array) maximum file =
            let observed = readBounded maximum file
            expected.Length = observed.Length && Array.forall2 (=) expected observed
        if not (unchanged captureBytes (64 * 1024 * 1024) (Path.Combine(installation.EvidenceRoot, "native-source-capture.json")))
           || not (unchanged snapshotBytes (64 * 1024 * 1024) (Path.Combine(installation.EvidenceRoot, "native-source-snapshot.json")))
           || not (unchanged verificationBytes 1048576 (Path.Combine(installation.EvidenceRoot, "native-source-verification.json")))
           || not (unchanged sourceReferenceBytes 65536 (Path.Combine(Path.GetDirectoryName(path), "source-reference.json")))
           || not (unchanged runtimeManifestBytes (1024 * 1024) verifier.RuntimeManifestPath) then
            invalidOp "installed origin retained evidence changed"
        let query: TelemetryStoreApplication.InstalledOriginQuery =
            { WorkspaceId = principal.Scope.Workspace
              ProducerId = principal.Scope.Producer; StreamId = principal.Scope.Stream
              Role = "native-collector"; GrantId = principal.GrantId.Value
              GrantGeneration = principal.GrantGeneration.Value
              ManagerReceiptSha256 = digestBytes managerBytes
              CapabilityProfileSha256 = profileSha
              CapabilityResultSha256 = digestBytes resultBytes
              NativeCaptureSha256 = digestBytes captureBytes
              NativeVerificationSha256 = digestBytes verificationBytes
              InstallationSha256 = framedDigest [ configBytes; sidecarBytes; File.ReadAllBytes installation.ExecutablePath;
                                                   sourceReferenceBytes; runtimeManifestBytes; snapshotBytes ] }
        { Query = query; ObservedAt = observed; ExpiresAt = expires
          InstallationSha256 = query.InstallationSha256 }

    let private installedOriginEnvelope (principal: TelemetryReceipt.Principal) (material: InstalledOriginMaterial) =
        let query = material.Query
        let fields =
            [ query.WorkspaceId; query.ProducerId; query.StreamId; query.GrantId; string query.GrantGeneration;
              query.ManagerReceiptSha256; query.CapabilityProfileSha256; query.CapabilityResultSha256;
              query.NativeCaptureSha256; query.NativeVerificationSha256; material.ObservedAt; material.ExpiresAt;
              material.InstallationSha256 ]
        let digest = framedDigest (fields |> Seq.map Encoding.UTF8.GetBytes)
        let fact = JsonObject()
        fact["kind"] <- JsonValue.Create "learn-installed-origin/1"
        fact["identity"] <- JsonValue.Create("installed-origin-" + digest[..31])
        fact["revision"] <- JsonValue.Create 0
        fact["workspaceId"] <- JsonValue.Create query.WorkspaceId
        fact["producerId"] <- JsonValue.Create query.ProducerId
        fact["streamId"] <- JsonValue.Create query.StreamId
        fact["role"] <- JsonValue.Create query.Role
        fact["grantId"] <- JsonValue.Create query.GrantId
        fact["grantGeneration"] <- JsonValue.Create query.GrantGeneration
        fact["managerReceiptSha256"] <- JsonValue.Create query.ManagerReceiptSha256
        fact["capabilityProfileSha256"] <- JsonValue.Create query.CapabilityProfileSha256
        fact["capabilityResultSha256"] <- JsonValue.Create query.CapabilityResultSha256
        fact["nativeCaptureSha256"] <- JsonValue.Create query.NativeCaptureSha256
        fact["nativeVerificationSha256"] <- JsonValue.Create query.NativeVerificationSha256
        fact["capabilityObservedAt"] <- JsonValue.Create material.ObservedAt
        fact["capabilityExpiresAt"] <- JsonValue.Create material.ExpiresAt
        fact["installationSha256"] <- JsonValue.Create material.InstallationSha256
        let payload = JsonObject()
        payload["schema"] <- JsonValue.Create TelemetryStore.BatchSchema
        payload["ingestId"] <- JsonValue.Create("receipt-installed-origin-" + digest[..31])
        payload["sourceIdentity"] <- JsonValue.Create "protected-installed-origin"
        payload["generation"] <- JsonValue.Create(string query.GrantGeneration)
        payload["cursor"] <- JsonValue.Create digest
        payload["eventCount"] <- JsonValue.Create 1
        payload["events"] <- JsonArray(fact)
        let envelope = JsonObject()
        envelope["schema"] <- JsonValue.Create TelemetryReceipt.Schema
        envelope["workspaceId"] <- JsonValue.Create principal.Scope.Workspace
        envelope["producerId"] <- JsonValue.Create principal.Scope.Producer
        envelope["streamId"] <- JsonValue.Create principal.Scope.Stream
        envelope["batchId"] <- JsonValue.Create("installed-origin-" + digest[..31])
        envelope["payload"] <- payload
        CanonicalJson.canonicalize(Encoding.UTF8.GetBytes(envelope.ToJsonString()))
        |> Result.map Encoding.UTF8.GetBytes |> Result.defaultWith invalidOp

    let private installedOrigin path config assessmentFor persist now =
        match Configuration.loadNativeCollectorInstallation path config with
        | Error errors -> Error errors
        | Ok(installation, _) when installation.Schema <> "fsgg.telemetry.native-collector-installation/3" ->
            Error [ "verified native collector installation is unavailable" ]
        | Ok(installation, principal) ->
            match storeFor config principal.Scope.Workspace with
            | None -> Error [ "native collector workspace is unavailable" ]
            | Some store ->
                let material = installedOriginMaterial path config installation principal now
                if persist then
                    let bytes = installedOriginEnvelope principal material
                    TelemetryStoreApplication.submitReceiptPrincipal store.Root (assessmentFor store.Root) principal bytes
                    |> Result.bind (fun _ -> TelemetryStoreApplication.drainReceipts store.Root (assessmentFor store.Root) principal.Scope.Workspace)
                    |> Result.bind (fun _ -> TelemetryStoreApplication.resolveInstalledOriginAt now store.Root (assessmentFor store.Root) material.Query)
                else
                    TelemetryStoreApplication.resolveInstalledOriginAt now store.Root (assessmentFor store.Root) material.Query
                |> Result.map (fun origin ->
                    JsonSerializer.Serialize
                        {| schema = "fsgg.learn.installed-producer-receipt/1"; source = {| producerId = material.Query.ProducerId; revision = string origin.Revision; recordId = origin.RecordId; observedAt = origin.ObservedAt |};
                           workspaceId = material.Query.WorkspaceId; producerId = material.Query.ProducerId; streamId = material.Query.StreamId;
                           role = material.Query.Role; grantId = material.Query.GrantId; grantGeneration = material.Query.GrantGeneration;
                           managerReceiptSha256 = material.Query.ManagerReceiptSha256; capabilityProfileSha256 = material.Query.CapabilityProfileSha256;
                           capabilityResultSha256 = material.Query.CapabilityResultSha256; nativeCaptureSha256 = material.Query.NativeCaptureSha256;
                           nativeVerificationSha256 = material.Query.NativeVerificationSha256; capabilityObservedAt = origin.ObservedAt;
                           capabilityExpiresAt = origin.ExpiresAt |} + "\n")

    let private collectNativeDelivery path config assessmentFor transportFor sourceRef =
        match Configuration.loadNativeDeliverySourceInstallation path config with
        | Error errors -> Error errors
        | Ok(sourceInstallation, nativeInstallation, principal) ->
            match storeFor config principal.Scope.Workspace with
            | None -> Error [ "native delivery source workspace is unavailable" ]
            | Some store ->
                match TelemetryStoreApplication.resolveNativeDeliveryCandidate store.Root (assessmentFor store.Root) sourceRef with
                | Error errors -> Error errors
                | Ok candidate when not (sourceInstallation.AllowedRepositories |> Array.contains candidate.Repository) ->
                    Error [ "native delivery candidate repository is outside the installed scope" ]
                | Ok candidate ->
                    let installationDigest =
                        String.concat "\n" [ digestFile(path + ".native-collector.json")
                                             digestFile(path + ".native-delivery-source.json")
                                             digestFile(sourceInstallation.GitHubCredentialFile) ]
                        |> Encoding.UTF8.GetBytes |> digestBytes
                    let artifactName = candidate.BindingDigest + ".delivery-capture.json"
                    let artifactPath = Path.Combine(nativeInstallation.EvidenceRoot, artifactName)
                    let retained =
                        if File.Exists artifactPath then writePrivateAtomic nativeInstallation.EvidenceRoot artifactName Array.empty
                        else
                            let token = File.ReadAllText(sourceInstallation.GitHubCredentialFile).Trim()
                            if token.Length < 1 || token.Length > 4096 then invalidOp "native delivery GitHub credential is invalid"
                            let disposable, transport = transportFor token
                            use _transport = disposable
                            let observation =
                                NativeDeliverySource.acquire transport candidate
                                |> Result.defaultWith (String.concat "; " >> invalidOp)
                            let envelope = nativeDeliveryEnvelope principal candidate observation
                            let capture = JsonObject()
                            capture["schema"] <- JsonValue.Create "fsgg.telemetry.protected-native-delivery-capture/1"
                            capture["installationDigest"] <- JsonValue.Create installationDigest
                            capture["grantId"] <- JsonValue.Create principal.GrantId.Value
                            capture["grantGeneration"] <- JsonValue.Create principal.GrantGeneration.Value
                            capture["candidateBinding"] <- JsonNode.Parse candidate.Binding
                            capture["candidateDigest"] <- JsonValue.Create candidate.BindingDigest
                            capture["responseBody"] <- JsonValue.Create observation.ResponseBody
                            capture["sourceDigest"] <- JsonValue.Create observation.SourceDigest
                            capture["envelope"] <- JsonNode.Parse envelope
                            writePrivateAtomic nativeInstallation.EvidenceRoot artifactName (Encoding.UTF8.GetBytes(capture.ToJsonString()))
                    let envelopeBytes, _, _ = readNativeDeliveryCapture principal installationDigest candidate retained
                    TelemetryStoreApplication.submitReceiptPrincipal store.Root (assessmentFor store.Root) principal envelopeBytes
                    |> Result.bind (fun _ -> TelemetryStoreApplication.drainReceipts store.Root (assessmentFor store.Root) principal.Scope.Workspace)
                    |> Result.map (fun _ -> JsonSerializer.Serialize({| schema = "fsgg.telemetry.native-delivery-result/1"; status = "applied"; sourceRef = sourceRef |}) + "\n")

    let private collectNative
        (path: string)
        (config: HostConfig)
        assessmentFor
        (dispatchId: string)
        (parentThreadText: string)
        (nativeAgent: string)
        =
        match Guid.TryParseExact(parentThreadText, "D") with
        | false, _ -> Error [ "native parent selector is invalid" ]
        | true, parentThread ->
            match Configuration.loadNativeCollectorInstallation path config with
            | Error errors -> Error errors
            | Ok(installation, principal) ->
                match storeFor config principal.Scope.Workspace with
                | None -> Error [ "native collector workspace is unavailable" ]
                | Some store ->
                    let assessment = assessmentFor store.Root

                    match
                        TelemetryStoreApplication.resolveNativeCollectorDispatch
                            store.Root
                            assessment
                            dispatchId
                            nativeAgent
                    with
                    | Error errors -> Error errors
                    | Ok resolved when
                        resolved.RequestedModel <> installation.Model
                        || resolved.RequestedEffort <> installation.Effort
                        ->
                        Error [ "native collector profile differs from durable admission" ]
                    | Ok resolved ->
                        let qualified = installation.Schema <> "fsgg.telemetry.native-collector-installation/1"
                        let artifactName =
                            digestBytes (Encoding.UTF8.GetBytes(String.concat "\n" [ dispatchId; parentThreadText; nativeAgent ]))
                            + (if qualified then ".capture.json" else ".envelope.json")
                        let artifactPath = Path.Combine(installation.EvidenceRoot, artifactName)

                        let retainedBytes =
                            if File.Exists artifactPath then
                                writePrivateAtomic installation.EvidenceRoot artifactName Array.empty
                            else
                                match
                                    SkillTelemetryReaders.NativeUsage.collectProtectedWith
                                        installation.ExecutablePath
                                        installation.CodexHome
                                        parentThread
                                        nativeAgent
                                        resolved.RootInvocationId
                                        resolved.InvocationId
                                        0
                                with
                                | Error error -> invalidOp error.Message
                                | Ok inventory when
                                    not inventory.Complete
                                    || inventory.Provider <> Some installation.Provider
                                    || inventory.Model <> Some installation.Model
                                    || inventory.Effort <> Some installation.Effort
                                    || inventory.InventoryPaging.GetArrayLength() <> 1
                                    || inventory.AllTurnIds.Length <> inventory.Turns.Length
                                    ->
                                    invalidOp "native collector reader evidence is incomplete"
                                | Ok inventory ->
                                    let envelopeBytes =
                                        nativeCollectorEnvelope installation principal dispatchId parentThreadText nativeAgent resolved inventory
                                    let evidenceBytes =
                                        if not qualified then envelopeBytes
                                        else
                                            let capture = JsonObject()
                                            capture["schema"] <- JsonValue.Create "fsgg.telemetry.protected-native-capture/2"
                                            capture["installationDigest"] <- JsonValue.Create(digestBytes(File.ReadAllBytes(path + ".native-collector.json")))
                                            capture["grantId"] <- JsonValue.Create principal.GrantId.Value
                                            capture["grantGeneration"] <- JsonValue.Create principal.GrantGeneration.Value
                                            capture["envelope"] <- JsonNode.Parse envelopeBytes
                                            capture["sourceBinding"] <- JsonNode.Parse(inventory.SourceBinding.GetRawText())
                                            capture["appServerResponses"] <- JsonNode.Parse(inventory.AppServerResponses.GetRawText())
                                            capture["rolloutRecords"] <- JsonNode.Parse(inventory.RolloutRecords.GetRawText())
                                            capture["turns"] <-
                                                inventory.Turns
                                                |> List.map (fun turn ->
                                                    {| threadId = string inventory.ThreadId; turnId = string turn.TurnId
                                                       sequence = turn.Sequence; provider = turn.Provider; model = turn.Model
                                                       effort = turn.Effort; input = turn.Usage.Input; cachedInput = turn.Usage.CachedInput
                                                       output = turn.Usage.Output; reasoning = turn.Usage.Reasoning; total = turn.Usage.Total |})
                                                |> JsonSerializer.SerializeToNode
                                            Encoding.UTF8.GetBytes(capture.ToJsonString())
                                    writePrivateAtomic installation.EvidenceRoot artifactName evidenceBytes

                        let bytes = if qualified then readCapture path principal retainedBytes |> fst else retainedBytes

                        match TelemetryReceipt.parse bytes with
                        | Error _ -> Error [ "native collector replay artifact is invalid" ]
                        | Ok envelope when envelope.Scope <> principal.Scope ->
                            Error [ "native collector replay artifact has foreign authority" ]
                        | Ok _ ->
                            match TelemetryStoreApplication.submitReceiptPrincipal store.Root assessment principal bytes with
                            | Error errors -> Error errors
                            | Ok _ ->
                                match
                                    TelemetryStoreApplication.drainReceipts
                                        store.Root
                                        assessment
                                        principal.Scope.Workspace
                                with
                                | Error errors -> Error errors
                                | Ok _ ->
                                    Ok(
                                        JsonSerializer.Serialize
                                            {|
                                                schema = "fsgg.telemetry.native-collector-result/1"
                                                status = "applied"
                                                dispatchId = dispatchId
                                                invocationId = resolved.InvocationId
                                                sourceVerification = "unknown"
                                                snapshotOrigin = "unknown"
                                                sharedCostCompleteness = "unknown"
                                            |}
                                        + "\n"
                                    )

    let private exportLearning path config assessmentFor includeNativeDelivery =
        match Configuration.loadNativeCollectorInstallation path config with
        | Error errors -> Error errors
        | Ok(installation, _) when installation.Schema <> "fsgg.telemetry.native-collector-installation/2" ->
            Error [ "qualified native collector installation is unavailable" ]
        | Ok(installation, principal) ->
            match storeFor config principal.Scope.Workspace with
            | None -> Error [ "native collector workspace is unavailable" ]
            | Some store ->
                let files =
                    Directory.EnumerateFiles(installation.EvidenceRoot, "*.capture.json")
                    |> Seq.filter (fun file ->
                        System.Text.RegularExpressions.Regex.IsMatch(
                            Path.GetFileName file,
                            "^[0-9a-f]{64}\\.capture\\.json$"))
                    |> Seq.truncate 1001 |> Seq.toArray
                if files.Length > 1000 then invalidOp "native collector capture population exceeds the bound"
                let captures = JsonArray()
                let mutable captureBytes = 0
                for file in files |> Array.sort do
                    let bytes = readPrivateEvidence file
                    captureBytes <- captureBytes + bytes.Length
                    if captureBytes > 3145728 then invalidOp "native collector capture bytes exceed the export bound"
                    let envelopeBytes, capture = readCapture path principal bytes
                    let envelope = TelemetryReceipt.parse envelopeBytes |> Result.defaultWith (fun _ -> invalidOp "invalid native capture")
                    let exported = JsonObject()
                    exported["receiptKey"] <- JsonValue.Create envelope.Key
                    exported["envelopeDigest"] <- JsonValue.Create envelope.Digest
                    exported["producer"] <- JsonValue.Create principal.Scope.Producer
                    exported["stream"] <- JsonValue.Create principal.Scope.Stream
                    exported["grantId"] <- capture["grantId"].DeepClone()
                    exported["grantGeneration"] <- capture["grantGeneration"].DeepClone()
                    exported["events"] <- capture["envelope"].["payload"].["events"].DeepClone()
                    exported["turns"] <- capture["turns"].DeepClone()
                    captures.Add exported
                match TelemetryStoreApplication.scopedDashboardSnapshot store.Root (assessmentFor store.Root) principal.Scope.Workspace None with
                | Error errors -> Error errors
                | Ok snapshot ->
                    let output = JsonObject()
                    output["schema"] <- JsonValue.Create "fsgg.telemetry.protected-learning-export/1"
                    output["snapshot"] <- JsonNode.Parse snapshot
                    output["captures"] <- captures
                    if includeNativeDelivery then
                        match Configuration.loadNativeDeliverySourceInstallation path config with
                        | Error errors -> raise (InvalidOperationException(String.concat "; " errors))
                        | Ok(_, _, deliveryPrincipal) when deliveryPrincipal <> principal ->
                            invalidOp "native delivery source principal differs"
                        | Ok(sourceInstallation, _, _) ->
                            let installationDigest =
                                String.concat "\n" [ digestFile(path + ".native-collector.json")
                                                     digestFile(path + ".native-delivery-source.json")
                                                     digestFile(sourceInstallation.GitHubCredentialFile) ]
                                |> Encoding.UTF8.GetBytes |> digestBytes
                            let files =
                                Directory.EnumerateFiles(installation.EvidenceRoot, "*.delivery-capture.json")
                                |> Seq.truncate 1001 |> Seq.toArray
                            if files.Length > 1000 then invalidOp "native delivery capture population exceeds the bound"
                            let deliveryCaptures = JsonArray()
                            for file in files |> Array.sort do
                                let bytes = readPrivateEvidence file
                                captureBytes <- captureBytes + bytes.Length
                                if captureBytes > 3145728 then invalidOp "protected capture bytes exceed the export bound"
                                let preliminary = JsonNode.Parse(bytes).AsObject()
                                let envelopeBytes =
                                    CanonicalJson.canonicalize(Encoding.UTF8.GetBytes(preliminary["envelope"].ToJsonString()))
                                    |> Result.map Encoding.UTF8.GetBytes |> Result.defaultWith invalidOp
                                let preliminaryEnvelope = TelemetryReceipt.parse envelopeBytes |> Result.defaultWith (String.concat "; " >> invalidOp)
                                let sourceRef =
                                    match preliminaryEnvelope.Batch.Facts with
                                    | [ { Payload = TelemetryStore.LearnNativeDeliverySource(_, value, _, _, _, _, _, _, _, _, _, _, _) } ] -> value
                                    | _ -> invalidOp "native delivery capture source is invalid"
                                let candidate =
                                    TelemetryStoreApplication.resolveNativeDeliveryCandidate store.Root (assessmentFor store.Root) sourceRef
                                    |> Result.defaultWith (String.concat "; " >> invalidOp)
                                let _, envelope, capture = readNativeDeliveryCapture principal installationDigest candidate bytes
                                let exported = JsonObject()
                                exported["receiptKey"] <- JsonValue.Create envelope.Key
                                exported["envelopeDigest"] <- JsonValue.Create envelope.Digest
                                exported["producer"] <- JsonValue.Create principal.Scope.Producer
                                exported["stream"] <- JsonValue.Create principal.Scope.Stream
                                exported["grantId"] <- capture["grantId"].DeepClone()
                                exported["grantGeneration"] <- capture["grantGeneration"].DeepClone()
                                exported["candidateBinding"] <- capture["candidateBinding"].DeepClone()
                                exported["candidateDigest"] <- capture["candidateDigest"].DeepClone()
                                exported["responseBody"] <- capture["responseBody"].DeepClone()
                                exported["sourceDigest"] <- capture["sourceDigest"].DeepClone()
                                exported["events"] <- capture["envelope"].["payload"].["events"].DeepClone()
                                deliveryCaptures.Add exported
                            output["schema"] <- JsonValue.Create "fsgg.telemetry.protected-learning-export/2"
                            output["deliveryCaptures"] <- deliveryCaptures
                    let json = output.ToJsonString() + "\n"
                    if Encoding.UTF8.GetByteCount json > 4194304 then Error [ "protected learning export exceeds the bound" ]
                    else Ok json

    let private preflight (config: HostConfig) assessmentFor =
        try
            if config.BrowserPrincipals.Length = 0 then
                Error [ "dashboard-auth-unavailable" ]
            else
                Configuration.credentials config |> ignore
                Configuration.browserKeyHashes config |> ignore

                use _browser =
                    new BrowserSecurity.Service(BrowserComposition.options config, config.BrowserPrincipals)

                use _certificate =
                    System.Security.Cryptography.X509Certificates.X509CertificateLoader.LoadPkcs12FromFile(
                        config.CertificatePath,
                        File.ReadAllText(config.CertificatePasswordFile).Trim()
                    )

                let failures =
                    config.Stores
                    |> Array.toList
                    |> List.collect (fun store ->
                        let assessment = assessmentFor store.Root
                        let free = freeSpaceFor store.Root

                        [
                            match TelemetryStoreApplication.status store.Root assessment with
                            | Error errors -> yield! errors
                            | Ok _ -> ()
                            match
                                TelemetryStoreApplication.scopedDashboardSnapshot
                                    store.Root
                                    assessment
                                    store.WorkspaceId
                                    None
                            with
                            | Error errors -> yield! errors
                            | Ok _ -> ()
                            if free |> Option.forall (fun bytes -> bytes < 512L * 1024L * 1024L) then
                                yield "capacity-reserve-unavailable"
                        ])

                if failures.IsEmpty then
                    Ok(
                        JsonSerializer.Serialize
                            {|
                                schema = "fsgg.telemetry.host-preflight/1"
                                status = "ready"
                                stores = config.Stores.Length
                                dashboardAuthentication = "ready"
                                supportedStoreSchemaMin = 10
                                supportedStoreSchemaMax = 12
                            |}
                        + "\n"
                    )
                else
                    Error failures
        with _ ->
            Error [ "invalid-configuration" ]

    let private status (config: HostConfig) assessmentFor =
        let stores =
            config.Stores
            |> Array.map (fun store ->
                let assessment = assessmentFor store.Root

                let state =
                    match TelemetryStoreApplication.status store.Root assessment with
                    | Ok _ -> "ready"
                    | Error _ -> "unavailable"

                match TelemetryStoreApplication.receiptCapacity store.Root assessment with
                | Ok(lifetime, pending, pendingBytes) ->
                    {|
                        workspaceId = store.WorkspaceId
                        status = state
                        capacity = "available"
                        lifetimeReceipts = Some lifetime
                        pendingReceipts = Some pending
                        pendingBytes = Some pendingBytes
                    |}
                | Error _ ->
                    {|
                        workspaceId = store.WorkspaceId
                        status = state
                        capacity = "unavailable"
                        lifetimeReceipts = None
                        pendingReceipts = None
                        pendingBytes = None
                    |})

        let processState =
            match Runtime.ServiceLock.Probe config.ServiceLockPath with
            | Ok true -> "running"
            | Ok false -> "stopped"
            | Error _ -> "unknown"

        let dashboardState =
            if config.BrowserPrincipals.Length > 0 then
                "configured"
            else
                "unavailable"

        Ok(
            JsonSerializer.Serialize
                {|
                    schema = "fsgg.telemetry.host-status/1"
                    ``process`` = processState
                    recovery = "unknown"
                    dashboardAuthentication = dashboardState
                    supportedStoreSchemaMin = 10
                    supportedStoreSchemaMax = 12
                    stores = stores
                |}
            + "\n"
        )

    let runWithDependenciesAt
        (now: DateTimeOffset)
        (argv: string array)
        (assessmentFor: string -> TelemetryStore.DurabilityAssessment)
        (deliveryTransportFor: string -> IDisposable * Transport.ISinglePageGitHubTransport) =
        match List.ofArray argv with
        | [ "init"; "--root"; root; "--workspace"; workspace ] ->
            match TelemetryStoreApplication.initialize root (assessmentFor root) with
            | Error errors -> resultExit "storage-unavailable" (Error errors)
            | Ok _ ->
                TelemetryStoreApplication.provisionReceiptWorkspace root (assessmentFor root) workspace
                |> resultExit "storage-unavailable"
        | [ "enroll-producer"
            "--config"
            path
            "--reference"
            reference
            "--secret-file"
            secret
            "--workspace"
            workspace
            "--producer"
            producer
            "--stream"
            stream ]
        | [ "enroll-producer"
            "--config"
            path
            "--reference"
            reference
            "--secret-file"
            secret
            "--workspace"
            workspace
            "--producer"
            producer
            "--stream"
            stream
            "--revoked" ] ->
            match load path with
            | Error errors -> resultExit "invalid-configuration" (Error errors)
            | Ok config ->
                withLock config (fun () ->
                    let revoked = argv.Length = 14

                    match
                        config.Credentials
                        |> Array.tryFind (fun entry ->
                            entry.Reference = reference
                            && entry.SecretFile = secret
                            && entry.WorkspaceId = workspace
                            && entry.ProducerId = producer
                            && entry.StreamId = stream
                            && entry.Revoked = revoked),
                        storeFor config workspace
                    with
                    | Some entry, Some store ->
                        match
                            TelemetryStoreApplication.provisionReceiptWorkspace
                                store.Root
                                (assessmentFor store.Root)
                                workspace
                        with
                        | Error errors -> resultExit "storage-unavailable" (Error errors)
                        | Ok _ ->
                            let principal: TelemetryReceipt.Principal =
                                { Scope = { Workspace = workspace; Producer = producer; Stream = stream }
                                  Role = if config.Schema = "fsgg.telemetry.host-config/2" && entry.Role = "native-collector" then TelemetryReceipt.NativeCollector else TelemetryReceipt.Generic
                                  GrantId = if config.Schema = "fsgg.telemetry.host-config/2" then Some entry.GrantId else None
                                  GrantGeneration = if config.Schema = "fsgg.telemetry.host-config/2" then Some entry.GrantGeneration else None }
                            TelemetryStoreApplication.enrollReceiptPrincipal
                                store.Root
                                (assessmentFor store.Root)
                                principal
                            |> resultExit "storage-unavailable"
                    | _ -> resultExit "invalid-configuration" (Error [ "enrollment is not declared by config" ]))
        | [ "collect-native"
            "--config"
            path
            "--dispatch"
            dispatchId
            "--parent-thread"
            parentThread
            "--native-agent"
            nativeAgent ] ->
            match load path with
            | Error errors -> resultExit "invalid-configuration" (Error errors)
            | Ok config ->
                withLock config (fun () ->
                    try
                        collectNative path config assessmentFor dispatchId parentThread nativeAgent
                        |> resultExit "native-collector-refused"
                    with _ ->
                        resultExit "native-collector-refused" (Error [ "native collector refused" ]))
        | [ "collect-native-delivery"; "--config"; path; "--source-ref"; sourceRef ] ->
            match load path with
            | Error errors -> resultExit "invalid-configuration" (Error errors)
            | Ok config ->
                withLock config (fun () ->
                    try
                        collectNativeDelivery path config assessmentFor deliveryTransportFor sourceRef
                        |> resultExit "native-delivery-source-refused"
                    with _ -> resultExit "native-delivery-source-refused" (Error [ "native delivery source refused" ]))
        | [ "collect-installed-origin"; "--config"; path ] ->
            match load path with
            | Error errors -> resultExit "invalid-configuration" (Error errors)
            | Ok config ->
                withLock config (fun () ->
                    try installedOrigin path config assessmentFor true now |> resultExit "installed-origin-refused"
                    with _ -> resultExit "installed-origin-refused" (Error [ "installed origin refused" ]))
        | [ "read-installed-origin"; "--config"; path ] ->
            match load path with
            | Error errors -> resultExit "invalid-configuration" (Error errors)
            | Ok config ->
                withLock config (fun () ->
                    try installedOrigin path config assessmentFor false now |> resultExit "installed-origin-unavailable"
                    with _ -> resultExit "installed-origin-unavailable" (Error [ "installed origin unavailable" ]))
        | [ "read-native-route"; "--config"; path; "--original-item"; originalItem ] ->
            match load path with
            | Error errors -> resultExit "invalid-configuration" (Error errors)
            | Ok config ->
                withLock config (fun () ->
                    try
                        match Configuration.loadNativeCollectorInstallation path config with
                        | Error errors -> resultExit "native-route-unavailable" (Error errors)
                        | Ok(_, principal) ->
                            match storeFor config principal.Scope.Workspace with
                            | None -> resultExit "native-route-unavailable" (Error [ "native collector workspace is unavailable" ])
                            | Some store ->
                                TelemetryStoreApplication.readNativeRoutePopulation
                                    store.Root (assessmentFor store.Root) originalItem
                                |> resultExit "native-route-unavailable"
                    with _ -> resultExit "native-route-unavailable" (Error [ "native route unavailable" ]))
        | [ "export-learning"; "--config"; path ] ->
            match load path with
            | Error errors -> resultExit "invalid-configuration" (Error errors)
            | Ok config ->
                withLock config (fun () ->
                    try exportLearning path config assessmentFor false |> resultExit "native-export-refused"
                    with _ -> resultExit "native-export-refused" (Error [ "native export refused" ]))
        | [ "export-learning"; "--config"; path; "--include-native-delivery" ] ->
            match load path with
            | Error errors -> resultExit "invalid-configuration" (Error errors)
            | Ok config ->
                withLock config (fun () ->
                    try exportLearning path config assessmentFor true |> resultExit "native-export-refused"
                    with _ -> resultExit "native-export-refused" (Error [ "native export refused" ]))
        | [ "preflight"; "--config"; path ] ->
            match load path with
            | Error errors -> resultExit "invalid-configuration" (Error errors)
            | Ok config -> withLock config (fun () -> preflight config assessmentFor |> resultExit "preflight-failed")
        | [ "status"; "--config"; path ] ->
            match load path with
            | Error errors -> resultExit "invalid-configuration" (Error errors)
            | Ok config -> status config assessmentFor |> resultExit "status-unavailable"
        | [ "backup"; "--config"; path; "--output"; output ] ->
            match load path with
            | Error errors -> resultExit "invalid-configuration" (Error errors)
            | Ok config ->
                withLock config (fun () ->
                    try
                        let outputParent =
                            if Path.IsPathFullyQualified output then
                                Path.GetDirectoryName(Path.GetFullPath output)
                            else
                                null

                        if
                            not (Path.IsPathFullyQualified output)
                            || isNull outputParent
                            || not (Directory.Exists outputParent)
                            || hasSymlinkAncestor output
                            || Directory.Exists output
                            || File.Exists output
                        then
                            resultExit
                                "backup-integrity-failed"
                                (Error [ "backup output must be a fresh child of an existing real directory" ])
                        else
                            let temporary = output + ".tmp-" + Guid.NewGuid().ToString("N")

                            try
                                Directory.CreateDirectory temporary |> ignore

                                if not (OperatingSystem.IsWindows()) then
                                    File.SetUnixFileMode(
                                        temporary,
                                        UnixFileMode.UserRead ||| UnixFileMode.UserWrite ||| UnixFileMode.UserExecute
                                    )

                                let mutable failure = None

                                for store in config.Stores do
                                    if failure.IsNone then
                                        match
                                            TelemetryStoreApplication.backupReceiptStore
                                                store.Root
                                                (assessmentFor store.Root)
                                                store.WorkspaceId
                                                (Path.Combine(temporary, store.WorkspaceId))
                                        with
                                        | Ok _ -> ()
                                        | Error errors -> failure <- Some errors

                                match failure with
                                | Some errors -> resultExit "backup-integrity-failed" (Error errors)
                                | None ->
                                    let workspaces =
                                        config.Stores
                                        |> Array.sortBy _.WorkspaceId
                                        |> Array.map (fun store ->
                                            {|
                                                workspaceId = store.WorkspaceId
                                                path = store.WorkspaceId
                                                manifestSha256 =
                                                    digestFile (
                                                        Path.Combine(temporary, store.WorkspaceId, "manifest.json")
                                                    )
                                            |})

                                    let manifest =
                                        JsonSerializer.Serialize
                                            {|
                                                schema = "fsgg.telemetry.host-backup-set/1"
                                                hostVersion = "0.2.1"
                                                supportedStoreSchemaMin = 10
                                                supportedStoreSchemaMax = 12
                                                configMetadataSha256 = configMetadataDigest config
                                                createdAt = DateTimeOffset.UtcNow.ToString("O")
                                                workspaces = workspaces
                                            |}
                                        + "\n"

                                    let manifestPath = Path.Combine(temporary, "backup-manifest.json")
                                    File.WriteAllText(manifestPath, manifest, UTF8Encoding(false))
                                    flushFile manifestPath
                                    syncDirectory temporary
                                    Directory.Move(temporary, output)
                                    syncDirectory (Path.GetDirectoryName output)

                                    Console.Out.Write(
                                        JsonSerializer.Serialize
                                            {|
                                                schema = "fsgg.telemetry.host-backup-result/1"
                                                output = output
                                                manifestSha256 =
                                                    digestFile (Path.Combine(output, "backup-manifest.json"))
                                            |}
                                        + "\n"
                                    )

                                    0
                            finally
                                if Directory.Exists temporary then
                                    Directory.Delete(temporary, true)
                    with _ ->
                        resultExit "backup-integrity-failed" (Error [ "backup creation failed" ]))
        | [ "restore"; "--config"; path; "--input"; input; "--state-root"; stateRoot ] ->
            match load path with
            | Error errors -> resultExit "invalid-configuration" (Error errors)
            | Ok config ->
                withLock config (fun () ->
                    try
                        if
                            not (Path.IsPathFullyQualified input)
                            || not (Directory.Exists input)
                            || hasSymlinkAncestor input
                            || not (Path.IsPathFullyQualified stateRoot)
                            || Directory.Exists stateRoot
                            || File.Exists stateRoot
                            || hasSymlinkAncestor stateRoot
                        then
                            resultExit "backup-integrity-failed" (Error [ "restore paths are invalid" ])
                        else
                            let temporary = stateRoot + ".tmp-" + Guid.NewGuid().ToString("N")

                            try
                                let manifestPath = Path.Combine(input, "backup-manifest.json")

                                if not (File.Exists manifestPath) || FileInfo(manifestPath).Length > 1024L * 1024L then
                                    resultExit "backup-integrity-failed" (Error [ "backup manifest unavailable" ])
                                else
                                    let manifestBytes = File.ReadAllBytes manifestPath
                                    use document = JsonDocument.Parse manifestBytes
                                    let root = document.RootElement
                                    let names = root.EnumerateObject() |> Seq.map _.Name |> Seq.toArray
                                    let frozenManifestDigest = digestBytes manifestBytes
                                    let createdAt = root.GetProperty("createdAt").GetString()
                                    let mutable parsedCreatedAt = DateTimeOffset.MinValue

                                    let declared =
                                        root.GetProperty("workspaces").EnumerateArray()
                                        |> Seq.map (fun entry ->
                                            let entryNames = entry.EnumerateObject() |> Seq.map _.Name |> Seq.toArray

                                            if
                                                entryNames.Length <> 3
                                                || Array.distinct entryNames |> Array.length <> 3
                                                || Set.ofArray entryNames
                                                   <> set["workspaceId"
                                                          "path"
                                                          "manifestSha256"]
                                            then
                                                invalidOp "invalid workspace manifest entry"

                                            entry.GetProperty("workspaceId").GetString(),
                                            entry.GetProperty("path").GetString(),
                                            entry.GetProperty("manifestSha256").GetString())
                                        |> Seq.truncate 129
                                        |> Seq.toArray

                                    let expected = config.Stores |> Array.map _.WorkspaceId |> Array.sort

                                    let actual =
                                        declared |> Array.map (fun (workspace, _, _) -> workspace) |> Array.sort

                                    let actualEntries =
                                        Directory.EnumerateFileSystemEntries(input, "*", SearchOption.TopDirectoryOnly)
                                        |> Seq.truncate 131
                                        |> Seq.toArray

                                    let actualNames = actualEntries |> Array.map Path.GetFileName |> Array.sort

                                    let expectedNames =
                                        Array.append [| "backup-manifest.json" |] expected |> Array.sort

                                    let schemaMin = root.GetProperty("supportedStoreSchemaMin").GetInt32()
                                    let schemaMax = root.GetProperty("supportedStoreSchemaMax").GetInt32()

                                    let invalidManifest =
                                        names.Length <> 7
                                        || Array.distinct names |> Array.length <> 7
                                        || Set.ofArray names
                                           <> set["schema"
                                                  "hostVersion"
                                                  "supportedStoreSchemaMin"
                                                  "supportedStoreSchemaMax"
                                                  "configMetadataSha256"
                                                  "createdAt"
                                                  "workspaces"]
                                        || root.GetProperty("schema").GetString() <> "fsgg.telemetry.host-backup-set/1"
                                        || String.IsNullOrWhiteSpace(root.GetProperty("hostVersion").GetString())
                                        || root.GetProperty("configMetadataSha256").GetString()
                                           <> configMetadataDigest config
                                        || isNull createdAt
                                        || not (
                                            DateTimeOffset.TryParse(
                                                createdAt,
                                                Globalization.CultureInfo.InvariantCulture,
                                                Globalization.DateTimeStyles.RoundtripKind,
                                                &parsedCreatedAt
                                            )
                                        )
                                        || declared.Length <> expected.Length
                                        || actual <> expected
                                        || Array.distinct actual |> Array.length <> actual.Length
                                        || actualNames <> expectedNames
                                        || actualEntries
                                           |> Array.exists (fun entry ->
                                               if Directory.Exists entry then
                                                   not (isNull (DirectoryInfo(entry).LinkTarget))
                                               else
                                                   not (isNull (FileInfo(entry).LinkTarget)))
                                        || declared
                                           |> Array.exists (fun (workspace, relative, digest) ->
                                               isNull workspace
                                               || not (FS.GG.Coord.TelemetryReceipt.validId workspace)
                                               || relative <> workspace
                                               || not (isLowerSha256 digest)
                                               || digestFile (Path.Combine(input, relative, "manifest.json"))
                                                  <> digest)

                                    if invalidManifest then
                                        resultExit "backup-integrity-failed" (Error [ "backup manifest invalid" ])
                                    elif not (
                                        (schemaMin = 9 && schemaMax = 9)
                                        || (schemaMin = 10 && schemaMax = 10)
                                        || (schemaMin = 10 && schemaMax = 12)
                                    ) then
                                        resultExit "restore-incompatible" (Error [ "backup schema is incompatible" ])
                                    else
                                        Directory.CreateDirectory temporary |> ignore

                                        if not (OperatingSystem.IsWindows()) then
                                            File.SetUnixFileMode(
                                                temporary,
                                                UnixFileMode.UserRead
                                                ||| UnixFileMode.UserWrite
                                                ||| UnixFileMode.UserExecute
                                            )

                                        let mutable failure = None

                                        for store in config.Stores do
                                            if failure.IsNone then
                                                match
                                                    TelemetryStoreApplication.restoreReceiptStore
                                                        (Path.Combine(input, store.WorkspaceId))
                                                        (Path.Combine(temporary, store.WorkspaceId))
                                                        (assessmentFor stateRoot)
                                                        store.WorkspaceId
                                                with
                                                | Ok _ ->
                                                    let _, _, declaredDigest =
                                                        declared
                                                        |> Array.find (fun (workspace, _, _) ->
                                                            workspace = store.WorkspaceId)

                                                    if
                                                        digestFile (
                                                            Path.Combine(input, store.WorkspaceId, "manifest.json")
                                                        )
                                                        <> declaredDigest
                                                    then
                                                        failure <- Some [ "backup input changed during restore" ]
                                                | Error errors -> failure <- Some errors

                                        match failure with
                                        | Some [ "backup-incompatible" ] ->
                                            resultExit
                                                "restore-incompatible"
                                                (Error [ "backup schema is incompatible" ])
                                        | Some errors -> resultExit "backup-integrity-failed" (Error errors)
                                        | None when digestFile manifestPath <> frozenManifestDigest ->
                                            resultExit
                                                "backup-integrity-failed"
                                                (Error [ "backup input changed during restore" ])
                                        | None ->
                                            syncDirectory temporary
                                            Directory.Move(temporary, stateRoot)
                                            syncDirectory (Path.GetDirectoryName stateRoot)

                                            Console.Out.Write(
                                                JsonSerializer.Serialize
                                                    {|
                                                        schema = "fsgg.telemetry.host-restore-set/1"
                                                        root = stateRoot
                                                        sourceManifestSha256 = frozenManifestDigest
                                                    |}
                                                + "\n"
                                            )

                                            0
                            finally
                                if Directory.Exists temporary then
                                    Directory.Delete(temporary, true)
                    with _ ->
                        resultExit "backup-integrity-failed" (Error [ "backup restore failed" ]))
        | _ ->
            writeError "usage" [ "invalid command" ]
            2

    let private productionDeliveryTransport token =
        let transport = new Transport.HttpTransport("https://api.github.com", token)
        (transport :> IDisposable), (transport :> Transport.ISinglePageGitHubTransport)

    let runWithDependencies argv assessmentFor deliveryTransportFor =
        runWithDependenciesAt DateTimeOffset.UtcNow argv assessmentFor deliveryTransportFor

    let runWithAssessment argv assessmentFor =
        runWithDependencies argv assessmentFor productionDeliveryTransport

    let run argv = runWithAssessment argv TelemetryStoreApplication.assessProductionRoot

module Program =
    [<EntryPoint>]
    let main argv =
        let servePath =
            match List.ofArray argv with
            | [ "serve"; "--config"; path ] -> Some path
            | [ path ] -> Some path
            | _ -> None

        match servePath with
        | None -> Operations.run argv
        | Some path ->
            match Configuration.load path with
            | Error _ -> 2
            | Ok config when config.BrowserPrincipals.Length = 0 -> 2
            | Ok config ->
                try
                    match Runtime.ServiceLock.Acquire config.ServiceLockPath with
                    | Error _ -> 4
                    | Ok serviceLock ->
                        use serviceLock = serviceLock
                        let credentials = Configuration.credentials config
                        let browserOptions = BrowserComposition.options config

                        use browserSecurity =
                            new BrowserSecurity.Service(browserOptions, config.BrowserPrincipals)

                        match Runtime.recover config with
                        | Error _ -> 3
                        | Ok() when
                            config.Stores
                            |> Array.exists (fun store ->
                                BrowserComposition.snapshot config store.WorkspaceId None |> Result.isError)
                            ->
                            3
                        | Ok() ->
                            use state = new Runtime.HostState(config)
                            state.Ready <- true
                            state.StartDrain()

                            use certificate =
                                System.Security.Cryptography.X509Certificates.X509CertificateLoader.LoadPkcs12FromFile(
                                    config.CertificatePath,
                                    File.ReadAllText(config.CertificatePasswordFile).Trim()
                                )

                            let builder = Hosting.createBuilder config.ListenUrl certificate
                            let app = builder.Build()
                            Endpoints.configure app state credentials
                            BrowserEndpoints.map app browserOptions browserSecurity (BrowserComposition.snapshot config)
                            app.Run()
                            0
                with :? IOException ->
                    4
