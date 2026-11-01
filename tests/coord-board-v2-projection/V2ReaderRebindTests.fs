namespace FS.GG.Coord.Tests

/// FS-GG/.github#5120: the splice behind `rebind-reader`. Only the named identity values change; every other byte is
/// carried over, and anything the splice cannot place exactly refuses.
module V2ReaderRebindTests =
    open System
    open Xunit
    open FS.GG.Coord.GitHub
    open FS.GG.Coord.GitHub.V2Enrollment

    let private oldArtifact = String.replicate 64 "4"
    let private oldRecipe = String.replicate 40 "7"
    let private newArtifact = String.replicate 64 "f"
    let private newRecipe = String.replicate 40 "3"

    // Compact, like a runtime binding: an import identity and a nested payload binding beside the reader identity.
    let private binding =
        "{\"bindingVersion\":5,\"importRecipeRevision\":\"" + String.replicate 40 "a" + "\",\"importArtifactSha256\":\"" + String.replicate 64 "b"
        + "\",\"recipeRevision\":\"" + oldRecipe + "\",\"populationRevision\":\"" + String.replicate 40 "e" + "\",\"artifactSha256\":\"" + oldArtifact
        + "\",\"programmeEnrollment\":{\"binding\":{\"recipeRevision\":\"" + String.replicate 40 "a" + "\",\"artifactSha256\":\"" + String.replicate 64 "b" + "\"}}}"

    let private fields = [ "artifactSha256", oldArtifact, newArtifact; "recipeRevision", oldRecipe, newRecipe ]

    [<Fact>]
    let ``the reader identity values change and every other byte is carried over`` () =
        let rebound = V2EnrollmentCommands.rebindFields binding fields
        Assert.Equal(Ok(binding.Replace(oldArtifact, newArtifact).Replace(oldRecipe, newRecipe)), rebound)
        // The import identity and the nested payload binding are untouched.
        match rebound with
        | Ok text ->
            Assert.Contains("\"importArtifactSha256\":\"" + String.replicate 64 "b", text)
            Assert.Contains("\"binding\":{\"recipeRevision\":\"" + String.replicate 40 "a", text)
            Assert.Equal(binding.Length, text.Length)
        | Error refusal -> failwithf "%A" refusal

    [<Fact>]
    let ``a pretty-printed migration scope keeps its layout`` () =
        let scope =
            "{\n \"sourceRevision\": \"" + oldRecipe + "\",\n \"artifactSha256\": \"" + oldArtifact + "\",\n \"cliArtifactSha256\": \"" + String.replicate 64 "5"
            + "\",\n \"principalLogin\": \"synthetic\"\n}"
        let cli = String.replicate 64 "6"
        let rebound =
            V2EnrollmentCommands.rebindFields scope [ "sourceRevision", oldRecipe, newRecipe; "artifactSha256", oldArtifact, newArtifact; "cliArtifactSha256", String.replicate 64 "5", cli ]
        Assert.Equal(Ok(scope.Replace(oldRecipe, newRecipe).Replace(oldArtifact, newArtifact).Replace(String.replicate 64 "5", cli)), rebound)

    [<Fact>]
    let ``the splice refuses whatever it cannot place exactly`` () =
        let refused (json: string) (named: (string * string * string) list) = Assert.Equal(Error MalformedInput, V2EnrollmentCommands.rebindFields json named)
        // An old value that is not the current top-level value.
        refused binding [ "artifactSha256", String.replicate 64 "9", newArtifact ]
        // A field the document does not have.
        refused binding [ "cliArtifactSha256", oldArtifact, newArtifact ]
        // A replacement that is not a 40- or 64-hex identity, so no other text can be injected.
        refused binding [ "artifactSha256", oldArtifact, "f\",\"projectNumber\":4,\"x\":\"" ]
        refused binding [ "recipeRevision", oldRecipe, String.replicate 39 "3" ]
        // A second, nested copy of the same `"name": "old"`: the splice could touch the wrong one, so it refuses.
        let nested = binding.Replace("\"binding\":{\"recipeRevision\":\"" + String.replicate 40 "a", "\"binding\":{\"recipeRevision\":\"" + oldRecipe)
        refused nested fields
        // Not a JSON object.
        refused "[1]" fields
        refused "{" fields
        // Unchanged values are a successful identical splice (the command, not the splice, decides "already bound").
        Assert.Equal(Ok binding, V2EnrollmentCommands.rebindFields binding [ "artifactSha256", oldArtifact, oldArtifact ])
