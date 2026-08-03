#load "release-train-lib.fsx"

open System
open System.IO
open System.Text.Json
open System.Text.RegularExpressions
open ReleaseTrain

type Package = {
    Project: string
    PackageId: string
    Version: string
    PackageReferences: string list
}

type ReleaseSet = {
    Id: string
    Workflow: string
    TagPatterns: string list
    ExpectedTags: string list
    BaselineTag: string
    BaselineCommit: string
    ChangedFiles: string list
    Packages: Package list
}

type RepositoryAudit = {
    Id: string
    FullName: string
    Path: string
    Exists: bool
    Dirty: bool
    Head: string
    OriginMain: string
    HeadEqualsOriginMain: bool
    BaselineTag: string
    BaselineCommit: string
    ChangedFiles: string list
    ReleaseWorkflows: string list
    Packages: Package list
    ReleaseSets: ReleaseSet list
    Findings: string list
}

type Audit = {
    SchemaVersion: int
    GeneratedAt: DateTimeOffset
    Root: string
    Repositories: RepositoryAudit list
}

let usage () =
    printfn "Usage: dotnet fsi scripts/release-train-audit.fsx [--root DIR] [--siblings-root DIR] [--repo ID] [--fetch] [--json] [--output FILE] [--selftest]"
    printfn "Exit: 0 = audit completed; 1 = findings; 3 = no verdict."

let msbuildProperties repo project =
    let result =
        runProcess repo "dotnet" [
            "msbuild"
            project
            "-nologo"
            "-getProperty:IsPackable"
            "-getProperty:PackageId"
            "-getProperty:Version"
            "-getProperty:PackageVersion"
        ]
    if result.ExitCode <> 0 then
        Error(result.StdErr + Environment.NewLine + result.StdOut)
    else
        try
            use doc = JsonDocument.Parse result.StdOut
            let props = doc.RootElement.GetProperty "Properties"
            let value (name: string) =
                let mutable found = Unchecked.defaultof<JsonElement>
                if props.TryGetProperty(name, &found) then found.GetString() else ""
            Ok(value "IsPackable", value "PackageId", value "PackageVersion", value "Version")
        with ex ->
            Error $"could not parse evaluated MSBuild properties for {project}: {ex.Message}"

let relative root path = Path.GetRelativePath(root, path).Replace('\\', '/')

let discoverPackages repo =
    [ ".fsproj"; ".csproj"; ".vbproj" ]
    |> Seq.collect (fun extension -> filesNamed extension repo)
    |> Seq.fold (fun (packages, findings) project ->
        let rel = relative repo project
        match msbuildProperties repo rel with
        | Error detail ->
            packages, $"MSBuild evaluation failed for {rel}: {detail.Trim()}" :: findings
        | Ok (packable, packageId, packageVersion, version) ->
            if String.Equals(packable, "true", StringComparison.OrdinalIgnoreCase) then
                let resolvedVersion =
                    if String.IsNullOrWhiteSpace packageVersion then version else packageVersion
                let id =
                    if String.IsNullOrWhiteSpace packageId then Path.GetFileNameWithoutExtension project
                    else packageId
                {
                    Project = rel
                    PackageId = id
                    Version = resolvedVersion
                    PackageReferences = xmlValues project "PackageReference"
                } :: packages,
                findings
            else packages, findings
    ) ([], [])
    |> fun (packages, findings) -> List.sortBy _.PackageId packages, List.rev findings

let git repo args = tryRun repo "git" args |> Option.defaultValue ""

let tagPatterns (workflowText: string) =
    Regex.Matches(workflowText, @"(?m)^\s*tags:\s*\[(?<items>[^\]]+)\]")
    |> Seq.cast<Match>
    |> Seq.collect (fun row ->
        Regex.Matches(row.Groups["items"].Value, """['"](?<tag>[^'"]+\*)['"]""")
        |> Seq.cast<Match>
        |> Seq.map (fun item -> item.Groups["tag"].Value))
    |> Seq.distinct
    |> Seq.toList

let releaseSetName (workflow: string) (patterns: string list) =
    let fromWorkflow =
        Path.GetFileNameWithoutExtension(workflow)
            .Replace("release-", "", StringComparison.OrdinalIgnoreCase)
    match patterns with
    | first :: _ when first <> "v*" -> first.Split('/')[0]
    | _ -> fromWorkflow

