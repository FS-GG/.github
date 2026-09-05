namespace FS.GG.Coord.GitHub.Tests

open System.Text.Json
open Xunit
open FS.GG.Coord
open FS.GG.Coord.GitHub

module QualificationEvidenceTests =
    let private head = System.String('a', 40)
    [<Fact>]
    let ``#3209 run job and check observations retain exact subject and terminal state`` () =
        let observation =
            QualificationEvidence.observeHosted
                { Complete = true
                  Items =
                    [ { Scope = QualificationEvidence.WorkflowRun; Id = "10"; HeadSha = head; State = QualificationEvidence.Completed "success" }
                      { Scope = QualificationEvidence.Job; Id = "20"; HeadSha = head; State = QualificationEvidence.InProgress }
                      { Scope = QualificationEvidence.CheckRun; Id = "30"; HeadSha = System.String('b', 40); State = QualificationEvidence.Queued } ] }
        Assert.True(observation.Complete)
        Assert.Equal<string list>([ "run"; "job"; "check" ], observation.Checks |> List.map _.Scope)
        Assert.Equal("completed", observation.Checks[0].State)
        Assert.Equal("in_progress", observation.Checks[1].State)
        Assert.Equal(System.String('b', 40), observation.Checks[2].SubjectRevision)

    [<Fact>]
    let ``#3209 renders the existing delivery obligation grammar exactly`` () =
        let none = QualificationEvidence.renderObligationComment head Qualification.NoObligations
        Assert.Equal($"<!-- fsgg:delivery-obligations none head=%s{head} -->\n", none)
        let some =
            QualificationEvidence.renderObligationComment
                head
                (Qualification.Obligation { Id = "coord-0.82.0"; Kind = "package-release" })
        Assert.Equal($"<!-- fsgg:delivery-obligation id=coord-0.82.0 kind=package-release head=%s{head} -->\n", some)

    [<Fact>]
    let ``#3209 wrong typed hosted snapshot returns typed error`` () =
        let bytes = System.Text.Encoding.UTF8.GetBytes("{\"schema\":\"fsgg.qualification.hosted-observation/1\",\"complete\":1,\"items\":[]}")
        match QualificationEvidence.parseHostedSnapshot bytes with
        | Error errors -> Assert.Contains("invalid hosted observation JSON", String.concat ";" errors)
        | Ok _ -> failwith "expected typed hosted parse refusal"

    [<Fact>]
    let ``#3209 obligation readback binds exact GitHub comment identity`` () =
        let bytes =
            JsonSerializer.SerializeToUtf8Bytes
                {| schema = QualificationEvidence.ObligationReadbackSchema
                   commentId = 7L
                   url = "https://github.com/FS-GG/.github/pull/1#issuecomment-8"
                   author = "github-actions[bot]"
                   body = QualificationEvidence.renderObligationComment head Qualification.NoObligations |}
        match QualificationEvidence.parseObligationReadback bytes with
        | Error errors -> Assert.Contains("exact GitHub PR issuecomment URL", String.concat ";" errors)
        | Ok _ -> failwith "expected comment identity mismatch refusal"

    [<Fact>]
    let ``#3209 authoritative readback preserves the existing delivery marker body`` () =
        let body = QualificationEvidence.renderObligationComment head Qualification.NoObligations
        let bytes =
            JsonSerializer.SerializeToUtf8Bytes
                {| schema = QualificationEvidence.ObligationReadbackSchema
                   commentId = 7L
                   url = "https://github.com/FS-GG/.github/pull/1#issuecomment-7"
                   author = "github-actions[bot]"
                   body = body |}
        let observed = QualificationEvidence.parseObligationReadback bytes |> Result.defaultWith (String.concat ";" >> failwith)
        Assert.Equal(body, observed.Body)

    [<Fact>]
    let ``#3209 obligation readback refuses an unknown schema`` () =
        let bytes =
            JsonSerializer.SerializeToUtf8Bytes
                {| schema = "fsgg.qualification.obligation-readback/999"
                   commentId = 7L
                   url = "https://github.com/FS-GG/.github/pull/1#issuecomment-7"
                   author = "github-actions[bot]"
                   body = QualificationEvidence.renderObligationComment head Qualification.NoObligations |}
        match QualificationEvidence.parseObligationReadback bytes with
        | Error errors -> Assert.Contains("readback schema", String.concat ";" errors)
        | Ok _ -> failwith "expected schema refusal"

    [<Fact>]
    let ``#3209 obligation readback wrong typed body returns a typed error`` () =
        let bytes =
            System.Text.Encoding.UTF8.GetBytes
                "{\"schema\":\"fsgg.qualification.obligation-readback/1\",\"commentId\":7,\"url\":\"https://github.com/FS-GG/.github/pull/1#issuecomment-7\",\"author\":\"bot\",\"body\":1}"
        match QualificationEvidence.parseObligationReadback bytes with
        | Error errors -> Assert.Contains("invalid obligation readback JSON", String.concat ";" errors)
        | Ok _ -> failwith "expected typed body refusal"
