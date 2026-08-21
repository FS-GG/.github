module FS.GG.Coord.GitHub.Tests.WriteTests

open Xunit
open FS.GG.Coord.Types
open FS.GG.Coord.GitHub
open FS.GG.Coord.GitHub.Errors
open FS.GG.Coord.GitHub.Transport
open FS.GG.Coord.GitHub.Writes

let private aRef =
    { Owner = "FS-GG"
      Repo = "FS.GG.SDD"
      Number = 42 }

let private me = WorkerId "vole-418"
let private them = WorkerId "kite-461"

/// THE ORDINARY CALLER (#1646): this process's OWN worker id is the one it is acting as.
///
/// That is every invocation the protocol prescribes — `eval "$(scripts/fsgg-coord whoami --mint)"` and then
/// run the verb — so it is the default throughout this file, and the impersonation legs below are the ones
/// that deviate from it. Spelling it as a named value rather than `Derives me` at fifty call sites keeps the
/// deviation VISIBLE: a test that passes anything else is making a point.
let private itsMe = Derives me

let private now = System.DateTimeOffset.UtcNow.ToString("o")

let private marker (id: int) (worker: string) (extra: string) =
    let body = $"<!-- fsgg:claim worker=%s{worker} lease=120%s{extra} -->"
    $"""{{"id":%d{id},"body":"%s{body}","updated_at":"%s{now}"}}"""

/// A marker whose lease LAPSED — last heartbeated 3h ago, well past the 120m lease used throughout. The
/// next claimant must COLLECT it, never merely out-order it.
let private stale = System.DateTimeOffset.UtcNow.AddHours(-3.0).ToString("o")

let private staleClaimJson (id: int) (worker: string) (extra: string) =
    let body = $"<!-- fsgg:claim worker=%s{worker} lease=120%s{extra} -->"
    $"""{{"id":%d{id},"body":"%s{body}","updated_at":"%s{stale}"}}"""

/// The comments endpoint answers with a JSON array. F# will not let a string literal sit inside an
/// interpolation hole, so the array is built here rather than spelled inline at every call site.
let private comments (ms: string list) = "[" + String.concat "," ms + "]"

let private markerWithExactBody (id: int) (body: string) =
    System.Text.Json.JsonSerializer.Serialize(
        {| id = id
           body = body
           updated_at = now |}
    )

let private durableLeaseComment (id: int) (body: string) =
    System.Text.Json.JsonSerializer.Serialize(
        {| id = id
           html_url = $"https://example.invalid/comments/%d{id}"
           body = body |}
    )

let private postedCommentBody (request: Request) =
    match request.Body with
    | Json payload ->
        use document = System.Text.Json.JsonDocument.Parse payload
        document.RootElement.GetProperty("body").GetString()
    | other -> failwith $"expected a JSON comment POST, got %A{other}"

/// A comment the lock reader cannot classify: its id is readable, but its body is not. It may be a claim
/// marker, so a decision made from the readable markers beside it would be a decision from a lower bound.
let private unclassifiableComment (id: int) =
    $"""{{"id":%d{id},"body":null,"updated_at":"%s{now}"}}"""

/// A transport that answers a SEQUENCE of canned responses — so a CAS, which reads, posts, and re-reads,
/// can be driven through a real race.
let private scripted (responses: IoResult<Response> list) =
    let queue = System.Collections.Generic.Queue<IoResult<Response>>(responses)

    Fake.Recorder(fun _ ->
        if queue.Count = 0 then
            failwith "the transport was called more times than the test scripted"
        else
            queue.Dequeue())

let private scriptedSteps (steps: (Request -> IoResult<Response>) list) =
    let queue = System.Collections.Generic.Queue<Request -> IoResult<Response>>(steps)

    Fake.Recorder(fun request ->
        if queue.Count = 0 then
            failwith "the transport was called more times than the test scripted"
        else
            queue.Dequeue() request)

let private ok (body: string) =
    Ok
        { Status = 200
          Body = body
          ETag = None
          NextLink = None; Headers = Map.empty }

[<Fact>]
let ``#2131 guarded merge binds GitHub's write to the inspected head SHA`` () =
    let recorder = Fake.Recorder(fun request ->
        Assert.Equal("PUT", request.Method)
        Assert.Equal("repos/FS-GG/FS.GG.SDD/pulls/99/merge", request.Path)
        match request.Body with
        | Json body -> Assert.Contains("head-a", body)
        | _ -> failwith "a guarded merge must carry the inspected head SHA"
        ok """{"merged":true}""")

    match Writes.mergeAtHead recorder aRef 99 "head-a" with
    | Ok true -> Assert.Equal(1, recorder.RestCalls)
    | outcome -> failwithf "expected a guarded merge, got %A" outcome

[<Fact>]
let ``#2131 a GitHub head mismatch is a refused guarded merge, not a green write`` () =
    let recorder = Fake.Recorder(fun _ -> ok """{"merged":false,"message":"Head branch was modified"}""")

    match Writes.mergeAtHead recorder aRef 99 "head-a" with
    | Ok false -> ()
    | outcome -> failwithf "expected a refused guarded merge, got %A" outcome

// ---- the CHORE LOCK: the same CAS, on a different SUBJECT (ADR-0041, #873) --------------------------
//
// #873 asked which substrate a chore lock takes, and framed `Writes.claim` as "145 lines of claim-specific
// policy that a chore lock wants none of" — so reusing it meant factoring the org's most safety-critical
// function, and not reusing it meant a second CAS (#485). ADR-0041 decides it by observing that the framing
// is wrong on its load-bearing point: `claim` touches ONLY comments, its lease is already a PARAMETER, and
// its one board coupling is the caller-supplied `readPreviousStatus` callback. It is already a general
// comment-order CAS over an arbitrary issue ref.
//
// These pin that premise, because an ADR whose central claim nothing checks is a claim of coverage with
// nothing behind it (#944). If somebody gives `claim` a board dependency — a `set-field`, a project read —
// ADR-0041's decision silently stops being true, and the chore lock built on it stops being buildable.
// That must fail HERE, not in #733's wiring.
//
// And it does, by a mechanism worth naming rather than trusting: `scripted` answers a FIXED list of
// responses and `failwith`s the moment it is called a fourth time. So scripting exactly three responses
// (read, post, re-read) asserts the CALL SHAPE, not merely the outcome — an added board read is a fourth
// call and reds these instantly. Verified by mutation on this tree: inserting one extra transport read into
// `claim` fails 26 of these 57 tests, including all three below.
//
// The subject is a CLOSED, off-board issue that exists only to be a lock (ADR-0041): closed, so it never
// appears in an `--state open` read and can never be mistaken for work; not LOCKED, because a locked
// conversation refuses comments and the marker IS a comment.

/// The per-repo chore-lock issue — off the board, and not `aRef`. The prefix (`fsgg:claim`) needs no
/// parameterising precisely because the SUBJECT disambiguates: only chore markers live here.
let private choreLock =
    { Owner = "FS-GG"
      Repo = "FS.GG.SDD"
      Number = 7 }

/// A chore is seconds long, not two hours (#550). The lease is a parameter, so this costs no refactor.
let private choreLease = 2

[<Fact>]
let ``the chore lock is the item CAS UNCHANGED — an off-board ref, a short lease, and no column`` () =
    let transport =
        scripted
            [ ok "[]" // 1. read: the lock is free
              ok """{"id":901}""" // 2. post our marker
              ok (comments [ marker 901 "vole-418" "" ]) ] // 3. re-read: we hold it

    // The chore-lock configuration, in full: a short lease, and `fun () -> None` for the board callback —
    // a lock issue has no column to restore, which is why the coupling belongs in the callback and not in
    // the CAS. No new function, no new marker prefix, no parameter that does not already exist.
    match claim transport choreLease RefuseLiveHolder ignore me itsMe None choreLock (fun () -> None) with
    | Ok(Won(held, _)) ->
        Assert.Equal(901L, held.MarkerId)
        Assert.Equal(me, held.Worker)

        // #481's `prev=` is ABSENT rather than empty — the column nobody recorded is not restored, and the
        // chore lock never had one. This is the whole of what `claim` wanted from the board, declined.
        Assert.Equal(None, held.PreviousStatus)
    | other -> failwith $"the chore lock must be winnable with the CAS as it stands — got %A{other}"

    // AND IT RAN ON THE CHORE-LOCK SUBJECT — asserted, because `scripted` cannot enforce it. The fake
    // answers a queue and IGNORES the request, so every assertion above would hold identically if `claim`
    // had addressed item #42. That is not a nitpick here: ADR-0041's argument for reusing the CAS verbatim
    // is precisely that *the SUBJECT disambiguates* — only chore markers live on the lock issue, which is
    // why the `fsgg:claim` prefix needs no parameterising. A test that cannot see the subject is not
    // testing that argument.
    Assert.True(transport.Logged "comment-list FS-GG/FS.GG.SDD 7", "the CAS did not read the chore-lock issue")
    Assert.True(transport.Logged "comment-post FS-GG/FS.GG.SDD 7", "the marker was not posted to the chore-lock issue")

    // ...and NOT on an item. The lock and the items it reconciles are different subjects, and conflating
    // them would put a chore marker on somebody's live claim.
    Assert.False(transport.Logged "comment-post FS-GG/FS.GG.SDD 42", "a chore marker was posted to an ITEM")

[<Fact>]
let ``#1732 a scoped claim records its path repository in the marker`` () =
    let responses =
        System.Collections.Generic.Queue<IoResult<Response>>(
            [ ok "[]"
              ok """{"id":901}"""
              ok (comments [ marker 901 "vole-418" " pathRepo=FS.GG.Rendering" ]) ]
        )

    let bodies = System.Collections.Generic.List<string>()

    let transport =
        Fake.Recorder(fun req ->
            match req.Body with
            | Json body -> bodies.Add body
            | _ -> ()

            responses.Dequeue())

    match
        claimScoped
            transport
            120
            RefuseLiveHolder
            ignore
            me
            itsMe
            None
            aRef
            (fun () -> None)
            (fun () -> Some "FS.GG.Rendering")
            (fun () -> Ok())
    with
    | Ok(Won(held, _)) ->
        Assert.Equal(Some "FS.GG.Rendering", held.PathRepo)
        Assert.Contains("pathRepo=FS.GG.Rendering", Seq.last bodies)
    | other -> failwith $"the scoped claim must win and carry its path repository — got %A{other}"

[<Fact>]
let ``#2758 a claim marker records the dispatched agent contract version`` () =
    let variable = "FSGG_AGENT_CONTRACT_VERSION"
    let before = System.Environment.GetEnvironmentVariable variable
    let version = System.String('a', 64)

    try
        System.Environment.SetEnvironmentVariable(variable, version)

        let responses =
            System.Collections.Generic.Queue<IoResult<Response>>(
                [ ok "[]"
                  ok """{"id":901}"""
                  ok (comments [ marker 901 "vole-418" $" agentContract=%s{version}" ]) ]
            )

        let bodies = System.Collections.Generic.List<string>()

        let transport =
            Fake.Recorder(fun req ->
                match req.Body with
                | Json body -> bodies.Add body
                | _ -> ()

                responses.Dequeue())

        match
            claimScoped
                transport
                120
                RefuseLiveHolder
                ignore
                me
                itsMe
                None
                aRef
                (fun () -> None)
                (fun () -> Some "FS.GG.Rendering")
                (fun () -> Ok())
        with
        | Ok(Won _) -> Assert.Contains($"agentContract=%s{version}", Seq.last bodies)
        | other -> failwith $"the attributed claim must win — got %A{other}"
    finally
        System.Environment.SetEnvironmentVariable(variable, before)

[<Fact>]
let ``#2758 create then environment change then heartbeat preserves the dispatch agent contract`` () =
    let variable = "FSGG_AGENT_CONTRACT_VERSION"
    let before = System.Environment.GetEnvironmentVariable variable
    let dispatched = System.String('a', 64)
    let renewalEnvironment = System.String('b', 64)
    let mutable scans = 0
    let mutable posted = ""
    let mutable patched = ""

    let bodyOf (request: Request) =
        match request.Body with
        | Json payload ->
            use doc = System.Text.Json.JsonDocument.Parse payload
            doc.RootElement.GetProperty("body").GetString()
        | _ -> failwith "claim and heartbeat must send a JSON comment body"

    let transport =
        Fake.Recorder(fun request ->
            match request.Method, request.Path with
            | "GET", "repos/FS-GG/FS.GG.SDD/issues/42/comments" ->
                scans <- scans + 1

                if scans = 1 then
                    ok "[]"
                else
                    let comment =
                        System.Text.Json.JsonSerializer.Serialize
                            [ {| id = 901
                                 body = posted
                                 updated_at = System.DateTimeOffset.UtcNow.ToString("o") |} ]

                    ok comment
            | "POST", "repos/FS-GG/FS.GG.SDD/issues/42/comments" ->
                posted <- bodyOf request
                ok """{"id":901}"""
            | "PATCH", "repos/FS-GG/FS.GG.SDD/issues/comments/901" ->
                patched <- bodyOf request
                ok ""
            | method', path -> failwith $"unexpected #2758 fixture request: %s{method'} %s{path}")

    try
        System.Environment.SetEnvironmentVariable(variable, dispatched)

        match
            claimScoped
                transport
                120
                RefuseLiveHolder
                ignore
                me
                itsMe
                None
                aRef
                (fun () -> None)
                (fun () -> Some "FS.GG.Rendering")
                (fun () -> Ok())
        with
        | Ok(Won(held, _)) ->
            System.Environment.SetEnvironmentVariable(variable, renewalEnvironment)

            match heartbeat transport 120 held with
            | Ok beaten ->
                Assert.Equal(Some dispatched, beaten.AgentContract)
                Assert.Contains($"agentContract=%s{dispatched}", patched)
                Assert.DoesNotContain($"agentContract=%s{renewalEnvironment}", patched)
            | Error e -> failwith $"the attributed heartbeat should have landed — got %A{e}"
        | other -> failwith $"the attributed claim must win before its heartbeat — got %A{other}"
    finally
        System.Environment.SetEnvironmentVariable(variable, before)

[<Fact>]
let ``a rejected new force admission evicts nothing and posts no marker`` () =
    let mutable admissions = 0
    let transport = scripted [ ok (comments [ marker 901 "kite-461" "" ]) ]

    let rejected () =
        admissions <- admissions + 1
        Error(RateLimited(UnknownBudget, None))

    match
        claimScoped transport 120 StealLiveHolder ignore me itsMe None aRef (fun () -> None) (fun () -> None) rejected
    with
    | Error(RateLimited _) -> ()
    | other -> failwith $"a rejected force admission must stop before eviction — got %A{other}"

    Assert.Equal(1, admissions)
    Assert.False(transport.Logged "comment-delete FS-GG/FS.GG.SDD 901")
    Assert.False(transport.Logged "comment-post FS-GG/FS.GG.SDD 42")

[<Fact>]
let ``a chore is CLAIMED, not broadcast — the CAS refuses the second worker`` () =
    // CONDITION 1, and the reason the chore queue shipped unwired. If N workers each call `next` and each is
    // handed the same chore, N of them do it — #464 (N workers file one finding N times) and #463 (two
    // workers hand-synced the same kit twice in a day), rediscovered inside the mechanism meant to help.
    //
    // The item CAS already refuses this, on this subject, with no changes. That IS the decision.
    let transport =
        scripted
            [ ok "[]"
              ok """{"id":902}""" // ours
              ok (comments [ marker 901 "kite-461" ""; marker 902 "vole-418" "" ]) // a rival got there first
              ok "" ] // so we withdraw

    match claim transport choreLease RefuseLiveHolder ignore me itsMe None choreLock (fun () -> None) with
    | Ok(Lost w) -> Assert.Equal(them, w)
    | other -> failwith $"two workers must not hold one chore — got %A{other}"

    Assert.True(transport.Logged "comment-delete FS-GG/FS.GG.SDD 902")

[<Fact>]
let ``the chore lock COLLECTS a dead holder's debris — the lock a worker died holding`` () =
    // #873 argued a chore lock "has no debris to collect". It has exactly the debris every lock has: a
    // worker that dies mid-chore leaves a marker, and with a short lease it lapses in minutes. Collection is
    // not policy a chore lock wants NONE of — it is the thing that makes a short lease self-healing, and it
    // is already written. This is one of three reasons ADR-0041 does not parameterise these protections off.
    let transport =
        scripted
            [ ok (comments [ staleClaimJson 800 "kite-461" "" ]) // a lapsed chore lock, holder gone
              ok """{"id":901}"""
              ok (comments [ staleClaimJson 800 "kite-461" ""; marker 901 "vole-418" "" ])
              ok "" ] // collect the dead marker

    match claim transport choreLease RefuseLiveHolder ignore me itsMe None choreLock (fun () -> None) with
    | Ok(Won(held, evicted)) ->
        Assert.Equal(901L, held.MarkerId)
        Assert.Contains(them, evicted)
    | other -> failwith $"a lapsed chore lock must be collectable — got %A{other}"

    Assert.True(transport.Logged "comment-delete FS-GG/FS.GG.SDD 800")

