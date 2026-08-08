namespace FS.GG.Coord.Core.Tests

open Xunit
open FS.GG.Coord

module IntakeTests =
    let private draft: Intake.Draft =
        { Schema = Intake.Schema; Id = "intake-42"; Owner = "FS-GG"; Repository = ".github"
          Title = "title"; Observed = "observed"; RootCause = "cause"; Acceptance = "acceptance"
          Verification = "verification"; Paths = [ "src/FS.GG.Coord.Core" ]; Class = "hardening"
          Status = "Backlog"; Disposition = Some Intake.Create; Phase = None; Severity = None
          BlockedBy = None; BlockedOn = None; BacklogReason = Some "not-yet-actionable"; JudgementQuestion = None }

    [<Fact>]
    let ``#2134 intake draft refuses an unknown schema before IO`` () =
        match Intake.validate { draft with Schema = "fsgg.coord.intake/v0" } with
        | Error findings -> Assert.Contains(findings, fun finding -> finding.Field = "schema")
        | Ok _ -> failwith "an unsupported schema must refuse"

    [<Fact>]
    let ``#2134 intake draft refuses blank paths before IO`` () =
        match Intake.validate { draft with Paths = [ "" ] } with
        | Error findings -> Assert.Contains(findings, fun finding -> finding.Field = "paths")
        | Ok _ -> failwith "an empty path must refuse"

    [<Fact>]
    let ``#2134 intake draft refuses an implicit duplicate disposition`` () =
        match Intake.validate { draft with Disposition = None } with
        | Error findings -> Assert.Contains(findings, fun finding -> finding.Field = "disposition")
        | Ok _ -> failwith "apply must not infer create or reuse"

    [<Fact>]
    let ``#2134 intake draft refuses paths that escape the repository`` () =
        match Intake.validate { draft with Paths = [ "../outside" ] } with
        | Error findings -> Assert.Contains(findings, fun finding -> finding.Field = "paths")
        | Ok _ -> failwith "a repository escape must refuse"

    [<Fact>]
    let ``#2134 receipt cannot turn a different draft into a retry`` () =
        let receipt: IntakeReceipt.Receipt = { DraftId = "other"; Owner = "FS-GG"; Repository = ".github"; IssueNumber = 42; DraftDigest = "wrong" }
        Assert.True(IntakeReceipt.validate draft receipt |> Result.isError)

    [<Fact>]
    let ``#2134 Backlog requires an explicit reason code`` () =
        match Intake.validate { draft with BacklogReason = None } with
        | Error findings -> Assert.Contains(findings, fun finding -> finding.Field = "backlogReason")
        | Ok _ -> failwith "Backlog without a reason must refuse"

    [<Fact>]
    let ``#2134 Blocked requires a dependency or human park`` () =
        match Intake.validate { draft with Status = "Blocked"; BacklogReason = None } with
        | Error findings -> Assert.Contains(findings, fun finding -> finding.Field = "blockedBy")
        | Ok _ -> failwith "incoherent Blocked must refuse"

    [<Fact>]
    let ``#2134 Blocked refuses a noncanonical dependency token`` () =
        match Intake.validate { draft with Status = "Blocked"; BacklogReason = None; BlockedBy = Some "x#y" } with
        | Error findings -> Assert.Contains(findings, fun finding -> finding.Field = "blockedBy")
        | Ok _ -> failwith "x#y is not a canonical dependency"

    [<Fact>]
    let ``#2134 Ready refuses unresolved judgement`` () =
        match Intake.validate { draft with Status = "Ready"; BacklogReason = None; JudgementQuestion = Some "owner?" } with
        | Error findings -> Assert.Contains(findings, fun finding -> finding.Field = "status")
        | Ok _ -> failwith "Ready with judgement must refuse"
