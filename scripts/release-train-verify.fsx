#load "release-train-lib.fsx"

open System
open System.IO
open System.Text.Json
open ReleaseTrain

type ExpectedPackage = {
    Id: string
    Version: string
}

type VerificationPlan = {
    Name: string
    RepositoryPath: string
    Tag: string
    Commit: string
    Packages: ExpectedPackage list
}

type PackageVerification = {
    PackageId: string
    Version: string
    GitHubUrl: string
    NuGetUrl: string
    GitHubArchiveSha256: string
    NuGetArchiveSha256: string
    PayloadFiles: int
    PayloadIdentical: bool
    Differences: string list
    GitHubAvailable: bool
    NuGetAvailable: bool
}

type VerificationReport = {
    SchemaVersion: int
    GeneratedAt: DateTimeOffset
    Name: string
    ExpectedPackages: int
    ObservedPackages: int
    Tag: string
    ExpectedCommit: string
    SubjectCommit: string
    TagCommit: string
    TagMatchesExpectedCommit: bool
    Conclusion: string
    GitHubAvailable: bool
    NuGetAvailable: bool
    Packages: PackageVerification list
}

let usage () =
    printfn "Usage: dotnet fsi scripts/release-train-verify.fsx --manifest FILE [--github-index URL] [--nuget-index URL]"
    printfn "       [--github-user USER] [--timeout-seconds N] [--interval-seconds N] [--artifacts DIR] [--allow-partial]"
    printfn "       [--json] [--output FILE] [--selftest]"
    printfn "Exit: 0 = fully verified; 1 = verification finding; 3 = no verdict."

let requiredString (root: JsonElement) (name: string) =
    let mutable value = Unchecked.defaultof<JsonElement>
    if not (root.TryGetProperty(name, &value)) || value.ValueKind <> JsonValueKind.String then
        failwith $"manifest requires string property `{name}`"
    value.GetString()

let readPlan path =
    use doc = JsonDocument.Parse(File.ReadAllText path)
    let root = doc.RootElement
    let packages =
        let mutable value = Unchecked.defaultof<JsonElement>
        if not (root.TryGetProperty("packages", &value)) || value.ValueKind <> JsonValueKind.Array then
            failwith "manifest requires array property `packages`"
        [
            for package in value.EnumerateArray() do
                yield {
                    Id = requiredString package "id"
                    Version = requiredString package "version"
                }
        ]
    if packages.IsEmpty then failwith "manifest package list is empty"
    let duplicates =
        packages
        |> List.countBy (fun package -> package.Id.ToLowerInvariant(), package.Version.ToLowerInvariant())
        |> List.filter (fun (_, count) -> count > 1)
    if not duplicates.IsEmpty then failwith "manifest contains duplicate package ID/version rows"
    {
        Name = requiredString root "name"
        RepositoryPath = requiredString root "repositoryPath"
        Tag = requiredString root "tag"
        Commit = requiredString root "commit"
        Packages = packages
    }

let intOption (name: string) (fallback: int) (args: string list) =
    match parseSimpleOption name args with
    | Some value ->
        match Int32.TryParse value with
        | true, parsed when parsed > 0 -> parsed
        | _ -> failwith $"{name} requires a positive integer"
    | None -> fallback

let token () =
    [ Environment.GetEnvironmentVariable "GITHUB_TOKEN"
      Environment.GetEnvironmentVariable "GH_TOKEN" ]
    |> List.tryFind (String.IsNullOrWhiteSpace >> not)

let githubUser args =
    parseSimpleOption "--github-user" args
    |> Option.orElseWith (fun () ->
        Environment.GetEnvironmentVariable "GITHUB_ACTOR"
        |> Option.ofObj
        |> Option.filter (String.IsNullOrWhiteSpace >> not))
    |> Option.orElseWith (fun () -> tryRun "." "gh" [ "api"; "user"; "--jq"; ".login" ])

