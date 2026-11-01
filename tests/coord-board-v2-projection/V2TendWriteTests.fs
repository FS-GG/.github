/// The board-v2 tend writer (FS-GG/.github#5136 phase 2 PR 4; design §5, §6, §8).
module FS.GG.Coord.GitHub.Tests.V2TendWriteTests

open System
open System.IO
open System.Text.Json.Nodes
open Xunit
open FS.GG.Coord.GitHub
open FS.GG.Coord.GitHub.Transport
open FS.GG.Coord.GitHub.Tests.TendFakeGitHub

let private temporary () =
    let path = Path.Combine(Path.GetTempPath(), "tend-write-" + Guid.NewGuid().ToString("N"))
    Directory.CreateDirectory(path, UnixFileMode.UserRead ||| UnixFileMode.UserWrite ||| UnixFileMode.UserExecute) |> ignore
    path

type private Harness =
    { Board: Board
      Root: string
      Waits: Collections.Generic.List<TimeSpan>
      Context: V2TendWrite.Context }

let private harness (run: string) (root: string) (board: Board) =
    let closable = Collections.Generic.HashSet<string>()
    let waits = Collections.Generic.List<TimeSpan>()
    { Board = board
      Root = root
      Waits = waits
      Context =
        { Reads = board.Transport
          Send = V2TendWrite.allowlisted projectId closable.Contains board.Mutate
          Custody = V2TendWrite.Custody(Path.Combine(root, "tend"), run, ignore)
          Gate = V2TendWrite.GateCustody(Path.Combine(root, "gate"), ignore)
          Clock = TimeProvider.System
          Pacer = V2TendWrite.Pacer(TimeProvider.System, (fun span -> waits.Add span), TimeSpan.FromSeconds 1.0)
          Closable = closable
          Settings = V2TendWrite.defaultSettings projectId (set [ "EHotwagner" ]) } }

let private item ref content values =
    { ContentId = content; Ref = ref; Open = true; StateReason = None; Parent = Some "FS-GG/x#1"; Values = Map.ofList values; Archived = false }

/// A snapshot whose cards agree with the fake board's items.
let private snapshotOf (board: Board) (prs: (string * V2Tend.LinkedPr list) list) : V2Tend.Snapshot =
    let status = function "Backlog" -> V2Tend.Backlog | "Ready" -> V2Tend.Ready | "In progress" -> V2Tend.InProgress | "In review" -> V2Tend.InReview | "Blocked" -> V2Tend.Blocked | _ -> V2Tend.Done
    let cards =
        [ for KeyValue(itemId, i) in board.Items ->
            ({ ItemId = itemId; Ref = i.Ref; Kind = V2Tend.Issue; Open = i.Open; CreatedAt = DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero)
               UpdatedAt = DateTimeOffset(2026, 10, 30, 0, 0, 0, TimeSpan.Zero); ClosedAt = (if i.Open then None else Some(DateTimeOffset(2026, 10, 30, 0, 0, 0, TimeSpan.Zero)))
               Status = i.Values.TryFind "Status" |> Option.map status; StatusUpdatedAt = None
               WorkLevel = i.Values.TryFind "Work level" |> Option.map (function "Outcome" -> V2Tend.Outcome | _ -> V2Tend.Delivery)
               Track = i.Values.TryFind "Track" |> Option.map (function "Active delivery" -> V2Tend.ActiveDelivery | _ -> V2Tend.FollowUp)
               Roadmap = i.Values.TryFind "Roadmap"; Parent = i.Parent; SubIssues = []; SubIssuesComplete = true; Blockers = []; BlockersComplete = true
               MarkerBlockers = Some []; Prs = (prs |> List.tryPick (fun (id, p) -> if id = itemId then Some p else None) |> Option.defaultValue []); TimelineComplete = true
               Intended = None; BlockedOn = None; AcceptanceEvidence = None; Enrolled = false; EnrolledRoadmap = None; TendClosedBefore = false } : V2Tend.Card) ]
    { ObservedAt = DateTimeOffset(2026, 11, 1, 12, 0, 0, TimeSpan.Zero); Cards = cards; Ledger = [] }

let private effect id itemId ref field preimage target rules depends : V2Tend.Effect =
    { Id = id; ItemId = itemId; Ref = ref; Field = field; Preimage = preimage; Target = target; Rules = rules; DependsOn = depends }

let private plan (effects: V2Tend.Effect list) (closures: V2Tend.Closure list) : V2Tend.Plan =
    { Ruleset = V2Tend.ruleset; ObservedAt = DateTimeOffset(2026, 11, 1, 12, 0, 0, TimeSpan.Zero); Apply = effects; WouldApply = []; Deferred = []
      Propose = []; Refuse = []; T1iComparison = { Pairs = 0; Disagreements = 0 }; Close = closures; WouldClose = [] }

