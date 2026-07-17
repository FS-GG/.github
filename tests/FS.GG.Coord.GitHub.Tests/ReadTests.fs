module FS.GG.Coord.GitHub.Tests.ReadTests

open Xunit
open FS.GG.Coord.Types
open FS.GG.Coord.GitHub
open FS.GG.Coord.GitHub.Errors
open FS.GG.Coord.GitHub.Transport

/// A transport that answers every request with one canned body.
let private serving (body: string) =
    Fake.Recorder(fun _ ->
        Ok
            { Status = 200
              Body = body
              ETag = None
              NextLink = None })

/// A transport that fails every request.
let private failing (error: IoError) = Fake.Recorder(fun _ -> Error error)

// ---- #461: the lock is never guessed at ------------------------------------------------------------

[<Fact>]
let ``#461 a MALFORMED comments page is a failed read, NOT an empty lock`` () =
    // THE FOUNDING INCIDENT OF THIS LAYER. The claim-candidate read came back as bytes that are not JSON —
    // a truncated page, a proxy error body, a 5xx rendered as text — and `gh` EXITED 0. `$cand` was the
    // empty string, `jq 'length'` printed nothing AND exited 0 (so `set -euo pipefail` never fired), the
    // loop body never ran, and `active_claims` returned `[]`.
    //
    // A failed read wearing an empty set's clothes. And `[]` is a CLAIM — it says "I read the locks and
    // nobody holds anything." A failed scan is not entitled to make it.
    let recorder = serving "<html>502 Bad Gateway</html>"

    match Reads.markers recorder "FS-GG" "FS.GG.SDD" 42 with
    | Error(Malformed _) -> ()
    | Ok markers -> failwith $"a malformed page must NEVER read as an empty lock — got %d{List.length markers} marker(s)"
    | Error other -> failwith $"expected Malformed — got %A{other}"

[<Fact>]
let ``#461 ...and an EMPTY body is a failed read too, not an unheld item`` () =
    let recorder = serving ""

    match Reads.markers recorder "FS-GG" "FS.GG.SDD" 42 with
    | Error(Malformed _) -> ()
    | other -> failwith $"an empty body is not an empty result — got %A{other}"

[<Fact>]
let ``#461 the guard must NOT fire on a legitimately empty comment list`` () =
    // The counterweight, and it is as important as the guard. A real, successful scan that found no markers
    // is a valid answer — the item is genuinely free. A fail-closed rule that also refuses the good path
    // would deadlock the board.
    let recorder = serving "[]"

    match Reads.markers recorder "FS-GG" "FS.GG.SDD" 42 with
    | Ok [] -> ()
    | other -> failwith $"a successful scan with no markers is an empty set — got %A{other}"

[<Fact>]
let ``the marker read is NEVER conditional - a 304 could hide a live lock`` () =
    let recorder = serving "[]"
    Reads.markers recorder "FS-GG" "FS.GG.SDD" 42 |> ignore

    // **A lock may never be read from a cache.** A 304 serving a body captured before the marker was posted
    // would report zero comments over a live claim. Going direct means there is no ETag to be stale.
    Assert.True(recorder.Logged "comment-list FS-GG/FS.GG.SDD 42")
    Assert.Equal(0, recorder.GraphQlCalls)
    Assert.Equal(1, recorder.RestCalls)

// ---- the marker grammar ----------------------------------------------------------------------------

let private comment (id: int) (body: string) (updatedAt: string) =
    let escaped = body.Replace("\"", "\\\"")
    $"""{{"id":%d{id},"body":"%s{escaped}","updated_at":"%s{updatedAt}"}}"""

let private now = System.DateTimeOffset.UtcNow.ToString("o")

[<Fact>]
let ``a marker is ANCHORED - a say message that QUOTES one cannot forge a lock`` () =
    // Un-anchor the pattern and any free-form `say` message whose text merely mentions
    // `<!-- fsgg:claim worker=ghost -->` takes the lock on the item it was posted to. This is a security
    // property, not a style one.
    let forgery =
        "I tried to claim it but saw <!-- fsgg:claim worker=ghost --> already there"

    let recorder = serving $"[{comment 901 forgery now}]"

    match Reads.markers recorder "FS-GG" "FS.GG.SDD" 42 with
    | Ok [] -> ()
    | other -> failwith $"a quoted marker is not a marker — got %A{other}"

[<Fact>]
let ``a marker we cannot parse a WORKER out of is held by nobody - and it BLOCKS`` () =
    // A half-written lock must fail CLOSED. If an unparseable marker vanished, the item would read as free
    // and a second worker would be handed it — which is the one thing a lock exists to prevent. So it
    // becomes a claim held by `unparsed-marker`: nobody can heartbeat it, nobody can release it by name,
    // and it holds the item until somebody reaps it deliberately.
    let recorder =
        serving $"""[{comment 901 "<!-- fsgg:claim lease=120 -->" now}]"""

    match Reads.markers recorder "FS-GG" "FS.GG.SDD" 42 with
    | Ok [ m ] -> Assert.Equal(WorkerId "unparsed-marker", m.Worker)
    | other -> failwith $"an unparseable marker must still block — got %A{other}"

