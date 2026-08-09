namespace FS.GG.Coord.Core.Tests

open Xunit
open FS.GG.Coord

module DeliveryRouteTests =
    let private receipt route : DeliveryRoute.Receipt =
        { DeliveryRoute.Schema = DeliveryRoute.Schema
          Subject = "FS-GG/.github#2137"
          SubjectRevision = "body-sha"
          Route = route
          Agent = "brant-cf73"
          Timestamp = "2026-08-09T00:00:00Z"
          ReasonCodes = [ "multi-phase" ]
          Rationale = "The state machine crosses the client, board and SDD boundary."
          DeclaredImpacts = [ "public-cli" ]
          ObservedFacts = [ "current-board-read" ]
          SddWorkId = Some "2137-delivery-route"
          SpecHome = Some "work/2137-delivery-route/spec.md"
          RequiredGates = [ "implementationReady"; "analyze"; "verify"; "ship" ] }

    [<Fact>]
    let ``#2137 SDD routing is explicit and current rather than inferred from checklist facts`` () =
        Assert.True(DeliveryRoute.validate "FS-GG/.github#2137" "body-sha" (receipt (Some DeliveryRoute.SddRequired)) |> Result.isOk)
        Assert.True(DeliveryRoute.validate "FS-GG/.github#2137" "body-sha" (receipt None) |> Result.isError)

    [<Fact>]
    let ``#2137 a changed subject revision invalidates a previously valid routing receipt`` () =
        Assert.True(DeliveryRoute.validate "FS-GG/.github#2137" "new-body-sha" (receipt (Some DeliveryRoute.SddRequired)) |> Result.isError)

    [<Fact>]
    let ``#2137 SDD route cannot omit its work binding or required gate`` () =
        let missingBinding = { receipt (Some DeliveryRoute.SddRequired) with SddWorkId = None; RequiredGates = [] }
        Assert.True(DeliveryRoute.validate "FS-GG/.github#2137" "body-sha" missingBinding |> Result.isError)

    [<Fact>]
    let ``#2137 SDD route rejects a mismatched spec home or lifecycle gate contract`` () =
        let mismatchedSpec =
            { receipt (Some DeliveryRoute.SddRequired) with
                SpecHome = Some "work/another-item/spec.md" }

        let mismatchedGates =
            { receipt (Some DeliveryRoute.SddRequired) with
                RequiredGates = [ "implementationReady"; "verify" ] }

        Assert.True(DeliveryRoute.validate "FS-GG/.github#2137" "body-sha" mismatchedSpec |> Result.isError)
        Assert.True(DeliveryRoute.validate "FS-GG/.github#2137" "body-sha" mismatchedGates |> Result.isError)

    [<Fact>]
    let ``#2137 decision facts never select a route and unreadable revisions produce no verdict`` () =
        match DeliveryRoute.decide "FS-GG/.github#2137" None (Some(receipt (Some DeliveryRoute.SddRequired))) with
        | DeliveryRoute.Unreadable _ -> ()
        | other -> failwithf "unreadable route facts must fail closed, got %A" other

        match DeliveryRoute.decide "FS-GG/.github#2137" (Some "body-sha") None with
        | DeliveryRoute.Stale errors -> Assert.Contains("delivery-route receipt is missing", errors)
        | other -> failwithf "missing decision must not default, got %A" other

    [<Fact>]
    let ``#2137 receipt rejects malformed identity evidence and clock facts`` () =
        let malformed =
            { receipt (Some DeliveryRoute.Lightweight) with
                Agent = ""
                Timestamp = "soon"
                ReasonCodes = [ "multi phase" ]
                DeclaredImpacts = [ "public cli" ]
                ObservedFacts = [ "" ]
                SddWorkId = None
                SpecHome = None
                RequiredGates = [] }
        Assert.True(DeliveryRoute.validate "FS-GG/.github#2137" "body-sha" malformed |> Result.isError)
