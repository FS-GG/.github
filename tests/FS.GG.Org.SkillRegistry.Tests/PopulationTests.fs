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
    let ``case colliding roster and checkout names refuse before map reduction`` () =
        let alias = { checkout with Repo = "fs.gg.game" }
        let input = { good with Rostered = Ok [ "FS.GG.Game"; "fs.gg.game" ]; Checkouts = [ checkout; alias ] }
        has "roster-case-collision" input
        has "checkout-case-collision" input

    [<Fact>]
    let ``malformed roster name cannot qualify a matching checkout`` () =
        let foreign = { Repo = "EHotwagner/S.I.R."; Manifest = Absent }
        has "roster-name" { Rostered = Ok [ foreign.Repo ]; Checkouts = [ foreign ]; Rows = [] }

    [<Fact>]
    let ``malformed checkout name cannot qualify an absent manifest`` () =
        let malformed = { Repo = "../FS.GG.Game"; Manifest = Absent }
        has "checkout-name" { good with Checkouts = malformed :: good.Checkouts }

    [<Fact>]
    let ``distinct repositories cannot collapse to one owner with unbound manifest sources`` () =
        let source = { checkout with Manifest = Parsed [ { entry with SuppliedBy = None } ] }
        let alias = { Repo = "FS-GG-Game"; Manifest = Parsed [ { entry with SuppliedBy = None } ] }
        let input = { good with Rostered = Ok [ source.Repo; alias.Repo ]; Checkouts = [ source; alias ] }
        has "roster-owner-collision" input
        has "checkout-owner-collision" input

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
    let ``non SDD manifest cannot omit source binding`` () =
        let unbound = { entry with SuppliedBy = None }
        let input = { good with Checkouts = [ { checkout with Manifest = Parsed [ unbound ] } ]; Rows = [ { row with Source = "FS.GG.Game/skills/other/SKILL.md" } ] }
        has "supplied-by-missing" input

    [<Fact>]
    let ``reordering population facts does not change findings`` () =
        let extra = { Repo = "FS.GG.Rendering"; Manifest = Parsed [ { entry with Id = "other" } ] }
        let input = { good with Rostered = Ok [ checkout.Repo; extra.Repo ]; Checkouts = [ checkout; extra ]; Rows = [ { row with Sha256 = String.replicate 64 "b" }; { row with Id = "orphan"; Source = "FS.GG.Game/skills/orphan/SKILL.md" } ] }
        let reversed = { input with Rostered = Ok [ extra.Repo; checkout.Repo ]; Checkouts = [ extra; checkout ]; Rows = List.rev input.Rows }
        Assert.Equal<PopulationFinding list>(Population.inspect input, Population.inspect reversed)

    [<Fact>]
    let ``duplicate row order cannot change secondary findings`` () =
        let stale = { row with Sha256 = String.replicate 64 "b" }
        let input = { good with Rows = [ row; stale ] }
        Assert.Equal<PopulationFinding list>(Population.inspect input, Population.inspect { input with Rows = List.rev input.Rows })

    [<Fact>]
    let ``duplicate checkout order cannot change secondary findings`` () =
        let absent = { checkout with Manifest = Absent }
        let input = { good with Checkouts = [ checkout; absent ] }
        Assert.Equal<PopulationFinding list>(Population.inspect input, Population.inspect { input with Checkouts = List.rev input.Checkouts })

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
    let ``uppercase alias id cannot qualify a matching row and manifest`` () =
        let input = {
            good with
                Rows = [ { row with Id = "Core" } ]
                Checkouts = [ { checkout with Manifest = Parsed [ { entry with Id = "Core" } ] } ]
        }
        has "row-id" input
        has "manifest-id" input

    [<Fact>]
    let ``trailing newline cannot be hidden by regex end anchor`` () =
        let input = {
            Rostered = Ok [ "FS.GG.Game\n" ]
            Checkouts = [ { checkout with Repo = "FS.GG.Game\n"; Manifest = Parsed [ { entry with Id = "core\n"; Sha256 = digest + "\n" } ] } ]
            Rows = [ { row with Id = "core\n"; Source = "FS.GG.Game\n/skills/core/SKILL.md"; Sha256 = digest + "\n" } ]
        }
        has "roster-name" input
        has "checkout-name" input
        has "row-id" input
        has "manifest-id" input
        has "row-digest" input
        has "manifest-digest" input

    [<Fact>]
    let ``control character in matching source paths refuses`` () =
        let suppliedBy = "skills/\u0000core"
        let input = {
            good with
                Rows = [ { row with Source = "FS.GG.Game/" + suppliedBy + "/SKILL.md" } ]
                Checkouts = [ { checkout with Manifest = Parsed [ { entry with SuppliedBy = Some suppliedBy } ] } ]
        }
        has "source-path" input
        has "supplied-by-path" input

    [<Fact>]
    let ``null supplied-by fact refuses without throwing`` () =
        let input = { good with Checkouts = [ { checkout with Manifest = Parsed [ { entry with SuppliedBy = Some null } ] } ] }
        has "supplied-by-path" input

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
