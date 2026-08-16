namespace FS.GG.Coord.Cli.Tests

open Xunit
open FS.GG.Coord
open FS.GG.Coord.GitHub
open FS.GG.Coord.Cli

/// `take`'s EXIT CONTRACT, PINNED TO THE ENGINE THAT RETURNS IT (#889).
///
/// `Protocol.takeExitCodes` is prose — hand-authored strings, like every other fact in `Protocol.fs`.
/// Generating a document from prose only guarantees that the copies AGREE; it does not make them TRUE.
/// The copies of `take`'s table agreed with each other for as long as they existed, and were wrong the
/// whole time: `/pnext-item` §1 documented `EX_PARTIAL` as `take` failing to READ the board, where
/// `Errors.ExPartial` is a WRITE that half-landed and `take` cannot return it at all.
///
/// So this file is the half the generator cannot do. It ties each documented number to the CONSTANT the
/// engine returns rather than to a digit, so retyping `ExitNone` as 8 fails here instead of shipping a
/// table that lies.
///
/// WHAT #918 CLOSED, AND WHAT IT DID NOT. There is now a union — `FS.GG.Coord.ExitCode` — with one
/// `toInt` and a declared per-command domain (`takeCodes`/`landableCodes`), and `Core.Tests` checks
/// `Protocol.takeExitCodes`/`landableExitCodes` COMPLETE against that domain. So a code the command's
/// declared return set contains, but the table omits, now fails a gate — the defect that shipped the
/// first table with no row for `ExitRed`. What is STILL hand-derived is the domain list itself: `take`'s
/// handler returns an int, not an `ExitCode`, so the compiler cannot force `takeCodes` to equal the set
/// of arms `Client.take` actually reaches. The reachability rows below remain a reading of `Client.take`,
/// now cross-checked against `takeCodes`. Full behavioural totality needs `take` to RETURN the union —
/// a further refactor, left for when `Client.fs` is free (a live claim held it as this landed).
///
/// It lives in `Cli.Tests` for a dependency reason: the codes are declared in `Cli`/`GitHub`, which
/// reference `Core`, so `Protocol.fs` cannot see them. `Cli.Tests` references `Cli` and therefore sees
/// both ends of the claim. `Core.Tests` covers the shape of the list; only here can it be checked
/// against the engine.
module ExitContractTests =

    let private codeFor (name: string) =
        Protocol.takeExitCodes
        |> List.tryFind (fun c -> c.Name = name)
        |> Option.map (fun c -> c.Code)

    let private documented (code: int) =
        Protocol.takeExitCodes |> List.exists (fun c -> c.Code = code)

    /// The named codes — the ones a worker greps for, and the ones #585 bought.
    [<Fact>]
    let ``the documented EX_* codes are the literals the engine returns`` () =
        Assert.Equal(Some Client.ExitNone, codeFor "EX_NONE")
        Assert.Equal(Some Client.ExitContended, codeFor "EX_CONTENDED")
        Assert.Equal(Some Errors.ExRate, codeFor "EX_RATE")

    /// The unnamed ones. `take` reaches 0 and `ExitContended` directly, and `ExitError` through `fail`
    /// on a board it could not read (`Client.fs`, #585's arm).
    [<Fact>]
    let ``the documented success and read-failure codes are the engine's`` () =
        Assert.True(documented Client.ExitGreen, "0 (claimed) is not documented")
        Assert.True(documented Client.ExitError, "1 (could not read the board) is not documented")

    /// EX_PARTIAL IS NOT IN `take`'S CONTRACT, AND DOCUMENTING IT WAS #889.
    ///
    /// `Errors.ExPartial` is a `set-field --batch` outcome (constructed at exactly one site, in
    /// `Board.setFieldBatch`): the board is half-written and NOTHING is queued. `take`'s only failure
    /// arms are `fail` over a pure READ, which cannot produce it, and the claim CAS, which is not a
    /// batch write. A table that lists it under `take` sends a worker to retry a board that somebody
    /// else's command has already half-written.
    ///
    /// KEYED ON THE NAME, NOT THE NUMBER, and that distinction was load-bearing when `Errors.ExPartial`
    /// and `Client.ExitNoVerdict` were BOTH 4 (the collision #918 has since fixed — they are now 9 and 4).
    /// The name is the right key anyway: it is the semantic identity, and asserting "9 is undocumented"
    /// would forbid nothing useful, whereas "the meaning EX_PARTIAL is not a `take` outcome" is the true
    /// claim. Asserting "4 is undocumented" would ALSO have forbidden
    /// documenting a NoVerdict, which `renderDecision` has a live arm for and `take` would propagate
    /// the day `Batch.schedule` grows a NoVerdict leg. The defect was the NAME and its meaning, so that
    /// is what this refuses.
    [<Fact>]
    let ``EX_PARTIAL is not documented as a take outcome`` () =
        Assert.DoesNotContain("EX_PARTIAL", Protocol.takeExitCodes |> List.map (fun c -> c.Name))

        for c in Protocol.takeExitCodes do
            Assert.False(
                c.Meaning.Contains "half-landed" || c.Meaning.Contains "half-written",
                $"take exit %d{c.Code} describes a half-landed WRITE — that is EX_PARTIAL's meaning, and `take` cannot return it (#889)")

    /// THE RED LEG. `Batch.schedule` REFUSES a batch when an in-flight claim declares a touch-set that
    /// matches no file (`Batch.fs`, tested in `BatchTests`), `renderDecision` turns that into `ExitRed`,
    /// and `take` propagates it verbatim. It is reachable, and the first draft of the generated table
    /// omitted it — the hand-written table's much-maligned "≠0, ≠2" row did at least cover it.
    [<Fact>]
    let ``take's REFUSED leg is documented`` () =
        Assert.True(
            documented Client.ExitRed,
            "3 (the batch was REFUSED) is reachable from `take` via renderDecision's Red arm and is not documented")

    /// THE FLOOR (#266, #436). A gate that asserts "the numbers agree" over an EMPTY list agrees
    /// vacuously — and this whole file would then pass while `take`'s contract went undocumented. The
    /// count is deliberately not pinned: rows may come and go, but the table may not empty out.
    [<Fact>]
    let ``take's contract is actually stated`` () =
        Assert.NotEmpty Protocol.takeExitCodes
        Assert.True(
            Protocol.takeExitCodes |> List.exists (fun c -> c.Name <> ""),
            "no EX_* code is documented at all — the table has gone vacuous")

    /// #918 — THE ONE SOURCE, ENFORCED ACROSS THE THREE MODULES THAT USED TO DISAGREE.
    ///
    /// The codes are one union now, `FS.GG.Coord.ExitCode`, with one `toInt`. `Errors` and `Program`
    /// derive their constants from it directly. `Client` still spells its own literals — a live claim
    /// held `Client.fs` when this landed, so its derivation is a follow-up — which is exactly why this
    /// pins every module's constant to the union's number: until `Client.fs` derives them too, this is
    /// what refuses a drift. Retype any constant, or the union, and the two disagree here.
    [<Fact>]
    let ``every module's exit constant is the union's number`` () =
        Assert.Equal(ExitCode.toInt ExitCode.Green, Client.ExitGreen)
        Assert.Equal(ExitCode.toInt ExitCode.Error, Client.ExitError)
        Assert.Equal(ExitCode.toInt ExitCode.Red, Client.ExitRed)
        Assert.Equal(ExitCode.toInt ExitCode.NoVerdict, Client.ExitNoVerdict)
        Assert.Equal(ExitCode.toInt ExitCode.NoneStartable, Client.ExitNone)
        Assert.Equal(ExitCode.toInt ExitCode.Contended, Client.ExitContended)
        Assert.Equal(ExitCode.toInt ExitCode.Pending, Client.ExitPending)
        Assert.Equal(ExitCode.toInt ExitCode.NotOpen, Client.ExitNotOpen)
        Assert.Equal(ExitCode.toInt ExitCode.Rate, Errors.ExRate)
        Assert.Equal(ExitCode.toInt ExitCode.Offboard, Errors.ExOffboard)
        Assert.Equal(ExitCode.toInt ExitCode.Partial, Errors.ExPartial)

    /// #918 — AND THE COLLISION IS GONE. `Errors.ExOffboard` was `3` (colliding with `Client.ExitRed`)
    /// and `Errors.ExPartial` `4` (colliding with `Client.ExitNoVerdict`). Distinct meanings — off-board
    /// is a fact on a successful read, partial is a half-landed write — so they moved to their own
    /// numbers rather than merging into the verdict codes.
    [<Fact>]
    let ``the github-layer codes no longer collide with the verdict codes`` () =
        Assert.NotEqual(Errors.ExOffboard, Client.ExitRed)
        Assert.NotEqual(Errors.ExPartial, Client.ExitNoVerdict)
        Assert.Equal(8, Errors.ExOffboard)
        Assert.Equal(9, Errors.ExPartial)

    // ---- `landable` (#944) -------------------------------------------------------------------------
    //
    // `Protocol.landableExitCodes` (#900) is prose, exactly as `takeExitCodes` above is, and it arrived
    // with only the half `Core` can do. `Core.Tests/ProtocolTests.fs` pins what the rows SAY — 3 is RED,
    // 7 is PENDING, there is no 75 — and that is a real gate, but `Core` cannot see `Cli`'s constants, so
    // nothing tied `Code = 7` to `Client.ExitPending`. Retype `ExitPending = 8` and every gate stayed
    // green while the generated table — the one a worker builds a poll loop from — quietly lied.
    //
    // `ProtocolTests` has asserted IN PROSE, since #900, that "`ExitContractTests` ties the same rows to
    // `Client.ExitPending`/`ExitRed`". Until this block, it did not. A comment claiming a gate that does
    // not exist is #266's signature landing inside the file written to refuse it.
    //
    // Keyed on the CONSTANT, never the digit — the same discipline #889 used above. This USED to be load
    // bearing twice over, because `Client.ExitRed`/`Errors.ExOffboard` were BOTH 3 and
    // `Client.ExitNoVerdict`/`Errors.ExPartial` BOTH 4, so a digit could not tell a verdict from a
    // GitHub-layer fact. #918 RESOLVED that: the codes are one union now (`FS.GG.Coord.ExitCode`), and it
    // moved `ExOffboard` to 8 and `ExPartial` to 9, off the verdict codes they collided with. Keying on
    // the constant remains right — it is the semantic identity, not the number — and
    // `every module's exit constant is the union's number` below pins all three modules to that one union.

    let private landableDocuments (code: int) =
        Protocol.landableExitCodes |> List.exists (fun c -> c.Code = code)

    let private landableMeaningOf (code: int) =
        Protocol.landableExitCodes
        |> List.tryFind (fun c -> c.Code = code)
        |> Option.map (fun c -> c.Meaning.ToUpperInvariant())

    /// THE PIN #900 SHIPPED WITHOUT. Every documented `landable` row is keyed to the constant the engine
    /// actually returns, so retyping one fails HERE rather than in a worker's poll loop three weeks on.
    [<Fact>]
    let ``the documented landable codes are the literals the engine returns`` () =
        Assert.True(landableDocuments Client.ExitGreen, "0 (green — the only code that means merge) is not documented")
        Assert.True(landableDocuments Client.ExitPending, "7 (pending — the only code that means wait) is not documented")
        Assert.True(landableDocuments Client.ExitRed, "3 (red/conflicted) is not documented")
        Assert.True(landableDocuments Client.ExitNoVerdict, "4 (unknown — fail-closed) is not documented")
        Assert.True(landableDocuments Client.ExitError, "1 (refused input) is not documented")

        Assert.True(
            landableDocuments Client.ExitNotOpen,
            "10 (the PR is not open — merged, or closed unmerged) is not documented, so a merged PR has no documented outcome (#1680)")

    /// THE TWO CODES THE POLL LOOP READS, tied to their meanings THROUGH the constants.
    ///
    /// `ProtocolTests` pins "7 says PENDING" and "3 says RED" in `Core`. This pins that
    /// `Client.ExitPending`'s VALUE is the row that says PENDING — so the two halves compose into
    /// "ExitPending = 7, and 7 means pending", which is the claim #900's table could not make alone.
    /// Retyping the constant lands on a row that means something else, or on no row at all; either way
    /// this fails, which is the whole point of the file.
    ///
    /// `StartsWith`, NOT `Contains`, and the difference is not pedantry: "REGISTERED" CONTAINS "RED",
    /// and "registered" is a word in row 7's PENDING text ("none have registered yet"). A `Contains`
    /// check for RED therefore PASSES on the pending row — so with the two meanings swapped, the half of
    /// this test that is supposed to catch #900 would wave it through, and only the PENDING assertion
    /// would be doing any work. Both rows open with their verdict word, so anchoring is both stronger
    /// and truer to the table.
    [<Fact>]
    let ``landable's wait code and its stop code are pinned to the engine's constants`` () =
        match landableMeaningOf Client.ExitPending with
        | None ->
            Assert.Fail
                "Client.ExitPending names no documented landable row — the ONE retryable verdict is undocumented, so a poll loop reads it as an unrecognised failure and stops waiting on a PR that is merely still running (#900)"
        | Some m ->
            Assert.True(
                m.StartsWith "PENDING",
                "Client.ExitPending's value is not documented as PENDING — a loop built on this table waits on the wrong code (#900)")

        match landableMeaningOf Client.ExitRed with
        | None -> Assert.Fail "Client.ExitRed names no documented landable row — a red/conflicted PR has no documented outcome"
        | Some m ->
            Assert.True(
                m.StartsWith "RED",
                "Client.ExitRed's value is not documented as RED — #900 is precisely that it was called 'pending', and a loop that waits on it never terminates")

    /// THE ENUMERATION MUST BE COMPLETE IN BOTH DIRECTIONS (#889). Every assertion above is an EXISTENCE
    /// check: it catches a code the engine returns and the table omits. Nothing yet catches the reverse
    /// — a code the table documents and `landable` CANNOT return — which is the defect #889 fixed for
    /// `take`, where the recipe documented an `EX_PARTIAL` that `take` has no arm for.
    ///
    /// `landable`'s surface is its closing match (`Client.fs`): `PrGreen`/`PrPending`/`PrRed`
    /// `PrConflicted`/`PrUnknown`, plus `ExitError` from the arms that refuse the INPUT ahead of the
    /// read, plus `Program.main`'s defect 2. `ExitNone` and `ExitContended` are `take`'s — there is no
    /// queue to be empty and no CAS to lose. Documenting one here would hand a worker `take`'s remedy
    /// (back off and retry) for a PR verdict that will never change.
    ///
    /// Keyed on the NUMBER is safe HERE, and only here: unlike #889's `EX_PARTIAL` (which collides with
    /// `ExitNoVerdict` at 4), 5 and 6 collide with nothing in `landable`'s set (#918).
    [<Fact>]
    let ``landable documents none of take's codes`` () =
        Assert.False(
            landableDocuments Client.ExitNone,
            "landable documents 5 (EX_NONE) — that is `take`'s empty queue; landable has no queue, and its remedy (back off and retry) is wrong for every verdict landable returns")

        Assert.False(
            landableDocuments Client.ExitContended,
            "landable documents 6 (EX_CONTENDED) — that is `take`'s lost CAS; landable takes no lock")

    /// `ExitPending` IS THE ONE RETRYABLE CODE, so it must not collide with a way to STOP. Its own
    /// comment in `Client.fs` says it "dodges the reserved codes", and that dodge is only a fact for as
    /// long as something checks it: a retyped `ExitPending = 3` would make "keep waiting" and "the PR is
    /// RED" the same number, and the table could not tell a worker which one they got.
    [<Fact>]
    let ``landable's pending code is distinct from every code that means stop`` () =
        Assert.NotEqual(Client.ExitPending, Client.ExitGreen)
        Assert.NotEqual(Client.ExitPending, Client.ExitRed)
        Assert.NotEqual(Client.ExitPending, Client.ExitNoVerdict)
        Assert.NotEqual(Client.ExitPending, Client.ExitError)

    /// THE FLOOR, for `landable`'s table specifically (#266, #436). Every assertion above is an
    /// existence check over `landableExitCodes`; an emptied table would fail them — but a table emptied
    /// down to a single lucky row would not, and `Assert.NotEmpty` in `Core.Tests` cannot see the
    /// constants. Stated here so the vacuity is refused on both sides of the dependency edge.
    [<Fact>]
    let ``landable's contract is actually stated`` () =
        Assert.NotEmpty Protocol.landableExitCodes

    /// #918 CLOSED THIS GAP — it used to be the one row pinned by a bare digit.
    ///
    /// `landable` returns SIX codes; the sixth is the defect handler's 2. `Program.fs` declared it
    /// `let private ExitDefect = 2`, so `Cli.Tests` could not see the constant and this asserted the
    /// DIGIT — strictly weaker (retyping `ExitDefect = 9` left it green and the table lying). Strengthening
    /// it then meant editing `Program.fs`, "the same class of problem #918 owns", and would have smuggled
    /// #918's decision into a test file. #918 is now made: the number lives in `FS.GG.Coord.ExitCode`, and
    /// `Program.fs`'s `ExitDefect` derives from it — so this pins the documented row to
    /// `ExitCode.toInt ExitCode.Defect`, the union case itself, not a digit.
    [<Fact>]
    let ``landable's defect code is documented, pinned to the union (#918)`` () =
        Assert.True(
            landableDocuments (ExitCode.toInt ExitCode.Defect),
            "2 (the engine broke — Program.main's defect handler) is reachable from `landable` and is not documented")

    /// #1680 — THE PIN THAT KEEPS THE MERGED VERDICT OFF THE RETRYABLE CODE. `ProtocolTests` pins that
    /// row 10 SAYS "MERGED"; this pins that `Client.ExitNotOpen`'s VALUE is that row. The two halves
    /// compose into "ExitNotOpen = 10, and 10 means merged/closed", which is the claim #900 taught us a
    /// table cannot make alone — and #1680 is the same defect reached from the other side, so it gets
    /// the same two-sided treatment.
    [<Fact>]
    let ``#1680 landable's not-open code is pinned to the engine's constant, and is not the wait code`` () =
        match landableMeaningOf Client.ExitNotOpen with
        | None ->
            Assert.Fail
                "Client.ExitNotOpen names no documented landable row — a merged PR's exit code is undocumented, so a recovery path reads it as an unrecognised failure"
        | Some m ->
            Assert.True(
                m.StartsWith "MERGED",
                "Client.ExitNotOpen's value is not documented as MERGED — #1680 is that a merged PR was reported as 'pending', and a loop built on that never terminates")

        // The defect in one assertion: the merged verdict must never be reachable through the code the
        // contract defines as worth retrying.
        Assert.NotEqual(Client.ExitNotOpen, Client.ExitPending)
        // Nor through the failure code — `merged` is a success whose next act is to STAMP.
        Assert.NotEqual(Client.ExitNotOpen, Client.ExitRed)
        Assert.NotEqual(Client.ExitNotOpen, Client.ExitGreen)

    /// #1680 AC6, for the ONE landable table that is NOT generated. `Protocol.landableExitCodes` is
    /// projected into both skill roots by `scripts/generate-projections`, so those copies cannot drift.
    /// `Options.usage` — the engine's own `--help` — is hand-authored prose beside it, and is exactly the
    /// shape of restatement #900/#889/#1667 were each filed about. It cannot practically be GENERATED
    /// (the rows are paragraphs; `--help` needs a line), so it is PINNED instead: every code the engine
    /// can return for `landable` must appear in the usage block. Add a verdict without documenting it
    /// here and this fails.
    [<Fact>]
    let ``#1680 AC6 the usage block documents every landable exit code the engine can return`` () =
        let usage = Options.usage

        for code in ExitCode.landableCodes do
            let n = ExitCode.toInt code

            // The generic codes (1 error, 2 defect) are documented in the shared table above the
            // per-command line, so only the landable-specific ones are asserted against the digit.
            if n <> ExitCode.toInt ExitCode.Error && n <> ExitCode.toInt ExitCode.Defect then
                Assert.True(
                    usage.Contains $"%d{n} " || usage.Contains $"· %d{n}",
                    $"the usage block never mentions landable exit %d{n} — a caller reading --help cannot learn what the engine will return (#1680 AC6)")

        // And the word, not merely the digit: AC2's distinguishability has to survive into `--help`.
        Assert.Contains("merged", usage)
        Assert.Contains("closed", usage)

