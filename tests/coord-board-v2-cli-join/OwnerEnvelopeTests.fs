module FS.GG.Coord.GitHub.Tests.OwnerEnvelopeTests

open System
open System.IO
open System.Text.Json
open Xunit
open FS.GG.Coord
open FS.GG.Coord.OwnerContinuation
open FS.GG.Coord.GitHub
open FS.GG.Coord.GitHub.Transport
open FS.GG.Coord.GitHub.V2ProjectionSource

let private policyBytes () =
    use stream = typeof<Decision>.Assembly.GetManifestResourceStream("FS.GG.Coord.Core.RootIdentityPolicy.json")
    use reader = new StreamReader(stream)
    reader.ReadToEnd()

let private fixture () =
    let native = ProgrammeFixture.transport ProgrammeFixture.unchanged
    // Frozen synthetic native-byte cohort, not a scored historical throughput comparison.
    let report = inspectFixed native (ProgrammeFixture.binding ()) None
    let report = {report with NativeIntegrator = {report.NativeIntegrator with ObservedAt = DateTimeOffset.Parse "2026-11-03T14:00:00Z"}}
    let context = ProgrammeFixture.context report
    let identity = continuationIdentity context.Slices.Head
    let now = context.ObservedAt
    let fact predicate value support =
        { Id = predicateName predicate; Subject = identity; Predicate = predicate; Value = Known value; Support = support
          ObservedAt = now; ValidUntil = now.AddMinutes 1.0; Evidence = ["exact-source-method-and-evidence"]; Supersedes = [] }
    let owner = AgentAttestation {Supplier = identity.Owner; Role = "owner"; ActualModel = None; ActualEffort = None; Method = "exact source verification"; Contributors = [identity.Owner]; ContributorsComplete = true; Limitations = ["fixture"]}
    let reviewer = AgentAttestation {Supplier = "independent-reviewer"; Role = "reviewer"; ActualModel = None; ActualEffort = None; Method = "independent exact source review"; Contributors = [identity.Owner]; ContributorsComplete = true; Limitations = ["fixture"]}
    let facts = [fact SourceReady true owner; fact VerificationPassed true owner; fact IndependentReview true reviewer; fact Isolated true owner; fact ResourceShare true owner]
    let rootPolicy = RootIdentityPolicy.policy |> Result.defaultWith failwith
    let envelopeDocument =
        {| schema = "fsgg.coord.owner-envelope/1"; policy = "adr0096-finite-sequence"; rootPolicySha256 = rootPolicy.Sha256
           subject = identity.Subject; owner = identity.Owner; sliceId = identity.Slice; attempt = identity.Attempt; revision = identity.Revision; scopeDigest = identity.ScopeDigest
           head = context.Slices.Head.Head.Value; issuedAt = now.AddMinutes(-1.0); expiresAt = now.AddMinutes 10.0
           principalId = "viewer-id"; invocation = context.Invocation; immutableInputsSha256 = continuationEnvelopeInputDigest report context context.Slices.Head
           writeDomains = context.Slices.Head.Paths |> List.map (fun path -> context.Slices.Head.Repository + ":" + path)
           original = "original/cursor-42"; runId = 42L; runAttempt = 2; steps = ["observe-ci";"merge-pr"] |}
    let authority = continuationEnvelopeIdentity report context.Slices.Head
    let envelopeDocument = {| envelopeDocument with revision = authority.Revision; scopeDigest = authority.ScopeDigest |}
    let body = "<!-- fsgg:owner-continuation-envelope/v1 -->\n" + JsonSerializer.Serialize envelopeDocument
    let reference = { NodeId = "IC_native_root"; UpdatedAt = now.ToString("O"); BodySha256 = digest body }
    let request = { Envelope = Some reference; Subject = identity; Action = Next; Original = Some "original/cursor-42"; Facts = facts }
    report, context, now, request, body, rootPolicy

