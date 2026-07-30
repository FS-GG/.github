module FS.GG.Coord.Cli.Tests.CrossRepoRankTests

open Xunit
open FS.GG.Coord
open FS.GG.Coord.Types
open FS.GG.Coord.Cli
open FS.GG.Coord.GitHub
open FS.GG.Coord.GitHub.Transport

/// .github#1628 — THE RANK'S BLOCKING COUNT, ACROSS THE `--repo` SCOPE.
///
/// `RankTests` pins the counting rule and the fold. What it cannot reach is the one thing that made this
/// defect invisible: the SCOPE is applied by `Scan.snapshot`, in a different project, between the rows
/// and the candidates. A Core-only fixture must hand itself an already-scoped list, so it can only assert
/// that the fix works given the premise — never that the premise holds in the wiring.
///
/// So every test here drives the REAL scan (`Scan.snapshot`), the REAL codec (`Snapshot.parse`, inside
/// `Client.renderDecision`) and the REAL fold, against a recording fake, with `--repo .github` — the exact
/// spelling `take --repo <repo>` produces and therefore the exact spelling that was wrong. This is the
/// composition #1598 already learned to test end-to-end rather than in pieces: the rewrite it landed
/// "would have compiled, passed its unit tests, and changed nothing about the live board" if `rows` had
/// stayed discarded at `renderDecision`, and that near-miss is the reason this file exists.
///
/// AC2's fixture, exactly: one item in repo A, three open items in repo B naming it in `Blocked by`, and
/// a `--repo A` batch that ranks it as blocking three.

let private ok (body: string) =
    Ok
        { Status = 200
          Body = body
          ETag = None
          NextLink = None }

let private ref' repo n : Ref =
    { Owner = "FS-GG"
      Repo = repo
      Number = n }

let private row repo n blockedBy state isPr : Scan.Row =
    { Ref = ref' repo n
      Title = $"%s{repo} item %d{n}"
      Status = Ready
      BlockedByRaw = blockedBy
      State = state
      IsPullRequest = isPr
      PathRepo = repo
      BoardClass = None
      Severity = Unset
      Phase = None
      CreatedAt = None }

/// The hub — one `.github` item — and its three OPEN dependents, all in the OTHER repo. Nothing else on
/// the board names it, so under a `--repo .github` scope every one of those edges used to vanish.
///
/// The last two rows are the filters, present so they can be observed to have no effect rather than
/// asserted about in a comment:
///
/// - a CLOSED `FS.GG.SDD` item naming the hub. Nobody is waiting on anything, so it is not a dependent —
///   `Rank.blockingCounts` has always said "how many OPEN items", and a wider source set must not quietly
///   widen that too.
/// - a PULL REQUEST naming the hub. `Scan.snapshot` drops PRs BEFORE it scopes, so counting one here
///   would credit a dependent the candidate-set spelling never could — a NEW disagreement between the
///   scoped and unscoped answers, introduced by the fix for a disagreement.
let private board =
    [ row ".github" 10 "" Open false
      row ".github" 11 "" Open false
      row "FS.GG.SDD" 200 "FS-GG/.github#10" Open false
      row "FS.GG.SDD" 201 "FS-GG/.github#10" Open false
      row "FS.GG.SDD" 202 "FS-GG/.github#10" Open false
      row "FS.GG.SDD" 203 "FS-GG/.github#10" Closed false
      row "FS.GG.SDD" 204 "FS-GG/.github#10" Open true ]

/// Answers by ENDPOINT, like `ScanRoundTripTests`: the off-board open-issue sweep, the marker read, and
/// the body read are three different questions and one fake must not collapse them.
///
/// Every body declares a DISTINCT touch-set, so nothing collides and the batch is a pure ordering test.
/// The number is taken from the path rather than fixed, so a body can never be served for the wrong item.
let private recorder () =
    Fake.Recorder(fun req ->
        if req.Path.EndsWith "/issues" then
            ok "[]"
        elif req.Path.EndsWith "/comments" then
            ok "[]"
        else
            let n = req.Path.Split('/') |> Array.last
            ok $"""{{"number":%s{n},"body":"Paths: src/f%s{n}.fs"}}""")

let private options (args: string list) : Options.Options =
    match Options.parse args with
    | Ok o -> o
    | Error e -> failwith $"the fixture's own arguments must parse: %s{e}"

/// The offer path, end to end, scoped to `.github` — `Scan.snapshot` then `Client.renderDecision`, which
/// is what `batch`, `next` and `take` each call.
let private scopedBatch (transport: Fake.Recorder) =
    match Scan.snapshot transport board (Some ".github") false None 120 with
    | Error e -> failwith $"the scan must produce a snapshot — got %A{e}"
    | Ok(document, receipt) ->

    match Client.renderDecision (options [ "batch"; "--repo"; ".github" ]) board document with
    | Result.Error code -> failwith $"the batch must be schedulable — got exit %d{code}"
    | Ok result -> document, receipt, result

