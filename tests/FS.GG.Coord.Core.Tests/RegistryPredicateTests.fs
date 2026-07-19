namespace FS.GG.Coord.Tests

open Xunit
open FS.GG.Coord
open FS.GG.Coord.RegistryPredicate

/// The ADR-0050 registry-predicate oracle (call-site A, .github#1202). The verdict logic is pure and
/// tested here with no filesystem; the Cli reads the bytes and hands in the owner declaration. Every
/// fail-closed path (#266) is a verdict of `Unknown`, never a refutation the oracle could not prove.
module RegistryPredicateTests =

    // A one-row slice of the real registry/skills.yml shape — a single-line flow-mapping whose
    // `materializes-when` value carries a COMMA inside brackets, the case a naive comma-split breaks.
    let private sampleRows =
        "# a comment line, skipped\n\
         skills:\n\
         \x20 - { id: fs-gg-playtest, scope: product, owner: fs-gg-game, source: FS.GG.Game/template/product-skills/fs-gg-playtest/SKILL.md, sha256: abc123, mirrored: false, materializes-when: \"profile in [game, sample-pack]\" }\n\
         \x20 - { id: fs-gg-game-core, scope: product, owner: fs-gg-game, source: FS.GG.Game/template/product-skills/fs-gg-game-core/SKILL.md, sha256: def456, mirrored: true }\n"

    // ---- parseRows -------------------------------------------------------------------------------

    [<Fact>]
    let ``parseRows lifts id, owner, source and every field`` () =
        let rows = parseRows sampleRows
        Assert.Equal(2, List.length rows)
        let row = (findRow rows "fs-gg-playtest").Value
        Assert.Equal(Some "fs-gg-game", row.Owner)
        Assert.Equal(Some "FS.GG.Game/template/product-skills/fs-gg-playtest/SKILL.md", row.Source)
        Assert.Equal(Some "false", Map.tryFind "mirrored" row.Fields)
        Assert.Equal(Some "product", Map.tryFind "scope" row.Fields)

    [<Fact>]
    let ``parseRows keeps a comma inside a bracketed/quoted value intact`` () =
        let row = (findRow (parseRows sampleRows) "fs-gg-playtest").Value
        Assert.Equal(Some "profile in [game, sample-pack]", Map.tryFind "materializes-when" row.Fields)

    [<Fact>]
    let ``parseRows skips comments and the header, keeping only data rows`` () =
        let rows = parseRows sampleRows
        Assert.All(rows, fun r -> Assert.False(System.String.IsNullOrEmpty r.Id))
        Assert.Equal<string list>([ "fs-gg-playtest"; "fs-gg-game-core" ], rows |> List.map (fun r -> r.Id))

    // ---- parseAssertion --------------------------------------------------------------------------

    let private formBody (id: string) (field: string) (value: string) =
        sprintf
            "### From (requesting repo / agent)\n\nFS.GG.Game\n\n### Asserted registry id\n\n%s\n\n### Asserted registry field\n\n%s\n\n### Asserted registry value\n\n%s\n\n### Priority\n\nnormal\n"
            id
            field
            value

    [<Fact>]
    let ``parseAssertion reads the id/field/value triple off the form headings`` () =
        Assert.Equal(
            Some { Id = "fs-gg-playtest"; Field = "mirrored"; Value = "true" },
            parseAssertion (formBody "fs-gg-playtest" "mirrored" "true")
        )

    [<Fact>]
    let ``parseAssertion is None when a field was left blank (_No response_)`` () =
        Assert.Equal(None, parseAssertion (formBody "fs-gg-playtest" "mirrored" "_No response_"))

    [<Fact>]
    let ``parseAssertion is None on a body with no assertion headings at all`` () =
        Assert.Equal(None, parseAssertion "### Request\n\nplease publish X\n")

    [<Fact>]
    let ``the three assertion labels are the ones the parser anchors on`` () =
        // Pinned so a future rename of one side is caught here; the run.sh e2e pins them to the FORM.
        Assert.Equal(("Asserted registry id", "Asserted registry field", "Asserted registry value"), assertionLabels)

    // ---- classify --------------------------------------------------------------------------------

    let private rows = parseRows sampleRows
    let private assert' id field value : Assertion = { Id = id; Field = field; Value = value }

    [<Fact>]
    let ``owner declares the asserted value -> Agrees`` () =
        Assert.Equal(Agrees, classify rows (Declares "false") (assert' "fs-gg-playtest" "mirrored" "false"))

    [<Fact>]
    let ``owner declares a DIFFERENT value -> Contradicts carrying the owner value (the #1194 instance)`` () =
        match classify rows (Declares "false") (assert' "fs-gg-playtest" "mirrored" "true") with
        | Contradicts(ownerValue, note) ->
            Assert.Equal("false", ownerValue)
            Assert.Contains(".github#658", note)
        | other -> failwithf "expected Contradicts, got %A" other

    [<Fact>]
    let ``comparison is case- and space-insensitive for mirrored`` () =
        Assert.Equal(Agrees, classify rows (Declares "false") (assert' "fs-gg-playtest" "mirrored" "  FALSE "))

    [<Fact>]
    let ``an absent owner value (Silent) is Unknown, never a refutation (.github#658)`` () =
        match classify rows Silent (assert' "fs-gg-playtest" "mirrored" "true") with
        | Unknown _ -> ()
        | other -> failwithf "expected Unknown, got %A" other

    [<Fact>]
    let ``an unreadable owner manifest is Unknown, fail-closed (#266)`` () =
        match classify rows (Unreadable "no checkout") (assert' "fs-gg-playtest" "mirrored" "true") with
        | Unknown reason -> Assert.Contains("no checkout", reason)
        | other -> failwithf "expected Unknown, got %A" other

    [<Fact>]
    let ``a missing registry row is Unknown before the owner is ever consulted`` () =
        match classify rows (Declares "true") (assert' "no-such-skill" "mirrored" "true") with
        | Unknown _ -> ()
        | other -> failwithf "expected Unknown, got %A" other

    [<Fact>]
    let ``an unsupported field is Unknown even when the owner Declares a value`` () =
        // Only `mirrored` is compared today; `materializes-when` abstains rather than fork the normalizer.
        Assert.False(supportsField "materializes-when")
        match classify rows (Declares "profile in [game]") (assert' "fs-gg-playtest" "materializes-when" "profile in [game]") with
        | Unknown _ -> ()
        | other -> failwithf "expected Unknown, got %A" other

    [<Fact>]
    let ``supportsField is true only for mirrored`` () =
        Assert.True(supportsField "mirrored")
        Assert.True(supportsField "  MIRRORED ")
        Assert.False(supportsField "sha256")
        Assert.False(supportsField "owner")

    [<Fact>]
    let ``name is the verdict word`` () =
        Assert.Equal("agrees", name Agrees)
        Assert.Equal("contradicts", name (Contradicts("false", "note")))
        Assert.Equal("unknown", name (Unknown "reason"))
