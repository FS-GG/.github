namespace FS.GG.Telemetry.Host

open System
open System.Text
open System.Text.Json
open System.Text.Json.Nodes
open System.Threading
open FS.GG.Coord
open FS.GG.Coord.Cli

module internal NativeResponsesEntry =
    let private unwrap = function Ok value -> value | Error errors -> invalidOp(String.concat ";" errors)
    let private require condition reason = if not condition then invalidOp reason
    let private bytes (node: JsonNode) = JsonSerializer.SerializeToUtf8Bytes node
    let private node (raw: byte array) = JsonNode.Parse raw
    let private text (name: string) (value: JsonNode) = value.[name].GetValue<string>()
    let private remaining (capture: NativeResponsesCollection.Capture) =
        require (capture.Phase.RemainingMilliseconds > 0) "responses-original-whole-deadline"
    let private cas (record: JsonNode) =
        JsonSerializer.SerializeToNode
            {| requestId = text "requestId" record; expectedRevision = record.["revision"].GetValue<int64>()
               expectedContentDigest = text "contentDigest" record |}
    let private envelope (principal: TelemetryReceipt.Principal) (batchId: string) (events: JsonArray) =
        let payload = JsonObject()
        payload.["schema"] <- JsonValue.Create TelemetryStore.BatchSchema
        payload.["ingestId"] <- JsonValue.Create("receipt-" + batchId)
        payload.["sourceIdentity"] <- JsonValue.Create principal.Scope.Producer
        payload.["generation"] <- JsonValue.Create "g1"
        payload.["cursor"] <- JsonValue.Create batchId
        payload.["eventCount"] <- JsonValue.Create events.Count
        payload.["events"] <- events
        let result = JsonObject()
        result.["schema"] <- JsonValue.Create TelemetryReceipt.Schema
        result.["workspaceId"] <- JsonValue.Create principal.Scope.Workspace
        result.["producerId"] <- JsonValue.Create principal.Scope.Producer
        result.["streamId"] <- JsonValue.Create principal.Scope.Stream
        result.["batchId"] <- JsonValue.Create batchId
        result.["payload"] <- payload
        bytes result

    let private apply storeRoot assessment (capture: NativeResponsesCollection.Capture) (principal: TelemetryReceipt.Principal) raw =
        remaining capture
        let parsed = TelemetryReceipt.parse raw |> unwrap
        require (parsed.Scope = principal.Scope) "responses-receipt-owner-mismatch"
        TelemetryStoreApplication.submitReceiptPrincipal storeRoot assessment principal raw |> unwrap |> ignore
        remaining capture
        TelemetryStoreApplication.drainReceipts storeRoot assessment principal.Scope.Workspace |> unwrap |> ignore
        remaining capture
        let receipt = TelemetryStoreApplication.lookupReceipt storeRoot assessment principal.Scope parsed.BatchId |> unwrap |> JsonNode.Parse
        require (text "status" receipt = "applied" && text "digest" receipt = parsed.Digest) "responses-receipt-not-applied"

    let private settleUnattached storeRoot assessment principal (capture: NativeResponsesCollection.Capture) =
        remaining capture
        let claim = JsonNode.Parse capture.ClaimReceipt
        require (isNull claim.["invocationRef"]) "responses-unverified-claim-already-attached"
        let value = JsonObject()
        value.["schema"] <- JsonValue.Create "fsgg.telemetry.efficiency-analysis-settle-input/1"
        value.["cas"] <- cas claim
        value.["claimId"] <- claim.["claimId"].DeepClone()
        value.["state"] <- JsonValue.Create "failed"
        value.["resultAssessmentRef"] <- null
        value.["reason"] <- JsonValue.Create "responses-provider-observation-unverified-outcome-unknown"
        value.["usageRefs"] <- JsonArray()
        value.["invocationOutcome"] <- JsonValue.Create "unknown"
        value.["reconciliationRefs"] <- JsonArray()
        value.["authority"] <- claim.["canonicalRequest"].["authority"].DeepClone()
        value.["settledAt"] <- JsonValue.Create(DateTimeOffset.UtcNow.ToString("O"))
        value.["invocationRef"] <- null
        let result = TelemetryStoreApplication.efficiencyAnalysis storeRoot assessment principal "settle" (bytes value) None |> unwrap
        remaining capture
        result

    let renderAssessmentFact (itemId: string) (operationId: string) (observedAt: string)
                             (canonicalRequestBytes: byte array) (assessmentBytes: byte array) : Result<byte array, string list> =
        try
            let request = node canonicalRequestBytes
            let fact = JsonObject()
            fact.["kind"] <- JsonValue.Create "efficiency-assessment/1"
            fact.["identity"] <- JsonValue.Create("responses-assessment-" + operationId)
            fact.["itemId"] <- JsonValue.Create itemId
            fact.["revision"] <- JsonValue.Create 1
            fact.["assessment"] <- node assessmentBytes
            fact.["provenance"] <- request.["authority"].DeepClone()
            fact.["observedAt"] <- JsonValue.Create observedAt
            Ok(bytes fact)
        with _ -> Error ["responses-assessment-fact-declaration-invalid"]

    let private assessmentFact (capture: NativeResponsesCollection.Capture) (verified: NativeResponsesCollection.VerifiedCapture)
                               (packet: NativeResponsesFacts.Packet) (claim: JsonNode) =
        require verified.CompletionAccepted "responses-assessment-not-completed"
        let output = verified.Observation.OutputText |> Option.defaultWith (fun () -> invalidOp "responses-assessment-output-absent")
        require (Encoding.UTF8.GetByteCount output <= 16384) "responses-assessment-byte-bound"
        let request = claim.["canonicalRequest"]
        let identity = "responses-assessment-" + verified.Operation.OperationId
        use captureDocument = JsonDocument.Parse(ReadOnlyMemory<byte> capture.Bytes)
        let generationBytes = Convert.FromBase64String(captureDocument.RootElement.GetProperty("generationRequestBase64").GetString())
        use generation = JsonDocument.Parse(ReadOnlyMemory<byte> generationBytes)
        let inputText = generation.RootElement.GetProperty("input").[0].GetProperty("content").GetString()
        // These are the exact frozen request bytes; the preclaim gate already requires
        // the retained evidence codec. Do not canonicalize or rewrite them after capture.
        let packetBytes = NativeResponsesCollection.validateInputPacket inputText |> unwrap
        let usage = packet.UsageRef |> Option.defaultWith (fun () -> invalidOp "responses-assessment-usage-absent") |> node
        let prepared =
            NativeResponsesAssessment.prepare
                { OutputText = output; PacketBytes = packetBytes; CanonicalRequestBytes = bytes request
                  RequestId = text "requestId" claim; AnalysisUsageRef = text "id" usage
                  ClaimedAt = text "claimedAt" claim; ObservedAt = verified.Operation.ObservedAt
                  OperationId = verified.Operation.OperationId; Producer = verified.Origin.Principal.Scope.Producer }
            |> unwrap
        let fact =
            renderAssessmentFact verified.Operation.ItemId verified.Operation.OperationId verified.Operation.ObservedAt
                (bytes request) prepared |> unwrap |> node
        identity, fact

    let run configPath (config: HostConfig) runtimeCredentialReference dispatchIdentity itemId requestPath =
        let mutable actualClaim: string option = None
        let mutable operationId: string option = None
        let mutable failureDetails: string list = []
        try
            let credential = config.Credentials |> Array.tryFind (fun row -> row.Reference = runtimeCredentialReference && not row.Revoked && row.Role = "generic")
                             |> Option.defaultWith (fun () -> invalidOp "responses-runtime-credential-unavailable")
            let store = config.Stores |> Array.tryFind (fun row -> row.WorkspaceId = credential.WorkspaceId)
                        |> Option.defaultWith (fun () -> invalidOp "responses-workspace-unavailable")
            let principal: TelemetryReceipt.Principal =
                { Scope = { Workspace = credential.WorkspaceId; Producer = credential.ProducerId; Stream = credential.StreamId }
                  Role = TelemetryReceipt.Generic; GrantId = Some credential.GrantId; GrantGeneration = Some credential.GrantGeneration }
            let raw = Configuration.readResponsesPrivateBytes configPath requestPath 400000 |> unwrap
            use parsed = JsonDocument.Parse(ReadOnlyMemory<byte> raw, JsonDocumentOptions(MaxDepth = 32))
            let root = parsed.RootElement
            let names = root.EnumerateObject() |> Seq.map _.Name |> Seq.toArray
            require (names.Length = 5 && Set.ofArray names = set [ "instructions"; "inputText"; "schemaName"; "schemaBase64"; "claimTemplate" ]) "responses-closed-input"
            let request: NativeResponses.Request =
                { Instructions = root.GetProperty("instructions").GetString(); InputText = root.GetProperty("inputText").GetString()
                  SchemaName = root.GetProperty("schemaName").GetString(); SchemaJson = Convert.FromBase64String(root.GetProperty("schemaBase64").GetString()) }
            let claimTemplate = Encoding.UTF8.GetBytes(root.GetProperty("claimTemplate").GetRawText())
            let captured = NativeResponsesCollection.collect configPath config store.Root principal dispatchIdentity itemId claimTemplate request CancellationToken.None
                           |> fun work -> work.GetAwaiter().GetResult()
            let capture =
                match captured with
                | Ok value -> actualClaim <- Some value.ClaimReceipt; value
                | Error refusal -> actualClaim <- refusal.ClaimReceipt; operationId <- refusal.ClaimAttemptId; failureDetails <- refusal.Errors; invalidOp(String.concat ";" refusal.Errors)
            use capturedDoc = JsonDocument.Parse(ReadOnlyMemory<byte> capture.Bytes)
            operationId <- Some(capturedDoc.RootElement.GetProperty("operationId").GetString())
            let assessment = TelemetryStoreApplication.assessProductionRoot store.Root
            let verified =
                match NativeResponsesCollection.verify configPath config store.Root capture with
                | Ok value -> value
                | Error refusal ->
                    failureDetails <- refusal.Errors
                    if capture.Phase.RemainingMilliseconds > 0 then
                        try actualClaim <- Some(settleUnattached store.Root assessment principal capture)
                        with _ -> failureDetails <- failureDetails @ [ "responses-failure-settlement-unconfirmed" ]
                    invalidOp "responses-owned-verification-refused"
            let packet = NativeResponsesFacts.build verified |> unwrap
            apply store.Root assessment capture verified.Origin.Principal packet.EnvelopeBytes
            remaining capture
            let claim = JsonNode.Parse capture.ClaimReceipt
            let attach = JsonObject()
            attach.["schema"] <- JsonValue.Create "fsgg.telemetry.efficiency-analysis-attach-invocation-input/1"
            attach.["cas"] <- cas claim
            attach.["claimId"] <- JsonValue.Create verified.Operation.OperationId
            attach.["dispatchRef"] <- node verified.Operation.DispatchRef
            attach.["invocationRef"] <- JsonValue.Create verified.Operation.InvocationId
            attach.["lineageRefs"] <- JsonArray(node packet.ObservationRef)
            attach.["authority"] <- claim.["canonicalRequest"].["authority"].DeepClone()
            attach.["attachedAt"] <- JsonValue.Create(DateTimeOffset.UtcNow.ToString("O"))
            let attached = TelemetryStoreApplication.efficiencyAnalysis store.Root assessment principal "attach-invocation" (bytes attach) None |> unwrap
            actualClaim <- Some attached
            let mutable assessmentId: string option = None
            let mutable completionFailure = None
            if verified.CompletionAccepted then
                try
                    let identity, fact = assessmentFact capture verified packet claim
                    apply store.Root assessment capture principal (envelope principal ("responses-assessment-" + verified.Operation.OperationId) (JsonArray(fact)))
                    assessmentId <- Some identity
                with _ -> completionFailure <- Some "responses-assessment-content-or-admission-refused"
            remaining capture
            let latest = JsonNode.Parse attached
            let settle = JsonObject()
            settle.["schema"] <- JsonValue.Create "fsgg.telemetry.efficiency-analysis-settle-input/1"
            settle.["cas"] <- cas latest
            settle.["claimId"] <- JsonValue.Create verified.Operation.OperationId
            settle.["state"] <- JsonValue.Create(if assessmentId.IsSome then "settled" else "failed")
            settle.["resultAssessmentRef"] <- assessmentId |> Option.map (fun value -> JsonValue.Create value :> JsonNode) |> Option.defaultValue null
            settle.["reason"] <- if assessmentId.IsSome then null else JsonValue.Create(Option.defaultValue "responses-observation-not-completed-assessment" completionFailure)
            settle.["usageRefs"] <- JsonArray(packet.UsageRef |> Option.map node |> Option.toArray)
            settle.["invocationOutcome"] <- JsonValue.Create(if assessmentId.IsSome then "completed" elif verified.Observation.Status = NativeResponses.Failed || verified.Observation.Status = NativeResponses.Cancelled then "failed" else "unknown")
            settle.["reconciliationRefs"] <- JsonArray()
            settle.["authority"] <- claim.["canonicalRequest"].["authority"].DeepClone()
            settle.["settledAt"] <- JsonValue.Create(DateTimeOffset.UtcNow.ToString("O"))
            settle.["invocationRef"] <- JsonValue.Create verified.Operation.InvocationId
            let settled = TelemetryStoreApplication.efficiencyAnalysis store.Root assessment principal "settle" (bytes settle) None |> unwrap
            actualClaim <- Some settled
            remaining capture
            Ok(JsonSerializer.Serialize {| schema = "fsgg.telemetry.responses-collection-result/1"; operationId = verified.Operation.OperationId
                                           responseId = (verified.Observation.ResponseId |> Option.map _.Value |> Option.defaultValue ""); observationVerified = true
                                           accepted = assessmentId.IsSome; captureSha256 = capture.Sha256; queue = JsonNode.Parse settled |})
        with _ ->
            // The consumed claim remains. Capture-retention gaps are explicit; never replay the generation.
            Error ([ "responses-collection-incomplete" ] @ failureDetails
                   @ (operationId |> Option.map (fun value -> "responses-operation:" + value) |> Option.toList)
                   @ (if actualClaim.IsSome then [ "responses-claim-consumed-no-automatic-retry" ] else []))
