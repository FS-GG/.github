namespace FS.GG.Coord.Cli.Tests

open System
open System.IO
open Xunit
open FS.GG.Coord.GitHub
open FS.GG.Coord.GitHub.Transport
open FS.GG.Coord.Cli

/// THE LIVENESS READ MAY NOT UNDER-REPORT (.github#1668).
///
/// **The incident.** Two observations of `.github#1594`, 32 seconds apart, disagreed about whether a claim
/// marker existed — and they disagreed in the direction that defeats the lock:
///
///   * `19:51:57Z` — `who --all-repos` → `.github#1594  UNCLAIMED — In progress with NO claim marker`,
///     plus `WARNING — … (someone is working outside the protocol).`
///   * `19:52:29Z` — `claim .github#1594 --force` → `STOLE .github#1594 from worker 'heron-99e5' (--force)`
///
/// The engine is the more authoritative of the two, and provably: `announceTheft` is reached only through
/// the eviction callback, which is invoked only after `evictLive` has DELETED a live marker (`Writes.fs`).
/// So a live `heron-99e5` marker existed 32 seconds after a read that reported none.
///
/// **What this file does and does not pin.** It does NOT pin a cause for that specific 32-second window;
/// the marker in question was destroyed by the very eviction that proved it existed, and no evidence
/// distinguishing a stale read from visibility lag survives. What it pins is the property that made the
/// disagreement DANGEROUS rather than merely puzzling, and that property was real, present, and provable in
/// the read path: **`who` could not tell "there is no claim here" from "I could not read this issue's
/// comments", and it rendered the second as the first — with an accusation attached.**
///
/// `Reads.markers` classified every comment into marker-or-nothing. A comment with no readable `body`, and a
/// comment whose body IS a claim marker but which carries no orderable `id`, both fell out of the list
/// silently. An issue whose only marker was one of those returned `Ok []`, and `Ok []` on an In-progress row
/// is exactly `UNCLAIMED — In progress with NO claim marker`. The old code even argued this away in a comment
/// that was false on its face — it claimed an unorderable marker "still blocks below", and nothing blocked.
///
/// That is #266's substitution ("I could not look" reported as "I looked, and it is empty") sitting inside
/// the one read that separates workers, and it is the same fail-open .github#1794 closed one function away in
/// `openIssues`. The direction is what makes it urgent: a `who` that under-reports invites a host to dispatch
/// a second worker onto held work, and turns `reap`/`adopt` from "collect the dead" into "collect the living".
///
/// **The pair is the gate.** Leg one is the repair; leg two is the thing the repair must not break. If they
/// ever agree again, either the fail-closed arm has been lost or `UNCLAIMED` has stopped being sayable about
/// a genuinely unheld item — and a `who` that cries wolf on every row is a `who` operators learn to ignore,
/// which costs the same double-dispatch by a longer road.
module WhoLivenessTests =

    let private ok (body: string) : Errors.IoResult<Response> =
        Ok
            { Status = 200
              Body = body
              ETag = None
              NextLink = None; Headers = Map.empty }

    /// One board, one row, `In progress` — the only column that licenses an `unclaimed` verdict at all, so
    /// the fixture must serve it or neither leg is testing what it says it is.
    let private graphqlAnswer (query: string) : string option =
        if query.Contains "projectsV2" then
            Some
                """{"data":{"organization":{"projectsV2":{"nodes":[{"number":12,"title":"Coordination","id":"PVT_coord"}]}}},"rateLimit":{"cost":1,"remaining":4977}}"""
        elif query.Contains "fields(first" then
            Some
                """{"data":{"organization":{"projectV2":{"fields":{"nodes":[{"id":"PVTSSF_status","name":"Status","dataType":"SINGLE_SELECT","options":[{"id":"opt_ready","name":"Ready"},{"id":"opt_wip","name":"In progress"}]}]}}}},"rateLimit":{"cost":1,"remaining":4977}}"""
        elif query.Contains "items(first" then
            Some
                """{"data":{"organization":{"projectV2":{"items":{"pageInfo":{"hasNextPage":false,"endCursor":null},"nodes":[{"status":{"name":"In progress"},"blockedBy":null,"content":{"__typename":"Issue","number":42,"title":"item 42","state":"OPEN","repository":{"nameWithOwner":"FS-GG/FS.GG.SDD"}}}]}}}},"rateLimit":{"cost":1,"remaining":4977}}"""
        else
            None

    /// A LIVE claim marker that the reader cannot place in the CAS's total order.
    ///
    /// `markerRe` matches — this is unambiguously `vole-418`'s claim, posted seconds ago — but the `id` is a
    /// STRING, so there is no orderable comment id. This is the ordering .github#1668 AC5 asks for, expressed
    /// in the one place the engine can actually be made to reproduce it: a marker that exists and a read that
    /// does not see it. Whether the live board produced this shape through a stale read, a visibility lag, or
    /// a truncated page does not change what `who` owes the operator when it happens.
    let private unorderableMarker =
        """[{"id":"9001","body":"<!-- fsgg:claim worker=vole-418 lease=120 -->\nheld","user":{"login":"EHotwagner"},"created_at":"2026-07-27T19:51:00Z","updated_at":"2026-07-27T19:51:00Z"}]"""

    /// The control: an In-progress item whose comment thread is EMPTY and completely readable. Nothing is
    /// hidden, so `UNCLAIMED` is the true answer and must survive the repair intact.
    let private noComments = "[]"

    /// The OTHER unreadable shape: a comment with no `body` field at all. We cannot say whether it was a
    /// marker, which is exactly why it may not be dropped — the `markerRe` test never even runs on it.
    let private noBodyComment =
        """[{"id":9002,"user":{"login":"EHotwagner"},"created_at":"2026-07-27T19:51:00Z","updated_at":"2026-07-27T19:51:00Z"}]"""

    /// A LAPSED marker (10h old against the 120m default lease) sitting beside a comment we could not read.
    ///
    /// This is the arm the first repair MISSED, and it is the dangerous one: the marker list is NOT empty,
    /// so classification reaches `Stale` and the read's incompleteness had nowhere to go. `STALE` is the row
    /// a human reads immediately before `reap`, and the hidden comment may be a LIVE marker sitting behind
    /// the lapsed one — in which case the claim is not dead and reaping it destroys a live lock.
    let private staleMarkerPlusUnreadable =
        let old = DateTimeOffset.UtcNow.AddHours(-10.0).ToString "yyyy-MM-ddTHH:mm:ssZ"

        $"""[{{"id":9003,"body":"<!-- fsgg:claim worker=heron-99e5 lease=120 -->\nheld","user":{{"login":"EHotwagner"}},"created_at":"%s{old}","updated_at":"%s{old}"}},{{"id":9004,"user":{{"login":"EHotwagner"}},"created_at":"%s{old}","updated_at":"%s{old}"}}]"""

    let private worldWith (board: string -> string option) (comments: string) =
        Fake.Recorder(fun (req: Request) ->
            let path = req.Path.Trim '/'

            match req.Method, path with
            | "POST", "graphql" ->
                match req.Body with
                | Query(document, _) ->
                    match board document with
                    | Some answer -> ok answer
                    | None -> Error(Errors.NotFound "the fixture serves no board WRITE — the lock is what is under test")
                | _ -> Error(Errors.NotFound "a graphql call with no document")
            | "GET", "rate_limit" -> ok """{"resources":{"graphql":{"remaining":4980,"limit":5000}}}"""
            // Arm B (the off-board open-issue scan) finds nothing, so the row under test reaches the
            // classifier through arm A — the In-progress board column — which is the arm that can say
            // `UNCLAIMED` and therefore the arm that could say it wrongly.
            | "GET", "repos/FS-GG/FS.GG.SDD/issues" -> ok "[]"
            | "GET", "repos/FS-GG/FS.GG.SDD/issues/42/comments" -> ok comments
            | "GET", "repos/FS-GG/FS.GG.SDD/issues/42" -> ok """{"number":42,"body":"Paths: src/Thing.fs"}"""
            | m, p -> Error(Errors.NotFound $"the fixture serves no %s{m} %s{p}"))

    /// The #1668 board: one OPEN row, `In progress`.
    let private world (comments: string) = worldWith graphqlAnswer comments

    let private context (transport: Fake.Recorder) : Client.Context =
        { Transport = transport
          Owner = "FS-GG"
          Title = "Coordination"
          DefaultRepo = Some "FS.GG.SDD"
          ChoreLocks = [] }

    /// Drive `Client.who` against a throwaway cache, on `ForceStealTests.runClaim`'s licence exactly:
    /// `AssemblyInfo.fs` disables cross-class parallelism, so the process-global `FSGG_COORD_CACHE` is safe
    /// to point somewhere private per call, and a fresh directory stops one leg passing on the other's reads.
    ///
    /// BOTH STREAMS ARE CAPTURED, because the two halves of this verb's answer are deliberately split across
    /// them: the row is stdout, and the warning — the accusation this issue is about — is stderr.
    let private runWho (transport: Fake.Recorder) (args: string list) : int * string * string =
        let dir = Path.Combine(Path.GetTempPath(), "fsgg-1668-" + Guid.NewGuid().ToString "n")
        let previousCache = Environment.GetEnvironmentVariable "FSGG_COORD_CACHE"
        let stdout = Console.Out
        let stderr = Console.Error
        use capturedOut = new StringWriter()
        use capturedErr = new StringWriter()

        try
            Directory.CreateDirectory dir |> ignore
            Environment.SetEnvironmentVariable("FSGG_COORD_CACHE", dir)
            Console.SetOut capturedOut
            Console.SetError capturedErr

            let opts =
                match Options.parse args with
                | Ok o -> o
                | Error e -> failwithf "the fixture's own argv did not parse: %s" e

            let code = Client.who (context transport) opts
            Console.Out.Flush()
            Console.Error.Flush()
            code, capturedOut.ToString(), capturedErr.ToString()
        finally
            Console.SetOut stdout
            Console.SetError stderr
            Environment.SetEnvironmentVariable("FSGG_COORD_CACHE", previousCache)

            try
                Directory.Delete(dir, true)
            with _ ->
                ()

    // ---- THE PAIR --------------------------------------------------------------------------------

    [<Fact>]
    let ``#1668 a marker the read cannot classify is NEVER reported as UNCLAIMED, and carries no accusation`` () =
        let transport = world unorderableMarker

        let code, out, err = runWho transport [ "who"; "--repo"; "FS.GG.SDD" ]

        Assert.Equal(0, code)

        // THE ASSERTION AC5 ASKS FOR, STATED NEGATIVELY BECAUSE THAT IS THE CONTRACT: whatever else this row
        // says, it does not say the item is unheld. This is the line that failed before the repair.
        Assert.DoesNotContain("UNCLAIMED", out)

        // ...and stated positively, so the row cannot pass by going silent. A row that vanished entirely
        // would satisfy the negative assertion and be WORSE than the bug — a held item omitted from the
        // liveness read altogether.
        Assert.Contains("FS.GG.SDD#42", out)
        Assert.Contains("UNDETERMINED", out)

        // THE ACCUSATION IS WITHHELD (AC2). "Someone is working outside the protocol" is a charge against a
        // person, and in the incident it was levelled at a worker holding a valid marker.
        Assert.DoesNotContain("working outside the protocol", err)

        // What replaces it must say the answer is a LOWER BOUND, not a fact...
        Assert.Contains("INCOMPLETE", err)
        Assert.Contains("LOWER BOUND", err)

        // ...and must name WHY, or an operator cannot act on it. The reason identifies the offending comment
        // and what was wrong with it.
        Assert.Contains("comment 0", err)
        Assert.Contains("`id`", err)

    [<Fact>]
    let ``#1668 a COMPLETE read of an In-progress item with no marker still says UNCLAIMED`` () =
        let transport = world noComments

        let code, out, err = runWho transport [ "who"; "--repo"; "FS.GG.SDD" ]

        Assert.Equal(0, code)

        // The fail-closed arm must be keyed on the read being INCOMPLETE, never on the marker list being
        // empty. Here nothing is hidden — an empty, fully-readable comment thread — so the verdict is a
        // genuine observation and #1668 must not have softened it into a shrug.
        Assert.Contains("UNCLAIMED", out)
        Assert.DoesNotContain("UNDETERMINED", out)

        // And case 20's warning still fires, on the row that genuinely earns it.
        Assert.Contains("working outside the protocol", err)

    [<Fact>]
    let ``#1668 --json gives the two facts DIFFERENT state words, not a cleverer worker field`` () =
        let undetermined = world unorderableMarker
        let unclaimed = world noComments

        let _, hidden, _ = runWho undetermined [ "who"; "--repo"; "FS.GG.SDD"; "--json" ]
        let _, plain, _ = runWho unclaimed [ "who"; "--repo"; "FS.GG.SDD"; "--json" ]

        // A machine consumer keying on `unclaimed` must not silently begin receiving rows that mean "could
        // not determine" — that is the same substitution #1668 is about, relocated into the wire contract.
        Assert.Contains("\"state\":\"undetermined\"", hidden)
        Assert.Contains("\"state\":\"unclaimed\"", plain)

        // BOTH have a null worker, which is precisely why `.state` had to carry the distinction: "nobody
        // holds it" and "I cannot say who holds it" have the same empty answer in that field.
        Assert.Contains("\"worker\":null", hidden)
        Assert.Contains("\"worker\":null", plain)

        // The reasons ride the row, so a consumer can report them without re-deriving anything — and the
        // ARRAY'S CONTENTS are asserted, not merely its presence. An empty `undetermined: []` would satisfy
        // a presence check while telling a consumer nothing, which is the shape of a vacuous gate.
        Assert.Contains("\"undetermined\":[", hidden)
        Assert.Contains("comment 0", hidden)

        // ...and appear ONLY there, so every existing unclaimed row stays byte-identical.
        Assert.DoesNotContain("\"undetermined\":[", plain)

    [<Fact>]
    let ``#1668 a comment with no readable body is unclassifiable too — not 'not a marker'`` () =
        let transport = world noBodyComment

        let code, out, err = runWho transport [ "who"; "--repo"; "FS.GG.SDD" ]

        Assert.Equal(0, code)

        // The `markerRe` test never runs on a comment whose body did not read, so "it is not a marker" is
        // not something the engine is in a position to say. Both unreadable shapes must fail closed, or the
        // repair only covers the half that happened to be noticed first.
        Assert.DoesNotContain("UNCLAIMED", out)
        Assert.Contains("UNDETERMINED", out)
        Assert.DoesNotContain("working outside the protocol", err)
        Assert.Contains("no readable `body`", err)

    [<Fact>]
    let ``#1668 a STALE row built from an incomplete read is flagged and is NOT offered to reap or adopt`` () =
        let transport = world staleMarkerPlusUnreadable

        let code, out, err = runWho transport [ "who"; "--repo"; "FS.GG.SDD" ]

        Assert.Equal(0, code)

        // The marker list is NOT empty here, so the row is legitimately STALE and must still say so — the
        // caveat qualifies the verdict, it does not replace it.
        Assert.Contains("STALE", out)

        // ...but the read was short, and `STALE` is the row a human reads immediately before `reap`. If the
        // hidden comment is a LIVE marker behind this lapsed one, the claim is not dead at all. Keying the
        // caveat on the STATE rather than on the READ is exactly the gap this leg exists to hold shut.
        Assert.Contains("INCOMPLETE", err)
        Assert.Contains("LOWER BOUND", err)
        Assert.Contains("stale", err)
        Assert.Contains("Do NOT dispatch a worker, `reap`, or `adopt`", err)

    [<Fact>]
    let ``#1668 --json carries the incompleteness on a STALE row, whose state word is still stale`` () =
        let transport = world staleMarkerPlusUnreadable

        let _, out, _ = runWho transport [ "who"; "--repo"; "FS.GG.SDD"; "--json" ]

        // The wire contract must carry the same pairing the human stream does: a real lock state, AND the
        // fact that the read behind it was short. A consumer that keys only on `.state` would reap this row.
        Assert.Contains("\"state\":\"stale\"", out)
        Assert.Contains("\"undetermined\":[", out)
        // NOTE the backtick-free substring: `Utf8JsonWriter`'s default encoder escapes ` as \u0060, so a
        // literal "`body`" would never match however correct the payload was — a gate that fails for a
        // reason with nothing to do with what it is testing.
        Assert.Contains("no readable", out)

    // ---- THE POST-MERGE WINDOW (.github#2225) ----------------------------------------------------
    //
    // The SAME substitution this file is about — "I could not look" rendered as "I looked, and it is
    // empty" — reached through a third route: not a comment the reader could not classify, but an issue
    // whose marker was never REQUESTED at all.
    //
    // `who`'s candidate set was arm A (board rows whose column is `In progress`) UNION arm B
    // (`Reads.openIssues`). An item whose PR has merged is CLOSED, so arm B cannot see it, and it has
    // usually already left `In progress`, so arm A cannot either. Its claim marker sat intact on the
    // issue and nothing ever read it — and the hardened per-candidate read below could not save it,
    // because a read nobody issues has no incompleteness to report. `who` answered EMPTY.
    //
    // That is worse than #1668's shape rather than milder: #1668 at least rendered a ROW. Here the held
    // item is absent from the answer entirely, which reads as "nothing in flight" to the one operator
    // who could act — and it happens in the window where the work is most valuable and least reversible.

    /// The post-merge board: one row, `In review` and CLOSED. Deliberately NOT `In progress` — that is
    /// arm A, which already worked; a fixture using it would pass without the repair.
    let private graphqlPostMerge (query: string) : string option =
        if query.Contains "projectsV2" then
            Some
                """{"data":{"organization":{"projectsV2":{"nodes":[{"number":12,"title":"Coordination","id":"PVT_coord"}]}}},"rateLimit":{"cost":1,"remaining":4977}}"""
        elif query.Contains "fields(first" then
            Some
                """{"data":{"organization":{"projectV2":{"fields":{"nodes":[{"id":"PVTSSF_status","name":"Status","dataType":"SINGLE_SELECT","options":[{"id":"opt_ready","name":"Ready"},{"id":"opt_wip","name":"In progress"},{"id":"opt_rev","name":"In review"},{"id":"opt_done","name":"Done"}]}]}}}},"rateLimit":{"cost":1,"remaining":4977}}"""
        elif query.Contains "items(first" then
            Some
                """{"data":{"organization":{"projectV2":{"items":{"pageInfo":{"hasNextPage":false,"endCursor":null},"nodes":[{"status":{"name":"In review"},"blockedBy":null,"content":{"__typename":"Issue","number":42,"title":"item 42","state":"CLOSED","repository":{"nameWithOwner":"FS-GG/FS.GG.SDD"}}}]}}}},"rateLimit":{"cost":1,"remaining":4977}}"""
        else
            None

    /// A LIVE, perfectly readable claim marker. Nothing here is hidden or malformed — the only reason it
    /// could go unreported is that nobody asked for it.
    let private liveMarker =
        let now = DateTimeOffset.UtcNow.ToString "yyyy-MM-ddTHH:mm:ssZ"

        $"""[{{"id":9005,"body":"<!-- fsgg:claim worker=curlew-8afd lease=120 -->\nheld","user":{{"login":"EHotwagner"}},"created_at":"%s{now}","updated_at":"%s{now}"}}]"""

    [<Fact>]
    let ``.github#2225 a live claim on a CLOSED, UNSTAMPED item is REPORTED - silence is not an answer`` () =
        let transport = worldWith graphqlPostMerge liveMarker

        let code, out, err = runWho transport [ "who"; "--repo"; "FS.GG.SDD" ]

        Assert.Equal(0, code)

        // THE LINE THAT FAILED BEFORE THE REPAIR. The row was absent entirely and `who` printed only its
        // scope header, which an operator reads as "nothing is in flight".
        Assert.Contains("FS.GG.SDD#42", out)

        // And it names the HOLDER, because "something is in flight" and "curlew-8afd is in flight, talk to
        // them" are different instructions (#428).
        Assert.Contains("curlew-8afd", out)

        // The claim is WITHIN its lease, so it is held — not stale, and emphatically not reapable. A row
        // that appeared but said STALE would invite `reap` on a live lock, which is a worse outcome than
        // the silence it replaced.
        Assert.DoesNotContain("STALE", out)

        // Closing is not a protocol violation, so no accusation rides along.
        Assert.DoesNotContain("working outside the protocol", err)

    [<Fact>]
    let ``.github#2225 a CLOSED item with NO marker is still silent - the repair widens the read, not the verdict`` () =
        let transport = worldWith graphqlPostMerge noComments

        let code, out, _ = runWho transport [ "who"; "--repo"; "FS.GG.SDD" ]

        Assert.Equal(0, code)

        // THE THING THE REPAIR MUST NOT BREAK. Arm C adds closed rows to the set of issues whose markers
        // are READ; it does not license a verdict about them. A markerless closed row is not in flight and
        // must not appear — and it must certainly never be `UNCLAIMED`, which is an accusation reserved for
        // an `In progress` column with no marker (work outside the protocol). A released claim on a merged
        // item is the ORDINARY end of the lifecycle, not a violation.
        Assert.DoesNotContain("FS.GG.SDD#42", out)
        Assert.DoesNotContain("UNCLAIMED", out)

    // ---- .github#2312: `who`'s Stale pick IS the exported lease-free ordering rule --------------------

    /// TWO LAPSED markers, posted in the order GitHub would have issued them.
    ///
    /// The higher id is served FIRST, so a reader that trusted its input's order rather than sorting would
    /// answer `late-9105`. Both are 10 hours old against the 120-minute default lease, so neither is live
    /// and `who` reaches the `Stale` arm — the one arm that consults the lease-free rule.
    let private twoLapsedMarkers =
        let old = DateTimeOffset.UtcNow.AddHours(-10.0).ToString "yyyy-MM-ddTHH:mm:ssZ"

        $"""[{{"id":9105,"body":"<!-- fsgg:claim worker=late-9105 lease=120 -->\nheld","user":{{"login":"EHotwagner"}},"created_at":"%s{old}","updated_at":"%s{old}"}},{{"id":9101,"body":"<!-- fsgg:claim worker=first-9101 lease=120 -->\nheld","user":{{"login":"EHotwagner"}},"created_at":"%s{old}","updated_at":"%s{old}"}}]"""

    [<Fact>]
    let ``.github#2312 who's STALE holder is the LOWEST-id marker - the rule it no longer re-implements`` () =
        // `who` used to hand-roll `List.sortBy (fun m -> m.Id) |> List.tryHead` for this arm; it now calls
        // `Reads.lowestId`. That is a source fact, and `OpLockTests` gates it as one — but a source gate
        // cannot show that the ANSWER still comes out right, and slice 2's acceptance asks for the
        // consumer's BEHAVIOUR to follow the exported rule rather than merely to import it.
        //
        // So this leg is the behavioural half: break `Reads.lowestId` and this reds, which is what "every
        // consumer's behaviour changes when the subject changes" means for `who` specifically.
        let transport = world twoLapsedMarkers

        let code, out, _ = runWho transport [ "who"; "--repo"; "FS.GG.SDD" ]

        Assert.Equal(0, code)
        Assert.Contains("STALE", out)

        // The CAS's total order names the FIRST-posted marker as the holder, whatever order the read
        // returned them in. Naming `late-9105` here would be the engine reporting the wrong worker as the
        // lock's owner — the row a human reads immediately before `reap`.
        Assert.Contains("first-9101", out)
        Assert.DoesNotContain("late-9105", out)