[<Fact>]
let ``the marker's prev= column is decoded, and %% comes out LAST`` () =
    // `enc_status` encodes `%` FIRST, so it must be decoded LAST — otherwise a status containing a literal
    // `%20` decodes into a space that was never there. It is the classic escaping-order bug, and the board
    // column it corrupts is the one `release` puts back (#481).
    let body =
        "<!-- fsgg:claim worker=vole-418 lease=120 prev=In%20progress -->"

    let recorder = serving $"[{comment 901 body now}]"

    match Reads.markers recorder "FS-GG" "FS.GG.SDD" 42 with
    | Ok [ m ] -> Assert.Equal(Some InProgress, m.PreviousStatus)
    | other -> failwith $"the previous column must be recovered — got %A{other}"

// ---- the CAS's total order -------------------------------------------------------------------------

[<Fact>]
let ``the CAS winner is the LOWEST LIVE comment id`` () =
    // GitHub issues comment ids from ONE server-side sequence, so "lowest id wins" is a total order that
    // every racer observes identically. That is what makes this a real compare-and-swap with a real
    // linearisation point, rather than a hopeful convention — and ADR-0040 C4 keeps it exactly as it is.
    let markers =
        [ { Reads.Id = 903L
            Reads.Worker = WorkerId "late"
            Reads.Session = None
            Reads.AgeSeconds = 10
            Reads.PreviousStatus = None
            Reads.Raw = "" }
          { Reads.Id = 901L
            Reads.Worker = WorkerId "first"
            Reads.Session = None
            Reads.AgeSeconds = 10
            Reads.PreviousStatus = None
            Reads.Raw = "" } ]

    match Reads.winner 120 markers with
    | Some m -> Assert.Equal(WorkerId "first", m.Worker)
    | None -> failwith "a live marker must win"

[<Fact>]
let ``a STALE marker does not win - but an unreadable AGE is not stale`` () =
    // A negative age means we could not read the marker's timestamp. Reading that as an EXPIRED lease would
    // reap a live claim on the strength of a field we failed to parse — a failed read deciding a lock,
    // which is the exact substitution this layer exists to make impossible.
    let stale =
        { Reads.Id = 901L
          Reads.Worker = WorkerId "dead"
          Reads.Session = None
          Reads.AgeSeconds = 99999
          Reads.PreviousStatus = None
          Reads.Raw = "" }

    let ageUnknown =
        { stale with
            Reads.Id = 902L
            Reads.Worker = WorkerId "unknown-age"
            Reads.AgeSeconds = -1 }

    Assert.True(Reads.isStale 120 stale)
    Assert.False(Reads.isStale 120 ageUnknown)

    match Reads.winner 120 [ stale; ageUnknown ] with
    | Some m -> Assert.Equal(WorkerId "unknown-age", m.Worker)
    | None -> failwith "the marker whose age we could not read still holds the item"

// ---- #476: MERGED is not CLOSED --------------------------------------------------------------------

[<Fact>]
let ``#476 a MERGED pull request resolves as BlockerMerged, not BlockerClosed`` () =
    // An issue's state is OPEN | CLOSED. A PR's is OPEN | CLOSED | **MERGED**. A rule that clears a blocker
    // only on CLOSED therefore unblocks when the blocking PR is ABANDONED and blocks forever once it is
    // FINISHED — the gate opens precisely when the work is thrown away, and shuts precisely when it is
    // done.
    let recorder =
        serving """{"state":"closed","pull_request":{"merged_at":"2026-07-14T10:00:00Z"}}"""

    match Reads.blockerState recorder "FS-GG" "FS.GG.SDD" 8 with
    | Ok BlockerMerged -> ()
    | other -> failwith $"a merged PR must resolve as MERGED — got %A{other}"

[<Fact>]
let ``#476 ...and an ABANDONED pull request is BlockerClosed`` () =
    let recorder =
        serving """{"state":"closed","pull_request":{"merged_at":null}}"""

    match Reads.blockerState recorder "FS-GG" "FS.GG.SDD" 8 with
    | Ok BlockerClosed -> ()
    | other -> failwith $"a closed-unmerged PR is CLOSED — got %A{other}"

[<Fact>]
let ``a blocker we could not READ is Unknown - and Unknown BLOCKS`` () =
    // "I could not look" is not "I looked and it is fine" (#266, #421). The safe direction on a lock is
    // always to hold it — an unresolvable blocker keeps the item blocked and says so.
    let recorder = failing (Transport "connection reset")

    match Reads.blockerState recorder "FS-GG" "FS.GG.SDD" 8 with
    | Ok BlockerUnknown -> ()
    | other -> failwith $"an unreadable blocker must block — got %A{other}"

[<Fact>]
let ``one unreadable blocker does not STARVE the board`` () =
    // A 502 on ONE issue is local to that issue. The item it blocks stays blocked and explains itself,
    // while every other item on the board is still schedulable. Failing the whole scan on one bad ref would
    // be fail-closed in the wrong place — it would turn one unreachable issue into a dead queue.
    let recorder = failing (Http(500, "boom"))
    let result = Reads.blockerState recorder "FS-GG" "FS.GG.SDD" 8
    Assert.True(Result.isOk result)

