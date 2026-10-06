namespace FS.GG.Telemetry.Host

open System
open System.IO
open System.Security.Cryptography
open System.Text
open System.Text.Json
open System.Text.Json.Nodes
open System.Threading
open System.Threading.Tasks
open FS.GG.Coord
open FS.GG.Coord.Cli
open FS.GG.Telemetry

module internal NativeResponsesCollection =
    let private policySha256 = "7a0e6970101b8cc9343c23ebe5d273e183cf4535b4dc09fca4d48a0bee7ae071"
    // Replaced only with the root-selected, independently qualified Responses verifier
    // source digest. AppServer/exec or caller-supplied module declarations cannot activate.
    let private verifierSha256 = "599034490c93333875b169c7fc4d3e873e3d911fe5571ad90127ea86b1e8b373"
    let private sha (bytes: byte array) = Convert.ToHexString(SHA256.HashData bytes).ToLowerInvariant()
    let private utf8 = UTF8Encoding(false, true)
    let private require condition reason = if not condition then invalidOp reason
    let private unwrap = function Ok value -> value | Error errors -> invalidOp(String.concat ";" errors)
    let private exact (value: JsonElement) names =
        require (value.ValueKind = JsonValueKind.Object) "responses-object-required"
        let rows = value.EnumerateObject() |> Seq.toArray
        require (rows.Length = List.length names && (rows |> Array.map _.Name |> Set.ofArray) = Set.ofList names)
            "responses-closed-object"
    let rec private unique (value: JsonElement) =
        match value.ValueKind with
        | JsonValueKind.Object ->
            let rows = value.EnumerateObject() |> Seq.toArray
            require ((rows |> Array.map _.Name |> Array.distinct |> Array.length) = rows.Length) "responses-duplicate-property"
            rows |> Array.iter (fun row -> unique row.Value)
        | JsonValueKind.Array -> value.EnumerateArray() |> Seq.iter unique
        | _ -> ()
    let private document (bytes: byte array) =
        let parsed = JsonDocument.Parse(ReadOnlyMemory<byte> bytes, JsonDocumentOptions(MaxDepth = 32))
        try unique parsed.RootElement; parsed with _ -> parsed.Dispose(); reraise()
    let private text (name: string) (value: JsonElement) = value.GetProperty(name).GetString()
    let private number (name: string) (value: JsonElement) = value.GetProperty(name).GetInt64()
    let private digest (value: string) =
        not (isNull value) && value.Length = 64 && value <> String('0',64)
        && (value |> Seq.forall (fun c -> Char.IsAsciiDigit c || (c >= 'a' && c <= 'f')))
    let private boundedPrivate config path maximum = Configuration.readResponsesPrivateBytes config path maximum |> unwrap
    let private remaining (phase: DirectResponses.Phase) =
        require (phase.RemainingMilliseconds > 0) "responses-original-whole-deadline"

    type Installed =
        { Config: ResponsesCollectorInstallationConfig
          Principal: TelemetryReceipt.Principal
          Profile: byte array
          ProfileSha256: string
          InstructionsSha256: string
          SchemaSha256: string
          SchemaName: string
          ManagerSha256: string
          ResultSha256: string
          InstallationSha256: string
          ObservedAt: string }

    let private installed (phase: DirectResponses.Phase) configPath hostConfig storeRoot =
        remaining phase
        let config, principal = Configuration.loadResponsesCollectorInstallation configPath hostConfig |> unwrap
        // Only the selected independently replayed module source is eligible;
        // metadata still requires actual installed/static qualification below.
        require (digest verifierSha256 && config.NativeVerifier.ModuleSha256 = verifierSha256)
            "responses-independent-verifier-not-qualified"
        for path in [ config.NativeVerifier.RuntimeExecutablePath; config.NativeVerifier.ModulePath; config.NativeVerifier.RuntimeManifestPath ] do
            require (Configuration.responsesImmutableFile path) "responses-verifier-path-custody"
        require (NativeSourceVerification.validateResponsesManifest config.NativeVerifier) "responses-verifier-closure-unavailable"
        let profileBytes = boundedPrivate configPath config.CapabilityProfilePath 65536
        let resultBytes = boundedPrivate configPath config.CapabilityResultPath 65536
        let managerBytes = boundedPrivate configPath (configPath + ".native-collector.receipt.json") 65536
        let sidecarBytes = boundedPrivate configPath (configPath + ".native-collector.json") 16384
        let hostBytes = boundedPrivate configPath configPath 65536
        require (sha profileBytes = config.CapabilityProfileSha256 && sha resultBytes = config.CapabilityResultSha256)
            "responses-capability-bytes-changed"
        use profileDoc = document profileBytes
        use resultDoc = document resultBytes
        use managerDoc = document managerBytes
        let profile = profileDoc.RootElement
        let result = resultDoc.RootElement
        let manager = managerDoc.RootElement
        exact profile [ "schema"; "sourceVariant"; "provider"; "model"; "effort"; "countEndpoint"; "generationEndpoint"
                        "inputTokenLimit"; "outputTokenLimit"; "wholeMilliseconds"; "networkMilliseconds"
                        "requestPolicySha256"; "instructionsSha256"; "responseSchemaSha256"; "responseSchemaName"
                        "verifierModuleSha256"; "verifierRuntimeManifestSha256"; "installedRoots"; "installedFiles" ]
        require (text "schema" profile = "fsgg.telemetry.responses-capability-profile/1"
                 && text "sourceVariant" profile = config.SourceVariant && text "provider" profile = config.Provider
                 && text "model" profile = config.Model && text "effort" profile = config.Effort
                 && text "countEndpoint" profile = config.CountEndpoint && text "generationEndpoint" profile = config.GenerationEndpoint
                 && number "inputTokenLimit" profile = 8000L && number "outputTokenLimit" profile = 1500L
                 && number "wholeMilliseconds" profile = 60000L && number "networkMilliseconds" profile = 55000L
                 && text "requestPolicySha256" profile = policySha256
                 && text "verifierModuleSha256" profile = verifierSha256
                 && text "verifierRuntimeManifestSha256" profile = config.NativeVerifier.RuntimeManifestSha256)
            "responses-profile-correspondence"
        for name in [ "instructionsSha256"; "responseSchemaSha256" ] do
            require (digest(text name profile)) "responses-profile-request-pin"
        require (not (String.IsNullOrWhiteSpace(text "responseSchemaName" profile))) "responses-profile-schema-name"
        let files = profile.GetProperty "installedFiles"
        let roots = profile.GetProperty("installedRoots").EnumerateArray() |> Seq.map _.GetString() |> Seq.toArray
        require (roots.Length > 0 && roots.Length <= 8 && (Array.distinct roots |> Array.length) = roots.Length) "responses-installed-roots"
        let below (root: string) (path: string) = path.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar) + string Path.DirectorySeparatorChar, StringComparison.Ordinal)
        for root in roots do
            require (Path.IsPathFullyQualified root && Path.GetFullPath root = root && Directory.Exists root
                     && isNull (DirectoryInfo(root).LinkTarget)) "responses-installed-root-shape"
            for other in roots do if root <> other then require (not (below root other)) "responses-installed-root-overlap"
        let actual = ResizeArray<string>()
        let mutable entryCount = 0
        let rec inventory directory =
            remaining phase
            for path in Directory.EnumerateFileSystemEntries directory do
                entryCount <- entryCount + 1
                require (actual.Count < 512 && entryCount <= 4096) "responses-installed-inventory-bound"
                let attributes = File.GetAttributes path
                require ((attributes &&& FileAttributes.ReparsePoint) = enum 0) "responses-installed-link"
                if (attributes &&& FileAttributes.Directory) <> enum 0 then inventory path
                else actual.Add path
        roots |> Array.iter inventory
        require (files.ValueKind = JsonValueKind.Array && files.GetArrayLength() > 0 && files.GetArrayLength() <= 512)
            "responses-installed-file-bound"
        let expected = ResizeArray<string>()
        let mutable total = 0L
        let components = System.Collections.Generic.Dictionary<string,string>(StringComparer.Ordinal)
        for row in files.EnumerateArray() do
            exact row [ "path"; "bytes"; "sha256"; "components" ]
            let path = text "path" row
            let size = number "bytes" row
            let expectedDigest = text "sha256" row
            require (size > 0L && size <= 200L*1024L*1024L && digest expectedDigest
                     && Configuration.responsesImmutableFile path && (roots |> Array.exists (fun root -> below root path)))
                "responses-installed-file-custody"
            require (expected.Count = 0 || String.CompareOrdinal(expected.[expected.Count-1],path) < 0) "responses-installed-file-order"
            expected.Add path
            total <- total + size
            require (total <= 200L*1024L*1024L) "responses-installed-total-bound"
            remaining phase
            use stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read)
            require (stream.Length = size && Convert.ToHexString(SHA256.HashData stream).ToLowerInvariant() = expectedDigest)
                "responses-installed-file-changed"
            for component in row.GetProperty("components").EnumerateArray() do
                let name = component.GetString()
                require (List.contains name [ "host"; "client"; "core"; "store" ] && components.TryAdd(name,path)) "responses-installed-component"
        require (Set.ofSeq expected = Set.ofSeq actual) "responses-installed-inventory-mismatch"
        for name, assembly in [ "host", typeof<HostConfig>.Assembly; "client", typeof<DirectResponses.Phase>.Assembly
                                "core", typeof<NativeResponses.Request>.Assembly
                                "store", typeof<TelemetryStoreApplication.NativeCollectorDispatch>.Assembly ] do
            require (components.ContainsKey name && components.[name] = assembly.Location) "responses-loaded-component-mismatch"
        let filesDigest = sha(JsonSerializer.SerializeToUtf8Bytes files)
        exact result [ "schema"; "sourceVariant"; "profileSha256"; "verifierRuntimeManifestSha256"; "verifierModuleSha256"
                       "installedFilesSha256"; "scenarioResults"; "ownedCustodyClean"; "resourceFailed"
                       "elapsedMilliseconds"; "originalWholeMilliseconds"; "operationSpecificCaptureProduced" ]
        require (text "schema" result = "fsgg.telemetry.responses-static-qualification/1"
                 && text "sourceVariant" result = config.SourceVariant && text "profileSha256" result = sha profileBytes
                 && text "verifierRuntimeManifestSha256" result = config.NativeVerifier.RuntimeManifestSha256
                 && text "verifierModuleSha256" result = verifierSha256 && text "installedFilesSha256" result = filesDigest
                 && result.GetProperty("ownedCustodyClean").GetBoolean() && not(result.GetProperty("resourceFailed").GetBoolean())
                 && not(result.GetProperty("operationSpecificCaptureProduced").GetBoolean())
                 && number "originalWholeMilliseconds" result > 0L && number "originalWholeMilliseconds" result <= 60000L
                 && number "elapsedMilliseconds" result >= 0L && number "elapsedMilliseconds" result < number "originalWholeMilliseconds" result)
            "responses-static-qualification-mismatch"
        let roster = [ "bounded-client-wire-cardinality"; "cancellation-retirement"; "credential-role-separation"
                       "current-installed-closure"; "denied-count-no-generation"; "request-policy-caps"
                       "verifier-mutated-capture-refused"; "verifier-stale-snapshot-refused"; "verifier-valid-capture" ]
        let cases = result.GetProperty("scenarioResults").EnumerateArray() |> Seq.toArray
        require (cases.Length = roster.Length) "responses-static-roster"
        for row,name in Array.zip cases (List.toArray roster) do
            exact row [ "name"; "outcome"; "evidenceSha256" ]
            require (text "name" row = name && text "outcome" row = "pass" && digest(text "evidenceSha256" row)) "responses-static-case"
        exact manager [ "schema"; "status"; "ownerUid"; "hostConfigSha256"; "sidecarSha256"; "sourceVariant"
                        "credentialReference"; "workspaceId"; "producerId"; "streamId"; "grantId"; "grantGeneration"
                        "providerCredentialReference"; "providerCredentialFile"; "capabilityProfileSha256"; "capabilityResultSha256"
                        "verifierRuntimeManifestSha256"; "verifierModuleSha256"; "installedFilesSha256"
                        "sourceVerification"; "snapshotOrigin"; "activationAuthorized" ]
        require (text "schema" manager = "fsgg.telemetry.native-collector-installation-receipt/4"
                 && text "status" manager = "installed" && number "ownerUid" manager = int64(Configuration.responsesOwnerUid())
                 && text "hostConfigSha256" manager = sha hostBytes && text "sidecarSha256" manager = sha sidecarBytes
                 && text "sourceVariant" manager = config.SourceVariant && text "credentialReference" manager = config.CredentialReference
                 && text "workspaceId" manager = principal.Scope.Workspace && text "producerId" manager = principal.Scope.Producer
                 && text "streamId" manager = principal.Scope.Stream && text "grantId" manager = principal.GrantId.Value
                 && number "grantGeneration" manager = principal.GrantGeneration.Value
                 && text "providerCredentialReference" manager = config.ProviderCredentialReference
                 && text "providerCredentialFile" manager = config.ProviderCredentialFile
                 && text "capabilityProfileSha256" manager = sha profileBytes && text "capabilityResultSha256" manager = sha resultBytes
                 && text "verifierRuntimeManifestSha256" manager = config.NativeVerifier.RuntimeManifestSha256
                 && text "verifierModuleSha256" manager = verifierSha256 && text "installedFilesSha256" manager = filesDigest
                 && text "sourceVerification" manager = "unknown" && text "snapshotOrigin" manager = "unknown"
                 && not(manager.GetProperty("activationAuthorized").GetBoolean())) "responses-manager-receipt-mismatch"
        remaining phase
        TelemetryStoreApplication.validateResponsesCollectorPrincipal storeRoot
            (TelemetryStoreApplication.assessProductionRoot storeRoot) principal |> unwrap
        { Config = config; Principal = principal; Profile = profileBytes; ProfileSha256 = sha profileBytes
          InstructionsSha256 = text "instructionsSha256" profile; SchemaSha256 = text "responseSchemaSha256" profile
          SchemaName = text "responseSchemaName" profile
          ManagerSha256 = sha managerBytes; ResultSha256 = sha resultBytes
          InstallationSha256 = sha(JsonSerializer.SerializeToUtf8Bytes [| sha hostBytes; sha sidecarBytes; sha profileBytes; sha resultBytes; filesDigest; config.NativeVerifier.RuntimeManifestSha256; verifierSha256 |])
          ObservedAt = DateTimeOffset.UtcNow.ToString("O") }

    type OriginEvidence =
        { Principal: TelemetryReceipt.Principal
          ManagerReceiptSha256: string
          CapabilityProfileSha256: string
          CapabilityResultSha256: string
          InstallationSha256: string
          ObservedAt: string
          ExpiresAt: string }
    type OperationEvidence =
        { OperationId: string
          ItemId: string
          OriginalItemId: string
          InvocationId: string
          DispatchRef: byte array
          ClaimRef: byte array
          GenerationRequestSha256: string
          CountRequestSha256: string
          CountResponseSha256: string
          ResponseSha256: string
          ResponseBytes: int
          CaptureSha256: string
          VerificationSha256: string
          ObservedAt: string }

    type Failure = { Errors: string list; ClaimAttemptId: string option; ClaimReceipt: string option }

    type Capture private (phase: DirectResponses.Phase, bytes: byte array, snapshot: byte array, observed: NativeResponses.ResponseObservation option,
                          failures: string list, claimReceipt: string, selected: Installed, operation: OperationEvidence, expiresAt: string) =
        member _.Phase = phase
        member _.Bytes = Array.copy bytes
        member _.Sha256 = sha bytes
        member _.SnapshotBytes = Array.copy snapshot
        member _.Response = observed
        member _.Failure = failures
        member _.ClaimReceipt = claimReceipt
        member internal _.Selected = selected
        member internal _.Operation = operation
        member internal _.ExpiresAt = expiresAt
        static member internal Create(phase, bytes, snapshot, observed, failures, claimReceipt, selected, operation, expiresAt) =
            Capture(phase, Array.copy bytes, Array.copy snapshot, observed, failures, claimReceipt, selected, operation, expiresAt)

    let collect configPath hostConfig storeRoot (runtimePrincipal: TelemetryReceipt.Principal)
                dispatchIdentity itemId (claimTemplate: byte array) (request: NativeResponses.Request)
                (cancellationToken: CancellationToken) = task {
        let phaseStartedAt = DateTimeOffset.UtcNow
        let phase = DirectResponses.beginPhase()
        let mutable claimReceipt = None
        let mutable claimAttemptId = None
        try
            let selected = installed phase configPath hostConfig storeRoot
            require (runtimePrincipal.Role = TelemetryReceipt.Generic
                     && runtimePrincipal.Scope.Workspace = selected.Principal.Scope.Workspace
                     && (hostConfig.Stores |> Array.exists (fun store -> store.WorkspaceId = runtimePrincipal.Scope.Workspace && store.Root = storeRoot)))
                "responses-queue-owner-store"
            let assessment = TelemetryStoreApplication.assessProductionRoot storeRoot
            let dispatch = TelemetryStoreApplication.resolveResponsesCollectorDispatch storeRoot assessment runtimePrincipal dispatchIdentity |> unwrap
            require (dispatch.ItemId = itemId && (dispatch.RequestedModel = selected.Config.Model || dispatch.RequestedModel = "sol") && dispatch.RequestedEffort = selected.Config.Effort) "responses-resolved-dispatch-policy"
            let invocationId = dispatch.InvocationId
            require (TelemetryReceipt.validId itemId && TelemetryReceipt.validId invocationId
                     && TelemetryReceipt.validId dispatchIdentity) "responses-operation-identities"
            require (not cancellationToken.IsCancellationRequested) "responses-cancelled-before-claim"
            let frozen = NativeResponses.freeze request |> unwrap
            use generationDoc = document frozen.GenerationBody
            let format = generationDoc.RootElement.GetProperty("text").GetProperty("format")
            let schema = utf8.GetBytes(format.GetProperty("schema").GetRawText())
            require (sha(utf8.GetBytes request.Instructions) = selected.InstructionsSha256
                     && sha schema = selected.SchemaSha256 && request.SchemaName = selected.SchemaName) "responses-installed-request-policy"
            require (not(isNull claimTemplate) && claimTemplate.Length > 0 && claimTemplate.Length <= 16384) "responses-claim-template-bound"
            use templateDoc = document claimTemplate
            let template = JsonNode.Parse(claimTemplate).AsObject()
            require (not(template.ContainsKey "dispatchRef")) "responses-caller-dispatch-reference"
            let operationId = Guid.NewGuid().ToString("N")
            template.["claimId"] <- JsonValue.Create operationId
            template.["invocationRef"] <- null
            require (template.["modelAlias"].GetValue<string>() = "sol") "responses-claim-model-alias"
            let support = template.["limitSupport"].AsObject()
            require (support.Count = 3 && ([ "inputTokens"; "outputTokens"; "seconds" ]
                     |> List.forall (fun name -> support.ContainsKey name && support.[name].GetValue<string>() = "enforced")))
                "responses-claim-limit-support"
            remaining phase
            claimAttemptId <- Some operationId
            let receipt = TelemetryStoreApplication.efficiencyAnalysisClaimProspective storeRoot assessment runtimePrincipal
                                dispatchIdentity itemId (JsonSerializer.SerializeToUtf8Bytes template) |> unwrap
            claimReceipt <- Some receipt
            use claimDoc = document (utf8.GetBytes receipt)
            let claim = claimDoc.RootElement
            require (text "state" claim = "claimed" && text "claimId" claim = operationId
                     && claim.GetProperty("invocationRef").ValueKind = JsonValueKind.Null) "responses-claimed-receipt-join"
            let packet = CanonicalJson.canonicalize(utf8.GetBytes request.InputText) |> Result.defaultWith (fun _ -> invalidOp "responses-evidence-packet-json")
            require (text "evidenceDigest" (claim.GetProperty "canonicalRequest") = "sha256:" + sha(utf8.GetBytes packet))
                "responses-claimed-evidence-mismatch"
            let claimRef = JsonSerializer.SerializeToNode
                               {| requestId = text "requestId" claim; claimId = operationId; revision = number "revision" claim
                                  contentDigest = text "contentDigest" claim
                                  owner = {| producer = runtimePrincipal.Scope.Producer; stream = runtimePrincipal.Scope.Stream |}
                                  generation = number "claimGeneration" claim |}
            let dispatchRef = JsonNode.Parse(claim.GetProperty("dispatchRef").GetRawText())
            remaining phase
            // Exact private provider secret is read only after actual installed/grant/claim joins.
            let keyBytes = boundedPrivate configPath selected.Config.ProviderCredentialFile 4096
            let providerKey = try utf8.GetString keyBytes finally Array.Clear keyBytes
            let! outcome = DirectResponses.execute phase frozen providerKey cancellationToken
            let capture = JsonObject()
            capture.["schema"] <- JsonValue.Create "fsgg.telemetry.responses-owned-capture/1"
            capture.["sourceVariant"] <- JsonValue.Create "openai-responses/1"
            capture.["operationId"] <- JsonValue.Create operationId
            capture.["invocationId"] <- JsonValue.Create invocationId
            capture.["originalItemId"] <- JsonValue.Create dispatch.OriginalItemId
            capture.["observedAt"] <- JsonValue.Create(DateTimeOffset.UtcNow.ToString("O"))
            capture.["dispatchRef"] <- dispatchRef.DeepClone()
            capture.["claimRef"] <- claimRef.DeepClone()
            for name,value in [ "countRequestBase64", frozen.CountBody; "generationRequestBase64", frozen.GenerationBody
                                "countResponseBase64", outcome.CountResponseBody; "generationResponseBase64", outcome.GenerationResponseBody ] do
                capture.[name] <- JsonValue.Create(Convert.ToBase64String value)
            capture.["countStatus"] <- outcome.CountStatus |> Option.map (fun value -> JsonValue.Create value :> JsonNode) |> Option.defaultValue null
            capture.["generationStatus"] <- outcome.GenerationStatus |> Option.map (fun value -> JsonValue.Create value :> JsonNode) |> Option.defaultValue null
            capture.["countBodyComplete"] <- JsonValue.Create outcome.CountBodyComplete
            capture.["generationBodyComplete"] <- JsonValue.Create outcome.GenerationBodyComplete
            capture.["stage"] <- JsonValue.Create(match outcome.Stage with
                | DirectResponses.NotSent -> "not-sent" | DirectResponses.CountSent -> "count-sent"
                | DirectResponses.CountAccepted -> "count-accepted" | DirectResponses.GenerationSent -> "generation-sent"
                | DirectResponses.ResponseCaptured -> "response-captured")
            capture.["elapsedMilliseconds"] <- JsonValue.Create phase.ElapsedMilliseconds
            capture.["wholeMilliseconds"] <- JsonValue.Create 60000
            capture.["networkMilliseconds"] <- JsonValue.Create 55000
            capture.["failures"] <- JsonSerializer.SerializeToNode(List.toArray outcome.Failure)
            capture.["cleanupFailures"] <- JsonSerializer.SerializeToNode(List.toArray outcome.CleanupFailure)
            let snapshot = JsonObject()
            snapshot.["schema"] <- JsonValue.Create "fsgg.telemetry.responses-verification-snapshot/1"
            for name in [ "sourceVariant"; "operationId"; "invocationId"; "originalItemId"; "dispatchRef"; "claimRef" ] do
                snapshot.[name] <- capture.[name].DeepClone()
            snapshot.["installedProfileSha256"] <- JsonValue.Create selected.ProfileSha256
            snapshot.["instructionsSha256"] <- JsonValue.Create selected.InstructionsSha256
            snapshot.["responseSchemaSha256"] <- JsonValue.Create selected.SchemaSha256
            snapshot.["responseSchemaName"] <- JsonValue.Create selected.SchemaName
            snapshot.["responseSchemaBase64"] <- JsonValue.Create(Convert.ToBase64String schema)
            let bytes = JsonSerializer.SerializeToUtf8Bytes capture
            let snapshotBytes = JsonSerializer.SerializeToUtf8Bytes snapshot
            require (bytes.Length <= 1100000 && snapshotBytes.Length <= 400000) "responses-owned-capture-bound"
            let operation: OperationEvidence =
                { OperationId = operationId; ItemId = itemId; OriginalItemId = dispatch.OriginalItemId; InvocationId = invocationId
                  DispatchRef = JsonSerializer.SerializeToUtf8Bytes dispatchRef; ClaimRef = JsonSerializer.SerializeToUtf8Bytes claimRef
                  GenerationRequestSha256 = frozen.GenerationSha256; CountRequestSha256 = frozen.CountSha256
                  CountResponseSha256 = sha outcome.CountResponseBody; ResponseSha256 = sha outcome.GenerationResponseBody
                  ResponseBytes = outcome.GenerationResponseBody.Length; CaptureSha256 = sha bytes; VerificationSha256 = ""
                  ObservedAt = capture.["observedAt"].GetValue<string>() }
            return Ok(Capture.Create(phase, bytes, snapshotBytes, outcome.Response, outcome.Failure @ outcome.CleanupFailure,
                                     receipt, selected, operation, phaseStartedAt.AddMilliseconds(60000.).ToString("O")))
        with
        | :? InvalidOperationException as error ->
            // Controlled internal reason codes only; never an HTTP exception/key/header message.
            let reason = if error.Message.StartsWith("responses-",StringComparison.Ordinal) then error.Message else "responses-local-refusal"
            return Error { Errors = (if claimReceipt.IsSome then [ reason; "responses-claim-consumed-no-automatic-retry" ] elif claimAttemptId.IsSome then [ reason; "responses-claim-outcome-unknown-no-automatic-retry" ] else [ reason ]); ClaimAttemptId = claimAttemptId; ClaimReceipt = claimReceipt }
        | _ -> return Error { Errors = (if claimReceipt.IsSome then [ "responses-local-failure"; "responses-claim-consumed-no-automatic-retry" ] elif claimAttemptId.IsSome then [ "responses-local-failure"; "responses-claim-outcome-unknown-no-automatic-retry" ] else [ "responses-local-failure" ]); ClaimAttemptId = claimAttemptId; ClaimReceipt = claimReceipt }
    }

    type VerifiedCapture private (origin: OriginEvidence, operation: OperationEvidence,
                                  observation: NativeResponses.ResponseObservation, accepted: bool) =
        member _.Origin = origin
        member _.Operation = { operation with DispatchRef = Array.copy operation.DispatchRef; ClaimRef = Array.copy operation.ClaimRef }
        member _.Observation = observation
        member _.CompletionAccepted = accepted
        static member internal Create(origin, operation, observation, accepted) = VerifiedCapture(origin, operation, observation, accepted)

    let private retain (phase: DirectResponses.Phase) (root: string) (operationId: string) (name: string) (bytes: byte array) =
        remaining phase
        let directory = Path.Combine(root, "responses-" + operationId)
        if name = "capture.json" then require (not(Directory.Exists directory) && not(File.Exists directory)) "responses-capture-collision"
        if not (Directory.Exists directory) then
            Directory.CreateDirectory(directory, UnixFileMode.UserRead ||| UnixFileMode.UserWrite ||| UnixFileMode.UserExecute) |> ignore
        require (isNull(DirectoryInfo(directory).LinkTarget)
                 && File.GetUnixFileMode(directory) = (UnixFileMode.UserRead ||| UnixFileMode.UserWrite ||| UnixFileMode.UserExecute))
            "responses-capture-directory-custody"
        let options = FileStreamOptions(Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None,
                                       UnixCreateMode = (UnixFileMode.UserRead ||| UnixFileMode.UserWrite))
        use stream = new FileStream(Path.Combine(directory, name), options)
        stream.Write bytes
        stream.Flush(true)
        remaining phase

    /// This operation accepts only the opaque capture created by the actual HTTP owner.
    /// Raw files, supplied flags and verifier exit status cannot construct VerifiedCapture.
    let verify configPath hostConfig storeRoot (capture: Capture) =
        let mutable retained = false
        let failure reason =
            if obj.ReferenceEquals(capture, null) then
                Error { Errors = [ reason ]; ClaimAttemptId = None; ClaimReceipt = None }
            else
                Error { Errors = capture.Failure @ [ reason; (if retained then "responses-owned-capture-retained" else "responses-owned-capture-retention-unavailable"); "responses-claim-consumed-no-automatic-retry" ]
                        ClaimAttemptId = Some capture.Operation.OperationId; ClaimReceipt = Some capture.ClaimReceipt }
        try
            let phase = capture.Phase
            remaining phase
            let original = capture.Selected
            // Retain owned bytes while original time remains, before expensive current-state revalidation.
            // An already exhausted phase reports retention unavailable; it does not obtain a new write budget.
            retain phase original.Config.EvidenceRoot capture.Operation.OperationId "capture.json" capture.Bytes
            retain phase original.Config.EvidenceRoot capture.Operation.OperationId "snapshot.json" capture.SnapshotBytes
            retained <- true
            let current = installed phase configPath hostConfig storeRoot
            require (current.Principal = original.Principal && current.InstallationSha256 = original.InstallationSha256
                     && current.ManagerSha256 = original.ManagerSha256) "responses-installed-authority-changed"
            let actual = NativeSourceVerification.verifyResponses phase original.Config.NativeVerifier original.Config.EvidenceRoot
                            capture.Bytes capture.SnapshotBytes |> unwrap
            retain phase original.Config.EvidenceRoot capture.Operation.OperationId "verification.json" actual
            use parsed = document actual
            let result = parsed.RootElement
            require (result.GetProperty("observationVerified").GetBoolean()) "responses-observation-unverified"
            let observed = capture.Response |> Option.defaultWith (fun () -> invalidOp "responses-provider-observation-absent")
            let responseId = observed.ResponseId |> Option.defaultWith (fun () -> invalidOp "responses-provider-identity-absent")
            require (text "responseId" result = responseId.Value && text "profileSha256" result = original.ProfileSha256
                     && text "requestSha256" result = capture.Operation.GenerationRequestSha256
                     && text "countRequestSha256" result = capture.Operation.CountRequestSha256
                     && text "countResponseSha256" result = capture.Operation.CountResponseSha256
                     && text "responseSha256" result = capture.Operation.ResponseSha256) "responses-verifier-observation-join"
            // Re-read current grant after independent replay, before creating any source authority.
            TelemetryStoreApplication.validateResponsesCollectorPrincipal storeRoot
                (TelemetryStoreApplication.assessProductionRoot storeRoot) original.Principal |> unwrap
            remaining phase
            let origin: OriginEvidence =
                { Principal = original.Principal; ManagerReceiptSha256 = original.ManagerSha256
                  CapabilityProfileSha256 = original.ProfileSha256; CapabilityResultSha256 = original.ResultSha256
                  InstallationSha256 = original.InstallationSha256; ObservedAt = original.ObservedAt; ExpiresAt = capture.ExpiresAt }
            Ok(VerifiedCapture.Create(origin, { capture.Operation with VerificationSha256 = sha actual }, observed,
                                      result.GetProperty("accepted").GetBoolean()))
        with
        | :? InvalidOperationException as error when error.Message.StartsWith("responses-", StringComparison.Ordinal) -> failure error.Message
        | _ -> failure "responses-owned-verification-refused"