let markdown report =
    printfn "# Release verification — %s" report.Name
    printfn ""
    printfn "- Expected/observed packages: %d/%d" report.ExpectedPackages report.ObservedPackages
    printfn "- Tag `%s`: `%s`" report.Tag report.TagCommit
    printfn "- Expected commit: `%s` (%s)" report.ExpectedCommit (if report.TagMatchesExpectedCommit then "match" else "MISMATCH")
    printfn "- Feed availability: GitHub=%b, NuGet=%b" report.GitHubAvailable report.NuGetAvailable
    printfn ""
    printfn "| Package | GitHub archive | NuGet archive | Payload |"
    printfn "|---|---|---|---|"
    for package in report.Packages do
        printfn "| `%s %s` | `%s` | `%s` | %s (%d files) |"
            package.PackageId package.Version package.GitHubArchiveSha256 package.NuGetArchiveSha256
            (if package.PayloadIdentical then "identical" else "DIFFERENT") package.PayloadFiles

let selftest () =
    selfTestPackageComparison()
    let root = Path.Combine(Path.GetTempPath(), $"release-verify-plan-{Guid.NewGuid():N}")
    Directory.CreateDirectory root |> ignore
    let path = Path.Combine(root, "plan.json")
    File.WriteAllText(path, """{"name":"set","repositoryPath":".","tag":"v1.0.0","commit":"abc","packages":[{"id":"Example","version":"1.0.0"}]}""")
    let plan = readPlan path
    Directory.Delete(root, true)
    if plan.Name <> "set" || plan.Packages.Length <> 1 then failwith "verification manifest self-test failed"
    let report = {
        SchemaVersion = 2; GeneratedAt = DateTimeOffset.UnixEpoch; Name = "set"; ExpectedPackages = 1; ObservedPackages = 1
        Tag = "v1"; ExpectedCommit = "abc"; SubjectCommit = "abc"; TagCommit = "abc"; TagMatchesExpectedCommit = true
        Conclusion = "success"; GitHubAvailable = true; NuGetAvailable = true
        Packages = [ { PackageId = "Example"; Version = "1.0.0"; GitHubUrl = "github"; NuGetUrl = "nuget"; GitHubArchiveSha256 = "a"; NuGetArchiveSha256 = "b"; PayloadFiles = 1; PayloadIdentical = true; Differences = []; GitHubAvailable = true; NuGetAvailable = true } ]
    }
    use document = JsonDocument.Parse(json report)
    for field in [ "subjectCommit"; "conclusion"; "gitHubAvailable"; "nuGetAvailable" ] do
        let mutable value = Unchecked.defaultof<JsonElement>
        if not (document.RootElement.TryGetProperty(field, &value)) then failwith $"verification report lacks `{field}`"
    printfn "release-train-verify: self-test passed"

