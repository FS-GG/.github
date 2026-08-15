namespace FS.GG.Coord

module StructuredDecision =
    open System
    open System.Globalization
    open System.Security.Cryptography
    open System.Text

    [<Literal>]
    let RouteSchema = "fsgg.coord.route-decision/v2"

    [<Literal>]
    let ReviewSchema = "fsgg.coord.review-decision/v2"

    [<Literal>]
    let PolicyVersion = "structured-decisions/1"

    type RouteRecord =
        { Schema: string; Subject: string; Revision: int; PreviousDigest: string option
          Scope: string list; Dependencies: string list; TouchSet: string list; PolicyVersion: string
          Route: DeliveryRoute.Route option; Agent: string; Timestamp: string; ReasonCodes: string list
          Rationale: string; SddWorkId: string option; SpecHome: string option; RequiredGates: string list
          Digest: string }

    type ReviewKind = Initial | Confirmation | Escalation | RepairPhase | Acceptance
    type ReviewVerdict = Pass | ChangesRequired | Accepted

    /// See `StructuredDecision.fsi` for why the grant lives in the record rather than beside it, and why
    /// `GrantUrl` is required to be present but never resolved.
    type SuccessionGrant =
        { OriginalCritic: string
          GrantedBy: string
          GrantUrl: string }

    type ReviewRecord =
        { Schema: string; Subject: string; Revision: int; PreviousDigest: string option
          HeadSha: string; Critic: string; Verdict: ReviewVerdict; AcceptedExceptions: string list
          RouteApplicability: string; RouteEvidence: string list; PolicyVersion: string
          Kind: ReviewKind; Round: int; InitialReview: string option
          PrecedingReview: string option; DiffAuditRequired: bool; DiffAuditReceipts: string list
          Succession: SuccessionGrant option
          Timestamp: string; Digest: string }

    let private frame (value: string) = $"%d{Encoding.UTF8.GetByteCount value}:%s{value}"
    let private scalar value = value |> Option.defaultValue "" |> frame
    let private strings values = values |> List.map frame |> String.concat ""
    let private digest fields =
        fields
        |> String.concat "|"
        |> Encoding.UTF8.GetBytes
        |> SHA256.HashData
        |> Convert.ToHexString
        |> _.ToLowerInvariant()

    let private routeName = function
        | Some DeliveryRoute.Lightweight -> "lightweight"
        | Some DeliveryRoute.SddRequired -> "sdd-required"
        | None -> ""

    let routeDigest (record: RouteRecord) =
        digest
            [ frame record.Schema; frame record.Subject; string record.Revision; scalar record.PreviousDigest
              strings record.Scope; strings record.Dependencies; strings record.TouchSet; frame record.PolicyVersion
              frame (routeName record.Route); frame record.Agent; frame record.Timestamp; strings record.ReasonCodes
              frame record.Rationale; scalar record.SddWorkId; scalar record.SpecHome; strings record.RequiredGates ]

    let private kindName = function
        | Initial -> "initial"
        | Confirmation -> "confirmation"
        | Escalation -> "escalation"
        | RepairPhase -> "repair-phase"
        | Acceptance -> "acceptance"
    let private verdictName = function Pass -> "pass" | ChangesRequired -> "changes-required" | Accepted -> "accepted"

    /// APPENDED, and only when a grant is present (.github#2662). `digest` joins its fields with `|`, so
    /// contributing NOTHING for an absent grant leaves the joined string — and therefore the digest —
    /// byte-identical to what every record already written to a pull request recorded. Contributing the
    /// three framed fields when present is what makes the grant tamper-evident, and is exactly what makes
    /// an engine built before this field fail closed on a succession record: it ignores the unknown
    /// `succession` key, recomputes over the eighteen fields it knows, and reports a digest mismatch
    /// rather than accepting a record whose grant it cannot see.
    let private successionFields =
        function
        | None -> []
        | Some grant -> [ frame grant.OriginalCritic; frame grant.GrantedBy; frame grant.GrantUrl ]

    let reviewDigest (record: ReviewRecord) =
        digest
            ([ frame record.Schema; frame record.Subject; string record.Revision; scalar record.PreviousDigest
               frame record.HeadSha; frame record.Critic; frame (verdictName record.Verdict)
               strings record.AcceptedExceptions; frame record.RouteApplicability; strings record.RouteEvidence
               frame record.PolicyVersion; frame (kindName record.Kind)
               string record.Round; scalar record.InitialReview; scalar record.PrecedingReview
               string record.DiffAuditRequired; strings record.DiffAuditReceipts; frame record.Timestamp ]
             @ successionFields record.Succession)

    let private blank field value = if String.IsNullOrWhiteSpace value then [ $"%s{field} is required" ] else []
    let private values field items =
        if List.isEmpty items || items |> List.exists String.IsNullOrWhiteSpace then
            [ $"%s{field} must contain one or more non-empty values" ]
        else []
    let private timestamp (value: string) =
        match DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind) with
        | true, _ -> []
        | _ -> [ "timestamp must be an ISO-8601 instant" ]
    let private sha (value: string) =
        value.Length = 40 && value |> Seq.forall Uri.IsHexDigit

    /// See `StructuredDecision.fsi`. The check is a PREFIX, not a fixed list, because the dispatch
    /// convention (`fsgg-critic-<route>`) is what is actually stable — a route name this module has no
    /// reason to enumerate. Moved here from `Driver.fs`'s private copy by .github#2662, because the
    /// ledger validator needs the same rule and the module that owns the record should own the predicate
    /// about its `critic` field; `Review.fs` keeps its own copy for the reason the signature file states.
    let isGenericCriticIdentity (identity: string) =
        not (String.IsNullOrWhiteSpace identity)
        && identity.Trim().StartsWith("fsgg-critic-", StringComparison.OrdinalIgnoreCase)

    let private chainErrors getRevision getPrevious getDigest calculate records =
        records
        |> List.mapi (fun index record ->
            let expectedRevision = index + 1
            let expectedPrevious = if index = 0 then None else Some(getDigest records[index - 1])
            [ if getRevision record <> expectedRevision then
                  yield $"revision must be contiguous and append-only: expected %d{expectedRevision}"
              if getPrevious record <> expectedPrevious then
                  yield $"revision %d{expectedRevision} previousDigest does not bind the preceding record"
              if getDigest record <> calculate record then
                  yield $"revision %d{expectedRevision} digest does not match its structured inputs" ])
        |> List.concat

    let validateRouteLedger (expectedSubject: string) (records: RouteRecord list) =
        let errors =
            [ if List.isEmpty records then yield "structured route ledger is empty"
              for record in records do
                  if record.Schema <> RouteSchema then yield $"schema must be '%s{RouteSchema}'"
                  if record.Subject <> expectedSubject then yield "subject does not match the routed item"
                  if record.PolicyVersion <> PolicyVersion then yield $"policyVersion must be '%s{PolicyVersion}'"
                  yield! blank "agent" record.Agent
                  yield! blank "timestamp" record.Timestamp
                  yield! timestamp record.Timestamp
                  yield! values "scope" record.Scope
                  yield! values "dependencies" record.Dependencies
                  yield! values "touchSet" record.TouchSet
                  yield! values "reasonCodes" record.ReasonCodes
                  yield! blank "rationale" record.Rationale
                  let receipt : DeliveryRoute.Receipt =
                      { Schema = DeliveryRoute.Schema; Subject = record.Subject; SubjectRevision = record.Digest
                        Route = record.Route; Agent = record.Agent; Timestamp = record.Timestamp
                        ReasonCodes = record.ReasonCodes; Rationale = record.Rationale
                        DeclaredImpacts = [ "structured-scope" ]; ObservedFacts = [ "structured-dependencies" ]
                        SddWorkId = record.SddWorkId; SpecHome = record.SpecHome; RequiredGates = record.RequiredGates }
                  match DeliveryRoute.validate record.Subject record.Digest receipt with
                  | Ok _ -> ()
                  | Error routeErrors -> yield! routeErrors
              yield! chainErrors (fun (record: RouteRecord) -> record.Revision) _.PreviousDigest _.Digest routeDigest records ]
        if List.isEmpty errors then Ok(List.last records) else Error errors

    let validateReviewLedger (expectedSubject: string) (records: ReviewRecord list) =
        let mutable generationCritic: string option = None
        let mutable expectedConfirmationRound = 0
        let errors =
            [ if List.isEmpty records then yield "structured review ledger is empty"
              match records with
              | first :: _ when first.Kind <> Initial -> yield "structured review ledger must begin with an initial record"
              | _ -> ()
              for index, record in records |> List.indexed do
                  let preceding = if index = 0 then None else Some records[index - 1]
                  match record.Kind, preceding with
                  | Initial, None ->
                      generationCritic <- Some record.Critic
                      expectedConfirmationRound <- 0
                  | Initial, Some prior when prior.Kind = Acceptance ->
                      generationCritic <- Some record.Critic
                      expectedConfirmationRound <- 0
                      if prior.HeadSha = record.HeadSha then
                          yield "a new review generation requires head movement after host acceptance"
                  | Initial, Some _ ->
                      yield "a new initial review is allowed only after host acceptance"
                  | (Confirmation | Escalation | RepairPhase | Acceptance), Some prior when prior.Kind = Acceptance ->
                      yield "host acceptance may be followed only by a new initial review generation"
                  | Confirmation, _ ->
                      expectedConfirmationRound <- expectedConfirmationRound + 1
                      if record.Round <> expectedConfirmationRound then
                          yield $"confirmation round must be contiguous within its generation: expected %d{expectedConfirmationRound}"
                  | (Escalation | RepairPhase | Acceptance), _ -> ()
                  | _, _ -> ()

                  // CRITIC CONTINUITY, and the ONE accountable exception to it (.github#2662).
                  //
                  // The no-grant arm is textually and behaviourally what it has always been, including its
                  // message: an identity change nobody granted is refused exactly as before, which is the
                  // property this change must not weaken and the reason the pre-existing differing-critic
                  // test passes unmodified.
                  //
                  // The grant arm exists because `.github#2417` taught the DECISION layer that a chain
                  // whose critic despawned can be handed to a successor, and never taught the LEDGER — so
                  // a granted successor could review and then had no honest shape to record in. It applies
                  // to `Confirmation`, `Escalation` and `RepairPhase` alike: the rule below is keyed on
                  // `Kind <> Initial`, so exempting `Confirmation` alone would leave a successor able to
                  // pass a chain but unable to escalate one into the repair phase, which was measured on a
                  // live chain rather than inferred.
                  //
                  // Admitting REBINDS `generationCritic`, and that is the whole mechanism: every later
                  // record in the generation — including the host's `acceptance` — then binds the
                  // successor through this same unchanged conjunct, and a second grant (a successor can
                  // itself despawn) must name the successor as ITS outgoing critic.
                  let successionErrors =
                      match record.Succession with
                      | None ->
                          [ if record.Kind <> Initial && generationCritic <> Some record.Critic then
                                yield "every record in one review generation must bind the same critic" ]
                      | Some grant ->
                          [ match record.Kind with
                            | Initial | Acceptance ->
                                // An initial binds its own generation critic outright and has nothing to
                                // succeed; an acceptance is the host's record and by then the seat has
                                // already changed hands. Neither is a succession.
                                yield "a critic-succession grant belongs to a confirmation, escalation, or repair-phase record"
                            | Confirmation | Escalation | RepairPhase ->
                                if generationCritic = Some record.Critic then
                                    // Succession is an exception to a rule. A record that does not trip
                                    // that rule has no exception to claim, and a decorative grant would
                                    // assert a provenance no reader could tell from a real one.
                                    yield "a critic-succession grant requires a record that changes the generation's critic"
                                elif generationCritic <> Some grant.OriginalCritic then
                                    yield "a critic-succession grant must name the generation's current critic as its outgoing critic"
                                elif String.IsNullOrWhiteSpace grant.GrantedBy then
                                    yield "a critic-succession grant must name the granting identity"
                                elif String.IsNullOrWhiteSpace grant.GrantUrl then
                                    yield "a critic-succession grant must name the grant's URL"
                                elif isGenericCriticIdentity grant.OriginalCritic
                                     || isGenericCriticIdentity record.Critic
                                     || isGenericCriticIdentity grant.GrantedBy then
                                    // .github#2451 in the successor slot: a bare `fsgg-critic-<route>`
                                    // string is shared by every critic ever dispatched at that route, so
                                    // it witnesses nothing about which instance reviewed.
                                    yield "a critic-succession grant must bind minted, distinguishing identities" ]

                  yield! successionErrors

                  if record.Succession.IsSome && List.isEmpty successionErrors then
                      generationCritic <- Some record.Critic
              for record in records do
                  if record.Schema <> ReviewSchema then yield $"schema must be '%s{ReviewSchema}'"
                  if record.Subject <> expectedSubject then yield "subject does not match the pull request"
                  if record.PolicyVersion <> PolicyVersion then yield $"policyVersion must be '%s{PolicyVersion}'"
                  yield! blank "headSha" record.HeadSha
                  if not (sha record.HeadSha) then yield "headSha must be an exact 40-hex commit SHA"
                  yield! blank "critic" record.Critic
                  if record.RouteApplicability <> "meaningful" && record.RouteApplicability <> "not-meaningful" then
                      yield "routeApplicability must be meaningful or not-meaningful"
                  if record.RouteApplicability = "meaningful" && List.length record.RouteEvidence <> 4 then
                      yield "meaningful route evidence must contain built artifact, command, comparison, and result"
                  if record.RouteApplicability = "not-meaningful" && List.length record.RouteEvidence <> 1 then
                      yield "not-meaningful route evidence must contain exactly one reason"
                  yield! blank "timestamp" record.Timestamp
                  yield! timestamp record.Timestamp
                  if record.Revision < 1 then yield "revision must be positive"
                  match record.Kind, record.Verdict with
                  | Acceptance, Accepted -> ()
                  | Acceptance, _ -> yield "acceptance records must carry verdict accepted"
                  | (Initial | Confirmation), (Pass | ChangesRequired) -> ()
                  | (Escalation | RepairPhase), ChangesRequired -> ()
                  | _ -> yield "review records must carry pass or changes-required"
                  if record.Kind = Initial && record.Round <> 0 then yield "initial review round must be zero"
                  if record.Kind = Confirmation && record.Round < 1 then yield "confirmation round must be positive"
                  if record.Kind = Initial && (record.InitialReview.IsSome || record.PrecedingReview.IsSome) then
                      yield "initial review records cannot carry review back-references"
                  if record.Kind <> Initial && record.InitialReview |> Option.exists String.IsNullOrWhiteSpace then
                      yield "initialReview must be non-empty when present"
                  if record.Kind <> Initial && record.InitialReview.IsNone then
                      yield "non-initial review records must bind the initial review"
                  if record.Kind <> Initial && record.PrecedingReview.IsNone then
                      yield "non-initial review records must bind the preceding review"
                  if record.Kind = Acceptance && not (List.isEmpty record.AcceptedExceptions) then
                      yield "accepted exceptions belong to critic review records, not host acceptance"
                  if record.Kind <> Initial && record.DiffAuditRequired then
                      yield "diffAuditRequired belongs to the initial review record"
                  if record.Kind <> Acceptance && not (List.isEmpty record.DiffAuditReceipts) then
                      yield "diffAuditReceipts belong to the acceptance record"
                  if record.DiffAuditReceipts |> List.exists String.IsNullOrWhiteSpace then
                      yield "diffAuditReceipts must contain only non-empty encoded receipts"
                  if record.AcceptedExceptions |> List.exists String.IsNullOrWhiteSpace then
                      yield "acceptedExceptions must contain only non-empty identifiers"
              yield! chainErrors (fun (record: ReviewRecord) -> record.Revision) _.PreviousDigest _.Digest reviewDigest records ]
        if List.isEmpty errors then Ok records else Error errors

    let toEffectiveRoute (record: RouteRecord) : DeliveryRoute.Receipt =
        { Schema = DeliveryRoute.Schema; Subject = record.Subject; SubjectRevision = record.Digest
          Route = record.Route; Agent = record.Agent; Timestamp = record.Timestamp
          ReasonCodes = record.ReasonCodes; Rationale = record.Rationale
          DeclaredImpacts = [ "structured-scope" ]; ObservedFacts = [ "structured-dependencies" ]
          SddWorkId = record.SddWorkId; SpecHome = record.SpecHome; RequiredGates = record.RequiredGates }