[<Fact>]
let ``#534 an EXHAUSTED BUDGET is NOT degraded to 'blocker unknown' - it is propagated`` () =
    // THE DISTINCTION THE ARM ABOVE DEPENDS ON, AND THE BUG THIS FILE ALMOST SHIPPED.
    //
    // "One unreadable blocker must not starve the board" is right for a TRANSIENT — a 502 on one issue. It
    // is catastrophically wrong for a RATE LIMIT, because a rate limit is not a fact about this ref: it is
    // a fact about the CLIENT, and the very next resolution fails identically.
    //
    // Degrade it, and EVERY blocker on the board resolves `Unknown`; every `Unknown` blocks; the tool
    // reports "nothing schedulable" over a full queue and exits **0**. That is #534 (the budget-exhausted
    // message swallowed, the worker told there is nothing to do) wearing #421's clothes (a budget failure
    // reported as a fact about an item) — and the caller would never back off, because it was never told
    // to.
    let recorder = failing (RateLimited(UnknownBudget, None))

    match Reads.blockerState recorder "FS-GG" "FS.GG.SDD" 8 with
    | Error(RateLimited _) -> ()
    | other -> failwith $"an exhausted budget must not masquerade as an unresolvable blocker — got %A{other}"

[<Fact>]
let ``#534 ...and prAlive propagates it too - reap must not decide liveness on a read it cannot make`` () =
    let recorder = failing (RateLimited(UnknownBudget, None))

    match Reads.prAlive recorder "FS-GG" "FS.GG.SDD" 42 with
    | Error(RateLimited _) -> ()
    | other -> failwith $"an exhausted budget must not masquerade as unknown liveness — got %A{other}"

// ---- #581: the lease is not the life ---------------------------------------------------------------

[<Fact>]
let ``#581 an OPEN item PR is proof of life - the lease lapsed, the WORK did not`` () =
    // Lease expiry is EVIDENCE of abandonment, never PROOF, and its false positive is systematic: work that
    // simply takes longer than the lease. An open PR on the item's own `item/<n>-*` branch is the worktree
    // protocol's own artifact and is server-side proof that the worker is still there.
    let recorder =
        serving """[{"number":77,"head":{"ref":"item/42-the-thing"}}]"""

    match Reads.prAlive recorder "FS-GG" "FS.GG.SDD" 42 with
    | Ok(LeaseExpiredPrOpen 77) -> ()
    | other -> failwith $"an open item PR is proof of life — got %A{other}"

[<Fact>]
let ``#581 a PR on ANOTHER item's branch is not proof of life for this one`` () =
    let recorder =
        serving """[{"number":77,"head":{"ref":"item/99-something-else"}}]"""

    match Reads.prAlive recorder "FS-GG" "FS.GG.SDD" 42 with
    | Ok LeaseExpiredNoPr -> ()
    | other -> failwith $"another item's PR says nothing about this one — got %A{other}"

[<Fact>]
let ``#581 a FAILED pr read is Unknown, NOT 'no PR' - this is what reaped live work`` () =
    // The distinction that stops a transient 5xx from collecting the claim of a worker who is visibly,
    // demonstrably still working. `LivenessUnknown` and `LeaseExpiredNoPr` are different facts, and only
    // one of them licenses a reap.
    let recorder = failing (Http(502, "bad gateway"))

    match Reads.prAlive recorder "FS-GG" "FS.GG.SDD" 42 with
    | Ok LivenessUnknown -> ()
    | other -> failwith $"an unreadable PR list must not read as 'no PR' — got %A{other}"

// ---- #641: a pull request is not an issue ----------------------------------------------------------

[<Fact>]
let ``#641 the open-issue scan EXCLUDES pull requests`` () =
    // A PR is an issue in REST, and it is not an item of work. `fsgg-coord issues` listed PRs as issues, so
    // the duplicate-check read a PR as "already filed" and silently suppressed a real finding.
    let recorder =
        serving
            """[{"number":42,"body":"Paths: src/**"},
                {"number":43,"body":"a PR","pull_request":{"url":"https://api.github.com/pulls/43"}}]"""

    match Reads.openIssues recorder "FS-GG" "FS.GG.SDD" with
    | Ok issues ->
        Assert.Equal(1, List.length issues)
        Assert.Equal(42, fst issues.[0])
    | other -> failwith $"a PR is not an issue — got %A{other}"

[<Fact>]
let ``#461 a malformed issue list is an error, not an empty candidate set`` () =
    let recorder = serving "not json at all"

    match Reads.openIssues recorder "FS-GG" "FS.GG.SDD" with
    | Error(Malformed _) -> ()
    | other -> failwith $"an unreadable issue list must refuse — got %A{other}"

// ---- the issue body --------------------------------------------------------------------------------

[<Fact>]
let ``an issue with a NULL body reads as empty - that is a successful read, not a failure`` () =
    // GitHub returns `"body": null` for an issue nobody wrote a description for, and that is a real,
    // successfully-observed fact: the issue exists and declares nothing. `TouchSet.parse` will call it
    // `Undeclared` — an OMISSION — which is the correct verdict and a different one from `Unreadable`.
    let recorder = serving """{"number":42,"body":null}"""

    match Reads.issueBody recorder "FS-GG" "FS.GG.SDD" 42 with
    | Ok "" -> ()
    | other -> failwith $"a null body is an empty body — got %A{other}"

