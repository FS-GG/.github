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
    match claim transport choreLease me None choreLock (fun () -> None) with
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

    match claim transport choreLease me None choreLock (fun () -> None) with
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

    match claim transport choreLease me None choreLock (fun () -> None) with
    | Ok(Won(held, evicted)) ->
        Assert.Equal(901L, held.MarkerId)
        Assert.Contains(them, evicted)
    | other -> failwith $"a lapsed chore lock must be collectable — got %A{other}"

    Assert.True(transport.Logged "comment-delete FS-GG/FS.GG.SDD 800")

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
              Error(RateLimited(UnknownBudget, None)) ] // 4. DELETE faults — leave it for reap

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
    let transport = scripted [ Error(RateLimited(UnknownBudget, None)) ]

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
    match verifyHeld transport 120 me None aRef with
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

    match verifyHeld transport 120 me None aRef with
    | Error(Malformed _) -> ()
    | other -> failwith $"a failed read must not mint a capability — got %A{other}"

[<Fact>]
let ``verifyHeld returns the capability when the live winner IS us`` () =
    let transport = Fake.Recorder(fun _ -> ok (comments [ marker 901 "vole-418" "" ]))

    match verifyHeld transport 120 me None aRef with
    | Ok(Holds held) -> Assert.Equal(901L, held.MarkerId)
    | other -> failwith $"we hold it — got %A{other}"

[<Fact>]
let ``verifyHeld does NOT hold when SOMEBODY ELSE holds it - and that is not a capability`` () =
    let transport = Fake.Recorder(fun _ -> ok (comments [ marker 901 "kite-461" "" ]))

    match verifyHeld transport 120 me None aRef with
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

    match verifyHeld transport 120 me (Some(SessionId "ed60050b")) aRef with
    | Ok(TwinHolds(SessionId theirs)) -> Assert.Equal("79b9e347", theirs)
    | other -> failwith $"our id in another session is a TWIN, not us — got %A{other}"

[<Fact>]
let ``#1031 a twin is a case of its OWN - collapsing it into DoesNotHold would misdiagnose the lease`` () =
    // WHY IT IS NOT `DoesNotHold`. A caller handed `DoesNotHold` re-reads the markers to say WHY, keys on
    // the worker id, finds OUR id on the live winner — and concludes the only other thing that fits: "your
    // lease expired, re-claim it". That is advice to go take a lock a twin is working behind. The outcome
    // has to carry the twin, because no id-keyed question downstream can recover it.
    let transport = Fake.Recorder(fun _ -> ok (comments [ marker 901 "vole-418" " session=79b9e347" ]))

    match verifyHeld transport 120 me (Some(SessionId "ed60050b")) aRef with
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

    match verifyHeld transport 120 me (Some(SessionId "ed60050b")) aRef with
    | Ok(Holds held) -> Assert.Equal(901L, held.MarkerId)
    | other -> failwith $"a sessionless marker with our id must stay ours — got %A{other}"

[<Fact>]
let ``#1031 our OWN session verifies its own marker - or no worker could ever renew`` () =
    let transport = Fake.Recorder(fun _ -> ok (comments [ marker 901 "vole-418" " session=79b9e347" ]))

    match verifyHeld transport 120 me (Some(SessionId "79b9e347")) aRef with
    | Ok(Holds held) -> Assert.Equal(901L, held.MarkerId)
    | other -> failwith $"our own session must verify its own lock — got %A{other}"

[<Fact>]
let ``#1031 a SESSIONLESS caller keeps the old behaviour over a marker that carries one`` () =
    // The other half of "both sessions must be known": a worker whose own session is unknown cannot call
    // anything a twin, because it has nothing to compare. `claim` treats this as ours; so must this.
    let transport = Fake.Recorder(fun _ -> ok (comments [ marker 901 "vole-418" " session=79b9e347" ]))

    match verifyHeld transport 120 me None aRef with
    | Ok(Holds held) -> Assert.Equal(901L, held.MarkerId)
    | other -> failwith $"a caller with no session of its own cannot conclude twin — got %A{other}"

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

    match claim transport 120 me None aRef (fun () -> None) with
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

    match claim transport 120 me None aRef (fun () -> None) with
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

    match claim transport 120 me None aRef (fun () -> None) with
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

    match claim transport 120 me None aRef (fun () -> Some InReview) with
    | Ok(Won(held, _)) -> Assert.Equal(Some InReview, held.PreviousStatus)
    | other -> failwith $"the previous column must be recorded — got %A{other}"

    // The column was read and recorded, and the CAS STILL spent nothing on GraphQL: the read never crossed
    // this transport.
    Assert.Equal(0, transport.GraphQlCalls)
    Assert.Equal(3, transport.RestCalls)
