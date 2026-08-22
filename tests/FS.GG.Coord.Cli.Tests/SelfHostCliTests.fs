namespace FS.GG.Coord.Cli.Tests

open System
open System.Diagnostics
open System.IO
open System.Security.Cryptography
open Xunit
open FS.GG.Coord.SelfHost

module SelfHostCliTests =
    let private run args =
        let engine = Path.Combine(AppContext.BaseDirectory, "fsgg-coord-engine.dll")
        let start = ProcessStartInfo("dotnet")
        start.UseShellExecute <- false
        start.RedirectStandardOutput <- true
        start.RedirectStandardError <- true
        start.ArgumentList.Add engine
        for arg in args do start.ArgumentList.Add arg
        use child = Process.Start start
        let stdout = child.StandardOutput.ReadToEnd()
        let stderr = child.StandardError.ReadToEnd()
        child.WaitForExit()
        child.ExitCode, stdout, stderr

    let private git directory args =
        let start = ProcessStartInfo("git")
        start.WorkingDirectory <- directory
        start.UseShellExecute <- false
        start.RedirectStandardOutput <- true
        start.RedirectStandardError <- true
        for arg in args do start.ArgumentList.Add arg
        use child = Process.Start start
        let stdout = child.StandardOutput.ReadToEnd().Trim()
        let stderr = child.StandardError.ReadToEnd()
        child.WaitForExit()
        if child.ExitCode <> 0 then failwithf "git %s failed: %s" (String.concat " " args) stderr
        stdout

    let private withReceipt action =
        let directory = Path.Combine(Path.GetTempPath(), "fsgg-self-host-" + Guid.NewGuid().ToString "n")
        Directory.CreateDirectory directory |> ignore
        try
            let candidate = Path.Combine(directory, "candidate-engine")
            let versionOut = "1.2.0-candidate"
            File.WriteAllText(candidate, "#!/bin/sh\n[ \"$1\" = \"--version\" ] && printf '%s\\n' '1.2.0-candidate'\n")
            File.SetUnixFileMode(candidate, UnixFileMode.UserRead ||| UnixFileMode.UserWrite ||| UnixFileMode.UserExecute)
            let hash = File.ReadAllBytes candidate |> SHA256.HashData |> Convert.ToHexString |> _.ToLowerInvariant()
            let snapshot = Path.Combine(directory, "snapshot.json")
            File.WriteAllText(snapshot, "{\"snapshot\":1}")
            let snapshotHash = File.ReadAllBytes snapshot |> SHA256.HashData |> Convert.ToHexString |> _.ToLowerInvariant()
            let receipt =
                createReceipt
                    "base"
                    "head"
                    hash
                    versionOut
                    "shared refusal"
                    snapshotHash
                    BootstrapReason.NewSchemaCase
                    { Build = "build"; Unit = "unit"; FocusedProductionRoute = "route"; Provenance = "provenance"; Inversion = "inversion" }
                    "decision"
                    "action"
                    { Actor = "host/ron000"; AcceptedAt = DateTimeOffset.Parse "2026-08-22T18:00:00Z" }
                |> Result.defaultWith (String.concat "; " >> failwith)
            let receiptPath = Path.Combine(directory, "receipt.txt")
            File.WriteAllText(receiptPath, encodeReceipt receipt)
            action receiptPath candidate snapshot
        finally
            Directory.Delete(directory, true)

    [<Fact>]
    let ``stable host command verifies exact candidate bytes and refuses drift`` () =
        withReceipt (fun receipt candidate _ ->
            let code, stdout, _ = run [ "self-host"; "verify"; receipt; candidate ]
            Assert.Equal(0, code)
            Assert.Contains("SELF-HOST-AUTHORIZED", stdout)
            File.WriteAllBytes(candidate, [| 4uy |])
            let refused, _, stderr = run [ "self-host"; "verify"; receipt; candidate ]
            Assert.NotEqual(0, refused)
            Assert.Contains("does not match", stderr))

    [<Fact>]
    let ``host mint binds measured candidate and snapshot bytes`` () =
        withReceipt (fun receipt candidate snapshot ->
            let directory = Path.GetDirectoryName receipt
            let proposal = Path.Combine(directory, "proposal.json")
            let output = Path.Combine(directory, "minted.txt")
            File.WriteAllText(
                proposal,
                """{"baseSha":"base","candidateHeadSha":"head","sharedRefusal":"shared refusal","reason":"new-schema-case","evidence":{"build":"build","unit":"unit","focusedProductionRoute":"route","provenance":"provenance","inversion":"inversion"},"candidateDecisionKey":"decision","candidateActionKey":"action","hostAcceptance":{"actor":"host/ron000","acceptedAt":"2026-08-22T18:00:00Z"}}"""
            )
            let code, stdout, stderr = run [ "self-host"; "mint"; proposal; candidate; snapshot; output ]
            Assert.True((code = 0), stderr)
            Assert.Contains("SELF-HOST-RECEIPT", stdout)
            match File.ReadAllText output |> tryDecodeReceipt with
            | Ok(Some minted) ->
                let candidateHash = File.ReadAllBytes candidate |> SHA256.HashData |> Convert.ToHexString |> _.ToLowerInvariant()
                let snapshotHash = File.ReadAllBytes snapshot |> SHA256.HashData |> Convert.ToHexString |> _.ToLowerInvariant()
                Assert.Equal(candidateHash, minted.CandidateBinarySha256)
                Assert.Equal(snapshotHash, minted.SnapshotSha256)
                Assert.Equal("1.2.0-candidate", minted.CandidateVersion)
            | other -> failwithf "expected minted receipt, got %A" other)

    [<Fact>]
    let ``post-merge replay command refuses decision or action disagreement`` () =
        withReceipt (fun receipt _ snapshot ->
            let code, stdout, _ = run [ "self-host"; "replay"; receipt; snapshot; "decision"; "action" ]
            Assert.Equal(0, code)
            Assert.Contains("SELF-HOST-REPLAY-AGREES", stdout)
            let refused, _, stderr = run [ "self-host"; "replay"; receipt; snapshot; "different"; "action" ]
            Assert.NotEqual(0, refused)
            Assert.Contains("decision key disagrees", stderr)
            File.WriteAllText(snapshot, "changed snapshot")
            let stale, _, snapshotError = run [ "self-host"; "replay"; receipt; snapshot; "decision"; "action" ]
            Assert.NotEqual(0, stale)
            Assert.Contains("snapshot SHA-256", snapshotError))

    [<Fact>]
    let ``stable verifier binds receipt base and head to the candidate checkout`` () =
        let directory = Path.Combine(Path.GetTempPath(), "fsgg-self-host-git-" + Guid.NewGuid().ToString "n")
        Directory.CreateDirectory directory |> ignore
        try
            git directory [ "init"; "-q"; "-b"; "main" ] |> ignore
            git directory [ "config"; "user.email"; "self-host@example.invalid" ] |> ignore
            git directory [ "config"; "user.name"; "Self Host Test" ] |> ignore
            let candidate = Path.Combine(directory, "candidate-engine")
            File.WriteAllText(candidate, "#!/bin/sh\n[ \"$1\" = \"--version\" ] && printf '%s\\n' '1.2.0-candidate'\n")
            File.SetUnixFileMode(candidate, UnixFileMode.UserRead ||| UnixFileMode.UserWrite ||| UnixFileMode.UserExecute)
            git directory [ "add"; "candidate-engine" ] |> ignore
            git directory [ "commit"; "-q"; "-m"; "base" ] |> ignore
            let baseSha = git directory [ "rev-parse"; "HEAD" ]
            git directory [ "update-ref"; "refs/remotes/origin/main"; baseSha ] |> ignore
            File.WriteAllText(Path.Combine(directory, "change.txt"), "candidate change")
            git directory [ "add"; "change.txt" ] |> ignore
            git directory [ "commit"; "-q"; "-m"; "candidate" ] |> ignore
            let headSha = git directory [ "rev-parse"; "HEAD" ]
            let hash = File.ReadAllBytes candidate |> SHA256.HashData |> Convert.ToHexString |> _.ToLowerInvariant()
            let snapshot = Path.Combine(directory, "snapshot.json")
            File.WriteAllText(snapshot, "{\"snapshot\":1}")
            let snapshotHash = File.ReadAllBytes snapshot |> SHA256.HashData |> Convert.ToHexString |> _.ToLowerInvariant()
            let receipt =
                createReceipt baseSha headSha hash "1.2.0-candidate" "shared refusal" snapshotHash
                    BootstrapReason.RelocatedDecisionBoundary
                    { Build = "build"; Unit = "unit"; FocusedProductionRoute = "route"; Provenance = "provenance"; Inversion = "inversion" }
                    "decision" "action"
                    { Actor = "host/ron000"; AcceptedAt = DateTimeOffset.Parse "2026-08-22T18:00:00Z" }
                |> Result.defaultWith (String.concat "; " >> failwith)
            let receiptPath = Path.Combine(directory, "receipt.txt")
            File.WriteAllText(receiptPath, encodeReceipt receipt)
            let code, _, stderr = run [ "self-host"; "verify"; receiptPath; candidate; directory ]
            Assert.True((code = 0), stderr)
            git directory [ "commit"; "--allow-empty"; "-q"; "-m"; "head drift" ] |> ignore
            let refused, _, drift = run [ "self-host"; "verify"; receiptPath; candidate; directory ]
            Assert.NotEqual(0, refused)
            Assert.Contains("does not match receipt head", drift)
        finally
            Directory.Delete(directory, true)