// ---- the CAS ---------------------------------------------------------------------------------------

[<Fact>]
let ``#1896 the CAS pre-read refuses one unclassifiable comment beside a readable marker, before posting`` () =
    let transport =
        scripted [ ok (comments [ marker 901 "kite-461" ""; unclassifiableComment 902 ]) ]

    match claim transport 120 RefuseLiveHolder ignore me itsMe None aRef (fun () -> None) with
    | Error(Malformed(_, detail)) ->
        Assert.Contains("claim-marker scan is incomplete", detail)
        Assert.Contains("comment 1", detail)
    | other -> failwith $"an incomplete pre-read must refuse the CAS — got %A{other}"

    Assert.False(transport.Logged "comment-post", "the CAS posted against an incomplete lock read")

[<Fact>]
let ``#1896 the CAS re-read refuses incompleteness and withdraws the marker it already posted`` () =
    let transport =
        scripted
            [ ok "[]"
              ok """{"id":901}"""
              ok (comments [ marker 901 "vole-418" ""; unclassifiableComment 902 ])
              ok "" ]

    match claim transport 120 RefuseLiveHolder ignore me itsMe None aRef (fun () -> None) with
    | Ok(Undecided reason) -> Assert.Contains("claim-marker scan is incomplete", reason)
    | other -> failwith $"an incomplete re-read must be an undecided loss — got %A{other}"

    Assert.True(transport.Logged "comment-delete FS-GG/FS.GG.SDD 901", "the CAS left its marker orphaned")

[<Fact>]
let ``the CAS WINS when our marker is the lowest live id`` () =
    let transport =
        scripted
            [ ok "[]" // 1. read: nobody holds it
              ok """{"id":901}""" // 2. post our marker
              ok (comments [ marker 901 "vole-418" "" ]) ] // 3. re-read: we are the lowest

    match claim transport 120 RefuseLiveHolder ignore me itsMe None aRef (fun () -> None) with
    | Ok(Won(held, _)) ->
        Assert.Equal(901L, held.MarkerId)
        Assert.Equal(me, held.Worker)
    | other -> failwith $"we should have won — got %A{other}"

[<Fact>]
let ``the CAS LOSES to a lower id, and WITHDRAWS its own marker`` () =
    // The race, exactly: our marker is posted, and between the post and the re-read a rival's marker lands
    // with a LOWER id. Comment ids come from one server-side sequence, so both racers compute the same
    // total order and exactly one of them concludes it won.
    let transport =
        scripted
            [ ok "[]" // 1. read: free
              ok """{"id":902}""" // 2. post ours (902)
              ok (comments [ marker 901 "kite-461" ""; marker 902 "vole-418" "" ]) // 3. re-read: 901 beat us
              ok "" ] // 4. DELETE our 902

    match claim transport 120 RefuseLiveHolder ignore me itsMe None aRef (fun () -> None) with
    | Ok(Lost w) -> Assert.Equal(them, w)
    | other -> failwith $"we should have lost to the lower id — got %A{other}"

    // BACKING OFF CLEANLY IS HALF THE PROTOCOL. A marker we posted and did not withdraw is a lock held by a
    // worker who does not know they hold it, and nothing will ever release it.
    Assert.True(transport.Logged "comment-delete FS-GG/FS.GG.SDD 902")

[<Fact>]
let ``'we cannot tell' is a LOSS - our marker missing from the re-read withdraws and refuses`` () =
    // The CAS's sharpest rule. Reading "I could not see my own marker" as a WIN would grant a lock on the
    // strength of an observation we did not make — and two workers would be handed the same files.
    let transport =
        scripted
            [ ok "[]"
              ok """{"id":901}"""
              ok "[]" // the re-read does not contain our marker at all
              ok "" ] // so we withdraw it

    match claim transport 120 RefuseLiveHolder ignore me itsMe None aRef (fun () -> None) with
    | Ok(Undecided _) -> ()
    | other -> failwith $"an unobservable outcome is a LOSS, never a win — got %A{other}"

    Assert.True(transport.Logged "comment-delete FS-GG/FS.GG.SDD 901")

[<Fact>]
let ``a FAILED re-read withdraws the marker and refuses - it never wins by default`` () =
    let transport =
        scripted
            [ ok "[]"
              ok """{"id":901}"""
              Error(Http(502, "bad gateway")) // the re-read failed
              ok "" ] // withdraw

    match claim transport 120 RefuseLiveHolder ignore me itsMe None aRef (fun () -> None) with
    | Ok(Undecided _) -> ()
    | other -> failwith $"a failed re-read must not win the lock — got %A{other}"

    Assert.True(transport.Logged "comment-delete FS-GG/FS.GG.SDD 901")

[<Fact>]
let ``a marker we can neither win with NOR withdraw is reported LOUDLY - it is orphaned`` () =
    // The one genuinely bad outcome, and it must be reported as itself: the marker is on the issue, we do
    // not hold the item, and a human has to reap it. Swallowing this would leave a lock nobody owns and
    // nobody can see.
    let transport =
        scripted
            [ ok "[]"
              ok """{"id":901}"""
              Error(Http(502, "bad gateway"))
              Error(Http(500, "delete failed")) ]

    match claim transport 120 RefuseLiveHolder ignore me itsMe None aRef (fun () -> None) with
    | Error(Transport detail) -> Assert.Contains("orphaned", detail)
    | other -> failwith $"an orphaned marker must be loud — got %A{other}"

[<Fact>]
let ``the CAS refuses BEFORE posting when somebody else already holds a live lock`` () =
    // Post-then-withdraw would work, but it leaves a comment somebody has to read on an item that was never
    // ours. Refuse cheaply.
    let transport = scripted [ ok (comments [ marker 901 "kite-461" "" ]) ]

    match claim transport 120 RefuseLiveHolder ignore me itsMe None aRef (fun () -> None) with
    | Ok(Lost w) -> Assert.Equal(them, w)
    | other -> failwith $"a live rival lock must be refused — got %A{other}"

    Assert.Equal(0, transport.Count "comment-post")

[<Fact>]
let ``an UNPARSEABLE marker blocks the item - it is a claim held by nobody`` () =
    // A half-written lock must fail CLOSED. If it vanished, the item would read as free and a second worker
    // would be handed files somebody may be standing in.
    // A marker with no `worker=` key at all — truncated, hand-edited, or written by an older client.
    let unparseable =
        $"""{{"id":901,"body":"<!-- fsgg:claim lease=120 -->","updated_at":"%s{now}"}}"""

    let transport = scripted [ ok (comments [ unparseable ]) ]

    match claim transport 120 RefuseLiveHolder ignore me itsMe None aRef (fun () -> None) with
    | Ok BlockedByUnparseableMarker -> ()
    | other -> failwith $"an unparseable marker must block — got %A{other}"

    Assert.Equal(0, transport.Count "comment-post")

[<Fact>]
let ``re-claiming an item we ALREADY hold renews it in place and posts no second marker`` () =
    // Running the CAS again would post a SECOND marker of ours with a HIGHER id — which we would then lose
    // to our own first one, withdraw, and report as a loss on an item we hold. A worker would be told to
    // stop working on the thing it is holding the lock for. So a re-claim RENEWS the one marker we have (a
    // PATCH), never posts another — a `Renewed`, not a fresh `Won`.
    let transport =
        scripted
            [ ok (comments [ marker 901 "vole-418" "" ]) // 1. read: our own live marker
              ok """{"id":901}""" ] // 2. PATCH: renew the lease in place

    match claim transport 120 RefuseLiveHolder ignore me itsMe None aRef (fun () -> None) with
    | Ok(Renewed(held, _)) -> Assert.Equal(901L, held.MarkerId)
    | other -> failwith $"re-claiming our own live lock renews it in place — got %A{other}"

    // No second marker POSTed — the renew is a PATCH of the one we already hold.
    Assert.Equal(0, transport.Count "comment-post")
    Assert.True(transport.Logged "comment-patch FS-GG/FS.GG.SDD 901")

// ---- case 24 (a)/(b)/(l): a won claim COLLECTS the stale marker it claimed over --------------------

[<Fact>]
let ``a won claim COLLECTS a stale OTHER worker's marker and names the evicted worker`` () =
    // (a) A stale marker must be COLLECTED by the next claimant, never merely ignored. An ignored marker is
    // what `heartbeat` later resurrects underneath the new holder — two live markers, one item.
    let transport =
        scripted
            [ ok (comments [ staleClaimJson 810 "ghost-111" "" ]) // 1. read: a STALE claim by ghost-111
              ok """{"id":901}""" // 2. post ours (ghost-111's lease has lapsed, so nobody live blocks us)
              ok (comments [ staleClaimJson 810 "ghost-111" ""; marker 901 "vole-418" "" ]) // 3. re-read: we win
              ok "" ] // 4. DELETE ghost-111's stale marker

    match claim transport 120 RefuseLiveHolder ignore me itsMe None aRef (fun () -> None) with
    | Ok(Won(held, collected)) ->
        Assert.Equal(901L, held.MarkerId)
        Assert.Equal<WorkerId list>([ WorkerId "ghost-111" ], collected)
    | other -> failwith $"a claim over a stale marker wins and collects it — got %A{other}"

    // Exactly one marker survives: the stale one is gone.
    Assert.True(transport.Logged "comment-delete FS-GG/FS.GG.SDD 810")

[<Fact>]
let ``a claim renewing our OWN stale marker ends with ONE marker and reports no eviction`` () =
    // (b) Re-claiming when MY OWN marker went stale must renew a single marker, not mint a second — and it
    // is not an eviction to report, because you do not message yourself.
    let transport =
        scripted
            [ ok (comments [ staleClaimJson 811 "vole-418" "" ]) // 1. OUR OWN marker, gone stale
              ok """{"id":901}""" // 2. post a fresh one
              ok (comments [ staleClaimJson 811 "vole-418" ""; marker 901 "vole-418" "" ]) // 3. re-read: fresh wins
              ok "" ] // 4. DELETE our own superseded stale marker

    match claim transport 120 RefuseLiveHolder ignore me itsMe None aRef (fun () -> None) with
    | Ok(Won(held, collected)) ->
        Assert.Equal(901L, held.MarkerId)
        Assert.Empty(collected)
    | other -> failwith $"renewing our own stale marker is a win — got %A{other}"

    Assert.True(transport.Logged "comment-delete FS-GG/FS.GG.SDD 811")

[<Fact>]
let ``collecting a stale marker a peer already removed (404) is not fatal`` () =
    // (l) Two claimants collecting the SAME expired marker: the loser's DELETE 404s because the winner
    // already removed it. "Already gone" is the goal state of a collector, so the claim still wins.
    let transport =
        scripted
            [ ok (comments [ staleClaimJson 818 "ghost-444" "" ])
              ok """{"id":901}"""
              ok (comments [ staleClaimJson 818 "ghost-444" ""; marker 901 "vole-418" "" ])
              Error(NotFound "already gone") ] // 4. the winner's delete landed first

    match claim transport 120 RefuseLiveHolder ignore me itsMe None aRef (fun () -> None) with
    | Ok(Won(held, collected)) ->
        Assert.Equal(901L, held.MarkerId)
        Assert.Equal<WorkerId list>([ WorkerId "ghost-444" ], collected) // a 404 IS a successful collect
    | other -> failwith $"a benign 404 on collection is still a win — got %A{other}"

[<Fact>]
let ``a stale marker we could not delete is LEFT for reap, never a reason to fail a won claim`` () =
    // Collection is best-effort. A genuine (non-404) delete failure leaves the stale marker for `reap` and
    // is NOT reported as an eviction — but the claim we already won stands.
    let transport =
        scripted
            [ ok (comments [ staleClaimJson 810 "ghost-111" "" ])
              ok """{"id":901}"""
              ok (comments [ staleClaimJson 810 "ghost-111" ""; marker 901 "vole-418" "" ])
              Error(RateLimited(UnknownBudget, None)) ] // 4. DELETE faults — leave it for reap

    match claim transport 120 RefuseLiveHolder ignore me itsMe None aRef (fun () -> None) with
    | Ok(Won(held, collected)) ->
        Assert.Equal(901L, held.MarkerId)
        Assert.Empty(collected)
    | other -> failwith $"a failed collection does not fail a won claim — got %A{other}"

[<Fact>]
let ``a STALE unparseable marker is collected as debris but never notified (no worker to tell)`` () =
    // A live unparseable marker BLOCKS (fails closed); a merely STALE one is debris. Collecting it is fine,
    // but its `worker` is a sentinel — `say`ing to "unparsed-marker" would address no worker at all.
    let staleUnparseable =
        $"""{{"id":810,"body":"<!-- fsgg:claim lease=120 -->\nhalf-written","updated_at":"%s{stale}"}}"""

    let transport =
        scripted
            [ ok (comments [ staleUnparseable ]) // 1. a STALE marker with no parseable worker
              ok """{"id":901}""" // 2. post ours (the stale one does not block a live winner)
              ok (comments [ staleUnparseable; marker 901 "vole-418" "" ]) // 3. re-read: we win
              ok "" ] // 4. DELETE the stale debris

    match claim transport 120 RefuseLiveHolder ignore me itsMe None aRef (fun () -> None) with
    | Ok(Won(held, collected)) ->
        Assert.Equal(901L, held.MarkerId)
        Assert.Empty(collected) // deleted, but there is no worker to notify
    | other -> failwith $"a stale unparseable marker is debris, not a blocker on a won claim — got %A{other}"

    Assert.True(transport.Logged "comment-delete FS-GG/FS.GG.SDD 810")

// ---- #419: an id two workers share is not a lock ---------------------------------------------------

[<Fact>]
let ``#419 a live marker with OUR id but a DIFFERENT session is a TWIN - refused, not adopted`` () =
    // The regression #419 was filed on: the marker is ours by id, so the "already ours" branch adopted it
    // and the heartbeat renewed it — silently putting two workers on one item. When both sessions are known
    // and differ, it is a twin: refuse, and carry the OTHER session so the caller can name it.
    let transport = scripted [ ok (comments [ marker 901 "vole-418" " session=79b9e347" ]) ]

    match claim transport 120 RefuseLiveHolder ignore me itsMe (Some(SessionId "ed60050b")) aRef (fun () -> None) with
    | Ok(Twin(SessionId theirs)) -> Assert.Equal("79b9e347", theirs)
    | other -> failwith $"our id in another session is a twin, not a win — got %A{other}"

    // Refused BEFORE the CAS posts anything — the twin's marker is untouched.
    Assert.Equal(0, transport.Count "comment-post")

[<Fact>]
let ``#419 a SESSIONLESS marker with our id is genuinely ours - a heartbeat, not a twin`` () =
    // The boundary of the rule (#419 leg 4). A marker with no `session=` — a human, a harness exporting
    // none, any pre-#419 marker — is indistinguishable from ours. Failing closed on it would lock a worker
    // out of an item they really hold, so it keeps the old behaviour: ours.
    let transport =
        scripted [ ok (comments [ marker 901 "vole-418" "" ]); ok """{"id":901}""" ] // read, then PATCH the renew

    match claim transport 120 RefuseLiveHolder ignore me itsMe (Some(SessionId "ed60050b")) aRef (fun () -> None) with
    | Ok(Renewed(held, _)) -> Assert.Equal(901L, held.MarkerId)
    | other -> failwith $"a sessionless marker with our id must stay ours — got %A{other}"

[<Fact>]
let ``#419 the SAME session re-claiming its own marker is a heartbeat, not a twin`` () =
    // Without this, the refusal would fire on the worker itself — it could never renew its own claim.
    let transport =
        scripted [ ok (comments [ marker 901 "vole-418" " session=79b9e347" ]); ok """{"id":901}""" ] // read, then PATCH

    match claim transport 120 RefuseLiveHolder ignore me itsMe (Some(SessionId "79b9e347")) aRef (fun () -> None) with
    | Ok(Renewed(held, _)) -> Assert.Equal(901L, held.MarkerId)
    | other -> failwith $"our own session re-claiming is a heartbeat, not a twin — got %A{other}"

[<Fact>]
let ``a failed FIRST read is fatal, and nothing is posted`` () =
    // The only cheap place to fail, which is why the read comes first: we have posted nothing, so there is
    // no marker to clean up. Guessing the lock state from a failed read is the one thing a lock may never
    // do.
    let transport = scripted [ Error(RateLimited(UnknownBudget, None)) ]

    match claim transport 120 RefuseLiveHolder ignore me itsMe None aRef (fun () -> None) with
    | Error(RateLimited _) -> ()
    | other -> failwith $"a failed lock read must refuse — got %A{other}"

    Assert.Equal(0, transport.Count "comment-post")

