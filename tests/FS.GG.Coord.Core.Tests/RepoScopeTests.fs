namespace FS.GG.Coord.Tests

open Xunit
open FS.GG.Coord

/// `.github#2363` criteria 1 and 4 — the union-typed `RepoScope.resolve` (`.github#2398`) is tested at its
/// OWN layer, not only through `Options.resolveRepo`'s CLI wrapper (`tests/FS.GG.Coord.Cli.Tests/
/// RepoScopeTests.fs`, added by #2398). `Options.resolveRepo raw = RepoScope.resolve raw` is a one-line
/// forward (`Options.fs:1327-1328`), so the wrapper's tests and these prove the identical fact about the
/// identical function — but `RepoScope` is a `FS.GG.Coord.Core` module and #2363's declared `Paths:` name
/// `src/FS.GG.Coord.Core/RepoScope.fs` directly, so this module is where the property is pinned at its
/// own source rather than only at a consumer one layer up.
module RepoScopeTests =

    // ---- criterion 1/4: `sir` and `S.I.R.` resolve to the identical `Scope` value -----------------

    [<Theory>]
    [<InlineData("sir")>]
    [<InlineData("Sir")>]
    [<InlineData("SIR")>]
    [<InlineData("S.I.R.")>]
    let ``sir and S.I.R. resolve to the identical Scope value, at RepoScope's own layer`` (raw: string) =
        Assert.Equal(RepoScope.Repository "S.I.R.", RepoScope.resolve raw)

    /// Gate-inversion evidence, actually run (not merely argued): deleting `RepoScope.fs`'s
    /// `| "sir" -> Repository "S.I.R."` arm (so `"sir"` falls through to the generic `_` arm and resolves
    /// to `Repository "sir"`, the raw un-canonicalized token) turned 6 of these 9 tests red —
    /// `dotnet test tests/FS.GG.Coord.Core.Tests --filter FullyQualifiedName~RepoScopeTests` reported
    /// `Failed: 6, Passed: 3` with `Expected: Repository "S.I.R." / Actual: Repository "sir"` (and the
    /// analogous `orFallback` failures) for the `sir`/`Sir`/`SIR` cases — `"S.I.R."` itself still passed
    /// by construction, since it already falls through to the same default arm unchanged. Restoring the
    /// arm reran green: `Failed: 0, Passed: 9`.

    // ---- criterion 4: `orFallback` composed onto `resolve` converges both spellings to ONE string ----

    /// The composed function every real consumer actually calls (`enrich`, `Lanes.pathRepoOf`,
    /// `Options.resolveRepoName`) is `orFallback fallback (resolve raw)`, never `resolve` alone — a bare
    /// `Scope` cannot be written to a board field or compared against another repo's plain name. Proving
    /// spelling convergence at `resolve` (above) is necessary but not sufficient: this closes the gap by
    /// showing the SAME convergence survives the one additional step every real call site takes.
    [<Theory>]
    [<InlineData("sir")>]
    [<InlineData("Sir")>]
    [<InlineData("SIR")>]
    [<InlineData("S.I.R.")>]
    let ``orFallback composed onto resolve converges every sir spelling to the identical canonical string``
        (raw: string)
        =
        // `fallback` is deliberately a value neither spelling could ever equal — if `orFallback` fell
        // through to it for either input, this assertion (not a later one) is what would catch it.
        Assert.Equal("S.I.R.", RepoScope.orFallback "never-the-fallback" (RepoScope.resolve raw))

    /// Gate-inversion evidence, actually run: mutating `orFallback`'s `Repository name -> name` arm to
    /// `Repository _ -> fallback` (treating every resolved repository as if it were the non-repository
    /// sentinel) turned exactly this theory's 4 cases red — `Failed: 4, Passed: 5` on the same filtered
    /// run, `Expected: "S.I.R." / Actual: "never-the-fallback"` for all four — while the `resolve`-only
    /// theory above and the `cross-repo` fact stayed green, isolating the mutation to the one function it
    /// targeted. Restoring the arm reran green: `Failed: 0, Passed: 9`.

    // ---- `cross-repo` still resolves to NonRepository at this layer too (defense-in-depth with #2398) --

    [<Fact>]
    let ``cross-repo still resolves to NonRepository at RepoScope's own layer`` () =
        match RepoScope.resolve "cross-repo" with
        | RepoScope.NonRepository "cross-repo" -> ()
        | other -> failwithf "cross-repo must resolve to NonRepository \"cross-repo\" — got %A" other