let private run (h: Harness) (input: V2Tend.Plan) (snapshot: V2Tend.Snapshot) =
    let uncertain, _ = V2TendWrite.sweepOwn h.Context
    let records, _ = h.Context.Gate.Records()
    let gateUncertain, gateDeferred = V2TendWrite.gateHolds records
    V2TendWrite.apply h.Context { Plan = input; Snapshot = snapshot; OwnUncertain = uncertain; GateUncertain = gateUncertain; GateDeferred = gateDeferred }

let private request document variables subject : Request =
    { Method = "POST"; Path = "graphql"; Query = []; Body = Query(document, variables); Budget = GraphQl; IfNoneMatch = None; Subject = subject }

// ---------------------------------------------------------------------------------------------------

[<Fact>]
let ``the allowlist refuses hand-written issue and foreign mutations before dispatch`` () =
    let board = Board()
    let mutable sent = 0
    let closable = set [ "I_delivery" ]
    let send = V2TendWrite.allowlisted projectId closable.Contains (fun r -> sent <- sent + 1; board.Mutate r)
    let refused =
        [ // closeIssue on an Outcome, a Blocked Delivery or any issue with no running C1 close
          request "mutation($issue: ID!, $c0: String!) { closeIssue(input: {issueId: $issue, clientMutationId: $c0}) { clientMutationId } }" [ "issue", VId "I_outcome"; "c0", VString "x" ] "I_outcome"
          // the subject is closable but the document closes another issue through a literal
          request "mutation($issue: ID!) { closeIssue(input: {issueId: \"I_other\"}) { clientMutationId } }" [ "issue", VId "I_delivery" ] "I_delivery"
          // the subject is closable but the issue id comes from another variable
          request "mutation($issue: ID!, $other: ID!) { closeIssue(input: {issueId: $other}) { clientMutationId } }" [ "issue", VId "I_delivery"; "other", VId "I_other" ] "I_delivery"
          request "mutation($issue: ID!) { updateIssue(input: {id: $issue}) { clientMutationId } }" [ "issue", VId "I_delivery" ] "I_delivery"
          request "mutation($issue: ID!, $child: ID!) { addSubIssue(input: {issueId: $issue, subIssueId: $child}) { clientMutationId } }" [ "issue", VId "I_delivery"; "child", VId "I_c" ] "I_delivery"
          request "mutation($issue: ID!) { reopenIssue(input: {issueId: $issue}) { clientMutationId } }" [ "issue", VId "I_delivery" ] "I_delivery"
          // an issue mutation aliased beside an allowed project field
          request "mutation($project: ID!, $item: ID!, $issue: ID!) { a: archiveProjectV2Item(input: {projectId: $project, itemId: $item}) { clientMutationId } b: closeIssue(input: {issueId: $issue}) { clientMutationId } }" [ "project", VId projectId; "item", VId "PVTI_1"; "issue", VId "I_delivery" ] "I_delivery"
          request "mutation($project: ID!, $item: ID!, $issue: ID!) { a: archiveProjectV2Item(input: {projectId: $project, itemId: $item}) { clientMutationId } b: updateIssue(input: {id: $issue}) { clientMutationId } }" [ "project", VId projectId; "item", VId "PVTI_1"; "issue", VId "I_delivery" ] "PVTI_1"
          // a project mutation on another project, by variable or by literal
          request "mutation($project: ID!, $item: ID!) { archiveProjectV2Item(input: {projectId: $project, itemId: $item}) { clientMutationId } }" [ "project", VId "PVT_other"; "item", VId "PVTI_1" ] "PVTI_1"
          request "mutation($project: ID!, $item: ID!) { archiveProjectV2Item(input: {projectId: \"PVT_other\", itemId: $item}) { clientMutationId } }" [ "project", VId projectId; "item", VId "PVTI_1" ] "PVTI_1"
          request "mutation($project: ID!, $p: ID!, $item: ID!) { archiveProjectV2Item(input: {projectId: $p, itemId: $item}) { clientMutationId } }" [ "project", VId projectId; "p", VId "PVT_other"; "item", VId "PVTI_1" ] "PVTI_1"
          // a foreign item through a literal, beside the bound project
          request "mutation($project: ID!, $item: ID!) { archiveProjectV2Item(input: {projectId: $project, itemId: \"PVTI_foreign\"}) { clientMutationId } }" [ "project", VId projectId; "item", VId "PVTI_1" ] "PVTI_1"
          // a closable issue mutation followed by another issue mutation on the same variable
          request "mutation($issue: ID!) { b: closeIssue(input: {issueId: $issue}) { clientMutationId } c: updateIssue(input: {id: $issue}) { clientMutationId } }" [ "issue", VId "I_delivery" ] "I_delivery"
          // a variable declared twice, a variable supplied twice
          request "mutation($issue: ID!, $issue: ID!, $c0: String!) { closeIssue(input: {issueId: $issue, stateReason: COMPLETED, clientMutationId: $c0}) { clientMutationId } }" [ "issue", VId "I_delivery"; "c0", VString "x" ] "I_delivery"
          request "mutation($issue: ID!, $c0: String!) { closeIssue(input: {issueId: $issue, stateReason: COMPLETED, clientMutationId: $c0}) { clientMutationId } }" [ "issue", VId "I_delivery"; "issue", VId "I_other"; "c0", VString "x" ] "I_delivery"
          // closeIssue on the closable issue with another state reason, or none
          request "mutation($issue: ID!, $c0: String!) { closeIssue(input: {issueId: $issue, stateReason: NOT_PLANNED, clientMutationId: $c0}) { clientMutationId } }" [ "issue", VId "I_delivery"; "c0", VString "x" ] "I_delivery"
          request "mutation($issue: ID!, $c0: String!) { closeIssue(input: {issueId: $issue, clientMutationId: $c0}) { clientMutationId } }" [ "issue", VId "I_delivery"; "c0", VString "x" ] "I_delivery"
          // two operations, a query, an unparseable document
          request "mutation($project: ID!, $item: ID!) { archiveProjectV2Item(input: {projectId: $project, itemId: $item}) { clientMutationId } } mutation { x }" [ "project", VId projectId; "item", VId "PVTI_1" ] "PVTI_1"
          request "query { viewer { login } }" [] "PVTI_1"
          request "mutation { archiveProjectV2Item(input: {" [] "PVTI_1" ]
    for r in refused do
        Assert.True(send r |> Result.isError, (match r.Body with Query(d, _) -> d | _ -> ""))
    Assert.Equal(0, sent)
    Assert.Empty(board.Mutations)

