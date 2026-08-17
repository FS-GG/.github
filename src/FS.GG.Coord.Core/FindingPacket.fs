namespace FS.GG.Coord

open System
open System.Text.Json
open System.Text.RegularExpressions

module FindingPacket =
    [<Literal>]
    let Schema = "fsgg.coord.finding-packet/v1"

    [<Literal>]
    let ResultSchema = "fsgg.coord.packet-result/v1"

    type Answer =
        | Found of string
        | SearchedNotFound of string
        | NotSearched of string

    type Cause =
        | Established of string
        | NotEstablished of string

    type Packet =
        { Schema: string
          Surface: string
          Cause: Cause
          RedToday: Answer
          DerivedBy: Answer
          ClassRow: Answer
          WhyNotHere: string
          Paths: string list
          Finder: string }

    type Finding = { Field: string; Detail: string }

    type IntakeSeed =
        { Observed: string
          RootCause: string
          Paths: string list }

    let private fieldNames =
        [ "schema"; "surface"; "cause"; "redToday"; "derivedBy"; "classRow"; "whyNotHere"; "paths"; "finder" ]

    let private known = Set.ofList fieldNames

    // The reply path is `scripts/fsgg-coord say <ref> --to <worker>`, which takes an id. A sentence
    // that NAMES an id is not one, and the analyst cannot reply to it.
    let private workerId = Regex(@"^[a-z][a-z0-9]*-[a-z0-9]+$", RegexOptions.Compiled)

    let private answerShape =
        "must be an object carrying exactly one of 'found' (the finder searched and this is the answer), "
        + "'searchedNotFound' (the finder searched and there is none; the string is the search performed) "
        + "or 'notSearched' (the finder did not search; the string is why not)"

    let private causeShape =
        "must be an object carrying exactly one of 'established' (the root cause) "
        + "or 'notEstablished' (what was measured instead)"

    // The reason `none` and `null` are both refused, said once so every field says it the same way.
    let private collapses =
        "cannot distinguish 'the finder searched and found nothing' from 'the finder did not search', "
        + "and the filing bar's tests rest on that distinction"

    let private oneOf (field: string) (shape: string) (blame: string) =
        { Field = field; Detail = $"%s{blame}; it %s{shape}" }

    // Decode a one-key tagged union. `cases` maps the JSON key to its constructor.
    let private decodeUnion (field: string) (shape: string) (cases: (string * (string -> 'a)) list) (element: JsonElement) =
        match element.ValueKind with
        | JsonValueKind.String ->
            let raw = element.GetString()
            Error(oneOf field shape $"is the string '%s{raw}', which %s{collapses}")
        | JsonValueKind.Null -> Error(oneOf field shape $"is null, which %s{collapses}")
        | JsonValueKind.Object ->
            let properties = element.EnumerateObject() |> Seq.toList

            match properties with
            | [ property ] ->
                match cases |> List.tryFind (fun (name, _) -> name = property.Name) with
                | None -> Error(oneOf field shape $"names the unknown case '%s{property.Name}'")
                | Some(_, construct) ->
                    if property.Value.ValueKind = JsonValueKind.String then
                        Ok(construct (property.Value.GetString()))
                    else
                        Error(oneOf field shape $"'%s{property.Name}' must carry a string")
            | [] -> Error(oneOf field shape "is an empty object")
            | _ ->
                let named = properties |> List.map (fun property -> property.Name) |> String.concat ", "
                Error(oneOf field shape $"names more than one case (%s{named})")
        | other -> Error(oneOf field shape $"is a JSON %s{other.ToString().ToLowerInvariant()}")

    let private requiredString (root: JsonElement) (field: string) =
        match root.TryGetProperty field with
        | true, value when value.ValueKind = JsonValueKind.String -> Ok(value.GetString())
        | true, value when value.ValueKind = JsonValueKind.Null ->
            Error { Field = field; Detail = "is required and must be a string, but is null" }
        | true, _ -> Error { Field = field; Detail = "must be a string" }
        | false, _ -> Error { Field = field; Detail = "is required" }

    let private requiredUnion (root: JsonElement) (field: string) shape cases =
        match root.TryGetProperty field with
        | true, value -> decodeUnion field shape cases value
        | false, _ -> Error { Field = field; Detail = $"is required and %s{shape}" }

    let private requiredPaths (root: JsonElement) =
        match root.TryGetProperty "paths" with
        | true, value when value.ValueKind = JsonValueKind.Array ->
            let items = value.EnumerateArray() |> Seq.toList

            if items |> List.forall (fun item -> item.ValueKind = JsonValueKind.String) then
                Ok(items |> List.map (fun item -> item.GetString()))
            else
                Error { Field = "paths"; Detail = "must be an array of strings" }
        | true, _ -> Error { Field = "paths"; Detail = "must be an array of strings" }
        | false, _ -> Error { Field = "paths"; Detail = "is required" }

    let private failures results =
        results |> List.choose (function Error finding -> Some finding | Ok _ -> None)

    let private validPath (path: string) =
        not (String.IsNullOrWhiteSpace path)
        && not (path.StartsWith "/")
        && not (path.Contains "\\")
        && path.Split('/') |> Array.forall (fun segment -> segment <> "" && segment <> "." && segment <> "..")

    let private answerPayload =
        function
        | Found value -> "found", value
        | SearchedNotFound value -> "searchedNotFound", value
        | NotSearched value -> "notSearched", value

    let private causePayload =
        function
        | Established value -> "established", value
        | NotEstablished value -> "notEstablished", value

    let validate (packet: Packet) =
        let findings =
            [ if packet.Schema <> Schema then
                  yield { Field = "schema"; Detail = $"must be '{Schema}'" }

              for field, value in
                  [ "surface", packet.Surface; "whyNotHere", packet.WhyNotHere; "finder", packet.Finder ] do
                  if String.IsNullOrWhiteSpace value then
                      yield { Field = field; Detail = "is required" }

              // Every union case carries a string the analyst can CHECK. A blank one is the sentinel
              // this schema exists to abolish, wearing the new shape.
              let caseName, causeValue = causePayload packet.Cause

              if String.IsNullOrWhiteSpace causeValue then
                  yield { Field = "cause"; Detail = $"'%s{caseName}' must carry a non-blank string" }

              for field, answer in
                  [ "redToday", packet.RedToday; "derivedBy", packet.DerivedBy; "classRow", packet.ClassRow ] do
                  let caseName, value = answerPayload answer

                  if String.IsNullOrWhiteSpace value then
                      yield { Field = field; Detail = $"'%s{caseName}' must carry a non-blank string" }

              if not (String.IsNullOrWhiteSpace packet.Finder) && not (workerId.IsMatch packet.Finder) then
                  yield
                      { Field = "finder"
                        Detail =
                          "must be the finder's minted worker id alone (for example 'merlin-efd3'), because "
                          + "`scripts/fsgg-coord say <ref> --to <worker>` is how an analyst replies and it takes an id, "
                          + "not a sentence naming one" }

              if List.isEmpty packet.Paths then
                  yield { Field = "paths"; Detail = "must declare at least one path" }

              if packet.Paths |> List.exists (validPath >> not) then
                  yield
                      { Field = "paths"
                        Detail = "must be relative repository paths without empty, '.' or '..' segments" } ]

        if List.isEmpty findings then Ok packet else Error findings

    let parse (document: string) =
        try
            use parsed = JsonDocument.Parse document
            let root = parsed.RootElement

            if root.ValueKind <> JsonValueKind.Object then
                Error [ { Field = "document"; Detail = $"must be a JSON object carrying a {Schema} packet" } ]
            else

            let unknown =
                root.EnumerateObject()
                |> Seq.map (fun property -> property.Name)
                |> Seq.filter (known.Contains >> not)
                |> Seq.toList

            if not (List.isEmpty unknown) then
                Error
                    [ for name in unknown ->
                        { Field = name
                          Detail =
                            $"is not a field of {Schema}; the fields are " + String.concat ", " fieldNames } ]
            else

            let schema = requiredString root "schema"
            let surface = requiredString root "surface"
            let whyNotHere = requiredString root "whyNotHere"
            let finder = requiredString root "finder"

            let cause =
                requiredUnion root "cause" causeShape [ "established", Established; "notEstablished", NotEstablished ]

            let answerCases =
                [ "found", Found; "searchedNotFound", SearchedNotFound; "notSearched", NotSearched ]

            let redToday = requiredUnion root "redToday" answerShape answerCases
            let derivedBy = requiredUnion root "derivedBy" answerShape answerCases
            let classRow = requiredUnion root "classRow" answerShape answerCases
            let paths = requiredPaths root

            match schema, surface, whyNotHere, finder, cause, redToday, derivedBy, classRow, paths with
            | Ok schema, Ok surface, Ok whyNotHere, Ok finder, Ok cause, Ok redToday, Ok derivedBy, Ok classRow, Ok paths ->
                Ok
                    { Schema = schema
                      Surface = surface
                      Cause = cause
                      RedToday = redToday
                      DerivedBy = derivedBy
                      ClassRow = classRow
                      WhyNotHere = whyNotHere
                      Paths = paths
                      Finder = finder }
            | _ ->
                Error(
                    failures [ schema; surface; whyNotHere; finder ]
                    @ failures [ cause ]
                    @ failures [ redToday; derivedBy; classRow ]
                    @ failures [ paths ]
                )
        with :? JsonException as ex ->
            Error [ { Field = "document"; Detail = $"is not valid JSON: %s{ex.Message}" } ]

    let toIntakeSeed (packet: Packet) =
        { Observed = packet.Surface
          RootCause =
            match packet.Cause with
            | Established cause -> cause
            // .github#1858's rule, applied at the packet: an unestablished cause says so explicitly and
            // gives what was measured instead, rather than silently becoming a blank field downstream.
            | NotEstablished measured -> $"Root cause not established. Measured instead: %s{measured}"
          Paths = packet.Paths }

    let renderResult (packet: Packet) =
        let text (value: string) = JsonSerializer.Serialize value
        let caseOf = answerPayload >> fst
        let causeCase, _ = causePayload packet.Cause
        let paths = packet.Paths |> List.map text |> String.concat ","

        String.concat
            ""
            [ "{\"schema\":\""
              ResultSchema
              "\",\"kind\":\"validated\",\"finder\":"
              text packet.Finder
              ",\"surface\":"
              text packet.Surface
              ",\"cause\":\""
              causeCase
              "\",\"redToday\":\""
              caseOf packet.RedToday
              "\",\"derivedBy\":\""
              caseOf packet.DerivedBy
              "\",\"classRow\":\""
              caseOf packet.ClassRow
              "\",\"paths\":["
              paths
              "],\"writes\":0}" ]
