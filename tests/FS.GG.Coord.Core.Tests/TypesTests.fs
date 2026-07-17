namespace FS.GG.Coord.Tests

open FSharp.Reflection
open Xunit
open FS.GG.Coord
open FS.GG.Coord.Types

/// THE WIRE VOCABULARY IS ONE FUNCTION, AND THESE ARE THE PROPERTIES THE COMPILER CANNOT GIVE US.
///
/// `Types.statusWireName` is a TOTAL match, so the compiler already owns coverage: a new `BoardStatus`
/// case fails the build rather than rendering as something. That is the half reflection does not need to
/// check, and `ProtocolTests` says so in as many words about `Schedulability.kind`.
///
/// What the compiler cannot see is what a case is named ONCE somebody is forced to name it. It will
/// accept `""`, it will accept a name another case already uses, and it will accept `" Ready "` — each
/// of which reaches the board and none of which is a type error. That is this module's subject.
module TypesTests =

    /// Every `BoardStatus` case, built by reflection rather than typed out here.
    ///
    /// A hand-written list is the fifth copy of the vocabulary wearing a different hat: it would be
    /// correct on the day it was typed and silently short by one the day a case is added — which is the
    /// precise failure #983 exists to end, reproduced inside its own regression test.
    let private everyCase: (string * BoardStatus) list =
        FSharpType.GetUnionCases typeof<BoardStatus>
        |> Array.map (fun c -> c.Name, FSharpValue.MakeUnion(c, [||]) :?> BoardStatus)
        |> Array.toList

    [<Fact>]
    let ``reflection can actually see the union - the guard below is not vacuous`` () =
        // If `everyCase` silently came back empty, every `for` loop under it would pass by iterating
        // nothing, and this module would report green over a vocabulary it never looked at. That is
        // #266's signature, and it is exactly what these tests are meant to catch elsewhere.
        Assert.Equal(7, List.length everyCase)

    [<Fact>]
    let ``exactly ONE case renders empty on the wire, and it is NoStatus`` () =
        // `NoStatus -> ""` is the wire being honest: an unset column really is the empty string. But it
        // is a licence to render nothing, and the next case added must not quietly take it. A case that
        // renders "" would SET A COLUMN TO NOTHING and read back as `NoStatus` — a write that silently
        // unsets the very field it was asked to set.
        let empty = everyCase |> List.filter (fun (_, s) -> statusWireName s = "")

        Assert.Equal<string list>([ "NoStatus" ], empty |> List.map fst)

    [<Fact>]
    let ``no two cases share a wire name`` () =
        // Two cases spelling the same option name makes the board AMBIGUOUS in the read direction: the
        // column comes back as one string and there is no longer a fact about which case wrote it.
        let names = everyCase |> List.map (snd >> statusWireName)

        Assert.Equal<string list>(List.distinct names, names)

    [<Fact>]
    let ``a wire name is exactly what the board stores - no padding, no casing drift`` () =
        // Projects v2 matches its option names literally. `" Ready "` or `"in progress"` type-checks,
        // renders fine in a terminal, and matches NO option on the board — the write fails, or worse,
        // silently no-ops.
        for name, s in everyCase do
            let w = statusWireName s

            if name <> "NoStatus" then
                Assert.False(System.String.IsNullOrWhiteSpace w, $"case {name} renders blank on the wire")
                Assert.Equal(w.Trim(), w)

    [<Fact>]
    let ``the wire spellings are pinned to what the BOARD actually calls its columns`` () =
        // The one place the vocabulary is written down twice ON PURPOSE — here, against the engine. This
        // is the assertion that fails if somebody "tidies" `In progress` into `InProgress`, which is a
        // change no compiler and no other test in this file can object to.
        //
        // These are the six option names on the FS-GG "Coordination" board, plus the empty string for an
        // unset column. Sentence case, and `In progress`/`In review` are NOT title-cased — the board
        // spells them that way, and the board is the authority (#437 was a worker grepping for a status
        // that was rendered differently from how they were told).
        Assert.Equal("", statusWireName NoStatus)
        Assert.Equal("Backlog", statusWireName Backlog)
        Assert.Equal("Ready", statusWireName Ready)
        Assert.Equal("In progress", statusWireName InProgress)
        Assert.Equal("Blocked", statusWireName Blocked)
        Assert.Equal("In review", statusWireName InReview)
        Assert.Equal("Done", statusWireName Done)
