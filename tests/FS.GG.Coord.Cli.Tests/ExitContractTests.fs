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
