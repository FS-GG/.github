namespace FS.GG.Coord

open System
open System.Collections.Generic
open System.IO
open System.Security.Cryptography
open System.Text
open System.Text.Json

module NativeResponses =
    [<Literal>]
    let Model = "gpt-6.1-sol"
    [<Literal>]
    let Effort = "medium"
    [<Literal>]
    let MaximumInputTokens = 8000L
    [<Literal>]
    let MaximumOutputTokens = 1500L

    type Request =
        { Instructions: string; InputText: string; SchemaName: string; SchemaJson: byte array }

    let private digest (bytes: byte array) = Convert.ToHexStringLower(SHA256.HashData bytes)

    type FrozenRequest internal (count: byte array, generation: byte array) =
        let count = Array.copy count
        let generation = Array.copy generation
        let countDigest = digest count
        let generationDigest = digest generation
        member _.CountBody = Array.copy count
        member _.GenerationBody = Array.copy generation
        member _.CountSha256 = countDigest
        member _.GenerationSha256 = generationDigest
        member _.SharedFieldsSha256 = countDigest

    type CountAdmission internal (tokens: int64, request: FrozenRequest, responseDigest: string) =
        member _.InputTokens = tokens
        member _.GenerationSha256 = request.GenerationSha256
        member _.CountRequestSha256 = request.CountSha256
        member _.CountResponseSha256 = responseDigest

    type ProviderResponseId internal (value: string) =
        member _.Value = value

    type ResponseStatus =
        | Completed | Incomplete | Failed | Cancelled | InProgress | Queued
        | UnknownStatus of string option
    type UsageState = Unknown | Partial | Complete
    type UsageObservation =
        { State: UsageState
          InputTokens: int64 option; OutputTokens: int64 option; TotalTokens: int64 option
          CachedInputTokens: int64 option; CacheWriteInputTokens: int64 option
          ReasoningOutputTokens: int64 option; Issues: string list }
    type ResponseObservation =
        { ResponseId: ProviderResponseId option; ObservedModel: string option
          ProviderCreatedAt: DateTimeOffset option; Status: ResponseStatus; Usage: UsageObservation
          OutputText: string option; Refusal: string option; FailureCode: string option
          IncompleteReason: string option; Issues: string list }

    let private require (condition: bool) (code: string) = if not condition then invalidOp code
    let private utf8 = UTF8Encoding(false, true)
    let private attempt (action: unit -> 'T) =
        try Ok(action ())
        with
        | :? JsonException -> Error [ "invalid-json" ]
        | :? DecoderFallbackException -> Error [ "invalid-utf8" ]
        | :? EncoderFallbackException -> Error [ "invalid-utf8" ]
        | :? InvalidOperationException as ex -> Error [ ex.Message ]
        | :? ArgumentException -> Error [ "invalid-value" ]

    let rec private unique (node: JsonElement) =
        match node.ValueKind with
        | JsonValueKind.Object ->
            let names = HashSet<string>(StringComparer.Ordinal)
            for property in node.EnumerateObject() do
                require (names.Add property.Name) "duplicate-property"
                unique property.Value
        | JsonValueKind.Array -> for value in node.EnumerateArray() do unique value
        | _ -> ()

    let private parse (cap: int) (bytes: byte array) =
        require (not (isNull bytes) && bytes.Length > 0 && bytes.Length <= cap) "body-byte-bound"
        let raw = Array.copy bytes
        let document = JsonDocument.Parse(utf8.GetString raw, JsonDocumentOptions(MaxDepth = 32))
        try
            require (document.RootElement.ValueKind = JsonValueKind.Object) "object-required"
            unique document.RootElement
            document
        with _ -> document.Dispose(); reraise ()

    let private property (name: string) (node: JsonElement) =
        match node.TryGetProperty name with
        | true, value when value.ValueKind <> JsonValueKind.Null -> Some value
        | _ -> None

    let private optionalText (name: string) (node: JsonElement) =
        property name node |> Option.bind (fun value ->
            if value.ValueKind = JsonValueKind.String then Some(value.GetString()) else None)

    let private integer (value: JsonElement) =
        let mutable result = 0L
        if value.ValueKind = JsonValueKind.Number
           && (value.GetRawText() |> Seq.forall Char.IsAsciiDigit)
           && value.TryGetInt64(&result) then Some result else None

    let rec private canonical (writer: Utf8JsonWriter) (value: JsonElement) =
        match value.ValueKind with
        | JsonValueKind.Object ->
            writer.WriteStartObject()
            for row in value.EnumerateObject() |> Seq.sortWith (fun (a: JsonProperty) (b: JsonProperty) -> StringComparer.Ordinal.Compare(a.Name,b.Name)) do
                writer.WritePropertyName row.Name
                canonical writer row.Value
            writer.WriteEndObject()
        | JsonValueKind.Array ->
            writer.WriteStartArray()
            for row in value.EnumerateArray() do canonical writer row
            writer.WriteEndArray()
        | _ -> value.WriteTo writer

    let private serialize (action: Utf8JsonWriter -> unit) =
        use stream = new MemoryStream()
        use writer = new Utf8JsonWriter(stream)
        action writer
        writer.Flush()
        let bytes = stream.ToArray()
        require (bytes.Length <= 262144) "request-byte-bound"
        bytes

    let freeze (request: Request) : Result<FrozenRequest, string list> = attempt (fun () ->
        require (not (isNull (box request))) "request-required"
        let validText (value: string) = not (String.IsNullOrWhiteSpace value) && not (value.Contains '\000')
        require (validText request.Instructions && validText request.InputText) "request-text-required"
        require (utf8.GetByteCount(request.Instructions) <= 262144 && utf8.GetByteCount(request.InputText) <= 262144) "request-byte-bound"
        require (not (String.IsNullOrEmpty request.SchemaName) && request.SchemaName.Length <= 64
                 && (request.SchemaName |> Seq.forall (fun c -> Char.IsAsciiLetterOrDigit c || c = '_' || c = '-'))) "schema-name-invalid"
        use schema = parse 262144 request.SchemaJson
        let shared (writer: Utf8JsonWriter) =
            writer.WriteString("model",Model)
            writer.WriteString("instructions",request.Instructions)
            writer.WritePropertyName "input"
            writer.WriteStartArray()
            writer.WriteStartObject()
            writer.WriteString("role","user")
            writer.WriteString("content",request.InputText)
            writer.WriteEndObject()
            writer.WriteEndArray()
            writer.WritePropertyName "reasoning"
            writer.WriteStartObject(); writer.WriteString("effort",Effort); writer.WriteEndObject()
            writer.WritePropertyName "text"
            writer.WriteStartObject(); writer.WritePropertyName "format"; writer.WriteStartObject()
            writer.WriteString("type","json_schema"); writer.WriteString("name",request.SchemaName)
            writer.WriteBoolean("strict",true); writer.WritePropertyName "schema"
            canonical writer schema.RootElement
            writer.WriteEndObject(); writer.WriteEndObject()
            writer.WritePropertyName "tools"; writer.WriteStartArray(); writer.WriteEndArray()
            writer.WriteString("tool_choice","none")
            writer.WriteBoolean("parallel_tool_calls",false)
            writer.WriteString("truncation","disabled")
        let count = serialize (fun writer -> writer.WriteStartObject(); shared writer; writer.WriteEndObject())
        let generation = serialize (fun writer ->
            writer.WriteStartObject(); shared writer
            writer.WriteNumber("max_output_tokens",MaximumOutputTokens)
            writer.WriteBoolean("store",false); writer.WriteBoolean("stream",false); writer.WriteBoolean("background",false)
            writer.WriteEndObject())
        FrozenRequest(count,generation))

    let admitCount (request: FrozenRequest) (responseBytes: byte array) : Result<CountAdmission, string list> = attempt (fun () ->
        require (not (isNull (box request))) "frozen-request-required"
        let raw = if isNull responseBytes then null else Array.copy responseBytes
        use doc = parse 4096 raw
        let root = doc.RootElement
        let names = root.EnumerateObject() |> Seq.map _.Name |> Set.ofSeq
        require (names = Set.ofList [ "object"; "input_tokens" ]) "count-fields-invalid"
        require (optionalText "object" root = Some "response.input_tokens") "count-object-invalid"
        let amount = property "input_tokens" root |> Option.bind integer
        require (amount |> Option.exists (fun n -> n >= 1L && n <= MaximumInputTokens)) "count-not-admitted"
        CountAdmission(amount.Value,request,digest raw))

    let private unknownUsage: UsageObservation =
        { State = Unknown; InputTokens = None; OutputTokens = None; TotalTokens = None
          CachedInputTokens = None; CacheWriteInputTokens = None; ReasoningOutputTokens = None
          Issues = [ "usage-unavailable" ] }

    let private usage (root: JsonElement) : UsageObservation =
        match property "usage" root with
        | None -> unknownUsage
        | Some node when node.ValueKind <> JsonValueKind.Object -> { unknownUsage with Issues = [ "usage-object-invalid" ] }
        | Some node ->
            let issues = ResizeArray<string>()
            let counter (required: bool) (name: string) (source: JsonElement) =
                match property name source with
                | None ->
                    if required then issues.Add(name + "-unavailable")
                    None
                | Some value ->
                    match integer value with
                    | Some n -> Some n
                    | None -> issues.Add(name + "-invalid"); None
            let input = counter true "input_tokens" node
            let output = counter true "output_tokens" node
            let total = counter true "total_tokens" node
            let details (name: string) =
                match property name node with
                | Some value when value.ValueKind = JsonValueKind.Object -> Some value
                | Some _ -> issues.Add(name + "-invalid"); None
                | _ -> None
            let inputs = details "input_tokens_details"
            let outputs = details "output_tokens_details"
            let cached = inputs |> Option.bind (counter false "cached_tokens")
            let written = inputs |> Option.bind (counter false "cache_write_tokens")
            let reasoning = outputs |> Option.bind (counter false "reasoning_tokens")
            match input,output,total with
            | Some i,Some o,Some t when i > Int64.MaxValue-o || t <> i+o -> issues.Add "inclusive-total-mismatch"
            | _ -> ()
            let contain (value: int64 option) (bound: int64 option) (code: string) =
                match value,bound with
                | Some n,Some maximum when n > maximum -> issues.Add code
                | _ -> ()
            contain input total "input-exceeds-total"
            contain output total "output-exceeds-total"
            match input with
            | Some _ ->
                contain cached input "cached-input-exceeds-input"
                contain written input "cache-write-exceeds-input"
            | None ->
                contain cached total "cached-input-exceeds-total"
                contain written total "cache-write-exceeds-total"
            match output with
            | Some _ -> contain reasoning output "reasoning-exceeds-inclusive-output"
            | None -> contain reasoning total "reasoning-exceeds-total"
            // Breakouts overlap within each population; only the two population lower bounds add.
            let lowerBound (values: int64 option list) = values |> List.choose id |> List.fold max 0L
            let minimumInput = lowerBound [input; cached; written]
            let minimumOutput = lowerBound [output; reasoning]
            if minimumInput > Int64.MaxValue - minimumOutput then
                issues.Add "inclusive-lower-bound-overflow"
            else
                match total with
                | Some maximum when minimumInput + minimumOutput > maximum -> issues.Add "inclusive-lower-bound-exceeds-total"
                | _ -> ()
            { State = if issues.Count = 0 then Complete else Partial
              InputTokens = input; OutputTokens = output; TotalTokens = total
              CachedInputTokens = cached; CacheWriteInputTokens = written; ReasoningOutputTokens = reasoning
              Issues = List.ofSeq issues }

    let decodeResponse (responseBytes: byte array) : Result<ResponseObservation, string list> = attempt (fun () ->
        use doc = parse 262144 responseBytes
        let root = doc.RootElement
        let issues = ResizeArray<string>()
        if optionalText "object" root <> Some "response" then issues.Add "response-object-unavailable"
        let responseId =
            match optionalText "id" root with
            | Some id when not (String.IsNullOrWhiteSpace id) && utf8.GetByteCount(id) <= 256
                           && (id |> Seq.forall (fun c -> not (Char.IsControl c) && not (Char.IsWhiteSpace c))) -> Some(ProviderResponseId id)
            | _ -> issues.Add "provider-response-id-unavailable"; None
        let model = optionalText "model" root |> Option.filter (fun value -> utf8.GetByteCount(value) <= 256 && (value |> Seq.forall (fun c -> not (Char.IsControl c))))
        if model |> Option.forall String.IsNullOrWhiteSpace then issues.Add "observed-model-unavailable"
        let status =
            match optionalText "status" root with
            | Some "completed" -> Completed | Some "incomplete" -> Incomplete
            | Some "failed" -> Failed | Some "cancelled" -> Cancelled
            | Some "in_progress" -> InProgress | Some "queued" -> Queued
            | value -> issues.Add "response-status-unknown"; UnknownStatus value
        let created =
            match property "created_at" root with
            | None -> None
            | Some value ->
                match integer value with
                | Some n when n <= 253402300799L -> Some(DateTimeOffset.FromUnixTimeSeconds n)
                | _ -> issues.Add "provider-created-at-invalid"; None
        let nestedText (parent: string) (name: string) =
            match property parent root with
            | None -> None
            | Some node when node.ValueKind = JsonValueKind.Object -> optionalText name node
            | _ -> issues.Add(parent + "-invalid"); None
        let failure = nestedText "error" "code"
        if property "error" root |> Option.isSome then issues.Add "provider-error-present"
        let incomplete = nestedText "incomplete_details" "reason"
        if property "incomplete_details" root |> Option.isSome then issues.Add "provider-incomplete-details-present"
        match property "tools" root with
        | Some node when node.ValueKind = JsonValueKind.Array && node.GetArrayLength() = 0 -> ()
        | Some _ -> issues.Add "unexpected-response-tools"
        | None -> ()
        let texts = ResizeArray<string>()
        let refusals = ResizeArray<string>()
        match property "output" root with
        | Some output when output.ValueKind = JsonValueKind.Array ->
            for item in output.EnumerateArray() do
                if item.ValueKind <> JsonValueKind.Object then issues.Add "output-item-invalid"
                else
                    match optionalText "type" item with
                    | Some "reasoning" -> ()
                    | Some "message" ->
                        if optionalText "status" item <> Some "completed" then issues.Add "output-message-incomplete"
                        if optionalText "role" item <> Some "assistant" then issues.Add "output-message-role-invalid"
                        match property "content" item with
                        | Some content when content.ValueKind = JsonValueKind.Array ->
                            for part in content.EnumerateArray() do
                                if part.ValueKind <> JsonValueKind.Object then issues.Add "output-content-invalid"
                                else
                                    match optionalText "type" part with
                                    | Some "output_text" ->
                                        match optionalText "text" part with
                                        | Some text -> texts.Add text
                                        | _ -> issues.Add "output-text-unavailable"
                                    | Some "refusal" ->
                                        match optionalText "refusal" part with
                                        | Some text -> refusals.Add text
                                        | _ -> issues.Add "refusal-text-unavailable"
                                    | _ -> issues.Add "unexpected-output-content"
                        | _ -> issues.Add "output-content-unavailable"
                    | _ -> issues.Add "unexpected-output-item"
        | _ -> issues.Add "output-unavailable"
        { ResponseId = responseId; ObservedModel = model; ProviderCreatedAt = created
          Status = status; Usage = usage root
          OutputText = if texts.Count = 0 then None else Some(String.Concat(texts.ToArray()))
          Refusal = if refusals.Count = 0 then None else Some(String.Concat(refusals.ToArray()))
          FailureCode = failure; IncompleteReason = incomplete; Issues = List.ofSeq issues })

    let validateCompletion (count: CountAdmission) (response: ResponseObservation) : Result<string, string list> =
        if isNull (box count) || isNull (box response) then Error [ "completion-operands-required" ]
        else
            let issues = ResizeArray<string>(response.Issues @ response.Usage.Issues)
            if response.Status <> Completed then issues.Add "provider-not-completed"
            if response.ResponseId.IsNone then issues.Add "provider-response-id-unavailable"
            if response.ObservedModel <> Some Model then issues.Add "observed-model-mismatch"
            if response.Refusal.IsSome then issues.Add "provider-refusal"
            if response.Usage.State <> Complete then issues.Add "usage-not-complete"
            match response.Usage.InputTokens with
            | Some n when n >= 1L && n <= MaximumInputTokens && n = count.InputTokens -> ()
            | _ -> issues.Add "count-input-not-corresponding"
            match response.Usage.OutputTokens with
            | Some n when n >= 1L && n <= MaximumOutputTokens -> ()
            | _ -> issues.Add "inclusive-output-not-admitted"
            match response.Usage.InputTokens,response.Usage.OutputTokens,response.Usage.TotalTokens with
            | Some i,Some o,Some t when i >= 0L && o >= 0L && i <= Int64.MaxValue-o && t = i+o -> ()
            | _ -> issues.Add "inclusive-total-mismatch"
            let breakout (name: string) (bound: int64 option) (value: int64 option) =
                match bound,value with
                | Some total,Some n when n < 0L || n > total -> issues.Add name
                | None,Some _ -> issues.Add name
                | _ -> ()
            breakout "cached-input-exceeds-input" response.Usage.InputTokens response.Usage.CachedInputTokens
            breakout "cache-write-exceeds-input" response.Usage.InputTokens response.Usage.CacheWriteInputTokens
            breakout "reasoning-exceeds-inclusive-output" response.Usage.OutputTokens response.Usage.ReasoningOutputTokens
            match response.OutputText with
            | Some text when not (String.IsNullOrWhiteSpace text) && issues.Count = 0 -> Ok text
            | _ ->
                if response.OutputText |> Option.forall String.IsNullOrWhiteSpace then issues.Add "assessment-text-unavailable"
                Error(List.ofSeq issues)
