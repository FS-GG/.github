namespace FS.GG.Coord.Tests

open System.Text
open Xunit
open FS.GG.Coord

module TelemetryCiTests =
    let private interval startAt endAt =
        TelemetryCi.interval (Some startAt) (Some endAt) |> Option.get

    let private assignment (value: string) =
        value |> Encoding.UTF8.GetBytes |> TelemetryCi.parseAssignment

    [<Fact>]
    let ``UTEL-06D CI assignment accepts only its literal schema and safe identifiers`` () =
        let valid =
            """{"schema":"fsgg.telemetry.ci-assignment/1","featureId":"UTEL-06","itemId":"UTEL-06.4","attemptId":"attempt-1","parentAttemptId":null,"producerStream":"routine-delivery"}"""

        Assert.True(assignment valid |> Result.isOk)

        Assert.True(
            assignment (valid.Replace("ci-assignment/1", "ci-assignment/2"))
            |> Result.isError
        )

        Assert.True(
            assignment (valid.Replace("\"producerStream\"", "\"verdict\":\"pass\",\"producerStream\""))
            |> Result.isError
        )

        Assert.True(assignment (valid.Replace("UTEL-06.4", "../unsafe")) |> Result.isError)

    [<Fact>]
    let ``UTEL-04A parallel jobs sum runner time but union wall time`` () =
        let jobs =
            [
                interval "2026-01-01T00:00:00Z" "2026-01-01T00:01:00Z"
                interval "2026-01-01T00:00:00Z" "2026-01-01T00:00:40Z"
            ]

        Assert.Equal(Some 60L, TelemetryCi.unionSeconds jobs)

        Assert.Equal(
            100L,
            jobs
            |> List.sumBy (fun value -> int64 (value.EndUtc - value.StartUtc).TotalSeconds)
        )

    [<Fact>]
    let ``UTEL-04A administrative time subtracts useful overlap from wait union`` () =
        let waits =
            [
                interval "2026-01-01T00:00:00Z" "2026-01-01T00:02:00Z"
                interval "2026-01-01T00:00:30Z" "2026-01-01T00:01:30Z"
            ]

        let usefulAndProductive =
            [
                interval "2026-01-01T00:00:20Z" "2026-01-01T00:01:40Z"
                interval "2026-01-01T00:00:00Z" "2026-01-01T00:00:10Z"
            ]

        Assert.Equal(Some 30L, TelemetryCi.subtractSeconds waits usefulAndProductive)

    [<Fact>]
    let ``UTEL-04A missing or reversed endpoints stay unknown`` () =
        Assert.Equal(None, TelemetryCi.interval None (Some "2026-01-01T00:00:00Z"))
        Assert.Equal(None, TelemetryCi.interval (Some "2026-01-01T00:01:00Z") (Some "2026-01-01T00:00:00Z"))

    [<Fact>]
    let ``UTEL-06.8 correction contract is closed bounded and roundtrips exact assignment and predecessor`` () =
        let assignment feature item attempt : TelemetryCi.Assignment =
            { FeatureId = feature; ItemId = item; AttemptId = attempt; ParentAttemptId = None; ProducerStream = "routine-delivery" }
        let request : TelemetryCi.CorrectionRequest =
            { CorrectionId = "repair-one"; ExpectedPredecessor = None; Repository = "o/r"; PullRequest = 7L
              BaseRef = "main"; BaseSha = String.replicate 40 "d"; Head = String.replicate 40 "a"; MergeCommit = String.replicate 40 "b"
              Prior = assignment "wrong" "wrong-item" "wrong-attempt"; Effective = assignment "genuine" "genuine-item" "genuine-attempt"
              EvidenceSha256 = String.replicate 64 "e"; Reason = "source proof"; OperatorSource = "private-proof"; ObservedAt = "2026-10-06T12:00:00Z" }
        let plan : TelemetryCi.CorrectionPlan =
            { StoreId = String.replicate 32 "f"; Request = request
              Targets = [ { Table = "native_item_outcomes"; Identity = "native-one"; Revision = 1L; Digest = String.replicate 64 "c" } ] }
        let json = TelemetryCi.correctionPlanJson plan
        Assert.Equal(Ok plan, TelemetryCi.parseCorrectionPlan (Encoding.UTF8.GetBytes json))
        for invalid in
            [ json.Replace("\"storeId\":", "\"extra\":true,\"storeId\":")
              json.Replace("\"storeId\":", "\"storeId\":\"duplicate\",\"storeId\":")
              json.Replace("native_item_outcomes", "usage_observations")
              json.Replace("genuine-item", "../unsafe")
              json.Replace("ci-correction-plan/1", "ci-correction-plan/2")
              json.Replace("ci-correction-request/1", "ci-correction-request/2")
              json.Replace("\"prior\":", "\"prior\":null,\"prior\":") ] do
            Assert.True(TelemetryCi.parseCorrectionPlan (Encoding.UTF8.GetBytes invalid) |> Result.isError)