// ---- #1620: `--force` STEALS A LIVE CLAIM -----------------------------------------------------------
//
// The defect these pin is not a missing feature — it is a DISAGREEMENT. `adopt`'s live-claim refusal and
// the usage block both instructed the operator to run `claim --force` to take a live claim, and `--force`
// was read in exactly one place: the caller's #516 one-item-per-worker pre-check. So it refused
// identically with and without the flag, and the org's only documented recovery route for a holder that
// died mid-item — one `reap` refuses (open `item/<n>-*` PR, #581) and `adopt` refuses (a live claim is not
// an orphan) — dead-ended. Twice in one day, from API 529 alone.
//
// The decision (maintainer, recorded on #1620) was to implement the advertised power rather than withdraw
// it, because a documented dead end is a standing invitation to invent an undocumented route: the two
// found and declined were impersonating the dead worker (`release --worker <them>`, which the twin
// predicate would have ACCEPTED) and faking staleness with a shrunken `--lease`.
//
// The pair below is the whole point. One board, two flags, two outcomes — a `--force` that does not change
// the answer is the defect itself, and it would now cost a red test rather than an afternoon.

[<Fact>]
let ``#1620 --force TAKES a live claim held by another worker, and names who it took it from`` () =
    let transport =
        scripted
            [ ok (comments [ marker 901 "kite-461" "" ]) // 1. read: kite-461 holds a LIVE lock
              ok """{"id":902}""" // 2. post replacement
              ok "" // 3. evict their marker
              ok (comments [ marker 902 "vole-418" "" ]) // 4. election read: the way is clear, we win
              ok (comments [ marker 902 "vole-418" "" ]) ] // 5. final census after cleanup

    match claim transport 120 StealLiveHolder ignore me itsMe None aRef (fun () -> None) with
    | Ok(Stolen(held, from, _, _)) ->
        Assert.Equal(902L, held.MarkerId)
        Assert.Equal(me, held.Worker)
        // WHO WE TOOK IT FROM IS PART OF THE OUTCOME, not a detail the caller reconstructs. It is what the
        // caller `say`s to the displaced worker and posts on the item — a steal nobody can see afterwards
        // is worse than a refusal, because the displaced worker is still running.
        Assert.Equal<WorkerId list>([ them ], from)
    | other -> failwith $"--force must take a live claim — got %A{other}"

    // THE HOLDER'S MARKER IS DELETED, not merely out-ordered. Leaving it would leave two live markers on
    // one item, and their `heartbeat` would keep renewing a lock we believe we hold.
    Assert.True(transport.Logged "comment-delete FS-GG/FS.GG.SDD 901")

[<Fact>]
let ``#1620 the SAME board WITHOUT --force still refuses - the flag is what makes the difference`` () =
    // The other half of the pair, and the assertion that actually failed before this landed: identical
    // output and exit code with and without the flag. `--force`'s advertised meaning ("take someone else's
    // item") and its implemented meaning ("hold two items") were different powers, and only the second
    // existed. If these two tests ever agree again, the flag has drifted back.
    let transport = scripted [ ok (comments [ marker 901 "kite-461" "" ]) ]

    match claim transport 120 RefuseLiveHolder ignore me itsMe None aRef (fun () -> None) with
    | Ok(Lost w) -> Assert.Equal(them, w)
    | other -> failwith $"without --force a live rival lock must be refused — got %A{other}"

    Assert.Equal(0, transport.Count "comment-delete")
    Assert.Equal(0, transport.Count "comment-post")

[<Fact>]
let ``#1620 a steal does NOT override a TWIN - a broken identity is not a contested item`` () =
    // #419/#1031 stay absolute under `--force`, and the reason is not squeamishness: the marker carries OUR
    // worker id, so forcing here deletes a lock a same-id sibling is actively working behind — and it is
    // reachable by accident, because every subagent of one Claude Code session derives the same id. The
    // remedy for a broken identity is a NEW identity, not a bigger hammer.
    let transport = scripted [ ok (comments [ marker 901 "vole-418" " session=79b9e347" ]) ]

    match claim transport 120 StealLiveHolder ignore me itsMe (Some(SessionId "ed60050b")) aRef (fun () -> None) with
    | Ok(Twin(SessionId theirs)) -> Assert.Equal("79b9e347", theirs)
    | other -> failwith $"--force must NOT steal from a twin — got %A{other}"

    Assert.Equal(0, transport.Count "comment-delete")
    Assert.Equal(0, transport.Count "comment-post")

[<Fact>]
let ``#1620 a steal does NOT override an UNPARSEABLE marker - a lock held by nobody still blocks`` () =
    // A half-written lock fails CLOSED, with or without the flag: `--force` takes an item from a WORKER,
    // and there is no worker here to take it from. `reap` owns this, and the item stays blocked until it
    // runs. Sitting BEHIND a parseable holder is the interesting case — evicting the holder would promote
    // a marker nobody can be held responsible for.
    let unparseable =
        $"""{{"id":902,"body":"<!-- fsgg:claim lease=120 -->","updated_at":"%s{now}"}}"""

    let transport = scripted [ ok (comments [ marker 901 "kite-461" ""; unparseable ]) ]

    match claim transport 120 StealLiveHolder ignore me itsMe None aRef (fun () -> None) with
    | Ok BlockedByUnparseableMarker -> ()
    | other -> failwith $"an unparseable lock blocks even under --force — got %A{other}"

    Assert.Equal(0, transport.Count "comment-delete")
    Assert.Equal(0, transport.Count "comment-post")

[<Fact>]
let ``#2772 failed cleanup retains the posted replacement and reports the standing incumbent`` () =
    // The replacement is posted before cleanup. A failed DELETE response is classified only after this
    // complete census proves both markers remain and the older incumbent is still authoritative.
    let transport =
        scripted
            [ ok (comments [ marker 901 "kite-461" "" ])
              ok """{"id":902}"""
              Error(Http(500, "delete failed"))
              ok (comments [ marker 901 "kite-461" ""; marker 902 "vole-418" "" ]) ]

    match claim transport 120 StealLiveHolder ignore me itsMe None aRef (fun () -> None) with
    | Ok(CleanupRequired(replacement, removed, failed, failedMarkerId, reason, censuses)) ->
        Assert.Equal(902L, replacement.MarkerId)
        Assert.Empty removed
        Assert.Equal(them, failed)
        Assert.Equal(901L, failedMarkerId)
        Assert.Contains("delete failed", reason)
        Assert.Equal(Some 901L, censuses.Before.WinnerMarkerId)
        Assert.Equal(Some 901L, censuses.After |> Option.bind _.WinnerMarkerId)
        Assert.Equal<int64 list>([ 901L; 902L ], censuses.After.Value.Markers |> List.map _.MarkerId)
    | other -> failwith $"failed cleanup must retain the replacement — got %A{other}"

    Assert.Equal(1, transport.Count "comment-post")

[<Fact>]
let ``#2772 retry reuses the retained replacement and completes cleanup without posting debris`` () =
    let transport =
        scripted
            [ ok (comments [ marker 901 "kite-461" ""; marker 902 "vole-418" "" ])
              ok ""
              ok (comments [ marker 902 "vole-418" "" ])
              ok (comments [ marker 902 "vole-418" "" ]) ]

    match claim transport 120 StealLiveHolder ignore me itsMe None aRef (fun () -> None) with
    | Ok(Stolen(replacement, removed, _, censuses)) ->
        Assert.Equal(902L, replacement.MarkerId)
        Assert.Equal<WorkerId list>([ them ], removed)
        Assert.Equal<int64 list>([ 901L; 902L ], censuses.Before.Markers |> List.map _.MarkerId)
        Assert.Equal<int64 list>([ 902L ], censuses.After.Value.Markers |> List.map _.MarkerId)
    | other -> failwith $"retry must reconcile the retained replacement — got %A{other}"
    Assert.Equal(0, transport.Count "comment-post")

[<Fact>]
let ``#2772 an ambiguous DELETE response is resolved from the complete census before classification`` () =
    let transport =
        scripted
            [ ok (comments [ marker 901 "kite-461" "" ])
              ok """{"id":902}"""
              Error(Http(503, "response lost"))
              ok (comments [ marker 902 "vole-418" "" ])
              ok (comments [ marker 902 "vole-418" "" ])
              ok (comments [ marker 902 "vole-418" "" ]) ]

    match claim transport 120 StealLiveHolder ignore me itsMe None aRef (fun () -> None) with
    | Ok(Stolen(replacement, removed, _, censuses)) ->
        Assert.Equal(902L, replacement.MarkerId)
        Assert.Equal<WorkerId list>([ them ], removed)
        Assert.Equal(Some 902L, censuses.After |> Option.bind _.WinnerMarkerId)
    | other -> failwith $"complete census must settle ambiguous delete — got %A{other}"

[<Fact>]
let ``#2772 a vanished replacement and surviving incumbent returns census-backed OldHolderStands`` () =
    let transport =
        scripted
            [ ok (comments [ marker 901 "kite-461" "" ])
              ok """{"id":902}"""
              Error(Http(503, "delete response lost"))
              ok (comments [ marker 901 "kite-461" "" ]) ]

    match claim transport 120 StealLiveHolder ignore me itsMe None aRef (fun () -> None) with
    | Ok(OldHolderStands(replacementMarkerId, holder, holderMarkerId, removed, censuses)) ->
        Assert.Equal(902L, replacementMarkerId)
        Assert.Equal(them, holder)
        Assert.Equal(901L, holderMarkerId)
        Assert.Empty removed
        Assert.Equal(Some 901L, censuses.Before.WinnerMarkerId)
        Assert.Equal(Some 901L, censuses.After |> Option.bind _.WinnerMarkerId)
    | other -> failwith $"a complete surviving-incumbent census must govern the result — got %A{other}"

[<Fact>]
let ``#2772 a readable empty post-census is a typed no-holder anomaly`` () =
    let transport =
        scripted
            [ ok (comments [ marker 901 "kite-461" "" ])
              ok """{"id":902}"""
              Error(Http(503, "delete response lost"))
              ok "[]" ]

    match claim transport 120 StealLiveHolder ignore me itsMe None aRef (fun () -> None) with
    | Ok(NoHolderRemaining(Some replacementMarkerId, removed, censuses)) ->
        Assert.Equal(902L, replacementMarkerId)
        Assert.Empty removed
        Assert.Equal(Some 901L, censuses.Before.WinnerMarkerId)
        Assert.Equal(None, censuses.After |> Option.bind _.WinnerMarkerId)
        Assert.Empty censuses.After.Value.Markers
    | other -> failwith $"a complete empty census must not collapse into a transport error — got %A{other}"

[<Fact>]
let ``#2772 an unreadable census after replacement POST failure returns no ownership verdict`` () =
    let transport =
        scripted
            [ ok (comments [ marker 901 "kite-461" "" ])
              Error(Http(500, "post failed"))
              Error(Http(503, "census failed")) ]

    match claim transport 120 StealLiveHolder ignore me itsMe None aRef (fun () -> None) with
    | Ok(PostStateUnreadable(None, removed, reason, censuses)) ->
        Assert.Empty removed
        Assert.Contains("replacement POST failed", reason)
        Assert.Contains("post failed", reason)
        Assert.Contains("census failed", reason)
        Assert.Equal(Some 901L, censuses.Before.WinnerMarkerId)
        Assert.True(Option.isNone censuses.After)
    | other -> failwith $"an unreadable post-failure census must authorize nothing — got %A{other}"

[<Fact>]
let ``#2772 a fresh winner after cleanup is distinct from an ordinary incumbent refusal`` () =
    let transport =
        scripted
            [ ok (comments [ marker 901 "kite-461" "" ])
              ok """{"id":903}"""
              ok ""
              ok (comments [ marker 902 "otter-77" ""; marker 903 "vole-418" "" ])
              ok ""
              ok (comments [ marker 902 "otter-77" "" ]) ]

    match claim transport 120 StealLiveHolder ignore me itsMe None aRef (fun () -> None) with
    | Ok(ForcedClaimLost(winner, censuses)) ->
        Assert.Equal(WorkerId "otter-77", winner)
        Assert.Equal(Some 901L, censuses.Before.WinnerMarkerId)
        Assert.Equal(Some 902L, censuses.After |> Option.bind _.WinnerMarkerId)
        Assert.Equal<int64 list>([ 902L ], censuses.After.Value.Markers |> List.map _.MarkerId)
    | other -> failwith $"a fresh post-cleanup winner needs a forced-transition result — got %A{other}"

[<Fact>]
let ``#1620 a steal that loses the FRESH race backs off cleanly - it does not force its way past`` () =
    // The steal clears the way and then races like everybody else; it is not a second lock protocol that
    // wins by fiat. A worker arriving AFTER the eviction and posting a lower id genuinely wins, and we
    // withdraw our own marker exactly as any loser does — the item transferred, just not to us.
    let transport =
        scripted
            [ ok (comments [ marker 901 "kite-461" "" ]) // 1. read: kite-461 holds it
              ok """{"id":903}""" // 2. post ours
              ok "" // 3. evict 901
              ok (comments [ marker 902 "otter-77" ""; marker 903 "vole-418" "" ]) // 4. a newcomer beat us
              ok "" // 5. withdraw ours
              ok (comments [ marker 902 "otter-77" "" ]) ] // 6. final census

    match claim transport 120 StealLiveHolder ignore me itsMe None aRef (fun () -> None) with
    | Ok(ForcedClaimLost(w, _)) -> Assert.Equal(WorkerId "otter-77", w)
    | other -> failwith $"a steal that loses the fresh race is a LOSS — got %A{other}"

    Assert.True(transport.Logged "comment-delete FS-GG/FS.GG.SDD 903")

[<Fact>]
let ``#1620 a steal still COLLECTS stale debris, and reports it apart from who it stole from`` () =
    // Two different facts, two different lists, and the caller owes a different message for each: a lapsed
    // lease being tidied up is not a working worker being displaced. Collapsing them would send the "your
    // expired claim was collected" courtesy to a worker whose claim had not expired at all.
    let transport =
        scripted
            [ ok (comments [ marker 901 "kite-461" ""; staleClaimJson 700 "ghost-111" "" ]) // live holder + stale debris
              ok """{"id":902}"""
              ok "" // evict the LIVE marker 901
              ok (comments [ staleClaimJson 700 "ghost-111" ""; marker 902 "vole-418" "" ])
              ok "" // collect the stale 700
              ok (comments [ marker 902 "vole-418" "" ]) ] // final census after stale cleanup

    match claim transport 120 StealLiveHolder ignore me itsMe None aRef (fun () -> None) with
    | Ok(Stolen(_, from, collected, censuses)) ->
        Assert.Equal<WorkerId list>([ them ], from)
        Assert.Equal<WorkerId list>([ WorkerId "ghost-111" ], collected)
        Assert.Equal<int64 list>([ 902L ], censuses.After.Value.Markers |> List.map _.MarkerId)
    | other -> failwith $"a steal collects stale debris too — got %A{other}"

    Assert.True(transport.Logged "comment-delete FS-GG/FS.GG.SDD 700")

// ---- #1620: THE EVICTION IS ANNOUNCED WHEN IT HAPPENS, NOT WHEN IT PAYS OFF ------------------------
//
// The eviction and the acquisition are two events, and only the first destroys anything. Reporting the
// theft through the `Stolen` OUTCOME alone would report it only when both succeeded — leaving the worker
// whose live lock we deleted uninformed on exactly the executions where the deletion bought us nothing.
// Replacement POST happens first. Confirmed deletions are announced even if final election later fails;
// a failed POST touches no incumbent and announces no theft.

[<Fact>]
let ``#2772 a replacement POST failure leaves the incumbent standing and announces no eviction`` () =
    let evicted = ResizeArray<WorkerId>()

    let transport =
        scripted
            [ ok (comments [ marker 901 "kite-461" "" ]) // read: kite-461 holds it
              Error(Http(500, "post failed"))
              ok (comments [ marker 901 "kite-461" "" ]) ] // authoritative post-failure census

    match claim transport 120 StealLiveHolder evicted.AddRange me itsMe None aRef (fun () -> None) with
    | Ok(ReplacementPostFailed(holder, holderMarkerId, reason, censuses)) ->
        Assert.Equal(them, holder)
        Assert.Equal(901L, holderMarkerId)
        Assert.Contains("post failed", reason)
        Assert.Equal(Some 901L, censuses.Before.WinnerMarkerId)
        Assert.Equal(Some 901L, censuses.After |> Option.bind _.WinnerMarkerId)
        Assert.Equal<int64 list>([ 901L ], censuses.After.Value.Markers |> List.map _.MarkerId)
    | other -> failwith $"a failed replacement post must prove the old holder stands — got %A{other}"

    Assert.Empty evicted
    Assert.Equal(0, transport.Count "comment-delete")

[<Fact>]
let ``#2772 a response-lost POST that stored the replacement reconciles old plus replacement`` () =
    let mutable exactDraft = ""
    let transport =
        scriptedSteps
            [ fun _ -> ok (comments [ marker 901 "kite-461" "" ])
              fun request ->
                  exactDraft <- postedCommentBody request
                  Error(Http(503, "POST response lost"))
              fun _ -> ok (comments [ marker 901 "kite-461" ""; markerWithExactBody 902 exactDraft ])
              fun _ -> ok "" // delete incumbent only after census discovers the exact replacement
              fun _ -> ok (comments [ markerWithExactBody 902 exactDraft ])
              fun _ -> ok (comments [ markerWithExactBody 902 exactDraft ]) ]

    match claim transport 120 StealLiveHolder ignore me itsMe None aRef (fun () -> None) with
    | Ok(Stolen(replacement, removed, _, censuses)) ->
        Assert.Equal(902L, replacement.MarkerId)
        Assert.Equal<WorkerId list>([ them ], removed)
        Assert.Equal<int64 list>([ 902L ], censuses.After.Value.Markers |> List.map _.MarkerId)
    | other -> failwith $"a landed ambiguous POST must reconcile through cleanup — got %A{other}"

    Assert.True(transport.Logged "comment-delete FS-GG/FS.GG.SDD 901")