[<Fact>]
let ``an issue body we could NOT read is an error - never an empty touch-set`` () =
    // This is the one that matters. Coercing an unread body to `Undeclared` would report a confident
    // OMISSION about an item nobody looked at — and then schedule every other item against a surface we
    // cannot see. The engine's own `TouchSet.Unreadable` case exists for exactly this, and it can only be
    // produced by a caller that KNOWS the read failed.
    let recorder = failing (Http(502, "bad gateway"))

    match Reads.issueBody recorder "FS-GG" "FS.GG.SDD" 42 with
    | Error(Http(502, _)) -> ()
    | other -> failwith $"an unreadable body must refuse to become a touch-set — got %A{other}"

// ---- #421, at the read ----------------------------------------------------------------------------

[<Fact>]
let ``#421 a rate-limited read propagates as RateLimited - never as 'not there'`` () =
    // The read layer must carry the budget failure OUT, intact. The moment it degrades to an empty result
    // the caller cannot tell an exhausted budget from an absent subject — and it then acts on the second
    // one, with all the confidence of a read it never got (#421). The remediation itself is harmless — an
    // `item-add` for an issue already on the board is idempotent (#871); the invented certainty is not.
    let recorder = failing (RateLimited(UnknownBudget, None))

    match Reads.markers recorder "FS-GG" "FS.GG.SDD" 42 with
    | Error(RateLimited _) -> ()
    | other -> failwith $"an exhausted budget must not become an empty lock — got %A{other}"

    match Reads.issueBody recorder "FS-GG" "FS.GG.SDD" 42 with
    | Error(RateLimited _) -> ()
    | other -> failwith $"an exhausted budget must not become an empty body — got %A{other}"

// ---- the sub-issue graph (lint / rollup) -----------------------------------------------------------

[<Fact>]
let ``subIssues reads the total apart from the visible nodes, with each child's ref and state`` () =
    let transport =
        serving
            """{"data":{"repository":{"issue":{"subIssues":{"totalCount":2,"nodes":[
                 {"number":51,"state":"OPEN","repository":{"nameWithOwner":"FS-GG/FS.GG.SDD"}},
                 {"number":52,"state":"CLOSED","repository":{"nameWithOwner":"FS-GG/FS.GG.SDD"}}]}}}}}"""

    match Reads.subIssues transport "FS-GG" "FS.GG.SDD" 50 with
    | Ok set ->
        Assert.Equal(2, set.Total)
        Assert.Equal<Reads.SubIssue list>(
            [ ({ Ref = "FS-GG/FS.GG.SDD#51"; Open = true }: Reads.SubIssue)
              { Ref = "FS-GG/FS.GG.SDD#52"; Open = false } ],
            set.Children
        )
    | Error e -> failwith $"the graph must resolve — got %A{e}"

[<Fact>]
let ``subIssues keeps a truncated graph honest - Total exceeds the visible nodes`` () =
    // The distinction EPIC-CHILDREN-TRUNCATED and the rollup depend on: five children, only two returned.
    let transport =
        serving
            """{"data":{"repository":{"issue":{"subIssues":{"totalCount":5,"nodes":[
                 {"number":1,"state":"CLOSED","repository":{"nameWithOwner":"FS-GG/FS.GG.SDD"}},
                 {"number":2,"state":"CLOSED","repository":{"nameWithOwner":"FS-GG/FS.GG.SDD"}}]}}}}}"""

    match Reads.subIssues transport "FS-GG" "FS.GG.SDD" 50 with
    | Ok set -> Assert.True(set.Total > List.length set.Children)
    | Error e -> failwith $"the graph must resolve — got %A{e}"

[<Fact>]
let ``subIssues FAILS CLOSED - an unreadable graph is an error, never an empty set`` () =
    // An epic whose children could not be read must not roll up as "no children".
    match Reads.subIssues (failing (RateLimited(UnknownBudget, None))) "FS-GG" "FS.GG.SDD" 50 with
    | Error(RateLimited _) -> ()
    | other -> failwith $"a failed graph read must be an error — got %A{other}"

[<Fact>]
let ``refIsPullRequest is true iff the issues payload carries a pull_request object`` () =
    let asPr =
        serving """{"number":418,"pull_request":{"url":"https://github.com/x/y/pull/418"}}"""

    let asIssue = serving """{"number":414,"body":"a plain issue"}"""

    match Reads.refIsPullRequest asPr "FS-GG" "FS.GG.SDD" 418 with
    | Ok true -> ()
    | other -> failwith $"a PR payload must probe true — got %A{other}"

    match Reads.refIsPullRequest asIssue "FS-GG" "FS.GG.SDD" 414 with
    | Ok false -> ()
    | other -> failwith $"a plain issue must probe false — got %A{other}"


// ---- messages: the say/inbox channel ---------------------------------------------------------------

/// One `fsgg:msg` comment, rendered exactly as `Writes.say` writes the body: REAL newlines separating the
/// marker comment, the `**from → to**` header, and the text.
let private msgComment (cid: int) (fromW: string) (dest: string) (text: string) =
    let body = $"<!-- fsgg:msg from={fromW} to={dest} -->\n**{fromW} → {dest}**\n\n{text}"
    let jbody = System.Text.Json.JsonSerializer.Serialize body
    $"""{{"id":{cid},"body":{jbody},"created_at":"2026-07-16T00:00:0{cid}Z"}}"""

