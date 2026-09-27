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
    let ``repository node ID with whitespace cannot root commit`` () =
        let malformedPin = { pin with RepositoryNodeId = pin.RepositoryNodeId + "\n" }
        let malformedObservation =
            { observation with RepositoryNodeId = malformedPin.RepositoryNodeId }
        let mutable calls = 0
        let unsafeReader =
            { new GitCommitProvenance.IReadOnlyCommitReader with
                member _.ReadExact _ =
                    calls <- calls + 1
                    Ok malformedObservation }
        match GitCommitProvenance.inspectProvisionalRoot malformedPin rootTreeId unsafeReader with
        | Error diagnostic -> Assert.Equal("git-commit-provenance", diagnostic.Code)
        | Ok root -> failwithf "malformed node ID rooted commit: %A" root
        Assert.Equal(0, calls)

    [<Fact>]
    let ``dot segment repository cannot root commit`` () =
        let malformedPin = { pin with RepositoryFullName = "FS-GG/.." }
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
        | Ok root -> failwithf "dot segment repository rooted commit: %A" root
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
    [<InlineData("7d49c876b9cded130831960eb86e50bcebd8188c", "dHJlZSAyYjU1MmQ2YmY5ZDdiNDQ1OGEyOGZjNjU2NjNmYmMyYzdiMDIyMWRjCnBhcmVudCBub3QtYS1zaGExCmF1dGhvciBGaXh0dXJlIDxmaXh0dXJlQGV4YW1wbGUuaW52YWxpZD4gMCArMDAwMApjb21taXR0ZXIgRml4dHVyZSA8Zml4dHVyZUBleGFtcGxlLmludmFsaWQ+IDAgKzAwMDAKCmZpeGVkIG1hbGZvcm1lZCBwYXJlbnQgZml4dHVyZQo=", "parent")>]
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
    let ``hashed NUL in commit header cannot certify root`` () =
        // Fixed with git hash-object -t commit --literally --stdin over header bytes containing NUL.
        let id = "db54d9c053dcc5424179270c6f114eda147848bf"
        let bytes = Convert.FromBase64String(
            "dHJlZSAyYjU1MmQ2YmY5ZDdiNDQ1OGEyOGZjNjU2NjNmYmMyYzdiMDIyMWRjCmF1dGhvciBGaXh0dXJlIDxmaXh0dXJlQGV4YW1wbGUuaW52YWxpZD4gMCArMDAwMApjb21taXR0ZXIgRml4dHVyZSA8Zml4dHVyZUBleGFtcGxlLmludmFsaWQ+IDAgKzAwMDAKeC1leHRyYSBiZWZvcmUAYWZ0ZXIKCmZpeGVkIG1hbGZvcm1lZCBoZWFkZXIgZml4dHVyZQo=")
        let malformedPin = { pin with CommitId = id }
        let malformedObservation = { observation with CommitId = id; RawCommit = bytes }
        let rawReader =
            { new GitCommitProvenance.IReadOnlyCommitReader with
                member _.ReadExact _ = Ok malformedObservation }
        match GitCommitProvenance.inspectProvisionalRoot malformedPin rootTreeId rawReader with
        | Error diagnostic ->
            Assert.Equal("git-commit-provenance", diagnostic.Code)
            Assert.Contains("NUL", diagnostic.Message)
        | Ok root -> failwithf "NUL-bearing commit header rooted a graph: %A" root

    [<Theory>]
    [<InlineData("e7397a26e2f174b48d5f827be83177d27f4a2db8", "dHJlZSAyYjU1MmQ2YmY5ZDdiNDQ1OGEyOGZjNjU2NjNmYmMyYzdiMDIyMWRjCmF1dGhvciBGaXh0dXJlIDAgKzAwMDAKY29tbWl0dGVyIEZpeHR1cmUgPGZpeHR1cmVAZXhhbXBsZS5pbnZhbGlkPiAwICswMDAwCgpmaXhlZCBmaXh0dXJlCg==")>]
    [<InlineData("d855c73d582340e39e8447c34abfc8a2502064cf", "dHJlZSAyYjU1MmQ2YmY5ZDdiNDQ1OGEyOGZjNjU2NjNmYmMyYzdiMDIyMWRjCmF1dGhvciBGaXh0dXJlIDxmaXh0dXJlQGV4YW1wbGUuaW52YWxpZD4gMCArMDAwMApjb21taXR0ZXIgRml4dHVyZSAwICswMDAwCgpmaXhlZCBmaXh0dXJlCg==")>]
    [<InlineData("b772b07ae61fc6e96b2524fee4d05ea9c0f7c4ff", "dHJlZSAyYjU1MmQ2YmY5ZDdiNDQ1OGEyOGZjNjU2NjNmYmMyYzdiMDIyMWRjCmF1dGhvciBGaXh0dXJlIDxmaXh0dXJlQGV4YW1wbGUuaW52YWxpZD4KY29tbWl0dGVyIEZpeHR1cmUgPGZpeHR1cmVAZXhhbXBsZS5pbnZhbGlkPiAwICswMDAwCgpmaXhlZCBmaXh0dXJlCg==")>]
    [<InlineData("19303b3944553890f7daa173bab804a33ed3d4cb", "dHJlZSAyYjU1MmQ2YmY5ZDdiNDQ1OGEyOGZjNjU2NjNmYmMyYzdiMDIyMWRjCmF1dGhvciBGaXh0dXJlIDxmaXh0dXJlQGV4YW1wbGUuaW52YWxpZD4gbm90YWRhdGUgKzAwMDAKY29tbWl0dGVyIEZpeHR1cmUgPGZpeHR1cmVAZXhhbXBsZS5pbnZhbGlkPiAwICswMDAwCgpmaXhlZCBmaXh0dXJlCg==")>]
    let ``git fsck rejected author or committer identity cannot root commit`` id encoded =
        // Fixed with git hash-object --literally; git fsck --strict reports missingEmail,
        // missingSpaceBeforeDate, or badDate for these independently authored commit bytes.
        let malformedPin = { pin with CommitId = id }
        let malformed = { observation with CommitId = id; RawCommit = Convert.FromBase64String(encoded) }
        let rawReader =
            { new GitCommitProvenance.IReadOnlyCommitReader with
                member _.ReadExact _ = Ok malformed }
        match GitCommitProvenance.inspectProvisionalRoot malformedPin rootTreeId rawReader with
        | Error diagnostic ->
            Assert.Equal("git-commit-provenance", diagnostic.Code)
            Assert.Contains("identity", diagnostic.Message)
        | Ok root -> failwithf "git-fsck-rejected identity rooted a graph: %A" root

    [<Fact>]
    let ``git fsck accepted empty email retains provisional commit root`` () =
        let id = "6dafd16f57156de921d473e4c3afac57b529d654"
        let raw = Convert.FromBase64String("dHJlZSAyYjU1MmQ2YmY5ZDdiNDQ1OGEyOGZjNjU2NjNmYmMyYzdiMDIyMWRjCmF1dGhvciBGaXh0dXJlIDw+IDAgKzAwMDAKY29tbWl0dGVyIEZpeHR1cmUgPGZpeHR1cmVAZXhhbXBsZS5pbnZhbGlkPiAwICswMDAwCgpmaXhlZCBmaXh0dXJlCg==")
        let acceptedPin = { pin with CommitId = id }
        let accepted = { observation with CommitId = id; RawCommit = raw }
        let rawReader =
            { new GitCommitProvenance.IReadOnlyCommitReader with
                member _.ReadExact _ = Ok accepted }
        match GitCommitProvenance.inspectProvisionalRoot acceptedPin rootTreeId rawReader with
        | Error diagnostic -> failwithf "git-fsck-accepted identity refused: %A" diagnostic
        | Ok root -> Assert.Equal(rootTreeId, root.TreeId)

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
