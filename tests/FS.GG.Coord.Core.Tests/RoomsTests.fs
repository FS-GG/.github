namespace FS.GG.Coord.Tests

open Xunit
open FS.GG.Coord
open FS.GG.Coord.Types

/// The `Rooms:` body-line grammar (ADR-0051). It shares `Paths:`'s fence-skipping and
/// up-to-three-leading-spaces rule and the four ref spellings of `EpicBody.childRefs`, so every incident
/// those families had is a test to inherit here. What is UNIQUE to `Rooms:` — additive union across lines
/// and multiple refs per line — gets its own cases.
module RoomsTests =

    let private shorts (refs: Ref list) =
        refs |> List.map (fun r -> $"%s{r.Owner}/%s{r.Repo}#%d{r.Number}")

    // The referencing item is a `.github` item throughout, so a bare `#R` resolves to `FS-GG/.github#R`.
    let private parse body = Rooms.parse "FS-GG" ".github" body

    [<Fact>]
    let ``no Rooms line is the empty set`` () =
        Assert.Equal<string list>([], shorts (parse "Just an ordinary body.\n\nPaths: src/A"))

    [<Fact>]
    let ``a null body is the empty set, not a crash`` () =
        Assert.Equal<string list>([], shorts (parse null))

    [<Fact>]
    let ``a bare #R adopts the referencing item's owner and repo`` () =
        Assert.Equal<string list>([ "FS-GG/.github#42" ], shorts (parse "x\n\nRooms: #42"))

    [<Fact>]
    let ``a lowercase keyword, leading spaces and surrounding space all parse`` () =
        // The `Paths:`/`Blocked on:` family varies only the FIRST letter's case (`[Rr]ooms`), so this
        // pins the same tolerance — a leading-space, lowercase-`r`, space-padded line — and no more.
        Assert.Equal<string list>([ "FS-GG/.github#42" ], shorts (parse "x\n\n   rooms:   #42  "))

    [<Fact>]
    let ``one line may list several rooms — every ref is read, not the first`` () =
        // The departure from `Paths:`/`childRefs`: a room line is additive, so BOTH refs are kept.
        Assert.Equal<string list>(
            [ "FS-GG/.github#12"; "FS-GG/.github#13" ],
            shorts (parse "x\n\nRooms: #12, #13"))

    [<Fact>]
    let ``several Rooms lines UNION — a follow-up adds its own and keeps the room alive (ADR-0051 §4)`` () =
        Assert.Equal<string list>(
            [ "FS-GG/.github#12"; "FS-GG/.github#13" ],
            shorts (parse "x\n\nRooms: #12\n\nRooms: #13"))

    [<Fact>]
    let ``duplicate refs across lines collapse`` () =
        Assert.Equal<string list>([ "FS-GG/.github#12" ], shorts (parse "Rooms: #12\nRooms: #12"))

    [<Fact>]
    let ``all four ref spellings canonicalize to owner/repo#n`` () =
        let body =
            "Rooms: #8 FS.GG.SDD#8 FS-GG/FS.GG.Rendering#12 https://github.com/FS-GG/FS.GG.Audio/issues/9"

        Assert.Equal<string list>(
            [ "FS-GG/.github#8"
              "FS-GG/FS.GG.Audio#9"
              "FS-GG/FS.GG.Rendering#12"
              "FS-GG/FS.GG.SDD#8" ],
            shorts (parse body))

    [<Fact>]
    let ``a repo#n carries its repo, owner defaults`` () =
        Assert.Equal<string list>([ "FS-GG/FS.GG.SDD#8" ], shorts (parse "Rooms: FS.GG.SDD#8"))

    [<Fact>]
    let ``a Rooms line inside a fence is a QUOTATION, not a declaration (#277)`` () =
        let body =
            "How to reference a room:\n\
             \n\
             ```\n\
             Rooms: #12\n\
             ```\n\
             \n\
             ...and this issue references none."

        Assert.Equal<string list>([], shorts (parse body))

    [<Fact>]
    let ``prose on the line is ignored — only refs name rooms`` () =
        Assert.Equal<string list>([ "FS-GG/.github#12" ], shorts (parse "Rooms: see #12 to negotiate"))

    [<Fact>]
    let ``a Rooms line with no ref names no room`` () =
        Assert.Equal<string list>([], shorts (parse "Rooms: TBD"))
