namespace FS.GG.Org.SkillRegistry.Tests

open System
open FS.GG.Org.SkillRegistry
open Xunit

module PopulationTests =
    let private digest = String.replicate 64 "a"
    let private row = { Id = "core"; Owner = "fs-gg-game"; Source = "FS.GG.Game/skills/core/SKILL.md"; Sha256 = digest }
    let private entry = { Id = "core"; SuppliedBy = Some "skills/core/"; Sha256 = digest }
    let private checkout = { Repo = "FS.GG.Game"; Manifest = Parsed [ entry ] }
    let private good = { Rostered = Ok [ "FS.GG.Game" ]; Checkouts = [ checkout ]; Rows = [ row ] }
    let private codes input = Population.inspect input |> List.map (fun item -> item.Code)
    let private has code input = Assert.Contains(code, codes input)

    [<Fact>]
    let ``matching population and source identity is clean`` () =
        Assert.Empty(Population.inspect good)

    [<Fact>]
    let ``missing rostered checkout refuses`` () =
        has "roster-unreachable" { good with Rostered = Ok [ "FS.GG.Game"; "FS.GG.Templates" ] }

    [<Fact>]
    let ``unreadable roster cannot become an empty clean population`` () =
        has "roster-unreadable" { Rostered = Error "missing file"; Checkouts = []; Rows = [] }

    [<Fact>]
    let ``readable empty roster cannot qualify an empty population`` () =
        has "roster-empty" { Rostered = Ok []; Checkouts = []; Rows = [] }

    [<Fact>]
    let ``named producer with no checkout refuses`` () =
        has "manifest-unreachable" { good with Checkouts = [] }

    [<Fact>]
    let ``named producer without manifest refuses`` () =
        has "manifest-missing" { good with Checkouts = [ { checkout with Manifest = Absent } ] }

    [<Fact>]
    let ``unreadable named manifest refuses`` () =
        has "manifest-unreadable" { good with Checkouts = [ { checkout with Manifest = Unreadable "bad json" } ] }

    [<Fact>]
    let ``readable empty named manifest cannot hide a row`` () =
        has "row-undeclared" { good with Checkouts = [ { checkout with Manifest = Parsed [] } ] }

    [<Fact>]
    let ``reachable nonproducer without manifest is allowed`` () =
        let input = { good with Rostered = Ok [ "FS.GG.Game"; "FS.GG.Audio" ]; Checkouts = { Repo = "FS.GG.Audio"; Manifest = Absent } :: good.Checkouts }
        Assert.Empty(Population.inspect input)

    [<Fact>]
    let ``unnamed discovered producer cannot hide a skill`` () =
        let extra = { Repo = "FS.GG.Templates"; Manifest = Parsed [ { entry with Id = "template" } ] }
        has "missing-row" { good with Checkouts = extra :: good.Checkouts }

    [<Fact>]
    let ``two producers cannot infer ownership of an absent row`` () =
        let extra = { Repo = "FS.GG.Rendering"; Manifest = Parsed [ entry ] }
        has "missing-ambiguous" { good with Checkouts = extra :: good.Checkouts; Rows = [] }

    [<Fact>]
    let ``wrong registry owner refuses`` () =
        let input = { good with Rows = [ { row with Owner = "fs-gg-rendering" } ] }
        Assert.Contains("source-owner", codes input)
        Assert.Contains("owner-unmatched", codes input)

    [<Fact>]
    let ``source traversal refuses before filesystem access`` () =
        has "source-path" { good with Rows = [ { row with Source = "FS.GG.Game/../elsewhere/SKILL.md" } ] }

    [<Fact>]
    let ``absolute source refuses`` () =
        has "source-path" { good with Rows = [ { row with Source = "/FS.GG.Game/skills/core/SKILL.md" } ] }

    [<Fact>]
    let ``backslash source refuses`` () =
        has "source-path" { good with Rows = [ { row with Source = "FS.GG.Game\\skills\\core\\SKILL.md" } ] }

    [<Fact>]
    let ``wrong contained source refuses identity`` () =
        has "source-identity" { good with Rows = [ { row with Source = "FS.GG.Game/skills/other/SKILL.md" } ] }

    [<Fact>]
    let ``traversing supplied-by refuses`` () =
        let bad = { entry with SuppliedBy = Some "skills/../other" }
        has "supplied-by-path" { good with Checkouts = [ { checkout with Manifest = Parsed [ bad ] } ] }

    [<Fact>]
    let ``stale registry digest refuses`` () =
        has "manifest-digest-mismatch" { good with Rows = [ { row with Sha256 = String.replicate 64 "b" } ] }

    [<Fact>]
    let ``uppercase digest refuses`` () =
        has "row-digest" { good with Rows = [ { row with Sha256 = digest.ToUpperInvariant() } ] }

    [<Fact>]
    let ``duplicate registry identity refuses`` () =
        has "registry-duplicate" { good with Rows = [ row; row ] }

    [<Fact>]
    let ``duplicate manifest identity refuses`` () =
        has "manifest-duplicate" { good with Checkouts = [ { checkout with Manifest = Parsed [ entry; entry ] } ] }

    [<Fact>]
    let ``SDD manifest lacking supplied-by can still match a declared row`` () =
        let sddRow = { row with Owner = "fs-gg-sdd"; Source = "FS.GG.SDD/.claude/skills/core/SKILL.md" }
        let sdd = { Repo = "FS.GG.SDD"; Manifest = Parsed [ { entry with SuppliedBy = None } ] }
        Assert.Empty(Population.inspect { Rostered = Ok [ "FS.GG.SDD" ]; Checkouts = [ sdd ]; Rows = [ sddRow ] })

    [<Fact>]
    let ``dot github owner is preserved`` () =
        let driverRow = { row with Owner = ".github"; Source = ".github/.claude/skills/core/SKILL.md" }
        let driver = { Repo = ".github"; Manifest = Parsed [ { entry with SuppliedBy = Some ".claude/skills/core" } ] }
        Assert.Empty(Population.inspect { Rostered = Ok [ ".github" ]; Checkouts = [ driver ]; Rows = [ driverRow ] })

    [<Fact>]
    let ``shared id uses registry owner to choose authoritative declaration`` () =
        let mirror = { Repo = "FS.GG.Rendering"; Manifest = Parsed [ { entry with Sha256 = String.replicate 64 "b" } ] }
        Assert.Empty(Population.inspect { good with Checkouts = mirror :: good.Checkouts })