[<Fact>]
let ``every TendChange case renders only allowlisted root fields, and together exactly the allowlist`` () =
    let delivery =
        let item : V2TendWrite.ItemState =
            { ItemId = "PVTI_d"; ProjectId = projectId; Archived = false; ContentId = "I_d"; Ref = "FS-GG/x#5"; Open = true; Parent = Some "FS-GG/x#1"
              Fields = []; Values = Map.empty; Named = Map.ofList [ "Work level", "Delivery"; "Status", "In review" ]; Prs = Map.empty }
        let issue : V2TendWrite.IssueReread = { IssueId = "I_d"; Ref = "FS-GG/x#5"; Open = true; CrossReferences = []; TendClosed = false }
        match V2TendWrite.deliveryToClose { Id = "C1:PVTI_d:closed"; ItemId = "PVTI_d"; Ref = "FS-GG/x#5"; MergedPrs = [] } item issue (V2TendWrite.closingComment []) with
        | Ok d -> d
        | Error e -> failwith e
    let changes =
        [ V2TendWrite.SetFieldValue(projectId, "PVTI_1", [ "F_Status", V2TendWrite.OptionValue "O_1"; "F_Roadmap", V2TendWrite.TextValue "x" ])
          V2TendWrite.ClearFieldValue(projectId, "PVTI_1", "F_Status")
          V2TendWrite.AddItem(projectId, "I_1")
          V2TendWrite.RemoveCreatedItem(projectId, "PVTI_1")
          V2TendWrite.ArchiveItem(projectId, "PVTI_1")
          V2TendWrite.CloseDelivery delivery ]
    let fields =
        [ for change in changes do
            for r in V2TendWrite.render "m" change do
                Assert.Equal(Ok(), V2TendWrite.admits projectId (fun i -> i = "I_d") r)
                match r.Body with
                | Query(d, _) -> yield! (V2TendReads.parseDocument d).Value.Operations.Head.RootFields
                | _ -> () ]
        |> Set.ofList
    Assert.Equal<Set<string>>(Set.union V2TendWrite.projectRootFields V2TendWrite.issueRootFields, fields)
    // The close documents are refused once the close is no longer running.
    for r in V2TendWrite.render "m" (V2TendWrite.CloseDelivery delivery) do
        Assert.True(V2TendWrite.admits projectId (fun _ -> false) r |> Result.isError)

[<Fact>]
let ``a field effect is re-read immediately before its one mutation, held in custody and verified by readback`` () =
    let root = temporary ()
    try
        let board = Board()
        board.AddItem("PVTI_a", item "FS-GG/x#10" "I_a" [ "Status", "Ready"; "Work level", "Delivery" ])
        board.AddItem("PVTI_b", item "FS-GG/x#11" "I_b" [ "Status", "Ready" ])
        let h = harness "run1" root board
        let snapshot = snapshotOf board []
        let effects =
            [ effect "W1h:a" "PVTI_a" "FS-GG/x#10" V2Tend.TrackField None "Follow-up" [ "T1f" ] []
              effect "W1h:b" "PVTI_b" "FS-GG/x#11" V2Tend.WorkLevelField None "Delivery" [ "W1h" ] []
              effect "T1f:b" "PVTI_b" "FS-GG/x#11" V2Tend.TrackField None "Follow-up" [ "T1f" ] [ "W1h:b" ] ]
        let outcomes = run h (plan effects []) snapshot
        Assert.Equal<string list>([ "written"; "written" ], outcomes |> List.map (fun o -> o.Result))
        Assert.Equal(Some "Follow-up", board.Items.["PVTI_a"].Values.TryFind "Track")
        Assert.Equal(Some "Delivery", board.Items.["PVTI_b"].Values.TryFind "Work level")
        // One mutation per item, both fields of item b in one document.
        Assert.Equal(2, board.Mutations.Length)
        // In the call log, the request immediately before each mutation is the re-read of the same item.
        let calls = board.Calls
        for index, call in List.indexed calls do
            if call.Kind = "mutation" then
                let previous = calls.[index - 1]
                Assert.True(previous.Kind = "query" && previous.Subject = call.Subject && previous.Document.Contains "node(id: $item)", sprintf "call %d" index)
        // The second mutation waited for the pacing interval.
        Assert.NotEmpty(h.Waits)
        Assert.True(h.Waits |> Seq.forall (fun w -> w > TimeSpan.Zero && w <= TimeSpan.FromSeconds 1.0))
        // Custody records are verified, one per item.
        let records, malformed = h.Context.Custody.Records()
        Assert.Equal(0, malformed)
        Assert.Equal<string list>([ "verified"; "verified" ], records |> List.map (fun (_, r) -> string r["state"]))
    finally Directory.Delete(root, true)

