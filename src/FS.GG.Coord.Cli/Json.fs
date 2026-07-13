namespace FS.GG.Coord.Cli

module Json =

    open System.Text.Json

    type Error = { Path: string; Message: string }

    let err path message = Error [ { Path = path; Message = message } ]

    let prop (path: string) (name: string) (el: JsonElement) : Result<JsonElement, Error list> =
        match el.TryGetProperty name with
        | true, v -> Ok v
        | _ -> err $"%s{path}.%s{name}" "required field is missing"

    let optProp (name: string) (el: JsonElement) : JsonElement option =
        match el.TryGetProperty name with
        | true, v when v.ValueKind <> JsonValueKind.Null -> Some v
        | _ -> None

    let asString (path: string) (el: JsonElement) : Result<string, Error list> =
        if el.ValueKind = JsonValueKind.String then
            Ok(el.GetString())
        else
            err path $"expected a string, got %A{el.ValueKind}"

    let asInt (path: string) (el: JsonElement) : Result<int, Error list> =
        match el.ValueKind with
        | JsonValueKind.Number ->
            match el.TryGetInt32() with
            | true, n -> Ok n
            | _ -> err path "expected a 32-bit integer"
        | k -> err path $"expected a number, got %A{k}"

    let asBool (path: string) (el: JsonElement) : Result<bool, Error list> =
        match el.ValueKind with
        | JsonValueKind.True -> Ok true
        | JsonValueKind.False -> Ok false
        | k -> err path $"expected a boolean, got %A{k}"

    let asArray (path: string) (el: JsonElement) : Result<JsonElement list, Error list> =
        if el.ValueKind = JsonValueKind.Array then
            Ok(el.EnumerateArray() |> List.ofSeq)
        else
            err path $"expected an array, got %A{el.ValueKind}"

    let stringField path name el =
        prop path name el |> Result.bind (asString $"%s{path}.%s{name}")

    let intField path name el =
        prop path name el |> Result.bind (asInt $"%s{path}.%s{name}")

    let collect (results: Result<'a, Error list> list) : Result<'a list, Error list> =
        let errors =
            results
            |> List.collect (
                function
                | Error e -> e
                | Ok _ -> []
            )

        if List.isEmpty errors then
            Ok(
                results
                |> List.choose (
                    function
                    | Ok v -> Some v
                    | Error _ -> None
                )
            )
        else
            Error errors
