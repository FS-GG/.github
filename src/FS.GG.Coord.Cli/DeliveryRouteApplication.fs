namespace FS.GG.Coord.Cli

open System
open System.IO
open System.Text.Json
open FS.GG.Coord
open FS.GG.Coord.Cli.Options

module DeliveryRouteApplication =
    let private requiredString (root: JsonElement) (name: string) =
        match root.TryGetProperty name with
        | true, value when value.ValueKind = JsonValueKind.String -> Ok(value.GetString())
        | true, _ -> Error $"{name} must be a string"
        | false, _ -> Error $"{name} is required"

    let private optionalString (root: JsonElement) (name: string) =
        match root.TryGetProperty name with
        | false, _ -> Ok None
        | true, value when value.ValueKind = JsonValueKind.Null -> Ok None
        | true, value when value.ValueKind = JsonValueKind.String -> Ok(Some(value.GetString()))
        | true, _ -> Error $"{name} must be a string or null"

    let private strings (root: JsonElement) (name: string) =
        match root.TryGetProperty name with
        | true, value when value.ValueKind = JsonValueKind.Array && value.EnumerateArray() |> Seq.forall (fun item -> item.ValueKind = JsonValueKind.String) ->
            Ok(value.EnumerateArray() |> Seq.map _.GetString() |> List.ofSeq)
        | true, _ -> Error $"{name} must be an array of strings"
        | false, _ -> Error $"{name} is required"

    let private integer (root: JsonElement) (name: string) =
        match root.TryGetProperty name with
        | true, value ->
            match value.TryGetInt32() with
            | true, number -> Ok number
            | _ -> Error $"{name} must be a 32-bit integer"
        | false, _ -> Error $"{name} is required"

    let private route (root: JsonElement) =
        match root.TryGetProperty "route" with
        | true, value when value.ValueKind = JsonValueKind.String && value.GetString() = "lightweight" -> Ok(Some DeliveryRoute.Lightweight)
        | true, value when value.ValueKind = JsonValueKind.String && value.GetString() = "sdd-required" -> Ok(Some DeliveryRoute.SddRequired)
        | true, value when value.ValueKind = JsonValueKind.Null -> Ok None
        | true, _ -> Error "route must be lightweight, sdd-required, or null"
        | false, _ -> Error "route is required"

    let decodeStructured (raw: string) =
        try
            use document = JsonDocument.Parse raw
            let root = document.RootElement
            if root.ValueKind <> JsonValueKind.Object then Error "record must be a JSON object" else
            let schema = requiredString root "schema"
            let subject = requiredString root "subject"
            let revision = integer root "revision"
            let previous = optionalString root "previousDigest"
            let scope = strings root "scope"
            let dependencies = strings root "dependencies"
            let touchSet = strings root "touchSet"
            let policy = requiredString root "policyVersion"
            let selectedRoute = route root
            let agent = requiredString root "agent"
            let timestamp = requiredString root "timestamp"
            let reasons = strings root "reasonCodes"
            let rationale = requiredString root "rationale"
            let work = optionalString root "sddWorkId"
            let spec = optionalString root "specHome"
            let gates = strings root "requiredGates"
            let digest = requiredString root "digest"
            match schema, subject, revision, previous, scope, dependencies, touchSet, policy,
                  selectedRoute, agent, timestamp, reasons, rationale, work, spec, gates, digest with
            | Ok schema, Ok subject, Ok revision, Ok previous, Ok scope, Ok dependencies, Ok touchSet,
              Ok policy, Ok selectedRoute, Ok agent, Ok timestamp, Ok reasons, Ok rationale, Ok work,
              Ok spec, Ok gates, Ok digest ->
                Ok
                    ({ Schema = schema; Subject = subject; Revision = revision; PreviousDigest = previous
                       Scope = scope; Dependencies = dependencies; TouchSet = touchSet; PolicyVersion = policy
                       Route = selectedRoute; Agent = agent; Timestamp = timestamp; ReasonCodes = reasons
                       Rationale = rationale; SddWorkId = work; SpecHome = spec; RequiredGates = gates
                       Digest = digest }: StructuredDecision.RouteRecord)
            | _ ->
                let error = function Error value -> [ value ] | Ok _ -> []
                [ yield! error schema; yield! error subject; yield! error revision; yield! error previous
                  yield! error scope; yield! error dependencies; yield! error touchSet; yield! error policy
                  yield! error selectedRoute; yield! error agent; yield! error timestamp; yield! error reasons
                  yield! error rationale; yield! error work; yield! error spec; yield! error gates; yield! error digest ]
                |> String.concat "; " |> Error
        with :? JsonException as error -> Error $"record is not valid JSON: {error.Message}"

    let private render kind errors =
        let detail = errors |> String.concat "; "
        printfn "%s" (JsonSerializer.Serialize {| schema = "fsgg.coord.delivery-route-result/v2"; kind = kind; errors = errors; detail = detail |})

    let run opts =
        match opts.Args with
        | [ "validate"; subject; path ] ->
            match File.ReadAllText path |> decodeStructured with
            | Error error -> render "refusal" [ error ]; ExitCode.toInt ExitCode.Error
            | Ok record ->
                match StructuredDecision.validateRouteLedger subject [ record ] with
                | Ok _ -> render "current" []; ExitCode.toInt ExitCode.Green
                | Error errors -> render "refusal" errors; ExitCode.toInt ExitCode.Error
        | [ "record"; _ ]
        | [ "show"; _ ] ->
            render "refusal" [ "route record/show require the live GitHub receipt boundary" ]; ExitCode.toInt ExitCode.Error
        | _ ->
            render "refusal" [ "usage: route validate <subject> <record.json>" ]; ExitCode.toInt ExitCode.Error
