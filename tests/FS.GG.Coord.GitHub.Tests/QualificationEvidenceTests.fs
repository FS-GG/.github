namespace FS.GG.Coord.GitHub.Tests

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
    let ``#3209 typed no-obligations comment round trips at exact head`` () =
        let body = QualificationEvidence.renderObligationComment head Qualification.NoObligations
        let observed = QualificationEvidence.readObligationComments head [ "ordinary prose"; body ] |> Result.defaultWith (String.concat ";" >> failwith)
        Assert.Equal(head, observed.HeadSha)
        Assert.Equal<Qualification.ObligationDeclaration list>([ Qualification.NoObligations ], observed.Declarations)

    [<Fact>]
    let ``#3209 duplicate current-head declarations remain visible for fail-closed validation`` () =
        let first = QualificationEvidence.renderObligationComment head (Qualification.Obligations [ "publish" ])
        let second = QualificationEvidence.renderObligationComment head Qualification.NoObligations
        let observed = QualificationEvidence.readObligationComments head [ first; second ] |> Result.defaultWith (String.concat ";" >> failwith)
        Assert.Equal(2, observed.Declarations.Length)

    [<Fact>]
    let ``#3209 stale malformed and unknown-field obligation comments do not pass as current`` () =
        let stale = QualificationEvidence.renderObligationComment (System.String('b', 40)) Qualification.NoObligations
        let observed = QualificationEvidence.readObligationComments head [ stale ] |> Result.defaultWith (String.concat ";" >> failwith)
        Assert.False(observed.HeadSha = head)
        let malformed = stale.Replace("\"ids\":[]", "\"ids\":[],\"unknown\":true")
        match QualificationEvidence.readObligationComments head [ malformed ] with
        | Error errors -> Assert.Contains("unknown fields", String.concat ";" errors)
        | Ok _ -> failwith "expected malformed obligation refusal"
