namespace FS.GG.Org.Policy.Tests

open System
open System.Text
open FS.GG.Org.Policy
open Xunit

module GitHubProtectedBranchPinTests =
    let private repo: GitHubProtectedBranchPin.ExactRepository =
        { RepositoryNodeId = "R_fixture_one"; RepositoryFullName = "FS-GG/.github" }
    let private rootTreeId = "2b552d6bf9d7b4458a28fc65663fbc2c7b0221dc"
    let private commitId = "539aff7e655d22b1761850cd6be868eecc2886e4"
    let private branchJson =
        """{"name":"main","commit":{"sha":"539aff7e655d22b1761850cd6be868eecc2886e4"},"protected":true}"""
    let private endpoint = "https://api.github.com/repos/FS-GG/.github/branches/main"
    let private response (json: string): GitHubProtectedBranchPin.BranchResponse =
        { StatusCode = 200; ResponseUrl = endpoint; MediaType = "application/json"
          Body = Encoding.UTF8.GetBytes(json) }
    let private reader reply =
        { new GitHubProtectedBranchPin.IReadOnlyProtectedBranchReader with
            member _.ReadExact request =
                Assert.Equal(endpoint, request.Url)
                Assert.Equal("FS-GG", request.Owner)
                Assert.Equal(".github", request.Name)
                Assert.Equal("main", request.Branch)
                reply }
    let private refused message reply =
        match GitHubProtectedBranchPin.inspectProvisionalPin repo (reader reply) with
        | Error diagnostic ->
            Assert.Equal("github-protected-pin", diagnostic.Code)
            Assert.Contains(message, diagnostic.Message)
        | Ok pin -> failwithf "unaccepted branch response produced pin: %A" pin

    [<Fact>]
    let ``exact protected main response derives commit pin`` () =
        match GitHubProtectedBranchPin.inspectProvisionalPin repo (reader (Ok(response branchJson))) with
        | Error diagnostic -> failwithf "protected main refused: %A" diagnostic
        | Ok pin ->
            Assert.Equal(repo.RepositoryNodeId, pin.RepositoryNodeId)
            Assert.Equal(repo.RepositoryFullName, pin.RepositoryFullName)
            Assert.Equal(commitId, pin.CommitId)

    [<Fact>]
    let ``unprotected missing and null protection cannot mint pin`` () =
        refused "protected" (Ok(response (branchJson.Replace("true", "false"))))
        refused "protected" (Ok(response (branchJson.Replace(",\"protected\":true", ""))))
        refused "protected" (Ok(response (branchJson.Replace("true", "null"))))

    [<Fact>]
    let ``wrong branch name and stale commit refuse`` () =
        refused "main" (Ok(response (branchJson.Replace("\"name\":\"main\"", "\"name\":\"other\""))))
        refused "SHA-1" (Ok(response (branchJson.Replace(commitId, "refs/heads/main"))))

    [<Fact>]
    let ``commit ID with terminal newline cannot mint protected pin`` () =
        refused "SHA-1" (Ok(response (branchJson.Replace(commitId, commitId + "\\n"))))

    [<Fact>]
    let ``repository name with terminal newline cannot mint protected pin`` () =
        let malformedRepo = { repo with RepositoryFullName = repo.RepositoryFullName + "\n" }
        let mutable calls = 0
        let unsafeReader =
            { new GitHubProtectedBranchPin.IReadOnlyProtectedBranchReader with
                member _.ReadExact request =
                    calls <- calls + 1
                    Ok { response branchJson with ResponseUrl = request.Url } }
        match GitHubProtectedBranchPin.inspectProvisionalPin malformedRepo unsafeReader with
        | Error diagnostic -> Assert.Equal("github-protected-pin", diagnostic.Code)
        | Ok pin -> failwithf "noncanonical repository minted protected pin: %A" pin
        Assert.Equal(0, calls)

    [<Fact>]
    let ``terminal newline request segment cannot reach protected transport`` () =
        let request: GitHubProtectedBranchPin.ExactRequest =
            { Owner = "FS-GG"; Name = ".github"; Branch = "main"; Url = endpoint }
        let malformedName = ".github\n"
        let malformed =
            { request with Name = malformedName
                           Url = "https://api.github.com/repos/FS-GG/" + malformedName + "/branches/main" }
        Assert.False(GitHubProtectedBranchPin.isExactReadRequest malformed)

    [<Fact>]
    let ``repository node ID with whitespace cannot mint protected pin`` () =
        let malformedRepo = { repo with RepositoryNodeId = repo.RepositoryNodeId + "\n" }
        let mutable calls = 0
        let unsafeReader =
            { new GitHubProtectedBranchPin.IReadOnlyProtectedBranchReader with
                member _.ReadExact _ =
                    calls <- calls + 1
                    Ok(response branchJson) }
        match GitHubProtectedBranchPin.inspectProvisionalPin malformedRepo unsafeReader with
        | Error diagnostic -> Assert.Equal("github-protected-pin", diagnostic.Code)
        | Ok pin -> failwithf "malformed node ID minted protected pin: %A" pin
        Assert.Equal(0, calls)

    [<Fact>]
    let ``duplicate protected or commit fields cannot mask rejected facts`` () =
        refused "duplicate" (Ok(response (branchJson.Replace("\"protected\":true", "\"protected\":false,\"protected\":true"))))
        refused "duplicate" (Ok(response (branchJson.Replace("\"sha\":\"" + commitId + "\"", "\"sha\":\"" + String.replicate 40 "a" + "\",\"sha\":\"" + commitId + "\""))))

    [<Fact>]
    let ``HTTP status origin media and unavailable reader refuse`` () =
        refused "status" (Ok { response branchJson with StatusCode = 302 })
        refused "origin" (Ok { response branchJson with ResponseUrl = "https://foreign.example/branches/main" })
        refused "media" (Ok { response branchJson with MediaType = "text/html" })
        refused "unavailable" (Error ())
        match GitHubProtectedBranchPin.inspectProvisionalPin repo Unchecked.defaultof<_> with
        | Error diagnostic -> Assert.Equal("github-protected-pin", diagnostic.Code)
        | Ok pin -> failwithf "absent reader produced pin: %A" pin

    [<Fact>]
    let ``malformed and oversized branch responses refuse`` () =
        refused "malformed" (Ok(response "{"))
        refused "bounded" (Ok(response (branchJson + String.replicate 65536 " ")))

    [<Fact>]
    let ``unprotected branch cannot borrow a formerly supplied accepted-looking pin`` () =
        let pin: GitCommitProvenance.ExactCommitPin =
            { RepositoryNodeId = repo.RepositoryNodeId
              RepositoryFullName = repo.RepositoryFullName
              CommitId = commitId }
        let membershipJson =
            """{"data":{"repository":{"id":"R_fixture_one","nameWithOwner":"FS-GG/.github","object":{"__typename":"Commit","oid":"539aff7e655d22b1761850cd6be868eecc2886e4","tree":{"oid":"2b552d6bf9d7b4458a28fc65663fbc2c7b0221dc"}}}}}"""
        let membershipReader =
            { new GitHubCommitMembership.IReadOnlyGraphQlReader with
                member _.ExecuteExact _ = Ok(Encoding.UTF8.GetBytes(membershipJson)) }
        let commitObservation: GitCommitProvenance.CommitObservation =
            { RepositoryNodeId = repo.RepositoryNodeId
              RepositoryFullName = repo.RepositoryFullName
              CommitId = commitId
              RawCommit = Convert.FromBase64String("dHJlZSAyYjU1MmQ2YmY5ZDdiNDQ1OGEyOGZjNjU2NjNmYmMyYzdiMDIyMWRjCmF1dGhvciBGaXh0dXJlIDxmaXh0dXJlQGV4YW1wbGUuaW52YWxpZD4gMCArMDAwMApjb21taXR0ZXIgRml4dHVyZSA8Zml4dHVyZUBleGFtcGxlLmludmFsaWQ+IDAgKzAwMDAKCmZpeGVkIEEgdG8gQiBmaXh0dXJlCg==") }
        let commitReader =
            { new GitCommitProvenance.IReadOnlyCommitReader with
                member _.ReadExact _ = Ok commitObservation }
        let trees =
            [ rootTreeId, Convert.FromBase64String("MTAwNjQ0IFJFQURNRS5tZAC2/ExiC2fZX5U6XBwSMKqrXa2hsDQwMDAwIHNyYwA52/KHUpV/+mkQOSnNWFWL4KxgIg==")
              "39dbf28752957ffa69103929cd58558be0ac6022", Convert.FromBase64String("NDAwMDAgQQCc41uNK5clEiNIWKIvkXjch4zKhDQwMDAwIEIAiKntiowmvIzzDfFCvu2LgF9XXew=")
              "9ce35b8d2b972512234858a22f9178dc878cca84", Convert.FromBase64String("MTAwNjQ0IEEuZnNwcm9qAKXQx8j6erOtWXN7LmERQxD3K5F7")
              "88a9ed8a8c26bc8cf30df142beed8b805f575dec", Convert.FromBase64String("MTAwNjQ0IEIuZnNwcm9qAEIwkWFkdAcpD62CTJ/80p7E/pYO") ]
        let projects =
            [ "src/A/A.fsproj", Encoding.UTF8.GetBytes("<Project><ProjectReference Include='../B/B.fsproj' /></Project>")
              "src/B/B.fsproj", Encoding.UTF8.GetBytes("<Project />") ]
        match ProjectReferenceXml.inspectSuppliedGitHubMembershipSnapshot pin membershipReader commitReader rootTreeId trees projects with
        | Error diagnostic -> failwithf "supplied-pin counterexample changed: %A" diagnostic
        | Ok graph -> Assert.Equal<string list>([ "src/B/B.fsproj" ], graph.["src/A/A.fsproj"])
        let unprotected = response (branchJson.Replace("true", "false"))
        match ProjectReferenceXml.inspectSuppliedProtectedBranchSnapshot repo (reader (Ok unprotected)) membershipReader commitReader rootTreeId trees projects with
        | Error diagnostic -> Assert.Equal("github-protected-pin", diagnostic.Code)
        | Ok graph -> failwithf "unprotected branch produced graph: %A" graph
        match ProjectReferenceXml.inspectSuppliedProtectedBranchSnapshot repo (reader (Ok(response branchJson))) membershipReader commitReader rootTreeId trees projects with
        | Error diagnostic -> failwithf "matching protected tip refused: %A" diagnostic
        | Ok graph -> Assert.Equal<string list>([ "src/B/B.fsproj" ], graph.["src/A/A.fsproj"])
        let mutable tipReads = 0
        let movingTipReader =
            { new GitHubProtectedBranchPin.IReadOnlyProtectedBranchReader with
                member _.ReadExact request =
                    Assert.Equal(endpoint, request.Url)
                    tipReads <- tipReads + 1
                    let observed =
                        if tipReads = 1 then branchJson
                        else branchJson.Replace(commitId, String.replicate 40 "a")
                    Ok(response observed) }
        match ProjectReferenceXml.inspectSuppliedProtectedBranchSnapshot repo movingTipReader membershipReader commitReader rootTreeId trees projects with
        | Error diagnostic ->
            Assert.Equal("github-protected-pin", diagnostic.Code)
            Assert.Contains("changed", diagnostic.Message)
        | Ok graph -> failwithf "changed protected tip produced supplied graph: %A" graph
        Assert.Equal(2, tipReads)
        let mutable unavailableReads = 0
        let unavailableFinalReader =
            { new GitHubProtectedBranchPin.IReadOnlyProtectedBranchReader with
                member _.ReadExact request =
                    Assert.Equal(endpoint, request.Url)
                    unavailableReads <- unavailableReads + 1
                    if unavailableReads = 1 then Ok(response branchJson) else Error () }
        match ProjectReferenceXml.inspectSuppliedProtectedBranchSnapshot repo unavailableFinalReader membershipReader commitReader rootTreeId trees projects with
        | Error diagnostic -> Assert.Equal("github-protected-pin", diagnostic.Code)
        | Ok graph -> failwithf "unavailable final tip produced supplied graph: %A" graph
        Assert.Equal(2, unavailableReads)
        let staleTip = response (branchJson.Replace(commitId, String.replicate 40 "a"))
        match ProjectReferenceXml.inspectSuppliedProtectedBranchSnapshot repo (reader (Ok staleTip)) membershipReader commitReader rootTreeId trees projects with
        | Error diagnostic -> Assert.Equal("github-commit-membership", diagnostic.Code)
        | Ok graph -> failwithf "stale protected tip produced graph: %A" graph