let verify args =
    task {
        let manifest =
            parseSimpleOption "--manifest" args
            |> Option.defaultWith (fun () -> failwith "--manifest is required")
            |> Path.GetFullPath
        let plan = readPlan manifest
        let repoPath = ensureDirectory plan.RepositoryPath
        let timeout = intOption "--timeout-seconds" 600 args
        let interval = intOption "--interval-seconds" 15 args
        let stamp = DateTimeOffset.UtcNow.ToString "yyyyMMddTHHmmssZ"
        let artifactRoot =
            parseSimpleOption "--artifacts" args
            |> Option.defaultValue (Path.Combine("artifacts", "release-train", $"verify-{stamp}"))
            |> Path.GetFullPath
        Directory.CreateDirectory artifactRoot |> ignore

        let githubIndex =
            parseSimpleOption "--github-index" args
            |> Option.defaultValue "https://nuget.pkg.github.com/FS-GG/index.json"
        let nugetIndex =
            parseSimpleOption "--nuget-index" args
            |> Option.defaultValue "https://api.nuget.org/v3/index.json"
        let githubToken =
            token() |> Option.defaultWith (fun () -> failwith "GITHUB_TOKEN or GH_TOKEN is required for GitHub Packages")
        let githubUsername =
            githubUser args |> Option.defaultWith (fun () -> failwith "could not determine GitHub username; pass --github-user")
        use githubClient = createHttpClient (Some githubUsername) (Some githubToken)
        use nugetClient = createHttpClient None None
        let! githubBase = normalizeNuGetBase githubIndex githubClient
        let! nugetBase = normalizeNuGetBase nugetIndex nugetClient
        let deadline = DateTimeOffset.UtcNow.AddSeconds(float timeout)
        let pollInterval = TimeSpan.FromSeconds(float interval)
        let allowPartial = hasFlag "--allow-partial" args
        let tryDownload (client: System.Net.Http.HttpClient) (url: string) (target: string) =
            task {
                use! response = client.GetAsync url
                if response.IsSuccessStatusCode then
                    let! bytes = response.Content.ReadAsByteArrayAsync()
                    File.WriteAllBytes(target, bytes)
                    return true
                elif response.StatusCode = System.Net.HttpStatusCode.NotFound then return false
                else
                    let! detail = response.Content.ReadAsStringAsync()
                    return failwith $"{url} returned HTTP {int response.StatusCode}: {detail}"
            }

        let results = ResizeArray<PackageVerification>()
        for package in plan.Packages do
            let githubUrl = packageUrl githubBase package.Id package.Version
            let nugetUrl = packageUrl nugetBase package.Id package.Version
            let safe = $"{package.Id}.{package.Version}".ToLowerInvariant()
            let githubPath = Path.Combine(artifactRoot, $"{safe}.github.nupkg")
            let nugetPath = Path.Combine(artifactRoot, $"{safe}.nuget.nupkg")
            let! githubAvailable, nugetAvailable =
                if allowPartial then
                    task {
                        let! github = tryDownload githubClient githubUrl githubPath
                        let! nuget = tryDownload nugetClient nugetUrl nugetPath
                        return github, nuget
                    }
                else
                    task {
                        do! downloadWhenAvailable githubClient githubUrl githubPath deadline pollInterval
                        do! downloadWhenAvailable nugetClient nugetUrl nugetPath deadline pollInterval
                        return true, true
                    }
            let githubFiles, nugetFiles, differences =
                if githubAvailable && nugetAvailable then comparePackages githubPath nugetPath
                else 0, 0, [ "one or both package feeds are unavailable" ]
            results.Add {
                PackageId = package.Id
                Version = package.Version
                GitHubUrl = githubUrl
                NuGetUrl = nugetUrl
                GitHubArchiveSha256 = if githubAvailable then sha256File githubPath else ""
                NuGetArchiveSha256 = if nugetAvailable then sha256File nugetPath else ""
                PayloadFiles = githubFiles
                PayloadIdentical = differences.IsEmpty && githubFiles = nugetFiles
                Differences = differences
                GitHubAvailable = githubAvailable
                NuGetAvailable = nugetAvailable
            }

        let tagResult = runProcess repoPath "git" [ "rev-list"; "-n"; "1"; plan.Tag ]
        let tagCommit = requireSuccess $"resolve tag {plan.Tag}" tagResult
        return {
            SchemaVersion = 2
            GeneratedAt = DateTimeOffset.UtcNow
            Name = plan.Name
            ExpectedPackages = plan.Packages.Length
            ObservedPackages = results.Count
            Tag = plan.Tag
            ExpectedCommit = plan.Commit
            SubjectCommit = plan.Commit
            TagCommit = tagCommit
            TagMatchesExpectedCommit = String.Equals(tagCommit, plan.Commit, StringComparison.OrdinalIgnoreCase)
            Conclusion = "success"
            GitHubAvailable = results |> Seq.forall (fun package -> package.GitHubAvailable)
            NuGetAvailable = results |> Seq.forall (fun package -> package.NuGetAvailable)
            Packages = results |> Seq.toList
        }
    }

let main () =
    let args = normalizedArgs()
    if hasFlag "--help" args || hasFlag "-h" args then usage(); exitOk
    elif hasFlag "--selftest" args then selftest(); exitOk
    else
        try
            let report = verify args |> Async.AwaitTask |> Async.RunSynchronously
            parseSimpleOption "--output" args |> Option.iter (fun path -> writeJson (Some path) report)
            if hasFlag "--json" args then writeJson None report else markdown report
            let packageFailure =
                report.ObservedPackages <> report.ExpectedPackages
                || not report.GitHubAvailable
                || not report.NuGetAvailable
                || report.Packages |> List.exists (fun package -> not package.PayloadIdentical)
            if packageFailure || not report.TagMatchesExpectedCommit then exitFinding else exitOk
        with
        | :? System.Net.Http.HttpRequestException as ex ->
            eprintfn "release-train-verify: no verdict: %s" ex.Message
            exitNoVerdict
        | ex ->
            eprintfn "release-train-verify: verification failed: %s" ex.Message
            exitFinding

exit (main())