[<Fact>]
let ``a readback that shows any other change is uncertain, and the record stays possible-send`` () =
    let root = temporary ()
    try
        let board = Board()
        board.AddItem("PVTI_a", item "FS-GG/x#10" "I_a" [ "Status", "Ready" ])
        // Another writer, outside the lock, sets the Roadmap while the send is in flight.
        board.BeforeMutation <- fun _ -> board.Items.["PVTI_a"].Values <- board.Items.["PVTI_a"].Values.Add("Roadmap", "elsewhere")
        let h = harness "run1" root board
        let outcomes = run h (plan [ effect "T1f:a" "PVTI_a" "FS-GG/x#10" V2Tend.TrackField None "Follow-up" [ "T1f" ] [] ] []) (snapshotOf board [])
        Assert.Equal(("uncertain", "readback-differs"), (outcomes.Head.Result, outcomes.Head.Detail))
        let records, _ = h.Context.Custody.Records()
        let _, record = records.Head
        Assert.Equal("possible-send", string record.["state"])
    finally Directory.Delete(root, true)

[<Fact>]
let ``a difference between the snapshot and the re-read refuses the item and sends nothing`` () =
    let root = temporary ()
    try
        let board = Board()
        board.AddItem("PVTI_a", item "FS-GG/x#10" "I_a" [ "Status", "Ready" ])
        let snapshot = snapshotOf board []
        board.Items.["PVTI_a"].Values <- board.Items.["PVTI_a"].Values.Add("Status", "Blocked") // a person moved it
        let h = harness "run1" root board
        let outcomes = run h (plan [ effect "S3:a" "PVTI_a" "FS-GG/x#10" V2Tend.StatusField (Some "Ready") "In progress" [ "S3b" ] [] ] []) snapshot
        Assert.Equal(("refused", "field-changed:Status"), (outcomes.Head.Result, outcomes.Head.Detail))
        Assert.Empty(board.Mutations)
    finally Directory.Delete(root, true)

[<Fact>]
let ``a deciding PR that changed since the snapshot refuses an S3 move`` () =
    let root = temporary ()
    try
        let board = Board()
        board.AddItem("PVTI_a", item "FS-GG/x#10" "I_a" [ "Status", "In progress" ])
        board.AddPr("FS-GG/x#100", { State = "OPEN"; Draft = false; MergedAt = None; Body = "Refs #10"; MergeOid = ""; Comments = [] })
        let linked : V2Tend.LinkedPr = { Id = "FS-GG/x#100"; State = V2Tend.PrOpen; Draft = false; Linkage = V2Tend.Strict; UpdatedAt = DateTimeOffset.UtcNow; MergedAt = None; Continues = false; Completes = false }
        let snapshot = snapshotOf board [ "PVTI_a", [ linked ] ]
        board.Prs.["FS-GG/x#100"].State <- "CLOSED"
        let h = harness "run1" root board
        let outcomes = run h (plan [ effect "S3:a" "PVTI_a" "FS-GG/x#10" V2Tend.StatusField (Some "In progress") "In review" [ "S3" ] [] ] []) snapshot
        Assert.Equal(("refused", "deciding-pr-changed"), (outcomes.Head.Result, outcomes.Head.Detail))
        Assert.Empty(board.Mutations)
    finally Directory.Delete(root, true)

[<Fact>]
let ``without a custody record nothing is sent`` () =
    let root = temporary ()
    try
        let board = Board()
        board.AddItem("PVTI_a", item "FS-GG/x#10" "I_a" [ "Status", "Ready" ])
        File.WriteAllText(Path.Combine(root, "tend"), "not a directory")
        let h = harness "run1" root board
        let outcomes = run h (plan [ effect "T1f:a" "PVTI_a" "FS-GG/x#10" V2Tend.TrackField None "Follow-up" [ "T1f" ] [] ] []) (snapshotOf board [])
        Assert.Equal(("refused", "refused-custody"), (outcomes.Head.Result, outcomes.Head.Detail))
        Assert.Empty(board.Mutations)
    finally Directory.Delete(root, true)