[<Fact>]
let ``messages parses an fsgg:msg comment - id, from, to, and the text with the header peeled off`` () =
    let recorder =
        serving ("[" + msgComment 7 "finch-a3f" "smew-f31" "I own src/Audio until Friday." + "]")

    match Reads.messages recorder "FS-GG" "FS.GG.SDD" 42 with
    | Ok [ m ] ->
        Assert.Equal(7L, m.Id)
        Assert.Equal("finch-a3f", m.From)
        Assert.Equal("smew-f31", m.To)
        // The `<!-- … -->` marker and the `**from → to**` header are peeled; the message itself remains.
        Assert.Equal("I own src/Audio until Friday.", m.Text)
    | other -> failwith $"expected one parsed message — got %A{other}"

[<Fact>]
let ``messages keeps a broadcast (to=*) and orders by comment id`` () =
    let page =
        "[" + msgComment 9 "finch-a3f" "*" "second" + "," + msgComment 4 "finch-a3f" "smew-f31" "first" + "]"

    match Reads.messages (serving page) "FS-GG" "FS.GG.SDD" 42 with
    | Ok [ a; b ] ->
        // Lowest comment id first — the same total order `markers` returns, so a cursor keyed on the id is
        // monotone regardless of the order GitHub returned the page in.
        Assert.Equal(4L, a.Id)
        Assert.Equal(9L, b.Id)
        Assert.Equal("*", b.To)
    | other -> failwith $"expected two ordered messages — got %A{other}"

[<Fact>]
let ``messages ignores a claim marker and any non-message comment`` () =
    // A comments page carries claim markers and plain comments too. `messages` reads ONLY `fsgg:msg`, so a
    // lock marker on the same issue never surfaces as mail.
    let marker =
        comment 1 "<!-- fsgg:claim worker=ghost -->" now

    let plain = comment 2 "just a normal human comment" now
    let page = "[" + marker + "," + plain + "," + msgComment 3 "finch-a3f" "smew-f31" "the only message" + "]"

    match Reads.messages (serving page) "FS-GG" "FS.GG.SDD" 42 with
    | Ok [ m ] -> Assert.Equal("the only message", m.Text)
    | other -> failwith $"a claim marker and a plain comment are not messages — got %A{other}"

[<Fact>]
let ``messages does NOT deliver a comment whose TEXT merely quotes a msg marker (anchored)`` () =
    // The same forgery `markerRe` refuses: an un-anchored match would let a message BODY that quotes an
    // `fsgg:msg` header be read as a real message header. The regex is anchored at the start of the body.
    let forgery =
        comment 5 "look what I can write: <!-- fsgg:msg from=ghost to=victim -->" now

    match Reads.messages (serving ("[" + forgery + "]")) "FS-GG" "FS.GG.SDD" 42 with
    | Ok [] -> ()
    | other -> failwith $"a quoted marker mid-body is not a message — got %A{other}"

[<Fact>]
let ``messages FAILS CLOSED on a malformed page - a lost message is not an empty mailbox`` () =
    // A message is not a lock, so a single unparseable message is DROPPED — but a page we could not read at
    // all is still an error, never an empty mailbox that reports "no new mail" over an unread warning.
    match Reads.messages (serving "<html>502</html>") "FS-GG" "FS.GG.SDD" 42 with
    | Error(Malformed _) -> ()
    | other -> failwith $"a malformed page must be an error, not an empty mailbox — got %A{other}"

// ---- issues: the ETag-revalidated REST list (#446/#418) --------------------------------------------

/// A private cache directory for the ETag round-trip. `Reads.issues` stores the body + its validator on
/// disk (that is what makes a later 304 answerable), so a test of it owns a throwaway cache the way
/// `CacheTests.Sandbox` does — an inherited cache would be testing whatever ran before it.
type private IssuesCache() =
    let dir =
        System.IO.Path.Combine(System.IO.Path.GetTempPath(), "fsgg-issues-test-" + System.Guid.NewGuid().ToString("N"))

    do
        System.IO.Directory.CreateDirectory dir |> ignore
        System.Environment.SetEnvironmentVariable("FSGG_COORD_CACHE", dir)

    interface System.IDisposable with
        member _.Dispose() =
            System.Environment.SetEnvironmentVariable("FSGG_COORD_CACHE", null)

            try
                System.IO.Directory.Delete(dir, true)
            with _ ->
                ()

/// A stateful transport: 200 + ETag for an UNCONDITIONAL read, 304 for a conditional one whose validator
/// matches — exactly how GitHub answers `If-None-Match`. It is the ETag revalidation the command is built on.
let private etagServer (body: string) (etag: string) =
    Fake.Recorder(fun (req: Request) ->
        match req.IfNoneMatch with
        | Some e when e = etag -> Ok { Status = 304; Body = ""; ETag = Some etag; NextLink = None }
        | _ -> Ok { Status = 200; Body = body; ETag = Some etag; NextLink = None })

