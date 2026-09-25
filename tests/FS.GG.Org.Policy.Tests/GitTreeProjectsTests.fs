namespace FS.GG.Org.Policy.Tests

open System
open System.Text
open FS.GG.Org.Policy
open Xunit

module GitTreeProjectsTests =
    // Fixed raw Git tree objects and SHA-1 IDs generated independently with git hash-object.
    let private objectRow oid encoded = oid, Convert.FromBase64String(encoded)

    let private root =
        objectRow "a2a00b1cb79592e3dc312008e3e9f02c78a8c014"
            "MTAwNjQ0IFJFQURNRS5tZAC2/ExiC2fZX5U6XBwSMKqrXbWhsDQwMDAwIHNyYwB55dcUrLIlJeu3CBnQp+RaZikgIA=="

    let private src =
        objectRow "79e5d714acb22525ebb70819d0a7e45a66292020"
            "NDAwMDAgQQAtQqaIEJYFFUn9uNHOYc68aBtsHjQwMDAwIEIAiKntiowmvIzzDfFCvu2LgF9XXew="

    let private a =
        objectRow "2d42a6881096051549fdb8d1ce61cebc681b6c1e"
            "MTAwNjQ0IEEuZnNwcm9qAEIwkWFkdAcpD62CTJ/80p7E/pYO"

    let private b =
        objectRow "88a9ed8a8c26bc8cf30df142beed8b805f575dec"
            "MTAwNjQ0IEIuZnNwcm9qAEIwkWFkdAcpD62CTJ/80p7E/pYO"

    let private complete = [ root; src; a; b ]

    // This second fixed tree binds A to an actual A -> B edge. The first fixture has two
    // identical empty project blobs and cannot expose a stale A source.
    let private edgeRoot =
        objectRow "2b552d6bf9d7b4458a28fc65663fbc2c7b0221dc"
            "MTAwNjQ0IFJFQURNRS5tZAC2/ExiC2fZX5U6XBwSMKqrXa2hsDQwMDAwIHNyYwA52/KHUpV/+mkQOSnNWFWL4KxgIg=="
    let private edgeSrc =
        objectRow "39dbf28752957ffa69103929cd58558be0ac6022"
            "NDAwMDAgQQCc41uNK5clEiNIWKIvkXjch4zKhDQwMDAwIEIAiKntiowmvIzzDfFCvu2LgF9XXew="
    let private edgeA =
        objectRow "9ce35b8d2b972512234858a22f9178dc878cca84"
            "MTAwNjQ0IEEuZnNwcm9qAKXQx8j6erOtWXN7LmERQxD3K5F7"
    let private edgeTrees = [ edgeRoot; edgeSrc; edgeA; b ]
    let private edgeBytes = Encoding.UTF8.GetBytes("<Project><ProjectReference Include='../B/B.fsproj' /></Project>")
    let private emptyBytes = Encoding.UTF8.GetBytes("<Project />")
    let private projectBytes = [ "src/A/A.fsproj", edgeBytes; "src/B/B.fsproj", emptyBytes ]

    let private validRead (kind: GitTreeProjects.ObjectKind) (oid: string)
        : Result<GitTreeProjects.ObjectObservation, unit> =
        let entries =
            (edgeTrees |> List.map (fun (objectId, bytes) -> objectId, GitTreeProjects.Tree, bytes))
            @ [ "a5d0c7c8fa7ab3ad59737b2e61114310f72b917b", GitTreeProjects.Blob, edgeBytes
                "42309161647407290fad824c9ffcd29ec4fe960e", GitTreeProjects.Blob, emptyBytes ]
        match entries |> List.tryFind (fun (objectId, expectedKind, _) -> objectId = oid && expectedKind = kind) with
        | None -> Error ()
        | Some(_, _, bytes) ->
            Ok { ObjectId = oid; Kind = kind; Bytes = bytes }

    let private objectReader read =
        { new GitTreeProjects.IReadOnlyObjectReader with
            member _.ReadExact(kind, oid) = read kind oid }

    let private protectedRepository: GitHubProtectedBranchPin.ExactRepository =
        { RepositoryNodeId = "R_fixture_one"; RepositoryFullName = "FS-GG/.github" }

    let private protectedResponse (request: GitHubProtectedBranchPin.ExactRequest) commitId
        : GitHubProtectedBranchPin.BranchResponse =
        let body =
            sprintf """{"name":"main","commit":{"sha":"%s"},"protected":true}""" commitId
        { StatusCode = 200
          ResponseUrl = request.Url
          MediaType = "application/json"
          Body = Encoding.UTF8.GetBytes(body) }

    let private protectedCommit = "539aff7e655d22b1761850cd6be868eecc2886e4"

    let private membershipReader =
        { new GitHubCommitMembership.IReadOnlyGraphQlReader with
            member _.ExecuteExact _ =
                Ok(Encoding.UTF8.GetBytes("""{"data":{"repository":{"id":"R_fixture_one","nameWithOwner":"FS-GG/.github","object":{"__typename":"Commit","oid":"539aff7e655d22b1761850cd6be868eecc2886e4","tree":{"oid":"2b552d6bf9d7b4458a28fc65663fbc2c7b0221dc"}}}}}""")) }

    let private commitReader =
        { new GitCommitProvenance.IReadOnlyCommitReader with
            member _.ReadExact pin =
                Ok { RepositoryNodeId = pin.RepositoryNodeId; RepositoryFullName = pin.RepositoryFullName
                     CommitId = pin.CommitId
                     RawCommit = Convert.FromBase64String("dHJlZSAyYjU1MmQ2YmY5ZDdiNDQ1OGEyOGZjNjU2NjNmYmMyYzdiMDIyMWRjCmF1dGhvciBGaXh0dXJlIDxmaXh0dXJlQGV4YW1wbGUuaW52YWxpZD4gMCArMDAwMApjb21taXR0ZXIgRml4dHVyZSA8Zml4dHVyZUBleGFtcGxlLmludmFsaWQ+IDAgKzAwMDAKCmZpeGVkIEEgdG8gQiBmaXh0dXJlCg==") } }

    let private inspectProtected branchReader read =
        ProjectReferenceXml.inspectReadOnlyProtectedBranchSnapshot
            protectedRepository branchReader membershipReader commitReader (fst edgeRoot) (objectReader read)

    let private refusedObject message read =
        match ProjectReferenceXml.inspectReadOnlyGitObjectSnapshot (fst edgeRoot) (objectReader read) with
        | Error diagnostic ->
            Assert.Equal("git-object-provider", diagnostic.Code)
            Assert.Contains(message, diagnostic.Message)
        | Ok graph -> failwithf "unverified object provider produced graph: %A" graph

    [<Fact>]
    let ``read-only object closure preserves independently fixed A to B edge`` () =
        match ProjectReferenceXml.inspectReadOnlyGitObjectSnapshot (fst edgeRoot) (objectReader validRead) with
        | Error diagnostic -> failwithf "valid object closure refused: %A" diagnostic
        | Ok graph -> Assert.Equal<string list>([ "src/B/B.fsproj" ], graph.["src/A/A.fsproj"])

    [<Fact>]
    let ``protected pin through read-only object closure has no supplied-byte shortcut`` () =
        let branchReader =
            { new GitHubProtectedBranchPin.IReadOnlyProtectedBranchReader with
                member _.ReadExact request =
                    Ok(protectedResponse request protectedCommit) }
        let inspect read = inspectProtected branchReader read
        match inspect validRead with
        | Error diagnostic -> failwithf "complete protected object chain refused: %A" diagnostic
        | Ok graph -> Assert.Equal<string list>([ "src/B/B.fsproj" ], graph.["src/A/A.fsproj"])
        match inspect (fun kind oid ->
                if kind = GitTreeProjects.Blob && oid = "42309161647407290fad824c9ffcd29ec4fe960e" then Error ()
                else validRead kind oid) with
        | Error diagnostic -> Assert.Equal("git-object-provider", diagnostic.Code)
        | Ok graph -> failwithf "missing B blob produced protected graph: %A" graph

    [<Fact>]
    let ``protected tip changing during graph reads has no provisional graph`` () =
        let mutable reads = 0
        let branchReader =
            { new GitHubProtectedBranchPin.IReadOnlyProtectedBranchReader with
                member _.ReadExact request =
                    reads <- reads + 1
                    let commit = if reads = 1 then protectedCommit else String.replicate 40 "a"
                    Ok(protectedResponse request commit) }
        match inspectProtected branchReader validRead with
        | Error diagnostic ->
            Assert.Equal("github-protected-pin", diagnostic.Code)
            Assert.Contains("changed", diagnostic.Message)
        | Ok graph -> failwithf "changed protected tip produced graph: %A" graph
        Assert.Equal(2, reads)

    [<Fact>]
    let ``unavailable final protected tip read has no provisional graph`` () =
        let mutable reads = 0
        let branchReader =
            { new GitHubProtectedBranchPin.IReadOnlyProtectedBranchReader with
                member _.ReadExact request =
                    reads <- reads + 1
                    if reads = 1 then Ok(protectedResponse request protectedCommit)
                    else Error () }
        match inspectProtected branchReader validRead with
        | Error diagnostic -> Assert.Equal("github-protected-pin", diagnostic.Code)
        | Ok graph -> failwithf "unavailable final pin read produced graph: %A" graph
        Assert.Equal(2, reads)

    [<Fact>]
    let ``missing referenced project blob prevents graph despite valid supplied snapshot`` () =
        match ProjectReferenceXml.inspectSuppliedGitSnapshot (fst edgeRoot) edgeTrees projectBytes with
        | Error diagnostic -> failwithf "supplied counterexample changed: %A" diagnostic
        | Ok graph -> Assert.Equal<string list>([ "src/B/B.fsproj" ], graph.["src/A/A.fsproj"])
        refusedObject "absent" (fun kind oid ->
            if kind = GitTreeProjects.Blob && oid = "42309161647407290fad824c9ffcd29ec4fe960e" then Error ()
            else validRead kind oid)

    [<Fact>]
    let ``wrong object kind and identity cannot satisfy exact read`` () =
        refusedObject "kind" (fun kind oid ->
            match validRead kind oid with
            | Ok observation when kind = GitTreeProjects.Tree && oid = fst edgeA ->
                Ok { observation with Kind = GitTreeProjects.Blob }
            | other -> other)
        refusedObject "ID" (fun kind oid ->
            match validRead kind oid with
            | Ok observation when kind = GitTreeProjects.Blob && oid = "a5d0c7c8fa7ab3ad59737b2e61114310f72b917b" ->
                Ok { observation with ObjectId = String.replicate 40 "a" }
            | other -> other)

    [<Fact>]
    let ``tampered blob bytes and missing subtree cannot certify graph`` () =
        refusedObject "hash" (fun kind oid ->
            match validRead kind oid with
            | Ok observation when kind = GitTreeProjects.Blob && oid = "a5d0c7c8fa7ab3ad59737b2e61114310f72b917b" ->
                let changed = Array.copy observation.Bytes
                changed.[0] <- byte 'X'
                Ok { observation with Bytes = changed }
            | other -> other)
        refusedObject "absent" (fun kind oid ->
            if kind = GitTreeProjects.Tree && oid = fst edgeA then Error ()
            else validRead kind oid)

    [<Fact>]
    let ``absent or throwing object reader cannot certify graph`` () =
        match ProjectReferenceXml.inspectReadOnlyGitObjectSnapshot (fst edgeRoot) Unchecked.defaultof<_> with
        | Error diagnostic -> Assert.Equal("git-object-provider", diagnostic.Code)
        | Ok graph -> failwithf "absent reader produced graph: %A" graph
        refusedObject "unavailable" (fun _ _ -> failwith "private reader failure")

    let private refusedBlob expectedMessage sources =
        match ProjectReferenceXml.inspectSuppliedGitSnapshot (fst edgeRoot) edgeTrees sources with
        | Error diagnostic ->
            Assert.Equal("git-blob-source", diagnostic.Code)
            Assert.Contains(expectedMessage, diagnostic.Message)
        | Ok graph -> failwithf "unbound project bytes produced a graph: %A" graph

    [<Fact>]
    let ``tree blob binding preserves the A to B edge for Rule b`` () =
        match ProjectReferenceXml.inspectSuppliedGitSnapshot (fst edgeRoot) edgeTrees projectBytes with
        | Error diagnostic -> failwithf "bound source refused: %A" diagnostic
        | Ok graph ->
            Assert.Equal<string list>([ "src/B/B.fsproj" ], graph.["src/A/A.fsproj"])
            match RuleB.inspect [ "src/A/**" ] graph with
            | Error diagnostic -> failwithf "coverage refused: %A" diagnostic
            | Ok coverage -> Assert.Equal<(string * string) list>([ "src/A/A.fsproj", "src/B/B.fsproj" ], coverage.Uncovered)

    [<Fact>]
    let ``stale A bytes cannot erase tree bound edge`` () =
        let stale = [ "src/A/A.fsproj", emptyBytes; "src/B/B.fsproj", emptyBytes ]
        // A caller can make the older supplied-digest adapter green by choosing the stale
        // digest too. The fixed Git tree above independently fixes A's actual blob ID.
        let staleDigest = "400b35829b2a391f4048da02e1108b98db09431b2947e806a184743bd1b33c3a"
        match ProjectReferenceXml.inspectSuppliedProjectBytesAgainstDigests
                [ "src/A/A.fsproj", staleDigest; "src/B/B.fsproj", staleDigest ] stale with
        | Error diagnostic -> failwithf "old supplied digest counterexample changed: %A" diagnostic
        | Ok graph ->
            match RuleB.inspect [ "src/A/**" ] graph with
            | Error diagnostic -> failwithf "counterexample coverage refused: %A" diagnostic
            | Ok coverage -> Assert.Empty(coverage.Uncovered)
        refusedBlob "blob ID" stale

    [<Fact>]
    let ``missing duplicate and foreign supplied project rows refuse blob binding`` () =
        refusedBlob "absent" [ "src/A/A.fsproj", edgeBytes ]
        refusedBlob "duplicate" (projectBytes @ [ "src/A/A.fsproj", edgeBytes ])
        refusedBlob "not in" (projectBytes @ [ "src/C/C.fsproj", emptyBytes ])

    [<Fact>]
    let ``null project bytes cannot satisfy tree blob ID`` () =
        refusedBlob "absent" [ "src/A/A.fsproj", Unchecked.defaultof<byte[]>; "src/B/B.fsproj", emptyBytes ]

    let private refused expectedMessage rootOid objects =
        match GitTreeProjects.inspectSha1 rootOid objects with
        | Error diagnostic ->
            Assert.Equal("git-tree-roster", diagnostic.Code)
            Assert.Contains(expectedMessage, diagnostic.Message)
        | Ok roster -> failwithf "unverified tree produced project roster: %A" roster

    [<Fact>]
    let ``rooted tree discovers independent project omitted by supplied XML rows`` () =
        match GitTreeProjects.inspectSha1 (fst root) complete with
        | Error diagnostic -> failwithf "valid rooted tree refused: %A" diagnostic
        | Ok roster ->
            Assert.Equal<(string * string) list>(
                [ "src/A/A.fsproj", "42309161647407290fad824c9ffcd29ec4fe960e"
                  "src/B/B.fsproj", "42309161647407290fad824c9ffcd29ec4fe960e" ], roster)
            match ProjectReferenceXml.inspectSuppliedProjectSetAgainstRoster
                    (roster |> List.map fst) [ "src/A/A.fsproj", "<Project />" ] with
            | Error diagnostic ->
                Assert.Equal("project-roster", diagnostic.Code)
                Assert.Contains("src/B/B.fsproj", diagnostic.Message)
            | Ok graph -> failwithf "tree-discovered B was omitted from graph: %A" graph

    [<Fact>]
    let ``missing referenced subtree has no roster verdict`` () =
        refused "absent" (fst root) [ root; src; a ]

    [<Fact>]
    let ``tree bytes must match their supplied Git object ID`` () =
        let oid, raw = root
        let tampered = Array.copy raw
        tampered.[0] <- byte '2'
        refused "hash" oid ((oid, tampered) :: [ src; a; b ])

    [<Fact>]
    let ``duplicate supplied tree object cannot overwrite its bytes`` () =
        refused "duplicate supplied tree object" (fst root) (root :: complete)

    [<Fact>]
    let ``symlink and gitlink tree entries require external source proof`` () =
        [ objectRow "14ed1140ca526e48d1d1ea31c8f0657383892d02"
              "MTIwMDAwIEEuZnNwcm9qAEIwkWFkdAcpD62CTJ/80p7E/pYO"
          objectRow "0b017b3739e233b8d61860101daa5d282c067986"
              "MTYwMDAwIHZlbmRvcgAREREREREREREREREREREREREREQ==" ]
        |> List.iter (fun row -> refused "symlink or gitlink" (fst row) [ row ])

    [<Fact>]
    let ``malformed or duplicate tree entries refuse even with matching object ID`` () =
        [ objectRow "c9ef3ac65ce4b6784c47545c3bde9d7c8f001aff"
              "MTAwNjQ0IEEuZnNwcm9qAEIwkWFkdAcpD62CTJ/80p7E/pY="
          objectRow "8fdad4213708e1d79a52e18d5b9c2aa6c5f208e9"
              "MTAwNjQ0IEEuZnNwcm9qAEIwkWFkdAcpD62CTJ/80p7E/pYOMTAwNjQ0IEEuZnNwcm9qAEIwkWFkdAcpD62CTJ/80p7E/pYO" ]
        |> List.iter (fun row -> refused "tree entry" (fst row) [ row ])

    [<Fact>]
    let ``Git reserved dotgit tree entry cannot certify a project roster`` () =
        // Fixed raw trees from git hash-object --literally. git fsck --strict reports hasDotgit.
        [ objectRow "2bb4e4b14ca4b042c015dc808d7811677b796d35"
              "NDAwMDAgLmdpdACc41uNK5clEiNIWKIvkXjch4zKhA=="
          objectRow "e5c0b895b61feb3e4eef633c8fe60779b880e75b"
              "NDAwMDAgLkdJVACc41uNK5clEiNIWKIvkXjch4zKhA=="
          objectRow "fd0c9520e518b00f8550f0726cd2d5dc9c302c36"
              "NDAwMDAgLmdpdCAAnONbjSuXJRIjSFiiL5F43IeMyoQ="
          objectRow "7c05d8023a3d81dfa5b5655c2bfaff8eb41dcafb"
              "NDAwMDAgLmdpdC4AnONbjSuXJRIjSFiiL5F43IeMyoQ=" ]
        |> List.iter (fun reserved -> refused "reserved .git" (fst reserved) [ reserved; edgeA ])

    [<Fact>]
    let ``empty project tree cannot certify discovery`` () =
        let row = objectRow "d25592c38ef63a211bf1d582f0d5e6c015438854"
                    "MTAwNjQ0IFJFQURNRS5tZAC2/ExiC2fZX5U6XBwSMKqrXbWhsA=="
        refused "no discoverable projects" (fst row) [ row ]

    [<Fact>]
    let ``non SHA1 root identifier has no roster verdict`` () =
        refused "40 lowercase" (String.replicate 64 "a") complete

    [<Fact>]
    let ``terminal newline root cannot dispatch raw object provider`` () =
        let mutable calls = 0
        let provider =
            objectReader (fun _ _ ->
                calls <- calls + 1
                Error ())
        match ProjectReferenceXml.inspectReadOnlyGitObjectSnapshot (fst edgeRoot + "\n") provider with
        | Error diagnostic -> Assert.Equal("git-object-provider", diagnostic.Code)
        | Ok graph -> failwithf "noncanonical root produced graph: %A" graph
        Assert.Equal(0, calls)
