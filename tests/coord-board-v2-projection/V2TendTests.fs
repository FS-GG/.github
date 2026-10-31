/// Pure derivation of the board reconciler (FS-GG/.github#5136 phase 2 PR 2; design §3, §4, §10, §11).
module FS.GG.Coord.GitHub.Tests.V2TendTests

open System
open System.IO
open Xunit
open FS.GG.Coord.GitHub
open FS.GG.Coord.GitHub.V2Tend

let private all = V2Tend.v1 V2Tend.applyRules
let private starting = V2Tend.v1 (set [ "W1h"; "T1f" ])
let private without (rules: string list) = V2Tend.v1 (V2Tend.applyRules - set rules)

let private fixture (name: string) =
    match V2Tend.decodeSnapshot (File.ReadAllText(Path.Combine(__SOURCE_DIRECTORY__, "tend-fixtures", name))) with
    | Ok snapshot -> snapshot
    | Error error -> failwith error

let private effectOn (plan: Plan) item field = plan.Apply |> List.tryFind (fun e -> e.ItemId = item && e.Field = field)
let private target plan item field = effectOn plan item field |> Option.map (fun e -> e.Target)
let private rules plan item field = effectOn plan item field |> Option.map (fun e -> e.Rules)
let private proposed (plan: Plan) reference reason = plan.Propose |> List.exists (fun p -> p.Ref = reference && p.Reason = reason)
let private refused (plan: Plan) reference reason = plan.Refuse |> List.exists (fun r -> r.Ref = reference && r.Reason = reason)
let private deferredOn (plan: Plan) item field = plan.Deferred |> List.tryFind (fun d -> d.ItemId = item && d.Field = field)

// ---------------------------------------------------------------------------------------------------
// The closed intended-fields grammar and the fixture decoder
// ---------------------------------------------------------------------------------------------------

[<Fact>]
let ``the intended-fields line parses only the closed grammar`` () =
    let parsed = V2Tend.parseIntendedLine "Work level: Delivery · Track: Follow-up · Intended Status: **Backlog**" true
    Assert.Equal(Some { WorkLevel = Some Delivery; Track = Some FollowUp; Status = Some Backlog; RootAuthored = true }, parsed)
    Assert.Equal(Some { WorkLevel = None; Track = None; Status = Some InProgress; RootAuthored = false },
                 V2Tend.parseIntendedLine "Intended Status: In progress" false)
    for line in [ "Work level: Epic"; "Track: Active"; "Status: Ready"; "Work level Delivery"; ""; "Work level: Delivery · Work level: Outcome" ] do
        Assert.Equal(None, V2Tend.parseIntendedLine line true)

[<Fact>]
let ``the snapshot decoder refuses an unknown schema or value`` () =
    Assert.True(V2Tend.decodeSnapshot """{"schema":"other","observedAt":"2026-11-01T00:00:00Z","cards":[]}""" |> Result.isError)
    Assert.True(V2Tend.decodeSnapshot """{"schema":"fsgg.board-tend-snapshot/1","observedAt":"2026-11-01T00:00:00Z","cards":[{"itemId":"a","status":"Doing"}]}""" |> Result.isError)
    Assert.True(V2Tend.decodeSnapshot "not json" |> Result.isError)

// ---------------------------------------------------------------------------------------------------
// §11 acceptance fixtures
// ---------------------------------------------------------------------------------------------------

[<Fact>]
let ``case 1: no Status fills Backlog, Ready only from a Root-authored line, and proposes other intents`` () =
    let plan = V2Tend.derive all (fixture "01-no-status.json")
    Assert.Equal(Some "Backlog", target plan "I-plain" StatusField)
    Assert.Equal(Some [ "S9r" ], rules plan "I-ready-root" StatusField)
    Assert.Equal(Some "Ready", target plan "I-ready-root" StatusField)
    Assert.Equal(Some "Backlog", target plan "I-ready-other" StatusField)
    Assert.True(proposed plan "FS-GG/x#3" "intended-ready")
    Assert.Equal(Some "Backlog", target plan "I-progress" StatusField)
    Assert.True(proposed plan "FS-GG/x#4" "intended-in-progress")
    // S9 then S3 collapse into one effect from the empty preimage.
    let collapsed = (effectOn plan "I-with-pr" StatusField).Value
    Assert.Equal((None, "In review", [ "S9"; "S3" ]), (collapsed.Preimage, collapsed.Target, collapsed.Rules))
    Assert.False(proposed plan "FS-GG/x#1" "intended-backlog")

