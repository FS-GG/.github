namespace FS.GG.Coord.GitHub

module internal GraphQlEnvelope =
    open System
    open System.Text.Json

    type MutationResult =
        | InvalidJson
        | NotObject
        | InvalidErrors
        | Errors
        | Partial
        | Applied
        | NoResult

    type MigrationClearResult =
        | ClearSuccess
        | ClearErrors
        | ClearMalformed

    let classifyMigrationClear (body: string) =
        try
            if System.Text.Encoding.UTF8.GetByteCount body > 4 * 1024 * 1024 then
                invalidArg "body" "bounded response required"
            use document = JsonDocument.Parse(body, JsonDocumentOptions(MaxDepth = 32))
            let rec distinct (node: JsonElement) =
                match node.ValueKind with
                | JsonValueKind.Object ->
                    let properties = node.EnumerateObject() |> Seq.toList
                    (properties |> List.map (fun property -> property.Name) |> Set.ofList |> Set.count) = properties.Length
                    && (properties |> List.forall (fun property -> distinct property.Value))
                | JsonValueKind.Array -> node.EnumerateArray() |> Seq.forall distinct
                | _ -> true
            let root = document.RootElement
            if root.ValueKind <> JsonValueKind.Object then
                invalidArg "body" "ambiguous envelope"
            match root.TryGetProperty "errors" with
            | true, errors when errors.ValueKind <> JsonValueKind.Array || errors.GetArrayLength() <> 0 ->
                ClearErrors
            | _ ->
                if not (distinct root) then
                    invalidArg "body" "ambiguous envelope"
                if root.GetProperty("data").ValueKind <> JsonValueKind.Object then
                    invalidArg "body" "complete data object required"
                ClearSuccess
        with _ -> ClearMalformed

    let tryMeter (body: string) =
        if String.IsNullOrWhiteSpace body then
            None
        else
            try
                use document = JsonDocument.Parse body
                let root = document.RootElement

                if root.ValueKind <> JsonValueKind.Object then
                    None
                else
                    match root.TryGetProperty "data" with
                    | true, data when data.ValueKind = JsonValueKind.Object ->
                        match data.TryGetProperty "rateLimit" with
                        | true, meter when meter.ValueKind = JsonValueKind.Object ->
                            let integer (name: string) =
                                match meter.TryGetProperty name with
                                | true, value when value.ValueKind = JsonValueKind.Number ->
                                    match value.TryGetInt32() with
                                    | true, number -> Some number
                                    | _ -> None
                                | _ -> None

                            match integer "cost", integer "remaining" with
                            | Some cost, Some remaining -> Some(cost, remaining)
                            | _ -> None
                        | _ -> None
                    | _ -> None
            with
            | :? JsonException
            | :? InvalidOperationException
            | :? FormatException
            | :? OverflowException -> None

    let classifyMutation (body: string) =
        try
            use document = JsonDocument.Parse body
            let root = document.RootElement

            if root.ValueKind <> JsonValueKind.Object then
                NotObject
            else
                let hasErrors, errors = root.TryGetProperty "errors"
                let hasData, data = root.TryGetProperty "data"

                let hasMutationResult =
                    hasData
                    && data.ValueKind = JsonValueKind.Object
                    && (data.EnumerateObject() |> Seq.isEmpty |> not)
                    && (data.EnumerateObject()
                        |> Seq.exists (fun property -> property.Value.ValueKind <> JsonValueKind.Null))

                if hasErrors && errors.ValueKind <> JsonValueKind.Array then
                    InvalidErrors
                elif hasErrors && errors.GetArrayLength() > 0 then
                    if hasMutationResult then Partial else Errors
                elif hasMutationResult then
                    Applied
                else
                    NoResult
        with :? JsonException ->
            InvalidJson
