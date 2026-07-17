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
/// WHAT IT DOES NOT DO, said plainly, because the gap has already cost this change once: it pins the
/// documentation to the engine's CONSTANTS, not to `take`'s BEHAVIOUR. Rewire `take` to return
/// `ExitGreen` on an empty queue and every assertion here still passes. Nothing enumerates the set of
/// codes `take` can actually return — the return paths are ints threaded through three modules, not a
/// union the compiler can force a match on. That is exactly how the first draft of this table shipped
/// with no row for `ExitRed`, which `take` propagates from `renderDecision`'s Red arm. The rows below
/// that assert reachability are hand-derived from reading `Client.take`, and they are only as good as
/// that reading. Making this total needs `take` to return a typed verdict rather than an int — a
/// refactor across `Cli`/`GitHub`, filed rather than smuggled in here.
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
    /// KEYED ON THE NAME, NOT THE NUMBER, and that distinction is load-bearing: `Errors.ExPartial` and
    /// `Client.ExitNoVerdict` are BOTH 4 (they are ints in different modules — see the collision this
    /// PR files rather than fixes). Asserting "4 is undocumented" would therefore also forbid
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
    // Keyed on the CONSTANT, never the digit — the same discipline #889 used above, and here it is load
    // bearing twice over: `Client.ExitRed`/`Errors.ExOffboard` are BOTH 3, and
    // `Client.ExitNoVerdict`/`Errors.ExPartial` are BOTH 4 (#918). This block asserts what `landable`
    // returns; it does not try to resolve that collision.

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

    /// WHAT THIS BLOCK CANNOT PIN, said out loud rather than left as a silent hole (#889's discipline).
    ///
    /// `landable` returns SIX codes; five are pinned above. The sixth is 2 — `Program.main`'s defect
    /// handler — and `Program.fs` declares it `let private ExitDefect = 2`, so `Cli.Tests` cannot
    /// reference the constant and this asserts the DIGIT. That is strictly weaker: retyping
    /// `ExitDefect = 9` leaves this green and the table lying, which is the exact failure the rest of
    /// the file exists to refuse.
    ///
    /// It is left this way deliberately. Making it public is an edit to `Program.fs` — outside this
    /// item's touch-set, and the same class of problem #918 owns (the codes are ints across three
    /// modules, two of them colliding). Fixing it here would smuggle #918's decision into a test file.
    [<Fact>]
    let ``landable's defect code is documented — pinned only by number, see #918`` () =
        Assert.True(
            landableDocuments 2,
            "2 (the engine broke — Program.main's defect handler) is reachable from `landable` and is not documented")
