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
