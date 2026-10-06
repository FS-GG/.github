namespace FS.GG.Coord

open System
open System.Collections.Generic
open System.Security.Cryptography
open System.Text
open System.Text.Json

module NativeExecJsonl =
    type LocalTurnKey =
        { CaptureSha256: string
          ThreadId: Guid
          StartFrameOrdinal: int }
    type Usage =
        { Input: int64
          CachedInput: int64
          CacheWriteInput: int64 option
          Output: int64
          Reasoning: int64 option
          Total: int64 }
    type Turn =
        { Key: LocalTurnKey
          TurnOrdinal: int
          CompletionFrameOrdinal: int
          NativeTurnId: Guid option
          Usage: Usage }
    type Capture =
        { CaptureSha256: string
          CaptureBytes: int
          FrameCount: int
          ThreadId: Guid
          Turn: Turn }

    let private refuse code = invalidOp code
    let private require condition code = if not condition then refuse code
    let rec private unique (node: JsonElement) =
        match node.ValueKind with
        | JsonValueKind.Object ->
            let names = HashSet<string>(StringComparer.Ordinal)
            for property in node.EnumerateObject() do
                require (names.Add property.Name) "duplicate-property"
                unique property.Value
        | JsonValueKind.Array -> for value in node.EnumerateArray() do unique value
        | _ -> ()
    let private closed (required: string list) (optional: string list) (node: JsonElement) =
        require (node.ValueKind = JsonValueKind.Object) "object-required"
        let names = node.EnumerateObject() |> Seq.map _.Name |> Set.ofSeq
        require (Set.isSubset (Set.ofList required) names && Set.isSubset names (Set.ofList (required @ optional))) "closed-frame-fields"
    let private text (name: string) (node: JsonElement) =
        let value = node.GetProperty name
        require (value.ValueKind = JsonValueKind.String) "string-required"
        value.GetString()
    let private counter (name: string) (node: JsonElement) =
        let value = node.GetProperty name
        require (value.ValueKind = JsonValueKind.Number) "integer-counter-required"
        let mutable result = 0L
        require ((value.GetRawText() |> Seq.forall Char.IsAsciiDigit) && value.TryGetInt64(&result) && result >= 0L) "integer-counter-required"
        result
    let private optionalCounter (name: string) (node: JsonElement) =
        match node.TryGetProperty name with
        | true, _ -> Some(counter name node)
        | _ -> None
    let private usage (node: JsonElement) =
        closed [ "input_tokens"; "cached_input_tokens"; "output_tokens" ] [ "cache_write_input_tokens"; "reasoning_output_tokens" ] node
        let input = counter "input_tokens" node
        let cached = counter "cached_input_tokens" node
        let output = counter "output_tokens" node
        let write = optionalCounter "cache_write_input_tokens" node
        let reasoning = optionalCounter "reasoning_output_tokens" node
        require (input > 0L && output > 0L) "default-or-zero-pilot-usage"
        require (cached <= input && (write |> Option.forall (fun amount -> amount <= input - cached))) "cache-input-exceeds-inclusive-input"
        require (reasoning |> Option.forall (fun amount -> amount <= output)) "reasoning-exceeds-inclusive-output"
        require (input <= Int64.MaxValue - output) "inclusive-total-overflow"
        { Input = input; CachedInput = cached; CacheWriteInput = write
          Output = output; Reasoning = reasoning; Total = input + output }

    let decode (stdout: byte array) =
        try
            require (not (isNull stdout) && stdout.Length > 0 && stdout.Length <= 262144) "capture-byte-bound"
            let stdout = Array.copy stdout
            require (stdout[stdout.Length - 1] = 10uy) "incomplete-jsonl-frame"
            let digest = Convert.ToHexStringLower(SHA256.HashData stdout)
            let utf8 = UTF8Encoding(false, true)
            let mutable thread: Guid option = None
            let mutable start: int option = None
            let mutable completion: (int * Usage) option = None
            let items = Dictionary<string, string * bool>(StringComparer.Ordinal)
            let mutable offset = 0
            let mutable frame = 0
            for index in 0 .. stdout.Length - 1 do
                if stdout[index] = 10uy then
                    require (frame < 4096) "frame-count-bound"
                    let length = index - offset
                    require (length > 0 && length <= 65536) "frame-byte-bound"
                    let raw = utf8.GetString(stdout, offset, length)
                    use document = JsonDocument.Parse(raw, JsonDocumentOptions(MaxDepth = 32))
                    let node = document.RootElement
                    unique node
                    require (completion.IsNone) "frame-after-turn-completion"
                    let kind = text "type" node
                    match kind with
                    | "thread.started" ->
                        closed [ "type"; "thread_id" ] [] node
                        require (thread.IsNone && start.IsNone && frame = 0) "duplicate-or-resumed-thread"
                        let mutable id = Guid.Empty
                        require (Guid.TryParseExact(text "thread_id" node, "D", &id) && id <> Guid.Empty) "native-thread-uuid-required"
                        thread <- Some id
                    | "turn.started" ->
                        closed [ "type" ] [] node
                        require (thread.IsSome && start.IsNone) "duplicate-or-unjoined-turn-start"
                        start <- Some frame
                    | "turn.completed" ->
                        closed [ "type"; "usage" ] [] node
                        require (thread.IsSome && start.IsSome && (items.Values |> Seq.forall snd)) "incomplete-turn-population"
                        completion <- Some(frame, usage (node.GetProperty "usage"))
                    | "item.started" | "item.updated" | "item.completed" ->
                        closed [ "type"; "item" ] [] node
                        require (thread.IsSome && start.IsSome) "item-outside-started-turn"
                        let item = node.GetProperty "item"
                        closed [ "id"; "type"; "text" ] [] item
                        let id, itemKind = text "id" item, text "type" item
                        require (not (String.IsNullOrWhiteSpace id)) "item-identity-required"
                        require (itemKind = "agent_message" || itemKind = "reasoning") "unaccounted-tool-or-model-population"
                        text "text" item |> ignore
                        match kind, items.TryGetValue id with
                        | "item.started", (false, _) -> items.Add(id, (itemKind, false))
                        | "item.updated", (true, (prior, false)) when prior = itemKind -> ()
                        | "item.completed", (false, _) -> items.Add(id, (itemKind, true))
                        | "item.completed", (true, (prior, false)) when prior = itemKind -> items[id] <- (itemKind, true)
                        | _ -> refuse "duplicate-or-unjoined-item-lifecycle"
                    | _ -> refuse "unsupported-frame-population"
                    frame <- frame + 1
                    offset <- index + 1
            match thread, start, completion with
            | Some id, Some startOrdinal, Some(completionOrdinal, counters) ->
                Ok { CaptureSha256 = digest; CaptureBytes = stdout.Length; FrameCount = frame; ThreadId = id
                     Turn = { Key = { CaptureSha256 = digest; ThreadId = id; StartFrameOrdinal = startOrdinal }
                              TurnOrdinal = 0; CompletionFrameOrdinal = completionOrdinal; NativeTurnId = None; Usage = counters } }
            | _ -> Error [ "incomplete-thread-turn-lifecycle" ]
        with
        | :? JsonException -> Error [ "malformed-jsonl-frame" ]
        | :? DecoderFallbackException -> Error [ "invalid-utf8-frame" ]
        | :? InvalidOperationException as error -> Error [ error.Message ]
        | :? ArgumentException -> Error [ "malformed-jsonl-frame" ]
        | :? KeyNotFoundException -> Error [ "closed-frame-fields" ]
