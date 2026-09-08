namespace FS.GG.Coord.Tests

open Xunit
open FS.GG.Coord

module TelemetryCiTests =
    let private interval startAt endAt = TelemetryCi.interval (Some startAt) (Some endAt) |> Option.get

    [<Fact>]
    let ``UTEL-04A parallel jobs sum runner time but union wall time`` () =
        let jobs = [ interval "2026-01-01T00:00:00Z" "2026-01-01T00:01:00Z"; interval "2026-01-01T00:00:00Z" "2026-01-01T00:00:40Z" ]
        Assert.Equal(Some 60L, TelemetryCi.unionSeconds jobs)
        Assert.Equal(100L, jobs |> List.sumBy (fun value -> int64 (value.EndUtc - value.StartUtc).TotalSeconds))

    [<Fact>]
    let ``UTEL-04A administrative time subtracts useful overlap from wait union`` () =
        let waits = [ interval "2026-01-01T00:00:00Z" "2026-01-01T00:02:00Z"; interval "2026-01-01T00:00:30Z" "2026-01-01T00:01:30Z" ]
        let usefulAndProductive = [ interval "2026-01-01T00:00:20Z" "2026-01-01T00:01:40Z"; interval "2026-01-01T00:00:00Z" "2026-01-01T00:00:10Z" ]
        Assert.Equal(Some 30L, TelemetryCi.subtractSeconds waits usefulAndProductive)

    [<Fact>]
    let ``UTEL-04A missing or reversed endpoints stay unknown`` () =
        Assert.Equal(None, TelemetryCi.interval None (Some "2026-01-01T00:00:00Z"))
        Assert.Equal(None, TelemetryCi.interval (Some "2026-01-01T00:01:00Z") (Some "2026-01-01T00:00:00Z"))
