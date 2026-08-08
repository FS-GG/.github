namespace FS.GG.Coord.Core.Tests

open Xunit
open FS.GG.Coord

module IntakeTests =
    let private draft: Intake.Draft =
        { Schema = Intake.Schema; Id = "intake-42"; Owner = "FS-GG"; Repository = ".github"
          Title = "title"; Observed = "observed"; RootCause = "cause"; Acceptance = "acceptance"
          Verification = "verification"; Paths = [ "src/FS.GG.Coord.Core" ]; Class = "hardening"
          Status = "Backlog"; Disposition = Some Intake.Create }

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
