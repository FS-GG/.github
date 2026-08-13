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

    // ---- .github#2324: the sdd-required route's own mandatory output ---------------------------------

    [<Fact>]
    let ``#2324 an sdd-required receipt names its own work and readiness package directories`` () =
        Assert.Equal<string list>(
            [ "work/2137-delivery-route"; "readiness/2137-delivery-route" ],
            DeliveryRoute.mandatorySddPaths (receipt (Some DeliveryRoute.SddRequired))
        )

    [<Fact>]
    let ``#2324 a lightweight route obliges no package, so it exempts nothing`` () =
        let lightweight =
            { receipt (Some DeliveryRoute.Lightweight) with
                SddWorkId = None
                SpecHome = None
                RequiredGates = [] }

        Assert.Empty(DeliveryRoute.mandatorySddPaths lightweight)
        // Even a lightweight receipt still CARRYING an SDD binding (which `validate` refuses outright)
        // exempts nothing: the route, not the leftover field, decides.
        Assert.Empty(DeliveryRoute.mandatorySddPaths (receipt (Some DeliveryRoute.Lightweight)))
        Assert.Empty(DeliveryRoute.mandatorySddPaths (receipt None))

    [<Fact>]
    let ``#2324 a receipt whose SDD binding does not validate exempts nothing`` () =
        // Each of these makes `validateSddBinding` unhappy, and every one of them must fail CLOSED to the
        // empty list rather than produce a partly-guessed package location.
        Assert.Empty(DeliveryRoute.mandatorySddPaths { receipt (Some DeliveryRoute.SddRequired) with SddWorkId = None })
        Assert.Empty(DeliveryRoute.mandatorySddPaths { receipt (Some DeliveryRoute.SddRequired) with SddWorkId = Some "" })
        Assert.Empty(DeliveryRoute.mandatorySddPaths { receipt (Some DeliveryRoute.SddRequired) with SpecHome = None })

        Assert.Empty(
            DeliveryRoute.mandatorySddPaths
                { receipt (Some DeliveryRoute.SddRequired) with
                    SpecHome = Some "work/some-other-item/spec.md" }
        )

        Assert.Empty(
            DeliveryRoute.mandatorySddPaths
                { receipt (Some DeliveryRoute.SddRequired) with
                    RequiredGates = [ "implementationReady"; "verify" ] }
        )

    [<Fact>]
    let ``#2324 a work id that is not path-safe never becomes an exemption`` () =
        // `..` is a machine token by `tokens`' own rule (every character is `.`), and
        // `work/../spec.md` satisfies the expected-spec form — so without the leading-alphanumeric guard
        // this receipt would exempt `work/..` and `readiness/..`, i.e. the repository root.
        let traversal =
            { receipt (Some DeliveryRoute.SddRequired) with
                SddWorkId = Some ".."
                SpecHome = Some "work/../spec.md" }

        Assert.Empty(DeliveryRoute.mandatorySddPaths traversal)

        let hidden =
            { receipt (Some DeliveryRoute.SddRequired) with
                SddWorkId = Some ".git"
                SpecHome = Some "work/.git/spec.md" }

        Assert.Empty(DeliveryRoute.mandatorySddPaths hidden)

        let slashed =
            { receipt (Some DeliveryRoute.SddRequired) with
                SddWorkId = Some "2324/../.."
                SpecHome = Some "work/2324/../../spec.md" }

        Assert.Empty(DeliveryRoute.mandatorySddPaths slashed)

    [<Fact>]
    let ``#2324 the exemption is bound to this receipt's own work id, never to work or readiness as roots`` () =
        let paths = DeliveryRoute.mandatorySddPaths (receipt (Some DeliveryRoute.SddRequired))
        Assert.DoesNotContain("work", paths)
        Assert.DoesNotContain("readiness", paths)
        Assert.All(paths, fun p -> Assert.EndsWith("2137-delivery-route", p))