let private decisionFor (result: Batch.BatchResult) (n: int) =
    result.Decisions |> List.find (fun d -> d.Item.Ref.Number = n)

// ---- AC2 ---------------------------------------------------------------------------------------------

[<Fact>]
let ``#1628 AC2 a --repo scoped batch ranks a cross-repo hub as blocking THREE`` () =
    let _, receipt, result = scopedBatch (recorder ())

    // THE SCOPE IS REAL, and it is asserted rather than assumed. If `Scan.snapshot` stopped scoping, the
    // count below would be right for the wrong reason and this file would be testing nothing.
    Assert.Equal(2, receipt.Candidates)
    Assert.Equal<int list>([ 10; 11 ], result.Decisions |> List.map (fun d -> d.Item.Ref.Number))

    // AC2. Three dependents, none of them on the candidate list, all of them counted.
    Assert.Equal(3, (decisionFor result 10).Rank.Blocking)
    Assert.Equal(0, (decisionFor result 11).Rank.Blocking)

[<Fact>]
let ``#1628 the SCOPED candidate list still cannot see those edges — the fixture is not vacuous`` () =
    // The counterfactual, from the same bytes: `Batch.schedule` derives its counts from the candidates,
    // which is what the offer path used to do, and over a `--repo .github` snapshot the hub reads as
    // blocking NOTHING. Without this the test above would keep passing if the whole-board counts were
    // replaced by candidate-derived ones on a board that happened to have its dependents in scope.
    let document, _, _ = scopedBatch (recorder ())

    match Snapshot.parse document with
    | Error errors -> failwith $"the engine's own snapshot must parse: %A{errors}"
    | Ok request ->

    let candidates = request.Candidates |> List.map (fun c -> c.Item)

    match Batch.schedule request.AllowBacklog request.Limit request.InFlight candidates with
    | Green r -> Assert.Equal(0, (decisionFor r 10).Rank.Blocking)
    | other -> failwith $"the batch must be schedulable — got %A{other}"

[<Fact>]
let ``#1628 AC1 the scoped count equals the unscoped count, on one board at one instant`` () =
    // AC1 as the equality it is: the same hub, ranked by a `--repo .github` batch and by a bare org-wide
    // one, must carry the same blocking count. That two answers to one question existed at all is the
    // defect; that they now agree is the fix.
    let _, _, scoped = scopedBatch (recorder ())

    let unscoped =
        match Scan.snapshot (recorder ()) board None false None 120 with
        | Error e -> failwith $"the scan must produce a snapshot — got %A{e}"
        | Ok(document, _) ->
            match Client.renderDecision (options [ "batch" ]) board document with
            | Result.Error code -> failwith $"the batch must be schedulable — got exit %d{code}"
            | Ok result -> result

    Assert.Equal(3, (decisionFor scoped 10).Rank.Blocking)
    Assert.Equal((decisionFor unscoped 10).Rank.Blocking, (decisionFor scoped 10).Rank.Blocking)

// ---- AC3 — the whole-board edges cost no additional read ---------------------------------------------

[<Fact>]
let ``#1628 AC3 the whole-board count costs the transport NOTHING`` () =
    let transport = recorder ()

    match Scan.snapshot transport board (Some ".github") false None 120 with
    | Error e -> failwith $"the scan must produce a snapshot — got %A{e}"
    | Ok(document, _) ->

    // The scan is the only thing that spends. Snapshot it here, then decide, then compare — a delta of
    // zero is the claim, and it is a claim about CALL SHAPE that no assertion on the returned rank could
    // make (ADR-0040 C1: the fake counts calls precisely so budget claims stay checkable after the port).
    let restAfterScan = transport.RestCalls
    let graphQlAfterScan = transport.GraphQlCalls

    match Client.renderDecision (options [ "batch"; "--repo"; ".github" ]) board document with
    | Result.Error code -> failwith $"the batch must be schedulable — got exit %d{code}"
    | Ok result ->

    Assert.Equal(3, (decisionFor result 10).Rank.Blocking)

    // AC3. `Scan.blockerGraph` is pure over rows already in hand (#1090), so the whole board's edges are
    // free — the same reason `Blockers.cycles` gets its graph for nothing.
    Assert.Equal(restAfterScan, transport.RestCalls)
    Assert.Equal(graphQlAfterScan, transport.GraphQlCalls)

