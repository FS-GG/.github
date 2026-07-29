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
              NextLink = None }

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

    let private world (comments: string) =
        Fake.Recorder(fun (req: Request) ->
            let path = req.Path.Trim '/'

            match req.Method, path with
            | "POST", "graphql" ->
                match req.Body with
                | Query(document, _) ->
                    match graphqlAnswer document with
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
        Assert.Contains("not free", err)

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

        // The reasons ride the row, so a consumer can report them without re-deriving anything...
        Assert.Contains("\"undetermined\":[", hidden)

        // ...and appear ONLY there, so every existing unclaimed row stays byte-identical.
        Assert.DoesNotContain("\"undetermined\":[", plain)