[<Fact>]
let ``#2772 a response-lost POST whose replacement already wins reports ReplacementWon`` () =
    let mutable exactDraft = ""
    let transport =
        scriptedSteps
            [ fun _ -> ok (comments [ marker 901 "kite-461" "" ])
              fun request ->
                  exactDraft <- postedCommentBody request
                  Error(Http(503, "POST response lost"))
              fun _ -> ok (comments [ markerWithExactBody 902 exactDraft ])
              fun _ -> ok (comments [ markerWithExactBody 902 exactDraft ])
              fun _ -> ok (comments [ markerWithExactBody 902 exactDraft ]) ]

    match claim transport 120 StealLiveHolder ignore me itsMe None aRef (fun () -> None) with
    | Ok(ReplacementWon(replacement, collected, censuses)) ->
        Assert.Equal(902L, replacement.MarkerId)
        Assert.Empty collected
        Assert.Equal(Some 901L, censuses.Before.WinnerMarkerId)
        Assert.Equal(Some 902L, censuses.After |> Option.bind _.WinnerMarkerId)
        Assert.Equal<int64 list>([ 902L ], censuses.After.Value.Markers |> List.map _.MarkerId)
    | other -> failwith $"a landed replacement that already wins must not be called the old holder — got %A{other}"

    Assert.Equal(0, transport.Count "comment-delete")

[<Fact>]
let ``#2772 a response-lost POST with failed cleanup retains both markers for deterministic retry`` () =
    let mutable exactDraft = ""
    let transport =
        scriptedSteps
            [ fun _ -> ok (comments [ marker 901 "kite-461" "" ])
              fun request ->
                  exactDraft <- postedCommentBody request
                  Error(Http(503, "POST response lost"))
              fun _ -> ok (comments [ marker 901 "kite-461" ""; markerWithExactBody 902 exactDraft ])
              fun _ -> Error(Http(500, "cleanup failed"))
              fun _ -> ok (comments [ marker 901 "kite-461" ""; markerWithExactBody 902 exactDraft ]) ]

    match claim transport 120 StealLiveHolder ignore me itsMe None aRef (fun () -> None) with
    | Ok(CleanupRequired(replacement, removed, failed, failedMarkerId, reason, censuses)) ->
        Assert.Equal(902L, replacement.MarkerId)
        Assert.Empty removed
        Assert.Equal(them, failed)
        Assert.Equal(901L, failedMarkerId)
        Assert.Contains("cleanup failed", reason)
        Assert.Equal<int64 list>([ 901L; 902L ], censuses.After.Value.Markers |> List.map _.MarkerId)
    | other -> failwith $"ambiguous POST cleanup failure must retain its discovered replacement — got %A{other}"

[<Fact>]
let ``#2772 response-lost POST rejects a same-fields marker whose exact renewal token differs`` () =
    let mutable differentDraft = ""
    let transport =
        scriptedSteps
            [ fun _ -> ok (comments [ marker 901 "kite-461" "" ])
              fun request ->
                  differentDraft <-
                      postedCommentBody request
                      |> fun body -> System.Text.RegularExpressions.Regex.Replace(body, "renewed=[0-9]+", "renewed=0")
                  Error(Http(503, "POST response lost"))
              fun _ -> ok (comments [ marker 901 "kite-461" ""; markerWithExactBody 902 differentDraft ]) ]

    match claim transport 120 StealLiveHolder ignore me itsMe None aRef (fun () -> None) with
    | Ok(ReplacementPostFailed(holder, holderMarkerId, _, censuses)) ->
        Assert.Equal(them, holder)
        Assert.Equal(901L, holderMarkerId)
        Assert.Equal<int64 list>([ 901L; 902L ], censuses.After.Value.Markers |> List.map _.MarkerId)
    | other -> failwith $"a different request's marker must not authorize incumbent deletion — got %A{other}"

    Assert.Equal(0, transport.Count "comment-delete")

[<Fact>]
let ``#1620 a steal that LOSES the fresh race still announces the eviction it performed`` () =
    // We displaced kite-461 and then lost to a newcomer. We get nothing, but kite-461 has still lost its
    // lock, and it is still running — the notice is owed by the eviction, not by the acquisition.
    let evicted = ResizeArray<WorkerId>()

    let transport =
        scripted
            [ ok (comments [ marker 901 "kite-461" "" ])
              ok """{"id":903}"""
              ok "" // evict 901
              ok (comments [ marker 902 "otter-77" ""; marker 903 "vole-418" "" ]) // a newcomer beat us
              ok "" // withdraw ours
              ok (comments [ marker 902 "otter-77" "" ]) ] // final census

    match claim transport 120 StealLiveHolder evicted.AddRange me itsMe None aRef (fun () -> None) with
    | Ok(ForcedClaimLost(w, _)) -> Assert.Equal(WorkerId "otter-77", w)
    | other -> failwith $"a steal that loses the fresh race is a LOSS — got %A{other}"

    Assert.Equal<WorkerId list>([ them ], List.ofSeq evicted)

[<Fact>]
let ``#1620 a claim that evicts NOBODY never calls onEvict - no theft is reported that did not happen`` () =
    // The other side of the same rule. `--force` against an item that turns out to be FREE displaces
    // nobody, and the caller keys its "you displaced someone" message on this callback — so firing it
    // here would print a displacement that never occurred.
    let evicted = ResizeArray<WorkerId>()

    let transport =
        scripted [ ok "[]"; ok """{"id":901}"""; ok (comments [ marker 901 "vole-418" "" ]) ]

    match claim transport 120 StealLiveHolder evicted.AddRange me itsMe None aRef (fun () -> None) with
    | Ok(Won _) -> ()
    | other -> failwith $"--force on a free item is an ordinary win — got %A{other}"

    Assert.Empty evicted

[<Fact>]
let ``#1620 a steal does NOT evict past a live marker carrying OUR OWN id - the twin rule covers both slots`` () =
    // #419 BEHIND THE HOLDER, which is the position the winner-only guard misses. Left unguarded, the
    // eviction deletes kite-461's real live lock and then loses the re-read to our own twin's surviving
    // marker — a live lock destroyed, and nothing taken. Refuse before deleting anything.
    let transport =
        scripted
            [ ok (
                  comments
                      [ marker 901 "kite-461" "" // the live HOLDER
                        marker 902 "vole-418" " session=79b9e347" ] // our id, another session, queued behind
              ) ]

    match claim transport 120 StealLiveHolder ignore me itsMe (Some(SessionId "ed60050b")) aRef (fun () -> None) with
    | Ok(Twin(SessionId theirs)) -> Assert.Equal("79b9e347", theirs)
    | other -> failwith $"a twin behind the holder must refuse the steal — got %A{other}"

    Assert.Equal(0, transport.Count "comment-delete")
    Assert.Equal(0, transport.Count "comment-post")

[<Fact>]
let ``#1620 --force on a FREE item is an ordinary win - it reports no theft it did not commit`` () =
    // `take --force`-shaped invocations are common (a recovering worker forces because it does not know
    // whether the item is still held). When there is nobody to displace, the outcome must be `Won`: a
    // `Stolen` naming nobody would put a theft notice on an item nobody was thrown off.
    let transport =
        scripted [ ok "[]"; ok """{"id":901}"""; ok (comments [ marker 901 "vole-418" "" ]) ]

    match claim transport 120 StealLiveHolder ignore me itsMe None aRef (fun () -> None) with
    | Ok(Won(held, _)) -> Assert.Equal(901L, held.MarkerId)
    | other -> failwith $"--force on a free item is an ordinary win — got %A{other}"

    Assert.Equal(0, transport.Count "comment-delete")

[<Fact>]
let ``#1620 --force re-claiming our OWN live marker renews it - it does not steal from itself`` () =
    // A `take --force` retry must stay idempotent. Our own live marker reaches the renew path, not the
    // steal path, so it is PATCHed in place and no marker is deleted or posted.
    let transport =
        scripted [ ok (comments [ marker 901 "vole-418" "" ]); ok """{"id":901}""" ]

    match claim transport 120 StealLiveHolder ignore me itsMe None aRef (fun () -> None) with
    | Ok(Renewed(held, _)) -> Assert.Equal(901L, held.MarkerId)
    | other -> failwith $"--force must not steal from ourselves — got %A{other}"

    Assert.Equal(0, transport.Count "comment-post")
    Assert.Equal(0, transport.Count "comment-delete")

// ---- #481: the column a claim overwrote ------------------------------------------------------------

[<Fact>]
let ``#481 the claim RECORDS the column it overwrote, so release can put it back`` () =
    let transport =
        scripted [ ok "[]"; ok """{"id":901}"""; ok (comments [ marker 901 "vole-418" " prev=In%20review" ]) ]

    match claim transport 120 RefuseLiveHolder ignore me itsMe None aRef (fun () -> Some InReview) with
    | Ok(Won(held, _)) ->
        // The marker we POST carries `prev=`, percent-encoded — and the capability carries it forward, so
        // `release` restores the column somebody chose rather than guessing `Ready`.
        Assert.True(transport.Logged "comment-post")
        Assert.Equal(Some InReview, held.PreviousStatus)
    | other -> failwith $"the previous column must be recorded — got %A{other}"

[<Fact>]
let ``#481 a column NOBODY recorded is not restored - release says so rather than inventing one`` () =
    let transport =
        scripted [ ok "[]"; ok """{"id":901}"""; ok (comments [ marker 901 "vole-418" "" ]); ok "" ]

    match claim transport 120 RefuseLiveHolder ignore me itsMe None aRef (fun () -> None) with
    | Ok(Won(held, _)) ->
        match release transport held with
        | Ok None -> ()
        | other -> failwith $"a column nobody recorded cannot be restored — got %A{other}"
    | other -> failwith $"expected a win — got %A{other}"

// ---- #550: the marker is addressed by ID, never by worker string -----------------------------------

/// THE ONLY WAY TO GET A `Held`, AND THAT IS THE POINT.
///
/// This test assembly CANNOT fabricate a capability — `Held` has no public constructor, so even a test
/// cannot forge proof that a worker holds a lock. Every test below that needs one must go and get it the
/// way production code does: read the markers, and confirm the live winner is us.
///
/// A test suite that could mint the capability would be testing a different type from the one that ships.
let private acquire (transport: Fake.Recorder) =
    match verifyHeld transport 120 me itsMe None aRef with
    | Ok(Holds held) -> held
    | other -> failwith $"the fixture must actually hold the lock — got %A{other}"

[<Fact>]
let ``#550 heartbeat PATCHes the marker by its COMMENT ID, not by the worker string`` () =
    // The defect: `release` and `heartbeat` picked their marker by WORKER STRING alone, so a twin — the
    // same worker id in a different session — could delete or renew a lock it did not hold. The id is the
    // lock, and it is the only thing that identifies a marker uniquely.
    let transport =
        scripted [ ok (comments [ marker 901 "vole-418" " prev=In%20review" ]); ok "" ]

    let held = acquire transport

    match heartbeat transport 120 held with
    | Ok _ -> Assert.True(transport.Logged "comment-patch FS-GG/FS.GG.SDD 901")
    | Error e -> failwith $"the heartbeat should have landed — got %A{e}"

[<Fact>]
let ``#550 ...and it re-emits the column it overwrote, so a long-lived claim does not forget it`` () =
    // A PATCH rewrites the WHOLE body, so every field must be re-emitted from the capability. A claim that
    // had been beating for two hours used to forget the column it overwrote, because the rewrite did not
    // carry `prev=` forward and nothing had kept it — so the release that followed put back `Ready` over a
    // column somebody had deliberately chosen.
    let transport =
        scripted
            [ ok (comments [ marker 901 "vole-418" " prev=In%20review" ]) // acquire
              ok "" // the heartbeat's PATCH
              ok "" ] // the release's DELETE

    let held = acquire transport

    match heartbeat transport 120 held with
    | Ok beaten ->
        match release transport beaten with
        | Ok(Some InReview) -> ()
        | other -> failwith $"the heartbeat must not lose the previous column — got %A{other}"
    | Error e -> failwith $"the heartbeat should have landed — got %A{e}"

// ---- #1149: heartbeat must re-emit `session=`, or twin-detection dies after the first beat -----------
//
// Same family as #550 (heartbeat forgetting `prev=`), and worse: a PATCH rewrites the WHOLE body, so a
// field the capability does not hold is a field the rewrite drops. `heartbeat` passed `None` for the
// session, so the FIRST beat left a SESSIONLESS marker — and `twinSession` cannot tell a sessionless
// marker from a human's, so a same-id twin's `claim` then read `Renewed`, not `Twin`, and both workers
// held the item. The fix carries the session on the `Held` so the rewrite can re-emit it.

/// Like `scripted`, but it also CAPTURES every REST body it is sent, so a test can assert on the exact
/// bytes `heartbeat` PATCHed rather than on a round-trip proxy — the wire content is what #1149 is about.
let private capturing (responses: IoResult<Response> list) =
    let queue = System.Collections.Generic.Queue<IoResult<Response>>(responses)
    let bodies = System.Collections.Generic.List<string>()

    let recorder =
        Fake.Recorder(fun req ->
            match req.Body with
            | Json b -> bodies.Add b
            | _ -> ()

            if queue.Count = 0 then
                failwith "the transport was called more times than the test scripted"
            else
                queue.Dequeue())

    recorder, bodies

let private holdAs (session: string) (transport: Fake.Recorder) =
    match verifyHeld transport 120 me itsMe (Some(SessionId session)) aRef with
    | Ok(Holds held) -> held
    | other -> failwith $"the fixture must hold the lock as %s{session} — got %A{other}"

[<Fact>]
let ``#1149 heartbeat re-emits session= so the marker does not go SESSIONLESS`` () =
    let transport, bodies =
        capturing
            [ ok (comments [ marker 901 "vole-418" " session=S1" ]) // acquire, as session S1
              ok "" ] // the heartbeat's PATCH

    let held = holdAs "S1" transport

    match heartbeat transport 120 held with
    | Ok _ -> Assert.Contains("session=S1", Seq.last bodies) // the beaten body still carries the session
    | Error e -> failwith $"the heartbeat should have landed — got %A{e}"

[<Fact>]
let ``#2217 heartbeat changes the marker body so GitHub advances the server lease clock`` () =
    // A PATCH carrying the exact marker body GitHub already has is a no-op: `updated_at` stays at the
    // original claim time and the lease expires while heartbeat reports green.  The renewal token is the
    // deliberately unparsed changing field that turns this into a real server-side update.
    let transport, bodies =
        capturing
            [ ok (comments [ marker 901 "vole-418" " session=S1" ])
              ok "" ]

    let held = holdAs "S1" transport

    match heartbeat transport 120 held with
    | Ok _ -> Assert.Contains("renewed=", Seq.last bodies)
    | Error e -> failwith $"the heartbeat should have landed — got %A{e}"

[<Fact>]
let ``#2217 two heartbeats renew a lapsed server lease only when each PATCH changes its stored body`` () =
    // This is a small GitHub comment model, including the fact the live API matters for: an identical
    // PATCH is a no-op and leaves `updated_at` untouched.  Start with a capability acquired while live,
    // advance that stored server clock beyond the 120-minute window, then beat twice.  The final scan is
    // the lease fact the operator depends on, not merely a request-shape assertion.
    let mutable stored = "<!-- fsgg:claim worker=vole-418 lease=120 renewed=constant session=S1 -->"
    let mutable updatedAt = System.DateTimeOffset.UtcNow
    let patches = System.Collections.Generic.List<string>()

    let comments () =
        System.Text.Json.JsonSerializer.Serialize [ {| id = 901; body = stored; updated_at = updatedAt.ToString("o") |} ]

    let transport =
        Fake.Recorder(fun request ->
            match request.Method, request.Path with
            | "GET", "repos/FS-GG/FS.GG.SDD/issues/42/comments" -> ok (comments ())
            | "PATCH", "repos/FS-GG/FS.GG.SDD/issues/comments/901" ->
                match request.Body with
                | Json payload ->
                    use doc = System.Text.Json.JsonDocument.Parse payload
                    let body = doc.RootElement.GetProperty("body").GetString()
                    patches.Add body

                    // The controlled server behavior: a byte-identical PATCH does not advance the clock.
                    if body <> stored then
                        stored <- body
                        updatedAt <- System.DateTimeOffset.UtcNow

                    ok ""
                | _ -> failwith "heartbeat must PATCH a JSON comment body"
            | method', path -> failwith $"unexpected #2217 fixture request: %s{method'} %s{path}")

    let held = holdAs "S1" transport
    updatedAt <- System.DateTimeOffset.UtcNow.AddHours(-3.0) // the lease is now past its 120-minute window

    match heartbeat transport 120 held, heartbeat transport 120 held with
    | Ok _, Ok _ ->
        Assert.Equal(2, patches.Count)
        Assert.NotEqual<string>(patches.[0], patches.[1])

        match verifyHeld transport 120 me itsMe (Some(SessionId "S1")) aRef with
        | Ok(Holds _) -> () // changed bodies advanced `updated_at`, so the lapsed lease is live again
        | other -> failwith $"two real renewal PATCHes must revive the server lease — got %A{other}"
    | first, second -> failwith $"both heartbeats must land — got %A{first}, %A{second}"

