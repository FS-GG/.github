#load "release-train-lib.fsx"

open System
open System.IO
open System.Text.Json
open ReleaseTrain

type Stage = {
    Name: string
    State: string
    Evidence: string
}

type StatusReport = {
    SchemaVersion: int
    GeneratedAt: DateTimeOffset
    Complete: bool
    Stages: Stage list
}

let usage () =
    printfn "Usage: dotnet fsi scripts/release-train-status.fsx --audit FILE --workflows FILE"
    printfn "       [--verification FILE ...] [--registry complete|pending] [--json] [--output FILE] [--selftest]"
    printfn "Rerun the producing audit/workflow/verification commands first; this command summarizes their evidence."
    printfn "Exit: 0 = every represented stage complete; 1 = incomplete/findings; 3 = no verdict."

let arrayLength (root: JsonElement) (name: string) =
    let mutable value = Unchecked.defaultof<JsonElement>
    if root.TryGetProperty(name, &value) && value.ValueKind = JsonValueKind.Array then
        value.GetArrayLength()
    else failwith $"evidence file requires array `{name}`"

let readRoot path =
    let text = File.ReadAllText path
    let doc = JsonDocument.Parse text
    doc

let auditStage path =
    use doc = readRoot path
    let repos = doc.RootElement.GetProperty "repositories"
    let findings =
        repos.EnumerateArray()
        |> Seq.sumBy (fun repo -> arrayLength repo "findings")
    {
        Name = "audit"
        State = if findings = 0 then "complete" else "needs-review"
        Evidence = $"{repos.GetArrayLength()} repositories, {findings} finding(s)"
    }

let workflowStage path =
    use doc = readRoot path
    let results = doc.RootElement.GetProperty "results"
    let errors =
        results.EnumerateArray()
        |> Seq.sumBy (fun result -> arrayLength result "errors")
    let warnings =
        results.EnumerateArray()
        |> Seq.sumBy (fun result -> arrayLength result "warnings")
    {
        Name = "workflow-preflight"
        State = if errors = 0 then "complete" else "blocked"
        Evidence = $"{results.GetArrayLength()} workflows, {errors} error(s), {warnings} warning(s)"
    }

let verificationStage path =
    use doc = readRoot path
    let root = doc.RootElement
    let name = root.GetProperty("name").GetString()
    let expected = root.GetProperty("expectedPackages").GetInt32()
    let observed = root.GetProperty("observedPackages").GetInt32()
    let tagMatch = root.GetProperty("tagMatchesExpectedCommit").GetBoolean()
    let packages = root.GetProperty "packages"
    let payloadsMatch =
        packages.EnumerateArray()
        |> Seq.forall (fun package -> package.GetProperty("payloadIdentical").GetBoolean())
    let tagState = if tagMatch then "match" else "mismatch"
    let payloadState = if payloadsMatch then "identical" else "different"
    {
        Name = $"verification:{name}"
        State =
            if expected = observed && tagMatch && payloadsMatch then "complete"
            else "blocked"
        Evidence =
            $"{observed}/{expected} packages; tag={tagState}; payloads={payloadState}"
    }

let markdown report =
    printfn "# Release train status"
    printfn ""
    printfn "Overall: **%s**" (if report.Complete then "complete" else "incomplete")
    printfn ""
    printfn "| Stage | State | Evidence |"
    printfn "|---|---|---|"
    for stage in report.Stages do
        printfn "| `%s` | %s | %s |" stage.Name stage.State stage.Evidence

let selftest () =
    let root = Path.Combine(Path.GetTempPath(), $"release-status-selftest-{Guid.NewGuid():N}")
    Directory.CreateDirectory root |> ignore
    let audit = Path.Combine(root, "audit.json")
    let workflows = Path.Combine(root, "workflows.json")
    let verification = Path.Combine(root, "verification.json")
    File.WriteAllText(audit, """{"repositories":[{"findings":[]}]}""")
    File.WriteAllText(workflows, """{"results":[{"errors":[],"warnings":[]}]}""")
    File.WriteAllText(verification, """{"name":"set","expectedPackages":1,"observedPackages":1,"tagMatchesExpectedCommit":true,"packages":[{"payloadIdentical":true}]}""")
    let stages = [ auditStage audit; workflowStage workflows; verificationStage verification ]
    Directory.Delete(root, true)
    if stages |> List.exists (fun stage -> stage.State <> "complete") then
        failwith "status summary self-test failed"
    printfn "release-train-status: self-test passed"

let main () =
    let args = normalizedArgs()
    if hasFlag "--help" args || hasFlag "-h" args then usage(); exitOk
    elif hasFlag "--selftest" args then selftest(); exitOk
    else
        try
            let audit =
                parseSimpleOption "--audit" args
                |> Option.defaultWith (fun () -> failwith "--audit is required")
            let workflows =
                parseSimpleOption "--workflows" args
                |> Option.defaultWith (fun () -> failwith "--workflows is required")
            let verificationFiles = optionValues "--verification" args
            let registryState = parseSimpleOption "--registry" args |> Option.defaultValue "pending"
            if registryState <> "complete" && registryState <> "pending" then
                failwith "--registry must be `complete` or `pending`"
            let stages =
                [
                    auditStage audit
                    workflowStage workflows
                    for verification in verificationFiles do verificationStage verification
                    {
                        Name = "registry"
                        State = registryState
                        Evidence =
                            if registryState = "complete" then "merged canonical registry verified"
                            else "registry reconciliation not yet recorded"
                    }
                ]
            let report = {
                SchemaVersion = 1
                GeneratedAt = DateTimeOffset.UtcNow
                Complete = stages |> List.forall (fun stage -> stage.State = "complete")
                Stages = stages
            }
            parseSimpleOption "--output" args |> Option.iter (fun path -> writeJson (Some path) report)
            if hasFlag "--json" args then writeJson None report else markdown report
            if report.Complete then exitOk else exitFinding
        with ex ->
            eprintfn "release-train-status: no verdict: %s" ex.Message
            exitNoVerdict

exit (main())