let private reader (native: IGitHubTransport) body author editor policy runStatus conclusion attempt =
    { new IGitHubTransport with
        member _.Send request =
            if request.Subject = "owner-continuation-native-envelope" then
                let issue = {| number = 4730; repository = {| nameWithOwner = "FS-GG/.github" |} |}
                let response = {| data = {| viewer = {| id = "viewer-id" |}; subject = {| id = "I_outcome"; parent = issue |}; repository = {| nameWithOwner = "FS-GG/.github"; policy = {| text = policy |} |};
                                          node = {| id = "IC_native_root"; body = body; updatedAt = (DateTimeOffset.Parse "2026-11-03T14:00:00Z").ToString("O")
                                                    lastEditedAt = (None: string option); author = {| login = author |}; editor = editor; issue = issue |} |} |}
                Ok { Status = 200; Body = JsonSerializer.Serialize response; Headers = Map.empty; ETag = None; NextLink = None }
            elif request.Subject = "owner-continuation-original-readback" then
                Ok { Status = 200; Body = JsonSerializer.Serialize {| id = 42L; run_attempt = attempt; head_sha = ProgrammeFixture.head; repository = {| full_name = "FS-GG/.github" |}; status = runStatus; conclusion = conclusion |}; Headers = Map.empty; ETag = None; NextLink = None }
            else native.Send request
        member _.SendMutation _ = failwith "pure advice must not send a mutation"
        member _.RetryMutation _ = failwith "pure advice must not replay" }

let private collect report context now request body author policy status conclusion attempt =
    // Fixture time is supplied explicitly; native observations cannot read the wall clock.
    let reference = request.Envelope.Value
    let baseReader = reader (ProgrammeFixture.transport ProgrammeFixture.unchanged) body author (None: string option) policy status conclusion attempt
    let joined =
        { new IGitHubTransport with
            member _.Send r =
                baseReader.Send r |> Result.map (fun response ->
                    if r.Subject = "owner-continuation-native-envelope" then
                        let node = System.Text.Json.Nodes.JsonNode.Parse response.Body
                        node.["data"].["node"].["updatedAt"] <- JsonSerializer.SerializeToNode reference.UpdatedAt
                        { response with Body = node.ToJsonString() }
                    else response)
            member _.SendMutation _ = failwith "no mutation"
            member _.RetryMutation _ = failwith "no replay" }
    OwnerContinuationReads.collect joined report context now request

[<Fact>]
let ``shared embedded Root policy preserves canonical identities without admin fallback`` () =
    Assert.True(RootIdentityPolicy.isRoot "ehotwagner" |> Result.defaultValue false)
    Assert.False(RootIdentityPolicy.isRoot "repository-admin" |> Result.defaultValue true)
    Assert.Equal(digest (policyBytes ()), RootIdentityPolicy.digest |> Result.defaultWith failwith)

[<Fact>]
let ``native successful original advances covered sequence through registered existing route`` () =
    let report, context, now, request, body, policy = fixture ()
    let facts = collect report context now request body (Set.minElement policy.RootLogins) (policyBytes ()) "completed" "success" 2
    Assert.Equal(Known true, (facts |> List.find (fun f -> f.Predicate = EnvelopeCurrent)).Value)
    Assert.Equal(Known true, (facts |> List.find (fun f -> f.Predicate = OriginalSucceeded)).Value)
    let decision = recommendOwnerContinuationsWithFacts (Map.ofList [request.Subject.Slice, facts]) (Map.ofList [request.Subject.Slice, Ok(RoutineMerge(String.replicate 64 "a"))]) report context now [request] |> List.exactlyOne
    Assert.Equal(ContinueOwner Deliver, decision.Recommendation)
    Assert.True(decision.GrantReference.IsSome)
    Assert.Equal("native-delivery-readback", decision.NextEvent)

[<Theory>]
[<InlineData("in_progress", "pending", 2)>]
[<InlineData("completed", "success", 3)>]
let ``pending or wrong-attempt original is observed without send or replacement`` status conclusion attempt =
    let report, context, now, request, body, policy = fixture ()
    let facts = collect report context now request body (Set.minElement policy.RootLogins) (policyBytes ()) status conclusion attempt
    let decision = recommendOwnerContinuationsWithFacts (Map.ofList [request.Subject.Slice, facts]) Map.empty report context now [request] |> List.exactlyOne
    Assert.Equal(ObserveOriginal "original/cursor-42", decision.Recommendation)

