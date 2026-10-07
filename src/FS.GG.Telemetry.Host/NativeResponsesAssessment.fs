namespace FS.GG.Telemetry.Host

open System
open System.Collections.Generic
open System.Text
open System.Text.Json
open System.Text.Json.Nodes
open System.Text.RegularExpressions
open FS.GG.Coord

module internal NativeResponsesAssessment =
    type Input =
        { OutputText: string
          PacketBytes: byte array
          CanonicalRequestBytes: byte array
          RequestId: string
          AnalysisUsageRef: string
          ClaimedAt: string
          ObservedAt: string
          OperationId: string
          Producer: string }

    let private require condition reason = if not condition then invalidOp reason
    let private utf8 = UTF8Encoding(false,true)
    let private parse (maximum: int) (raw: byte array) =
        require (not(isNull raw) && raw.Length>0 && raw.Length<=maximum) "assessment-json-byte-bound"
        utf8.GetString raw |> ignore
        use document = JsonDocument.Parse(ReadOnlyMemory<byte> raw,JsonDocumentOptions(MaxDepth=32))
        let rec unique (node: JsonElement) =
            match node.ValueKind with
            | JsonValueKind.Object ->
                let names = HashSet<string>(StringComparer.Ordinal)
                for field in node.EnumerateObject() do
                    require (names.Add field.Name) "assessment-duplicate-field"
                    unique field.Value
            | JsonValueKind.Array -> for value in node.EnumerateArray() do unique value
            | _ -> ()
        unique document.RootElement
        JsonNode.Parse(utf8.GetString raw)
    let private text (name: string) (node: JsonNode) = node.[name].GetValue<string>()
    let private canonical (node: JsonNode) =
        CanonicalJson.canonicalize(utf8.GetBytes(node.ToJsonString())) |> Result.defaultWith invalidOp
    let private reference (node: JsonNode) = text "id" node,text "kind" node,node.["revision"].GetValue<int64>()

    let prepare (input: Input) : Result<byte array,string list> =
        try
            let packet = parse 24576 input.PacketBytes
            let request = parse 65536 input.CanonicalRequestBytes
            let model = parse 16384 (utf8.GetBytes input.OutputText)
            require (text "schema" packet="fsgg.telemetry.efficiency-evidence-packet/1") "assessment-packet-schema"
            let digest = "sha256:" + CanonicalJson.sha256 input.PacketBytes
            require (text "evidenceDigest" request=digest && text "evidenceDigest" model=digest) "assessment-raw-evidence-mismatch"
            match EfficiencyEvidence.validate input.PacketBytes with
            | Error reason -> invalidOp("assessment-packet-refused:" + reason)
            | Ok _ -> ()
            require (text "requestId" request=input.RequestId) "assessment-request-id-mismatch"
            require (text "analysisPolicyVersion" request="efficiency-analysis-policy/1") "assessment-analysis-policy-mismatch"
            let keyParts = JsonArray()
            for name in ["itemId";"outcomeId";"outcomeEpoch";"scope"] do keyParts.Add(if isNull packet.["subject"].[name] then null else packet.["subject"].[name].DeepClone())
            keyParts.Add(JsonValue.Create digest)
            keyParts.Add(JsonValue.Create "efficiency-analysis-policy/1")
            use keyDocument = JsonDocument.Parse(keyParts.ToJsonString())
            let keyBytes = EfficiencyEvidence.encode keyDocument.RootElement |> Result.defaultWith invalidOp
            require (CanonicalJson.sha256 keyBytes=input.RequestId) "assessment-idempotency-key-mismatch"
            require (JsonNode.DeepEquals(model.["subject"],packet.["subject"])
                     && JsonNode.DeepEquals(model.["subject"],request.["subject"])) "assessment-subject-mismatch"
            require (JsonNode.DeepEquals(model.["coverage"],packet.["coverage"])
                     && JsonNode.DeepEquals(model.["omissions"],packet.["omissions"])) "assessment-coverage-or-omissions-altered"
            require (isNull model.["supersedes"]) "assessment-supersession-unavailable"
            let records = packet.["records"].AsArray()
            require (records.Count<=128) "assessment-packet-record-bound"
            let refs =
                records |> Seq.map (fun record ->
                    require (record.["analysisGenerated"].GetValue<bool>()=false) "assessment-analysis-record-substantive"
                    require (text "itemId" record=text "itemId" packet.["subject"]) "assessment-record-item-mismatch"
                    let actual = record.["ref"]
                    let canonicalRef = record.["canonicalRef"]
                    require (text "id" actual=text "id" canonicalRef
                             && actual.["revision"].GetValue<int64>()=canonicalRef.["revision"].GetValue<int64>()) "assessment-record-reference-mismatch"
                    reference actual) |> Set.ofSeq
            let ids = refs |> Set.map (fun (id,_,_) -> id)
            let ambiguous =
                ids |> Set.filter (fun id -> refs |> Seq.filter (fun (name,_,_) -> name=id) |> Seq.length |> fun count -> count>1)
            for id in ambiguous do
                require (refs |> Seq.filter (fun (name,_,_) -> name=id) |> Seq.forall (fun (_,kind,_) -> kind="process-review")) "assessment-ambiguous-evidence-id"
            let resolve (node: JsonNode) = require (Set.contains (reference node) refs) "assessment-unresolved-evidence-revision"
            for node in model.["evidenceRefs"].AsArray() do resolve node
            require (refs |> Seq.exists (fun (id,kind,_) -> id=text "outcomeId" packet.["subject"] && kind="outcome")) "assessment-outcome-unresolved"
            // No trusted deterministic metric cards or then-policy alternatives are supplied
            // by this retained-only caller. Refuse references instead of inventing witnesses.
            require (model.["metricRefs"].AsArray().Count=0) "assessment-metric-witness-unavailable"
            for finding in model.["findings"].AsArray() do
                for node in finding.["affectedObjects"].AsArray() do resolve node
                for name in ["evidenceRefs";"recoveryRefs"] do
                    for node in finding.[name].AsArray() do
                        let id = node.GetValue<string>()
                        require (Set.contains id ids && not(Set.contains id ambiguous)) "assessment-finding-reference-unresolved-or-ambiguous"
                let supported = List.contains (text "epistemicStatus" finding) ["observed";"supported-inference"]
                if supported then
                    require (finding.["evidenceRefs"].AsArray().Count>0) "assessment-supported-claim-without-evidence"
                    require (text "necessity" finding<>"avoidable") "assessment-then-policy-alternative-unavailable"
                else require (text "primaryCause" finding="unknown") "assessment-unsupported-primary-cause"
            let numeric (value: JsonNode) =
                if not(isNull value) then
                    require (not(Regex.IsMatch(value.GetValue<string>(),@"\d"))) "assessment-numeric-prose-refused"
            numeric model.["outcomeSynopsis"]
            for value in model.["wentWell"].AsArray() do numeric value
            for finding in model.["findings"].AsArray() do
                for name in ["summary";"uncertainty";"alternative"] do numeric finding.[name]
            for improvement in model.["improvements"].AsArray() do
                for name in ["mechanism";"validationMethod"] do numeric improvement.[name]
            let identity = "responses-assessment-" + input.OperationId
            model.["assessmentId"] <- JsonValue.Create identity
            model.["revision"] <- JsonValue.Create 1
            model.["supersedes"] <- null
            model.["provenance"] <- JsonSerializer.SerializeToNode
                {| producer=input.Producer;modelAlias="sol";promptVersion="responses-installed-instructions/1"
                   rubricVersion="efficiency-rubric/1";taxonomyVersion="efficiency-taxonomy/1"
                   analysisPolicyVersion="efficiency-analysis-policy/1";usageRefs=[|input.AnalysisUsageRef|]
                   startedAt=input.ClaimedAt;finishedAt=input.ObservedAt;validationResult="accepted" |}
            model.["lifecycle"] <- JsonSerializer.SerializeToNode
                {| state="partial";idempotencyKey=input.RequestId;failureReason=(null:string)
                   itemReviewRef=(null:string);generatedAt=input.ObservedAt |}
            model.["publication"] <- JsonSerializer.SerializeToNode {| visibility="private";policyVersion=(null:string) |}
            let fact = JsonObject()
            fact.["kind"] <- JsonValue.Create "efficiency-assessment/1"
            fact.["identity"] <- JsonValue.Create identity
            fact.["itemId"] <- JsonValue.Create(text "itemId" packet.["subject"])
            fact.["revision"] <- JsonValue.Create 1
            fact.["assessment"] <- model.DeepClone()
            fact.["provenance"] <- request.["authority"].DeepClone()
            fact.["observedAt"] <- JsonValue.Create input.ObservedAt
            use factDocument = JsonDocument.Parse(canonical fact)
            match EfficiencyInput.parseEvent factDocument.RootElement with
            | Error error -> Error ["assessment-schema-refused";error]
            | Ok _ -> Ok(utf8.GetBytes(canonical model))
        with
        | :? InvalidOperationException as error -> Error [error.Message]
        | :? JsonException -> Error ["assessment-json-malformed"]
        | :? ArgumentException -> Error ["assessment-input-invalid"]
        | :? KeyNotFoundException -> Error ["assessment-field-unavailable"]
        | :? NullReferenceException -> Error ["assessment-field-unavailable"]
