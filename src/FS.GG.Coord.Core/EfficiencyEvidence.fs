namespace FS.GG.Coord

open System
open System.Globalization
open System.Text
open System.Text.Json

module EfficiencyEvidence =
    let semanticKind (kind: string) =
        match kind with
        | "usage" | "runtime-turn-usage" -> Some "usage"
        | "native-item-outcome" -> Some "outcome"
        | "attempt" -> Some "attempt"
        | "runtime-admission" | "runtime-start" | "runtime-terminal" | "runtime-gap"
        | "expected-dispatch" | "invocation-lineage" | "operational-activation"
        | "runtime-native-inventory/1" | "runtime-native-inventory-source/1" -> Some "invocation"
        | "learn-installed-origin/1" -> Some "adoption"
        | "ci-run" -> Some "ci-run"
        | "ci-job" -> Some "ci-job"
        | "ci-step" -> Some "operation"
        | "pull-request-head" -> Some "pr"
        | "activity-span" | "activity-usage-attribution" | "efficiency-resource-allocation/1" -> Some "activity"
        | "complication" | "efficiency-problem-episode/1" -> Some "complication"
        | "process-review" -> Some "process-review"
        | "correction" -> Some "correction"
        | "evidence" | "source" | "parent-child" | "coverage" | "diagnostic" -> Some "operation"
        | _ -> None

    let private validCanonicalRef (reference: JsonElement) =
        let bounded (name: string) =
            let value = reference.GetProperty name
            if value.ValueKind <> JsonValueKind.String then false
            else
                let mutable runes = value.GetString().EnumerateRunes()
                let mutable length = 0
                while runes.MoveNext() do length <- length + 1
                length > 0 && length <= 256
        let revision = reference.GetProperty "revision"
        let digest = reference.GetProperty "contentDigest"
        bounded "id" && bounded "kind"
        && (match revision.TryGetInt64() with true, value -> value >= 0L | _ -> false)
        && digest.ValueKind = JsonValueKind.String
        && System.Text.RegularExpressions.Regex.IsMatch(digest.GetString(), "^sha256:[a-f0-9]{64}$", System.Text.RegularExpressions.RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds 50.)

    let private quoted (builder: StringBuilder) (text: string) =
        builder.Append('"') |> ignore
        for character in text do
            match character with
            | '"' -> builder.Append("\\\"") |> ignore
            | '\\' -> builder.Append("\\\\") |> ignore
            | '\b' -> builder.Append("\\b") |> ignore
            | '\f' -> builder.Append("\\f") |> ignore
            | '\n' -> builder.Append("\\n") |> ignore
            | '\r' -> builder.Append("\\r") |> ignore
            | '\t' -> builder.Append("\\t") |> ignore
            | value when int value < 32 || int value > 126 ->
                builder.Append("\\u").Append((int value).ToString("x4", CultureInfo.InvariantCulture)) |> ignore
            | value -> builder.Append value |> ignore
        builder.Append('"') |> ignore

    let private codePoints (text: string) =
        let mutable runes = text.EnumerateRunes()
        let values = ResizeArray<int>()
        while runes.MoveNext() do values.Add runes.Current.Value
        values.ToArray()

    let private compareCodePoints (first: string) (last: string) =
        let firstPoints, lastPoints = codePoints first, codePoints last
        let mutable index = 0
        let mutable ordering = 0
        while ordering = 0 && index < min firstPoints.Length lastPoints.Length do
            ordering <- compare firstPoints[index] lastPoints[index]
            index <- index + 1
        if ordering = 0 then compare firstPoints.Length lastPoints.Length else ordering

    let encode (node: JsonElement) =
        try
            let builder = StringBuilder()
            let rec write (value: JsonElement) =
                match value.ValueKind with
                | JsonValueKind.Object ->
                    let properties = value.EnumerateObject() |> Seq.toArray
                    if properties.Length <> (properties |> Array.map _.Name |> Set.ofArray |> Set.count) then
                        invalidOp "efficiency-evidence-duplicate-property"
                    builder.Append '{' |> ignore
                    properties
                    |> Array.sortWith (fun first last -> compareCodePoints first.Name last.Name)
                    |> Array.iteri (fun index property ->
                        if index > 0 then builder.Append ',' |> ignore
                        quoted builder property.Name
                        builder.Append ':' |> ignore
                        write property.Value)
                    builder.Append '}' |> ignore
                | JsonValueKind.Array ->
                    builder.Append '[' |> ignore
                    value.EnumerateArray() |> Seq.iteri (fun index child ->
                        if index > 0 then builder.Append ',' |> ignore
                        write child)
                    builder.Append ']' |> ignore
                | JsonValueKind.String -> quoted builder (value.GetString())
                | JsonValueKind.Number ->
                    match value.TryGetInt64() with
                    | true, number -> builder.Append(number.ToString(CultureInfo.InvariantCulture)) |> ignore
                    | _ -> invalidOp "efficiency-evidence-noninteger-counter-unavailable"
                | JsonValueKind.True -> builder.Append "true" |> ignore
                | JsonValueKind.False -> builder.Append "false" |> ignore
                | JsonValueKind.Null -> builder.Append "null" |> ignore
                | _ -> invalidOp "efficiency-evidence-unsupported-value"
            write node
            Ok(Encoding.UTF8.GetBytes(builder.ToString()))
        with
        | :? InvalidOperationException as error -> Error error.Message
        | :? JsonException as error -> Error error.Message

    let validate (bytes: byte array) =
        if isNull bytes || bytes.Length = 0 || bytes.Length > 24576 then
            Error "efficiency-evidence-packet-byte-bound"
        else
            try
                use document = JsonDocument.Parse(ReadOnlyMemory<byte>(bytes), JsonDocumentOptions(MaxDepth = 64))
                let root = document.RootElement
                let closed names (node: JsonElement) =
                    node.ValueKind = JsonValueKind.Object
                    && (node.EnumerateObject() |> Seq.map _.Name |> Seq.sort |> Seq.toList) = List.sort names
                let boundedString limit (node: JsonElement) =
                    if node.ValueKind <> JsonValueKind.String then false
                    else
                        let mutable runes = node.GetString().EnumerateRunes()
                        let mutable length = 0
                        while runes.MoveNext() do length <- length + 1
                        length > 0 && length <= limit
                let validCoverage (node: JsonElement) =
                    let names = [ "population"; "usage"; "classification"; "lineage"; "dependency" ]
                    closed names node && (names |> List.forall (fun name ->
                        let value = node.GetProperty name
                        value.ValueKind = JsonValueKind.String
                        && Set.contains (value.GetString()) (set [ "complete"; "partial"; "unknown"; "not-applicable" ])))
                let validSubject (node: JsonElement) =
                    closed [ "itemId"; "outcomeId"; "outcomeEpoch"; "scope" ] node
                    && boundedString 256 (node.GetProperty "itemId")
                    && boundedString 256 (node.GetProperty "outcomeId")
                    && (let epoch = node.GetProperty "outcomeEpoch"
                        epoch.ValueKind = JsonValueKind.Null || (match epoch.TryGetInt64() with true, value -> value >= 1L | _ -> false))
                    && node.GetProperty("scope").ValueKind = JsonValueKind.String
                    && Set.contains (node.GetProperty("scope").GetString()) (set [ "native-item"; "provisional-delivery" ])
                let validOmissions (node: JsonElement) =
                    node.ValueKind = JsonValueKind.Array && node.GetArrayLength() <= 32
                    && (node.EnumerateArray() |> Seq.forall (boundedString 1024))
                if not (closed [ "schema"; "subject"; "coverage"; "omissions"; "records" ] root)
                   || root.GetProperty("schema").GetString() <> "fsgg.telemetry.efficiency-evidence-packet/1"
                   || not (validSubject (root.GetProperty "subject"))
                   || not (validCoverage (root.GetProperty "coverage"))
                   || not (validOmissions (root.GetProperty "omissions"))
                   || root.GetProperty("records").ValueKind <> JsonValueKind.Array
                   || root.GetProperty("records").GetArrayLength() > 128 then
                    Error "efficiency-evidence-packet-shape"
                else
                    let records = root.GetProperty("records").EnumerateArray() |> Seq.toArray
                    let validRecord (record: JsonElement) =
                        closed [ "ref"; "canonicalRef"; "itemId"; "payload"; "priority"; "analysisGenerated" ] record
                        && record.GetProperty("analysisGenerated").ValueKind = JsonValueKind.False
                        && Encoding.UTF8.GetByteCount(record.GetRawText()) <= 16384
                        && closed [ "id"; "kind"; "revision" ] (record.GetProperty "ref")
                        && closed [ "id"; "kind"; "revision"; "contentDigest" ] (record.GetProperty "canonicalRef")
                        && validCanonicalRef (record.GetProperty "canonicalRef")
                        && (let reference = record.GetProperty "ref"
                            let canonical = record.GetProperty "canonicalRef"
                            reference.GetProperty("id").GetString() = canonical.GetProperty("id").GetString()
                            && (match reference.GetProperty("revision").TryGetInt64() with
                                | true, revision -> revision >= 0L && revision = canonical.GetProperty("revision").GetInt64()
                                | _ -> false)
                            && semanticKind (canonical.GetProperty("kind").GetString()) = Some(reference.GetProperty("kind").GetString()))
                        && Set.contains (record.GetProperty("priority").GetString()) (set [ "failure"; "correction"; "success"; "other" ])
                    let uniqueReferences =
                        if not (records |> Array.forall validRecord) then false
                        else
                            let identities = records |> Array.map (fun record ->
                                let reference = record.GetProperty "canonicalRef"
                                reference.GetProperty("id").GetString(), reference.GetProperty("kind").GetString(), reference.GetProperty("revision").GetInt64())
                            (identities |> Set.ofArray |> Set.count) = records.Length
                    if not uniqueReferences then
                        Error "efficiency-evidence-record-shape-or-bound"
                    else
                        match encode root with
                        | Ok canonical when canonical = bytes -> Ok(root.Clone())
                        | Ok _ -> Error "efficiency-evidence-packet-codec-mismatch"
                        | Error reason -> Error reason
            with
            | :? JsonException as error -> Error error.Message
            | :? InvalidOperationException as error -> Error error.Message
