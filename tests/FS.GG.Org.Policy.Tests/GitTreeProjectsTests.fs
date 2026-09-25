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
    let ``empty project tree cannot certify discovery`` () =
        let row = objectRow "d25592c38ef63a211bf1d582f0d5e6c015438854"
                    "MTAwNjQ0IFJFQURNRS5tZAC2/ExiC2fZX5U6XBwSMKqrXbWhsA=="
        refused "no discoverable projects" (fst row) [ row ]

    [<Fact>]
    let ``non SHA1 root identifier has no roster verdict`` () =
        refused "40 lowercase" (String.replicate 64 "a") complete
