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
    // The error is deliberately NOT propagated: the item it blocks stays blocked and explains itself, while
    // every other item on the board is still schedulable. Failing the whole scan on one bad ref would be
    // fail-closed in the wrong place — it would turn one unreachable issue into a dead queue.
    let recorder = failing (Http(500, "boom"))
    let result = Reads.blockerState recorder "FS-GG" "FS.GG.SDD" 8
    Assert.True(Result.isOk result)

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
    // the caller cannot tell an exhausted budget from an absent subject — and the remediation for the
    // second one CREATES A DUPLICATE BOARD ITEM.
    let recorder = failing (RateLimited None)

    match Reads.markers recorder "FS-GG" "FS.GG.SDD" 42 with
    | Error(RateLimited _) -> ()
    | other -> failwith $"an exhausted budget must not become an empty lock — got %A{other}"

    match Reads.issueBody recorder "FS-GG" "FS.GG.SDD" 42 with
    | Error(RateLimited _) -> ()
    | other -> failwith $"an exhausted budget must not become an empty body — got %A{other}"
