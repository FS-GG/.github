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

/// A transport that answers a SEQUENCE of canned responses — so a CAS, which reads, posts, and re-reads,
/// can be driven through a real race.
let private scripted (responses: IoResult<Response> list) =
    let queue = System.Collections.Generic.Queue<IoResult<Response>>(responses)

    Fake.Recorder(fun _ ->
        if queue.Count = 0 then
            failwith "the transport was called more times than the test scripted"
        else
            queue.Dequeue())

let private ok (body: string) =
    Ok
        { Status = 200
          Body = body
          ETag = None
          NextLink = None }

// ---- the CAS ---------------------------------------------------------------------------------------

[<Fact>]
let ``the CAS WINS when our marker is the lowest live id`` () =
    let transport =
        scripted
            [ ok "[]" // 1. read: nobody holds it
              ok """{"id":901}""" // 2. post our marker
              ok (comments [ marker 901 "vole-418" "" ]) ] // 3. re-read: we are the lowest

    match claim transport 120 me None aRef (fun () -> None) with
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

    match claim transport 120 me None aRef (fun () -> None) with
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

    match claim transport 120 me None aRef (fun () -> None) with
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

    match claim transport 120 me None aRef (fun () -> None) with
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

    match claim transport 120 me None aRef (fun () -> None) with
    | Error(Transport detail) -> Assert.Contains("orphaned", detail)
    | other -> failwith $"an orphaned marker must be loud — got %A{other}"

[<Fact>]
let ``the CAS refuses BEFORE posting when somebody else already holds a live lock`` () =
    // Post-then-withdraw would work, but it leaves a comment somebody has to read on an item that was never
    // ours. Refuse cheaply.
    let transport = scripted [ ok (comments [ marker 901 "kite-461" "" ]) ]

    match claim transport 120 me None aRef (fun () -> None) with
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

    match claim transport 120 me None aRef (fun () -> None) with
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

    match claim transport 120 me None aRef (fun () -> None) with
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

    match claim transport 120 me None aRef (fun () -> None) with
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

    match claim transport 120 me None aRef (fun () -> None) with
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

    match claim transport 120 me None aRef (fun () -> None) with
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
              Error(RateLimited None) ] // 4. DELETE faults — leave it for reap

    match claim transport 120 me None aRef (fun () -> None) with
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

    match claim transport 120 me None aRef (fun () -> None) with
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

    match claim transport 120 me (Some(SessionId "ed60050b")) aRef (fun () -> None) with
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

    match claim transport 120 me (Some(SessionId "ed60050b")) aRef (fun () -> None) with
    | Ok(Renewed(held, _)) -> Assert.Equal(901L, held.MarkerId)
    | other -> failwith $"a sessionless marker with our id must stay ours — got %A{other}"

[<Fact>]
let ``#419 the SAME session re-claiming its own marker is a heartbeat, not a twin`` () =
    // Without this, the refusal would fire on the worker itself — it could never renew its own claim.
    let transport =
        scripted [ ok (comments [ marker 901 "vole-418" " session=79b9e347" ]); ok """{"id":901}""" ] // read, then PATCH

    match claim transport 120 me (Some(SessionId "79b9e347")) aRef (fun () -> None) with
    | Ok(Renewed(held, _)) -> Assert.Equal(901L, held.MarkerId)
    | other -> failwith $"our own session re-claiming is a heartbeat, not a twin — got %A{other}"

[<Fact>]
let ``a failed FIRST read is fatal, and nothing is posted`` () =
    // The only cheap place to fail, which is why the read comes first: we have posted nothing, so there is
    // no marker to clean up. Guessing the lock state from a failed read is the one thing a lock may never
    // do.
    let transport = scripted [ Error(RateLimited None) ]

    match claim transport 120 me None aRef (fun () -> None) with
    | Error(RateLimited _) -> ()
    | other -> failwith $"a failed lock read must refuse — got %A{other}"

    Assert.Equal(0, transport.Count "comment-post")

// ---- #481: the column a claim overwrote ------------------------------------------------------------

[<Fact>]
let ``#481 the claim RECORDS the column it overwrote, so release can put it back`` () =
    let transport =
        scripted [ ok "[]"; ok """{"id":901}"""; ok (comments [ marker 901 "vole-418" " prev=In%20review" ]) ]

    match claim transport 120 me None aRef (fun () -> Some InReview) with
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

    match claim transport 120 me None aRef (fun () -> None) with
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
    match verifyHeld transport 120 me aRef with
    | Ok(Some held) -> held
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
let ``an EMPTY touch-set is refused - it reserves nothing, and 'none' is a different decision`` () =
    match validate [] with
    | Error _ -> ()
    | Ok _ -> failwith "an empty token list is not a touch-set"

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
    // prevent, sitting inside its own constructor. `None` says "we looked, and this worker does not hold
    // it" — which is a claim a failed read is not entitled to make.
    let transport = Fake.Recorder(fun _ -> Error(Malformed("FS.GG.SDD#42", "not JSON")))

    match verifyHeld transport 120 me aRef with
    | Error(Malformed _) -> ()
    | other -> failwith $"a failed read must not mint a capability — got %A{other}"

[<Fact>]
let ``verifyHeld returns the capability when the live winner IS us`` () =
    let transport = Fake.Recorder(fun _ -> ok (comments [ marker 901 "vole-418" "" ]))

    match verifyHeld transport 120 me aRef with
    | Ok(Some held) -> Assert.Equal(901L, held.MarkerId)
    | other -> failwith $"we hold it — got %A{other}"

[<Fact>]
let ``verifyHeld returns None when SOMEBODY ELSE holds it - and None is not a capability`` () =
    let transport = Fake.Recorder(fun _ -> ok (comments [ marker 901 "kite-461" "" ]))

    match verifyHeld transport 120 me aRef with
    | Ok None -> ()
    | other -> failwith $"another worker's lock is not ours — got %A{other}"

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

// ---- reap: an expired lease is EVIDENCE of abandonment, not PROOF (#581) ----------------------------

let private staleMarker =
    { Reads.Id = 880L
      Reads.Worker = WorkerId "ghost-222"
      Reads.Session = None
      Reads.AgeSeconds = 10800 // 3h — well past a 120-minute lease
      Reads.PreviousStatus = None
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
