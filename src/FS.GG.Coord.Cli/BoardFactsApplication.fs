namespace FS.GG.Coord.Cli

/// Board-wide facts shared by scheduling and reconciliation.  This stays outside `Client` so command
/// handlers cannot grow their own subtly different views of the same scan.
module BoardFactsApplication =

    open FS.GG.Coord
    open FS.GG.Coord.Types
    open FS.GG.Coord.GitHub

    /// Count open, non-PR dependants over the whole board.  Sources are filtered after the graph is
    /// constructed so closed blocker targets still resolve as closed rather than becoming unknown.
    let blockingCounts (rows: Scan.Row list) : Map<Ref, int> =
        let counted =
            rows
            |> List.filter (fun row -> not row.IsPullRequest && row.State = Open)
            |> List.map (fun row -> row.Ref)
            |> Set.ofList

        Scan.blockerGraph rows
        |> List.filter (fun (source, _) -> Set.contains source counted)
        |> Rank.blockingCountsOf
