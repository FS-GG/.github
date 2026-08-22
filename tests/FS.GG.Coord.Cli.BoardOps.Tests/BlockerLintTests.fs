namespace FS.GG.Coord.Cli.Tests

open System.IO
open System.Text.RegularExpressions
open Xunit
open FS.GG.Coord
open FS.GG.Coord.Types
open FS.GG.Coord.GitHub
open FS.GG.Coord.GitHub.Transport
open FS.GG.Coord.Cli
open FS.GG.Coord.Cli.BoardOps

module BlockerLintTests =

    let private ref' n : Ref = { Owner = "FS-GG"; Repo = ".github"; Number = n }

    /// .github#2698 — a comment ledger holding one current delivery-route receipt for `subject`, as
    /// `Reads.commentBodies` reads it. Any fixture whose command RESOLVES a `Status=Ready` write needs
    /// one now; a `[]` ledger is the unschedulable-from-birth row the refusal exists to stop.
    let private routedLedger (subject: string) =
        System.Text.Json.JsonSerializer.Serialize
            [| {| id = 7900
                  body = StructuredFixtures.routeComment subject (Some DeliveryRoute.Lightweight) "fixture-route" None |} |]

    [<Fact>]
    let ``#2109 the Status=Blocked writer inventory is exhaustive and classifies every restore`` () =
        let deliberate, other =
            Client.blockedStatusWriterCoverage
            |> List.partition (function
                | Client.DeliberatePark _ -> true
                | _ -> false)

        let restores, impossible =
            other
            |> List.partition (function
                | Client.GuardedRestore _ -> true
                | _ -> false)

        Assert.Equal<string list>(
          [ "add --status Blocked";
              "intake apply Status=Blocked";
              "release --status Blocked";
              "set-field --batch Status=Blocked";
              "set-field Status Blocked" ],
            deliberate |> List.map (function Client.DeliberatePark name -> name | _ -> failwith "unreachable") |> List.sort
        )
        Assert.Equal<string list>(
            [ "reap (recorded previous Status=Blocked)"
              "release (recorded previous Status=Blocked)" ],
            restores |> List.map (function Client.GuardedRestore name -> name | _ -> failwith "unreachable") |> List.sort
        )
        Assert.Equal<string list>(
            [ "claim (Status=In progress)"; "done (Status=Done)" ],
            impossible |> List.map (function Client.CannotWriteBlocked name -> name | _ -> failwith "unreachable") |> List.sort
        )

        // The inventory is not a list tested against itself.  Count ALL transport shapes, not merely
        // the literal Status spellings: a generic `field/value` writer is precisely how reconcile
        // escaped the first inventory.  A new independent single/batch transport site now fails this
        // gate; routing through the shared boundary is the only way to avoid a new classification.
        let rec repoRoot dir =
            if File.Exists(Path.Combine(dir, "src/FS.GG.Coord.Cli/Client.fs")) then dir
            else repoRoot (Directory.GetParent(dir).FullName)

        let root = repoRoot (Directory.GetCurrentDirectory())
        let source =
            [ "src/FS.GG.Coord.Cli/Client.fs"
              "src/FS.GG.Coord.Cli.BoardOps/Handlers.fs"
              "src/FS.GG.Coord.Cli.Lifecycle/LiveHandlers.fs" ]
            |> List.map (fun path -> File.ReadAllText(Path.Combine(root, path)))
            |> String.concat "\n"
        let chore = File.ReadAllText(Path.Combine(root, "src/FS.GG.Coord.Core/Chore.fs"))
        let directStatusWrites =
            Regex.Matches(source, "Board\\.boardWrite[\\s\\S]{0,300}?\\\"Status\\\"").Count
        Assert.Equal(4, directStatusWrites)
        Assert.Equal(12, Regex.Matches(source, "Board\\.boardWrite\\b").Count)
        Assert.Equal(3, Regex.Matches(source, "Board\\.boardWriteBatch\\b").Count)
        Assert.Equal(3, Regex.Matches(source, "requireCoherentBlockedWrite ctx").Count)
        Assert.Equal(3, Regex.Matches(chore, "Some\\(\\\"Status\\\"").Count)
        Assert.Contains("LifecycleProjectionLag destination -> Some(\"Status\", statusWireName destination)", chore)
        Assert.Contains("PrematureCompletion destination -> Some(\"Status\", destination |> completionCorrectionStatus |> statusWireName)", chore)
        Assert.Contains("CompletionProjection -> Some(\"Status\", statusWireName Done)", chore)
        for retired in [ "StatusNotBlocked"; "BlockerCleared"; "ClosedIssueNotDone"; "ClaimStatusLag"; "ClaimReviewLag" ] do
            Assert.DoesNotContain(retired, chore)

        // ...AND THE CLI MAY NOT SPELL THAT COLUMN A SECOND TIME. `writesFor` builds BLOCKER-CLEARED's
        // two-field batch and used to hardcode `statusWireName FS.GG.Coord.Types.Ready` for the Status
        // half — a second answer to "what column does this rule write", harmless only while the Core's
        // answer was also an unconditional `Ready`. Once the Core began choosing `Backlog`, that literal
        // would have sent `Ready` anyway while the receipt printed `Backlog`: the board and its own
        // audit trail disagreeing, which is worse than the defect being repaired. The Status half now
        // comes from `write chore`, and this is the gate that keeps it there.
        Assert.DoesNotContain("statusWireName FS.GG.Coord.Types.Ready", source)

    [<Fact>]
    let ``BLOCKED-NO-REASON fires only for an unreasoned open blocked row`` () =
        let verdict body blockedBy =
            Client.blockedNoReasonVerdict IssueState.Open BoardStatus.Blocked blockedBy body

        Assert.True((verdict "Paths: src/A.fs" "").IsSome)
        Assert.True((verdict "Blocked on: human/decision" "").IsNone)
        Assert.True((verdict "Blocked on: human/action" "").IsNone)
        Assert.True((verdict "Paths: src/A.fs" "FS-GG/.github#2").IsNone)
        Assert.True((Client.blockedNoReasonVerdict IssueState.Closed BoardStatus.Blocked "" "").IsNone)

    [<Fact>]
    let ``#1739 human park is noted only after every machine blocker resolves`` () =
        let blocker state = { Ref = Some(ref' 2); Raw = ".github#2"; State = state }
        let verdict body blockers =
            Client.humanParkResolvedVerdict IssueState.Open BoardStatus.Blocked blockers body

        Assert.Contains("human decision", verdict "Blocked on: human/decision" [ blocker BlockerClosed ] |> Option.defaultValue "")
        Assert.Contains("human action", verdict "Blocked on: human/action" [ blocker BlockerMerged ] |> Option.defaultValue "")
        Assert.True((verdict "Blocked on: human/decision" [ blocker BlockerOpen ]).IsNone)
        Assert.True((verdict "Blocked on: human/decision" [ blocker BlockerUnknown ]).IsNone)
        Assert.True((verdict "Blocked on: human/decision" [ blocker BlockerUnparseable ]).IsNone)
        Assert.True((verdict "Blocked on: human/decision" []).IsNone)

    [<Fact>]
    let ``BLOCKER-CYCLE reports each member of a genuine ring and ignores a chain`` () =
        let a, b, c = ref' 1, ref' 2, ref' 3
        let openBlocker target = { Ref = Some target; Raw = target.Short; State = BlockerOpen }
        let ring = [ a, [ openBlocker b ]; b, [ openBlocker a ]; c, [ openBlocker b ] ]
        let findings = Client.blockerCycleVerdicts ring

        Assert.Equal<Ref list>([ a; b ], findings |> List.map fst |> List.sortBy (fun r -> r.Number))
        Assert.All(findings, fun (_, detail) -> Assert.Contains("cycle", detail))

    // ---- the WRITE-TIME park gate (.github#2079, AC1) ------------------------------------------------
    //
    // `requireCoherentParkIfBlocked` is the ONE gate `release --status Blocked` and
    // `set-field <ref> Status Blocked` both call — a no-op for every OTHER `--status`, and refused
    // BEFORE any write when the row would land with neither a non-empty `Blocked by` field nor a
    // `Blocked on: human/...` sentinel. These legs drive it against a fake transport, exactly as
    // `ForceStealTests`/`ApplicationServiceTests` drive their own gates — the pure verdict tests above
    // pin the PREDICATE; these pin that the CLI layer actually consults the live board with it.
    module private ParkGateFixture =

        let private ok (body: string) : Errors.IoResult<Response> =
            Ok
                { Status = 200
                  Body = body
                  ETag = None
                  NextLink = None; Headers = Map.empty }

        let subject: Ref =
            { Owner = "FS-GG"
              Repo = "FS.GG.SDD"
              Number = 42 }

        /// A board fixture: `Board.bootstrapCached` (project + fields) and `Board.itemBlockedBy` (the
        /// LIVE resolver read this gate makes), plus the REST body read for the sentinel check.
        /// `blockedByValue = None` is the field genuinely unset; `Some v` is a live non-empty field —
        /// and when it is `Some`, the body endpoint is deliberately UNSERVED, so a test asserting
        /// success on that path also proves the gate never read the body it did not need.
        let transport (blockedByValue: string option) (body: string) =
            Fake.Recorder(fun (req: Request) ->
                match req.Method, req.Path.Trim '/' with
                | "GET", "rate_limit" -> ok """{"resources":{"graphql":{"remaining":4980,"limit":5000}}}"""
                | "POST", "graphql" ->
                    match req.Body with
                    | Query(document, _) ->
                        if document.Contains "projectsV2" then
                            ok
                                """{"data":{"organization":{"projectsV2":{"nodes":[{"number":12,"title":"Coordination","id":"PVT_coord"}]}}},"rateLimit":{"cost":1,"remaining":4977}}"""
                        elif document.Contains "fields(first" then
                            ok
                                """{"data":{"organization":{"projectV2":{"fields":{"nodes":[{"id":"PVTSSF_status","name":"Status","dataType":"SINGLE_SELECT","options":[{"id":"opt_ready","name":"Ready"},{"id":"opt_blocked","name":"Blocked"}]},{"id":"PVTF_blocked","name":"Blocked by","dataType":"TEXT"}]}}}},"rateLimit":{"cost":1,"remaining":4977}}"""
                        elif document.Contains "\"Blocked by\"" then
                            match blockedByValue with
                            | Some v ->
                                ok
                                    $"""{{"data":{{"repository":{{"issue":{{"projectItems":{{"nodes":[{{"project":{{"number":12}},"fieldValueByName":{{"text":"%s{v}"}}}}]}}}}}}}},"rateLimit":{{"cost":1,"remaining":4977}}}}"""
                            | None ->
                                ok
                                    """{"data":{"repository":{"issue":{"projectItems":{"nodes":[{"project":{"number":12},"fieldValueByName":null}]}}}},"rateLimit":{"cost":1,"remaining":4977}}"""
                        else
                            Error(Errors.NotFound $"the fixture serves no answer for: %s{document}")
                    | _ -> Error(Errors.NotFound "a graphql call with no document")
                | "GET", "repos/FS-GG/FS.GG.SDD/issues/42" when blockedByValue.IsNone ->
                    ok (System.Text.Json.JsonSerializer.Serialize {| number = 42; body = body |})
                | m, p -> Error(Errors.NotFound $"the fixture serves no %s{m} %s{p} — a coherent field must never read the body"))

        let context (transport: Fake.Recorder) : Kernel.Context =
            { Transport = transport
              Owner = "FS-GG"
              Title = "Coordination"
              DefaultRepo = Some "FS.GG.SDD"
              ChoreLocks = [] }

        /// `Board.bootstrapCached` reads/writes `$FSGG_COORD_CACHE` — a real disk cache, unset in this
        /// process's ambient environment by default. Every leg below points it at a FRESH temp directory
        /// per call, exactly as `ForceStealTests.runClaim` and `ApplicationServiceTests.run` already do,
        /// so a board cached by an unrelated test (or a developer's real `.github` checkout) can never
        /// leak in and make one of these legs pass or fail on the wrong evidence.
        let run (blockedByValue: string option) (body: string) (requested: BoardStatus option) : Result<unit, int> =
            let dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "fsgg-2079-" + System.Guid.NewGuid().ToString "n")
            let previousCache = System.Environment.GetEnvironmentVariable "FSGG_COORD_CACHE"

            try
                System.IO.Directory.CreateDirectory dir |> ignore
                System.Environment.SetEnvironmentVariable("FSGG_COORD_CACHE", dir)
                let ctx = context (transport blockedByValue body)
                Client.requireCoherentParkIfBlocked ctx subject requested
            finally
                System.Environment.SetEnvironmentVariable("FSGG_COORD_CACHE", previousCache)

                try
                    System.IO.Directory.Delete(dir, true)
                with _ ->
                    ()

    [<Fact>]
    let ``release --status Blocked REFUSES when the field is empty and no sentinel is present`` () =
        match ParkGateFixture.run None "Paths: src/A.fs" (Some BoardStatus.Blocked) with
        | Ok() -> failwith "expected the park gate to refuse — the field is empty and the body has no sentinel"
        | Error code -> Assert.NotEqual(0, code)

    [<Fact>]
    let ``release --status Blocked PROCEEDS when a Blocked on: human/... sentinel is present`` () =
        Assert.Equal(Ok(), ParkGateFixture.run None "Blocked on: human/decision" (Some BoardStatus.Blocked))

    [<Fact>]
    let ``release --status Blocked PROCEEDS when the field already carries a ref, and never reads the body`` () =
        // The fixture refuses the body endpoint outright when `blockedByValue` is `Some` — so a passing
        // assertion here is also the proof that a non-empty field short-circuits before any body read.
        Assert.Equal(
            Ok(),
            ParkGateFixture.run (Some "FS-GG/FS.GG.SDD#9") "unreachable if the gate reads it" (Some BoardStatus.Blocked)
        )

    [<Fact>]
    let ``the gate is a no-op for every OTHER --status, and spends no GraphQL at all`` () =
        let dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "fsgg-2079-noop-" + System.Guid.NewGuid().ToString "n")
        let previousCache = System.Environment.GetEnvironmentVariable "FSGG_COORD_CACHE"

        try
            System.IO.Directory.CreateDirectory dir |> ignore
            System.Environment.SetEnvironmentVariable("FSGG_COORD_CACHE", dir)
            let unreachable = Fake.Recorder(fun _ -> Error(Errors.NotFound "the park gate must not call the transport here"))
            let ctx = ParkGateFixture.context unreachable

            Assert.Equal(Ok(), Client.requireCoherentParkIfBlocked ctx ParkGateFixture.subject None)
            Assert.Equal(Ok(), Client.requireCoherentParkIfBlocked ctx ParkGateFixture.subject (Some BoardStatus.Ready))
            Assert.Equal(Ok(), Client.requireCoherentParkIfBlocked ctx ParkGateFixture.subject (Some BoardStatus.InProgress))
        finally
            System.Environment.SetEnvironmentVariable("FSGG_COORD_CACHE", previousCache)

            try
                System.IO.Directory.Delete(dir, true)
            with _ ->
                ()

    // ---- the BATCH door onto the same park gate (.github#2098) ----------------------------------------
    //
    // `set-field --batch <ref> Status=Blocked "Blocked by=<ref>"` is ONE aliased mutation: AC1
    // (`.github#2098`) requires the whole document to validate before any alias is emitted, so there is
    // no "write the field, then read it back" step for the gate to borrow from `release`/single
    // `set-field`. `requireCoherentParkIfBlockedForBatch` is handed this batch's OWN pending `Blocked by`
    // write and must judge the pair coherent WITHOUT a live read that cannot see a mutation that has not
    // happened yet — these legs pin that it does, and that every other shape still defers to the
    // existing live-read gate unchanged.

    [<Fact>]
    let ``batch pairing Status=Blocked with a non-empty Blocked by in the SAME call PROCEEDS with no live read at all`` () =
        // An unreachable transport: if the wrapper fell through to the live-read gate here, this would
        // fail on the very first call instead of returning Ok — so a passing assertion is also the proof
        // that the pending pair short-circuited before any board read.
        let unreachable = Fake.Recorder(fun _ -> Error(Errors.NotFound "the batch park gate must not read the board when this batch's own pending write is coherent"))
        let ctx = ParkGateFixture.context unreachable

        Assert.Equal(
            Ok(),
            Client.requireCoherentParkIfBlockedForBatch
                ctx
                ParkGateFixture.subject
                (Some BoardStatus.Blocked)
                (Some(Board.Set "FS-GG/FS.GG.SDD#9"))
        )

    [<Fact>]
    let ``batch setting Status=Blocked with no Blocked by pair at all defers to the live gate, and refuses on an empty field with no sentinel`` () =
        let dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "fsgg-2098-batch-none-" + System.Guid.NewGuid().ToString "n")
        let previousCache = System.Environment.GetEnvironmentVariable "FSGG_COORD_CACHE"

        try
            System.IO.Directory.CreateDirectory dir |> ignore
            System.Environment.SetEnvironmentVariable("FSGG_COORD_CACHE", dir)
            let ctx = ParkGateFixture.context (ParkGateFixture.transport None "Paths: src/A.fs")

            match Client.requireCoherentParkIfBlockedForBatch ctx ParkGateFixture.subject (Some BoardStatus.Blocked) None with
            | Ok() -> failwith "expected the batch gate to defer to the live read and refuse — the field is empty and the body has no sentinel"
            | Error code -> Assert.NotEqual(0, code)
        finally
            System.Environment.SetEnvironmentVariable("FSGG_COORD_CACHE", previousCache)

            try
                System.IO.Directory.Delete(dir, true)
            with _ ->
                ()

    [<Fact>]
    let ``batch pairing Status=Blocked with a CLEARED Blocked by defers to the live gate — a clear is not a pending edge`` () =
        let dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "fsgg-2098-batch-clear-" + System.Guid.NewGuid().ToString "n")
        let previousCache = System.Environment.GetEnvironmentVariable "FSGG_COORD_CACHE"

        try
            System.IO.Directory.CreateDirectory dir |> ignore
            System.Environment.SetEnvironmentVariable("FSGG_COORD_CACHE", dir)
            let ctx = ParkGateFixture.context (ParkGateFixture.transport None "Paths: src/A.fs")

            match Client.requireCoherentParkIfBlockedForBatch ctx ParkGateFixture.subject (Some BoardStatus.Blocked) (Some Board.Clear) with
            | Ok() -> failwith "expected the batch gate to defer to the live read and refuse — a CLEAR leaves the field empty, same as no pair at all"
            | Error code -> Assert.NotEqual(0, code)
        finally
            System.Environment.SetEnvironmentVariable("FSGG_COORD_CACHE", previousCache)

            try
                System.IO.Directory.Delete(dir, true)
            with _ ->
                ()

    [<Fact>]
    let ``batch pairing Status=Blocked with a non-empty Blocked by PROCEEDS even when the live field is stale-empty`` () =
        // The fixture's live field is unset and its body has no sentinel — the live gate ALONE would
        // refuse this. A passing assertion proves the batch's own pending write is what carried it, not
        // a live board that happened to already agree.
        let dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "fsgg-2098-batch-pending-" + System.Guid.NewGuid().ToString "n")
        let previousCache = System.Environment.GetEnvironmentVariable "FSGG_COORD_CACHE"

        try
            System.IO.Directory.CreateDirectory dir |> ignore
            System.Environment.SetEnvironmentVariable("FSGG_COORD_CACHE", dir)
            let ctx = ParkGateFixture.context (ParkGateFixture.transport None "Paths: src/A.fs")

            Assert.Equal(
                Ok(),
                Client.requireCoherentParkIfBlockedForBatch
                    ctx
                    ParkGateFixture.subject
                    (Some BoardStatus.Blocked)
                    (Some(Board.Set "FS-GG/FS.GG.SDD#9"))
            )
        finally
            System.Environment.SetEnvironmentVariable("FSGG_COORD_CACHE", previousCache)

            try
                System.IO.Directory.Delete(dir, true)
            with _ ->
                ()

    [<Fact>]
    let ``the batch gate is a no-op for every OTHER --status, regardless of the pending Blocked by write`` () =
        let unreachable = Fake.Recorder(fun _ -> Error(Errors.NotFound "the batch park gate must not call the transport for a non-Blocked status"))
        let ctx = ParkGateFixture.context unreachable

        Assert.Equal(Ok(), Client.requireCoherentParkIfBlockedForBatch ctx ParkGateFixture.subject None None)
        Assert.Equal(Ok(), Client.requireCoherentParkIfBlockedForBatch ctx ParkGateFixture.subject (Some BoardStatus.Ready) (Some Board.Clear))
        Assert.Equal(
            Ok(),
            Client.requireCoherentParkIfBlockedForBatch ctx ParkGateFixture.subject (Some BoardStatus.InProgress) (Some(Board.Set "x"))
        )

    // ---- round 1 (independent review, PR #2103): a pending CLEAR must not trust a stale live field -----
    //
    // The round-1 defect: the wrapper's fallback arm treated "no `Blocked by` pair in this batch" and "a
    // pending CLEAR" identically, deferring both to the live-read gate. That is unsound for a CLEAR — the
    // live field is about to be overwritten to EMPTY by this SAME batch, so a live read that still finds
    // it non-empty is reading state the write itself already obsoletes. Reproduced end to end:
    // `set-field --batch <ref> Status=Blocked "Blocked by="` landed successfully whenever the live field
    // happened to still hold a stale ref — exactly the `Status=Blocked`-with-empty-field-and-no-sentinel
    // shape `.github#2079` exists to prevent. These legs pin the fix: a pending CLEAR consults ONLY the
    // sentinel, never the live field, via a transport that hard-refuses every GraphQL call.
    module private ClearedBlockedByFixture =

        let private ok (body: string) : Errors.IoResult<Response> =
            Ok
                { Status = 200
                  Body = body
                  ETag = None
                  NextLink = None; Headers = Map.empty }

        /// Serves ONLY the REST body read `requireSentinelIfBlockedByCleared` makes. Any GraphQL call —
        /// in particular the live `Blocked by` resolver read (`fieldValueByName`) — is a hard refusal, so
        /// a passing assertion below is also the proof that a pending CLEAR never consults the live field.
        let transport (body: string) =
            Fake.Recorder(fun (req: Request) ->
                match req.Method, req.Path.Trim '/' with
                | "GET", "repos/FS-GG/FS.GG.SDD/issues/42" ->
                    ok (System.Text.Json.JsonSerializer.Serialize {| number = 42; body = body |})
                | m, p ->
                    Error(Errors.NotFound $"a pending CLEAR must consult ONLY the body — got %s{m} %s{p}"))

    [<Fact>]
    let ``round 1: a pending Blocked by CLEAR REFUSES even when the LIVE field is stale non-empty, and never reads it`` () =
        // The critic's exact reproduction: no sentinel in the body, and — if the live field were consulted
        // — it would (wrongly) look coherent, because `ClearedBlockedByFixture` refuses that call outright
        // rather than serving a stale value. Under the round-1 defect this would never even reach the
        // assertion: the fallback to the live gate would have called it and the fixture's refusal would
        // surface as an unrelated `NotFound`, not the coherence refusal this pins.
        let ctx = ParkGateFixture.context (ClearedBlockedByFixture.transport "Paths: src/A.fs")

        match
            Client.requireCoherentParkIfBlockedForBatch
                ctx
                ParkGateFixture.subject
                (Some BoardStatus.Blocked)
                (Some Board.Clear)
        with
        | Ok() -> failwith "expected the batch gate to refuse — the body has no sentinel, and a pending CLEAR must not trust a stale live field"
        | Error code -> Assert.NotEqual(0, code)

    [<Fact>]
    let ``round 1: a pending Blocked by CLEAR PROCEEDS on a Blocked on: sentinel, still never touching the live field`` () =
        let ctx = ParkGateFixture.context (ClearedBlockedByFixture.transport "Blocked on: human/decision")

        Assert.Equal(
            Ok(),
            Client.requireCoherentParkIfBlockedForBatch
                ctx
                ParkGateFixture.subject
                (Some BoardStatus.Blocked)
                (Some Board.Clear)
        )

    // ---- AC2 (.github#2098): `set-field --batch` end to end, from raw argv to the aliased mutation ----
    //
    // The legs above pin `requireCoherentParkIfBlockedForBatch`'s PREDICATE. These drive
    // `Handlers.setField` itself — the whole family verb, argv to GraphQL — because the defect this issue is
    // about is in the WIRING inside `setFieldBatchCmd` (whether it computes the requested status and the
    // pending `Blocked by` write from the batch's own pairs and calls the gate at all), which only a
    // fixture that counts real GraphQL calls across the whole command can see. Same shape as
    // `ReleaseBlockedByFixture` above, for the batch door instead of `release`'s.
    module private SetFieldBatchParkFixture =

        let private ok (body: string) : Errors.IoResult<Response> =
            Ok
                { Status = 200
                  Body = body
                  ETag = None
                  NextLink = None; Headers = Map.empty }

        /// A full board fixture for `Handlers.setField --batch`: discovery (`projectsV2`, `fields(first`),
        /// the item-id lookup (`projectItems`), the aliased mutation itself
        /// (`updateProjectV2ItemFieldValue`), and — for the leg that must fall back to the LIVE gate — the
        /// `Blocked by` resolver read (`fieldValueByName`) and the REST issue body for the sentinel check.
        /// `body` is served on the REST read; it is UNREACHABLE on the leg where a same-call pending
        /// `Blocked by` write should short-circuit before any live read. `liveBlockedBy` answers the
        /// resolver read — `None` is the genuinely-empty field; `Some v` is a STALE non-empty value, for
        /// round 1's reproduction: a same-call CLEAR must not trust it.
        let private build (body: string) (liveBlockedBy: string option) =
            Fake.Recorder(fun (req: Request) ->
                match req.Method, req.Path.Trim '/' with
                | "GET", "rate_limit" -> ok """{"resources":{"graphql":{"remaining":4980,"limit":5000}}}"""
                | "GET", "repos/FS-GG/FS.GG.SDD/issues/42" ->
                    ok (System.Text.Json.JsonSerializer.Serialize {| number = 42; body = body |})
                | "POST", "graphql" ->
                    match req.Body with
                    | Query(document, _) ->
                        if document.Contains "projectsV2" then
                            ok
                                """{"data":{"organization":{"projectsV2":{"nodes":[{"number":12,"title":"Coordination","id":"PVT_coord"}]}}},"rateLimit":{"cost":1,"remaining":4977}}"""
                        elif document.Contains "fields(first" then
                            ok
                                """{"data":{"organization":{"projectV2":{"fields":{"nodes":[{"id":"PVTSSF_status","name":"Status","dataType":"SINGLE_SELECT","options":[{"id":"opt_ready","name":"Ready"},{"id":"opt_blocked","name":"Blocked"}]},{"id":"PVTF_blocked","name":"Blocked by","dataType":"TEXT"}]}}}},"rateLimit":{"cost":1,"remaining":4977}}"""
                        elif document.Contains "fieldValueByName" then
                            match liveBlockedBy with
                            | Some v ->
                                ok
                                    $"""{{"data":{{"repository":{{"issue":{{"projectItems":{{"nodes":[{{"project":{{"number":12}},"fieldValueByName":{{"text":"%s{v}"}}}}]}}}}}}}},"rateLimit":{{"cost":1,"remaining":4977}}}}"""
                            | None ->
                                ok
                                    """{"data":{"repository":{"issue":{"projectItems":{"nodes":[{"project":{"number":12},"fieldValueByName":null}]}}}},"rateLimit":{"cost":1,"remaining":4977}}"""
                        elif document.Contains "updateProjectV2ItemFieldValue" then
                            ok """{"data":{"f0":{"clientMutationId":null},"f1":{"clientMutationId":null}}}"""
                        // The item-id lookup shares the `projectItems` substring with the resolver read
                        // above — checked LAST, after both `fieldValueByName` and the mutation.
                        elif document.Contains "projectItems" then
                            ok
                                """{"data":{"repository":{"issue":{"projectItems":{"nodes":[{"id":"PVTI_42","project":{"number":12}}]}}}},"rateLimit":{"cost":1,"remaining":4977}}"""
                        else
                            Error(Errors.NotFound $"the fixture serves no answer for: %s{document}")
                    | _ -> Error(Errors.NotFound "a graphql call with no document")
                | m, p -> Error(Errors.NotFound $"the fixture serves no %s{m} %s{p}"))

        let transport (body: string) = build body None

        /// Round 1's reproduction fixture: the live `Blocked by` field is STALE non-empty. Used only by
        /// the leg that clears the field in the SAME batch — that write must be judged coherent (or not)
        /// on the sentinel alone, never on this stale value.
        let transportWithStaleLiveField (body: string) (staleValue: string) = build body (Some staleValue)

        let private sessionVars =
            [ "CLAUDE_CODE_SESSION_ID"; "OPENCODE_SESSION_ID"; "FSGG_AGENT_SESSION_ID"; "FSGG_WORKER" ]

        /// Drive `Handlers.setField` as a real command line, isolated on its own cache and identity — see
        /// `ReleaseBlockedByFixture.run`, whose shape this reuses.
        let run (transport: Fake.Recorder) (args: string list) : int * string * string =
            let dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "fsgg-2098-setfield-batch-" + System.Guid.NewGuid().ToString "n")
            let previousCache = System.Environment.GetEnvironmentVariable "FSGG_COORD_CACHE"
            let previousKitRoot = System.Environment.GetEnvironmentVariable "FSGG_KIT_ROOT"
            let previousSessions = sessionVars |> List.map (fun v -> v, System.Environment.GetEnvironmentVariable v)
            let stdout = System.Console.Out
            let stderr = System.Console.Error
            use capturedOut = new System.IO.StringWriter()
            use capturedErr = new System.IO.StringWriter()

            try
                System.IO.Directory.CreateDirectory dir |> ignore
                System.Environment.SetEnvironmentVariable("FSGG_COORD_CACHE", dir)
                System.Environment.SetEnvironmentVariable("FSGG_KIT_ROOT", dir)
                System.Environment.SetEnvironmentVariable("CLAUDE_CODE_SESSION_ID", null)
                System.Environment.SetEnvironmentVariable("OPENCODE_SESSION_ID", null)
                System.Environment.SetEnvironmentVariable("FSGG_AGENT_SESSION_ID", "2098setfieldbatch")
                System.Environment.SetEnvironmentVariable("FSGG_WORKER", "vole-2098")
                System.Console.SetOut capturedOut
                System.Console.SetError capturedErr

                let opts =
                    match Options.parse args with
                    | Ok o -> o
                    | Error e -> failwithf "the fixture's own argv did not parse: %s" e

                let context: Kernel.Context =
                    { Transport = transport
                      Owner = "FS-GG"
                      Title = "Coordination"
                      DefaultRepo = Some "FS.GG.SDD"
                      ChoreLocks = [] }

                let code = Handlers.setField context opts
                System.Console.Out.Flush()
                System.Console.Error.Flush()
                code, capturedOut.ToString(), capturedErr.ToString()
            finally
                System.Console.SetOut stdout
                System.Console.SetError stderr
                System.Environment.SetEnvironmentVariable("FSGG_COORD_CACHE", previousCache)
                System.Environment.SetEnvironmentVariable("FSGG_KIT_ROOT", previousKitRoot)

                for name, value in previousSessions do
                    System.Environment.SetEnvironmentVariable(name, value)

                try
                    System.IO.Directory.Delete(dir, true)
                with _ ->
                    ()

        let batchArgs (pairs: string list) = [ "set-field"; "--batch"; "FS.GG.SDD#42" ] @ pairs

    [<Fact>]
    let ``AC2: set-field --batch Status=Blocked ALONE — no other pair, empty field, no sentinel — is refused and writes nothing`` () =
        let transport = SetFieldBatchParkFixture.transport "Paths: src/A.fs"

        let code, _, err = SetFieldBatchParkFixture.run transport (SetFieldBatchParkFixture.batchArgs [ "Status=Blocked" ])

        Assert.NotEqual(0, code)
        Assert.Contains("2079", err)
        Assert.False(transport.Logged "updateProjectV2ItemFieldValue")

    [<Fact>]
    let ``AC2: set-field --batch Status=Blocked WITH 'Blocked by=<ref>' in the SAME call succeeds and writes both`` () =
        // The body carries no sentinel — if the fix wrongly fell through to a live read instead of
        // trusting this batch's own pending pair, this would refuse exactly like the leg above.
        let transport = SetFieldBatchParkFixture.transport "Paths: src/A.fs"

        let code, out, _ =
            SetFieldBatchParkFixture.run
                transport
                (SetFieldBatchParkFixture.batchArgs [ "Status=Blocked"; "Blocked by=FS-GG/FS.GG.SDD#9" ])

        Assert.Equal(0, code)
        Assert.Contains("Status = Blocked", out)
        Assert.Contains("Blocked by = FS-GG/FS.GG.SDD#9", out)
        Assert.True(transport.Logged "updateProjectV2ItemFieldValue")

    [<Fact>]
    let ``round 1: set-field --batch Status=Blocked with 'Blocked by=' (a same-call CLEAR) is refused even when the LIVE field is stale non-empty, and writes nothing`` () =
        // The critic's exact end-to-end reproduction against the round-1 head: the live field still names
        // a ref (`FS-GG/FS.GG.SDD#9`, stale), the body has no sentinel, and this SAME call clears the
        // field alongside `Status=Blocked`. Under the round-1 defect the wrapper deferred to the live
        // read, found the stale non-empty value, and wrongly returned Ok — this call landed, exit 0.
        let transport = SetFieldBatchParkFixture.transportWithStaleLiveField "Paths: src/A.fs" "FS-GG/FS.GG.SDD#9"

        let code, _, err =
            SetFieldBatchParkFixture.run transport (SetFieldBatchParkFixture.batchArgs [ "Status=Blocked"; "Blocked by=" ])

        Assert.NotEqual(0, code)
        Assert.Contains("2079", err)
        Assert.False(transport.Logged "updateProjectV2ItemFieldValue")

    // ---- #2143: field-write receipts preserve the explicit owner -----------------------------------

    module private SetFieldReceiptFixture =

        let private ok (body: string) : Errors.IoResult<Response> =
            Ok
                { Status = 200
                  Body = body
                  ETag = None
                  NextLink = None; Headers = Map.empty }

        /// Both owners have `rogue3#96` on the same board. The item lookup returns the owner-specific
        /// project item id, so the CLI fixture checks the mutation target and the receipt from one argv.
        let transport () =
            Fake.Recorder(fun (req: Request) ->
                match req.Method, req.Path.Trim '/' with
                | "GET", "rate_limit" -> ok """{"resources":{"graphql":{"remaining":4980,"limit":5000}}}"""
                | "POST", "graphql" ->
                    match req.Body with
                    | Query(document, variables) ->
                        if document.Contains "projectsV2" then
                            ok
                                """{"data":{"organization":{"projectsV2":{"nodes":[{"number":12,"title":"Coordination","id":"PVT_coord"}]}}},"rateLimit":{"cost":1,"remaining":4977}}"""
                        elif document.Contains "fields(first" then
                            ok
                                """{"data":{"organization":{"projectV2":{"fields":{"nodes":[{"id":"PVTSSF_status","name":"Status","dataType":"SINGLE_SELECT","options":[{"id":"opt_ready","name":"Ready"}]},{"id":"PVTF_blocked","name":"Blocked by","dataType":"TEXT"}]}}}},"rateLimit":{"cost":1,"remaining":4977}}"""
                        elif document.Contains "node(id: $projectId)" then
                            ok
                                """{"data":{"node":{"items":{"pageInfo":{"hasNextPage":false,"endCursor":null},"nodes":[{"id":"PVTI_default96","content":{"number":96,"repository":{"nameWithOwner":"FS-GG/rogue3"}}},{"id":"PVTI_external96","content":{"number":96,"repository":{"nameWithOwner":"EHotwagner/rogue3"}}}]}}}}"""
                        elif document.Contains "updateProjectV2ItemFieldValue" || document.Contains "clearProjectV2ItemFieldValue" then
                            if document.Contains "f0:" then
                                ok """{"data":{"f0":{"clientMutationId":null},"f1":{"clientMutationId":null}}}"""
                            else
                                ok """{"data":{"updateProjectV2ItemFieldValue":{"clientMutationId":null}}}"""
                        elif document.Contains "projectItems" then
                            let owner =
                                variables
                                |> List.tryPick (fun (name, value) ->
                                    match name, value with
                                    | "owner", VString text -> Some text
                                    | _ -> None)
                                |> Option.defaultValue ""

                            let item = if owner = "EHotwagner" then "PVTI_external96" else "PVTI_default96"

                            ok
                                $"""{{"data":{{"repository":{{"issue":{{"projectItems":{{"nodes":[{{"id":"%s{item}","project":{{"number":12}}}}]}}}}}}}},"rateLimit":{{"cost":1,"remaining":4977}}}}"""
                        else
                            Error(Errors.NotFound $"the receipt fixture serves no answer for: %s{document}")
                    | _ -> Error(Errors.NotFound "a graphql call with no document")
                // .github#2690: a landed `Status` write now also records the scheduling intent that keeps
                // it — a comment on the row. It is served here rather than asserted on: what these legs are
                // about is WHICH twin the mutation addressed, and the receipt is orthogonal to that. The
                // channel's own coverage lives in `ApplicationServiceTests` and `LifecycleProjectionTests`,
                // where the receipt is read back by the reconcile pass that used to revert it.
                | "POST", "repos/FS-GG/rogue3/issues/96/comments"
                | "POST", "repos/EHotwagner/rogue3/issues/96/comments" -> ok """{"id":9096}"""
                // .github#2698: `set-field <ref> Status Ready` — both spellings these legs drive — now
                // requires a CURRENT delivery-route receipt on the row it promotes. The subject is bound to
                // the canonical owner, so each twin gets its OWN receipt: a fixture that served one for
                // both would quietly agree with a gate that ignored the subject binding, which is the
                // failure `validateRouteLedger` exists to catch and the one these legs are already about.
                | "GET", "repos/FS-GG/rogue3/issues/96/comments" -> ok (routedLedger "FS-GG/rogue3#96")
                | "GET", "repos/EHotwagner/rogue3/issues/96/comments" -> ok (routedLedger "EHotwagner/rogue3#96")
                | m, p -> Error(Errors.NotFound $"the receipt fixture serves no %s{m} %s{p}"))

    [<Theory>]
    [<InlineData(false, "rogue3#96", "FS-GG/rogue3#96", "EHotwagner/rogue3#96", "PVTI_default96", "PVTI_external96")>]
    [<InlineData(false, "https://github.com/EHotwagner/rogue3/issues/96", "EHotwagner/rogue3#96", "FS-GG/rogue3#96", "PVTI_external96", "PVTI_default96")>]
    [<InlineData(true, "rogue3#96", "FS-GG/rogue3#96", "EHotwagner/rogue3#96", "PVTI_default96", "PVTI_external96")>]
    [<InlineData(true, "https://github.com/EHotwagner/rogue3/issues/96", "EHotwagner/rogue3#96", "FS-GG/rogue3#96", "PVTI_external96", "PVTI_default96")>]
    let ``#2143 single and batch receipts distinguish default and external same-name twins``
        (batch: bool)
        (refArg: string)
        (expectedRef: string)
        (otherRef: string)
        (expectedItem: string)
        (otherItem: string)
        =
        let transport = SetFieldReceiptFixture.transport ()

        let args =
            if batch then
                [ "set-field"; "--batch"; refArg; "Status=Ready"; "Blocked by=" ]
            else
                [ "set-field"; refArg; "Status"; "Ready" ]

        let code, out, err = SetFieldBatchParkFixture.run transport args

        Assert.Equal(0, code)
        Assert.Equal("", err)
        Assert.Contains(expectedRef, out)
        Assert.DoesNotContain(otherRef, out)
        Assert.True(transport.Logged expectedItem, $"mutation log: %A{transport.Log}")
        Assert.False(transport.Logged otherItem, $"mutation log: %A{transport.Log}")

    // ---- `reconcile` withholds BLOCKER-CLEARED on the divergence (.github#2079, leg 2) ---------------
    //
    // `FS.GG.SDD#42` is `Blocked`, its FIELD names one blocker, `FS.GG.SDD#8`, which is CLOSED. The two
    // bodies below differ only by legacy dependency prose. The lifecycle projection must remain identical:
    // body prose is no longer parsed back into dependency meaning.
    module private ReconcileWithholdFixture =

        let private ok (body: string) : Errors.IoResult<Response> =
            Ok
                { Status = 200
                  Body = body
                  ETag = None
                  NextLink = None; Headers = Map.empty }

        let private itemJson (n: int) (status: string) (blockedBy: string option) (state: string) (body: string option) =
            // `Scan.parseRow` reads `nested node "blockedBy" "text"` — the TEXT field's value is a
            // nested `{"text": "..."}` object on the wire, exactly as every other field is, NOT a bare
            // string. `null` (no value at all) is the shape an empty field takes.
            let blockedByJson =
                match blockedBy with
                | Some b -> $"""{{"text":"%s{b}"}}"""
                | None -> "null"

            let bodyJson = body |> Option.map System.Text.Json.JsonSerializer.Serialize |> Option.defaultValue "null"
            $"""{{"status":{{"name":"%s{status}"}},"blockedBy":%s{blockedByJson},"content":{{"__typename":"Issue","number":%d{n},"title":"item %d{n}","body":%s{bodyJson},"state":"%s{state}","repository":{{"nameWithOwner":"FS-GG/FS.GG.SDD"}}}}}}"""

        /// `#42` is `Blocked` with field = `FS-GG/FS.GG.SDD#8`; `#8` is CLOSED, on-board — so the field
        /// alone is a satisfied `BLOCKER-CLEARED` precondition. `body42` is the only thing that varies.
        let transport (body42: string) =
            let items = [ itemJson 42 "Blocked" (Some "FS-GG/FS.GG.SDD#8") "OPEN" (Some body42); itemJson 8 "Done" None "CLOSED" None ] |> String.concat ","

            Fake.Recorder(fun (req: Request) ->
                match req.Method, req.Path.Trim '/' with
                | "GET", "rate_limit" -> ok """{"resources":{"graphql":{"remaining":4980,"limit":5000}}}"""
                | "POST", "graphql" ->
                    match req.Body with
                    | Query(document, _) ->
                        if document.Contains "projectsV2" then
                            ok
                                """{"data":{"organization":{"projectsV2":{"nodes":[{"number":12,"title":"Coordination","id":"PVT_coord"}]}}},"rateLimit":{"cost":1,"remaining":4977}}"""
                        elif document.Contains "fields(first" then
                            ok
                                """{"data":{"organization":{"projectV2":{"fields":{"nodes":[{"id":"PVTSSF_status","name":"Status","dataType":"SINGLE_SELECT","options":[{"id":"opt_ready","name":"Ready"},{"id":"opt_blocked","name":"Blocked"}]},{"id":"PVTF_blocked","name":"Blocked by","dataType":"TEXT"}]}}}},"rateLimit":{"cost":1,"remaining":4977}}"""
                        elif document.Contains "items(first" then
                            ok
                                $"""{{"data":{{"organization":{{"projectV2":{{"items":{{"pageInfo":{{"hasNextPage":false,"endCursor":null}},"nodes":[%s{items}]}}}}}}}},"rateLimit":{{"cost":1,"remaining":4977}}}}"""
                        else
                            Error(Errors.NotFound $"the fixture serves no answer for: %s{document}")
                    | _ -> Error(Errors.NotFound "a graphql call with no document")
                // `#42` is the sole OPEN candidate — its body and markers ARE read; `#8` is CLOSED and
                // swept with neither.
                | "GET", "repos/FS-GG/FS.GG.SDD/issues/42" ->
                    ok (System.Text.Json.JsonSerializer.Serialize {| number = 42; body = body42 |})
                | "GET", "repos/FS-GG/FS.GG.SDD/issues/42/comments" -> ok "[]"
                | "GET", "repos/FS-GG/FS.GG.SDD/issues" -> ok "[]"
                // `#42`'s blockers are all resolved on the FIELD alone, so `Scan` probes for an open
                // `item/42-*` PR (.github#1738's `BLOCKER-CLEARED` candidate-set widening) before this
                // fixture's chore can even be derived. None open here.
                | "GET", "repos/FS-GG/FS.GG.SDD/pulls" -> ok "[]"
                // No open PR found the branch's proof-of-life is asked next (`Reads.itemBranchPushed`) —
                // `matching-refs` under the `item/42-` prefix, empty here too.
                | "GET", "repos/FS-GG/FS.GG.SDD/git/matching-refs/heads/item/42-" -> ok "[]"
                | m, p -> Error(Errors.NotFound $"the fixture serves no %s{m} %s{p}"))

        let private context (transport: Fake.Recorder) : Kernel.Context =
            { Transport = transport
              Owner = "FS-GG"
              Title = "Coordination"
              DefaultRepo = Some "FS.GG.SDD"
              ChoreLocks = [] }

        /// Run `reconcile --repo FS.GG.SDD --json` (dry run — no `--apply`, no worker needed), isolated
        /// on its own `$FSGG_COORD_CACHE`, and return the exit code plus stdout/stderr separately.
        let run (body42: string) : int * string * string =
            let dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "fsgg-2079-reconcile-" + System.Guid.NewGuid().ToString "n")
            let previousCache = System.Environment.GetEnvironmentVariable "FSGG_COORD_CACHE"
            let previousKitRoot = System.Environment.GetEnvironmentVariable "FSGG_KIT_ROOT"
            let stdout = System.Console.Out
            let stderr = System.Console.Error
            use capturedOut = new System.IO.StringWriter()
            use capturedErr = new System.IO.StringWriter()

            try
                System.IO.Directory.CreateDirectory dir |> ignore
                System.Environment.SetEnvironmentVariable("FSGG_COORD_CACHE", dir)
                System.Environment.SetEnvironmentVariable("FSGG_KIT_ROOT", dir)
                System.Console.SetOut capturedOut
                System.Console.SetError capturedErr

                let opts =
                    match Options.parse [ "reconcile"; "--repo"; "FS.GG.SDD"; "--json" ] with
                    | Ok o -> o
                    | Error e -> failwithf "the fixture's own argv did not parse: %s" e

                let code = Client.reconcile (context (transport body42)) opts
                System.Console.Out.Flush()
                System.Console.Error.Flush()
                code, capturedOut.ToString(), capturedErr.ToString()
            finally
                System.Console.SetOut stdout
                System.Console.SetError stderr
                System.Environment.SetEnvironmentVariable("FSGG_COORD_CACHE", previousCache)
                System.Environment.SetEnvironmentVariable("FSGG_KIT_ROOT", previousKitRoot)

                try
                    System.IO.Directory.Delete(dir, true)
                with _ ->
                    ()

    [<Fact>]
    let ``reconcile routes a cleared blocker through the sole lifecycle reducer`` () =
        // A REAL touch-set. This was `Paths: none` as filler — the subject is the #2079 withhold control,
        // not the touch-set — and since .github#2220 that body puts the row on the `Backlog` path. The
        // assertion below still held there, so nothing failed; it had simply stopped being the ORDINARY
        // cleared row this control is the positive half of.
        let _, out, _ = ReconcileWithholdFixture.run "Paths: src/A.fs"
        Assert.Contains("LIFECYCLE-PROJECTION-LAG", out)
        Assert.DoesNotContain("BLOCKER-CLEARED", out)

    [<Fact>]
    let ``an inert body blocker never resurrects the retired reducer`` () =
        let _, out, err = ReconcileWithholdFixture.run "Blocked by: FS-GG/FS.GG.SDD#9"

        Assert.DoesNotContain("BLOCKER-CLEARED", out)
        Assert.Contains("LIFECYCLE-PROJECTION-LAG", out)
        Assert.True(System.String.IsNullOrWhiteSpace err, err)

    // ---- `release --blocked-by` end to end (.github#2079 round-1 review, finding 2) -----------------
    //
    // `writeBlockedByIfRequested` performs a LIVE board write. It must never run for a caller who does
    // not hold the row — `release <ref> --blocked-by <x>` reaches `writeBlockedByIfRequested` on argv
    // whether or not the caller holds anything, so the write has to be gated on the SAME holder check
    // `release` already makes, not ahead of it. These legs drive `Client.release` itself (not the pure
    // gate above), because the defect was in the CALL ORDER inside `release`, which only a fixture that
    // actually counts GraphQL calls across the whole command can see.
    module private ReleaseBlockedByFixture =

        let private ok (body: string) : Errors.IoResult<Response> =
            Ok
                { Status = 200
                  Body = body
                  ETag = None
                  NextLink = None; Headers = Map.empty }

        /// A live claim marker, or none — `Writes.verifyHeld`'s subject, `ForceStealTests.Thread` scaled
        /// down to what this fixture needs (no POST, since `release` never adds a comment).
        type private Thread(holder: string option) =
            let comments = System.Collections.Generic.Dictionary<int64, string>()

            do
                match holder with
                | Some w -> comments.[8042L] <- $"<!-- fsgg:claim worker=%s{w} lease=120 -->"
                | None -> ()

            member _.Json() =
                let ts = System.DateTime.UtcNow.ToString "yyyy-MM-ddTHH:mm:ssZ"

                comments
                |> Seq.sortBy (fun kv -> kv.Key)
                |> Seq.map (fun kv ->
                    $"""{{"id":%d{kv.Key},"body":"%s{kv.Value}","user":{{"login":"EHotwagner"}},"created_at":"%s{ts}","updated_at":"%s{ts}"}}""")
                |> String.concat ","
                |> sprintf "[%s]"

            member _.Remove(id: int64) = comments.Remove id |> ignore

        /// The NON-HOLDER fixture. It answers ONLY `rate_limit` and the marker read — every OTHER
        /// endpoint, GraphQL included, is a hard refusal. If the fix is correct, `release` never reaches
        /// any of them: it refuses at `Writes.verifyHeld` before attempting a single board read or write.
        let nonHolderTransport (heldBy: string option) =
            let thread = Thread(heldBy)

            Fake.Recorder(fun (req: Request) ->
                match req.Method, req.Path.Trim '/' with
                | "GET", "rate_limit" -> ok """{"resources":{"graphql":{"remaining":4980,"limit":5000}}}"""
                | "GET", "repos/FS-GG/FS.GG.SDD/issues/42/comments" -> ok (thread.Json())
                | m, p -> Error(Errors.NotFound $"the non-holder leg must touch NOTHING else — got %s{m} %s{p}"))

        /// The HOLDER fixture: a full board (bootstrap, the `Blocked by` resolver read, the item-id
        /// lookup, the field mutation, the marker delete) so `release --blocked-by --status Blocked` can
        /// run to completion. `body` carries the `Blocked on:` sentinel, so the post-write coherence
        /// check passes on the BODY path — this fixture is static and does not model the just-written
        /// field's new value coming back on a re-read.
        let holderTransport (body: string) =
            let thread = Thread(Some "vole-418")

            Fake.Recorder(fun (req: Request) ->
                match req.Method, req.Path.Trim '/' with
                | "GET", "rate_limit" -> ok """{"resources":{"graphql":{"remaining":4980,"limit":5000}}}"""
                | "GET", "repos/FS-GG/FS.GG.SDD/issues/42/comments" -> ok (thread.Json())
                | "DELETE", p when p.StartsWith "repos/FS-GG/FS.GG.SDD/issues/comments/" ->
                    thread.Remove(int64 (p.Substring(p.LastIndexOf '/' + 1)))
                    ok ""
                | "GET", "repos/FS-GG/FS.GG.SDD/issues/42" ->
                    ok (System.Text.Json.JsonSerializer.Serialize {| number = 42; body = body |})
                | "POST", "graphql" ->
                    match req.Body with
                    | Query(document, _) ->
                        if document.Contains "projectsV2" then
                            ok
                                """{"data":{"organization":{"projectsV2":{"nodes":[{"number":12,"title":"Coordination","id":"PVT_coord"}]}}},"rateLimit":{"cost":1,"remaining":4977}}"""
                        elif document.Contains "fields(first" then
                            ok
                                """{"data":{"organization":{"projectV2":{"fields":{"nodes":[{"id":"PVTSSF_status","name":"Status","dataType":"SINGLE_SELECT","options":[{"id":"opt_ready","name":"Ready"},{"id":"opt_blocked","name":"Blocked"}]},{"id":"PVTF_blocked","name":"Blocked by","dataType":"TEXT"}]}}}},"rateLimit":{"cost":1,"remaining":4977}}"""
                        elif document.Contains "fieldValueByName" then
                            ok
                                """{"data":{"repository":{"issue":{"projectItems":{"nodes":[{"project":{"number":12},"fieldValueByName":null}]}}}},"rateLimit":{"cost":1,"remaining":4977}}"""
                        elif document.Contains "updateProjectV2ItemFieldValue" then
                            ok """{"data":{"updateProjectV2ItemFieldValue":{"clientMutationId":null}}}"""
                        // The item-id lookup: `projectItems`, but with NEITHER `fieldValueByName` (that
                        // arm above) NOR the mutation — checked last, after both.
                        elif document.Contains "projectItems" then
                            ok
                                """{"data":{"repository":{"issue":{"projectItems":{"nodes":[{"id":"PVTI_42","project":{"number":12}}]}}}},"rateLimit":{"cost":1,"remaining":4977}}"""
                        else
                            Error(Errors.NotFound $"the fixture serves no answer for: %s{document}")
                    | _ -> Error(Errors.NotFound "a graphql call with no document")
                | m, p -> Error(Errors.NotFound $"the fixture serves no %s{m} %s{p}"))

        let sessionVars =
            [ "CLAUDE_CODE_SESSION_ID"; "OPENCODE_SESSION_ID"; "FSGG_AGENT_SESSION_ID"; "FSGG_WORKER" ]

        /// Drive `Client.release` as a real command line, isolated on its own cache and identity — see
        /// `ForceStealTests.runClaim`, whose licence (`AssemblyInfo.fs` disables cross-class parallelism)
        /// this reuses.
        let run (transport: Fake.Recorder) (args: string list) : int * string =
            let dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "fsgg-2079-release-" + System.Guid.NewGuid().ToString "n")
            let previousCache = System.Environment.GetEnvironmentVariable "FSGG_COORD_CACHE"
            let previousKitRoot = System.Environment.GetEnvironmentVariable "FSGG_KIT_ROOT"
            let previousSessions = sessionVars |> List.map (fun v -> v, System.Environment.GetEnvironmentVariable v)
            let stdout = System.Console.Out
            use captured = new System.IO.StringWriter()

            try
                System.IO.Directory.CreateDirectory dir |> ignore
                System.Environment.SetEnvironmentVariable("FSGG_COORD_CACHE", dir)
                System.Environment.SetEnvironmentVariable("FSGG_KIT_ROOT", dir)
                System.Environment.SetEnvironmentVariable("CLAUDE_CODE_SESSION_ID", null)
                System.Environment.SetEnvironmentVariable("OPENCODE_SESSION_ID", null)
                System.Environment.SetEnvironmentVariable("FSGG_AGENT_SESSION_ID", "ed60050b")
                System.Environment.SetEnvironmentVariable("FSGG_WORKER", "vole-418")
                System.Console.SetOut captured

                let opts =
                    match Options.parse args with
                    | Ok o -> o
                    | Error e -> failwithf "the fixture's own argv did not parse: %s" e

                let context: Kernel.Context =
                    { Transport = transport
                      Owner = "FS-GG"
                      Title = "Coordination"
                      DefaultRepo = Some "FS.GG.SDD"
                      ChoreLocks = [] }

                let code = Client.release context opts
                System.Console.Out.Flush()
                code, captured.ToString()
            finally
                System.Console.SetOut stdout
                System.Environment.SetEnvironmentVariable("FSGG_COORD_CACHE", previousCache)
                System.Environment.SetEnvironmentVariable("FSGG_KIT_ROOT", previousKitRoot)

                for name, value in previousSessions do
                    System.Environment.SetEnvironmentVariable(name, value)

                try
                    System.IO.Directory.Delete(dir, true)
                with _ ->
                    ()

        let releaseArgs (extra: string list) =
            [ "release"; "FS.GG.SDD#42"; "--worker"; "vole-418" ] @ extra

    [<Fact>]
    let ``release --blocked-by from a NON-HOLDER mutates NOTHING, and refuses`` () =
        // Held by a DIFFERENT worker — `snipe-f893`, never `vole-418` — so `Writes.verifyHeld` answers
        // `DoesNotHold`. Under the round-1 defect this fixture would reach the (refused) GraphQL calls
        // BEFORE that answer; under the fix it never spends a single GraphQL point.
        let transport = ReleaseBlockedByFixture.nonHolderTransport (Some "snipe-f893")

        let code, _ =
            ReleaseBlockedByFixture.run transport (ReleaseBlockedByFixture.releaseArgs [ "--blocked-by"; "FS-GG/FS.GG.SDD#9" ])

        Assert.NotEqual(0, code)
        Assert.Equal(0, transport.GraphQlCalls)

    [<Fact>]
    let ``release --blocked-by from a worker holding NOTHING (no marker at all) also mutates NOTHING`` () =
        let transport = ReleaseBlockedByFixture.nonHolderTransport None

        let code, _ =
            ReleaseBlockedByFixture.run transport (ReleaseBlockedByFixture.releaseArgs [ "--blocked-by"; "FS-GG/FS.GG.SDD#9" ])

        Assert.NotEqual(0, code)
        Assert.Equal(0, transport.GraphQlCalls)

    [<Fact>]
    let ``release --blocked-by from the HOLDER lands both the field write and the release`` () =
        let transport = ReleaseBlockedByFixture.holderTransport "Blocked on: human/decision"

        let code, out =
            ReleaseBlockedByFixture.run
                transport
                (ReleaseBlockedByFixture.releaseArgs [ "--status"; "Blocked"; "--blocked-by"; "FS-GG/FS.GG.SDD#9" ])

        Assert.Equal(0, code)
        Assert.Contains("released", out)
        // The field write DID happen — this is the positive half of the pair, proving the reordering
        // did not turn into an over-correction that refuses a legitimate holder too.
        Assert.True(transport.Logged "updateProjectV2ItemFieldValue" || transport.Logged "item-edit")