[<Fact>]
let ``failed terminal original returns owner repair instead of repeating finished observation`` () =
    let report, context, now, request, body, policy = fixture ()
    let facts = collect report context now request body (Set.minElement policy.RootLogins) (policyBytes ()) "completed" "failure" 2
    let decision = recommendOwnerContinuationsWithFacts (Map.ofList [request.Subject.Slice, facts]) Map.empty report context now [request] |> List.exactlyOne
    Assert.Equal(RequestJudgment "original-failed-owner-repair", decision.Recommendation)

[<Theory>]
[<InlineData("admin")>]
[<InlineData("policy")>]
[<InlineData("body")>]
[<InlineData("expired")>]
let ``untrusted issuer policy drift changed envelope and expiry cannot advance`` change =
    let report, context, now, request, body, policy = fixture ()
    let author = if change = "admin" then "repository-admin" else Set.minElement policy.RootLogins
    let rawPolicy = if change = "policy" then "{}" else policyBytes ()
    let nativeBody = if change = "body" then body + " " else body
    let now = if change = "expired" then now.AddMinutes 11.0 else now
    let facts = collect report context now request nativeBody author rawPolicy "completed" "success" 2
    let decision = recommendOwnerContinuationsWithFacts (Map.ofList [request.Subject.Slice, facts]) (Map.ofList [request.Subject.Slice, Ok(RoutineMerge(String.replicate 64 "a"))]) report context now [request] |> List.exactlyOne
    Assert.NotEqual<Recommendation>(ContinueOwner Deliver, decision.Recommendation)

[<Fact>]
let ``inferred pipeline makes progress and never asks for dispatch or Root grant for owned preparation`` () =
    let report, context, now, request, _, _ = fixture ()
    let baseline = { request with Envelope = None; Original = None; Facts = request.Facts @ [{ request.Facts.Head with Id = "effect-clear"; Predicate = EffectClear }] }
    let cases: (Predicate * NextAction) list = [SourceReady, OwnerContinuation.Implement; VerificationPassed, VerifyIsolated; IndependentReview, Review]
    for predicate, expected in cases do
        let facts = baseline.Facts |> List.map (fun f -> if f.Predicate = predicate then { f with Value = Known false } else f)
        let decision = recommendOwnerContinuations report {context with ImplementationSlots = None; ReviewSlots = None; Reservations = None; ReservationObservation = None; CapacityObservation = None} now [{baseline with Facts = facts}] |> List.exactlyOne
        Assert.Equal(ContinueOwner expected, decision.Recommendation)

[<Fact>]
let ``fresh positives cannot hide stale conflicting evidence`` () =
    let report, context, now, request, _, _ = fixture ()
    let stale = { request.Facts.Head with Id = "stale-source-conflict"; Value = Known false; ObservedAt = now.AddMinutes(-6.0) }
    let request = {request with Envelope = None; Original = None; Facts = stale :: request.Facts @ [{request.Facts.Head with Id = "effect-clear"; Predicate = EffectClear}]}
    let decision = recommendOwnerContinuations report context now [request] |> List.exactlyOne
    Assert.Equal(RequestJudgment "supply-current-owner-stage-state", decision.Recommendation)

[<Fact>]
let ``metadata clocks refresh without a circular grant while body head and hierarchy revoke`` () =
    let report, context, _, _, _, _ = fixture ()
    let slice = context.Slices.Head
    let initial = continuationEnvelopeInputDigest report context slice
    let clockOnly = {slice with Join = {slice.Join with UpdatedAt = "a-new-native-comment-clock"}; PullRequestUpdatedAt = Some "new-native-check-clock"}
    Assert.Equal(initial, continuationEnvelopeInputDigest report {context with ObservedAt = context.ObservedAt.AddSeconds 1.0; InputSha256 = String.replicate 64 "b"} clockOnly)
    for drift in [ {slice with Join = {slice.Join with BodySha256 = String.replicate 64 "b"}}; {slice with Head = Some(String.replicate 40 "b")}; {slice with Join = {slice.Join with HierarchySha256 = Some(String.replicate 64 "b")}} ] do
        Assert.NotEqual<string>(initial, continuationEnvelopeInputDigest report context drift)

