namespace FS.GG.Coord.Cli.Tests

open Xunit
open FS.GG.Coord
open FS.GG.Coord.Types
open FS.GG.Coord.GitHub
open FS.GG.Coord.GitHub.Transport
open FS.GG.Coord.Cli

module BlockerLintTests =

    let private ref' n : Ref = { Owner = "FS-GG"; Repo = ".github"; Number = n }

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

    // ---- `Blocked by` BODY-VS-FIELD divergence (.github#2079) ---------------------------------------
    //
    // `BLOCKED-NO-REASON` above reds ONLY when the field is EMPTY. This is its dual: the field is
    // NON-empty (so `BLOCKED-NO-REASON` is silent) but STALE — the `FS.GG.Templates#348` shape, where a
    // park's real edge landed as a `Blocked by:` body line instead of the field, and the field kept an
    // unrelated (here, fully-resolved) set. `blockedByBodyDivergence` is the ONE predicate `lint`'s
    // `BLOCKED-BY-INERT` and `reconcile`'s `BLOCKER-CLEARED` withholding both consult.

    [<Fact>]
    let ``no Blocked by body line is coherent — nothing to diverge from`` () =
        Assert.Equal<string list>(
            [],
            Client.blockedByBodyDivergence "FS-GG" "FS.GG.Templates" "FS-GG/FS.GG.Templates#345" "Paths: none"
        )

    [<Fact>]
    let ``the FS.GG.Templates#348 shape — a stale field and a body line naming a ref the field lacks`` () =
        // The field carries four CLOSED refs; the body's `Blocked by:` line names a FIFTH, different one
        // (the real, still-open blocker). The field does not carry it, so it is reported.
        let field = "FS-GG/.github#2068, FS-GG/FS.GG.Templates#345, FS-GG/FS.GG.Game#551, FS-GG/FS.GG.Templates#346"
        let body = "Unimplementable — the toolchain does not compile.\n\nBlocked by: FS-GG/FS.GG.Templates#370"

        Assert.Equal<string list>(
            [ "FS-GG/FS.GG.Templates#370" ],
            Client.blockedByBodyDivergence "FS-GG" "FS.GG.Templates" field body
        )

    [<Fact>]
    let ``an empty field with a body line still diverges — BLOCKED-NO-REASON structurally cannot see this`` () =
        Assert.Equal<string list>(
            [ "FS-GG/FS.GG.Templates#370" ],
            Client.blockedByBodyDivergence "FS-GG" "FS.GG.Templates" "" "Blocked by: FS-GG/FS.GG.Templates#370"
        )

    [<Fact>]
    let ``a body line that IS already in the field is coherent, not a divergence`` () =
        Assert.Equal<string list>(
            [],
            Client.blockedByBodyDivergence "FS-GG" "FS.GG.Templates" "FS-GG/FS.GG.Templates#370" "Blocked by: #370"
        )

    [<Fact>]
    let ``different SPELLINGS of the same ref canonicalize equal and do not diverge`` () =
        // A bare `#370` in the body and the field's fully-qualified rendering of the same issue must
        // compare equal — a divergence here would be a false positive on every ordinary park.
        Assert.Equal<string list>(
            [],
            Client.blockedByBodyDivergence "FS-GG" "FS.GG.Templates" "FS-GG/FS.GG.Templates#370" "Blocked by: #370"
        )

    [<Fact>]
    let ``prose in the body's Blocked by line is not a ref and draws no divergence`` () =
        // `Blockers.canonicalizeBlockedBy` refuses prose; a line this rule cannot canonicalize contributes
        // nothing to the comparison rather than being reported as a phantom ref.
        Assert.Equal<string list>(
            [],
            Client.blockedByBodyDivergence "FS-GG" "FS.GG.Templates" "" "Blocked by: RESOLVED, shipped last week"
        )

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
                  NextLink = None }

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

        let context (transport: Fake.Recorder) : Client.Context =
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

    // ---- `reconcile` withholds BLOCKER-CLEARED on the divergence (.github#2079, leg 2) ---------------
    //
    // `FS.GG.SDD#42` is `Blocked`, its FIELD names one blocker, `FS.GG.SDD#8`, which is CLOSED — so
    // `Chore.derive`'s own precondition (untouched by this issue) is satisfied on the field alone, and
    // `BLOCKER-CLEARED` would fire. The BODY's `Blocked by:` line names a DIFFERENT ref, `#9`. Whether
    // `#9` genuinely exists or is open is irrelevant to `blockedByBodyDivergence` — it is not a subset of
    // the field's set, and that is the whole predicate — so `reconcile` must withhold the promotion this
    // fixture would otherwise hand out.
    module private ReconcileWithholdFixture =

        let private ok (body: string) : Errors.IoResult<Response> =
            Ok
                { Status = 200
                  Body = body
                  ETag = None
                  NextLink = None }

        let private itemJson (n: int) (status: string) (blockedBy: string option) (state: string) =
            // `Scan.parseRow` reads `nested node "blockedBy" "text"` — the TEXT field's value is a
            // nested `{"text": "..."}` object on the wire, exactly as every other field is, NOT a bare
            // string. `null` (no value at all) is the shape an empty field takes.
            let blockedByJson =
                match blockedBy with
                | Some b -> $"""{{"text":"%s{b}"}}"""
                | None -> "null"

            $"""{{"status":{{"name":"%s{status}"}},"blockedBy":%s{blockedByJson},"content":{{"__typename":"Issue","number":%d{n},"title":"item %d{n}","state":"%s{state}","repository":{{"nameWithOwner":"FS-GG/FS.GG.SDD"}}}}}}"""

        /// `#42` is `Blocked` with field = `FS-GG/FS.GG.SDD#8`; `#8` is CLOSED, on-board — so the field
        /// alone is a satisfied `BLOCKER-CLEARED` precondition. `body42` is the only thing that varies.
        let transport (body42: string) =
            let items = [ itemJson 42 "Blocked" (Some "FS-GG/FS.GG.SDD#8") "OPEN"; itemJson 8 "Done" None "CLOSED" ] |> String.concat ","

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

        let private context (transport: Fake.Recorder) : Client.Context =
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
    let ``reconcile proposes BLOCKER-CLEARED when the body agrees with the field (or says nothing)`` () =
        let _, out, _ = ReconcileWithholdFixture.run "Paths: none"
        Assert.Contains("BLOCKER-CLEARED", out)

    [<Fact>]
    let ``reconcile WITHHOLDS BLOCKER-CLEARED on the FS.GG.Templates#348 shape — a body line the field lacks`` () =
        let _, out, err = ReconcileWithholdFixture.run "Blocked by: FS-GG/FS.GG.SDD#9"

        Assert.DoesNotContain("BLOCKER-CLEARED", out)
        Assert.Contains("withheld BLOCKER-CLEARED", err)
        Assert.Contains("FS-GG/FS.GG.SDD#9", err)
