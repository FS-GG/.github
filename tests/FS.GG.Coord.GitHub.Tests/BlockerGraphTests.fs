module FS.GG.Coord.GitHub.Tests.BlockerGraphTests

open Xunit
open FS.GG.Coord
open FS.GG.Coord.Types
open FS.GG.Coord.GitHub

/// `Scan.blockerGraph` builds the `Blocked by` graph `Blockers.cycles` reads — from the scanned ROWS
/// alone, with no transport (#1090).
///
/// THE DEFECT THIS SERVES. A `Blocked by` ring — #1059 → #1063 → #1073 → #1059 — is a deadlock no worker
/// and no per-item gate can see: every item on it is individually well-formed (non-empty blockers, every
/// blocker OPEN, every ref a real issue), so `take` passes over each with "Status is Blocked" and the ring
/// sits forever. `Blockers.cycles` owns the detection (tested in Core); this function is the seam that
/// hands it the graph, and its whole trick is that an on-board blocker's OPEN/CLOSED state is already in
/// the scan, so the graph costs nothing to build. These tests pin that construction — most sharply, the
/// regression: the real 2026-07-17 ring, as rows, must produce a graph in which `Blockers.cycles` finds a
/// three-item ring.
module BlockerGraphTests =

    let private row owner repo n status state (blockedBy: string) : Scan.Row =
        { Ref = { Owner = owner; Repo = repo; Number = n }
          Title = $"item %d{n}"
          Status = status
          BlockedByRaw = blockedBy
          State = state
          IsPullRequest = false
          PathRepo = repo
          BoardClass = None
          Severity = Unset
          Phase = None
          CreatedAt = None
          SweptBody = None }

    // A `.github` open item blocked by a bare-`#n` list — the spelling the board actually uses.
    let private gh n (blockedBy: string) =
        row "FS-GG" ".github" n BoardStatus.Blocked IssueState.Open blockedBy

    let private ringOf (rings: Ref list list) =
        rings |> List.map (fun ring -> ring |> List.map (fun r -> r.Number) |> List.sort) |> List.sort

    // ---- the regression: the real ring, as rows, is caught -----------------------------------------

    [<Fact>]
    let ``the 1059-1063-1073 ring produces a graph in which cycles finds the three-item ring`` () =
        // #1059 ──(Blocked by)──▶ #1063 ──▶ #1073 ──▶ #1059. Every blocker OPEN and on the board.
        let rows =
            [ gh 1059 "#1063"
              gh 1063 "#1073"
              gh 1073 "#1059" ]

        let rings = Blockers.cycles (Scan.blockerGraph rows)
        Assert.Equal<int list list>([ [ 1059; 1063; 1073 ] ], ringOf rings)

    // ---- edges are drawn from resolution read off the scan, with NO transport ----------------------

    [<Fact>]
    let ``a CLOSED blocker breaks the ring - its edge is resolved and dropped`` () =
        // Same three refs, but #1073 is CLOSED. The edge #1063 → #1073 is resolved, so no ring closes.
        let rows =
            [ gh 1059 "#1063"
              gh 1063 "#1073"
              row "FS-GG" ".github" 1073 BoardStatus.Done IssueState.Closed "#1059" ]

        Assert.Empty(Blockers.cycles (Scan.blockerGraph rows))

    [<Fact>]
    let ``a two-item ring is caught`` () =
        let rows = [ gh 10 "#11"; gh 11 "#10" ]
        Assert.Equal<int list list>([ [ 10; 11 ] ], ringOf (Blockers.cycles (Scan.blockerGraph rows)))

    [<Fact>]
    let ``a self-edge is a one-item ring`` () =
        let rows = [ gh 42 "#42" ]
        Assert.Equal<int list list>([ [ 42 ] ], ringOf (Blockers.cycles (Scan.blockerGraph rows)))

    [<Fact>]
    let ``a DAG has no ring - no false positive`` () =
        // #1 ← #2 ← #3, and #1 blocks nothing. A chain is not a cycle.
        let rows = [ gh 1 ""; gh 2 "#1"; gh 3 "#2" ]
        Assert.Empty(Blockers.cycles (Scan.blockerGraph rows))

    [<Fact>]
    let ``a diamond - shared blocker, not a cycle`` () =
        // #2 and #3 both blocked by #1; #4 blocked by both. No back-edge, no ring.
        let rows = [ gh 1 ""; gh 2 "#1"; gh 3 "#1"; gh 4 "#2, #3" ]
        Assert.Empty(Blockers.cycles (Scan.blockerGraph rows))

    // ---- fail-closed / under-report edges ----------------------------------------------------------

    [<Fact>]
    let ``an OFF-BOARD blocker draws no edge - it cannot be a ring node`` () =
        // #10 is blocked by #999, which is not in the scan. No node, no edge, no invented deadlock.
        let rows = [ gh 10 "#999"; gh 11 "#10" ]
        Assert.Empty(Blockers.cycles (Scan.blockerGraph rows))

    [<Fact>]
    let ``prose in a dependency field draws no edge`` () =
        // Unparseable blocker: it BLOCKS every other reader, but it names no node, so no ring.
        let rows = [ gh 10 "blocked on a design review"; gh 11 "#10" ]
        Assert.Empty(Blockers.cycles (Scan.blockerGraph rows))

    [<Fact>]
    let ``a comma-separated blocker list is split into separate edges`` () =
        // #12 blocked by #10 AND #11; #10 → #12 closes one ring, #11 has no back-edge.
        let rows = [ gh 10 "#12"; gh 11 ""; gh 12 "#10, #11" ]
        Assert.Equal<int list list>([ [ 10; 12 ] ], ringOf (Blockers.cycles (Scan.blockerGraph rows)))

    [<Fact>]
    let ``an empty BlockedByRaw yields a node with no blockers`` () =
        let graph = Scan.blockerGraph [ gh 5 "" ]
        Assert.Equal(1, List.length graph)
        let _, blockers = List.head graph
        Assert.Empty blockers

    [<Fact>]
    let ``an owner-qualified cross-repo ref matches the on-board node`` () =
        // The scheduler writes qualified refs; a bare and a qualified spelling of the same node must both
        // resolve to it.
        let rows =
            [ gh 20 "FS-GG/.github#21"
              gh 21 "#20" ]

        Assert.Equal<int list list>([ [ 20; 21 ] ], ringOf (Blockers.cycles (Scan.blockerGraph rows)))
