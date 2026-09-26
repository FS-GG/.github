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
        Id = id; CurrentWork = if activity = Running then "Current task for " + id else ""
        Role = Worker; Activity = activity; Model = model; Effort = High
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
        CliStatus = None
        PeriodUsage = UnknownPeriodUsage
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
            Authenticated = true; CollectorVerified = true; EvidenceId = "post-host-workspace-status-1"
            Pending = 0; PendingUnacknowledged = 0; UnacknowledgedLossy = false
        }
        runner, host, queue

    let private authenticatedStatus () = {
        Authenticated = true; CollectorVerified = true; EvidenceId = "cli-status-observation-1"
        ObservedAt = at.AddMinutes(-1.0); WeeklyRemainingPercent = 26
        WeeklyResetLocal = DateTimeOffset(2026, 9, 30, 9, 28, 0, TimeSpan.FromHours(2.0))
        WeeklyResetTimeZone = "Europe/Vienna"
        ContextUsedTokens = 191000L; ContextCapacityTokens = 258000L
    }

    let private rootSessionId = "01a0d6e3-6928-7352-b724-f449e585c41c"
    let private childSessionId = "01a0d6e3-6928-7352-b724-f449e585c41d"

    let private nativeCounters input cached output : NativeTokenCounters = {
        InputTokens = input; CachedInputTokens = cached; OutputTokens = output
        TotalTokens = input + output
    }

    let private nativeEvent minutes ordinal counters percent : NativeTokenCountEvent = {
        ObservedAt = at.AddMinutes(-minutes); Ordinal = ordinal; Counters = counters
        PrimaryRate = percent |> Option.map (fun used -> {
            AccountScopeId = "collector-verified-account-1"
            LimitId = "codex"; WindowMinutes = 10080; UsedPercent = used
            ResetsAt = DateTimeOffset(2026, 9, 30, 7, 28, 0, TimeSpan.Zero)
        })
    }

    let private teamWindow () : TeamCounterWindow = {
        RootSessionId = rootSessionId
        WindowStart = at.AddMinutes(-10.0); WindowEnd = at
        DeclaredSessionCount = 2
        Sessions = [
            { SessionId = rootSessionId; ParentSessionId = None
              StartedAt = at.AddMinutes(-30.0); CompleteThrough = at
              HistoryComplete = true; CollectorVerified = true; EvidenceId = "root-jsonl-sha256-1"
              Events = [
                  nativeEvent 21.0 1L (nativeCounters 100L 20L 40L) (Some 70M)
                  nativeEvent 11.0 2L (nativeCounters 100L 20L 40L) (Some 70M)
                  nativeEvent 1.0 3L (nativeCounters 130L 25L 50L) (Some 72M)
              ] }
            { SessionId = childSessionId; ParentSessionId = Some rootSessionId
              StartedAt = at.AddMinutes(-14.0); CompleteThrough = at
              HistoryComplete = true; CollectorVerified = true; EvidenceId = "child-jsonl-sha256-1"
              Events = [ nativeEvent 5.0 1L (nativeCounters 20L 4L 10L) (Some 71M) ] }
        ]
    }

    let private render snapshot =
        match ProgressRenderer.renderSnapshot snapshot with
        | Ok markdown -> markdown
        | Error errors -> failwithf "unexpected refusal: %A" errors

    let private refuses reason snapshot =
        match ProgressRenderer.renderSnapshot snapshot with
        | Ok _ -> failwithf "unsupported claim accepted: %s" reason
        | Error errors -> Assert.Contains(reason, String.concat "; " errors)

    [<Fact>]
    let ``local JSONL counters render diagnostics without authenticated collector or Host capture`` () =
        let local = { teamWindow () with
                        Sessions = (teamWindow ()).Sessions
                                   |> List.map (fun session -> { session with CollectorVerified = false }) }
        let snapshot = { baseSnapshot () with PeriodUsage = LocalCounterDiagnostic local }
        let actual = render snapshot
        Assert.Contains("local JSONL diagnostic only; no authenticated collector or Host receipt", actual)
        Assert.Contains("team-wide 10-minute native token_count delta", actual)
        Assert.Contains("End-to-end capture acceptance: 🟡 Pending", actual)
        Assert.Equal(actual, render snapshot)
        let verifiedSessions =
            local.Sessions |> List.map (fun session -> { session with CollectorVerified = true })
        let falselyVerified = { local with Sessions = verifiedSessions }
        refuses "explicitly unverified complete-history provenance"
            { snapshot with PeriodUsage = LocalCounterDiagnostic falselyVerified }

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
                "| Lane | Current work | Role | Activity | Model | Effort | Reservation | State | Launch settings evidence |"
                "| --- | --- | --- | --- | --- | --- | --- | --- | --- |"
                "| A | Current task for A | Worker | Running | gpt-6-sol | high | Reserved direct V2 | 🟢 Active/Healthy | Explicit orchestrator spawn (spawn-record-1) |"
                "| B | — | Worker | Idle | gpt-6-astra | high | General | 🟡 Pending | — |"
                "| O | Current task for O | Orchestrator | Running | gpt-6-sol | high | General | 🟢 Active/Healthy | Explicit user instruction (visible-profile-1) |"
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
                "- Authenticated health observation: authenticated=True, ready=True, collector verified=True, observed=2026-09-25 11:58:00 UTC, evidence=health-receipt-1"
                "- Configured workspace observation: configured=True, collector verified=True, observed=2026-09-25 11:59:00 UTC, evidence=workspace-status-1"
                "- Current queue: pending=0, pending unacknowledged=0, unacknowledged lossy=false"
                "- End-to-end capture acceptance: 🟡 Pending — no end-to-end capture acceptance claimed"
                ""
                "## CLI status and token usage"
                ""
                "- Authenticated CLI /status weekly evidence: 🔘 Unknown — none supplied"
                "- Context occupancy: 🔘 Unknown — none supplied"
                "- Period usage: 🔘 Unknown — genuine native turn usage has not been supplied"
                "- Completed periods: 🔘 Unknown — no complete native team counter history"
                "- All-period team total: 🔘 Unknown — no complete native team counter history"
                "- Team mean per completed period: 🔘 Unknown — no complete native team counter window"
                "- Native weekly allowance: 🔘 Unknown — no fresh native primary.used_percent observation"
                "- Weekly exhaustion estimate: 🔘 Unknown — no qualified weekly percent slope"
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
    let ``authenticated CLI status renders weekly allowance and context occupancy without inventing period usage`` () =
        let baseline = { baseSnapshot () with CliStatus = Some(authenticatedStatus ()) }
        let actual = render baseline
        Assert.Equal(actual, render baseline)
        Assert.Contains("26% left; reset 2026-09-30 09:28 +02:00 (Europe/Vienna)", actual)
        Assert.Contains("observed 2026-09-25 11:59:00 UTC; provenance cli-status-observation-1", actual)
        Assert.Contains("191000/258000 tokens in current context window; not cumulative usage", actual)
        Assert.Contains("- Period usage: 🔘 Unknown — genuine native turn usage has not been supplied", actual)
        Assert.DoesNotContain("five-minute", actual)
        Assert.DoesNotContain("5-minute", actual)
        let changedContext = { baseline with CliStatus = Some { (authenticatedStatus ()) with ContextUsedTokens = 257000L } }
        Assert.Contains("- Period usage: 🔘 Unknown", render changedContext)

    [<Fact>]
    let ``CLI status needs authenticated timely provenance and plausible independent fields`` () =
        let baseline = baseSnapshot ()
        let status = authenticatedStatus ()
        let withStatus value = { baseline with CliStatus = Some value }
        refuses "authenticated collector-verified provenance" (withStatus { status with Authenticated = false })
        refuses "authenticated collector-verified provenance" (withStatus { status with CollectorVerified = false })
        refuses "authenticated collector-verified provenance" (withStatus { status with EvidenceId = " " })
        refuses "observation exceeds report time" (withStatus { status with ObservedAt = at.AddSeconds(1.0) })
        refuses "weekly remaining percent" (withStatus { status with WeeklyRemainingPercent = 101 })
        refuses "weekly reset must follow report time" (withStatus { status with WeeklyResetLocal = at.AddMinutes(-1.0) })
        refuses "local time zone" (withStatus { status with WeeklyResetTimeZone = "" })
        let utcReset = { status with WeeklyResetLocal = status.WeeklyResetLocal.ToUniversalTime(); WeeklyResetTimeZone = "Etc/UTC" }
        Assert.Contains("2026-09-30 07:28 +00:00 (Etc/UTC)", render (withStatus utcReset))
        refuses "context occupancy must fit its capacity" (withStatus { status with ContextUsedTokens = 258001L })

    [<Fact>]
    let ``period usage requires native runner turns and stays separate from CLI context occupancy`` () =
        let baseline = { baseSnapshot () with CliStatus = Some(authenticatedStatus ()) }
        let runner, _, _ = acceptedClaim ()
        let usage = {
            WindowStart = at.AddMinutes(-5.0); WindowEnd = at
            Runner = runner; CollectorVerified = true
        }
        let actual = render { baseline with PeriodUsage = NativeTurnPeriodUsage usage }
        Assert.Contains("input=14, output=7 native turn tokens", actual)
        Assert.Contains("191000/258000 tokens in current context window", actual)
        let withUsage value = { baseline with PeriodUsage = NativeTurnPeriodUsage value }
        refuses "collector-verified native runner provenance" (withUsage { usage with CollectorVerified = false })
        refuses "collector-verified native runner provenance" (withUsage { usage with Runner = { runner with Origin = SyntheticOrUnknown } })
        refuses "period runner workspace must match telemetry workspace"
            (withUsage { usage with Runner = { runner with WorkspaceId = "other-workspace" } })
        refuses "genuine native turn IDs and usage" (withUsage { usage with Runner = { runner with NativeTurns = [] } })
        refuses "positive native turn token counts"
            (withUsage { usage with Runner = { runner with NativeTurns = [ { TurnId = "native-turn-1"; InputTokens = 0; OutputTokens = 0 } ] } })

    [<Fact>]
    let ``team counter mean divides all completed root-anchored periods including an idle period`` () =
        let baseline = baseSnapshot ()
        let window = teamWindow ()
        let actual = render { baseline with PeriodUsage = NativeCounterPeriodUsage window }
        Assert.Equal(actual, render { baseline with PeriodUsage = NativeCounterPeriodUsage window })
        Assert.Contains("team-wide 10-minute native token_count delta 2026-09-25 11:50:00 UTC to 2026-09-25 12:00:00 UTC: input=50, cached input=9, noncached input=41, output=20, total=70; sessions=2", actual)
        Assert.Contains("3 completed 10-minute periods from 2026-09-25 11:30:00 UTC through 2026-09-25 12:00:00 UTC; zero-use periods included", actual)
        Assert.Contains("input=150, cached input=29, noncached input=121, output=60, total=210 tokens across all completed periods", actual)
        Assert.Contains("input=50.00, cached input=9.67, noncached input=40.33, output=20.00, total=70.00 team tokens/period", actual)
        Assert.Contains("- Team mean per completed period:", actual)
        Assert.DoesNotContain("tokens/session", actual)
        Assert.Contains("used=72.00%, remaining=28.00%", actual)
        Assert.Contains("approximately 2026-09-25 16:39:00 UTC at the continuous-use, account-wide weekly percent slope", actual)
        Assert.Contains("End-to-end capture acceptance: 🟡 Pending", actual)
        let changedEvent = { window.Sessions.Head.Events.[2] with Counters = nativeCounters 230L 25L 50L }
        let changedRoot = { window.Sessions.Head with Events = List.updateAt 2 changedEvent window.Sessions.Head.Events }
        let changedWindow = { window with Sessions = changedRoot :: window.Sessions.Tail }
        let changed = render { baseline with PeriodUsage = NativeCounterPeriodUsage changedWindow }
        Assert.Contains("total=170; sessions=2", changed)
        Assert.Contains("approximately 2026-09-25 16:39:00 UTC", changed)

        let flatRecentEvent =
            { window.Sessions.Head.Events.[1] with
                PrimaryRate = Some { window.Sessions.Head.Events.[1].PrimaryRate.Value with UsedPercent = 72M } }
        let flatRecentChildEvent =
            { window.Sessions.Tail.Head.Events.Head with
                PrimaryRate = Some { window.Sessions.Tail.Head.Events.Head.PrimaryRate.Value with UsedPercent = 72M } }
        let flatRecentRoot =
            { window.Sessions.Head with
                Events = List.updateAt 1 flatRecentEvent window.Sessions.Head.Events }
        let flatRecentChild =
            { window.Sessions.Tail.Head with Events = [ flatRecentChildEvent ] }
        let flatRecentWindow = { window with Sessions = [ flatRecentRoot; flatRecentChild ] }
        let flatRecent = render { baseline with PeriodUsage = NativeCounterPeriodUsage flatRecentWindow }
        Assert.Contains("approximately 2026-09-25 16:39:00 UTC", flatRecent)

        let allFlatFirst =
            { window.Sessions.Head.Events.Head with
                PrimaryRate = Some { window.Sessions.Head.Events.Head.PrimaryRate.Value with UsedPercent = 72M } }
        let allFlatRoot =
            { flatRecentRoot with Events = List.updateAt 0 allFlatFirst flatRecentRoot.Events }
        let allFlatWindow = { window with Sessions = [ allFlatRoot; flatRecentChild ] }
        let allFlat = render { baseline with PeriodUsage = NativeCounterPeriodUsage allFlatWindow }
        Assert.Contains("Weekly exhaustion estimate: 🔘 Unknown", allFlat)

        let foreignFirst =
            { window.Sessions.Head.Events.Head with
                PrimaryRate = Some { window.Sessions.Head.Events.Head.PrimaryRate.Value with
                                        AccountScopeId = "foreign-account" } }
        let foreignRoot =
            { flatRecentRoot with Events = List.updateAt 0 foreignFirst flatRecentRoot.Events }
        let incompatibleWindow = { window with Sessions = [ foreignRoot; flatRecentChild ] }
        let incompatible = render { baseline with PeriodUsage = NativeCounterPeriodUsage incompatibleWindow }
        Assert.Contains("Weekly exhaustion estimate: 🔘 Unknown", incompatible)

        let foreignResetFirst =
            { window.Sessions.Head.Events.Head with
                PrimaryRate = Some { window.Sessions.Head.Events.Head.PrimaryRate.Value with
                                        ResetsAt = at.AddDays(7.0) } }
        let foreignResetRoot =
            { flatRecentRoot with Events = List.updateAt 0 foreignResetFirst flatRecentRoot.Events }
        let foreignResetWindow = { window with Sessions = [ foreignResetRoot; flatRecentChild ] }
        let incompatibleReset = render { baseline with PeriodUsage = NativeCounterPeriodUsage foreignResetWindow }
        Assert.Contains("Weekly exhaustion estimate: 🔘 Unknown", incompatibleReset)

        let idleDescendants =
            [ for ordinal in 1 .. 28 ->
                { window.Sessions.Tail.Head with
                    SessionId = $"idle-descendant-{ordinal}"
                    ParentSessionId = Some childSessionId
                    Events = [] } ]
        let wholeTeam =
            { window with DeclaredSessionCount = 30
                          Sessions = window.Sessions @ idleDescendants }
        let wholeTeamText = render { baseline with PeriodUsage = NativeCounterPeriodUsage wholeTeam }
        Assert.Contains("total=70.00 team tokens/period", wholeTeamText)
        Assert.Contains("sessions=30", wholeTeamText)

    [<Fact>]
    let ``native counter claims fail closed on lineage provenance and arithmetic`` () =
        let baseline = baseSnapshot ()
        let window = teamWindow ()
        let withWindow value = { baseline with PeriodUsage = NativeCounterPeriodUsage value }
        let root = window.Sessions.Head
        let child = window.Sessions.Tail.Head
        refuses "declared native session count" (withWindow { window with DeclaredSessionCount = 30 })
        refuses "native session lineage" (withWindow { window with Sessions = [ root; { child with ParentSessionId = Some "foreign" } ] })
        refuses "collector-verified complete-history provenance"
            (withWindow { window with Sessions = [ { root with HistoryComplete = false }; child ] })
        refuses "native session history must cover the report window"
            (withWindow { window with Sessions = [ { root with CompleteThrough = at.AddSeconds(-1.0) }; child ] })
        let badComponents = { root.Events.[2] with Counters = { root.Events.[2].Counters with TotalTokens = 999L } }
        let badComponentsRoot = { root with Events = List.updateAt 2 badComponents root.Events }
        refuses "native token_count components disagree"
            (withWindow { window with Sessions = [ badComponentsRoot; child ] })
        let blankAccountEvent =
            { root.Events.[2] with
                PrimaryRate = Some { root.Events.[2].PrimaryRate.Value with AccountScopeId = "" } }
        let blankAccountRoot = { root with Events = List.updateAt 2 blankAccountEvent root.Events }
        refuses "native primary weekly rate limit is invalid"
            (withWindow { window with Sessions = [ blankAccountRoot; child ] })
        let decreased = { root.Events.[2] with Counters = nativeCounters 90L 19L 50L }
        let decreasedRoot = { root with Events = List.updateAt 2 decreased root.Events }
        refuses "native cumulative counters decreased"
            (withWindow { window with Sessions = [ decreasedRoot; child ] })
        let badCachedDelta = { root.Events.[2] with Counters = nativeCounters 130L 60L 50L }
        let badCachedRoot = { root with Events = List.updateAt 2 badCachedDelta root.Events }
        refuses "native cached input delta exceeds input delta"
            (withWindow { window with Sessions = [ badCachedRoot; child ] })
        refuses "native completed periods must be anchored at root session start"
            (withWindow { window with Sessions = [ { root with StartedAt = at.AddMinutes(-29.0) }; child ] })

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
    let ``running lane requires current work and renders it as escaped text`` () =
        let baseline = baseSnapshot ()
        let worker = baseline.Lanes.[1]
        refuses "running lane current work must be nonblank"
            (replaceWorker baseline { worker with CurrentWork = "  " })
        let described = replaceWorker baseline { worker with CurrentWork = "GS2-09.9 | held\nsource" }
        let markdown = render described
        Assert.Contains("| A | GS2-09.9 \\| held source | Worker | Running |", markdown)
        Assert.Equal(markdown, render described)

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
        refuses "authenticated collector-verified later queue evidence"
            (withClaim (AcceptedCaptureClaim(runner, host, { later with Authenticated = false })))
        refuses "authenticated collector-verified later queue evidence"
            (withClaim (AcceptedCaptureClaim(runner, host, { later with CollectorVerified = false })))
        refuses "authenticated collector-verified later queue evidence"
            (withClaim (AcceptedCaptureClaim(runner, host, { later with EvidenceId = "" })))
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
        Assert.Contains("| A | Current task for A | Worker | Running | gpt-6-sol | high | Reserved direct V2 | 🟡 Pending |", rendered)

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

    [<Fact>]
    let ``healthy readiness refuses stale authenticated health`` () =
        let baseline = baseSnapshot ()
        let health = baseline.Telemetry.HealthObservation.Value
        let telemetry = { baseline.Telemetry with
                            HealthObservation = Some { health with ObservedAt = at.AddMinutes(-6.0) } }
        refuses "healthy telemetry requires fresh authenticated health observation"
            { baseline with Telemetry = telemetry }

    [<Fact>]
    let ``healthy readiness refuses stale configured workspace status`` () =
        let baseline = baseSnapshot ()
        let workspace = baseline.Telemetry.WorkspaceObservation.Value
        let telemetry = { baseline.Telemetry with
                            WorkspaceObservation = Some { workspace with ObservedAt = at.AddMinutes(-6.0) } }
        refuses "healthy telemetry requires fresh configured workspace observation"
            { baseline with Telemetry = telemetry }

    [<Fact>]
    let ``five minute observation boundary is accepted without claiming capture`` () =
        let baseline = baseSnapshot ()
        let health = baseline.Telemetry.HealthObservation.Value
        let workspace = baseline.Telemetry.WorkspaceObservation.Value
        let telemetry = {
            baseline.Telemetry with
                HealthObservation = Some { health with ObservedAt = at.AddMinutes(-5.0) }
                WorkspaceObservation = Some { workspace with ObservedAt = at.AddMinutes(-5.0) }
        }
        let markdown = render { baseline with Telemetry = telemetry }
        Assert.Contains("- Telemetry readiness: 🟢 Active/Healthy", markdown)
        Assert.Contains("- End-to-end capture acceptance: 🟡 Pending", markdown)