let discoverReleaseSets (repoId: string) (repo: string) (originMain: string) (packages: Package list) (workflows: string list) =
    let directlyMapped =
        workflows
        |> List.choose (fun workflow ->
            let full = Path.Combine(repo, workflow.Replace('/', Path.DirectorySeparatorChar))
            let text = File.ReadAllText full
            let patterns = tagPatterns text
            let members =
                packages
                |> List.filter (fun package -> text.Contains(package.Project, StringComparison.Ordinal))
            if patterns.IsEmpty || members.IsEmpty then None
            else Some(workflow, patterns, members))
    let candidates =
        match directlyMapped with
        | [ (workflow, patterns, _) ] when (packages |> List.map _.Version |> List.distinct |> List.length) = 1 ->
            [ (workflow, patterns, packages) ]
        | [ _ ] -> [] // Preserve the repository-level fallback when one workflow indirectly assembles mixed-version projects.
        | many -> many
    let membershipFindings =
        if candidates.Length <= 1 then []
        else
            packages
            |> List.choose (fun package ->
                let matches = candidates |> List.filter (fun (_, _, members) -> members |> List.exists (fun packageMember -> packageMember.Project = package.Project))
                match matches.Length with
                | 1 -> None
                | 0 -> Some $"packable project {package.Project} is not mapped to a tagged release workflow"
                | count -> Some $"packable project {package.Project} is mapped to {count} tagged release workflows")
    let sets, setFindings =
        candidates
        |> List.map (fun (workflow, patterns, members) ->
            let versions = members |> List.map _.Version |> List.distinct
            let versionFinding =
                if versions.Length = 1 then []
                else [ $"release workflow {workflow} groups packages with different evaluated versions" ]
            let expectedTags =
                match versions with
                | [ version ] -> patterns |> List.map (fun pattern -> pattern.Replace("*", version))
                | _ -> []
            let baselineTag =
                patterns
                |> List.tryPick (fun pattern ->
                    git repo [ "tag"; "--merged"; "origin/main"; "--sort=-creatordate"; "--list"; pattern ]
                    |> fun output -> output.Split('\n', StringSplitOptions.RemoveEmptyEntries) |> Array.tryHead)
                |> Option.defaultValue ""
            let baselineCommit = if baselineTag = "" then "" else git repo [ "rev-list"; "-n"; "1"; baselineTag ]
            let changedFiles =
                if baselineTag = "" then []
                else
                    git repo [ "diff"; "--name-only"; $"{baselineTag}..{originMain}" ]
                    |> fun output -> output.Split('\n', StringSplitOptions.RemoveEmptyEntries) |> Array.toList
            let id = $"{repoId}:{releaseSetName workflow patterns}"
            let renderedPatterns = String.concat ", " patterns
            let baselineFinding = if baselineTag = "" then [ $"release set {id} has no reachable tag matching {renderedPatterns}" ] else []
            {
                Id = id; Workflow = workflow; TagPatterns = patterns; ExpectedTags = expectedTags
                BaselineTag = baselineTag; BaselineCommit = baselineCommit; ChangedFiles = changedFiles
                Packages = members
            }, versionFinding @ baselineFinding)
        |> List.unzip
    sets, membershipFindings @ (setFindings |> List.collect id)

let auditRepo (fetch: bool) (authorityRoot: string) (parent: string) ((id, fullName): string * string) =
    let repoName = fullName.Split('/') |> Array.last
    let path = if id = ".github" then authorityRoot else Path.Combine(parent, repoName)
    if not (Directory.Exists(Path.Combine(path, ".git"))) && not (File.Exists(Path.Combine(path, ".git"))) then
        {
            Id = id; FullName = fullName; Path = path; Exists = false; Dirty = false
            Head = ""; OriginMain = ""; HeadEqualsOriginMain = false; BaselineTag = ""
            BaselineCommit = ""; ChangedFiles = []; ReleaseWorkflows = []; Packages = []
            ReleaseSets = []
            Findings = [ $"missing rostered sibling checkout: {path}" ]
        }
    else
        if fetch then
            runProcess path "git" [ "fetch"; "--quiet"; "origin"; "main"; "--tags" ]
            |> requireSuccess $"git fetch in {fullName}"
            |> ignore
        let head = git path [ "rev-parse"; "HEAD" ]
        let originMain = git path [ "rev-parse"; "origin/main" ]
        let dirty = git path [ "status"; "--porcelain" ] |> String.IsNullOrWhiteSpace |> not
        let baselineTag =
            git path [ "tag"; "--merged"; "origin/main"; "--sort=-creatordate" ]
            |> fun output -> output.Split('\n', StringSplitOptions.RemoveEmptyEntries) |> Array.tryHead
            |> Option.defaultValue ""
        let baselineCommit =
            if baselineTag = "" then "" else git path [ "rev-list"; "-n"; "1"; baselineTag ]
        let changedFiles =
            if baselineTag = "" then []
            else
                git path [ "diff"; "--name-only"; $"{baselineTag}..origin/main" ]
                |> fun output -> output.Split('\n', StringSplitOptions.RemoveEmptyEntries) |> Array.toList
        let workflows =
            let directory = Path.Combine(path, ".github", "workflows")
            if Directory.Exists directory then
                Directory.EnumerateFiles directory
                |> Seq.filter (fun file ->
                    let name = Path.GetFileName(file).ToLowerInvariant()
                    name.Contains "release" || name.Contains "publish")
                |> Seq.map (relative path)
                |> Seq.sort
                |> Seq.toList
            else []
        let packages, packageFindings = discoverPackages path
        let releaseSets, releaseSetFindings = discoverReleaseSets id path originMain packages workflows
        let findings =
            [
                if dirty then $"working tree is dirty: {path}"
                if originMain = "" then $"origin/main is unavailable: {path}"
                if head <> originMain then $"HEAD {head} does not equal origin/main {originMain}"
                if packages.IsEmpty then $"no evaluated packable projects discovered in {path}"
                if baselineTag = "" then $"no reachable tag found for baseline inspection in {path}"
            ] @ packageFindings @ releaseSetFindings
        {
            Id = id; FullName = fullName; Path = path; Exists = true; Dirty = dirty
            Head = head; OriginMain = originMain; HeadEqualsOriginMain = head = originMain
            BaselineTag = baselineTag; BaselineCommit = baselineCommit; ChangedFiles = changedFiles
            ReleaseWorkflows = workflows; Packages = packages; ReleaseSets = releaseSets; Findings = findings
        }

