module FS.GG.Coord.GitHub.Tests.TransportTests

open System
open System.Security.Cryptography
open Xunit
open FS.GG.Coord.GitHub
open FS.GG.Coord.GitHub.Errors
open FS.GG.Coord.GitHub.Transport
open FS.GG.Coord.GitHub.V1Admission
open FS.GG.Coordination.GitHub

/// A response that succeeded, with a body.
let ok (body: string) =
    Ok
        {
            Status = 200
            Body = body
            ETag = None
            NextLink = None
            Headers = Map.empty
        }

let request path budget =
    {
        Method = "GET"
        Path = path
        Query = []
        Body = NoBody
        Budget = budget
        IfNoneMatch = None
        Subject = path
    }

type private StubFence(allow: bool) =
    let mutable calls = 0

    member _.Calls = calls

    interface IMutationFence with
        member _.Dispatch(_, send) =
            calls <- calls + 1
            if allow then Ok(send ()) else Error [ "test-refusal" ]

let private oid value =
    V1AdmissionRegistry.gitObjectId (String.replicate 40 value)
    |> Result.defaultWith failwith

let private digest (bytes: byte array) =
    SHA256.HashData bytes
    |> Convert.ToHexString
    |> _.ToLowerInvariant()
    |> V1AdmissionRegistry.sha256Digest
    |> Result.defaultWith failwith

let private envelope (providerRequest: Request) requestBytes =
    {
        Request = providerRequest
        Admission =
            {
                OperationId = "operation-1"
                Owner = "worker-1"
                Request =
                    {
                        EffectId = "effect-1"
                        RequestDigest = digest requestBytes
                        CanonicalRequestBytes = Array.copy requestBytes
                        Preconditions =
                            {
                                ExpectedEpochCommit = oid "a"
                                ExpectedEpochGeneration = 1L
                                ExpectedClaimGeneration = None
                                ExpectedOperationGeneration = 1L
                            }
                    }
            }
    }

[<Fact>]
let ``typed SendMutation records the full envelope in the fake`` () =
    let recorder = Fake.Recorder(fun _ -> ok "{}")
    let transport = recorder :> IGitHubTransport

    let providerRequest =
        { request "repos/o/r/issues/1" Rest with
            Method = "PATCH"
            Body = Json "{\"state\":\"closed\"}"
        }

    let bytes = canonicalMutationBytes providerRequest
    transport.SendMutation(envelope providerRequest bytes) |> ignore

    Assert.Single(recorder.Mutations) |> ignore
    Assert.Equal(1, recorder.RestCalls)

[<Fact>]
let ``fenced transport forwards only after the typed fence authorizes exact bytes`` () =
    let recorder = Fake.Recorder(fun _ -> ok "{}")
    let fence = StubFence true

    let transport =
        FencedTransport(recorder :> IGitHubTransport, fence) :> IGitHubTransport

    let providerRequest =
        { request "repos/o/r/issues/1" Rest with
            Method = "PATCH"
            Body = Json "{\"state\":\"closed\"}"
        }

    let bytes = canonicalMutationBytes providerRequest

    match transport.SendMutation(envelope providerRequest bytes) with
    | Ok _ ->
        Assert.Equal(1, fence.Calls)
        Assert.Equal(1, recorder.RestCalls)
    | Error error -> failwithf "expected authorized dispatch, got %A" error

[<Fact>]
let ``fenced transport does not call the provider when durable admission refuses`` () =
    let recorder = Fake.Recorder(fun _ -> ok "{}")
    let fence = StubFence false

    let transport =
        FencedTransport(recorder :> IGitHubTransport, fence) :> IGitHubTransport

    let providerRequest =
        { request "repos/o/r/issues/1" Rest with
            Method = "PATCH"
            Body = Json "{\"state\":\"closed\"}"
        }

    let bytes = canonicalMutationBytes providerRequest

    match transport.SendMutation(envelope providerRequest bytes) with
    | Error(Malformed(_, detail)) ->
        Assert.Contains("test-refusal", detail)
        Assert.Equal(1, fence.Calls)
        Assert.Equal(0, recorder.RestCalls)
    | other -> failwithf "expected durable admission refusal, got %A" other