[<Fact>]
let ``an uncertain send is never resent: later runs refuse the item and only read it back`` () =
    let root = temporary ()
    try
        let board = Board()
        board.AddItem("PVTI_a", item "FS-GG/x#10" "I_a" [ "Status", "Ready" ])
        let effects = [ effect "T1f:a" "PVTI_a" "FS-GG/x#10" V2Tend.TrackField None "Follow-up" [ "T1f" ] [] ]
        // The send fails before reaching GitHub.
        board.FailMutation <- true
        let first = run (harness "run1" root board) (plan effects []) (snapshotOf board [])
        Assert.Equal("uncertain", first.Head.Result)
        board.FailMutation <- false
        // The next run reads back (the target is not there), keeps the record and refuses the item.
        let second = run (harness "run2" root board) (plan effects []) (snapshotOf board [])
        Assert.Equal(("refused", "uncertain-custody"), (second.Head.Result, second.Head.Detail))
        Assert.Equal(1, board.Mutations.Length)
        // Root fixes the item by hand to the target: the next sweep proves it and verifies the record.
        board.Items.["PVTI_a"].Values <- board.Items.["PVTI_a"].Values.Add("Track", "Follow-up")
        let h = harness "run3" root board
        let uncertain, sweep = V2TendWrite.sweepOwn h.Context
        Assert.Empty(uncertain)
        Assert.Contains(sweep, fun o -> o.Result = "verified")
        Assert.Equal(1, board.Mutations.Length)
    finally Directory.Delete(root, true)

[<Fact>]
let ``a lost response after a send that took effect is uncertain, then verified by the next sweep`` () =
    let root = temporary ()
    try
        let board = Board()
        board.AddItem("PVTI_a", item "FS-GG/x#10" "I_a" [ "Status", "Ready" ])
        board.LoseResponse <- true
        let first = run (harness "run1" root board) (plan [ effect "T1f:a" "PVTI_a" "FS-GG/x#10" V2Tend.TrackField None "Follow-up" [ "T1f" ] [] ] []) (snapshotOf board [])
        Assert.Equal("uncertain", first.Head.Result)
        let uncertain, sweep = V2TendWrite.sweepOwn (harness "run2" root board).Context
        Assert.Empty(uncertain)
        Assert.Equal("verified", sweep.Head.Result)
        Assert.Equal(1, board.Mutations.Length)
    finally Directory.Delete(root, true)

[<Fact>]
let ``an effect whose dependency was not written is deferred, and the item budget defers the rest`` () =
    let root = temporary ()
    try
        let board = Board()
        board.AddItem("PVTI_o", item "FS-GG/x#1" "I_o" [ "Work level", "Outcome" ])
        board.AddItem("PVTI_d", item "FS-GG/x#10" "I_d" [ "Work level", "Delivery" ])
        board.AddItem("PVTI_e", item "FS-GG/x#11" "I_e" [ "Work level", "Delivery" ])
        let snapshot = snapshotOf board []
        board.Items.["PVTI_o"].Values <- board.Items.["PVTI_o"].Values.Add("Status", "Blocked") // the parent's fill is refused
        let effects =
            [ effect "S9:o" "PVTI_o" "FS-GG/x#1" V2Tend.StatusField None "In progress" [ "S9" ] []
              effect "O1:d" "PVTI_d" "FS-GG/x#10" V2Tend.TrackField None "Follow-up" [ "T1f" ] [ "S9:o" ]
              effect "T1f:e" "PVTI_e" "FS-GG/x#11" V2Tend.TrackField None "Follow-up" [ "T1f" ] [] ]
        let h = harness "run1" root board
        let outcomes = run { h with Context = { h.Context with Settings = { h.Context.Settings with MaxItemsPerRun = 1 } } } (plan effects []) snapshot
        Assert.Equal<(string * string) list>(
            [ "refused", "field-changed:Status"; "deferred", "dependency-not-written:S9:o"; "deferred", "budget" ],
            outcomes |> List.map (fun o -> o.Result, o.Detail))
        Assert.Empty(board.Mutations)
    finally Directory.Delete(root, true)

