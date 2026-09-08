namespace FS.GG.Coord

open System
open System.Text.Json
open System.Text.RegularExpressions

module TelemetryCi =
    [<Literal>]
    let AssignmentSchema = "fsgg.telemetry.ci-assignment/1"

    type Assignment =
        { FeatureId: string; ItemId: string; AttemptId: string
          ParentAttemptId: string option; ProducerStream: string }

    type Interval = { StartUtc: DateTimeOffset; EndUtc: DateTimeOffset }
    type Timing =
        { RunnerSeconds: int64 option; WallSeconds: int64 option
          AdministrativeSeconds: int64 option; CriticalPathSeconds: int64 option }

    let private safe value =
        not (String.IsNullOrWhiteSpace value) && Regex.IsMatch(value, "^[A-Za-z0-9][A-Za-z0-9._-]{0,127}$")

    let parseAssignment (bytes: byte array) =
        try
            use document = JsonDocument.Parse bytes
            let root = document.RootElement
            let allowed = Set [ "schema"; "featureId"; "itemId"; "attemptId"; "parentAttemptId"; "producerStream" ]
            let unknown = root.EnumerateObject() |> Seq.map _.Name |> Seq.filter (allowed.Contains >> not) |> Seq.toList
            let text (name: string) =
                match root.TryGetProperty name with
                | true, value when value.ValueKind = JsonValueKind.String && safe(value.GetString()) -> Some(value.GetString())
                | _ -> None
            let parent =
                match root.TryGetProperty "parentAttemptId" with
                | false, _ -> Some None
                | true, value when value.ValueKind = JsonValueKind.Null -> Some None
                | true, value when value.ValueKind = JsonValueKind.String && safe(value.GetString()) -> Some(Some(value.GetString()))
                | _ -> None
            match text "schema", text "featureId", text "itemId", text "attemptId", parent, text "producerStream" with
            | Some schema, Some feature, Some item, Some attempt, Some parentAttempt, Some producer when schema = AssignmentSchema && unknown.IsEmpty ->
                Ok { FeatureId = feature; ItemId = item; AttemptId = attempt; ParentAttemptId = parentAttempt; ProducerStream = producer }
            | _ when not unknown.IsEmpty -> Error [ "assignment contains unknown fields: " + String.concat "," unknown ]
            | _ -> Error [ "assignment must be closed, schema-current, and contain safe identifiers" ]
        with :? JsonException as error -> Error [ "invalid assignment JSON: " + error.Message ]

    let interval (startUtc: string option) (endUtc: string option) =
        match startUtc, endUtc with
        | Some startText, Some endText ->
            match DateTimeOffset.TryParse startText, DateTimeOffset.TryParse endText with
            | (true, startAt), (true, endAt) when endAt >= startAt -> Some { StartUtc = startAt; EndUtc = endAt }
            | _ -> None
        | _ -> None

    let private merged intervals =
        intervals
        |> List.sortBy _.StartUtc
        |> List.fold (fun acc next ->
            match acc with
            | current :: tail when next.StartUtc <= current.EndUtc -> { current with EndUtc = max current.EndUtc next.EndUtc } :: tail
            | _ -> next :: acc) []
        |> List.rev

    let unionSeconds intervals =
        if List.isEmpty intervals then None
        else merged intervals |> List.sumBy (fun value -> int64 (value.EndUtc - value.StartUtc).TotalSeconds) |> Some

    let subtractSeconds source excluded =
        if List.isEmpty source then None else
        let sourceSeconds = unionSeconds source |> Option.defaultValue 0L
        let overlaps =
            [ for a in merged source do
                for b in merged excluded do
                    let startAt = max a.StartUtc b.StartUtc
                    let endAt = min a.EndUtc b.EndUtc
                    if endAt > startAt then yield { StartUtc = startAt; EndUtc = endAt } ]
        Some(sourceSeconds - (unionSeconds overlaps |> Option.defaultValue 0L))
