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