[<Fact>]
let ``items held by the gate's custody or eligible for archiving are never written`` () =
    let root = temporary ()
    try
        let board = Board()
        board.AddItem("PVTI_a", item "FS-GG/x#10" "I_a" [ "Status", "Ready" ])
        board.AddItem("PVTI_b", item "FS-GG/x#11" "I_b" [ "Status", "Ready" ])
        let h = harness "run1" root board
        let effects =
            [ effect "T1f:a" "PVTI_a" "FS-GG/x#10" V2Tend.TrackField None "Follow-up" [ "T1f" ] []
              effect "T1f:b" "PVTI_b" "FS-GG/x#11" V2Tend.TrackField None "Follow-up" [ "T1f" ] [] ]
        let outcomes =
            V2TendWrite.apply h.Context
                { Plan = plan effects []; Snapshot = snapshotOf board []; OwnUncertain = Set.empty; GateUncertain = set [ "PVTI_a" ]; GateDeferred = set [ "PVTI_b" ] }
        Assert.Equal<string list>([ "gate-custody-uncertain"; "gate-deferred-pending" ], outcomes |> List.map (fun o -> o.Detail))
        Assert.Empty(board.Mutations)
        // 201 Done and closed cards: the oldest is the archiver's.
        let cards =
            [ for i in 0 .. 200 ->
                { (snapshotOf board []).Cards.Head with
                    ItemId = sprintf "PVTI_done_%d" i; Status = Some V2Tend.Done; Open = false; ClosedAt = Some(DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero).AddHours(float i)) } ]
        Assert.Equal<Set<string>>(set [ "PVTI_done_0" ], V2TendWrite.archiveEligible { (snapshotOf board []) with Cards = cards } 200)
    finally Directory.Delete(root, true)

let private closureBoard () =
    let board = Board()
    board.AddItem("PVTI_d", item "FS-GG/x#20" "I_d" [ "Status", "In review"; "Work level", "Delivery" ])
    board.AddPr("FS-GG/x#200", { State = "MERGED"; Draft = false; MergedAt = Some "2026-10-28T10:00:00Z"; Body = "Refs #20\n<!-- fsgg:delivery-completes/v1 -->"; MergeOid = "abc123"
                                 Comments = [ "https://github.com/FS-GG/x/pull/200#issuecomment-1", "<!-- fsgg:review-decision/v2 -->\n{\"schema\":\"fsgg.coord.review-decision/v2\",\"verdict\":\"pass\"}" ] })
    board.AddPr("FS-GG/x#201", { State = "MERGED"; Draft = false; MergedAt = Some "2026-10-29T10:00:00Z"; Body = "Refs #20"; MergeOid = "def456"; Comments = [] })
    board.Timelines.["I_d"] <- [ "FS-GG/x#200"; "FS-GG/x#201" ]
    board

let private closure : V2Tend.Closure = { Id = "C1:PVTI_d:closed"; ItemId = "PVTI_d"; Ref = "FS-GG/x#20"; MergedPrs = [ "FS-GG/x#200"; "FS-GG/x#201" ] }

[<Fact>]
let ``C1 comments with merge commits and recorded verdicts, then closes the Delivery as completed`` () =
    let root = temporary ()
    try
        let board = closureBoard ()
        let h = harness "run1" root board
        let outcomes = run h (plan [] [ closure ]) (snapshotOf board [])
        Assert.Equal("written", outcomes.Head.Result)
        Assert.False(board.Items.["PVTI_d"].Open)
        Assert.Equal(Some "COMPLETED", board.Items.["PVTI_d"].StateReason)
        Assert.Equal<string list>([ "I_d"; "I_d" ], board.Mutations |> List.map (fun m -> m.Subject))
        Assert.True(board.Mutations.Head.Document.Contains "addComment" && board.Mutations.[1].Document.Contains "closeIssue")
        let _, body, _ = board.Comments.["I_d"].[0]
        Assert.Contains("FS-GG/x#200: merge commit abc123; independent verdict pass (https://github.com/FS-GG/x/pull/200#issuecomment-1)", body)
        Assert.Contains("FS-GG/x#201: merge commit def456; Unknown: no recorded independent verdict", body)
        Assert.EndsWith(V2TendWrite.tendClosedMarker, body)
        Assert.Empty(h.Context.Closable)
        let records, _ = h.Context.Custody.Records()
        let _, record = records.Head
        Assert.Equal("verified", string record.["state"])
    finally Directory.Delete(root, true)

[<Fact>]
let ``an uncertain close is never resent, and the item's field writes are refused too`` () =
    let root = temporary ()
    try
        let board = closureBoard ()
        // Run 1: the comment send fails after custody was written.
        board.FailMutation <- true
        let first = run (harness "run1" root board) (plan [] [ closure ]) (snapshotOf board [])
        Assert.Equal(("uncertain", "comment-unconfirmed"), (first.Head.Result, first.Head.Detail))
        Assert.Equal(1, board.Mutations.Length)
        board.FailMutation <- false
        // Run 2: the close and a field effect on the same item are refused; nothing is sent.
        let effects = [ effect "T1f:d" "PVTI_d" "FS-GG/x#20" V2Tend.TrackField None "Follow-up" [ "T1f" ] [] ]
        let second = run (harness "run2" root board) (plan effects [ closure ]) (snapshotOf board [])
        Assert.Equal<(string * string * string) list>(
            [ "fields", "refused", "uncertain-custody"; "close", "refused", "uncertain-custody" ],
            second |> List.map (fun o -> o.Kind, o.Result, o.Detail))
        Assert.Equal(1, board.Mutations.Length)
        Assert.True(board.Items.["PVTI_d"].Open)
    finally Directory.Delete(root, true)

