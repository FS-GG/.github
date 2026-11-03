module FS.GG.Coord.GitHub.Tests.OwnerContinuationTests

open System
open System.IO
open System.Text.Json
open Xunit
open FS.GG.Coord
open FS.GG.Coord.OwnerContinuation
open FS.GG.Coord.GitHub
open FS.GG.Coord.GitHub.V2ProjectionSource
open FS.GG.Coord.Cli

let private now = DateTimeOffset.Parse "2026-11-03T14:00:00Z"
let private subject: Identity = { Subject = "FS-GG/.github#5281"; Owner = "worker"; Slice = "slice"; Attempt = "attempt"; Revision = "revision"; ScopeDigest = String.replicate 64 "a" }
let private fact predicate =
    { Id = predicateName predicate; Subject = subject; Predicate = predicate; Value = Known true
      Support = (if predicate = ScopeCurrent then NativeObservation("adapter", subject.ScopeDigest) else AgentAttestation {Supplier = "worker"; Role = "owner"; ActualModel = None; ActualEffort = None; Method = "isolated exact source check"; Contributors = ["worker"]; ContributorsComplete = true; Limitations = ["fixture"]})
      ObservedAt = now; ValidUntil = now.AddMinutes 1.0; Evidence = ["bounded-evidence"]; Supersedes = [] }
let private request: Request = { Envelope = None; Subject = subject; Action = VerifyIsolated; Original = None; Facts = [fact ScopeCurrent; fact EffectClear; fact Isolated; fact ResourceShare] }
let private decision r = recommend now r

[<Fact>]
let ``ordinary owned check advances without CI review grant or dispatch capacity`` () =
    Assert.Equal(ContinueOwner VerifyIsolated, (decision request).Recommendation)
    Assert.Contains("adr0096-ordinary-verification", (decision request).Reasons)

[<Theory>]
[<InlineData("unknown")>]
[<InlineData("conflict")>]
[<InlineData("false")>]
let ``unknown conflict and negative isolation cannot satisfy verification`` state =
    let value = match state with "unknown" -> Unknown "missing-supplier" | "conflict" -> Conflict ["a";"b"] | _ -> Known false
    let facts = request.Facts |> List.map (fun f -> if f.Predicate = Isolated then { f with Value = value } else f)
    Assert.Equal(Await "isolated-output-and-resource-share-evidence", (decision { request with Facts = facts }).Recommendation)

[<Fact>]
let ``original survives expiry restart conflicting scope and failed checks without resend`` () =
    let r = { request with Original = Some "original/cursor-42"; Facts = request.Facts |> List.map (fun f -> { f with ValidUntil = now.AddDays(-1.0); Value = Unknown "expired" }) }
    let first = decision r
    let recovered = decision { r with Facts = List.rev r.Facts }
    Assert.Equal(ObserveOriginal "original/cursor-42", first.Recommendation)
    Assert.Equal(encode [first], encode [recovered])
    Assert.Equal("original-terminal-readback", first.NextEvent)

[<Fact>]
let ``possible write without original requests custody judgment`` () =
    let r = { request with Facts = request.Facts |> List.filter (fun f -> f.Predicate <> EffectClear) }
    Assert.Equal(RequestJudgment "locate-original-before-advancement", (decision r).Recommendation)

[<Fact>]
let ``reported and wrong-subject positive facts never establish an executable predicate`` () =
    for alter in [ (fun f -> { f with Support = Reported "summary" }); (fun f -> { f with Subject = { subject with Attempt = "other" } }) ] do
        let r = { request with Facts = request.Facts |> List.map (fun f -> if f.Predicate = ResourceShare then alter f else f) }
        Assert.Equal(Await "isolated-output-and-resource-share-evidence", (decision r).Recommendation)

[<Fact>]
let ``stale future and earliest-read five minute bounds are conservative`` () =
    for time in [ now.AddSeconds 1.0; now.AddMinutes(-5.0).AddTicks(-1L); DateTimeOffset.MinValue ] do
        let r = { request with Facts = request.Facts |> List.map (fun f -> if f.Predicate = ScopeCurrent then { f with ObservedAt = time; ValidUntil = now.AddMinutes 1.0 } else f) }
        Assert.Equal(Await "native-scope-refresh", (decision r).Recommendation)
    let r = { request with Facts = request.Facts |> List.map (fun f -> { f with ObservedAt = now.AddMinutes(-5.0) }) }
    Assert.Equal(ContinueOwner VerifyIsolated, (decision r).Recommendation)