[<Fact>]
let ``reviewer self contributor and incomplete contribution claims cannot complete review`` () =
    let report, context, now, request, _, _ = fixture ()
    for supplier, contributors, complete in [request.Subject.Owner, [request.Subject.Owner], true; "reviewer", ["reviewer";request.Subject.Owner], true; "reviewer", [request.Subject.Owner], false] do
        let facts = request.Facts |> List.map (fun f ->
            if f.Predicate = IndependentReview then
                {f with Support = AgentAttestation { Supplier = supplier; Role = "reviewer"; ActualModel = None; ActualEffort = None; Method = "review"; Contributors = contributors; ContributorsComplete = complete; Limitations = ["fixture"] }}
            else f)
        let r = {request with Envelope = None; Original = None; Action = Deliver; Facts = facts @ [{facts.Head with Id = "effect-clear"; Predicate = EffectClear}]}
        let decision = recommendOwnerContinuationsWithPreflight (Map.ofList [r.Subject.Slice, Ok(RoutineMerge(String.replicate 64 "a"))]) report context now [r] |> List.exactlyOne
        Assert.Equal(Await "independent-source-review", decision.Recommendation)

[<Fact>]
let ``expired original holds and same owner near matches survive while exact self and unrelated scope continue`` () =
    let report, context, now, request, _, _ = fixture ()
    let slice = context.Slices.Head
    let hold: Reservation = {SliceId = slice.Id; Attempt = slice.Attempt; Operation = slice.Operation; Owner = slice.Owner; Repository = slice.Repository; Paths = slice.Paths; PullRequest = slice.PullRequest; Head = slice.Head; CustodyReference = "original/held"}
    let r = {request with Envelope = None; Original = None; Action = VerifyIsolated; Facts = request.Facts @ [{request.Facts.Head with Id = "effect-clear"; Predicate = EffectClear}]}
    for held in [{hold with Attempt = "other-attempt"}; {hold with Operation = V2ProjectionSource.Implement}; {hold with Head = Some(String.replicate 40 "b")}] do
        let expired = {context with Reservations = Some [held]; ReservationObservation = context.ReservationObservation |> Option.map (fun observation -> {observation with ValidUntil = now.AddMinutes(-1.0)})}
        let decision = recommendOwnerContinuations report expired now [r] |> List.exactlyOne
        Assert.Equal(ObserveOriginal "original/held", decision.Recommendation)
    for held in [hold; {hold with Repository = "FS-GG/Other"; Attempt = "other"}] do
        let decision = recommendOwnerContinuations report {context with Reservations = Some [held]} now [r] |> List.exactlyOne
        Assert.Equal(ContinueOwner VerifyIsolated, decision.Recommendation)

[<Fact>]
let ``merged source awaits whole delivery acceptance instead of merging twice or closing outcome`` () =
    let report, context, now, request, _, _ = fixture ()
    let report = {report with NativeIntegrator = {report.NativeIntegrator with PullRequests = report.NativeIntegrator.PullRequests |> List.map (fun p -> {p with State = "MERGED"})}}
    let r = {request with Envelope = None; Original = None; Facts = request.Facts @ [{request.Facts.Head with Id = "effect-clear"; Predicate = EffectClear}]}
    let decision = recommendOwnerContinuations report context now [r] |> List.exactlyOne
    Assert.Equal(Await "delivery-acceptance-readback", decision.Recommendation)