[<Fact>]
let ``items are applied in dependency order, so a child listed first still converges in one run`` () =
    let root = temporary ()
    try
        let board = Board()
        board.AddItem("PVTI_o", item "FS-GG/x#1" "I_o" [ "Work level", "Outcome" ])
        board.AddItem("PVTI_d", item "FS-GG/x#10" "I_d" [ "Work level", "Delivery" ])
        let effects =
            [ effect "T1f:d" "PVTI_d" "FS-GG/x#10" V2Tend.TrackField None "Follow-up" [ "T1f" ] [ "S9:o" ]
              effect "S9:o" "PVTI_o" "FS-GG/x#1" V2Tend.TrackField None "Follow-up" [ "T1f" ] [] ]
        let outcomes = run (harness "run1" root board) (plan effects []) (snapshotOf board [])
        Assert.Equal<(string * string) list>([ "PVTI_o", "written"; "PVTI_d", "written" ], outcomes |> List.map (fun o -> o.Subject, o.Result))
    finally Directory.Delete(root, true)

[<Fact>]
let ``C1 refuses an open linked PR, a reopen marker, a card that is not an In review Delivery`` () =
    let cases =
        [ "close-open-linked-pr", (fun (b: Board) ->
              b.AddPr("FS-GG/x#202", { State = "OPEN"; Draft = true; MergedAt = None; Body = "Refs FS-GG/x#20"; MergeOid = ""; Comments = [] })
              b.Timelines.["I_d"] <- b.Timelines.["I_d"] @ [ "FS-GG/x#202" ])
          "close-reopened-after-close", (fun b ->
              b.Comments.["I_d"] <- Collections.Generic.List([ "IC_old", "closed\n" + V2TendWrite.tendClosedMarker, "EHotwagner" ]))
          "close-not-a-delivery", (fun b -> b.Items.["PVTI_d"].Values <- b.Items.["PVTI_d"].Values.Add("Work level", "Outcome"))
          "close-not-in-review", (fun b -> b.Items.["PVTI_d"].Values <- b.Items.["PVTI_d"].Values.Add("Status", "Blocked")) ]
    for expected, change in cases do
        let root = temporary ()
        try
            let board = closureBoard ()
            let snapshot = snapshotOf board []
            change board
            let outcomes = V2TendWrite.apply (harness "run1" root board).Context { Plan = plan [] [ closure ]; Snapshot = snapshot; OwnUncertain = Set.empty; GateUncertain = Set.empty; GateDeferred = Set.empty }
            Assert.Equal(("refused", expected), (outcomes.Head.Result, outcomes.Head.Detail))
            Assert.Empty(board.Mutations)
            Assert.True(board.Items.["PVTI_d"].Open)
        finally Directory.Delete(root, true)

// ---- GR ----

let private gateRecord (root: string) (repo: string) (pr: int) (fields: (string * JsonNode) list) =
    let directory = Path.Combine(root, "gate")
    Directory.CreateDirectory directory |> ignore
    let key = Security.Cryptography.SHA256.HashData(Text.Encoding.UTF8.GetBytes(sprintf "%s#%d" (repo.ToLowerInvariant()) pr)) |> Convert.ToHexString |> fun h -> h.Substring(0, 32).ToLowerInvariant()
    let record = JsonObject()
    record["schema"] <- V2TendWrite.gateSchema
    record["repo"] <- repo
    record["pr"] <- pr
    for name, value in fields do record[name] <- value
    File.WriteAllText(Path.Combine(directory, key + ".json"), record.ToJsonString())

let private gateStates (h: Harness) =
    let records, malformed = h.Context.Gate.Records()
    Assert.Equal(0, malformed)
    records |> List.map (fun r -> r.Pr, r.State) |> List.sort

[<Fact>]
let ``GR supersedes older deferred records per Delivery and replays the newest in merge order`` () =
    let root = temporary ()
    try
        let board = Board()
        board.AddItem("PVTI_a", item "FS-GG/x#30" "I_a" [ "Status", "In progress" ])
        let deferred (pr: int) (merged: string) (target: string) : (string * JsonNode) list =
            [ "delivery", JsonValue.Create "FS-GG/x#30"; "itemId", JsonValue.Create "PVTI_a"; "from", JsonValue.Create "In progress"; "to", JsonValue.Create target
              "state", JsonValue.Create "deferred"; "mergedAt", JsonValue.Create merged; "recordedAt", JsonValue.Create "2026-11-01T00:00:00Z" ]
        gateRecord root "FS-GG/x" 301 (deferred 301 "2026-10-30T10:00:00Z" "In progress")
        gateRecord root "FS-GG/x" 302 (deferred 302 "2026-10-31T10:00:00Z" "In review")
        let h = harness "run1" root board
        let outcomes = V2TendWrite.gatePass h.Context true
        Assert.Contains(outcomes, fun o -> o.Result = "superseded" && o.Subject = "FS-GG/x#301")
        Assert.Contains(outcomes, fun o -> o.Result = "verified" && o.Subject = "FS-GG/x#302")
        Assert.Equal(Some "In review", board.Items.["PVTI_a"].Values.TryFind "Status")
        Assert.Equal<(int * string) list>([ 301, "superseded"; 302, "verified" ], gateStates h)
        Assert.Equal(1, board.Mutations.Length)
        // The rewritten records keep the gate's fields.
        let records, _ = h.Context.Gate.Records()
        Assert.True(records |> List.forall (fun r -> r.Record["recordedAt"] <> null && r.Record["delivery"] <> null))
    finally Directory.Delete(root, true)