[<Fact>]
let ``fenced transport refuses changed provider bytes before acquiring a permit`` () =
    let recorder = Fake.Recorder(fun _ -> ok "{}")
    let fence = StubFence true

    let transport =
        FencedTransport(recorder :> IGitHubTransport, fence) :> IGitHubTransport

    let providerRequest =
        { request "repos/o/r/issues/1" Rest with
            Method = "PATCH"
            Body = Json "{\"state\":\"closed\"}"
        }

    let stale = Text.Encoding.UTF8.GetBytes "different"

    match transport.SendMutation(envelope providerRequest stale) with
    | Error(Malformed(_, detail)) ->
        Assert.Contains("request bytes differ", detail)
        Assert.Equal(0, fence.Calls)
        Assert.Equal(0, recorder.RestCalls)
    | other -> failwithf "expected exact-byte refusal, got %A" other

[<Fact>]
let ``response header lookup is case-insensitive for GitHub rate-limit headers`` () =
    let response =
        {
            Status = 403
            Body = "rate limited"
            Headers = Map.ofList [ "x-ratelimit-resource", "core"; "x-ratelimit-reset", "1893456000" ]
            ETag = None
            NextLink = None
        }

    Assert.Equal(Some "core", Transport.header "X-RateLimit-Resource" response)
    Assert.Equal(Some "1893456000", Transport.header "X-RateLimit-Reset" response)

// ---- the call counters: the corpus's `gcount`, ported ---------------------------------------------

[<Fact>]
let ``#418 the meter read is FREE - GET rate_limit is billed to NEITHER counter`` () =
    // The corpus asserts `bootstrap: 2 GraphQL calls` and then measures every later assertion as a DELTA
    // from it. Bill the meter read and every one of those deltas shifts by one — so this is not a
    // micro-optimisation, it is the calibration the whole budget half of the fixture rests on. It is also
    // true of the real API: reading the meter does not spend the meter, which is exactly what makes "back
    // off until the reset" a strategy rather than a guess.
    let recorder = Fake.Recorder(fun _ -> ok "{}")
    let transport = recorder :> IGitHubTransport

    transport.Send(request "rate_limit" Free) |> ignore

    Assert.Equal(0, recorder.GraphQlCalls)
    Assert.Equal(0, recorder.RestCalls)

[<Fact>]
let ``a FAILED call still counts - you were charged for it`` () =
    // The `gh` stub increments its counter BEFORE it injects its 403, and it is right to: a call that came
    // back rate-limited is a call you made and were billed for. Counting only successes would report a
    // budget you did not spend — and the one thing a meter exists to do is tell you what you spent.
    let recorder = Fake.Recorder(fun _ -> Error(RateLimited(UnknownBudget, None)))

    let transport = recorder :> IGitHubTransport

    transport.Send(request "graphql" GraphQl) |> ignore

    Assert.Equal(1, recorder.GraphQlCalls)

[<Fact>]
let ``the two budgets are counted apart - GraphQL and REST are not one meter`` () =
    // They are separate limits with different units (nodes vs requests) and different exhaustion
    // behaviour, and the lock lives on REST because a lock may never live on the budget that dies first
    // (ADR-0027). WHICH budget that is has already inverted once — #418 measured GraphQL dying first,
    // #895 measured REST — so a single counter would make the distinction unobservable exactly when it
    // matters. It is the distinction the whole design rests on.
    let recorder = Fake.Recorder(fun _ -> ok "{}")
    let transport = recorder :> IGitHubTransport

    transport.Send(request "graphql" GraphQl) |> ignore
    transport.Send(request "repos/o/r/issues/1" Rest) |> ignore
    transport.Send(request "repos/o/r/issues/2" Rest) |> ignore

    Assert.Equal(1, recorder.GraphQlCalls)
    Assert.Equal(2, recorder.RestCalls)

