namespace FS.GG.Coord

open System
open System.IO
open System.Text
open System.Text.Json
open System.Text.Json.Nodes
open System.Text.RegularExpressions

module TelemetryStore =
    [<Literal>]
    let BatchSchema = "fsgg.telemetry.ingest/1"
    [<Literal>]
    let MaxBatchBytes = 64 * 1024
    [<Literal>]
    let MaxEventBytes = 64 * 1024
    [<Literal>]
    let MaxEvents = 64

    type Coverage =
        { RecordValidity: string; JoinIntegrity: string; PopulationCoverage: string; Qualification: string
          Eligible: int64 option; Observed: int64 option }
    type Payload =
        | Item of featureId: string option
        | Feature of name: string
        | Attempt of parentItemId: string * status: string
        | ParentChild of parentId: string * childId: string
        | PullRequestHead of repository: string * pullRequest: int64 * head: string
        | Source of sourceIdentity: string * generation: string * cursor: string
        | Usage of provider: string * model: string * effort: string * input: int64 * cachedInput: int64 * cacheWriteInput: int64 * output: int64 * reasoning: int64 option * total: int64 * responses: int64 * sessions: int64 * turns: int64
        | Delivery of state: string * expectedHead: string option * observedHead: string option * pullRequest: int64 option
        | Evidence of digest: string * availability: string
        | Coverage of Coverage
        | Diagnostic of code: string * severity: string
        | Correction of targetIdentity: string * reason: string
    type Fact =
        { Identity: string; ItemId: string option; Revision: int64; Kind: string; Payload: Payload
          Canonical: string; ContentDigest: string }
    type Batch =
        { IngestId: string; SourceIdentity: string; Generation: string; Cursor: string; Facts: Fact list; ContentDigest: string }
    type Aggregate =
        { ItemId: string; FactCount: int64; UsageObservations: int64; DeliveryObservations: int64
          Input: int64; CachedInput: int64; CacheWriteInput: int64; Output: int64; Reasoning: int64 option; Total: int64
          RecordValidity: string; JoinIntegrity: string; PopulationCoverage: string; Qualification: string }
    type DurabilityAssessment = ApprovedLocalDurable | Unsafe of reason: string | DurabilityUnverified of reason: string

    let private nonEmpty label (value: string) =
        if String.IsNullOrWhiteSpace value then Error $"%s{label} must be a non-empty string" else Ok value
    let private requiredText (label: string) (node: JsonElement) (name: string) =
        match node.TryGetProperty name with
        | true, value when value.ValueKind = JsonValueKind.String -> nonEmpty $"%s{label}.%s{name}" (value.GetString())
        | _ -> Error $"%s{label}.%s{name} must be a non-empty string"
    let private optionalText (label: string) (node: JsonElement) (name: string) =
        match node.TryGetProperty name with
        | false, _ -> Ok None
        | true, value when value.ValueKind = JsonValueKind.Null -> Ok None
        | true, value when value.ValueKind = JsonValueKind.String -> nonEmpty $"%s{label}.%s{name}" (value.GetString()) |> Result.map Some
        | _ -> Error $"%s{label}.%s{name} must be a non-empty string or null"
    let private requiredInt (label: string) (node: JsonElement) (name: string) =
        match node.TryGetProperty name with
        | true, value when value.ValueKind = JsonValueKind.Number ->
            match value.TryGetInt64() with true, number when number >= 0L -> Ok number | _ -> Error $"%s{label}.%s{name} must be a non-negative integer"
        | _ -> Error $"%s{label}.%s{name} must be a non-negative integer"
    let private optionalInt (label: string) (node: JsonElement) (name: string) =
        match node.TryGetProperty name with
        | false, _ -> Ok None
        | true, value when value.ValueKind = JsonValueKind.Null -> Ok None
        | true, value when value.ValueKind = JsonValueKind.Number ->
            match value.TryGetInt64() with true, number when number >= 0L -> Ok(Some number) | _ -> Error $"%s{label}.%s{name} must be a non-negative integer or null"
        | _ -> Error $"%s{label}.%s{name} must be a non-negative integer or null"
    let private closed label allowed (node: JsonElement) =
        let unknown = node.EnumerateObject() |> Seq.map _.Name |> Seq.filter (fun name -> not (Set.contains name allowed)) |> Seq.toList
        let names = String.concat "," unknown
        if unknown.IsEmpty then Ok () else Error $"%s{label} contains unknown field(s): %s{names}"
    let private sequence results =
        let errors = results |> List.choose (function Error e -> Some e | _ -> None)
        if errors.IsEmpty then Ok(results |> List.choose (function Ok value -> Some value | _ -> None)) else Error errors
    let private checkedAdd label left right =
        try Ok(Checked.(+) left right) with :? OverflowException -> Error $"%s{label} overflows int64"

    let private parseEvent index (node: JsonElement) =
        let label = $"events[%d{index}]"
        let kindResult = requiredText label node "kind"
        let identityResult = requiredText label node "identity"
        let itemResult = optionalText label node "itemId"
        let revisionResult = optionalInt label node "revision" |> Result.map (Option.defaultValue 0L)
        match kindResult, identityResult, itemResult, revisionResult with
        | Ok kind, Ok identity, Ok itemId, Ok revision ->
            let common = Set.ofList [ "kind"; "identity"; "itemId"; "revision" ]
            let make allowed payload =
                closed label (Set.union common (Set.ofList allowed)) node
                |> Result.map (fun () ->
                    let canonical = JsonNode.Parse(node.GetRawText()) |> fun value -> CanonicalJson.canonicalize (Encoding.UTF8.GetBytes(value.ToJsonString())) |> Result.defaultWith invalidOp
                    { Identity = identity; ItemId = itemId; Revision = revision; Kind = kind; Payload = payload; Canonical = canonical; ContentDigest = CanonicalJson.sha256(Encoding.UTF8.GetBytes canonical) })
            match kind with
            | "item" -> optionalText label node "featureId" |> Result.bind (Item >> make [ "featureId" ])
            | "feature" -> requiredText label node "name" |> Result.bind (Feature >> make [ "name" ])
            | "attempt" ->
                match requiredText label node "parentItemId", requiredText label node "status" with
                | Ok parent, Ok status -> make [ "parentItemId"; "status" ] (Attempt(parent, status))
                | values -> Error(sprintf "%A" values)
            | "parent-child" ->
                match requiredText label node "parentId", requiredText label node "childId" with
                | Ok parent, Ok child -> make [ "parentId"; "childId" ] (ParentChild(parent, child))
                | values -> Error(sprintf "%A" values)
            | "pr-head" ->
                match requiredText label node "repository", requiredInt label node "prNumber", requiredText label node "head" with
                | Ok repo, Ok pr, Ok head when head.Length = 40 && head |> Seq.forall Char.IsAsciiHexDigitLower -> make [ "repository"; "prNumber"; "head" ] (PullRequestHead(repo, pr, head))
                | Ok _, Ok _, Ok _ -> Error $"%s{label}.head must be 40 lowercase hexadecimal characters"
                | values -> Error(sprintf "%A" values)
            | "source" ->
                match requiredText label node "sourceIdentity", requiredText label node "generation", requiredText label node "cursor" with
                | Ok source, Ok generation, Ok cursor -> make [ "sourceIdentity"; "generation"; "cursor" ] (Source(source, generation, cursor))
                | values -> Error(sprintf "%A" values)
            | "usage" ->
                match requiredText label node "provider", requiredText label node "model", requiredText label node "effort",
                      requiredInt label node "input", requiredInt label node "cachedInput", requiredInt label node "cacheWriteInput",
                      requiredInt label node "output", optionalInt label node "reasoning", requiredInt label node "total",
                      requiredInt label node "responses", requiredInt label node "sessions", requiredInt label node "turns" with
                | Ok provider, Ok model, Ok effort, Ok input, Ok cached, Ok write, Ok output, Ok reasoning, Ok total, Ok responses, Ok sessions, Ok turns ->
                    match checkedAdd "usage.total" input output with
                    | Error reason -> Error reason
                    | Ok expected when expected <> total -> Error $"%s{label}.total must equal input + output"
                    | Ok _ when cached > input || write > input - cached -> Error $"%s{label} cachedInput + cacheWriteInput exceeds input"
                    | Ok _ when reasoning |> Option.exists (fun count -> count > output) -> Error $"%s{label}.reasoning exceeds output"
                    | Ok _ -> make [ "provider"; "model"; "effort"; "input"; "cachedInput"; "cacheWriteInput"; "output"; "reasoning"; "total"; "responses"; "sessions"; "turns" ] (Usage(provider, model, effort, input, cached, write, output, reasoning, total, responses, sessions, turns))
                | values -> Error(sprintf "%A" values)
            | "delivery" ->
                match requiredText label node "state", optionalText label node "expectedHead", optionalText label node "observedHead", optionalInt label node "prNumber" with
                | Ok state, Ok expected, Ok observed, Ok pr -> make [ "state"; "expectedHead"; "observedHead"; "prNumber" ] (Delivery(state, expected, observed, pr))
                | values -> Error(sprintf "%A" values)
            | "evidence" ->
                match requiredText label node "digest", requiredText label node "availability" with
                | Ok digest, Ok availability -> make [ "digest"; "availability" ] (Evidence(digest, availability))
                | values -> Error(sprintf "%A" values)
            | "coverage" ->
                match requiredText label node "recordValidity", requiredText label node "joinIntegrity", requiredText label node "populationCoverage", requiredText label node "qualification", optionalInt label node "eligible", optionalInt label node "observed" with
                | Ok validity, Ok join, Ok coverage, Ok qualification, Ok eligible, Ok observed ->
                    let allowedCoverage = Set.ofList [ "unknown"; "partial"; "complete"; "not-evaluated" ]
                    if not (Set.contains coverage allowedCoverage) then Error $"%s{label}.populationCoverage has an unsupported value"
                    else make [ "recordValidity"; "joinIntegrity"; "populationCoverage"; "qualification"; "eligible"; "observed" ] (Coverage { RecordValidity = validity; JoinIntegrity = join; PopulationCoverage = coverage; Qualification = qualification; Eligible = eligible; Observed = observed })
                | values -> Error(sprintf "%A" values)
            | "diagnostic" ->
                match requiredText label node "code", requiredText label node "severity" with
                | Ok code, Ok severity -> make [ "code"; "severity" ] (Diagnostic(code, severity))
                | values -> Error(sprintf "%A" values)
            | "correction" ->
                match requiredText label node "targetIdentity", requiredText label node "reason" with
                | Ok target, Ok reason -> make [ "targetIdentity"; "reason" ] (Correction(target, reason))
                | values -> Error(sprintf "%A" values)
            | _ -> Error $"%s{label}.kind is unsupported"
        | values -> Error(sprintf "%A" values)

    let parseBatch (bytes: byte array) =
        if bytes.Length > MaxBatchBytes then Error [ $"batch exceeds %d{MaxBatchBytes} bytes" ] else
        try
            use document = JsonDocument.Parse bytes
            let root = document.RootElement
            if root.ValueKind <> JsonValueKind.Object then Error [ "batch must be a JSON object" ] else
            match closed "batch" (Set.ofList [ "schema"; "ingestId"; "sourceIdentity"; "generation"; "cursor"; "eventCount"; "events" ]) root,
                  requiredText "batch" root "schema", requiredText "batch" root "ingestId", requiredText "batch" root "sourceIdentity",
                  requiredText "batch" root "generation", requiredText "batch" root "cursor", requiredInt "batch" root "eventCount" with
            | Ok (), Ok schema, Ok ingestId, Ok source, Ok generation, Ok cursor, Ok declared when schema = BatchSchema ->
                if not (Regex.IsMatch(ingestId, "^[A-Za-z0-9][A-Za-z0-9._-]{0,127}$")) then Error [ "batch.ingestId is not a safe immutable-batch identifier" ]
                elif not (Regex.IsMatch(source, "^[A-Za-z0-9][A-Za-z0-9._-]{0,127}$")) then Error [ "batch.sourceIdentity is not a safe producer-stream identifier" ]
                else
                    match root.TryGetProperty "events" with
                    | true, events when events.ValueKind = JsonValueKind.Array && events.GetArrayLength() <= MaxEvents ->
                        let values = events.EnumerateArray() |> Seq.toList
                        if int64 values.Length <> declared then Error [ "batch.eventCount does not match events length" ] else
                        let oversize = values |> List.tryFindIndex (fun event -> Encoding.UTF8.GetByteCount(event.GetRawText()) > MaxEventBytes)
                        match oversize with
                        | Some index -> Error [ $"events[%d{index}] exceeds %d{MaxEventBytes} bytes" ]
                        | None ->
                            match values |> List.mapi parseEvent |> sequence with
                            | Error errors -> Error errors
                            | Ok facts ->
                                let duplicates = facts |> List.countBy _.Identity |> List.filter (fun (_, count) -> count > 1)
                                if not duplicates.IsEmpty then Error [ "batch contains duplicate native fact identities" ] else
                                match CanonicalJson.canonicalize bytes with
                                | Error reason -> Error [ reason ]
                                | Ok canonical -> Ok { IngestId = ingestId; SourceIdentity = source; Generation = generation; Cursor = cursor; Facts = facts; ContentDigest = CanonicalJson.sha256(Encoding.UTF8.GetBytes canonical) }
                    | true, events when events.ValueKind = JsonValueKind.Array -> Error [ $"batch exceeds %d{MaxEvents} events" ]
                    | _ -> Error [ "batch.events must be an array" ]
            | Ok (), Ok schema, _, _, _, _, _ -> Error [ $"unsupported batch schema '%s{schema}'" ]
            | values -> Error [ sprintf "%A" values ]
        with :? JsonException as error -> Error [ $"invalid JSON: %s{error.Message}" ]

    let reduce itemId (facts: Fact list) =
        let selected = facts |> List.filter (fun fact -> fact.ItemId = Some itemId || (fact.Kind = "item" && fact.Identity = itemId))
        let usage = selected |> List.choose (fun fact -> match fact.Payload with Usage(_,_,_,i,c,w,o,r,t,_,_,_) -> Some(i,c,w,o,r,t) | _ -> None)
        let add label selector = usage |> List.map selector |> List.fold (fun state value -> state |> Result.bind (fun current -> checkedAdd label current value)) (Ok 0L)
        match add "input" (fun (v,_,_,_,_,_) -> v), add "cachedInput" (fun (_,v,_,_,_,_) -> v), add "cacheWriteInput" (fun (_,_,v,_,_,_) -> v), add "output" (fun (_,_,_,v,_,_) -> v), add "total" (fun (_,_,_,_,_,v) -> v) with
        | Ok input, Ok cached, Ok write, Ok output, Ok total ->
            let reasoningValues = usage |> List.map (fun (_,_,_,_,v,_) -> v)
            let reasoning = if reasoningValues |> List.exists Option.isNone then None else reasoningValues |> List.choose id |> List.fold (fun state value -> state |> Result.bind (fun current -> checkedAdd "reasoning" current value)) (Ok 0L) |> Result.toOption
            let coverage = selected |> List.choose (fun fact -> match fact.Payload with Coverage value -> Some value | _ -> None) |> List.tryLast
            let value name getter = coverage |> Option.map getter |> Option.defaultValue name
            Ok { ItemId = itemId; FactCount = int64 selected.Length; UsageObservations = int64 usage.Length; DeliveryObservations = selected |> List.filter (fun fact -> match fact.Payload with Delivery _ -> true | _ -> false) |> List.length |> int64
                 Input = input; CachedInput = cached; CacheWriteInput = write; Output = output; Reasoning = reasoning; Total = total
                 RecordValidity = value "unknown" _.RecordValidity; JoinIntegrity = value "unknown" _.JoinIntegrity
                 PopulationCoverage = value "unknown" _.PopulationCoverage; Qualification = value "not-evaluated" _.Qualification }
        | values -> Error [ sprintf "%A" values ]

    let publicJson aggregate =
        JsonSerializer.Serialize
            {| schema = "fsgg.telemetry.public-summary/1"; item = aggregate.ItemId; factCount = aggregate.FactCount
               usageObservations = aggregate.UsageObservations; deliveryObservations = aggregate.DeliveryObservations
               usage = {| input = aggregate.Input; cachedInput = aggregate.CachedInput; cacheWriteInput = aggregate.CacheWriteInput; output = aggregate.Output; reasoning = aggregate.Reasoning; total = aggregate.Total |}
               recordValidity = aggregate.RecordValidity; joinIntegrity = aggregate.JoinIntegrity
               populationCoverage = aggregate.PopulationCoverage; qualification = aggregate.Qualification |} + "\n"

    let validateStoreRoot path assessment =
        if String.IsNullOrWhiteSpace path then Error [ "store root is not configured" ]
        elif not (Path.IsPathFullyQualified path) then Error [ "store root must be an absolute path" ]
        else
            match assessment with
            | ApprovedLocalDurable -> Ok(Path.GetFullPath path)
            | Unsafe reason -> Error [ "unsafe store root: " + reason ]
            | DurabilityUnverified reason -> Error [ "store root durability-unverified: " + reason ]
