namespace FS.GG.Coord.Cli.Tests

open System
open Xunit
open FS.GG.Coord
open FS.GG.Coord.Types
open FS.GG.Coord.Cli

/// THE LEDGER CODEC (#634) — the last place a false green can be manufactured.
///
/// The fold in `Divergence` is total and fails closed. That is worth nothing if the reader in front of
/// it will quietly turn a broken row into a plausible one: a `compared: -1` clamped to `0`, a missing
/// `outcome` defaulted to `0`, a `day` read under the ambient locale. Each of those manufactures a fact
/// the fleet never reported, and the fold would then be deciding — correctly, and totally — about a
/// world that does not exist.
///
/// So the rule this module tests is the one every codec here obeys: **a malformed document is an ERROR,
/// never a default.**
module FleetTests =

    let private ledger (reports: string) =
        $"""{{"schema":"fsgg.coord.ledger/1","engine":"0.1.0","requiredDays":3,"minWorkers":2,
              "today":"2026-07-13","reports":[%s{reports}]}}"""

    let private row =
        """{"worker":"w-a","day":"2026-07-12","engine":"0.1.0","ran":4,"skipped":0,
            "compared":12,"outcome":0,"unpaired":0,"engineRed":0,"reason":0}"""

    let private errorsOf =
        function
        | Error (es: Fleet.Error list) -> es
        | Ok _ -> []

    let private isError r = not (List.isEmpty (errorsOf r))

    let private messages r =
        errorsOf r |> List.map (fun e -> $"%s{e.Path}: %s{e.Message}") |> String.concat " | "

    [<Fact>]
    let ``a well-formed ledger round-trips into typed reports`` () =
        match Fleet.parse (ledger row) with
        | Ok q ->
            Assert.Equal("0.1.0", q.Engine)
            Assert.Equal(3, q.RequiredDays)
            Assert.Equal(2, q.MinWorkers)
            Assert.Equal(DateOnly(2026, 7, 13), q.Today)

            let r = List.exactlyOne q.Reports
            Assert.Equal(WorkerId "w-a", r.Worker)
            Assert.Equal(DateOnly(2026, 7, 12), r.Day)
            Assert.Equal(12, r.Compared)
        | Error es -> failwith $"expected Ok, got %A{es}"

    [<Fact>]
    let ``a NEGATIVE count is refused, not clamped to zero`` () =
        // Clamping would turn a broken publisher into a day that merely looks uncovered — a defect
        // wearing the costume of a fact.
        let bad =
            """{"worker":"w-a","day":"2026-07-12","engine":"0.1.0","ran":4,"skipped":0,
                "compared":-1,"outcome":0,"unpaired":0,"engineRed":0,"reason":0}"""

        let result = Fleet.parse (ledger bad)
        Assert.True(isError result)
        Assert.Contains("may not be negative", messages result)

    [<Fact>]
    let ``a MISSING count is refused, not defaulted to zero`` () =
        // `outcome` is the field that says the engines disagreed. Defaulting it to 0 would read a
        // publisher that forgot to send it as a publisher reporting agreement.
        let bad =
            """{"worker":"w-a","day":"2026-07-12","engine":"0.1.0","ran":4,"skipped":0,
                "compared":12,"unpaired":0,"engineRed":0,"reason":0}"""

        let result = Fleet.parse (ledger bad)
        Assert.True(isError result)
        Assert.Contains("outcome", messages result)

    [<Fact>]
    let ``a locale-ambiguous day is refused — a ledger may not mean different things in two places`` () =
        let bad =
            """{"worker":"w-a","day":"07/12/2026","engine":"0.1.0","ran":4,"skipped":0,
                "compared":12,"outcome":0,"unpaired":0,"engineRed":0,"reason":0}"""

        let result = Fleet.parse (ledger bad)
        Assert.True(isError result)
        Assert.Contains("yyyy-MM-dd", messages result)

    [<Fact>]
    let ``a BLANK worker id is refused — an anonymous row cannot be counted toward a quorum`` () =
        let bad =
            """{"worker":"   ","day":"2026-07-12","engine":"0.1.0","ran":4,"skipped":0,
                "compared":12,"outcome":0,"unpaired":0,"engineRed":0,"reason":0}"""

        let result = Fleet.parse (ledger bad)
        Assert.True(isError result)
        Assert.Contains("may not be blank", messages result)

    [<Fact>]
    let ``an unknown schema is refused — a shim may not outlive its engine in silence`` () =
        let doc =
            """{"schema":"fsgg.coord.ledger/99","engine":"0.1.0","requiredDays":3,"minWorkers":2,
                "today":"2026-07-13","reports":[]}"""

        let result = Fleet.parse doc
        Assert.True(isError result)
        Assert.Contains("unsupported ledger schema", messages result)

    [<Fact>]
    let ``EVERY error is reported, not just the first`` () =
        // A wire format debugged one field per round-trip, across six repos, does not get debugged.
        let bad =
            """{"worker":"","day":"nope","engine":"0.1.0","ran":-4,"skipped":0,
                "compared":12,"outcome":0,"unpaired":0,"engineRed":0,"reason":0}"""

        let es = errorsOf (Fleet.parse (ledger bad))
        Assert.True(List.length es >= 3, $"expected several errors, got %A{es}")

    [<Fact>]
    let ``the rendered verdict carries the tag the client switches on`` () =
        let evidence: Divergence.Evidence =
            { Window = [ DateOnly(2026, 7, 12) ]
              Engine = "0.1.0"
              Workers = [ WorkerId "w-a"; WorkerId "w-b" ]
              Ran = 4
              Skipped = 1
              Compared = 12
              ReasonDivergences = 2
              Discarded = 3 }

        let json = Fleet.render (Green evidence)
        Assert.Contains("\"verdict\":\"green\"", json)
        Assert.Contains("\"compared\":12", json)
        Assert.Contains("\"discarded\":3", json)

    [<Fact>]
    let ``a no-verdict renders as no-verdict and carries its reason`` () =
        let json = Fleet.render (NoVerdict "the shadow has never reported")
        Assert.Contains("\"verdict\":\"no-verdict\"", json)
        Assert.Contains("never reported", json)

    [<Fact>]
    let ``a red verdict renders every reason`` () =
        let json = Fleet.render (Red [ "they disagreed"; "on 2026-07-12" ])
        Assert.Contains("\"verdict\":\"red\"", json)
        Assert.Contains("2026-07-12", json)
