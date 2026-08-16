namespace FS.GG.Coord.Tests

open Xunit
open FS.GG.Coord
open FS.GG.Coord.Types
open FS.GG.Coord.Chore

module ChoreTests =

    let private ref number : Ref =
        { Owner = "FS-GG"; Repo = ".github"; Number = number }

    let private worker = WorkerId "dunlin-753c"
    let private other = WorkerId "plover-a4cf"

    let private claim holder : Claim =
        { Worker = holder
          Session = None
          AgeSeconds = 60
          PreviousStatus = Some Ready }

    let private blocker number state : Blocker =
        { Ref = Some(ref number); Raw = $".github#%d{number}"; State = state }

    let private item number : Item =
        { Ref = ref number
          PathRepo = ".github"
          Status = Ready
          State = Open
          TouchSet = Declared [ Matchable "src/" ]
          Blockers = []
          Claim = None
          ItemPr = None
          ItemPrUnreadable = false
          HumanBlock = None
          Predicate = None
          Class = None
          Kind = None
          BoardKind = None
          CommentCount = None
          BoardClass = None
          DeliveryRoute =
            DeliveryRoute.Current
                { Schema = DeliveryRoute.Schema
                  Subject = "test"
                  SubjectRevision = "test"
                  Route = Some DeliveryRoute.Lightweight
                  Agent = "test"
                  Timestamp = "2026-01-01T00:00:00Z"
                  ReasonCodes = [ "test" ]
                  Rationale = "test"
                  DeclaredImpacts = [ "test" ]
                  ObservedFacts = [ "test" ]
                  SddWorkId = None
                  SpecHome = None
                  RequiredGates = [] }
          Severity = Unset
          Phase = None
          AgeDays = None }

    let private rules items =
        derive items |> List.map (fun chore -> chore.Kind.RuleId)

    [<Fact>]
    let ``derive retains only stale claim and Class maintenance`` () =
        let stale = { item 1 with Claim = Some(claim other, LeaseExpiredNoPr) }
        let classLag = { item 2 with Class = Some Defect; BoardClass = None }
        Assert.Equal<string list>([ "STALE-CLAIM"; "CLASS-PROJECTION-LAG" ], rules [ stale; classLag ])

    [<Fact>]
    let ``uncertain or live claims never become stale claim chores`` () =
        for liveness in [ LeaseHeld; LeaseExpiredPrOpen 42; LeaseExpiredBranchPushed; LivenessUnknown ] do
            Assert.Empty(derive [ { item 1 with Claim = Some(claim other, liveness) } ])

    [<Fact>]
    let ``reserved rows defer Class projection`` () =
        let held =
            { item 1 with
                Claim = Some(claim other, LeaseHeld)
                Class = Some Decision
                Kind = None
                BoardKind = None
                CommentCount = None
                BoardClass = None }
        Assert.Empty(derive [ held ])
        Assert.Equal("CLASS-PROJECTION-LAG", (derive [ { held with Claim = None } ]).Head.Kind.RuleId)

    [<Fact>]
    let ``derive has no second Status reducer`` () =
        let lifecycleShapes =
            [ { item 1 with State = Closed; Status = Ready }
              { item 2 with Status = Blocked; Blockers = [ blocker 3 BlockerClosed ] }
              { item 4 with Status = Ready; Blockers = [ blocker 5 BlockerOpen ] }
              { item 6 with Claim = Some(claim other, LeaseHeld); Status = Backlog }
              { item 7 with Claim = Some(claim other, LeaseHeld); ItemPr = Some 8 } ]
        Assert.Empty(derive lifecycleShapes)

    [<Fact>]
    let ``lifecycleProjection is the sole carrier for a changed verified destination`` () =
        let chore = lifecycleProjection (item 1) Blocked |> Option.get
        Assert.Equal("LIFECYCLE-PROJECTION-LAG", chore.Kind.RuleId)
        Assert.Equal(Some("Status", "Blocked"), chore.Kind.Write)
        Assert.Contains("Status=Blocked", chore.Statement)

    [<Fact>]
    let ``offer admits a direct reducer result while derive cannot recreate it`` () =
        let stale = { item 1 with Status = Backlog }
        Assert.Empty(derive [ stale ])
        let projected = lifecycleProjection stale Ready |> Option.toList
        let safe = safePoint AtNext worker (Whole [ stale ]) [ stale ] |> Option.get
        let offered = offerIncluding safe projected |> Option.get
        Assert.Equal("LIFECYCLE-PROJECTION-LAG", offered.Kind.RuleId)
        Assert.Equal(Some("Status", "Ready"), offered.Kind.Write)

    [<Fact>]
    let ``lifecycleProjection is idempotent and refuses NoStatus`` () =
        Assert.True((lifecycleProjection { item 1 with Status = Blocked } Blocked).IsNone)
        Assert.True((lifecycleProjection (item 1) NoStatus).IsNone)

    [<Fact>]
    let ``lifecycleProjection never reinterprets human facts after the reducer decided`` () =
        let parked =
            { item 1 with
                Status = Blocked
                HumanBlock = Some AwaitingHumanDecision
                Class = Some Decision }
        Assert.Equal("Ready", (lifecycleProjection parked Ready |> Option.get).Kind.Write.Value |> snd)

    [<Fact>]
    let ``safePoint refuses filtered evidence and a worker holding any claim`` () =
        let held = { item 1 with Claim = Some(claim worker, LeaseHeld) }
        Assert.True((safePoint AtNext worker (Filtered []) []).IsNone)
        Assert.True((safePoint AtNext worker (Whole [ held ]) [ held ]).IsNone)

    [<Fact>]
    let ``offer is bounded and orders stale claims before Class maintenance`` () =
        let board =
            [ { item 1 with Class = Some Hardening }
              { item 2 with Claim = Some(claim other, LeaseExpiredNoPr) } ]
        let safe = safePoint AtNext worker (Whole board) board |> Option.get
        Assert.Equal("STALE-CLAIM", (offer safe |> Option.get).Kind.RuleId)

    [<Fact>]
    let ``retirement re-derives the same condition`` () =
        let before = [ { item 1 with Claim = Some(claim other, LeaseExpiredNoPr) } ]
        let chore = (derive before).Head
        Assert.False(isRetired chore before)
        Assert.True(isRetired chore [ item 1 ])
