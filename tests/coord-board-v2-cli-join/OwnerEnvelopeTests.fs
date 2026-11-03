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
           principalId = "viewer-id"; invocation = context.Invocation; immutableInputsSha256 = continuationEnvelopeInputDigest context context.Slices.Head
           writeDomains = context.Slices.Head.Paths |> List.map (fun path -> context.Slices.Head.Repository + ":" + path)
           original = "original/cursor-42"; runId = 42L; runAttempt = 2; steps = ["observe-ci";"merge-pr"] |}
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
    let decision = recommendOwnerContinuationsWithFacts (Map.ofList [request.Subject.Slice, facts]) (Map.ofList [request.Subject.Slice, Ok(String.replicate 64 "a")]) report context now [request] |> List.exactlyOne
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
    let decision = recommendOwnerContinuationsWithFacts (Map.ofList [request.Subject.Slice, facts]) (Map.ofList [request.Subject.Slice, Ok(String.replicate 64 "a")]) report context now [request] |> List.exactlyOne
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
    let _, context, _, _, _, _ = fixture ()
    let slice = context.Slices.Head
    let initial = continuationEnvelopeInputDigest context slice
    let clockOnly = {slice with Join = {slice.Join with UpdatedAt = "a-new-native-comment-clock"}; PullRequestUpdatedAt = Some "new-native-check-clock"}
    Assert.Equal(initial, continuationEnvelopeInputDigest {context with ObservedAt = context.ObservedAt.AddSeconds 1.0; InputSha256 = String.replicate 64 "b"} clockOnly)
    for drift in [ {slice with Join = {slice.Join with BodySha256 = String.replicate 64 "b"}}; {slice with Head = Some(String.replicate 40 "b")}; {slice with Join = {slice.Join with HierarchySha256 = Some(String.replicate 64 "b")}} ] do
        Assert.NotEqual<string>(initial, continuationEnvelopeInputDigest context drift)

[<Fact>]
let ``reviewer self contributor and incomplete contribution claims cannot complete review`` () =
    let report, context, now, request, _, _ = fixture ()
    for supplier, contributors, complete in [request.Subject.Owner, [request.Subject.Owner], true; "reviewer", ["reviewer";request.Subject.Owner], true; "reviewer", [request.Subject.Owner], false] do
        let facts = request.Facts |> List.map (fun f ->
            if f.Predicate = IndependentReview then
                {f with Support = AgentAttestation { Supplier = supplier; Role = "reviewer"; ActualModel = None; ActualEffort = None; Method = "review"; Contributors = contributors; ContributorsComplete = complete; Limitations = ["fixture"] }}
            else f)
        let r = {request with Envelope = None; Original = None; Action = Deliver; Facts = facts @ [{facts.Head with Id = "effect-clear"; Predicate = EffectClear}]}
        let decision = recommendOwnerContinuationsWithPreflight (Map.ofList [r.Subject.Slice, Ok(String.replicate 64 "a")]) report context now [r] |> List.exactlyOne
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
    let after = recommendOwnerContinuationsWithPreflight (Map.ofList [r.Subject.Slice, Ok(String.replicate 64 "a")]) report context (now.AddSeconds 20.0) [r] |> List.exactlyOne
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
