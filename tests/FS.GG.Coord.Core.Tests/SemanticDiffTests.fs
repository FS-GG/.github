namespace FS.GG.Coord.Tests

open Xunit
open FS.GG.Coord.SemanticDiff

module SemanticDiffTests =
    let receipt baseSha headSha occurrences =
        receipt "FS-GG/rogue3" baseSha headSha "oldName" "newName" [ "src/Rogue3/Protocol.fs" ] true occurrences

    [<Fact>]
    let ``quoted F# rename is inventoried even though the identifier rename compiles`` () =
        let occurrences =
            inventory
                "src/Rogue3/Protocol.fs"
                "let oldName = 1\nlet message = \"oldName\"\nlet escaped = \"\\\"oldName\\\"\"\nlet interpolation = $\"{oldName}\""
                "let newName = 1\nlet message = \"newName\"\nlet escaped = \"\\\"newName\\\"\"\nlet interpolation = $\"{newName}\""
                "oldName"
                "newName"

        Assert.Equal(3, List.length occurrences)
        Assert.All(occurrences, fun occurrence -> Assert.Equal(Unresolved, occurrence.Disposition))

        Assert.True(
            validate "base" "head" (receipt "base" "head" occurrences)
            |> List.exists (fun error -> error.Contains "unresolved")
        )

    [<Fact>]
    let ``comments character literals docs generated tests and identifier-only edits classify deterministically`` () =
        let classify path before after =
            inventory path before after "oldName" "newName"
            |> List.exactlyOne
            |> _.Classification

        Assert.Equal(Comment, classify "src/A.fs" "// oldName" "// newName")
        Assert.Equal(CharacterLiteral, classify "src/A.fs" "let c = 'oldName'" "let c = 'newName'")
        Assert.Equal(Documentation, classify "docs/a.md" "oldName" "newName")
        Assert.Equal(GeneratedArtifact, classify "src/A.generated.fs" "\"oldName\"" "\"newName\"")
        Assert.Equal(TestText, classify "tests/A.Tests.fs" "let text = \"oldName\"" "let text = \"newName\"")
        Assert.Empty(inventory "src/A.fs" "let oldName = 1" "let newName = 1" "oldName" "newName")

    [<Fact>]
    let ``unrelated insertions and deletions cannot shift quoted renames out of the inventory`` () =
        let before = "let removed = 0\nlet first = \"oldName\"\nlet second = \"oldName\""

        let after =
            "let inserted = 1\nlet first = \"newName\"\nlet second = \"newName\"\nlet tail = 2"

        let occurrences = inventory "src/A.fs" before after "oldName" "newName"
        Assert.Equal(2, occurrences.Length)
        Assert.Equal<int list>([ 2; 3 ], occurrences |> List.map _.Line)

        Assert.Equal<string list>(
            [ "let first = \"newName\""; "let second = \"newName\"" ],
            occurrences |> List.map _.After
        )

    /// The escalated finding: the no-receipt path measured the OCCURRENCE threshold with the changed-FILE
    /// count.  One file, six quoted occurrences, default threshold 5 — the bypass is that `1 >= 5` is
    /// false while `6 >= 5` is true, so the receipt stayed optional for exactly the shape it exists for.
    [<Fact>]
    let ``one file with six quoted occurrences is classified from occurrences not changed-file count`` () =
        let before =
            [ "let oldName = 1"
              "let a = \"oldName\""
              "let b = \"oldName\""
              "let c = \"oldName\""
              "let d = \"oldName\""
              "let e = \"oldName\""
              "let f = \"oldName\"" ]
            |> String.concat "\n"

        let after = before.Replace("oldName", "newName")
        let files = [ "src/Rogue3/Protocol.fs", before, after ]

        // The tokens are recovered from the diff itself; nothing supplied them.
        Assert.Equal<(string * string) list>([ "oldName", "newName" ], discoverRenames files)

        let occurrences = discoveredOccurrences files
        Assert.Equal(6, occurrences.Length)

        // The bypass, stated as an assertion: the file count would have disproved the threshold.
        Assert.False(activationRequired 5 (List.length files) "ordinary commit" None)
        Assert.True(activationRequired 5 occurrences.Length "ordinary commit" None)

    [<Fact>]
    let ``discovery survives shifted lines and admits only the rename shape`` () =
        // A leading insertion shifts every line; discovery pairs by content, not by line number.
        let shifted =
            discoverRenames [ "src/A.fs", "let p = \"oldName\"", "// added\nlet p = \"newName\"" ]

        Assert.Equal<(string * string) list>([ "oldName", "newName" ], shifted)

        // A line that changed in two unrelated ways is not a rename and is never guessed at.
        Assert.Empty(discoverRenames [ "src/A.fs", "let p = \"oldName\" + one", "let p = \"newName\" + two" ])

        // Neither is a pure insertion, a pure deletion, or an unchanged file.
        Assert.Empty(discoverRenames [ "src/A.fs", "let p = 1", "let p = 1\nlet q = 2" ])
        Assert.Empty(discoverRenames [ "src/A.fs", "let p = 1\nlet q = 2", "let p = 1" ])
        Assert.Empty(discoverRenames [ "src/A.fs", "let p = 1", "let p = 1" ])

    [<Fact>]
    let ``discovered occurrences cover comments escaped and interpolated strings and skip identifier-only renames`` () =
        let before =
            "let oldName = 1\n// oldName\nlet m = \"oldName\"\nlet e = \"\\\"oldName\\\"\"\nlet i = $\"{oldName}\""

        let after = before.Replace("oldName", "newName")
        let occurrences = discoveredOccurrences [ "src/A.fs", before, after ]

        // `let oldName = 1` is an identifier-only rename with no quoted text: correctly not an occurrence.
        Assert.Equal<string list>(
            [ "// newName"; "let m = \"newName\""; "let e = \"\\\"newName\\\"\""; "let i = $\"{newName}\"" ],
            occurrences |> List.map _.After
        )

        // A generated artifact is still inventoried — it is dispositioned, not ignored.
        Assert.Equal(
            GeneratedArtifact,
            discoveredOccurrences [ "src/A.generated.fs", "\"oldName\"", "\"newName\"" ]
            |> List.exactlyOne
            |> _.Classification
        )

    [<Fact>]
    let ``discovery is deterministic and spans several files and renames`` () =
        let files =
            [ "src/A.fs", "let a = \"alpha\"", "let a = \"beta\""
              "src/B.fs", "let b = \"gamma\"", "let b = \"delta\"" ]

        let pairs = discoverRenames files
        Assert.Equal<(string * string) list>([ "alpha", "beta"; "gamma", "delta" ], pairs)
        Assert.Equal<(string * string) list>(pairs, discoverRenames (List.rev files))
        // Each rename is inventoried against every file, and identical rows are never double-counted.
        Assert.Equal(2, (discoveredOccurrences files).Length)

    /// Discovery must stay tractable at the diff shape it exists for. Pairing is bucketed by skeleton;
    /// without that it is quadratic in the size of the diff, and a real bulk rename — thousands of
    /// changed lines on both sides — would not finish. This asserts the answer at that scale, so a
    /// change that drops the bucketing shows up as a suite that stops completing rather than silently.
    [<Fact>]
    let ``discovery stays tractable and exact at bulk-rename scale`` () =
        let rows = 2000

        let before =
            [ for index in 1..rows -> $"let field%d{index} = \"oldName\" // row %d{index}" ]
            |> String.concat "\n"

        let files = [ "src/Protocol.fs", before, before.Replace("oldName", "newName") ]
        Assert.Equal<(string * string) list>([ "oldName", "newName" ], discoverRenames files)
        Assert.Equal(rows, (discoveredOccurrences files).Length)

    [<Fact>]
    let ``bulk rename activation is derived from typed item or commit facts and threshold`` () =
        Assert.True(activationRequired 5 5 "ordinary commit" None)
        Assert.True(activationRequired 5 0 "change names\nBulk rename: true" None)
        Assert.True(activationRequired 5 0 "ordinary commit" (Some "Paths: src/\nBulk rename: true"))
        Assert.False(activationRequired 5 0 "ordinary commit" (Some "prose says Bulk rename: true eventually"))
        Assert.False(activationRequired 5 0 "ordinary commit" None)

    [<Fact>]
    let ``receipt rejects stale duplicate and unresolved evidence and accepts accountable dispositions`` () =
        let occurrence =
            inventory "src/Rogue3/Protocol.fs" "let x = \"oldName\"" "let x = \"newName\"" "oldName" "newName"
            |> List.head

        let complete =
            { occurrence with
                Disposition = IntendedContractChange }

        Assert.Empty(validate "base" "head" (receipt "base" "head" [ complete ]))
        Assert.NotEmpty(validate "other" "head" (receipt "base" "head" [ complete ]))
        Assert.NotEmpty(validate "base" "other" (receipt "base" "head" [ complete ]))
        Assert.NotEmpty(validate "base" "head" (receipt "base" "head" [ complete; complete ]))

    [<Fact>]
    let ``versioned occurrence receipt round trips and malformed dispositions fail closed`` () =
        let occurrence =
            inventory "Fixture.fs" "let x = \"oldName\"" "let x = \"newName\"" "oldName" "newName"
            |> List.exactlyOne

        let complete =
            { occurrence with
                Disposition = IntendedContractChange }

        let source = receipt "base" "head" [ complete ]
        let encoded = source |> toBase64

        match ofBase64 encoded with
        | Ok parsed ->
            Assert.Empty(validate "base" "head" parsed)
            Assert.Equal(complete.Id, parsed.Occurrences.Head.Id)
        | Error errors -> failwithf "%A" errors

        let malformed =
            toJson source
            |> fun json -> json.Replace("intended-contract-change", "self-approved")

        Assert.True(Result.isError (ofJson malformed))
