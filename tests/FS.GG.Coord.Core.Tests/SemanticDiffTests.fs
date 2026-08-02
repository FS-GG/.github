namespace FS.GG.Coord.Tests

open Xunit
open FS.GG.Coord.SemanticDiff

module SemanticDiffTests =
    let receipt baseSha headSha occurrences =
        receipt "FS-GG/rogue3" baseSha headSha [ "src/Rogue3/Protocol.fs" ] true occurrences

    [<Fact>]
    let ``quoted F# rename is inventoried even though the identifier rename compiles`` () =
        let occurrences =
            inventory "src/Rogue3/Protocol.fs"
                "let oldName = 1\nlet message = \"oldName\"\nlet escaped = \"\\\"oldName\\\"\"\nlet interpolation = $\"{oldName}\""
                "let newName = 1\nlet message = \"newName\"\nlet escaped = \"\\\"newName\\\"\"\nlet interpolation = $\"{newName}\""
                "oldName" "newName"
        Assert.Equal(3, List.length occurrences)
        Assert.All(occurrences, fun occurrence -> Assert.Equal(Unresolved, occurrence.Disposition))
        Assert.True(validate "base" "head" (receipt "base" "head" occurrences) |> List.exists (fun error -> error.Contains "unresolved"))

    [<Fact>]
    let ``comments character literals docs generated tests and identifier-only edits classify deterministically`` () =
        let classify path before after = inventory path before after "oldName" "newName" |> List.exactlyOne |> _.Classification
        Assert.Equal(Comment, classify "src/A.fs" "// oldName" "// newName")
        Assert.Equal(CharacterLiteral, classify "src/A.fs" "let c = 'oldName'" "let c = 'newName'")
        Assert.Equal(Documentation, classify "docs/a.md" "oldName" "newName")
        Assert.Equal(GeneratedArtifact, classify "src/A.generated.fs" "\"oldName\"" "\"newName\"")
        Assert.Equal(TestText, classify "tests/A.Tests.fs" "let text = \"oldName\"" "let text = \"newName\"")
        Assert.Empty(inventory "src/A.fs" "let oldName = 1" "let newName = 1" "oldName" "newName")

    [<Fact>]
    let ``receipt rejects stale duplicate and unresolved evidence and accepts accountable dispositions`` () =
        let occurrence = inventory "src/Rogue3/Protocol.fs" "let x = \"oldName\"" "let x = \"newName\"" "oldName" "newName" |> List.head
        let complete = { occurrence with Disposition = IntendedContractChange }
        Assert.Empty(validate "base" "head" (receipt "base" "head" [ complete ]))
        Assert.NotEmpty(validate "other" "head" (receipt "base" "head" [ complete ]))
        Assert.NotEmpty(validate "base" "other" (receipt "base" "head" [ complete ]))
        Assert.NotEmpty(validate "base" "head" (receipt "base" "head" [ complete; complete ]))