[<Fact>]
let ``fact permutations cache deletion and duplicate evaluation have byte identical advice`` () =
    let expected = encode [decision request]
    for shift in 0 .. 23 do
        let random = Random shift
        let facts = request.Facts |> List.sortBy (fun _ -> random.Next())
        Assert.Equal(expected, encode [decision { request with Facts = facts }])
    Assert.Equal(expected, encode [decision request])

[<Fact>]
let ``semantic input changes alter the determining digest`` () =
    let changed = { request with Facts = request.Facts |> List.map (fun f -> if f.Predicate = Isolated then { f with Evidence = ["different-evidence"] } else f) }
    Assert.NotEqual<string>((decision request).InputDigest, (decision changed).InputDigest)
    Assert.Equal((decision request).Recommendation, (decision changed).Recommendation)

[<Fact>]
let ``duplicate identities refuse and preserve explicit conflicts`` () =
    Assert.Equal(RequestJudgment "duplicate-fact-identities", (decision { request with Facts = request.Facts @ [fact Isolated] }).Recommendation)

[<Fact>]
let ``agent attestation of native checks cannot establish protected delivery`` () =
    let r = { request with Action = Deliver; Facts = request.Facts @ [fact NativeRollup; fact IndependentReview; {fact NativePullRequest with Support = NativeObservation("adapter", subject.ScopeDigest)}] }
    Assert.Equal(Await "native-check-qualification", (decision r).Recommendation)

let private observed () =
    let transport = ProgrammeFixture.transport ProgrammeFixture.newAssignment
    let report = inspectFixed transport (ProgrammeFixture.binding ()) None
    let context = ProgrammeFixture.context report
    Assert.Empty transport.Mutations
    report, context

let private envelope (context: SelectionContext) original =
    let slice = context.Slices.Head
    let identity = continuationIdentity slice
    let facts = [EffectClear; Isolated; ResourceShare] |> List.map (fun predicate ->
        {| id = predicateName predicate; scopeDigest = identity.ScopeDigest; predicate = predicateName predicate; state = "known"; value = true; detail = ([]: string list)
           support = {| kind = "attestation"; supplier = slice.Owner; role = "owner"; model = (None: string option); effort = (None: string option); method = "isolated fresh outputs, credential free, bounded share"; contributors = [slice.Owner]; contributorsComplete = true; limitations = ["fixture"] |}
           observedAt = context.ObservedAt; validUntil = context.ValidUntil; evidence = ["isolated-private-output-and-resource-share"]; supersedes = ([]: string list) |})
    use integrator = JsonDocument.Parse(ProgrammeFixture.integratorJson context)
    JsonSerializer.Serialize {| schema = "fsgg.coord.owner-continuation-input/1"; repositoryCheckout = (None: string option); draft = (None: string option); integrator = integrator.RootElement;
                               requests = [ {| sliceId = slice.Id; attempt = slice.Attempt; owner = slice.Owner; revision = identity.Revision
                                               scopeDigest = identity.ScopeDigest; action = "verify-isolated"; original = original; envelope = (None: string option); facts = facts |} ] |}

[<Fact>]
let ``actual closed codec rejects native support duplicate identities unknown fields and stale joins`` () =
    let _, context = observed ()
    let json = envelope context (None: string option)
    Assert.True(BoardV2Application.parseOwnerContinuationObservations (ProgrammeFixture.binding ()) json |> Result.isOk)
    for bad in [ json.Insert(1, "\"extra\":1,"); json.Insert(1, "\"schema\":\"duplicate\",")
                 json.Replace("\"attestation\"", "\"native\""); json.Replace((continuationIdentity context.Slices.Head).ScopeDigest, String.replicate 64 "c"); String.replicate 262145 "x" ] do
        Assert.True(BoardV2Application.parseOwnerContinuationObservations (ProgrammeFixture.binding ()) bad |> Result.isError)

