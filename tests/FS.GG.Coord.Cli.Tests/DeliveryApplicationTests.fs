namespace FS.GG.Coord.Cli.Tests

open Xunit
open FS.GG.Coord
open FS.GG.Coord.Cli

module DeliveryApplicationTests =
    let comment body : Driver.ReviewComment = { Id = 1L; Url = "https://example.test/1"; Body = body }

    [<Fact>]
    let ``#2131 non-empty obligation receipt is head-bound and verifies only its declared id`` () =
        let comments =
            [ comment "<!-- fsgg:delivery-obligation id=nuget kind=publication head=head-a -->"
              comment "<!-- fsgg:delivery-receipt id=nuget head=head-a evidence=https://nuget.example/package -->" ]
        match DeliveryApplication.obligationsFromComments "head-a" comments with
        | Ok [ obligation ] ->
            Assert.Equal("nuget", obligation.Id)
            Assert.Equal("publication", obligation.Kind)
            Assert.True(obligation.Verified)
        | other -> failwithf "expected one verified obligation, got %A" other

    [<Fact>]
    let ``#2131 stale and undeclared obligation facts are refused`` () =
        match DeliveryApplication.obligationsFromComments "head-b" [ comment "<!-- fsgg:delivery-obligation id=nuget kind=publication head=head-a -->" ] with
        | Error reason -> Assert.Contains("stale", reason)
        | other -> failwithf "expected stale declaration refusal, got %A" other

        match DeliveryApplication.obligationsFromComments "head-a" [] with
        | Error reason -> Assert.Contains("undeclared", reason)
        | other -> failwithf "expected undeclared refusal, got %A" other