[<Fact>]
let ``case 2: Work level is structural, a conflict is refused, Track inherits or defaults`` () =
    let plan = V2Tend.derive all (fixture "02-missing-fields.json")
    Assert.Equal(Some "Outcome", target plan "I-outcome" WorkLevelField)
    Assert.Equal(Some "Delivery", target plan "I-child" WorkLevelField)
    Assert.Equal(None, effectOn plan "I-conflict" WorkLevelField)
    Assert.True(refused plan "FS-GG/x#12" "work-level-conflict")
    Assert.Equal(Some [ "W1d" ], rules plan "I-orphan" WorkLevelField)
    Assert.True(proposed plan "FS-GG/x#14" "work-level-undetermined")
    // The parentless Outcome takes the default once its Work level settled; its child inherits that.
    Assert.Equal(Some "Follow-up", target plan "I-outcome" TrackField)
    let child = (effectOn plan "I-child" TrackField).Value
    Assert.Equal((Some "Follow-up", [ "T1i" ]), (Some child.Target, child.Rules))
    Assert.Contains((effectOn plan "I-outcome" TrackField).Value.Id, child.DependsOn)

[<Fact>]
let ``case 3 and 16: closed cards move to Done only after the settle window, Blocked included`` () =
    let plan = V2Tend.derive all (fixture "03-closed.json")
    Assert.Equal(Some "Done", target plan "I-closed-old" StatusField)
    Assert.Equal(None, effectOn plan "I-closed-new" StatusField)
    Assert.Equal(Some "Done", target plan "I-closed-blocked" StatusField)
    Assert.Equal(Some "Blocked", (effectOn plan "I-closed-blocked" StatusField).Value.Preimage)
    Assert.Equal(None, effectOn plan "I-closed-done" StatusField)

[<Fact>]
let ``case 4 and 17: In review moves back only when nothing is linked and the settle window passed`` () =
    let plan = V2Tend.derive all (fixture "04-in-review.json")
    Assert.Equal(Some [ "S4" ], rules plan "I-nothing" StatusField)
    Assert.Equal(None, effectOn plan "I-recent" StatusField)
    Assert.Equal(None, effectOn plan "I-merged" StatusField)
    Assert.True(proposed plan "FS-GG/x#32" "acceptance-ready")
    Assert.Equal(Some "In progress", target plan "I-abandoned" StatusField) // a closed unmerged PR is ignored
    Assert.Equal(None, effectOn plan "I-partial" StatusField)
    Assert.True(refused plan "FS-GG/x#34" "timeline-incomplete")

[<Fact>]
let ``case 5: an open PR moves to In review, idle work is proposed, finished work is acceptance-ready`` () =
    let plan = V2Tend.derive all (fixture "05-in-progress.json")
    Assert.Equal(Some [ "S3" ], rules plan "I-open-pr" StatusField)
    Assert.True(proposed plan "FS-GG/x#41" "stale-active")
    Assert.False(proposed plan "FS-GG/x#42" "stale-active")
    Assert.True(proposed plan "FS-GG/x#43" "acceptance-ready")
    Assert.Equal(Some [ "S3b" ], rules plan "I-draft" StatusField)
    Assert.Equal(None, effectOn plan "I-blocked-pr" StatusField) // Blocked is never moved by an open PR

[<Fact>]
let ``case 6: Backlog saying Nothing is only ever proposed, enrolled or not`` () =
    let plan = V2Tend.derive all (fixture "06-backlog-nothing.json")
    Assert.Empty(plan.Apply |> List.filter (fun e -> e.Field = StatusField))
    Assert.True(proposed plan "FS-GG/x#50" "blocked-on-nothing")
    Assert.True(proposed plan "FS-GG/x#51" "blocked-on-nothing-unenrolled")
    Assert.False(plan.Propose |> List.exists (fun p -> p.Ref = "FS-GG/x#52"))
    Assert.True(proposed plan "FS-GG/x#53" "blockers-closed")
    Assert.True(proposed plan "FS-GG/x#54" "no-blocker")
    Assert.True(refused plan "FS-GG/x#55" "blockers-unread")