[<Fact>]
let ``#1732 heartbeat re-emits marker path scope`` () =
    let transport, bodies =
        capturing
            [ ok (comments [ marker 901 "vole-418" " session=S1 pathRepo=FS.GG.Rendering" ])
              ok "" ]

    let held = holdAs "S1" transport

    match heartbeat transport 120 held with
    | Ok beaten ->
        Assert.Equal(Some "FS.GG.Rendering", beaten.PathRepo)
        Assert.Contains("pathRepo=FS.GG.Rendering", Seq.last bodies)
    | Error e -> failwith $"the heartbeat should preserve the path scope — got %A{e}"

[<Fact>]
let ``#1149 a twin's claim AFTER a heartbeat is refused Twin, not Renewed`` () =
    // The failure scenario, end to end and with the REAL heartbeat output. Worker A (S1) claims and beats
    // once; twin B (S2) — same shared-account id — claims the same item. Before the fix, A's beaten marker
    // was sessionless and B got `Renewed`, the double-hold ADR-0027's CAS exists to prevent.
    let transport, bodies =
        capturing
            [ ok (comments [ marker 901 "vole-418" " session=S1" ]) // A acquires as S1
              ok "" ] // A's heartbeat PATCH

    let held = holdAs "S1" transport

    match heartbeat transport 120 held with
    | Ok _ -> ()
    | Error e -> failwith $"the heartbeat should have landed — got %A{e}"

    // The marker as it stands on the issue after A's beat — the exact comment text A's heartbeat PATCHed,
    // lifted out of the `{"body": …}` payload it was sent in, not a hand-written stand-in. If the fix
    // regresses, this comment is sessionless and B is not refused.
    let beatenComment =
        System.Text.Json.JsonDocument.Parse(Seq.last bodies).RootElement.GetProperty("body").GetString()

    let beatenMarker = $"""{{"id":901,"body":"%s{beatenComment}","updated_at":"%s{now}"}}"""

    let twinTransport = scripted [ ok (comments [ beatenMarker ]) ]

    match claim twinTransport 120 RefuseLiveHolder ignore me itsMe (Some(SessionId "S2")) aRef (fun () -> None) with
    | Ok(Twin(SessionId theirs)) -> Assert.Equal("S1", theirs)
    | other -> failwith $"a twin claiming after a heartbeat must be refused Twin — got %A{other}"

// ---- #273 / #523: the touch-set ---------------------------------------------------------------------

[<Fact>]
let ``#273 an UNMATCHABLE token is refused - it would reserve nothing and read as disjoint`` () =
    // A token that can never match a file conflicts with NOTHING, so it reads as DISJOINT against every
    // other worker — a lock that succeeds under exactly the conditions it exists to prevent. It may not be
    // written to an issue body, and here it cannot be.
    match validate [ "**/Audio.fs" ] with
    | Error message ->
        Assert.Contains("reserve NOTHING", message)
        // THE REFUSAL MUST NAME WHAT WOULD HAVE BEEN ACCEPTED. One that does not merely moves the worker's
        // confusion one step later — and the grammar it quotes is the CORE's, not a second copy of it, so
        // the rule and the sentence explaining the rule cannot drift apart.
        Assert.Contains("supported:", message)
        Assert.Contains("spell the paths out", message)
    | Ok _ -> failwith "an unmatchable token must be refused"

[<Fact>]
let ``#273 a matchable touch-set validates`` () =
    match validate [ "src/Audio/**"; "tests/AudioTests.fs" ] with
    | Ok v -> Assert.Equal<string list>([ "src/Audio/**"; "tests/AudioTests.fs" ], v.Tokens)
    | Error e -> failwith $"a good touch-set must validate — got %s{e}"

[<Fact>]
let ``#1507 a FLAG-SHAPED token is refused at the write, even though the parser now stops before it`` () =
    // THE DECLARATION BOUNDARY. `widen <ref> --paths <five real paths> --json` corrupted a live claim, and
    // the parser is only half of why: this function is the ONE gate between a token and the PATCH, and it
    // had nothing to say. `--json` carries no glob metacharacter, so `TouchSet.classify` called it
    // `Matchable` and validation passed — the token then reserved no file at all, which is precisely the
    // #273 fail-open this very check exists to close, arriving through a token shaped like a path.
    //
    // Belt AND braces, deliberately. The parser fix means `widen`/`set-paths` can no longer PRODUCE this
    // input, so this test pins a path that should now be unreachable from the CLI. It stays because
    // `validate` is also the gate for a declaration typed into an issue body by hand, and because "the
    // caller is careful" is the assumption the original defect was built on.
    match validate [ "src/Audio/**"; "--json" ] with
    | Error message ->
        Assert.Contains("reserve NOTHING", message)
        // Name the offending token and ONLY it. The five real paths were fine; a refusal that blamed them
        // too would send the worker rewriting a declaration that was already correct.
        Assert.Contains("--json", message)
        // `src/Audio` appears nowhere in the refusal: not in the offending-token list, and not in the
        // grammar blurb the message quotes (which uses `src/Foo`). A trailing-period spelling of this
        // assertion would have been vacuous — it can never match regardless of which tokens were blamed.
        Assert.DoesNotContain("src/Audio", message)
    | Ok _ -> failwith "a flag must never validate as a touch-set token — this is how `--json` reached a live `Paths:` line"

[<Fact>]
let ``an EMPTY touch-set is refused - it reserves nothing, and 'none' is a different decision`` () =
    match validate [] with
    | Error _ -> ()
    | Ok _ -> failwith "an empty token list is not a touch-set"

// ---- #863: `widen --paths none` is how the SENTINEL gets declared ---------------------------------
// `Paths: none` is a DECISION (#496), and the refusal directly above tells the worker to make it:
// "Declare `Paths: none` if that is the decision". `widen --paths none` is how they would. The sentinel
// is not a path, so `TouchSet.classify` calls it `Unmatchable` — and the #273 check would therefore
// refuse it for "never matching a file", which is precisely what the sentinel is FOR. The tool would
// instruct the worker to do a thing and then refuse to do it, citing a rule that does not apply.

[<Fact>]
let ``#863 the 'none' SENTINEL validates — it is a decision, not an unmatchable path`` () =
    match validate [ "none" ] with
    | Ok v -> Assert.Equal<string list>([ "none" ], v.Tokens)
    | Error e -> failwith $"`widen --paths none` is how the sentinel is declared — got %s{e}"

[<Fact>]
let ``#863 the sentinel is CANONICALISED — 'none none' is the very body #863 is about`` () =
    // `rewrite` joins the tokens verbatim, so passing these through unreduced would emit
    // `Paths: none none` — #863's own reproduction, written by the tool that exists to repair it.
    match validate [ "none"; "NONE" ] with
    | Ok v -> Assert.Equal<string list>([ "none" ], v.Tokens)
    | Error e -> failwith $"a repeated sentinel is still the sentinel — got %s{e}"

[<Fact>]
let ``#863 a validated sentinel round-trips through rewrite to DeclaredNone`` () =
    // The two halves have to agree: what `widen` WRITES, `parse` must READ as the same decision.
    // Anything else and the tool emits a body its own parser disagrees with.
    let v = validate [ "none" ] |> Result.defaultWith failwith
    let body = (rewrite "An epic." v).Body
    Assert.Contains("Paths: none", body)
    Assert.Equal(DeclaredNone, FS.GG.Coord.TouchSet.parse body)

[<Fact>]
let ``#1103 the 'any' sentinel validates and canonicalises, distinct from 'none'`` () =
    match validate [ "any"; "ANY" ] with
    | Ok v -> Assert.Equal<string list>([ "any" ], v.Tokens)
    | Error e -> failwith $"`widen --paths any` declares a schedulable chore — got %s{e}"

[<Fact>]
let ``#1103 a validated 'any' round-trips through rewrite to DeclaredChore, NOT DeclaredNone`` () =
    // The whole of leg 8: what `widen --paths any` WRITES, `parse` must READ as the chore, never the
    // epic sentinel. Canonicalising 'any' to 'none' (as the pre-#1103 code did for every sentinel)
    // would silently turn a schedulable chore into an unschedulable epic.
    let v = validate [ "any" ] |> Result.defaultWith failwith
    let body = (rewrite "A file-less chore." v).Body
    Assert.Contains("Paths: any", body)
    Assert.Equal(DeclaredChore, FS.GG.Coord.TouchSet.parse body)

[<Fact>]
let ``#1103 mixing 'none' and 'any' is refused — they mean opposite things`` () =
    match validate [ "none"; "any" ] with
    | Error message -> Assert.Contains("opposite", message)
    | Ok _ -> failwith "'none' (unschedulable) and 'any' (schedulable) cannot both hold; must be refused"

[<Fact>]
let ``#863 'none' beside real paths is refused as a CONTRADICTION, naming the choice`` () =
    match validate [ "none"; "src/A/**" ] with
    | Error message ->
        // NOT the #273 "can never match a file" sentence: that reports `none` as a typo'd path and sends
        // the worker looking for the wrong mistake. The refusal has to name the actual decision.
        Assert.Contains("sentinel", message)
        Assert.Contains("src/A/**", message)
        Assert.DoesNotContain("reserve NOTHING", message)
    | Ok _ -> failwith "declaring 'none' alongside real paths is a contradiction and must be refused"

[<Fact>]
let ``#863 a real unmatchable token is STILL refused when the sentinel is not involved`` () =
    // The sentinel branch must not swallow #273's check on its way past.
    match validate [ "**/Audio.fs" ] with
    | Error message -> Assert.Contains("reserve NOTHING", message)
    | Ok _ -> failwith "an unmatchable token must still be refused"

[<Fact>]
let ``rewrite replaces the FIRST declaration and drops the rest`` () =
    // Two `Paths:` lines are an ambiguity, and an ambiguity in a reservation is two workers each reading
    // the one that suits them.
    let body = "Some prose\n\nPaths: src/old/**\n\nMore prose\n\nPaths: src/older/**\n"
    let v = validate [ "src/new/**" ] |> Result.defaultWith failwith
    let result = rewrite body v

    Assert.Contains("Paths: src/new/**", result.Body)
    Assert.DoesNotContain("src/old/**", result.Body)
    Assert.DoesNotContain("src/older/**", result.Body)

[<Fact>]
let ``rewrite is FENCE-AWARE - a Paths: inside a code block is PROSE, not a declaration`` () =
    // Rewriting it would corrupt documentation into a reservation — an EXAMPLE of a touch-set silently
    // becoming a real one, on somebody else's issue.
    let body = "Example:\n\n```\nPaths: src/example/**\n```\n\nPaths: src/real/**\n"
    let v = validate [ "src/new/**" ] |> Result.defaultWith failwith
    let result = rewrite body v

    Assert.Contains("Paths: src/example/**", result.Body) // the fenced one SURVIVES
    Assert.Contains("Paths: src/new/**", result.Body)
    Assert.DoesNotContain("src/real/**", result.Body)

[<Fact>]
let ``rewrite APPENDS when the body declared nothing - #496's omission is repairable`` () =
    let body = "An issue nobody gave a touch-set."
    let v = validate [ "src/new/**" ] |> Result.defaultWith failwith
    let result = rewrite body v

    Assert.Contains("An issue nobody gave a touch-set.", result.Body)
    Assert.Contains("Paths: src/new/**", result.Body)

// ---- #972: what `widen` WRITES, `take` must be able to READ -----------------------------------------

/// The declaration `rewrite` produced, as the SCHEDULER sees it — `TouchSet.parse` of the written body.
///
/// THIS IS THE ASSERTION THE SUITE WAS MISSING, and its absence is why three fence trackers drifted for
/// three months. Every test above asserts `Contains("Paths: …")` on the rewritten string, and a substring
/// is present whether or not it is inside a code block — so a declaration written INTO a fence passes them
/// all. `rewrite` and `TouchSet.parse` are the write and the read of one fact, and the only honest question
/// about them is whether they agree (#972, #485).
let private asScheduled (body: string) (tokens: string list) =
    let v = validate tokens |> Result.defaultWith failwith
    FS.GG.Coord.TouchSet.parse (rewrite body v).Body

[<Fact>]
let ``#972 a declaration appended below an UNTERMINATED fence is one the scheduler can see`` () =
    // MEASURED, before the fix: `widen` returned success and `TouchSet.parse` returned `Undeclared`. The
    // append landed inside the code block and the closer was written UNDERNEATH it — the close was there,
    // with a comment naming this exact hazard ("including, on the next pass, the declaration we just
    // wrote"), and it ran in the wrong order. The item then sat `Ready`, apparently declared, and never
    // scheduled; nothing anywhere reported a failure.
    match asScheduled "Some prose\n\n```\nunterminated code" [ "src/new/**" ] with
    | Declared tokens -> Assert.Equal<PathToken list>([ Matchable "src/new/**" ], tokens)
    | other -> failwithf "the scheduler cannot see the declaration widen reported writing: %A" other

[<Fact>]
let ``#972 an unterminated TILDE fence is closed with a tilde marker, not a backtick one`` () =
    // `rewrite` appended a literal "```" whatever the opener was, so a `~~~` fence stayed open and the
    // appended declaration stayed inside it.
    match asScheduled "Some prose\n\n~~~\nunterminated code" [ "src/new/**" ] with
    | Declared tokens -> Assert.Equal<PathToken list>([ Matchable "src/new/**" ], tokens)
    | other -> failwithf "a tilde fence was not repaired: %A" other

[<Fact>]
let ``#972 rewrite and TouchSet agree about a fence indented four spaces`` () =
    // `rewrite` used `^\s*` and called this a fence; `TouchSet` used `^ {0,3}` and did not. One body, two
    // rules, and `widen` wrote under one while `take` scheduled under the other. Four spaces is an indented
    // code block, so the `Paths:` line below is ordinary text and is the one that gets replaced.
    match asScheduled "    ```\nPaths: src/old/**" [ "src/new/**" ] with
    | Declared tokens -> Assert.Equal<PathToken list>([ Matchable "src/new/**" ], tokens)
    | other -> failwithf "rewrite and TouchSet still disagree: %A" other

[<Fact>]
let ``#972 a quoted declaration stays quoted, and the real one is what round-trips`` () =
    // The #277 rule, asserted through the READER rather than by substring: the fenced example survives
    // untouched AND the scheduler reads only the real declaration.
    let body = "Example:\n\n```\nPaths: src/example/**\n```\n\nPaths: src/real/**\n"

    match asScheduled body [ "src/new/**" ] with
    | Declared tokens -> Assert.Equal<PathToken list>([ Matchable "src/new/**" ], tokens)
    | other -> failwithf "expected the real declaration to round-trip: %A" other

// ---- #706: widen cannot be called without the lock --------------------------------------------------

[<Fact>]
let ``#706 widen takes the HELD claim - the ownership check is an ARGUMENT, not an if`` () =
    // The defect: `widen` never checked that the caller held the claim, so a worker rewrote a LIVE holder's
    // touch-set by accident — changing the reservation protecting the files somebody was standing in. #646
    // then proposed to keep it that way.
    //
    // THIS TEST CANNOT ASSERT THE ABSENCE OF A BUG THAT DOES NOT COMPILE. `widen` demands a `Held`, and
    // `Held` has no public constructor — the only doors to one are `claim` (win the CAS) and `verifyHeld`
    // (re-read and confirm). The line `widen transport aRef rewritten` is not a test that fails; it is a
    // program that does not build. What this test pins is that the capability is genuinely REQUIRED and
    // genuinely THREADED: the PATCH goes to the item the capability names, and to no other.
    let transport =
        scripted [ ok (comments [ marker 901 "vole-418" "" ]); ok "" ]

    let held = acquire transport
    let v = validate [ "src/new/**" ] |> Result.defaultWith failwith
    let rewritten = rewrite "Paths: src/old/**" v

    match widen transport held rewritten with
    | Ok() -> Assert.True(transport.Logged "issue-patch FS-GG/FS.GG.SDD 42")
    | Error e -> failwith $"a held widen must land — got %A{e}"

[<Fact>]
let ``#523 the PATCH cannot precede its own validation - the re-check PRODUCES what the write consumes`` () =
    // The defect: `widen` PATCHed the body and re-checked it AFTERWARDS, so on an exhausted budget the
    // declaration was already rewritten when the refusal arrived — a rejected widen that left a trace.
    //
    // The chain is now `validate -> Validated -> rewrite -> Rewritten -> widen`, and `widen` accepts
    // nothing else. A bad token cannot reach the PATCH, because it cannot produce the value the PATCH takes.
    // Here the refusal happens before the write is even representable, and NO request is sent.
    let transport = scripted []

    match validate [ "**/never-matches.fs" ] with
    | Error _ -> Assert.Equal(0, transport.RestCalls)
    | Ok _ -> failwith "an unmatchable token must not reach the PATCH"

