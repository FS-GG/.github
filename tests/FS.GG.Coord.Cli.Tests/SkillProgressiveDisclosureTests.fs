namespace FS.GG.Coord.Cli.Tests

open System
open System.IO
open System.Text.RegularExpressions
open Xunit

module SkillProgressiveDisclosureTests =

    let private repoRoot =
        let rec up (d: DirectoryInfo) =
            if isNull (box d) then failwith "repository root not found"
            elif Directory.Exists(Path.Combine(d.FullName, ".claude", "skills")) then d.FullName
            else up d.Parent

        up (DirectoryInfo AppContext.BaseDirectory)

    let private skills =
        [ "check-board"
          "pnext-item"
          "intra-repo-parallel-work"
          "cross-repo-coordination"
          "drive-board"
          "work-board"
          "work-roadmap" ]

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
            let words = body.Split([| ' '; '\t'; '\r'; '\n' |], StringSplitOptions.RemoveEmptyEntries).Length

            Assert.True(lines < 500, $"{skill}/SKILL.md has {lines} lines; the hard limit is below 500")
            Assert.True(words < 5000, $"{skill}/SKILL.md has {words} words; the triggered-body target is below 5000")
            Assert.Contains("references/", body)

    [<Fact>]
    let ``all active roots carry the same routed skill directories`` () =
        for skill in skills do
            let canonical = files ".claude" skill
            Assert.Equal<Map<string, string>>(canonical, files ".agents" skill)
            Assert.Equal<Map<string, string>>(canonical, files ".codex" skill)

    [<Fact>]
    let ``representative tasks route to focused references instead of loading one monolith`` () =
        Assert.Contains("mechanical-reconciliation", body "check-board")
        Assert.Contains("judgement-findings", body "check-board")
        Assert.Contains("command-contracts", body "pnext-item")
        Assert.Contains("findings-and-filing", body "pnext-item")
        Assert.Contains("merge-and-release", body "pnext-item")
        Assert.Contains("mailbox-and-board", body "cross-repo-coordination")
        Assert.Contains("contract-changes", body "cross-repo-coordination")
        Assert.Contains("coherent-releases", body "cross-repo-coordination")

        for host in [ "drive-board"; "work-board"; "work-roadmap" ] do
            Assert.Contains("host-loop", body host)

    [<Fact>]
    let ``generated protocol facts live in routed references not triggered bodies`` () =
        for skill in [ "check-board"; "pnext-item"; "intra-repo-parallel-work"; "cross-repo-coordination" ] do
            let body = body skill
            Assert.DoesNotContain("BEGIN GENERATED:", body)

        let reference skill name =
            File.ReadAllText(Path.Combine(skillPath ".claude" skill, "references", name))

        Assert.Contains("fsgg-protocol:reconcile-rules", reference "check-board" "mechanical-reconciliation.md")
        Assert.Contains("fsgg-protocol:take-exit-codes", reference "pnext-item" "command-contracts.md")
        Assert.Contains("BEGIN GENERATED: fsgg-protocol -->", reference "intra-repo-parallel-work" "protocol-facts.md")
        Assert.Contains("fsgg-protocol:filing-rules", reference "cross-repo-coordination" "mailbox-and-board.md")

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
                    let externallyMaterialized = target.Contains($"{Path.DirectorySeparatorChar}fs-gg-sdd-lifecycle{Path.DirectorySeparatorChar}")

                    Assert.True(
                        externallyMaterialized || File.Exists target,
                        $"{Path.GetRelativePath(repoRoot, file)} links to missing {target}"
                    )