[<Fact>]
let ``case 7: the Outcome roll-up stays within Backlog, Ready and In progress`` () =
    let plan = V2Tend.derive all (fixture "07-rollup.json")
    let active = (effectOn plan "O-active" StatusField).Value
    Assert.Equal((Some "Ready", "In progress", [ "O1" ]), (active.Preimage, active.Target, active.Rules))
    Assert.Equal(Some "In progress", target plan "O-review" StatusField)
    Assert.True(proposed plan "FS-GG/x#64" "all-children-in-review")
    Assert.Equal(None, effectOn plan "O-blocked" StatusField)
    Assert.True(proposed plan "FS-GG/x#66" "rollup-from-blocked")
    Assert.Equal(None, effectOn plan "O-partial" StatusField)
    Assert.True(refused plan "FS-GG/x#68" "children-incomplete")
    // An empty Outcome whose children are all Blocked (settled) gets the Backlog fallback and a proposal.
    Assert.Equal(Some "Backlog", target plan "O-empty" StatusField)
    Assert.True(proposed plan "FS-GG/x#70" "rollup-blocked")
    Assert.True(refused plan "FS-GG/x#72" "child-done-open")
    Assert.True(proposed plan "FS-GG/x#73" "done-open")
    Assert.True(proposed plan "FS-GG/x#74" "children-complete")
    for effect in plan.Apply do
        Assert.NotEqual<string>("In review", effect.Target)
        Assert.NotEqual<string>("Blocked", effect.Target)

[<Fact>]
let ``case 8: Roadmap is filled only on enrolled cards and never overwritten`` () =
    let plan = V2Tend.derive all (fixture "08-roadmap.json")
    Assert.Equal(Some [ "RM1" ], rules plan "D-fill" RoadmapField)
    Assert.True(refused plan "FS-GG/x#121" "roadmap-conflicts-with-enrollment")
    Assert.Equal(None, effectOn plan "D-differs" RoadmapField)
    Assert.Equal(None, effectOn plan "D-unenrolled" RoadmapField)
    Assert.True(refused plan "FS-GG/x#123" "pull-request-card")
    Assert.True(refused plan "X-card" "unreadable")

[<Fact>]
let ``case 11: a loose link is proposed and never moves a card`` () =
    let plan = V2Tend.derive all (fixture "11-loose-link.json")
    Assert.Empty(plan.Apply)
    Assert.True(proposed plan "FS-GG/x#130" "loose-link:multiple-deliveries")
    // A loosely linked merged PR keeps S4 from firing.
    Assert.Equal(None, effectOn plan "D-loose-review" StatusField)

[<Fact>]
let ``case 13: same-run dependencies are visible within the run and ordered`` () =
    let snapshot = fixture "13-same-run.json"
    let plan = V2Tend.derive all snapshot
    let parentTrack = (effectOn plan "O-new" TrackField).Value
    let childTrack = (effectOn plan "D-new" TrackField).Value
    Assert.Equal(([ "T1d" ], "Active delivery"), (parentTrack.Rules, parentTrack.Target))
    Assert.Equal(([ "T1i" ], "Active delivery"), (childTrack.Rules, childTrack.Target))
    Assert.Equal<string list>([ parentTrack.Id ], childTrack.DependsOn)
    Assert.Equal(Some "In review", target plan "D-new" StatusField)
    let outcome = (effectOn plan "O-new" StatusField).Value
    Assert.Equal("In progress", outcome.Target)
    Assert.Contains((effectOn plan "D-new" StatusField).Value.Id, outcome.DependsOn)
    Assert.Contains(outcome.Id, V2Tend.dependantsOf plan (effectOn plan "D-new" StatusField).Value.Id)
    // Applied and derived again: nothing is left to apply.
    Assert.Empty((V2Tend.derive all (V2Tend.applyPlan plan snapshot)).Apply)