[<Fact>]
let ``explicit same issuer supersession advances while future evidence cannot leak across replay cutoff`` () =
    let report, context, now, request, _, _ = fixture ()
    let old = request.Facts |> List.find (fun f -> f.Predicate = VerificationPassed) |> fun f -> {f with Id = "verification-before-cutoff"; Value = Known false}
    let next = {old with Id = "verification-after-cutoff"; ObservedAt = now.AddSeconds 20.0; ValidUntil = now.AddMinutes 1.0; Value = Known true; Supersedes = [old.Id]}
    let other = request.Facts |> List.filter (fun f -> f.Predicate <> VerificationPassed)
    let r = {request with Envelope = None; Original = None; Facts = other @ [old;next;{old with Id = "effect-clear"; Predicate = EffectClear; Value = Known true}]}
    let before = recommendOwnerContinuations report context now [r] |> List.exactlyOne
    Assert.NotEqual<Recommendation>(ContinueOwner Deliver, before.Recommendation)
    let after = recommendOwnerContinuationsWithPreflight (Map.ofList [r.Subject.Slice, Ok(RoutineMerge(String.replicate 64 "a"))]) report context (now.AddSeconds 20.0) [r] |> List.exactlyOne
    Assert.Equal(ContinueOwner Deliver, after.Recommendation)
    Assert.Contains(old.Id, after.DeterminingFacts)
    Assert.Contains(next.Id, after.DeterminingFacts)

[<Fact>]
let ``supersession cannot cross issuer scope predicate or form cycles`` () =
    let report, context, now, request, _, _ = fixture ()
    let old = request.Facts.Head
    let second = {old with Id = "superseder"; Supersedes = [old.Id]}
    for changed in [{second with Support = Reported "summary"}; {second with Predicate = VerificationPassed}; {second with Subject = {second.Subject with Attempt = "other"}}; {second with Supersedes = ["absent"]}] do
        let r = {request with Envelope = None; Original = None; Facts = request.Facts @ [changed]}
        let decision = recommendOwnerContinuations report context now [r] |> List.exactlyOne
        Assert.Equal(RequestJudgment "invalid-fact-supersession", decision.Recommendation)
    let cycle = {request with Envelope = None; Original = None; Facts = [{old with Supersedes = [second.Id]}; second]}
    Assert.Equal(RequestJudgment "invalid-fact-supersession", (recommendOwnerContinuations report context now [cycle] |> List.exactlyOne).Recommendation)

[<Fact>]
let ``resolved original cannot hide a different overlapping uncertain original`` () =
    let report, context, now, request, body, policy = fixture ()
    let resolved = collect report context now request body (Set.minElement policy.RootLogins) (policyBytes ()) "completed" "success" 2
    let slice = context.Slices.Head
    let hold: Reservation = {SliceId = slice.Id; Attempt = "different-attempt"; Operation = slice.Operation; Owner = slice.Owner; Repository = slice.Repository; Paths = slice.Paths; PullRequest = slice.PullRequest; Head = slice.Head; CustodyReference = "original/other-unknown"}
    let decision = recommendOwnerContinuationsWithFacts (Map.ofList [request.Subject.Slice, resolved]) (Map.ofList [request.Subject.Slice, Ok(RoutineMerge(String.replicate 64 "a"))]) report {context with Reservations = Some [hold]} now [request] |> List.exactlyOne
    Assert.Equal(ObserveOriginal "original/other-unknown", decision.Recommendation)
    Assert.True(decision.GrantReference.IsNone)
    let failed = resolved |> List.map (fun f -> if f.Predicate = OriginalSucceeded then {f with Value = Known false} elif f.Predicate = OriginalFailed then {f with Value = Known true} else f)
    let decision = recommendOwnerContinuationsWithFacts (Map.ofList [request.Subject.Slice, failed]) Map.empty report {context with Reservations = Some [hold]} now [request] |> List.exactlyOne
    Assert.Equal(ObserveOriginal "original/other-unknown", decision.Recommendation)

[<Fact>]
let ``route support advice explicitly retains unobserved executor authority refreshes`` () =
    let report, context, now, request, body, policy = fixture ()
    let resolved = collect report context now request body (Set.minElement policy.RootLogins) (policyBytes ()) "completed" "success" 2
    let decision = recommendOwnerContinuationsWithFacts (Map.ofList [request.Subject.Slice, resolved]) (Map.ofList [request.Subject.Slice, Ok(RoutineMerge(String.replicate 64 "a"))]) report context now [request] |> List.exactlyOne
    Assert.Equal(ContinueOwner Deliver, decision.Recommendation)
    Assert.Contains("executor-current-r1-linkage", decision.RequiredRefresh)
    Assert.Contains("executor-delivery-marker", decision.RequiredRefresh)