// ---- the log grammar: the corpus's `$GH_LOG`, ported ----------------------------------------------
//
// THESE ARE THE ~20 ASSERTIONS ADR-0040 C1 IMPLIES BUT DOES NOT NAME. The shell corpus greps `$GH_LOG`
// for `gh project item-edit`'s CLI FLAGS — bytes that exist in no HTTP request anywhere. Under the port
// that write becomes a GraphQL mutation, so unless the fake synthesises the old line from the typed
// request, those assertions do not FAIL — they simply stop being about anything, which is worse than
// failing. Pinning the grammar here means a change to it breaks a test in this file rather than silently
// hollowing out twenty assertions over in the shell.

[<Fact>]
let ``the log names a SINGLE_SELECT write in the gh stub's grammar`` () =
    let recorder = Fake.Recorder(fun _ -> ok "{}")
    let transport = recorder :> IGitHubTransport

    transport.Send
        { request "graphql" GraphQl with
            Body =
                Query(
                    "mutation { updateProjectV2ItemFieldValue(input: {...}) { clientMutationId } }",
                    [
                        "itemId", VId "PVTI_coord123"
                        "projectId", VId "PVT_coord"
                        "fieldId", VId "PVTSSF_phase"
                        "optionId", VId "opt_p2"
                    ]
                )
        }
    |> ignore

    Assert.True(recorder.Logged "--single-select-option-id opt_p2")
    Assert.True(recorder.Logged "--field-id PVTSSF_phase")
    Assert.True(recorder.Logged "--project-id PVT_coord --field-id PVTSSF_phase")
    Assert.True(recorder.Logged "--id PVTI_coord123")

[<Fact>]
let ``an EMPTY value is --clear, never --text with an empty string`` () =
    // A real trap and a real bug: `gh project item-edit --text ''` is a NO-OP ("no changes to make"), so an
    // empty write silently left the old value in place and the board went on showing a `Blocked by` that
    // had been cleared. The clear is a DIFFERENT mutation (`clearProjectV2ItemFieldValue`), and the log has
    // to show that it was the one sent.
    let recorder = Fake.Recorder(fun _ -> ok "{}")
    let transport = recorder :> IGitHubTransport

    transport.Send
        { request "graphql" GraphQl with
            Body =
                Query(
                    "mutation { clearProjectV2ItemFieldValue(input: {...}) { clientMutationId } }",
                    [
                        "itemId", VId "PVTI_coord123"
                        "projectId", VId "PVT_coord"
                        "fieldId", VId "PVTSSF_blocked"
                    ]
                )
        }
    |> ignore

    Assert.True(recorder.Logged "--clear")
    Assert.False(recorder.Logged "--text ")