[<Fact>]
let ``case 20: a young leaf inherits its parent's Track at once and never takes Follow-up`` () =
    let snapshot = fixture "20-young-leaf.json"
    let plan = V2Tend.derive all snapshot
    Assert.Equal(None, effectOn plan "D-young" WorkLevelField)
    Assert.Equal(Some SettleWindow, deferredOn plan "D-young" WorkLevelField |> Option.map (fun d -> d.Reason))
    Assert.False((deferredOn plan "D-young" WorkLevelField).Value.Counts)
    Assert.Equal(Some "Active delivery", target plan "D-young" TrackField)
    // With T1i off, the would-be value is shown, the field waits, and Follow-up is never written.
    let off = V2Tend.derive (without [ "T1i" ]) snapshot
    Assert.Equal(None, effectOn off "D-young" TrackField)
    Assert.Contains(off.WouldApply, fun e -> e.ItemId = "D-young" && e.Target = "Active delivery")
    Assert.Equal(Some RuleDisabled, deferredOn off "D-young" TrackField |> Option.map (fun d -> d.Reason))
    // After the settle window W1h writes Delivery.
    let later = V2Tend.derive all { snapshot with ObservedAt = snapshot.ObservedAt.AddHours 23.0 }
    Assert.Equal(Some "Delivery", target later "D-young" WorkLevelField)

[<Fact>]
let ``case 21 and 24: a leaf declaring Outcome is refused even while W1d is off`` () =
    let plan = V2Tend.derive (without [ "W1d" ]) (fixture "02-missing-fields.json")
    Assert.True(refused plan "FS-GG/x#12" "work-level-conflict")
    Assert.Equal(None, effectOn plan "I-conflict" WorkLevelField)

[<Fact>]
let ``case 23 and 24: an off-board parent defers W1h, which expires into a proposal and still applies later`` () =
    let snapshot = fixture "23-expiry.json"
    let first = V2Tend.derive all snapshot
    let deferred = (deferredOn first "D-offboard" WorkLevelField).Value
    Assert.Equal((InputPending, true, false), (deferred.Reason, deferred.Counts, deferred.Expired))
    // Third run: two earlier runs in the ledger.
    let third = V2Tend.derive all { snapshot with Ledger = [ { Key = deferred.Key; FirstDeferredAt = snapshot.ObservedAt.AddHours -2.0; Runs = 2 } ] }
    Assert.True((deferredOn third "D-offboard" WorkLevelField).Value.Expired)
    Assert.True(proposed third "FS-GG/x#100" "work-level-undetermined")
    // 24 hours after the first deferral, even on the second run.
    let aged = V2Tend.derive all { snapshot with Ledger = [ { Key = deferred.Key; FirstDeferredAt = snapshot.ObservedAt.AddHours -25.0; Runs = 1 } ] }
    Assert.True(proposed aged "FS-GG/x#100" "work-level-undetermined")
    // The parent joins the board: the effect applies and the proposal clears.
    let parent = { snapshot.Cards.Head with ItemId = "O-joined"; Ref = "FS-GG/x#999"; Parent = None; SubIssues = [ "FS-GG/x#100", true ]; Status = Some Ready }
    let settled = V2Tend.derive all { snapshot with Cards = parent :: snapshot.Cards; Ledger = third.Deferred |> List.map (fun d -> { Key = d.Key; FirstDeferredAt = snapshot.ObservedAt; Runs = 5 }) }
    Assert.Equal(Some "Delivery", target settled "D-offboard" WorkLevelField)
    Assert.False(proposed settled "FS-GG/x#100" "work-level-undetermined")

[<Fact>]
let ``case 26 and 27: a declared Track wins over inheritance and the default`` () =
    let snapshot = fixture "26-27-declared-track.json"
    let on = V2Tend.derive all snapshot
    Assert.Equal(Some ("Follow-up", [ "T1d" ]), effectOn on "D-declared" TrackField |> Option.map (fun e -> e.Target, e.Rules))
    Assert.Equal(Some ("Active delivery", [ "T1d" ]), effectOn on "D-parentless" TrackField |> Option.map (fun e -> e.Target, e.Rules))
    let off = V2Tend.derive (without [ "T1d" ]) snapshot
    Assert.Equal(None, effectOn off "D-declared" TrackField)
    Assert.Equal(None, effectOn off "D-parentless" TrackField)
    Assert.Empty(off.Apply |> List.filter (fun e -> e.Field = TrackField))
    let key = (deferredOn off "D-declared" TrackField).Value.Key
    let expired = V2Tend.derive (without [ "T1d" ]) { snapshot with Ledger = [ { Key = key; FirstDeferredAt = snapshot.ObservedAt.AddHours -30.0; Runs = 1 } ] }
    Assert.True(proposed expired "FS-GG/x#111" "track-declared-pending")

