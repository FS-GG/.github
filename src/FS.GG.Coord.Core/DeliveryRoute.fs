namespace FS.GG.Coord

module DeliveryRoute =
    [<Literal>]
    let Schema = "fsgg.coord.delivery-route/v1"

    type Route = Lightweight | SddRequired

    type Receipt =
        { Schema: string; Subject: string; SubjectRevision: string; Route: Route option
          Agent: string; Timestamp: string; ReasonCodes: string list; Rationale: string
          DeclaredImpacts: string list; ObservedFacts: string list; SddWorkId: string option
          SpecHome: string option; RequiredGates: string list }

    type Verdict = Current of Receipt | Stale of string list | Unreadable of string list

    let private nonEmpty field value =
        if System.String.IsNullOrWhiteSpace value then [ $"%s{field} is required" ] else []

    let private nonEmptyList field values =
        if List.isEmpty values || values |> List.exists System.String.IsNullOrWhiteSpace then [ $"%s{field} must contain one or more non-empty values" ] else []

    let private timestamp (value: string) =
        match System.DateTimeOffset.TryParse(value, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind) with
        | true, _ -> []
        | false, _ -> [ "timestamp must be an ISO-8601 instant" ]

    let private tokens field values =
        let malformed value =
            System.String.IsNullOrWhiteSpace value
            || value |> Seq.exists (fun c -> not (System.Char.IsLetterOrDigit c || c = '-' || c = '_' || c = '.'))
        if values |> List.exists malformed then [ $"%s{field} must contain non-empty machine tokens" ] else []

    /// An SDD route is bound to one concrete work package.  This is deliberately stricter than
    /// merely carrying non-empty strings: otherwise a receipt could name a removed work directory or
    /// silently substitute a neighbouring specification while still looking complete on the board.
    let validateSddBinding receipt =
        match receipt.Route, receipt.SddWorkId, receipt.SpecHome with
        | Some SddRequired, Some workId, Some specHome when not (System.String.IsNullOrWhiteSpace workId) ->
            let expectedSpec = $"work/%s{workId}/spec.md"
            [ yield! tokens "sddWorkId" [ workId ]
              if specHome <> expectedSpec then
                  yield $"specHome must bind sddWorkId at '%s{expectedSpec}'"
              if receipt.RequiredGates <> [ "implementationReady"; "analyze"; "verify"; "ship" ] then
                  yield "requiredGates must be implementationReady, analyze, verify, ship in lifecycle order" ]
        | _ -> []

    let validate expectedSubject expectedRevision receipt =
        let errors =
            [ if receipt.Schema <> Schema then yield $"schema must be '%s{Schema}'"
              if receipt.Subject <> expectedSubject then yield "subject does not match the live item"
              if receipt.SubjectRevision <> expectedRevision then yield "subjectRevision is stale"
              yield! nonEmpty "agent" receipt.Agent
              yield! nonEmpty "timestamp" receipt.Timestamp
              yield! timestamp receipt.Timestamp
              yield! nonEmpty "rationale" receipt.Rationale
              yield! nonEmptyList "reasonCodes" receipt.ReasonCodes
              yield! tokens "reasonCodes" receipt.ReasonCodes
              yield! nonEmptyList "declaredImpacts" receipt.DeclaredImpacts
              yield! tokens "declaredImpacts" receipt.DeclaredImpacts
              yield! nonEmptyList "observedFacts" receipt.ObservedFacts
              yield! tokens "observedFacts" receipt.ObservedFacts
              match receipt.Route with
              | None -> yield "route is required and must be agent-authored"
              | Some Lightweight when receipt.SddWorkId.IsSome || receipt.SpecHome.IsSome || not (List.isEmpty receipt.RequiredGates) ->
                  yield "lightweight route must not carry SDD bindings or gates"
              | Some Lightweight -> ()
              | Some SddRequired ->
                  match receipt.SddWorkId with
                  | Some value when not (System.String.IsNullOrWhiteSpace value) -> ()
                  | _ -> yield "sdd-required route requires sddWorkId"
                  match receipt.SpecHome with
                  | Some value when not (System.String.IsNullOrWhiteSpace value) -> ()
                  | _ -> yield "sdd-required route requires specHome"
                  yield! nonEmptyList "requiredGates" receipt.RequiredGates
                  yield! validateSddBinding receipt ]
        if List.isEmpty errors then Ok receipt else Error errors

    let decide expectedSubject expectedRevision receipt =
        match expectedRevision, receipt with
        | None, _ -> Unreadable [ "subject revision could not be read" ]
        | Some _, None -> Stale [ "delivery-route receipt is missing" ]
        | Some revision, Some candidate ->
            match validate expectedSubject revision candidate with
            | Ok valid -> Current valid
            | Error errors -> Stale errors