[<Fact>]
let ``the aliased batch document is logged whole - one document, aliases in order (#448)`` () =
    let recorder = Fake.Recorder(fun _ -> ok "{}")
    let transport = recorder :> IGitHubTransport

    let document =
        "mutation { f0: updateProjectV2ItemFieldValue(input: {...}) { clientMutationId } f1: clearProjectV2ItemFieldValue(input: {...}) { clientMutationId } }"

    transport.Send
        { request "graphql" GraphQl with
            Body = Query(document, [])
        }
    |> ignore

    Assert.True(recorder.Logged "batch-mutation mutation {")
    Assert.True(recorder.Logged "f0: updateProjectV2ItemFieldValue")
    Assert.True(recorder.Logged "f1: clearProjectV2ItemFieldValue")

[<Fact>]
let ``#494 a REST read names the repo it was ADDRESSED TO`` () =
    // The defect was an issue read sent to the wrong repository — same number, different repo, and two
    // repos can each have an issue #494. The only way a black-box fixture can see that is if the log names
    // the repo the request actually went to, so the corpus can assert both that the right one was read and
    // that the same-numbered issue next door was NOT.
    let recorder = Fake.Recorder(fun _ -> ok "[]")
    let transport = recorder :> IGitHubTransport

    transport.Send(request "repos/FS-GG/FS.GG.Rendering/issues/494" Rest) |> ignore

    Assert.True(recorder.Logged "issue-get FS-GG/FS.GG.Rendering 494")
    Assert.Equal(0, recorder.Count "issue-get FS-GG/FS.GG.SDD 494")

[<Fact>]
let ``the comment verbs are the stub's verbs - post, delete, patch, list`` () =
    let recorder = Fake.Recorder(fun _ -> ok "[]")
    let transport = recorder :> IGitHubTransport

    transport.Send(request "repos/FS-GG/FS.GG.SDD/issues/70/comments" Rest)
    |> ignore

    transport.Send
        { request "repos/FS-GG/FS.GG.SDD/issues/70/comments" Rest with
            Method = "POST"
        }
    |> ignore

    transport.Send
        { request "repos/FS-GG/FS.GG.SDD/issues/comments/901" Rest with
            Method = "DELETE"
        }
    |> ignore

    transport.Send
        { request "repos/FS-GG/FS.GG.SDD/issues/comments/901" Rest with
            Method = "PATCH"
        }
    |> ignore

    Assert.True(recorder.Logged "comment-list FS-GG/FS.GG.SDD 70")
    Assert.True(recorder.Logged "comment-post FS-GG/FS.GG.SDD 70")
    Assert.True(recorder.Logged "comment-delete FS-GG/FS.GG.SDD 901")
    Assert.True(recorder.Logged "comment-patch FS-GG/FS.GG.SDD 901")

[<Fact>]
let ``#418 the assignee goes over REST - it is never a GraphQL issue-edit`` () =
    // `gh issue edit --add-assignee @me` costs 4 measured GraphQL points; the REST assignees endpoint costs
    // zero of them. Claim + release used to hand back 8 points per item for nothing — on the budget #418
    // measured dying first, in the loop that drains it.
    let recorder = Fake.Recorder(fun _ -> ok "{}")
    let transport = recorder :> IGitHubTransport

    transport.Send
        { request "repos/FS-GG/FS.GG.SDD/issues/70/assignees" Rest with
            Method = "POST"
        }
    |> ignore

    Assert.True(recorder.Logged "assignee-post FS-GG/FS.GG.SDD 70")
    Assert.Equal(0, recorder.Count "issue-edit")
    Assert.Equal(0, recorder.GraphQlCalls)

[<Fact>]
let ``the claim scan is UNCONDITIONAL - inm=none, because a 304 could hide a live marker`` () =
    // The counterweight to the ETag cache, and it is not an optimisation: a 304 serving a body captured
    // before a claim marker was posted would report `comments: 0` over a LIVE LOCK. **A lock may never be
    // read from a cache.** The corpus asserts `inm=none` on exactly this request, so the log must carry it.
    let recorder = Fake.Recorder(fun _ -> ok "[]")
    let transport = recorder :> IGitHubTransport

    transport.Send(request "repos/FS-GG/FS.GG.Rendering/issues" Rest) |> ignore

    Assert.True(recorder.Logged "issue-list FS-GG/FS.GG.Rendering paginate=1 inm=none")

[<Fact>]
let ``#507 the child's id is sent as a JSON NUMBER, not a string`` () =
    // Under `gh`, `-f sub_issue_id=1047` sent the id as a JSON *string* and collected a 422; `-F` sent it
    // as a number. The corpus asserts the typed form. One layer down, over HTTP, the same defect is a JSON
    // body whose `sub_issue_id` is quoted — so the assertion is restated, not dropped.
    let recorder = Fake.Recorder(fun _ -> ok "{}")
    let transport = recorder :> IGitHubTransport

    transport.Send
        { request "repos/FS-GG/FS.GG.SDD/issues/42/sub_issues" Rest with
            Method = "POST"
            Body = Json """{"sub_issue_id":1047}"""
        }
    |> ignore

    Assert.True(recorder.Logged "sub-issue-add FS-GG/FS.GG.SDD 42 -F sub_issue_id=1047")