[<Fact>]
let ``adapter joins native owner scope and revision while unrelated gaps do not block checks`` () =
    let report, context = observed ()
    let slice = context.Slices.Head
    let identity = continuationIdentity slice
    let facts = request.Facts |> List.filter (fun f -> f.Predicate <> ScopeCurrent) |> List.map (fun f -> { f with Subject = identity; ObservedAt = context.ObservedAt; ValidUntil = context.ValidUntil })
    let r = { request with Subject = identity; Facts = facts }
    let output = recommendOwnerContinuations { report with MembershipGap = Some(Errors.Malformed("unrelated", "missing")); NativeIntegrator = { report.NativeIntegrator with RepositoryGaps = Map.ofList ["FS-GG/Other", "unavailable"] } } { context with ImplementationSlots = None; CapacityObservation = None } context.ObservedAt [r]
    Assert.Equal(ContinueOwner VerifyIsolated, output.Head.Recommendation)
    let wrong = { context with Slices = [{slice with Owner = "new-owner"}] }
    Assert.Equal(Await "native-scope-refresh", (recommendOwnerContinuations report wrong context.ObservedAt [r]).Head.Recommendation)

[<Theory>]
[<InlineData(false)>]
[<InlineData(true)>]
let ``actual inspect CLI emits owner advice or retains original with zero native effects`` original =
    let _, context = observed ()
    let context = { context with ImplementationSlots = None; ReviewSlots = None; HostedCiSlots = None; CapacityObservation = None; Reservations = None; ReservationObservation = None }
    let json = envelope context (if original then Some "original/cursor-42" else None)
    let directory = Path.Combine(Path.GetTempPath(), "owner-continuation-" + Guid.NewGuid().ToString("N"))
    Directory.CreateDirectory(directory, UnixFileMode.UserRead ||| UnixFileMode.UserWrite ||| UnixFileMode.UserExecute) |> ignore
    try
        let binding = Path.Combine(directory, "binding.json")
        let output = Path.Combine(directory, "report.json")
        File.WriteAllText(binding, ProgrammeFixture.bindingJson ())
        let transport = ProgrammeFixture.transport ProgrammeFixture.newAssignment
        use input = new StringReader(json)
        let result = BoardV2Application.runWithTransportAndInput directory transport input ["inspect"; "--binding-file"; binding; "--report-file"; output; "--owner-continuation-stdin"]
        Assert.Equal(0, result)
        use document = JsonDocument.Parse(File.ReadAllText output)
        let root = document.RootElement
        Assert.Equal("fsgg.coord.board-v2-owner-inspection/1", root.GetProperty("schema").GetString())
        let advice = root.GetProperty("ownerContinuation").[0]
        Assert.Equal((if original then "observe-original" else "continue-owner"), advice.GetProperty("kind").GetString())
        Assert.False(advice.GetProperty("dispatchAuthority").GetBoolean())
        Assert.False(root.TryGetProperty("selection") |> fst)
        Assert.Equal(context.Slices.Head.Attempt, advice.GetProperty("attempt").GetString())
        Assert.Empty transport.Mutations
    finally Directory.Delete(directory, true)

[<Fact>]
let ``owner continuation flag is read only mutually exclusive and opt in`` () =
    let validate args = BoardV2Application.validateInvocation (["board-v2"] @ args) |> Option.get
    let files = ["--binding-file";"private.json";"--report-file";"new.json"]
    Assert.True(validate (["inspect"] @ files @ ["--owner-continuation-stdin"]) |> Result.isOk)
    Assert.True(validate (["refresh"] @ files @ ["--owner-continuation-stdin"]) |> Result.isError)
    Assert.True(validate (["inspect"] @ files @ ["--owner-continuation-stdin";"--integrator-stdin"]) |> Result.isError)

[<Fact>]
let ``semantic identity is quiet across explicit clock changes and changes at freshness expiry`` () =
    let first = recommend now request
    let same = recommend (now.AddSeconds 1.0) request
    let expired = recommend (now.AddMinutes 2.0) request
    Assert.Equal(first.Id, same.Id)
    Assert.NotEqual<string>(first.InputDigest, same.InputDigest)
    Assert.NotEqual<string>(first.Id, expired.Id)
    Assert.Equal(Await "native-scope-refresh", expired.Recommendation)