[<Fact>]
let ``the starting set writes only W1h and T1f`` () =
    for name in Directory.EnumerateFiles(Path.Combine(__SOURCE_DIRECTORY__, "tend-fixtures"), "*.json") do
        let plan = V2Tend.derive starting (fixture (Path.GetFileName name))
        for effect in plan.Apply do
            Assert.True(effect.Rules |> List.forall (fun rule -> rule = "W1h" || rule = "T1f"), effect.Id)

[<Fact>]
let ``T1i disagreement is counted over undeclared pairs with Tracks people set`` () =
    let plan = V2Tend.derive all (fixture "26-27-declared-track.json")
    Assert.Equal({ Pairs = 0; Disagreements = 0 }, plan.T1iComparison) // the only pair is declared
    let rollup = V2Tend.derive all (fixture "07-rollup.json")
    Assert.Equal({ Pairs = 7; Disagreements = 0 }, rollup.T1iComparison)
    let changed = fixture "07-rollup.json"
    let changed = { changed with Cards = changed.Cards |> List.map (fun c -> if c.ItemId = "D-61" then { c with Track = Some FollowUp } else c) }
    Assert.Equal({ Pairs = 7; Disagreements = 1 }, (V2Tend.derive all changed).T1iComparison)

// ---------------------------------------------------------------------------------------------------
// Properties over seeded generated boards (no wall clock, no randomness without a seed)
// ---------------------------------------------------------------------------------------------------

let private observed = DateTimeOffset(2026, 11, 1, 12, 0, 0, TimeSpan.Zero)

