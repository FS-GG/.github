namespace FS.GG.Coord

open System
open System.Text.Json
open System.Text.RegularExpressions

module TelemetryRuntime =
    [<Literal>]
    let AssignmentSchema = "fsgg.telemetry.codex-assignment/1"

    type Assignment =
        { FeatureId: string; ItemId: string; AttemptId: string
          ParentAttemptId: string option; ProducerStream: string }

    type TurnUsage =
        { ThreadId: string; TurnId: string option; TurnSequence: int64
          Provider: string option; ObservedModel: string option; ObservedEffort: string option; Backend: string option
          Input: int64; CachedInput: int64; Output: int64; Reasoning: int64 option; Total: int64 }

    type Projection = ThreadStarted of threadId: string | TurnStarted of threadId: string * turnId: string option * turnSequence: int64 | TurnUsageCompleted of TurnUsage | Gap of code: string

    let private safe (value: string) =
        not (String.IsNullOrWhiteSpace value) && Regex.IsMatch(value, "^[A-Za-z0-9][A-Za-z0-9._-]{0,127}$")
    let private text (root: JsonElement) (name: string) =
        match root.TryGetProperty name with
        | true, value when value.ValueKind = JsonValueKind.String && not (String.IsNullOrWhiteSpace(value.GetString())) -> Some(value.GetString())
        | _ -> None
    let private number (root: JsonElement) (name: string) =
        match root.TryGetProperty name with
        | true, value when value.ValueKind = JsonValueKind.Number -> match value.TryGetInt64() with true, count when count >= 0L -> Some count | _ -> None
        | _ -> None
    let private checkedTotal input output = try Some(Checked.(+) input output) with :? OverflowException -> None

    let parseAssignment (bytes: byte array) =
        try
            use document = JsonDocument.Parse bytes
            let root = document.RootElement
            let allowed = Set.ofList [ "schema"; "featureId"; "itemId"; "attemptId"; "parentAttemptId"; "producerStream" ]
            let unknown = root.EnumerateObject() |> Seq.map _.Name |> Seq.filter (fun name -> not (Set.contains name allowed)) |> Seq.toList
            let parent =
                match root.TryGetProperty "parentAttemptId" with
                | false, _ -> Some None
                | true, value when value.ValueKind = JsonValueKind.Null -> Some None
                | true, value when value.ValueKind = JsonValueKind.String && safe(value.GetString()) -> Some(Some(value.GetString()))
                | _ -> None
            match text root "schema", text root "featureId", text root "itemId", text root "attemptId", parent, text root "producerStream" with
            | Some schema, Some feature, Some item, Some attempt, Some parentAttempt, Some producer when schema = AssignmentSchema && unknown.IsEmpty && List.forall safe [ feature; item; attempt; producer ] ->
                Ok { FeatureId = feature; ItemId = item; AttemptId = attempt; ParentAttemptId = parentAttempt; ProducerStream = producer }
            | _ when not unknown.IsEmpty -> Error [ "assignment contains unknown fields: " + String.concat "," unknown ]
            | _ -> Error [ "assignment must be closed, schema-current, and contain safe feature/item/attempt/producer identifiers" ]
        with :? JsonException as error -> Error [ "invalid assignment JSON: " + error.Message ]

    let projectLine (currentThreadId: string option) (turnSequence: int64) (raw: string) =
        try
            use document = JsonDocument.Parse raw
            let root = document.RootElement
            match text root "type" with
            | Some "thread.started" ->
                match text root "thread_id" with Some thread -> Some(ThreadStarted thread) | None -> Some(Gap "missing-thread-id")
            | Some "turn.started" ->
                match text root "thread_id" |> Option.orElse currentThreadId with
                | Some thread -> Some(TurnStarted(thread, text root "turn_id", turnSequence))
                | None -> Some(Gap "missing-turn-thread")
            | Some "turn.completed" ->
                match root.TryGetProperty "usage", (text root "thread_id" |> Option.orElse currentThreadId) with
                | (true, usage), Some thread when usage.ValueKind = JsonValueKind.Object ->
                    match number usage "input_tokens", number usage "cached_input_tokens", number usage "output_tokens" with
                    | Some input, Some cached, Some output when cached <= input ->
                        match checkedTotal input output with
                        | Some total ->
                            Some(TurnUsageCompleted
                                { ThreadId = thread; TurnId = text root "turn_id"; TurnSequence = turnSequence
                                  Provider = text root "provider"; ObservedModel = text root "model"; ObservedEffort = text root "effort"; Backend = text root "backend"
                                  Input = input; CachedInput = cached; Output = output; Reasoning = number usage "reasoning_output_tokens"; Total = total })
                        | None -> Some(Gap "usage-overflow")
                    | _ -> Some(Gap "malformed-turn-usage")
                | _ -> Some(Gap "missing-turn-usage")
            | Some "error" -> Some(Gap "runtime-error")
            | _ -> None
        with :? JsonException -> Some(Gap "malformed-json-frame")
