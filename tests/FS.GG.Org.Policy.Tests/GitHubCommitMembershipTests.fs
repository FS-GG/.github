namespace FS.GG.Org.Policy.Tests

open System
open System.Text
open FS.GG.Org.Policy
open Xunit

module GitHubCommitMembershipTests =
    let private rootTreeId = "2b552d6bf9d7b4458a28fc65663fbc2c7b0221dc"
    let private commitId = "539aff7e655d22b1761850cd6be868eecc2886e4"
    let private pin: GitCommitProvenance.ExactCommitPin =
        { RepositoryNodeId = "R_fixture_one"; RepositoryFullName = "FS-GG/.github"; CommitId = commitId }
    let private valid =
        """{"data":{"repository":{"id":"R_fixture_one","nameWithOwner":"FS-GG/.github","object":{"__typename":"Commit","oid":"539aff7e655d22b1761850cd6be868eecc2886e4","tree":{"oid":"2b552d6bf9d7b4458a28fc65663fbc2c7b0221dc"}}}}}"""
    let private reader (json: string) =
        { new GitHubCommitMembership.IReadOnlyGraphQlReader with
            member _.ExecuteExact request =
                Assert.Equal("FS-GG", request.Owner)
                Assert.Equal(".github", request.Name)
                Assert.Equal(commitId, request.CommitId)
                Assert.Contains("followRenames: false", request.Document)
                Assert.Contains("object(oid: $commit)", request.Document)
                Ok(Encoding.UTF8.GetBytes(json)) }
    let private refused message json =
        match GitHubCommitMembership.inspectProvisionalMembership pin rootTreeId (reader json) with
        | Error diagnostic ->
            Assert.Equal("github-commit-membership", diagnostic.Code)
            Assert.Contains(message, diagnostic.Message)
        | Ok fact -> failwithf "unproven repository membership accepted: %A" fact

    [<Fact>]
    let ``exact GraphQL repository commit and tree facts bind provisionally`` () =
        match GitHubCommitMembership.inspectProvisionalMembership pin rootTreeId (reader valid) with
        | Error diagnostic -> failwithf "valid membership refused: %A" diagnostic
        | Ok fact ->
            Assert.Equal(pin.RepositoryNodeId, fact.RepositoryNodeId)
            Assert.Equal(commitId, fact.CommitId)
            Assert.Equal(rootTreeId, fact.TreeId)

    [<Fact>]
    let ``GraphQL errors refuse even when data appears complete`` () =
        refused "errors" (valid.Replace("{\"data\"", "{\"errors\":[{\"message\":\"partial\"}],\"data\""))

    [<Fact>]
    let ``foreign repository identity and alias refuse`` () =
        refused "repository identity" (valid.Replace("R_fixture_one", "R_fixture_other"))
        refused "repository identity" (valid.Replace("FS-GG/.github", "fs-gg/.github"))

    [<Fact>]
    let ``wrong object type commit and tree refuse`` () =
        refused "Commit" (valid.Replace("\"__typename\":\"Commit\"", "\"__typename\":\"Tag\""))
        refused "commit ID" (valid.Replace(commitId, String.replicate 40 "a"))
        refused "tree ID" (valid.Replace(rootTreeId, String.replicate 40 "b"))

    [<Fact>]
    let ``terminal newline IDs cannot establish provisional membership`` () =
        let malformedPin = { pin with CommitId = commitId + "\n" }
        let malformedTreeId = rootTreeId + "\n"
        let malformedJson =
            valid.Replace(commitId, commitId + "\\n")
                 .Replace(rootTreeId, rootTreeId + "\\n")
        let malformedReader =
            { new GitHubCommitMembership.IReadOnlyGraphQlReader with
                member _.ExecuteExact _ = Ok(Encoding.UTF8.GetBytes(malformedJson)) }
        match GitHubCommitMembership.inspectProvisionalMembership malformedPin malformedTreeId malformedReader with
        | Error diagnostic ->
            Assert.Equal("github-commit-membership", diagnostic.Code)
            Assert.Contains("exact", diagnostic.Message)
        | Ok fact -> failwithf "noncanonical object IDs established membership: %A" fact

    [<Fact>]
    let ``repository name with terminal newline cannot establish membership`` () =
        let malformedPin = { pin with RepositoryFullName = pin.RepositoryFullName + "\n" }
        let malformedJson = valid.Replace("FS-GG/.github", "FS-GG/.github\\n")
        let mutable calls = 0
        let unsafeReader =
            { new GitHubCommitMembership.IReadOnlyGraphQlReader with
                member _.ExecuteExact _ =
                    calls <- calls + 1
                    Ok(Encoding.UTF8.GetBytes(malformedJson)) }
        match GitHubCommitMembership.inspectProvisionalMembership malformedPin rootTreeId unsafeReader with
        | Error diagnostic -> Assert.Equal("github-commit-membership", diagnostic.Code)
        | Ok fact -> failwithf "noncanonical repository established membership: %A" fact
        Assert.Equal(0, calls)

    [<Fact>]
    let ``terminal newline request segment cannot reach GraphQL transport`` () =
        let mutable captured = None
        let capture =
            { new GitHubCommitMembership.IReadOnlyGraphQlReader with
                member _.ExecuteExact request =
                    captured <- Some request
                    Ok(Encoding.UTF8.GetBytes(valid)) }
        match GitHubCommitMembership.inspectProvisionalMembership pin rootTreeId capture with
        | Error diagnostic -> failwithf "valid capture refused: %A" diagnostic
        | Ok _ -> ()
        let exact = captured |> Option.get
        Assert.False(GitHubCommitMembership.isExactReadRequest { exact with Name = exact.Name + "\n" })

    [<Fact>]
    let ``repository node ID with whitespace cannot establish membership`` () =
        let malformedPin = { pin with RepositoryNodeId = pin.RepositoryNodeId + "\n" }
        let malformedJson = valid.Replace("R_fixture_one", "R_fixture_one\\n")
        let mutable calls = 0
        let unsafeReader =
            { new GitHubCommitMembership.IReadOnlyGraphQlReader with
                member _.ExecuteExact _ =
                    calls <- calls + 1
                    Ok(Encoding.UTF8.GetBytes(malformedJson)) }
        match GitHubCommitMembership.inspectProvisionalMembership malformedPin rootTreeId unsafeReader with
        | Error diagnostic -> Assert.Equal("github-commit-membership", diagnostic.Code)
        | Ok fact -> failwithf "malformed node ID established membership: %A" fact
        Assert.Equal(0, calls)

    [<Fact>]
    let ``dot segment repository cannot establish membership`` () =
        let malformedPin = { pin with RepositoryFullName = "FS-GG/.." }
        let malformedJson = valid.Replace("FS-GG/.github", malformedPin.RepositoryFullName)
        let mutable calls = 0
        let unsafeReader =
            { new GitHubCommitMembership.IReadOnlyGraphQlReader with
                member _.ExecuteExact _ =
                    calls <- calls + 1
                    Ok(Encoding.UTF8.GetBytes(malformedJson)) }
        match GitHubCommitMembership.inspectProvisionalMembership malformedPin rootTreeId unsafeReader with
        | Error diagnostic -> Assert.Equal("github-commit-membership", diagnostic.Code)
        | Ok fact -> failwithf "dot segment repository established membership: %A" fact
        Assert.Equal(0, calls)

    [<Fact>]
    let ``dot segment request cannot reach GraphQL transport`` () =
        let mutable captured = None
        let capture =
            { new GitHubCommitMembership.IReadOnlyGraphQlReader with
                member _.ExecuteExact request =
                    captured <- Some request
                    Ok(Encoding.UTF8.GetBytes(valid)) }
        match GitHubCommitMembership.inspectProvisionalMembership pin rootTreeId capture with
        | Error diagnostic -> failwithf "valid request capture refused: %A" diagnostic
        | Ok _ -> ()
        let exact = captured |> Option.get
        Assert.False(GitHubCommitMembership.isExactReadRequest { exact with Name = ".." })

    [<Fact>]
    let ``null and missing object prevent a membership verdict`` () =
        refused "object" (valid.Replace("\"object\":{\"__typename\"", "\"wrongField\":{\"__typename\""))
        refused "object" (valid.Replace("\"object\":{\"__typename\":\"Commit\",\"oid\":\"" + commitId + "\",\"tree\":{\"oid\":\"" + rootTreeId + "\"}}", "\"object\":null"))
        refused "repository" """{"data":{"repository":null}}"""

    [<Fact>]
    let ``malformed and oversized responses refuse`` () =
        refused "malformed" "{"
        refused "bounded size" (valid + String.replicate 65536 " ")

    [<Fact>]
    let ``duplicate JSON keys cannot hide foreign facts`` () =
        refused "duplicate" (valid.Replace("\"id\":\"R_fixture_one\"", "\"id\":\"R_fixture_other\",\"id\":\"R_fixture_one\""))
        refused "duplicate" (valid.Replace("\"oid\":\"" + rootTreeId + "\"", "\"oid\":\"" + String.replicate 40 "b" + "\",\"oid\":\"" + rootTreeId + "\""))

    [<Fact>]
    let ``missing reader and unavailable response refuse`` () =
        let absent = Unchecked.defaultof<GitHubCommitMembership.IReadOnlyGraphQlReader>
        match GitHubCommitMembership.inspectProvisionalMembership pin rootTreeId absent with
        | Error diagnostic -> Assert.Equal("github-commit-membership", diagnostic.Code)
        | Ok fact -> failwithf "absent reader accepted: %A" fact
        let unavailable =
            { new GitHubCommitMembership.IReadOnlyGraphQlReader with
                member _.ExecuteExact _ = Error () }
        match GitHubCommitMembership.inspectProvisionalMembership pin rootTreeId unavailable with
        | Error diagnostic -> Assert.Equal("github-commit-membership", diagnostic.Code)
        | Ok fact -> failwithf "unavailable read accepted: %A" fact

    [<Fact>]
    let ``valid-looking borrowed commit reader cannot override foreign GitHub membership`` () =
        let commitBytes = Convert.FromBase64String("dHJlZSAyYjU1MmQ2YmY5ZDdiNDQ1OGEyOGZjNjU2NjNmYmMyYzdiMDIyMWRjCmF1dGhvciBGaXh0dXJlIDxmaXh0dXJlQGV4YW1wbGUuaW52YWxpZD4gMCArMDAwMApjb21taXR0ZXIgRml4dHVyZSA8Zml4dHVyZUBleGFtcGxlLmludmFsaWQ+IDAgKzAwMDAKCmZpeGVkIEEgdG8gQiBmaXh0dXJlCg==")
        let observation: GitCommitProvenance.CommitObservation =
            { RepositoryNodeId = pin.RepositoryNodeId
              RepositoryFullName = pin.RepositoryFullName
              CommitId = commitId
              RawCommit = commitBytes }
        let borrowed =
            { new GitCommitProvenance.IReadOnlyCommitReader with
                member _.ReadExact _ = Ok observation }
        // The prior pure commit reader accepts its own repository claim.
        match GitCommitProvenance.inspectProvisionalRoot pin rootTreeId borrowed with
        | Error diagnostic -> failwithf "borrowed-reader counterexample changed: %A" diagnostic
        | Ok _ -> ()
        let foreignResponse = valid.Replace("R_fixture_one", "R_fixture_other")
        let trees =
            [ rootTreeId, Convert.FromBase64String("MTAwNjQ0IFJFQURNRS5tZAC2/ExiC2fZX5U6XBwSMKqrXa2hsDQwMDAwIHNyYwA52/KHUpV/+mkQOSnNWFWL4KxgIg==")
              "39dbf28752957ffa69103929cd58558be0ac6022", Convert.FromBase64String("NDAwMDAgQQCc41uNK5clEiNIWKIvkXjch4zKhDQwMDAwIEIAiKntiowmvIzzDfFCvu2LgF9XXew=")
              "9ce35b8d2b972512234858a22f9178dc878cca84", Convert.FromBase64String("MTAwNjQ0IEEuZnNwcm9qAKXQx8j6erOtWXN7LmERQxD3K5F7")
              "88a9ed8a8c26bc8cf30df142beed8b805f575dec", Convert.FromBase64String("MTAwNjQ0IEIuZnNwcm9qAEIwkWFkdAcpD62CTJ/80p7E/pYO") ]
        let projects =
            [ "src/A/A.fsproj", Encoding.UTF8.GetBytes("<Project><ProjectReference Include='../B/B.fsproj' /></Project>")
              "src/B/B.fsproj", Encoding.UTF8.GetBytes("<Project />") ]
        match ProjectReferenceXml.inspectSuppliedGitHubMembershipSnapshot pin (reader foreignResponse) borrowed rootTreeId trees projects with
        | Error diagnostic -> Assert.Equal("github-commit-membership", diagnostic.Code)
        | Ok graph -> failwithf "foreign membership produced graph: %A" graph
        match ProjectReferenceXml.inspectSuppliedGitHubMembershipSnapshot pin (reader valid) borrowed rootTreeId trees projects with
        | Error diagnostic -> failwithf "matching membership and source refused: %A" diagnostic
        | Ok graph -> Assert.Equal<string list>([ "src/B/B.fsproj" ], graph.["src/A/A.fsproj"])
