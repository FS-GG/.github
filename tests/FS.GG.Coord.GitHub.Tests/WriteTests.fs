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
    | Ok(Won held) ->
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
let ``re-claiming an item we ALREADY hold does not post a second marker`` () =
    // Running the CAS again would post a SECOND marker of ours with a HIGHER id — which we would then lose
    // to our own first one, withdraw, and report as a loss on an item we hold. A worker would be told to
    // stop working on the thing it is holding the lock for.
    let transport = scripted [ ok (comments [ marker 901 "vole-418" "" ]) ]

    match claim transport 120 me None aRef (fun () -> None) with
    | Ok(Won held) -> Assert.Equal(901L, held.MarkerId)
    | other -> failwith $"re-claiming our own live lock is a no-op win — got %A{other}"

    Assert.Equal(0, transport.Count "comment-post")

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
    | Ok(Won held) ->
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
    | Ok(Won held) ->
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
