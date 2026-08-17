namespace FS.GG.Coord.Cli

/// Board-wide facts shared by scheduling and reconciliation, computed once from one scan.
///
/// This module stays OUTSIDE `Client` so that command handlers cannot grow their own subtly
/// different views of the same scan — and that is a claim only a signature can enforce. The
/// exported surface below is the whole of it: one function, over `Scan.Row list`, returning a
/// map. A second handler that wanted "the blocking counts, but not counting X" cannot add a
/// second spelling here without the addition being visible in this file, which is the difference
/// between a convention and a boundary (.github#2731).
///
/// `Client.fsi` consumes this exact shape as an injected seam
/// (`FS.GG.Coord.GitHub.Scan.Row list -> Map<FS.GG.Coord.Types.Ref, int>`), so the signature is
/// also the contract that seam is typed against.
module BoardFactsApplication =

    /// Count each ref's OPEN, NON-PULL-REQUEST dependants across the whole board — the blocking
    /// count `Rank` uses to order candidates, so a row that blocks many others outranks one that
    /// blocks none.
    ///
    /// PROMISES. The result is keyed by BLOCKER (the source of a `Blocked by` edge), never by the
    /// blocked row, and carries an entry only for a ref with at least one such dependant: a ref
    /// absent from the map blocks nothing, and callers must read that absence as zero rather than
    /// as unknown.
    ///
    /// REFUSES to let a CLOSED blocker target degrade into an unknown one. The open/non-PR filter
    /// is applied to the edge SOURCES after `Scan.blockerGraph` has already constructed the whole
    /// graph, never to the rows fed into it. Filtering first would delete closed and pull-request
    /// rows before their edges existed, and an edge whose target had been deleted resolves as
    /// unknown — which the scheduler treats as still-blocking. The consequence a caller can rely
    /// on: a dependant blocked only by CLOSED work is reported as blocked by nothing.
    ///
    /// Pure. Reads no board and issues no request; every fact comes from the supplied rows.
    val blockingCounts: rows: FS.GG.Coord.GitHub.Scan.Row list -> Map<FS.GG.Coord.Types.Ref, int>
