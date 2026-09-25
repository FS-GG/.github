namespace FS.GG.V2.Progress.Tests

open System
open Xunit
open FS.GG.V2.Progress

module ProgressRendererTests =
    let private at = DateTimeOffset(2026, 9, 25, 12, 0, 0, TimeSpan.Zero)
    let private headA = String.replicate 40 "a"
    let private headB = String.replicate 40 "b"
    let private link label = { Label = label; Url = "https://github.com/FS-GG/.github/pull/123" }

    let private lane id model reservation state = {
        Id = id; Model = model; Effort = High; Reservation = reservation; State = state
    }

    let private baseSnapshot () : ProgressSnapshot = {
        AsOf = at
        RoadmapHead = headA
        Lanes = [ lane "B" Gpt6Sol General Pending; lane "A" Gpt6Astra DirectV2 ActiveHealthy ]
        DeclaredLaneCounts = {
            Total = 2; Active = 1; ReservedDirectV2 = 1; ActiveReservedDirectV2 = 1
            ByModel = [ Gpt6Astra, 1; Gpt6Sol, 1 ]
        }
        Workstreams = [
            { Name = "Telemetry"; State = ActiveHealthy; Detail = "Queue ready"; PrCount = 1; EvidenceCount = 2 }
            { Name = "Capture"; State = Pending; Detail = "Needs native run"; PrCount = 0; EvidenceCount = 0 }
        ]
        DeclaredEvidenceCounts = { Prs = 1; Evidence = 2 }
        Telemetry = {
            WorkspaceId = "main-fsharp-dev"; Readiness = ActiveHealthy
            Pending = 0; PendingUnacknowledged = 0; UnacknowledgedLossy = false
            Capture = NoAcceptedCaptureClaim
        }
        ProtectedHolds = [ "Authority write remains disabled" ]
        Checks = [ { Name = "Build"; State = CompletedInfo; Detail = "Focused tests pass" } ]
        Risks = [ { Name = "Capture"; State = BlockedIncompleteEvidence; Detail = "No accepted run" } ]
        NextActions = [ "Collect genuine runner evidence" ]
        CompletionHistory = []
    }

    let private acceptedClaim () =
        let runner = {
            Origin = NativeRunnerItem; WorkspaceId = "main-fsharp-dev"; ItemId = "GS2-14"
            ObservedAt = at.AddMinutes(-3.0); Evidence = link "runner item"
            NativeTurns = [ { TurnId = "native-turn-1"; InputTokens = 14; OutputTokens = 7 } ]
        }
        let host = {
            WorkspaceId = "main-fsharp-dev"; ItemId = "GS2-14"; Applied = true
            ObservedAt = at.AddMinutes(-2.0); Evidence = link "Host receipt"
        }
        let queue = {
            WorkspaceId = "main-fsharp-dev"; ObservedAt = at.AddMinutes(-1.0)
            Pending = 0; PendingUnacknowledged = 0; UnacknowledgedLossy = false
        }
        runner, host, queue

    let private render snapshot =
        match ProgressRenderer.renderSnapshot snapshot with
        | Ok markdown -> markdown
        | Error errors -> failwithf "unexpected refusal: %A" errors

    let private refuses reason snapshot =
        match ProgressRenderer.renderSnapshot snapshot with
        | Ok _ -> failwithf "unsupported claim accepted: %s" reason
        | Error errors -> Assert.Contains(reason, String.concat "; " errors)

    [<Fact>]
    let ``Markdown bytes are stable and telemetry readiness is separate from capture`` () =
        let actual = render (baseSnapshot ())
        let expected =
            [
                "# V2 progress update"
                ""
                "- Snapshot: 2026-09-25 12:00:00 UTC"
                $"- Recorded roadmap head: `{headA}`"
                ""
                "## Lanes"
                ""
                "| Lane | Model | Effort | Reservation | State |"
                "| --- | --- | --- | --- | --- |"
                "| A | gpt-6-astra | high | Reserved direct V2 | 🟢 Active/Healthy |"
                "| B | gpt-6-sol | high | General | 🟡 Pending |"
                ""
                "Total: 2; active: 1; reserved direct V2: 1; active reserved direct V2: 1."
                "Models: gpt-6-astra: 1, gpt-6-sol: 1."
                ""
                "## Workstreams"
                ""
                "| Workstream | State | PRs | Evidence | Detail |"
                "| --- | --- | ---: | ---: | --- |"
                "| Capture | 🟡 Pending | 0 | 0 | Needs native run |"
                "| Telemetry | 🟢 Active/Healthy | 1 | 2 | Queue ready |"
                ""
                "PRs: 1; evidence links: 2."
                ""
                "## Telemetry and capture"
                ""
                "- Telemetry readiness: 🟢 Active/Healthy"
                "- Workspace: `main-fsharp-dev`"
                "- Current queue: pending=0, pending unacknowledged=0, unacknowledged lossy=false"
                "- End-to-end capture acceptance: 🟡 Pending — no end-to-end capture acceptance claimed"
                ""
                "## Protected holds"
                ""
                "- 🟠 Blocked/Incomplete evidence — Authority write remains disabled"
                ""
                "## Checks"
                ""
                "| Check | State | Detail |"
                "| --- | --- | --- |"
                "| Build | 🔵 Completed/Info | Focused tests pass |"
                ""
                "## Risks"
                ""
                "| Risk | State | Detail |"
                "| --- | --- | --- |"
                "| Capture | 🟠 Blocked/Incomplete evidence | No accepted run |"
                ""
                "## Next actions"
                ""
                "- Collect genuine runner evidence"
                ""
                "## Last 5 completions"
                ""
                "| Time (UTC) | Item / workstream | Result | PR / evidence | Recorded roadmap head |"
                "| --- | --- | --- | --- | --- |"
            ] |> String.concat "\n" |> fun value -> value + "\n"
        Assert.Equal(expected, actual)
        Assert.Equal(actual, render (baseSnapshot ()))

    [<Fact>]
    let ``history uses UTC newest-first tie breaks and exactly five real rows`` () =
        let completion item minutes = {
            CompletedAt = at.AddMinutes(-minutes); Item = item; Workstream = "Core"
            Result = CompletedInfo; Link = Some(link "PR 123"); RecordedRoadmapHead = headB
        }
        let history = [ completion "old" 6.0; completion "B" 1.0; completion "four" 4.0
                        completion "A" 1.0; completion "five" 5.0; completion "three" 3.0 ]
        let actual = render { baseSnapshot () with CompletionHistory = history }
        let table = actual.Split("## Last 5 completions\n\n", StringSplitOptions.None).[1]
        let rows = table.Split('\n') |> Array.filter (fun line -> line.StartsWith("| 2026-"))
        Assert.Equal(5, rows.Length)
        Assert.Contains("| A / Core |", rows.[0])
        Assert.Contains("| B / Core |", rows.[1])
        Assert.Contains("| three / Core |", rows.[2])
        Assert.Contains("| four / Core |", rows.[3])
        Assert.Contains("| five / Core |", rows.[4])
        Assert.DoesNotContain("old / Core", table)
        Assert.Contains($"| `{headB}` |", rows.[0])
        Assert.Contains("[PR 123](<https://github.com/FS-GG/.github/pull/123>)", rows.[0])

        let four = render { baseSnapshot () with CompletionHistory = history |> List.take 4 }
        let fourRows = four.Split("## Last 5 completions\n\n", StringSplitOptions.None).[1].Split('\n')
                       |> Array.filter (fun line -> line.StartsWith("| 2026-"))
        Assert.Equal(4, fourRows.Length)

        let tie = { completion "A" 1.0 with Link = Some(link "AAA") }
        let reversed = { completion "A" 1.0 with Link = Some(link "ZZZ") }
        let first = render { baseSnapshot () with CompletionHistory = [ reversed; tie ] }
        let second = render { baseSnapshot () with CompletionHistory = [ tie; reversed ] }
        Assert.Equal(first, second)
        Assert.True(first.IndexOf("[AAA]", StringComparison.Ordinal) < first.IndexOf("[ZZZ]", StringComparison.Ordinal))

    [<Fact>]
    let ``all semantic markers carry visible text labels`` () =
        let expected = [
            ActiveHealthy, "🟢 Active/Healthy"; Pending, "🟡 Pending"
            BlockedIncompleteEvidence, "🟠 Blocked/Incomplete evidence"
            FailedUnsafe, "🔴 Failed/Unsafe"; CompletedInfo, "🔵 Completed/Info"
            Unknown, "🔘 Unknown"
        ]
        for state, label in expected do
            Assert.Equal(label, ProgressRenderer.statusText state)
        let checks = expected |> List.mapi (fun index (state, _) ->
            { Name = $"Check {index}"; State = state; Detail = "Evidence" })
        let actual = render { baseSnapshot () with Checks = checks }
        for _, label in expected do Assert.Contains(label, actual)

    [<Fact>]
    let ``lane total model effort and reservation claims must match detail`` () =
        let baseline = baseSnapshot ()
        refuses "declared total lane count" { baseline with DeclaredLaneCounts = { baseline.DeclaredLaneCounts with Total = 3 } }
        refuses "declared model counts" { baseline with DeclaredLaneCounts = { baseline.DeclaredLaneCounts with ByModel = [ Gpt6Sol, 2 ] } }
        refuses "declared direct V2 reservation count" { baseline with DeclaredLaneCounts = { baseline.DeclaredLaneCounts with ReservedDirectV2 = 0 } }
        refuses "declared active direct V2 reservation count" { baseline with DeclaredLaneCounts = { baseline.DeclaredLaneCounts with ActiveReservedDirectV2 = 0 } }
        refuses "lane IDs must be unique" { baseline with Lanes = [ baseline.Lanes.[0]; { baseline.Lanes.[1] with Id = "B" } ] }
        refuses "declared PR/evidence counts" { baseline with DeclaredEvidenceCounts = { Prs = 2; Evidence = 2 } }

    [<Fact>]
    let ``capture claim requires native runner turns exact applied Host receipt and later zero queue`` () =
        let baseline = baseSnapshot ()
        let runner, host, later = acceptedClaim ()
        let withClaim claim = { baseline with Telemetry = { baseline.Telemetry with Capture = claim } }
        let good = render (withClaim (AcceptedCaptureClaim(runner, host, later)))
        Assert.Contains("🔵 Completed/Info — end-to-end capture accepted", good)
        refuses "genuine native runner item" (withClaim (AcceptedCaptureClaim({ runner with Origin = SyntheticOrUnknown }, host, later)))
        refuses "native turn IDs and usage" (withClaim (AcceptedCaptureClaim({ runner with NativeTurns = [] }, host, later)))
        refuses "native turn usage" (withClaim (AcceptedCaptureClaim({ runner with NativeTurns = [ { TurnId = "turn"; InputTokens = 0; OutputTokens = 0 } ] }, host, later)))
        refuses "native turn IDs must be unique" (withClaim (AcceptedCaptureClaim({ runner with NativeTurns = runner.NativeTurns @ runner.NativeTurns }, host, later)))
        refuses "applied Host receipt" (withClaim (AcceptedCaptureClaim(runner, { host with Applied = false }, later)))
        refuses "exact runner workspace/item" (withClaim (AcceptedCaptureClaim(runner, { host with ItemId = "other" }, later)))
        refuses "later zero, lossless queue" (withClaim (AcceptedCaptureClaim(runner, host, { later with Pending = 1 })))
        refuses "later zero, lossless queue" (withClaim (AcceptedCaptureClaim(runner, host, { later with PendingUnacknowledged = 1 })))
        refuses "later zero, lossless queue" (withClaim (AcceptedCaptureClaim(runner, host, { later with UnacknowledgedLossy = true })))
        refuses "later than Host receipt" (withClaim (AcceptedCaptureClaim(runner, host, { later with ObservedAt = host.ObservedAt })))
        refuses "later queue workspace" (withClaim (AcceptedCaptureClaim(runner, host, { later with WorkspaceId = "other" })))
        refuses "runner item must be an HTTPS" (withClaim (AcceptedCaptureClaim({ runner with Evidence = { runner.Evidence with Url = "http://example.com" } }, host, later)))
        refuses "runner observation must be UTC" (withClaim (AcceptedCaptureClaim({ runner with ObservedAt = runner.ObservedAt.ToOffset(TimeSpan.FromHours(2.0)) }, host, later)))

    [<Fact>]
    let ``non-UTC completion times and future history refuse rather than shift`` () =
        let baseline = baseSnapshot ()
        let entry = {
            CompletedAt = at.AddMinutes(-1.0); Item = "GS2-14"; Workstream = "Core"
            Result = CompletedInfo; Link = None; RecordedRoadmapHead = headA
        }
        refuses "completion time must be UTC" { baseline with CompletionHistory = [ { entry with CompletedAt = entry.CompletedAt.ToOffset(TimeSpan.FromHours(2.0)) } ] }
        refuses "completion time exceeds report time" { baseline with CompletionHistory = [ { entry with CompletedAt = at.AddMinutes(1.0) } ] }
