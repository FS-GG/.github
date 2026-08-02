namespace FS.GG.Coord.Cli.Tests

open Xunit
open FS.GG.Coord.Types
open FS.GG.Coord.Cli

/// #548: `<ref>` accepts a bare `n`/`#n`, resolved against the checkout you are standing in.
///
/// The bug these cover is not a papercut. `parseRef` accepted three forms — a URL, `owner/repo#n`,
/// `repo#n` — and every recipe hands a worker a FOURTH: `heartbeat <issue>`, `done <issue> --flip`,
/// `widen <issue>`, typed straight after `take` printed the item. The refusal ended as an expired lease
/// and two workers on one item (`heartbeat`, unattended), as merged work with the board un-flipped
/// (`done`), and as a usage error indistinguishable from a collision (`widen`).
///
/// `parseRefIn` is the pure core `parseRef` delegates to — `parseRef` takes a `Context` carrying a live
/// transport, which is not constructible here, so the parser is exposed and tested directly. Same idiom
/// as `RepoScopeTests` over `parseGitHubSlug`.
///
/// The end-to-end legs (a real checkout, a real remote) belong to the coord-engine parity harness; what
/// parity cannot cheaply reach — and what the ask names as the one thing to get right — is the REFUSAL
/// when there is no repo to infer from.
module RefParseTests =

    let private owner = "FS-GG"

    // ---- the four accepted forms -------------------------------------------------------------------

    [<Theory>]
    // The bare form (#548), with and without the `#` — matching `Blockers.canonToken`, which has always
    // taken `#n`. Both spellings resolve against the checkout.
    [<InlineData("548", "FS-GG", ".github", 548)>]
    [<InlineData("#548", "FS-GG", ".github", 548)>]
    // The three forms that already worked, held here so the bare form cannot regress them.
    [<InlineData("FS.GG.Game#171", "FS-GG", "FS.GG.Game", 171)>]
    [<InlineData("FS-GG/FS.GG.Rendering#543", "FS-GG", "FS.GG.Rendering", 543)>]
    [<InlineData("https://github.com/FS-GG/FS.GG.SDD/issues/393", "FS-GG", "FS.GG.SDD", 393)>]
    let ``every accepted ref form parses`` (raw: string) (eOwner: string) (eRepo: string) (eNum: int) =
        let expected: Ref =
            { Owner = eOwner
              Repo = eRepo
              Number = eNum }

        Assert.Equal(Ok expected, Client.parseRefIn owner (Some ".github") raw)

    [<Fact>]
    let ``a bare number resolves against the repo it is given, not the board's own`` () =
        // The default is the CHECKOUT — a worker standing in FS.GG.Game who types `171` means Game#171,
        // never `.github#171`. This is the whole point of the field; a constant would be a new bug.
        let expected: Ref =
            { Owner = owner
              Repo = "FS.GG.Game"
              Number = 171 }

        Assert.Equal(Ok expected, Client.parseRefIn owner (Some "FS.GG.Game") "171")

    [<Fact>]
    let ``an explicit ref beats the default repo`` () =
        // A qualified ref names its own repo; the checkout must not override it.
        let expected: Ref =
            { Owner = owner
              Repo = "FS.GG.Audio"
              Number = 12 }

        Assert.Equal(Ok expected, Client.parseRefIn owner (Some ".github") "FS.GG.Audio#12")

    // ---- the refusal: no repo to infer from --------------------------------------------------------

    [<Theory>]
    [<InlineData("506")>]
    [<InlineData("#506")>]
    let ``a bare number with no repo to infer is a hard error`` (raw: string) =
        // `None` is what `defaultRepoScope` yields outside any checkout, and — the case the ask singles
        // out — inside a checkout whose owner is not the board's. Addressing `FS-GG/<something>#506` on
        // that evidence would be silently wrong, so it must refuse.
        match Client.parseRefIn owner None raw with
        | Ok r -> failwith $"expected a refusal, got %s{r.Owner}/%s{r.Repo}#%d{r.Number}"
        | Error msg ->
            // ASSERT THE MESSAGE, not just the failure: an rc-only assertion cannot tell "no repo to
            // infer" from any other usage error, and telling those apart is the acceptance criterion.
            // #611 is the precedent — a diagnostic that names the wrong cause sends the reader to check
            // the one thing that was never wrong.
            Assert.Contains("cannot resolve the bare issue number", msg)
            Assert.Contains(raw, msg)
            Assert.Contains("not a FS-GG checkout", msg)
            // It still names the accepted forms, so the remedy is in the message that refused.
            Assert.Contains("owner/repo#n", msg)

    // ---- the refusal: not a ref at all -------------------------------------------------------------

    [<Theory>]
    [<InlineData("")>]
    [<InlineData("nonsense")>]
    [<InlineData("548x")>] // anchored: a number with trailing prose is not a ref
    [<InlineData("x548")>]
    [<InlineData("5.4")>]
    [<InlineData("-1")>]
    [<InlineData("FS.GG.Game#")>] // a repo with no number
    [<InlineData("#")>]
    let ``a token that is not a ref is refused even with a default repo`` (raw: string) =
        // A default repo must not turn junk into a ref. The bare form is `^#?(\d+)$` — anchored, digits
        // only — so it widens the grammar by exactly one shape and nothing else.
        match Client.parseRefIn owner (Some ".github") raw with
        | Ok r -> failwith $"expected a refusal, got %s{r.Owner}/%s{r.Repo}#%d{r.Number}"
        | Error msg -> Assert.Contains("unrecognised issue ref", msg)

    // ---- the refusal: digits that cannot be an issue number ----------------------------------------

    [<Theory>]
    [<InlineData("99999999999999999999")>] // the bare form (#548) — newly reachable
    [<InlineData("#99999999999999999999")>]
    [<InlineData(".github#99999999999999999999")>] // ...and the three forms that always were
    [<InlineData("FS-GG/.github#99999999999999999999")>]
    [<InlineData("https://github.com/FS-GG/.github/issues/99999999999999999999")>]
    let ``a number too large for an issue is a REFUSAL, not an engine defect`` (raw: string) =
        // `int` THROWS on a digit run past Int32, and an unhandled throw here hits the defect boundary:
        // `heartbeat 99999999999999999999` — an ordinary typo — exited 2, which the take-exit-code
        // contract reads as "the ENGINE broke — report it, do not retry". A worker would report a bug
        // that does not exist and never learn what they actually mistyped. A bad argument is a bad
        // argument: refuse it, name the range, and stay out of the defect channel.
        match Client.parseRefIn owner (Some ".github") raw with
        | Ok r -> failwith $"expected a refusal, got #%d{r.Number}"
        | Error msg ->
            Assert.Contains("out of range", msg)
            Assert.Contains("2147483647", msg)

    [<Fact>]
    let ``the largest Int32 issue number still parses`` () =
        // The boundary itself is legal — the refusal must start one past it, not at it.
        match Client.parseRefIn owner (Some ".github") "2147483647" with
        | Ok r -> Assert.Equal(2147483647, r.Number)
        | Error msg -> failwith $"expected a parse, got: %s{msg}"

    // ---- the owner guard: the ask's one ambiguity criterion ----------------------------------------

    [<Theory>]
    [<InlineData("FS-GG/.github", ".github")>]
    [<InlineData("FS-GG/FS.GG.Game", "FS.GG.Game")>]
    [<InlineData("fs-gg/FS.GG.Audio", "FS.GG.Audio")>] // the owner compares case-insensitively, as GitHub does
    let ``a checkout owned by the board yields its repo as the bare-n default`` (slug: string) (expected: string) =
        Assert.Equal(Some expected, Client.defaultRepoForOwner owner slug)

    [<Theory>]
    [<InlineData("acme/thing")>] // another org entirely — the case the ask singles out
    [<InlineData("FS-GG-evil/.github")>] // a lookalike owner is NOT the board's
    [<InlineData("notfsgg/FS.GG.Game")>] // ...even when the REPO name is one of ours
    let ``a checkout owned by anyone else yields no default`` (slug: string) =
        // Without this, `resolveRepo` would throw the owner away and a bare `506` in an `acme/thing`
        // checkout would silently address `FS-GG/thing#506` — an issue in a repo the caller is not
        // standing in. That is the one thing the issue says to get right, and it is why this is not
        // simply `scopedRepo`. `None` here is what makes `parseRefIn` refuse.
        Assert.Equal(None, Client.defaultRepoForOwner owner slug)

    // ---- .github#2107: a closing keyword next to the board's OWN shorthand ------------------------
    //
    // `.github#2095` is exactly `parse`'s `short` form — the one this file's own `every accepted ref
    // form parses` theory confirms is a LEGAL board ref. It is also the one form GitHub's own
    // closing-keyword grammar does not parse: GitHub wants a bare `#<n>` (same-repo) or `owner/repo#<n>`
    // (cross-repo), and `.github#2095` is neither. Two confirmed occurrences (.github#2095/PR#2099,
    // .github#2098/PR#2103) both merged with the issue left open, and both needed a manual close because
    // editing a merged PR's body does not replay it.

    [<Theory>]
    // The real sentence from both confirmed occurrences.
    [<InlineData("Closes .github#2095", "Closes .github#2095", ".github#2095", "2095")>]
    [<InlineData("Closes .github#2098.", "Closes .github#2098", ".github#2098", "2098")>]
    // Every keyword GitHub's own grammar recognises (check-closing-keywords.py's list), case-insensitive.
    [<InlineData("closes FS.GG.Game#12", "closes FS.GG.Game#12", "FS.GG.Game#12", "12")>]
    [<InlineData("CLOSED FS.GG.Game#12", "CLOSED FS.GG.Game#12", "FS.GG.Game#12", "12")>]
    [<InlineData("Fix .github#1", "Fix .github#1", ".github#1", "1")>]
    [<InlineData("fixes .github#1", "fixes .github#1", ".github#1", "1")>]
    [<InlineData("Fixed .github#1", "Fixed .github#1", ".github#1", "1")>]
    [<InlineData("Resolve .github#1", "Resolve .github#1", ".github#1", "1")>]
    [<InlineData("resolves .github#1", "resolves .github#1", ".github#1", "1")>]
    [<InlineData("Resolved .github#1", "Resolved .github#1", ".github#1", "1")>]
    let ``a closing keyword next to board shorthand is caught, with the ref and number split out``
        (body: string)
        (matched: string)
        (ref_: string)
        (number: string)
        =
        match RefParsing.boardShorthandCloses body with
        | [ f ] ->
            Assert.Equal(matched, f.Matched)
            Assert.Equal(ref_, f.Ref)
            Assert.Equal(number, f.Number)
        | other -> failwith $"expected exactly one finding, got %A{other}"

    [<Theory>]
    // The bare form — the same-repo GitHub keyword this org's own docs prescribe.
    [<InlineData("Closes #2095")>]
    [<InlineData("closes #2095.")>]
    // The full cross-repo form — GitHub's other legal shape.
    [<InlineData("Closes FS-GG/.github#2095")>]
    [<InlineData("Fixes owner/repo#12")>]
    // No keyword at all: a bare board ref mentioned in prose, not adjacent to a closing verb.
    [<InlineData("See .github#2095 for context.")>]
    // Prose that merely CONTAINS a keyword as a substring must not false-positive (#643's own trap, here
    // for word-boundary correctness rather than negation).
    [<InlineData("This unfixed problem predates .github#2095.")>]
    [<InlineData("The closest match is .github#2095.")>]
    let ``neither valid GitHub form nor keyword-free prose is ever flagged`` (body: string) =
        Assert.Empty(RefParsing.boardShorthandCloses body)

    [<Fact>]
    let ``multiple defects in one body are ALL reported, in order — 'closes #1, #2' drops the second`` () =
        // The doc precedent (pnext-item deep-detail.md) is that a caller must repeat the keyword for a
        // second ref to bind at all — so this fixture repeats it, matching the only form that is even a
        // candidate defect for BOTH refs.
        let body = "Closes .github#1, closes .github#2."

        match RefParsing.boardShorthandCloses body with
        | [ a; b ] ->
            Assert.Equal(".github#1", a.Ref)
            Assert.Equal(".github#2", b.Ref)
        | other -> failwith $"expected exactly two findings in order, got %A{other}"

    [<Fact>]
    let ``an empty body has no findings`` () = Assert.Empty(RefParsing.boardShorthandCloses "")
