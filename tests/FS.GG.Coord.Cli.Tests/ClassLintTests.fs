namespace FS.GG.Coord.Cli.Tests

open System
open Xunit
open FS.GG.Coord
open FS.GG.Coord.Types
open FS.GG.Coord.Cli

/// `lint` NAMES THE FAULT IT FOUND — absent and out-of-vocabulary are different faults (.github#1651).
///
/// THE DEFECT THIS PINS, measured twice in one run on 2026-07-27, in two repos, by two workers, with two
/// different invented words:
///
///   `FS.GG.Templates#321`  wrote `Class: docs`         and meant `hardening`
///   `FS.GG.SDD#747`        wrote `Class: enhancement`  and meant `hardening`
///
/// BOTH WROTE A `Class:` LINE. Neither forgot one. `Class.derive` answers `None` for "no line" and for
/// "a line whose word we do not speak" alike, so the single `CLASS-UNSET` rule told both authors their
/// text *"records no `Class:`"* — false, and false in the most expensive direction: the reader goes
/// looking for a missing line, finds a present one, and has to work out unaided that the VALUE is the
/// problem. Under ADR-0066 an unclassed row counts as a POSSIBLE defect, so neither row degraded to
/// "roughly classed" — each blocked a clean termination read for the whole board until a human read the
/// body by hand.
///
/// WHY THE FIXTURES ARE A MATRIX AND NOT AN EXAMPLE. AC4 asks for absent, each of the three legal values,
/// an out-of-vocabulary word, and a case/whitespace variant, because the rule that must hold is not "this
/// one word reds" but "the two causes never render alike, in either direction". A test over `docs` alone
/// would stay green if the fix had simply renamed the finding.
module ClassLintTests =

    let private status = "Ready"

    let private verdict (title: string) (body: string) = Client.classVerdict status title body

    let private codeOf title body =
        verdict title body |> Option.map fst

    let private detailOf title body =
        match verdict title body with
        | Some(_, detail) -> detail
        | None -> failwith "expected a finding and there was none"

    // ---- AC4's fixture matrix ----------------------------------------------------------------------

    [<Fact>]
    let ``ABSENT - a body with no Class line at all is CLASS-UNSET`` () =
        Assert.Equal(Some "CLASS-UNSET", codeOf "Ordinary work" "Paths: src/A/**\n\nJust prose.")

    [<Fact>]
    let ``VALID - each of the three legal values classes the row and produces NO finding`` () =
        // Driven from the UNION, not from a literal list of three: a fourth `ItemClass` arrives here with
        // no edit, and a case that stopped being accepted fails this test rather than a reviewer's memory.
        for c in Class.legalClasses do
            let word = itemClassWireName c
            let body = $"Paths: src/A/**\n\nClass: %s{word}\n"
            let found = verdict "Ordinary work" body

            Assert.True(found.IsNone, $"`Class: %s{word}` is a legal declaration and produced a finding: %A{found}")

    [<Fact>]
    let ``OUT OF VOCABULARY - the two words actually measured are CLASS-INVALID, never CLASS-UNSET`` () =
        for word in [ "docs"; "enhancement" ] do
            let body = $"Paths: src/A/**\n\nClass: %s{word}\n"
            Assert.Equal(Some "CLASS-INVALID", codeOf "Ordinary work" body)

    [<Fact>]
    let ``the invalid diagnostic never says the row records no Class - that is the whole bug`` () =
        // THE REGRESSION, STATED AS THE SENTENCE THAT MUST NOT APPEAR. Both live instances were reported
        // with these words over a body that had one.
        for word in [ "docs"; "enhancement"; "bug"; "P1" ] do
            let detail = detailOf "Ordinary work" $"Paths: src/A/**\n\nClass: %s{word}\n"

            Assert.False(
                detail.Contains "records no `Class:`",
                $"`Class: %s{word}` was described as recording no `Class:` — the diagnostic names the wrong fault (.github#1651)"
            )

    [<Fact>]
    let ``AC3 - the invalid diagnostic QUOTES the offending value back`` () =
        // Not "invalid class". Both authors believed they were classing the row correctly, so the value
        // they wrote has to appear or they cannot see which of their lines is meant.
        Assert.Contains("\"enhancement\"", detailOf "Ordinary work" "Class: enhancement")

    [<Fact>]
    let ``AC3 - the invalid diagnostic lists EVERY legal value, read from the union`` () =
        let detail = detailOf "Ordinary work" "Class: docs"

        for c in Class.legalClasses do
            Assert.Contains($"`Class: %s{itemClassWireName c}`", detail)

    [<Fact>]
    let ``AC4 DECISION - a case or whitespace variant of a legal value is ACCEPTED`` () =
        // DECIDED, NOT LEFT TO CHANCE, and decided the way ADR-0066 already wrote it down: *"value
        // normalised for case and space"*. The normalisation is `Types.itemClassOfWireName`'s
        // `Trim().ToLowerInvariant()` — the SAME function `reconcile` uses to read the board column back,
        // so accepting `Class: Defect` here is not a leniency, it is the projection round-tripping.
        //
        // Rejecting a case variant would take a correctly-triaged row and report it as untriaged, which is
        // this item's own failure mode reintroduced by its own fix.
        //
        // THE KEY IS A DIFFERENT QUESTION FROM THE VALUE, and its answer is `Class:` or `class:` and
        // nothing else. That is not this module's choice: `[Cc]lass:` is the shape `TouchSet`'s
        // `[Pp]aths:` and `HumanBlock`'s `[Bb]locked [Oo]n:` already have, and #1103 decided ONE grammar
        // for body-line sentinels. A third spelling in one of three sentinels is the drift ADR-0045
        // exists to prevent, so `CLASS: defect` is not a `Class:` line at all and reads as absent.
        for variant in [ "Class: Defect"; "Class: DEFECT"; "class: defect"; "Class:    defect   "; "  Class: Defect" ] do
            Assert.True(
                (verdict "Ordinary work" $"Paths: src/A/**\n\n%s{variant}\n").IsNone,
                $"'%s{variant}' is a legal declaration under ADR-0066's normalisation and was refused"
            )

        // The key-case answer, pinned so the decision is a fact rather than a habit.
        Assert.Equal(Some "CLASS-UNSET", codeOf "Ordinary work" "Paths: src/A/**\n\nCLASS: defect\n")

    // ---- the edges the two live instances did not cover --------------------------------------------

    [<Fact>]
    let ``a Class line with an EMPTY value is invalid, not absent`` () =
        // The key was declared and the value could not be read. #266's rule: a subject you could not
        // evaluate is never a subject that passed — and "you wrote nothing" is a different instruction
        // from "you wrote the wrong word", so the value is rendered rather than quoted as "".
        Assert.Equal(Some "CLASS-INVALID", codeOf "Ordinary work" "Paths: src/A/**\n\nClass:\n")
        Assert.Contains("(empty)", detailOf "Ordinary work" "Class:   \n")

    [<Fact>]
    let ``a FENCED Class line declares nothing, so it is ABSENT and not invalid`` () =
        // A fenced line QUOTES the grammar, it does not use it (#277) — this very repo's ADR and issue
        // bodies quote `Class: docs` while explaining the bug. Reporting them as invalid declarations
        // would make the fix fire on the documentation of the fix.
        let body = "How to class a row:\n\n```\nClass: docs\n```\n\n...but this body never wrote a real one.\n"
        Assert.Equal(Some "CLASS-UNSET", codeOf "Ordinary work" body)

    [<Fact>]
    let ``an invalid line is reported even when some OTHER evidence classes the row`` () =
        // `[decision]` in the title, or ADR-0045's sentinel, resolves the row — and the body still holds a
        // word this engine does not speak, which the next reader will trust. Rescuing the row is not the
        // same as the line being right, and only one of those two facts is `derive`'s business.
        Assert.Equal(Some "CLASS-INVALID", codeOf "[decision] X or Y" "Paths: none\n\nClass: docs\n")

        Assert.Equal(
            Some "CLASS-INVALID",
            codeOf "Ordinary work" "Paths: none\n\nBlocked on: human/decision\n\nClass: enhancement\n"
        )

    [<Fact>]
    let ``an invalid line SUPPRESSES CLASS-UNSET - one fault, one finding`` () =
        // Emitting both would be the collapse this item is about wearing two hats: a body that wrote a
        // line did not omit one, and a driver handed both codes learns nothing it can act on.
        Assert.Equal(Some "CLASS-INVALID", codeOf "Ordinary work" "Class: docs")

    [<Fact>]
    let ``a valid line beside an invalid one still reports the invalid one`` () =
        let body = "Class: defect\n\nClass: docs\n"
        Assert.Equal(Some "CLASS-INVALID", codeOf "Ordinary work" body)
        Assert.Contains("\"docs\"", detailOf "Ordinary work" body)

    // ---- AC5: the vocabulary is READ, not restated -------------------------------------------------

    [<Fact>]
    let ``AC5 the legal set the checker renders IS the union, case for case`` () =
        // #916's receipt is that hand-copied rule sets AGREE with each other and are wrong the whole time,
        // so "the menu lists three words" is not the assertion worth making. This one is: the menu holds
        // exactly one entry per union case, found by reflection, so a fourth `ItemClass` cannot reach the
        // board's vocabulary while `lint` keeps offering three — and nobody has to remember to come here.
        let cases = Reflection.FSharpType.GetUnionCases typeof<ItemClass>

        Assert.Equal(cases.Length, List.length Class.legalClasses)

        for case in cases do
            let c = Reflection.FSharpValue.MakeUnion(case, [||]) :?> ItemClass
            Assert.Contains(c, Class.legalClasses)
            Assert.Contains($"`Class: %s{itemClassWireName c}`", LintApplication.classMenu)

        // And the checker states no OTHER word as legal. Counting the rendered entries is what catches a
        // literal smuggled into the menu beside the derived ones.
        Assert.Equal(
            cases.Length,
            LintApplication.classMenu.Split("`Class: ").Length - 1
        )

    [<Fact>]
    let ``AC5 the ABSENT diagnostic reads the same vocabulary`` () =
        // `CLASS-UNSET`'s sentence used to spell the three words by hand. It was RIGHT, which is exactly
        // how #916's defect gets in — the copy that agrees is still a copy.
        let detail = detailOf "Ordinary work" "Paths: src/A/**\n\nJust prose."

        for c in Class.legalClasses do
            Assert.Contains($"`Class: %s{itemClassWireName c}`", detail)

    // ---- `add`'s refusal is the SAME sentence (AC1) -------------------------------------------------

    [<Fact>]
    let ``AC1 add refuses exactly the bodies lint calls invalid, with the same words`` () =
        // ONE SENTENCE, TWO CALLERS. `add` is the engine's first sight of an item — the issue itself is
        // written with `gh` — so it is the only place a closed vocabulary can be enforced while the author
        // is still standing there. A second hand-written refusal would be the drift in the fix.
        Assert.True((Client.outOfVocabularyClass "Paths: src/A/**\n\nClass: defect\n").IsNone)
        Assert.True((Client.outOfVocabularyClass "Paths: src/A/**\n\nJust prose.").IsNone)

        match Client.outOfVocabularyClass "Class: docs", verdict "Ordinary work" "Class: docs" with
        | Some refusal, Some(_, lintDetail) ->
            Assert.Contains(refusal, lintDetail)
            Assert.Contains("\"docs\"", refusal)
        | other -> failwith $"add and lint must agree on an out-of-vocabulary class: %A{other}"

    [<Fact>]
    let ``AC1 add does NOT refuse an unclassed body - unboarded is worse than untriaged`` () =
        // Refusing an item with no `Class:` line would make "filed but never boarded" a routine outcome,
        // and an unboarded row is invisible to every scheduler rather than merely untriaged. `lint`'s
        // `CLASS-UNSET` is the rule that owns absence, and it can only fire on a row that is ON the board.
        Assert.True((Client.outOfVocabularyClass "Paths: none\n\nAn epic.").IsNone)
