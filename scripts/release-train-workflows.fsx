#load "release-train-lib.fsx"

open System
open System.IO
open System.Text.RegularExpressions
open ReleaseTrain

type WorkflowFinding = {
    Rule: string
    Severity: string
    Message: string
}

type WorkflowResult = {
    Repository: string
    Workflow: string
    Errors: WorkflowFinding list
    Warnings: WorkflowFinding list
}

type WorkflowReport = {
    SchemaVersion: int
    GeneratedAt: DateTimeOffset
    Results: WorkflowResult list
}

let usage () =
    printfn "Usage: dotnet fsi scripts/release-train-workflows.fsx [--root DIR] [--siblings-root DIR] [--repo DIR] [--json] [--output FILE] [--selftest]"
    printfn "Exit: 0 = no errors; 1 = release-workflow errors; 3 = no verdict."

let finding rule severity message = { Rule = rule; Severity = severity; Message = message }

let jobBlocks (text: string) =
    let jobsIndex = text.IndexOf("jobs:", StringComparison.OrdinalIgnoreCase)
    if jobsIndex < 0 then [ text ]
    else
        let body = text.Substring jobsIndex
        Regex.Matches(
            body,
            @"(?ms)^  [A-Za-z0-9_-]+:\s*\r?\n.*?(?=^  [A-Za-z0-9_-]+:\s*\r?\n|\z)"
        )
        |> Seq.cast<Match>
        |> Seq.map _.Value
        |> Seq.toList

let checkText (text: string) =
    let contains (value: string) = text.Contains(value, StringComparison.OrdinalIgnoreCase)
    let releaseContextErrors =
        jobBlocks text
        |> List.choose (fun job ->
            if job.Contains("gh release", StringComparison.OrdinalIgnoreCase)
               && not (job.Contains("actions/checkout", StringComparison.OrdinalIgnoreCase))
               && not (job.Contains("GH_REPO:", StringComparison.OrdinalIgnoreCase))
               && not (job.Contains("--repo", StringComparison.OrdinalIgnoreCase)) then
                Some(
                    finding
                        "github-release-repository"
                        "error"
                        "checkout-free job invokes `gh release` without `GH_REPO` or `--repo`"
                )
            else None)
    let repacksAfterPush =
        jobBlocks text
        |> List.exists (fun job ->
            let push = Regex.Match(job, @"dotnet\s+nuget\s+push\b", RegexOptions.IgnoreCase)
            push.Success
            && Regex.IsMatch(job.Substring(push.Index + push.Length), @"dotnet\s+pack\b", RegexOptions.IgnoreCase))
    let pushIndex sourcePattern =
        let matched =
            Regex.Match(
                text,
                $@"dotnet\s+nuget\s+push\b[\s\S]{{0,600}}?{sourcePattern}",
                RegexOptions.IgnoreCase
            )
        if matched.Success then Some matched.Index else None
    let errors =
        releaseContextErrors @ [
            if contains "NuGet/login@" && not (Regex.IsMatch(text, @"id-token:\s*write", RegexOptions.IgnoreCase)) then
                finding
                    "nuget-oidc-permission"
                    "error"
                    "NuGet trusted publishing is present but `id-token: write` is absent"
            if contains "nuget.pkg.github.com" && contains "nuget.org" && repacksAfterPush then
                finding
                    "single-pack"
                    "error"
                    "workflow runs `dotnet pack` after feed publication begins; both feeds must receive the pre-push immutable artifact"
            if contains "nuget.pkg.github.com" && contains "nuget.org" then
                let githubPush = pushIndex @"nuget\.pkg\.github\.com"
                let nugetPush = pushIndex @"(?:api|www)\.nuget\.org"
                if Option.isSome githubPush && Option.isSome nugetPush && githubPush.Value > nugetPush.Value then
                    finding
                        "feed-order"
                        "error"
                        "nuget.org appears before GitHub Packages; the org feed must be pushed first"
        ]
    let warnings =
        [
            if (contains "dotnet pack" || contains "nupkg") && not (contains "sha256sum") then
                finding
                    "artifact-checksum"
                    "warning"
                    "release workflow does not visibly create or verify SHA256SUMS"
            if contains "nuget.pkg.github.com" && contains "nuget.org"
               && not (contains "actions/upload-artifact")
               && not (contains "actions/download-artifact") then
                finding
                    "immutable-artifact"
                    "warning"
                    "dual-feed workflow has no visible upload/download artifact handoff; inspect byte-identity manually"
            if contains "gh release" && not (contains "--clobber") then
                finding
                    "github-release-rerun"
                    "warning"
                    "`gh release` does not visibly use `--clobber`; verify reruns are idempotent"
        ]
    errors, warnings