// ---- verifyHeld fails closed ------------------------------------------------------------------------

[<Fact>]
let ``verifyHeld fails CLOSED - an unreadable marker set yields an error, never a capability`` () =
    // Manufacturing a capability from a failed read would be the fail-open this whole type exists to
    // prevent, sitting inside its own constructor. `DoesNotHold` says "we looked, and this worker does not
    // hold it" — which is a claim a failed read is not entitled to make.
    let transport = Fake.Recorder(fun _ -> Error(Malformed("FS.GG.SDD#42", "not JSON")))

    match verifyHeld transport 120 me itsMe None aRef with
    | Error(Malformed _) -> ()
    | other -> failwith $"a failed read must not mint a capability — got %A{other}"

[<Fact>]
let ``#1896 verifyHeld refuses an incomplete successful scan, never minting a capability`` () =
    let transport =
        scripted [ ok (comments [ marker 901 "vole-418" ""; unclassifiableComment 902 ]) ]

    match verifyHeld transport 120 me itsMe None aRef with
    | Error(Malformed(_, detail)) -> Assert.Contains("claim-marker scan is incomplete", detail)
    | other -> failwith $"a lower-bound marker list must not mint Held — got %A{other}"

[<Fact>]
let ``verifyHeld returns the capability when the live winner IS us`` () =
    let transport = Fake.Recorder(fun _ -> ok (comments [ marker 901 "vole-418" "" ]))

    match verifyHeld transport 120 me itsMe None aRef with
    | Ok(Holds held) -> Assert.Equal(901L, held.MarkerId)
    | other -> failwith $"we hold it — got %A{other}"

[<Fact>]
let ``verifyHeld does NOT hold when SOMEBODY ELSE holds it - and that is not a capability`` () =
    let transport = Fake.Recorder(fun _ -> ok (comments [ marker 901 "kite-461" "" ]))

    match verifyHeld transport 120 me itsMe None aRef with
    | Ok DoesNotHold -> ()
    | other -> failwith $"another worker's lock is not ours — got %A{other}"

// ---- verifyHeld matches on a SESSION predicate, not the worker id (#1031) ----------------------------

[<Fact>]
let ``#1031 verifyHeld REFUSES the capability over a TWIN's marker - our id, another session`` () =
    // THE GAP THIS CLOSES. `claim` has refused a twin since #419, and `release`/`heartbeat` scope to the
    // winning comment id (#550) — so the reachable path was already shut. But `verifyHeld` still matched on
    // the WORKER ID alone, and it is the only door to `Held`: a deliberately mis-targeted ref matched a
    // twin's marker here and was handed the capability that authorises PATCHing and DELETING it. The id was
    // the last place this invariant was asserted by convention rather than construction (#839 residual 2/4).
    let transport = Fake.Recorder(fun _ -> ok (comments [ marker 901 "vole-418" " session=79b9e347" ]))

    match verifyHeld transport 120 me itsMe (Some(SessionId "ed60050b")) aRef with
    | Ok(TwinHolds(SessionId theirs)) -> Assert.Equal("79b9e347", theirs)
    | other -> failwith $"our id in another session is a TWIN, not us — got %A{other}"

[<Fact>]
let ``#1031 a twin is a case of its OWN - collapsing it into DoesNotHold would misdiagnose the lease`` () =
    // WHY IT IS NOT `DoesNotHold`. A caller handed `DoesNotHold` re-reads the markers to say WHY, keys on
    // the worker id, finds OUR id on the live winner — and concludes the only other thing that fits: "your
    // lease expired, re-claim it". That is advice to go take a lock a twin is working behind. The outcome
    // has to carry the twin, because no id-keyed question downstream can recover it.
    let transport = Fake.Recorder(fun _ -> ok (comments [ marker 901 "vole-418" " session=79b9e347" ]))

    match verifyHeld transport 120 me itsMe (Some(SessionId "ed60050b")) aRef with
    | Ok DoesNotHold -> failwith "a twin must not be reported as a plain non-hold — the caller cannot tell"
    | Ok(TwinHolds _) -> ()
    | other -> failwith $"expected a twin — got %A{other}"

[<Fact>]
let ``#1031 a SESSIONLESS marker with our id still verifies - the boundary of the rule`` () =
    // The same boundary `claim` draws (#419 leg 4), and it must be drawn identically or the tool refuses you
    // the lock in one verb and hands it to you in another. A marker with no `session=` — a human, a harness
    // exporting none, any pre-#419 marker — is indistinguishable from ours. Failing closed here would lock a
    // worker out of an item they really hold, and `release`/`heartbeat` would break for every such marker.
    let transport = Fake.Recorder(fun _ -> ok (comments [ marker 901 "vole-418" "" ]))

    match verifyHeld transport 120 me itsMe (Some(SessionId "ed60050b")) aRef with
    | Ok(Holds held) -> Assert.Equal(901L, held.MarkerId)
    | other -> failwith $"a sessionless marker with our id must stay ours — got %A{other}"

[<Fact>]
let ``#1031 our OWN session verifies its own marker - or no worker could ever renew`` () =
    let transport = Fake.Recorder(fun _ -> ok (comments [ marker 901 "vole-418" " session=79b9e347" ]))

    match verifyHeld transport 120 me itsMe (Some(SessionId "79b9e347")) aRef with
    | Ok(Holds held) -> Assert.Equal(901L, held.MarkerId)
    | other -> failwith $"our own session must verify its own lock — got %A{other}"

[<Fact>]
let ``#1031 a SESSIONLESS caller keeps the old behaviour over a marker that carries one`` () =
    // The other half of "both sessions must be known": a worker whose own session is unknown cannot call
    // anything a twin, because it has nothing to compare. `claim` treats this as ours; so must this.
    let transport = Fake.Recorder(fun _ -> ok (comments [ marker 901 "vole-418" " session=79b9e347" ]))

    match verifyHeld transport 120 me itsMe None aRef with
    | Ok(Holds held) -> Assert.Equal(901L, held.MarkerId)
    | other -> failwith $"a caller with no session of its own cannot conclude twin — got %A{other}"

// ---- #1646: a SESSION IS NOT A CREDENTIAL -----------------------------------------------------------
//
// These sit beside the #1031 legs above deliberately, and the two blocks together are one statement.
//
// #1031 pinned: OUR ID, ANOTHER SESSION ⇒ refused. That is a shared IDENTITY, and the session is what tells
// the two apart. These pin the case the session CANNOT reach: our id, THEIR session — because it is the
// same session. Every subagent of one Claude Code session shares one session id (`whoami` says so), so for
// the fan-out this board actually runs, the second factor #1031 added is one every sibling also holds:
// `twinSession` concludes twin only when both sessions are KNOWN and DIFFER, and equal sessions returned
// `None`, which `verifyHeld` read as "ours".
//
// So a caller that names another worker's id — copied straight off the board — matched the id leg because
// it was copied, and matched the session leg because it is a sibling. Both legs, and the door to the
// capability that authorises PATCHing and DELETING that marker.
//
// The third fact is the one `--worker` cannot restate: who this PROCESS is with the flag taken away.

/// A caller whose own resolved identity is `kite-461` — the sibling in the fan-out, asking to act as
/// `vole-418`. It is the ONLY thing that differs from the passing legs above.
let private impersonator = Derives them

[<Fact>]
let ``#1646 verifyHeld REFUSES the capability when the caller NAMES another worker whose marker is live`` () =
    // THE HOLE, AT ITS OWN LEVEL. `me` (vole-418) holds the live marker; the caller is `kite-461` and has
    // passed `--worker vole-418`. Sessions are IDENTICAL, so #1031's predicate has nothing to say.
    let transport = Fake.Recorder(fun _ -> ok (comments [ marker 901 "vole-418" " session=624f304a" ]))

    match verifyHeld transport 120 me impersonator (Some(SessionId "624f304a")) aRef with
    | Ok(ImpersonatesHolder(derived, named)) ->
        Assert.Equal(them, derived)
        Assert.Equal(me, named)
    | other -> failwith $"naming another worker's live marker must not open the door to Held — got %A{other}"

[<Fact>]
let ``#1646 the session is not what decides it - a DIFFERING session reaches the same refusal, by the new rule`` () =
    // THE PAIR THAT MAKES THE POINT, and its name is the point. Change ONE thing about the leg above — the
    // sessions now differ — and the caller is refused either way. Before #1646 this configuration was the
    // ONLY one refused, which is why the hole was invisible: the protocol demonstrably rejected this argv
    // shape, just never in the configuration the fleet actually runs in.
    //
    // IT IS NOT `TwinHolds`, AND THAT IS DELIBERATE — the ordering, stated. A twin means OUR id is shared.
    // Our id is `kite-461`; the marker says `vole-418`. Nobody shares anything with us: we typed somebody
    // else's id, and they have a twin. Reporting `TwinHolds` would tell this caller to `whoami --mint` over
    // an identity collision it does not have, and #1031's own case doc is the argument — an outcome that
    // sends the reader to the wrong remedy is the defect that case exists to prevent.
    let transport = Fake.Recorder(fun _ -> ok (comments [ marker 901 "vole-418" " session=79b9e347" ]))

    match verifyHeld transport 120 me impersonator (Some(SessionId "ed60050b")) aRef with
    | Ok(TwinHolds _) -> failwith "we do not share an id with anybody — `whoami --mint` is the wrong remedy here"
    | Ok(ImpersonatesHolder _) -> ()
    | other -> failwith $"a differing session must still refuse — got %A{other}"

[<Fact>]
let ``#1646 it is NOT DoesNotHold - a typo and an impersonation need different messages`` () =
    // WHY IT IS A CASE OF ITS OWN, and it is `TwinHolds`'s reason one step further out. `DoesNotHold` sends
    // the caller to the heartbeat diagnosis, which re-reads the markers, keys on the id it was GIVEN, finds
    // that id on the live winner and reports "your lease EXPIRED, re-claim it" — about somebody else's
    // lease, as though it were ours. And `TwinHolds` would be worse: it prescribes `whoami --mint` for an
    // identity collision this caller does not have. Neither remedy is the one that fits.
    let transport = Fake.Recorder(fun _ -> ok (comments [ marker 901 "vole-418" " session=624f304a" ]))

    match verifyHeld transport 120 me impersonator (Some(SessionId "624f304a")) aRef with
    | Ok DoesNotHold -> failwith "an impersonation reported as a plain non-hold sends the caller to 'your lease expired'"
    | Ok(TwinHolds _) -> failwith "this is not a shared id — prescribing `whoami --mint` would be the wrong remedy"
    | Ok(ImpersonatesHolder _) -> ()
    | other -> failwith $"expected the impersonation refusal — got %A{other}"

[<Fact>]
let ``#1646 a MISTYPED --worker is not an accusation - an id that holds nothing stays DoesNotHold`` () =
    // THE COMMONER MISTAKE, AND THE BOUNDARY OF THE ACCUSATION. `kite-461` typed `--worker vole-418`, and
    // vole-418 holds NOTHING here — the live marker is somebody else's entirely. There is no lock to take,
    // so there is nothing to accuse anybody of: this is a flag to re-check, and the caller must be told so
    // rather than told it is impersonating. The refusal fires only where the named id owns the live lock.
    let transport = Fake.Recorder(fun _ -> ok (comments [ marker 901 "smew-f31" " session=624f304a" ]))

    match verifyHeld transport 120 me impersonator (Some(SessionId "624f304a")) aRef with
    | Ok DoesNotHold -> ()
    | other -> failwith $"a named id that holds nothing is a typo, not an impersonation — got %A{other}"

[<Fact>]
let ``#1646 a worker still operates its OWN claim across processes - including after a heartbeat rewrote the marker`` () =
    // THE REGRESSION THIS MUST NOT CAUSE, and it is `verifyHeld`'s whole reason for existing: every command
    // after `claim` is a fresh process, so the capability has to survive one. Here the marker was written by
    // an EARLIER process of the same worker — and `heartbeat` rewrites the whole body (#1149), so this is
    // the post-heartbeat marker, session and all. Same id, same self, and it still verifies.
    let transport = Fake.Recorder(fun _ -> ok (comments [ marker 901 "vole-418" " session=624f304a prev=Ready" ]))

    match verifyHeld transport 120 me itsMe (Some(SessionId "624f304a")) aRef with
    | Ok(Holds held) ->
        Assert.Equal(901L, held.MarkerId)
        Assert.Equal(Some Ready, held.PreviousStatus)
    | other -> failwith $"a worker must keep operating its own claim across processes — got %A{other}"

[<Fact>]
let ``#1646 a SESSIONLESS marker with our id still verifies - the #1031 boundary must not regress`` () =
    // #1031 leg 3, re-asserted against the NEW predicate. A marker with no `session=` — a human, a harness
    // exporting none, any pre-#419 marker — is indistinguishable from ours, and failing closed on it would
    // lock a worker out of an item they really hold. The new question is about the CALLER's own id, not the
    // marker's session, so it must leave this case exactly where it found it.
    let transport = Fake.Recorder(fun _ -> ok (comments [ marker 901 "vole-418" "" ]))

    match verifyHeld transport 120 me itsMe (Some(SessionId "624f304a")) aRef with
    | Ok(Holds held) -> Assert.Equal(901L, held.MarkerId)
    | other -> failwith $"a sessionless marker with our id must stay ours — got %A{other}"

[<Fact>]
let ``#1646 a caller that derives NOTHING is not refused - the human operator --worker exists for`` () =
    // THE COST, STATED. `--worker` has legitimate callers that resolve no identity of their own: a human
    // operator, a harness exporting no session and setting no `$FSGG_WORKER`. They have nothing to be
    // measured against, so the question is UNASKABLE — and answering an unaskable question "no" would lock
    // out exactly the callers the flag exists for, which is #1031's boundary reached from one fact further
    // out. #1646 records this as residue rather than a clean close: a caller that unsets its own identity
    // before impersonating lands here too, and the tool cannot tell it from the operator.
    let transport = Fake.Recorder(fun _ -> ok (comments [ marker 901 "vole-418" " session=624f304a" ]))

    match verifyHeld transport 120 me DerivesNothing (Some(SessionId "624f304a")) aRef with
    | Ok(Holds held) -> Assert.Equal(901L, held.MarkerId)
    | other -> failwith $"a caller with no derived identity must keep the pre-#1646 behaviour — got %A{other}"

[<Fact>]
let ``#1646 the --force STEAL under a foreign id is refused - and it is the sharpest of the four`` () =
    // FOUND BY REVIEW, AFTER THE REFUSAL FIRST WENT IN ON `claim`'s RE-CLAIM ARM ALONE. The reasoning that
    // left this open was that the other arms CREATE a marker rather than adopt one, and creating a lock
    // under somebody else's name is a different act from taking theirs. `--force` is where that stops being
    // true — it deletes the holder's live marker on the way. Measured on this tree before the fix:
    //
    //   $ FSGG_WORKER=kite-461 fsgg-coord-engine claim FS.GG.SDD#42 --force --worker smew-f31
    //   STOLE FS.GG.SDD#42 from worker 'vole-418' (--force)                              rc=0
    //   <!-- fsgg:msg from=smew-f31 to=vole-418 -->
    //
    // `kite-461` destroyed `vole-418`'s lock and #1620's notice — the thing that makes a steal accountable —
    // was signed by `smew-f31`, who did nothing. So this does not merely BYPASS the accounting the way
    // `release --worker` does; it FALSIFIES it, in the only surviving record of a lock that no longer exists.
    //
    // ONE TRANSPORT RESPONSE IS SCRIPTED, AND THAT IS THE ASSERTION. `scripted` fails the moment it is
    // called a second time, so a refusal that read the markers first — let alone one that got as far as the
    // eviction DELETE — reds here. The refusal has to precede the read, and it does.
    // A THIRD party, because that is what makes the false attribution visible: the caller is `kite-461`, the
    // beneficiary it names is `smew-f31`, and the worker whose lock would be destroyed is a fourth. Naming
    // the caller's OWN id here would be an ordinary sanctioned steal and prove nothing.
    let beneficiary = WorkerId "smew-f31"
    let transport = scripted []

    match claim transport 120 StealLiveHolder ignore beneficiary impersonator (Some(SessionId "624f304a")) aRef (fun () -> None) with
    | Ok(Impersonates(derived, named)) ->
        Assert.Equal(them, derived)
        Assert.Equal(beneficiary, named)
        Assert.Equal(0, transport.RestCalls)
    | other -> failwith $"a --force steal under a foreign id must be refused before anything is read — got %A{other}"

