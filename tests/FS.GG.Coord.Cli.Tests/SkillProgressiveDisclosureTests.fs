namespace FS.GG.Coord.Cli.Tests

open System
open System.IO
open System.Text.RegularExpressions
open Xunit

module SkillProgressiveDisclosureTests =

    let private repoRoot =
        let rec up (d: DirectoryInfo) =
            if isNull (box d) then
                failwith "repository root not found"
            elif Directory.Exists(Path.Combine(d.FullName, ".claude", "skills")) then
                d.FullName
            else
                up d.Parent

        up (DirectoryInfo AppContext.BaseDirectory)

    let private skills =
        [
            "check-board"
            "pnext-item"
            "intra-repo-parallel-work"
            "cross-repo-coordination"
            "drive-board"
            "work-board"
            "work-roadmap"
        ]

    let private skillPath root skill =
        Path.Combine(repoRoot, root, "skills", skill)

    let private files root skill =
        let dir = skillPath root skill

        Directory.GetFiles(dir, "*", SearchOption.AllDirectories)
        |> Array.map (fun path -> Path.GetRelativePath(dir, path).Replace('\\', '/'), File.ReadAllText path)
        |> Map.ofArray

    let private body skill =
        File.ReadAllText(Path.Combine(skillPath ".claude" skill, "SKILL.md"))

    [<Fact>]
    let ``triggered skill bodies stay below the progressive-disclosure budget`` () =
        for skill in skills do
            let body = File.ReadAllText(Path.Combine(skillPath ".claude" skill, "SKILL.md"))
            let lines = body.Split('\n').Length

            let words =
                body.Split([| ' '; '\t'; '\r'; '\n' |], StringSplitOptions.RemoveEmptyEntries).Length

            Assert.True(lines < 500, $"{skill}/SKILL.md has {lines} lines; the hard limit is below 500")
            Assert.True(words < 5000, $"{skill}/SKILL.md has {words} words; the triggered-body target is below 5000")
            if skill = "drive-board" then
                // The finite organization wrapper loads the common coordinator rather than
                // duplicating its routed instructions in a second triggered body.
                Assert.Contains("[$work-board](../work-board/SKILL.md)", body)
                Assert.Contains("scope only", body)
            else
                Assert.Contains("references/", body)

    [<Fact>]
    let ``all active roots carry the same routed skill directories`` () =
        for skill in skills do
            let canonical = files ".claude" skill
            Assert.Equal<Map<string, string>>(canonical, files ".agents" skill)

    [<Fact>]
    let ``representative tasks route to focused references instead of loading one monolith`` () =
        Assert.Contains("mechanical-reconciliation", body "check-board")
        Assert.Contains("judgement-findings", body "check-board")
        Assert.Contains("strict-item-lifecycle", body "pnext-item")
        let strictItemLifecycle =
            File.ReadAllText(Path.Combine(skillPath ".claude" "pnext-item", "references", "strict-item-lifecycle.md"))
        Assert.Contains("command-contracts", strictItemLifecycle)
        Assert.Contains("findings-and-filing", strictItemLifecycle)
        Assert.Contains("merge-and-release", strictItemLifecycle)
        Assert.Contains("mailbox-and-board", body "cross-repo-coordination")
        Assert.Contains("contract-changes", body "cross-repo-coordination")
        Assert.Contains("coherent-releases", body "cross-repo-coordination")
        let coordinator = body "work-board"
        Assert.Contains("references/lifecycle-contract.md", coordinator)
        Assert.Contains("references/intake.md", coordinator)
        Assert.Contains(".agents/skills/pnext-item/SKILL.md", coordinator)
        let contract =
            File.ReadAllText(Path.Combine(skillPath ".claude" "work-board", "references", "lifecycle-contract.md"))
        for route in
            [ "org-scope.md"
              "workspace-scope.md"
              "bootstrap.md"
              "delivery-reliability.md"
              "observation.md" ] do
            Assert.Contains(route, contract)
        Assert.Contains("../work-board/SKILL.md", body "drive-board")
        Assert.DoesNotContain("$work-programme", coordinator)
        Assert.Contains("feedback-contract", body "work-roadmap")

        Assert.Contains("heavyweight-roadmap", body "work-roadmap")
        let heavyweightRoadmap =
            File.ReadAllText(Path.Combine(skillPath ".claude" "work-roadmap", "references", "heavyweight-roadmap.md"))
        Assert.Contains("host-loop", heavyweightRoadmap)

    [<Fact>]
    let ``generated protocol facts live in routed references not triggered bodies`` () =
        for skill in
            [
                "check-board"
                "pnext-item"
                "intra-repo-parallel-work"
                "cross-repo-coordination"
            ] do
            let body = body skill
            Assert.DoesNotContain("BEGIN GENERATED:", body)

        for root in [ ".claude"; ".agents" ] do
            let reference skill name =
                File.ReadAllText(Path.Combine(skillPath root skill, "references", name))

            // Organization V1 projections are retired in both runtimes. Their
            // absence cannot be established by dropping the retained shared/product facts.
            Assert.DoesNotContain("fsgg-protocol:reconcile-rules", reference "check-board" "mechanical-reconciliation.md")
            Assert.DoesNotContain("fsgg-protocol:blocker-states", reference "check-board" "mechanical-reconciliation.md")
            Assert.DoesNotContain("fsgg-protocol:snapshot-shape", reference "check-board" "mechanical-reconciliation.md")
            Assert.DoesNotContain("fsgg-protocol:filing-rules", reference "cross-repo-coordination" "mailbox-and-board.md")
            Assert.DoesNotContain("fsgg-protocol:board-statuses", reference "cross-repo-coordination" "mailbox-and-board.md")
            Assert.DoesNotContain("fsgg-protocol:wave-policy", reference "drive-board" "host-loop.md")
            Assert.Contains("fsgg-protocol:take-exit-codes", reference "pnext-item" "command-contracts.md")
            Assert.Contains("BEGIN GENERATED: fsgg-protocol -->", reference "intra-repo-parallel-work" "protocol-facts.md")
            Assert.DoesNotContain("fsgg-protocol:wave-policy", reference "work-board" "host-loop.md")
            for host in [ "drive-board"; "work-board" ] do
                Assert.DoesNotContain("fsgg:wave-model:v1", reference host "host-loop.md")
                Assert.Contains("Canonical work-board owns coordination", reference host "host-loop.md")

    [<Fact>]
    let ``moving detail into references does not break relative markdown links`` () =
        let relativeMarkdown = Regex(@"\]\((\.\./[^)#]+\.md)(?:#[^)]+)?\)")

        for skill in skills do
            let dir = skillPath ".claude" skill

            for file in Directory.GetFiles(dir, "*.md", SearchOption.AllDirectories) do
                let text = File.ReadAllText file

                for hit in relativeMarkdown.Matches text do
                    let target = Path.GetFullPath(hit.Groups.[1].Value, Path.GetDirectoryName file)
                    // `fs-gg-sdd-lifecycle` is supplied by the SDD consumer rather than authored in this
                    // repository; every repo-local route must resolve in the source tree.
                    let externallyMaterialized =
                        target.Contains(
                            $"{Path.DirectorySeparatorChar}fs-gg-sdd-lifecycle{Path.DirectorySeparatorChar}"
                        )

                    Assert.True(
                        externallyMaterialized || File.Exists target,
                        $"{Path.GetRelativePath(repoRoot, file)} links to missing {target}"
                    )
