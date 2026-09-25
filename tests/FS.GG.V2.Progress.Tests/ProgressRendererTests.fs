namespace FS.GG.V2.Progress.Tests

open System
open Xunit
open FS.GG.V2.Progress

module ProgressRendererTests =
    let private at = DateTimeOffset(2026, 9, 25, 12, 0, 0, TimeSpan.Zero)
    let private headA = String.replicate 40 "a"
    let private headB = String.replicate 40 "b"
    let private link label = { Label = label; Url = "https://github.com/FS-GG/.github/pull/123" }

    let private explicitLaunch model = {
        Model = model; Effort = High; Source = ExplicitOrchestratorSpawn; EvidenceId = "spawn-record-1"
    }

    let private lane id activity model reservation state launch = {
        Id = id; Role = Worker; Activity = activity; Model = model; Effort = High
        Reservation = reservation; State = state; Launch = launch
    }

    let private baseSnapshot () : ProgressSnapshot = {
        AsOf = at
        RoadmapHead = headA
        Lanes = [
            lane "B" Idle Gpt6Astra General Pending None
            lane "A" Running Gpt6Sol DirectV2 ActiveHealthy (Some(explicitLaunch Gpt6Sol))
            { lane "O" Running Gpt6Sol General ActiveHealthy
                (Some { Model = Gpt6Sol; Effort = High; Source = ExplicitUserInstruction;
                        EvidenceId = "visible-profile-1" }) with Role = Orchestrator }
        ]
        DeclaredLaneCounts = {
            Total = 3; Active = 2; ReservedDirectV2 = 1; ActiveReservedDirectV2 = 1
            ByModel = [ Gpt6Astra, 1; Gpt6Sol, 2 ]
        }
        Workstreams = [
            { Name = "Telemetry"; State = ActiveHealthy; Detail = "Queue ready"; PrCount = 1; EvidenceCount = 2 }
            { Name = "Capture"; State = Pending; Detail = "Needs native run"; PrCount = 0; EvidenceCount = 0 }
        ]
        DeclaredEvidenceCounts = { Prs = 1; Evidence = 2 }
        Telemetry = {
            WorkspaceId = "main-fsharp-dev"; Readiness = ActiveHealthy
            HealthObservation = Some {
                Authenticated = true; Ready = true; CollectorVerified = true
                ObservedAt = at.AddMinutes(-2.0); EvidenceId = "health-receipt-1"
            }
            WorkspaceObservation = Some {
                Configured = true; CollectorVerified = true; WorkspaceId = "main-fsharp-dev"
                Pending = 0; PendingUnacknowledged = 0; UnacknowledgedLossy = false
                ObservedAt = at.AddMinutes(-1.0); EvidenceId = "workspace-status-1"
            }
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

    let private replaceWorker (baseline: ProgressSnapshot) worker =
        { baseline with Lanes = [ baseline.Lanes.[0]; worker; baseline.Lanes.[2] ] }

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
                "| Lane | Role | Activity | Model | Effort | Reservation | State | Launch settings evidence |"
                "| --- | --- | --- | --- | --- | --- | --- | --- |"
                "| A | Worker | Running | gpt-6-sol | high | Reserved direct V2 | 🟢 Active/Healthy | Explicit orchestrator spawn (spawn-record-1) |"
                "| B | Worker | Idle | gpt-6-astra | high | General | 🟡 Pending | — |"
                "| O | Orchestrator | Running | gpt-6-sol | high | General | 🟢 Active/Healthy | Explicit user instruction (visible-profile-1) |"
                ""
                "Total: 3; active: 2; reserved direct V2: 1; active reserved direct V2: 1."
                "Models: gpt-6-astra: 1, gpt-6-sol: 2."
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
                "- Authenticated health observation: authenticated=True, ready=True, collector verified=True, evidence=health-receipt-1"
                "- Configured workspace observation: configured=True, collector verified=True, evidence=workspace-status-1"
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
        refuses "declared total lane count" { baseline with DeclaredLaneCounts = { baseline.DeclaredLaneCounts with Total = 4 } }
        refuses "declared model counts" { baseline with DeclaredLaneCounts = { baseline.DeclaredLaneCounts with ByModel = [ Gpt6Sol, 2 ] } }
        refuses "declared direct V2 reservation count" { baseline with DeclaredLaneCounts = { baseline.DeclaredLaneCounts with ReservedDirectV2 = 0 } }
        refuses "declared active direct V2 reservation count" { baseline with DeclaredLaneCounts = { baseline.DeclaredLaneCounts with ActiveReservedDirectV2 = 0 } }
        refuses "lane IDs must be unique" (replaceWorker baseline { baseline.Lanes.[1] with Id = "B" })
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
            Result = CompletedInfo; Link = Some(link "PR 123"); RecordedRoadmapHead = headA
        }
        refuses "completion time must be UTC" { baseline with CompletionHistory = [ { entry with CompletedAt = entry.CompletedAt.ToOffset(TimeSpan.FromHours(2.0)) } ] }
        refuses "completion time exceeds report time" { baseline with CompletionHistory = [ { entry with CompletedAt = at.AddMinutes(1.0) } ] }

    [<Fact>]
    let ``an Astra reserved lane cannot stand in for a Sol high direct V2 worker`` () =
        let baseline = baseSnapshot ()
        let astra = { baseline.Lanes.[1] with Model = Gpt6Astra; Launch = Some(explicitLaunch Gpt6Astra) }
        refuses "active V2 worker requires explicit gpt-6-sol/high spawn evidence"
            { replaceWorker baseline astra with
                DeclaredLaneCounts = { baseline.DeclaredLaneCounts with ByModel = [ Gpt6Astra, 2; Gpt6Sol, 1 ] } }

    [<Fact>]
    let ``healthy telemetry without authenticated and configured observations cannot qualify`` () =
        let baseline = baseSnapshot ()
        refuses "healthy telemetry requires authenticated health and configured workspace observations"
            { baseline with Telemetry = { baseline.Telemetry with HealthObservation = None; WorkspaceObservation = None } }

    [<Fact>]
    let ``completion table refuses pending and linkless rows`` () =
        let entry = {
            CompletedAt = at.AddMinutes(-1.0); Item = "GS2-14"; Workstream = "Core"
            Result = Pending; Link = None; RecordedRoadmapHead = headA
        }
        refuses "completion requires a terminal result and evidence link"
            { baseSnapshot () with CompletionHistory = [ entry ] }

    [<Fact>]
    let ``running is independent of semantic health and explicit launch evidence is mandatory`` () =
        let baseline = baseSnapshot ()
        let worker = baseline.Lanes.[1]
        let runningPending = { worker with State = Pending }
        let rendered = render (replaceWorker baseline runningPending)
        Assert.Contains("Total: 3; active: 2; reserved direct V2: 1; active reserved direct V2: 1.", rendered)
        Assert.Contains("| A | Worker | Running | gpt-6-sol | high | Reserved direct V2 | 🟡 Pending |", rendered)

        refuses "running lane requires explicit launch settings evidence"
            (replaceWorker baseline { worker with Launch = None })
        refuses "lane model/effort disagrees with explicit launch settings"
            (replaceWorker baseline { worker with Launch = Some { (explicitLaunch Gpt6Sol) with Effort = Medium } })
        refuses "runtime self-introspection is not launch settings provenance"
            (replaceWorker baseline { worker with Launch = Some { (explicitLaunch Gpt6Sol) with Source = RuntimeSelfIntrospection } })
        refuses "active reserved direct V2 worker is required"
            { replaceWorker baseline { worker with Activity = Idle } with
                DeclaredLaneCounts = { baseline.DeclaredLaneCounts with Active = 1; ActiveReservedDirectV2 = 0 } }
        refuses "direct V2 reservation requires a worker role"
            { replaceWorker baseline { worker with Role = Orchestrator } with
                DeclaredLaneCounts = { baseline.DeclaredLaneCounts with ActiveReservedDirectV2 = 0 } }

    [<Fact>]
    let ``healthy telemetry needs separately verified health and configured workspace facts`` () =
        let baseline = baseSnapshot ()
        let health = baseline.Telemetry.HealthObservation.Value
        let workspace = baseline.Telemetry.WorkspaceObservation.Value
        let change telemetry = { baseline with Telemetry = telemetry }
        refuses "healthy telemetry requires authenticated health and configured workspace observations"
            (change { baseline.Telemetry with HealthObservation = Some { health with Authenticated = false } })
        refuses "healthy telemetry requires authenticated health and configured workspace observations"
            (change { baseline.Telemetry with HealthObservation = Some { health with CollectorVerified = false } })
        refuses "healthy telemetry requires authenticated health and configured workspace observations"
            (change { baseline.Telemetry with WorkspaceObservation = Some { workspace with Configured = false } })
        refuses "healthy telemetry requires authenticated health and configured workspace observations"
            (change { baseline.Telemetry with WorkspaceObservation = Some { workspace with CollectorVerified = false } })
        refuses "workspace observation queue disagrees with telemetry queue"
            (change { baseline.Telemetry with WorkspaceObservation = Some { workspace with Pending = 1 } })

    [<Fact>]
    let ``history accepts only terminal outcomes with evidence`` () =
        let baseline = baseSnapshot ()
        let entry = {
            CompletedAt = at.AddMinutes(-1.0); Item = "GS2-14"; Workstream = "Core"
            Result = CompletedInfo; Link = Some(link "PR 123"); RecordedRoadmapHead = headA
        }
        for status in [ ActiveHealthy; Pending; BlockedIncompleteEvidence; Unknown ] do
            refuses "completion requires a terminal result and evidence link"
                { baseline with CompletionHistory = [ { entry with Result = status } ] }
        refuses "completion requires a terminal result and evidence link"
            { baseline with CompletionHistory = [ { entry with Link = None } ] }
        Assert.Contains("🔴 Failed/Unsafe", render { baseline with CompletionHistory = [ { entry with Result = FailedUnsafe } ] })

    [<Fact>]
    let ``Astra orchestrator cannot satisfy explicit Sol high profile`` () =
        let baseline = baseSnapshot ()
        let orchestrator = {
            baseline.Lanes.[2] with
                Model = Gpt6Astra
                Launch = Some { Model = Gpt6Astra; Effort = High;
                                Source = ExplicitUserInstruction; EvidenceId = "visible-profile-1" }
        }
        refuses "running orchestrator requires explicit gpt-6-sol/high visible-profile evidence"
            { baseline with Lanes = [ baseline.Lanes.[0]; baseline.Lanes.[1]; orchestrator ];
                            DeclaredLaneCounts = {
                                baseline.DeclaredLaneCounts with ByModel = [ Gpt6Astra, 2; Gpt6Sol, 1 ]
                            } }
        refuses "running orchestrator requires explicit gpt-6-sol/high visible-profile evidence"
            { baseline with Lanes = [ baseline.Lanes.[0]; baseline.Lanes.[1];
                                      { baseline.Lanes.[2] with Launch = Some(explicitLaunch Gpt6Sol) } ] }

    [<Fact>]
    let ``active report requires exactly one orchestrator`` () =
        let baseline = baseSnapshot ()
        refuses "active progress snapshot requires exactly one orchestrator"
            { baseline with Lanes = baseline.Lanes |> List.take 2;
                            DeclaredLaneCounts = { baseline.DeclaredLaneCounts with Total = 2; Active = 1;
                                                                                  ByModel = [ Gpt6Astra, 1; Gpt6Sol, 1 ] } }
        refuses "active progress snapshot requires exactly one orchestrator"
            { baseline with Lanes = baseline.Lanes @ [ { baseline.Lanes.[2] with Id = "O2" } ];
                            DeclaredLaneCounts = { baseline.DeclaredLaneCounts with Total = 4; Active = 3;
                                                                                  ByModel = [ Gpt6Astra, 1; Gpt6Sol, 3 ] } }

    [<Fact>]
    let ``worker instruction alone is not an explicit spawn record`` () =
        let baseline = baseSnapshot ()
        let worker = baseline.Lanes.[1]
        refuses "active V2 worker requires explicit gpt-6-sol/high spawn evidence"
            (replaceWorker baseline { worker with Launch = Some { (explicitLaunch Gpt6Sol) with Source = ExplicitUserInstruction } })