[<Fact>]
let ``#1646 a FRESH claim under a foreign id is refused - a lock nobody can drop is not a lock`` () =
    // THE THIRD ARM, and it is refused for a reason of its own. `claim --worker <them>` on a FREE item posted
    // a marker in their name and reported success — and then its own creator could not heartbeat or release
    // it, because every verb that would operate it goes through `verifyHeld` and is refused. A live
    // reservation, under a name that never asked for it, that stands until the lease lapses because the only
    // worker who could drop it does not know it exists.
    //
    // `reap` cannot collect it (the claim is not stale) and `release` refuses it (we are not them), so
    // "recoverable by the ordinary path" was not true of it either.
    let transport = scripted []

    match claim transport 120 RefuseLiveHolder ignore me impersonator None aRef (fun () -> None) with
    | Ok(Impersonates(derived, named)) ->
        Assert.Equal(them, derived)
        Assert.Equal(me, named)
        Assert.Equal(0, transport.RestCalls)
    | other -> failwith $"a fresh claim under a foreign id must be refused — got %A{other}"

[<Fact>]
let ``#1646 claim is the OTHER door and it refuses too - a re-claim under a foreign id is not a renewal`` () =
    // `Held` HAS TWO DOORS (`Writes.fsi`: "the only ways to hold one are to win the CAS or to re-read"), and
    // closing one is closing half. `claim`'s re-claim arm hands back a `Renewed` over a marker it did not
    // create, on the strength of the id alone — and it PATCHes that marker, so `claim --worker <them>`
    // renewed a live holder's lease and reported the item held. Measured on this tree before the fix:
    //   $ FSGG_WORKER=kite-461 fsgg-coord-engine claim FS.GG.SDD#44 --worker vole-418
    //   held FS.GG.SDD#44 by worker vole-418 (lease renewed; lock held; ...)   rc=0
    //
    // ONE TRANSPORT RESPONSE IS SCRIPTED, and that is an assertion: `scripted` fails the moment it is called
    // a second time, so the refusal must happen on the READ, before the PATCH. A refusal that renewed the
    // lease first would be no refusal at all.
    let transport = scripted [ ok (comments [ marker 901 "vole-418" " session=624f304a" ]) ]

    match claim transport 120 RefuseLiveHolder ignore me impersonator (Some(SessionId "624f304a")) aRef (fun () -> None) with
    | Ok(Impersonates(derived, named)) ->
        Assert.Equal(them, derived)
        Assert.Equal(me, named)
    | other -> failwith $"`claim --worker <them>` must not renew their lease — got %A{other}"

[<Fact>]
let ``#1646 the two doors agree - claim and verifyHeld answer the SAME question the same way`` () =
    // `twinSession`'s rule, restated for the new predicate: "a `claim` that calls a marker a twin and a
    // `verifyHeld` that calls the same marker ours would mean the tool refuses you the lock and then
    // authorises you to delete it." Both now route through one `impersonated`, and this drives BOTH over the
    // identical marker set to pin that they cannot drift.
    let markerSet = comments [ marker 901 "vole-418" " session=624f304a" ]
    let forClaim = scripted [ ok markerSet ]
    let forVerify = Fake.Recorder(fun _ -> ok markerSet)

    let claimSaidNo =
        match claim forClaim 120 RefuseLiveHolder ignore me impersonator (Some(SessionId "624f304a")) aRef (fun () -> None) with
        | Ok(Impersonates _) -> true
        | _ -> false

    let verifySaidNo =
        match verifyHeld forVerify 120 me impersonator (Some(SessionId "624f304a")) aRef with
        | Ok(ImpersonatesHolder _) -> true
        | _ -> false

    let agreed = claimSaidNo = verifySaidNo

    Assert.True(
        agreed,
        $"the two doors to Held disagreed: claim refused = %b{claimSaidNo}, verifyHeld refused = %b{verifySaidNo}"
    )

    Assert.True(claimSaidNo, "both doors must refuse the impersonation")

// ---- child: the id is a NUMBER ----------------------------------------------------------------------

[<Fact>]
let ``#507 child POSTs the REST id as a JSON NUMBER, not a string`` () =
    // `gh api -f sub_issue_id=1047` sent it as a quoted string and collected a 422; `-F` sent it as a
    // number. And it is the child's REST INTEGER ID, never its number — two repos can each have an issue
    // #7, and posting a number where an id belongs attaches the wrong issue silently.
    let transport = Fake.Recorder(fun _ -> ok "{}")

    match child transport aRef 1047L with
    | Ok() -> Assert.True(transport.Logged "sub-issue-add FS-GG/FS.GG.SDD 42 -F sub_issue_id=1047")
    | Error e -> failwith $"the child should have been attached — got %A{e}"

// ---- say needs no lock ------------------------------------------------------------------------------

[<Fact>]
let ``say does NOT require the lock - the worker who lost the race must still be able to speak`` () =
    // Gating a message on the lock would silence exactly the worker with something urgent to say: the one
    // who just lost the race, or the one warning the holder that their touch-sets overlap.
    let transport = Fake.Recorder(fun _ -> ok """{"id":950}""")

    match say transport me them aRef "our touch-sets overlap on src/Audio" with
    | Ok() -> Assert.True(transport.Logged "comment-post FS-GG/FS.GG.SDD 42")
    | Error e -> failwith $"a message needs no lock — got %A{e}"

[<Fact>]
let ``a follow-up disposition is a durable issue comment, not a worker mailbox message`` () =
    let transport = Fake.Recorder(fun _ -> ok """{"id":950}""")

    match followupDisposition transport aRef me "deferred: driver will resurface this open item" with
    | Ok() -> Assert.True(transport.Logged "comment-post FS-GG/FS.GG.SDD 42")
    | Error e -> failwith $"the disposition must be recorded on the owed issue — got %A{e}"

// ---- reap: an expired lease is EVIDENCE of abandonment, not PROOF (#581) ----------------------------

let private staleMarker =
    { Reads.Id = 880L
      Reads.Worker = WorkerId "ghost-222"
      Reads.Session = None
      Reads.AgeSeconds = 10800 // 3h — well past a 120-minute lease
      Reads.PreviousStatus = None
      Reads.PathRepo = None
      Reads.AgentContract = None
      Reads.Raw = "<!-- fsgg:claim worker=ghost-222 lease=120 -->" }

[<Fact>]
let ``#581 reapable is GREEN only when the lease lapsed AND no PR is open`` () =
    // The single door to the capability `reap` consumes. `LeaseExpiredNoPr` is the one Liveness that
    // licenses a break: the lease lapsed and we LOOKED for the item's PR and found none.
    match reapable aRef staleMarker LeaseExpiredNoPr with
    | Ok r ->
        Assert.Equal(880L, r.MarkerId)
        Assert.Equal("ghost-222", r.Worker.Value)
        Assert.Equal(42, r.Ref.Number)
    | Error e -> failwith $"a lapsed lease with no open PR is reapable — got %A{e}"

[<Fact>]
let ``#581 reapable REFUSES a claim whose item PR is open - the work is alive, not abandoned`` () =
    // The leg that reaped live work twice: the lease lapsed but an `item/<n>-*` PR is open, so the work is
    // demonstrably still happening. The refusal names the PR so it is checkable.
    match reapable aRef staleMarker (LeaseExpiredPrOpen 433) with
    | Error(WorkAlive pr) -> Assert.Equal(433, pr)
    | other -> failwith $"an open PR must block the reap — got %A{other}"

[<Fact>]
let ``#1055 reapable REFUSES a claim whose branch is pushed but has no PR - work in progress`` () =
    // §5 opens the PR only after the work, so a pushed `item/<n>-*` branch with no PR is a worker mid-flight
    // (#1055). Refuse — and with its OWN case, WorkAliveBranch, NOT Undetermined: we DID tell (a branch is
    // pushed), and collapsing "the work is alive" into "could not tell" is the #581 mistake this gate exists
    // to prevent.
    match reapable aRef staleMarker LeaseExpiredBranchPushed with
    | Error WorkAliveBranch -> ()
    | other -> failwith $"a pushed branch with no PR must block the reap, distinctly — got %A{other}"

[<Fact>]
let ``#581 reapable FAILS CLOSED when liveness is unknown - a lock we cannot rule dead we may not break`` () =
    // "We could not ask" is NOT "there is no PR". A transient read failure must not become a reaped claim.
    match reapable aRef staleMarker LivenessUnknown with
    | Error(Undetermined _) -> ()
    | other -> failwith $"an unreadable liveness must refuse the reap — got %A{other}"

[<Fact>]
let ``#581 reapable refuses a lease that is not even expired`` () =
    // A live lease should never have reached here; reap it and the whole gate is a lie. Refuse rather than
    // manufacture a capability out of a held lock.
    match reapable aRef staleMarker LeaseHeld with
    | Error(Undetermined _) -> ()
    | other -> failwith $"a held lease is not reapable — got %A{other}"

[<Fact>]
let ``#581 reap RE-VERIFIES the marker is still stale, then DELETES it by its comment id`` () =
    // The lock IS the comment id: reap addresses the marker by id, never by worker string (a twin's id
    // would name the wrong comment, #550 one command over). A 404 would be success too, but here it lands.
    // It re-reads the markers first — `Reapable` was proven against a SNAPSHOT — and only deletes because
    // the marker is STILL stale on the fresh read.
    let transport =
        scripted
            [ ok (comments [ staleClaimJson 880 "ghost-222" "" ]) // 1. re-verify: still stale
              ok "" ] // 2. DELETE lands

    match reapable aRef staleMarker LeaseExpiredNoPr with
    | Error e -> failwith $"the fixture marker is reapable — got %A{e}"
    | Ok r ->
        match reap transport 120 r with
        | Ok Reaped -> Assert.True(transport.Logged "comment-delete FS-GG/FS.GG.SDD 880")
        | other -> failwith $"the reap should have deleted the marker — got %A{other}"

[<Fact>]
let ``#1896 reap refuses an incomplete re-verification and deletes no marker`` () =
    let transport =
        scripted [ ok (comments [ staleClaimJson 880 "ghost-222" ""; unclassifiableComment 902 ]) ]

    match reapable aRef staleMarker LeaseExpiredNoPr with
    | Error e -> failwith $"the fixture marker is reapable — got %A{e}"
    | Ok r ->
        match reap transport 120 r with
        | Error(Malformed(_, detail)) -> Assert.Contains("claim-marker scan is incomplete", detail)
        | other -> failwith $"an incomplete re-verification must refuse the reap — got %A{other}"

    Assert.False(transport.Logged "comment-delete", "reap deleted a marker from an incomplete scan")

[<Fact>]
let ``reap SKIPS a marker the holder RENEWED between the scan and the delete`` () =
    // The re-verify's whole point: `Reapable` is a snapshot verdict, and a holder that heartbeated since is
    // ALIVE. Reaping it would be `reap` causing the double-hold it exists to clean up. The fresh read shows
    // marker 880 renewed (a `now` timestamp), so it is left alone and NOTHING is deleted.
    let transport = scripted [ ok (comments [ marker 880 "ghost-222" "" ]) ] // fresh — renewed since the scan

    match reapable aRef staleMarker LeaseExpiredNoPr with
    | Error e -> failwith $"the fixture marker is reapable — got %A{e}"
    | Ok r ->
        match reap transport 120 r with
        | Ok(RenewedSinceScan _) -> Assert.False(transport.Logged "comment-delete FS-GG/FS.GG.SDD 880")
        | other -> failwith $"a marker renewed since the scan must be SKIPPED, not reaped — got %A{other}"

[<Fact>]
let ``reap treats a marker a peer already collected as AlreadyGone, deleting nothing`` () =
    // A peer collected the same stale marker between our scan and our re-verify — "already gone" is a
    // collector's goal state, not a failure, and there is nothing to delete.
    let transport = scripted [ ok "[]" ] // the marker is gone on the fresh read

    match reapable aRef staleMarker LeaseExpiredNoPr with
    | Error e -> failwith $"the fixture marker is reapable — got %A{e}"
    | Ok r ->
        match reap transport 120 r with
        | Ok AlreadyGone -> Assert.False(transport.Logged "comment-delete FS-GG/FS.GG.SDD 880")
        | other -> failwith $"a marker a peer already collected must read AlreadyGone — got %A{other}"

// ---- #895/#977: the LOCK's transport — the one enforced fact the doctrine rests on -----------------
//
// `Protocol.claimLock` states it, four ADRs rest on it (ADR-0027, ADR-0034 §3, ADR-0040 C4, ADR-0038),
// and three generated projections repeat it: THE LOCK LIVES ON REST. A lock may never live on the budget
// that dies first, and #418 measured GraphQL dying first under fan-out — five workers looping `take`
// drained it in ~15 minutes, while REST's per-request meter carried on.
//
// Nothing asserted it. `TransportTests` pins the ASSIGNEE's transport (#418) and never the LOCK's — so if
// the CAS ever took a GraphQL point (a refactor, an added board read on the claim path), the lock would
// silently move onto the budget the fleet drains by scanning, four ADRs' invariant would be false, and
// EVERY DOCUMENT WOULD STILL CLAIM IT HELD. That is #895's own condition — *the recipe steering the fleet
// onto the lock's budget* — reachable by accident, with nothing to catch it.
//
// #977 asked for a GATE on the REST-thrift doctrine and found no prose gate is possible: a string-matcher
// cannot tell a quotation from a use, so it would red the very text that RETIRES the advice (#968, #974)
// and red §4's sanctioned dedupe reads. `check-graphql-monopoly.py` refuses that trap in as many words.
// These tests are what a gate on this doctrine can honestly be: not a checker of English, but a pin on
// the one fact the English is ABOUT — structural, offline, and red only on a real violation.
//
// WHY EVERY LEG ASSERTS `RestCalls` TOO. `Assert.Equal(0, GraphQlCalls)` is green on a claim that made no
// calls at all — it cannot tell "the lock is REST" from "the CAS never ran". That is #606's signature
// exactly (zero checks scoring as all-passed), and it is the failure this file must not reproduce while
// pinning it.

[<Fact>]
let ``#895 the LOCK ITSELF goes over REST - the winning CAS never spends a GraphQL point`` () =
    let transport =
        scripted
            [ ok "[]" // 1. read the live markers
              ok """{"id":901}""" // 2. POST our marker — the linearisation point
              ok (comments [ marker 901 "vole-418" "" ]) ] // 3. re-read: we are the lowest live id

    match claim transport 120 RefuseLiveHolder ignore me itsMe None aRef (fun () -> None) with
    | Ok(Won(held, _)) -> Assert.Equal(901L, held.MarkerId)
    | other -> failwith $"we should have won — got %A{other}"

    // THE ASSERTION: the whole CAS — read, post, re-read — is billed to REST, and not one call to GraphQL.
    Assert.Equal(0, transport.GraphQlCalls)

    // ...and it actually RAN. Three calls, the three legs above. Without this, a `claim` that made no call
    // whatsoever would satisfy the line above and report the invariant green (#606).
    Assert.Equal(3, transport.RestCalls)

[<Fact>]
let ``#895 the WITHDRAW is on the lock's budget too - a lost race never reaches for GraphQL`` () =
    // The withdraw is the leg most likely to drift onto the wrong budget, and the worst one to lose. It is
    // the path a CONTENDED board takes — exactly when REST is scarcest and the fleet is racing — and a
    // marker we posted and could not withdraw is a lock held by a worker who does not know they hold it.
    // If the delete ever needed a GraphQL point, an exhausted GraphQL budget would strand orphaned markers
    // on every lost race, and `take`'s documented "back off briefly and retry" (EX_CONTENDED) would be
    // advice that cannot be followed.
    let transport =
        scripted
            [ ok "[]"
              ok """{"id":902}""" // ours
              ok (comments [ marker 901 "kite-461" ""; marker 902 "vole-418" "" ]) // 901 beat us
              ok "" ] // DELETE our 902

    match claim transport 120 RefuseLiveHolder ignore me itsMe None aRef (fun () -> None) with
    | Ok(Lost w) -> Assert.Equal(them, w)
    | other -> failwith $"we should have lost to the lower id — got %A{other}"

    Assert.True(transport.Logged "comment-delete FS-GG/FS.GG.SDD 902")
    Assert.Equal(0, transport.GraphQlCalls)
    Assert.Equal(4, transport.RestCalls) // read, post, re-read, delete

[<Fact>]
let ``#895 the RENEW is on the lock's budget too - holding a claim never spends a GraphQL point`` () =
    // The lease renewal is what a long-running worker does every few minutes for the life of an item, so it
    // is the highest-FREQUENCY write on the lock's path and the one whose cost compounds across a fleet.
    // `heartbeat` on GraphQL would put KEEPING a lock on the budget that dies first — a worker would lose
    // an item it never stopped working, to a budget it never spent on the work.
    let transport =
        scripted
            [ ok (comments [ marker 901 "vole-418" "" ]) // 1. read: our own live marker
              ok """{"id":901}""" ] // 2. PATCH: renew in place

    match claim transport 120 RefuseLiveHolder ignore me itsMe None aRef (fun () -> None) with
    | Ok(Renewed(held, _)) -> Assert.Equal(901L, held.MarkerId)
    | other -> failwith $"re-claiming our own live lock renews it in place — got %A{other}"

    Assert.Equal(0, transport.GraphQlCalls)
    Assert.Equal(2, transport.RestCalls) // read, patch

