namespace FS.GG.Coord.Cli

// The contract this module states — what `blockingCounts` promises and what it refuses — lives in
// `BoardFactsApplication.fsi`, which is where the compiler keeps it (.github#2730: a `///` here would
// be discarded and reach no consumer). What follows is implementation reasoning only.
module BoardFactsApplication =

    open FS.GG.Coord
    open FS.GG.Coord.Types
    open FS.GG.Coord.GitHub

    // ORDER IS THE WHOLE CORRECTNESS ARGUMENT, and it is easy to "simplify" wrongly: the filter is
    // applied to the edge SOURCES after `blockerGraph` has seen every row, never to the rows passed
    // into it. Filtering first deletes closed and pull-request rows before their edges exist, and an
    // edge whose target has been deleted resolves as UNKNOWN — which the scheduler treats as still
    // blocking, so work blocked only by closed work would never become ready.
    let blockingCounts (rows: Scan.Row list) : Map<Ref, int> =
        let counted =
            rows
            |> List.filter (fun row -> not row.IsPullRequest && row.State = Open)
            |> List.map (fun row -> row.Ref)
            |> Set.ofList

        Scan.blockerGraph rows
        |> List.filter (fun (source, _) -> Set.contains source counted)
        |> Rank.blockingCountsOf
