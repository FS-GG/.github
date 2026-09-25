namespace FS.GG.Org.Policy.Tests

open System
open System.Text
open FS.GG.Org.Policy
open Xunit

module GitCommitProvenanceTests =
    // Independently fixed with git hash-object -t commit --stdin. Malformed objects use
    // --literally because ordinary Git refuses those header shapes before hashing.
    let private rootTreeId = "2b552d6bf9d7b4458a28fc65663fbc2c7b0221dc"
    let private commitId = "539aff7e655d22b1761850cd6be868eecc2886e4"
    let private commitBytes =
        Convert.FromBase64String(
            "dHJlZSAyYjU1MmQ2YmY5ZDdiNDQ1OGEyOGZjNjU2NjNmYmMyYzdiMDIyMWRjCmF1dGhvciBGaXh0dXJlIDxmaXh0dXJlQGV4YW1wbGUuaW52YWxpZD4gMCArMDAwMApjb21taXR0ZXIgRml4dHVyZSA8Zml4dHVyZUBleGFtcGxlLmludmFsaWQ+IDAgKzAwMDAKCmZpeGVkIEEgdG8gQiBmaXh0dXJlCg==")
    let private pin: GitCommitProvenance.ExactCommitPin =
        { RepositoryNodeId = "R_fixture_one"; RepositoryFullName = "FS-GG/.github"; CommitId = commitId }
    let private observation: GitCommitProvenance.CommitObservation =
        { RepositoryNodeId = pin.RepositoryNodeId
          RepositoryFullName = pin.RepositoryFullName
          CommitId = commitId
          RawCommit = commitBytes }
    let private reader answer =
        { new GitCommitProvenance.IReadOnlyCommitReader with
            member _.ReadExact requested =
                Assert.Equal(pin, requested)
                answer }
    let private refused message expectedRoot answer =
        match GitCommitProvenance.inspectProvisionalRoot pin expectedRoot (reader answer) with
        | Error diagnostic ->
            Assert.Equal("git-commit-provenance", diagnostic.Code)
            Assert.Contains(message, diagnostic.Message)
        | Ok verified -> failwithf "unproven commit rooted the tree: %A" verified

    [<Fact>]
    let ``exact commit bytes bind the expected root tree provisionally`` () =
        match GitCommitProvenance.inspectProvisionalRoot pin rootTreeId (reader (Ok observation)) with
        | Error diagnostic -> failwithf "valid commit refused: %A" diagnostic
        | Ok verified ->
            Assert.Equal(rootTreeId, verified.TreeId)
            Assert.Equal(commitId, verified.CommitId)
            Assert.Equal(pin.RepositoryNodeId, verified.RepositoryNodeId)

    [<Fact>]
    let ``wrong rooted tree cannot borrow exact commit bytes`` () =
        refused "tree ID" "a2a00b1cb79592e3dc312008e3e9f02c78a8c014" (Ok observation)

    [<Fact>]
    let ``repository alias and foreign repository ID refuse`` () =
        refused "repository identity" rootTreeId (Ok { observation with RepositoryFullName = "fs-gg/.github" })
        refused "repository identity" rootTreeId (Ok { observation with RepositoryNodeId = "R_fixture_other" })

    [<Fact>]
    let ``branch aliases and absent repository pins cannot request provenance`` () =
        let invalidPins =
            [ { pin with CommitId = "refs/heads/main" }
              { pin with RepositoryNodeId = "" }
              { pin with RepositoryFullName = "FS-GG/.github/alias" } ]
        for invalidPin in invalidPins do
            let unexpected =
                { new GitCommitProvenance.IReadOnlyCommitReader with
                    member _.ReadExact _ = failwith "invalid pin reached reader" }
            match GitCommitProvenance.inspectProvisionalRoot invalidPin rootTreeId unexpected with
            | Error diagnostic -> Assert.Equal("git-commit-provenance", diagnostic.Code)
            | Ok verified -> failwithf "invalid pin accepted: %A" verified

    [<Fact>]
    let ``terminal newline IDs cannot dispatch raw commit provider`` () =
        let mutable calls = 0
        let countingReader =
            { new GitCommitProvenance.IReadOnlyCommitReader with
                member _.ReadExact _ =
                    calls <- calls + 1
                    Error () }
        for candidatePin, candidateTree in
            [ { pin with CommitId = commitId + "\n" }, rootTreeId
              pin, rootTreeId + "\n" ] do
            match GitCommitProvenance.inspectProvisionalRoot candidatePin candidateTree countingReader with
            | Error diagnostic -> Assert.Equal("git-commit-provenance", diagnostic.Code)
            | Ok verified -> failwithf "noncanonical ID certified commit: %A" verified
        Assert.Equal(0, calls)

    [<Fact>]
    let ``repository name with terminal newline cannot root commit`` () =
        let malformedPin = { pin with RepositoryFullName = pin.RepositoryFullName + "\n" }
        let malformedObservation =
            { observation with RepositoryFullName = malformedPin.RepositoryFullName }
        let mutable calls = 0
        let unsafeReader =
            { new GitCommitProvenance.IReadOnlyCommitReader with
                member _.ReadExact _ =
                    calls <- calls + 1
                    Ok malformedObservation }
        match GitCommitProvenance.inspectProvisionalRoot malformedPin rootTreeId unsafeReader with
        | Error diagnostic -> Assert.Equal("git-commit-provenance", diagnostic.Code)
        | Ok root -> failwithf "noncanonical repository rooted commit: %A" root
        Assert.Equal(0, calls)

    [<Fact>]
    let ``wrong commit claim and changed commit bytes refuse`` () =
        refused "commit ID" rootTreeId (Ok { observation with CommitId = String.replicate 40 "a" })
        let changed = Array.copy commitBytes
        changed.[changed.Length - 2] <- byte 'X'
        refused "hash" rootTreeId (Ok { observation with RawCommit = changed })

    [<Fact>]
    let ``missing and throwing readers have no root verdict`` () =
        refused "unavailable" rootTreeId (Error())
        match GitCommitProvenance.inspectProvisionalRoot pin rootTreeId Unchecked.defaultof<_> with
        | Error diagnostic -> Assert.Equal("git-commit-provenance", diagnostic.Code)
        | Ok verified -> failwithf "absent reader accepted: %A" verified
        let throwing =
            { new GitCommitProvenance.IReadOnlyCommitReader with
                member _.ReadExact _ = failwith "private provider failure" }
        match GitCommitProvenance.inspectProvisionalRoot pin rootTreeId throwing with
        | Error diagnostic -> Assert.Equal("git-commit-provenance", diagnostic.Code)
        | Ok verified -> failwithf "throwing reader accepted: %A" verified

    [<Theory>]
    [<InlineData("c4c51c08f95d8d148e0e89a499fd1b9256a77012", "dHJlZSAyYjU1MmQ2YmY5ZDdiNDQ1OGEyOGZjNjU2NjNmYmMyYzdiMDIyMWRjCnRyZWUgMmI1NTJkNmJmOWQ3YjQ0NThhMjhmYzY1NjYzZmJjMmM3YjAyMjFkYwphdXRob3IgRml4dHVyZSA8Zml4dHVyZUBleGFtcGxlLmludmFsaWQ+IDAgKzAwMDAKY29tbWl0dGVyIEZpeHR1cmUgPGZpeHR1cmVAZXhhbXBsZS5pbnZhbGlkPiAwICswMDAwCgptZXNzYWdlCg==", "duplicate")>]
    [<InlineData("20f4821d35a6f3e5465404f5674b09bf1d031932", "dHJlZSBOT1QtQS1TSEExCmF1dGhvciBGaXh0dXJlIDxmaXh0dXJlQGV4YW1wbGUuaW52YWxpZD4gMCArMDAwMApjb21taXR0ZXIgRml4dHVyZSA8Zml4dHVyZUBleGFtcGxlLmludmFsaWQ+IDAgKzAwMDAKCm1lc3NhZ2UK", "tree header")>]
    let ``hashed malformed commit headers cannot certify root`` id encoded message =
        let malformedPin = { pin with CommitId = id }
        let malformed = { observation with CommitId = id; RawCommit = Convert.FromBase64String(encoded) }
        let supplied =
            { new GitCommitProvenance.IReadOnlyCommitReader with
                member _.ReadExact _ = Ok malformed }
        match GitCommitProvenance.inspectProvisionalRoot malformedPin rootTreeId supplied with
        | Error diagnostic -> Assert.Contains(message, diagnostic.Message)
        | Ok verified -> failwithf "malformed commit accepted: %A" verified

    [<Fact>]
    let ``pinned commit composes with exact blob and XML graph inspection`` () =
        let row oid encoded = oid, Convert.FromBase64String(encoded)
        let trees =
            [ row rootTreeId "MTAwNjQ0IFJFQURNRS5tZAC2/ExiC2fZX5U6XBwSMKqrXa2hsDQwMDAwIHNyYwA52/KHUpV/+mkQOSnNWFWL4KxgIg=="
              row "39dbf28752957ffa69103929cd58558be0ac6022" "NDAwMDAgQQCc41uNK5clEiNIWKIvkXjch4zKhDQwMDAwIEIAiKntiowmvIzzDfFCvu2LgF9XXew="
              row "9ce35b8d2b972512234858a22f9178dc878cca84" "MTAwNjQ0IEEuZnNwcm9qAKXQx8j6erOtWXN7LmERQxD3K5F7"
              row "88a9ed8a8c26bc8cf30df142beed8b805f575dec" "MTAwNjQ0IEIuZnNwcm9qAEIwkWFkdAcpD62CTJ/80p7E/pYO" ]
        let projects =
            [ "src/A/A.fsproj", Encoding.UTF8.GetBytes("<Project><ProjectReference Include='../B/B.fsproj' /></Project>")
              "src/B/B.fsproj", Encoding.UTF8.GetBytes("<Project />") ]
        match ProjectReferenceXml.inspectSuppliedPinnedGitSnapshot pin (reader (Ok observation)) rootTreeId trees projects with
        | Error diagnostic -> failwithf "pinned project graph refused: %A" diagnostic
        | Ok graph -> Assert.Equal<string list>([ "src/B/B.fsproj" ], graph.["src/A/A.fsproj"])