[<Fact>]
let ``#895 the pre-claim column read is the CALLER's - the CAS routes no board read of its own`` () =
    // The sharp one, and the reason the invariant is TRUE rather than merely observed.
    //
    // `claim` really does need the column it is about to overwrite (#481, so `release` can put it back),
    // and that read is Projects v2 — GraphQL, with no REST form. If `claim` made it, the lock's path would
    // spend a GraphQL point by construction and this whole invariant would be unattainable.
    //
    // It does not make it. The column arrives as an INJECTED `unit -> BoardStatus option` (`Writes.fs:184`)
    // — so the board read is the caller's to make and the caller's to bill, and the CAS's transport traffic
    // stays REST whatever the callback does. That injection is not a testing convenience; it is the seam
    // that keeps the lock off the dying budget, and it is worth a test of its own so that a future refactor
    // "simplifying" the callback into a transport call fails HERE, loudly, rather than in production on an
    // exhausted budget six months later.
    //
    // So: a callback that answers (the #481 path, `prev=In%20review` on the wire), and the counters unmoved.
    let transport =
        scripted [ ok "[]"; ok """{"id":901}"""; ok (comments [ marker 901 "vole-418" " prev=In%20review" ]) ]

    match claim transport 120 RefuseLiveHolder ignore me itsMe None aRef (fun () -> Some InReview) with
    | Ok(Won(held, _)) -> Assert.Equal(Some InReview, held.PreviousStatus)
    | other -> failwith $"the previous column must be recorded — got %A{other}"

    // The column was read and recorded, and the CAS STILL spent nothing on GraphQL: the read never crossed
    // this transport.
    Assert.Equal(0, transport.GraphQlCalls)
    Assert.Equal(3, transport.RestCalls)

// ---- appendRoomLine (ADR-0051, #1215) — PURE, fence-safe, additive -----------------------------------

[<Fact>]
let ``appendRoomLine appends a Rooms line the parser can read back`` () =
    let body = appendRoomLine "An ordinary issue body.\n\nPaths: src/A" "#42"
    // The written line must be a REAL declaration to `Rooms.parse` — resolved against the item's own repo.
    Assert.Equal<FS.GG.Coord.Types.Ref list>(
        [ { Owner = "FS-GG"; Repo = "FS.GG.SDD"; Number = 42 } ],
        FS.GG.Coord.Rooms.parse "FS-GG" "FS.GG.SDD" body)

[<Fact>]
let ``appendRoomLine is additive — a second room keeps the first`` () =
    let once = appendRoomLine "body" "#12"
    let twice = appendRoomLine once "#13"

    Assert.Equal<FS.GG.Coord.Types.Ref list>(
        [ { Owner = "FS-GG"; Repo = "FS.GG.SDD"; Number = 12 }
          { Owner = "FS-GG"; Repo = "FS.GG.SDD"; Number = 13 } ],
        FS.GG.Coord.Rooms.parse "FS-GG" "FS.GG.SDD" twice)

[<Fact>]
let ``#2801 appendRoomLine is idempotent for the same automatic room`` () =
    let once = appendRoomLine "body" "#12"
    let twice = appendRoomLine once "#12"
    Assert.Equal(once, twice)

[<Fact>]
let ``appendRoomLine closes an unterminated fence FIRST, so the line is not swallowed (#972)`` () =
    // A body ending inside a fence would otherwise eat the appended declaration, and `Rooms.parse` —
    // which reads only unfenced lines — would never see it. The room MUST survive.
    let body = appendRoomLine "See the grammar:\n\n```\nRooms: #99\n" "#42"

    Assert.Equal<FS.GG.Coord.Types.Ref list>(
        [ { Owner = "FS-GG"; Repo = "FS.GG.SDD"; Number = 42 } ],
        FS.GG.Coord.Rooms.parse "FS-GG" "FS.GG.SDD" body)

// ---- room WRITES (ADR-0051, #1215): create the issue, back-ref members, close on roll-up ------------

[<Fact>]
let ``createRoom POSTs to the issues endpoint and returns the new room's ref`` () =
    let transport = scripted [ ok """{"number":220}""" ]

    match createRoom transport "FS-GG" "FS.GG.SDD" "coordination room over FS.GG.SDD#302" "Paths: none" with
    | Ok r ->
        Assert.Equal({ Owner = "FS-GG"; Repo = "FS.GG.SDD"; Number = 220 }, r)
        Assert.True(transport.Logged "issue-list FS-GG/FS.GG.SDD", "createRoom did not hit the repo's issues endpoint")
    | Error e -> failwith $"createRoom must return the new room's ref — got %A{e}"

[<Fact>]
let ``#2134 createIntake refuses an invalid draft before any issue POST`` () =
    let transport = scripted []
    let draft: FS.GG.Coord.Intake.Draft =
        { Schema = FS.GG.Coord.Intake.Schema; Id = "intake-42"; Owner = "FS-GG"; Repository = "FS.GG.SDD"; Title = "t"; Observed = "o"; RootCause = "r"; Acceptance = "a"; Verification = "v"; Paths = []; Class = "hardening"; Status = "Backlog"; Disposition = Some FS.GG.Coord.Intake.Create; Phase = None; Severity = None; BlockedBy = None; BlockedOn = None; BacklogReason = Some "not-yet-actionable"; JudgementQuestion = None }
    match createIntake transport draft with
    | Error _ -> Assert.False(transport.Logged "issue-list FS-GG/FS.GG.SDD")
    | Ok _ -> failwith "invalid intake must refuse"

[<Fact>]
let ``createRoom fails LOUDLY when the response carries no number`` () =
    // We cannot reference a room we cannot name — a created-but-unnameable room must be an error, not a
    // silent success that then writes back-references to nothing.
    let transport = scripted [ ok """{"url":"whatever"}""" ]

    match createRoom transport "FS-GG" "FS.GG.SDD" "t" "b" with
    | Error _ -> ()
    | Ok r -> failwith $"a numberless create must fail — got %A{r}"

[<Fact>]
let ``writeRoomRef PATCHes the member issue with the appended Rooms line`` () =
    let transport = scripted [ ok "{}" ]

    match writeRoomRef transport aRef "Paths: src/A" "#220" with
    | Ok() -> Assert.True(transport.Logged $"issue-patch FS-GG/FS.GG.SDD %d{aRef.Number}", "writeRoomRef did not PATCH the member body")
    | Error e -> failwith $"writeRoomRef must succeed on a 200 — got %A{e}"

[<Fact>]
let ``closeRoom PATCHes the room issue (the derived roll-up close)`` () =
    let transport = scripted [ ok "{}" ]

    match closeRoom transport aRef with
    | Ok() -> Assert.True(transport.Logged $"issue-patch FS-GG/FS.GG.SDD %d{aRef.Number}", "closeRoom did not PATCH the room issue")
    | Error e -> failwith $"closeRoom must succeed on a 200 — got %A{e}"

// ---- #2801 mutual-overlap writer recovery ----------------------------------------------------------

[<Fact>]
let ``#2801 durable wait receipt is observed after its comment write`` () =
    let marker = "<!-- fsgg:overlap-wait/v1 key=a-b -->"
    let body = marker + "\n{\"schema\":\"fsgg.coord.overlap-wait/v1\"}"
    let transport =
        scripted
            [ ok "[]"
              ok "{\"id\":901}"
              ok (comments [ markerWithExactBody 901 body ]) ]

    match writeDurableComment transport aRef marker body with
    | Ok(CommentWritten 901L) -> Assert.Equal(3, transport.RestCalls)
    | other -> failwith $"expected observed durable receipt write, got %A{other}"

[<Fact>]
let ``#2801 response-lost wait receipt converges from the authoritative re-read`` () =
    let marker = "<!-- fsgg:overlap-wait/v1 key=a-b -->"
    let body = marker + "\n{\"schema\":\"fsgg.coord.overlap-wait/v1\"}"
    let transport =
        scripted
            [ ok "[]"
              Error(Transport "response lost")
              ok (comments [ markerWithExactBody 901 body ]) ]

    match writeDurableComment transport aRef marker body with
    | Ok CommentAlreadyPresent -> Assert.Equal(3, transport.RestCalls)
    | other -> failwith $"response loss must reconcile to the exact stored receipt, got %A{other}"

[<Fact>]
let ``#2801 conflicting wait receipt at one marker fails before a write`` () =
    let marker = "<!-- fsgg:overlap-wait/v1 key=a-b -->"
    let existing = marker + "\n{\"revision\":1}"
    let proposed = marker + "\n{\"revision\":2}"
    let transport = scripted [ ok (comments [ markerWithExactBody 901 existing ]) ]

    match writeDurableComment transport aRef marker proposed with
    | Error(Malformed _) ->
        Assert.Equal(1, transport.RestCalls)
        Assert.False(transport.Logged "comment-post")
    | other -> failwith $"same-marker conflict must fail closed, got %A{other}"

[<Fact>]
let ``#2801 exact existing wait receipt is an idempotent no-write`` () =
    let marker = "<!-- fsgg:overlap-wait/v1 key=a-b -->"
    let body = marker + "\n{\"schema\":\"fsgg.coord.overlap-wait/v1\"}"
    let transport = scripted [ ok (comments [ markerWithExactBody 901 body ]) ]

    match writeDurableComment transport aRef marker body with
    | Ok CommentAlreadyPresent ->
        Assert.Equal(1, transport.RestCalls)
        Assert.False(transport.Logged "comment-post")
    | other -> failwith $"exact durable receipt retry must be a no-write, got %A{other}"

[<Fact>]
let ``#2801 first board-orchestrator contender wins its immutable generation`` () =
    let marker = "<!-- fsgg:board-orchestrator-lease-key/v1 board=coord generation=3 -->"
    let body = marker + "\n<!-- fsgg:board-orchestrator-lease/v1 -->\n{}"
    let transport =
        scripted
            [ ok "[]"
              ok "{\"id\":901}"
              ok (comments [ durableLeaseComment 901 body ]) ]
    match acquireDurableLease transport aRef marker body with
    | Ok(LeaseAcquired 901L) -> Assert.Equal(3, transport.RestCalls)
    | other -> failwith $"first contender should acquire the generation, got %A{other}"

[<Fact>]
let ``#2801 losing board-orchestrator race removes only its own candidate`` () =
    let marker = "<!-- fsgg:board-orchestrator-lease-key/v1 board=coord generation=3 -->"
    let winner = marker + "\n<!-- fsgg:board-orchestrator-lease/v1 -->\n{\"holder\":\"B1\"}"
    let mine = marker + "\n<!-- fsgg:board-orchestrator-lease/v1 -->\n{\"holder\":\"B2\"}"
    let transport =
        scripted
            [ ok "[]"
              ok "{\"id\":902}"
              ok (comments [ durableLeaseComment 901 winner; durableLeaseComment 902 mine ])
              ok "{}" ]
    match acquireDurableLease transport aRef marker mine with
    | Ok(LeaseContended 901L) ->
        Assert.Equal(4, transport.RestCalls)
        Assert.True(transport.Logged "comment-delete FS-GG/FS.GG.SDD 902")
        Assert.False(transport.Logged "comment-delete FS-GG/FS.GG.SDD 901")
    | other -> failwith $"losing contender should withdraw itself, got %A{other}"

[<Fact>]
let ``#2801 automatic room is created once and confirmed by cycle marker`` () =
    let marker = "<!-- fsgg:mutual-overlap-room/v1 cycle=abc -->"
    let body = marker + "\n\nPaths: none"
    let after = System.Text.Json.JsonSerializer.Serialize [ {| number = 220; body = body |} ]
    let transport = scripted [ ok "[]"; ok "{\"number\":220}"; ok after ]

    match ensureRoom transport "FS-GG" "FS.GG.SDD" marker "automatic room" body with
    | Ok(RoomCreated room) ->
        Assert.Equal(220, room.Number)
        Assert.Equal(3, transport.RestCalls)
    | other -> failwith $"expected one confirmed room, got %A{other}"

[<Fact>]
let ``#2801 response-lost room create reuses the one observed cycle room`` () =
    let marker = "<!-- fsgg:mutual-overlap-room/v1 cycle=abc -->"
    let body = marker + "\n\nPaths: none"
    let after = System.Text.Json.JsonSerializer.Serialize [ {| number = 220; body = body |} ]
    let transport = scripted [ ok "[]"; Error(Transport "response lost"); ok after ]

    match ensureRoom transport "FS-GG" "FS.GG.SDD" marker "automatic room" body with
    | Ok(RoomAlreadyPresent room) -> Assert.Equal(220, room.Number)
    | other -> failwith $"response-lost create must recover the marker-keyed room, got %A{other}"

[<Fact>]
let ``#2801 duplicate cycle rooms fail closed before create`` () =
    let marker = "<!-- fsgg:mutual-overlap-room/v1 cycle=abc -->"
    let body = marker + "\n\nPaths: none"
    let existing =
        System.Text.Json.JsonSerializer.Serialize
            [ {| number = 220; body = body |}
              {| number = 221; body = body |} ]
    let transport = scripted [ ok existing ]

    match ensureRoom transport "FS-GG" "FS.GG.SDD" marker "automatic room" body with
    | Error(Malformed _) ->
        Assert.Equal(1, transport.RestCalls)
    | other -> failwith $"duplicate marker-keyed rooms must fail closed, got %A{other}"

[<Fact>]
let ``#2801 unreadable room census refuses rather than inventing absence`` () =
    let marker = "<!-- fsgg:mutual-overlap-room/v1 cycle=abc -->"
    let body = marker + "\n\nPaths: none"
    let transport = scripted [ ok "[{\"number\":219}]" ]

    match ensureRoom transport "FS-GG" "FS.GG.SDD" marker "automatic room" body with
    | Error(Malformed _) -> Assert.Equal(1, transport.RestCalls)
    | other -> failwith $"unreadable open room bodies must refuse, got %A{other}"

[<Fact>]
let ``#2801 response-lost room back-reference PATCH is accepted only after readback`` () =
    let transport =
        scripted
            [ ok "{\"body\":\"Paths: src/A\"}"
              Error(Transport "response lost")
              ok "{\"body\":\"Paths: src/A\\n\\nRooms: #220\"}" ]

    match ensureRoomRef transport aRef "#220" with
    | Ok() -> Assert.Equal(3, transport.RestCalls)
    | other -> failwith $"response-lost back-reference must reconcile from the issue body, got %A{other}"

[<Fact>]
let ``#2801 precedence narrows the loser without releasing its held claim`` () =
    let receiptMarker = "<!-- fsgg:overlap-precedence/v1 cycle=abc revision=1 -->"
    let receiptBody = receiptMarker + "\n{\"winner\":43,\"loser\":42}"
    let narrowedBody = "Paths: tests/loser-only.fs"
    let narrowed = validate [ "tests/loser-only.fs" ] |> Result.map (rewrite "Paths: src/shared.fs tests/loser-only.fs") |> Result.defaultWith failwith
    let transport =
        scripted
            [ ok (comments [ marker 901 "vole-418" "" ]) // acquire losing claim
              ok "[]" // precedence pre-census
              ok "{\"id\":902}" // precedence post
              ok (comments [ markerWithExactBody 902 receiptBody ]) // precedence post-census
              ok "{\"body\":\"Paths: src/shared.fs tests/loser-only.fs\"}" // path pre-census
              ok "{}" // narrow PATCH
              ok (System.Text.Json.JsonSerializer.Serialize {| body = narrowedBody |}) ] // path post-census

    let held = acquire transport
    match applyArbitration transport held aRef receiptMarker receiptBody narrowed with
    | Ok LoserNarrowed ->
        Assert.True(transport.Logged "issue-patch FS-GG/FS.GG.SDD 42")
        Assert.False(transport.Logged "comment-delete")
    | other -> failwith $"precedence must preserve the losing claim while narrowing, got %A{other}"

[<Fact>]
let ``#2801 response-lost loser narrow converges without a second precedence receipt`` () =
    let receiptMarker = "<!-- fsgg:overlap-precedence/v1 cycle=abc revision=1 -->"
    let receiptBody = receiptMarker + "\n{\"winner\":43,\"loser\":42}"
    let narrowedBody = "Paths: tests/loser-only.fs"
    let narrowed = validate [ "tests/loser-only.fs" ] |> Result.map (rewrite "Paths: src/shared.fs tests/loser-only.fs") |> Result.defaultWith failwith
    let transport =
        scripted
            [ ok (comments [ marker 901 "vole-418" "" ])
              ok (comments [ markerWithExactBody 902 receiptBody ]) // receipt already durable
              ok "{\"body\":\"Paths: src/shared.fs tests/loser-only.fs\"}"
              Error(Transport "response lost after PATCH")
              ok (System.Text.Json.JsonSerializer.Serialize {| body = narrowedBody |}) ]

    let held = acquire transport
    match applyArbitration transport held aRef receiptMarker receiptBody narrowed with
    | Ok LoserNarrowed ->
        Assert.Equal(1, transport.Count "issue-patch")
        Assert.False(transport.Logged "comment-post")
        Assert.False(transport.Logged "comment-delete")
    | other -> failwith $"response-lost narrow must reconcile from the exact body, got %A{other}"
