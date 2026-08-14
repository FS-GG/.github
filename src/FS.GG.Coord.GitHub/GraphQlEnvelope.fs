namespace FS.GG.Coord.GitHub

module internal GraphQlEnvelope =
    open System
    open System.Text.Json

    let tryMeter (body: string) =
        if String.IsNullOrWhiteSpace body then None else
        try
            use document = JsonDocument.Parse body
            let root = document.RootElement
            if root.ValueKind <> JsonValueKind.Object then None else
            match root.TryGetProperty "data" with
            | true, data when data.ValueKind = JsonValueKind.Object ->
                match data.TryGetProperty "rateLimit" with
                | true, meter when meter.ValueKind = JsonValueKind.Object ->
                    let integer (name: string) =
                        match meter.TryGetProperty name with
                        | true, value when value.ValueKind = JsonValueKind.Number ->
                            match value.TryGetInt32() with true, number -> Some number | _ -> None
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