[<Theory>]
[<InlineData(false)>]
[<InlineData(true)>]
let ``missing or owner reported absent review initiates review without reviewer approval of absence`` ownerReportsAbsence =
    let report, context, now, request, _, _ = fixture ()
    let ownerFact = request.Facts.Head
    let facts = request.Facts |> List.filter (fun f -> f.Predicate <> IndependentReview)
    let absent = if ownerReportsAbsence then [{ownerFact with Id = "review-not-obtained"; Predicate = IndependentReview; Value = Known false}] else []
    let r = {request with Envelope = None; Original = None; Facts = facts @ absent @ [{ownerFact with Id = "effect-clear"; Predicate = EffectClear}]}
    Assert.Equal(ContinueOwner Review, (recommendOwnerContinuations report context now [r] |> List.exactlyOne).Recommendation)

[<Fact>]
let ``native changes required returns same owner to implementation and native CI failure awaits repair`` () =
    let report, context, now, request, _, _ = fixture ()
    let r = {request with Envelope = None; Original = None; Facts = request.Facts @ [{request.Facts.Head with Id = "effect-clear"; Predicate = EffectClear}]}
    let changes = {report with NativeIntegrator = {report.NativeIntegrator with PullRequests = report.NativeIntegrator.PullRequests |> List.map (fun p -> {p with ReviewDecision = Some "CHANGES_REQUESTED"})}}
    Assert.Equal(ContinueOwner OwnerContinuation.Implement, (recommendOwnerContinuations changes context now [r] |> List.exactlyOne).Recommendation)
    let failed = {report with NativeIntegrator = {report.NativeIntegrator with PullRequests = report.NativeIntegrator.PullRequests |> List.map (fun p -> {p with Checks = ["ci:COMPLETED:FAILURE"]})}}
    Assert.Equal(Await "native-check-qualification", (recommendOwnerContinuations failed context now [r] |> List.exactlyOne).Recommendation)

[<Fact>]
let ``explicit reviewer repair evidence cannot be erased by absent native changes request`` () =
    let report, context, now, request, _, _ = fixture ()
    let review = request.Facts |> List.find (fun f -> f.Predicate = IndependentReview)
    let repair = {review with Id = "independent-repair-finding"; Predicate = ChangesRequired}
    let r = {request with Envelope = None; Original = None; Facts = request.Facts @ [repair;{request.Facts.Head with Id = "effect-clear"; Predicate = EffectClear}]}
    Assert.Equal(ContinueOwner OwnerContinuation.Implement, (recommendOwnerContinuations report context now [r] |> List.exactlyOne).Recommendation)
    Assert.Equal(Await "owner-source-repair", (recommendOwnerContinuations report context now [{r with Action = Deliver}] |> List.exactlyOne).Recommendation)