[<Fact>]
let ``actual compact owner report resumes its own history without promoting history to facts`` () =
    let _, context = observed ()
    let json = envelope context (None: string option)
    let directory = Path.Combine(Path.GetTempPath(), "owner-history-" + Guid.NewGuid().ToString("N"))
    Directory.CreateDirectory(directory, UnixFileMode.UserRead ||| UnixFileMode.UserWrite ||| UnixFileMode.UserExecute) |> ignore
    try
        let binding = Path.Combine(directory, "binding.json")
        File.WriteAllText(binding, ProgrammeFixture.bindingJson ())
        let run name previous =
            let output = Path.Combine(directory, name)
            let transport = ProgrammeFixture.transport ProgrammeFixture.newAssignment
            use input = new StringReader(json)
            let args = ["inspect"; "--binding-file"; binding; "--report-file"; output; "--owner-continuation-stdin"] @ (previous |> Option.map (fun path -> ["--previous-report-file"; path]) |> Option.defaultValue [])
            Assert.Equal(0, BoardV2Application.runWithTransportAndInput directory transport input args)
            Assert.Empty transport.Mutations
            use document = JsonDocument.Parse(File.ReadAllText output)
            output, document.RootElement.Clone()
        let firstPath, first = run "first.json" None
        let _, second = run "second.json" (Some firstPath)
        Assert.False(first.TryGetProperty("items") |> fst)
        Assert.Equal(1, first.GetProperty("changedOwnerContinuation").GetArrayLength())
        Assert.Equal(0, second.GetProperty("changedOwnerContinuation").GetArrayLength())
        Assert.Equal(first.GetProperty("ownerContinuation").[0].GetProperty("id").GetString(), second.GetProperty("ownerContinuation").[0].GetProperty("id").GetString())
        Assert.Equal("continue-owner", second.GetProperty("ownerContinuation").[0].GetProperty("kind").GetString())
    finally Directory.Delete(directory, true)

[<Fact>]
let ``fact decoder refuses copying review evidence to a different source scope`` () =
    let _, context = observed ()
    let json = envelope context (None: string option)
    let node = System.Text.Json.Nodes.JsonNode.Parse json
    node.["requests"].[0].["facts"].[0].["scopeDigest"] <- JsonSerializer.SerializeToNode(String.replicate 64 "f")
    Assert.True(BoardV2Application.parseOwnerContinuationObservations (ProgrammeFixture.binding ()) (node.ToJsonString()) |> Result.isError)

[<Theory>]
[<InlineData(false)>]
[<InlineData(true)>]
let ``actual owner invocation scopes item gaps while retaining global membership failure`` membershipGap =
    let first = ProgrammeFixture.issue
    let other = {first with Number = 4700}
    let rows = ["I_outcome", first, "docs/coordination/programme.md"; "I_other", other, "docs/coordination/programme.md"]
    let baseBinding = ProgrammeFixture.binding ()
    let selected = {baseBinding with SelectedIssues = Map.ofList ["I_outcome", "FS-GG/.github#4634"; "I_other", "FS-GG/.github#4700"]} |> fun binding -> ProgrammeFixture.enroll binding rows
    let healthy = ProgrammeFixture.populationTransport selected rows ProgrammeFixture.unchanged
    let report = inspectFixed healthy selected None
    let context = ProgrammeFixture.context report
    let directory = Path.Combine(Path.GetTempPath(), "owner-scoped-exit-" + Guid.NewGuid().ToString("N"))
    Directory.CreateDirectory(directory, UnixFileMode.UserRead ||| UnixFileMode.UserWrite ||| UnixFileMode.UserExecute) |> ignore
    try
        let bindingPath = Path.Combine(directory, "binding.json")
        let output = Path.Combine(directory, "report.json")
        File.WriteAllText(bindingPath, ProgrammeFixture.bindingJsonFor selected)
        let change (request: Transport.Request) response =
            if (membershipGap && request.Subject = "complete V2 project memberships") || (not membershipGap && request.Subject = "FS-GG/.github#4700 native observation") then
                Error(Errors.Malformed(request.Subject, "synthetic unavailable supplier"))
            else response
        let transport = ProgrammeFixture.populationTransport selected rows change
        use input = new StringReader(envelope context (None: string option))
        let result = BoardV2Application.runWithTransportAndInput directory transport input ["inspect"; "--binding-file"; bindingPath; "--report-file"; output; "--owner-continuation-stdin"]
        Assert.Equal((if membershipGap then 3 else 0), result)
        use document = JsonDocument.Parse(File.ReadAllText output)
        Assert.Equal((if membershipGap then "await" else "continue-owner"), document.RootElement.GetProperty("ownerContinuation").[0].GetProperty("kind").GetString())
        Assert.Empty transport.Mutations
    finally Directory.Delete(directory, true)