let releaseWorkflows repo =
    let directory = Path.Combine(repo, ".github", "workflows")
    if not (Directory.Exists directory) then []
    else
        Directory.EnumerateFiles directory
        |> Seq.filter (fun file ->
            let name = Path.GetFileName(file).ToLowerInvariant()
            (name.EndsWith(".yml") || name.EndsWith(".yaml"))
            && (name.Contains("release") || name.Contains("publish")))
        |> Seq.sort
        |> Seq.toList

let checkRepo repo =
    let full = ensureDirectory repo
    let repository =
        tryRun full "git" [ "config"; "--get"; "remote.origin.url" ]
        |> Option.defaultValue full
    releaseWorkflows full
    |> List.map (fun workflow ->
        let errors, warnings = File.ReadAllText workflow |> checkText
        {
            Repository = repository
            Workflow = Path.GetRelativePath(full, workflow).Replace('\\', '/')
            Errors = errors
            Warnings = warnings
        })

let markdown report =
    printfn "# Release workflow checks"
    printfn ""
    for result in report.Results do
        printfn "## %s — `%s`" result.Repository result.Workflow
        printfn ""
        if result.Errors.IsEmpty && result.Warnings.IsEmpty then printfn "- OK"
        for item in result.Errors do printfn "- ERROR `%s`: %s" item.Rule item.Message
        for item in result.Warnings do printfn "- WARNING `%s`: %s" item.Rule item.Message
        printfn ""

let selftest () =
    let broken =
        """
permissions:
  contents: write
jobs:
  publish:
    steps:
      - uses: NuGet/login@v1
      - run: dotnet pack
      - run: dotnet nuget push --source https://nuget.pkg.github.com/FS-GG
      - run: dotnet nuget push --source https://api.nuget.org/v3/index.json
      - run: gh release create "$GITHUB_REF_NAME"
"""
    let fixedText =
        broken.Replace("contents: write", "contents: write\n  id-token: write")
              .Replace("  - run: gh release", "  - env:\n      GH_REPO: FS-GG/example\n    run: gh release")
    let brokenErrors, _ = checkText broken
    let fixedErrors, _ = checkText fixedText
    if brokenErrors.Length <> 2 || not fixedErrors.IsEmpty then
        failwith $"workflow checker self-test failed: broken={brokenErrors.Length}, fixed={fixedErrors.Length}"
    printfn "release-train-workflows: self-test passed"

let main () =
    let args = normalizedArgs()
    if hasFlag "--help" args || hasFlag "-h" args then usage(); exitOk
    elif hasFlag "--selftest" args then selftest(); exitOk
    else
        try
            let root = parseSimpleOption "--root" args |> Option.defaultValue "." |> ensureDirectory
            let repositories =
                match parseSimpleOption "--repo" args with
                | Some repo -> [ ensureDirectory repo ]
                | None ->
                    let registry = Path.Combine(root, "registry", "repos.yml")
                    if not (File.Exists registry) then failwith $"roster not found: {registry}"
                    let parent =
                        parseSimpleOption "--siblings-root" args
                        |> Option.map ensureDirectory
                        |> Option.defaultValue (Directory.GetParent(root).FullName)
                    repoRows registry
                    |> List.map (fun (id, fullName) ->
                        if id = ".github" then root
                        else Path.Combine(parent, fullName.Split('/') |> Array.last))
            let missing = repositories |> List.filter (Directory.Exists >> not)
            if not missing.IsEmpty then
                missing |> List.iter (eprintfn "release-train-workflows: missing repository: %s")
                exitNoVerdict
            else
                let results = repositories |> List.collect checkRepo
                if results.IsEmpty then
                    eprintfn "release-train-workflows: no release/publish workflows discovered"
                    exitNoVerdict
                else
                    let report = { SchemaVersion = 1; GeneratedAt = DateTimeOffset.UtcNow; Results = results }
                    parseSimpleOption "--output" args |> Option.iter (fun path -> writeJson (Some path) report)
                    if hasFlag "--json" args then writeJson None report else markdown report
                    if results |> List.exists (fun result -> not result.Errors.IsEmpty) then exitFinding else exitOk
        with ex ->
            eprintfn "release-train-workflows: no verdict: %s" ex.Message
            exitNoVerdict

exit (main())
