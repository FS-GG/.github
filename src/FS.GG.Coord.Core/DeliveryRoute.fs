namespace FS.GG.Coord

module DeliveryRoute =
    [<Literal>]
    let Schema = "fsgg.coord.route-decision/effective"

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

    /// A work id this module is willing to build a filesystem path out of: it must LEAD with an
    /// alphanumeric. `tokens` above already refuses `/`, `\`, and whitespace, but it accepts `.` freely —
    /// so `..` is a valid `sddWorkId` by that rule alone, and `work/../spec.md` satisfies
    /// `validateSddBinding`'s expected-spec form. Concatenating that into an exemption would name the
    /// repository root. One leading-character rule closes `.`, `..`, and `.hidden` together, and every
    /// real work id (`2306-widen-refusal-atomicity`, `2496`) begins with a digit.
    let private pathSafeWorkId =
        System.Text.RegularExpressions.Regex(@"^[A-Za-z0-9][A-Za-z0-9._-]*$", System.Text.RegularExpressions.RegexOptions.Compiled)

    /// The package directories the `sdd-required` route ITSELF produces — `work/<sddWorkId>` and
    /// `readiness/<sddWorkId>` — or the empty list for any receipt that does not cleanly name one.
    ///
    /// FAILS CLOSED, and the asymmetry is the point (.github#2324): the empty list exempts NOTHING and
    /// leaves every changed file reported exactly as it is today, while a wrong list would silently
    /// excuse a path nobody declared. "I could not read what this route obliges" and "this route obliges
    /// nothing" are opposite facts, and only one of them is safe to act on (#266).
    ///
    /// IT ASKS `validateSddBinding` RATHER THAN RESTATING IT. That function already pins `specHome` to
    /// `work/<workId>/spec.md` and already restricts `sddWorkId` to machine tokens, so the location of an
    /// SDD package is decided in ONE place and this derivation cannot drift away from the binding the
    /// receipt was validated against (#485 is what two copies of one rule costs). The single guard added
    /// on top is `pathSafeWorkId`, which closes a traversal shape `tokens` alone admits.
    ///
    /// It is deliberately NOT a `TouchSet` token list: these are directory names, and whether a changed
    /// file lies under one is `TouchSet.covers`' question to answer, not a second containment rule here.
    /// THE MATCH HEAD REPRODUCES `validateSddBinding`'s OWN GUARD, and that is required rather than
    /// redundant: `validateSddBinding` answers `[]` — no errors — for a receipt it never examined, because
    /// its `| _ -> []` arm swallows a missing `specHome` or a blank `sddWorkId` along with every
    /// lightweight receipt. Asking "did it report errors?" without first establishing that it HAD
    /// something to check reads an unexamined receipt as a validated one. (Measured: the first cut of this
    /// function omitted `Some _` for `SpecHome` and happily exempted `work/<id>`/`readiness/<id>` for a
    /// receipt carrying no `specHome` at all.)
    let mandatorySddPaths (receipt: Receipt) : string list =
        match receipt.Route, receipt.SddWorkId, receipt.SpecHome with
        | Some SddRequired, Some workId, Some _ when
            not (System.String.IsNullOrWhiteSpace workId)
            && pathSafeWorkId.IsMatch workId
            && List.isEmpty (validateSddBinding receipt)
            ->
            [ $"work/%s{workId}"; $"readiness/%s{workId}" ]
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
