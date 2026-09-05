namespace FS.GG.Coord.GitHub.Tests

open System.Text.Json
open Xunit
open FS.GG.Coord
open FS.GG.Coord.GitHub

module QualificationEvidenceTests =
    let private head = System.String('a', 40)
    let private comment id body : QualificationEvidence.ObligationComment =
        { CommentId = id
          Url = $"https://github.com/FS-GG/.github/pull/1#issuecomment-%d{id}"
          Author = "github-actions[bot]"
          Body = body }

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
    let ``#3209 typed no-obligations comment round trips at exact head`` () =
        let body = QualificationEvidence.renderObligationComment head Qualification.NoObligations
        let observed = QualificationEvidence.readObligationComments head [ comment 1L "ordinary prose"; comment 2L body ] |> Result.defaultWith (String.concat ";" >> failwith)
        Assert.Equal(head, observed.HeadSha)
        Assert.Equal<Qualification.ObligationDeclaration list>([ Qualification.NoObligations ], observed.Declarations)
        Assert.Equal(Some 2L, observed.Readback |> Option.map _.CommentId)

    [<Fact>]
    let ``#3209 duplicate current-head declarations remain visible for fail-closed validation`` () =
        let first = QualificationEvidence.renderObligationComment head (Qualification.Obligations [ "publish" ])
        let second = QualificationEvidence.renderObligationComment head Qualification.NoObligations
        let observed = QualificationEvidence.readObligationComments head [ comment 1L first; comment 2L second ] |> Result.defaultWith (String.concat ";" >> failwith)
        Assert.Equal(2, observed.Declarations.Length)
        Assert.True(observed.Readback.IsNone)

    [<Fact>]
    let ``#3209 stale malformed and unknown-field obligation comments do not pass as current`` () =
        let stale = QualificationEvidence.renderObligationComment (System.String('b', 40)) Qualification.NoObligations
        let observed = QualificationEvidence.readObligationComments head [ comment 1L stale ] |> Result.defaultWith (String.concat ";" >> failwith)
        Assert.False(observed.HeadSha = head)
        let malformed = stale.Replace("\"ids\":[]", "\"ids\":[],\"unknown\":true")
        match QualificationEvidence.readObligationComments head [ comment 2L malformed ] with
        | Error errors -> Assert.Contains("unknown fields", String.concat ";" errors)
        | Ok _ -> failwith "expected malformed obligation refusal"

    [<Fact>]
    let ``#3209 obligation inspection distinguishes guarded create from verified readback`` () =
        match QualificationEvidence.inspectObligationComments head Qualification.NoObligations [] with
        | Ok(QualificationEvidence.GuardedCreateIntent body) -> Assert.Contains(head, body)
        | other -> failwithf "expected guarded create intent, got %A" other
        let body = QualificationEvidence.renderObligationComment head Qualification.NoObligations
        match QualificationEvidence.inspectObligationComments head Qualification.NoObligations [ comment 7L body ] with
        | Ok(QualificationEvidence.VerifiedReadback observed) -> Assert.Equal(Some 7L, observed.Readback |> Option.map _.CommentId)
        | other -> failwithf "expected verified readback, got %A" other

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
