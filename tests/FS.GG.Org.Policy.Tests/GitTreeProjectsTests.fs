namespace FS.GG.Org.Policy.Tests

open System
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
