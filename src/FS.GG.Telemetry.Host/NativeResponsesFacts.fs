namespace FS.GG.Telemetry.Host

open System
open System.Text
open System.Text.Json.Nodes
open FS.GG.Coord

module internal NativeResponsesFacts =
    type Packet =
        { EnvelopeBytes: byte array
          OriginRef: byte array
          ObservationRef: byte array
          UsageRef: byte array option
          InventoryRef: byte array option
          SourceRef: byte array option }

    let private text (value: string) : JsonNode = JsonValue.Create value
    let private number (value: int64) : JsonNode = JsonValue.Create value
    let private nullable (value: string option) = value |> Option.map text |> Option.defaultValue null
    let private counter (value: int64 option) = value |> Option.map number |> Option.defaultValue null
    let private objectOf (fields: (string * JsonNode) list) =
        let node = JsonObject()
        // Shared declaration fields are reused across facts; each JSON tree owns
        // its own child nodes, including nested references and arrays.
        for key,value in fields do
            node.[key] <- if isNull value then null else value.DeepClone()
        node
    let private canonical (node: JsonNode) =
        match CanonicalJson.canonicalize (Encoding.UTF8.GetBytes(node.ToJsonString())) with
        | Ok value -> Encoding.UTF8.GetBytes value
        | Error error -> invalidOp error
    let private reference (node: JsonObject) =
        objectOf ["id",text(node.["identity"].GetValue<string>());"kind",text(node.["kind"].GetValue<string>());
                  "revision",number 0L;"contentDigest",text("sha256:" + CanonicalJson.sha256(canonical node))]
    let private status = function
        | NativeResponses.Completed -> "completed" | NativeResponses.Incomplete -> "incomplete"
        | NativeResponses.Failed -> "failed" | NativeResponses.Cancelled -> "cancelled"
        | NativeResponses.InProgress -> "in_progress" | NativeResponses.Queued -> "queued"
        | NativeResponses.UnknownStatus value -> Option.defaultValue "unknown" value

    let renderFixture (origin: NativeResponsesCollection.OriginEvidence)
                      (operation: NativeResponsesCollection.OperationEvidence)
                      (observation: NativeResponses.ResponseObservation) : Result<Packet,string list> =
        try
            let principal = origin.Principal
            if not (TelemetryReceipt.validPrincipal principal) || principal.Role <> TelemetryReceipt.NativeCollector then
                invalidOp "native-responses-facts-principal-invalid"
            if not (TelemetryReceipt.validId operation.OperationId) then invalidOp "native-responses-facts-operation-invalid"
            let responseId = observation.ResponseId |> Option.map (fun value -> value.Value) |> Option.defaultWith (fun () -> invalidOp "provider-response-id-unavailable")
            let id suffix = operation.OperationId + "-" + suffix
            let fact kind suffix fields =
                objectOf (["kind",text kind;"identity",text(id suffix);"revision",number 0L] @ fields)
            let item = ["itemId",text operation.ItemId;"invocationId",text operation.InvocationId]
            let resource = ["provider",text "openai";"sourceVariant",text "openai-responses/1";
                            "responseId",text responseId;"responseSha256",text operation.ResponseSha256]
            let originFact = fact "learn-installed-origin/1" "origin"
                                ["workspaceId",text principal.Scope.Workspace;"producerId",text principal.Scope.Producer;
                                 "streamId",text principal.Scope.Stream;"role",text "native-collector";
                                 "grantId",nullable principal.GrantId;"grantGeneration",counter principal.GrantGeneration;
                                 "managerReceiptSha256",text origin.ManagerReceiptSha256;
                                 "capabilityProfileSha256",text origin.CapabilityProfileSha256;
                                 "capabilityResultSha256",text origin.CapabilityResultSha256;
                                 "nativeCaptureSha256",text operation.CaptureSha256;
                                 "nativeVerificationSha256",text operation.VerificationSha256;
                                 "capabilityObservedAt",text origin.ObservedAt;"capabilityExpiresAt",text origin.ExpiresAt;
                                 "installationSha256",text origin.InstallationSha256;"nativeSourceVariant",text "openai-responses/1"]
            let observationFact = fact "runtime-provider-observation/1" "observation"
                                   (item @ resource @ ["generationRequestSha256",text operation.GenerationRequestSha256;
                                    "countRequestSha256",text operation.CountRequestSha256;"countResponseSha256",text operation.CountResponseSha256;
                                    "observedAt",text operation.ObservedAt;
                                    "providerCreatedAt",nullable(observation.ProviderCreatedAt |> Option.map (fun value -> value.ToString("O")));
                                    "status",text(status observation.Status)])
            let usage = observation.Usage
            let hasUsage = [usage.InputTokens;usage.OutputTokens;usage.TotalTokens;usage.CachedInputTokens;usage.CacheWriteInputTokens;usage.ReasoningOutputTokens] |> List.exists Option.isSome
            let usageFact =
                if not hasUsage then None
                else Some(fact "runtime-response-usage/1" "usage"
                         (item @ resource @ ["requestedModel",text NativeResponses.Model;"observedModel",nullable observation.ObservedModel;
                          "requestedEffort",text NativeResponses.Effort;"observedEffort",null;
                          "scope",text "provider-response";"provenance",text "openai-responses";
                          "input",counter usage.InputTokens;"cachedInput",counter usage.CachedInputTokens;
                          "cacheWriteInput",counter usage.CacheWriteInputTokens;"output",counter usage.OutputTokens;
                          "reasoning",counter usage.ReasoningOutputTokens;"total",counter usage.TotalTokens]))
            let expected () =
                let array = JsonArray()
                array.Add(objectOf ["responseId",text responseId;"responseSha256",text operation.ResponseSha256])
                array
            let inventory,source =
                match usageFact with
                | None -> None,None
                | Some usageNode ->
                    let binding = objectOf ["schema",text "fsgg.telemetry.native-inventory-source-binding/3";
                                  "producerIdentity",text "fsgg-work-roadmap-native-collector/1";
                                  "capturedAt",text operation.ObservedAt;"sourceVariant",text "openai-responses/1";
                                  "originalItemId",text operation.OriginalItemId;"invocationId",text operation.InvocationId;
                                  "revision",number 0L;"responseId",text responseId;
                                  "generationRequestSha256",text operation.GenerationRequestSha256;"countRequestSha256",text operation.CountRequestSha256;
                                  "countResponseSha256",text operation.CountResponseSha256;"responseSha256",text operation.ResponseSha256;
                                  "responseBytes",number(int64 operation.ResponseBytes);"operationBindingSha256",text operation.CaptureSha256;
                                  "claimRef",JsonNode.Parse(Encoding.UTF8.GetString operation.ClaimRef);"dispatchRef",JsonNode.Parse(Encoding.UTF8.GetString operation.DispatchRef);
                                  "providerObservationRef",reference observationFact;"usageRef",reference usageNode;
                                  "installedOriginRef",reference originFact;"expectedResponses",expected()]
                    let bindingBytes = canonical binding
                    let inventoryNode = fact "runtime-native-inventory/1" "inventory"
                                           (item @ ["inventoryId",text(id "inventory");"originalItemId",text operation.OriginalItemId;
                                            "page",number 1L;"pages",number 1L;"sourceVariant",text "openai-responses/1";
                                            "expectedResponses",expected();"expectedProvider",text "openai";
                                            "requestedModel",text NativeResponses.Model;"requestedEffort",text NativeResponses.Effort;
                                            "support",text "response-native-final-counters";"followupBaseline",number 0L;
                                            "capturedAt",text operation.ObservedAt;"sourceKind",text "provider-capability-and-dispatch-roster";
                                            "sourceDigest",text operation.CaptureSha256])
                    let sourceNode = fact "runtime-native-inventory-source/1" "source"
                                        (item @ ["inventoryId",text(id "inventory");"originalItemId",text operation.OriginalItemId;
                                         "sourceDigest",text operation.CaptureSha256;
                                         "sourceBinding",objectOf ["schema",text "fsgg.telemetry.native-inventory-source-binding/3";
                                          "producerIdentity",text "fsgg-work-roadmap-native-collector/1";
                                          "sha256",text(CanonicalJson.sha256 bindingBytes);"bytesBase64",text(Convert.ToBase64String bindingBytes)]])
                    Some inventoryNode,Some sourceNode
            let events = [Some originFact;Some observationFact;usageFact;inventory;source] |> List.choose (fun value -> value)
            let array = JsonArray()
            for event in events do array.Add(event.DeepClone())
            let batchId = id "facts"
            let batch = objectOf ["schema",text TelemetryStore.BatchSchema;"ingestId",text batchId;
                                  "sourceIdentity",text "fsgg-work-roadmap-native-collector";"generation",text "g1";
                                  "cursor",text batchId;"eventCount",number(int64 events.Length);"events",array]
            let envelope = objectOf ["schema",text TelemetryReceipt.Schema;"workspaceId",text principal.Scope.Workspace;
                                     "producerId",text principal.Scope.Producer;"streamId",text principal.Scope.Stream;
                                     "batchId",text batchId;"payload",batch]
            let bytes = canonical envelope
            // Use the real canonical decoder, not a parallel handwritten wire validator.
            match TelemetryReceipt.parse bytes with
            | Error errors -> Error errors
            | Ok _ -> Ok { EnvelopeBytes=bytes;OriginRef=canonical(reference originFact);ObservationRef=canonical(reference observationFact);
                           UsageRef=usageFact |> Option.map (reference >> canonical);
                           InventoryRef=inventory |> Option.map (reference >> canonical);SourceRef=source |> Option.map (reference >> canonical) }
        with
        | :? InvalidOperationException as error -> Error [error.Message]
        | :? System.Text.Json.JsonException -> Error ["native-responses-facts-reference-invalid"]
        | :? ArgumentException -> Error ["native-responses-facts-argument-invalid"]

    let build (capture: NativeResponsesCollection.VerifiedCapture) =
        renderFixture capture.Origin capture.Operation capture.Observation