[<Fact>]
let ``#1628 AC3 the SCAN's own cost is unchanged — the wider count reads no wider`` () =
    // The other half of "no additional read", and the one a delta-of-zero above cannot cover: the fix
    // must not have made the SCAN pay for the rows it now counts. It does not, because those rows were
    // already scanned — `--repo` scopes the CANDIDATES, never the board read.
    //
    // Two candidates, each costing one body read and one marker read, plus the one off-board open-issue
    // sweep for the single in-scope repo (.github#1525). The three `FS.GG.SDD` dependents are counted and
    // never read: they are not candidates, so no body and no marker is fetched for any of them.
    let transport = recorder ()

    match Scan.snapshot transport board (Some ".github") false None 120 with
    | Error e -> failwith $"the scan must produce a snapshot — got %A{e}"
    | Ok _ ->

    // The `gh` stub's own log grammar (`issue-get FS-GG/.github 10`, `comment-list FS-GG/.github 10`),
    // which is what ADR-0040 C1's budget assertions are written against.
    Assert.Equal(2, transport.Count "FS-GG/.github 10")
    Assert.Equal(2, transport.Count "FS-GG/.github 11")

    for dependent in [ 200; 201; 202; 203; 204 ] do
        Assert.Equal(0, transport.Count $"FS-GG/FS.GG.SDD %d{dependent}")

// ---- AC4 — the source set widened; what counts as an EDGE did not ------------------------------------

[<Fact>]
let ``#1628 AC4 a CLOSED dependent and a PULL REQUEST are not dependents`` () =
    // Both are on the board naming the hub, and both are excluded — which is why the count is three and
    // not five. The failure this guards is the tempting one: reaching for `Scan.blockerGraph`'s edges
    // wholesale, which include every row it was handed, PRs and closed issues alike.
    let _, _, result = scopedBatch (recorder ())
    Assert.Equal(3, (decisionFor result 10).Rank.Blocking)

    // And the exclusions are the reason, not a coincidence of the fixture: drop them from the board and
    // the answer does not move.
    let withoutThem =
        board |> List.filter (fun r -> r.Ref.Number <> 203 && r.Ref.Number <> 204)

    Assert.Equal(3, Client.boardBlockingCounts withoutThem |> Map.find (ref' ".github" 10))
    Assert.Equal(3, Client.boardBlockingCounts board |> Map.find (ref' ".github" 10))

[<Fact>]
let ``#1628 a CLOSED blocker is filtered AFTER the graph is built, never before`` () =
    // THE SUBTLE ONE, and the reason `boardBlockingCounts` filters rows only on the SOURCE side.
    // `Scan.blockerGraph` resolves a blocker's state by looking the target up in the rows it was handed,
    // and treats a miss as `BlockerUnknown` — which BLOCKS. Filter the rows to open ones BEFORE building
    // the graph and every CLOSED target becomes an unknown one, so edges that resolved long ago start
    // counting again: the fix for an undercount would have shipped an overcount.
    //
    // Here the hub itself is CLOSED. Nothing may be counted against it — its dependents' edges have all
    // resolved — and a pre-filtered graph would say three.
    let closedHub =
        board
        |> List.map (fun r -> if r.Ref.Number = 10 then { r with State = Closed } else r)

    Assert.Equal(None, Client.boardBlockingCounts closedHub |> Map.tryFind (ref' ".github" 10))

[<Fact>]
let ``#1628 an off-board blocker is credited to a node no candidate can be`` () =
    // AC4's other half. An off-board ref parses, so it draws an edge and gets a count — but it names
    // nothing on the board, so `Rank.ofItemsWith` (which reads by ref) never looks it up. Asserting it is
    // present-and-inert is more honest than asserting it is absent: the map is a lookup table, and an
    // entry nobody reads costs nothing.
    let withOffBoard =
        board @ [ row "FS.GG.SDD" 205 "FS-GG/FS.GG.Rendering#9999" Open false ]

    let counts = Client.boardBlockingCounts withOffBoard

    Assert.Equal(1, counts |> Map.find (ref' "FS.GG.Rendering" 9999))
    Assert.Equal(3, counts |> Map.find (ref' ".github" 10))

[<Fact>]
let ``#1628 prose in a dependency field draws no edge at all`` () =
    let withProse =
        board @ [ row "FS.GG.SDD" 206 "waiting on the platform team" Open false ]

    let counts = Client.boardBlockingCounts withProse

    // No node to credit, so the map is exactly what it was — never a guess with a scheduler behind it.
    Assert.Equal<Map<Ref, int>>(Client.boardBlockingCounts board, counts)

// ---- AC5 ---------------------------------------------------------------------------------------------

[<Fact>]
let ``#1628 AC5 --explain reports the same count the ordering used`` () =
    // Through the real path, not the fold in isolation: a driver reading `--explain` beside a live
    // `take --repo` must see the number that actually decided the order.
    let _, _, result = scopedBatch (recorder ())

    let hubLine =
        Batch.explainRanking result |> List.find (fun l -> l.Contains ".github#10")

    Assert.Contains("blocking 3", hubLine)