[<Fact>]
let ``issues returns the raw body, then revalidates with the stored ETag and serves the 304 from cache (#418)`` () =
    // The command's whole reason to exist: a repeat listing costs NOTHING. The first read is unconditional
    // (inm=none) and caches the body with its validator; the second sends the ETag, the server answers 304,
    // and the body is served FROM CACHE — the budget-free read.
    use _cache = new IssuesCache()
    let body = """[{"number":501},{"number":502}]"""
    let etag = "W/\"issues-v1\""
    let recorder = etagServer body etag

    match Reads.issues recorder "FS-GG" "FS.GG.SDD" "open" None false with
    | Ok b -> Assert.Equal(body, b)
    | other -> failwith $"first read must return the body — got %A{other}"

    Assert.True(recorder.Logged "issue-list FS-GG/FS.GG.SDD paginate=1 inm=none")

    match Reads.issues recorder "FS-GG" "FS.GG.SDD" "open" None false with
    | Ok b -> Assert.Equal(body, b)
    | other -> failwith $"a 304 must serve the cached body, never an empty result — got %A{other}"

    Assert.True(recorder.Logged $"issue-list FS-GG/FS.GG.SDD paginate=1 inm={etag}")

[<Fact>]
let ``issues --refresh drops the stored ETag and re-reads unconditionally`` () =
    // `--refresh` (fresh=true) is the caller saying "ignore the cache". Even with a warm body+etag, the
    // read goes out UNCONDITIONAL (inm=none), so a caller who suspects a stale cache can force a full read.
    use _cache = new IssuesCache()
    let body = """[{"number":501}]"""
    let etag = "W/\"issues-v1\""
    let recorder = etagServer body etag

    Reads.issues recorder "FS-GG" "FS.GG.SDD" "open" None false |> ignore // warm the cache

    match Reads.issues recorder "FS-GG" "FS.GG.SDD" "open" None true with
    | Ok b -> Assert.Equal(body, b)
    | other -> failwith $"a --refresh read must return the fresh body — got %A{other}"

    // Both requests carried NO validator — the second because --refresh dropped it.
    Assert.Equal(2, recorder.Count "issue-list FS-GG/FS.GG.SDD paginate=1 inm=none")

[<Fact>]
let ``issues fails closed on an unreadable list - never an empty array`` () =
    // A listing we could not read is an ERROR, not "this repo has no issues" — the same fail-closed rule
    // the rest of this layer holds. The body is passed through raw, so an empty-but-present array `[]` is a
    // real answer; a 502 is not.
    use _cache = new IssuesCache()

    match Reads.issues (failing (Http(502, "bad gateway"))) "FS-GG" "FS.GG.SDD" "open" None false with
    | Error(Http(502, _)) -> ()
    | other -> failwith $"an unreadable listing must refuse — got %A{other}"

[<Fact>]
let ``issues fails closed on a 200 that is not a JSON array - a proxy error page is not an empty listing`` () =
    // The #461 rule at the `issues` surface: a 200 carrying a proxy's HTML error body (or a truncated page)
    // must NOT be emitted verbatim as if it were the issue list — it is a failed read. A present-but-empty
    // `[]` passes (a real answer); garbage does not, and nothing is cached for a later 304 to serve.
    use _cache = new IssuesCache()

    match Reads.issues (serving "<html>502 Bad Gateway</html>") "FS-GG" "FS.GG.SDD" "open" None false with
    | Error(Malformed _) -> ()
    | other -> failwith $"a non-JSON 200 must be a failed read, not a listing — got %A{other}"

    match Reads.issues (serving "[]") "FS-GG" "FS.GG.SDD" "open" None false with
    | Ok "[]" -> ()
    | other -> failwith $"a present-but-empty array is a real answer — got %A{other}"


// ---- the conditional landable reads (the `--wait` poll loop) ---------------------------------------
//
// `landable --wait` polls `prLandableRequire` up to 30 times at 20s intervals, and each poll reads THREE
// REST paths: the PR object, its head SHA's workflow runs, and its check-runs. That is ~90 REST calls per
// wait, per worker, per item — on the budget the whole fleet shares — and `pnext-item` drives a wait on
// every item. A poll that finds no change is exactly the 304 case, because "nothing has changed yet" is
// what waiting MEANS. So all three revalidate.
//
// What makes that safe is NOT that the reads are cheap. It is that a 304 is the server asserting the body
// we hold is current, and that the validator is only ever stored where it can stand for the WHOLE answer:
// a single resource, or a page with headroom (`Reads.memoisable`). These tests hold both lines.

/// A per-path ETag server. 200 + a path-derived validator on an unconditional read; 304 when the caller
/// sends that validator back — how GitHub answers `If-None-Match`. It RECORDS the validator every request
/// carried, per path, which is the one fact the fake's log grammar does not carry for these paths.
///
/// `runs` is how many workflow runs the page carries, which is how a test drives the headroom boundary.
/// `nextLink` makes every 200 advertise a next page.
type private LandableServer(sha: string, ?runs: int, ?nextLink: string) =
    let seen = System.Collections.Generic.List<string * string option>()
    let runCount = defaultArg runs 1

    /// `runCount` green runs, all in the same check suite — the page whose SIZE the headroom rule reads.
    let runsBody =
        let one (i: int) =
            $"""{{"path":".github/workflows/b%d{i}.yml","event":"pull_request","head_branch":"item/42-x","run_number":%d{i},"status":"completed","conclusion":"success","check_suite_id":1,"pull_requests":[{{"number":801}}]}}"""

        let items = [ 1..runCount ] |> List.map one |> String.concat ","
        $"""{{"total_count":%d{runCount},"workflow_runs":[%s{items}]}}"""

    let bodies =
        [ "repos/FS-GG/FS.GG.SDD/pulls/801",
          "{\"number\":801,\"state\":\"open\",\"mergeable\":true,\"head\":{\"ref\":\"item/42-x\",\"sha\":\""
          + sha
          + "\"}}"
          "repos/FS-GG/FS.GG.SDD/actions/runs", runsBody
          "repos/FS-GG/FS.GG.SDD/commits/" + sha + "/check-runs",
          """{"total_count":1,"check_runs":[{"name":"build","check_suite":{"id":1},"status":"completed","conclusion":"success"}]}""" ]

    /// The validator is derived from the path AND the sha, so a test can prove one subject's body is never
    /// served as another's — a WRONG answer, not merely a stale one, feeding a decision to merge.
    let etagOf (path: string) = $"W/\"%s{path}@%s{sha}\""

    member _.Validators(path: string) =
        seen |> Seq.filter (fun (p, _) -> p = path) |> Seq.map snd |> List.ofSeq

    member _.Recorder =
        Fake.Recorder(fun (req: Request) ->
            seen.Add(req.Path, req.IfNoneMatch)

            match bodies |> List.tryFind (fun (p, _) -> p = req.Path) with
            | None -> Error(NotFound req.Path)
            | Some(_, body) ->
                let etag = etagOf req.Path

                match req.IfNoneMatch with
                | Some e when e = etag -> Ok { Status = 304; Body = ""; ETag = Some etag; NextLink = None }
                | _ ->
                    Ok
                        { Status = 200
                          Body = body
                          ETag = Some etag
                          NextLink = nextLink })

/// The three reads of one `landable` poll.
let private pollPaths (sha: string) =
    [ "repos/FS-GG/FS.GG.SDD/pulls/801"
      "repos/FS-GG/FS.GG.SDD/actions/runs"
      $"repos/FS-GG/FS.GG.SDD/commits/%s{sha}/check-runs" ]

[<Fact>]
let ``every read of the landable poll revalidates on the second look, and the 304s reach the SAME verdict`` () =
    // THE WIN, AND ITS SAFETY ARGUMENT, IN ONE TEST. The first poll is unconditional and caches each body
    // with its validator; the second sends them back, is served 304s — and still scores GREEN. A cache that
    // changed the verdict would be a cache deciding whether to merge.
    use _cache = new IssuesCache()
    let server = LandableServer "sha-green"

    Assert.Equal(PrGreen, Reads.prLandable server.Recorder "FS-GG" "FS.GG.SDD" 801)
    Assert.Equal(PrGreen, Reads.prLandable server.Recorder "FS-GG" "FS.GG.SDD" 801)

    for path in pollPaths "sha-green" do
        match server.Validators path with
        | [ None; Some _ ] -> ()
        | other -> failwith $"%s{path}: poll one must be unconditional and poll two must revalidate — got %A{other}"

[<Fact>]
let ``a page with NO headroom is never memoised - the boundary the whole rule exists to refuse`` () =
    // THE PROOF'S EDGE. A page carrying exactly `per_page` items and no `Link` looks complete and is not
    // provably so: if the set later grows, the new items land on page two and page one can stay
    // byte-identical — so the server would answer 304 and we would serve a one-page body for a two-page set,
    // scoring a merge verdict over runs we never saw (#461). Only a page with HEADROOM (`n < per_page`)
    // guarantees that growth rewrites page one. So a full page stores no validator and the next poll pays.
    use _cache = new IssuesCache()
    let server = LandableServer("sha-green", runs = 100) // per_page is 100 — a full page, no headroom

    Reads.prLandable server.Recorder "FS-GG" "FS.GG.SDD" 801 |> ignore
    Reads.prLandable server.Recorder "FS-GG" "FS.GG.SDD" 801 |> ignore

    let conditional =
        server.Validators "repos/FS-GG/FS.GG.SDD/actions/runs" |> List.filter Option.isSome

    if not conditional.IsEmpty then
        failwith $"a FULL page cannot prove headroom and must not be memoised — got %A{conditional}"

    // ...while the same poll's PR object — a single resource, which cannot paginate — still revalidates. The
    // rule is per-subject, not a blanket retreat.
    match server.Validators "repos/FS-GG/FS.GG.SDD/pulls/801" with
    | [ None; Some _ ] -> ()
    | other -> failwith $"a single resource still revalidates — got %A{other}"

[<Fact>]
let ``a response that PAGINATES stores no validator, whatever its shape`` () =
    // A merged response's ETag is page one's alone. Storing it would revalidate a two-page set against its
    // first page — the hazard headroom exists to make unreachable, and this is the backstop under it.
    use _cache = new IssuesCache()
    let server = LandableServer("sha-green", nextLink = "https://api.github.com/x?page=2")

    Reads.prLandable server.Recorder "FS-GG" "FS.GG.SDD" 801 |> ignore
    Reads.prLandable server.Recorder "FS-GG" "FS.GG.SDD" 801 |> ignore

    for path in pollPaths "sha-green" do
        let conditional = server.Validators path |> List.filter Option.isSome

        if not conditional.IsEmpty then
            failwith $"%s{path}: a paginated response's page-one ETag must never be stored — got %A{conditional}"

[<Fact>]
let ``the runs cache is keyed on the head SHA - one commit's green is never served as another's`` () =
    // `actions/runs` is the SAME PATH for every commit; the SHA rides in the QUERY. Key on the path alone and
    // a force-push would be served the PREVIOUS commit's green — not a stale answer but a WRONG one, and what
    // it decides is whether to merge. So the cache key carries the query.
    use _cache = new IssuesCache()

    Reads.prLandable (LandableServer "sha-one").Recorder "FS-GG" "FS.GG.SDD" 801 |> ignore

    let second = LandableServer "sha-two"
    Assert.Equal(PrGreen, Reads.prLandable second.Recorder "FS-GG" "FS.GG.SDD" 801)

    match second.Validators "repos/FS-GG/FS.GG.SDD/actions/runs" with
    | [ None ] -> ()
    | other -> failwith $"a different head SHA must not reuse the previous commit's validator — got %A{other}"

[<Fact>]
let ``issues judges headroom on the RAW page, not on the filtered projection (#641)`` () =
    // THE SUBTLE ONE. `issues` caches a PROJECTION — pull requests dropped (#641) — but `memoisable` asks a
    // question about the PAGE the server sent and what its ETag stands for. Serve a FULL page of 100 raw
    // items that filters down to 60 issues: judged on the filtered body it would "prove" headroom (60 < 100)
    // and memoise a validator that cannot vouch for the set; judged on the raw page (100 = per_page, no
    // headroom) it must refuse. Getting this backwards would serve a one-page body for a two-page list once
    // a repo crossed 100 open issues — #461, laundered through our own filter, on a delay.
    use _cache = new IssuesCache()

    let raw =
        let issue (i: int) = "{\"number\":" + string i + "}"
        let pr (i: int) = "{\"number\":" + string i + ",\"pull_request\":{\"url\":\"u\"}}"
        // 60 issues + 40 PRs = a full page of 100.
        let items = [ for i in 1..60 -> issue i ] @ [ for i in 61..100 -> pr i ]
        "[" + String.concat "," items + "]"

    let seen = System.Collections.Generic.List<string option>()

    let recorder =
        Fake.Recorder(fun (req: Request) ->
            seen.Add req.IfNoneMatch
            Ok { Status = 200; Body = raw; ETag = Some "W/\"full-page\""; NextLink = None })

    match Reads.issues recorder "FS-GG" "FS.GG.SDD" "open" None false with
    | Ok body -> Assert.DoesNotContain("pull_request", body) // the projection still drops PRs
    | other -> failwith $"the listing must be returned — got %A{other}"

    Reads.issues recorder "FS-GG" "FS.GG.SDD" "open" None false |> ignore

    if seen |> Seq.exists Option.isSome then
        failwith
            $"a full RAW page has no headroom and must not be memoised, however few items survive the #641 filter — got %A{List.ofSeq seen}"

[<Fact>]
let ``a page we cannot COUNT is never memoised - headroom unproven is headroom refused`` () =
    // The fail-closed clause of the headroom rule. `memoisable` proves headroom by COUNTING the page; a body
    // that parses but is not shaped as the caller declared (here: no `workflow_runs` array) yields no count,
    // and no count means no proof. It must refuse rather than assume — the cost of not memoising is one paid
    // read, and the cost of assuming is a validator vouching for a set nobody measured.
    use _cache = new IssuesCache()
    let seen = System.Collections.Generic.List<string * string option>()

    let recorder =
        Fake.Recorder(fun (req: Request) ->
            seen.Add(req.Path, req.IfNoneMatch)

            let body =
                if req.Path.EndsWith "actions/runs" then
                    // Valid JSON, and countable by nobody: the declared `workflow_runs` array is absent.
                    """{"total_count":0}"""
                elif req.Path.EndsWith "check-runs" then
                    """{"total_count":1,"check_runs":[{"name":"build","check_suite":{"id":1},"status":"completed","conclusion":"success"}]}"""
                else
                    "{\"number\":801,\"state\":\"open\",\"mergeable\":true,\"head\":{\"ref\":\"item/42-x\",\"sha\":\"sha-x\"}}"

            Ok { Status = 200; Body = body; ETag = Some "W/\"v1\""; NextLink = None })

    Reads.prLandable recorder "FS-GG" "FS.GG.SDD" 801 |> ignore
    Reads.prLandable recorder "FS-GG" "FS.GG.SDD" 801 |> ignore

    let validatorsFor (needle: string) =
        seen
        |> Seq.filter (fun (p, _) -> p.EndsWith needle)
        |> Seq.map snd
        |> List.ofSeq

    // The uncountable runs page proved no headroom, so it stored nothing and BOTH polls went out
    // unconditional.
    match validatorsFor "actions/runs" with
    | [ None; None ] -> ()
    | other -> failwith $"an uncountable page must never be memoised — got %A{other}"

    // THE COUNTERWEIGHT, and it is what stops this passing for the wrong reason: the same poll's countable
    // reads DO revalidate. A blanket failure to memoise anything would satisfy the assertion above.
    match validatorsFor "check-runs" with
    | [ None; Some _ ] -> ()
    | other -> failwith $"a countable page with headroom must still revalidate — got %A{other}"
