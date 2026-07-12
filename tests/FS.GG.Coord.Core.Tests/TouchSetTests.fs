namespace FS.GG.Coord.Tests

open Xunit
open FsCheck
open FsCheck.Xunit
open FS.GG.Coord
open FS.GG.Coord.Types

/// The `Paths:` grammar. Every test here is an incident.
module TouchSetTests =

    // ---- #277: a declaration is a line you WROTE as one --------------------------------------------
    // A `Paths:` line inside a fenced code block is a QUOTATION of the grammar, not a use of it — and
    // the protocol docs quote it constantly. An item that "declares" its touch-set only inside a fence
    // has declared nothing, and a token that reserves nothing conflicts with nothing, which is how two
    // workers end up in one file. The rule: unschedulable beats mis-scheduled.

    [<Fact>]
    let ``#277 a Paths: line inside a fence is a QUOTATION, not a declaration`` () =
        let body =
            "Here is how you declare a touch-set:\n\
             \n\
             ```\n\
             Paths: src/Example/**\n\
             ```\n\
             \n\
             ...and this issue does not declare one."

        Assert.Equal(Undeclared, TouchSet.parse body)

    [<Fact>]
    let ``#277 a real declaration OUTSIDE the fence is still found`` () =
        let body =
            "Quoting the grammar:\n\
             \n\
             ```\n\
             Paths: src/Quoted/**\n\
             ```\n\
             \n\
             Paths: src/Real/**"

        Assert.Equal(Declared [ Matchable "src/Real/**" ], TouchSet.parse body)

    // ---- #435: a BACKTICKED declaration was refused as unmatchable ---------------------------------
    // ...so the item silently never scheduled, and `take` reported an empty queue over it. Backticks
    // are markdown, not grammar.

    [<Fact>]
    let ``#435 backticks are stripped — a backticked declaration is a declaration`` () =
        Assert.Equal(Declared [ Matchable "src/Scene/**" ], TouchSet.parse "Paths: `src/Scene/**`")

    // ---- #496: `Paths: none` is a DECISION; a missing line is an OMISSION ---------------------------
    // Before they were told apart they rendered identically, so no gate could be written at all: nine
    // items of real work went invisible to every worker who asked for work, and the one surface whose
    // job is board health reported `0 error(s)` over a dead queue. Neither is schedulable. Only one is
    // a bug. THE TYPE now makes conflating them impossible.

    [<Fact>]
    let ``#496 'Paths: none' is the SENTINEL, not an omission`` () =
        Assert.Equal(DeclaredNone, TouchSet.parse "An epic.\n\nPaths: none")

    [<Fact>]
    let ``#496 no Paths: line at all is an OMISSION, and a different fact`` () =
        Assert.Equal(Undeclared, TouchSet.parse "An item somebody forgot to declare.")

    [<Fact>]
    let ``#496 the two are not equal — the whole point of the issue`` () =
        Assert.NotEqual<TouchSet>(DeclaredNone, Undeclared)

    // ---- #273: not a glob language ------------------------------------------------------------------
    // A token that matches nothing CONFLICTS WITH NOTHING, so it reads as DISJOINT against every other
    // worker — ADR-0021's own failure, one level down: a lock that succeeds under exactly the
    // conditions it exists to prevent.

    [<Theory>]
    [<InlineData("src/Scene/**")>]
    [<InlineData("src/Scene/*")>]
    [<InlineData("src/Scene/")>]
    [<InlineData("Directory.Packages.props")>]
    let ``#273 the sanctioned grammar is matchable`` (token: string) =
        match TouchSet.classify token with
        | Matchable _ -> ()
        | Unmatchable t -> failwith $"'%s{t}' is part of the grammar and must be matchable"

    [<Theory>]
    [<InlineData("**/packages.lock.json")>] // a LEADING **/ matches nothing
    [<InlineData("src/*/Types.fs")>] // a * in the MIDDLE matches nothing
    [<InlineData("src/Scene/?.fs")>]
    [<InlineData("src/[Ss]cene/**")>]
    let ``#273 a token that can match no file is refused, never tolerated`` (token: string) =
        match TouchSet.classify token with
        | Unmatchable _ -> ()
        | Matchable t -> failwith $"'%s{t}' can match no file — treating it as a token makes it DISJOINT against everyone"

    // ---- overlap: exact equality OR subtree containment, either direction ---------------------------

    [<Fact>]
    let ``overlap: a directory contains its own files`` () =
        Assert.True(TouchSet.tokensOverlap "src/Scene" "src/Scene/Types.fs")
        Assert.True(TouchSet.tokensOverlap "src/Scene/Types.fs" "src/Scene")

    [<Fact>]
    let ``overlap: a trailing glob is the same subtree`` () =
        Assert.True(TouchSet.tokensOverlap "src/Scene/**" "src/Scene/Types.fs")

    [<Fact>]
    let ``overlap: a PREFIX is not a subtree — src/Scene must not swallow src/SceneGraph`` () =
        // The `/` in the containment test is the whole difference. Without it, every worker in
        // `src/Scene` would block every worker in `src/SceneGraph`, and the scheduler would serialise
        // the repo for no reason.
        Assert.False(TouchSet.tokensOverlap "src/Scene" "src/SceneGraph/Types.fs")

    [<Fact>]
    let ``#309 declaring a PARENT reserves the child exactly as effectively as naming it`` () =
        // The trap behind #309: FS.GG.Game declared `readiness/**` to cover a generated baseline, and
        // every [core] item then pairwise-overlapped every other — the whole P6 Game phase collapsed to
        // ONE worker, in the phase the protocol exists to fan out.
        let parent = Declared [ Matchable "readiness/**" ]
        let child = Declared [ Matchable "readiness/surface-baselines/pkg.txt" ]
        Assert.NotEmpty(TouchSet.conflicts parent child)

    [<Fact>]
    let ``disjoint touch-sets may run in parallel`` () =
        let a = Declared [ Matchable "src/Scene/**" ]
        let b = Declared [ Matchable "src/Audio/**" ]
        Assert.Empty(TouchSet.conflicts a b)

    // ---- properties ---------------------------------------------------------------------------------

    [<Property>]
    let ``overlap is symmetric`` (a: NonNull<string>) (b: NonNull<string>) =
        TouchSet.tokensOverlap a.Get b.Get = TouchSet.tokensOverlap b.Get a.Get

    [<Property>]
    let ``a token always overlaps itself`` (t: NonNull<string>) =
        TouchSet.tokensOverlap t.Get t.Get

    [<Fact>]
    let ``an unmatchable token is never silently dropped — it survives as a NAMED case`` () =
        // The failure mode this forecloses: a parser that discards what it cannot understand produces
        // a touch-set that looks smaller and cleaner than the truth, and reserves less than the author
        // asked for. Every token must come back, classified.
        match TouchSet.parse "Paths: src/Ok/**, **/bad.json" with
        | Declared tokens ->
            Assert.Equal(2, List.length tokens)
            Assert.Equal<string list>([ "**/bad.json" ], TouchSet.unmatchable (Declared tokens))
        | other -> failwith $"expected a declaration, got %A{other}"