[<Fact>]
let ``owning plan and dependency comment clocks cannot create an envelope digest fixed point`` () =
    let original, context, now, request, _, _ = fixture ()
    let item = original.Items.Head
    let source = item.Evidence.Native.Value
    let plan = {source with NodeId = "I_plan"; Url = "https://github.com/FS-GG/.github/issues/3008"; UpdatedAt = "2026-11-03T13:59:00Z"; BodySha256 = String.replicate 64 "e"}
    let dependency = {plan with NodeId = "I_dependency"; Url = "https://github.com/FS-GG/.github/issues/42"}
    let report = {original with Items = [{item with Evidence = {item.Evidence with PlanObservation = Some plan; DependencyObservations = [dependency]; DependencyReadComplete = true}}]}
    let slice = context.Slices.Head
    let expected = continuationEnvelopeInputDigest report context slice
    let afterPlan = {plan with UpdatedAt = now.ToString("O")}
    let afterDependency = {dependency with UpdatedAt = now.ToString("O")}
    let changed = {report with Items = [{report.Items.Head with SourceRevision = Some(slice.Join.SourceRevision + "-current-native-plan-clock"); Evidence = {report.Items.Head.Evidence with PlanObservation = Some afterPlan; DependencyObservations = [afterDependency]}}]}
    let refreshed = {slice with Join = {slice.Join with SourceRevision = changed.Items.Head.SourceRevision.Value}}
    Assert.Equal(expected, continuationEnvelopeInputDigest changed {context with Slices = [refreshed]} refreshed)
    let bodyChanged = {changed with Items = [{changed.Items.Head with Evidence = {changed.Items.Head.Evidence with PlanObservation = Some {afterPlan with BodySha256 = String.replicate 64 "b"}}}]}
    Assert.NotEqual<string>(expected, continuationEnvelopeInputDigest bodyChanged context refreshed)
    let edgeChanged = {changed with Items = [{changed.Items.Head with Evidence = {changed.Items.Head.Evidence with DependencyObservations = []}}]}
    Assert.NotEqual<string>(expected, continuationEnvelopeInputDigest edgeChanged context refreshed)

let private mergedCollector closeIssue (request: Transport.Request) (response: Errors.IoResult<Transport.Response>) =
    response |> Result.map (fun response ->
        match request.Body with
        | Query(document, _) when document.Contains("pullRequests(first:") ->
            let root = System.Text.Json.Nodes.JsonNode.Parse response.Body
            let connection = root.["data"].["repository"].["pullRequests"]
            connection.["nodes"].AsArray().Clear()
            connection.["totalCount"] <- JsonSerializer.SerializeToNode 0
            {response with Body = root.ToJsonString()}
        | Query _ when request.Subject.EndsWith(" Current deliverable native join", StringComparison.Ordinal) ->
            let root = System.Text.Json.Nodes.JsonNode.Parse response.Body
            root.["data"].["repository"].["pullRequest"].["state"] <- JsonSerializer.SerializeToNode "MERGED"
            {response with Body = root.ToJsonString()}
        | Query _ when closeIssue && request.Subject.EndsWith(" native observation", StringComparison.Ordinal) ->
            let root = System.Text.Json.Nodes.JsonNode.Parse response.Body
            root.["data"].["repository"].["issue"].["state"] <- JsonSerializer.SerializeToNode "CLOSED"
            {response with Body = root.ToJsonString()}
        | _ -> response)

[<Theory>]
[<InlineData(false)>]
[<InlineData(true)>]
let ``actual native collector merged only identity readback continues acceptance even when issue closed`` closed =
    let transport = ProgrammeFixture.transport (mergedCollector closed)
    let report = inspectFixed transport (ProgrammeFixture.binding ()) None
    let context = ProgrammeFixture.context report
    let merged = report.NativeIntegrator.PullRequests |> List.find (fun p -> p.State = "MERGED")
    Assert.False(merged.ScopeCurrent)
    Assert.Empty merged.Checks
    let slice = {context.Slices.Head with PullRequest = Some merged.Url; PullRequestId = Some merged.Id; PullRequestUpdatedAt = Some merged.UpdatedAt; Head = Some merged.Head}
    let context = {context with Slices = [slice]}
    let _, _, _, original, _, _ = fixture ()
    let identity = continuationIdentity slice
    let r = {original with Envelope = None; Original = None; Subject = identity; Facts = original.Facts |> List.map (fun fact -> {fact with Subject = identity; ObservedAt = context.ObservedAt; ValidUntil = context.ValidUntil})}
    let advice = recommendOwnerContinuations report context context.ObservedAt [r] |> List.exactlyOne
    Assert.Equal(Await "delivery-acceptance-readback", advice.Recommendation)
    Assert.Empty transport.Mutations

