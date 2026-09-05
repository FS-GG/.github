namespace FS.GG.Coord.Cli.BoardOps.Tests

open System
open System.IO
open System.Text
open Xunit
open FS.GG.Coord
open FS.GG.Coord.Cli

module RoadmapWorkUnitTransactionTests =
    let private unwrap = function Ok value -> value | Error values -> failwithf "%A" values
    let private makePlan () =
        let obligations = [ "sdd:analyze"; "sdd:verify"; "sdd:ship"; "qualification"; "lifecycle"; "review" ]
        let previous: RoadmapWorkUnit.CatalogRow = { UnitId = "GS2-07.2"; Title = "previous"; State = RoadmapWorkUnit.Accepted; Prerequisite = Some "GS2-07.1"; Gates = []; EvidenceObligations = obligations; ContractSha256 = String.replicate 64 "e" }
        let selected: RoadmapWorkUnit.CatalogRow = { UnitId = "GS2-07.3"; Title = "compiler"; State = RoadmapWorkUnit.Unchecked; Prerequisite = Some previous.UnitId; Gates = [ "implementation"; "acceptance" ]; EvidenceObligations = obligations; ContractSha256 = String.replicate 64 "f" }
        ({ Schema = RoadmapWorkUnit.PreparationInputSchema
           RoadmapSourceDigest = "sha256:" + String.replicate 64 "a"
           CatalogSourceDigest = "sha256:" + String.replicate 64 "b"
           Catalog = [ previous; selected ]
           RoadmapRow = { UnitId = selected.UnitId; Title = selected.Title; Prerequisite = selected.Prerequisite; Gates = selected.Gates }
           AuthorityIssue = "https://github.com/FS-GG/.github/issues/3210"
           SddWorkId = "3210-roadmap-work-unit-compiler"
           RegistrationOwner = "FS-GG"
           RegistrationRepository = ".github"
           RegistrationPaths = [ "src/FS.GG.Coord.Core" ] } : RoadmapWorkUnit.PreparationInput)
        |> RoadmapWorkUnit.inspectPreparation |> unwrap

    [<Fact>]
    let ``#3210 compiler registrations are byte-stable inputs to the sole staged-intake transaction`` () =
        let plan = makePlan ()
        let identities = plan.Registrations |> List.map (fun registration -> registration.Id, IntakeReceipt.digest registration.Draft)
        let replay = makePlan () |> _.Registrations |> List.map (fun registration -> registration.Id, IntakeReceipt.digest registration.Draft)
        Assert.Equal<(string * string) list>(identities, replay)
        for registration in plan.Registrations do
            let path = Path.Combine(Path.GetTempPath(), "fsgg-3210-intake-" + Guid.NewGuid().ToString("n") + ".json")
            try
                File.WriteAllText(path, RoadmapWorkUnit.canonicalIntakeDraft registration, UTF8Encoding(false))
                let decoded = IntakeApplication.readDraft path |> unwrap
                Assert.Equal(registration.Draft, decoded)
                Assert.Equal(IntakeReceipt.digest registration.Draft, IntakeReceipt.digest decoded)
            finally File.Delete path