[<Fact>]
let ``GR refuses a changed preimage, skips a closed Delivery, and without replay only sweeps`` () =
    let root = temporary ()
    try
        let board = Board()
        board.AddItem("PVTI_a", item "FS-GG/x#30" "I_a" [ "Status", "Blocked" ])
        board.AddItem("PVTI_b", { item "FS-GG/x#31" "I_b" [ "Status", "In progress" ] with Open = false })
        board.AddItem("PVTI_c", item "FS-GG/x#32" "I_c" [ "Status", "In review" ])
        let record (delivery: string) (itemId: string) (state: string) : (string * JsonNode) list =
            [ "delivery", JsonValue.Create delivery; "itemId", JsonValue.Create itemId; "from", JsonValue.Create "In progress"; "to", JsonValue.Create "In review"
              "state", JsonValue.Create state; "mergedAt", JsonValue.Create "2026-10-31T10:00:00Z" ]
        gateRecord root "FS-GG/x" 401 (record "FS-GG/x#30" "PVTI_a" "deferred")
        gateRecord root "FS-GG/x" 402 (record "FS-GG/x#31" "PVTI_b" "deferred")
        gateRecord root "FS-GG/x" 403 (record "FS-GG/x#32" "PVTI_c" "possible-send")
        let h = harness "run1" root board
        // Without GR enabled: the possible-send record is read back and verified; deferred records stay.
        V2TendWrite.gatePass h.Context false |> ignore
        Assert.Equal<(int * string) list>([ 401, "deferred"; 402, "deferred"; 403, "verified" ], gateStates h)
        let gateUncertain, gateDeferred = V2TendWrite.gateHolds (fst (h.Context.Gate.Records()))
        Assert.Equal<Set<string>>(set [ "PVTI_a"; "PVTI_b" ], gateDeferred)
        Assert.Empty(gateUncertain)
        V2TendWrite.gatePass h.Context true |> ignore
        Assert.Equal<(int * string) list>([ 401, "refused"; 402, "not-applicable"; 403, "verified" ], gateStates h)
        Assert.Empty(board.Mutations)
    finally Directory.Delete(root, true)

// ---- the enable file ----

[<Fact>]
let ``the enable file refuses unknown keys and rules and drops gated rules until the contract amendment`` () =
    Assert.True(V2TendWrite.parseEnable """{"apply":["W1h"],"approve":true}""" false |> Result.isError)
    Assert.True(V2TendWrite.parseEnable """{"apply":["W1h","X9"]}""" false |> Result.isError)
    Assert.True(V2TendWrite.parseEnable """{"apply":"W1h"}""" false |> Result.isError)
    Assert.True(V2TendWrite.parseEnable """{"apply":["W1h"],"maxItemsPerRun":501}""" false |> Result.isError)
    Assert.True(V2TendWrite.parseEnable "not json" false |> Result.isError)
    match V2TendWrite.parseEnable """{"apply":["W1h","T1f","GR","C1","S3"],"propose":true,"maxItemsPerRun":20}""" false with
    | Ok(enable, dropped) ->
        Assert.Equal<Set<string>>(set [ "W1h"; "T1f"; "GR" ], enable.Apply)
        Assert.Equal((true, 20), (enable.Propose, enable.MaxItemsPerRun))
        Assert.Equal(2, dropped.Length)
    | Error e -> failwith e
    match V2TendWrite.parseEnable """{"apply":["C1","S3"]}""" true with
    | Ok(enable, dropped) ->
        Assert.Equal<Set<string>>(set [ "C1"; "S3" ], enable.Apply)
        Assert.Empty(dropped)
        Assert.Equal(100, enable.MaxItemsPerRun)
    | Error e -> failwith e

[<Fact>]
let ``the document parser reports every root field after aliases and spreads`` () =
    let fields d = (V2TendReads.parseDocument d).Value.Operations |> List.map (fun o -> o.Kind, o.RootFields)
    Assert.Equal<(string * string list) list>([ "mutation", [ "closeIssue"; "archiveProjectV2Item" ] ], fields "mutation { a: closeIssue(input: {}) { x } archiveProjectV2Item(input: {}) @skip(if: false) { y { z } } }")
    Assert.Equal<(string * string list) list>([ "query", [ "..."; "viewer" ] ], fields "{ ... on Query { a } viewer { login } }")
    Assert.True((V2TendReads.parseDocument "mutation { a: }").IsNone)
    Assert.True((V2TendReads.parseDocument "mutation { a(b: 1.5) { c } }").IsNone)
    Assert.True((V2TendReads.parseDocument "mutation($a: String = \"x\") { b }").Value.HasStringLiteral)