[<Theory>]
[<InlineData("SKIPPED")>]
[<InlineData("NEUTRAL")>]
let ``accepted native rollup conclusions do not veto the qualified delivery route`` conclusion =
    let report, context, now, request, _, _ = fixture ()
    let report = {report with NativeIntegrator = {report.NativeIntegrator with PullRequests = report.NativeIntegrator.PullRequests |> List.map (fun p -> {p with Checks = ["required:COMPLETED:SUCCESS"; "conditional:COMPLETED:" + conclusion]})}}
    let r = {request with Envelope = None; Original = None; Facts = request.Facts @ [{request.Facts.Head with Id = "effect-clear"; Predicate = EffectClear}]}
    let advice = recommendOwnerContinuationsWithPreflight (Map.ofList [r.Subject.Slice, Ok(RoutineMerge(String.replicate 64 "a"))]) report context now [r] |> List.exactlyOne
    Assert.Equal(ContinueOwner Deliver, advice.Recommendation)
    Assert.Contains("executor-native-required-check-population", advice.RequiredRefresh)

[<Fact>]
let ``owned no PR progresses through the actual registered draft route before CI and review`` () =
    let transport = ProgrammeFixture.transport ProgrammeFixture.newAssignment
    let report = inspectFixed transport (ProgrammeFixture.binding ()) None
    let context = ProgrammeFixture.context report
    let identity = continuationIdentity context.Slices.Head
    let _, _, _, original, _, _ = fixture ()
    let facts = original.Facts |> List.filter (fun fact -> fact.Predicate <> IndependentReview) |> List.map (fun fact -> {fact with Subject = identity; ObservedAt = context.ObservedAt; ValidUntil = context.ValidUntil})
    let r = {original with Envelope = None; Original = None; Subject = identity; Facts = facts @ [{facts.Head with Id = "effect-clear"; Predicate = EffectClear}]}
    let inputs: V2Preflight.RoutineInputs =
        {DefaultBranch = "main"; Candidate = V2Preflight.Known {BaseRef = "main"; BaseSha = None; HeadRef = "routine/owned-source"; HeadSha = ProgrammeFixture.head; State = None}
         LiveBaseSha = V2Preflight.Known ProgrammeFixture.head; OpenPullRequests = V2Preflight.Known 0
         RoutineGate = V2Preflight.Known V2Preflight.Admitted; ClosingGate = V2Preflight.Known V2Preflight.NoClosing}
    let registered = V2Preflight.runRoutine V2Preflight.OpenPullRequest inputs
    Assert.Equal(V2Preflight.AllSupported, registered.Overall)
    let proof = RoutineDraft(digest (V2Preflight.toJson registered), ProgrammeFixture.head)
    let advice = recommendOwnerContinuationsWithPreflight (Map.ofList [identity.Slice, Ok proof]) report context context.ObservedAt [r] |> List.exactlyOne
    Assert.Equal(ContinueOwner OpenDraft, advice.Recommendation)
    Assert.Equal(Some ProgrammeFixture.head, advice.ArtifactRevision)
    Assert.Contains("executor-draft-true-at-create", advice.RequiredRefresh)
    Assert.Empty transport.Mutations

[<Fact>]
let ``changed owning promise needs scoped judgment while a timestamp alone needs only refresh`` () =
    let report, context, now, request, _, _ = fixture ()
    let r = {request with Envelope = None; Original = None; Action = VerifyIsolated; Facts = request.Facts @ [{request.Facts.Head with Id = "effect-clear"; Predicate = EffectClear}]}
    let body = {report with Items = report.Items |> List.map (fun item -> {item with Evidence = {item.Evidence with Native = item.Evidence.Native |> Option.map (fun native -> {native with BodySha256 = String.replicate 64 "b"})}})}
    Assert.Equal(RequestJudgment "reconcile-changed-owner-scope", (recommendOwnerContinuations body context now [r] |> List.exactlyOne).Recommendation)
    let clock = {report with Items = report.Items |> List.map (fun item -> {item with Evidence = {item.Evidence with Native = item.Evidence.Native |> Option.map (fun native -> {native with UpdatedAt = "2026-11-03T14:01:00Z"})}})}
    Assert.Equal(Await "native-scope-refresh", (recommendOwnerContinuations clock context now [r] |> List.exactlyOne).Recommendation)