let markdown (audit: Audit) =
    printfn "# NuGet release audit"
    printfn ""
    printfn "Generated: %s" (audit.GeneratedAt.ToString "O")
    printfn ""
    printfn "| Repository | Checkout | HEAD = origin/main | Baseline candidate | Changed files | Packages | Findings |"
    printfn "|---|---:|---:|---|---:|---:|---:|"
    for repo in audit.Repositories do
        printfn "| `%s` | %s | %s | `%s` | %d | %d | %d |"
            repo.FullName
            (if repo.Exists then "yes" else "no")
            (if repo.HeadEqualsOriginMain then "yes" else "no")
            repo.BaselineTag
            repo.ChangedFiles.Length
            repo.Packages.Length
            repo.Findings.Length
    printfn ""
    for repo in audit.Repositories do
        printfn "## %s" repo.FullName
        printfn ""
        for package in repo.Packages do
            printfn "- `%s` `%s` from `%s`" package.PackageId package.Version package.Project
        for releaseSet in repo.ReleaseSets do
            printfn "- release set `%s`: `%s` -> `%s` (%d package(s))" releaseSet.Id releaseSet.BaselineTag (String.concat ", " releaseSet.ExpectedTags) releaseSet.Packages.Length
        for finding in repo.Findings do
            printfn "- FINDING: %s" finding
        printfn ""
        if not repo.ChangedFiles.IsEmpty then
            printfn "Changed since baseline candidate `%s`:" repo.BaselineTag
            for file in repo.ChangedFiles do printfn "- `%s`" file
            printfn ""

let selftest () =
    let temp = Path.Combine(Path.GetTempPath(), $"release-audit-selftest-{Guid.NewGuid():N}")
    Directory.CreateDirectory temp |> ignore
    let roster = Path.Combine(temp, "repos.yml")
    File.WriteAllText(roster, "repos:\n  - { id: .github, full: FS-GG/.github, role: authority }\n  - { id: game, full: FS-GG/FS.GG.Game, role: framework }\n")
    let rows = repoRows roster
    Directory.Delete(temp, true)
    if rows <> [ ".github", "FS-GG/.github"; "game", "FS-GG/FS.GG.Game" ] then
        failwith "roster parser self-test failed"
    printfn "release-train-audit: self-test passed"

let main () =
    let args = normalizedArgs()
    if hasFlag "--help" args || hasFlag "-h" args then usage(); exitOk
    elif hasFlag "--selftest" args then selftest(); exitOk
    else
        try
            let root = parseSimpleOption "--root" args |> Option.defaultValue "." |> ensureDirectory
            let registry = Path.Combine(root, "registry", "repos.yml")
            if not (File.Exists registry) then
                eprintfn "release-train-audit: roster not found: %s" registry
                exitNoVerdict
            else
                let requestedRepo = parseSimpleOption "--repo" args
                let rows =
                    repoRows registry
                    |> List.filter (fun (id, _) -> requestedRepo |> Option.forall ((=) id))
                if rows.IsEmpty then
                    eprintfn "release-train-audit: roster is empty or --repo did not match"
                    exitNoVerdict
                else
                    let parent =
                        parseSimpleOption "--siblings-root" args
                        |> Option.map ensureDirectory
                        |> Option.defaultValue (Directory.GetParent(root).FullName)
                    let repositories =
                        rows |> List.map (auditRepo (hasFlag "--fetch" args) root parent)
                    let audit = {
                        SchemaVersion = 2
                        GeneratedAt = DateTimeOffset.UtcNow
                        Root = root
                        Repositories = repositories
                    }
                    parseSimpleOption "--output" args |> Option.iter (fun path -> writeJson (Some path) audit)
                    if hasFlag "--json" args then writeJson None audit else markdown audit
                    if repositories |> List.exists (fun repo -> not repo.Findings.IsEmpty) then exitFinding
                    else exitOk
        with ex ->
            eprintfn "release-train-audit: no verdict: %s" ex.Message
            exitNoVerdict

exit (main())