/// WHEN `EX_CONTENDED` FIRES — THE OTHER HALF OF `take`'S EXIT CONTRACT (.github#2683).
///
/// Everything above this line pins what the documented codes MEAN. This module pins WHEN one of them is
/// returned, because that is the half that was wrong: `take` asked the scheduler for exactly one
/// candidate and, on a lost claim CAS, returned `EX_CONTENDED` with no item — discarding a ranking it had
/// already computed and already paid a board scan for. Three times in one 2026-08-15/16
/// `drive-board-best` session that turned a lost race into a permanently idle implementer slot, the third
/// time with SIX free disjoint lanes and only two workers.
///
/// The remedy the engine named was a retry BY THE CALLER — the stderr line said "Pick another", the code
/// comment said "the caller retries" — and the caller is a disposable worker whose brief forbids exactly
/// that, because a second `take` against a live one is how two workers land on one item. So the retry had
/// no owner, and only a watching host ever recovered the slot.
///
/// **THE FIXTURE IS THE RACE, NOT A MOCK OF IT.** A candidate already carrying a live marker when the scan
/// reads it is never CHOSEN (`Schedulability.HeldBy`), so it could not reproduce this: the defect needs an
/// item that is free at scan time and held by the time our own CAS re-reads it. That window is what these
/// fixtures serve — the rival's marker appears on the read AFTER the scan's — and it is the same window
/// #418's cache and every real fan-out produce.
///
/// Both directions are pinned, because only the pair is a gate: the fallthrough must CLAIM the next lane,
/// and `EX_CONTENDED` must still fire — bounded, and only once every candidate the ranking offered has
/// been attempted and lost.
module TakeFallthroughTests =

    open System
    open System.Collections.Generic
    open System.IO
    open System.Text.Json
    open FS.GG.Coord.GitHub.Transport

    let private ok (body: string) : Errors.IoResult<Response> =
        Ok
            { Status = 200
              Body = body
              ETag = None
              NextLink = None
              Headers = Map.empty }

    /// The lanes the fixture board offers: disjoint touch-sets, so the scheduler admits all of them and
    /// the fallthrough has somewhere to go. Disjointness is not decoration — see
    /// `Client.takeCandidateBound`: losing candidate 1 means a rival now holds candidate 1's files, so a
    /// fallback that OVERLAPPED it would be a lane we must not take. FOUR of them, one more than the
    /// bound, so the bound itself is measurable rather than merely declared.
    let private lanes = [ 9001; 9002; 9003; 9004 ]

    let private routeFor (n: int) =
        StructuredFixtures.routeComment $"FS-GG/.github#%d{n}" (Some DeliveryRoute.Lightweight) "fixture-2683" None

    /// A live claim marker by somebody else — the rival that won the race we lost.
    let private rivalMarker (n: int) = $"<!-- fsgg:claim worker=rival-%d{n} lease=120 -->\nheld"

    /// HOW MANY TIMES ONE `take`'S SCAN READS A LANE'S COMMENT THREAD BEFORE ANY CLAIM RUNS. MEASURED,
    /// not assumed: `Scan.snapshot`'s marker read, then the delivery-route read `enrichDeliveryRoutes`
    /// makes. The rival appears on the read after those — the first one a claim's own CAS makes.
    ///
    /// **THE NUMBER IS SELF-CHECKING, WHICH IS WHY IT IS SAFE TO PIN.** Read it one too high and the
    /// rival lands after the CAS as well, so nothing is ever lost and the first leg's `is already held`
    /// assertion fails. Read it one too low and the rival lands INSIDE the scan, the scheduler sees the
    /// lane `HeldBy` and never chooses it — so `take` claims a later lane, exits 0 with the head never
    /// attempted, and that same assertion fails. The premise cannot rot silently in either direction.
    let private scanReadsPerLane = 2

    /// One lane's comment thread, mutated by the requests the way GitHub would mutate it: a POST appends
    /// with the next id, a DELETE removes. A CANNED list will not do — `Writes.claim`'s CAS posts its
    /// marker and then RE-READS to confirm it survived, so a thread that could not show the marker it was
    /// just given reports every win as "our marker vanished from the re-read", and the fixture would
    /// measure its own inertness rather than the engine.
    type private Lane(n: int) =
        let comments = ResizeArray<int64 * string>()
        let mutable nextId = int64 (10000 + n)
        let mutable reads = 0

        do comments.Add(int64 (7000 + n), routeFor n)

        member _.Reads = reads
        member _.CountRead() = reads <- reads + 1

        /// The rival's marker, added once. Idempotent: the CAS re-reads more than once and a thread that
        /// grew a second rival marker per read would be testing marker-scan de-duplication instead.
        member _.EnsureRival() =
            if not (comments |> Seq.exists (fun (_, b) -> b.Contains $"worker=rival-%d{n}") ) then
                nextId <- nextId + 1L
                comments.Add(nextId, rivalMarker n)

        member _.Add(body: string) =
            nextId <- nextId + 1L
            comments.Add(nextId, body)
            nextId

        member _.Remove(id: int64) =
            comments.RemoveAll(fun (i, _) -> i = id) |> ignore

        member _.Json() =
            let ts = DateTime.UtcNow.ToString "yyyy-MM-ddTHH:mm:ssZ"

            comments
            |> Seq.map (fun (id, body) ->
                JsonSerializer.Serialize
                    {| id = id
                       body = body
                       user = {| login = "EHotwagner" |}
                       created_at = ts
                       updated_at = ts |})
            |> String.concat ","
            |> fun inner -> "[" + inner + "]"

    let private boardRow (n: int) =
        $"""{{"status":{{"name":"Ready"}},"blockedBy":null,"class":null,"phase":null,"content":{{"__typename":"Issue","number":%d{n},"title":"lane %d{n}","state":"OPEN","repository":{{"nameWithOwner":"FS-GG/.github"}}}}}}"""

    let private graphqlAnswer (document: string) : string option =
        if document.Contains "projectsV2" then
            Some
                """{"data":{"organization":{"projectsV2":{"nodes":[{"number":12,"title":"Coordination","id":"PVT_coord"}]}}},"rateLimit":{"cost":1,"remaining":4977}}"""
        elif document.Contains "fields(first" then
            Some
                """{"data":{"organization":{"projectV2":{"fields":{"nodes":[{"id":"PVTSSF_status","name":"Status","dataType":"SINGLE_SELECT","options":[{"id":"opt_ready","name":"Ready"},{"id":"opt_wip","name":"In progress"}]}]}}}},"rateLimit":{"cost":1,"remaining":4977}}"""
        elif document.Contains "items(first" then
            let nodes = lanes |> List.map boardRow |> String.concat ","

            Some
                $"""{{"data":{{"organization":{{"projectV2":{{"items":{{"pageInfo":{{"hasNextPage":false,"endCursor":null}},"nodes":[%s{nodes}]}}}}}}}},"rateLimit":{{"cost":1,"remaining":4977}}}}"""
        else
            None

    /// How a lane behaves on the reads AFTER the scan's.
    type private Rival =
        /// Free the whole way through — the lane a fallthrough must land on.
        | StaysFree
        /// A rival's marker appears in the window between the scan's read and our CAS's re-read.
        | WinsTheRace
        /// The budget dies in that same window (acceptance 4's short-circuit).
        | ExhaustsTheBudget

    /// The board, served live. A lane behaves as its leg declared only once `scanReadsPerLane` reads have
    /// gone by — everything before that is the scan, and the scan must see every lane FREE or there is no
    /// race to lose.
    let private world (behaviour: Map<int, Rival>) (threads: Dictionary<int, Lane>) =
        Fake.Recorder(fun (req: Request) ->
            let path = req.Path.Trim '/'

            match req.Method, path with
            | "POST", "graphql" when
                (match req.Body with
                 | Query(document, _) -> document.Contains "comments(last:"
                 | _ -> false)
                ->
                match req.Body with
                | Query(_, variables) ->
                    let number =
                        variables
                        |> List.tryPick (fun (k, v) ->
                            match k, v with
                            | "number", VNumber n -> Some(int n)
                            | _ -> None)

                    match number with
                    | Some n ->
                        let nodes = [ {| body = routeFor n |} ] |> JsonSerializer.Serialize

                        ok
                            ("{\"data\":{\"repository\":{\"issue\":{\"comments\":{\"nodes\":"
                             + nodes
                             + "}}}},\"rateLimit\":{\"cost\":1,\"remaining\":4977}}")
                    | None -> Error(Errors.NotFound "the recent-comments query names no issue number")
                | _ -> Error(Errors.NotFound "a graphql call with no document")
            | "POST", "graphql" ->
                match req.Body with
                | Query(document, _) ->
                    match graphqlAnswer document with
                    | Some answer -> ok answer
                    | None ->
                        Error(Errors.NotFound "the fixture serves no board WRITE — the LOCK is what is under test")
                | _ -> Error(Errors.NotFound "a graphql call with no document")
            | "GET", "rate_limit" -> ok """{"resources":{"graphql":{"remaining":4980,"limit":5000}}}"""
            | "GET", "repos/FS-GG/.github/issues" -> ok "[]"
            | _ ->

            let threadOf (n: int) =
                match threads.TryGetValue n with
                | true, lane -> lane
                | _ ->
                    let lane = Lane n
                    threads.[n] <- lane
                    lane

            let lane =
                lanes
                |> List.tryFind (fun n ->
                    path = $"repos/FS-GG/.github/issues/%d{n}"
                    || path = $"repos/FS-GG/.github/issues/%d{n}/comments")

            let deleted =
                if req.Method <> "DELETE" then
                    None
                elif not (path.StartsWith "repos/FS-GG/.github/issues/comments/") then
                    None
                else
                    match Int64.TryParse(path.Substring(path.LastIndexOf '/' + 1)) with
                    | true, id -> Some id
                    | _ -> None

            match lane, req.Method, deleted with
            | _, _, Some id ->
                for n in lanes do
                    (threadOf n).Remove id

                ok ""
            | Some n, "GET", _ when path.EndsWith "/comments" ->
                let thread = threadOf n
                thread.CountRead()

                // THE WINDOW. The first `scanReadsPerLane` reads are the scan's, so every lane is free
                // and every lane is a candidate the scheduler may choose. Every read after them belongs
                // to a claim's own CAS, and that is where the leg's declared behaviour begins.
                let behaving =
                    thread.Reads > scanReadsPerLane
                    && (behaviour |> Map.tryFind n |> Option.defaultValue StaysFree) <> StaysFree

                match behaving, behaviour |> Map.tryFind n with
                | true, Some ExhaustsTheBudget -> Error(Errors.RateLimited(Errors.UnknownBudget, None))
                | true, Some WinsTheRace ->
                    thread.EnsureRival()
                    ok (thread.Json())
                | _ -> ok (thread.Json())
            | Some n, "GET", _ -> ok $"""{{"number":%d{n},"body":"Paths: src/lane%d{n}.fs"}}"""
            | Some n, "POST", _ ->
                let body =
                    match req.Body with
                    | Json payload -> JsonDocument.Parse(payload).RootElement.GetProperty("body").GetString()
                    | _ -> ""

                ok $"""{{"id":%d{(threadOf n).Add body}}}"""
            | _ -> Error(Errors.NotFound $"the fixture serves no %s{req.Method} %s{path}"))

    let private context (transport: Fake.Recorder) : Client.Context =
        { Transport = transport
          Owner = "FS-GG"
          Title = "Coordination"
          DefaultRepo = Some ".github"
          ChoreLocks = [] }

    let private sessionVars =
        [ "CLAUDE_CODE_SESSION_ID"; "OPENCODE_SESSION_ID"; "FSGG_AGENT_SESSION_ID"; "FSGG_WORKER" ]

    /// Drive the REAL `Client.take` against a throwaway cache and a pinned identity, on the same licence
    /// `ForceStealTests.runClaim` takes: `AssemblyInfo.fs` disables cross-class parallelism, so the
    /// process-global `FSGG_COORD_CACHE`/`FSGG_KIT_ROOT`/identity variables are safe to point somewhere
    /// private per call. The identity is pinned for #1646's reason — `claim` measures the named worker
    /// against the derived one, so an unpinned `$FSGG_WORKER` would make every leg an impersonation.
    let private runTake (transport: Fake.Recorder) : int * string =
        let dir = Path.Combine(Path.GetTempPath(), "fsgg-2683-" + Guid.NewGuid().ToString "n")
        let previousCache = Environment.GetEnvironmentVariable "FSGG_COORD_CACHE"
        let previousKitRoot = Environment.GetEnvironmentVariable "FSGG_KIT_ROOT"
        let previousSessions = sessionVars |> List.map (fun v -> v, Environment.GetEnvironmentVariable v)
        let stdout = Console.Out
        let stderr = Console.Error
        use captured = new StringWriter()
        use capturedError = new StringWriter()

        try
            Directory.CreateDirectory dir |> ignore
            Environment.SetEnvironmentVariable("FSGG_COORD_CACHE", dir)
            Environment.SetEnvironmentVariable("FSGG_KIT_ROOT", dir)
            Environment.SetEnvironmentVariable("CLAUDE_CODE_SESSION_ID", null)
            Environment.SetEnvironmentVariable("OPENCODE_SESSION_ID", null)
            Environment.SetEnvironmentVariable("FSGG_AGENT_SESSION_ID", "ed60050b")
            Environment.SetEnvironmentVariable("FSGG_WORKER", "vole-418")
            Console.SetOut captured
            Console.SetError capturedError

            let opts =
                match Options.parse [ "take"; "--repo"; ".github"; "--worker"; "vole-418" ] with
                | Ok o -> o
                | Error e -> failwithf "the fixture's own argv did not parse: %s" e

            let code = Client.take (context transport) opts
            Console.Out.Flush()
            Console.Error.Flush()
            code, capturedError.ToString()
        finally
            Console.SetOut stdout
            Console.SetError stderr
            Environment.SetEnvironmentVariable("FSGG_COORD_CACHE", previousCache)
            Environment.SetEnvironmentVariable("FSGG_KIT_ROOT", previousKitRoot)

            for name, value in previousSessions do
                Environment.SetEnvironmentVariable(name, value)

            try
                Directory.Delete(dir, true)
            with _ ->
                ()

    /// A lane was ATTEMPTED AND LOST iff the ENGINE said so about that exact lane. Read off the engine's
    /// own stderr rather than off a request count, because only a claim CAS can produce this sentence:
    /// the scheduler cannot have refused the lane (it would never have been claimed), and the scan cannot
    /// have emitted it (it does not run a CAS). It is also the fixture's premise in one observation — a
    /// lane the scan had seen held would never have been chosen, so this line could not exist.
    let private attemptedAndLost (said: string) (n: int) =
        said.Contains $".github#%d{n} is already held by rival-%d{n}"

    let private claimed (transport: Fake.Recorder) (n: int) =
        transport.Logged $"comment-post FS-GG/.github %d{n}"

    /// THE FIXTURE'S OWN NON-VACUITY (#266). Every leg reasons from "the scan offered all four lanes". If
    /// the scan never read a lane's thread, "we chose not to attempt it" and "it was never on offer"
    /// become one observation, and a leg asserting the former would be passing over the latter.
    let private assertEveryLaneWasOffered (threads: Dictionary<int, Lane>) =
        for n in lanes do
            Assert.True(
                (match threads.TryGetValue n with
                 | true, lane -> lane.Reads >= scanReadsPerLane
                 | _ -> false),
                $"the scan did not read lane %d{n}'s comment thread %d{scanReadsPerLane} time(s) — this leg measured a board that offered fewer lanes than it claims to")

    // ---- THE PAIR, PLUS THE TWO SHORT-CIRCUITS ------------------------------------------------------

    /// ACCEPTANCE 1. The head of the ranking is lost; the next candidate is CLAIMED, in the same call,
    /// off the same scan.
    [<Fact>]
    let ``.github#2683 a lost claim CAS advances to the next ranked candidate instead of idling the slot``
        ()
        =
        let threads = Dictionary<int, Lane>()
        let transport = world (Map.ofList [ 9001, WinsTheRace ]) threads

        let code, said = runTake transport

        assertEveryLaneWasOffered threads

        Assert.True(
            attemptedAndLost said 9001,
            "the engine never reported losing lane 9001 — the head of the ranking was not attempted, so this leg is not measuring a LOST race (see `scanReadsPerLane`)")

        Assert.Equal(Client.ExitGreen, code)

        Assert.False(
            claimed transport 9001,
            "a marker was posted to the lane a rival had already won — the CAS did not refuse it")

        Assert.True(
            claimed transport 9002,
            "the next ranked candidate was free and was never claimed: `take` discarded the remainder of a ranking it had already paid a board scan for, and the implementer slot idles (.github#2683)")

    /// ACCEPTANCE 3 AND ACCEPTANCE 2, IN ONE LEG. `EX_CONTENDED` still fires — but only after every
    /// candidate the ranking offered has been attempted and lost, and the ranking is BOUNDED: four lanes
    /// are free at scan time, three are attempted, the fourth is not. A fallthrough with no bound would
    /// attempt all four.
    [<Fact>]
    let ``.github#2683 EX_CONTENDED fires only when every candidate in the BOUNDED ranking has been lost``
        ()
        =
        let threads = Dictionary<int, Lane>()

        let transport =
            world (lanes |> List.map (fun n -> n, WinsTheRace) |> Map.ofList) threads

        let code, said = runTake transport

        assertEveryLaneWasOffered threads

        Assert.Equal(Client.ExitContended, code)

        for n in lanes do
            Assert.False(claimed transport n, $"lane %d{n} was claimed, and a rival held every one of them")

        Assert.Equal<int list>([ 9001; 9002; 9003 ], lanes |> List.filter (attemptedAndLost said))

    /// ACCEPTANCE 4. An exhausted budget is SYSTEMIC — it will not be less exhausted for the next
    /// candidate — so it short-circuits on the spot rather than spending two further claim attempts
    /// against it. This is the one verdict the fallthrough must NOT walk past.
    ///
    /// **THE LEG DISCRIMINATES BY CONSTRUCTION, WITHOUT COUNTING REQUESTS.** Only the head exhausts the
    /// budget; the three lanes behind it stay FREE the whole way through. A `take` that walked past
    /// `EX_RATE` would therefore CLAIM lane 9002 and exit 0. So "exit 75, and nothing was claimed
    /// anywhere" is the whole of the short-circuit — there is no third outcome that satisfies both.
    [<Fact>]
    let ``.github#2683 EX_RATE short-circuits immediately and is never retried against further candidates``
        ()
        =
        let threads = Dictionary<int, Lane>()
        let transport = world (Map.ofList [ 9001, ExhaustsTheBudget ]) threads

        let code, _ = runTake transport

        assertEveryLaneWasOffered threads

        Assert.Equal(Errors.ExRate, code)

        for n in lanes do
            Assert.False(
                claimed transport n,
                $"lane %d{n} was claimed after the head exhausted the budget — EX_RATE must short-circuit, not walk the ranking (.github#2683 acceptance 4)")

    /// ACCEPTANCE 6, AS A GATE RATHER THAN A READING. The engine must not tell a caller to perform the
    /// retry every worker brief in this org forbids. `Client.claim`'s lost-race line used to end "Pick
    /// another, or wait for the lease." — and from inside `take`'s own walk, "pick another" reads as "run
    /// `take` again", which is how two workers land on one item.
    ///
    /// The subject here is the message the ENGINE emits, so this drives the engine and reads its stderr
    /// rather than grepping the source: a source-text check would pass over a file that no longer emits
    /// the line at all.
    [<Fact>]
    let ``.github#2683 no engine message instructs the caller to perform the forbidden retry`` () =
        let threads = Dictionary<int, Lane>()

        let code, said =
            runTake (world (lanes |> List.map (fun n -> n, WinsTheRace) |> Map.ofList) threads)

        Assert.Equal(Client.ExitContended, code)

        // NON-VACUITY. If nothing was said at all, "it instructs no retry" is true of silence, and silence
        // is not what a worker that just lost every lane needs. This is the corpus the leg below scans;
        // an empty one would make its `DoesNotContain` a gate over nothing.
        Assert.Contains("is already held by", said)

        Assert.DoesNotContain("Pick another", said)

        // And the positive half: the engine names who does the advancing, so a reader of this line cannot
        // conclude that re-running `take` is the remedy.
        Assert.Contains("no caller re-runs it to retry", said)