let private generate (seed: int) : Snapshot =
    let random = Random seed
    let pick (items: 'a list) = items.[random.Next items.Length]
    let maybe p value = if random.NextDouble() < p then Some value else None
    let mutable cards: Card list = []
    let mutable next = 0
    let rec make (parent: string option) depth =
        next <- next + 1
        let reference = sprintf "FS-GG/g#%d" next
        let item = sprintf "G-%d" next
        let childCount = if depth < 3 && random.NextDouble() < 0.45 then random.Next(1, 4) else 0
        let children = [ for _ in 1 .. childCount -> make (Some reference) (depth + 1) ]
        let isOpen = random.NextDouble() < 0.85
        let created = observed.AddHours(-(float (random.Next(1, 24 * 40))))
        let prs =
            [ for index in 1 .. random.Next(0, 3) ->
                { Id = sprintf "PR-%d-%d" next index
                  State = pick [ PrOpen; PrClosed; PrMerged ]
                  Draft = random.NextDouble() < 0.2
                  Linkage = if random.NextDouble() < 0.2 then Loose "multiple-deliveries" else Strict
                  UpdatedAt = observed.AddHours(-(float (random.Next(1, 200)))) } ]
        let intended =
            if random.NextDouble() < 0.4 then
                Some { WorkLevel = maybe 0.3 (pick [ Outcome; Delivery ])
                       Track = maybe 0.4 (pick [ ActiveDelivery; FollowUp ])
                       Status = maybe 0.5 (pick [ Backlog; Ready; InProgress; Blocked ])
                       RootAuthored = random.NextDouble() < 0.5 }
            else None
        let card =
            { ItemId = item; Ref = reference; Kind = Issue; Open = isOpen
              CreatedAt = created; UpdatedAt = observed.AddHours(-(float (random.Next(1, 200))))
              ClosedAt = (if isOpen then None else Some(observed.AddHours(-(float (random.Next(1, 100))))))
              Status = maybe 0.6 (pick [ Backlog; Ready; InProgress; InReview; Blocked; Done ])
              StatusUpdatedAt = Some(observed.AddHours(-(float (random.Next(1, 200)))))
              WorkLevel = maybe 0.4 (if childCount > 0 then Outcome else Delivery)
              Track = maybe 0.4 (pick [ ActiveDelivery; FollowUp ])
              Roadmap = None; Parent = parent
              SubIssues = children |> List.map (fun (r, o) -> r, o); SubIssuesComplete = random.NextDouble() < 0.95
              Blockers = [ for _ in 1 .. random.Next(0, 2) -> { Ref = "FS-GG/g#999"; Open = random.NextDouble() < 0.5 } ]
              BlockersComplete = true; MarkerBlockers = Some []
              Prs = prs; TimelineComplete = random.NextDouble() < 0.95
              Intended = intended
              BlockedOn = maybe 0.3 "Nothing"; AcceptanceEvidence = maybe 0.3 "merge recorded"
              Enrolled = random.NextDouble() < 0.5; EnrolledRoadmap = maybe 0.5 "https://github.com/FS-GG/.github/blob/main/docs/x.md" }
        cards <- card :: cards
        reference, isOpen
    for _ in 1 .. random.Next(2, 6) do make None 0 |> ignore
    // A parent that is not on the board for some leaves.
    { ObservedAt = observed; Cards = List.rev cards; Ledger = [] }

let private seeds = [ 1 .. 60 ]
let private subsets =
    [ V2Tend.applyRules
      set [ "W1h"; "T1f" ]
      V2Tend.applyRules - set [ "T1i" ]
      V2Tend.applyRules - set [ "T1d"; "W1d" ]
      set [ "S1"; "S3"; "S3b"; "S4"; "S9"; "O1" ] ]

let private shuffle (seed: int) (items: 'a list) =
    let random = Random(seed * 7919)
    items |> List.map (fun item -> random.Next(), item) |> List.sortBy fst |> List.map snd

[<Fact>]
let ``the generated boards exercise every Apply rule, deferral and proposal kind`` () =
    let plans = seeds |> List.map (generate >> V2Tend.derive all)
    let used = plans |> List.collect (fun p -> p.Apply |> List.collect (fun e -> e.Rules)) |> Set.ofList
    for rule in [ "W1h"; "W1d"; "T1d"; "T1i"; "T1f"; "RM1"; "S1"; "S3"; "S3b"; "S4"; "S9"; "S9r"; "O1" ] do
        Assert.True(used.Contains rule, sprintf "no generated board produced %s" rule)
    Assert.True(plans |> List.exists (fun p -> not p.Deferred.IsEmpty))
    Assert.True(plans |> List.sumBy (fun p -> p.Apply.Length) > 200)
    for p in plans do Assert.DoesNotContain(p.Refuse, fun r -> r.Reason = "fixpoint-not-reached")

[<Fact>]
let ``dependants are transitive down a Track inheritance chain`` () =
    let basis = (generate 1).Cards.Head
    let make item reference parent children =
        { basis with
            ItemId = item; Ref = reference; Parent = parent; SubIssues = children |> List.map (fun c -> c, true); Open = true
            Status = Some Ready; WorkLevel = None; Track = None; Prs = []; Intended = None; Blockers = []; BlockedOn = None
            Enrolled = false; EnrolledRoadmap = None; SubIssuesComplete = true; TimelineComplete = true
            CreatedAt = observed.AddDays -10.0 }
    let top = { make "O" "FS-GG/t#1" None [ "FS-GG/t#2" ] with Intended = Some { WorkLevel = None; Track = Some ActiveDelivery; Status = None; RootAuthored = false } }
    let middle = make "M" "FS-GG/t#2" (Some "FS-GG/t#1") [ "FS-GG/t#3" ]
    let leaf = make "D" "FS-GG/t#3" (Some "FS-GG/t#2") []
    let plan = V2Tend.derive all { ObservedAt = observed; Cards = [ leaf; middle; top ]; Ledger = [] }
    let topTrack = (effectOn plan "O" TrackField).Value
    Assert.Equal<string list>([ "T1d" ], topTrack.Rules)
    // The middle card is not a leaf, so T1i does not reach it; the leaf has a non-leaf P3 parent.
    Assert.True(proposed plan "FS-GG/t#2" "track-undetermined")
    let leafPlan =
        V2Tend.derive all { ObservedAt = observed; Cards = [ { leaf with Parent = Some "FS-GG/t#1" }; { top with SubIssues = [ "FS-GG/t#3", true ] } ]; Ledger = [] }
    let leafTrack = (effectOn leafPlan "D" TrackField).Value
    Assert.Contains((effectOn leafPlan "O" TrackField).Value.Id, leafTrack.DependsOn)
    // Status: the leaf's S9 fill, the Outcome's roll-up on top of it, and a dependant of the dependant.
    let leafStatus = { leaf with Parent = Some "FS-GG/t#1"; Status = None }
    let chainPlan =
        V2Tend.derive all { ObservedAt = observed; Cards = [ leafStatus; { top with SubIssues = [ "FS-GG/t#3", true ]; Status = None; Parent = None } ]; Ledger = [] }
    let leafS9 = (effectOn chainPlan "D" StatusField).Value
    let rollup = (effectOn chainPlan "O" StatusField).Value
    Assert.Contains(leafS9.Id, rollup.DependsOn)
    let dependants = V2Tend.dependantsOf chainPlan leafS9.Id
    Assert.Contains(rollup.Id, dependants)
    let synthetic =
        { chainPlan with
            Apply = chainPlan.Apply @ [ { rollup with Id = "X:after-rollup"; ItemId = "Z"; DependsOn = [ rollup.Id ] } ] }
    Assert.Contains("X:after-rollup", V2Tend.dependantsOf synthetic leafS9.Id)

[<Fact>]
let ``determinism: permuting cards, PRs, sub-issues and blockers gives the same plan`` () =
    for seed in seeds do
        let snapshot = generate seed
        let permuted =
            { snapshot with
                Cards =
                    snapshot.Cards
                    |> List.map (fun c -> { c with Prs = shuffle seed c.Prs; SubIssues = shuffle seed c.SubIssues; Blockers = shuffle seed c.Blockers })
                    |> shuffle seed }
        Assert.Equal(V2Tend.derive all snapshot, V2Tend.derive all permuted)

let private keyOf (rule: string) (item: string) (field: Field) = rule + ":" + item + ":" + V2Tend.fieldName field

[<Fact>]
let ``idempotence: repeated application settles within k applications over every enabled subset`` () =
    for enabled in subsets do
        let parameters = V2Tend.v1 enabled
        for seed in seeds do
            let mutable snapshot = generate seed
            let mutable plan = V2Tend.derive parameters snapshot
            Assert.DoesNotContain(plan.Refuse, fun r -> r.Reason = "fixpoint-not-reached")
            let mutable applications = 0
            // k is at most the longest fallback chain: T1f then T1i, or S9 then O1's empty fill.
            while not plan.Apply.IsEmpty && applications < 4 do
                let previous = plan
                snapshot <- V2Tend.applyPlan plan snapshot
                applications <- applications + 1
                plan <- V2Tend.derive parameters snapshot
                // After the first application only input-pending deferrals produce effects.
                let pending = previous.Deferred |> List.filter (fun d -> d.Reason = InputPending) |> List.map (fun d -> d.ItemId, d.Field) |> Set.ofList
                for effect in plan.Apply do
                    Assert.True(pending.Contains(effect.ItemId, effect.Field), sprintf "seed %d: %s was not deferred input-pending" seed effect.Id)
            Assert.True(plan.Apply.IsEmpty, sprintf "seed %d did not settle within 4 applications" seed)
            Assert.True(applications <= 3, sprintf "seed %d needed %d applications" seed applications)

[<Fact>]
let ``liveness: after max(k, 3) runs every field is filled, refused or proposed`` () =
    for enabled in subsets do
        let parameters = V2Tend.v1 enabled
        for seed in seeds do
            let mutable snapshot = generate seed
            let mutable plan = V2Tend.derive parameters snapshot
            for run in 1 .. 4 do
                snapshot <- V2Tend.applyPlan plan snapshot
                snapshot <- { snapshot with Ledger = V2Tend.nextLedger plan snapshot.Ledger snapshot.ObservedAt; ObservedAt = snapshot.ObservedAt.AddHours 1.0 }
                plan <- V2Tend.derive parameters snapshot
            for d in plan.Deferred do
                Assert.True(d.Expired || not d.Counts, sprintf "seed %d: %s still deferred and not expired" seed d.Key)

[<Fact>]
let ``safety: Apply never overwrites a fill field, writes Blocked, leaves Blocked or Done, or closes the loop on an open card`` () =
    for seed in seeds do
        let snapshot = generate seed
        let cards = snapshot.Cards |> List.map (fun c -> c.ItemId, c) |> Map.ofList
        let plan = V2Tend.derive all snapshot
        for effect in plan.Apply do
            let card = cards.[effect.ItemId]
            if effect.Field <> StatusField then Assert.True(effect.Preimage.IsNone, effect.Id)
            Assert.NotEqual<string>("Blocked", effect.Target)
            if effect.Preimage = Some "Blocked" || effect.Preimage = Some "Done" then
                Assert.Equal<string list>([ "S1" ], effect.Rules)
            if effect.Target = "Done" then Assert.False(card.Open, effect.Id)
            let outcomeLike = card.WorkLevel = Some Outcome || (card.WorkLevel.IsNone && not card.SubIssues.IsEmpty)
            if outcomeLike && effect.Field = StatusField then
                Assert.NotEqual<string>("In review", effect.Target)

[<Fact>]
let ``partition: no field is both applied and refused or deferred, and would-apply always defers`` () =
    for enabled in subsets do
        for seed in seeds do
            let plan = V2Tend.derive (V2Tend.v1 enabled) (generate seed)
            let applied = plan.Apply |> List.map (fun e -> e.ItemId, e.Field) |> Set.ofList
            for d in plan.Deferred do
                Assert.False(applied.Contains(d.ItemId, d.Field), d.Key)
            for e in plan.WouldApply do
                Assert.Contains(plan.Deferred, fun d -> d.ItemId = e.ItemId && d.Field = e.Field && d.Reason = RuleDisabled)
                Assert.False(applied.Contains(e.ItemId, e.Field), e.Id)
            for e in plan.Apply do
                Assert.True(e.Rules |> List.forall enabled.Contains, e.Id)

[<Fact>]
let ``dependencies: every dependency is an applied effect and the order is acyclic`` () =
    for seed in seeds do
        let plan = V2Tend.derive all (generate seed)
        let ids = plan.Apply |> List.map (fun e -> e.Id) |> Set.ofList
        for e in plan.Apply do
            for dependency in e.DependsOn do Assert.True(ids.Contains dependency, dependency)
        // A topological order exists: repeatedly remove effects whose dependencies are all removed.
        let mutable remaining = plan.Apply
        let mutable sent = Set.empty
        let mutable progress = true
        while progress && not remaining.IsEmpty do
            let ready = remaining |> List.filter (fun e -> e.DependsOn |> List.forall sent.Contains)
            progress <- not ready.IsEmpty
            sent <- sent + (ready |> List.map (fun e -> e.Id) |> Set.ofList)
            remaining <- remaining |> List.filter (fun e -> not (sent.Contains e.Id))
        Assert.Empty(remaining)

[<Fact>]
let ``fallbacks wait for settled inputs: T1f waits while the card's own Work level is pending`` () =
    let card =
        { (generate 1).Cards.Head with
            ItemId = "L"; Ref = "FS-GG/f#2"; Parent = Some "FS-GG/f#999"; SubIssues = []; Open = true; Status = Some Ready
            WorkLevel = None; Track = None; Prs = []; Intended = None; Blockers = []; BlockedOn = None
            Enrolled = false; EnrolledRoadmap = None; SubIssuesComplete = true; TimelineComplete = true }
    let pending = V2Tend.derive all { ObservedAt = observed; Cards = [ card ]; Ledger = [] }
    Assert.Equal(Some InputPending, deferredOn pending "L" WorkLevelField |> Option.map (fun d -> d.Reason))
    Assert.Equal(None, effectOn pending "L" TrackField)
    Assert.Equal(Some InputPending, deferredOn pending "L" TrackField |> Option.map (fun d -> d.Reason))
    // Without a parent there is no structural value to wait for: the field is proposed and T1f applies.
    let parentless = V2Tend.derive all { ObservedAt = observed; Cards = [ { card with Parent = None } ]; Ledger = [] }
    Assert.True(proposed parentless "FS-GG/f#2" "work-level-undetermined")
    Assert.Equal(Some ("Follow-up", [ "T1f" ]), effectOn parentless "L" TrackField |> Option.map (fun e -> e.Target, e.Rules))
